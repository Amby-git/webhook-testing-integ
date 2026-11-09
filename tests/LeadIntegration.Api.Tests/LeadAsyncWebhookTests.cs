using System.Net;
using System.Net.Http.Json;
using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.Messaging;
using LeadIntegration.Api.Features.RequestCatcher;

namespace LeadIntegration.Api.Tests;

public sealed class LeadAsyncWebhookTests
{
    [Fact]
    public async Task WebhookReturns202_WhenEventIsQueued()
    {
        var publisher = new TestApiFactory.FakeLeadMessagePublisher();
        var salesforceClient = new TestApiFactory.FakeSalesforceLeadClient();
        var inboundObserver = new TestApiFactory.FakeInboundWebhookObserver();
        using var factory = new TestApiFactory(
            clientOverride: salesforceClient,
            messagePublisherOverride: publisher,
            inboundWebhookObserverOverride: inboundObserver);
        using var client = factory.CreateClient();

        using var response = await PostEventAsync(client, ValidEvent(), "corr-001");
        var accepted = await response.Content.ReadFromJsonAsync<LeadEventAcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(accepted);
        Assert.Equal("event-001", accepted.EventId);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Single(publisher.Messages);
        Assert.Equal("event-001", publisher.Messages.Single().EventId);
        Assert.Equal("Analytical Engines Ltd", publisher.Messages.Single().Lead.Company);
        Assert.Equal(0, salesforceClient.CreateCount);
        var observation = Assert.Single(inboundObserver.Observations);
        Assert.Equal("event-001", observation.EventId);
        Assert.Equal("HubSpot", observation.SourcePlatform);
        Assert.Equal("corr-001", observation.CorrelationId);
        Assert.StartsWith("application/json", observation.ContentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebhookReturns503_WhenRabbitMqCannotConfirmPublish()
    {
        var publisher = new TestApiFactory.FakeLeadMessagePublisher((_, _) =>
            Task.FromException(new RabbitMqUnavailableException("Broker unavailable.")));
        using var factory = new TestApiFactory(messagePublisherOverride: publisher);
        using var client = factory.CreateClient();

        using var response = await PostEventAsync(client, ValidEvent());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task WebhookPublishesToRabbitMq_WhileRequestCatcherObservationIsPending()
    {
        var observerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseObserver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new TestApiFactory.FakeInboundWebhookObserver(async (_, cancellationToken) =>
        {
            observerStarted.TrySetResult();
            await releaseObserver.Task.WaitAsync(cancellationToken);
        });
        var publisher = new TestApiFactory.FakeLeadMessagePublisher((_, _) =>
        {
            publishStarted.TrySetResult();
            return Task.CompletedTask;
        });
        using var factory = new TestApiFactory(
            messagePublisherOverride: publisher,
            inboundWebhookObserverOverride: observer);
        using var client = factory.CreateClient();

        var responseTask = PostEventAsync(client, ValidEvent());
        await observerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, publisher.PublishCount);

        releaseObserver.TrySetResult();
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task InvalidWebhookPayload_Returns400WithoutPublishing()
    {
        var publisher = new TestApiFactory.FakeLeadMessagePublisher();
        using var factory = new TestApiFactory(messagePublisherOverride: publisher);
        using var client = factory.CreateClient();

        using var response = await PostEventAsync(client, ValidEvent(company: " "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task DuplicateEvent_Returns409_AndPublishesOnce()
    {
        var publisher = new TestApiFactory.FakeLeadMessagePublisher();
        using var factory = new TestApiFactory(messagePublisherOverride: publisher);
        using var client = factory.CreateClient();

        using var first = await PostEventAsync(client, ValidEvent());
        using var duplicate = await PostEventAsync(client, ValidEvent());

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateEvents_AreAcceptedOnlyOnce()
    {
        var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new TestApiFactory.FakeLeadMessagePublisher(async (_, cancellationToken) =>
        {
            publishStarted.TrySetResult();
            await releasePublish.Task.WaitAsync(cancellationToken);
        });
        using var factory = new TestApiFactory(messagePublisherOverride: publisher);
        using var client = factory.CreateClient();

        var firstTask = PostEventAsync(client, ValidEvent());
        await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var duplicate = await PostEventAsync(client, ValidEvent());
        releasePublish.TrySetResult();
        using var first = await firstTask;

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task EventStatus_ReturnsAcceptedStateAfterPublisherConfirm()
    {
        var publisher = new TestApiFactory.FakeLeadMessagePublisher();
        using var factory = new TestApiFactory(messagePublisherOverride: publisher);
        using var client = factory.CreateClient();

        using var accepted = await PostEventAsync(client, ValidEvent());
        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/events/HubSpot/event-001");
        statusRequest.Headers.Add("X-Webhook-Api-Key", "test-api-key");
        using var statusResponse = await client.SendAsync(statusRequest);
        var status = await statusResponse.Content.ReadFromJsonAsync<LeadEventStatus>();

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(LeadEventProcessingStatus.Accepted, status?.Status);
        Assert.Equal(0, status?.AttemptCount);
    }

    private static Task<HttpResponseMessage> PostEventAsync(
        HttpClient client,
        object payload,
        string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/leads/async")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Webhook-Api-Key", "test-api-key");
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return client.SendAsync(request);
    }

    private static object ValidEvent(string company = "Analytical Engines Ltd") => new
    {
        externalLeadId = "mkt-001",
        sourcePlatform = "HubSpot",
        firstName = "Ada",
        lastName = "Lovelace",
        email = "ada@example.com",
        company,
        phone = "+35312345678",
        campaignId = "spring-2026",
        createdAt = DateTimeOffset.Parse("2026-11-09T10:15:00Z"),
        eventId = "event-001"
    };
}

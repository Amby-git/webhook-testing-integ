using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.Salesforce;
using LeadIntegration.Api.Features.Messaging;
using LeadIntegration.Api.Features.RequestCatcher;
using LeadIntegration.Contracts.Salesforce;
using LeadIntegration.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LeadIntegration.Api.Tests;

internal sealed class TestApiFactory(
    SalesforceClientFailure failure = SalesforceClientFailure.None,
    int webhookPermitLimit = 30,
    bool throwOnLeadRead = false,
    ISalesforceLeadClient? clientOverride = null,
    ILeadMessagePublisher? messagePublisherOverride = null,
    IInboundWebhookObserver? inboundWebhookObserverOverride = null) : WebApplicationFactory<ApiAssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WebhookAuthentication:ApiKey"] = "test-api-key",
                ["SalesforceSimulator:BaseUrl"] = "http://127.0.0.1:5280/",
                ["SalesforceSimulator:TimeoutSeconds"] = "10",
                ["WebhookRateLimit:PermitLimit"] = webhookPermitLimit.ToString(),
                ["WebhookRateLimit:WindowSeconds"] = "60"
            }));

        if (throwOnLeadRead)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILeadService>();
                services.AddSingleton<ILeadService, ThrowingLeadService>();
            });
        }
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesforceLeadClient>();
            services.AddSingleton(clientOverride ?? new FakeSalesforceLeadClient(failure));
            services.RemoveAll<ILeadMessagePublisher>();
            services.AddSingleton(messagePublisherOverride ?? new FakeLeadMessagePublisher());
            services.RemoveAll<IInboundWebhookObserver>();
            services.AddSingleton(inboundWebhookObserverOverride ?? new FakeInboundWebhookObserver());
        });
    }

    internal sealed class FakeInboundWebhookObserver(
        Func<LeadIntegration.Api.Contracts.Marketing.MarketingLeadWebhookRequest, CancellationToken, Task>? observe = null)
        : IInboundWebhookObserver
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<InboundWebhookObservation> _observations = new();

        public IReadOnlyCollection<InboundWebhookObservation> Observations => _observations.ToArray();

        public Task ObserveAsync(
            LeadIntegration.Api.Contracts.Marketing.MarketingLeadWebhookRequest request,
            string contentType,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _observations.Enqueue(new InboundWebhookObservation(
                "POST",
                contentType,
                request.EventId ?? string.Empty,
                request.SourcePlatform ?? string.Empty,
                correlationId,
                request));
            return observe is null ? Task.CompletedTask : observe(request, cancellationToken);
        }
    }

    internal sealed class FakeLeadMessagePublisher(
        Func<LeadProcessingMessage, CancellationToken, Task>? publish = null) : ILeadMessagePublisher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LeadProcessingMessage> _messages = new();
        private int _publishCount;

        public IReadOnlyCollection<LeadProcessingMessage> Messages => _messages.ToArray();
        public int PublishCount => Volatile.Read(ref _publishCount);

        public async Task PublishAsync(LeadProcessingMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _publishCount);
            if (publish is not null)
            {
                await publish(message, cancellationToken);
            }

            _messages.Enqueue(message);
        }
    }

    private sealed class ThrowingLeadService : ILeadService
    {
        public Task<LeadProcessingResult> ProcessAsync(
            LeadIntegration.Api.Contracts.Marketing.MarketingLeadWebhookRequest request,
            string? idempotencyKey,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public IReadOnlyCollection<Lead> GetAll() => throw new InvalidOperationException("Test exception.");

        public Lead? GetById(Guid id) => throw new NotImplementedException();

        public LeadUpdateResult Update(Guid id, LeadRequest validatedRequest) => throw new NotImplementedException();

        public bool Delete(Guid id) => throw new NotImplementedException();
    }

    internal sealed class FakeSalesforceLeadClient(SalesforceClientFailure failure = SalesforceClientFailure.None)
        : ISalesforceLeadClient
    {
        private int _createCount;
        public int CreateCount => _createCount;

        public Task<SalesforceCallResult<SalesforceLeadResponse>> CreateLeadAsync(
            SalesforceLeadDto lead,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            return Task.FromResult(failure == SalesforceClientFailure.None
                ? SalesforceCallResult<SalesforceLeadResponse>.Success(new SalesforceLeadResponse(
                    "00Q000000000001ABC", lead.FirstName, lead.LastName, lead.Email, lead.Phone,
                    lead.Company, lead.LeadSource, lead.ExternalLeadId, lead.CampaignId))
                : SalesforceCallResult<SalesforceLeadResponse>.Failed(failure));
        }

        public Task<SalesforceCallResult<SalesforceSimulatorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(failure == SalesforceClientFailure.None
                ? SalesforceCallResult<SalesforceSimulatorStatusResponse>.Success(new SalesforceSimulatorStatusResponse(
                    "Salesforce Simulator API", "available", true, "None"))
                : SalesforceCallResult<SalesforceSimulatorStatusResponse>.Failed(failure));
    }
}

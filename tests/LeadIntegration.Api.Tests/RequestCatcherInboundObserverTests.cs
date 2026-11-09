using System.Net;
using System.Text.Json;
using LeadIntegration.Api.Contracts.Marketing;
using LeadIntegration.Api.Features.RequestCatcher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeadIntegration.Api.Tests;

public sealed class RequestCatcherInboundObserverTests
{
    [Fact]
    public async Task ObserveAsync_Enabled_SendsNormalizedPayloadAndMetadataToIncoming()
    {
        HttpMethod? method = null;
        Uri? uri = null;
        string? body = null;
        string? contentType = null;
        IReadOnlyDictionary<string, string[]>? headers = null;
        using var client = CreateClient(async (request, cancellationToken) =>
        {
            method = request.Method;
            uri = request.RequestUri;
            contentType = request.Content?.Headers.ContentType?.MediaType;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var observer = CreateObserver(client, enabled: true);

        await observer.ObserveAsync(
            ValidRequest(),
            "application/json; charset=utf-8",
            "corr-inbound-001",
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://webhooks.requestcatcher.com/incoming", uri?.ToString());
        Assert.Equal("application/json", contentType);
        Assert.NotNull(body);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("POST", root.GetProperty("method").GetString());
        Assert.Equal("application/json; charset=utf-8", root.GetProperty("contentType").GetString());
        Assert.Equal("event-inbound-001", root.GetProperty("eventId").GetString());
        Assert.Equal("HubSpot", root.GetProperty("sourcePlatform").GetString());
        Assert.Equal("corr-inbound-001", root.GetProperty("correlationId").GetString());
        var payload = root.GetProperty("payload");
        Assert.Equal("Ada", payload.GetProperty("firstName").GetString());
        Assert.Equal("ada@example.com", payload.GetProperty("email").GetString());
        Assert.Equal("Example Ltd", payload.GetProperty("company").GetString());
        Assert.Equal("event-inbound-001", payload.GetProperty("eventId").GetString());
        Assert.Contains("X-Correlation-ID", headers!.Keys);
        Assert.Contains("X-Event-ID", headers.Keys);
        Assert.Contains("X-Source-Platform", headers.Keys);
    }

    [Fact]
    public async Task ObserveAsync_Disabled_DoesNotSendRequest()
    {
        var sendCount = 0;
        using var client = CreateClient((_, _) =>
        {
            Interlocked.Increment(ref sendCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var observer = CreateObserver(client, enabled: false);

        await observer.ObserveAsync(ValidRequest(), "application/json", "corr-disabled", CancellationToken.None);

        Assert.Equal(0, sendCount);
    }

    [Fact]
    public async Task ObserveAsync_HttpFailure_DoesNotThrow()
    {
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var observer = CreateObserver(client, enabled: true);

        var exception = await Record.ExceptionAsync(() => observer.ObserveAsync(
            ValidRequest(), "application/json", "corr-http-failure", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ObserveAsync_NetworkFailure_DoesNotThrow()
    {
        using var client = CreateClient((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated network failure.")));
        var observer = CreateObserver(client, enabled: true);

        var exception = await Record.ExceptionAsync(() => observer.ObserveAsync(
            ValidRequest(), "application/json", "corr-network-failure", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ObserveAsync_Timeout_DoesNotThrow()
    {
        using var client = CreateClient((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated client timeout.")));
        var observer = CreateObserver(client, enabled: true);

        var exception = await Record.ExceptionAsync(() => observer.ObserveAsync(
            ValidRequest(), "application/json", "corr-timeout", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ObserveAsync_DoesNotForwardCredentialsOrArbitraryHeaders()
    {
        IReadOnlyDictionary<string, string[]>? headers = null;
        string? body = null;
        using var client = CreateClient(async (request, cancellationToken) =>
        {
            headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var observer = CreateObserver(client, enabled: true);

        await observer.ObserveAsync(ValidRequest(), "application/json", "corr-safe", CancellationToken.None);

        Assert.NotNull(headers);
        Assert.DoesNotContain("Authorization", headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Webhook-Api-Key", headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Injected-Header", headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", body!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", body!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", body!, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(new FakeHttpMessageHandler(sendAsync))
        {
            BaseAddress = new Uri("https://webhooks.requestcatcher.com/"),
            Timeout = TimeSpan.FromSeconds(2)
        };

    private static RequestCatcherInboundObserver CreateObserver(HttpClient client, bool enabled) =>
        new(
            client,
            Options.Create(new RequestCatcherOptions
            {
                Enabled = enabled,
                BaseUrl = "https://webhooks.requestcatcher.com",
                TimeoutSeconds = 2
            }),
            NullLogger<RequestCatcherInboundObserver>.Instance);

    private static MarketingLeadWebhookRequest ValidRequest() => new(
        " external-001 ",
        " HubSpot ",
        " Ada ",
        " Lovelace ",
        " ADA@EXAMPLE.COM ",
        " Example Ltd ",
        " +35312345678 ",
        " spring-2026 ",
        DateTimeOffset.Parse("2026-11-09T10:15:00Z"),
        " event-inbound-001 ");

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => sendAsync(request, cancellationToken);
    }
}

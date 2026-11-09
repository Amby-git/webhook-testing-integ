using System.Net.Http.Json;
using LeadIntegration.Api.Contracts.Marketing;
using Microsoft.Extensions.Options;

namespace LeadIntegration.Api.Features.RequestCatcher;

public sealed class RequestCatcherInboundObserver(
    HttpClient httpClient,
    IOptions<RequestCatcherOptions> options,
    ILogger<RequestCatcherInboundObserver> logger) : IInboundWebhookObserver
{
    private readonly RequestCatcherOptions _options = options.Value;

    public async Task ObserveAsync(
        MarketingLeadWebhookRequest request,
        string contentType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var normalizedRequest = Normalize(request);
        var observation = new InboundWebhookObservation(
            "POST",
            contentType,
            normalizedRequest.EventId ?? string.Empty,
            normalizedRequest.SourcePlatform ?? string.Empty,
            correlationId,
            normalizedRequest);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "incoming")
            {
                Content = JsonContent.Create(observation)
            };
            message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            message.Headers.TryAddWithoutValidation("X-Event-ID", observation.EventId);
            message.Headers.TryAddWithoutValidation("X-Source-Platform", observation.SourcePlatform);

            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "RequestCatcher inbound observation returned HTTP {StatusCode} for event {EventId} from {SourcePlatform} with correlation {CorrelationId}",
                    (int)response.StatusCode,
                    observation.EventId,
                    observation.SourcePlatform,
                    observation.CorrelationId);
                return;
            }

            logger.LogInformation(
                "RequestCatcher recorded inbound webhook for event {EventId} from {SourcePlatform} with correlation {CorrelationId}",
                observation.EventId,
                observation.SourcePlatform,
                observation.CorrelationId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "RequestCatcher inbound observation timed out for event {EventId} from {SourcePlatform} with correlation {CorrelationId}",
                observation.EventId,
                observation.SourcePlatform,
                observation.CorrelationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A caller or host cancellation should not be reclassified as a RequestCatcher failure.
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "RequestCatcher inbound observation failed for event {EventId} from {SourcePlatform} with correlation {CorrelationId}; failure type {FailureType}",
                observation.EventId,
                observation.SourcePlatform,
                observation.CorrelationId,
                exception.GetType().Name);
        }
    }

    private static MarketingLeadWebhookRequest Normalize(MarketingLeadWebhookRequest request) => request with
    {
        ExternalLeadId = Normalize(request.ExternalLeadId),
        SourcePlatform = Normalize(request.SourcePlatform),
        FirstName = Normalize(request.FirstName),
        LastName = Normalize(request.LastName),
        Email = Normalize(request.Email)?.ToLowerInvariant(),
        Company = Normalize(request.Company),
        Phone = Normalize(request.Phone),
        CampaignId = Normalize(request.CampaignId),
        EventId = Normalize(request.EventId)
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

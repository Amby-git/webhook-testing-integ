using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.RequestCatcher;

public sealed record InboundWebhookObservation(
    string Method,
    string ContentType,
    string EventId,
    string SourcePlatform,
    string CorrelationId,
    MarketingLeadWebhookRequest Payload);

namespace LeadIntegration.Api.Features.Messaging;

public enum LeadEventAcceptanceOutcome
{
    Accepted,
    ValidationFailed,
    Duplicate,
    BrokerUnavailable
}

public sealed record LeadEventAcceptanceResult(
    LeadEventAcceptanceOutcome Outcome,
    LeadEventStatus? Event = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record LeadEventAcceptedResponse(
    string EventId,
    string SourcePlatform,
    string CorrelationId,
    string Status,
    string StatusUrl,
    DateTimeOffset AcceptedAt);

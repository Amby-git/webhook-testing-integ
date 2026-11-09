namespace LeadIntegration.Api.Features.Messaging;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum LeadEventProcessingStatus
{
    Accepted,
    Processing,
    Completed,
    Failed
}

/// <summary>
/// PII-free event status exposed to callers and operational tooling.
/// </summary>
public sealed record LeadEventStatus(
    string EventId,
    string SourcePlatform,
    string CorrelationId,
    LeadEventProcessingStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string? FailureReason = null);

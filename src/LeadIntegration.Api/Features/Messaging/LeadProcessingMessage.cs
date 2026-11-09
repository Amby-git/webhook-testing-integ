using LeadIntegration.Contracts.Salesforce;

namespace LeadIntegration.Api.Features.Messaging;

/// <summary>
/// Versioned internal message contract published to RabbitMQ for asynchronous Salesforce processing.
/// </summary>
public sealed record LeadProcessingMessage(
    string EventId,
    string SourcePlatform,
    string CorrelationId,
    DateTimeOffset AcceptedAt,
    SalesforceLeadDto Lead);

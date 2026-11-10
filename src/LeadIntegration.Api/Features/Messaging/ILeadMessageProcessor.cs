using LeadIntegration.Api.Features.Salesforce;

namespace LeadIntegration.Api.Features.Messaging;

public interface ILeadMessageProcessor
{
    Task<LeadMessageProcessingResult> ProcessAsync(
        LeadProcessingMessage message,
        int attemptCount,
        CancellationToken cancellationToken);
}

public enum LeadMessageProcessingOutcome
{
    Completed,
    AlreadyCompleted,
    PermanentFailure,
    TransientFailure
}

public sealed record LeadMessageProcessingResult(
    LeadMessageProcessingOutcome Outcome,
    string? SalesforceId = null,
    string? FailureReason = null,
    TimeSpan? RetryAfter = null,
    SalesforceClientFailure SalesforceFailure = SalesforceClientFailure.None);

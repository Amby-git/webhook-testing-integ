namespace LeadIntegration.Api.Features.Leads;

public enum LeadProcessingStatus
{
    Created,
    ValidationFailed,
    DuplicateEmail,
    DuplicateRequest,
    SalesforceValidationFailed,
    SalesforceRateLimited,
    SalesforceInternalError,
    SalesforceUnavailable,
    SalesforceNetworkError,
    SalesforceTimeout,
    SalesforceUnexpectedResponse,
    LocalPersistenceFailed
}

public sealed record LeadProcessingResult(
    LeadProcessingStatus Status,
    Lead? Lead = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

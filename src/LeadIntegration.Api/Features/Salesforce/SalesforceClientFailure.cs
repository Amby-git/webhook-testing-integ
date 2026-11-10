namespace LeadIntegration.Api.Features.Salesforce;

public enum SalesforceClientFailure
{
    None,
    Validation,
    RateLimited,
    InternalServerError,
    Unavailable,
    BadGateway,
    GatewayTimeout,
    Network,
    Timeout,
    UnexpectedResponse
}

public sealed record SalesforceCallResult<T>(
    T? Value,
    SalesforceClientFailure Failure,
    int? HttpStatusCode = null,
    TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Failure == SalesforceClientFailure.None && Value is not null;

    public static SalesforceCallResult<T> Success(T value) => new(value, SalesforceClientFailure.None);

    public static SalesforceCallResult<T> Failed(
        SalesforceClientFailure failure,
        int? httpStatusCode = null,
        TimeSpan? retryAfter = null) => new(default, failure, httpStatusCode, retryAfter);
}

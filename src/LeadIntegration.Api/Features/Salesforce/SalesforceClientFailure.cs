namespace LeadIntegration.Api.Features.Salesforce;

public enum SalesforceClientFailure
{
    None,
    Validation,
    RateLimited,
    InternalServerError,
    Unavailable,
    Network,
    Timeout,
    UnexpectedResponse
}

public sealed record SalesforceCallResult<T>(T? Value, SalesforceClientFailure Failure)
{
    public bool Succeeded => Failure == SalesforceClientFailure.None && Value is not null;

    public static SalesforceCallResult<T> Success(T value) => new(value, SalesforceClientFailure.None);

    public static SalesforceCallResult<T> Failed(SalesforceClientFailure failure) => new(default, failure);
}

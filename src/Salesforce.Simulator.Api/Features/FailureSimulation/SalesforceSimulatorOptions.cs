namespace Salesforce.Simulator.Api.Features.FailureSimulation;

public enum SalesforceFailureMode
{
    None,
    TooManyRequests,
    InternalServerError,
    ServiceUnavailable
}

public sealed class SalesforceSimulatorOptions
{
    public SalesforceFailureMode FailureMode { get; set; }
}

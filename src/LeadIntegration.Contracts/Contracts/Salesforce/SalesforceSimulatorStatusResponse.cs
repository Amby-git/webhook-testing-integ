namespace LeadIntegration.Contracts.Salesforce;

/// <summary>
/// Service status returned by the Salesforce simulator API.
/// </summary>
public sealed record SalesforceSimulatorStatusResponse(
    string Service,
    string Status,
    bool Simulated,
    string ConfiguredFailureMode);

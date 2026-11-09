namespace LeadIntegration.Contracts.Salesforce;

/// <summary>
/// Created Lead representation returned by the Salesforce simulator.
/// </summary>
public sealed record SalesforceLeadResponse(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? LeadSource,
    string? ExternalLeadId,
    string? CampaignId);

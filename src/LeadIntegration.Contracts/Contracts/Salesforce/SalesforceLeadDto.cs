namespace LeadIntegration.Contracts.Salesforce;

/// <summary>
/// Outbound lead payload shaped for the Salesforce Lead API.
/// </summary>
/// <remarks>
/// Company is nullable at the transport-contract level; the integration validates it as required
/// before sending this payload to the Salesforce simulator.
/// </remarks>
public sealed record SalesforceLeadDto(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? LeadSource,
    string? ExternalLeadId,
    string? CampaignId);

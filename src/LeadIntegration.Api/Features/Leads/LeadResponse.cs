namespace LeadIntegration.Api.Features.Leads;

public sealed record LeadResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Source,
    string Company,
    string? Phone,
    string? CampaignId,
    string SalesforceId);

namespace LeadIntegration.Api.Features.Leads;

public sealed record Lead(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Source,
    string Company,
    string? Phone,
    string? CampaignId,
    string ExternalLeadId,
    string EventId,
    string SalesforceId);

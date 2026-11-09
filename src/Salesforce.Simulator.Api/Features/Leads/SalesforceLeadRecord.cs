using LeadIntegration.Contracts.Salesforce;

namespace Salesforce.Simulator.Api.Features.Leads;

public sealed record SalesforceLeadRecord(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? LeadSource,
    string? ExternalLeadId,
    string? CampaignId)
{
    public static SalesforceLeadRecord From(string id, SalesforceLeadDto lead) => new(
        id,
        lead.FirstName,
        lead.LastName,
        lead.Email,
        lead.Phone,
        lead.Company,
        lead.LeadSource,
        lead.ExternalLeadId,
        lead.CampaignId);

    public SalesforceLeadResponse ToResponse() => new(
        Id,
        FirstName,
        LastName,
        Email,
        Phone,
        Company,
        LeadSource,
        ExternalLeadId,
        CampaignId);
}

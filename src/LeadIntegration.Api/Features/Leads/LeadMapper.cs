using LeadIntegration.Api.Contracts.Marketing;

namespace LeadIntegration.Api.Features.Leads;

public sealed class LeadMapper
{
    public Lead Map(MarketingLeadWebhookRequest request, string salesforceId) => new(
        Guid.NewGuid(),
        request.FirstName!.Trim(),
        request.LastName!.Trim(),
        request.Email!.Trim(),
        request.SourcePlatform!.Trim(),
        request.Company!.Trim(),
        NormalizeOptional(request.Phone),
        NormalizeOptional(request.CampaignId),
        request.ExternalLeadId!.Trim(),
        request.EventId!.Trim(),
        salesforceId);

    public Lead MapUpdate(LeadRequest request, Lead currentLead) => currentLead with
    {
        FirstName = request.FirstName!.Trim(),
        LastName = request.LastName!.Trim(),
        Email = request.Email!.Trim(),
        Source = request.Source!.Trim()
    };

    public LeadResponse ToResponse(Lead lead) => new(
        lead.Id,
        lead.FirstName,
        lead.LastName,
        lead.Email,
        lead.Source,
        lead.Company,
        lead.Phone,
        lead.CampaignId,
        lead.SalesforceId);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

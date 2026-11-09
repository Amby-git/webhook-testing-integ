using LeadIntegration.Api.Contracts.Marketing;
using LeadIntegration.Contracts.Salesforce;

namespace LeadIntegration.Api.Features.Salesforce;

/// <summary>
/// Maps marketing platform lead events into the outbound Salesforce Lead contract.
/// </summary>
public sealed class SalesforceLeadMapper
{
    public SalesforceLeadDto Map(MarketingLeadWebhookRequest request) => new(
        Normalize(request.FirstName),
        Normalize(request.LastName),
        Normalize(request.Email)?.ToLowerInvariant(),
        Normalize(request.Phone),
        Company: Normalize(request.Company),
        LeadSource: Normalize(request.SourcePlatform),
        ExternalLeadId: Normalize(request.ExternalLeadId),
        CampaignId: Normalize(request.CampaignId));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

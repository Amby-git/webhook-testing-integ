namespace LeadIntegration.Api.Contracts.Marketing;

/// <summary>
/// Inbound lead event contract received from a third-party marketing platform.
/// </summary>
public sealed record MarketingLeadWebhookRequest(
    string? ExternalLeadId,
    string? SourcePlatform,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Company,
    string? Phone,
    string? CampaignId,
    DateTimeOffset? CreatedAt,
    string? EventId);

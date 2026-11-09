using LeadIntegration.Api.Contracts.Marketing;
using LeadIntegration.Api.Features.Leads;
using LeadIntegration.Api.Features.Salesforce;

namespace LeadIntegration.Api.Tests;

public sealed class SalesforceLeadMapperTests
{
    private readonly SalesforceLeadMapper _mapper = new();
    private readonly MarketingLeadValidator _validator = new();

    [Fact]
    public void ValidMarketingLead_MapsToSalesforceLead()
    {
        var request = ValidMarketingLead();

        var result = _mapper.Map(request);

        Assert.Equal("Ada", result.FirstName);
        Assert.Equal("Lovelace", result.LastName);
        Assert.Equal("Analytical Engines Ltd", result.Company);
        Assert.Equal("HubSpot", result.LeadSource);
        Assert.Equal("mkt-001", result.ExternalLeadId);
    }

    [Fact]
    public void MissingCompany_ProducesValidationError()
    {
        var errors = _validator.Validate(ValidMarketingLead() with { Company = "  " });

        Assert.Contains(nameof(MarketingLeadWebhookRequest.Company), errors.Keys);
    }

    [Fact]
    public void Email_IsNormalized()
    {
        var result = _mapper.Map(ValidMarketingLead() with { Email = " ADA@EXAMPLE.COM " });

        Assert.Equal("ada@example.com", result.Email);
    }

    private static MarketingLeadWebhookRequest ValidMarketingLead() => new(
        ExternalLeadId: "  mkt-001  ",
        SourcePlatform: "  HubSpot  ",
        FirstName: "  Ada ",
        LastName: " Lovelace  ",
        Email: " ADA@EXAMPLE.COM ",
        Company: " Analytical Engines Ltd ",
        Phone: " +353 (1) 234-5678 ",
        CampaignId: " spring-2026 ",
        CreatedAt: DateTimeOffset.Parse("2026-11-09T10:15:00Z"),
        EventId: " event-001 ");
}

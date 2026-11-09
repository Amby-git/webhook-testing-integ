namespace LeadIntegration.Api.Features.Leads;

public sealed record LeadRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Source);

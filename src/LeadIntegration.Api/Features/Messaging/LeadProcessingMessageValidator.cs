using System.ComponentModel.DataAnnotations;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class LeadProcessingMessageValidator
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public string? Validate(LeadProcessingMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.EventId) ||
            string.IsNullOrWhiteSpace(message.SourcePlatform) ||
            string.IsNullOrWhiteSpace(message.CorrelationId) || message.AcceptedAt == default)
        {
            return "The message envelope is missing required fields.";
        }

        var lead = message.Lead;
        if (lead is null || string.IsNullOrWhiteSpace(lead.LastName) ||
            string.IsNullOrWhiteSpace(lead.Company) || string.IsNullOrWhiteSpace(lead.Email) ||
            !EmailValidator.IsValid(lead.Email))
        {
            return "The Salesforce lead payload is invalid or missing required fields.";
        }

        return null;
    }
}

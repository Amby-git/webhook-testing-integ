namespace LeadIntegration.Api.Features.Messaging;

public interface ILeadMessagePublisher
{
    Task PublishAsync(LeadProcessingMessage message, CancellationToken cancellationToken);
}

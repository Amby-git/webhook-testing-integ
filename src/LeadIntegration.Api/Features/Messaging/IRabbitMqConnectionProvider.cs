using RabbitMQ.Client;

namespace LeadIntegration.Api.Features.Messaging;

public interface IRabbitMqConnectionProvider
{
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken);
}

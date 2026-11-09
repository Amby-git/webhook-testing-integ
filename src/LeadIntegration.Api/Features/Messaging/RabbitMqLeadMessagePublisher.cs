using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class RabbitMqLeadMessagePublisher : ILeadMessagePublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqLeadMessagePublisher> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqLeadMessagePublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqLeadMessagePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(LeadProcessingMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await GetConnectionAsync(cancellationToken);
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);
            await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);

            await channel.ExchangeDeclareAsync(
                _options.ExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(
                _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(
                _options.QueueName,
                _options.ExchangeName,
                _options.RoutingKey,
                cancellationToken: cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.EventId,
                CorrelationId = message.CorrelationId,
                Headers = new Dictionary<string, object?>
                {
                    ["source-platform"] = message.SourcePlatform
                }
            };

            await channel.BasicPublishAsync(
                _options.ExchangeName,
                _options.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published lead event {EventId} from {SourcePlatform} with correlation {CorrelationId}",
                message.EventId,
                message.SourcePlatform,
                message.CorrelationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBrokerFailure(exception))
        {
            _logger.LogWarning(
                "RabbitMQ publish failed for event {EventId} from {SourcePlatform}; failure type {FailureType}",
                message.EventId,
                message.SourcePlatform,
                exception.GetType().Name);
            throw new RabbitMqUnavailableException("RabbitMQ could not confirm the lead event publish.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        if (string.IsNullOrWhiteSpace(_options.UserName) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new RabbitMqUnavailableException(
                "RabbitMQ credentials are not configured. Set RabbitMq__UserName and RabbitMq__Password.");
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds),
                AutomaticRecoveryEnabled = true,
                ClientProvidedName = "LeadIntegration.Api publisher"
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static bool IsBrokerFailure(Exception exception) => exception is
        BrokerUnreachableException or
        AlreadyClosedException or
        OperationInterruptedException or
        IOException or
        SocketException or
        TimeoutException;
}

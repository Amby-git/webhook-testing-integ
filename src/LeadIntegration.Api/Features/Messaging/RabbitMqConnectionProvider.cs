using System.Net.Sockets;
using System.IO;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnectionProvider> logger) : IRabbitMqConnectionProvider, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
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
                ClientProvidedName = "LeadIntegration.Api"
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Connected to RabbitMQ at {HostName}:{Port} on virtual host {VirtualHost}",
                _options.HostName, _options.Port, _options.VirtualHost);
            return _connection;
        }
        catch (Exception exception) when (IsBrokerFailure(exception))
        {
            logger.LogWarning("RabbitMQ connection failed with {FailureType}", exception.GetType().Name);
            throw new RabbitMqUnavailableException("RabbitMQ is unavailable.", exception);
        }
        finally
        {
            _connectionLock.Release();
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

    private static bool IsBrokerFailure(Exception exception) => exception is
        BrokerUnreachableException or AlreadyClosedException or OperationInterruptedException or
        AuthenticationFailureException or PossibleAuthenticationFailureException or ConnectFailureException or
        IOException or SocketException or TimeoutException;
}

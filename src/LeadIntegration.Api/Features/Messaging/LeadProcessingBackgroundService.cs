using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LeadIntegration.Api.Features.Messaging;

public sealed class LeadProcessingBackgroundService(
    IRabbitMqConnectionProvider connectionProvider,
    IServiceScopeFactory scopeFactory,
    ILeadEventStore eventStore,
    LeadProcessingMessageValidator validator,
    IOptions<RabbitMqOptions> options,
    ILogger<LeadProcessingBackgroundService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ConsumerEnabled)
        {
            logger.LogInformation("RabbitMQ consumer is disabled by configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("RabbitMQ consumer session failed with {FailureType}; reconnecting in {DelaySeconds}s",
                    exception.GetType().Name, _options.ReconnectDelaySeconds);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task RunConsumerSessionAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(channelOptions, stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, _options.ConsumerPrefetchCount, global: false, stoppingToken);

        var sessionEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await HandleDeliveryAsync(channel, delivery, stoppingToken);
        consumer.ShutdownAsync += (_, _) =>
        {
            sessionEnded.TrySetResult();
            return Task.CompletedTask;
        };
        consumer.UnregisteredAsync += (_, _) =>
        {
            sessionEnded.TrySetResult();
            return Task.CompletedTask;
        };

        var consumerTag = await channel.BasicConsumeAsync(
            _options.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        logger.LogInformation("RabbitMQ consumer {ConsumerTag} started on queue {QueueName}", consumerTag, _options.QueueName);

        try
        {
            await Task.WhenAny(sessionEnded.Task, Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken));
        }
        finally
        {
            if (channel.IsOpen && !stoppingToken.IsCancellationRequested)
            {
                await channel.BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None);
            }
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        LeadProcessingMessage? message = null;
        string eventId = delivery.BasicProperties.MessageId ?? "unknown";
        string sourcePlatform = HeaderString(delivery.BasicProperties.Headers, "source-platform") ?? "unknown";
        string correlationId = delivery.BasicProperties.CorrelationId ?? "unknown";
        var retryCount = HeaderInt(delivery.BasicProperties.Headers, "lead-retry-count");

        try
        {
            message = JsonSerializer.Deserialize<LeadProcessingMessage>(delivery.Body.Span);
            var validationFailure = validator.Validate(message);
            if (validationFailure is not null)
            {
                await PublishDeadLetterAsync(channel, delivery, validationFailure, retryCount, stoppingToken);
                if (message is not null)
                {
                    eventStore.MarkFailed(message, retryCount, validationFailure);
                }
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogWarning("Invalid RabbitMQ lead event {EventId} from {SourcePlatform} sent to dead-letter queue",
                    eventId, sourcePlatform);
                return;
            }

            eventId = message!.EventId;
            sourcePlatform = message.SourcePlatform;
            correlationId = message.CorrelationId;

            var existing = eventStore.Get(sourcePlatform, eventId);
            if (existing?.Status == LeadEventProcessingStatus.Completed)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogInformation("Duplicate completed event {EventId} from {SourcePlatform} acknowledged",
                    eventId, sourcePlatform);
                return;
            }

            var attempt = retryCount + 1;
            eventStore.MarkProcessing(message, attempt);
            logger.LogInformation("Processing lead event {EventId} from {SourcePlatform}, correlation {CorrelationId}, attempt {Attempt}",
                eventId, sourcePlatform, correlationId, attempt);

            await using var scope = scopeFactory.CreateAsyncScope();
            var messageProcessor = scope.ServiceProvider.GetRequiredService<ILeadMessageProcessor>();
            var result = await messageProcessor.ProcessAsync(message, attempt, stoppingToken);
            if (result.Outcome is LeadMessageProcessingOutcome.Completed or LeadMessageProcessingOutcome.AlreadyCompleted)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            if (result.Outcome == LeadMessageProcessingOutcome.PermanentFailure)
            {
                var reason = result.FailureReason ?? "Lead processing failed permanently.";
                await PublishDeadLetterAsync(channel, delivery, reason, retryCount, stoppingToken);
                eventStore.MarkFailed(message, attempt, reason);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            if (LeadRetryPolicy.ShouldDeadLetter(retryCount, _options.MaximumRetryCount))
            {
                var reason = $"Retry limit reached after Salesforce failure {result.SalesforceFailure}.";
                await PublishDeadLetterAsync(channel, delivery, reason, retryCount, stoppingToken);
                eventStore.MarkFailed(message, attempt, reason);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogError("Lead event {EventId} from {SourcePlatform} exhausted retries and entered dead-letter queue",
                    eventId, sourcePlatform);
                return;
            }

            var nextRetry = retryCount + 1;
            var delay = LeadRetryPolicy.GetDelay(nextRetry,
                _options.InitialRetryDelayMilliseconds,
                _options.MaximumRetryDelayMilliseconds,
                result.RetryAfter);
            await PublishRetryAsync(channel, delivery, nextRetry, delay, stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            logger.LogWarning("Retrying event {EventId} from {SourcePlatform}; retry {RetryCount} in {DelayMs}ms after {Failure}",
                eventId, sourcePlatform, nextRetry, delay.TotalMilliseconds, result.SalesforceFailure);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the delivery unacknowledged. Closing the channel on host shutdown returns it to RabbitMQ.
        }
        catch (JsonException)
        {
            try
            {
                await PublishDeadLetterAsync(channel, delivery, "Message body is not valid JSON.", retryCount, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError("Could not dead-letter malformed RabbitMQ message; failure type {FailureType}",
                    exception.GetType().Name);
                await DelayThenRequeueAsync(channel, delivery.DeliveryTag, stoppingToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError("Processing event {EventId} from {SourcePlatform} failed unexpectedly with {FailureType}; delivery will be requeued",
                eventId, sourcePlatform, exception.GetType().Name);
            await DelayThenRequeueAsync(channel, delivery.DeliveryTag, stoppingToken);
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Direct, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_options.QueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_options.QueueName, _options.ExchangeName, _options.RoutingKey,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(_options.RetryExchangeName, ExchangeType.Direct, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_options.RetryQueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.ExchangeName,
                ["x-dead-letter-routing-key"] = _options.RoutingKey
            }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_options.RetryQueueName, _options.RetryExchangeName, _options.RetryRoutingKey,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(_options.DeadLetterExchangeName, ExchangeType.Direct, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_options.DeadLetterQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_options.DeadLetterQueueName, _options.DeadLetterExchangeName,
            _options.DeadLetterRoutingKey, cancellationToken: cancellationToken);
    }

    private async Task PublishRetryAsync(IChannel channel, BasicDeliverEventArgs delivery, int retryCount,
        TimeSpan delay, CancellationToken cancellationToken)
    {
        var properties = CopyProperties(delivery.BasicProperties);
        properties.Headers ??= new Dictionary<string, object?>();
        properties.Headers["lead-retry-count"] = (long)retryCount;
        properties.Expiration = Math.Ceiling(delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        await channel.BasicPublishAsync(_options.RetryExchangeName, _options.RetryRoutingKey,
            mandatory: true, basicProperties: properties, body: delivery.Body, cancellationToken: cancellationToken);
    }

    private async Task PublishDeadLetterAsync(IChannel channel, BasicDeliverEventArgs delivery, string reason,
        int retryCount, CancellationToken cancellationToken)
    {
        var properties = CopyProperties(delivery.BasicProperties);
        properties.Headers ??= new Dictionary<string, object?>();
        properties.Headers["lead-retry-count"] = (long)retryCount;
        properties.Headers["dead-letter-reason"] = reason;
        await channel.BasicPublishAsync(_options.DeadLetterExchangeName, _options.DeadLetterRoutingKey,
            mandatory: true, basicProperties: properties, body: delivery.Body, cancellationToken: cancellationToken);
    }

    private BasicProperties CopyProperties(IReadOnlyBasicProperties source) => new()
    {
        ContentType = source.ContentType,
        ContentEncoding = source.ContentEncoding,
        DeliveryMode = DeliveryModes.Persistent,
        MessageId = source.MessageId,
        CorrelationId = source.CorrelationId,
        Type = source.Type,
        Headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : source.Headers.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

    private static int HeaderInt(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out var value)) return 0;
        return value switch
        {
            byte number => number,
            int number => number,
            long number when number is >= 0 and <= int.MaxValue => (int)number,
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    private static string? HeaderString(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out var value)) return null;
        return value switch
        {
            string text => text,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => value?.ToString()
        };
    }

    private async Task DelayThenRequeueAsync(IChannel channel, ulong deliveryTag, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !channel.IsOpen) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds)), cancellationToken);
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The channel shutdown returns the unacknowledged delivery to RabbitMQ.
        }
    }
}

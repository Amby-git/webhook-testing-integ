namespace LeadIntegration.Api.Features.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string ExchangeName { get; set; } = "lead.events";
    public string QueueName { get; set; } = "lead.events";
    public string RoutingKey { get; set; } = "lead.created";
    public int ConnectionTimeoutSeconds { get; set; } = 3;
    public bool ConsumerEnabled { get; set; }
    public ushort ConsumerPrefetchCount { get; set; } = 1;
    public string RetryExchangeName { get; set; } = "lead.events.retry";
    public string RetryQueueName { get; set; } = "lead.events.retry";
    public string RetryRoutingKey { get; set; } = "lead.retry";
    public string DeadLetterExchangeName { get; set; } = "lead.events.dead-letter";
    public string DeadLetterQueueName { get; set; } = "lead.events.dead-letter";
    public string DeadLetterRoutingKey { get; set; } = "lead.dead-letter";
    public int MaximumRetryCount { get; set; } = 5;
    public int InitialRetryDelayMilliseconds { get; set; } = 1000;
    public int MaximumRetryDelayMilliseconds { get; set; } = 30000;
    public int ReconnectDelaySeconds { get; set; } = 5;
}

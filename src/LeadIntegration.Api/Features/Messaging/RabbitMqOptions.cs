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
}

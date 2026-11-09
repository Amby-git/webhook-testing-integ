namespace LeadIntegration.Api.Features.Messaging;

public sealed class RabbitMqUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

namespace LeadIntegration.Api.Features.RequestCatcher;

public sealed class RequestCatcherOptions
{
    public const string SectionName = "RequestCatcher";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://webhooks.requestcatcher.com";
    public int TimeoutSeconds { get; set; } = 2;
}

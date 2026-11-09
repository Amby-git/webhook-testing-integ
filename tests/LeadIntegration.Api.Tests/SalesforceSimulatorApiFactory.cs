using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Salesforce.Simulator.Api;

namespace LeadIntegration.Api.Tests;

internal sealed class SalesforceSimulatorApiFactory(string failureMode = "None")
    : WebApplicationFactory<AssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SalesforceSimulator:FailureMode"] = failureMode
            }));
    }
}

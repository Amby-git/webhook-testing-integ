using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Salesforce.Simulator.Api.ApiDocumentation;

public sealed class SalesforceSimulatorOpenApiTransformer : IOpenApiOperationTransformer
{
    private const string LeadResponseJson = """
        {"id":"00Q3A40C6277C54F6A","firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","phone":"+35312345678","company":"Analytical Engines Ltd","leadSource":"HubSpot","externalLeadId":"mkt-001","campaignId":"spring-2026"}
        """;

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath?.Trim('/') ?? string.Empty;
        var method = context.Description.HttpMethod ?? string.Empty;

        if (path == "api/salesforce/leads" && method == "POST")
        {
            AddRequestExample(operation, "Salesforce Lead", "A valid B2B Lead payload.",
                """{"firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","phone":"+35312345678","company":"Analytical Engines Ltd","leadSource":"HubSpot","externalLeadId":"mkt-001","campaignId":"spring-2026"}""");
            AddResponseExample(operation, "201", LeadResponseJson);
        }
        else if (path == "api/salesforce/leads" && method == "GET")
        {
            AddResponseExample(operation, "200", $"[{LeadResponseJson}]");
        }
        else if (path == "api/salesforce/leads/{id}" && method == "GET")
        {
            AddPathExample(operation);
            AddResponseExample(operation, "200", LeadResponseJson);
        }
        else if (path == "api/salesforce/leads/{id}" && method == "PUT")
        {
            AddPathExample(operation);
            AddRequestExample(operation, "Replacement Salesforce Lead", "Replace the stored Lead fields.",
                """{"firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","phone":"+35312345678","company":"Analytical Engines Ltd","leadSource":"HubSpot","externalLeadId":"mkt-001","campaignId":"spring-2026"}""");
            AddResponseExample(operation, "200", LeadResponseJson);
        }
        else if (path == "api/salesforce/status")
        {
            AddResponseExample(operation, "200", """{"service":"Salesforce CRM Simulator","status":"available","simulated":true,"configuredFailureMode":"None"}""");
        }

        foreach (var response in operation.Responses ?? [])
        {
            response.Value.Description = ResponseDescription(path, method, response.Key);
            if (int.TryParse(response.Key, out var statusCode) && statusCode >= 400)
            {
                AddResponseExample(operation, response.Key, ProblemJson(statusCode));
            }
        }

        return Task.CompletedTask;
    }

    private static string ResponseDescription(string path, string method, string status) => (path, method, status) switch
    {
        ("api/salesforce/leads", "POST", "201") => "The Lead was created in the simulator and a Location header identifies the record.",
        ("api/salesforce/leads", "POST", "400") => "Required Salesforce Lead fields are missing or invalid.",
        ("api/salesforce/status", "GET", "503") => "The simulator is configured as unavailable.",
        (_, _, "400") => "Required Salesforce Lead fields are missing or invalid.",
        (_, _, "404") => "No simulated Salesforce Lead exists with the supplied ID.",
        (_, _, "429") => "The configured rate-limit failure was simulated; Retry-After is five seconds.",
        (_, _, "500") => "The configured internal-server failure was simulated.",
        (_, _, "503") => "The configured service-unavailable failure was simulated.",
        ("api/salesforce/leads", "GET", "200") => "All Leads currently stored in the simulator were returned.",
        ("api/salesforce/leads/{id}", "GET", "200") => "The requested Lead was found.",
        ("api/salesforce/leads/{id}", "PUT", "200") => "The existing Lead was updated.",
        ("api/salesforce/status", "GET", "200") => "The simulator status and configured failure mode were returned.",
        _ => "The request completed successfully."
    };

    private static string ProblemJson(int statusCode)
    {
        if (statusCode == StatusCodes.Status400BadRequest)
        {
            return """{"type":"about:blank","title":"One or more validation errors occurred.","status":400,"errors":{"Company":["Company is required for this simulated B2B Lead API."]}}""";
        }

        var title = statusCode switch
        {
            404 => "Lead not found",
            429 => "Simulated Salesforce failure",
            500 => "Simulated Salesforce failure",
            503 => "Simulated Salesforce failure",
            _ => "Request failed"
        };

        return $$"""{"type":"about:blank","title":"{{title}}","status":{{statusCode}},"detail":"Example simulator response.","simulated":true}""";
    }

    private static void AddRequestExample(OpenApiOperation operation, string name, string description, string json)
    {
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var content) == true)
        {
            content.Examples ??= new Dictionary<string, IOpenApiExample>();
            content.Examples[name] = new OpenApiExample
            {
                Summary = name,
                Description = description,
                Value = JsonNode.Parse(json)
            };
        }
    }

    private static void AddResponseExample(OpenApiOperation operation, string status, string json)
    {
        if (operation.Responses?.TryGetValue(status, out var response) == true && response.Content is not null)
        {
            var mediaType = response.Content.Values.FirstOrDefault();
            if (mediaType is not null)
            {
                mediaType.Example = JsonNode.Parse(json);
            }
        }
    }

    private static void AddPathExample(OpenApiOperation operation)
    {
        if (operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "id") is OpenApiParameter id)
        {
            id.Example = JsonValue.Create("00Q3A40C6277C54F6A");
        }
    }
}

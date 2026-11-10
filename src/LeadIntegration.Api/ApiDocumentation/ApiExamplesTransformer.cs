using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LeadIntegration.Api.ApiDocumentation;

public sealed class ApiExamplesTransformer : IOpenApiOperationTransformer
{
    private const string ExampleLeadJson = """
        {"id":"3a40c62e-77c5-4f69-88c5-59f856f83ce1","firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","source":"HubSpot","company":"Analytical Engines Ltd","phone":"+35312345678","campaignId":"spring-2026","salesforceId":"00Q3A40C6277C54F6A"}
        """;

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath?.TrimStart('/') ?? string.Empty;
        var method = context.Description.HttpMethod ?? string.Empty;

        if (path == "webhooks/leads")
        {
            AddRequestExample(operation, "Valid lead event", "Submit one lead event from a marketing platform.",
                """{"externalLeadId":"mkt-001","sourcePlatform":"HubSpot","firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","company":"Analytical Engines Ltd","phone":"+35312345678","campaignId":"spring-2026","createdAt":"2026-11-09T10:15:00Z","eventId":"event-001"}""");
            AddHeader(operation, "X-Webhook-Api-Key", required: true, "local-dev-key");
            AddHeader(operation, "Idempotency-Key", required: false, "campaign-42-ada-001");
            AddResponseExample(operation, "201", ExampleLeadJson);
        }
        else if (path == "webhooks/leads/async")
        {
            AddRequestExample(operation, "Valid asynchronous lead event", "Submit a lead event for asynchronous RabbitMQ processing.",
                """{"externalLeadId":"mkt-async-001","sourcePlatform":"HubSpot","firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","company":"Analytical Engines Ltd","phone":"+35312345678","campaignId":"spring-2026","createdAt":"2026-11-09T10:15:00Z","eventId":"event-async-001"}""");
            AddHeader(operation, "X-Webhook-Api-Key", required: true, "local-dev-key");
            AddHeader(operation, "X-Correlation-Id", required: false, "corr-async-001");
            AddResponseExample(operation, "202", """{"eventId":"event-async-001","sourcePlatform":"HubSpot","correlationId":"corr-async-001","status":"Accepted","statusUrl":"/integration/events/HubSpot/event-async-001","acceptedAt":"2026-11-09T10:15:00Z"}""");
        }
        else if (path == "integration/events/{sourcePlatform}/{eventId}")
        {
            AddHeader(operation, "X-Webhook-Api-Key", required: true, "local-dev-key");
            AddResponseExample(operation, "200", """{"eventId":"event-async-001","sourcePlatform":"HubSpot","correlationId":"corr-async-001","status":"Accepted","attemptCount":0,"createdAt":"2026-11-09T10:15:00Z","lastUpdatedAt":"2026-11-09T10:15:00Z","failureReason":null}""");
        }
        else if (path == "integration/events/failed")
        {
            AddHeader(operation, "X-Webhook-Api-Key", required: true, "local-dev-key");
            AddResponseExample(operation, "200", """[{"eventId":"event-async-001","sourcePlatform":"HubSpot","correlationId":"corr-async-001","status":"Failed","attemptCount":6,"createdAt":"2026-11-09T10:15:00Z","lastUpdatedAt":"2026-11-09T10:15:30Z","failureReason":"Retry limit reached after Salesforce failure Unavailable.","salesforceId":null}]""");
        }
        else if (path == "leads" && method == "GET")
        {
            AddResponseExample(operation, "200", $"[{ExampleLeadJson}]");
        }
        else if (path == "leads/{id}" && method == "GET")
        {
            AddPathExample(operation, "3a40c62e-77c5-4f69-88c5-59f856f83ce1");
            AddResponseExample(operation, "200", ExampleLeadJson);
        }
        else if (path == "leads/{id}" && method == "PUT")
        {
            AddPathExample(operation, "3a40c62e-77c5-4f69-88c5-59f856f83ce1");
            AddRequestExample(operation, "Replacement lead", "Replace the existing lead details.",
                """{"firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","source":"conference"}""");
            AddResponseExample(operation, "200", ExampleLeadJson);
        }
        else if (path == "leads/{id}" && method == "DELETE")
        {
            AddPathExample(operation, "3a40c62e-77c5-4f69-88c5-59f856f83ce1");
        }
        else if (path == "health")
        {
            AddResponseExample(operation, "200", """{"status":"healthy"}""");
        }
        else if (path == "integration/salesforce/status")
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

    private static string ProblemJson(int statusCode)
    {
        var title = statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            502 => "Bad Gateway",
            504 => "Gateway Timeout",
            _ => "Request Failed"
        };

        if (statusCode == StatusCodes.Status422UnprocessableEntity)
        {
            return """{"type":"about:blank","title":"One or more validation errors occurred.","status":422,"detail":"The Salesforce simulator rejected the mapped payload."}""";
        }

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            return """{"type":"about:blank","title":"One or more validation errors occurred.","status":400,"errors":{"Company":["Company is required for this B2B lead integration."]}}""";
        }

        return $$"""{"type":"about:blank","title":"{{title}}","status":{{statusCode}},"detail":"Example error response."}""";
    }

    private static string ResponseDescription(string path, string method, string statusCode) => (path, method, statusCode) switch
    {
        ("webhooks/leads", "POST", "201") => "Salesforce created the lead and the integration API stored its local representation.",
        ("webhooks/leads/async", "POST", "202") => "RabbitMQ confirmed the event publish. Salesforce processing has not necessarily completed.",
        ("webhooks/leads/async", "POST", "503") => "RabbitMQ did not confirm the event publish; the API did not accept the event.",
        ("webhooks/leads/async", "POST", "409") => "An event with this source platform and event ID has already been accepted.",
        ("integration/events/{sourcePlatform}/{eventId}", "GET", "200") => "The current in-memory processing status for the event was returned.",
        ("webhooks/leads", "POST", "400") => "The request body is malformed or required marketing lead fields are missing or invalid.",
        ("webhooks/leads", "POST", "401") => "The webhook API key is missing or invalid.",
        ("webhooks/leads", "POST", "409") => "The lead email or idempotency key has already been used.",
        ("webhooks/leads", "POST", "422") => "The Salesforce simulator rejected the mapped lead payload.",
        (_, _, "422") => "The JSON request is valid, but one or more required business fields are invalid.",
        ("webhooks/leads", "POST", "429") => "The caller exceeded the configured webhook request limit.",
        (_, _, "400") => "The supplied ID is malformed or the request body cannot be deserialized.",
        (_, _, "404") => "No lead exists with the supplied ID.",
        ("leads/{id}", "PUT", "409") => "Another lead already uses the supplied email address.",
        ("webhooks/leads", "POST", "503") => "The simulated Salesforce integration is unavailable; the lead was not accepted.",
        ("webhooks/leads", "POST", "502") => "The Salesforce simulator returned an internal or unexpected response.",
        ("webhooks/leads", "POST", "504") => "The Salesforce simulator did not respond before the configured timeout.",
        ("integration/salesforce/status", "GET", "503") => "The simulated Salesforce integration is configured as unavailable.",
        (_, _, "500") => "An unexpected server error occurred.",
        (_, _, "200") when path == "leads" => "The stored leads were returned successfully.",
        (_, _, "200") when path == "leads/{id}" && method == "PUT" => "The lead was updated successfully.",
        (_, _, "200") when path == "leads/{id}" => "The requested lead was found.",
        (_, _, "200") when path == "health" => "The API is healthy.",
        (_, _, "200") when path == "integration/salesforce/status" => "The Salesforce integration simulation is available.",
        (_, _, "204") => "The lead was deleted successfully.",
        _ => "The request completed successfully."
    };

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

    private static void AddResponseExample(OpenApiOperation operation, string statusCode, string json)
    {
        if (operation.Responses?.TryGetValue(statusCode, out var response) == true && response.Content is not null)
        {
            var content = response.Content.Values.FirstOrDefault();
            if (content is not null)
            {
                content.Example = JsonNode.Parse(json);
            }
        }
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string example)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            Example = JsonValue.Create(example)
        });
    }

    private static void AddPathExample(OpenApiOperation operation, string example)
    {
        var idParameter = operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "id") as OpenApiParameter;
        if (idParameter is not null)
        {
            idParameter.Example = JsonValue.Create(example);
        }
    }
}

# RequestCatcher inbound observability

RequestCatcher is an optional diagnostic destination for the asynchronous marketing webhook. It is not RabbitMQ, the Salesforce Simulator, or a required part of lead acceptance. Its outbound HTTP call is best-effort: an HTTP error or its short timeout is logged without logging the request body or personal data, and RabbitMQ publishing continues.

## Development configuration

`src/LeadIntegration.Api/appsettings.Development.json` contains these development defaults:

```json
{
  "RequestCatcher": {
    "Enabled": false,
    "BaseUrl": "https://webhooks.requestcatcher.com",
    "TimeoutSeconds": 2
  }
}
```

Enable it in the LeadIntegration.Api Rider run configuration by adding these environment variables:

```text
RequestCatcher__Enabled=true
RequestCatcher__BaseUrl=https://webhooks.requestcatcher.com
RequestCatcher__TimeoutSeconds=2
```

Restart LeadIntegration.Api after changing run-configuration values. To disable forwarding, set `RequestCatcher__Enabled=false` or remove the override; the checked-in development default is disabled. The timeout is validated to be between one and five seconds.

## Incoming observation

Only `POST /webhooks/leads/async` uses this observer. After the API key filter accepts the request, the endpoint sends a best-effort `POST /incoming` to the configured base URL. The RequestCatcher entry contains:

- observed method (`POST`) and original content type;
- event ID, source platform, and correlation ID;
- a normalized `MarketingLeadWebhookRequest` payload.

The observer receives the bound DTO and explicit metadata, never `HttpContext` or the incoming headers. It sends only its own JSON content type and the explicit correlation/event/source metadata headers. It does not forward the webhook API key, Authorization, Cookie, or arbitrary headers. String values are trimmed and email is lowercased; fields not in the DTO are not copied.

Because the payload includes lead details that may be personal data, enable this feature only for controlled development/testing and use synthetic lead data. Do not log the payload or enable it in production without an approved data-handling review.

The observer call runs alongside the existing validation/idempotency/RabbitMQ acceptance flow. A RequestCatcher timeout cannot stop or delay the start of RabbitMQ publishing, though the HTTP response can wait for the observer's bounded timeout before it returns. Invalid business payloads that deserialize and pass API-key authentication can be observed; malformed JSON and unauthorized calls do not reach the endpoint handler's observer call.

## Inspecting the request

Open the RequestCatcher UI and select the catcher/subdomain named `webhooks`. Send an authenticated request to the local async endpoint; use a fresh `EventId` for each attempt because the application rejects duplicate events:

```sh
curl -i --request POST 'http://127.0.0.1:5187/webhooks/leads/async' \
  -H 'Content-Type: application/json' \
  -H 'X-Webhook-Api-Key: local-dev-key' \
  -H 'X-Correlation-ID: requestcatcher-dev-001' \
  --data-binary '{
    "externalLeadId": "requestcatcher-dev-001",
    "sourcePlatform": "HubSpot",
    "firstName": "Test",
    "lastName": "Lead",
    "email": "test@example.com",
    "company": "Example Ltd",
    "createdAt": "2026-11-09T10:15:00Z",
    "eventId": "requestcatcher-dev-001"
  }'
```

The local webhook still returns `202 Accepted` only after RabbitMQ confirms publishing. RequestCatcher is not the acceptance authority. If the feature is enabled, a separate `/incoming` entry should appear in the `webhooks` catcher. A duplicate webhook request returns `409` and can produce another inbound observation because it is a new HTTP arrival.

## `/outgoing` status

`POST /outgoing` is reserved for the future RabbitMQ consumer stage. There is no consumer or outgoing observer in the current implementation, so no request is sent to `/outgoing` yet. Enabling inbound observability does not activate Salesforce processing.

## Automated tests

Tests use a fake `HttpMessageHandler`; they do not contact RequestCatcher. They verify the destination path, normalized payload and metadata, disabled behavior, HTTP failure and timeout handling, and that credential/arbitrary headers are not forwarded.

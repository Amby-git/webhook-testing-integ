# Local RabbitMQ setup

The synchronous `POST /webhooks/leads` flow remains available. The asynchronous `POST /webhooks/leads/async` route publishes accepted events to RabbitMQ and returns HTTP 202 after publisher confirmation. In Development, `LeadProcessingBackgroundService` is enabled and consumes the durable queue, sends the mapped lead to the Salesforce Simulator, and updates the in-memory event status.

## Start the broker

From the solution root, create a local environment file and start the container:

```sh
cp .env.example .env
docker compose up -d rabbitmq
docker compose ps
```

The `.env` file contains development-only credentials and is excluded from source control. The RabbitMQ container stores broker data in the `rabbitmq_data` Docker volume.

## Connect locally

| Purpose | Address |
|---|---|
| AMQP | `localhost:5672` |
| Management UI | `http://localhost:15672` |

Use the values from `.env` to sign in. The `lead_dev` / `local_dev_only_change_me` values are for local learning only; change them for any shared environment.

The API's Development configuration sets the broker host, port, and virtual host and enables the consumer. Supply matching credentials to the API through environment variables (for example, Rider's run-configuration environment):

```text
RabbitMq__UserName=lead_dev
RabbitMq__Password=local_dev_only_change_me
```

The asynchronous route is `POST http://127.0.0.1:5187/webhooks/leads/async`. Include `X-Webhook-Api-Key: local-dev-key` and a JSON `MarketingLeadWebhookRequest`. HTTP 202 means RabbitMQ confirmed publication; it does not mean Salesforce processing is complete. Check the returned status URL for `Accepted`, `Processing`, `Completed`, or `Failed`.

Transient downstream failures are sent to the configured retry queue with an exponential delay. Permanent or exhausted messages are routed to the dead-letter queue. Inspect the queues and consumers in the Management UI. Defaults are `lead.events` for the work queue, `lead.events.retry` for retry, and `lead.events.dead-letter` for dead letters. Event status and Simulator idempotency are in memory, so broker messages can survive restarts while those application records do not.

The consumer can be disabled by setting `RabbitMq__ConsumerEnabled=false`. Outside Development it is disabled by default. Retry count and delays can be overridden with `RabbitMq__MaximumRetryCount`, `RabbitMq__InitialRetryDelayMilliseconds`, and `RabbitMq__MaximumRetryDelayMilliseconds`.

## Stop the broker

Stop the container while retaining its data:

```sh
docker compose down
```

To remove the broker and its persisted local data as well:

```sh
docker compose down --volumes
```

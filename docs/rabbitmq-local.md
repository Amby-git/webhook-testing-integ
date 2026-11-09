# Local RabbitMQ setup

The existing synchronous `POST /webhooks/leads` flow remains available. A separate `POST /webhooks/leads/async` route publishes accepted events to RabbitMQ and returns HTTP 202 after publisher confirmation. The background consumer is a later implementation stage; at this stage messages remain in the durable RabbitMQ queue.

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

The API's Development configuration sets the broker host, port, and virtual host. Supply matching credentials to the API through environment variables (for example, Rider's run-configuration environment):

```text
RabbitMq__UserName=lead_dev
RabbitMq__Password=local_dev_only_change_me
```

The asynchronous route is `POST http://127.0.0.1:5187/webhooks/leads/async`. Include `X-Webhook-Api-Key: local-dev-key` and a JSON `MarketingLeadWebhookRequest`. HTTP 202 means RabbitMQ confirmed publication; it does not mean Salesforce processed the event. Check the returned status URL for the current in-memory status, which remains `Accepted` until background processing is implemented.

## Stop the broker

Stop the container while retaining its data:

```sh
docker compose down
```

To remove the broker and its persisted local data as well:

```sh
docker compose down --volumes
```

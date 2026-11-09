# Operations

How to see what GridWise is doing, what the two alerts mean, and what to do when events or batch results are late.
Everything here runs in the `otel-lgtm` container that `docker compose up` starts with the rest of the system.

## Grafana and the dashboard

Open Grafana at http://localhost:3000 (the port is `GRAFANA_PORT`). The otel-lgtm image lets you in without a login, as an admin:
that is fine on a laptop and must not be exposed anywhere else.

The dashboard is **Dashboards → GridWise → GridWise** (direct link: http://localhost:3000/d/gridwise). It is loaded from
`observability/grafana/dashboards/gridwise.json` when Grafana starts, so a change goes through the repository, not the UI.

| Panel | What it shows | Healthy |
|---|---|---|
| Recommendation POST p95 | 95th percentile of `POST /recommendations`; the red line is the 50 ms target | well under the line |
| Requests per second, 5xx per second | traffic and server errors per service (health checks left out) | 5xx flat at 0 |
| Outbox: unpublished rows, age of the oldest | rows a service has written but not yet sent to Kafka; the red line is the alert threshold (60 s) | 0, or a few rows for a second |
| Consumer lag per group | messages the groups `recommendation`, `batch-workers` and `batch` have not read yet | 0, or falling |
| Dead letters | messages sent to a dead-letter topic in the last 5 minutes, by source topic | 0 |
| Batch rows per second | rows of batch jobs whose results were stored, processed and failed | moves while a job runs |

The metrics are exported every 15 s, so a panel is up to about 30 s behind. The lag is sampled every 5 s by the consumer loop
itself: while a consumer is busy retrying one message it does not update the lag, and the panel keeps the last value.

Traces are in **Explore → Tempo** and logs in **Explore → Loki** (see the guide below).

## Alerts

The rules are in **Alerting → Alert rules**, folder GridWise, loaded from `observability/grafana/provisioning/alerting/gridwise.yaml`.
There is no contact point: a firing alert is shown there and on the alert list, nobody is paged.

### Outbox stuck

**Fires when** the oldest unpublished outbox row of a service is older than **60 s for 2 minutes** (one alert per service).

**Why these numbers.** A publisher looks at its outbox every second, so a healthy row waits a second or two. When Kafka cannot be
reached, publishing a row gives up after 10 s and the publisher tries again 5 s later; a broker restart is over in well under a minute.
60 s is therefore far above normal and above a short blip, and the 2 minutes stop a single slow minute from firing it. Measured:
with Kafka stopped and one admin change, the alert was pending after about 90 s and firing after about 3.5 minutes.

**What it means.** Changes are saved, but the other services do not hear about them: a new plan does not reach recommendations,
chunks do not reach the workers, results do not reach Batch. Nothing is lost; the rows wait in the outbox.

**What to do.**
1. Check Kafka: `docker compose ps kafka`, then `docker compose logs --tail 50 kafka`. Start it if it is down (`docker compose up -d kafka`).
2. If Kafka is fine, look at the service of the alert: `docker compose logs --since 10m <service> | grep -i outbox`
   ("Outbox publishing failed" with the reason).
3. Once Kafka is reachable the publisher sends the waiting rows by itself, oldest first; the alert clears within a minute
   (measured: 40 s after Kafka started). No manual replay is needed.

### Dead letters

**Fires when** any message was sent to a dead-letter topic (`recommendation.dlq`, `batch.dlq`) in the **last 5 minutes**
(one alert per source topic).

**Why this threshold.** A dead-lettered message is never retried by itself: it is a plan version the read model does not have,
or a chunk whose job will not complete. Every single one needs a human, so the threshold is "more than 0". The 5 minute window
is how long the alert stays visible; it clears by itself after that. **Cleared does not mean handled**: the message is still in
the dead-letter topic until it is reprocessed.

**What to do.**
1. Read the message and its `error` header (what failed and why); the commands are in `docs/events/README.md`
   ("Dead-letter topic"), for example:
   `docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --topic recommendation.dlq --from-beginning --timeout-ms 10000 --property print.key=true --property print.headers=true`
2. Fix the cause: a bug or a missing version in the consumer, data that needed something that has arrived since, or a message
   that was wrong in the first place.
3. Reprocess: `docker compose run --rm recommendation reprocess-dlq` (or `batch reprocess-dlq` for `batch.dlq`). A message that
   fails again goes back to the end of the topic and counts as a new dead letter, so the alert fires again.

## Guide: events or batch results are late

Symptoms: a plan changed in the admin does not appear in recommendations, or a batch job stays at the same number of chunks.

1. **Dashboard.** Open the GridWise dashboard for the last 30 minutes.
   - *Outbox* rising or old: the events have not left the producing service. Go to the "Outbox stuck" steps above.
   - *Consumer lag* rising for a group: the events are in Kafka but are not being read. `recommendation` = plan and tax group
     events, `batch-workers` = chunks, `batch` = results.
   - *Dead letters* above 0: messages were given up on. Go to the "Dead letters" steps above.
   - Everything at 0 but still late: the event was probably never written (the request failed) or was handled and ignored. Go on.
2. **Trace.** In **Explore → Tempo**, search with TraceQL, for example the last admin change:
   `{ resource.service.name = "catalog" && name =~ "POST.*|PUT.*|DELETE.*" }`, or one batch chunk: `{ name = "dispatch chunk" && span.job.id = "<job id>" }`.
   A trace can be searched about 30 s after the request.
   A complete trace has the HTTP request (or `dispatch chunk`), `publish`, then `process` in the next service. Where it stops is where
   the event stopped: no `publish` = still in the outbox; `publish` but no `process` = not consumed yet; a `process` with an error = the handler failed.
3. **Logs.** A change request has a correlation id (the `X-Correlation-Id` response header, or `correlation.id` on the request span).
   In **Explore → Loki** it finds the logs of that request in the service that received it:
   `{service_name="catalog"} | CorrelationId = "<id>"`. The logs of the services further down (the consumers) do not carry it;
   find them by the trace id from step 2: `{service_name=~"recommendation|recommendation-worker|batch"} | trace_id = "<trace id>"`.
   Warnings such as "failed; retrying" or "goes to the dead-letter topic" give the reason.
4. **Pipeline status.** `scripts/pipeline-status.sh` shows, from the databases and Kafka directly, the outbox backlog of catalog and
   recommendation, the lag of the group `recommendation` and the size of `recommendation.dlq`. For batch, the same view by hand:
   ```
   docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group batch-workers
   docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group batch
   docker compose exec batch-db psql -U batch -d batch -c "select count(*), min(occurred_at) from outbox_messages where published_at is null"
   ```
5. **Dead-letter topics and reprocessing.** If messages were dead-lettered, read them, fix the cause and run `reprocess-dlq`
   (see "Dead letters"). A batch job whose chunk was dead-lettered completes once the chunk is reprocessed.

## When there are emails

Once a notification service sends emails (for example "your plan was accepted"), "the email is late" is the same question one
step further: the service would consume `recommendation.events` and send through a mail provider. The guide extends naturally:

- its consumer group appears in *Consumer lag* and its dead-letter topic in *Dead letters* (the same metrics and alert, no new rule);
- the trace continues: `publish` in recommendation, `process` in notifications, then a span for the call to the mail provider;
- one new metric and alert would be needed for the part outside Kafka: emails failed or retried at the provider, and the age of the
  oldest email not yet sent, because a provider outage looks like "Outbox stuck" for emails;
- the correlation id of the accepting request reaches the email's logs, so step 3 finds a customer's email from their request.

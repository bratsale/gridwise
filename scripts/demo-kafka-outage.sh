#!/usr/bin/env bash
# Demo: stop Kafka, change data, start Kafka -> nothing lost, nothing sent twice.
set -euo pipefail

cd "$(dirname "$0")/.."

API=http://localhost:5101
TOPIC=catalog.plan-events

step() { printf '\n=== %s ===\n' "$1"; }
expect() { printf 'Expect: %s\n' "$1"; }
psql_catalog() { docker compose exec -T catalog-db psql -U catalog -d catalog -At -c "$1"; }
outbox_rows() { psql_catalog "SELECT event_type, aggregate_id, coalesce(published_at::text, 'NULL') FROM outbox_messages WHERE aggregate_id = '$PLAN_ID' ORDER BY occurred_at, id"; }
published_count() { psql_catalog "SELECT count(*) FROM outbox_messages WHERE aggregate_id = '$PLAN_ID' AND published_at IS NOT NULL"; }

# Poll "$@" every 2 s until it succeeds or $TIMEOUT seconds pass.
wait_for() {
  local timeout=$1; shift
  local deadline=$((SECONDS + timeout))
  until "$@" > /dev/null 2>&1; do
    if (( SECONDS >= deadline )); then return 1; fi
    sleep 2
  done
}

api_ready() { [[ "$(curl -s -o /dev/null -w '%{http_code}' "$API/plans")" == 200 ]]; }
kafka_healthy() { docker compose ps kafka | grep -q '(healthy)'; }
publisher_warned() { docker compose logs --no-log-prefix catalog | grep -q 'Outbox publishing failed'; }
all_published() { [[ "$(published_count)" == 2 ]]; }

step "1. Start the stack"
echo "Does: docker compose up -d --build, then waits for GET $API/plans = 200 (max 120 s)."
expect "stack is up and the API answers 200."
docker compose up -d --build
wait_for 120 api_ready || { echo "FAIL: API did not become ready within 120 s"; exit 1; }
echo "API is ready."

step "2. Stop Kafka"
echo "Does: docker compose stop kafka."
expect "Kafka is down; the catalog API keeps working."
docker compose stop kafka

step "3. Create a plan and a second version while Kafka is down"
echo "Does: POST /plans, then POST /plans/{id}/versions with If-Match \"1\"."
expect "201 for both; the plan id comes from the Location header."
NAME="Outage demo $(date -u +%Y%m%dT%H%M%SZ)"
TODAY=$(date -u +%F)
BODY="{\"name\":\"$NAME\",\"discount\":0.05,\"effectiveFrom\":\"$TODAY\",\"tiers\":[{\"threshold\":100,\"pricePerKwh\":0.10},{\"threshold\":null,\"pricePerKwh\":0.06}]}"

HEADERS=$(curl -s -o /dev/null -D - -X POST "$API/plans" \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: demo-$(date -u +%s)-$RANDOM" \
  -d "$BODY" | sed 's/\r$//')
echo "$HEADERS" | sed -n '1p'
PLAN_ID=$(echo "$HEADERS" | sed -n 's|^[Ll]ocation: /plans/\(.*\)$|\1|p')
[[ -n "$PLAN_ID" ]] || { echo "FAIL: no Location header in the create response"; exit 1; }
echo "Plan id: $PLAN_ID"

STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/plans/$PLAN_ID/versions" \
  -H 'Content-Type: application/json' -H 'If-Match: "1"' \
  -d "${BODY/\"name\":\"$NAME\"/\"name\":\"$NAME v2\"}")
echo "Second version: HTTP $STATUS"
[[ "$STATUS" == 201 ]] || { echo "FAIL: second version returned $STATUS"; exit 1; }

step "4. Outbox while Kafka is down"
echo "Does: reads outbox_messages for this plan from catalog-db and waits (max 40 s) for the publisher warning in the catalog log."
expect "2 rows with published_at NULL, and a 'Outbox publishing failed' warning in the log."
outbox_rows
wait_for 40 publisher_warned || echo "(no publisher warning seen within 40 s)"
docker compose logs --no-log-prefix catalog | grep 'Outbox publishing failed' | sed -e :a -e '$q;N;4,$D;ba' | sed 's/^\(.\{200\}\).*/\1/'

step "5. Start Kafka"
echo "Does: docker compose start kafka and waits until it is healthy (max 120 s)."
expect "Kafka becomes healthy."
docker compose start kafka
wait_for 120 kafka_healthy || { echo "FAIL: Kafka was not healthy within 120 s"; exit 1; }
echo "Kafka is healthy."

step "6. Wait for the publisher to catch up"
echo "Does: polls catalog-db until both rows for this plan have published_at set (max 60 s)."
expect "both rows published."
wait_for 60 all_published || echo "(rows were not all published within 60 s)"
outbox_rows
PUBLISHED=$(published_count)

step "7. Read $TOPIC from the beginning"
echo "Does: kafka-console-consumer with print.key, timeout 10 s; counts messages with key $PLAN_ID."
expect "exactly 2 messages with this key."
COUNT=$(docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 --topic "$TOPIC" --from-beginning \
  --timeout-ms 10000 --property print.key=true 2>/dev/null \
  | grep -c "^${PLAN_ID}"$'\t' || true)
echo "Messages with key $PLAN_ID: $COUNT"

step "8. Result"
if [[ "$COUNT" == 2 && "$PUBLISHED" == 2 ]]; then
  echo "PASS: 2 messages in Kafka, 2 outbox rows published."
else
  echo "FAIL: messages in Kafka = $COUNT (expected 2), published outbox rows = $PUBLISHED (expected 2)."
  exit 1
fi

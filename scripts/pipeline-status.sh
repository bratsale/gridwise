#!/usr/bin/env bash
# Shows how far events are behind in the Catalog -> Kafka -> Recommendation pipeline. Read-only.
set -euo pipefail

cd "$(dirname "$0")/.."

KAFKA_BIN=/opt/kafka/bin
# Kafka asks for a login: the tools run inside the kafka container as kafka-admin, with the client properties file that
# kafka/start.sh writes there.
BOOTSTRAP=localhost:9092

psql_in() { docker compose exec -T "$1" psql -U "$2" -d "$2" -At -F ' ' -c "$3"; }

# Prints "<count> unpublished, oldest <age>" or "0 unpublished, oldest none".
outbox_backlog() {
  local row count age
  row=$(psql_in "$1" "$2" "SELECT count(*), coalesce(floor(extract(epoch FROM now() - min(occurred_at)))::text, 'none') FROM outbox_messages WHERE published_at IS NULL")
  count=${row%% *}
  age=${row##* }
  if [[ "$age" == none ]]; then echo "$count unpublished, oldest none"; else echo "$count unpublished, oldest ${age} s old"; fi
}

echo "=== 1. Outbox backlog ==="
echo "Rows a service has written but not yet sent to Kafka (published_at IS NULL)."
echo "Healthy: 0, or a few rows that are younger than a few seconds. A growing count or an old row means the publisher or Kafka is stuck."
echo "catalog:        $(outbox_backlog catalog-db catalog)"
echo "recommendation: $(outbox_backlog recommendation-db recommendation)"

echo
echo "=== 2. Consumer lag, group 'recommendation' ==="
echo "How many messages Recommendation has not processed yet (log end offset - committed offset), per partition."
echo "Healthy: 0, or small and falling. A growing lag means the consumer or its database is stuck or too slow."
LAG_OUTPUT=$(docker compose exec -T kafka "$KAFKA_BIN/kafka-consumer-groups.sh" \
  --bootstrap-server "$BOOTSTRAP" --command-config /tmp/admin-client.properties --describe --group recommendation 2>&1 || true)
echo "$LAG_OUTPUT"
TOTAL_LAG=$(echo "$LAG_OUTPUT" | awk '$5 ~ /^[0-9]+$/ { sum += ($4 ~ /^[0-9]+$/ ? $5 - $4 : $5) } END { print sum + 0 }')
echo "Total lag: $TOTAL_LAG (a partition without a committed offset counts its whole log)"

echo
echo "=== 3. Dead-letter topic size ==="
echo "Messages in recommendation.dlq: poison messages, unknown versions and messages that failed 3 times."
echo "Healthy: 0. A growing number means messages are being rejected: look at the 'error' header (see docs/events/README.md)."
offsets() {
  docker compose exec -T kafka "$KAFKA_BIN/kafka-get-offsets.sh" \
    --bootstrap-server "$BOOTSTRAP" --command-config /tmp/admin-client.properties --topic recommendation.dlq --time "$1" | awk -F: '{ sum += $3 } END { print sum + 0 }'
}
END_OFFSETS=$(offsets -1)
START_OFFSETS=$(offsets -2)
echo "recommendation.dlq: $((END_OFFSETS - START_OFFSETS)) messages"

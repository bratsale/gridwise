#!/usr/bin/env bash
# Checks Kafka authentication and the ACLs (who may use which topic and consumer group) against the running stack.
#   bash scripts/kafka-acl-check.sh        exit code 0 = all checks passed, 1 = a check failed
# Each check runs a Kafka tool as one user in a throwaway container on the compose network, against kafka:9092.
# Nothing it does changes the system:
#   - writes send an envelope of type "AclCheck", which every consumer skips (unknown event types are skipped by design);
#   - reads use an assigned partition without a consumer group and without committing, so no live group is disturbed;
#   - groups are only described.
#   - The dead-letter topics are only read: a test message there would fail every later reprocess-dlq run. Their write
#     permission is checked as an ACL entry, and in practice whenever a service dead-letters a message.
# Passwords: the same environment variables as compose (KAFKA_*_PASSWORD), with the same dev defaults.
set -uo pipefail

cd "$(dirname "$0")/.."

IMAGE=${KAFKA_IMAGE:-apache/kafka:4.4.0}
NETWORK=${COMPOSE_NETWORK:-gridwise_default}
declare -A PASSWORDS=(
  [kafka-admin]=${KAFKA_ADMIN_PASSWORD:-kafka_admin_dev_pw}
  [catalog]=${KAFKA_CATALOG_PASSWORD:-catalog_kafka_dev_pw}
  [recommendation]=${KAFKA_RECOMMENDATION_PASSWORD:-recommendation_kafka_dev_pw}
  [batch]=${KAFKA_BATCH_PASSWORD:-batch_kafka_dev_pw}
)
FAILED=0
pass() { echo "ok    $1"; }
fail() { echo "FAIL  $1"; [[ -n "${2:-}" ]] && echo "      $2"; FAILED=1; }

# Runs a command in a Kafka tools container, with a client properties file for the user (none for "anonymous") at /tmp/client.properties.
kafka() { # kafka <user> <command...>
  local user=$1; shift
  local props="request.timeout.ms=5000
default.api.timeout.ms=10000
enable.auto.commit=false"
  if [[ "$user" != anonymous ]]; then
    props+="
security.protocol=SASL_PLAINTEXT
sasl.mechanism=SCRAM-SHA-512
sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required username=\"$user\" password=\"${PASSWORDS[$user]}\";"
  fi
  docker run --rm -i --network "$NETWORK" -e KAFKA_HEAP_OPTS=-Xmx128m -e PROPS="$props" --entrypoint bash "$IMAGE" \
    -c 'printf "%s\n" "$PROPS" > /tmp/client.properties; '"$*" 2>&1
}

BIN=/opt/kafka/bin
ENVELOPE='{"id":"00000000-0000-0000-0000-0000000ac1c0","type":"AclCheck","version":1,"timestamp":"2026-01-01T00:00:00Z","correlationId":"acl-check","data":{}}'

write() { kafka "$1" "echo '$ENVELOPE' | $BIN/kafka-console-producer.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --topic $2 --request-required-acks all"; }
read_topic() { kafka "$1" "$BIN/kafka-console-consumer.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --topic $2 --partition 0 --offset earliest --max-messages 1 --timeout-ms 4000"; }
describe_group() { kafka "$1" "$BIN/kafka-consumer-groups.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --describe --group $2"; }

denied() { grep -qiE 'AuthorizationException|not authorized|Authorization failed'; }

# Allowed means it worked: no exception at all, except the harmless ones: a read of an empty partition ends with a
# TimeoutException, and a consumer group that never ran is "not found". A write prints nothing when it succeeds.
expect_allowed() { # expect_allowed <description> <output>
  local unexpected
  unexpected=$(echo "$2" | grep -E 'Exception|ERROR' | grep -vE 'TimeoutException|Error processing message, terminating consumer process|GroupIdNotFoundException|does not exist' | grep -v '^\s*at ' | head -1)
  if [[ -n "$unexpected" ]]; then fail "$1" "$unexpected"; else pass "$1"; fi
}
expect_denied() {
  if echo "$2" | denied; then pass "$1"; else fail "$1 (it was allowed)" "$(echo "$2" | tail -1)"; fi
}

echo "=== Authentication ==="
out=$(kafka anonymous "$BIN/kafka-topics.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --list")
if echo "$out" | grep -qE 'TimeoutException|Disconnected|SaslAuthentication'; then pass "a client without a login is rejected"; else fail "a client without a login was not rejected" "$out"; fi
out=$(kafka catalog "$BIN/kafka-topics.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --list")
if echo "$out" | grep -q 'catalog.plan-events'; then pass "catalog logs in"; else fail "catalog cannot log in" "$out"; fi

echo "=== Denied ==="
expect_denied "catalog cannot write to batch.chunks" "$(write catalog batch.chunks)"
expect_denied "recommendation cannot write to catalog.plan-events" "$(write recommendation catalog.plan-events)"
expect_denied "batch cannot read catalog.plan-events" "$(read_topic batch catalog.plan-events)"
expect_denied "catalog cannot read catalog.plan-events (it only writes)" "$(read_topic catalog catalog.plan-events)"
expect_denied "batch cannot use the consumer group recommendation" "$(describe_group batch recommendation)"

echo "=== Allowed ==="
expect_allowed "catalog writes catalog.plan-events" "$(write catalog catalog.plan-events)"
expect_allowed "catalog writes catalog.tax-group-events" "$(write catalog catalog.tax-group-events)"
for topic in catalog.plan-events catalog.tax-group-events batch.chunks recommendation.dlq; do
  expect_allowed "recommendation reads $topic" "$(read_topic recommendation $topic)"
done
expect_allowed "recommendation writes recommendation.events" "$(write recommendation recommendation.events)"
expect_allowed "recommendation writes batch.results" "$(write recommendation batch.results)"
for group in recommendation batch-workers recommendation-dlq-reprocess; do
  expect_allowed "recommendation uses the consumer group $group" "$(describe_group recommendation $group)"
done
expect_allowed "batch writes batch.chunks" "$(write batch batch.chunks)"
for topic in batch.results batch.dlq; do
  expect_allowed "batch reads $topic" "$(read_topic batch $topic)"
done
for group in batch batch-dlq-reprocess; do
  expect_allowed "batch uses the consumer group $group" "$(describe_group batch $group)"
done

echo "=== Write permission on the dead-letter topics (ACL entries) ==="
acls=$(kafka kafka-admin "$BIN/kafka-acls.sh --bootstrap-server kafka:9092 --command-config /tmp/client.properties --list")
for entry in "recommendation.dlq:User:recommendation" "batch.dlq:User:batch"; do
  topic=${entry%%:*}; principal=${entry#*:}
  if echo "$acls" | awk -v t="name=$topic," '/ResourcePattern/ { inside = index($0, t) > 0 } inside' | grep -q "principal=$principal, host=\*, operation=WRITE, permissionType=ALLOW"; then
    pass "$principal may write $topic"
  else
    fail "$principal has no Write ACL on $topic"
  fi
done

echo
if (( FAILED )); then echo "Some checks FAILED."; exit 1; fi
echo "All checks passed."

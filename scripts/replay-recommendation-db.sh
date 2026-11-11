#!/usr/bin/env bash
# Rebuilds Recommendation's copy of the catalog (plan_versions, tax_group_versions) from the Kafka topics alone, and checks that the
# result matches the Catalog database. Proves that the topics are a complete source of truth for the read model (assignment module 02).
#   bash scripts/replay-recommendation-db.sh
# Exit code 0 = the rebuilt copy matches the Catalog, 1 = it does not (or a step failed).
#
# What it does:
#   1. reads the expected state from catalog-db: plans, plan versions, the highest version of every plan (and the same for tax groups)
#   2. stops recommendation and recommendation-worker (the workers use another group, batch-workers, but price rows from the same copy,
#      so they would calculate against an empty catalog during the rebuild)
#   3. truncates plan_versions and tax_group_versions (schema and migrations stay). There is no separate dedup table for these events:
#      the primary keys (plan_id, version_no) and (code, version_no) with ON CONFLICT DO NOTHING are the dedup, and they go with the rows.
#      recommendations, acceptances, processed_chunks and outbox_messages are Recommendation's own data, not the copy: not touched.
#   4. resets the offsets of the consumer group 'recommendation' on catalog.plan-events and catalog.tax-group-events to the earliest
#   5. starts the service again and polls (TIMEOUT, default 60 s) until the copy matches step 1, then starts the workers
# Recommendations are unavailable while it runs (a few seconds). Do not run it while a batch job is running.
set -uo pipefail

cd "$(dirname "$0")/.."

TIMEOUT=${TIMEOUT:-60}
KAFKA_BIN=/opt/kafka/bin
BOOTSTRAP=localhost:9092
GROUP=recommendation
TOPICS=(catalog.plan-events catalog.tax-group-events)

psql_in() { docker compose exec -T "$1" psql -U "$2" -d "$2" -At -F ' ' -c "$3"; }
kafka_groups() {
  docker compose exec -T kafka "$KAFKA_BIN/kafka-consumer-groups.sh" \
    --bootstrap-server "$BOOTSTRAP" --command-config /tmp/admin-client.properties "$@"
}

# The same query on both databases, so the two sides compare as one line:
# "<plans> <plan versions> <hash of plan:max version> <tax groups> <tax group versions> <hash of code:max version>".
STATE_SQL="
  SELECT (SELECT count(DISTINCT plan_id) FROM plan_versions),
         (SELECT count(*) FROM plan_versions),
         (SELECT coalesce(md5(string_agg(plan_id || ':' || v, ',' ORDER BY plan_id)), '-')
            FROM (SELECT plan_id, max(version_no) v FROM plan_versions GROUP BY plan_id) p),
         (SELECT count(DISTINCT code) FROM tax_group_versions),
         (SELECT count(*) FROM tax_group_versions),
         (SELECT coalesce(md5(string_agg(code || ':' || v, ',' ORDER BY code)), '-')
            FROM (SELECT code, max(version_no) v FROM tax_group_versions GROUP BY code) t)"
catalog_state() { psql_in catalog-db catalog "$STATE_SQL"; }
copy_state() { psql_in recommendation-db recommendation "$STATE_SQL" 2>/dev/null; }
describe() { # describe <state line>
  read -r plans versions plan_hash groups group_versions group_hash <<< "$1"
  echo "$plans plans, $versions plan versions (max versions ${plan_hash:0:8}), $groups tax groups, $group_versions tax group versions (max versions ${group_hash:0:8})"
}

# The exact containers are stopped and started with docker itself: `docker compose start` would also start the dependencies,
# which re-runs the one-shot kafka-init (about a minute) before the service starts.
SERVICE=$(docker compose ps -q recommendation)
WORKERS=$(docker compose ps -q recommendation-worker)
[[ -n "$SERVICE" ]] || { echo "FAIL: recommendation is not running (docker compose up -d --wait)"; exit 1; }

# Whatever happens, the service and the workers run again at the end (start is a no-op for running containers).
cleanup() {
  # shellcheck disable=SC2086
  docker start $SERVICE $WORKERS >/dev/null 2>&1
}
trap cleanup EXIT

# 1. Expected state. Events still in Catalog's outbox are not in Kafka yet: wait until they are sent.
for _ in $(seq 1 30); do
  unpublished=$(psql_in catalog-db catalog "SELECT count(*) FROM outbox_messages WHERE published_at IS NULL") || exit 1
  [[ "$unpublished" == 0 ]] && break
  sleep 1
done
if [[ "$unpublished" != 0 ]]; then echo "FAIL: catalog has $unpublished unpublished outbox rows after 30 s; is Kafka or the publisher stuck?"; exit 1; fi

expected=$(catalog_state) || exit 1
echo "Catalog (expected): $(describe "$expected")"
echo "Copy before:        $(describe "$(copy_state)")"

# 2. Stop the consumers of the copy.
workers=$(wc -w <<< "$WORKERS")
echo "Stopping recommendation and $workers worker(s)..."
# shellcheck disable=SC2086
docker stop $WORKERS $SERVICE >/dev/null || { echo "FAIL: could not stop the services"; exit 1; }

# 3. Empty the copy.
psql_in recommendation-db recommendation "TRUNCATE plan_versions, tax_group_versions" >/dev/null || { echo "FAIL: truncate failed"; exit 1; }
echo "Copy after truncate: $(describe "$(copy_state)")"

# 4. Back to the start of the topics. The group must have no members; a consumer that closed cleanly has left it, but give it time.
topic_args=()
for topic in "${TOPICS[@]}"; do topic_args+=(--topic "$topic"); done
reset_done=""
for _ in $(seq 1 30); do
  if out=$(kafka_groups --group "$GROUP" --reset-offsets --to-earliest "${topic_args[@]}" --execute 2>&1); then reset_done=1; break; fi
  sleep 2
done
if [[ -z "$reset_done" ]]; then echo "FAIL: could not reset the offsets of group '$GROUP':"; echo "$out"; exit 1; fi
echo "Offsets of group '$GROUP' reset to the earliest:"
echo "$out" | sed '/^$/d; s/^/  /'

# 5. Start the service and wait until the copy matches.
docker start "$SERVICE" >/dev/null || { echo "FAIL: could not start recommendation"; exit 1; }
start=$(date +%s.%N)
actual=""
while true; do
  actual=$(copy_state)
  [[ "$actual" == "$expected" ]] && break
  if (( $(date +%s) - ${start%.*} >= TIMEOUT )); then break; fi
  sleep 0.5
done
took=$(awk -v a="$start" -v b="$(date +%s.%N)" 'BEGIN { printf "%.1f", b - a }')

echo "Starting $workers worker(s) again..."
# shellcheck disable=SC2086
[[ -n "$WORKERS" ]] && docker start $WORKERS >/dev/null

echo
echo "Catalog (expected): $(describe "$expected")"
echo "Copy (rebuilt):     $(describe "$actual")"
if [[ "$actual" == "$expected" ]]; then
  echo "PASS: the copy was rebuilt from Kafka in ${took}s (from starting the container until the copy matched)"
  exit 0
fi
echo "FAIL: the copy does not match the Catalog after ${took}s (timeout ${TIMEOUT}s). Check recommendation.dlq and the logs of recommendation."
exit 1

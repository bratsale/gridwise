#!/usr/bin/env bash
# Batch throughput: a 100,000-row file with 1 and with 3 workers, two runs each. Needs the compose stack running.
#   bash scripts/perf/batch-throughput.sh
# Prints a markdown table (N, run, seconds, rows/s) for docs/perf/README.md, and a snapshot taken in the middle of each run
# (docker stats, consumer lag, unpublished outbox rows) that shows what limits the speed.
# The default of 2 workers is restored at the end, also when the script fails.
set -euo pipefail

cd "$(dirname "$0")/../.."
source scripts/perf/batch-lib.sh

ROWS=${ROWS:-100000}
RUNS=${RUNS:-2}
TIMEOUT=${TIMEOUT:-900}
WORKERS=(${WORKERS_LIST:-1 3})
FILE=$(mktemp --suffix=.csv)
RESULTS=$(mktemp)
trap 'rm -f "$FILE" "$RESULTS"; echo; echo "Restoring the default of 2 workers..."; set_workers 2' EXIT

bash scripts/perf/make-batch-csv.sh "$ROWS" "$FILE"
echo "File: $ROWS rows, $(du -h "$FILE" | cut -f1), $(grep -c nowhere "$FILE") unknown tax group rows"

snapshot() {
  echo "  --- snapshot ---"
  docker stats --no-stream --format '  {{.Name}}: cpu {{.CPUPerc}}, mem {{.MemUsage}}' \
    $(docker compose ps -q batch recommendation recommendation-worker batch-db recommendation-db kafka) | sort
  echo "  lag (current-offset, log-end-offset, lag) per partition:"
  for group in batch-workers batch; do
    docker compose exec -T kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group "$group" 2>/dev/null \
      | awk -v g="$group" 'NR > 2 && $1 != "" && $2 ~ /^batch\./ { lag += $6; end += $5; printf "  %s %s[%s] lag=%s\n", g, $2, $3, $6 } END { printf "  %s total lag=%d\n", g, lag }'
  done
  echo "  unpublished outbox rows: batch=$(psql_batch "SELECT count(*) FROM outbox_messages WHERE published_at IS NULL") recommendation=$(psql_recommendation "SELECT count(*) FROM outbox_messages WHERE published_at IS NULL")"
  echo "  ----------------"
}

for n in "${WORKERS[@]}"; do
  echo "=== $n worker(s) ==="
  set_workers "$n"
  sleep 20 # the consumer group rebalances when workers come or go
  for run in $(seq 1 "$RUNS"); do
    require_idle
    id=$(upload_job "$FILE")
    start=$(date +%s.%N)
    echo "run $run: job $id accepted"
    snapped=0
    while true; do
      status=$(job_field "$id" status)
      now=$(date +%s.%N)
      elapsed=$(awk -v a="$now" -v b="$start" 'BEGIN { print a - b }')
      if is_terminal "$status"; then break; fi
      if awk -v e="$elapsed" -v t="$TIMEOUT" 'BEGIN { exit !(e > t) }'; then echo "timeout after ${TIMEOUT}s" >&2; exit 1; fi
      if (( snapped == 0 )) && awk -v e="$elapsed" 'BEGIN { exit !(e > 12) }'; then snapshot; snapped=1; fi
      sleep 1
    done
    [[ "$status" == Completed ]] || { echo "job ended as $status" >&2; exit 1; }
    total=$(job_field "$id" totalRows)
    processed=$(job_field "$id" processedRows)
    failed=$(job_field "$id" failedRows)
    seconds=$(awk -v e="$elapsed" 'BEGIN { printf "%.1f", e }')
    rate=$(awk -v t="$total" -v e="$elapsed" 'BEGIN { printf "%.0f", t / e }')
    echo "run $run: Completed in ${seconds}s, $rate rows/s ($processed processed, $failed failed)"
    echo "| $n | $run | $seconds | $rate |" >> "$RESULTS"
  done
done

echo
echo "| Workers | Run | Seconds | Rows/s |"
echo "|---|---|---|---|"
cat "$RESULTS"

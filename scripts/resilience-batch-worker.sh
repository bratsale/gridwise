#!/usr/bin/env bash
# Resilience test: a worker is killed in the middle of a 100,000-row job. The job must still complete, with every row counted exactly once.
#   bash scripts/resilience-batch-worker.sh
# Exit code 0 = all checks passed, 1 = a check failed (or the job did not complete in time). The killed worker is brought back at the end.
set -uo pipefail

cd "$(dirname "$0")/.."
source scripts/perf/batch-lib.sh

ROWS=${ROWS:-100000}
TIMEOUT=${TIMEOUT:-900}
KILL_AT_PERCENT=${KILL_AT_PERCENT:-20}
FILE=$(mktemp --suffix=.csv)
FAILED=0

cleanup() {
  rm -f "$FILE"
  echo
  echo "Bringing the killed worker back..."
  docker compose up -d recommendation-worker >/dev/null 2>&1
}
trap cleanup EXIT

fail() { echo "FAIL: $*"; FAILED=1; }
check() { # check <description> <expected> <actual>
  if [[ "$2" == "$3" ]]; then echo "ok   $1 ($3)"; else fail "$1: expected $2, got $3"; fi
}

echo "Starting with 2 workers..."
set_workers 2
sleep 15 # let the consumer group settle
require_idle || exit 1

bash scripts/perf/make-batch-csv.sh "$ROWS" "$FILE"
id=$(upload_job "$FILE") || exit 1
start=$(date +%s)
echo "Job $id accepted ($ROWS rows)"

# Wait until about KILL_AT_PERCENT of the chunks are completed, then kill one worker.
killed=""
while true; do
  status=$(job_field "$id" status)
  total_chunks=$(job_field "$id" totalChunks)
  done_chunks=$(job_field "$id" completedChunks)
  if is_terminal "$status"; then echo "the job ended as $status before a worker could be killed"; exit 1; fi
  if (( total_chunks > 0 && done_chunks * 100 >= total_chunks * KILL_AT_PERCENT )); then break; fi
  if (( $(date +%s) - start > TIMEOUT )); then echo "timeout while waiting for $KILL_AT_PERCENT%"; exit 1; fi
  sleep 0.5
done
killed=$(docker compose ps -q recommendation-worker | head -1)
docker kill "$killed" >/dev/null
killed_at=$(( $(date +%s) - start ))
echo "Killed worker ${killed:0:12} after ${killed_at}s at $done_chunks/$total_chunks chunks"

# Wait for Completed.
last_progress=$done_chunks; last_change=$(date +%s); longest_stall=0
while true; do
  status=$(job_field "$id" status)
  done_chunks=$(job_field "$id" completedChunks)
  now=$(date +%s)
  if (( done_chunks != last_progress )); then
    stall=$(( now - last_change )); (( stall > longest_stall )) && longest_stall=$stall
    last_progress=$done_chunks; last_change=$now
  fi
  if is_terminal "$status"; then break; fi
  if (( now - start > TIMEOUT )); then echo "FAIL: the job did not complete within ${TIMEOUT}s (status $status, $done_chunks chunks)"; exit 1; fi
  sleep 1
done
finished_after=$(( $(date +%s) - start ))
echo "Job ended as $status after ${finished_after}s (the longest pause without progress after the kill: ${longest_stall}s)"
[[ "$status" == Completed ]] || { echo "FAIL: expected Completed"; exit 1; }

# Verify.
total_rows=$(job_field "$id" totalRows)
processed=$(job_field "$id" processedRows)
failed=$(job_field "$id" failedRows)
total_chunks=$(job_field "$id" totalChunks)
completed_chunks=$(job_field "$id" completedChunks)

echo
echo "Checks:"
check "processedRows + failedRows = totalRows" "$total_rows" "$(( processed + failed ))"
check "completedChunks = totalChunks" "$total_chunks" "$completed_chunks"
check "job_results rows = processedRows" "$processed" "$(psql_batch "SELECT count(*) FROM job_results WHERE job_id = '$id'")"
check "job_errors rows = failedRows" "$failed" "$(psql_batch "SELECT count(*) FROM job_errors WHERE job_id = '$id'")"
check "processed_chunks rows = totalChunks" "$total_chunks" "$(psql_recommendation "SELECT count(*) FROM processed_chunks WHERE job_id = '$id'")"
check "one result outbox row per chunk" "$total_chunks" "$(psql_recommendation "SELECT count(*) FROM outbox_messages WHERE aggregate_id = '$id' AND event_type = 'BatchChunkCompleted'")"

echo
echo "Report: $ROWS rows, $total_chunks chunks, $processed processed, $failed failed; worker killed at ${killed_at}s (${KILL_AT_PERCENT}% of the chunks); completed after ${finished_after}s."
if (( FAILED != 0 )); then echo "RESULT: FAILED"; exit 1; fi
echo "RESULT: PASSED"

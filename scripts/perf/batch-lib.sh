# Shared by the batch scripts (sourced, not run). Needs the compose stack and python3.
BASE=${BASE:-http://localhost:5100/api/batch}

# Uploads a CSV and prints the job id. Fails unless the answer is 202.
upload_job() {
  local file=$1 out code
  out=$(mktemp)
  code=$(curl -s -o "$out" -w '%{http_code}' -F "file=@$file" "$BASE/jobs")
  if [[ "$code" != 202 ]]; then echo "upload failed: HTTP $code $(cat "$out")" >&2; rm -f "$out"; return 1; fi
  python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' < "$out"
  rm -f "$out"
}

# Prints one field of the job (status, totalRows, totalChunks, completedChunks, processedRows, failedRows).
job_field() { curl -s "$BASE/jobs/$1" | python3 -c "import json,sys; print(json.load(sys.stdin)['$2'])"; }

is_terminal() { [[ "$1" == Completed || "$1" == Cancelled || "$1" == Failed ]]; }

# Fails when a job is still Pending or Running: it would take part of the capacity of a measurement.
require_idle() {
  local busy
  busy=$(curl -s "$BASE/jobs" | python3 -c 'import json,sys; print(sum(j["status"] in ("Pending","Running") for j in json.load(sys.stdin)))')
  if [[ "$busy" != 0 ]]; then echo "$busy job(s) are still Pending or Running; wait for them or cancel them first." >&2; return 1; fi
}

psql_batch() { docker compose exec -T batch-db psql -U batch -d batch -At -c "$1"; }
psql_recommendation() { docker compose exec -T recommendation-db psql -U recommendation -d recommendation -At -c "$1"; }

# Starts (or stops) workers until there are exactly $1, and waits until they run.
set_workers() { docker compose up -d --scale recommendation-worker="$1" --wait recommendation-worker >/dev/null 2>&1 || docker compose up -d --scale recommendation-worker="$1" >/dev/null; }

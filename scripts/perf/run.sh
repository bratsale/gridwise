#!/usr/bin/env bash
# Seeds 200 plans, then load-tests POST /recommendations with k6 in Docker.
set -euo pipefail

cd "$(dirname "$0")/../.."

source scripts/lib/auth.sh
bash scripts/perf/seed-200-plans.sh

# k6 calls the recommendation service directly on 5102, not through the gateway: the NFR is about the service, and the gateway's
# rate limit (20 calculations per 10 s per user) would refuse a load test by design. The service still checks the token: a customer's.
TOKEN=$(get_token customer)

# --network host: the Linux host network, so k6 reaches localhost:5102.
# k6 exits with 99 when a threshold is crossed: still print the hint, then return k6's exit code.
K6_EXIT=0
# TARGET_DURATION shortens the measured part of the target scenario (default 60s), for a quick check that the script works.
docker run --rm --network host -v "$PWD/scripts/perf:/scripts" \
  -e SCENARIO="${SCENARIO:-target}" -e TARGET_DURATION="${TARGET_DURATION:-60s}" -e TOKEN="$TOKEN" \
  grafana/k6 run /scripts/recommendations.js || K6_EXIT=$?

echo
echo "Copy the summary above (http_req_duration med, p(95), p(99), http_reqs rate, http_req_failed) into the table in docs/perf/README.md."
exit "$K6_EXIT"

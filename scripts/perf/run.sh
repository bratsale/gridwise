#!/usr/bin/env bash
# Seeds 200 plans, then load-tests POST /recommendations with k6 in Docker.
set -euo pipefail

cd "$(dirname "$0")/../.."

bash scripts/perf/seed-200-plans.sh

# --network host: the Linux host network, so k6 reaches localhost:5102.
# k6 exits with 99 when a threshold is crossed: still print the hint, then return k6's exit code.
K6_EXIT=0
docker run --rm --network host -v "$PWD/scripts/perf:/scripts" -e SCENARIO="${SCENARIO:-target}" grafana/k6 run /scripts/recommendations.js || K6_EXIT=$?

echo
echo "Copy the summary above (http_req_duration med, p(95), p(99), http_reqs rate, http_req_failed) into the table in docs/perf/README.md."
exit "$K6_EXIT"

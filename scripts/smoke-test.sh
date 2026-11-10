#!/usr/bin/env bash
# Smoke test of a running stack: the health endpoints of the three services and the gateway, and one recommendation
# (250 kWh, household) through the public entry: web (nginx) -> gateway -> recommendation.
# Usage: scripts/smoke-test.sh [env-file]   (the env file only supplies the ports; default ports are used without it)
set -euo pipefail

if [ $# -ge 1 ]; then
  set -a; . "$1"; set +a
fi
source "$(dirname "$0")/lib/auth.sh"
catalog=http://localhost:${CATALOG_PORT:-5101}
recommendation=http://localhost:${RECOMMENDATION_PORT:-5102}
batch=http://localhost:${BATCH_PORT:-5103}
gateway=http://localhost:${GATEWAY_PORT:-5104}
web=http://localhost:${WEB_PORT:-5100}

for url in "$catalog/health/ready" "$recommendation/health/ready" "$batch/health/ready" "$gateway/health/ready" "$web/"; do
  curl -fsS --max-time 10 -o /dev/null "$url"
  echo "ok   $url"
done

# The health endpoints need no token; the recommendation does (a customer's, from Keycloak in the same stack).
TOKEN=$(get_token customer)
echo "ok   token for customer from Keycloak"

# The plans reach the recommendation service through Kafka, so right after a start the first answers can be empty: retry.
for attempt in $(seq 1 30); do
  total=$(curl -fsS --max-time 10 -X POST "$web/api/recommendation/recommendations" -H 'Content-Type: application/json' -H "Authorization: Bearer $TOKEN" \
    -d '{"consumptionKwh":250,"taxGroup":"household"}' | jq -r '.recommended.total // empty' || true)
  if [ -n "$total" ]; then
    echo "ok   recommendation for 250 kWh household: best total $total"
    exit 0
  fi
  echo "     no recommended plan yet (attempt $attempt)"
  sleep 2
done
echo "FAILED: no recommendation for 250 kWh household" >&2
exit 1

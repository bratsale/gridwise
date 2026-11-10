#!/usr/bin/env bash
# Creates 200 "Perf plan NNN" plans through the Catalog API and waits until Recommendation has them.
set -euo pipefail

cd "$(dirname "$0")/../.."

source scripts/lib/auth.sh

# Directly to the catalog service: seeding is not part of the measurement, and the gateway's general rate limit would slow it down.
API=http://localhost:5101
WANT=200
TOKEN=$(get_token admin)
AUTH="Authorization: Bearer $TOKEN"

perf_plans_in_catalog() { { curl -s -H "$AUTH" "$API/plans" | grep -o '"name":"Perf plan' || true; } | wc -l; }
perf_plans_in_recommendation() {
  docker compose exec -T recommendation-db psql -U recommendation -d recommendation -At \
    -c "SELECT count(*) FROM plan_versions WHERE name LIKE 'Perf plan %'"
}
total_plan_versions() {
  docker compose exec -T recommendation-db psql -U recommendation -d recommendation -At -c "SELECT count(*) FROM plan_versions"
}

EXISTING=$(perf_plans_in_catalog)
if (( EXISTING >= WANT )); then
  echo "Skip: $EXISTING 'Perf plan' plans already exist."
else
  TODAY=$(date -u +%F)
  RUN=$(date -u +%s)
  echo "Creating $WANT plans (found $EXISTING)..."
  for i in $(seq 1 "$WANT"); do
    # Varied: 1-3 tiers, price level and discount depend on i.
    P1=$(printf '0.%03d' $((80 + i % 40)))
    P2=$(printf '0.%03d' $((60 + i % 30)))
    P3=$(printf '0.%03d' $((40 + i % 20)))
    DISCOUNT=$(printf '0.%02d' $((i % 16)))
    case $((i % 3)) in
      0) TIERS="[{\"threshold\":null,\"pricePerKwh\":$P1}]" ;;
      1) TIERS="[{\"threshold\":$((50 + i % 200)),\"pricePerKwh\":$P1},{\"threshold\":null,\"pricePerKwh\":$P2}]" ;;
      2) TIERS="[{\"threshold\":$((50 + i % 100)),\"pricePerKwh\":$P1},{\"threshold\":$((300 + i % 300)),\"pricePerKwh\":$P2},{\"threshold\":null,\"pricePerKwh\":$P3}]" ;;
    esac
    NAME=$(printf 'Perf plan %03d' "$i")
    STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/plans" \
      -H 'Content-Type: application/json' -H "$AUTH" \
      -H "Idempotency-Key: perf-$RUN-$i" \
      -d "{\"name\":\"$NAME\",\"discount\":$DISCOUNT,\"effectiveFrom\":\"$TODAY\",\"tiers\":$TIERS}")
    [[ "$STATUS" == 201 ]] || { echo "FAIL: $NAME returned HTTP $STATUS"; exit 1; }
  done
  echo "Created $WANT plans."
fi

echo "Waiting until recommendation-db has $WANT 'Perf plan' versions and at least $((WANT + 2)) plan_versions rows (max 120 s)..."
DEADLINE=$((SECONDS + 120))
until (( $(perf_plans_in_recommendation) >= WANT && $(total_plan_versions) >= WANT + 2 )); do
  if (( SECONDS >= DEADLINE )); then
    echo "FAIL: read model not complete within 120 s ($(perf_plans_in_recommendation) perf rows, $(total_plan_versions) rows)."
    exit 1
  fi
  sleep 2
done
echo "Read model ready: $(perf_plans_in_recommendation) perf rows, $(total_plan_versions) rows in total."

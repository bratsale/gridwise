#!/usr/bin/env bash
# Writes a CSV for the batch module: make-batch-csv.sh <rows> <file>
# Mixed household/business, consumption 0-1000 with up to 3 decimals, about 1% invalid rows
# (letters instead of a number, a negative number, an unknown tax group). Always the same file for the same row count.
set -euo pipefail

rows=${1:?usage: make-batch-csv.sh <rows> <file>}
file=${2:?usage: make-batch-csv.sh <rows> <file>}

awk -v n="$rows" 'BEGIN {
  srand(42)
  print "customer_id,consumption_kwh,tax_group"
  for (i = 1; i <= n; i++) {
    group = (rand() < 0.5) ? "household" : "business"
    kwh = sprintf("%.3f", rand() * 1000)
    sub(/0+$/, "", kwh); sub(/\.$/, "", kwh)          # 12.500 -> 12.5, 7.000 -> 7
    r = rand()
    if (r < 0.0034) kwh = "abc"
    else if (r < 0.0067) kwh = "-" kwh
    else if (r < 0.0100) group = "nowhere"
    printf "C-%06d,%s,%s\n", i, kwh, group
  }
}' > "$file"

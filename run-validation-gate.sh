#!/usr/bin/env bash
# EDD-015 / B2d.1 — Validation Gate runner (the merge gate for Stage 3).
#
# Spins up a throwaway Postgres, applies every migration, seeds the equivalence scenarios, then runs
# CapabilityEquivalenceGateTests — which executes BOTH the live CapabilityResolver and the parallel
# CanonicalCapabilityResolver against every active access_context and asserts identical capability sets.
# Exit 0 = canonical is byte-identical to legacy ⇒ Stage 3 may merge. Requires: postgresql@16, dotnet.
set -euo pipefail
export LC_ALL=C LANG=C
HERE="$(cd "$(dirname "$0")" && pwd)"
PGDATA="/tmp/edutech-gate-pg"; PORT="${GATE_PORT:-55480}"
DB="$HERE/EduTech 1.0/Database"
SEED="$HERE/EduTech 1.0Test/EduTech.Auth.Tests/Authentication/gate-seed.sql"

cleanup() { pg_ctl -D "$PGDATA" -w stop >/dev/null 2>&1 || true; rm -rf "$PGDATA"; }
trap cleanup EXIT

rm -rf "$PGDATA"
initdb --locale=C --encoding=UTF8 -U postgres -D "$PGDATA" >/dev/null
pg_ctl -D "$PGDATA" -l /tmp/edutech-gate-pg.log -o "-p $PORT -k /tmp -c listen_addresses=''" -w start >/dev/null
psql -h /tmp -p "$PORT" -U postgres -q -c "CREATE DATABASE schoolflow;" >/dev/null

echo "Applying migrations 0001 → latest…"
while IFS= read -r f; do
  psql -h /tmp -p "$PORT" -U postgres -d schoolflow -v ON_ERROR_STOP=1 -q -f "$f" >/dev/null
done < <(ls "$DB"/[0-9]*.sql | sort)

echo "Seeding equivalence scenarios…"
psql -h /tmp -p "$PORT" -U postgres -d schoolflow -v ON_ERROR_STOP=1 -q -f "$SEED" >/dev/null

echo "Running the Validation Gate…"
export GATE_DB="Host=/tmp;Port=$PORT;Database=schoolflow;Username=postgres"
cd "$HERE/EduTech 1.0"
dotnet test --filter "FullyQualifiedName~CapabilityEquivalenceGate"

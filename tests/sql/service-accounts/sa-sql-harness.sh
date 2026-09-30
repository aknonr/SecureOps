#!/usr/bin/env bash
# Isolated Service Accounts SQL harness for a disposable SQL Server container (Linux/cloud runners).
# Creates a NEW database, applies numbered migrations (now 001-025) in order,
# verifies replay refusal and prints the connection string for SECUREOPS_SA_SQL_TEST_CONNECTION.
# It never targets an existing database and never runs against corporate servers.
# Usage: SA_PASSWORD=... sa-sql-harness.sh <container> <new-database-name> [host-port]
set -euo pipefail
container="$1"; database="$2"; port="${3:-14333}"
[[ "$database" =~ ^SecureOps_Sa[A-Za-z0-9_]{1,40}$ ]] || { echo "Use a task-owned SecureOps_Sa* database name." >&2; exit 2; }
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
sqlcmd() { docker exec -w "$1" "$container" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -I -b "${@:2}"; }
exists=$(sqlcmd / -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$database'")
[[ "$exists" == "0" ]] || { echo "Database already exists; refusing to reuse it." >&2; exit 2; }
work="/tmp/sa-harness-$database"
docker exec -u 0 "$container" rm -rf "$work"
docker exec -u 0 "$container" mkdir -p "$work"
docker cp "$repo/sql" "$container:$work/"
# The reviewed entrypoints use Windows ':r ..\schema\...' paths; only this disposable copy is converted.
docker exec -u 0 "$container" bash -c "sed -i 's#\\\\#/#g' $work/sql/migrations/*.sql"
sqlcmd / -Q "CREATE DATABASE [$database]"
for file in $(ls "$repo/sql/migrations" | sort); do
  sqlcmd "$work/sql/migrations" -d "$database" -i "$file" > /dev/null
  echo "applied $file"
done
if [[ ! -f "$repo/sql/migrations/025-service-accounts.sql" ]]; then
  sqlcmd "$work/sql/pending/service-accounts" -d "$database" -i SA-001-service-accounts.sql > /dev/null
  echo "applied SA-001-service-accounts.sql (candidate)"
fi
if sqlcmd "$work/sql/pending/service-accounts" -d "$database" -i SA-001-service-accounts.sql > /dev/null 2>&1; then
  echo "Candidate replay was not refused." >&2; exit 1
fi
echo "replay refused as expected"
echo "SECUREOPS_SA_SQL_TEST_CONNECTION=Server=127.0.0.1,$port;Database=$database;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;Encrypt=False"

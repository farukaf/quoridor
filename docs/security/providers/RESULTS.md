# Selectable persistence validation — 2026-10-18

## Implementation scope

The leaderboard branch adds appsettings selection `Persistence:Provider=SQLite|PostgreSql` and provider-specific connection strings, with SQLite as default. Shared DbConnection/DbCommand SQL preserves parameterization, transactions, unique MatchId and the existing schema. SQLite relative paths retain the existing DataDirectory/quoridor.db file; a legacy-schema test proves data reuse. PostgreSQL uses pooled Npgsql 10.0.3. Timestamps remain normalized ISO8601 UTC text and counts Int64. Provider selection does not copy/migrate data. Data Protection keys remain in the persistent filesystem with either provider.

## Results

- Locked restore and Release build `-warnaserror -m:1`: passed, zero warnings/errors.
- .NET analyzer verification at error severity: passed.
- Shared SQLite/real-PostgreSQL unit/application suite: 23 passed, 0 failed, 0 skipped. It covers concurrency/replay, store reopen, persistence failure/retry, server actor/turn/wall checks, actual host startup/DI, provider/connection validation failures, no fallback, protected/tampered cookies and keys across host restart, UTC normalization, long counts and the existing SQLite schema.
- Selenium complete legal victory/history/unique count/forged localStorage/application-container restart: passed against PostgreSQL and separately against default SQLite, one test each. Exact TRX files are included.
- HTTP/static/room/SignalR smoke: passed for both running provider containers.
- Real PostgreSQL 18.6 database-container restart: completed result count preserved. Nonroot identity and counts are recorded in postgres-restart.json; installed patched package versions in postgres-packages.txt.
- Application image/NuGet/source audits: no known vulnerabilities, source secrets or misconfigurations in the included point-in-time reports.
- Sonar Cloud remains unexecuted pending account/project/token configuration. No quality-gate pass is claimed.

## PostgreSQL test image findings and treatment

The pinned official postgres:18-alpine baseline had 62 known findings: two Alpine-library findings (nghttp2-libs and zlib) and 60 in its Go-based /usr/local/bin/gosu. The original report is preserved as postgres-image-audit.json; the official image is not described as clean.

`tests/Postgres.Dockerfile` is isolated development/CI infrastructure. It derives from the immutable official digest, upgrades available Alpine patches, removes the unused gosu binary and runs the official entrypoint directly as the postgres user. No custom user-switch wrapper is introduced. Actual image-volume initialization and database restart passed as the nonroot postgres user. The derived-image report (postgres-hardened-image-audit.json) has zero known findings at scan time. This does not provision or audit a future managed/production PostgreSQL deployment and is not a guarantee of absence of vulnerabilities.

CI builds this test image before tests, starts a loopback-only PostgreSQL service, waits for TCP readiness, and exports the test connection at job scope so both ordinary tests and Sonar-wrapped tests have it. Database logs are preserved as artifacts. Missing local TEST_POSTGRES_CONNECTION_STRING fails the PostgreSQL tests instead of skipping them.

## Local clean-room reproduction

The orchestrator has a local mode600 env file `/tmp/quoridor-provider-validation.env`. Source it without printing its contents. It sets the isolated PostgreSQL connection and Chrome/application test environment. The database service is quoridor-postgres-hardened-tests on loopback15434; provider app containers are quoridor-provider-postgres18086 and quoridor-provider-sqlite18087, each with persistent identity keys/data volumes. The visual checkpoint container18085 is preserved independently.

```sh
source /tmp/quoridor-provider-validation.env
dotnet restore src/Quoridor.sln --locked-mode
dotnet build src/Quoridor.sln -c Release --no-restore -warnaserror -m:1
dotnet test src/Quoridor.sln -c Release -m:1 --logger trx
APP_BASE_URL=http://127.0.0.1:18087 APP_RESTART_CONTAINER=quoridor-provider-sqlite \
  dotnet test tests/Quoridor.BrowserTests -c Release -m:1 --logger trx
```

No sync/rebase/push or new PR was performed. Visual checkpoint124066e remains on its separate stacked branch pending orchestrator coordination and revalidation after updating the base.

# Leaderboard and player history

The home page lists the top 100 players by completed-game wins, then games, name and ID. Each player links to their latest 100 match results, including opponent and UTC completion time. Match names are snapshots; rendering is HTML encoded. Refresh the home/history page to see new results.

Players remain anonymous: the server issues a random ID in an ASP.NET authenticated cookie (`Quoridor.Player`). Cookies are protected by ASP.NET Data Protection, HttpOnly, SameSite=Lax, Secure in production, and valid for one year with sliding renewal. The circuit reads its authenticated claims through `AuthenticationStateProvider`; board event handlers never accept an ID supplied by the browser. Clearing cookies starts a new player; this is not a cross-device account or a defense against multiple anonymous identities.

Existing browser `USER_KEY` JSON can suggest only the display name, which requires confirmation. Its arbitrary ID/timezone are never imported. The archived Blazored.LocalStorage dependency has been removed. Names are normalized and limited to 1–40 characters without control characters. Database statements use parameters.

## Persistence and deployment

`DataDirectory` defaults to `data` for local development; the image sets it to `/data`. Mount **one persistent volume** at `/data` to preserve `quoridor.db`, WAL files and `keys/`. The image creates this directory for its nonroot UID 1654. Back up the directory with the application stopped or use SQLite's backup API; copying the database without its active WAL can lose committed results. Keep the keys private: they protect anonymous identity cookies.

```sh
docker volume create quoridor-data
docker run --name quoridor -p 8080:8080 -v quoridor-data:/data quoridor:leaderboard
```

Serve production through HTTPS so Secure cookies are sent. The app always sets Secure in production even when the backend connection from a TLS proxy is HTTP. Configure the deployment's trusted proxy/forwarded headers and TLS redirect behavior explicitly; do not accept arbitrary forwarded headers. Use Development only for local HTTP on non-loopback addresses.

The app initializes the selected database schema on startup and fails startup for an invalid/unwritable data directory. SQLite is accessed via Microsoft.Data.Sqlite and PostgreSQL via Npgsql, without EF or hand-written EF migrations. WAL mode and a busy timeout coordinate database writes. One application instance owns the in-memory live rooms; run a single replica. Active unfinished games reset when the process restarts, while completed results and identities survive restart/recreation with the volume.

## Authoritative results

A semaphore serializes each room's entry and board actions. The server verifies the authenticated actor, turn, legal destinations, wall count, overlap/crossing, board bounds and remaining paths for both players. Jumps over an adjacent opponent follow the straight path when available, with diagonals only when the jump is blocked. The room clears stale move/wall selections after turns.

Each round receives a unique MatchId. A winning move commits an immutable result in a SQLite transaction before resetting the board or publishing victory. MatchId is the primary key: duplicate completions cannot increase counts. Leaderboard counts are derived from result rows, with no separate counters to drift. If persistence fails, the current round/position remain available for retry with the same MatchId; no success is announced. Restarting after an uncertain commit still leaves at most one result row.

## Validation

Unit/integration tests cover legal victory, duplicate clicks and completion inserts, simultaneous writes, wrong turns/spectators/forged highlights, invalid destinations, overlapping/crossing and path-blocking walls, pawn jumps, malicious names, failed persistence/retry, reopened SQLite, startup/DI, protected/tampered cookies, HTML encoding, all affected page routes and SignalR negotiation. Time-dependent game tests use a fixed TimeProvider.

Selenium C# drives two independent headless Chrome profiles through a complete legal game, checks one win and player history, forges legacy localStorage without changing the authenticated player, and optionally restarts the container to verify the same cookie/result survive. It uses no production seed endpoint. Failure screenshots, browser HTML/logs and application/Grid logs are artifacts.

```sh
# Start a production app/container on this base URL first.
APP_BASE_URL=http://127.0.0.1:8080 APP_RESTART_CONTAINER=quoridor \
  dotnet test src/Quoridor.sln -c Release -m:1 --logger trx
# Optional: CHROME_BINARY=/path/to/chrome, or SELENIUM_URL=http://127.0.0.1:4444
```

Primary implementation references: [Blazor server authentication](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0), [SQLite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [SQLite async/WAL guidance](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

## Select SQLite or PostgreSQL

Set `Persistence:Provider` to `SQLite` (the default) or `PostgreSql`. Only the selected provider's connection string is used. An unsupported provider, blank required connection, missing PostgreSQL Host/Database, SQLite memory source, or unavailable database prevents application startup; there is no silent fallback. Example JSON files without credentials are in `docs/configuration/`.

SQLite uses `ConnectionStrings:SQLite`. A relative Data Source is resolved beneath `DataDirectory`, so the default `Data Source=quoridor.db` continues using the existing `/data/quoridor.db` container database and its existing schema. Absolute file paths are also supported. WAL/busy-timeout behavior remains in place.

PostgreSQL uses `ConnectionStrings:PostgreSql`, normally supplied as the secret environment variable `ConnectionStrings__PostgreSql`. Set `Persistence__Provider=PostgreSql` and provide Host, Database and the deployment's authentication/TLS configuration. Provision a dedicated database and role permitted to create its tables/indexes on initial startup and read/write those tables. Use verified TLS for a remote server. The example intentionally contains no password; inject credentials through the deployment's secret mechanism.

Both providers execute the same parameterized statements and transaction/idempotency rules through `DbConnection`/`DbCommand`. PostgreSQL connections use a pooled NpgsqlDataSource. IDs and completion timestamps retain the existing text schema; completion times are normalized to fixed-format ISO8601 UTC before storing, preserving chronological ordering and timestamp precision without provider-specific timezone conversions. Counts are read as Int64. Tie ordering uses lowercased name and ID; provider collation can differ for non-ASCII names.

**Selecting a provider does not migrate data.** Back up and explicitly transfer/verify data before switching an existing installation. Selecting an empty PostgreSQL database produces an empty leaderboard; returning to SQLite reopens its previous file. Do not assume the stores are synchronized.

Data Protection keys remain filesystem-backed regardless of provider. Even with PostgreSQL, persist `/data/keys` (or the entire `/data` volume) to preserve anonymous player cookies across container recreation. PostgreSQL holds game results, not the identity signing keys. Active rooms still require a single application replica because live board state is in memory.

### Real database integration tests

The same game, replay, concurrent-insert, failure/retry, reopen, UTC/count and application-startup/cookie tests run for SQLite and a real PostgreSQL server. PostgreSQL tests require `TEST_POSTGRES_CONNECTION_STRING`; they create unique temporary schemas and drop them afterward. Missing configuration fails rather than silently skipping. Use a dedicated isolated test database/role with schema creation rights. CI provisions a pinned PostgreSQL service and runs both providers.

```sh
# Example isolated local test service; these credentials are disposable test values.
docker build -f tests/Postgres.Dockerfile -t quoridor-postgres:tests .
docker run -d --name quoridor-postgres-tests --network host \
  -e PGPORT=15433 -e POSTGRES_USER=quoridor -e POSTGRES_DB=quoridor \
  -e POSTGRES_PASSWORD=quoridor-local-test \
  quoridor-postgres:tests -c listen_addresses=127.0.0.1
TEST_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=15433;Database=quoridor;Username=quoridor;Password=quoridor-local-test' \
  dotnet test tests/Quoridor.Tests -c Release -m:1 --logger trx
```

The test database Dockerfile pins the official base by digest, upgrades Alpine security packages, removes its unused outdated Go-based gosu binary, and starts directly as the supported postgres user. This is an isolated development/CI service, not a deployed database provisioned by the application. PostgreSQL service tests are independent of which provider the separately running Selenium application uses; the application can also be launched with PostgreSQL environment configuration to run the complete browser/restart scenario against that provider.

Provider references: [Npgsql basic usage and pooling](https://www.npgsql.org/doc/basic-usage.html), [connection parameters](https://www.npgsql.org/doc/connection-string-parameters), [timestamp handling](https://www.npgsql.org/doc/types/datetime.html).

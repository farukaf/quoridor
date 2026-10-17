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

The app initializes the SQLite schema on startup and fails startup for an invalid/unwritable data directory. SQLite is accessed via Microsoft.Data.Sqlite, without EF or hand-written EF migrations. WAL mode and a busy timeout coordinate database writes. One application instance owns the in-memory live rooms; run a single replica. Active unfinished games reset when the process restarts, while completed results and identities survive restart/recreation with the volume.

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

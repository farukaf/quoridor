# .NET 10 migration security evidence

Audit performed 2026-10-17 against the working tree on `chore/dotnet-10-security`.

## Scope and changes

Application target: .NET 10. SDK 10.0.402 (global.json), ASP.NET runtime 10.0.13 in the container. Current Microsoft MCR `10.0-alpine` images are pinned by immutable digest in Dockerfile. Both build/runtime stages use those pins. The runtime runs as UID 1654 on HTTP 8080 with a healthcheck; HTTPS belongs at the reverse proxy.

NuGet dependencies, including transitive dependencies, are locked and audited (`NuGetAuditMode=all`). Blazored.LocalStorage remains at its latest release 4.5.0; upstream is archived, so replacing it should accompany the planned protected identity change. No npm manifest, bundled third-party JS/CSS, CDN scripts or vendored frontend libraries exist: app.js contains only timezone lookup, and Blazor JS is shipped by ASP.NET.

CI replaces checkout v2/setup-dotnet v3 with current v7/v6 immutable commit pins, limits permissions to contents:read and disables persisted Git credentials. Docker credentials use stdin and environment variables. Tag-triggered publishing preserves existing behavior; no tag or image was published during this work. PR/main builds now restore locked dependencies, build with warnings as errors, fail on NuGet vulnerability findings and exercise the actual container with Selenium C#.

## Reproduction

```sh
dotnet restore src/Quoridor.sln --locked-mode
dotnet build src/Quoridor.sln -c Release --no-restore -warnaserror
dotnet format analyzers src/Quoridor.sln --verify-no-changes --severity error --no-restore
dotnet list src/Quoridor.sln package --vulnerable --include-transitive --format json > nuget-audit.json
python3 scripts/check-audit.py nuget-audit.json
docker build -t quoridor:dotnet10 .
docker run --name quoridor -p 8080:8080 quoridor:dotnet10
python3 scripts/smoke.py
# Local Chrome via Selenium Manager, or SELENIUM_URL to a standalone Grid:
APP_BASE_URL=http://127.0.0.1:8080 dotnet test src/Quoridor.sln -c Release --logger trx
trivy image --scanners vuln --format json --output image-audit.json quoridor:dotnet10
trivy fs --scanners vuln,secret,misconfig --skip-dirs docs/security --format json --output source-audit.json .
```

The host used for this audit requires `docker build --network host` and application `--network host` with `ASPNETCORE_HTTP_PORTS=18083`, since its bridge publishing did not respond. CI uses the ordinary GitHub-hosted runner network.

## GitHub and sources

`gh api repos/farukaf/quoridor/dependabot/alerts --paginate` returned `[]`. Code scanning returned 404 (no analysis found); secret scanning returned 404 (disabled). These unavailable services are not claimed as passing checks. No existing test project or Sonar configuration existed. .NET analyzers run successfully; whole-repository `dotnet format` additionally reports baseline whitespace/import formatting, intentionally not reformatted as part of a security migration.

Primary sources checked: [Microsoft .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [MCR .NET images](https://github.com/dotnet/dotnet-docker), [Blazored releases](https://github.com/Blazored/LocalStorage/releases), [Selenium .NET](https://www.selenium.dev/documentation/webdriver/getting_started/), [SonarScanner for .NET](https://docs.sonarsource.com/sonarqube-cloud/advanced-setup/ci-based-analysis/sonarscanner-for-dotnet/), and GitHub release/commit APIs for the pinned actions.

## Sonar Cloud activation

Create an OSS Sonar Cloud project linked to this public repository, disable Automatic Analysis, set repository variables `SONAR_ENABLED=true`, `SONAR_ORGANIZATION`, `SONAR_PROJECT_KEY`, and secret `SONAR_TOKEN`. The workflow installs SonarScanner 12.0.0, analyzes main and internal PRs, and waits for the quality gate. Enabled configuration missing any required value fails. Fork PRs never receive the secret. Until activated, the workflow explicitly reports that Sonar did not run; no Sonar gate has passed locally. OpenCover output from browser tests measures the test process only: the separately running app is not instrumented, so this does not establish application coverage.

## Next phase: leaderboard

Current identities are arbitrary GUID/name/timezone JSON in browser localStorage; rooms and victories exist only in a singleton dictionary and are lost on restart. Use an anonymous server-issued identity protected by ASP.NET Data Protection (HttpOnly cookie, SameSite, Secure behind HTTPS), with server-owned GUID and editable display name. Preserve stable identity through a persistent keys volume; no login required. Ignore client-supplied identity for authorization. Persist players and immutable match results in SQLite on `/data`, unique `MatchId`; transactionally insert once and derive leaderboard counts from completed results. Serialize state changes per room, verify actor/turn/legal move on the server, and assign each round a fresh match ID before play. Persist victory before resetting the board; retries reuse the same ID. Test duplicate completion, concurrent operations, forged identity, and container restart with data+keys volumes. The current global mutable selection/turn methods need those changes before wins become authoritative. MudBlazor can be evaluated in the subsequent visual layer without changing game state ownership.

## Recorded results

Build with warnings as errors succeeded (zero warnings/errors); locked restore and compiler analyzer verification succeeded. NuGet JSON reports no known vulnerable packages. Trivy 0.76.0 final runtime image audit reports no known OS/NuGet vulnerabilities; source audit reports no secrets or Dockerfile findings after adding healthcheck. Machine-readable reports are beside this file. These are point-in-time findings, not a guarantee of absence of vulnerabilities.

HTTP smoke passed home, room, static app JS/CSS, framework JS and SignalR negotiation advertising WebSockets. Selenium xUnit passed 1/1 using Chrome for Testing 156.0.8078.4 with Selenium Manager and two independent browser profiles against the nonroot production container. Verified player names, pawn move from r0c4 to r1c4, opponent turn, a wall placement decrementing wall count from 10 to 9, and turn return. A previous Grid attempt failed before testing the application due to this shared host's eventbus/video port collision; local Chrome then passed. No new API endpoints or game rules were introduced.

Local browser reproduction on this host:

```sh
CHROME_BINARY=/home/agent/.cache/ms-playwright/chromium-1248/chrome-linux64/chrome APP_BASE_URL=http://127.0.0.1:18083 dotnet test src/Quoridor.sln -c Release --logger trx
```

Sonar analysis remains unexecuted pending account/project/token configuration. Independent clean-context validation is pending with the orchestrator.

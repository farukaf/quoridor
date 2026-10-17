# Leaderboard validation — 2026-10-17

- Locked restore: passed using SDK 10.0.402.
- Build Release with `-warnaserror -m:1`: passed, zero warnings/errors.
- `dotnet format analyzers ... --verify-no-changes --severity error --no-restore`: passed.
- Unit/application tests: 7 passed, 0 failed; exact TRX in application-results.trx.
- Selenium C# Chrome 156 with Selenium Manager: 1 passed, 0 failed; full legal victory, replay, history, forged localStorage, cookie preservation and Docker restart. Exact TRX in browser-results.trx.
- HTTP/static/room/SignalR smoke: passed against production container on 18084.
- Container nonroot: uid/gid 1654; `/data` and `/data/keys` writable and owned by app. Container recreated on the same named `quoridor-leaderboard-test` volume before final browser run; earlier completed matches remained. Browser also restarted the recreated container while retaining its identity cookie and match history.
- NuGet direct/transitive and Trivy 0.76.0 image/source audits: no known vulnerabilities or source secret/misconfiguration findings. JSON reports included here.
- Sonar Cloud remains unexecuted pending project/token configuration, as recorded for phase one. No passing Sonar gate is claimed.
- One parallel host build initially failed with MSB4166; the serialized final build succeeded. No application test was waived.

Runtime used for validation: `quoridor:leaderboard`, container `quoridor-leaderboard`, named volume `quoridor-leaderboard-test`, host-network HTTP port 18084. The overridden port means this local container's fixed default-8080 healthcheck is not used as evidence; HTTP smoke and browser exercise 18084 directly.

Independent clean-context validation is pending. No PR, image publication, merge or deployment was performed for this phase.

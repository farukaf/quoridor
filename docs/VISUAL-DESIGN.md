# Responsive Quoridor interface

The interface uses the existing Razor architecture and local CSS. It adds no UI framework, remote fonts, image dependency or database changes. A warm paper/wood palette, green and coral pawns, shared design tokens, and a CSS board illustration connect the home, live room and player history.

The home provides room creation, rules and a ranked leaderboard. Rooms include an invitation link with clipboard feedback, a live turn announcement, player cards, wall counts, keyboard instructions and session results. Player history includes profile statistics explicitly scoped to the displayed latest 100 results. Long names wrap rather than pushing the page horizontally.

## Board input and accessibility

Native buttons expose square coordinates, pawn names, legal moves and wall-point state. Arrow keys move focus around the active board controls; Enter/Space select. Roving tabindex keeps the board from adding 181 tab stops. The toolbar switches between pawn and wall modes and focuses the appropriate control. In wall mode the pawn buttons are disabled, allowing 24×24 CSS pixel wall targets without overlapping active cell targets on a small screen. In move mode the wall controls are disabled; the tested 390px viewport retains cell targets above 24px in both width and height. These modes change input presentation only: server authorization and legal-move validation remain unchanged.

The layout includes a skip link, semantic landmarks, labelled inputs, table headers, visible keyboard focus, visible microtext of at least 10 CSS pixels, a polite live turn status and reduced-motion support. Player identity and turn are described in text as well as color. The name form is an in-page section, so no modal focus trap is needed.

Principal text contrast pairs were calculated using WCAG relative luminance: muted text on paper 4.74:1, muted on white 5.09:1, green on its light badge 5.53:1, coral on white 4.56:1, and preview caption 5.25:1. These exceed the normal-text threshold described in [W3C contrast guidance](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html). Touch mode follows [W3C target-size guidance](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html). This is a targeted implementation/verification record, not a claim of comprehensive WCAG conformance.

## Verification

Selenium C# runs the complete two-player game at 390, 768 and 1440px, including the real home-page Create a room CTA, keyboard pawn/wall actions, native details, invitation feedback, accessible button names, visible focus, no horizontal page overflow, long player names, unique victory/history, forged localStorage rejection and container restart. It captures home, room, board, leaderboard and history at every width in `artifacts/browser/`. The matrix uses actual browser interaction and no test-only production endpoint.

```sh
CHROME_BINARY=/home/agent/.cache/ms-playwright/chromium-1248/chrome-linux64/chrome \
APP_BASE_URL=http://127.0.0.1:18085 APP_RESTART_CONTAINER=quoridor-visual \
BROWSER_ARTIFACTS="$PWD/artifacts/browser" \
dotnet test src/Quoridor.sln -c Release -m:1 --logger trx
```

Local smoke container: `quoridor-visual`, image `quoridor:visual`, named volume `quoridor-visual-test`, host HTTP 18085. Its healthcheck is overridden at `docker run` to use 18085; the product Dockerfile continues to use the correct default 8080. Point-in-time NuGet/Trivy and test evidence is in `docs/security/visual/`. Sonar remains dependent on the configured account/project/token, as documented in the prior phases.

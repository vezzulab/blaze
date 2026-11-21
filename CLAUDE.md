# Blaze: project guide

Blaze is a **Firefox-based browser** for slow connections and low memory (Linux x86_64, alpha). It runs the official, unmodified Firefox engine and adds a tuned profile, a Lite extension, a resumable download engine, a signed updater, and an interface (themes Ember dark, Ash light). It is not a source fork. Public docs: `README.md`, `README.es.md`, `PRIVACY.md`, `SECURITY.md`, `docs/BENCHMARKS.md`, `docs/MAINTAINING.md`, site in `docs/` (GitHub Pages).

The owner writes in Spanish. Reply in Spanish; keep public docs in English with Spanish counterparts (`README.es.md`, `docs/es/`).

## Layout

| Path | What |
|---|---|
| `src/Blaze.Core` | Download engine, updater (`Updates/`), Firefox debugger client (`Browser/DevToolsClient.cs`), engine rebrand (`Browser/Rebrand.cs`). AOT-compatible. |
| `src/Blaze.Agent` | CLI (`blaze-agent get / update / addon-install / rebrand / version`), native AOT, no web framework. |
| `scripts/blaze` | The launcher (engine install, profile, update prompt, start). Used by the AppImage and from a checkout. |
| `profile/` | `user.js` (prefs), `chrome/` (Ember/Ash CSS), `error-pages.css` and `error-illustrations/` (applied to the engine by the rebrand). |
| `extension/` | Lite companion extension, Manifest v2. |
| `engine.env` | Pinned Firefox version + SHA-256 per locale. |
| `tests/Blaze.Tests` | 22 tests with local fake servers (downloads, fake GitHub, signatures, rebrand). |
| `tools/ui.py` | Headless Firefox over Marionette: `contract` and `shot`. |
| `packaging/build-appimage.sh` | Builds `dist/Blaze-<version>-x86_64.AppImage`. |

## Commands

```bash
dotnet build
(cd tests/Blaze.Tests && dotnet run -c Release)        # must stay green
dotnet publish src/Blaze.Agent -c Release -r linux-x64 -o dist/agent
python3 tools/ui.py contract                           # needs the engine: run scripts/blaze once
python3 tools/ui.py shot --scheme dark --out /tmp/x.png
packaging/build-appimage.sh
```

## Rules

1. **Security and privacy come first.** Do not loosen signature verification, add listening ports, add telemetry, or add background connections. Any new network contact must be measured and listed in `PRIVACY.md`.
2. **Stay light.** No new always-on process or heavy dependency without a measurement.
3. **Only state what was measured.** Do not invent numbers or claim other browsers are slower. Label estimates as estimates and single runs as single runs.
4. **Never guess UI changes.** Look at the real result with `tools/ui.py`. Do not capture the owner's desktop.
5. **Repository hygiene.** Commits carry no attribution trailers, no co-author lines, and no mention of AI tooling. Commit as the configured noreply identity. Never commit keys, tokens or `*.pem`. Never read or print the private signing key (`~/.config/blaze-signing/`).
6. Anything outward-facing (pushing, releases, enabling services) happens only when the owner asked for it.
7. Pinned engine: never ship an engine version whose `tools/ui.py contract` fails.

## Facts learned the hard way (Firefox 159)

- The urlbar background is `div.urlbar-background` (a class), not `#urlbar-background`. The lock icon is `#trust-icon-container`.
- `display: -moz-box` is invalid and silently drops the rule.
- Extension toolbar button: `toolbaritem#blaze-companion_blaze_local-browser-action`.
- A profile first started with vertical tabs gets no Reload/Downloads: the launcher starts once with the stock layout first.
- `user.js` values are persisted into `prefs.js`; removing a line does not undo it.
- Release Firefox rejects unsigned add-ons from the profile; Blaze loads the extension as a temporary add-on over a Unix socket (`-start-debugger-server <path>`). Socket paths are limited to ~108 chars.
- Error pages are drawn by a shadow-DOM component (`net-error-card`): user CSS cannot reach them.
- Window buttons: with the tabs in the side strip, Firefox draws them INSIDE `#nav-bar` (`.titlebar-buttonbox-container`, one per toolbar; headless shows them `display:none`). Blaze gives that group `order:1000`, a border and margins. The contract checks computed styles only; to see it use `BLAZE_UI_EXTRA_CSS=<file forcing the buttons visible> tools/ui.py shot`. The real look on the owner's desktop was never seen.
- Headless windows report zero-size layout boxes; test styles and prefs, not geometry.
- `spectacle` is broken on the owner's machine. Marionette screenshots capture the interface only (page area blank).
- `pkill -f <pattern>` kills your own shell if the same command line contains the pattern; use `[x]yz` in its own command, or kill by PID. Never kill every `firefox` process: the owner has windows open.
- Killing only the parent Firefox process leaves a `firefox-bin` child alive that holds the profile lock and pipes open.

## State and open items

- **Memory research (headless, 6 real tabs, PSS):** the extension process is about half of everything (uBlock Origin ~250-350 MB, Bitwarden ~90 MB, uBO Lite ~35 MB). Site-isolation changes and trimming uBO's lists gave nothing measurable. Discarding idle tabs saves real memory: the Lite extension now sleeps tabs idle for 10 min (alarm every minute, skips active, pinned, audible, non-web), with a switch in the panel. **Decided by the owner: keep full uBlock Origin** (uBO Lite would save ~300 MB but is less powerful); do not propose swapping it again unless asked. Bitwarden stays preinstalled too (~90 MB). Measure with `ChromeUtils.requestProcInfo()` for pids + `/proc/<pid>/smaps_rollup`; `residentUniqueSize` is not available on Linux.

- **Defaults mechanism:** lines in `profile/user.js` tagged `// @default:<name>` apply once per profile (marker `.blaze-default-<name>`), then belong to the user. Used for the homepage (DuckDuckGo) and the toolbar layout (the owner's own, with the four extensions grouped in the dropdown). The search engine default is the policy `SearchEngines.Default` (Firefox applies it once per value). Window buttons CSS, themes and prefs without the tag are enforced on every start.
- **The owner's own Blaze is the reference for defaults** (toolbar, DuckDuckGo, Bitwarden, the community cookie extension `idcac-pub@guus.ninja`). When asked to change defaults, look at their real window (the desktop portal screenshot works; Spectacle does not) and at `prefs.js` settings only. Never copy cookies, history, logins, sessions or the account device name.
- **Debugger socket is closed after the extension loads** (the agent calls `closeAllSocketListeners` in the parent process); before this Firefox showed its 'browser is under remote control' robot all session. `tools/` has no test for it yet; verify with Marionette reading `DevToolsSocketStatus.hasSocketOpened`.
- **About dialog and branding images are Blaze's**; `Rebrand.LogicVersion` must be bumped on every change to the patch logic, or already-patched engines keep the old result.

- **Rebrand works and is wired into the launcher.** `blaze-agent rebrand` rewrites the engine's two `omni.ja` archives into clean zips (brand strings, error CSS, and replacement artwork from `profile/error-illustrations/`). Editing the archives in place with .NET produced an engine that did not start in 120 s; writing a fresh zip starts in 0.4 s like the original. Do not go back to in-place editing.
- **Resolved by the engine upgrade:** the right-hand tab strip was not drawn on the Mozilla 159.0 tarball (but was on Fedora's 159) and IS drawn on 160.0.1 (screenshot-confirmed; `tools/ui.py contract` passes 15/15 in light and dark). Keep the pixel check: it caught a real regression.
- **Security freshness is now the top maintenance duty.** 0.1.0 shipped Firefox 159.0 while Mozilla had already fixed 102 CVEs (incl. sandbox escapes) in 159.0.1, 160 and 160.0.1. Check `https://product-details.mozilla.org/1.0/firefox_versions.json` before every release and after every Firefox release; bump `engine.env` and ship. Idea not built yet: make the launcher warn when the pinned engine is behind.
- Opening extra tabs through Marionette (`NewWindow`) also hides the strip in the automated setup; the contract takes its screenshot before that.
- Privacy Badger opens a welcome tab on a fresh profile (the contract closes it before looking).
- Planned: connect the download engine to Firefox's download button; per-site Lite exceptions; own new-tab and downloads panel; AMO-signed companion extension; default-browser integration.
- CI workflows are written but have never run. Releases (all signed with the key in ~/.config/blaze-signing): v0.1.0 (pre-release, **ships vulnerable Firefox 159.0 - do not recommend**), v0.1.1 (Firefox 160.0.1, security fix) v0.1.2 (the owner's defaults, extensions, DuckDuckGo, no remote-control robot, Blaze About dialog) and v0.1.3 (Latest: window buttons in fixed order, clear of the edge). The owner wants releases shown as normal releases (Latest), not pre-releases. 0.1.2 and 0.1.3 were verified against the published assets (signature, checksum, the real updater client against the real release JSON; the anonymous GitHub API rate limit blocks a direct run). The website's download button points to the releases page.

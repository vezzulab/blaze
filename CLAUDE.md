# Blaze: project guide

Blaze is a **Firefox-based browser** for slow connections and low memory (Linux x86_64, alpha). It runs the official, unmodified Firefox engine and adds a tuned profile, a Lite extension, a resumable download engine, a signed updater, and an interface (themes Ember dark, Ash light). It is not a source fork. Public docs: `README.md`, `README.es.md`, `PRIVACY.md`, `SECURITY.md`, `docs/BENCHMARKS.md`, `docs/MAINTAINING.md`, site in `docs/` (GitHub Pages).

The owner writes in Spanish. Reply in Spanish; keep public docs in English with Spanish counterparts (`README.es.md`, `docs/es/`).

## Layout

| Path | What |
|---|---|
| `src/Blaze.Core` | Download engine, updater (`Updates/`), Firefox debugger client (`Browser/DevToolsClient.cs`), experimental engine rebrand (`Browser/Rebrand.cs`). AOT-compatible. |
| `src/Blaze.Agent` | CLI (`blaze-agent get / update / addon-install / rebrand / version`), native AOT, no web framework. |
| `scripts/blaze` | The launcher (engine install, profile, update prompt, start). Used by the AppImage and from a checkout. |
| `profile/` | `user.js` (prefs), `chrome/` (Ember/Ash CSS), `error-pages.css` (experimental). |
| `extension/` | Lite companion extension, Manifest v2. |
| `engine.env` | Pinned Firefox version + SHA-256 per locale. |
| `tests/Blaze.Tests` | 21 tests with local fake servers (downloads, fake GitHub, signatures). |
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
- Headless windows report zero-size layout boxes; test styles and prefs, not geometry.
- `spectacle` is broken on the owner's machine. Marionette screenshots capture the interface only (page area blank).
- `pkill -f <pattern>` kills your own shell if the same command line contains the pattern.

## State and open items

- **Rebrand (experimental, not wired into the launcher):** `blaze-agent rebrand` rewrites the engine's `brand.ftl/properties` and appends `error-pages.css` so menus and error pages say Blaze. The repacked `omni.ja` is a standard zip, not Mozilla's optimised layout. A first start test did not bring Marionette up in 40 s, which suggests slower startup. **Measure startup time (original vs rebranded) before enabling it.** Do not claim Blaze-branded error pages until verified.
- **Fresh-profile sidebar:** a profile built by `tools/ui.py shot` showed no right-hand sidebar in one run, while the older flow did. The contract only checks prefs. Investigate, and make the contract check that the sidebar is really rendered.
- Planned: connect the download engine to Firefox's download button; per-site Lite exceptions; own new-tab and downloads panel; AMO-signed companion extension; default-browser integration.
- CI workflows are written but have never run.
- No release has been published yet. The website's download button points to the releases page.

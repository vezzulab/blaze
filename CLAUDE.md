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
- Headless windows report zero-size layout boxes; test styles and prefs, not geometry.
- `spectacle` is broken on the owner's machine. Marionette screenshots capture the interface only (page area blank).
- `pkill -f <pattern>` kills your own shell if the same command line contains the pattern; use `[x]yz` in its own command, or kill by PID. Never kill every `firefox` process: the owner has windows open.
- Killing only the parent Firefox process leaves a `firefox-bin` child alive that holds the profile lock and pipes open.

## State and open items

- **Rebrand works and is wired into the launcher.** `blaze-agent rebrand` rewrites the engine's two `omni.ja` archives into clean zips (brand strings, error CSS, and replacement artwork from `profile/error-illustrations/`). Editing the archives in place with .NET produced an engine that did not start in 120 s; writing a fresh zip starts in 0.4 s like the original. Do not go back to in-place editing.
- **Open, unresolved: the right-hand vertical tab strip is not drawn on the pinned Mozilla Firefox 159.0 in headless first-start tests**, but it is with the Fedora system Firefox 159 and the same profile. Confirmed NOT caused by `policies.json` or by the rebrand (tested with neither). Unknown: whether a headed window shows it, and why the builds differ. Ideas: compare default prefs of both builds (`about:config` diff), check `browser/defaults/preferences` in the tarball vs Fedora, try a headed run with the owner's consent. `python3 tools/ui.py contract` has a pixel check for it and will fail until this is understood. Do not make the check pass by weakening it.
- Opening extra tabs through Marionette (`NewWindow`) also hides the strip in the automated setup; the contract takes its screenshot before that.
- Privacy Badger opens a welcome tab on a fresh profile (the contract closes it before looking).
- Planned: connect the download engine to Firefox's download button; per-site Lite exceptions; own new-tab and downloads panel; AMO-signed companion extension; default-browser integration.
- CI workflows are written but have never run. Release v0.1.0 (pre-release, signed with the key in ~/.config/blaze-signing) is published; its first start from scratch was tested headless (192 s). The website's download button points to the releases page.

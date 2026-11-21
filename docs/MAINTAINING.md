# Maintaining Blaze

Rules that keep the promises in the README true. Read before cutting a release or touching the engine.

## Principles

1. **Security and privacy first.** When a feature and a privacy or security property conflict, the property wins unless the user explicitly opts in (as with DRM and Safe Browsing).
2. **Lightness.** No new always-on process, port or dependency without a measurement showing it is worth it. The agent stays a small native binary with no web framework.
3. **Say only what was measured.** Numbers in the README, website and these docs come from a run you can repeat. Mark estimates as estimates.

## Bumping the Firefox engine

Firefox security releases arrive roughly every four weeks. Because Blaze pins Firefox, **a Blaze release is how users get those fixes.**

1. Read the new version's SHA-256 list: `https://archive.mozilla.org/pub/firefox/releases/<version>/SHA256SUMS`.
2. Update `FIREFOX_VERSION` and the four `SHA256_*` lines in [`engine.env`](../engine.env).
3. Install it (`scripts/blaze`), then run **`python3 tools/ui.py contract`**. It starts the engine headless with the Blaze profile and checks the elements and styles Blaze relies on, in light and dark.
4. If a check fails, fix `profile/chrome/*.css` or `profile/user.js` (inspect the real DOM with the tool; do not guess). Firefox changes internals between versions: 159 moved the urlbar background from an id to a class and shipped VPN and AI buttons that Blaze hides.
5. Run the tests, build the AppImage, sign, release.

Never ship an engine version whose contract fails.

## Releasing

1. Set `<Version>` in `src/Blaze.Core/Blaze.Core.csproj` and `src/Blaze.Agent/Blaze.Agent.csproj` (they must match the tag).
2. `dotnet build && (cd tests/Blaze.Tests && dotnet run -c Release)` must pass.
3. `packaging/build-appimage.sh`.
4. Sign: `BLAZE_SIGNING_KEY=/path/to/release-key.pem tools/sign-release.sh dist/Blaze-<version>-x86_64.AppImage`. This writes `SHA256SUMS` and `SHA256SUMS.sig`.
5. Publish a GitHub release with those three files. The AppImage must be named `Blaze-<version>-x86_64.AppImage`, or the updater will not see it. Write release notes for users: the updater shows them verbatim (first 14 lines) before anyone decides. Say if the release changes the Firefox version (it triggers an 86 MB download on first run).
6. Mark releases below 1.0 as pre-releases.

The tag workflow (`.github/workflows/release.yml`) does steps 2 to 5 from a `v*` tag, with the private key in the `BLAZE_SIGNING_KEY` secret. It has not run yet.

## The signing key

- The private key is never committed (`*.pem` is ignored). Keep it offline, or as a GitHub Actions secret.
- The public key is the constant in `src/Blaze.Core/Updates/ReleaseVerifier.cs`.
- To rotate: generate a new pair, ship a release **signed with the old key** whose binary embeds the **new** public key, then sign later releases with the new key.
- A leaked key means attackers can ship updates every Blaze accepts. Rotate immediately and say so publicly.

## Things that will bite you

- Firefox persists whatever `user.js` sets into `prefs.js`. Deleting a line does not undo it; the launcher scrubs known stale keys, and only while Firefox is not running.
- A profile first started with vertical tabs on gets a toolbar without Reload and Downloads. The launcher starts once with the stock layout, then enables the sidebar prefs.
- The sidebar layout (right side, collapsed) is a **default**: it is applied on the first run only so users can change it.
- `display: -moz-box` is invalid now and drops the whole rule. Error pages are drawn by a shadow-DOM component that user CSS cannot reach.
- Release Firefox rejects unsigned add-ons in the profile folder, even with `xpinstall.signatures.required` off.
- Never edit Mozilla's `omni.ja` in place with .NET's `ZipArchive`: the engine then does not start. `Rebrand` writes a new standard zip instead (0.4 s start, same as the original).
- Check the engine you are bumping draws the right-hand tab strip: the Mozilla tarball and Fedora's Firefox 159 differed here (see the open-issues list in the repository notes). The contract's pixel check covers it.
- Never `pkill -f <pattern>` where the same command line also contains the pattern: it kills your own shell. Use the `[x]yz` bracket trick in its own command.
- Unix socket paths are limited to about 108 characters.

## Tools

- `tools/ui.py contract|shot`: drives a real headless Firefox over Marionette. The screenshot shows the browser interface only; page content renders blank in this mode.
- `tools/bench/countproxy.py`: counts bytes per host, can throttle.
- `tools/sign-release.sh`: signs a release.

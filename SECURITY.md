# Security

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** on this repository (Security tab, "Report a vulnerability"). Do not open a public issue for a security problem. You can expect an acknowledgement; this is a small project, so please be patient.

Supported version: the latest release. Blaze is in alpha.

## What Blaze protects, and how

| Risk | Defence |
|---|---|
| A forged or tampered update | Every release carries `SHA256SUMS` and `SHA256SUMS.sig` (ECDSA P-256, SHA-256). The matching public key is compiled into Blaze. An update whose signature does not verify, whose checksum differs, or that has no signature at all is refused and deleted. Tested against forged, re-signed and unsigned releases. |
| A compromised download server or mirror | Release assets must be served over HTTPS. The engine is checked against a checksum pinned in this repository, not one fetched from the same server. |
| Local access to the browser's control channel | No TCP port is opened. Blaze loads its extension over a Unix socket inside a `0700` folder, and deletes the socket right after. Other users and web pages cannot reach it. |
| Leaking credentials from downloads | State files are `0600`; cookies and referrer are dropped when a download completes. |
| Firefox breaking Blaze, or Blaze breaking Firefox | Pinned, checksum-verified engine; Firefox's own updater is off; a UI contract test runs before each engine bump. |
| Plain-http browsing | HTTPS-only mode is on by default. |
| Rate-limit or network failures when updating | Treated as "ask later", never as a reason to skip verification. |

## What it does not protect, and the limits of trust

- **The signing key is the root of trust.** Whoever holds the private key can ship updates that every Blaze accepts. It is never stored in the repository; the maintainer keeps it offline or as a CI secret ([docs/MAINTAINING.md](docs/MAINTAINING.md)). If it leaks, the fix is a new key shipped in a release signed with the old one.
- **Pinning Firefox delays its security fixes** until a Blaze release ships them. We treat each Firefox security release as a reason to release Blaze; until you update, you run the engine you have. Keep the daily update check on.
- **The companion extension is not Mozilla-signed.** It is loaded as a temporary add-on at each start. It asks for host access to web pages (to block images, fonts and media in Lite mode) and declares that it collects no data.
- **A local attacker running as your user** can read your profile and files like any process of yours. Blaze does not defend against that.
- **DRM is closed-source** code from Google, run in Firefox's sandbox.
- Blaze does not patch Firefox. Vulnerabilities in Firefox itself belong upstream at Mozilla; tell us too so we can ship the fix.

## Verifying a release yourself

```bash
openssl ec -pubin -inform DER -in <(base64 -d <<<"$(grep -o 'PublicKeyBase64 = "[^"]*' src/Blaze.Core/Updates/ReleaseVerifier.cs | cut -d'"' -f2)") -out blaze.pub
openssl dgst -sha256 -verify blaze.pub -signature SHA256SUMS.sig SHA256SUMS
sha256sum -c SHA256SUMS
```

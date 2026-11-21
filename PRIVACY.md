# Privacy

Blaze is built to say as little as possible about you. This page lists what it does contact, based on a measurement, not on a wish.

**What Blaze never does:** no telemetry, no crash reporting, no studies, no accounts or sync, no search suggestions as you type, no web push, no region lookup, no "what's new" pages, no analytics on the website. We saw no telemetry server in any measurement.

## How we measured

We ran Blaze's pinned Firefox 159 (headless, Linux) through a counting proxy ([`tools/bench/countproxy.py`](tools/bench/countproxy.py)) that records every host and byte, including HTTPS tunnels. Two situations: a **first start** with a brand-new profile (initialisation plus 70 s), and a **normal start** (60 s) afterwards. One run each. Connections that happen on a longer schedule (weekly, say) fall outside these windows and would not appear.

## What Blaze itself contacts

| Host | When | What for |
|---|---|---|
| `archive.mozilla.org` | First start, and when a Blaze release pins a new Firefox | Downloads the Firefox engine (87 MB). Verified against a checksum pinned in this repository. |
| `addons.mozilla.org` | First start | Downloads uBlock Origin and Privacy Badger (Mozilla-signed). |
| `api.github.com`, `github.com` | Once a day, and on `blaze --update` | Looks for a new Blaze release. Sends your IP address and the User-Agent `Blaze-Updater`; no identifier, no version of your system. Turn it off with `BLAZE_NO_UPDATE_CHECK=1`. |

## What Firefox contacts underneath (first start, 43 MB in total)

| Host | Size | Purpose (our reading) |
|---|---:|---|
| `dl.google.com`, `update.googleapis.com` | 17.4 MB | Widevine DRM module and its update check. **Closed-source, from Google.** Needed for Spotify and Netflix. Off with `BLAZE_DRM=0`. |
| `safebrowsing.googleapis.com` | 10.4 MB | Safe Browsing lists (phishing and malware). Off with `BLAZE_SAFEBROWSING=0`. |
| `firefox-settings-attachments.cdn.mozilla.net`, `firefox.settings.services.mozilla.com`, `content-signature-2.cdn.mozilla.net` | 8.4 MB | Mozilla's Remote Settings: blocklists and certificate-revocation data. |
| `aus5.mozilla.org`, `versioncheck-bg.addons.mozilla.org`, `services.addons.mozilla.org` | small | Plugin metadata and add-on version checks. |
| `ublockorigin.pages.dev`, `ublockorigin.github.io`, `cdn.jsdelivr.net`, `curbengh.github.io`, `malware-filter.gitlab.io`, `pgl.yoyo.org`, `publicsuffix.org` | 5.5 MB | uBlock Origin downloading its filter lists. |
| `www.eff.org` | 21 KB | Most likely Privacy Badger's list update. We did not confirm it. |

A **normal start afterwards** was quiet: 87 KB across three connections in one measurement, and 6.7 MB in another when a Safe Browsing refresh happened to fall inside the window. The rest of the time it is whatever the sites you visit do.

Hosts we removed after the first measurement showed them: `push.services.mozilla.com` (web push), `location.services.mozilla.com` (region lookup) and `ciscobinary.openh264.org` (an H.264 module only video calls use).

## Trade-offs you should know about

- **Safe Browsing** protects you from phishing, and it talks to Google. Per Mozilla's documentation Firefox downloads lists of hash prefixes and asks Google for full hashes only when a visited URL matches a prefix, so Google sees a prefix, not the address. If you prefer no contact at all, turn it off and accept no phishing warnings. It is on by default because it protects people who would not know to turn it on.
- **DRM** is a closed-source module from Google. It runs in Firefox's plugin sandbox. It is on by default because Spotify and Netflix need it; turn it off if you do not.
- **Extensions** make their own connections. uBlock Origin and Privacy Badger update their lists as shown above.
- **Your IP address** is visible to every server above, as it is to any site you visit. Blaze does not hide it; use a VPN or Tor for that.

## Data on your disk

Everything lives in `~/.local/share/blaze` (engine, profile, download state). Download state is readable only by you (`0600`) and drops the cookies and referrer of a download as soon as it finishes. Nothing is sent anywhere from there.

Questions or corrections: open an issue.

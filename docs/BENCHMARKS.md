# Benchmarks

What we measured, how, and where the numbers stop being trustworthy. We only compared Blaze with **Firefox as shipped**. We did not measure Chrome, Chromium, Brave, Edge, Safari or any lightweight browser, so Blaze makes no claim about them.

## Setup

- Machine: Fedora 44, 15 GB RAM. Engine: official Firefox 159.0 (the version pinned by Blaze 0.1.0; 0.1.1 pins 160.0.1 and the figures were not re-measured).
- "Firefox as shipped": a brand-new, empty profile. "Blaze": the same engine with the Blaze profile ([`profile/user.js`](../profile/user.js)). "Lite": Blaze with images, fonts and media blocked.
- Firefox ran **headless**, so the window, compositor and GPU process are not counted. Real desktop use is somewhat heavier for both.
- **One run per cell.** No averages, no confidence intervals.

## Memory

Sum of the PSS of every Firefox process (PSS splits shared pages fairly, unlike RSS), 40 s after load.

| Scenario | Firefox | Blaze | Difference | Processes |
|---|---:|---:|---:|---|
| One blank tab | 815 MB | 522 MB | -36% | 14 → 7 |
| One tab, Wikipedia | 878 MB | 637 MB | -27% | 13 → 7 |
| Four pages (example.org, Wikipedia, mozilla.org, Hacker News) | 1021 MB | 689 MB | -33% | 17 → 10 |

Notes. Of Blaze's 522 MB with a blank tab, about 500 MB is anonymous memory and about 165 MB is the engine mapped from disk (the two figures overlap in reporting). Content processes were small (54 and 30 MB); the parent process was about 403 MB. We tried the obvious knobs, including a single content process, no site isolation, no media or utility process, and no background services, and the total did not move beyond noise (667, 671 and 707 MB across three variants). We did not keep those settings.

## Data per page

Bytes through a counting proxy ([`tools/bench/countproxy.py`](../tools/bench/countproxy.py)): upstream plus downstream, including TLS overhead, **cold cache**, front pages only.

| Page | Firefox | Blaze | Blaze + Lite |
|---|---:|---:|---:|
| en.wikipedia.org/wiki/Web_browser | 1192 KB | 1104 KB (-7%) | 493 KB (-59%) |
| edition.cnn.com | 6920 KB | 4774 KB (-31%) | 3909 KB (-44%) |
| bbc.com/news | 8529 KB | 3885 KB (-54%) | 3363 KB (-61%) |

- Wikipedia with Lite came out at 739 KB in a second run, so **treat the single-digit and low-two-digit differences as noise**; the news-page differences were the consistent ones.
- With uBlock Origin and Privacy Badger installed the totals did not drop further in this test (CNN 4836 KB, BBC 3926 KB). We do not know why: Firefox's own strict tracking protection may already block the same requests, or the extensions may not have loaded their filters in the short headless run. We did **not** conclude that they do nothing.
- Page load **times** varied wildly (the same BBC page took 36.8 s in one run and 9.7 s in another), so we publish bytes and calculate time from them. At 256 kbps (32,000 bytes/s): 6920 KB = 3 min 41 s, 4774 KB = 2 min 33 s, 3909 KB = 2 min 5 s.
- One throttled run (about 256 kbps, Wikipedia only): 965 KB in 28.8 s as shipped, 739 KB in 22.3 s with Lite.

## Where the memory goes with real tabs (Blaze 0.1.3, Firefox 160.0.1)

Six real pages open (Wikipedia, Hacker News, BBC News, YouTube, GitHub, The Guardian), headless, 12 s after loading, memory as PSS per process type. **Single runs; the extension process in particular swings by about 100 MB between runs, so read these as orders of magnitude.**

| Setup | Total | Extension process |
|---|---:|---:|
| Blaze as shipped (uBlock Origin, Privacy Badger, cookie notices, Bitwarden) | 1298 MB (other runs: 1374 MB) | 672 MB (other runs: 494, 502, 522) |
| The same, no extensions at all | 744 MB | 37 MB |
| Only uBlock Origin | 1027 to 1168 MB | 278 to 398 MB |
| Only Bitwarden | 868 MB | 125 MB |
| Only uBlock Origin Lite (MV3) | 774 to 853 MB | 35 to 36 MB |
| uBlock Origin Lite + Privacy Badger + cookie notices + Bitwarden | 1083 to 1140 MB | 172 to 186 MB |

What we learned, and what we ruled out:

- **Extensions are the largest single cost**, about half the total. uBlock Origin alone is roughly 250 to 350 MB of it; uBlock Origin Lite is about 35 MB. Lite is a different, less powerful extension (it uses the browser's declarative rules), so swapping is a trade-off, not a free win.
- **Weakening site isolation saves nothing** (1257 and 1338 MB against 1298 MB, within the noise). We did not do it: it would cost security for no measurable gain.
- **Trimming uBlock Origin's filter lists did not help** in our runs (278 to 405 MB with trimmed lists, against 278 to 398 MB by default). We do not claim it does.
- **Discarding the five idle tabs** brought the total from 1298 MB to 1121 MB, and the web content processes from 58 MB to 35 MB. Pages heavier than these save more. This is what the "sleeping tabs" feature does automatically.

## First start

A new profile on Firefox 160.0.1 fetched about 49 MB in the background in 70 s (Mozilla's data and blocklists about 26 MB, Widevine 17 MB, Safe Browsing 6.5 MB), after the 87 MB engine download and about 38 MB of extensions (Bitwarden alone is 28 MB). On the earlier Firefox 159.0 the background figure was about 43 MB. One run each. See [PRIVACY.md](../PRIVACY.md). A start after that is near-silent.

## Downloader

21 automated tests (`tests/Blaze.Tests`) run against a local server that can drop connections, go offline, replace the file or refuse ranges. Highlights: completes intact when every connection is cut every 300 KB; resumes after a 3 s outage while re-downloading less than 20% extra; picks up from saved state after the app is closed; restarts cleanly when the remote file changes. Separately, a native build was killed with `kill -9` at about 28 of 40 MB and finished with a matching SHA-256 on restart.

The agent used about 10 MB of RAM (RSS) while downloading.

## Reproduce

```bash
python3 tools/bench/countproxy.py 18950 0 /tmp/stats.json &       # port, bytes/s limit (0 = none), stats file
# point a profile at 127.0.0.1:18950 (HTTP and HTTPS proxy), load a page, then read /tmp/stats.json and /tmp/stats.json.hosts
```

Set the second argument to `32000` to throttle to about 256 kbps. Use a fresh profile per run for a cold cache.

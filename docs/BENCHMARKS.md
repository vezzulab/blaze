# Benchmarks

What we measured, how, and where the numbers stop being trustworthy. We only compared Blaze with **Firefox as shipped**. We did not measure Chrome, Chromium, Brave, Edge, Safari or any lightweight browser, so Blaze makes no claim about them.

## Setup

- Machine: Fedora 44, 15 GB RAM. Engine: official Firefox 159.0.
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

## First start

A new profile fetches about 43 MB in the background after the 86 MB engine download: Widevine 17.4 MB, Safe Browsing 10.4 MB, Mozilla Remote Settings 8.4 MB, uBlock Origin and Privacy Badger lists about 5.5 MB. See [PRIVACY.md](../PRIVACY.md). A start after that is near-silent.

## Downloader

21 automated tests (`tests/Blaze.Tests`) run against a local server that can drop connections, go offline, replace the file or refuse ranges. Highlights: completes intact when every connection is cut every 300 KB; resumes after a 3 s outage while re-downloading less than 20% extra; picks up from saved state after the app is closed; restarts cleanly when the remote file changes. Separately, a native build was killed with `kill -9` at about 28 of 40 MB and finished with a matching SHA-256 on restart.

The agent used about 10 MB of RAM (RSS) while downloading.

## Reproduce

```bash
python3 tools/bench/countproxy.py 18950 0 /tmp/stats.json &       # port, bytes/s limit (0 = none), stats file
# point a profile at 127.0.0.1:18950 (HTTP and HTTPS proxy), load a page, then read /tmp/stats.json and /tmp/stats.json.hosts
```

Set the second argument to `32000` to throttle to about 256 kbps. Use a fresh profile per run for a cold cache.

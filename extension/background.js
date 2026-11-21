// Blaze Companion - Lite mode and sleeping tabs.
// Lite = do not download images, web fonts or media (<video>/<audio> files). Measured saving: ~44-61% on heavy pages.
// Event page (persistent:false): costs no memory while idle.

const BLOCKED_TYPES = ["image", "imageset", "font", "media"];

let lite = null;
const ready = browser.storage.local.get({ lite: false }).then((r) => { lite = !!r.lite; paint(); });

function paint() {
  browser.browserAction.setIcon({ path: lite ? "icons/lite-on.svg" : "icons/lite-off.svg" });
  browser.browserAction.setBadgeText({ text: lite ? "ON" : "" });
  browser.browserAction.setBadgeBackgroundColor({ color: "#ff6a1a" });
  browser.browserAction.setBadgeTextColor?.({ color: "#14141a" });
}

browser.webRequest.onBeforeRequest.addListener(
  async (details) => {
    if (details.tabId < 0) return {};      // browser-internal requests (favicons, etc.) are never blocked
    if (lite === null) await ready;
    return lite ? { cancel: true } : {};
  },
  { urls: ["http://*/*", "https://*/*"], types: BLOCKED_TYPES },
  ["blocking"]
);

browser.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.lite) { lite = !!changes.lite.newValue; paint(); }
});

paint();


// ---------------------------------------------------------------------------------------------------------------------
// Sleeping tabs: a tab you have not touched for a while is discarded (Firefox keeps its place in history and reloads it
// when you click it). Closing the page frees the memory of its content process, which is the biggest per-tab cost.
// Never touched: the tab you are on, pinned tabs, tabs playing sound, tabs that are not web pages.
// Defaults: on, after 10 minutes. The panel has the on/off switch.
const SWEEP_ALARM = "blaze-sleep-sweep";
const SLEEP_DEFAULTS = { sleepIdleTabs: true, sleepAfterMinutes: 10 };

async function sweepIdleTabs() {
  const { sleepIdleTabs, sleepAfterMinutes } = await browser.storage.local.get(SLEEP_DEFAULTS);
  if (!sleepIdleTabs) return;
  const cutoff = Date.now() - sleepAfterMinutes * 60000;
  const candidates = await browser.tabs.query({ active: false, pinned: false, audible: false, discarded: false });
  const ids = candidates
    .filter((t) => /^https?:/.test(t.url || "") && t.autoDiscardable !== false && (t.lastAccessed || Date.now()) < cutoff)
    .map((t) => t.id);
  if (ids.length) await browser.tabs.discard(ids);
}

browser.alarms.create(SWEEP_ALARM, { periodInMinutes: 1 });
browser.alarms.onAlarm.addListener((alarm) => { if (alarm.name === SWEEP_ALARM) sweepIdleTabs(); });

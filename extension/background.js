// Blaze Companion - Lite mode.
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

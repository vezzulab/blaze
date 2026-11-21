// Blaze profile - low memory + low bandwidth. Applied on every launch by Blaze.Agent.
// Anything here overrides prefs.js, so users cannot accidentally drift from the tuned state.

// ---------- Memory: processes ----------
user_pref("dom.ipc.processCount", 2);                    // default 8 content processes
user_pref("dom.ipc.processCount.webIsolated", 2);        // cap Fission isolated processes per site
user_pref("dom.ipc.processPrelaunch.enabled", false);    // don't keep a spare process warm
user_pref("dom.ipc.keepProcessesAlive.web", 0);
user_pref("dom.ipc.forkserver.enable", true);            // cheaper child spawn on Linux
user_pref("extensions.webextensions.remote", true);

// ---------- Memory: tabs ----------
user_pref("browser.tabs.unloadOnLowMemory", true);
user_pref("browser.tabs.min_inactive_duration_before_unload", 300000); // 5 min idle tabs can be unloaded
user_pref("browser.low_commit_space_threshold_mb", 1500);
user_pref("browser.sessionhistory.max_total_viewers", 1); // bfcache pages kept in RAM (default up to 8)
user_pref("browser.sessionstore.interval", 60000);        // fewer session writes
user_pref("browser.sessionstore.max_tabs_undo", 5);
user_pref("browser.sessionstore.max_windows_undo", 1);
user_pref("browser.newtab.preload", false);
user_pref("browser.startup.preXulSkeletonUI", false);

// ---------- Memory: caches ----------
user_pref("browser.cache.memory.enable", true);
user_pref("browser.cache.memory.capacity", 32768);        // 32 MB RAM cache; disk holds the rest
user_pref("browser.cache.memory.max_entry_size", 2048);
user_pref("image.cache.size", 2097152);                   // 2 MB decoded image cache budget hint
user_pref("image.mem.surfacecache.max_size_kb", 65536);
user_pref("layout.css.devPixelsPerPx", "-1.0");
user_pref("javascript.options.mem.high_water_mark", 128);
user_pref("javascript.options.mem.gc_incremental_slice_ms", 10);

// ---------- Bandwidth: cache on DISK, big and persistent (re-visits cost 0 bytes) ----------
user_pref("browser.cache.disk.enable", true);
user_pref("browser.cache.disk.smart_size.enabled", false);
user_pref("browser.cache.disk.capacity", 2097152);        // 2 GB
user_pref("browser.cache.disk.max_entry_size", 262144);   // 256 MB
user_pref("browser.cache.disk.metadata_memory_limit", 1024);
user_pref("browser.cache.frecency_experiment", 0);

// ---------- Bandwidth/speed: network ----------
user_pref("network.http.http3.enable", true);             // QUIC: best on lossy/slow links
user_pref("network.http.http3.enable_0rtt", false);         // 0-RTT data can be replayed; the speed gain is small
user_pref("network.http.max-persistent-connections-per-server", 8);
user_pref("network.http.max-connections", 900);
user_pref("network.http.pipelining", true);
user_pref("network.http.rcwn.enabled", true);             // race cache vs network
user_pref("network.dnsCacheEntries", 1000);
user_pref("network.dnsCacheExpiration", 3600);
user_pref("network.dnsCacheExpirationGracePeriod", 240);
user_pref("network.dns.disablePrefetch", true);           // no speculative DNS for links
user_pref("network.prefetch-next", false);
user_pref("network.predictor.enabled", false);
user_pref("network.http.speculative-parallel-limit", 0);
user_pref("browser.urlbar.speculativeConnect.enabled", false);
user_pref("network.http.accept-encoding", "gzip, deflate, br, zstd");
user_pref("network.http.accept-encoding.secure", "gzip, deflate, br, zstd");

// ---------- Bandwidth: media ----------
user_pref("media.autoplay.default", 5);                   // block autoplay (audible AND silent video)
user_pref("media.autoplay.blocking_policy", 2);
user_pref("media.preload.default", 1);                    // metadata only until user presses play
user_pref("media.preload.auto", 2);
user_pref("media.hardware-video-decoding.enabled", true); // GPU decode = less CPU + RAM
user_pref("media.ffmpeg.vaapi.enabled", true);
user_pref("media.mediasource.enabled", true);             // required by YouTube
user_pref("media.eme.enabled", true);                     // DRM (Widevine) for Netflix etc. - optional download
user_pref("media.gmp-widevinecdm.enabled", true);
user_pref("media.gmp-widevinecdm.visible", true);
user_pref("media.gmp-gmpopenh264.enabled", false);        // only for WebRTC H.264 calls; avoids a Cisco download
user_pref("image.lazy-loading.enabled", true);
user_pref("dom.image-lazy-loading.enabled", true);
user_pref("browser.display.use_document_fonts", 1);       // 0 = skip webfonts (Lite mode flips this)

// ---------- Less background chatter (bandwidth + RAM) ----------
user_pref("toolkit.telemetry.enabled", false);
user_pref("toolkit.telemetry.unified", false);
user_pref("toolkit.telemetry.archive.enabled", false);
user_pref("datareporting.healthreport.uploadEnabled", false);
user_pref("datareporting.policy.dataSubmissionEnabled", false);
user_pref("app.shield.optoutstudies.enabled", false);
user_pref("app.normandy.enabled", false);
user_pref("browser.discovery.enabled", false);
user_pref("browser.newtabpage.enabled", false);
user_pref("browser.newtabpage.activity-stream.feeds.telemetry", false);
user_pref("browser.newtabpage.activity-stream.telemetry", false);
user_pref("browser.newtabpage.activity-stream.feeds.section.topstories", false);
user_pref("browser.newtabpage.activity-stream.feeds.topsites", false);
user_pref("browser.newtabpage.activity-stream.showSponsored", false);
user_pref("browser.newtabpage.activity-stream.showSponsoredTopSites", false);
user_pref("browser.startup.homepage", "about:blank");
user_pref("browser.startup.page", 3);                     // restore previous session
user_pref("extensions.pocket.enabled", false);
user_pref("browser.urlbar.suggest.quicksuggest.sponsored", false);
user_pref("browser.urlbar.suggest.quicksuggest.nonsponsored", false);
user_pref("browser.search.suggest.enabled", false);       // no request per keystroke
user_pref("browser.urlbar.suggest.searches", false);
user_pref("extensions.getAddons.showPane", false);
user_pref("extensions.htmlaboutaddons.recommendations.enabled", false);
user_pref("browser.aboutwelcome.enabled", false);
user_pref("browser.shell.checkDefaultBrowser", false);
user_pref("browser.tabs.crashReporting.sendReport", false);
user_pref("browser.crashReports.unsubmittedCheck.enabled", false);
user_pref("browser.ping-centre.telemetry", false);
user_pref("network.captive-portal-service.enabled", false);
user_pref("network.connectivity-service.enabled", false);
user_pref("browser.region.update.enabled", false);
user_pref("extensions.update.interval", 604800);          // check extension updates weekly (uBO filters update themselves)

// ---------- Extensions (uBlock Origin / Privacy Badger live in <profile>/extensions) ----------
user_pref("extensions.autoDisableScopes", 0);             // enable profile-dropped extensions without prompt
user_pref("extensions.enabledScopes", 15);
user_pref("extensions.postDownloadThirdPartyPrompt", false);
user_pref("xpinstall.signatures.required", true);         // keep signature enforcement (AMO-signed only)

// ---------- Privacy defaults that also save bytes (blocked trackers never load) ----------
user_pref("privacy.trackingprotection.enabled", true);
user_pref("privacy.trackingprotection.socialtracking.enabled", true);
user_pref("privacy.trackingprotection.cryptomining.enabled", true);
user_pref("privacy.trackingprotection.fingerprinting.enabled", true);
user_pref("network.cookie.cookieBehavior", 5);            // Total Cookie Protection
user_pref("privacy.globalprivacycontrol.enabled", true);

// ---------- Modern UI ----------
user_pref("toolkit.legacyUserProfileCustomizations.stylesheets", true); // load chrome/userChrome.css
user_pref("sidebar.revamp", true);                        // new sidebar
user_pref("sidebar.verticalTabs", true);                  // vertical tabs: more page height, less chrome
user_pref("sidebar.position_start", false);               // sidebar (vertical tabs) on the RIGHT
user_pref("sidebar.visibility", "expand-on-hover");       // collapsed to an icon strip; expands over the page on hover (page keeps its width)
user_pref("sidebar.verticalTabs.dragToPinPromo.dismissed", true);
user_pref("browser.tabs.hoverPreview.enabled", false);    // thumbnails cost RAM
user_pref("browser.tabs.fadeOutUnloadedTabs", true);
user_pref("browser.toolbars.bookmarks.visibility", "never");
user_pref("browser.tabs.tabmanager.enabled", false);      // no "list all tabs" chevron
user_pref("browser.urlbar.trimHttps", true);               // show clean URLs
user_pref("browser.urlbar.trimURLs", true);
user_pref("browser.urlbar.shortcuts.bookmarks", false);    // fewer buttons in the urlbar dropdown
user_pref("browser.urlbar.shortcuts.tabs", false);
user_pref("browser.urlbar.shortcuts.history", false);
user_pref("browser.urlbar.showSearchTerms.enabled", true);
user_pref("browser.download.autohideButton", true);        // downloads button only while it has something to show
user_pref("browser.uidensity", 1);                        // compact
user_pref("browser.theme.dark-private-windows", true);
// Theme (Ember dark / Ash light) follows the OS setting; not forced here.
user_pref("browser.compactmode.show", true);
user_pref("browser.download.useDownloadDir", true);
user_pref("browser.download.alwaysOpenPanel", false);

// ---------- Security & privacy (Blaze principle: safe by default, nothing phones home) ----------
user_pref("dom.security.https_only_mode", true);                    // upgrade to HTTPS; warn before any plain-http page
user_pref("browser.contentblocking.category", "strict");            // strict tracking protection
user_pref("privacy.fingerprintingProtection", true);
user_pref("privacy.query_stripping.enabled", true);                 // strip tracking params (utm_*, fbclid...)
user_pref("privacy.query_stripping.enabled.pbmode", true);
user_pref("network.http.referer.XOriginTrimmingPolicy", 2);         // cross-site referrers carry only the origin
user_pref("media.peerconnection.ice.default_address_only", true);   // WebRTC must not leak local/VPN addresses
user_pref("network.IDN_show_punycode", true);                       // defeat look-alike (homograph) domains
user_pref("browser.search.update", false);
user_pref("extensions.systemAddon.update.url", "");
user_pref("security.protectionspopup.recordEventTelemetry", false);

// ---------- Update-proofing: Blaze pins its own tested Firefox; the browser must never update itself ----------
user_pref("app.update.auto", false);
user_pref("app.update.enabled", false);
user_pref("app.update.service.enabled", false);
user_pref("app.update.staging.enabled", false);
user_pref("app.update.checkInstallTime", false);
user_pref("browser.startup.homepage_override.mstone", "ignore");   // no "what's new" page after upgrades
user_pref("startup.homepage_welcome_url", "");
user_pref("startup.homepage_welcome_url.additional", "");
user_pref("browser.messaging-system.whatsNewPanel.enabled", false);

// ---------- Quiet by default: no background connections that are not needed to browse ----------
user_pref("dom.push.enabled", false);                      // web push keeps a socket to Mozilla open on every start
user_pref("dom.push.connection.enabled", false);
user_pref("browser.region.network.url", "");               // do not ask Mozilla which country this IP is in
user_pref("browser.region.local-geocoding", false);
user_pref("media.gmp-gmpopenh264.visible", false);
user_pref("media.gmp-gmpopenh264.autoupdate", false);

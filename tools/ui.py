#!/usr/bin/env python3
"""Drives a headless Blaze-profiled Firefox over Marionette to SEE the real UI.

  tools/ui.py contract [--firefox PATH] [--scheme light|dark|both]   exit 1 if the UI no longer matches what Blaze relies on
  tools/ui.py shot     [--firefox PATH] [--scheme light|dark] [--out FILE.png]

Run `contract` before bumping FIREFOX_VERSION in engine.env: Firefox changes its internal DOM between releases
(e.g. 159 renamed #urlbar-background to .urlbar-background) and a silent CSS miss is how a "pinned" UI rots.
Everything happens in a throw-away profile; nothing on your desktop is touched.
"""
import argparse, base64, glob, json, os, shutil, socket, subprocess, sys, tempfile, time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
TOKENS = {  # rgb strings Firefox reports for our tokens
    "light": {"bg": "rgb(244, 244, 247)", "surface": "rgb(255, 255, 255)"},
    "dark":  {"bg": "rgb(20, 20, 26)",    "surface": "rgb(28, 28, 36)"},
}
ORANGE = "rgb(255, 106, 26)"


def find_firefox():
    data = os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")
    managed = sorted(glob.glob(f"{data}/blaze/engine/*/firefox/firefox"))
    return managed[-1] if managed else shutil.which("firefox")


class Marionette:
    def __init__(self, port):
        self.s = None
        for _ in range(80):
            try:
                self.s = socket.create_connection(("127.0.0.1", port), timeout=120); break
            except OSError:
                time.sleep(0.5)
        if not self.s: raise RuntimeError("Marionette never came up")
        self.f = self.s.makefile("rwb", buffering=0); self.n = 0
        self._recv(); self.cmd("WebDriver:NewSession", {"capabilities": {}})

    def _recv(self):
        h = b""
        while not h.endswith(b":"): h += self.f.read(1)
        n = int(h[:-1]); buf = b""
        while len(buf) < n:                    # raw sockets may deliver a big message (a screenshot) in pieces
            chunk = self.f.read(n - len(buf))
            if not chunk: raise RuntimeError("Marionette closed the connection mid-message")
            buf += chunk
        return json.loads(buf)

    def cmd(self, name, params=None):
        self.n += 1; m = json.dumps([0, self.n, name, params or {}]).encode()
        self.f.write(str(len(m)).encode() + b":" + m)
        while True:
            r = self._recv()
            if isinstance(r, list) and r[1] == self.n:
                if r[2]: raise RuntimeError(f"{name}: {r[2].get('message', r[2])}")
                return r[3]

    def script(self, js):
        return self.cmd("WebDriver:ExecuteScript", {"script": js, "args": []})["value"]

    def quit(self):
        try: self.cmd("Marionette:Quit", {"flags": ["eForceQuit"]})
        except Exception: pass


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0)); return s.getsockname()[1]


def launch(firefox, profile, extra_args=()):
    port = free_port()
    with open(os.path.join(profile, "user.js"), "a") as f: f.write(f'user_pref("marionette.port", {port});\n')
    env = {**os.environ, "MOZ_REMOTE_ALLOW_SYSTEM_ACCESS": "1"}
    p = subprocess.Popen([firefox, "--headless", "--no-remote", "--profile", profile, "--marionette", "--remote-allow-system-access", *extra_args],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env)
    return p, Marionette(port)


def make_profile(firefox, scheme):
    """Build the profile exactly like a user's first run: with scripts/blaze itself (stock layout first, then the
    sidebar prefs), against a throw-away data dir that shares the already-installed engine. Falls back to a manual
    two-phase init only when the engine is not a Blaze-managed one."""
    marker = "/blaze/engine/"
    agent = os.path.join(REPO, "dist", "agent", "blaze-agent")
    if marker in firefox and os.path.exists(agent):
        data_root = firefox.split(marker)[0]
        eng_name = firefox.split(marker)[1].split("/")[0]              # e.g. 159.0-en-US
        locale = eng_name.split("-", 1)[1]
        tmp = tempfile.mkdtemp(prefix="blaze-ui-")
        xdg = os.path.join(tmp, "xdg"); os.makedirs(os.path.join(xdg, "blaze", "profile", "extensions"))
        os.symlink(os.path.join(data_root, "blaze", "engine"), os.path.join(xdg, "blaze", "engine"))
        for x in glob.glob(os.path.join(data_root, "blaze", "profile", "extensions", "*.xpi")):
            shutil.copy(x, os.path.join(xdg, "blaze", "profile", "extensions"))     # avoid re-downloading the add-ons
        env = {**os.environ, "XDG_DATA_HOME": xdg, "BLAZE_BOOTSTRAP_ONLY": "1", "BLAZE_NO_UPDATE_CHECK": "1",
               "BLAZE_LOCALE": locale, "BLAZE_AGENT": agent}
        r = subprocess.run([os.path.join(REPO, "scripts", "blaze")], env=env, capture_output=True, text=True, timeout=240)
        if r.returncode != 0: raise RuntimeError("scripts/blaze failed:\n" + r.stderr[-600:])
        prof = os.path.join(xdg, "blaze", "profile")
        with open(f"{prof}/user.js", "a") as f: f.write(f'\nuser_pref("ui.systemUsesDarkTheme", {1 if scheme == "dark" else 0});\n')
        with open(f"{prof}/chrome/userChrome.css", "a") as f: f.write("\n#remote-control-box{display:none!important}\n")  # automation stripes
        return prof

    prof = tempfile.mkdtemp(prefix="blaze-ui-")
    os.makedirs(f"{prof}/chrome")
    src = open(f"{REPO}/profile/user.js").read()
    open(f"{prof}/user.js", "w").write("\n".join(l for l in src.splitlines() if "sidebar." not in l) + "\n")
    p, m = launch(firefox, prof)
    time.sleep(3); m.quit(); time.sleep(2); p.kill(); p.wait()
    open(f"{prof}/user.js", "w").write(src + f'\nuser_pref("ui.systemUsesDarkTheme", {1 if scheme == "dark" else 0});\n')
    for css in glob.glob(f"{REPO}/profile/chrome/*.css"): shutil.copy(css, f"{prof}/chrome/")
    with open(f"{prof}/chrome/userChrome.css", "a") as f: f.write("\n#remote-control-box{display:none!important}\n")
    return prof


PROBE = r"""
const out={}; const q=(s)=>document.querySelector(s); const cs=(e)=>e?window.getComputedStyle(e):null;
const t=(k,f)=>{try{out[k]=f()}catch(e){out[k]='ERR '+e.message}};
t('brand',()=>Services.strings.createBundle('chrome://branding/locale/brand.properties').GetStringFromName('brandShortName'));
t('back',()=>!!q('#back-button')&&cs(q('#back-button')).display!=='none');
t('reload',()=>{const r=q('#reload-button');return !!r&&cs(r).display!=='none'});
t('urlbarBgEl',()=>!!q('#urlbar > .urlbar-background'));
t('urlbarBg',()=>{const c=cs(q('#urlbar > .urlbar-background'));return {bg:c.backgroundColor,border:parseFloat(c.borderTopWidth),radius:c.borderTopLeftRadius}});
t('lock',()=>{const e=q('#trust-icon-container')||q('#identity-box');return !!e&&cs(e).opacity==='1'&&cs(e).visibility==='visible'});
t('navbarBg',()=>cs(q('#nav-bar')).backgroundColor);
t('liteId',()=>!!q('#blaze-companion_blaze_local-browser-action'));
t('liteLabel',()=>{const x=q('#blaze-companion_blaze_local-browser-action .toolbarbutton-text');return !!x&&cs(x).display!=='none'&&x.getBoundingClientRect().width>0});
t('tabBg',()=>cs(gBrowser.selectedTab.querySelector('.tab-background')).backgroundColor);
t('sidebarRight',()=>({right:Services.prefs.getBoolPref('sidebar.position_start')===false,vertical:Services.prefs.getBoolPref('sidebar.verticalTabs'),mode:Services.prefs.getStringPref('sidebar.visibility','?')}));
t('clutter',()=>['#ipprotection-button','#ai-window-toggle','#smartwindow-group-tabs-button','#fxa-toolbar-menu-button'].filter(s=>{const e=q(s);return e&&cs(e).display!=='none'}));
t('prefs',()=>({autoUpdate:Services.prefs.getBoolPref('app.update.auto',true),httpsOnly:Services.prefs.getBoolPref('dom.security.https_only_mode',false),
   telemetry:Services.prefs.getBoolPref('toolkit.telemetry.enabled',true),push:Services.prefs.getBoolPref('dom.push.enabled',true)}));
return out;
"""


def sidebar_visible(png_bytes):
    """True/False from the pixels of a screenshot; None when Pillow is missing. A prefs-only check once passed while
    the strip was not drawn at all, so the contract looks at what is actually rendered."""
    try:
        import io
        from PIL import Image
    except ImportError:
        return None
    im = Image.open(io.BytesIO(png_bytes)).convert("RGB"); w, h = im.size
    bg = im.getpixel((w - 100, h // 2)); n = 0          # page background just left of the strip
    for x in range(w - 45, w - 5):
        for y in range(60, 320):
            if sum(abs(a - b) for a, b in zip(im.getpixel((x, y)), bg)) > 60: n += 1
    return n > 150


def check_scheme(firefox, scheme, shot=None):
    prof = make_profile(firefox, scheme)
    p, m = launch(firefox, prof)
    results = []
    try:
        m.cmd("WebDriver:SetWindowRect", {"width": 1280, "height": 760})
        m.cmd("Addon:Install", {"path": f"{REPO}/extension", "temporary": True})
        time.sleep(6)
        m.cmd("Marionette:SetContext", {"value": "chrome"})
        # Privacy Badger opens its welcome page in a second tab on a fresh profile. Keep only the first (blank) tab so the
        # check below sees the single-tab state, and so the page behind the strip is a plain background.
        m.script("for (const t of [...gBrowser.tabs].slice(1)) gBrowser.removeTab(t); return gBrowser.tabs.length;")
        time.sleep(3)
        # What a new user sees on first start (one tab). Opening extra tabs through Marionette hides the strip in
        # this automated setup, so the screenshot is taken BEFORE any navigation.
        early = base64.b64decode(m.cmd("WebDriver:TakeScreenshot", {"full": False, "hash": False})["value"])
        if shot: open(shot, "wb").write(early)

        m.cmd("Marionette:SetContext", {"value": "content"})
        try: m.cmd("WebDriver:Navigate", {"url": "https://example.org"})      # the lock icon only exists on https pages
        except Exception: pass
        time.sleep(6)
        m.cmd("Marionette:SetContext", {"value": "chrome"})
        o = m.script(PROBE)
        tok = TOKENS[scheme]

        def ck(name, ok, detail=""): results.append((name, bool(ok), detail))
        ck("engine presents itself as Blaze (brand name)", o["brand"] == "Blaze", f"brandShortName={o['brand']}; run scripts/blaze so the engine is rebranded")
        ck("back + reload buttons exist", o["back"] is True and o["reload"] is True, f"back={o['back']} reload={o['reload']}")
        ck("urlbar background element is .urlbar-background", o["urlbarBgEl"] is True)
        ub = o["urlbarBg"] if isinstance(o["urlbarBg"], dict) else {}
        ck("urlbar has the field fill", ub.get("bg") == tok["surface"], f"{ub.get('bg')} (want {tok['surface']})")
        ck("urlbar has a visible border (>=1px)", ub.get("border", 0) >= 1, f"{ub.get('border')}px")
        ck("lock / trust icon present and never faded", o["lock"] is True)
        ck("toolbar uses the Blaze background token", o["navbarBg"] == tok["bg"], f"{o['navbarBg']} (want {tok['bg']})")
        ck("Lite button present", o["liteId"] is True)
        ck("Lite button shows the word 'Lite'", o["liteLabel"] is True)
        ck("active tab is Blaze orange", o["tabBg"] == ORANGE, f"{o['tabBg']}")
        sr = o["sidebarRight"] if isinstance(o["sidebarRight"], dict) else {}
        ck("sidebar defaults: right side, vertical tabs, expand-on-hover", sr.get("right") is True and sr.get("vertical") is True and sr.get("mode") == "expand-on-hover", str(sr))
        vis = sidebar_visible(early)
        if vis is not None: ck("sidebar strip is actually drawn on first start (screenshot)", vis, "no tab strip pixels at the right edge")
        ck("Firefox clutter hidden (VPN, AI, accounts)", o["clutter"] == [], str(o["clutter"]))
        pr = o["prefs"] if isinstance(o["prefs"], dict) else {}
        ck("Firefox self-update is off", pr.get("autoUpdate") is False)
        ck("HTTPS-only on, telemetry off, web push off", pr.get("httpsOnly") is True and pr.get("telemetry") is False and pr.get("push") is False, str(pr))
    finally:
        m.quit(); time.sleep(2); p.kill()
        shutil.rmtree(prof if 'xdg' not in prof else os.path.dirname(os.path.dirname(os.path.dirname(prof))), ignore_errors=True)
    return results


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["contract", "shot"])
    ap.add_argument("--firefox"); ap.add_argument("--scheme", default=None); ap.add_argument("--out")
    a = ap.parse_args()
    ff = a.firefox or find_firefox()
    if not ff: sys.exit("no Firefox found (run scripts/blaze once, or pass --firefox)")
    ver = subprocess.run([ff, "--version"], capture_output=True, text=True).stdout.strip()
    print(f"engine: {ff}\n        {ver}")

    if a.mode == "shot":
        scheme = a.scheme or "light"
        out = a.out or os.path.join(tempfile.gettempdir(), f"blaze-{scheme}.png")
        check_scheme(ff, scheme, shot=out); print("saved", out); return

    failed = 0
    for scheme in (["light", "dark"] if a.scheme in (None, "both") else [a.scheme]):
        print(f"\n== {scheme} ==")
        for name, ok, detail in check_scheme(ff, scheme):
            print(f"  {'PASS' if ok else 'FAIL'}  {name}" + ("" if ok or not detail else f"   -> {detail}"))
            failed += (not ok)
    print("\nUI CONTRACT OK" if not failed else f"\n{failed} CHECK(S) FAILED - do not ship this Firefox version")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()

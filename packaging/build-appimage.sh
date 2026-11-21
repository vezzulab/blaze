#!/usr/bin/env bash
# Builds dist/Blaze-<version>-x86_64.AppImage  (needs: dotnet SDK 10 with AOT, ImageMagick, curl)
# The AppImage contains the Blaze agent (native AOT), launcher, profile, UI themes and the Lite extension.
# It does NOT contain Firefox: the launcher downloads the pinned, checksum-verified engine on first run (see engine.env).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Build tool, pinned by version AND checksum. Never run an unverified downloaded binary.
APPIMAGETOOL_VERSION=1.9.1
APPIMAGETOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0

DIST="$ROOT/dist"; APPDIR="$DIST/AppDir"
rm -rf "$APPDIR" "$DIST/agent"; mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/blaze" "$DIST/tools"

echo "==> publishing the native agent"
dotnet publish src/Blaze.Agent -c Release -r linux-x64 -o "$DIST/agent" >/dev/null
VERSION="$("$DIST/agent/blaze-agent" version)"

echo "==> assembling AppDir ($VERSION)"
install -m755 "$DIST/agent/blaze-agent" "$APPDIR/usr/bin/blaze-agent"
install -m755 scripts/blaze              "$APPDIR/usr/bin/blaze"
cp -r engine.env profile extension       "$APPDIR/usr/share/blaze/"
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export APPDIR="$HERE"
exec "$HERE/usr/bin/blaze" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

install -Dm644 assets/blaze.svg "$APPDIR/usr/share/icons/hicolor/scalable/apps/blaze.svg"
magick -background none -density 384 assets/blaze.svg -resize 256x256 "$APPDIR/blaze.png"
cp "$APPDIR/blaze.png" "$APPDIR/.DirIcon"
cat > "$APPDIR/blaze.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Blaze
GenericName=Web Browser
Comment=Firefox-based browser built for speed and a light footprint
Exec=blaze
Icon=blaze
Terminal=false
Categories=Network;WebBrowser;
StartupWMClass=Blaze
X-AppImage-Version=$VERSION
DESKTOP
install -Dm644 "$APPDIR/blaze.desktop" "$APPDIR/usr/share/applications/blaze.desktop"

TOOL="$DIST/tools/appimagetool-$APPIMAGETOOL_VERSION-x86_64.AppImage"
if [ ! -f "$TOOL" ]; then
  echo "==> fetching appimagetool $APPIMAGETOOL_VERSION"
  curl -fL --retry 8 --retry-delay 3 --retry-all-errors --connect-timeout 20 --max-time 900 -C - -o "$TOOL.part" "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
  echo "$APPIMAGETOOL_SHA256  $TOOL.part" | sha256sum -c --status - || { rm -f "$TOOL.part"; echo "appimagetool checksum mismatch; refusing to run it" >&2; exit 1; }
  mv "$TOOL.part" "$TOOL"
fi
chmod +x "$TOOL"

OUT="$DIST/Blaze-$VERSION-x86_64.AppImage"
echo "==> building $OUT"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$OUT" >/dev/null
chmod +x "$OUT"
echo "built: $OUT ($(du -h "$OUT" | cut -f1))"

#!/usr/bin/env bash
# Installs the Blaze icon and an app-menu launcher for the current user. No root needed. Safe to re-run.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ICONS="$HOME/.local/share/icons/hicolor"
APPS="$HOME/.local/share/applications"

mkdir -p "$ICONS/scalable/apps" "$APPS"
cp "$ROOT/assets/blaze.svg" "$ICONS/scalable/apps/blaze.svg"
for size in 48 64 128 256; do
  mkdir -p "$ICONS/${size}x${size}/apps"
  magick -background none -density 384 "$ROOT/assets/blaze.svg" -resize "${size}x${size}" "$ICONS/${size}x${size}/apps/blaze.png"
done

cat > "$APPS/blaze.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Blaze
GenericName=Web Browser
Comment=Fast, lightweight browser for slow connections
Exec=$ROOT/scripts/blaze
Icon=blaze
Terminal=false
Categories=Network;WebBrowser;
StartupWMClass=Blaze
StartupNotify=true
EOF

gtk-update-icon-cache -q -t "$ICONS" 2>/dev/null || true
update-desktop-database "$APPS" 2>/dev/null || true
kbuildsycoca6 >/dev/null 2>&1 || true
echo "Installed: $APPS/blaze.desktop"

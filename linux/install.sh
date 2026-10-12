#!/usr/bin/env bash
# Installs FModel for the current user (~/.local), run from the extracted release folder.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"

install -Dm755 "$HERE/FModel" "$PREFIX/lib/fmodel/FModel"
for f in "$HERE"/*.so; do [ -e "$f" ] && install -Dm644 "$f" "$PREFIX/lib/fmodel/$(basename "$f")"; done
mkdir -p "$PREFIX/bin"
ln -sf "$PREFIX/lib/fmodel/FModel" "$PREFIX/bin/FModel"
install -Dm644 "$HERE/fmodel.png" "$PREFIX/share/icons/hicolor/256x256/apps/fmodel.png"
sed "s|^Exec=.*|Exec=$PREFIX/bin/FModel %F|" "$HERE/fmodel.desktop" > "$PREFIX/share/applications/fmodel.desktop" 2>/dev/null || {
    mkdir -p "$PREFIX/share/applications"
    sed "s|^Exec=.*|Exec=$PREFIX/bin/FModel %F|" "$HERE/fmodel.desktop" > "$PREFIX/share/applications/fmodel.desktop"
}
command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" || true
echo "FModel installed to $PREFIX/lib/fmodel (launcher: $PREFIX/bin/FModel)"
echo "Settings: ~/.config/FModel, data/output: next to the binary if writable, otherwise ~/.local/share/FModel/Output"

#!/usr/bin/env bash
# Builds FModel-x86_64.AppImage (self-contained, includes the .NET runtime and all native libraries).
# Requirements: .NET 10 SDK, cmake, g++, curl. Output: ./FModel-x86_64.AppImage
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/FModel/bin/AppImage"
APPDIR="$OUT/FModel.AppDir"
TARGET="${APPIMAGE_OUTPUT:-$ROOT/FModel-x86_64.AppImage}"

git -C "$ROOT" submodule update --init --recursive
rm -rf "${OUT:?}"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

dotnet publish "$ROOT/FModel/FModel.csproj" -c Release -r linux-x64 --self-contained true \
    -o "$APPDIR/usr/lib/fmodel" \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=false \
    -p:DebugType=None -p:DebugSymbols=false \
    -p:GenerateDocumentationFile=false

if [ ! -f "$APPDIR/usr/lib/fmodel/CUE4Parse-Natives.so" ]; then
    echo "warning: CUE4Parse-Natives.so was not built (cmake/g++ missing?)" >&2
fi

ln -s ../lib/fmodel/FModel "$APPDIR/usr/bin/FModel"
cp "$ROOT/linux/fmodel.desktop" "$APPDIR/fmodel.desktop"
cp "$ROOT/linux/fmodel.desktop" "$APPDIR/usr/share/applications/fmodel.desktop"
cp "$ROOT/linux/fmodel.png" "$APPDIR/fmodel.png"
cp "$ROOT/linux/fmodel.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/fmodel.png"
ln -s fmodel.png "$APPDIR/.DirIcon"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/fmodel/FModel" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

TOOL="$OUT/appimagetool"
curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$TARGET"
echo "Done: $TARGET"

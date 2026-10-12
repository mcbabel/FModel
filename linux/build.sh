#!/usr/bin/env bash
# Builds a self-contained native Linux release of FModel.
#   ./linux/build.sh            -> linux-x64
#   ./linux/build.sh linux-arm64
# Output: FModel/bin/Publish/<rid>/FModel (single executable) + FModel-<rid>.tar.gz
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/FModel/bin/Publish/$RID"

git -C "$ROOT" submodule update --init --recursive
"$ROOT/linux/apply-patches.sh"

dotnet publish "$ROOT/FModel/FModel.csproj" -c Release -r "$RID" --self-contained true \
    -o "$OUT" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishReadyToRun=false \
    -p:DebugType=None -p:DebugSymbols=false \
    -p:GenerateDocumentationFile=false

cp "$ROOT/linux/fmodel.desktop" "$ROOT/linux/fmodel.png" "$ROOT/linux/install.sh" "$OUT/"
tar -C "$OUT/.." -czf "$ROOT/FModel-$RID.tar.gz" "$RID"
echo "Done: $OUT/FModel  (archive: FModel-$RID.tar.gz)"

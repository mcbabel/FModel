#!/usr/bin/env bash
# Applies the Linux fixes in linux/patches to the CUE4Parse submodule (idempotent).
# The submodule points to upstream CUE4Parse, these patches carry the changes Linux needs on top of it.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

for patch in "$ROOT"/linux/patches/cue4parse-*.patch; do
    [ -e "$patch" ] || continue
    if git -C "$ROOT/CUE4Parse" apply --reverse --check "$patch" 2>/dev/null; then
        echo "already applied: $(basename "$patch")"
    else
        git -C "$ROOT/CUE4Parse" apply "$patch"
        echo "applied: $(basename "$patch")"
    fi
done

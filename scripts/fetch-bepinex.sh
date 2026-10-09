#!/usr/bin/env bash
set -Eeuo pipefail

readonly RELEASE_TAG="5.4.11"
readonly VERSION="5.4.11.0"
readonly TARGET="${1:-win-x86}"
readonly ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
readonly DEST="$ROOT/artifacts/bepinex"

[[ "$TARGET" == win-x86 ]] || {
  printf 'Native Linux support is paused; only win-x86 for Crawl Windows 1.0.1 is available.\n' >&2
  exit 2
}
asset="BepInEx_x86_${VERSION}.zip"

mkdir -p "$DEST"
gh release download "v$RELEASE_TAG" \
  --repo BepInEx/BepInEx \
  --pattern "$asset" \
  --dir "$DEST" \
  --clobber
rm -rf "$DEST/BepInEx" "$DEST/doorstop_libs"
rm -f "$DEST/doorstop_config.ini" "$DEST/winhttp.dll" "$DEST/run_bepinex.sh"
bsdtar -xf "$DEST/$asset" -C "$DEST"
printf 'BepInEx %s preparado em %s\n' "$VERSION" "$DEST"

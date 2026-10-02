#!/usr/bin/env bash
set -Eeuo pipefail

readonly RELEASE_TAG="5.4.11"
readonly VERSION="5.4.11.0"
readonly TARGET="${1:-linux-x64}"
readonly ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
readonly DEST="$ROOT/artifacts/bepinex"

case "$TARGET" in
  linux-x64) asset="BepInEx_unix_${VERSION}.zip" ;;
  win-x86) asset="BepInEx_x86_${VERSION}.zip" ;;
  *) printf 'Target inválido: %s (use linux-x64 ou win-x86)\n' "$TARGET" >&2; exit 2 ;;
esac

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

#!/usr/bin/env bash
set -Eeuo pipefail

readonly GAME_DIR="${CRAWL_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Crawl}"
readonly PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/CrawlOnline"

if [[ -d "$PLUGIN_DIR" ]]; then
  rm -f "$PLUGIN_DIR/CrawlOnline.dll"
  rm -f "$PLUGIN_DIR/CrawlOnline.Runtime.dll"
  rmdir "$PLUGIN_DIR" 2>/dev/null || true
fi

printf 'Plugin Crawl Online removido. O BepInEx foi preservado para não apagar outros mods.\n'

#!/usr/bin/env bash
set -Eeuo pipefail

readonly ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
readonly GAME_DIR="${CRAWL_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Crawl}"
readonly LOADER="$ROOT/artifacts/bepinex"
readonly PLUGIN="$ROOT/src/CrawlOnline.Bootstrap/bin/Release/net35/CrawlOnline.dll"
readonly RUNTIME="$ROOT/src/CrawlOnline/bin/Release/net35/CrawlOnline.Runtime.dll"

[[ -x "$GAME_DIR/Crawl.x86_64" ]] || {
  printf 'Crawl não encontrado em %s\n' "$GAME_DIR" >&2
  exit 1
}

[[ -f "$LOADER/BepInEx/core/BepInEx.dll" ]] || {
  printf 'Execute scripts/fetch-bepinex.sh linux-x64 primeiro.\n' >&2
  exit 1
}

[[ -f "$PLUGIN" && -f "$RUNTIME" ]] || {
  printf 'Compile o projeto antes de instalar.\n' >&2
  exit 1
}

mkdir -p "$GAME_DIR/BepInEx/plugins/CrawlOnline"
mkdir -p "$GAME_DIR/BepInEx/config"
cp -a "$LOADER/BepInEx/core" "$GAME_DIR/BepInEx/"
cp -a "$ROOT/packaging/common/BepInEx.cfg" "$GAME_DIR/BepInEx/config/BepInEx.cfg"
cp -a "$LOADER/doorstop_libs" "$GAME_DIR/"
cp -a "$LOADER/run_bepinex.sh" "$GAME_DIR/"
cp -a "$PLUGIN" "$GAME_DIR/BepInEx/plugins/CrawlOnline/"
cp -a "$RUNTIME" "$GAME_DIR/BepInEx/plugins/CrawlOnline/"
chmod +x "$GAME_DIR/run_bepinex.sh"

cat <<EOF
Crawl Online instalado para desenvolvimento.

Teste direto (Steam deve estar aberto):
  cd "$GAME_DIR"
  ./run_bepinex.sh ./Crawl.x86_64

Para iniciar pela biblioteca Steam, use temporariamente esta opção de inicialização:
  ./run_bepinex.sh ./Crawl.x86_64 # %command%
EOF

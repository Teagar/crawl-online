#!/usr/bin/env bash
set -Eeuo pipefail

readonly ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
readonly MODE="${1:-}"
readonly TRACE_INPUT="${2:-}"
readonly TARGET_FPS="${3:-60}"
readonly SOURCE_GAME_DIR="${CRAWL_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Crawl}"
readonly WORK_ROOT="${CRAWL_ONLINE_WORK_ROOT:-/tmp/opencode}"
readonly REAL_HOME="$HOME"

if [[ "$MODE" != record && "$MODE" != replay ]] || [[ -z "$TRACE_INPUT" ]]; then
  printf 'Uso: %s record|replay CAMINHO_TRACE [FPS]\n' "$0" >&2
  exit 2
fi

[[ -x "$SOURCE_GAME_DIR/Crawl.x86_64" ]] || {
  printf 'Crawl não encontrado em %s\n' "$SOURCE_GAME_DIR" >&2
  exit 1
}

mkdir -p "$WORK_ROOT"
readonly INSTANCE="$(mktemp -d "$WORK_ROOT/crawl-online-harness.XXXXXX")"
readonly TEST_HOME="$INSTANCE/home"
readonly TRACE_PATH="$(realpath -m -- "$TRACE_INPUT")"
child_pid=""
cleanup() {
  if [[ -n "$child_pid" ]] && kill -0 "$child_pid" 2>/dev/null; then
    kill -TERM -- "-$child_pid" 2>/dev/null || true
    wait "$child_pid" 2>/dev/null || true
  fi
  if [[ -f "$INSTANCE/game/BepInEx/LogOutput.log" ]]; then
    cp "$INSTANCE/game/BepInEx/LogOutput.log" "$TRACE_PATH.$MODE.bepinex.log"
  fi
  if [[ -f "$TEST_HOME/.config/unity3d/Powerhoof/Crawl/Player.log" ]]; then
    cp "$TEST_HOME/.config/unity3d/Powerhoof/Crawl/Player.log" "$TRACE_PATH.$MODE.player.log"
  fi
  rm -rf -- "$INSTANCE"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
trap 'exit 129' HUP

mkdir -p "$INSTANCE/game"
cp -a --reflink=auto "$SOURCE_GAME_DIR/." "$INSTANCE/game/"
mkdir -p "$TEST_HOME/.local/share" "$TEST_HOME/.config"
[[ ! -e "$TEST_HOME/.steam" ]] && ln -s "$REAL_HOME/.steam" "$TEST_HOME/.steam"
[[ ! -e "$TEST_HOME/.local/share/Steam" ]] && ln -s "$REAL_HOME/.local/share/Steam" "$TEST_HOME/.local/share/Steam"

CRAWL_GAME_DIR="$INSTANCE/game" "$ROOT/scripts/install-dev-linux.sh" >/dev/null
mkdir -p "$(dirname -- "$TRACE_PATH")"

cd "$INSTANCE/game"
setsid env \
  HOME="$TEST_HOME" \
  SteamAppId=293780 \
  SteamGameId=293780 \
  CRAWL_ONLINE_TRACE_MODE="$MODE" \
  CRAWL_ONLINE_TRACE_PATH="$TRACE_PATH" \
  CRAWL_ONLINE_TARGET_FPS="$TARGET_FPS" \
  ./run_bepinex.sh ./Crawl.x86_64 -screen-fullscreen 0 -screen-width 960 -screen-height 540 &
child_pid=$!
wait "$child_pid"

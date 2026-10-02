#!/usr/bin/env bash
# Installs a verified Crawl Online release without copying any Crawl or Steam files.
set -Eeuo pipefail

usage() { cat <<'TEXT'
Usage: install-release-linux.sh <install|update|uninstall|diagnose> --package FILE_OR_DIRECTORY [--game-dir DIR] [--allow-unknown-game]

Download the Crawl Online release ZIP first and pass it with --package. The package
contains only Crawl Online; BepInEx is downloaded from its upstream release with the
SHA-256 recorded in the package manifest. Uninstall removes only Crawl Online files.
TEXT
}
command="${1:-}"; [[ -n "$command" ]] && shift || true
package=""; game_dir="${CRAWL_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Crawl}"; allow_unknown=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --package) package="${2:?missing package path}"; shift 2 ;;
    --game-dir) game_dir="${2:?missing game directory}"; shift 2 ;;
    --allow-unknown-game) allow_unknown=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done
case "$command" in install|update|uninstall|diagnose) ;; *) usage >&2; exit 2;; esac
[[ -d "$game_dir" && -x "$game_dir/Crawl.x86_64" ]] || { printf 'Crawl Linux was not found at %s. Use --game-dir.\n' "$game_dir" >&2; exit 1; }

work=""; stage=""; backup=""; activation_started=false; activation_complete=false
cleanup() {
  if [[ "$activation_started" == true && "$activation_complete" != true ]]; then
    rm -f "$plugin_dir/CrawlOnline.dll" "$plugin_dir/CrawlOnline.Runtime.dll" "$state"
    for old in CrawlOnline.dll CrawlOnline.Runtime.dll .crawl-online-install.json; do
      [[ -e "$backup/$old" ]] && mv "$backup/$old" "$plugin_dir/$old"
    done
  fi
  [[ -n "$stage" ]] && rm -rf "$stage"
  [[ -n "$backup" ]] && rm -rf "$backup"
  [[ -n "$work" ]] && rm -rf "$work"
  return 0
}
trap cleanup EXIT
if [[ -n "$package" ]]; then
  if [[ -d "$package" ]]; then release_dir="$package"; else
    [[ -f "$package" ]] || { printf 'Package not found: %s\n' "$package" >&2; exit 1; }
    work="$(mktemp -d)"; unzip -q "$package" -d "$work"
    release_dir="$(find "$work" -name CrawlOnline.release.json -printf '%h\n' | head -n1)"
  fi
else release_dir=""; fi
manifest="$release_dir/CrawlOnline.release.json"
[[ "$command" == uninstall || "$command" == diagnose ]] || [[ -f "$manifest" ]] || { printf 'A release package with CrawlOnline.release.json is required.\n' >&2; exit 2; }

python3 - "$manifest" "$game_dir" "$allow_unknown" "$command" <<'PY'
import hashlib, json, os, sys
manifest, game, allow, command = sys.argv[1:]
known='d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84'
assembly=os.path.join(game, 'Crawl_Data', 'Managed', 'Assembly-CSharp.dll')
if not os.path.isfile(assembly): raise SystemExit('Crawl Assembly-CSharp.dll is missing.')
digest=hashlib.sha256(open(assembly,'rb').read()).hexdigest()
print('Crawl Assembly-CSharp.dll SHA-256: '+digest)
if digest != known and allow != 'true': raise SystemExit('Unsupported Crawl Linux build. Re-run only after reviewing with --allow-unknown-game.')
if command in ('install', 'update'):
 m=json.load(open(manifest, encoding='utf-8'))
 if m.get('schemaVersion') != 1 or not isinstance(m.get('version'),str): raise SystemExit('Unsupported release manifest.')
 for name, expected in m.get('plugins',{}).items():
  file=os.path.join(os.path.dirname(manifest),'plugins',name)
  actual=hashlib.sha256(open(file,'rb').read()).hexdigest() if os.path.isfile(file) else ''
  if actual != expected: raise SystemExit('Package hash mismatch: '+name)
 print('Release '+m['version']+' package hashes verified.')
PY

plugin_dir="$game_dir/BepInEx/plugins/CrawlOnline"
state="$plugin_dir/.crawl-online-install.json"
if [[ "$command" == uninstall ]]; then
  rm -f "$plugin_dir/CrawlOnline.dll" "$plugin_dir/CrawlOnline.Runtime.dll" "$state"
  rmdir "$plugin_dir" 2>/dev/null || true
  printf 'Crawl Online removed. BepInEx and every other plugin were preserved.\n'; exit 0
fi
if [[ "$command" == diagnose ]]; then
  if [[ -f "$state" ]]; then
    cat "$state"
    python3 - "$state" "$plugin_dir" <<'PY'
import hashlib,json,os,sys
state,plugin_dir=sys.argv[1:]
data=json.load(open(state,encoding='utf-8'))
failed=False
for name,expected in data.get('plugins',{}).items():
 path=os.path.join(plugin_dir,name)
 actual=hashlib.sha256(open(path,'rb').read()).hexdigest() if os.path.isfile(path) else ''
 ok=actual==expected
 print(f'{name}: '+('verified' if ok else 'MISSING OR MODIFIED'))
 failed |= not ok
if failed: raise SystemExit('Crawl Online plugin integrity check failed.')
PY
  else
    printf 'Crawl Online is not installed.\n'
  fi
  [[ -f "$game_dir/BepInEx/core/BepInEx.dll" ]] && printf 'BepInEx core: present\n' || printf 'BepInEx core: missing\n'
  exit 0
fi

if [[ ! -f "$game_dir/BepInEx/core/BepInEx.dll" ]]; then
  readarray -t source < <(python3 - "$manifest" <<'PY'
import json,sys
x=json.load(open(sys.argv[1]))['bepInEx']['linux-x64']; print(x['url']); print(x['sha256'])
PY
)
  [[ "${source[1]}" =~ ^[0-9a-f]{64}$ ]] || { printf 'Release manifest lacks a valid BepInEx hash.\n' >&2; exit 1; }
  loader_zip="${work:-$(mktemp -d)}/bepinex.zip"; [[ -n "$work" ]] || work="${loader_zip%/*}"
  curl --fail --location --proto '=https' --tlsv1.2 -o "$loader_zip" "${source[0]}"
  [[ "$(sha256sum "$loader_zip" | awk '{print $1}')" == "${source[1]}" ]] || { printf 'BepInEx download hash mismatch.\n' >&2; exit 1; }
  unzip -q "$loader_zip" -d "$game_dir"
else
  strings "$game_dir/BepInEx/core/BepInEx.dll" | grep -q '5.4.11' || { printf 'Existing BepInEx is not 5.4.11; it was not changed.\n' >&2; exit 1; }
fi
mkdir -p "$plugin_dir"
stage="$(mktemp -d "$plugin_dir/.crawl-online-stage.XXXXXX")"
backup="$(mktemp -d "$plugin_dir/.crawl-online-backup.XXXXXX")"
cp "$release_dir/plugins/CrawlOnline.dll" "$release_dir/plugins/CrawlOnline.Runtime.dll" "$stage/"
python3 - "$manifest" > "$stage/.crawl-online-install.json" <<'PY'
import json,sys
m=json.load(open(sys.argv[1])); print(json.dumps({'version':m['version'],'plugins':m['plugins']}, indent=2))
PY
activation_started=true
for name in CrawlOnline.dll CrawlOnline.Runtime.dll .crawl-online-install.json; do
  [[ -e "$plugin_dir/$name" ]] && mv "$plugin_dir/$name" "$backup/$name"
done
activated=0
for name in CrawlOnline.dll CrawlOnline.Runtime.dll .crawl-online-install.json; do
  mv "$stage/$name" "$plugin_dir/$name"
  activated=$((activated + 1))
  if [[ "${CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE:-}" == 1 && $activated -eq 1 ]]; then
    printf 'Injected activation failure for rollback test.\n' >&2
    exit 97
  fi
done
activation_complete=true
printf 'Crawl Online installed. Linux Steam launch option (once): ./run_bepinex.sh ./Crawl.x86_64 # %%command%%\n'

#!/usr/bin/env bash
# Creates a redistributable package containing only Crawl Online binaries and installers.
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?usage: scripts/package-release.sh VERSION WINDOWS_BEPINEX_ZIP [output-dir]}"
WINDOWS_BEPINEX="${2:?missing Windows x86 BepInEx 5.4.11 archive}"
OUTPUT="${3:-$ROOT/dist}"
BOOTSTRAP="${CRAWL_ONLINE_BOOTSTRAP:-$ROOT/src/CrawlOnline.Bootstrap/bin/Release/net35/CrawlOnline.dll}"
RUNTIME="${CRAWL_ONLINE_RUNTIME:-$ROOT/src/CrawlOnline/bin/Release/net35/CrawlOnline.Runtime.dll}"
STAGE="$OUTPUT/CrawlOnline-$VERSION"

for file in "$BOOTSTRAP" "$RUNTIME" "$WINDOWS_BEPINEX"; do
  [[ -f "$file" ]] || { printf 'Required file is missing: %s\n' "$file" >&2; exit 1; }
done
[[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]] || { printf 'Invalid version: %s\n' "$VERSION" >&2; exit 2; }

python3 "$ROOT/scripts/audit-windows-compatibility.py" \
  --project "$ROOT/src/CrawlOnline.Bootstrap/CrawlOnline.Bootstrap.csproj" \
  --project "$ROOT/src/CrawlOnline/CrawlOnline.csproj" \
  --assembly "$BOOTSTRAP" \
  --assembly "$RUNTIME"

rm -rf "$STAGE"
mkdir -p "$STAGE/plugins"
cp "$BOOTSTRAP" "$RUNTIME" "$STAGE/plugins/"
cp "$ROOT/scripts/install-release-linux.sh" "$ROOT/scripts/install-release-windows.ps1" "$ROOT/scripts/collect-diagnostics-windows.ps1" "$STAGE/"
cp "$ROOT/docs/installation.md" "$STAGE/INSTALL.md"
cp "$ROOT/docs/windows-validation.md" "$STAGE/WINDOWS-VALIDATION.md"
chmod +x "$STAGE/install-release-linux.sh"

bootstrap_hash="$(sha256sum "$STAGE/plugins/CrawlOnline.dll" | awk '{print $1}')"
runtime_hash="$(sha256sum "$STAGE/plugins/CrawlOnline.Runtime.dll" | awk '{print $1}')"
windows_hash="$(sha256sum "$WINDOWS_BEPINEX" | awk '{print $1}')"
cat > "$STAGE/CrawlOnline.release.json" <<EOF_MANIFEST
{
  "schemaVersion": 1,
  "version": "$VERSION",
  "plugins": {
    "CrawlOnline.dll": "$bootstrap_hash",
    "CrawlOnline.Runtime.dll": "$runtime_hash"
  },
  "bepInEx": {
    "version": "5.4.11.0",
    "win-x86": {
      "url": "https://github.com/BepInEx/BepInEx/releases/download/v5.4.11/BepInEx_x86_5.4.11.0.zip",
      "sha256": "$windows_hash"
    }
  }
}
EOF_MANIFEST
(
  cd "$OUTPUT"
  rm -f "CrawlOnline-$VERSION.zip"
  zip -qr "CrawlOnline-$VERSION.zip" "CrawlOnline-$VERSION"
  "$ROOT/scripts/audit-release-package.py" "CrawlOnline-$VERSION.zip" "$VERSION"
  sha256sum "CrawlOnline-$VERSION.zip" > "CrawlOnline-$VERSION.zip.sha256"
)
printf 'Release package created: %s/CrawlOnline-%s.zip\n' "$OUTPUT" "$VERSION"

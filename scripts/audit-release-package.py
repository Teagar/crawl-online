#!/usr/bin/env python3
"""Fail closed unless a Crawl Online release ZIP is complete and redistributable."""
import hashlib
import json
import sys
import zipfile
from pathlib import PurePosixPath


def fail(message):
    raise SystemExit("release audit failed: " + message)


if len(sys.argv) != 3:
    raise SystemExit("usage: audit-release-package.py ARCHIVE VERSION")

archive_path = sys.argv[1]
version = sys.argv[2]
prefix = "CrawlOnline-" + version + "/"
expected = {
    "INSTALL.md",
    "CrawlOnline.release.json",
    "install-release-linux.sh",
    "install-release-windows.ps1",
    "collect-diagnostics-windows.ps1",
    "WINDOWS-VALIDATION.md",
    "plugins/CrawlOnline.dll",
    "plugins/CrawlOnline.Runtime.dll",
}

with zipfile.ZipFile(archive_path) as archive:
    files = {}
    for info in archive.infolist():
        if info.is_dir():
            continue
        name = info.filename
        if not name.startswith(prefix):
            fail("entry outside release root: " + name)
        relative = name[len(prefix):]
        if not relative or relative.startswith("/") or ".." in PurePosixPath(relative).parts:
            fail("unsafe entry: " + name)
        if relative in files:
            fail("duplicate entry: " + relative)
        files[relative] = archive.read(info)

if set(files) != expected:
    fail("unexpected package contents: " + ", ".join(sorted(set(files) ^ expected)))

try:
    manifest = json.loads(files["CrawlOnline.release.json"])
except (KeyError, ValueError) as error:
    fail("invalid manifest: " + str(error))

if manifest.get("schemaVersion") != 1 or manifest.get("version") != version:
    fail("manifest schema or version mismatch")
plugins = manifest.get("plugins")
if not isinstance(plugins, dict) or set(plugins) != {"CrawlOnline.dll", "CrawlOnline.Runtime.dll"}:
    fail("manifest plugin set mismatch")
for name, expected_hash in plugins.items():
    actual_hash = hashlib.sha256(files["plugins/" + name]).hexdigest()
    if expected_hash != actual_hash:
        fail("plugin hash mismatch: " + name)

bepinex = manifest.get("bepInEx")
if not isinstance(bepinex, dict) or bepinex.get("version") != "5.4.11.0":
    fail("BepInEx manifest mismatch")
for platform in ("linux-x64", "win-x86"):
    entry = bepinex.get(platform)
    if not isinstance(entry, dict) or not entry.get("url") or len(entry.get("sha256", "")) != 64:
        fail("BepInEx " + platform + " manifest mismatch")

print("release package audit passed: " + archive_path)

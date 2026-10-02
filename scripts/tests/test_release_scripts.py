#!/usr/bin/env python3
"""Static regression checks for the cross-platform release installer contract."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import tempfile
import zipfile

root = Path(__file__).resolve().parents[2]
linux = (root / 'scripts/install-release-linux.sh').read_text()
windows = (root / 'scripts/install-release-windows.ps1').read_text()
packager = (root / 'scripts/package-release.sh').read_text()
docs = (root / 'docs/installation.md').read_text()

for command in ('install', 'update', 'uninstall', 'diagnose'):
    assert command in linux and command in windows
for text in ('Assembly-CSharp.dll', 'sha256', '5.4.11', 'CrawlOnline.Runtime.dll'):
    assert text in linux and text in windows
assert 'rm -rf "$game_dir/BepInEx"' not in linux
assert 'Remove-Item -Recurse -Force -LiteralPath (Join-Path $GameDir' not in windows
assert 'BepInEx and every other plugin were preserved' in linux
assert 'BepInEx and every other plugin were preserved' in windows
assert 'BepInEx_unix_5.4.11.0.zip' in packager
assert 'BepInEx_x86_5.4.11.0.zip' in packager
assert 'multiplayer gameplay end-to-end validated' in docs
print('release installer static contract checks passed')

with tempfile.TemporaryDirectory() as temp:
    temp = Path(temp)
    bootstrap, runtime = temp / 'CrawlOnline.dll', temp / 'CrawlOnline.Runtime.dll'
    linux_loader, windows_loader = temp / 'linux.zip', temp / 'windows.zip'
    bootstrap.write_bytes(b'bootstrap'); runtime.write_bytes(b'runtime')
    linux_loader.write_bytes(b'linux loader'); windows_loader.write_bytes(b'windows loader')
    output = temp / 'out'
    environment = os.environ | {'CRAWL_ONLINE_BOOTSTRAP': str(bootstrap), 'CRAWL_ONLINE_RUNTIME': str(runtime)}
    subprocess.run([str(root / 'scripts/package-release.sh'), 'test-1', str(linux_loader), str(windows_loader), str(output)], check=True, env=environment, stdout=subprocess.PIPE, text=True)
    archive = output / 'CrawlOnline-test-1.zip'
    assert archive.is_file() and (output / 'CrawlOnline-test-1.zip.sha256').is_file()
    with zipfile.ZipFile(archive) as zipped:
        manifest = json.loads(zipped.read('CrawlOnline-test-1/CrawlOnline.release.json'))
    assert manifest['plugins']['CrawlOnline.dll'] == hashlib.sha256(b'bootstrap').hexdigest()
    assert manifest['bepInEx']['linux-x64']['sha256'] == hashlib.sha256(b'linux loader').hexdigest()
print('release package smoke test passed')

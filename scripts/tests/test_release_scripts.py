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
assert '.crawl-online-backup.' in linux and 'activation_started=true' in linux
assert '.crawl-online-backup-' in windows and '$activationStarted = $true' in windows
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
        names = set(zipped.namelist())
        manifest = json.loads(zipped.read('CrawlOnline-test-1/CrawlOnline.release.json'))
    assert 'CrawlOnline-test-1/INSTALL.md' in names
    assert not any(name.endswith(('Assembly-CSharp.dll', 'steam_api.dll', 'Crawl.exe')) for name in names)
    assert manifest['plugins']['CrawlOnline.dll'] == hashlib.sha256(b'bootstrap').hexdigest()
    assert manifest['bepInEx']['linux-x64']['sha256'] == hashlib.sha256(b'linux loader').hexdigest()

    game = temp / 'game'
    (game / 'Crawl_Data/Managed').mkdir(parents=True)
    (game / 'BepInEx/core').mkdir(parents=True)
    (game / 'BepInEx/plugins/OtherMod').mkdir(parents=True)
    (game / 'Crawl.x86_64').write_text('#!/bin/sh\n')
    (game / 'Crawl.x86_64').chmod(0o755)
    (game / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'legitimate test fixture')
    (game / 'BepInEx/core/BepInEx.dll').write_bytes(b'BepInEx 5.4.11 test fixture')
    (game / 'BepInEx/plugins/OtherMod/keep.txt').write_text('keep')
    release_dir = output / 'CrawlOnline-test-1'
    installer = root / 'scripts/install-release-linux.sh'
    common = ['--game-dir', str(game), '--allow-unknown-game']
    install = subprocess.run([str(installer), 'install', '--package', str(release_dir), *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert install.returncode == 0, install.stdout + install.stderr
    diagnostic = subprocess.run([str(installer), 'diagnose', *common], check=True, stdout=subprocess.PIPE, text=True)
    assert diagnostic.stdout.count('verified') == 2
    installed = game / 'BepInEx/plugins/CrawlOnline/CrawlOnline.Runtime.dll'
    bootstrap_installed = game / 'BepInEx/plugins/CrawlOnline/CrawlOnline.dll'
    before = (bootstrap_installed.read_bytes(), installed.read_bytes())
    rollback_environment = environment | {'CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE': '1'}
    rollback = subprocess.run([str(installer), 'update', '--package', str(release_dir), *common], env=rollback_environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert rollback.returncode == 97
    assert (bootstrap_installed.read_bytes(), installed.read_bytes()) == before
    installed.write_bytes(b'tampered')
    failed = subprocess.run([str(installer), 'diagnose', *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert failed.returncode != 0 and 'MISSING OR MODIFIED' in failed.stdout
    subprocess.run([str(installer), 'update', '--package', str(release_dir), *common], check=True, stdout=subprocess.PIPE, text=True)
    subprocess.run([str(installer), 'uninstall', *common], check=True, stdout=subprocess.PIPE, text=True)
    assert not (game / 'BepInEx/plugins/CrawlOnline').exists()
    assert (game / 'BepInEx/plugins/OtherMod/keep.txt').read_text() == 'keep'
print('release package smoke test passed')

#!/usr/bin/env python3
"""Static regression checks for the cross-platform release installer contract."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import zipfile

root = Path(__file__).resolve().parents[2]
linux = (root / 'scripts/install-release-linux.sh').read_text()
windows = (root / 'scripts/install-release-windows.ps1').read_text()
windows_diagnostics = (root / 'scripts/collect-diagnostics-windows.ps1').read_text()
windows_diagnostics_test = (root / 'scripts/tests/test_windows_diagnostics.ps1').read_text()
windows_installer_test = (root / 'scripts/tests/test_windows_installer.ps1').read_text()
packager = (root / 'scripts/package-release.sh').read_text()
auditor = root / 'scripts/audit-release-package.py'
compatibility_auditor = root / 'scripts/audit-windows-compatibility.py'
docs = (root / 'docs/installation.md').read_text()
bootstrap_source = (root / 'src/CrawlOnline.Bootstrap/CrawlOnlinePlugin.cs').read_text()
simulation_source = (root / 'src/CrawlOnline/Development/DevSimulationSession.cs').read_text()

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
assert 'audit-release-package.py' in packager
assert '[BepInPlugin(Id, Name, LoaderVersion)]' in bootstrap_source
assert 'LoaderVersion = "0.2.0"' in bootstrap_source
assert 'Version = "0.2.0-alpha.1"' in bootstrap_source
assert 'new object[] { Logger, gameBuildFingerprint }' in bootstrap_source
assert '#if CRAWLONLINE_DEV_SIMULATION' in bootstrap_source and '#if CRAWLONLINE_DEV_SIMULATION' in simulation_source
assert 'SteamMatchmaking.' not in simulation_source and 'SteamNetworking.' not in simulation_source
assert 'GameApi.' not in simulation_source and 'SystemGame' not in simulation_source
assert 'multiplayer gameplay end-to-end validated' in docs
assert '.crawl-online-backup.' in linux and 'activation_started=true' in linux
assert '.crawl-online-backup-' in windows and '$activationStarted = $true' in windows
for text in ('Crawl.exe', '--platform', 'win-x86', 'winhttp.dll', 'e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e'):
    assert text in linux
for text in ('Assembly-CSharp.dll', 'SystemSteam', 'Awake', 'WINEDLLOVERRIDES="winhttp=n,b"'):
    assert text in linux and text in docs
for text in ('Test-LateEntrypoint', 'Set-LateEntrypoint', 'Assembly-CSharp.dll', 'SystemSteam', 'Awake', 'MISSING OR INCORRECT'):
    assert text in windows
for text in ('Get-PeMachine', 'Protect-DiagnosticText', 'sanitized-bepinex.log', 'PRIVACY.txt', 'Compress-Archive'):
    assert text in windows_diagnostics
for forbidden_copy in ('Copy-Item $LogPath', 'Crawl.sav', 'steam_api.dll'):
    assert forbidden_copy not in windows_diagnostics
assert 'collect-diagnostics-windows.ps1' in packager
assert 'WINDOWS-VALIDATION.md' in packager
for text in ('Write-PeX86', 'PrivateName', 'redacted-long-id', 'forbidden binary or save'):
    assert text in windows_diagnostics_test
for text in ('Clean install', "Invoke-Installer 'update'", 'CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE', "Invoke-Installer 'uninstall'"):
    assert text in windows_installer_test
print('release installer static contract checks passed')


def windows_executable(machine: int = 0x14C) -> bytes:
    data = bytearray(512)
    data[:2] = b'MZ'
    data[60:64] = (128).to_bytes(4, 'little')
    data[128:132] = b'PE\0\0'
    data[132:134] = machine.to_bytes(2, 'little')
    return bytes(data)


def managed_pe(machine: int = 0x14C, clr_flags: int = 1, runtime: bytes = b'v2.0.50727\0') -> bytes:
    data = bytearray(1024)
    data[:2] = b'MZ'
    data[60:64] = (128).to_bytes(4, 'little')
    data[128:132] = b'PE\0\0'
    data[132:134] = machine.to_bytes(2, 'little')
    data[134:136] = (1).to_bytes(2, 'little')
    data[148:150] = (224).to_bytes(2, 'little')
    optional = 152
    data[optional:optional + 2] = (0x10B).to_bytes(2, 'little')
    data[optional + 92:optional + 96] = (16).to_bytes(4, 'little')
    data[optional + 96 + 14 * 8:optional + 100 + 14 * 8] = (0x2000).to_bytes(4, 'little')
    data[optional + 100 + 14 * 8:optional + 104 + 14 * 8] = (72).to_bytes(4, 'little')
    section = optional + 224
    data[section:section + 8] = b'.text\0\0\0'
    data[section + 8:section + 12] = (512).to_bytes(4, 'little')
    data[section + 12:section + 16] = (0x2000).to_bytes(4, 'little')
    data[section + 16:section + 20] = (512).to_bytes(4, 'little')
    data[section + 20:section + 24] = (512).to_bytes(4, 'little')
    clr = 512
    data[clr:clr + 4] = (72).to_bytes(4, 'little')
    data[clr + 4:clr + 8] = bytes((2, 0, 5, 0))
    data[clr + 8:clr + 12] = (0x2080).to_bytes(4, 'little')
    data[clr + 12:clr + 16] = (128).to_bytes(4, 'little')
    data[clr + 16:clr + 20] = clr_flags.to_bytes(4, 'little')
    metadata = 640
    data[metadata:metadata + 4] = (0x424A5342).to_bytes(4, 'little')
    data[metadata + 4:metadata + 8] = bytes((1, 0, 1, 0))
    data[metadata + 12:metadata + 16] = len(runtime).to_bytes(4, 'little')
    data[metadata + 16:metadata + 16 + len(runtime)] = runtime
    return bytes(data)

with tempfile.TemporaryDirectory() as temp:
    temp = Path(temp)
    bootstrap, runtime = temp / 'CrawlOnline.dll', temp / 'CrawlOnline.Runtime.dll'
    linux_loader, windows_loader = temp / 'linux.zip', temp / 'windows.zip'
    bootstrap_bytes = managed_pe()
    runtime_bytes = managed_pe()
    bootstrap.write_bytes(bootstrap_bytes); runtime.write_bytes(runtime_bytes)
    with zipfile.ZipFile(linux_loader, 'w') as archive:
        archive.writestr('BepInEx/core/BepInEx.dll', b'BepInEx 5.4.11 Linux fixture')
        archive.writestr('run_bepinex.sh', '#!/bin/sh\n')
    with zipfile.ZipFile(windows_loader, 'w') as archive:
        archive.writestr('BepInEx/core/BepInEx.dll', b'BepInEx 5.4.11 Windows x86 fixture')
        archive.writestr('winhttp.dll', b'x86 doorstop fixture')
        archive.writestr('doorstop_config.ini', '[UnityDoorstop]\n')
    output = temp / 'out'
    environment = os.environ | {'CRAWL_ONLINE_BOOTSTRAP': str(bootstrap), 'CRAWL_ONLINE_RUNTIME': str(runtime)}
    subprocess.run([str(root / 'scripts/package-release.sh'), 'test-1', str(linux_loader), str(windows_loader), str(output)], check=True, env=environment, stdout=subprocess.PIPE, text=True)
    archive = output / 'CrawlOnline-test-1.zip'
    assert archive.is_file() and (output / 'CrawlOnline-test-1.zip.sha256').is_file()
    with zipfile.ZipFile(archive) as zipped:
        names = set(zipped.namelist())
        manifest = json.loads(zipped.read('CrawlOnline-test-1/CrawlOnline.release.json'))
    assert 'CrawlOnline-test-1/INSTALL.md' in names
    assert 'CrawlOnline-test-1/WINDOWS-VALIDATION.md' in names
    assert 'CrawlOnline-test-1/collect-diagnostics-windows.ps1' in names
    assert not any(name.endswith(('Assembly-CSharp.dll', 'steam_api.dll', 'Crawl.exe')) for name in names)
    assert manifest['plugins']['CrawlOnline.dll'] == hashlib.sha256(bootstrap_bytes).hexdigest()
    assert manifest['bepInEx']['linux-x64']['sha256'] == hashlib.sha256(linux_loader.read_bytes()).hexdigest()
    subprocess.run([str(auditor), str(archive), 'test-1'], check=True, stdout=subprocess.PIPE, text=True)

    simulation_archive = output / 'CrawlOnline-test-1-simulation.zip'
    simulation_plugin = runtime_bytes + b'DevSimulationSession'
    simulation_manifest = dict(manifest)
    simulation_manifest['plugins'] = dict(manifest['plugins'])
    simulation_manifest['plugins']['CrawlOnline.Runtime.dll'] = hashlib.sha256(simulation_plugin).hexdigest()
    with zipfile.ZipFile(archive) as source, zipfile.ZipFile(simulation_archive, 'w') as destination:
        for info in source.infolist():
            payload = source.read(info)
            if info.filename.endswith('/plugins/CrawlOnline.Runtime.dll'):
                payload = simulation_plugin
            elif info.filename.endswith('/CrawlOnline.release.json'):
                payload = json.dumps(simulation_manifest).encode()
            destination.writestr(info, payload)
    simulation_audit = subprocess.run([str(auditor), str(simulation_archive), 'test-1'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert simulation_audit.returncode != 0 and 'development simulation build cannot be released' in simulation_audit.stderr

    incompatible_archive = output / 'CrawlOnline-test-1-incompatible.zip'
    with zipfile.ZipFile(archive) as source, zipfile.ZipFile(incompatible_archive, 'w') as destination:
        incompatible_plugin = managed_pe(machine=0x8664)
        incompatible_manifest = dict(manifest)
        incompatible_manifest['plugins'] = dict(manifest['plugins'])
        incompatible_manifest['plugins']['CrawlOnline.Runtime.dll'] = hashlib.sha256(incompatible_plugin).hexdigest()
        for info in source.infolist():
            payload = source.read(info)
            if info.filename.endswith('/plugins/CrawlOnline.Runtime.dll'):
                payload = incompatible_plugin
            elif info.filename.endswith('/CrawlOnline.release.json'):
                payload = json.dumps(incompatible_manifest).encode()
            destination.writestr(info, payload)
    incompatible_audit = subprocess.run([str(auditor), str(incompatible_archive), 'test-1'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert incompatible_audit.returncode != 0 and 'Windows compatibility' in incompatible_audit.stderr

    with zipfile.ZipFile(archive, 'a') as mutated:
        mutated.writestr('CrawlOnline-test-1/proprietary.dll', b'never redistribute')
    audit = subprocess.run([str(auditor), str(archive), 'test-1'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert audit.returncode != 0 and 'unexpected package contents' in audit.stderr

    invalid_assemblies = {
        'x64': managed_pe(machine=0x8664),
        'native': windows_executable(),
        'mixed-mode': managed_pe(clr_flags=0),
        'native-entrypoint': managed_pe(clr_flags=0x11),
        'modern-runtime': managed_pe(runtime=b'v4.0.30319\0'),
    }
    for label, payload in invalid_assemblies.items():
        invalid_assembly = temp / f'{label}.dll'
        invalid_assembly.write_bytes(payload)
        compatibility = subprocess.run([
            'python3', str(compatibility_auditor),
            '--project', str(root / 'src/CrawlOnline.Bootstrap/CrawlOnline.Bootstrap.csproj'),
            '--assembly', str(invalid_assembly),
        ], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        assert compatibility.returncode != 0, label

    compatibility = subprocess.run([
        'python3', str(compatibility_auditor),
        '--project', str(root / 'src/CrawlOnline.Bootstrap/CrawlOnline.Bootstrap.csproj'),
        '--project', str(root / 'src/CrawlOnline/CrawlOnline.csproj'),
        '--assembly', str(bootstrap), '--assembly', str(runtime),
    ], check=True, stdout=subprocess.PIPE, text=True)
    compatibility_report = json.loads(compatibility.stdout)
    assert all(item['target'] == 'net35' for item in compatibility_report['projects'])
    assert all(item['machine'] == 'i386' and item['ilOnly'] for item in compatibility_report['assemblies'])

    incompatible_project = temp / 'Incompatible.csproj'
    incompatible_project.write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net35" Version="1.0.3" PrivateAssets="all" /></ItemGroup>
</Project>''')
    incompatible_project_result = subprocess.run([
        'python3', str(compatibility_auditor), '--project', str(incompatible_project),
        '--assembly', str(bootstrap),
    ], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert incompatible_project_result.returncode != 0 and 'target framework must be exactly net35' in incompatible_project_result.stderr

    game = temp / 'game'
    (game / 'Crawl_Data/Managed').mkdir(parents=True)
    (game / 'BepInEx/core').mkdir(parents=True)
    (game / 'BepInEx/plugins/OtherMod').mkdir(parents=True)
    (game / 'Crawl.x86_64').write_text('#!/bin/sh\n')
    (game / 'Crawl.x86_64').chmod(0o755)
    (game / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'legitimate test fixture')
    (game / 'BepInEx/core/BepInEx.dll').write_bytes(b'BepInEx 5.4.11 test fixture')
    (game / 'run_bepinex.sh').write_text('#!/bin/sh\n')
    (game / 'run_bepinex.sh').chmod(0o755)
    (game / 'BepInEx/plugins/OtherMod/keep.txt').write_text('keep')
    release_dir = output / 'CrawlOnline-test-1'
    installer = root / 'scripts/install-release-linux.sh'
    common = ['--game-dir', str(game), '--allow-unknown-game']

    unsupported = subprocess.run([str(installer), 'install', '--package', str(release_dir), '--game-dir', str(game)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert unsupported.returncode != 0 and 'Unsupported Crawl linux build' in unsupported.stderr
    assert not (game / 'BepInEx/plugins/CrawlOnline').exists()

    tampered_release = temp / 'tampered-release'
    shutil.copytree(release_dir, tampered_release)
    (tampered_release / 'plugins/CrawlOnline.Runtime.dll').write_bytes(b'tampered package payload')
    tampered_install = subprocess.run([str(installer), 'install', '--package', str(tampered_release), *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert tampered_install.returncode != 0 and 'Package hash mismatch' in tampered_install.stderr
    assert not (game / 'BepInEx/plugins/CrawlOnline').exists()

    install = subprocess.run([str(installer), 'install', '--package', str(release_dir), *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert install.returncode == 0, install.stdout + install.stderr
    native_state = json.loads((game / 'BepInEx/plugins/CrawlOnline/.crawl-online-install.json').read_text())
    assert native_state['platform'] == 'linux' and native_state['loader'] == 'linux-x64'
    diagnostic = subprocess.run([str(installer), 'diagnose', *common], check=True, stdout=subprocess.PIPE, text=True)
    assert diagnostic.stdout.count('verified') == 2
    installed = game / 'BepInEx/plugins/CrawlOnline/CrawlOnline.Runtime.dll'
    bootstrap_installed = game / 'BepInEx/plugins/CrawlOnline/CrawlOnline.dll'
    state_path = game / 'BepInEx/plugins/CrawlOnline/.crawl-online-install.json'
    legacy_bootstrap, legacy_runtime = b'legacy bootstrap', b'legacy runtime'
    bootstrap_installed.write_bytes(legacy_bootstrap)
    installed.write_bytes(legacy_runtime)
    state_path.write_text(json.dumps({
        'version': '0.1.0-alpha.1',
        'platform': 'linux',
        'loader': 'linux-x64',
        'plugins': {
            'CrawlOnline.dll': hashlib.sha256(legacy_bootstrap).hexdigest(),
            'CrawlOnline.Runtime.dll': hashlib.sha256(legacy_runtime).hexdigest(),
        },
    }))
    subprocess.run([str(installer), 'update', '--package', str(release_dir), *common], check=True, stdout=subprocess.PIPE, text=True)
    assert bootstrap_installed.read_bytes() == bootstrap_bytes
    assert installed.read_bytes() == runtime_bytes
    assert json.loads(state_path.read_text())['version'] == 'test-1'
    assert (game / 'BepInEx/plugins/OtherMod/keep.txt').read_text() == 'keep'

    before = (bootstrap_installed.read_bytes(), installed.read_bytes())
    rollback_environment = environment | {'CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE': '1'}
    rollback = subprocess.run([str(installer), 'update', '--package', str(release_dir), *common], env=rollback_environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert rollback.returncode == 97
    assert (bootstrap_installed.read_bytes(), installed.read_bytes()) == before
    assert json.loads(state_path.read_text())['version'] == 'test-1'
    installed.write_bytes(b'tampered')
    failed = subprocess.run([str(installer), 'diagnose', *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert failed.returncode != 0 and 'MISSING OR MODIFIED' in failed.stdout
    subprocess.run([str(installer), 'update', '--package', str(release_dir), *common], check=True, stdout=subprocess.PIPE, text=True)

    correct_state = state_path.read_text()
    mismatched_state = json.loads(correct_state)
    mismatched_state['platform'] = 'proton'
    state_path.write_text(json.dumps(mismatched_state))
    wrong_depot = subprocess.run([str(installer), 'diagnose', *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert wrong_depot.returncode != 0 and 'does not match the detected game depot' in wrong_depot.stderr
    state_path.write_text(correct_state)

    subprocess.run([str(installer), 'uninstall', *common], check=True, stdout=subprocess.PIPE, text=True)
    assert not (game / 'BepInEx/plugins/CrawlOnline').exists()
    assert (game / 'BepInEx/plugins/OtherMod/keep.txt').read_text() == 'keep'

    (game / 'Crawl.exe').write_bytes(windows_executable())
    ambiguous = subprocess.run([str(installer), 'diagnose', *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert ambiguous.returncode != 0 and 'Both Linux and Windows' in ambiguous.stderr
    forced_linux = subprocess.run([str(installer), 'diagnose', '--platform', 'linux', *common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert forced_linux.returncode == 0

    proton = temp / 'proton-game'
    (proton / 'Crawl_Data/Managed').mkdir(parents=True)
    (proton / 'BepInEx/plugins/OtherMod').mkdir(parents=True)
    (proton / 'BepInEx/config').mkdir(parents=True)
    (proton / 'Crawl.exe').write_bytes(windows_executable())
    (proton / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'legitimate Windows test fixture')
    (proton / 'BepInEx/plugins/OtherMod/keep.txt').write_text('keep')
    (proton / 'BepInEx/config/BepInEx.cfg').write_text('[Logging.Console]\nEnabled = false\n\n[Preloader.Entrypoint]\nAssembly = UnityEngine.dll\nType = Application\nMethod = .cctor\n')
    proton_common = ['--game-dir', str(proton), '--allow-unknown-game']
    fake_bin = temp / 'bin'
    fake_bin.mkdir()
    fake_curl = fake_bin / 'curl'
    fake_curl.write_text("""#!/bin/sh
out=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = '-o' ]; then out="$2"; shift 2; else shift; fi
done
cp "$CRAWL_ONLINE_TEST_DOWNLOAD" "$out"
""")
    fake_curl.chmod(0o755)
    proton_environment = os.environ | {
        'PATH': str(fake_bin) + os.pathsep + os.environ['PATH'],
        'CRAWL_ONLINE_TEST_DOWNLOAD': str(windows_loader),
    }
    proton_install = subprocess.run([str(installer), 'install', '--package', str(release_dir), *proton_common], env=proton_environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert proton_install.returncode == 0, proton_install.stdout + proton_install.stderr
    proton_state = json.loads((proton / 'BepInEx/plugins/CrawlOnline/.crawl-online-install.json').read_text())
    assert proton_state['platform'] == 'proton' and proton_state['loader'] == 'win-x86'
    proton_config = (proton / 'BepInEx/config/BepInEx.cfg').read_text()
    assert 'Assembly = Assembly-CSharp.dll' in proton_config
    assert 'Type = SystemSteam' in proton_config
    assert 'Method = Awake' in proton_config
    assert '[Logging.Console]\nEnabled = false' in proton_config
    proton_diagnostic = subprocess.run([str(installer), 'diagnose', *proton_common], check=True, stdout=subprocess.PIPE, text=True)
    assert proton_diagnostic.stdout.count('verified') == 2 and 'win-x86 entrypoint: present' in proton_diagnostic.stdout
    assert 'late Crawl entrypoint: configured' in proton_diagnostic.stdout
    assert 'WINEDLLOVERRIDES="winhttp=n,b" %command%' in proton_diagnostic.stdout
    assert (proton / 'winhttp.dll').is_file() and not (proton / 'run_bepinex.sh').exists()
    bad_config = '[Preloader.Entrypoint]\nAssembly = UnityEngine.dll\nType = Application\nMethod = .cctor\n'
    (proton / 'BepInEx/config/BepInEx.cfg').write_text(bad_config)
    bad_entrypoint = subprocess.run([str(installer), 'diagnose', *proton_common], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert bad_entrypoint.returncode != 0 and 'MISSING OR INCORRECT' in bad_entrypoint.stdout
    failed_proton_update = subprocess.run([str(installer), 'update', '--package', str(release_dir), *proton_common], env=proton_environment | {'CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE': '1'}, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert failed_proton_update.returncode == 97
    assert (proton / 'BepInEx/config/BepInEx.cfg').read_text() == bad_config
    subprocess.run([str(installer), 'update', '--package', str(release_dir), *proton_common], check=True, stdout=subprocess.PIPE, text=True)
    subprocess.run([str(installer), 'uninstall', *proton_common], check=True, stdout=subprocess.PIPE, text=True)
    assert not (proton / 'BepInEx/plugins/CrawlOnline').exists()
    assert (proton / 'BepInEx/plugins/OtherMod/keep.txt').read_text() == 'keep'

    invalid = temp / 'invalid-proton'
    (invalid / 'Crawl_Data/Managed').mkdir(parents=True)
    (invalid / 'Crawl.exe').write_bytes(windows_executable(0x8664))
    (invalid / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'fixture')
    invalid_result = subprocess.run([str(installer), 'diagnose', '--game-dir', str(invalid), '--allow-unknown-game'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert invalid_result.returncode != 0 and 'Windows x86' in invalid_result.stderr

    wrong_loader = temp / 'wrong-loader'
    (wrong_loader / 'Crawl_Data/Managed').mkdir(parents=True)
    (wrong_loader / 'BepInEx/core').mkdir(parents=True)
    (wrong_loader / 'Crawl.exe').write_bytes(windows_executable())
    (wrong_loader / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'fixture')
    (wrong_loader / 'BepInEx/core/BepInEx.dll').write_bytes(b'BepInEx 5.4.11 fixture')
    (wrong_loader / 'run_bepinex.sh').write_text('#!/bin/sh\n')
    wrong = subprocess.run([str(installer), 'install', '--package', str(release_dir), '--game-dir', str(wrong_loader), '--allow-unknown-game'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert wrong.returncode != 0 and 'lacks the win-x86 entrypoint' in wrong.stderr
    assert not (wrong_loader / 'BepInEx/plugins/CrawlOnline').exists()

    wrong_version = temp / 'wrong-version-loader'
    (wrong_version / 'Crawl_Data/Managed').mkdir(parents=True)
    (wrong_version / 'BepInEx/core').mkdir(parents=True)
    (wrong_version / 'Crawl.exe').write_bytes(windows_executable())
    (wrong_version / 'Crawl_Data/Managed/Assembly-CSharp.dll').write_bytes(b'fixture')
    (wrong_version / 'BepInEx/core/BepInEx.dll').write_bytes(b'BepInEx 6 incompatible fixture')
    (wrong_version / 'winhttp.dll').write_bytes(b'x86 doorstop fixture')
    wrong_version_result = subprocess.run([str(installer), 'install', '--package', str(release_dir), '--game-dir', str(wrong_version), '--allow-unknown-game'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    assert wrong_version_result.returncode != 0 and 'not 5.4.11' in wrong_version_result.stderr
    assert not (wrong_version / 'BepInEx/plugins/CrawlOnline').exists()
print('release package smoke test passed')

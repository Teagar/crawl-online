# Installing Crawl Online

> **Experimental:** this installs the mod loader and Crawl Online only. It does not
> make multiplayer gameplay end-to-end validated. Every player needs their own
> legitimate Steam copy of Crawl.

1. Download the `CrawlOnline-<version>.zip` asset from the project's GitHub release
   page. Do **not** unpack or edit DLLs yourself.
2. Extract the ZIP without changing its contents, then close Crawl and Steam's game process.
3. Run one command below. The installer verifies the plugin hashes recorded inside the release package, the
   BepInEx 5.4.11 download hash, and the installed Crawl gameplay-assembly hash
   before changing files. An unknown game build stops safely; do not bypass that
   check unless you have reviewed the update.

The BepInEx console identifies this prerelease with the loader-compatible numeric
version `0.2.0`; Crawl Online diagnostics and Steam lobby metadata use the full
release version `0.2.0-alpha.1`.

## Windows (32-bit Crawl)

Open PowerShell in the extracted release folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-release-windows.ps1 install -Package .
```

If Crawl is in a non-standard Steam library, add
`-GameDir 'D:\SteamLibrary\steamapps\common\Crawl'`. Start Crawl normally through
Steam afterwards.

## Linux (native or Proton)

From the extracted release folder:

```bash
chmod +x install-release-linux.sh
./install-release-linux.sh install --package .
```

For a non-standard library add `--game-dir /path/to/Crawl`. The installer detects
either native `Crawl.x86_64` or Windows x86 `Crawl.exe` running through Proton,
validates the platform-specific assembly hash, and selects the matching pinned
BepInEx package:

- Native Linux uses `linux-x64` and prints the one-time Steam launch option
  `./run_bepinex.sh ./Crawl.x86_64 # %command%`.
- Proton uses the Windows `win-x86` Doorstop/BepInEx package. Start Crawl normally
  through Steam; no Linux wrapper launch option is required.

If both executables exist in one directory, auto-detection fails without changing
files. Review the directory and explicitly pass `--platform linux` or
`--platform proton`. An existing BepInEx installation must contain the matching
entrypoint (`run_bepinex.sh` or `winhttp.dll`); the installer will not overlay an
incompatible loader or disturb other plugins.

## Updating, removing, and diagnosis

Download and extract the new ZIP, then replace `install` with `update`. Existing Crawl Online DLLs
are replaced only after the new package is verified. Other BepInEx plugins are left
alone.

```powershell
.\install-release-windows.ps1 diagnose
.\install-release-windows.ps1 uninstall
```

```bash
./install-release-linux.sh diagnose
./install-release-linux.sh uninstall
```

Linux diagnosis records and verifies whether the installation targets native
Linux or Proton. Use the same `--game-dir` and, for an intentionally mixed
directory, the same explicit `--platform` used during installation.

Uninstall deletes only `BepInEx/plugins/CrawlOnline/CrawlOnline*.dll` and its small
Crawl Online state file. It deliberately preserves BepInEx and all other plugins;
remove BepInEx separately only if you installed no other BepInEx mods and no longer
want its loader.

## For release publishers

Build first, then create the release ZIP and its checksum without including Crawl,
Steam, or BepInEx files. Packaging performs a fail-closed allowlist audit of the
ZIP, manifest, and plugin hashes before writing its checksum:

```bash
dotnet build -c Release
./scripts/package-release.sh <version> /path/BepInEx_unix_5.4.11.0.zip /path/BepInEx_x86_5.4.11.0.zip
```

For example, `v0.2.0-alpha.1` is a local candidate name only until a human
reviews it and publishes it. Creating a package never creates a Git tag or
GitHub release, and is not evidence of remote multiplayer validation.

Upload the generated ZIP and `.sha256` as release assets. The package records hashes
of its two Crawl Online DLLs and of the upstream BepInEx archives; installers download
the latter directly from BepInEx and verify it before extraction.

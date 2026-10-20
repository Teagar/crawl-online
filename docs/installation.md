# Installing Crawl Online

> [!WARNING]
> **Experimental build.** Two legitimate Steam accounts have played through the
> first floor on one Linux PC through Proton. A complete campaign, native Windows,
> separate computers/networks, reconnect, and leave have not been validated yet.
> Expect bugs. Every participant must own and run a legitimate Steam copy of Crawl.

This package contains only Crawl Online and its installers. It does not contain
Crawl, Steam files, game assets, or BepInEx. The installer downloads the pinned
official BepInEx release and verifies it before installation.

## Before you start — every player

1. Install **Crawl from Steam**.
2. Start Crawl once, check that the main menu shows **v1.0.1**, then close it.
3. Open the [Crawl Online releases page](https://github.com/Teagar/crawl-online/releases).
4. Download `CrawlOnline-0.2.0-alpha.1.zip`. The `.sha256` file is optional and
   allows advanced users to verify the download.
5. Extract the ZIP. Open the extracted `CrawlOnline-0.2.0-alpha.1` folder—the one
   containing `INSTALL.md` and the installer scripts.
6. Close Crawl before installing. Steam itself may remain open.

Do not copy DLLs manually. The installer verifies the mod files, BepInEx 5.4.11,
the 32-bit executable, and the exact supported Crawl gameplay assembly before it
changes anything. Unsupported builds stop without installing.

The BepInEx console identifies this prerelease with the loader-compatible numeric
version `0.2.0`; Crawl Online diagnostics and Steam lobby metadata use the full
release version `0.2.0-alpha.1`.

## Windows (32-bit Crawl)

Native Windows has not yet been runtime-validated by the project, so this path is
provided for experimental testers.

1. In File Explorer, open the extracted release folder.
2. Click the address bar, type `powershell`, and press Enter.
3. Paste these commands:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-release-windows.ps1 install -Package .
```

4. A successful installation ends with `Crawl Online installed`.
5. Start Crawl normally from Steam. No launch option is required on Windows.

If Steam is installed in another library, specify the Crawl folder:

```powershell
.\install-release-windows.ps1 install -Package . -GameDir 'D:\SteamLibrary\steamapps\common\Crawl'
```

If PowerShell reports an unsupported build, open Crawl and confirm that it is the
Windows **v1.0.1** release. Do not bypass the hash check.

## Linux (Windows 1.0.1 through Proton)

Native Linux Crawl 1.0.3 is not supported by this release. Linux users must run
the Windows v1.0.1 build through Proton.

1. In Steam, right-click **Crawl → Properties → Compatibility**.
2. Enable **Force the use of a specific Steam Play compatibility tool** and choose
   Proton 10.0 (the tested version) or a current official Proton version.
3. Start Crawl once. Confirm that the menu shows **v1.0.1**, then close it.
4. In **Properties → General → Launch Options**, paste exactly:

   ```text
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

5. Open a terminal inside the extracted Crawl Online folder and run:

```bash
chmod +x install-release-linux.sh
./install-release-linux.sh install --package .
```

6. A successful installation says that Crawl Online was installed for Windows
   v1.0.1 through Proton. Start Crawl from Steam.

For a non-standard Steam library, pass the full Crawl folder:

```bash
./install-release-linux.sh install --package . \
  --game-dir "$HOME/path/to/SteamLibrary/steamapps/common/Crawl"
```

The installer also configures BepInEx to start late at `Assembly-CSharp.dll` /
`SystemSteam.Awake`. Without the `winhttp` override the mod does not load; using
BepInEx's default early entrypoint can leave Crawl on a black screen.

If both executables exist in one directory, auto-detection fails without changing
files. Use a clean Windows 1.0.1 depot directory. An existing BepInEx installation
must contain the matching `winhttp.dll` entrypoint; the installer will not overlay
an incompatible loader or disturb other plugins.

## Starting or joining a game

Every participant must install the **same Crawl Online release** and run the same
Crawl Windows v1.0.1 build. Participants should be Steam friends.

### Host

1. Start Crawl from Steam and stay at the main menu.
2. Select **ONLINE**.
3. Select **HOST GAME**.
4. After the lobby is created, select **INVITE FRIENDS** and invite the other player.
5. Return to Crawl and start the game normally after the status strip reports both
   players. Slot 0 is the host.

### Guest

1. Accept the Steam invitation. Steam should open Crawl and join automatically.
2. Alternatively, start Crawl, choose **ONLINE → JOIN FRIEND**, and select the
   available friend's lobby.
3. Wait for the upper-left status text to report **CONNECTED** before gameplay.

Lobby shortcuts remain available: `F8` hosts, `F7` opens Steam invitations, `F9`
leaves, and `F5` shows diagnostic details. Hosting and joining are intentionally
blocked after gameplay has started; return to the main menu first.

## What currently works

- Steam lobby creation, invitations, authentication, and slot assignment;
- synchronized map generation using only each player's local game assets;
- remote input, enemies, combat, hero rotation, and room transitions;
- completing the first floor and reaching the upgrade intermission.

The build is not considered stable. Progress beyond the first intermission, a
complete campaign, reconnect/leave, native Windows, and different computers or
networks still need validation. Report the exact step that failed, but never post
Steam IDs, lobby IDs, IP addresses, saves, or raw logs publicly.

## Updating, removing, and diagnosis

Download and extract the new ZIP, then replace `install` with `update`. Existing Crawl Online DLLs
are replaced only after the new package is verified. Other BepInEx plugins are left
alone.

```powershell
.\install-release-windows.ps1 diagnose
.\collect-diagnostics-windows.ps1
.\install-release-windows.ps1 uninstall
```

```bash
./install-release-linux.sh diagnose
./install-release-linux.sh uninstall
```

Linux diagnosis verifies that the installation targets the Windows 1.0.1 depot
through Proton, checks the required late BepInEx entrypoint, and prints the Proton
launch option.

On Windows, `diagnose` reports the executable architecture, known game build,
loader version, Doorstop presence, late entrypoint, plugin integrity, and log
availability. `collect-diagnostics-windows.ps1` creates a reviewable ZIP containing
only generated text and a sanitized BepInEx log excerpt. It excludes binaries,
saves, raw configuration, credentials, user paths, long Steam/lobby IDs, and
network addresses. Follow `WINDOWS-VALIDATION.md` and review the text before sharing.

### Common problems

- **ONLINE is missing:** close Crawl, run `diagnose`, and confirm BepInEx and its
  late entrypoint are present. On Linux, re-check the `WINEDLLOVERRIDES` launch option.
- **Black screen on Linux:** use the official Windows build through Proton and let
  the installer configure `Assembly-CSharp.dll / SystemSteam.Awake`; do not replace
  it with BepInEx's default early entrypoint.
- **Unsupported Crawl build:** this package accepts only Windows v1.0.1. Native
  Linux v1.0.3 is intentionally rejected.
- **Friends cannot see the lobby:** verify both players use the same mod release,
  the same game build, are Steam friends, and are at Crawl's main menu.
- **Need support:** run `diagnose`. On Windows, run the sanitized diagnostics
  collector and review its text output before sharing it.

Uninstall deletes only `BepInEx/plugins/CrawlOnline/CrawlOnline*.dll` and its small
Crawl Online state file. It deliberately preserves BepInEx and all other plugins;
remove BepInEx separately only if you installed no other BepInEx mods and no longer
want its loader.

## For release publishers

Build first, then create the release ZIP and its checksum without including Crawl,
Steam, or BepInEx files. Packaging performs a fail-closed allowlist audit of the
ZIP, manifest, plugin hashes, project targets, restored dependencies, and PE/CLR
headers before writing its checksum. Both plugins must remain PE32 i386, IL-only,
target `.NET Framework 3.5`, and identify the CLR 2.0–3.5 runtime:

```bash
dotnet build -c Release
./scripts/package-release.sh <version> /path/BepInEx_x86_5.4.11.0.zip
```

The compatibility gate can also be run directly on local build outputs with
`scripts/audit-windows-compatibility.py`. It verifies static compatibility with
the 32-bit Windows host; it does not replace execution on native Windows.

Development builds compiled with local simulation support are intentionally
invalid release inputs and are rejected by the package auditor. See
[the local simulation guide](local-simulation.md); never upload or distribute
those assemblies.

Creating a package never creates a Git tag or GitHub release and is not evidence
of complete or cross-machine multiplayer validation.

Upload the generated ZIP and `.sha256` as release assets. The package records hashes
of its two Crawl Online DLLs and of the upstream BepInEx archives; installers download
the latter directly from BepInEx and verify it before extraction.

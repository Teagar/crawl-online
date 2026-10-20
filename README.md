# Crawl Online

Experimental online multiplayer mod for Powerhoof's **Crawl**.

The goal is real netplay: every participant owns, runs, and renders their own copy of the game. Video streaming is not part of the architecture.

> [!WARNING]
> This is an experimental playable build, not a finished release. Two legitimate
> Steam accounts have connected and played through the first floor on one Linux
> PC using Proton. A complete match, native Windows, separate computers/networks,
> reconnect, and leave flows have not yet been validated.

## Confirmed game architecture

- Unity `5.4.2f2`, Mono scripting backend
- gameplay code in `Crawl_Data/Managed/Assembly-CSharp.dll`
- Steamworks.NET already shipped by the game
- legacy Steam P2P relay, lobby, and invitation APIs are available
- centralized player input through `PlayerData` and `SystemInput`

No proprietary game binaries or decompiled source are committed to this repository.

## Experimental player experience

1. Install the same Crawl Online release against the Windows 1.0.1 depot, either
   on native Windows or on Linux through Proton.
2. Start Crawl normally through Steam.
3. Open **ONLINE** and choose **HOST GAME**.
4. Choose **INVITE FRIENDS** to open Steam's invitation dialog.
5. Friends accept the invitation, or use **ONLINE → JOIN FRIEND**.
6. The host starts and configures the game. The client's assigned local keyboard
   is already live while its HUD reports the host's pre-game phase.
7. The mod starts the client from the authoritative map plan, then synchronizes
   input and corrective snapshots over Steam relay.

## Online menu, HUD and diagnostic controls

Use the native **ONLINE** main-menu entry to host or join. Outside that menu,
the passive upper-left status text appears only for an active connection or a
recoverable error. It reports role, connected slots (slot 0 is the host),
connection state, and errors without displaying Steam or lobby IDs. `F5` can
temporarily expand diagnostic details; they are not shown permanently.

- `F8` — create a friends-only lobby as host
- `F7` — open the Steam invite dialog while in a lobby
- `F9` — leave the current lobby
- `F5` — show/hide expanded diagnostic details
- `F6` — minimize/restore those details

`F8` and invite/join paths are refused during a campaign or scene transition
with guidance to return to the main menu; no lobby operation is started. The
HUD has no clickable controls, does not modify simulation, and does not consume
mouse gameplay input. This UI is not evidence of completed release or
cross-machine peer validation.

## Install a release

See the step-by-step [installation guide](docs/installation.md) for verified install,
update, uninstall, and diagnostics on Windows x86 and on Linux using the same
Windows 1.0.1 depot through Proton. Native Linux 1.0.3 support is paused. Release
packages contain only Crawl Online; the installer fetches the pinned x86 BepInEx
5.4.11 loader directly from upstream and never includes Crawl or Steam files.

## Build

Requirements:

- .NET SDK 8 or newer (build tooling only)
- a legitimate Steam installation of Crawl
- BepInEx 5.4.11 files staged under `artifacts/bepinex`

Linux/Proton example:

```bash
export CRAWL_GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Crawl"
./scripts/fetch-bepinex.sh win-x86
dotnet build -c Release
dotnet test -c Release
./scripts/install-release-linux.sh install --package /path/to/extracted-candidate --platform proton
```

Set the game's Steam launch option to:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Windows PowerShell example:

```powershell
$env:CRAWL_GAME_DIR = "C:\Program Files (x86)\Steam\steamapps\common\Crawl"
./scripts/fetch-bepinex.ps1
dotnet build -c Release
dotnet test -c Release
./scripts/install-dev-windows.ps1 -GameDir $env:CRAWL_GAME_DIR
```

No Steam launch option is required on Windows. The installer scripts preserve
other BepInEx plugins when uninstalling Crawl Online.

See [docs/architecture.md](docs/architecture.md),
[docs/determinism.md](docs/determinism.md), and
[docs/research.md](docs/research.md). The authenticated lobby handshake is in
[docs/session-protocol.md](docs/session-protocol.md). Contributor-facing runtime findings are
collected in [docs/modding-notes.md](docs/modding-notes.md).
The authoritative state wire format and ordering rules are documented in
[docs/authoritative-sync.md](docs/authoritative-sync.md).
The single-machine protocol harness, development-only ghost peers, reproducible
seeds and explicit non-coverage are documented in
[docs/local-simulation.md](docs/local-simulation.md).

## Legal boundary

This is an independent interoperability mod. Users must own Crawl. Installers will download the mod loader and copy only original Crawl Online binaries; they will not redistribute Crawl assets, assemblies, or Steam libraries.

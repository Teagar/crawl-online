# Crawl Online

Experimental cross-platform online multiplayer mod for Powerhoof's **Crawl**.

The goal is real netplay: every participant owns, runs, and renders their own copy of the game. Video streaming is not part of the architecture.

> [!WARNING]
> This project is an early compatibility and determinism prototype. It is not a playable release yet.

## Confirmed game architecture

- Unity `5.4.2f2`, Mono scripting backend
- gameplay code in `Crawl_Data/Managed/Assembly-CSharp.dll`
- Steamworks.NET already shipped by the game
- legacy Steam P2P relay, lobby, and invitation APIs are available
- centralized player input through `PlayerData` and `SystemInput`

No proprietary game binaries or decompiled source are committed to this repository.

## Planned player experience

1. Install the same Crawl Online release on Windows or Linux.
2. Start Crawl normally through Steam.
3. The host presses `F8` to create a private lobby.
4. The host presses `F7` to open the Steam invitation dialog.
5. Friends accept and run their own local copies.
6. The mod synchronizes input and authoritative corrections over Steam relay.

## Build

Requirements:

- .NET SDK 8 or newer (build tooling only)
- a legitimate Steam installation of Crawl
- BepInEx 5.4.11 files staged under `artifacts/bepinex`

Linux example:

```bash
export CRAWL_GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Crawl"
./scripts/fetch-bepinex.sh linux-x64
dotnet build -c Release
dotnet test -c Release
./scripts/install-dev-linux.sh
```

Set the game's Steam launch option to:

```text
./run_bepinex.sh ./Crawl.x86_64 # %command%
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

## Legal boundary

This is an independent interoperability mod. Users must own Crawl. Installers will download the mod loader and copy only original Crawl Online binaries; they will not redistribute Crawl assets, assemblies, or Steam libraries.

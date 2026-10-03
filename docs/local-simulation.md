# Local multiplayer simulation

The local simulator is a contributor tool for exercising Crawl Online's real
session protocol with one machine and one Steam account. It has two layers:

- a headless harness that does not start Crawl or Steam;
- an explicitly gated development build that shows three ghost peers in the
  Windows 1.0.1 runtime while remaining on the main menu.

Neither layer is evidence of remote Steam multiplayer. Synthetic endpoint IDs
exist only inside the process and are never submitted to Steamworks.

## Reproduce the headless matrix

Run only the simulator scenarios:

```bash
dotnet test tests/CrawlOnline.Protocol.Tests/CrawlOnline.Protocol.Tests.csproj \
  -c Release --filter FullyQualifiedName~HeadlessSessionHarnessTests
```

Run the complete protocol suite:

```bash
dotnet test CrawlOnline.slnx -c Release
```

The checked-in scenarios use these stable seeds:

| Seed | Scenario |
| ---: | --- |
| `1001` | host plus three peers, unique slots, inputs, reliable edges and snapshots |
| `1002` | full lobby and invalid build, nonce and transport identity |
| `1003` | loss, latency, jitter, duplication, reordering, corruption and bounded queues |
| `1004` | peer drop and authenticated reconnect to its original slot |
| `1005` | authoritative host leaving all clients |
| `1006` | requested slot remains unauthorized until host acceptance |
| `777` | identical script/report reproduction check |
| `424242` | intentional failure-report check |

`HeadlessSessionScenarioException` includes the seed, current tick, a bounded
128-entry timeline and the last roster/session state. A failed scenario can
therefore be rerun without recording a raw runtime transcript.

## Build the runtime simulator

The normal build does not contain `DevSimulationSession` and does not read its
configuration key. A runtime simulator requires both gates:

1. compile with `EnableLocalSimulation=true`;
2. set `Development.EnableLocalSimulation=true` in the local BepInEx config.

Example build, using a legitimate local Crawl installation for references:

```bash
export CRAWL_GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Crawl"
dotnet build CrawlOnline.slnx -c Release --no-restore \
  -p:EnableLocalSimulation=true \
  -p:BaseOutputPath=/tmp/opencode/crawl-online-simulation-build/
```

Copy the resulting `net35/CrawlOnline.dll` and
`net35/CrawlOnline.Runtime.dll` only into an already reversible development
installation, then create this local configuration:

```ini
[Development]
EnableLocalSimulation = true
```

Do not package or distribute those assemblies. `audit-release-package.py`
rejects simulation-enabled bootstrap and runtime markers. A normal release
build remains the only valid input to `package-release.sh`.

## Runtime controls and safety boundary

On a simulation-enabled build:

1. confirm the title screen reports the intended Crawl build;
2. open **ONLINE**;
3. choose **START SIMULATION**;
4. verify the lower-left `SIMULATION` watermark and `4/4` count;
5. press `F7` to drop one ghost (`3/4`) and again to reconnect it (`4/4`);
6. press `F9` or select **CANCEL** to stop.

The simulation adapter replaces the Steam session before any
`SteamLobbySession` is constructed. It removes **JOIN FRIEND**, never calls
Steam lobby, overlay, discovery or P2P APIs, and does not create fake SteamIDs.
It also omits the gameplay-world synchronizer and stops if the main-menu safety
scope is lost. Ghosts are protocol/roster participants visible in the HUD; they
are deliberately not campaign avatars and do not touch saves or achievements.

The runtime seed is `293780`. Runtime logs identify only synthetic peers `2`–`4`
and general state. Do not add real account, lobby, network-address, credential,
save or filesystem identity data to simulator reports.

## Coverage and limits

| Area | Locally covered | Not established by the simulator |
| --- | --- | --- |
| Session security | nonce/build/capability checks, transport-sender identity, membership, slots, stale attempts | Steam account authentication or malicious traffic through Steam relay |
| Gameplay protocol | continuous inputs, reliable button edges, snapshots, acknowledgements and sequence windows | complete Crawl campaign behavior or every game subsystem |
| Network behavior | deterministic latency, jitter, loss, duplication, reordering, corruption, bandwidth and disconnects | NAT traversal, relay behavior, Internet routing or Steam service outages |
| Lifecycle | full lobby, peer drop/reconnect, host-left and bounded cleanup | prolonged stability across real machines |
| Runtime UI | Windows 1.0.1 binary under Proton 10: menu, watermark and live `4/4 → 3/4 → 4/4` roster | native Windows execution or native-Linux gameplay compatibility |
| Distribution | normal/dev build separation and fail-closed release audit | permission to publish an alpha without remote evidence |

Release `v0.2.0-alpha.1` remains blocked until a legitimate second Steam
identity on another machine validates invitation/join, relay transport,
authenticated gameplay and clean leave. Native Windows validation remains a
separate gate. Do not describe a passing local simulation as remote multiplayer,
cross-platform gameplay or campaign completion.

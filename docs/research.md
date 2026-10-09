# Reverse-engineering notes

Historical research began against a Linux Steam installation. Native Linux 1.0.3
is now paused; current development and validation target only the legitimate
Windows 1.0.1 depot. Decompiled files are kept outside the repository.

## Compatibility fingerprint

```text
Unity: 5.4.2f2
Assembly-CSharp.dll SHA-256:
d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84

Assembly-CSharp-firstpass.dll SHA-256:
429f80051a305c4aef375fcaf0fd91d91bb9ffd381128206f05220a527ea53d5
```

Windows Steam depot `293781`, public manifest `9207527406397102173`:

```text
Crawl.exe: PE32 Intel i386 (32-bit)
Assembly-CSharp.dll SHA-256:
e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e

Assembly-CSharp-firstpass.dll SHA-256:
32fd784ce5bdb3d11dfba721cc263d92f8b2700d5f57331d16236ee5b737612a
```

These hashes identify researched builds; they are not game files and are safe to
publish. Only the Windows 1.0.1 hash is accepted by the current candidate.

## Findings

- `SystemSteam` initializes Steam App ID `293780` and pumps callbacks each update.
- the bundled Steamworks layer exposes lobby callbacks and legacy relay-capable P2P networking;
- `PlayerData` funnels human movement and Action/Alt/Start button state through `SystemInput`;
- there are four controller and four keyboard slots;
- gameplay and visual systems share `UnityEngine.Random` calls;
- movement and AI use `Time.fixedDeltaTime` heavily.

## Immediate experiment

1. Load a BepInEx plugin without modifying game assemblies.
2. Establish a Steam lobby and authenticated P2P handshake.
3. Record local inputs with fixed tick numbers.
4. Replay identical inputs in two instances.
5. Hash critical state and record the first divergent tick.

## Loader compatibility result

- BepInEx `5.4.23.2` is incompatible with Crawl's old Mono runtime: its
  preloader fails while applying `HarmonyInteropFix`.
- BepInEx `5.4.11.0` loads when optional runtime patches are disabled.
- Loading the chainloader from `UnityEngine.Application..cctor` is too early
  and corrupts Unity 5.4's script serialization map.
- Loading from `Assembly-CSharp.dll:SystemSteam.Awake` starts after script
  metadata is ready and avoids missing-behaviour/serialization errors.
- Both plugin assemblies target .NET Framework 3.5; targeting 4.5 references
  methods absent from the game's bundled `mscorlib`.
- The earlier Linux-native launch is historical evidence only and is not part of
  the current support or release matrix.

Windows packaging uses the matching x86 BepInEx 5.4.11 Doorstop build. The
public Windows depot was exercised under Proton with Wine's native `winhttp`
override: BepInEx reported `System platform: Windows`, accepted the Windows
assembly fingerprint, loaded the runtime, and produced zero missing-script
errors. Human validation on native Windows hardware remains part of the card
review and eventual release gate.

The authoritative session protocol has historical Linux host evidence, now
outside the active baseline. The official Windows 1.0.1 binary repeated creation
and leave under Proton through the Windows BepInEx path. A real Windows 1.0.1
peer join cannot be claimed from a single Steam account and remains pending
two-machine validation.

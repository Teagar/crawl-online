# Architecture

## Product boundary

Every peer runs and renders a local, legitimate Crawl installation. Steam provides lobby discovery, invitations, identity, NAT traversal, and packet relay. Crawl Online does not transmit video.

## Selected simulation model

The lobby owner is authoritative. Peers send timestamped/numbered input frames;
the owner advances gameplay and sends periodic state snapshots plus corrections.
Clients predict presentation where safe and reconcile critical state to the
owner. Steam P2P relay remains the transport.

- inputs preserve movement plus held/pressed/released button masks;
- packets carry monotonic sequence/tick identifiers;
- snapshots cover stable player, room, enemy, RNG, and transition state;
- clients acknowledge snapshots so the host can bound correction history;
- state hashes remain diagnostics for detecting and localizing drift.

Delayed-input lockstep was rejected by measurement. A clean trace-version-2
record/replay pair used identical quantized input semantics and diverged in both
exact and quantized critical state at the first checkpoint, logical frame 30.
Every compared checkpoint diverged. This satisfies the predeclared fallback
condition for host authority; lockstep is not a release path for this game build.

## Transport

Crawl already ships Steamworks.NET APIs required by the mod:

- `SteamMatchmaking.CreateLobby` and `JoinLobby`;
- Steam overlay invitations;
- `SteamNetworking.SendP2PPacket`;
- Valve P2P relay via `AllowP2PPacketRelay(true)`.

Protocol packets carry a magic value and explicit version. P2P requests are accepted only from current lobby members.

Application handshake adds a per-lobby random nonce, Steam sender identity,
monotonic connection attempt, exact verified game-assembly fingerprint,
capability negotiation, and host-assigned slot.
Opening a Steam P2P transport does not authenticate gameplay state by itself.
See [session-protocol.md](session-protocol.md).

## Injection points

Player input is centralized:

```text
PlayerData.GetInput* -> SystemInput.Input*
```

This is the preferred Harmony patch boundary because gameplay code asks `PlayerData` for movement and buttons rather than reading devices directly in most paths.

## Measured determinism constraints

- gameplay uses `UnityEngine.Random` extensively;
- cosmetic effects also consume `UnityEngine.Random`, potentially at frame-dependent rates;
- movement integrates with `Time.fixedDeltaTime`;
- coroutines and physics callback ordering can differ between machines;
- user unlock state influences selectable content.

These risks are no longer assumptions supporting a lockstep proposal: the
harness observed critical state divergence under equal nominal render settings.
The exact first subsystem to diverge may still be diagnosed, but it does not
change the authoritative-host decision.

The protocol must also negotiate game assembly hashes, mod version, unlock policy, and fixed simulation settings before starting a match.

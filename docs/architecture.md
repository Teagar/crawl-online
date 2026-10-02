# Architecture

## Product boundary

Every peer runs and renders a local, legitimate Crawl installation. Steam provides lobby discovery, invitations, identity, NAT traversal, and packet relay. Crawl Online does not transmit video.

## Proposed simulation model

The initial experiment uses delayed input lockstep:

- simulation advances at a fixed tick;
- peers exchange compact input frames;
- inputs are buffered for a small configurable delay;
- peers calculate periodic deterministic state hashes;
- the lobby owner detects divergence.

The release architecture will only retain pure lockstep if the determinism harness proves it stable. Otherwise, the lobby owner becomes authoritative and sends corrective snapshots. This decision is deliberately evidence-driven.

## Transport

Crawl already ships Steamworks.NET APIs required by the mod:

- `SteamMatchmaking.CreateLobby` and `JoinLobby`;
- Steam overlay invitations;
- `SteamNetworking.SendP2PPacket`;
- Valve P2P relay via `AllowP2PPacketRelay(true)`.

Protocol packets carry a magic value and explicit version. P2P requests are accepted only from current lobby members.

## Injection points

Player input is centralized:

```text
PlayerData.GetInput* -> SystemInput.Input*
```

This is the preferred Harmony patch boundary because gameplay code asks `PlayerData` for movement and buttons rather than reading devices directly in most paths.

## Known determinism risks

- gameplay uses `UnityEngine.Random` extensively;
- cosmetic effects also consume `UnityEngine.Random`, potentially at frame-dependent rates;
- movement integrates with `Time.fixedDeltaTime`;
- coroutines and physics callback ordering can differ between machines;
- user unlock state influences selectable content.

The protocol must also negotiate game assembly hashes, mod version, unlock policy, and fixed simulation settings before starting a match.

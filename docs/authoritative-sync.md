# Authoritative synchronization

## Packet identity and ordering

Gameplay input uses `SessionInput`, not the trace/experimental `Input` packet.
Every packet carries the lobby session nonce and a monotonic sequence. The host
accepts input only when the Steam sender's authenticated slot matches the input
slot and the sequence is newer for that slot.

Snapshots carry their own monotonic sequence and the latest input sequence
incorporated by the host. Clients reject snapshots from the wrong session and
duplicates/stale sequences, including across `uint32` wrap. Applied snapshots
are acknowledged so the host can keep only a bounded correction history.

## Snapshot schema

The fixed, versioned snapshot header includes:

- session nonce and snapshot sequence;
- host simulation tick, lifecycle flags, and last processed input sequence for each player slot;
- level and canonical current-room position/depth;
- transition generation;
- canonical RNG-state hash and the four Unity RNG state words.

Up to four player records are sorted by unique slot and include role/lifecycle
flags, gameplay state, quantized position/velocity, and current/maximum health.
Enemy records use a host-assigned non-zero lifetime ID plus a hashed local
archetype key, lifecycle flags, gameplay state, quantized position/velocity, and
health. The codec caps active enemies at 256; the largest legal packet is about
10 KiB, below the transport's 64 KiB receive limit.

The schema intentionally excludes Unity instance IDs, raw object names,
reflection order, and direct serialized game objects. Archetype names are
normalized locally and only their stable hash crosses the network. A client
binds a host enemy ID to the nearest unmatched local object with the same hash;
missing, extra, duplicate, or lifecycle-incompatible objects fail closed.

## Correction policy

- Input packets use unreliable/no-delay delivery; a newer frame supersedes an
  older frame.
- Snapshots use unreliable delivery and periodic resend through newer state.
- Acknowledgements are reliable and prune bounded host history.
- Session nonce, sender slot, and sequence validation happen before state is
  exposed to runtime application code.
- Unknown transitions fail closed rather than mutating local game objects with
  incomplete state.
- A room mismatch resolves the host's canonical room key against the client's
  generated map and invokes Crawl's own room-transition entry point once per
  transition generation. Correction waits for that transition to complete.
- Animation/gameplay-state mismatch does not block motion correction; direct
  private-state mutation is intentionally avoided.
- Alive/dead corrections use Crawl's public `Health.Suicide` and
  `Health.Resurrect` paths once per target state. A host-removed mapped enemy is
  killed first when needed and only then routed through `Player.Despawn`.
- Once a snapshot passes preflight and its entity corrections are accepted, the
  client restores Unity's complete four-word RNG state. The hash remains a
  diagnostic; raw reflection field names are not transmitted.

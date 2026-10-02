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
- host simulation tick and last processed input sequence;
- level and canonical current-room position/depth;
- transition generation;
- canonical RNG-state hash.

Up to four player records are sorted by unique slot and include role/lifecycle
flags, gameplay state, quantized position/velocity, and current/maximum health.
The complete four-player packet is currently 167 bytes, below Steam's practical
P2P limits and small enough for periodic unreliable delivery.

The schema intentionally excludes Unity instance IDs, object names, reflection
order, and direct serialized game objects. Enemy and transition payloads will be
added only with stable spawn identities and explicit application semantics.

## Correction policy

- Input packets use unreliable/no-delay delivery; a newer frame supersedes an
  older frame.
- Snapshots use unreliable delivery and periodic resend through newer state.
- Acknowledgements are reliable and prune bounded host history.
- Session nonce, sender slot, and sequence validation happen before state is
  exposed to runtime application code.
- Unknown transitions fail closed rather than mutating local game objects with
  incomplete state.

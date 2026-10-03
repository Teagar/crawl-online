# Steam session protocol

## Topology and trust

The Steam lobby owner is the authoritative gameplay host. Steam provides the
transport sender identity; a payload identity is accepted only when it matches
that sender and the sender is still a current lobby member. P2P transport
requests may be opened for lobby members, but a peer receives no gameplay slot
until the application-level hello succeeds.

The host publishes lobby metadata for:

- packet protocol version;
- session protocol version;
- mod build version;
- exact verified `Assembly-CSharp.dll` SHA-256 fingerprint;
- a cryptographically random per-lobby session nonce.

Joining requires exact packet, session-protocol, mod-build, and game-assembly
fingerprints before the hello is sent. This prevents two locally valid but
unvalidated game builds from beginning gameplay. In particular, Windows 1.0.1
and Linux-native 1.0.3 remain isolated until a real cross-build session
establishes gameplay compatibility.

The nonce invalidates packets retained from an earlier lobby. It is not a secret
and is not treated as authentication by itself.

## Handshake

`Hello` contains the session nonce, sender Steam ID, monotonically increasing
attempt, requested slot (or automatic assignment), capability flags, and the
full verified game-assembly fingerprint. The host verifies transport identity,
nonce, attempt, required authoritative-state capability, exact game build, and
slot availability.

`HelloAccepted` echoes nonce, attempt, and authoritative game fingerprint, then
assigns a slot from 1–3. It also identifies the host, maximum player count, and
negotiated capabilities. Slot 0 is permanently reserved for the authoritative
host. Clients validate every field against Steam lobby state before becoming
connected.

`HelloRejected` provides an explicit reason: identity, session, slot, capacity,
stale attempt, membership, capability, or game-build mismatch. Duplicate connected attempts
are idempotent; reconnecting a disconnected Steam identity requires a higher
attempt and retains its reserved slot while it remains in the lobby.

## Disconnect lifecycle

- Leaving sends an explicit disconnect and closes each Steam P2P session.
- Lobby departure callbacks clear peer P2P state and free the slot.
- A transport failure marks the host-side peer disconnected and closes stale
  Steam state; the log instructs the client to rejoin for a fresh attempt.
- If the authoritative owner leaves, clients end the session explicitly rather
  than silently accepting Steam's automatic lobby-owner migration.
- A new lobby always receives a new nonce and fresh roster.

The implementation deliberately fails closed. Automatic gameplay resumption and
snapshot catch-up will be added with authoritative snapshot support rather than
reusing unknown simulation state.

Gameplay input has two ordering domains per slot: low-latency continuous state
and reliable press/release events. A disconnected peer cannot authorize either
domain through its reserved slot until a newer authenticated hello reconnects
it.

## Runtime evidence

A legitimate Linux Steam instance loaded the implementation, created a
friends-only authoritative lobby, assigned the owner to slot 0, and left the
lobby through the normal F8/F9 controls without a managed exception. The
official 32-bit Windows build repeated the same create/slot-0/leave flow under
Proton while BepInEx reported `System platform: Windows`. A true Linux-to-Windows
peer handshake still requires two simultaneous Steam identities or machines and
remains a human validation item.

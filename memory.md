# Workspace Memory

Durable, non-secret context for **Crawl Online**.

## Objective

Deliver an installable Windows/Linux mod that supports internet host/join and a synchronized complete Crawl match while every player runs a legitimate local installation.

## Durable decisions

- Gartrol is the source of truth for cards, dependencies, routing, claims, leases, deliveries, commits, and evidence.
- For every executable change, use the full Gartrol lifecycle: verify mission/card readiness and routing, claim before mutation, maintain the lease, track subtasks/dependencies, attach test evidence and Git provenance, deliver as done, and leave validation/rejection to a human.
- Agents deliver cards as `done`; only humans validate or reject deliveries.
- No proprietary game binaries, assemblies, Steam libraries, or decompiled source may be committed.
- Delayed-input lockstep is rejected by a clean trace-v2 experiment: with symmetric quantized input, exact and quantized critical state diverged from the first checkpoint at frame 30. Use a host-authoritative model with snapshots and corrective reconciliation.
- Steam provides lobby discovery, invitations, identity, NAT traversal, and relay transport; video streaming is outside the product boundary.
- Session trust is anchored to current Steam lobby membership and transport sender identity. Each lobby uses a random nonce; host slot 0 is authoritative, peers receive slots 1-3 only after a versioned capability handshake, and stale sessions fail closed.
- Linux and the official Windows binary under Proton both confirm authoritative lobby creation, host slot 0, and leave through F8/F9. A real Windows-Linux peer handshake still requires two simultaneous legitimate Steam identities/machines.
- Authoritative gameplay packets carry the lobby nonce plus monotonic per-slot input or snapshot sequences. Clients acknowledge applied snapshots; stale, duplicate, wrong-session, and wrong-slot packets are rejected before runtime mutation.
- Snapshot input acknowledgements are tracked separately for all four slots. Host correction history is bounded per peer; player/enemy corrections are preflighted before application, and unsupported lifecycle or room transitions fail closed rather than triggering duplicate game effects.
- The public source and release repository is `https://github.com/Teagar/crawl-online`; private clean-room notes remain in `https://github.com/Teagar/crawl-online-research`.
- Crawl's Unity 5.4 Mono runtime requires BepInEx 5.4.11, .NET 3.5 plugin targets, and a late `SystemSteam.Awake` chainloader entrypoint; newer BepInEx runtime patches fail on this Mono build.
- The researched Linux `Assembly-CSharp.dll` SHA-256 is `d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84`; the Windows hash is `e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e`.
- Crawl's Windows executable is 32-bit and requires the x86 BepInEx/Unity Doorstop package.
- Build-time references to Crawl gameplay types are avoided; SDK resolution encounters incompatible transitive framework metadata, while Harmony/reflection preserves the net35 runtime boundary.
- Determinism traces begin at `SystemGame.OnLevelLoad`; inputs are captured after `SystemInput.UpdateInternal` and replayed through `PlayerData` getters with separate held/down/up masks.
- Record mode must feed quantized inputs back into its own simulation, and traces must record render frames plus fixed-step counts.
- Unity RNG is global across gameplay and cosmetic systems; hash its canonically ordered full state and never reset it per frame.
- Linux direct tests require Steam app IDs in the environment. Use disposable working directories, suppress Steam Cloud/achievement writes, and preserve logs before cleanup.
- A clean trace-v2 recording exercised 19,380 frames, 36,574 inputs, and 517 checkpoints; replay compared 198 checkpoints through frame 6,240 and every exact/quantized hash diverged from frame 30 without a harness exception.

## Reconciliation

- The initial loader, protocol, Steam lobby code, scripts, documentation, and tests were created before any Gartrol claim existed. Preserve them as pre-claim artifacts; never represent them as work produced by a retroactive attempt.

## Conventions

- Read `AGENTS.md` before editing.
- Record only concise facts that remain useful across sessions.
- Never store credentials, lease tokens, raw transcripts, or temporary status.

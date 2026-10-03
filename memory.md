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
- An authenticated Linux host smoke loads the authoritative Harmony hooks, creates a friends-only lobby, captures periodic menu-state snapshots, and leaves without managed exceptions. Unity destroyed-object null semantics must be checked after casting reflected room objects to `Component`.
- Network player slots are global while controller assignment is local: hosts materialize authenticated peers as input-neutral bots; clients transfer their physical controller/profile to the assigned global slot and materialize other active slots as neutral bots. Bot status is not authoritative equality state.
- The public source and release repository is `https://github.com/Teagar/crawl-online`; private clean-room notes remain in `https://github.com/Teagar/crawl-online-research`.
- Crawl's Unity 5.4 Mono runtime requires BepInEx 5.4.11, .NET 3.5 plugin targets, and a late `SystemSteam.Awake` chainloader entrypoint; newer BepInEx runtime patches fail on this Mono build.
- BepInEx 5 parses `BepInPlugin` versions as numeric `System.Version`: prereleases must use a numeric loader identity (for example `0.2.0`) while diagnostics, lobby compatibility, manifests, tags, and release notes retain the full version (`0.2.0-alpha.1`).
- The researched Linux `Assembly-CSharp.dll` SHA-256 is `d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84`; the Windows hash is `e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e`.
- Crawl's Windows executable is 32-bit and requires the x86 BepInEx/Unity Doorstop package.
- Release packaging emits only Crawl Online DLLs, verified manifests, and installers; installers pin and hash-check upstream BepInEx 5.4.11, verify known Crawl assembly hashes by platform, and uninstall only Crawl Online plugin files while preserving BepInEx and other plugins.
- The Linux release installer auto-detects native `Crawl.x86_64` versus Windows x86 `Crawl.exe` under Proton, verifies PE architecture and the platform-specific assembly hash, selects `linux-x64` or `win-x86`, and fails closed on mixed executables or an existing loader without the matching entrypoint.
- Build-time references to Crawl gameplay types are avoided; SDK resolution encounters incompatible transitive framework metadata, while Harmony/reflection preserves the net35 runtime boundary.
- The session HUD is a passive IMGUI contextual strip outside the native menu: it uses only original solid-colour primitives plus the legitimate game's runtime-loaded GUI font, exposes no clickable controls, and scales at integer 1x/2x for 540p/1080p. It is silent while offline, except for guidance/errors; F5 exposes expanded diagnostic details and F6 minimizes them. Host/join and invite acceptance are rejected outside the active main menu without session mutation.
- Clean-room runtime observation confirms the main scene owns a `MenuTextMenu` with supported insertion/removal, initialization callbacks, native item prefabs, 8-unit vertical spacing, and message-based actions. Insert future `ONLINE` at index 1 before `MsgLibrary`; fail closed to the untouched five-item menu if any reflected contract is absent.
- Native main-menu insertion is implemented through runtime reflection: wait for an active valid selection, use the legitimate item template, treat materialized `m_items` as runtime truth, restore selection through `SetSelectedItem`, and target an original bridge on the menu owner. Linux smoke confirmed one `ONLINE` row and native keyboard focus/action dispatch without entering local gameplay.
- The Online UI/session boundary uses an independent state machine with monotonic operation generations. Host/join/cancel/leave start new generations; asynchronous callbacks must match the current generation so late discovery, lobby, transport, or authentication results cannot reconnect after BACK.
- The native Online submenu transactionally swaps rendered items for `HOST GAME`, `JOIN FRIEND`, and `BACK` using legitimate runtime templates. BACK reconstructs the untouched main-menu data, reinserts one ONLINE item, and restores its focus; join remains an effect-free placeholder until its dedicated integration card.
- Native `HOST GAME` creates the existing friends-only authoritative Steam lobby, then replaces the submenu with `INVITE FRIENDS` and `CANCEL`. Cancellation during a pending create marks its callback for immediate leave/reset, while cancellation after creation clears lobby/P2P state before returning UI and flow to Offline.
- Native `JOIN FRIEND` filters Steam lobbies by build/protocol/slot metadata and immediate-friend ownership, presents at most three stable identity-free rows, and revalidates metadata before the authenticated slot handshake. Invite callbacks use the same generated flow and are rejected outside the active main menu.
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

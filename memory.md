# Workspace Memory

Durable, non-secret context for **Crawl Online**.

## Objective

Deliver an installable Windows/Linux mod that supports internet host/join and a synchronized complete Crawl match while every player runs a legitimate local installation.

## Durable decisions

- Gartrol is the source of truth for cards, dependencies, routing, claims, leases, deliveries, commits, and evidence.
- Agents deliver cards as `done`; only humans validate or reject deliveries.
- No proprietary game binaries, assemblies, Steam libraries, or decompiled source may be committed.
- The initial simulation experiment is delayed input lockstep; retain it only if the determinism harness proves stability. Otherwise use a host-authoritative model with corrective snapshots.
- Steam provides lobby discovery, invitations, identity, NAT traversal, and relay transport; video streaming is outside the product boundary.
- Crawl's Unity 5.4 Mono runtime requires BepInEx 5.4.11, .NET 3.5 plugin targets, and a late `SystemSteam.Awake` chainloader entrypoint; newer BepInEx runtime patches fail on this Mono build.
- The researched Linux `Assembly-CSharp.dll` SHA-256 is `d6f169535cf2123568359550d75fe1a9924948e04d8d0beb2eed7eb187542f84`; the Windows hash is `e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e`.
- Crawl's Windows executable is 32-bit and requires the x86 BepInEx/Unity Doorstop package.

## Reconciliation

- The initial loader, protocol, Steam lobby code, scripts, documentation, and tests were created before any Gartrol claim existed. Preserve them as pre-claim artifacts; never represent them as work produced by a retroactive attempt.

## Conventions

- Read `AGENTS.md` before editing.
- Record only concise facts that remain useful across sessions.
- Never store credentials, lease tokens, raw transcripts, or temporary status.

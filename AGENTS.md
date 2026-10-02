# Workspace Instructions

## Project

Crawl Online

## Objective

Build a cross-platform online multiplayer mod for Crawl in which every participant runs and renders a legitimate local copy of the game.

## Gartrol operating contract

- Read `memory.md` and load the `workspace-context` skill before planning.
- Inspect repository state before editing and preserve existing work.
- Use Gartrol as the source of truth for missions, cards, dependencies, routing, claims, leases, deliveries, commits, and evidence.
- Create executable work as routed cards and encode dependencies explicitly.
- Claim a ready card before the first repository mutation and keep its lease alive while working.
- Deliver agent work as `done` with evidence. Only humans validate or reject it.
- Never store credentials, lease tokens, proprietary game files, decompiled source, or raw transcripts in the workspace.
- Create missing guidance files only; never overwrite existing guidance.

## Project boundaries

- Do not commit proprietary Crawl binaries, assemblies, Steam libraries, or decompiled game source.
- Preserve the user's legitimate game installation and make development installation reversible.
- Treat determinism as an experimental question; do not claim lockstep viability without measured evidence.

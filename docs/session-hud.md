# Session HUD

The runtime draws a small, original lower-left Unity IMGUI panel from solid
colour primitives and the legitimate installation's already-loaded GUI font.
Its stepped frame, dark iron palette, warm trim, uppercase hierarchy, and
integer scaling are designed to remain coherent with Crawl without copying
game art or layout data. Crawl Online ships no Crawl textures, fonts, text,
binaries, or decompiled source.

## States and privacy

`SteamLobbySession.GetHudState()` projects transport state into an
identity-free `SessionHudState`: status, host/client role, local slot when
assigned, four connection flags, and an operator-facing message. Steam IDs,
lobby IDs, nonces, and packet data are deliberately absent from this model.

The panel covers `offline`, `creating`, `waiting`, `authenticating`,
`connected`, and `error`/reconnect guidance. A host can see authenticated peer
slots; a client sees the host and its assigned slot after acceptance. The UI
does not make multiplayer authority claims or alter simulation.

## Controls

- **F8:** host a friends-only lobby
- **F7:** Steam invite dialog
- **F9:** leave lobby
- **F5:** hide or restore the control hint
- **F6:** minimize or restore the panel

The panel contains no buttons or other interactive IMGUI controls. It never
captures mouse input; keyboard handling remains in the existing runtime update
loop and only the explicit function-key shortcuts affect Crawl Online.

## Validation boundary

Automated tests exercise the identity-free state projection, including host
roster, pending-client, and offline cases. A real authenticated Steam smoke
can cover menu/offline/host/leave on one identity; it cannot validate an actual
peer handshake. Capture 960x540 and 1920x1080 screenshots during human smoke
to confirm that the lower-left panel does not obscure central menu actions and
that all text fits. Layout uses 1x scale at 540p and 2x at 1080p so its visual
weight remains consistent.
Release validation remains a separate gate.

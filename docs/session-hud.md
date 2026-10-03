# Session HUD

The native **ONLINE** main-menu entry owns configuration. Outside that menu the
runtime draws only a small, original lower-left contextual Unity IMGUI strip
when a session is active or an error needs attention. It uses solid-colour
primitives and the legitimate installation's already-loaded GUI font. Its
stepped frame, dark iron palette, warm trim, uppercase hierarchy, and integer
scaling remain coherent with Crawl without copying game art or layout data.
Crawl Online ships no Crawl textures, fonts, text, binaries, or decompiled
source.

## States and privacy

`SteamLobbySession.GetHudState()` projects transport state into an
identity-free `SessionHudState`: status, host/client role, local slot when
assigned, four connection flags, and an operator-facing message. Steam IDs,
lobby IDs, nonces, and packet data are deliberately absent from this model.

The contextual strip covers `creating`, `waiting`, `authenticating`,
`connected`, and `error`/reconnect guidance. Offline state is silent outside
the menu. A host can see authenticated peer slots; a client sees the host and
its assigned slot after acceptance. `F5` temporarily opens expanded diagnostic
details; it is not a permanent gameplay panel. The UI does not make
multiplayer authority claims or alter simulation.

## Controls

- **F8:** host a friends-only lobby
- **F7:** Steam invite dialog
- **F9:** leave lobby
- **F5:** toggle expanded diagnostic details
- **F6:** minimize or restore those details

Host, join, and Steam invite acceptance are accepted only from the active main
menu. Attempts during a campaign or scene transition leave the session and
flow state unchanged and display guidance to return to the main menu. The strip
contains no buttons or other interactive IMGUI controls. It never captures
mouse input; keyboard handling remains in the existing runtime update loop and
only the explicit function-key shortcuts affect Crawl Online.

## Validation boundary

Automated tests exercise the identity-free state projection, including host
roster, pending-client, and offline cases. A real authenticated Steam smoke
can cover menu/offline/host/leave on one identity; it cannot validate an actual
peer handshake. Capture 960x540 and 1920x1080 screenshots during human smoke
to confirm that the lower-left panel does not obscure central menu actions and
that all text fits. Layout uses 1x scale at 540p and 2x at 1080p so its visual
weight remains consistent.
Release validation remains a separate gate.

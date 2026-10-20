# Session HUD

The native **ONLINE** main-menu entry owns configuration. Outside that menu the
runtime draws only a small, original upper-left contextual Unity IMGUI strip
when a session is active or an error needs attention. It uses solid-colour
primitives and discovers the legitimate installation's already-loaded native
menu font from active `TextMesh` labels. The default Unity GUI font remains a
fail-safe only while no native menu font is available. Its
native uppercase hierarchy and integer scaling remain coherent with Crawl
without copying game art or layout data. There is no panel, border, texture or
other background: only game-font text with a small legibility shadow. The HUD
contains ONLINE, connection state, player count and one short contextual line.
`F5` adds role/slot and one message line instead of opening a diagnostics window.
Crawl Online ships no Crawl textures, fonts, text, binaries, or decompiled
source.

## States and privacy

`SteamLobbySession.GetHudState()` projects transport state into a bounded
`SessionHudState`: status, host/client role, local slot when assigned, four
connection flags, pre-game phase, transient profile display names, and an
operator-facing message. Steam IDs, lobby IDs, nonces, and packet data are
deliberately absent from this model. Display names are sanitized to the native
font's conservative uppercase glyph set, limited to 16 characters, never
logged or persisted, and fall back to the numbered player slot.

The contextual strip covers `creating`, `waiting`, `authenticating`,
`connected`, and `error`/reconnect guidance. Offline state is silent outside
the menu. A host can see authenticated peer slots; a client sees the host and
its assigned slot after acceptance. `F5` temporarily opens expanded diagnostic
details; it is not a permanent gameplay panel. The UI does not make
multiplayer authority claims or alter simulation.

During pre-game setup, the native ONLINE submenu becomes a shared lobby view.
It lists P1-P4 as host, connected, local (`YOU`), or empty and shows the current
waiting, player-setup, or starting phase. The host gets `START GAME` only after
an authenticated peer is present, plus invite and leave actions. Clients get a
clear waiting instruction and can only leave; they cannot start the
authoritative transition. These rows reuse the legitimate runtime's native menu
items and font without shipping or copying game assets.

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

Automated tests exercise the bounded state projection and lobby presentation,
including host/client phases, sanitized names, roster, pending-client, and
offline cases. A real authenticated Steam smoke
can cover menu/offline/host/leave on one identity; it cannot validate an actual
peer handshake. Capture 960x540 and 1920x1080 screenshots during human smoke
to confirm that the upper-left strip does not obscure central gameplay and
that all text fits. Layout uses 1x scale at 540p/720p, 2x at 1080p/1440p and a
bounded 3x scale at 4K so its visual weight remains consistent.
Release validation remains a separate gate.

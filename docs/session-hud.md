# Session HUD

The runtime draws a small, original lower-left Unity IMGUI panel using only the
legitimate installation's already-loaded GUI skin. Crawl Online ships no Crawl
textures, fonts, text, binaries, or decompiled source.

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
- **F6:** minimize or restore the panel

The brief in-panel tutorial is dismissed with **Got it**. When minimized, only
the restore control handles mouse input. Keyboard handling remains in the
existing runtime update loop and is not captured by the HUD.

## Validation boundary

Automated tests exercise the identity-free state projection, including host
roster, pending-client, and offline cases. A real authenticated Steam smoke
can cover menu/offline/host/leave on one identity; it cannot validate an actual
peer handshake. Capture 960x540 and 1920x1080 screenshots during human smoke
to confirm that the lower-left panel does not obscure central menu actions.
Release validation remains a separate gate.

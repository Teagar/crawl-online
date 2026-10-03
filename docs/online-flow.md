# Online flow state machine

`OnlineFlowStateMachine` is the testable boundary between the native menu,
asynchronous Steam operations, and the existing session implementation. It has
no Unity or Steam dependency.

## States

| State | Meaning |
| --- | --- |
| `Offline` | Native Online flow is closed. |
| `OnlineMenu` | `HOST GAME`, `JOIN FRIEND`, and `BACK` may be presented. |
| `CreatingLobby` | A friends-only lobby request is pending. |
| `WaitingForPlayers` | The host lobby exists and accepts peers. |
| `DiscoveringFriend` | Friend-lobby discovery is pending. |
| `JoiningLobby` | Lobby/transport entry is pending. |
| `Authenticating` | Version, capability, nonce, identity, and slot checks are pending. |
| `Connected` | The authenticated session is ready. |
| `RecoverableError` | An actionable error can be retried or dismissed with `BACK`. |
| `Leaving` | Session cleanup is pending before returning to `OnlineMenu`. |

## Operation generations

Starting host, join, cancellation, or leave increments an operation generation.
Every asynchronous callback must carry the generation captured when its work
started. A callback from an older generation is rejected without changing state,
which prevents late discovery, lobby, or authentication callbacks from
reconnecting after `BACK`.

## Command rules

- `OpenOnline` enters `OnlineMenu` only from `Offline`.
- `Host` and `JoinFriend` begin their respective asynchronous paths only from
  `OnlineMenu`.
- Lobby, discovery, transport, and authentication completion commands are
  accepted only in their exact expected state and generation.
- `Fail` and `Timeout` produce `RecoverableError` only while an online operation
  is pending. Retry repeats the previous host/join intent with a new generation.
- `HostLeft` is recoverable while waiting or connected.
- `BACK` closes `OnlineMenu`, dismisses an error, or enters `Leaving` when
  cleanup is required. `CleanupComplete` is the only transition from `Leaving`
  back to `OnlineMenu`.
- Invalid commands are side-effect free: state, generation, and message remain
  unchanged.

The test matrix evaluates every command in every state in addition to complete
host/join, timeout, cancellation, stale-callback, retry, and host-left paths.

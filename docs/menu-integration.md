# Native menu integration contract

This document records only the minimum clean-room runtime contract needed to
integrate Crawl Online with Crawl's main menu. It contains no decompiled source,
method bodies, copied assets, or proprietary binaries.

## Observation method

The opt-in `MenuContractProbe` uses ordinary .NET reflection against objects
already loaded by a legitimate local installation. It records type/member shape
and visible menu metadata to the local BepInEx log. It is disabled unless the
process environment contains:

```text
CRAWL_ONLINE_MENU_PROBE=1
```

The probe captures once after a scene settles and can be requested again with
`F4`. Probe logs are disposable research evidence and must not be committed.

## Confirmed main-menu contract

- The title menu is hosted by the `SceneMenuMain` scene.
- Its `MenuMain` component owns a `MenuTextMenu` and a `MenuStateMachine`.
- The visible main list is initialized with five entries. Their action messages,
  in order, are `MsgStart`, `MsgLibrary`, `MsgOptions`, `MsgCredits`, and
  `MsgQuit`.
- `MenuTextMenu` maintains item-data and instantiated-item collections and
  exposes public runtime operations including `InsertItem`, `RemoveItem`,
  `SetSelectedItem`, `RegisterInitCallback`, and `UpdateItemPositions`.
- `MenuTextMenuItemData` carries the label, action message, enabled/visible
  flags, platform mask, and optional item prefab.
- A materialized `MenuTextMenuItem` owns the legitimate installation's text,
  selected/disabled colours, selection/action animations, sounds, and action
  message.
- The observed main list is one column with item spacing of 8 local units. The
  first item is at local Y 0 and subsequent items are positioned at -8-unit
  intervals.
- Selection starts at index 0 after initialization. The text menu handles row
  navigation, controller identity, mouse hotspots, selection animation, action
  delay, and dispatch to its owner.
- `MenuMain` participates in enter/update/exit callbacks from
  `MenuStateMachine`; menu reconstruction and scene re-entry must therefore be
  treated as normal lifecycle events rather than one-time process startup.

## Safe insertion design

The implementation card should:

1. Resolve all gameplay types and members by reflection at runtime. Keep the
   existing no-`Assembly-CSharp` build-reference boundary.
2. Wait for `MenuTextMenu` initialization, preferably through its initialization
   callback, and invoke `InsertItem(1, data)` so `ONLINE` sits immediately before
   the item whose action is `MsgLibrary`.
3. Use the menu's default item prefab instead of creating or shipping visual
   assets. This delegates font, tint, selection animation, action animation,
   sound, scaling, and input parity to the game.
4. Attach an original bridge component to the existing menu owner for the new
   action message. Do not patch an original action to mean something else.
5. Make installation idempotent across `OnEnable`, scene reload, and callback
   repetition. Track the inserted item and refuse duplicate action messages.
6. On any missing type, field, method, owner, or initialization invariant, log a
   single actionable warning and leave the original five-item menu untouched.
7. Preserve `F7`, `F8`, and `F9` as alpha diagnostics until the complete native
   flow has passed local and remote validation.

## Implemented insertion

The runtime integration now waits until the main menu is active with a valid
selection, clones the legitimate menu's existing item-data template, and calls
`InsertItem` at the anchored index. The rendered-item collection is the runtime
source of truth: `InsertItem` materializes the new item while the serialized
item-data template remains unchanged. The integration then restores the logical
selection through `SetSelectedItem` and explicitly targets an original bridge
component on the menu owner.

An authenticated Linux smoke confirmed one `ONLINE` row between `START GAME`
and `THE VAULT`, native focus/action animation, `W`/`S` navigation, and dispatch
to `MsgCrawlOnline` without entering the local-game flow. Repeated scans did not
create duplicates. Full controller/mouse parity, scene re-entry, resolutions,
and the Online submenu remain the responsibility of subsequent cards.

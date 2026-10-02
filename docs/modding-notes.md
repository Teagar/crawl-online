# Crawl interoperability notes

These notes preserve experimentally verified information useful to future
contributors without reproducing proprietary game code.

## Runtime and build constraints

- Crawl uses Unity `5.4.2f2` with the legacy Mono scripting backend.
- BepInEx `5.4.11.0` works when runtime patching is disabled and the chainloader
  is injected late at `SystemSteam.Awake`. Newer tested BepInEx runtime patches
  fail on this Mono build.
- Mod assemblies target .NET Framework 3.5. Referencing `Assembly-CSharp.dll`
  directly caused incompatible transitive framework resolution in the SDK
  build, so the runtime integration uses Harmony and reflection by type name.
- The bootstrap remains independent of game types and loads the runtime only
  after `SystemSteam.Initialized`.
- The Windows executable is 32-bit and requires BepInEx x86. Linux uses the
  x86_64 package. Exact supported assembly fingerprints are listed in
  [research.md](research.md).

## Input boundary

`SystemInput.UpdateInternal` is the useful completed-input boundary. Human and
bot consumers converge on the public `PlayerData` input getters, making those
getters a lower-risk replay seam than rewriting protected controller arrays.

A faithful frame requires:

- movement X/Y;
- held Action/Back/Start masks;
- pressed Action/Back/Start masks;
- released Action/Back/Start masks.

Edges cannot be reconstructed reliably from held buttons because Crawl also
contains device edge handling and directional repetition. Inputs are associated
with render updates, while the trace separately counts fixed steps: Unity may
run zero or several `FixedUpdate` calls for one input frame.

Record mode feeds quantized captured axes back through the same getter patches.
Without that feedback, recording would simulate full-precision input while
replay simulated the network representation, introducing a measurement error.

## Randomness and time

Unity's global random generator is shared by gameplay, procedural generation,
bots, audio, and visual effects. A rendering or audio difference can therefore
shift later gameplay randomness. The harness intentionally observes this risk
instead of resetting RNG every frame.

The initial seed is applied at the game-scene `SystemGame.OnLevelLoad` barrier,
before level generation. Menu input remains live; logical trace frames begin at
that barrier. Random-state fields are sorted by name before hashing so runtime
reflection order does not affect the result.

Crawl also uses render delta, fixed delta, frame count, and real elapsed time.
Slow-motion and differing Update/FixedUpdate schedules are independent lockstep
risks and must be tested at equal and differing render rates.

## Canonical state boundary

The experimental state hash includes:

- level and game-in-progress state;
- map completion, room count, current room, and enemies alive;
- rooms and door topology sorted by stable spatial keys;
- players sorted by their fixed player slot;
- active/hero/alive/bot flags, controller slot, player state, health, position,
  and rigid-body velocity;
- full Unity random state.

`SystemLevel.GetMonstersSpawned` exposes the current monster objects. Runtime
snapshots exclude objects already represented by an assigned player slot,
allocate host-local lifetime IDs for the remainder, and match them on clients by
a normalized archetype-name hash plus nearest position. Raw names and Unity
instance IDs are never transmitted or persisted.

Exact IEEE-754 values form the lockstep verdict. A second hash quantizes motion
to `1e-4` for diagnostics. Object addresses, Unity instance IDs, hash codes,
enumeration order, locale-formatted numbers, and cosmetic particles are not
included.

## Safe local experiments

Linux direct launches need `SteamAppId=293780` and `SteamGameId=293780`; without
them Steamworks attempts a restart and the old Unity player can crash before a
useful mod log is produced.

`scripts/run-determinism-linux.sh` creates a reflink/copy outside the repository,
uses an isolated Unity home and working directory, links the legitimate local
Steam installation, and removes the copy afterward. Harness mode suppresses
Steam Cloud save/delete operations and achievement writes. The launcher keeps
BepInEx and Unity logs next to the trace before cleanup.

The old Linux Unity player has produced a segmentation fault during shutdown.
That observation is not yet attributed to Crawl Online; preserved logs and a
control run are required before treating it as a mod defect.

## Evidence so far

A real Linux gameplay recording completed 25,050 logical frames, 37,584 input
records, and 793 state checkpoints without an incomplete trace record. This
proves that the hooks and canonical hashing execute during gameplay. It does
not prove determinism. A preliminary replay compared 780 checkpoints through
frame 23,490 and diverged in exact and quantized state from frame 30 onward,
without a harness exception. Because that recording predates record-side
quantized-input feedback and required a trace-header migration, a clean current
format run remains necessary before selecting the synchronization architecture.

A subsequent clean trace-version-2 recording used symmetric quantized input and
captured 19,380 frames, 36,574 inputs, and 517 checkpoints. Replay compared 198
checkpoints through frame 6,240; exact and quantized critical state diverged at
every checkpoint beginning at frame 30. Neither run logged a harness exception.
Delayed-input lockstep is therefore rejected, and host-authoritative snapshots
are the selected synchronization model.

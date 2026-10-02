# Determinism experiment

Lockstep is a hypothesis, not an architectural commitment. The experiment must
measure two independent simulations before gameplay networking is implemented.

## Trace contract

The binary trace format is versioned and little-endian. A header records:

- initial Unity random seed;
- fixed simulation tick rate;
- participating player count;
- state-hash interval.

Records contain either one player's quantized input for a tick or a 64-bit
state hash. Input records preserve held, pressed, and released button masks.
State hashing emits exact and `1e-4`-quantized FNV-1a values over RNG, players,
health, motion, rooms, doors, and level progress. Never hash object addresses,
reflection order, render-only particles, timestamps, or locale-formatted text.
Record mode feeds the captured, quantized axes back into the same simulation so
record and replay execute the exact input representation used by networking.

Harness mode starts at the game-scene load barrier, initializes Unity RNG there,
and suppresses Steam Cloud saves, deletes, and achievement writes. Menu input is
left live so a human can navigate to the same scenario before record or replay.

On Linux, build the project and stage BepInEx, then run disposable instances:

```bash
./scripts/run-determinism-linux.sh record /tmp/opencode/run-a.cotr 60
./scripts/run-determinism-linux.sh replay /tmp/opencode/run-a.cotr 60
./scripts/compare-determinism-traces.py /tmp/opencode/run-a.cotr /tmp/opencode/run-b.cotr
```

Replay writes `<trace>.report.csv` and `<trace>.replay.cotr`, allowing both the
live comparison and an independent trace-to-trace check. The launcher uses a
disposable game copy and Unity home while linking only the legitimate local
Steam installation needed by Steamworks. It preserves BepInEx and Unity logs
beside the trace. An interrupted final trace record is ignored safely.

## Required runs

1. Record a fixed player sequence from a clean test profile.
2. Replay it twice at the same render and fixed-update rates.
3. Compare every periodic hash and report the first divergent tick.
4. Replay at different render rates while preserving the fixed-update rate.
5. Repeat a representative room transition and combat sequence.

Each instance must use an isolated home/save directory and its own BepInEx log.
Traces and reports may be retained; proprietary game files must remain outside
the repository.

## Decision gate

Retain delayed-input lockstep only if repeated runs produce identical critical
state hashes through room generation, combat, death, and transition events.
Any unexplained divergence makes host-authoritative state with corrective
snapshots the default. Cosmetic-only divergence may be excluded only after the
hashed state boundary proves that it cannot affect gameplay RNG or physics.

## Results

An initial real gameplay trace exercised 25,050 logical frames and 793 state
checkpoints. A replay reached 23,490 frames and compared 780 checkpoints; every
checkpoint diverged, beginning at frame 30, in both exact and quantized hashes.
The harness remained active without a logged runtime exception.

This result is preliminary rather than the final architecture verdict: the
recording predates record-side feedback of quantized axes and was migrated from
trace header version 1 to version 2. That asymmetry can itself create state
differences. A fresh version-2 record/replay pair using identical quantized
input semantics is required before closing the decision gate.

The clean version-2 run then recorded 19,380 logical frames, 36,574 inputs, and
517 checkpoints with record-side quantized-input feedback enabled. Its replay
compared 198 checkpoints through frame 6,240. All 198 exact and quantized hashes
diverged, starting at frame 30. Record and replay logs show successful runtime
load and RNG initialization with no harness exception or automatic disable.

**Decision:** delayed-input lockstep is rejected for the researched Crawl build.
The networking implementation must use an authoritative lobby owner with state
snapshots and corrective reconciliation. The trace harness remains useful for
snapshot coverage and drift diagnostics, not as a lockstep release gate.

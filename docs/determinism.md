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
state hash. State hashing uses FNV-1a with explicit primitive encoding and
quantized floating-point values. Never hash object addresses, reflection order,
render-only particles, timestamps, or locale-formatted text.

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

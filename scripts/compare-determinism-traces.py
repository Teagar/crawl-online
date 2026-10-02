#!/usr/bin/env python3
"""Compare complete state-hash records from two Crawl Online traces."""

import struct
import sys
from pathlib import Path

HEADER_SIZE = 16
RECORD_SIZES = {1: 18, 2: 20, 3: 17}


def read_hashes(path: Path) -> dict[int, tuple[int, int]]:
    data = path.read_bytes()
    if len(data) < HEADER_SIZE or data[:4] != b"COTR" or data[4] != 2:
        raise ValueError(f"invalid trace header: {path}")

    hashes: dict[int, tuple[int, int]] = {}
    offset = HEADER_SIZE
    while offset < len(data):
        record_type = data[offset]
        offset += 1
        payload_size = RECORD_SIZES.get(record_type)
        if payload_size is None:
            raise ValueError(f"unknown record type {record_type} at offset {offset - 1}: {path}")
        if offset + payload_size > len(data):
            break  # A process shutdown may interrupt only the final record.
        payload = data[offset : offset + payload_size]
        offset += payload_size
        if record_type == 2:
            tick, exact, quantized = struct.unpack("<IQQ", payload)
            hashes[tick] = (exact, quantized)
    return hashes


def main() -> int:
    if len(sys.argv) != 3:
        print(f"usage: {Path(sys.argv[0]).name} TRACE_A TRACE_B", file=sys.stderr)
        return 2

    try:
        left = read_hashes(Path(sys.argv[1]))
        right = read_hashes(Path(sys.argv[2]))
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 2

    ticks = sorted(set(left) | set(right))
    for tick in ticks:
        if tick not in left or tick not in right:
            print(f"diverged tick={tick}: hash missing from one trace")
            return 1
        if left[tick] != right[tick]:
            exact_match = left[tick][0] == right[tick][0]
            quantized_match = left[tick][1] == right[tick][1]
            print(
                f"diverged tick={tick}: exact_match={exact_match} "
                f"quantized_match={quantized_match}"
            )
            return 1

    if not ticks:
        print("no comparable state hashes")
        return 2
    print(f"identical state hashes: {len(ticks)} checkpoints through tick {ticks[-1]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

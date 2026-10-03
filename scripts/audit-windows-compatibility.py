#!/usr/bin/env python3
"""Fail closed on Crawl Online artifacts incompatible with Windows x86/Crawl Mono."""

import argparse
import json
from pathlib import Path

from windows_compatibility import CompatibilityError, audit_project, inspect_managed_pe


parser = argparse.ArgumentParser()
parser.add_argument("--project", action="append", default=[], type=Path)
parser.add_argument("--assembly", action="append", default=[], type=Path)
args = parser.parse_args()
if not args.project or not args.assembly:
    parser.error("at least one --project and one --assembly are required")

try:
    result = {
        "projects": [audit_project(path) for path in args.project],
        "assemblies": [inspect_managed_pe(path.read_bytes(), path.name) for path in args.assembly],
    }
except (CompatibilityError, OSError, ValueError) as error:
    raise SystemExit("Windows compatibility audit failed: " + str(error))

print(json.dumps(result, sort_keys=True))

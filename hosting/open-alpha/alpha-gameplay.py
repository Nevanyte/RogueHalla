#!/usr/bin/env python3
"""Run an existing Source Arena command with the reviewed alpha gameplay settings.

Usage: alpha-gameplay.py /path/to/rh-server [the origin's existing server arguments]
All origin-specific arguments and environment variables pass through unchanged.
"""

import os
import sys


def gameplay_argv(args: list[str]) -> list[str]:
    if not args:
        raise ValueError("expected the rh-server executable and its existing arguments")
    binary, *old = args
    if not os.path.isfile(binary) or not os.access(binary, os.X_OK):
        raise ValueError("rh-server executable is missing or not executable")
    for flag in ("--fill-bots", "--bot-brain", "--map-pool", "--signature-landmarks"):
        if any(arg.startswith(flag + "=") for arg in old):
            raise ValueError(f"{flag}= syntax is not supported; use a separate value")
    runtime = [old[i + 1] for i, arg in enumerate(old[:-1]) if arg == "--runtime"]
    if runtime != ["source-arena"]:
        raise ValueError("expected exactly one --runtime source-arena")
    if "--map" not in old:
        raise ValueError("source-arena requires the existing --map fallback")
    new = []
    seen = set()
    i = 0
    while i < len(old):
        arg = old[i]
        if arg in ("--fill-bots", "--bot-brain"):
            if arg in seen or i + 1 >= len(old):
                raise ValueError(f"duplicate or incomplete {arg}")
            seen.add(arg)
            i += 2
            continue
        if arg in ("--map-pool", "--signature-landmarks"):
            if arg in seen:
                raise ValueError(f"duplicate {arg}")
            seen.add(arg)
        new.append(arg)
        i += 1
    for flag in ("--map-pool", "--signature-landmarks"):
        if flag not in seen:
            new.append(flag)
    new.extend(("--fill-bots", "8", "--bot-brain", "raid"))
    return [binary, *new]


if __name__ == "__main__":
    try:
        command = gameplay_argv(sys.argv[1:])
    except ValueError as error:
        print(f"alpha-gameplay: {error}", file=sys.stderr)
        sys.exit(2)
    os.execv(command[0], command)

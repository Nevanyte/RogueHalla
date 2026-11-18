#!/usr/bin/env python3
"""Readability check: README, module header and file-size rules over tracked files.

The standard is docs/agent/READABILITY.md; the allowlist is scripts/readability-allowlist.txt.
Run: python3 scripts/readability_check.py [--root DIR] [--allowlist FILE] [--write-allowlist]
Exit 0 when every violation is covered by a live allowlist entry and no entry is stale; exit 1 otherwise.
Standard library only.
"""

import posixpath
import argparse
import os
import re
import subprocess
import sys

RUST_LIMIT = 1500
CSHARP_LIMIT = 800
FIXED_README_DIRS = [
    "client-unity/RoguehallaNet", "client-unity/UnityProject", "hosting", "tools", "scripts",
    "parity", "protocol", "docs",
]
SYNCED_PREFIX = "client-unity/UnityProject/Assets/RoguehallaNet/"
KINDS = ("size", "header", "readme", "frozen", "exempt")
TYPE_DECL = re.compile(
    r"(public|internal|private|protected|sealed|static|abstract|partial|\s)*(class|struct|interface|enum|record)\s"
)


def tracked_files(root):
    out = subprocess.run(["git", "-C", root, "ls-files", "-z"], capture_output=True, check=True).stdout
    names = [n.decode("utf-8", "replace") for n in out.split(b"\0") if n]
    return sorted(n for n in names if os.path.isfile(os.path.join(root, n)))


def read_bytes(root, rel):
    with open(os.path.join(root, rel), "rb") as f:
        return f.read()


def count_lines(data):
    n = data.count(b"\n")
    if data and not data.endswith(b"\n"):
        n += 1
    return n


def is_rust_test(parts):
    name = parts[-1]
    return "tests" in parts[:-1] or name == "tests.rs" or name.endswith("_tests.rs")


def is_csharp_test(parts):
    return any(p.endswith("Tests") for p in parts[:-1])


def source_kind(rel):
    """'rust', 'csharp' or None: which rules apply to this tracked path."""
    parts = rel.split("/")
    if rel.endswith(".rs") and len(parts) >= 4 and parts[0] == "crates" and parts[2] == "src":
        return None if is_rust_test(parts) else "rust"
    if rel.endswith(".cs") and parts[0] == "client-unity" and not rel.startswith(SYNCED_PREFIX):
        return None if is_csharp_test(parts) else "csharp"
    return None


def has_rust_header(data, fragment=False):
    """A module starts with `//!`. A fragment pulled in by `include!` cannot (rustc rejects an inner doc comment
    there), so its header is a plain `//` comment."""
    for line in data.decode("utf-8", "replace").splitlines():
        if line.strip():
            return line.strip().startswith("//" if fragment else "//!")
    return False


INCLUDE_RE = re.compile(r'include!\(\s*"([^"]+\.rs)"\s*\)')


def included_fragments(root, files):
    """Paths of .rs files named by an `include!("...")` in a tracked Rust file, resolved against its directory."""
    out = set()
    for rel in files:
        if rel.endswith(".rs") and rel.startswith("crates/"):
            for m in INCLUDE_RE.finditer(read_bytes(root, rel).decode("utf-8", "replace")):
                out.add(posixpath.normpath(posixpath.join(posixpath.dirname(rel), m.group(1))))
    return out


def has_csharp_summary(data):
    for line in data.decode("utf-8", "replace").splitlines():
        if line.strip().startswith("/// <summary>"):
            return True
        if TYPE_DECL.match(line):
            return False
    return False


def glob_regex(pattern):
    out = []
    i = 0
    while i < len(pattern):
        c = pattern[i]
        if pattern.startswith("**", i):
            out.append(".*")
            i += 2
            continue
        out.append("[^/]*" if c == "*" else "[^/]" if c == "?" else re.escape(c))
        i += 1
    return re.compile("^" + "".join(out) + "$")


def readme_dirs(files):
    """Directories that must hold a README, because they hold tracked files."""
    dirs = set()
    for f in files:
        parts = f.split("/")
        if parts[0] == "crates" and len(parts) >= 3:
            dirs.add("crates/" + parts[1])
        for d in FIXED_README_DIRS:
            if f.startswith(d + "/"):
                dirs.add(d)
    return dirs


def find_violations(root, files, exempt_res):
    """Returns (size {path: lines}, header set, readme set)."""
    size, header, readme = {}, set(), set()
    fileset = set(files)
    fragments = included_fragments(root, files)
    for rel in files:
        kind = source_kind(rel)
        if kind is None:
            continue
        data = read_bytes(root, rel)
        limit = RUST_LIMIT if kind == "rust" else CSHARP_LIMIT
        n = count_lines(data)
        if n > limit and not any(r.match(rel) for r in exempt_res):
            size[rel] = n
        ok = has_rust_header(data, rel in fragments) if kind == "rust" else has_csharp_summary(data)
        if not ok:
            header.add(rel)
    for d in readme_dirs(files):
        if d + "/README.md" not in fileset:
            readme.add(d)
    return size, header, readme


def parse_allowlist(path):
    """Returns (entries, problems). entries: list of (lineno, kind, path, extra)."""
    entries, problems, seen = [], [], set()
    if not os.path.exists(path):
        return entries, problems
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    for no, raw in enumerate(lines, 1):
        s = raw.strip()
        if not s or s.startswith("#"):
            continue
        first = s.split(None, 1)[0]
        fields = s.split(None, 2 if first in ("frozen", "exempt") else 3)
        kind = fields[0]
        where = "allowlist line %d" % no
        if kind not in KINDS:
            problems.append("%s: unknown kind %r" % (where, kind))
            continue
        if len(fields) < 2:
            problems.append("%s: %s needs a path" % (where, kind))
            continue
        target, extra = fields[1], fields[2:]
        if kind == "size":
            if len(extra) != 1 or not extra[0].isdigit():
                problems.append("%s: size needs a numeric maximum" % where)
                continue
        elif kind in ("frozen", "exempt"):
            if not extra or not extra[0].strip():
                problems.append("%s: %s needs a reason" % (where, kind))
                continue
        elif extra:
            problems.append("%s: %s takes only a path" % (where, kind))
            continue
        key = (kind, target.rstrip("/"))
        if key in seen:
            problems.append("%s: duplicate %s entry for %s" % (where, kind, target))
            continue
        seen.add(key)
        entries.append((no, kind, target.rstrip("/"), extra[0] if extra else ""))
    return entries, problems


def write_allowlist(root, allowlist):
    files = tracked_files(root)
    kept = {"frozen": [], "exempt": []}
    if os.path.exists(allowlist):
        with open(allowlist, encoding="utf-8") as f:
            for raw in f:
                fields = raw.strip().split(None, 2)
                if len(fields) == 3 and fields[0] in kept:
                    kept[fields[0]].append(fields)
    exempt_res = [glob_regex(e[1]) for e in kept["exempt"]]
    size, header, readme = find_violations(root, files, exempt_res)
    out = ["# Readability allowlist: see docs/agent/READABILITY.md. Entries only ever shrink."]
    for kind in ("exempt", "frozen"):
        out += ["%s %s %s" % tuple(e) for e in sorted(kept[kind], key=lambda e: e[1])]
    out += ["header %s" % p for p in sorted(header)]
    out += ["readme %s" % p for p in sorted(readme)]
    out += ["size %s %d" % (p, size[p]) for p in sorted(size)]
    print("\n".join(out))
    return 0


def check(root, allowlist):
    files = tracked_files(root)
    fileset = set(files)
    entries, problems = parse_allowlist(allowlist)
    exempt_res = [glob_regex(e[2]) for e in entries if e[1] == "exempt"]
    size, header, readme = find_violations(root, files, exempt_res)

    listed = {"size": set(), "header": set(), "readme": set()}
    grew = set()
    counts = dict.fromkeys(KINDS, 0)
    all_dirs = {os.path.dirname(f) for f in files}
    for no, kind, path, extra in entries:
        counts[kind] += 1
        if kind in ("size", "header", "frozen") and path not in fileset:
            problems.append("%s %s: path does not exist (moved or renamed?); remove or update this entry" % (kind, path))
        elif kind == "readme" and not any(d == path or d.startswith(path + "/") for d in all_dirs):
            problems.append("readme %s: directory does not exist; remove this entry" % path)
        elif kind == "exempt":
            rx = glob_regex(path)
            if not any(rx.match(f) for f in files):
                problems.append("exempt %s: matches no file; remove this entry" % path)
        elif kind == "size":
            if path not in size:
                problems.append("size %s: file is within the limit now; remove this entry" % path)
            elif size[path] > int(extra):
                grew.add(path)
                problems.append("size %s: grew to %d lines, above its recorded %d" % (path, size[path], int(extra)))
            else:
                listed["size"].add(path)
        elif kind == "header":
            if path not in header:
                problems.append("header %s: file has a header now; remove this entry" % path)
            else:
                listed["header"].add(path)
        elif kind == "readme":
            if path not in readme:
                problems.append("readme %s: directory has a README now; remove this entry" % path)
            else:
                listed["readme"].add(path)

    for p in sorted(size):
        if p not in listed["size"] and p not in grew:
            problems.append("size %s: %d lines, over the limit; split it (new code must be readable)" % (p, size[p]))
    for p in sorted(header - listed["header"]):
        problems.append("header %s: no module header or summary; add one" % p)
    for p in sorted(readme - listed["readme"]):
        problems.append("readme %s/: no README.md; add one" % p)

    for line in problems:
        print(line)
    if problems:
        print("readability: FAIL (%d problems)" % len(problems))
        return 1
    print("readability: OK (allowlisted: %d over size, %d without header, %d without README; %d frozen, %d exempt)"
          % (counts["size"], counts["header"], counts["readme"], counts["frozen"], counts["exempt"]))
    return 0


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=os.path.dirname(here))
    ap.add_argument("--allowlist")
    ap.add_argument("--write-allowlist", action="store_true")
    args = ap.parse_args()
    root = os.path.abspath(args.root)
    allowlist = args.allowlist or os.path.join(root, "scripts", "readability-allowlist.txt")
    if args.write_allowlist:
        return write_allowlist(root, allowlist)
    return check(root, allowlist)


if __name__ == "__main__":
    sys.exit(main())

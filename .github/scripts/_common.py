"""Shared helpers for the licence-free validation scripts.

Every script takes an optional ``--root <dir>`` so it can be pointed at a temporary copy of the
package (the way the self-checks in Documentation~/CI.md break a copy on purpose). Without it the
repository root is used.
"""

import argparse
import json
import os
import re
import subprocess
import sys

SEMVER = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$")


def parse_args(description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("--root", default=None, help="package root (default: the repository root)")
    args = parser.parse_args()
    if args.root:
        return os.path.abspath(args.root)
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


def _git_files(root):
    top = subprocess.run(
        ["git", "-C", root, "rev-parse", "--show-toplevel"], capture_output=True, text=True
    )
    if top.returncode != 0:
        return None
    if os.path.normcase(os.path.realpath(top.stdout.strip())) != os.path.normcase(os.path.realpath(root)):
        return None
    out = subprocess.run(
        ["git", "-C", root, "ls-files", "--cached", "--others", "--exclude-standard"],
        capture_output=True, text=True, check=True,
    )
    return sorted(set(p for p in out.stdout.splitlines() if p and os.path.exists(os.path.join(root, p))))


def list_files(root):
    """Package files as forward-slash relative paths.

    Inside a git checkout this is tracked plus untracked-but-not-ignored files, so the result is the
    same on a clean CI checkout and in a working tree that has not been committed yet. Outside a git
    checkout (a temporary copy) it walks the directory and skips only ``.git``.
    """
    files = _git_files(root)
    if files is not None:
        return files
    found = []
    for base, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d != ".git"]
        for name in names:
            rel = os.path.relpath(os.path.join(base, name), root).replace(os.sep, "/")
            found.append(rel)
    return sorted(found)


def load_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def error(message, path=None):
    if path:
        print(f"::error file={path}::{message}")
    else:
        print(f"::error::{message}")
    print(f"ERROR: {message}", file=sys.stderr)

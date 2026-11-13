#!/usr/bin/env python3
"""Every Unity-visible file and folder must have a `.meta`, and no `.meta` may be orphaned.

A missing `.meta` does not fail a build and does not show in this repository's own test results.
Unity logs it as an Error, and the test framework turns an unexpected log error into an
UnhandledLogMessageException, which fails a CONSUMER'S ENTIRE SUITE. The sibling packages
(com.cuvara.uitoolkit, com.cuvara.dots) each shipped assets without a `.meta`; this gate exists so
this package does not.

Unity ignores paths that begin with a dot and folders that end in `~`, so those are skipped here
for the same reason Unity skips them. That is also why `Sandbox~`, `Samples~`, `Schemas~`, `Tools~`
and `Documentation~` are out of scope.

An orphan `.meta` (its asset is gone) is an error too: Unity deletes it on the next import and logs
a warning, and the stale GUID can collide with a later asset.

Usage: check_metas.py [--root <package root>]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402


def unity_visible(path):
    parts = path.split("/")
    for part in parts:
        if part.startswith("."):
            return False
        if part.endswith("~"):
            return False
    return True


def main():
    root = _common.parse_args("Check that every Unity-visible file and folder has a .meta")
    files = _common.list_files(root)
    have = set(files)

    assets = []
    directories = set()
    for path in files:
        if path.endswith(".meta") or not unity_visible(path):
            continue
        assets.append(path)
        parts = path.split("/")
        for i in range(1, len(parts)):
            directories.add("/".join(parts[:i]))

    missing = []
    for path in assets:
        if path + ".meta" not in have:
            missing.append(path)
    for directory in sorted(directories):
        if directory + ".meta" not in have:
            missing.append(directory + "/")

    known = set(assets) | directories
    orphans = []
    for path in files:
        if not path.endswith(".meta"):
            continue
        base = path[: -len(".meta")]
        if base not in known:
            orphans.append(path)

    failed = False
    if missing:
        failed = True
        print(f"{len(missing)} Unity-visible path(s) have no .meta:")
        for path in sorted(missing):
            print(f"  {path}")
            _common.error(f"Missing {path.rstrip('/')}.meta; Unity will not import this", path.rstrip("/"))
    if orphans:
        failed = True
        print(f"{len(orphans)} orphan .meta file(s) with no asset:")
        for path in sorted(orphans):
            print(f"  {path}")
            _common.error("Orphan .meta: its asset does not exist, or Unity ignores that path", path)
    if failed:
        return 1

    print(
        f"check_metas: OK, {len(assets)} Unity-visible file(s) and {len(directories)} folder(s) "
        f"have a .meta, 0 orphan .meta file(s)."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

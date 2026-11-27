#!/usr/bin/env python3
"""Validate CHANGELOG.md.

A '## [Unreleased]' heading must exist and come first. Released headings must read
'## [x.y.z] - YYYY-MM-DD'. The version in package.json must be the newest released heading or newer
than it (a version in development has no heading yet).

Usage: check_changelog.py [--root <package root>]
"""

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402

HEADING = re.compile(r"^## \[([^\]]+)\](.*)$", re.MULTILINE)
RELEASED = re.compile(r"^ - \d{4}-\d{2}-\d{2}\s*$")


def version_key(text):
    core = text.split("-")[0].split("+")[0]
    return tuple(int(x) for x in core.split("."))


def main():
    root = _common.parse_args("Validate CHANGELOG.md")
    problems = []

    try:
        with open(os.path.join(root, "CHANGELOG.md"), "r", encoding="utf-8") as handle:
            text = handle.read()
    except FileNotFoundError:
        _common.error("CHANGELOG.md not found", "CHANGELOG.md")
        return 1

    headings = HEADING.findall(text)
    names = [h[0] for h in headings]
    if "Unreleased" not in names:
        problems.append("CHANGELOG.md has no '## [Unreleased]' heading")
    elif names[0] != "Unreleased":
        problems.append("'## [Unreleased]' must be the first version heading")

    released = []
    for name, rest in headings:
        if name == "Unreleased":
            continue
        if not _common.SEMVER.match(name):
            problems.append(f"heading '[{name}]' is not a semantic version")
            continue
        if not RELEASED.match(rest):
            problems.append(f"heading '[{name}]' must read '## [{name}] - YYYY-MM-DD'")
        released.append(name)

    version = ""
    try:
        version = _common.load_json(os.path.join(root, "package.json")).get("version", "")
    except (FileNotFoundError, ValueError):
        problems.append("package.json is missing or invalid, cannot compare the version")

    if version and _common.SEMVER.match(version) and released:
        newest = max(released, key=version_key)
        if version_key(version) < version_key(newest):
            problems.append(f"package.json version {version} is older than the newest released heading [{newest}]")

    if problems:
        for problem in problems:
            _common.error(problem, "CHANGELOG.md")
        print(f"check_changelog: FAIL, {len(problems)} problem(s)")
        return 1

    print(
        f"check_changelog: OK, [Unreleased] present, {len(released)} released heading(s), "
        f"package version {version}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

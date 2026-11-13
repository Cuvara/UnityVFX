#!/usr/bin/env python3
"""Lay out the Sandbox project for a CI Unity job, the way the open path does on a workstation.

On a workstation `Tools~/setup-sandbox.py` creates a sandbox home outside the repository with two
links, `<home>/Sandbox -> Sandbox~` and `<home>/package -> repository`, and Unity opens
`<home>/Sandbox`, whose manifest references the package as `file:../../package`. Unity 6000.6 cannot
map package scripts to classes when the project path has a component that ends in `~` or starts with
`.`, so the project is never opened at `Sandbox~` (Documentation~/AUTHORING.md, Pitfalls).

A CI runner has no Unity installation for setup-sandbox.py to check and the game-ci container only
sees the workspace, so CI uses copies instead of links:

  <home>/package   the checkout (actions/checkout with `path: <home>/package`)
  <home>/Sandbox   a copy of <home>/package/Sandbox~ (without Library, Temp, Logs, UserSettings)
  <home>/Sandbox/Assets/Showcase   a copy of Samples~/Showcase, when it exists

No link is created and nothing is ever deleted. The script fails when the package is not at
`<home>/package`, when `<home>` is inside the repository that holds this script, when the manifest
does not reference `file:../../package`, when a component of the project path ends in `~` or starts
with `.`, when `Sandbox~` or `Samples~/Showcase` holds a link (it would be copied as its target), or
when `<home>/Sandbox` exists already.

Usage: ci_sandbox.py <home>
"""

import argparse
import json
import os
import shutil
import sys

PACKAGE_LINK_NAME = "package"
OPEN_NAME = "Sandbox"
REFERENCE = "file:../../" + PACKAGE_LINK_NAME
PACKAGE_NAME = "com.cuvara.vfx"
SKIP = {"Library", "Temp", "Logs", "UserSettings", "obj", "Captures"}


def fail(message):
    print(f"::error::{message}")
    print(f"ERROR: {message}", file=sys.stderr)
    return 1


def hidden_components(path):
    parts = [p for p in os.path.abspath(path).replace("\\", "/").split("/") if p]
    return [p for p in parts if (p.endswith("~") or p.startswith(".")) and not p.endswith(":")]


def is_inside(path, folder):
    path, folder = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(folder))
    return path == folder or path.startswith(folder.rstrip(os.sep) + os.sep)


def links_under(folder):
    found = []
    for base, dirs, files in os.walk(folder, followlinks=False):
        if os.path.samefile(base, folder):
            dirs[:] = [d for d in dirs if d not in SKIP]
        for name in dirs + files:
            entry = os.path.join(base, name)
            if os.path.islink(entry) or (hasattr(os.path, "isjunction") and os.path.isjunction(entry)):
                found.append(entry)
    return found


def main():
    parser = argparse.ArgumentParser(description="Lay out the Sandbox project for a CI Unity job")
    parser.add_argument("home", help="folder that holds the checkout as package/; Sandbox/ is created next to it")
    args = parser.parse_args()

    home = os.path.abspath(args.home)
    package = os.path.join(home, PACKAGE_LINK_NAME)
    sandbox_source = os.path.join(package, "Sandbox~")
    project = os.path.join(home, OPEN_NAME)

    script_repo = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    if is_inside(home, script_repo):
        return fail(f"the home {home} is inside the repository {script_repo}; use a folder next to the checkout")

    package_json = os.path.join(package, "package.json")
    if not os.path.isfile(package_json):
        return fail(f"no package at {package}: check out the repository with `path: <home>/{PACKAGE_LINK_NAME}`")
    with open(package_json, encoding="utf-8") as handle:
        if json.load(handle).get("name") != PACKAGE_NAME:
            return fail(f"{package_json} is not {PACKAGE_NAME}")
    if not os.path.isdir(sandbox_source):
        return fail(f"no Sandbox~ project in {package}")
    hidden = hidden_components(project)
    if hidden:
        return fail(f"the project path {project} has hidden components {hidden}; Unity 6000.6 would not map package scripts")
    if os.path.lexists(project):
        return fail(f"{project} exists already")

    manifest_path = os.path.join(sandbox_source, "Packages", "manifest.json")
    with open(manifest_path, encoding="utf-8") as handle:
        reference = json.load(handle).get("dependencies", {}).get(PACKAGE_NAME)
    if reference != REFERENCE:
        return fail(f"Sandbox~/Packages/manifest.json references {PACKAGE_NAME} as {reference!r}, expected {REFERENCE!r}")

    showcase = os.path.join(package, "Samples~", "Showcase")
    sources = [sandbox_source] + ([showcase] if os.path.isdir(showcase) else [])
    linked = [entry for source in sources for entry in links_under(source)]
    if linked:
        return fail(f"links in the sources, not copied: {linked}")

    shutil.copytree(sandbox_source, project, symlinks=False,
                    ignore=lambda folder, names: [n for n in names if os.path.samefile(folder, sandbox_source) and n in SKIP])

    copied_showcase = False
    if os.path.isdir(showcase):
        shutil.copytree(showcase, os.path.join(project, "Assets", "Showcase"), symlinks=False)
        copied_showcase = True

    resolved = os.path.normpath(os.path.join(project, "Packages", "..", "..", PACKAGE_LINK_NAME))
    if os.path.normcase(resolved) != os.path.normcase(package):
        return fail(f"{REFERENCE} resolves to {resolved}, not to {package}")

    print(json.dumps({
        "ok": True,
        "project": project.replace("\\", "/"),
        "package": package.replace("\\", "/"),
        "reference": REFERENCE,
        "showcase": copied_showcase,
    }, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

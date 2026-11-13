#!/usr/bin/env python3
"""Validate package.json against the Cuvara package convention and the versions in DESIGN.md.

Usage: check_package_json.py [--root <package root>]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402

EXPECTED_NAME = "com.cuvara.vfx"
EXPECTED_UNITY = "6000.6"
EXPECTED_DEPENDENCIES = {
    "com.unity.visualeffectgraph": "17.6.0",
    "com.unity.render-pipelines.universal": "17.6.0",
}
REQUIRED = ["name", "version", "displayName", "description", "unity", "license", "author", "dependencies"]


def main():
    root = _common.parse_args("Validate package.json")
    path = os.path.join(root, "package.json")
    problems = []

    try:
        package = _common.load_json(path)
    except FileNotFoundError:
        _common.error("package.json not found", "package.json")
        return 1
    except ValueError as exc:
        _common.error(f"package.json is not valid JSON: {exc}", "package.json")
        return 1

    for key in REQUIRED:
        if not package.get(key):
            problems.append(f"missing or empty field '{key}'")

    if package.get("name") != EXPECTED_NAME:
        problems.append(f"name must be '{EXPECTED_NAME}', got '{package.get('name')}'")
    version = package.get("version", "")
    if not _common.SEMVER.match(str(version)):
        problems.append(f"version '{version}' is not semantic versioning")
    if package.get("unity") != EXPECTED_UNITY:
        problems.append(f"unity must be '{EXPECTED_UNITY}', got '{package.get('unity')}'")
    if package.get("license") != "MIT":
        problems.append(f"license must be 'MIT', got '{package.get('license')}'")

    author = package.get("author")
    if not isinstance(author, dict) or not author.get("name") or not author.get("email"):
        problems.append("author must be an object with 'name' and 'email'")

    dependencies = package.get("dependencies")
    if not isinstance(dependencies, dict):
        dependencies = {}
    for name, wanted in EXPECTED_DEPENDENCIES.items():
        if dependencies.get(name) != wanted:
            problems.append(f"dependency '{name}' must be '{wanted}', got '{dependencies.get(name)}'")

    testables = package.get("testables")
    if not isinstance(testables, list) or EXPECTED_NAME not in testables:
        problems.append(f"testables must list '{EXPECTED_NAME}'")

    samples = package.get("samples", [])
    if not isinstance(samples, list):
        problems.append("samples must be an array")
        samples = []
    for sample in samples:
        sample_path = sample.get("path") if isinstance(sample, dict) else None
        if not sample_path or not os.path.isdir(os.path.join(root, sample_path)):
            problems.append(f"sample path '{sample_path}' does not exist")

    if not os.path.isfile(os.path.join(root, "LICENSE")):
        problems.append("LICENSE file is missing")

    if problems:
        for problem in problems:
            _common.error(problem, "package.json")
        print(f"check_package_json: FAIL, {len(problems)} problem(s)")
        return 1

    print(
        f"check_package_json: OK, {package['name']}@{version}, "
        f"{len(samples)} sample(s), {len(dependencies)} dependencies"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

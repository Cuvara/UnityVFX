#!/usr/bin/env python3
"""Fail unless the game-ci runner produced NUnit result XML with at least one test and no failures.

A green run that executed zero tests proves nothing, so zero is a failure.

Usage: assert_test_results.py <artifacts dir> [--label <text>]
"""

import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("artifacts", help="directory that holds the result XML (game-ci: artifacts/)")
    parser.add_argument("--label", default="tests")
    args = parser.parse_args()

    files = sorted(glob.glob(os.path.join(args.artifacts, "**", "*.xml"), recursive=True))
    if not files:
        print(f"::error::No {args.label} result XML was produced in {args.artifacts}; nothing was verified.")
        return 1

    total = passed = failed = errors = inconclusive = skipped = 0
    for path in files:
        try:
            run = ET.parse(path).getroot()
        except ET.ParseError as exc:
            print(f"::error::{path} is not parseable XML: {exc}")
            return 1
        if run.tag != "test-run":
            continue

        def count(key, run=run):
            return int(run.get(key) or 0)

        total += count("total")
        passed += count("passed")
        failed += count("failed")
        errors += count("errors")
        inconclusive += count("inconclusive")
        skipped += count("skipped")

    print(
        f"{args.label}: total={total} passed={passed} failed={failed} errors={errors} "
        f"inconclusive={inconclusive} skipped={skipped}"
    )
    if total == 0:
        print(f"::error::Zero {args.label} were executed. Check that the package resolves and its test assembly loads.")
        return 1
    if failed or errors:
        print(f"::error::{failed} failed and {errors} errored out of {total} {args.label}.")
        return 1
    print(f"::notice::{passed}/{total} {args.label} passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

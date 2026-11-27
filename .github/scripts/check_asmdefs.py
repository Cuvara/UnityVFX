#!/usr/bin/env python3
"""Every *.asmdef must parse, and its file name must equal its "name".

Exception: Editor/Bridge/Unity.Testing.VisualEffectGraph.Editor.asmdef is the only assembly that
does not use the Cuvara.VFX.* name (see Documentation~/DESIGN.md, "VFX Graph internals"); its file
name still has to equal its name.

Passes when the package has no asmdef yet.

Usage: check_asmdefs.py [--root <package root>]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402

BRIDGE = "Unity.Testing.VisualEffectGraph.Editor"


def main():
    root = _common.parse_args("Check asmdef names")
    asmdefs = [p for p in _common.list_files(root) if p.endswith(".asmdef") and "/." not in "/" + p]
    problems = 0

    for path in asmdefs:
        file_name = os.path.basename(path)[: -len(".asmdef")]
        try:
            name = _common.load_json(os.path.join(root, path)).get("name")
        except ValueError as exc:
            problems += 1
            _common.error(f"not valid JSON: {exc}", path)
            continue
        if name != file_name:
            problems += 1
            _common.error(f"asmdef file name '{file_name}' must equal its name '{name}'", path)
        elif not (name.startswith("Cuvara.VFX") or name == BRIDGE):
            problems += 1
            _common.error(f"assembly '{name}' must be named Cuvara.VFX* (only {BRIDGE} is exempt)", path)

    if problems:
        print(f"check_asmdefs: FAIL, {problems} problem(s) in {len(asmdefs)} asmdef(s)")
        return 1
    print(f"check_asmdefs: OK, {len(asmdefs)} asmdef(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

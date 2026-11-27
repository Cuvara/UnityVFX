#!/usr/bin/env python3
"""Validate every committed *.vfxrecipe.json against Schemas~/vfx-recipe.schema.json.

Passes when the schema does not exist yet or when there are no recipes (the schema and the first
recipes arrive with the package core). Fails on an unparseable schema, an unparseable recipe, or a
recipe that does not satisfy the schema. Needs the `jsonschema` package once there is something to
validate: `pip install jsonschema`.

Usage: check_recipes.py [--root <package root>]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402

SCHEMA_PATH = "Schemas~/vfx-recipe.schema.json"
SUFFIX = ".vfxrecipe.json"


def main():
    root = _common.parse_args("Validate committed recipes against the recipe schema")
    recipes = [p for p in _common.list_files(root) if p.endswith(SUFFIX)]
    schema_file = os.path.join(root, SCHEMA_PATH)

    if not os.path.isfile(schema_file):
        print(f"check_recipes: SKIP, {SCHEMA_PATH} does not exist yet ({len(recipes)} recipe(s) found, not validated)")
        return 0
    if not recipes:
        print("check_recipes: OK, 0 recipe(s) to validate")
        return 0

    try:
        import jsonschema
    except ImportError:
        _common.error("the 'jsonschema' package is required: pip install jsonschema")
        return 2

    try:
        schema = _common.load_json(schema_file)
    except ValueError as exc:
        _common.error(f"{SCHEMA_PATH} is not valid JSON: {exc}", SCHEMA_PATH)
        return 1
    try:
        validator_class = jsonschema.validators.validator_for(schema)
        validator_class.check_schema(schema)
    except jsonschema.SchemaError as exc:
        _common.error(f"{SCHEMA_PATH} is not a valid JSON Schema: {exc.message}", SCHEMA_PATH)
        return 1
    validator = validator_class(schema)

    failures = 0
    for recipe in recipes:
        try:
            document = _common.load_json(os.path.join(root, recipe))
        except ValueError as exc:
            failures += 1
            _common.error(f"not valid JSON: {exc}", recipe)
            continue
        problems = sorted(validator.iter_errors(document), key=lambda e: list(e.absolute_path))
        for problem in problems:
            failures += 1
            location = "/".join(str(p) for p in problem.absolute_path) or "(root)"
            _common.error(f"{location}: {problem.message}", recipe)

    if failures:
        print(f"check_recipes: FAIL, {failures} problem(s) in {len(recipes)} recipe(s)")
        return 1
    print(f"check_recipes: OK, {len(recipes)} recipe(s) valid against {SCHEMA_PATH}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

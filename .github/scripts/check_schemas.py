#!/usr/bin/env python3
"""Every JSON Schema under Schemas~/ is a valid draft 2020-12 schema.

The schemas are the contract that rpg-factory scripts and skills read (recipe, describe, inspect,
capture stats). A schema that does not parse, that declares another draft, or that is not a valid
schema of its draft would make every validation that uses it meaningless, so it fails here first.

Checks, for every `Schemas~/*.schema.json`:
- the file parses as JSON and is an object;
- `$schema` is `https://json-schema.org/draft/2020-12/schema`;
- `$id` is present and unique across the schemas;
- `Draft202012Validator.check_schema` accepts it (needs `pip install jsonschema`).

Fails when Schemas~/ holds no schema at all. Usage: check_schemas.py [--root <package root>]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402

SCHEMA_DIR = "Schemas~"
SUFFIX = ".schema.json"
DRAFT_2020_12 = "https://json-schema.org/draft/2020-12/schema"


def main():
    root = _common.parse_args("Check that every JSON Schema under Schemas~/ is a valid draft 2020-12 schema")
    schemas = [p for p in _common.list_files(root) if p.startswith(SCHEMA_DIR + "/") and p.endswith(SUFFIX)]
    if not schemas:
        _common.error(f"no *{SUFFIX} file under {SCHEMA_DIR}/")
        return 1

    try:
        from jsonschema import Draft202012Validator, SchemaError
    except ImportError:
        _common.error("the 'jsonschema' package is required: pip install jsonschema")
        return 2

    failures = 0
    ids = {}
    for path in schemas:
        try:
            schema = _common.load_json(os.path.join(root, path))
        except ValueError as exc:
            failures += 1
            _common.error(f"not valid JSON: {exc}", path)
            continue
        if not isinstance(schema, dict):
            failures += 1
            _common.error("a schema must be a JSON object", path)
            continue
        if schema.get("$schema") != DRAFT_2020_12:
            failures += 1
            _common.error(f"$schema must be '{DRAFT_2020_12}', got '{schema.get('$schema')}'", path)
        schema_id = schema.get("$id")
        if not isinstance(schema_id, str) or not schema_id:
            failures += 1
            _common.error("$id is missing", path)
        elif schema_id in ids:
            failures += 1
            _common.error(f"$id '{schema_id}' is also used by {ids[schema_id]}", path)
        else:
            ids[schema_id] = path
        try:
            Draft202012Validator.check_schema(schema)
        except SchemaError as exc:
            failures += 1
            _common.error(f"not a valid draft 2020-12 schema: {exc.message}", path)

    if failures:
        print(f"check_schemas: FAIL, {failures} problem(s) in {len(schemas)} schema(s)")
        return 1
    print(f"check_schemas: OK, {len(schemas)} schema(s) are valid draft 2020-12 schemas")
    return 0


if __name__ == "__main__":
    sys.exit(main())

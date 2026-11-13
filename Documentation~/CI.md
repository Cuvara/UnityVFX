# CI

Workflow: `.github/workflows/ci.yml`, triggered on push to `main` and `develop` and on pull
requests to `main`. Same shape as `Cuvara/UIToolkit` and `Cuvara/UnityDots`: a `validate` job that
needs no Unity licence, plus game-ci Unity jobs.

## Jobs

| Job | Needs | Runs |
|---|---|---|
| `validate` | nothing (no Unity, no GPU, no secret) | the five validation scripts below and a YAML parse of every workflow file |
| `licence` | nothing | turns the presence of the `UNITY_LICENSE` secret into the output `present` |
| `editmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, EditMode, project `Sandbox~` |
| `playmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, PlayMode, project `Sandbox~` |

A secret cannot be read in a job-level `if`, so the `licence` job reads it into a step `env` and
publishes a boolean. When the secret is absent (a fork, or a repository that has not been given it
yet) `editmode` and `playmode` are **skipped**, not failed, and `validate` still gates the change.

The Unity jobs:

- Use `Sandbox~` as `projectPath`. The Sandbox references the package as `file:../..` and lists it
  in `testables`, so the package's `Tests/Editor` and `Tests/Runtime` assemblies run.
- Run `Tools~/setup-sandbox.py` first when it exists. It creates the gitignored
  `Sandbox~/Assets/Showcase` link to `Samples~/Showcase`.
- Cache `Sandbox~/Library`, keyed on the Unity version, the Sandbox manifest and `package.json`.
- Finish with `.github/scripts/assert_test_results.py`, which fails on zero tests, a failure or an
  error. A green run that executed nothing is not a pass.
- Upload the NUnit XML as an artifact.

Secrets used: `UNITY_LICENSE` (required for the jobs to run), `UNITY_EMAIL`, `UNITY_PASSWORD`.

Limits: a hosted runner has no GPU. Tests that need a real render or a GPU readback (capture,
`aliveParticleCount`) cannot give a meaningful result there. Those tests are run locally against the
Sandbox with `Tools~/vfxcli.py` and the Unity Test Runner; the pipeline's licence-free gate is
`validate`. The game-ci image for `6000.6.9f1` must exist; if the runner reports that the editor
image cannot be pulled, the job fails and the pin in `UNITY_VERSION` is the thing to look at.

## Validation scripts

All scripts are plain Python 3 (standard library only, except `check_recipes.py`, which needs
`pip install jsonschema` once there is a recipe to validate). Run them from the repository root.
Each accepts `--root <dir>` to check another copy of the package, and exits 0 on success, 1 on a
failed check and 2 when a dependency is missing.

| Script | Checks |
|---|---|
| `check_package_json.py` | name `com.cuvara.vfx`, semver `version`, `displayName`, `description`, `unity` `6000.6`, `license` `MIT`, `author` name and email, dependencies `com.unity.visualeffectgraph` and `com.unity.render-pipelines.universal` both `17.6.0`, `testables`, `LICENSE` present, every declared sample path exists |
| `check_changelog.py` | `## [Unreleased]` exists and is first; released headings read `## [x.y.z] - YYYY-MM-DD`; `package.json` version is the newest released heading or newer |
| `check_metas.py` | every tracked or untracked-not-ignored Unity-visible file and folder has a `.meta`, and no `.meta` is orphaned. Dot paths and any path with a `~` segment are skipped, as Unity skips them |
| `check_asmdefs.py` | every `.asmdef` parses, its file name equals its `name`, and the name is `Cuvara.VFX*` (only `Unity.Testing.VisualEffectGraph.Editor` is exempt) |
| `check_recipes.py` | every `*.vfxrecipe.json` validates against `Schemas~/vfx-recipe.schema.json`. Passes while the schema or the recipes do not exist yet; fails on an invalid recipe once both exist |
| `assert_test_results.py <dir>` | used by the Unity jobs: NUnit XML exists, total greater than zero, no failure, no error |

Run all of them locally:

```
pip install jsonschema
python .github/scripts/check_package_json.py
python .github/scripts/check_changelog.py
python .github/scripts/check_metas.py
python .github/scripts/check_asmdefs.py
python .github/scripts/check_recipes.py
```

On Windows use the interpreter that `py` or your Python install provides; on a machine where
`python3` is the Microsoft Store alias, call the interpreter by its full path.

The file listing is `git ls-files --cached --others --exclude-standard`, so the scripts give the same
answer on a clean CI checkout and in a working tree that has not been committed yet. Outside a git
checkout (a temporary copy) they walk the directory.

### Prove a gate can fail

A check that cannot fail proves nothing. Break a copy on purpose and expect exit code 1:

```
cp -r . /tmp/vfx-copy && rm -rf /tmp/vfx-copy/.git
sed -i 's/"unity": "6000.6"/"unity": "6000.3"/' /tmp/vfx-copy/package.json
python .github/scripts/check_package_json.py --root /tmp/vfx-copy      # exit 1
sed -i 's/\[Unreleased\]/[Next]/' /tmp/vfx-copy/CHANGELOG.md
python .github/scripts/check_changelog.py --root /tmp/vfx-copy         # exit 1
rm /tmp/vfx-copy/LICENSE.meta
python .github/scripts/check_metas.py --root /tmp/vfx-copy             # exit 1
```

For `check_recipes.py`, put a schema in `Schemas~/` and a recipe that violates it in a copy.

## Adding a gate

Add a script under `.github/scripts/` that takes `--root` (use `_common.parse_args`), add a `validate`
step for it, add its row to the table above, and prove it can fail on a broken copy.

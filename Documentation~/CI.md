# CI

Workflow: `.github/workflows/ci.yml`, triggered on push to `main` and `develop` and on pull
requests to `main`. Same shape as `Cuvara/UIToolkit` and `Cuvara/UnityDots`: a `validate` job that
needs no Unity licence, plus game-ci Unity jobs that check the repository out under `package/`.

## Jobs

| Job | Needs | Runs |
|---|---|---|
| `validate` | nothing (no Unity, no GPU, no secret) | the six validation scripts below, a `py_compile` of `Tools~/*.py` and `.github/scripts/*.py`, and a YAML parse of every workflow file |
| `licence` | nothing | turns the presence of the `UNITY_LICENSE` secret into the output `present` |
| `editmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, `testMode: EditMode`, project `sandbox-home/Sandbox` (a copy of `Sandbox~`) |
| `playmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, `testMode: PlayMode`, project `sandbox-home/Sandbox` |

A secret cannot be read in a job-level `if`, so the `licence` job reads it into a step `env` and
publishes a boolean. When the secret is absent (a fork, or a repository that has not been given it
yet) `editmode` and `playmode` are **skipped**, not failed, and `validate` still gates the change.

Secrets used: `UNITY_LICENSE` (required for the Unity jobs to run), `UNITY_EMAIL`, `UNITY_PASSWORD`.
`UNITY_VERSION` in the workflow pins the editor image (`6000.6.9f1`).

### The Sandbox in CI

The Unity jobs test the package through the Sandbox project, which lists `com.cuvara.vfx` in
`testables`, so the package's `Tests/Editor` (`Cuvara.VFX.Editor.Tests`) and `Tests/Runtime`
(`Cuvara.VFX.Tests`) assemblies run. They never open the project at `Sandbox~`:

- Unity 6000.6 cannot map package scripts to classes when the project path has a component that
  ends in `~` or starts with `.` (AUTHORING.md, Pitfalls, "Package scripts have no class when the
  project path has a hidden component").
- `Sandbox~/Packages/manifest.json` references the package as `file:../../package`, relative to the
  project's `Packages` folder. On a workstation that resolves through the sandbox home that
  `Tools~/setup-sandbox.py` makes with links (AUTHORING.md, "Sandbox").

A runner has no Unity installation for `setup-sandbox.py` to check, and the game-ci container sees
only the workspace, so the jobs make the same layout with copies and no link:

| Path in the workspace | What it is |
|---|---|
| `sandbox-home/package` | the checkout (`actions/checkout` with `path: sandbox-home/package`) |
| `sandbox-home/Sandbox` | a copy of `Sandbox~` without `Library`, `Temp`, `Logs`, `UserSettings`, `obj`, `Captures`; the `projectPath` of game-ci |
| `sandbox-home/Sandbox/Assets/Showcase` | a copy of `Samples~/Showcase`, only when it exists |

`.github/scripts/ci_sandbox.py sandbox-home` makes it. It fails when the package is not at
`<home>/package`, when the home is inside the repository that holds the script, when the manifest
does not reference `file:../../package`, when the project path has a hidden component, when
`Sandbox~` or `Samples~/Showcase` holds a link, or when `<home>/Sandbox` exists already. It creates
no link and deletes nothing. The copy carries the committed `Assets/Plugins/NuGet` DLLs the Unity-MCP
plugin needs to compile; if they ever leave the repository (`THIRD-PARTY.md`), a Unity job needs a restore step
first (`setup-sandbox.py --restore-nuget` needs an editor with a display and network access; not tried in CI).

The Unity jobs then:

- Cache `sandbox-home/Sandbox/Library`, keyed on the Unity version, the Sandbox manifest and lock
  file and `package.json`.
- Finish with `.github/scripts/assert_test_results.py`, which fails on zero tests, a failure or an
  error. A green run that executed nothing is not a pass.
- Upload the NUnit XML as an artifact (`editmode-results`, `playmode-results`).

### Limits

- The Unity jobs have not run yet: the repository has no remote and no `UNITY_LICENSE` secret. The
  layout script was checked locally on a copy of the tracked files (exit 0; a second run, a home
  without a package and a home inside the repository each exit 1).
- A hosted runner can have no GPU. Tests that need a real render or a GPU readback (`CaptureTests`,
  anything that reads `aliveParticleCount`) cannot give a meaningful result there. They are run
  locally against the Sandbox with the Unity Test Runner through Unity-MCP (AUTHORING.md, "Open,
  watch, check, drive, close"); the licence-free gate is `validate`. See "GPU tests" below.

### GPU tests

Rule: a test that simulates an effect on the GPU or reads a render target back calls
`GraphicsRequirement.IgnoreUnlessGpuSimulationRuns()` (`Tests/Editor/GraphicsRequirement.cs`)
before it does anything else, and before it enters play mode. When `SystemInfo.graphicsDeviceType`
is `Null` (no graphics device, for example `-nographics`) or `SystemInfo.supportsComputeShaders` is
false, the test ends as **Ignored**, and the reason names the missing device or feature. It is never
a silent pass. It runs and must pass where a GPU is present, as in the local Sandbox.

- Guarded today: `CaptureTests.SameSeedTwice_GivesTheSamePeak_NoMinusOne_AndAllFiles`. The other
  `CaptureTests` check arguments in edit mode and need no GPU.
- Not guarded: the build, describe and inspect tests (the VFX Graph compile and the shader import
  run without a GPU), and the PlayMode tests (their `VisualEffect` components have no asset, so
  nothing is simulated).
- In a game-ci run, read the skipped count that `assert_test_results.py` prints. Each skipped test
  is an Ignored GPU test, and its NUnit XML holds the reason. A skipped count above the number of
  guarded tests is a problem to look at.
- Add the guard to every new test that needs a capture, a readback or `aliveParticleCount`.
- The Sandbox holds the Unity-MCP plugin. `Sandbox~/UserSettings` is gitignored, so in CI the
  plugin starts without the local configuration that `setup-sandbox.py` writes (AUTHORING.md,
  Pitfalls, "The Unity-MCP plugin starts in Cloud mode"). Whether it logs an error that fails a
  test run in batch mode is not known yet; look there first if the first licensed run fails outside
  the package's tests.
- The game-ci image for `6000.6.9f1` must exist. If the runner reports that the editor image cannot
  be pulled, the job fails and the pin in `UNITY_VERSION` is the thing to look at.

## Validation scripts

All scripts are plain Python 3 (standard library only, except `check_schemas.py` and
`check_recipes.py`, which need `pip install jsonschema`). Run them from the repository root. Each
accepts `--root <dir>` to check another copy of the package, and exits 0 on success, 1 on a failed
check and 2 when a dependency is missing.

| Script | Checks |
|---|---|
| `check_package_json.py` | name `com.cuvara.vfx`, semver `version`, `displayName`, `description`, `unity` `6000.6`, `license` `MIT`, `author` name and email, dependencies `com.unity.visualeffectgraph` and `com.unity.render-pipelines.universal` both `17.6.0`, `testables`, `LICENSE` present, every declared sample path exists |
| `check_changelog.py` | `## [Unreleased]` exists and is first; released headings read `## [x.y.z] - YYYY-MM-DD`; `package.json` version is the newest released heading or newer |
| `check_metas.py` | every tracked or untracked-not-ignored Unity-visible file and folder has a `.meta`, and no `.meta` on a Unity-visible path is orphaned. Paths with a component that starts with `.` or ends in `~` are not Unity-visible and are skipped in both passes, as Unity skips them: `check_metas.py` does not judge the `.meta` files under dot or `~` paths, such as those of the nested `Sandbox~` project and of `Samples~`, which belong to the project that imports them |
| `check_asmdefs.py` | every `.asmdef` parses, its file name equals its `name`, and the name is `Cuvara.VFX*` (only `Unity.Testing.VisualEffectGraph.Editor` is exempt) |
| `check_schemas.py` | every `Schemas~/*.schema.json` parses, declares `$schema` `https://json-schema.org/draft/2020-12/schema`, has a unique `$id`, and passes `Draft202012Validator.check_schema`; fails when `Schemas~/` holds no schema |
| `check_recipes.py` | the recipe schema is a valid JSON Schema and every `*.vfxrecipe.json` validates against `Schemas~/vfx-recipe.schema.json`. Passes while the schema or the recipes do not exist; fails on an invalid recipe once both exist |
| `ci_sandbox.py <home>` | used by the Unity jobs: lays out `<home>/Sandbox` (see "The Sandbox in CI") |
| `assert_test_results.py <dir>` | used by the Unity jobs: NUnit XML exists, total greater than zero, no failure, no error |

Run the `validate` checks locally:

```
pip install jsonschema pyyaml
python .github/scripts/check_package_json.py
python .github/scripts/check_changelog.py
python .github/scripts/check_metas.py
python .github/scripts/check_asmdefs.py
python .github/scripts/check_schemas.py
python .github/scripts/check_recipes.py
```

On Windows use the interpreter that `py` or your Python install provides; on a machine where
`python3` is the Microsoft Store alias, call the interpreter by its full path.

The file listing is `git ls-files --cached --others --exclude-standard`, so the scripts give the same
answer on a clean CI checkout and in a working tree that has not been committed yet. Outside a git
checkout (a temporary copy) they walk the directory.

### Prove a gate can fail

A check that cannot fail proves nothing. Break a copy on purpose and expect exit code 1. Make the
copy from the tracked files (never with a recursive copy of a working tree that may hold the
Sandbox links), and remove it afterwards only after checking that it holds no link:

```
git archive --format=tar --prefix=vfx-copy/ HEAD | tar -xf - -C /tmp
sed -i 's/"unity": "6000.6"/"unity": "6000.3"/' /tmp/vfx-copy/package.json
python .github/scripts/check_package_json.py --root /tmp/vfx-copy      # exit 1
sed -i 's/\[Unreleased\]/[Next]/' /tmp/vfx-copy/CHANGELOG.md
python .github/scripts/check_changelog.py --root /tmp/vfx-copy         # exit 1
rm /tmp/vfx-copy/LICENSE.meta
python .github/scripts/check_metas.py --root /tmp/vfx-copy             # exit 1
sed -i 's#draft/2020-12#draft/2019-09#' /tmp/vfx-copy/Schemas~/vfx-inspect.schema.json
python .github/scripts/check_schemas.py --root /tmp/vfx-copy           # exit 1
```

For `check_recipes.py`, add a recipe that violates the schema to a copy (an unknown op, or an `id`
that is not kebab-case).

## Adding a gate

Add a script under `.github/scripts/` that takes `--root` (use `_common.parse_args`), add a `validate`
step for it, add its row to the table above, and prove it can fail on a broken copy.

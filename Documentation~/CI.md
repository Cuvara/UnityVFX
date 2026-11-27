# CI

Workflow: `.github/workflows/ci.yml`, triggered on push to `main` and `develop` and on pull
requests to `main`. Same shape as `Cuvara/UIToolkit` and `Cuvara/UnityDots`: a `validate` job that
needs no Unity licence, plus game-ci Unity jobs that check the repository out under `package/`.

## Jobs

| Job | Needs | Runs |
|---|---|---|
| `validate` | nothing (no Unity, no GPU, no secret) | the six validation scripts below, a `py_compile` of `Tools~/*.py` and `.github/scripts/*.py`, and a YAML parse of every workflow file |
| `licence` | nothing | turns the presence of the `UNITY_LICENSE` secret into the output `present` |
| `editmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, `testMode: EditMode`, `customParameters: -testCategory !GPU`, project `sandbox-home/Sandbox` (a copy of `Sandbox~`) |
| `playmode` | `licence`, runs only when `present == 'true'` | game-ci `unity-test-runner`, `testMode: PlayMode`, `customParameters: -testCategory !GPU`, project `sandbox-home/Sandbox` |

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
- Finish with `.github/scripts/assert_test_results.py --excluded-category GPU`, which fails on zero
  tests, a failure or an error (a green run that executed nothing is not a pass) and writes the
  counts to the job summary (see "GPU tests").
- Upload the NUnit XML as an artifact (`editmode-results`, `playmode-results`).

### What CI runs and what only the Sandbox runs

The hosted runner has no GPU. Every test that needs VFX Graph compute simulation or a capture is in
the NUnit category `GPU`; both Unity jobs exclude that category, so **CI runs**:

| Job | CI runs (`-testCategory !GPU`) | Only the Sandbox runs (category `GPU`) |
|---|---|---|
| `editmode` | 126 tests | 3 tests |
| `playmode` | 82 tests | 7 tests |

The numbers are from the local Sandbox, 2026-11-27 (see "Local evidence"). The job summary that
`assert_test_results.py` writes shows the counts each job really ran and names the excluded category.

The GPU category holds:

- EditMode (3): `CaptureTests.SameSeedTwice_GivesTheSamePeak_NoMinusOne_AndAllFiles`,
  `CaptureTests.AnotherLoadedScene_DoesNotChangeTheCapture_AndIsRestoredAfterwards` and
  `CaptureCastTests.StandInWithThreeTargets_BindsTheChain_TwiceTheSame_AndAMissingPrefabPlacesThePlaceholder`.
  The other tests of those fixtures check arguments in edit mode and need no GPU.
- PlayMode (7): the whole fixture `Cuvara.VFX.Sandbox.ShowcaseTests.ShowcaseSmokeTests`
  (`Sandbox~/Assets/Tests`): every test loads the Showcase scene, whose stations play VFX Graph
  effects. `.github/scripts/ci_sandbox.py` copies the whole Sandbox, including `Assets/Tests`, and
  `Samples~/Showcase` into the CI project, so these smoke tests are in the CI PlayMode project and
  would run there; the category is what keeps them out. The seven tests are
  `Scene_LoadsAndPlays_WithNoErrorOrException`,
  `Scene_HasTheFourStations_AndTheLaneHasACasterAndThreeTargets3To4MetresApart`,
  `EveryCharacter_HasTheFiveSockets`,
  `SpawnPoint_WithoutAPrefab_PlacesThePlaceholderWithTheFiveSockets`,
  `CameraPresets_ThroughTheOverlay_PlaceTheCameraAtTheSharedPresetForTheSelectedStation`,
  `LightingPresets_ThroughTheOverlay_EqualTheSharedPresetData` and
  `FixtureStation_HasAliveParticles_AfterItsTrigger`.

The build, describe and inspect tests (the VFX Graph compile and the shader import run without a GPU)
and the other PlayMode tests (their `VisualEffect` components have no asset, so nothing is simulated)
are not in the category and run in CI.

### Limits

- Run 45974445050 (PR #1, `Cuvara/UnityVFX`), before the GPU category existed, showed why the
  exclusion is needed: the EditMode job crashed the editor natively in `VFXManager::Update` ->
  `VFX::DispatchCompute` (no result XML), and the PlayMode job reported 89 total, 88 passed, 1
  failed (`ShowcaseSmokeTests.FixtureStation_HasAliveParticles_AfterItsTrigger`, "BurstQuads has alive
  particles after the station's trigger, Expected: greater than 0").
- The runner renders with Mesa llvmpipe (OpenGL Core 4.5), a software rasteriser that **claims**
  compute shader support. A check on `SystemInfo.supportsComputeShaders` alone passes there, and VFX
  Graph then crashes the editor in `VFX::DispatchCompute`. The guard below also checks the device name.
- CI never runs the GPU category: the capture, the Showcase scene and the particle counts are checked
  only where a real GPU is present, in the local Sandbox (AUTHORING.md, "Open, watch, check, drive,
  close"). The licence-free gate is `validate`.
- The GPU category needs an editor that renders. In `-batchmode` no camera renders, so a `VisualEffect`
  is most likely culled and never simulates: the Showcase particle test
  (`FixtureStation_HasAliveParticles_AfterItsTrigger`) saw 0 alive particles in a batch run on the
  real GPU (PlayMode, all categories: 88 of 89 passed, 1 failed). The three EditMode capture tests
  passed in batch mode. Run the GPU category in an opened editor through Unity-MCP (`tests-run`), not
  in `-batchmode`; there the same PlayMode run passed 89 of 89.
- The Sandbox holds the Unity-MCP plugin. `Sandbox~/UserSettings` is gitignored, so in CI the plugin
  starts without the local configuration that `setup-sandbox.py` writes (AUTHORING.md, Pitfalls, "The
  Unity-MCP plugin starts in Cloud mode"). Look there first if a licensed run fails outside the
  package's tests.
- The game-ci image for `6000.6.9f1` must exist. If the runner reports that the editor image cannot
  be pulled, the job fails and the pin in `UNITY_VERSION` is the thing to look at.

### GPU tests

Rule: every new test that reaches VFX simulation, a capture or `aliveParticleCount` gets the category
**and** the guard:

```csharp
[Category(GraphicsRequirement.Category)]               // "GPU"
...
GraphicsRequirement.IgnoreUnlessGpuSimulationRuns();   // first statement, before play mode or a scene load
```

`GraphicsRequirement` (`Tests/Runtime/GraphicsRequirement.cs`, namespace and assembly
`Cuvara.VFX.Tests`) holds the const `Category` (`"GPU"`) and `SkipVariable`. The Editor test assembly
and the Sandbox Showcase test assembly reference `Cuvara.VFX.Tests`. A fixture whose every test needs
the GPU (`ShowcaseSmokeTests`) carries the category on the class and runs the guard in `SetUp`, so the
scene never loads.

The guard ends the test as **Ignored**, with the reason, never as a silent pass and never as a crash,
when any of these is true:

- the environment variable `CUVARA_VFX_SKIP_GPU_TESTS` is `1` or `true` (any case): forces the skip
  whatever the device;
- `SystemInfo.graphicsDeviceType` is `Null` (no graphics device, for example `-nographics`);
- `SystemInfo.supportsComputeShaders` is false;
- the device name or vendor contains a software rasteriser name (case-insensitive): `llvmpipe`,
  `softpipe`, `lavapipe`, `SwiftShader`, `Microsoft Basic Render`.

The reason names the device, for example: `skipped on request: the environment variable
CUVARA_VFX_SKIP_GPU_TESTS is '1' (graphics device Direct3D12 'NVIDIA GeForce RTX 5060' (vendor
'NVIDIA')); the GPU tests run only where VFX Graph can simulate on a GPU`.

CI keeps the category out in two layers: both Unity jobs pass `customParameters: -testCategory !GPU`
(the value is deliberately unquoted; the comment in `ci.yml` explains how game-ci v4.3.2 splits it),
and the guard ignores any GPU test that a run without the exclusion still reaches on a software device.

Job summary: `assert_test_results.py <dir> --label <text> --excluded-category GPU` prints
`<label>: total=... passed=... failed=... errors=... inconclusive=... skipped=...`, fails on zero
tests, a failure or an error, and, when `GITHUB_STEP_SUMMARY` is set, appends a table of the counts and
the sentence that the `GPU` category is excluded in CI and runs only in the local Sandbox. A skipped
count above 0 in a CI run is a test outside the category that called `Assert.Ignore`: read its reason
in the NUnit XML.

### Local evidence

Sandbox, Unity 6000.6.9f1, Windows, Direct3D12 on an NVIDIA GeForce RTX 5060, 2026-11-27. "GPU tests"
is the number of GPU-category tests in the run. No GPU test was Ignored locally without the skip variable.

| Run | How | Total | Passed | Failed | Skipped / Ignored | GPU tests |
|---|---|---:|---:|---:|---:|---:|
| EditMode, all categories | `-batchmode` | 129 | 129 | 0 | 0 | 3 |
| PlayMode, all categories | `-batchmode` | 89 | 88 | 1 | 0 | 7 |
| PlayMode, all categories | opened editor, Unity-MCP `tests-run` | 89 | 89 | 0 | 0 | 7 |
| EditMode, `-testCategory !GPU` | `-batchmode` | 126 | 126 | 0 | 0 | 0 |
| PlayMode, `-testCategory !GPU` | `-batchmode` | 82 | 82 | 0 | 0 | 0 |
| PlayMode, `-testCategory GPU`, `CUVARA_VFX_SKIP_GPU_TESTS=1` | `-batchmode` | 7 | 0 | 0 | 7 Ignored | 7 |
| EditMode, `-testCategory GPU`, `CUVARA_VFX_SKIP_GPU_TESTS=1` | `-batchmode` | 3 | 0 | 0 | 3 Ignored | 3 |

- The batch runs with an exclusion used the exact CI argument `-testCategory !GPU`: 129 - 126 = 3 and
  89 - 82 = 7, so the GPU tests dropped out.
- The one failure is the batch-mode limit above: 0 alive particles with no rendering camera.
- With `CUVARA_VFX_SKIP_GPU_TESTS=1` every GPU test ended Ignored (never Failed or Error) with the
  reason above, in both modes.

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
| `assert_test_results.py <dir> [--label <text>] [--excluded-category <name>]` | used by the Unity jobs: NUnit XML exists, total greater than zero, no failure, no error; prints the counts and writes them to the job summary (`GITHUB_STEP_SUMMARY`), naming the excluded category as Sandbox-only |

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

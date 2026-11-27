# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Package

- `package.json` (`com.cuvara.vfx` 0.1.0, Unity 6000.6, dependencies `com.unity.visualeffectgraph`
  17.6.0 and `com.unity.render-pipelines.universal` 17.6.0, `testables`), MIT licence, README,
  `.gitignore` and `.gitattributes`.
- `package.json` `files` (`["*", "!Sandbox~"]`): an npm or OpenUPM tarball leaves out `Sandbox~` (133
  entries, the 45 Unity-MCP NuGet DLLs, 17.8 MiB); checked with `npm pack --dry-run` in a copy outside
  the repository: 349 entries before, 217 after. A Git URL install still downloads the whole
  repository (`THIRD-PARTY.md`).

#### Runtime (assembly `Cuvara.VFX`)

- References `Unity.VisualEffectGraph.Runtime`, `Unity.RenderPipelines.Core.Runtime` and
  `Unity.RenderPipelines.Universal.Runtime` (the lighting presets set URP Volume components); no
  Entities, netcode, VContainer or other Cuvara package.
- `VfxLibrary` (ScriptableObject): effect id to prefab, duration and tags; ordinal lookup;
  `Validate` reports empty and duplicate ids, missing prefabs, prefabs without a `VisualEffect` and
  negative durations.
- `VfxSocket`: a named attach point with the well-known names `head`, `chest`, `hand_l`, `hand_r`,
  `feet` and a lookup under a root.
- `VfxPlayer`: plays an effect by id at a position, on a socket or under a transform, one-shot or
  looped, pooled per effect id, with no managed allocation per play once the pool is warm;
  `VfxHandle` stops one instance.
- `ChainLightningBinder`: writes `SourcePosition`, `TargetPosition0` to `TargetPosition7` and
  `HopCount` into a `VisualEffect` every frame; at most 8 targets; null, destroyed and inactive
  targets skipped; missing properties skipped silently.
- `VfxLightingPreset` (`day`, `dusk` default, `night`: key and fill light, trilight ambient,
  background, Bloom, ACES, vignette, post exposure) and `VfxCameraPreset` (`gameplay` default, about
  45 degrees at 10 m; `close`; `side`), shared by the capture stage and the Showcase.
- `Runtime/Showcase/`, shared by the Showcase scene and the capture stage:
  - `VfxStation`: a label, an effect id of the player's library (changeable at run time), an anchor
    (a transform or a `VfxSocket` under it), targets (transforms or sockets under them) and a
    trigger (`Manual`, `Interval` in seconds on scaled time, or `Key`); `PlayNow` plays through
    `VfxPlayer` and binds a `ChainLightningBinder` of the played instance to the anchor and the
    valid targets (up to 8); no allocation per frame in `Update`.
  - The `Key` trigger reads the Input System when the project has `com.unity.inputsystem` and its
    active input handling includes it (version define `CUVARA_VFX_INPUT_SYSTEM` plus
    `ENABLE_INPUT_SYSTEM`), else the legacy Input Manager (`ENABLE_LEGACY_INPUT_MANAGER`). The
    runtime asmdef references `Unity.InputSystem` by name for this; the package gets no new
    dependency.
  - `VfxLightingRig`: applies a `VfxLightingPreset` to a key light, a fill light, a Volume (its own
    run-time profile, never the scene's profile asset), the scene ambient and the camera
    background; the static `Apply` is the one implementation the Showcase and the capture stage
    call.
  - `VfxCameraRig`: places a camera from a `VfxCameraPreset` around a focus point; the static
    `Place` is the shared implementation.
  - `VfxMannequin.Create`: a code-built capsule mannequin (1.8 m, feet at y = 0, facing +Z, no
    collider, one URP Lit material destroyed with it) with the five well-known sockets, the
    placeholder when no character model is available.

#### Library and Effects

- `Library/Grid/CuvaraVfxGrid.shader` (`Cuvara/VFX/Grid`), written for this package: a lit opaque
  URP 17.6 surface (main and additional lights in Forward and Forward+, the ambient probe, shadows
  cast and received, depth and depth-normals passes, SRP Batcher compatible, no texture) with
  world-space grid lines every cell (1 m) and stronger lines every 5 cells, anti-aliased with
  screen-space derivatives (at least one pixel wide, fading to their average coverage where cells
  get smaller than two pixels). Albedo, line colour, line width, cell size and line strengths are
  material properties.
- `Library/Grid/VfxGridGround.mat` (albedo 0.30, lines 0.10) and `VfxGridBackdrop.mat` (albedo
  0.36, lines 0.14), the same albedo values as the capture stage's ground and backdrop.
- `Effects/README.md`: the folder layout of one effect; no effect exists yet.

#### Editor

- Bridge assembly `Unity.Testing.VisualEffectGraph.Editor` (the name VFX Graph 17.6 lets see its
  internals) with `VfxGraphBridge` only: a version guard for VFX Graph 17.6.x, the library catalog,
  the graph description, the write primitives (`GraphEdit`: create a graph with VFX Graph's own
  `CreateNewAsset`, add nodes by catalog name and variant, set settings and slot values, link slots
  and flow, add subgraphs and sticky notes, create subgraph assets, save with
  `WriteAssetWithSubAssets`), `CompileReport` and `InspectOutputs`. It exposes plain values only,
  never an internal type, and references `Unity.ShaderGraph.Editor` for the Shader Graph of an
  output. No `.vfx` text is written or patched.
- Editor assembly `Cuvara.VFX.Editor` with the agent entry point `VfxCli.Run(verb, jsonArgs)` (JSON
  envelope `ok`, `verb`, `errors`, `warnings`, payload; never throws; an unknown verb lists the
  valid ones; unused arguments become warnings) and `VfxCli.Batch()` for `-executeMethod`
  (`-vfxVerb`, `-vfxArgs`, `-vfxOut`; exit code 0, 1 or 2).
- Verb `catalog`: contexts, blocks, operators and parameter types with variant, settings (allowed
  values, defaults, forwarded settings such as Initialize's `capacity`) and slots; the recipe op
  names and fields; the texture steps and their fields; the VFX Graph version. Experimental entries
  are always left out.
- Verb `describe`: contexts, blocks, operators, parameters, settings, slot values, slot links, flow
  links, systems with capacity, sticky notes and groups, with stable ids and ordering; also for
  `.vfxoperator` and `.vfxblock` subgraphs.
- Verbs `build` (recipe path) and `apply` (asset and ops) with the ops `add_parameter`,
  `add_context`, `add_block`, `add_operator`, `set_setting`, `set_slot`, `link_slots`, `link_flow`,
  `use_subgraph`, `set_output` and `comment`: recipe-local ids, slot addresses `<id>.<slot path>`
  (`#<index>` for unnamed slots, `out` for a single output), catalog type names with an optional
  catalog variant id, asset values by project-relative path, `{"guid"}` or `{"path"}`. An unknown
  op, field, id, type, variant, setting, slot or enum value fails with the op index and the valid
  names, nearest first (`op_error`). The graph is built on a temporary copy, compiled and copied over
  the real `.vfx` only when clean, keeping its GUID; a failed build leaves the previous asset
  byte-identical. Two builds of a recipe describe to the same JSON (`describe_sha256`).
- Verb `import-texture`: the steps `luminance_to_alpha`, `premultiply`, `pack_channels`,
  `resize_pot`, `flipbook`, `wrap`, `linear` and `srgb` on managed byte arrays in integer
  arithmetic, so the output bytes are the same on every run; writes the PNG and its
  `TextureImporter` settings and returns the SHA-256 of the source, of every `pack_channels` input
  and of the output. A failed import leaves an existing target and its `.meta` unchanged.
- Verb `inspect`: the static facts a lint reads, for a `.vfx` or a prefab with one `VisualEffect`:
  systems with capacity, outputs with blend mode, generated shader, Shader Graph and effective render
  features, textures with size, format, compression and estimated GPU bytes, exposed properties with
  defaults and prefab overrides, HDR colour values, the recipe hash and `describe_sha256`.
- Verb `capture`: a deterministic play-test in a neutral stage made from code, with a fixed seed and
  `Time.captureDeltaTime`; writes one PNG per sample, `contact-sheet.png` and `stats.json`. The
  effect is held still for `settle_frames` at every sample and probe, because VFX Graph refreshes
  `aliveParticleCount` only every 60 frames, so counts are exact and never -1. The verb returns a
  pending result and finishes after the domain reload of play mode (`VfxCaptureJob`); in batch mode
  the job writes `-vfxOut` and exits the editor.

#### Schemas, fixture and tests

- JSON Schemas (draft 2020-12) `Schemas~/vfx-recipe.schema.json` (`cuvara.vfx-recipe/1`),
  `vfx-describe.schema.json`, `vfx-inspect.schema.json` and `vfx-capture.schema.json`.
- Fixture `Tests/Fixtures/BurstQuads`: `BurstQuads.vfxrecipe.json` and the `BurstQuads.vfx` it
  builds (one burst of 24 additive quads with a colour over life and an exposed `Tint` colour).
- EditMode test assembly `Cuvara.VFX.Editor.Tests`: the internals guard, the envelope and batch
  entry, catalog and describe determinism, recipe round trip and unknown names, coverage of every op
  field (`RecipeCoverageTests`), the recipe schema, texture import determinism and failure cases,
  inspect, and capture determinism (two captures with one seed).
- PlayMode test assembly `Cuvara.VFX.Tests`: `VfxLibrary`, `VfxSocket`, `VfxPlayer`,
  `ChainLightningBinder` and the presets.
- PlayMode tests `VfxStationTests` (interval and direct plays, effect change, socket anchors,
  `ChainLightningBinder` binding and its limit, the Input System key map), `VfxRigTests` (every value
  of each lighting preset through the rig and the static `Apply`; camera position, rotation and
  field of view of each camera preset) and `VfxMannequinTests` (sockets, height, no collider,
  nothing left after the root is destroyed). The test asmdef gets the same `Unity.InputSystem`
  reference and version define as the runtime.

#### Sandbox and tools

- Agent-owned `Sandbox~` Unity project (6000.6.9f1): URP 17.6.0 with HDR, Forward+, depth and
  opaque textures, VFX Graph 17.6.0, Unity-MCP 1.7.0 from OpenUPM (with its NuGet DLLs committed),
  Test Framework, Input System, linear colour space; settings created and checked by
  `SandboxSetup.Apply()` / `Report()`.
- `Tools~/setup-sandbox.py` and `Tools~/sandbox_paths.py`: an idempotent per-clone sandbox home
  outside the repository (`Sandbox` link = the open path, `package` link = the package, referenced
  as `file:../../package`), a hard error on any link cycle inside the repository, local Unity-MCP
  configuration, and `--open` / `--watch` / `--close` with a memory guard (default 12 GB private
  bytes) plus `--remove` / `--print-open-path`. Unity 6000.6 cannot map package scripts to classes
  when the project path has a `~` or `.` component, so the Sandbox is always opened through the
  open path.
- `Tools~/setup-sandbox.py --restore-nuget` (and `--open`, when it finds the folder missing): brings
  the Unity-MCP NuGet DLLs of `Sandbox~/Assets/Plugins/NuGet` back through the plugin's own restore when
  the folder is absent or incomplete against `.nuget-installed.json` (`unity-mcp-cli open
  --launch-dismiss-timeout-ms 400000`, ready only when the set is complete and the editor compiled;
  memory guard on; `--keep-open`). `python Tools~/setup-sandbox.py` reports the state under `nuget`.
  Tested by hand with the committed folder moved away: ready after 52.9 s and 44.0 s, 45 of 45 DLLs
  identical to the committed ones by SHA-256.
- `Tools~/vfxcli.py <verb> [--project] [--args | --json] [--out] [--mode] [--timeout]
  [--memory-limit-gb]`: runs a verb through Unity-MCP `script-execute` when the Sandbox editor is
  open (arguments and result through files), else through a batch-mode editor with graphics under
  the memory guard; waits for pending verbs; rewrites path arguments relative to the current
  directory; exit codes 0 ok, 1 ok false, 2 usage, 3 no editor.

#### CI and documentation

- `.github/workflows/ci.yml`: a `validate` job that needs no Unity licence (`package.json` fields,
  CHANGELOG heading, `.meta` coverage, asmdef names, the JSON Schemas as valid draft 2020-12
  schemas, committed recipes against the recipe schema, Python compile, workflow YAML) and game-ci
  EditMode and PlayMode jobs on a copy of the Sandbox laid out like the open path
  (`.github/scripts/ci_sandbox.py`), skipped when the `UNITY_LICENSE` secret is absent.
  `check_metas.py` does not judge `.meta` files under dot or `~` paths (the nested `Sandbox~`
  project, `Samples~`).
- `Documentation~/AUTHORING.md` (the authoring guide, the verified pitfalls and the differences from
  DESIGN.md), `CAPTURE.md`, `CI.md` and `DESIGN.md`.

### Changed

- An unknown name error now reads `unknown <what> '<name>'; valid names (nearest first): ...`
  instead of forming a plural from `<what>`.
- `describe` prints `resource_version`, and it warns when a graph is older than the installed
  VFX Graph (the next compile would upgrade it in memory).
- The compile report of `build` and `apply` has `upgraded`. When VFX Graph logs "The source graph
  was out of date and has been upgraded before compilation", the result gets a warning; for
  `build` it is an error.
- `CaptureTests.SameSeedTwice_GivesTheSamePeak_NoMinusOne_AndAllFiles` is reported as Ignored,
  with the reason, when the editor has no graphics device or no compute shader support
  (Documentation~/CI.md, "GPU tests").

### Fixed

- A built graph and a new subgraph were saved at resource version 0, so VFX Graph converted every
  gradient colour key with `Color.linear` at each compile, and `describe` did not match the
  compiled graph. A new graph is now at the current graph and resource versions before the first
  op. `apply` upgrades an older asset before its ops and warns. The BurstQuads fixture is rebuilt
  (`m_ResourceVersion: 1`).
- The README quick start built the tracked fixture in place and wrote `catalog.json` into the
  repository root. It now uses a copy under `Sandbox~/Assets/Scratch` and a gitignored `vfx-out/`
  folder.

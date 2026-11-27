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
- `THIRD-PARTY.md`: the notice of every third-party file in the repository: the two Quaternius packs of
  the Showcase test characters (CC0 1.0 Universal, source, author, download date 2026-11-27, archive
  SHA-256, what was changed), and the 45 Unity-MCP NuGet DLLs of the Sandbox (one row each with version,
  author, licence, source and a SHA-256 match with the nuget.org package, the full licence texts, what
  each install route downloads). The Quaternius site-wide licence page (QAL v1.0) and the three
  PolyForm Shield `Rql.*.dll` files are marked as owner decisions before a public release.
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

#### Showcase sample (`Samples~/Showcase`, listed in `package.json` `samples`)

- `Showcase.unity`: a grey grid room (floor and 10 m walls in the `Library/Grid` materials, 24 by 68 m,
  no skybox) with four `VfxStation`s in a row along +Z, 14 m apart: a chain-lightning lane (a caster
  and three targets 3.25 m apart on a diagonal row, anchored on the caster's `hand_r`, targets on their
  `chest`), a single-target station (a caster and one target 3 m apart), a ground area station (a
  5 m disc on the floor) and an empty pedestal. A `VfxLightingRig` drives a soft-shadowed directional
  key, a fill, the trilight ambient and a global Volume (Bloom, ACES, vignette, post exposure) with
  `dusk` as the saved default; a `VfxCameraRig` places the camera.
- Characters are not baked into the scene: each character place is a `ShowcaseCharacterSpawn` that
  instantiates its prefab in play mode, or the placeholder `VfxMannequin` (five sockets, one
  information line in the console) when the reference is empty or the model is missing, so the scene
  opens and plays without `Characters/`.
- `ShowcaseOverlay` (play mode, IMGUI): select a station (1-4, Tab), step its effect through the
  `VfxLibrary` (Q, E), replay (Space), time scale 0.1 to 2 (T), lighting preset (L), camera preset
  around the selected station (C), hide the panel (H); station labels in the Game View. It applies
  presets only through `VfxLightingPreset.Get` and `VfxCameraPreset.Get` with the shared rigs and reads
  keys through `VfxStation.WasPressedThisFrame`, so the assembly `Cuvara.VFX.Samples.Showcase`
  (references only `Cuvara.VFX`) compiles with the Input System, the legacy Input Manager or both.
- `Characters/` (2.15 MB): two CC0 rigged humanoid test characters from the Quaternius pack Universal
  Base Characters (Standard), `VfxTestCharacterMale.prefab` (1.79 m) and `VfxTestCharacterFemale.prefab`
  (1.75 m), Humanoid avatars with `VfxSocket`s on `head`, `chest`, `hand_l`, `hand_r` and `feet`, one
  looping idle clip (`Armature|Idle_Loop` of the Universal Animation Library, Standard, 2.5 s), a
  controller, two URP Lit materials and `LICENSE.md`.
- `ShowcaseLibrary.asset` with the fixtures `chain-targets` (lane) and `burst-quads` (single target
  and ground area), referenced from `Tests/Fixtures` by two prefabs in `Effects/`, not copied; a
  `VolumeProfile` with the `dusk` values for the edit-mode look; two URP Lit materials (area marker,
  pedestal); a README.

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
- `capture` places optional characters (`VfxCaptureCast`): `character` (a prefab path, or
  `placeholder` for the code-built `VfxMannequin`), `target_count` (0 to 8), `target_spacing`
  (default 3.5 m), `source_socket` (default `hand_r`) and `target_socket` (default `chest`). The
  caster and the targets stand in a lane along +X centred on the effect's origin, their Animators
  evaluated once at time 0 and then disabled. Every `ChainLightningBinder` of the effect is bound to
  the caster's source socket and the targets' sockets before the first simulation step; a `.vfx`
  whose graph exposes the binder's default properties gets a binder. A prefab path that does not
  exist places the placeholder with a warning. Without `character` a capture places nothing, as
  before.

#### Schemas, fixture and tests

- JSON Schemas (draft 2020-12) `Schemas~/vfx-recipe.schema.json` (`cuvara.vfx-recipe/1`),
  `vfx-describe.schema.json`, `vfx-inspect.schema.json` and `vfx-capture.schema.json`.
- Fixture `Tests/Fixtures/BurstQuads`: `BurstQuads.vfxrecipe.json` and the `BurstQuads.vfx` it
  builds (one burst of 24 additive quads with a colour over life and an exposed `Tint` colour).
- Fixture `Tests/Fixtures/ChainTargets`: exposes `SourcePosition`, `TargetPosition0` to
  `TargetPosition7` and `HopCount` (the `ChainLightningBinder` defaults) and draws one burst of 48
  additive quads on the line from `SourcePosition` to `TargetPosition7`.
- EditMode tests `CaptureCastTests`: parsing of the character inputs with every error, the
  placeholder fallback, the job round trip, and a capture with a stand-in prefab built by the test
  and 3 targets on `ChainTargets` (lane positions, bound positions and hop count read back from the
  graph, files, the schema, equal counts for one seed twice, and a missing prefab that places the
  placeholder).
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
- PlayMode smoke tests of the Showcase in the Sandbox (`Sandbox~/Assets/Tests`, assembly
  `Cuvara.VFX.Sandbox.ShowcaseTests`, no compile reference to the sample): the scene loads and plays
  with no error, its four stations and the lane geometry, five sockets on every character, the
  overlay's lighting and camera presets equal to the shared preset data, alive particles at the
  fixture station, and the placeholder of a spawn point without a prefab. Ignored, with the reason,
  when `Assets/Showcase` is absent; the particle test also without a graphics device or compute
  shaders. The CI project gets a copy of the sample (`ci_sandbox.py`), so they run there too.

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
  (`.github/scripts/ci_sandbox.py`), skipped when the `UNITY_LICENSE` secret is absent. The jobs use
  `game-ci/unity-test-runner@v4.3.2`: v4.3.1 starts the editor container with Docker's default 64 MB
  `/dev/shm`, and Unity 6000.6 aborts with "Insufficient shared memory available".
  `check_metas.py` does not judge `.meta` files under dot or `~` paths (the nested `Sandbox~`
  project, `Samples~`).
- `Documentation~/AUTHORING.md` (the authoring guide, the verified pitfalls and the 49 differences from
  DESIGN.md), `CAPTURE.md`, `CI.md`, `DESIGN.md` and `SHOWCASE.md` (the Showcase as built: the room and
  its measured station layout, `VfxStation`, the characters, sockets and the placeholder mannequin, the
  lighting and why it matches the capture stage, the overlay and its keys, how to add an effect, the
  smoke tests, the limits and known gaps).
- `README.md` lists the Showcase, `Runtime/Showcase`, `Library`, `Effects` and `Tests/Fixtures/ChainTargets`.

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
- The capture stage uses the grid materials of `Library/Grid` for its ground (now 80 m) and its
  backdrop (now 80 by 20 m, 6 m behind the origin instead of 8 m), and applies lighting and camera
  through `VfxLightingRig.Apply` and `VfxCameraRig.Place`, the code the Showcase uses.
  `VfxCaptureStage.GroundAlbedo` and `BackdropAlbedo` are removed (they were documented as linear but
  are sRGB material values); `GroundMaterialPath` and `BackdropMaterialPath` name the materials. The
  frames of every capture change (CAPTURE.md has the new mean luminance figures); counts do not.
- The `night` lighting preset's key light heading is -45 degrees instead of 30, so it lights the
  front of the backdrop in every camera preset (the Showcase uses the same value).
- `stats.json` has two optional fields, `characters` (null without characters) and `binders` (empty
  without a bound binder); every existing field keeps its name and meaning, and
  `Schemas~/vfx-capture.schema.json` lists both (not required, so earlier files stay valid).
- `vfxcli.py` rewrites the `character` path argument too: a path inside `Samples~/Showcase` becomes
  `Assets/Showcase/...` (where the Sandbox sees the sample) for every path argument, and the keyword
  `placeholder` is passed unchanged.
- `VfxStation.PlayNow` returns `VfxHandle.None` without logging and without looking up a player when
  the station has no effect id, so an empty station with the `Interval` trigger stays silent instead
  of logging an error at every interval.
- The Sandbox URP asset (`Sandbox~/Assets/Settings/Sandbox_URP.asset`) supports soft shadows, which
  the presets' key light asks for (`LightShadows.Soft`); URP renders soft shadows only when the asset
  supports them.

### Fixed

- A capture taken while another scene was loaded (for example `Showcase.unity` open in the editor)
  showed that scene's meshes, characters and effects around the effect: the stage disabled only the
  other scenes' cameras, lights and Volumes. It now deactivates every active root object of the other
  loaded scenes while it exists and activates exactly those again afterwards, so the frames and
  counts do not depend on the open scenes (`CaptureTests.AnotherLoadedScene_DoesNotChangeTheCapture_AndIsRestoredAfterwards`).
- Every cell of a capture contact sheet showed a brown band at the top and a ground with no
  readable surface: the gameplay frame saw only a 1 m strip of an untextured backdrop 8 m away, lit
  orange by the dusk key more brightly than the ground. The stage now has the grid ground and a grid
  wall 6 m away, so the frame reads as a floor with a wall behind it in every camera and lighting
  preset.
- With the `night` preset and the `side` camera the backdrop faced away from the key light and
  rendered near black; the night key now comes from the same side as the day and dusk keys.
- A built graph and a new subgraph were saved at resource version 0, so VFX Graph converted every
  gradient colour key with `Color.linear` at each compile, and `describe` did not match the
  compiled graph. A new graph is now at the current graph and resource versions before the first
  op. `apply` upgrades an older asset before its ops and warns. The BurstQuads fixture is rebuilt
  (`m_ResourceVersion: 1`).
- The README quick start built the tracked fixture in place and wrote `catalog.json` into the
  repository root. It now uses a copy under `Sandbox~/Assets/Scratch` and a gitignored `vfx-out/`
  folder.
- CI failed on a runner with no GPU (run 45974445050 on PR #1): the EditMode job crashed the editor
  natively in `VFXManager::Update` -> `VFX::DispatchCompute` (no result XML), and the PlayMode job
  failed `ShowcaseSmokeTests.FixtureStation_HasAliveParticles_AfterItsTrigger` (0 alive particles).
  The runner renders with Mesa llvmpipe, which claims compute support but cannot run VFX Graph.
  Every test that needs VFX Graph simulation or a capture is now in the NUnit category `GPU` (the 3
  EditMode capture tests and the 7 `ShowcaseSmokeTests`), and both Unity jobs pass
  `customParameters: -testCategory !GPU`: CI runs 126 EditMode and 82 PlayMode tests and the
  category runs only in the local Sandbox.
- `GraphicsRequirement` moved to `Tests/Runtime` (`Cuvara.VFX.Tests`, shared with the Sandbox
  Showcase tests) with the const `Category` and `SkipVariable`. Its guard ends a GPU test as Ignored,
  with the reason, before play mode or a scene load when `CUVARA_VFX_SKIP_GPU_TESTS` is `1` or `true`,
  when there is no graphics device, no compute shader support, or when the device is a software
  rasteriser (llvmpipe, softpipe, lavapipe, SwiftShader, Microsoft Basic Render), so no headless run
  reaches `VFXManager.Update`.
- `assert_test_results.py --excluded-category` writes the counts and the note that the category runs
  only in the Sandbox to the job summary (`GITHUB_STEP_SUMMARY`).
- `Documentation~/CI.md` lists which tests CI runs and which only the Sandbox runs, the guard, and the
  local counts of 2026-11-27 (EditMode 129 of 129; PlayMode 89 of 89 in an opened editor; `!GPU` runs
  126 and 82). The GPU category needs an editor that renders: in `-batchmode` the Showcase particle
  test sees 0 alive particles (PlayMode 88 of 89).

# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Package scaffold: `package.json` (`com.cuvara.vfx` 0.1.0, Unity 6000.6, VFX Graph 17.6.0 and URP
  17.6.0), MIT licence, README, and the licence-free CI validation (`package.json` fields, CHANGELOG
  heading, `.meta` coverage, recipe schema validation) with game-ci EditMode and PlayMode jobs that
  are skipped when the `UNITY_LICENSE` secret is absent.
- Agent-owned `Sandbox~` Unity project (6000.6.9f1): URP 17.6.0 with HDR, Forward+, depth and
  opaque textures, VFX Graph 17.6.0, Unity-MCP 1.7.0 from OpenUPM, Test Framework, Input System,
  linear colour space; settings created and checked by `SandboxSetup.Apply()` / `Report()`.
- `Tools~/setup-sandbox.py` and `Tools~/sandbox_paths.py`: an idempotent per-clone sandbox home
  outside the repository (`Sandbox` link = the open path, `package` link = the package, referenced
  as `file:../../package`), the Showcase link, a hard error on any link cycle inside the
  repository, local Unity-MCP config, and `--open` / `--watch` / `--close` with a memory guard
  (default 12 GB private bytes) plus `--remove` / `--print-open-path`. Unity 6000.6 cannot map
  package scripts to classes when the project path has a `~` or `.` component, so the Sandbox is
  always opened through the open path (see `Documentation~/AUTHORING.md`).
- `Documentation~/AUTHORING.md` with the Sandbox workflow and the verified pitfalls.
- Runtime assembly `Cuvara.VFX` (references `Unity.VisualEffectGraph.Runtime` only):
  `VfxLibrary` (effect id to prefab, duration and tags; ordinal lookup; `Validate` reports empty and
  duplicate ids, missing prefabs, prefabs without a `VisualEffect` and negative durations),
  `VfxSocket` (named attach point with the well-known names `head`, `chest`, `hand_l`, `hand_r`,
  `feet` and a lookup under a root), `VfxPlayer` (plays an effect by id at a position or on a socket,
  one-shot or looped, pooled per effect id, no managed allocation per play once the pool is warm,
  `VfxHandle` to stop an instance) and `ChainLightningBinder` (writes `SourcePosition`,
  `TargetPosition0`..`TargetPosition7` and `HopCount` into a `VisualEffect` every frame; at most 8
  targets; null, destroyed and inactive targets skipped; missing properties skipped silently).
- PlayMode test assembly `Cuvara.VFX.Tests` (gated by `UNITY_INCLUDE_TESTS`) for the four runtime
  types.
- Bridge assembly `Unity.Testing.VisualEffectGraph.Editor` (the name VFX Graph 17.6 lets see its
  internals) with `VfxGraphBridge` only: a version guard for VFX Graph 17.6.x, the library catalog
  and the graph description, exposed as plain values (strings, numbers, lists and the ordered
  `VfxGraphBridge.Map`), never as an internal type.
- Editor assembly `Cuvara.VFX.Editor`: the agent entry point `VfxCli.Run(verb, jsonArgs)` (JSON
  envelope `ok`, `verb`, `errors`, `warnings`, payload; never throws; an unknown verb lists the
  valid ones; unused arguments become warnings) and `VfxCli.Batch()` for `-executeMethod`
  (`-vfxVerb`, `-vfxArgs`, `-vfxOut`; exit code 0, 1 or 2), a verb registry that finds one
  `IVfxVerb` class per file, a dependency-free JSON reader and writer with stable output, and the
  read-only verbs `catalog` (contexts, blocks, operators and parameter types with variant, settings
  with allowed values and defaults, and slots; recipe op names; VFX Graph version) and `describe`
  (contexts, blocks, operators, parameters, settings, slot values, slot links, flow links, systems
  with capacity, sticky notes and groups, with stable ids and ordering).
- `Schemas~/vfx-describe.schema.json` for the `describe` result.
- `Tools~/vfxcli.py <verb> [--project] [--args | --json] [--out] [--mode]`: runs a verb through
  Unity-MCP `script-execute` when the Sandbox editor is open (arguments and result through files),
  else through a batch-mode editor with graphics under the memory guard; exit codes 0 ok, 1 ok
  false, 2 usage, 3 no editor.
- EditMode test assembly `Cuvara.VFX.Editor.Tests` (gated by `UNITY_INCLUDE_TESTS`): the internals
  guard, the envelope and batch entry, catalog content and determinism, describe determinism on a
  temporary copy of VFX Graph's Simple Burst template.
- `Documentation~/AUTHORING.md`: the agent interface, what Unity's sample graphs show about systems,
  spawning, attributes, colour over life and blend modes, and new pitfalls.
- Recipe builder and the verbs `build` (recipe path) and `apply` (asset and ops): the ops
  `add_parameter`, `add_context`, `add_block`, `add_operator`, `set_setting`, `set_slot`,
  `link_slots`, `link_flow`, `use_subgraph`, `set_output` and `comment`, with recipe-local ids,
  slot addresses `<id>.<slot path>` (`#<index>` for unnamed slots, `out` for a single output),
  catalog type names with an optional catalog variant id, and asset references by project-relative
  path. An unknown op, field, id, type, variant, setting, slot or enum value fails with the op index
  and the valid names, nearest first (`op_error`). The graph is built on a temporary copy, compiled
  (in-memory compile, per-node errors, import with logged errors, generated shader messages) and
  copied over the real `.vfx` only when clean, keeping its GUID; a failed build leaves the previous
  asset byte-identical. The result reports compile errors, the capacity per system and
  `describe_sha256`; two builds of a recipe describe to the same JSON.
- Bridge write primitives (`VfxGraphBridge.GraphEdit`, `CompileReport`, `Systems`,
  `NearestFirst`, `EditException`): create a graph with VFX Graph's own `CreateNewAsset`, add
  nodes by catalog name and variant, set settings and slot values (numbers, enums, vectors, HDR
  colours, gradients, curves, VFX structs, assets), link slots and flow, add subgraphs and sticky
  notes, save with `WriteAssetWithSubAssets`. No `.vfx` text is written or patched.
- `Schemas~/vfx-recipe.schema.json` (`cuvara.vfx-recipe/1`) and the fixture
  `Tests/Fixtures/BurstQuads` (`BurstQuads.vfxrecipe.json` and the `BurstQuads.vfx` it builds: one
  burst of 24 additive quads with a colour over life and an exposed `Tint` colour).
- `catalog` prints `recipe_op_fields` (required and optional fields per op).
- EditMode tests: recipe round trip and determinism, unknown names (block type, setting, slot, op,
  id, variant) leaving the previous asset byte-identical, `apply` on describe ids, the recipe schema
  against the fixture and against the builder's op list (with a small JSON Schema validator).
- Verb `import-texture` (`source` PNG, `target` `.png` asset, ordered `transform`): the steps
  `luminance_to_alpha` (`color`: `keep`, `white`, `unpremultiply`), `premultiply`, `pack_channels`
  (`r`, `g`, `b`, `a` from a constant or a file and channel), `resize_pot` (`max_size`; area
  average), `flipbook` (`columns`, `rows`, checked against the output size), `wrap` (`u`, `v` or
  `mode`: `repeat`, `clamp`), `linear` and `srgb`. The pixel steps run on managed byte arrays in
  integer arithmetic, so the output bytes are the same on every run. The verb writes the PNG and
  its `TextureImporter` settings (sRGB, alpha source, alpha is transparency, wrap, mip maps, max
  size, compression) and returns the SHA-256 of the source, of every `pack_channels` input and of
  the output, the sizes, the applied steps and the import settings. An unknown step, parameter or
  value fails with the valid names, nearest first (`step_error`); a failed import leaves an
  existing target untouched. `catalog` prints `texture_steps` and `texture_step_fields`.
- EditMode tests for `import-texture`: output hash determinism, `luminance_to_alpha`,
  `premultiply`, `pack_channels` and `resize_pot` on known pixels, importer settings for linear,
  sRGB and wrap, and the failure cases on PNGs the tests write themselves.

### Changed

- `catalog` lists the settings a model forwards (Initialize: `capacity`, `boundsMode` and the
  other data settings; composed outputs: their topology and shading settings) through
  `VFXModel.GetSettings`, the same list `set_setting` accepts.
- `describe` also takes an absolute path inside a local package of the project (the repository
  path of this package).
- `Tools~/vfxcli.py` resolves path arguments (`recipe`, `asset`, `effect`, `source`, `target`,
  `out`, `file`, `texture`) written relative to the current directory: a path inside the
  repository is passed as `Packages/com.cuvara.vfx/...`, a path inside `Sandbox~/Assets` as
  `Assets/...`, any other path (and every `out`) as an absolute path. `build` with
  `Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json` from the repository root now works.
- `.gitignore` ignores every `Sandbox~/Assets/CuvaraVfx*Temp*` folder and its `.meta`, which a
  crashed build or test run can leave behind.

### Fixed

- `check_metas.py` no longer reports the `.meta` files of the nested `Sandbox~` project (and later
  `Samples~`) as orphans: its orphan pass only judges `.meta` files on Unity-visible paths.

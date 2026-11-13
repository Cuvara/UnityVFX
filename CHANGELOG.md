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

### Fixed

- `check_metas.py` no longer reports the `.meta` files of the nested `Sandbox~` project (and later
  `Samples~`) as orphans: its orphan pass only judges `.meta` files on Unity-visible paths.

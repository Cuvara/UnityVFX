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

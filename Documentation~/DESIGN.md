# com.cuvara.vfx - design and agent interface

Status: contract for the first implementation. A task lead may refine a detail, but changes to the
agent interface (CLI verbs, file formats) are reported to the run lead, because rpg-factory scripts
and skills consume them.

## Purpose

A Unity package that lets coding agents author game effects with VFX Graph, play-test them in a
fixed showcase scene, and produce machine-readable evidence (a contact sheet and a stats file) for
review. The package is the **mechanism**: build, describe, capture, import. The **policy** (the
project's style profile, budgets and the lint that judges an effect) lives in the rpg-factory
plugin and reads the files this package writes.

## Versions (verified on this machine, 2026-11-13)

- Unity `6000.6.9f1`, `com.unity.visualeffectgraph` 17.6.0, `com.unity.render-pipelines.universal` 17.6.0.
- Editor: `C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe`.
- Editor automation: `com.ivanmurzak.unity.mcp` 1.7.0 and the global `unity-mcp-cli` 0.93.2
  (`open`, `wait-for-ready`, `status`, `run-tool <tool> --input-file args.json`, `close`).

## VFX Graph internals

Unity has no public API to create or edit a graph. The model types (`VFXGraph`, `VFXContext`,
`VFXBlock`, `VFXOperator`, `VFXSlot`, `VFXParameter`, `VFXLibrary`, `VisualEffectAssetEditorUtility`)
are internal to `Unity.VisualEffectGraph.Editor`.

- Access: one small editor assembly whose asmdef is named `Unity.Testing.VisualEffectGraph.Editor`.
  That name is in the `InternalsVisibleTo` list of
  `com.unity.visualeffectgraph/Editor/PackageInfo.cs`, so the assembly compiles against the
  internals. An upstream API change then breaks compilation instead of failing silently.
- This is the only assembly that does not use the `Cuvara.VFX.*` name. It contains `VfxGraphBridge`
  and nothing else; every other assembly calls the bridge through its public API.
- 17.6 API: `VisualEffectResource.GetGraph()` and `CreateGraph()` (extension methods in
  `Editor/Models/VFXGraph.cs`); `GetOrCreateGraph` no longer exists. `VFXTemplateHelper` is public.
- Never write or patch `.vfx` YAML as text.
- Reference graphs to read with `describe` (do not copy their assets: Unity Companion License):
  the package samples under `Samples~/VFXGraphAdditions/VFX/` (for example `Lightning.vfx`) and
  `Samples~/VFXLearningTemplates/VFX/` inside the built-in package folder.

## Layout

```
package.json                 com.cuvara.vfx, unity 6000.6, deps visualeffectgraph + URP 17.6.0
Runtime/                     Cuvara.VFX: VfxLibrary, VfxPlayer, VfxSocket, ChainLightningBinder
Editor/                      Cuvara.VFX.Editor: VfxCli, recipe builder, texture import, capture
Editor/Bridge/               Unity.Testing.VisualEffectGraph.Editor: VfxGraphBridge only
Library/                     shared subgraphs, gradients, textures used by every effect
Effects/<Name>/              <Name>.vfxrecipe.json, <Name>.vfx, <Name>.prefab, textures
Samples~/Showcase/           prototype scene (grid room, test characters, lighting presets)
Sandbox~/                    agent-owned Unity project; references the package as file:../..
Schemas~/                    JSON Schemas of the formats below
Tests/Editor, Tests/Runtime  Cuvara.VFX.Editor.Tests, Cuvara.VFX.Tests
Tools~/                      vfxcli.py (one command for agents), setup-sandbox.py
Documentation~/              AUTHORING.md, CAPTURE.md, SHOWCASE.md, CI.md, this file
```

Folders ending in `~` are invisible to Unity. `Sandbox~/Assets/Showcase` is a directory junction
(Windows) or symlink to `Samples~/Showcase`, created by `Tools~/setup-sandbox.py` and gitignored,
so the sample has one copy and its `.meta` files are committed under `Samples~/Showcase`.

## Runtime (assembly `Cuvara.VFX`)

- `VfxLibrary` (ScriptableObject): effect id to prefab, with duration and tags.
- `VfxPlayer`: plays an effect by id at a position or on a `VfxSocket`, one-shot or looped, pooled.
- `VfxSocket`: a named attach point on a character (`head`, `chest`, `hand_l`, `hand_r`, `feet`).
- `ChainLightningBinder`: takes a source transform and N target transforms and writes their
  positions and the hop count into the exposed properties of a `VisualEffect`.
- No dependency on Entities, netcode, VContainer or any other Cuvara package.

## Agent interface

One entry point, JSON in and JSON out, so every bridge can call it:

```csharp
public static string Cuvara.VFX.Editor.VfxCli.Run(string verb, string jsonArgs);   // returns JSON
public static void   Cuvara.VFX.Editor.VfxCli.Batch();   // -executeMethod, reads -vfxVerb, -vfxArgs <file>, -vfxOut <file>
```

`Tools~/vfxcli.py <verb> [--project <unity project>] [--args <file.json>] [--out <file.json>]`
calls `Run` through `unity-mcp-cli run-tool script-execute` when an editor has the project open,
and falls back to a batch-mode editor with graphics (never `-nographics`: VFX needs a GPU)
otherwise. Exit codes: 0 ok, 1 the verb reported `"ok": false`, 2 usage, 3 no editor available.

Every result is `{"ok": bool, "verb": str, "errors": [..], "warnings": [..], ...payload}`.

| Verb | Input | Output |
|---|---|---|
| `catalog` | optional `filter` | contexts, blocks, operators and parameter types the library offers; each with its type name, settings (name, type, allowed values) and slots (name, type); VFX Graph version |
| `describe` | `asset` | the graph as JSON: contexts, blocks, operators, parameters, settings, slot values, links, flow links |
| `build` | `recipe` (path) | creates or replaces the `.vfx` next to the recipe; returns compile errors, capacity per system |
| `apply` | `asset`, `ops` | applies ops to an existing graph (same op set as a recipe) |
| `import-texture` | `source`, `target`, `transform` | writes the transformed texture and its import settings; returns SHA-256 of source and output |
| `inspect` | `effect` (prefab or `.vfx`) | static facts for lint: systems with capacity, output type and blend mode, shader or shader graph GUID and name, textures (path, size, format, sRGB, mip, compression), exposed properties with default values, HDR colour values found in the graph |
| `capture` | `effect`, `seed`, `times`, `camera`, `lighting`, `out` | `contact-sheet.png`, one PNG per sample, `stats.json` |

`build` and `apply` work on a temporary copy, compile it, and replace the real asset only when the
compile is clean. A failed build leaves the previous asset untouched.

### Recipe (`<Name>.vfxrecipe.json`, schema `Schemas~/vfx-recipe.schema.json`)

The source of truth of an effect. Committed next to the `.vfx` it builds.

```json
{
  "schema": "cuvara.vfx-recipe/1",
  "id": "chain-lightning",
  "vfx_graph": "17.6",
  "meta": {"duration_seconds": 0.9, "looping": false, "tags": ["lightning"],
           "phases": [{"name": "anticipation", "seconds": 0.1}, {"name": "impact", "seconds": 0.2}, {"name": "dissipation", "seconds": 0.6}]},
  "ops": [
    {"op": "add_parameter", "id": "p_color", "name": "CoreColor", "type": "Color", "value": [4.0, 6.0, 12.0, 1.0], "exposed": true},
    {"op": "add_context", "id": "spawn", "type": "VFXBasicSpawner"},
    {"op": "add_block", "context": "spawn", "id": "burst", "type": "VFXSpawnerBurst", "settings": {}, "slots": {"Count": 40}},
    {"op": "link_flow", "from": "spawn", "to": "init"},
    {"op": "link_slots", "from": "p_color.out", "to": "output.Color"}
  ]
}
```

- Ops (final names fixed by the implementation and listed by `catalog`): `add_parameter`,
  `add_context`, `add_block`, `add_operator`, `set_setting`, `set_slot`, `link_slots`, `link_flow`,
  `use_subgraph`, `set_output` (texture, blend mode, colour mapping), `comment`.
- Every node has a recipe-local `id`; slots are addressed as `<id>.<slot name>`, never by index.
- Type names, setting names and slot names are the ones `catalog` prints. An unknown name is a
  build error that lists the valid names.
- Asset references are project-relative paths; the builder resolves them to GUIDs.
- A recipe is deterministic: building it twice gives graphs that `describe` to the same JSON.

### Capture (`stats.json`, schema `Schemas~/vfx-capture.schema.json`)

Deterministic play-test of one effect in a neutral capture stage that the capture code creates
(backdrop, ground, the same lighting presets as the Showcase), so it does not depend on a scene.

- Play mode, `Time.captureDeltaTime` fixed (default 1/60), `resetSeedOnPlay` off, `startSeed` from
  the input. Frames are read from the camera's render target (HDR, post-processing on).
- `times`: seconds at which a frame is sampled. `camera`: `gameplay` (default, about 45 degrees,
  MMO distance), `close`, `side`. `lighting`: `day`, `dusk` (default), `night`.

```json
{
  "schema": "cuvara.vfx-capture/1",
  "effect": "Effects/ChainLightning/ChainLightning.prefab",
  "recipe_sha256": "...", "seed": 1234, "delta_time": 0.016667,
  "camera": "gameplay", "lighting": "dusk",
  "unity": "6000.6.9f1", "vfx_graph": "17.6.0",
  "duration_seconds": 0.92,
  "alive": {"peak": 412, "samples": [{"t": 0.0, "count": 0}, {"t": 0.1, "count": 380}]},
  "bounds": {"center": [0, 1, 0], "size": [6.2, 2.4, 1.1]},
  "systems": [{"name": "Arcs", "capacity": 256, "peak": 190}],
  "frames": [{"t": 0.1, "file": "frame-0.10.png", "sha256": "...", "mean_luminance": 0.21, "max_luminance": 7.8}],
  "contact_sheet": {"file": "contact-sheet.png", "sha256": "...", "columns": 4}
}
```

Two captures with the same recipe, seed and settings must report the same `alive.peak`.
`aliveParticleCount` is a GPU readback: the capture waits for a valid value and never records -1.
Overdraw, draw calls and frame time are **not** measured; they stay a human review item.

### Texture import (`import-texture`)

Generated textures arrive as opaque PNGs on a black background. `transform` is an ordered list of
named, deterministic steps: `luminance_to_alpha`, `premultiply`, `pack_channels` (R, G, B, A from
named sources), `resize_pot` (max size), `flipbook` (columns, rows), `wrap` (repeat or clamp per
axis), `linear` or `srgb`. The result records both hashes so a delivery record can prove which
approved generation produced which game texture.

## Showcase scene (`Samples~/Showcase`)

- A grey grid room made with a procedural grid shader of this package. No Unity sample asset is
  copied into the repository.
- Stations: a chain-lightning lane (one caster and three targets in a row), a single-target
  station, a ground area station, an empty pedestal.
- Test characters: CC0 rigged humanoids with realistic proportions and an idle animation; licence
  and source recorded in `THIRD-PARTY.md`. Each has `VfxSocket`s.
- Lighting tuned for a realistic style: soft directional key, fill, low ambient; URP with HDR,
  Forward+, depth and opaque textures; a Volume with Bloom, ACES tonemapping and a light vignette.
  Presets `day`, `dusk`, `night` shared with the capture stage.
- An overlay to pick the effect, replay it, change the time scale, the lighting preset and the
  camera preset.

## Sandbox project (`Sandbox~/`)

Agent-owned: agents may open, close, drive and rebuild it without asking. It contains URP, VFX
Graph, the Unity-MCP plugin, the test framework and the Input System, and nothing game-specific.
`Sandbox~/Library`, `Temp`, `Logs`, `UserSettings` and `Captures` are gitignored. Only one editor
instance may have it open. Agents never open, drive or modify another Unity project (the game
client's editor belongs to the user).

## Conventions

Same as `Cuvara/UIToolkit` and `Cuvara/UnityDots`: `package.json` at the repository root, MIT,
Keep a Changelog `CHANGELOG.md` with `## [Unreleased]`, `.meta` for every Unity-visible file and
folder, asmdef file name equal to its `name`, tests in gated test assemblies, CI with a `validate`
job that needs no Unity licence plus game-ci jobs. English only. No tag without the owner.

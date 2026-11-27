# com.cuvara.vfx

Agent-driven VFX Graph authoring for Unity 6 (URP). A coding agent can build a visual effect from
a JSON recipe, read back any graph as JSON, import generated textures deterministically, read the
static facts a lint needs, and capture a reproducible contact sheet plus a stats file that a
reviewer or a lint can read.

This package is the **mechanism**. The style profile, budgets and the lint that judge an effect
live in the project that consumes it (for the Cuvara RPG, the `rpg-factory` plugin).

## Requirements

- Unity `6000.6` (built and tested with `6000.6.9f1`), `com.unity.visualeffectgraph` 17.6.0 and
  `com.unity.render-pipelines.universal` 17.6.0 (the package's dependencies). The editor code
  checks for VFX Graph 17.6.x, because it reaches VFX Graph internals that change between versions.
- No dependency on Entities, netcode, VContainer or any other Cuvara package.
- For the agent tools: Python 3.8 or newer, and the global `unity-mcp-cli` (0.93.2) for running
  verbs in an open editor. `capture` needs a GPU (batch mode runs with graphics, never
  `-nographics`).

## Install

Add the package to a project's `Packages/manifest.json`, from git (the repository
`Cuvara/UnityVFX` is not published yet) or from a local clone:

```json
"com.cuvara.vfx": "https://github.com/Cuvara/UnityVFX.git"
"com.cuvara.vfx": "file:../../UnityVFX"
```

To work on the package itself, use the agent-owned `Sandbox~` project in this repository; never
open it at `Sandbox~` directly (see [AUTHORING.md](Documentation~/AUTHORING.md), "Sandbox").

## Quick start

From the repository root, build a scratch copy of the fixture effect, capture it, and close the
editor. Every file this writes is gitignored: the copy under `Sandbox~/Assets/Scratch`, the results
under `vfx-out/` and the capture under `Sandbox~/Captures`. Do not build the committed fixture in
place: each build gives the tracked `.vfx` new bytes.

```sh
python Tools~/setup-sandbox.py --open
mkdir -p Sandbox~/Assets/Scratch/BurstQuads
cp Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json Sandbox~/Assets/Scratch/BurstQuads/
python Tools~/vfxcli.py catalog --out vfx-out/catalog.json
python Tools~/vfxcli.py build --json '{"recipe": "Sandbox~/Assets/Scratch/BurstQuads/BurstQuads.vfxrecipe.json"}' --out vfx-out/build.json
python Tools~/vfxcli.py capture --json '{"effect": "Sandbox~/Assets/Scratch/BurstQuads/BurstQuads.vfx", "seed": 1234}' --out vfx-out/capture.json
python Tools~/setup-sandbox.py --close
```

The capture lands in `Sandbox~/Captures/BurstQuads/seed-1234-gameplay-dusk/`:
`contact-sheet.png`, one PNG per sample and `stats.json`. Delete `Sandbox~/Assets/Scratch` (with
`AssetDatabase.DeleteAsset("Assets/Scratch")` while the editor is open) and `vfx-out/` when done.

## Agent interface

One entry point, JSON in and JSON out:

```csharp
string Cuvara.VFX.Editor.VfxCli.Run(string verb, string jsonArgs);   // returns JSON
void   Cuvara.VFX.Editor.VfxCli.Batch();                             // -executeMethod, batch mode
```

```
python Tools~/vfxcli.py <verb> [--project <unity project>] [--args <file.json> | --json '<object>']
                               [--out <file.json>] [--mode auto|editor|batch]
```

Verbs: `catalog`, `describe`, `build`, `apply`, `import-texture`, `inspect`, `capture`. Every result
is `{"ok": bool, "verb": str, "errors": [..], "warnings": [..], ...payload}`. Exit codes: 0 ok,
1 the verb reported `"ok": false`, 2 usage, 3 no editor available.

An effect is a `<Name>.vfxrecipe.json` (schema `Schemas~/vfx-recipe.schema.json`) committed next to
the `.vfx` it builds. Never edit a `.vfx` as text.

## Layout

| Path | Content |
|---|---|
| `Runtime/` | `Cuvara.VFX`: `VfxLibrary`, `VfxPlayer` (with `VfxHandle`), `VfxSocket`, `ChainLightningBinder`; `Presets/`: `VfxLightingPreset`, `VfxCameraPreset` |
| `Runtime/Showcase/` | `Cuvara.VFX` pieces shared by the Showcase scene and the capture stage: `VfxStation`, `VfxLightingRig` (applies a lighting preset), `VfxCameraRig` (places a camera from a camera preset), `VfxMannequin` (a code-built placeholder character with the five sockets) |
| `Library/` | shared assets every effect and the Showcase may use: `Grid/` holds the procedural grid shader `Cuvara/VFX/Grid` and its ground and backdrop materials |
| `Effects/` | one folder per effect (`<Name>.vfxrecipe.json`, `.vfx`, `.prefab`, textures); none exists yet, `Effects/README.md` describes the layout |
| `Editor/` | `Cuvara.VFX.Editor`: `Cli/` (`VfxCli`), `Verbs/`, `Recipe/` (recipe builder), `Texture/` (texture import), `Inspect/`, `Capture/` |
| `Editor/Bridge/` | `Unity.Testing.VisualEffectGraph.Editor`: `VfxGraphBridge` only (the assembly name VFX Graph lets see its internals) |
| `Tests/Editor`, `Tests/Runtime` | `Cuvara.VFX.Editor.Tests` (EditMode), `Cuvara.VFX.Tests` (PlayMode) |
| `Tests/Fixtures/BurstQuads/` | the fixture recipe and the `.vfx` it builds |
| `Schemas~/` | JSON Schemas (draft 2020-12): recipe, describe, inspect, capture stats |
| `Tools~/` | `vfxcli.py`, `setup-sandbox.py`, `sandbox_paths.py` |
| `Sandbox~/` | agent-owned Unity 6000.6.9f1 project (URP, VFX Graph, Unity-MCP, Test Framework, Input System) that references this package as `file:../../package` through the open path |
| `Sandbox~/Assets/Plugins/NuGet/` | the Unity-MCP plugin's third-party DLLs, committed so a first open compiles; `python Tools~/setup-sandbox.py --restore-nuget` brings them back if they are removed, and the npm tarball leaves out `Sandbox~` (AUTHORING.md, "The committed NuGet DLLs"); their licence entries are in `THIRD-PARTY.md` |
| `.github/` | CI workflow and the licence-free validation scripts |
| `Documentation~/` | the documents below |

`Samples~/Showcase/` (the Showcase scene) is being built; `Effects/` holds no effect yet.

## Documentation

- [AUTHORING.md](Documentation~/AUTHORING.md): how to author an effect: the Sandbox, vfxcli.py and
  every verb, the recipe format, texture import, inspect, the verified pitfalls, and the differences
  from DESIGN.md.
- [CAPTURE.md](Documentation~/CAPTURE.md): how a capture works and what `stats.json` guarantees.
- [CI.md](Documentation~/CI.md): the CI jobs and how to run the validation scripts locally.
- [DESIGN.md](Documentation~/DESIGN.md): the contract the package was built against.
- [CHANGELOG.md](CHANGELOG.md).

## Licence

MIT. See `LICENSE`.

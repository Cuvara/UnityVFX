# com.cuvara.vfx

Agent-driven VFX Graph authoring for Unity 6 (URP). A coding agent can build a visual effect from
a JSON recipe, read back any graph as JSON, import generated textures deterministically, and
capture a reproducible contact sheet plus a stats file that a reviewer or a lint can read.

This package is the **mechanism**. The style profile, budgets and the lint that judge an effect
live in the project that consumes it (for the Cuvara RPG, the `rpg-factory` plugin).

- Unity `6000.6`, `com.unity.visualeffectgraph` 17.6.0, `com.unity.render-pipelines.universal` 17.6.0.
- No dependency on Entities, netcode, VContainer or any other Cuvara package.

## Agent interface

One entry point, JSON in and JSON out:

```csharp
string Cuvara.VFX.Editor.VfxCli.Run(string verb, string jsonArgs);   // returns JSON
void   Cuvara.VFX.Editor.VfxCli.Batch();                             // -executeMethod, batch mode
```

```
python Tools~/vfxcli.py <verb> [--project <unity project>] [--args <file.json>] [--out <file.json>]
```

Verbs: `catalog`, `describe`, `build`, `apply`, `import-texture`, `inspect`, `capture`. Every result
is `{"ok": bool, "verb": str, "errors": [..], "warnings": [..], ...payload}`. Exit codes: 0 ok,
1 the verb reported `"ok": false`, 2 usage, 3 no editor available.

An effect is a `<Name>.vfxrecipe.json` (schema in `Schemas~/`) committed next to the `.vfx` it
builds. Never edit a `.vfx` as text.

## Layout

| Path | Content |
|---|---|
| `Runtime/` | `Cuvara.VFX`: `VfxLibrary`, `VfxPlayer`, `VfxSocket`, `ChainLightningBinder` |
| `Editor/` | `Cuvara.VFX.Editor`: `VfxCli`, recipe builder, texture import, capture |
| `Editor/Bridge/` | `Unity.Testing.VisualEffectGraph.Editor`: `VfxGraphBridge` only |
| `Library/` | shared subgraphs, gradients and textures used by every effect |
| `Effects/` | one folder per effect: recipe, `.vfx`, prefab, textures |
| `Samples~/Showcase/` | prototype scene |
| `Sandbox~/` | agent-owned Unity project that references this package as `file:../..` |
| `Schemas~/` | JSON Schemas of the recipe, the capture stats and the describe output |
| `Tools~/` | `vfxcli.py`, `setup-sandbox.py` |
| `.github/scripts/` | licence-free validation scripts |

## Documentation

Everything is under `Documentation~/`:

- `DESIGN.md`: the contract (verbs, file formats, layout, conventions).
- `AUTHORING.md`: how to author an effect, and the verified pitfalls of the VFX Graph internals.
- `CAPTURE.md`: how a capture works and what `stats.json` guarantees.
- `SHOWCASE.md`: the Showcase sample scene.
- `CI.md`: the CI jobs and how to run the validation scripts locally.

## Licence

MIT. See `LICENSE`.

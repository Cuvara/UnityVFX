# Authoring effects with com.cuvara.vfx

How agents and people open the agent-owned Sandbox project, drive it, and what has gone wrong
before. The formats and verbs are in [DESIGN.md](DESIGN.md).

## Sandbox

`Sandbox~/` is a Unity 6000.6.9f1 project committed in this repository: URP 17.6.0 (HDR, Forward+,
depth and opaque textures), VFX Graph 17.6.0, the Unity-MCP plugin `com.ivanmurzak.unity.mcp`
1.7.0 (OpenUPM), the Test Framework and the Input System (active input handling: Input System
package only), linear colour space. It contains nothing game-specific. Agents may open, close,
drive and rebuild it without asking. Only one editor may have it open at a time. Never open, drive
or modify another Unity project: the game client's editor belongs to the user, and it also runs
Unity-MCP on this machine.

### Never open `Sandbox~` directly: use the open path

Unity 6000.6 cannot map package scripts to their classes when the project path has a hidden
component (see Pitfalls). The project is therefore always opened through a link in a per-clone
folder outside the repository, the **sandbox home**. `Tools~/setup-sandbox.py` creates it:

| Path | What it is |
|---|---|
| `<sandbox home>` | `%LOCALAPPDATA%\Cuvara\vfx-sandbox-<8 hex of a hash of the repository path>` on Windows, `~/Library/Caches/Cuvara/...` on macOS, `~/Cuvara/...` on Linux; `CUVARA_VFX_SANDBOX_HOME` overrides it |
| `<sandbox home>/Sandbox` -> `Sandbox~/` | the **open path**: the only path an editor or `unity-mcp-cli` gets |
| `<sandbox home>/package` -> repository root | the package: `Packages/manifest.json` has `"com.cuvara.vfx": "file:../../package"` |
| `Sandbox~/Assets/Showcase` -> `Samples~/Showcase` | only when `Samples~/Showcase` exists (gitignored) |

The sandbox home may contain no component that starts with `.` or ends with `~` and may not be
inside the repository. No link inside the repository may point at the repository root or at an
ancestor of itself: that is a directory cycle (see Pitfalls), and `setup-sandbox.py` stops with a
hard error naming the link. `file:../../package` resolves only through the open path, which is
the only way the project is opened. `Tools~/sandbox_paths.py` holds these rules;
`resolve_project("Sandbox~")` returns the open path, so `vfxcli.py --project Sandbox~` keeps working.

**Pass the open path, never `Sandbox~`, to every `unity-mcp-cli` command. Start and stop the
editor only with `setup-sandbox.py --open` and `--close`.**

### Open, watch, check, drive, close

Run from the repository root (`python` is any Python 3.8+):

```sh
python Tools~/setup-sandbox.py                 # once per clone, and whenever in doubt; prints JSON
OPEN="$(python Tools~/setup-sandbox.py --print-open-path)"

python Tools~/setup-sandbox.py --open          # setup, refuse if this clone's editor already runs,
                                               # start it, print its memory every few seconds until
                                               # Unity-MCP is ready (--hold N: watch N s more)
python Tools~/setup-sandbox.py --watch --duration 60   # PID, private bytes, working set
unity-mcp-cli status "$OPEN"                    # must say: Unity is running (PID n), MCP server reachable
```

The memory guard of `--open`, `--watch` and `--close` kills the Sandbox editor (that PID and its
own child processes, nothing else) when its private bytes pass `--memory-limit-gb` (default 12,
or `CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB`). A healthy Sandbox editor stayed flat at about 2.6 to 2.9 GB
private bytes and 2.1 GB working set, with the Search index ready (7332 documents).

Before the first `run-tool`, prove that the answering editor is the Sandbox, not the user's editor:

```sh
unity-mcp-cli run-tool script-execute "$OPEN" \
  --input '{"csharpCode": "Application.dataPath + \" pid \" + System.Diagnostics.Process.GetCurrentProcess().Id"}'
# expected: <open path>/Assets and the PID that status printed
```

Drive it with `run-tool`:

```sh
unity-mcp-cli run-tool script-execute "$OPEN" --input-file args.json   # {"csharpCode": "..."}
unity-mcp-cli run-tool rql-query "$OPEN" \
  --input '{"query": "console | where logType == '"'"'Error'"'"' or logType == '"'"'Exception'"'"' | select logType message"}'
unity-mcp-cli run-tool rql-query "$OPEN" --input '{"query": "editor | select isCompiling isUpdating isPlaying"}'
unity-mcp-cli run-tool tests-run "$OPEN" --input-file tests.json
```

Check the project settings (returns JSON with `ok`, `problems`, the active pipeline, HDR, depth and
opaque textures, rendering path, colour space, input handling, VFX resources and the resolved
package versions, sources and paths):

```sh
unity-mcp-cli run-tool script-execute "$OPEN" \
  --input '{"csharpCode": "Cuvara.VFX.Sandbox.Editor.SandboxSetup.Report()"}'
```

`SandboxSetup.Apply()` (`Sandbox~/Assets/Editor/SandboxSetup.cs`, menu *Tools > Cuvara VFX
Sandbox*) creates or repairs the same settings through editor APIs and saves them; it is
idempotent. A change of the input handling takes effect after an editor restart.

Close the editor (not `unity-mcp-cli close`, and not `EditorApplication.Exit` from a tool call;
see Pitfalls):

```sh
python Tools~/setup-sandbox.py --close         # SaveAssets through Unity-MCP, close the window of
                                               # that PID, wait up to 300 s (memory guard on),
                                               # then kill exactly that PID
```

`--close` finds the editor by its command line (it carries the sandbox home) and reports how it
ended: `exited by itself N s after the close request (exit code 0)`, or killed by the script
after the timeout or above the memory limit (exit code 1). An exit code other than 0 is reported
as a problem: another process ended the editor. `python Tools~/setup-sandbox.py --remove` removes
the links (links only) and an empty sandbox home.

### Fresh clone

1. `python Tools~/setup-sandbox.py`: checks that Unity 6000.6.9f1 is installed and that the
   repository holds no link cycle, removes links of the first layout (as links), creates the
   sandbox home and its links, and pins the Unity-MCP plugin to a local server
   (`unity-mcp-cli bootstrap-local`) when `Sandbox~/UserSettings/AI-Game-Developer-Config.json`
   is missing or not in `Custom` mode.
2. `python Tools~/setup-sandbox.py --open`. The first import takes several minutes.

`Sandbox~/Assets/Plugins/NuGet` (the plugin's NuGet DLLs) is committed, as in the game client:
without it the first open stops at Unity's compile-errors dialog (see Pitfalls).

## Agent interface: `Tools~/vfxcli.py`

Every verb (DESIGN.md, "Agent interface") runs through one command. It prints the result JSON on
stdout and, with `--out`, writes the same text to a file; progress goes to stderr.

```sh
python Tools~/vfxcli.py catalog                                  # --project Sandbox~ is the default
python Tools~/vfxcli.py catalog --json '{"filter": "spawn", "include_sub_variants": true}'
python Tools~/vfxcli.py describe --json '{"asset": "Assets/Effects/Spark.vfx"}' --out spark.json
python Tools~/vfxcli.py describe --args args.json --mode batch  # force a batch-mode editor
```

| Exit code | Meaning |
|---|---|
| 0 | `"ok": true` |
| 1 | `"ok": false` (the errors say why), or the call into Unity failed |
| 2 | usage: no verb, a verb name that is not `[a-z][a-z0-9-]*`, unreadable or non-object arguments |
| 3 | no editor available: the open path or the package link is missing (the message names `python Tools~/setup-sandbox.py`), an editor has the project open but Unity-MCP does not answer, a batch run was asked while an editor has the project open, or Unity 6000.6.9f1 is not installed |

- **Editor path** (`--mode editor`, or `auto` when this clone's Sandbox editor runs): the verb runs
  through `unity-mcp-cli run-tool script-execute <open path>`, which calls `VfxCli.Run`. The
  arguments and the result travel through files in a temporary folder, because script-execute cuts
  a returned string at 20000 characters (see Pitfalls); the catalog is about 800 KB.
- **Batch path** (`--mode batch`, or `auto` when no editor has the project open): `Unity.exe
  -batchmode -projectPath <open path> -executeMethod Cuvara.VFX.Editor.VfxCli.Batch -vfxVerb <verb>
  -vfxArgs <file> -vfxOut <file> -logFile <file>`, with graphics (never `-nographics`). vfxcli.py
  watches its private bytes and kills that PID above the memory limit (12 GB by default). A batch
  `catalog` took 14 s with a 1.7 GB peak; an editor `catalog` takes about 3 s.
- The two paths give the same bytes: `catalog` through the editor and through a batch editor had
  the same SHA-256.
- **Path arguments** (`recipe`, `asset`, `effect`, `source`, `target`, `out`, `file`, `texture`, at
  any depth of the arguments) may be written relative to the shell's current directory, as the
  examples in this file and DESIGN.md do. vfxcli.py rewrites them before the call and logs each
  rewrite on stderr: a path inside the repository becomes `Packages/com.cuvara.vfx/<path>`, a path
  inside `Sandbox~/Assets` or `Sandbox~/Packages` becomes `Assets/...` or `Packages/...` (also when
  given as an absolute path: the editor sees the project through the open path, not `Sandbox~`),
  any other path becomes absolute, and `out` is always absolute. Paths that already start with
  `Assets/` or `Packages/` are passed unchanged. `VfxCli.Run` itself does not do this: its current
  directory is the project.

### Read a graph with `describe`

`describe` gives every node a stable id in the serialised order of the graph's children: contexts
`c0`, `c1`; their blocks `c0.b0`; operators `o0`; parameters `p0`; other models `x0`; systems
`s0`. A slot is `<id>.<slot path>`, the path joining slot names with dots
(`c1.b0._Position`, `o3.coordinate`, `c5.b0.A.x`). Math operators have output slots without a
name; their address is `#<index>` (`o0.#0`). Each slot entry carries its address component as
`ref`. Links are listed once, from the input side, sorted by `to`; flow links by `to`, `to_slot`.
A parameter carries `tooltip` only when it has one, so graphs without tooltips describe as before.
Two calls on the same asset gave byte-identical output for all 36 graphs of the VFX Graph samples.

### What Unity's sample graphs show

Read with `describe` from a gitignored scratch copy (`Sandbox~/Assets/Scratch`, deleted
afterwards) of `Samples~/VFXGraphAdditions` (Lightning, Flames, Smoke, Sparks) and
`Samples~/VFXLearningTemplates/VFX` (32 graphs) of the built-in VFX Graph 17.6.0 package. Their
assets are under the Unity Companion License: never copy them into this repository.

- **A system** is a chain of contexts joined by flow links, slot 0 to slot 0:
  `VFXBasicSpawner` (context type `Spawner`, data `SpawnEvent`) -> `VFXBasicInitialize` (`Init`)
  -> `VFXBasicUpdate` (`Update`) -> one or more outputs (`Output`). The spawner is a system of its
  own (`data_type` `SpawnEvent`, capacity null); Initialize, Update and the outputs share one
  particle system (`data_type` `Particle` or `ParticleStrip`) that holds `capacity`,
  `boundsMode` and the strip settings. One Update can feed several outputs (`MultipleOutputs`: one
  flow link from Update to each of three outputs).
- **Spawning:** a burst is block `VFXSpawnerBurst` in the spawner, setting `repeat` `Single` or
  `Periodic`, input `Count` (float; `Vector2` when `spawnMode` is `Random`) and `Delay`. A
  continuous rate is `VFXSpawnerConstantRate` with input `Rate`. The spawner's own settings
  `loopDuration` and `loopCount` (`Infinite`, `Constant`, `Random`) decide looping.
- **GPU events** are slot links, not flow links: `TriggerEvent` in an Update (output `evt`) links
  to the `evt` input of a `VFXBasicGPUEvent` context, which then has a flow link to its own
  Initialize (Lightning: `c6.b1.evt -> c7.evt`, flow `c7 -> c8`).
- **Attributes** are set with `SetAttribute` blocks: setting `attribute` (`lifetime`, `position`,
  `velocity`, `color`, `alpha`, `size`, `scale`), `Composition` (`Overwrite`, `Add`, `Multiply`,
  `Blend`), `Random` (`Off`, `Uniform`, `PerComponent`) and `Source`. The input slot is the
  attribute name with a leading underscore (`_Lifetime`, `_Color`, `_Position`); a random block
  has inputs `A` and `B` instead. The library names them `|Set|_Lifetime|Random Uniform`; catalog
  prints the plain `title` "Set Lifetime Random Uniform" next to `name`.
- **Colour over life** is block `AttributeFromCurve` with `attribute` `color`, `SampleMode`
  `OverLife`, `ColorMode` (`Color`, `Alpha`, `ColorAndAlpha`) and `Composition` (usually
  `Multiply` in an output, `Overwrite` in Initialize); its input `Color` is a `Gradient`. Alpha
  over life is the same block with `attribute` `alpha` and an `AnimationCurve` input `Alpha`.
  Sizes over life use `attribute` `size` with input `Size`. In the samples these blocks sit in the
  output context more often than in Update.
- **HDR colour** is a colour value above 1: Lightning sets `color` with
  `[2.76, 34.89, 64]` (a `Vector3` input of `SetAttribute`), Sparks multiplies a gradient over life.
- **Blend mode** is the output setting `blendMode`: `Opaque`, `Alpha`, `Additive`,
  `AlphaPremultiplied` (across the samples: 40 Opaque, 39 Alpha, 17 Additive, 1 premultiplied).
  `colorMapping` is `Default` or `GradientMapped` (then the output has a `gradient` input);
  `useSoftParticle`, `sort`, `cullMode`, `zWriteMode`, `zTestMode` and `useAlphaClipping` sit next
  to it. The texture is the output input `mainTexture` (unlit) or `baseColorMap` (URP Lit); the
  default is `White1px` from `com.unity.render-pipelines.core`.
- **Additive quads** (the fixture `Tests/Fixtures/BurstQuads`): `VFXPlanarPrimitiveOutput` with
  `primitiveType` `Quad` and `blendMode` `Additive` (15 in the samples), most often with an
  `Orient` block in `mode` `FaceCameraPlane` (6 of 15; 3 had no `Orient` block).
- **Exposed parameters** are `VFXParameter` nodes (`exposed` true, `exposed_name`) whose output
  slot `o` links into inputs (`p0.o -> o2.PositionMap`). Built-in values come from operators such
  as `VFXDynamicBuiltInParameter` (`Delta Time (VFX)`) and `VFXAttributeParameter`
  (`Get|_Position|Current`).

## Recipe: build and apply

A recipe (`<Name>.vfxrecipe.json`, schema `Schemas~/vfx-recipe.schema.json`, id
`cuvara.vfx-recipe/1`) is the source of truth of an effect; `build` turns it into `<Name>.vfx` in
the same folder. The worked example is the fixture
`Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json` (one burst of 24 additive quads, lifetime
0.6 to 0.9 s, capacity 32, colour over life, one exposed colour `Tint`).

```sh
python Tools~/vfxcli.py build --json '{"recipe": "Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json"}'
python Tools~/vfxcli.py build --json '{"recipe": "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json"}'
python Tools~/vfxcli.py build --json '{"recipe": "C:/Workspaces/UnityIndie/UnityVFX/Effects/Spark/Spark.vfxrecipe.json"}'
python Tools~/vfxcli.py apply --args ops.json   # {"asset": "...", "ops": [...]}
```

The verbs take project-relative paths (`Assets/...`, `Packages/com.cuvara.vfx/...`) or absolute
ones. An absolute path inside the repository resolves through the package (the nearest
`package.json` that names a registered package), so the repository path and the
`<sandbox home>/package` path both work. Through vfxcli.py a path relative to the shell's current
directory works too (Agent interface, path arguments); through `VfxCli.Run` it does not (see
Pitfalls).

### Top level

| Field | Required | Meaning |
|---|---|---|
| `schema` | yes | `"cuvara.vfx-recipe/1"`; anything else is an error before any op runs |
| `id` | yes | effect id, kebab-case (`chain-lightning`) |
| `vfx_graph` | no | the VFX Graph major.minor the recipe was written for; another installed major.minor is a warning |
| `meta` | no | `duration_seconds`, `looping`, `tags`, `phases` (`name`, `seconds`); not read by `build`, kept for capture and lint |
| `ops` | yes | the ops, run in order |

An unknown top-level field is an error.

### Ids and slot addresses

- Every node an op creates has a recipe-local `id`: `^[A-Za-z_][A-Za-z0-9_-]*$`, unique in the
  recipe. Blocks have ids of their own (`burst`, not `spawn.burst`).
- `apply` starts with the ids `describe` prints for the existing graph (`c0`, `c0.b1`, `o2`, `p0`);
  new nodes use the ids their ops give, which may not reuse one.
- A slot is `<id>.<slot path>`: the master slot name, then member names, joined with dots
  (`init.bounds.center`, `burst.Count`, `tint._Color`). A slot without a name (the output of `Add`,
  `Multiply` and other math operators) is `#<index>` (`o0.#0`), as in `describe`. `<id>.out` is
  the only output of a node that has exactly one (a parameter's `o`, an operator's `#0`); a slot
  really named `out` wins. Indices are never used for named slots.
- `link_slots` takes `from` among output slots and `to` among input slots; `set_slot` and the
  `slots` map address input slots, or the value of a parameter.

### Ops

Names, fields and one-line summaries are also printed by `catalog` (`recipe_ops`,
`recipe_op_fields`). An unknown op, field, id, type, variant, setting, slot or enum value is an
error that names the op index and lists the valid names, nearest first (`op 3 (add_block 'burst'):
unknown block type 'VFXSpawnerBurstt'; valid block types (nearest first): VFXSpawnerBurst, ...`);
the result also carries `op_error` (`index`, `op`, `message`, `valid_names`). The first failing op
stops the build.

| Op | Required | Optional | Does |
|---|---|---|---|
| `add_parameter` | `id`, `type`, `name` | `value`, `exposed` (default true), `category`, `tooltip` | a parameter of a catalog parameter type, by its `name` (`Color`, `Vector 3`) or `type` (`Vector3`); `name` is the exposed name |
| `add_context` | `id`, `type` | `variant`, `label`, `settings`, `slots` | a context |
| `add_block` | `id`, `context`, `type` | `variant`, `settings`, `slots` | a block at the end of `context` |
| `add_operator` | `id`, `type` | `variant`, `settings`, `slots` | an operator |
| `set_setting` | `node`, `name`, `value` | | one setting |
| `set_slot` | `slot` | `value`, `space` (`None`, `Local`, `World`) | an input slot value or space, or a parameter value |
| `link_slots` | `from`, `to` | | output slot to input slot |
| `link_flow` | `from`, `to` | `from_slot`, `to_slot` (default 0) | flow from one context to the next |
| `use_subgraph` | `id`, `asset` | `context`, `slots` | `.vfx` gives a subgraph context, `.vfxoperator` an operator, `.vfxblock` a block in `context`; `slots` are the subgraph's exposed parameters (see Subgraphs) |
| `set_output` | `node` | `texture` (an asset value), `blend_mode`, `color_mapping`, `gradient` | `blendMode` and `colorMapping` settings, the `mainTexture` (or `baseColorMap`) input, the `gradient` input of `GradientMapped` (set `color_mapping` in the same op or before) |
| `comment` | `text` | `title` | a sticky note |

- **`type`** is the catalog `type` (`VFXSpawnerBurst`) or `type_full`. **`variant`** is a catalog
  `variant.id` (`Spawn/Single Burst`, `Context/#2Output Basic/Output Particle|Unlit|Quad`): the node
  is created as the graph window creates it from that menu entry. Without `variant`, a type with
  one catalog entry uses that entry, and a type with several entries (`SetAttribute`,
  `AttributeFromCurve`) is created with its field defaults; then `settings` choose the attribute.
  Use `variant` for nodes whose variant setting is not JSON (`VFXInlineOperator`).
- **`settings`** and **`slots`** are maps applied in order, settings first, after the node is in
  the graph. Setting names are the catalog's; settings that a context forwards (Initialize:
  `capacity`, `boundsMode`, `dataType` live on the system's data) are listed and set on the context.
  A setting can change the slots (`attribute` turns `_Lifetime` into `_Color`; `Random` turns the
  input into `A` and `B`), so set it before the slots.
- **Data settings follow the flow.** A flow link from Initialize hands Initialize's data to Update
  and the outputs (`VFXContext.InnerLink`), so set `capacity` and `boundsMode` on the Initialize
  context.
- **Values:** numbers, booleans and strings as JSON; enums by name (flags as `"A, B"` or a list);
  `Vector2/3/4` as arrays; `Color` as `[r, g, b]` or `[r, g, b, a]` (HDR values above 1 allowed);
  `Gradient` as `{"mode", "color_space", "color_keys": [{"t", "color"}], "alpha_keys": [{"t",
  "alpha"}]}` (the `describe` form, at most 8 keys each); `AnimationCurve` as `[[t, v], ...]` with
  linear tangents or `{"keys": [{"time", "value", "in_tangent", "out_tangent", ...}], "pre_wrap",
  "post_wrap"}`; VFX structs (`Position`, `Vector`, `DirectionType`, `AABox`, `Sphere`) as objects
  of their fields (`{"center": [0, 1, 0], "size": [4, 4, 4]}`), and a struct with one field
  (`Position.position`, `Vector.vector`) also as that field's value (`[0, 1, 0]`). A map changes
  only the fields it names.
- **Asset values** (a texture slot, a `Texture2D` parameter, `set_output` `texture`) are a
  project-relative path (`Assets/...`, `Packages/<package>/...`), `{"guid": "<32 hex>"}` or
  `{"path": "..."}`; the builder loads the asset and the graph stores its GUID (`describe` prints
  `path` and `guid`). `guid` and `path` together must name the same asset. A missing asset, an
  unknown GUID, an asset of another type (`is not a Texture2D`) or another field in the object is an
  error. `use_subgraph` `asset` is a path only (its extension picks the node).

Every op and op field above is used by a recipe that builds and is checked through `describe` in
`Tests/Editor/RecipeBuildTests.cs` (the BurstQuads fixture) and `Tests/Editor/RecipeCoverageTests.cs`
(`set_output` with every field, asset values by path and by GUID, slot spaces, `add_operator`,
`set_setting`, `link_flow` flow slots, a context label, a parameter's category and tooltip, the three
subgraph kinds); `EveryOpAndField_IsUsedByARecipeThatBuilds` fails when an op field is added
without a recipe that uses it.

### Subgraphs

`use_subgraph` adds a node that holds a subgraph asset; `describe` prints the asset under the
node's setting `m_Subgraph` (`path`, `guid`), its inputs under `inputs` and links into them under
`links`. The only setting of the three subgraph nodes in VFX Graph 17.6 is `m_Subgraph`, which
`asset` sets, so the op has no `settings` field (`unknown field of use_subgraph 'settings'`).

| Asset | Node | Inputs (`slots`, link targets) | Outputs |
|---|---|---|---|
| `.vfxoperator` (`VisualEffectSubgraphOperator`) | operator `VFXSubgraphOperator` | the exposed parameters of the subgraph, by exposed name, in their order | its output parameters, by name (`twice.Out`) |
| `.vfxblock` (`VisualEffectSubgraphBlock`) | block `VFXSubgraphBlock` in `context` | the exposed parameters | none |
| `.vfx` (`VisualEffectAsset`) | context `VFXSubgraphContext` (`context_type` `Subgraph`) | the exposed parameters of that graph | none; its flow inputs are the graph's events and the start and stop of its spawners without an input link (`VFXSubgraphContext.RecreateCopyIfNeeded`; not exercised by the tests) |

- A `.vfxblock` declares the contexts it suits with the setting `m_SuitableContexts` of its
  `VFXBlockSubgraphContext` (`Init`, `Update`, `Output`, `InitAndUpdate`, ...; default
  `InitAndUpdateAndOutput`); a block in another context fails with `cannot be added to context`.
- `context` is required for a `.vfxblock` and refused for the other two.
- Subgraph assets are made in the graph window or, by code, with the bridge:
  `VfxGraphBridge.GraphEdit.CreateSubgraph(path, blockContextId)` creates an empty `.vfxoperator` or
  `.vfxblock` with VFX Graph's own `VisualEffectAssetEditorUtility.CreateNew<T>` (the call of its
  "Convert to Subgraph"); a `.vfxblock` also gets its `VFXBlockSubgraphContext` (the graph window
  adds it when it opens the asset; `CreateNew` does not), registered under `blockContextId`.
  `AddParameter` adds the inputs, `AddOutputParameter` the outputs of an operator subgraph (a
  parameter with `isOutput`, whose input slot is `<id>.i`); then `Save()` and import the asset
  before a graph uses it. A `.vfx` used as a subgraph is an ordinary graph (`build` a recipe).
  `RecipeCoverageTests` builds all three this way in a temporary folder; nothing from Unity's
  samples is copied.

### What `build` and `apply` do

1. Check the recipe (top level, then each op's name and fields).
2. Create the graph in `Assets/CuvaraVfxBuildTemp/<Name>.vfx` with VFX Graph's
   `VisualEffectAssetEditorUtility.CreateNewAsset` (`apply`: copy the asset there with
   `AssetDatabase.CopyAsset`).
3. Run the ops through `VfxGraphBridge.GraphEdit` (`VFXModel.AddChild`, `SetSettingValue`,
   `VFXSlot.value`, `VFXSlot.Link`, `VFXContext.LinkTo`), place the new nodes, give each parameter
   its graph node, and write the file with `VisualEffectResource.WriteAssetWithSubAssets`. No `.vfx`
   text is written or patched.
4. Compile (`VfxGraphBridge.CompileReport`): the in-memory compile the graph window makes
   (`VFXGraph.Compile`), the errors VFX Graph reports per node, a re-import of the copy (which
   generates the shaders) with every error logged meanwhile, and the error messages of the
   generated shaders. Any error stops here; the real asset has not been touched.
5. Copy the compiled file over `<Name>.vfx` and re-import it. Its `.meta` is not written, so its
   GUID stays the same (`guid_stable`). The temporary folder is deleted in every case.

Result payload: `recipe`, `recipe_sha256`, `recipe_id` (build only), `asset` (`path`, `guid`),
`ops_applied`, `compile_errors`, `created`, `guid_stable`, `previous_sha256`, `sha256`,
`describe_sha256` (SHA-256 of the compact JSON `VfxCli.Run("describe")` returns for the asset) and
`systems` (`id`, `name`, `data_type`, `capacity`). Compile warnings become warnings of the result.

A recipe is deterministic in its graph, not in its bytes: two builds gave the same
`describe_sha256` (`1224b507...`, twice) and different `.vfx` SHA-256 (see Pitfalls). Compare
`describe` output, not file hashes.

## Texture import

`import-texture` turns a generated PNG (usually opaque, the effect on black) into an effect texture:
it runs an ordered list of steps on the pixels, writes the result as a PNG asset and sets its import
settings. The result records the SHA-256 of the source and of the output, so a delivery record can
prove which approved generation produced which game texture.

```sh
python Tools~/vfxcli.py import-texture --args glow.json
```

```json
{
  "source": "C:/gen/runs/2026-11-13/glow_00001.png",
  "target": "Effects/Spark/Spark_Glow.png",
  "transform": [
    {"step": "luminance_to_alpha", "color": "unpremultiply"},
    "premultiply",
    {"step": "resize_pot", "max_size": 256},
    {"step": "flipbook", "columns": 4, "rows": 4},
    {"step": "wrap", "mode": "clamp"},
    "srgb"
  ]
}
```

| Argument | Required | Meaning |
|---|---|---|
| `source` | yes | a PNG: project-relative (`Assets/...`, `Packages/<package>/...`) or absolute (anywhere, for example a generator's output folder) |
| `target` | yes | the `.png` asset to write: project-relative, or absolute inside the project or a local package (the repository) |
| `transform` | no | the steps, run in order; each a step name (`"premultiply"`) or an object `{"step": name, ...parameters}` |

### Steps

Names and parameters are also printed by `catalog` (`texture_steps`, `texture_step_fields`).

| Step | Parameters | Does |
|---|---|---|
| `luminance_to_alpha` | `color`: `keep` (default), `white`, `unpremultiply` | alpha = Rec. 709 luminance of RGB, `(2126 R + 7152 G + 722 B + 5000) / 10000`; `white` sets RGB to 255; `unpremultiply` divides RGB by the new alpha (`min(255, round(c * 255 / a))`, 0 where alpha is 0), so an image on black becomes straight alpha |
| `premultiply` | none | RGB = `round(c * a / 255)` |
| `pack_channels` | `r`, `g`, `b`, `a`: at least one | each named channel from a constant in [0, 1] (`round(v * 255)`) or a source `{"file": path, "channel": name}` with the channel `r`, `g`, `b`, `a` or `luminance`; without `file` the source is the image before this step (so channels can be swapped); a file must have the image's size at this step; unnamed channels are kept |
| `resize_pot` | `max_size`: a power of two, default 16384 | each axis to its nearest power of two (n goes to p when n * n < 2 * p * p for the power of two p <= n < 2p, else to 2p: 5 to 4, 6 to 8, 700 to 512), then both axes halved until neither exceeds `max_size`; an exact area average (box filter) in integers, rounded once |
| `flipbook` | `columns`, `rows` (required) | records the frame grid in the result (`flipbook`: columns, rows, frames, frame size); the output width must divide by `columns` and the height by `rows`, checked after every step |
| `wrap` | `u`, `v`, or `mode` (both): `repeat` or `clamp` | importer wrap mode per axis; an axis not named is `clamp` |
| `linear` | none | import as linear data (sRGB off): masks, noise, flow maps |
| `srgb` | none | import as sRGB colour (the default) |

`flipbook`, `wrap` and one of `linear` / `srgb` may each appear once; the pixel steps may repeat.
Resize straight-alpha colour after `premultiply`, or the transparent black around a shape darkens
its edges.

Every pixel step works on a managed RGBA byte array with integer arithmetic: no GPU blit, no
platform filter, no floating point except the `round(v * 255)` of a `pack_channels` constant. PNGs
are decoded and encoded with Unity's CPU codec (`ImageConversion.LoadImage`,
`ImageConversion.EncodeArrayToPNG`; 8 bits per channel, RGBA). The same source and transform give
the same output bytes: two runs and a second target gave the same `output_sha256` (the tests and
the check below).

### Import settings

Set on the target's `TextureImporter` after the PNG is written, and read back into `import_settings`:

| Setting | Value |
|---|---|
| texture type, shape | `Default`, `Texture2D` |
| `sRGBTexture` | true; false after `linear` |
| `alphaSource` | `FromInput` when any pixel has an alpha below 255, else `None` |
| `alphaIsTransparency` | true for straight alpha in sRGB: alpha present, sRGB, and not premultiplied (the last of `premultiply`, `luminance_to_alpha` and a `pack_channels` that sets `a` was not `premultiply`) |
| wrap U, V | from `wrap`, else `Clamp` |
| filter, mip maps | `Bilinear`; mip maps on (box filter) |
| `npotScale` | `None`: the size is the one the steps made; a size that is not a power of two is a warning |
| `maxTextureSize` | the smallest of 32 to 16384 that keeps the output size |
| compression | `Compressed` (normal quality), not crunched, not readable; no platform overrides |

### What it does, in order

1. Check the transform (every step name, parameter and value; on failure `step_error` with the
   index, the step and the valid names nearest first), then the source (exists, starts with the
   PNG signature) and the target (inside the project or a package, ends with `.png`).
2. Decode the source and run the steps in memory; check the flipbook grid on the final size.
3. Write the PNG (creating missing folders), import it, set the import settings and re-import. When
   writing or importing fails, the previous PNG and `.meta` are put back where they changed (a new
   target is deleted). An error before this step never touches the target. Tested at each stage
   with SHA-256 before and after: an unknown step, a `pack_channels` file of another size, a
   flipbook grid that does not divide the resized image, and a write that fails (read-only target).

Result payload: `source`, `source_sha256`, `source_size`, `target`, `steps` (each applied step with
its parameters and defaults; `resize_pot` adds `from` and `to`), `inputs` (each `pack_channels`
file with its SHA-256 and size), `size`, `flipbook`, `created`, `previous_sha256`, `guid`,
`guid_stable`, `output_sha256`, `import_settings`.

Verified on 2026-11-13: a 48x40 opaque radial glow written by a script, imported twice into
`Sandbox~/Assets/Scratch/Glow.png` (gitignored, deleted afterwards) with `luminance_to_alpha`
(`unpremultiply`), `premultiply`, `resize_pot` 32, `flipbook` 2x1, `wrap` `clamp` and `srgb`: both
runs `ok`, 32x16, `source_sha256` `b68857f7...` and `output_sha256` `77941d5f...` both times; the
second run had `guid_stable` true.

## Pitfalls

Each entry is a verified failure: symptom, cause, fix, file.

### Package scripts have no class when the project path has a hidden component

- **Symptom:** every ScriptableObject from a package is saved with `m_Script: {fileID: 0}`. The
  console shows `No script asset for UniversalRenderPipelineGlobalSettings` and two
  `NullReferenceException` in `RenderPipelineResourcesEditorUtils.TryReloadContainedNullFields`; a
  new `UniversalRenderPipelineGlobalSettings N.asset` appears at each start or refresh.
  `MonoScript.GetClass()` is null for 1333 of 1333 scripts of `com.unity.ugui`,
  `com.unity.inputsystem` and `com.ivanmurzak.unity.mcp` (and for URP, VFX Graph and core scripts),
  while `Assets/` scripts resolve.
- **Cause:** Unity 6000.6.9f1 does not map package scripts to classes when the project path has a
  component that ends in `~` or starts with `.`. Measured with the same Library: opened at
  `.../UnityVFX/Sandbox~` 0/1333 (twice, with a clean Library), through a junction named
  `.sandbox` 0/1333, through a junction at a plain path 929/1333. A local package outside the
  project with a `~` folder in its own path resolves normally.
- **Fix:** open the project through the open path `<sandbox home>/Sandbox` and reference the
  package as `file:../../package` (`<sandbox home>/package` -> repository root). A `file:../..`
  entry resolves relative to the opened path, so through a plain-path link it pointed outside the
  repository: `com.cuvara.vfx: The file [C:\Users\duycu\AppData\Local\Temp\package.json] cannot be
  found`. The first fix, an embedded package through `Sandbox~/Packages/com.cuvara.vfx` ->
  repository root, made a directory cycle (next entry).
- **Healthy baseline** (same three packages, through the open path): 929 of 1333 resolve. The 404
  that do not are expected: 301 under `Tests/` of packages that are not testable (their test
  assemblies are not compiled), 22 `*.pre-Unity.6.5.cs` files compiled out by
  `#if !UNITY_6000_5_OR_NEWER`, 81 others such as the ugui TMP HDRP shader GUIs, whose assembly
  needs HDRP. A count near 0 means the bug, not a missing package.
- **File:** `Tools~/sandbox_paths.py`, `Tools~/setup-sandbox.py`, `Sandbox~/Packages/manifest.json`.

### A link cycle in the project made the editor grow by gigabytes per second

- **Symptom:** the Sandbox editor grew to 21.9 GB working set and about 90 GB private commit,
  about 2 GB every 8 s, until it was killed (PID 10536). PID 9480, never asked to quit, went 7.86
  GB private at 18:38:35, then 9.06, 10.50, 11.81 and 12.80 GB at 18:38:44, when a watchdog killed
  it; PID 4324 had 10.7 GB private about 28 s after its start. In both logs
  (`Sandbox~/Logs/Editor.log` of PID 9480, lines 396-397) the last markers before the growth are
  `Start Indexing on Editor startup` and `[Indexing] Starting Initial Indexing for Assets`, about
  8.5 s after the start. An editor that was then asked to quit logged `Input System module state
  changed to: Shutdown.` and nothing more: the quit did not end an editor that was already
  running away, and `setup-sandbox.py --close` of that time reported `exited` for an editor that
  someone else had killed.
- **Cause (probable, not proven by a stack):** a directory cycle inside the project,
  `Sandbox~/Packages/com.cuvara.vfx` -> repository root, which contains `Sandbox~` again, walked
  by the editor's initial Search indexing. `cmd /c dir /AL /S /B <repository>` printed
  `Sandbox~\Packages\com.cuvara.vfx\Sandbox~\Packages\com.cuvara.vfx\...` without end and had to
  be stopped. Editors opened without that link did not grow.
- **Fix:** no link inside the repository points at the repository or an ancestor of itself: the
  package is reached through `<sandbox home>/package` and `file:../../package`, outside the
  project. `setup-sandbox.py` stops with a hard error naming any such link, removes the old one as
  a link, and `--open`, `--watch` and `--close` kill the editor (that PID and its own children)
  above 12 GB private bytes. Verified with the cycle-free layout on 2026-11-13, three guarded
  opens: peak 2.86 GB private and 2.09 GB working set over 266 s (flat after 20 s), then 2.42 and
  2.51 GB; the Search index `Assets` (roots `Assets`, `Packages`) was ready with 7332 documents;
  `--close` ended the editor by itself in 6.0 s and 3.0 s, exit code 0.
- **File:** `Tools~/sandbox_paths.py` (`links_in_repo`), `Tools~/setup-sandbox.py`,
  `Sandbox~/Packages/manifest.json`.

### Quitting with `EditorApplication.Exit` from a tool call

- **Symptom:** `run-tool script-execute` with `AssetDatabase.SaveAssets(); EditorApplication.Exit(0);`
  answers with a reset connection; the editor then exited after 6 to 9 s twice, and did not end
  three times (one of them is the runaway above). A quit through
  `EditorApplication.delayCall += () => EditorApplication.Exit(0)` returned its reply but never
  ran: the editor stayed open.
- **Cause:** not established; the editor quits while the plugin is still inside the tool call.
- **Fix:** `setup-sandbox.py --close` saves through `script-execute` (`AssetDatabase.SaveAssets()`
  only), then closes the window of exactly that PID (`taskkill /PID n` without `/F`, `SIGTERM`
  elsewhere): a normal quit, which ended the editor 3 of 3 times before. It waits 300 s with the
  memory guard on, kills exactly that PID after the timeout, and reports how the editor ended,
  with its exit code.
- **File:** `Tools~/setup-sandbox.py`.

### `unity-mcp-cli close` does not find an editor opened through a link

- **Symptom:** `unity-mcp-cli close "$OPEN"` prints `SUCCESS: no running Editor for project at
  ...\Sandbox~` and the editor keeps running.
- **Cause:** unity-mcp-cli 0.93.2, `dist/commands/close.js` `resolveEditorPid`: the path is
  canonicalised with `fs.realpathSync` (the link becomes `Sandbox~`), reading
  `Temp/UnityLockfile` fails with `EBUSY` on Windows while the editor runs, the editor's command
  line carries the open path, and the symlink fallback compares the canonical path with itself.
  `status`, `wait-for-ready` and `run-tool` work with the open path.
- **Fix:** `python Tools~/setup-sandbox.py --close`, which finds the editor by the sandbox home in
  its command line (previous entry). Do not update the global unity-mcp-cli for this: the user's
  editor uses it too.
- **File:** `Tools~/setup-sandbox.py`.

### The Unity-MCP plugin starts in Cloud mode

- **Symptom:** after `unity-mcp-cli install-plugin`, `wait-for-ready` times out after 600 s;
  `status` shows `Config Server https://ai-game.dev/mcp ... HTTP 401` and the console
  `[AI Game Developer] The server rejected the authorization`.
- **Cause:** plugin 1.7.0 writes `"connectionMode": "Cloud"` into
  `UserSettings/AI-Game-Developer-Config.json` on first open.
- **Fix:** `unity-mcp-cli bootstrap-local --url http://localhost:<port> --token <random> <project>`
  with the editor closed; `setup-sandbox.py` does it when the config is missing or not `Custom`.
  The plugin then starts its own server (`Library/mcp-server/.../gamedev-mcp-server.exe`, a child
  of the editor) on that port. The Sandbox uses port 26305; the user's editor uses another one.
- **File:** `Tools~/setup-sandbox.py`.

### The editor restarts itself once after the Input System is added

- **Symptom:** the editor PID from `open` disappears and another editor starts. Its command line
  is `"-projectPath" "<path>"` (flag in quotes), which unity-mcp-cli 0.93.2 does not parse, so
  `status` says `Unity is not running with this project` and `close` finds nothing.
- **Cause:** with `activeInputHandler: 0` the Input System package enables its backends and
  restarts the editor.
- **Fix:** the committed `ProjectSettings/ProjectSettings.asset` has `activeInputHandler: 1`
  (set by `SandboxSetup.Apply()`), so a clone does not restart. If it happens, end that editor by
  its PID (find it by its command line containing the project path), never by process name.
- **File:** `Sandbox~/Assets/Editor/SandboxSetup.cs`.

### Missing NuGet DLLs stop the first open at a dialog

- **Symptom:** with `Sandbox~/Assets/Plugins/NuGet` absent, the first open compiles with
  `error CS0234: The type or namespace name 'ReflectorNet' does not exist in the namespace
  'com.IvanMurzak'` and stops at Unity's compile-errors dialog; `wait-for-ready` times out. The
  default 30 s auto-dismiss of `unity-mcp-cli open` ended before the compile (about 45 s).
- **Cause:** the plugin restores its NuGet DLLs only once the editor runs past that dialog.
- **Fix:** keep `Assets/Plugins/NuGet` committed. Recovery without it: `unity-mcp-cli open
  --launch-dismiss-timeout-ms 400000 "$OPEN"` clicks *Ignore*, and the plugin restores the DLLs.
- **File:** `Sandbox~/Assets/Plugins/NuGet/`.

### A string literal counts as a GC allocation the first time a method runs

- **Symptom:** `Assert.That(() => { player.Play("spark", pos); ... }, Is.Not.AllocatingGCMemory())`
  failed although the same play had run before outside the delegate, and the same check passed for
  `Time.time`, `GameObject.SetActive` and re-parenting a `VisualEffect`. Isolated: a
  `Dictionary<string, T>.TryGetValue("s", out _)` inside the measured delegate allocated, the same
  call with the key in a captured local (`k = "s"`) did not, for every value type tried (a package
  class, a private class, `int`, `string`).
- **Cause:** the editor runs Mono in Debug code optimisation (`CompilationPipeline.codeOptimization`
  printed `Debug`); the first execution of a method that loads a string literal resolves the literal,
  and the `GC.Alloc` recorder used by `AllocatingGCMemory` counts it. A lambda is its own method, so a
  warm-up call outside the lambda does not help.
- **Fix:** in an allocation test, build the delegate once, invoke it once, then assert on the same
  delegate (`VfxPlayerTests.WarmPool_PlayAndReturn_DoNotAllocate`). This is a test artefact, not an
  allocation of the code under test.
- **File:** `Tests/Runtime/VfxPlayerTests.cs`.

### `script-execute` cuts a returned string at 20000 characters

- **Symptom:** a snippet that returns `new string('x', 30000)` comes back with 20000 characters and
  the note `[truncated: 20000 of 30000 characters shown; return a slice or a summary]`. The
  catalog result is about 800 KB.
- **Cause:** Unity-MCP 1.7.0 bounds tool results (`Editor/Scripts/API/Tool/Script.Execute.cs`:
  a string to 20000 characters, a JSON result over 131072 characters to its first 20000).
- **Fix:** `vfxcli.py` never returns the result through the tool: the snippet reads the arguments
  from a file, calls `VfxCli.Run` and writes the result to a file, and returns only its length.
- **File:** `Tools~/vfxcli.py` (`run_in_editor`).

### Reading settings of a library model can throw

- **Symptom:** `catalog` warned `block UnityEditor.VFX.Block.ScreenSpaceSize (Size/Screen Space Size)
  could not be read: NullReferenceException`, and that block was missing from the list (134
  blocks instead of 135).
- **Cause:** `VFXModel.GetSettings(false, ...)` evaluates `filteredOutSettings`; `ScreenSpaceSize`
  reads `GetData()` of its parent context there, and the instance cached by `VFXLibrary` has no
  parent.
- **Fix:** the bridge lists settings with `VFXModel.GetSettings(true)` (every `VFXSetting` field
  the model shows or forwards, filtered to those visible in the inspector or the graph) and falls
  back to `VFXModel.GetFields(type)` when that throws; `visible_by_default` is computed in a `try`
  and is null when it throws. Catalog then has 135 blocks and no warning (verified again after the
  next entry's change).
- **File:** `Editor/Bridge/VfxGraphBridge.cs` (`CatalogSettings`), `Editor/Bridge/VfxGraphBridge.Edit.cs`
  (`UserSettings`).

### Catalog listed no settings for Initialize although `capacity` is set there

- **Symptom:** `catalog` printed `"settings": []` for both `VFXBasicInitialize` variants, while the
  graph window shows `capacity` and `boundsMode` on Initialize and `describe` shows them under the
  system. A recipe could not learn the names it has to use on the Initialize context.
- **Cause:** `VFXBasicInitialize.GetSetting` and `GetSettings` forward to its `VFXData`; rendered
  outputs forward to their render-pipeline sub-output, and composed (Shader Graph) outputs replace
  some base settings with those of their topology and shading. `VFXModel.GetFields(type)` sees only
  the model's own fields.
- **Fix:** catalog and the builder use the same list (`UserSettings`: `GetSettings(true)`, then the
  fields as a fallback). Initialize now lists `dataType`, `capacity`, `stripCapacity`,
  `particlePerStripCount`, `boundsMode`; 5 of 396 catalog entries changed (two Initialize, three
  composed outputs, which no longer list the `blendMode` family their Shader Graph decides).
  `set_setting` on Initialize sets the data (`describe`: system `s1` `capacity` 32).
- **File:** `Editor/Bridge/VfxGraphBridge.Edit.cs` (`UserSettings`).

### The default bounds mode warns on every build

- **Symptom:** the first build of BurstQuads succeeded with the warning `compile: c1
  (VFXBasicInitialize): In order to record the bounds, the current graph needs to be attached to a
  scene instance via the Target Game Object panel`.
- **Cause:** a new Initialize context has `boundsMode` `Recorded`, which expects bounds recorded in
  the graph window.
- **Fix:** set `boundsMode` to `Manual` and give `bounds` (`center`, `size`), or `Automatic`, in the
  recipe. BurstQuads uses `Manual` with a 4 m box; later builds had no warning.
- **File:** `Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json`.

### Two builds of the same recipe give different `.vfx` bytes

- **Symptom:** three builds of the unchanged BurstQuads recipe wrote `.vfx` files with the SHA-256
  `421f1d66...`, `aa4728e2...`, `7c26acdb...`, while `describe_sha256` of the last two was the same
  (`a1e11d66...`).
- **Cause:** the graph's nodes are sub-assets of the `.vfx`; Unity gives every newly created
  sub-asset a new local file id when the asset is written. The graph is the same; the ids in the
  YAML are not.
- **Fix:** compare graphs with `describe` (`describe_sha256` of the result), never with the file
  hash. Rebuild a committed `.vfx` only when its recipe changed, so its diff stays meaningful.
- **File:** `Editor/Recipe/VfxRecipeBuilder.cs` (`describe_sha256`).

### A path relative to the repository is not found

- **Symptom:** `vfxcli.py build --json '{"recipe": "Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json"}'`
  from the repository root failed with `recipe '...' is not inside the project or a package of it`.
- **Cause:** the verb runs inside the editor, whose current directory is the Sandbox project, and a
  relative path is read as project-relative (`Assets/...`, `Packages/...`).
- **Fix:** vfxcli.py resolves a relative path argument against the shell's current directory and
  passes a path inside the repository as `Packages/com.cuvara.vfx/<path in the repository>` (Agent
  interface, path arguments); the same command then exited 0 with `describe_sha256` `1224b507...`.
  Through `VfxCli.Run` directly, pass `Packages/com.cuvara.vfx/<path>` or an absolute path.
- **File:** `Tools~/vfxcli.py` (`resolve_path_args`), `Editor/Recipe/VfxProjectPaths.cs`.

### A `VisualEffect` made by a script in edit mode does not simulate

- **Symptom:** in the Sandbox editor in edit mode, a `VisualEffect` with BurstQuads created from
  `script-execute`, `Reinit()`, then `Simulate(1/60, 5)` and a camera render per call over five
  calls: `aliveParticleCount` 0, `HasAnySystemAwake()` false, `culled` true. The first attempt with
  `Camera.Render()` into an `ARGBHalf` render texture read back near black (largest r+g+b 0.016
  for a 0.05/0.05/0.08 background; cause not established); `RenderPipeline.SubmitRenderRequest`
  with a `StandardRequest` rendered the scene (a test cube showed).
- **Cause:** not established; the effect never left the culled state in edit mode.
- **Fix:** look at an effect in play mode (next entry). Render with `SubmitRenderRequest` and a
  `StandardRequest` into a render texture. Objects made for a check get `HideFlags.DontSave` in
  edit mode and are destroyed afterwards; an untitled scene cannot take an additive new scene
  (`Cannot create a new scene additively with an untitled scene unsaved`).
- **File:** none (workflow; the capture verb owns the real procedure).

### Play mode in an unfocused Sandbox editor does not advance

- **Symptom:** after `EditorApplication.EnterPlaymode()` from `script-execute`, `Time.frameCount`
  stayed 1 and `Time.time` 0 for more than 2 s; the effect stayed culled with no particle.
- **Cause:** the editor window has no focus (`Application.isFocused` false) and
  `Application.runInBackground` was false, so the player loop did not run.
- **Fix:** set `Application.runInBackground = true` in play mode: 2 s later `Time.frameCount` was
  543. `PlayerSettings.runInBackground` stayed `False` after leaving play mode, so the project is
  not changed. With it, BurstQuads showed 24 alive particles about 0.3 s after `Reinit()`, a spread
  of orange additive quads in the render, and 0 after its lifetime.
- **File:** none (workflow).

### `Random` `Uniform` on a vector attribute puts particles on a line

- **Symptom:** BurstQuads with velocity `SetAttribute` `Random` `Uniform` between `A` [-1.5, 0.5,
  -1.5] and `B` [1.5, 2.5, 1.5] rendered as one diagonal streak.
- **Cause:** `Uniform` draws one random number per particle and lerps every component with it,
  so all velocities lie on the segment from `A` to `B`. `PerComponent` draws one per component.
- **Fix:** `PerComponent` for a spread in a box; BurstQuads uses it.
- **File:** `Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json`.

### `vfxcli.py` crashed printing the catalog on a Windows console

- **Symptom:** `UnicodeEncodeError: 'charmap' codec can't encode character '\u03b5'` from
  `sys.stdout.write` after the verb had succeeded; the process exited 1.
- **Cause:** Python writes stdout in the console code page (cp1252); a library name holds `ε`.
- **Fix:** vfxcli.py writes UTF-8 bytes to `sys.stdout.buffer`; result files are UTF-8 without BOM.
- **File:** `Tools~/vfxcli.py` (`emit`).

### Inline `--input` JSON with a Windows path

- **Symptom:** `unity-mcp-cli run-tool script-execute ... --input '{"csharpCode": "... @\"C:\\Users\\..."}'`
  failed with `--input must be valid JSON ... Bad escaped character in JSON at position 125`.
- **Cause:** shell quoting and JSON escaping of backslashes inside a C# string in a JSON string;
  one level is easily lost.
- **Fix:** write the tool input with a JSON library to a file and pass `--input-file <absolute
  path>`, as vfxcli.py does.
- **File:** `Tools~/vfxcli.py` (`run_in_editor`).

### The console still shows a compile error that is fixed

- **Symptom:** after fixing `error CS0121` in a test and recompiling, `rql-query console | where
  logType == 'Error'` still listed it, while `EditorUtility.scriptCompilationFailed` was `false`
  and the test assembly was loaded.
- **Cause:** the plugin's console buffer keeps old entries (see Other observations).
- **Fix:** after a recompile, ask `script-execute` for `EditorUtility.scriptCompilationFailed`;
  count console errors only after an editor restart (`setup-sandbox.py --close`, `--open`).
- **File:** none (workflow).

### `import-texture` over a read-only target reported a failed restore

- **Symptom:** an import onto an existing read-only `Out.png` failed with two errors: `import of
  '...Out.png' failed, the target is restored: UnauthorizedAccessException: Access to the path ...
  is denied.` and `the target '...Out.png' could not be restored: UnauthorizedAccessException ...`.
  The PNG and its `.meta` were in fact unchanged (same SHA-256 before and after).
- **Cause:** the restore wrote the previous bytes back unconditionally; the write that had just
  failed failed again.
- **Fix:** the restore writes the PNG and the `.meta` back only when their bytes differ from the
  previous ones, and re-imports only then. The same case now gives one error. Verified by
  `TextureImportTests.FailedImport_LeavesAnExistingTargetAndItsMetaWithTheSameHashes("write_fails")`,
  which failed with the old restore (`Expected: property Count equal to 1 But was: 2`) and passes
  with the fix.
- **File:** `Editor/Texture/VfxTextureImport.cs` (`Restore`).

### Other observations

- Never remove a link while an editor has the project open through it: the running editor
  recreated `ProfilerCaptures/`, `UserSettings/` and `Library/mcp-server/` as a real folder at the
  old link path. Close the editor first; remove links only with `setup-sandbox.py --remove`.
- `rql-query console` reads the plugin's own log buffer: clearing Unity's console
  (`LogEntries.Clear`) does not clear it. Restart the editor for a clean count.
- After `AssetDatabase.Refresh()` from `script-execute`, a background editor did not recompile a
  changed `Assets/` script; `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` with
  `CompilationPipeline.RequestScriptCompilation()` did. Wait with `rql-query editor | select
  isCompiling` before calling the new code.
- `wait-for-ready` succeeds as soon as the MCP server answers, which can be before the import and
  compile end: also wait for `isCompiling` and `isUpdating` to be false.
- `run-tool --input-file` resolves a relative file against the current directory; use absolute
  paths in scripts that change directory.
- `VFXLibrary.GetContexts()`, `GetBlocks()` and `GetOperators()` drop experimental entries unless
  the editor preference "Experimental Operators/Blocks" is on. The bridge always drops them itself,
  so `catalog` does not depend on that preference.
- Unity 6000.6's analyzers warn `UAC0005` on `AppDomain.CurrentDomain.GetAssemblies()`; the guard
  tests find the VFX Graph editor assembly with `Type.GetType("UnityEditor.VFX.VFXGraph,
  Unity.VisualEffectGraph.Editor")` instead, so the test assembly does not reference it.
- NUnit in the Test Framework 1.8: `Is.Ordered.Using(StringComparer.Ordinal)` does not compile
  (`CS0121`, ambiguous between `Using(IComparer)` and `Using<T>(IComparer<T>)`); cast to
  `IComparer<string>`.
- Gradient key times come back quantised: a key written at `t` 0.5 is described as 0.5000076, and
  0.7 as 0.7000076 (Unity stores gradient key times in 16 bits). Two builds still describe equal.
- A `Color` parameter links to the `Vector3` input `_Color` of a `SetAttribute` `color` block
  (`p0.o -> c3.b1._Color` in BurstQuads) and the graph compiles.
- `build` fails on any error logged during its compile and import, also one from another source,
  so a busy editor can fail a clean graph; the log line is in the errors with the prefix `log:`.

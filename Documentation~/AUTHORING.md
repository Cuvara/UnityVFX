# Authoring effects with com.cuvara.vfx

The guide for an agent (or a person) who authors an effect with this package: open the agent-owned
Sandbox, learn the names with `catalog` and from existing graphs with `describe`, write a recipe,
`build` it, bring textures in with `import-texture`, check the static facts with `inspect` and
play-test it with `capture`. Every verb runs through one command, `Tools~/vfxcli.py`.

The order of work:

1. Sandbox: set it up and open it ([Sandbox](#sandbox)).
2. `catalog` before writing anything: the type, setting and slot names a recipe uses
   ([Learn the names with `catalog`](#learn-the-names-with-catalog)).
3. `describe` an existing graph to see how a system is put together
   ([Read a graph with `describe`](#read-a-graph-with-describe)).
4. Write `<Name>.vfxrecipe.json`, then `build` (or `apply` ops to a graph)
   ([Recipe: build and apply](#recipe-build-and-apply)).
5. `import-texture` for generated textures ([Texture import](#texture-import)).
6. `inspect` for budgets and lint facts ([Inspect an effect](#inspect-an-effect)).
7. `capture`, then look at the contact sheet ([Capture an effect](#capture-an-effect),
   [CAPTURE.md](CAPTURE.md)).

[DESIGN.md](DESIGN.md) is the contract the package was built against; where the built verbs,
formats, schemas or layout differ from it, the difference is listed in
[Differences from DESIGN.md](#differences-from-designmd) at the end of this file. Verified failures
and their fixes are under [Pitfalls](#pitfalls).

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
python Tools~/setup-sandbox.py --restore-nuget  # only when Assets/Plugins/NuGet is absent or incomplete:
                                               # bring the Unity-MCP NuGet DLLs back, then close the editor
                                               # (--open does the restore by itself and leaves the editor open)
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
2. `python Tools~/setup-sandbox.py --open`. The first import takes several minutes. When
   `Sandbox~/Assets/Plugins/NuGet` is absent or incomplete (the clone no longer carries the DLLs),
   `--open` brings the DLLs back by itself, see "Restoring the DLLs" below.

### The committed NuGet DLLs

`Sandbox~/Assets/Plugins/NuGet` holds the third-party .NET DLLs the Unity-MCP plugin needs (45
DLLs, 17.8 MiB = 18.6 MB, measured as 18,617,104 bytes of DLLs; with the 45 `.meta` files, the folder
`.meta` and `.nuget-installed.json` it is 91 files and 18,649,076 bytes: `McpPlugin`, `ReflectorNet`,
the ASP.NET Core SignalR client, Roslyn `Microsoft.CodeAnalysis`, `Microsoft.Extensions.*` and their
dependencies; the list with versions is `Sandbox~/Assets/Plugins/NuGet/.nuget-installed.json`). They
are the plugin's own restore output, committed as the game client commits them, because a first open
without the folder stops at about 1000 compile errors and stays there unless the editor is started
the way `--restore-nuget` does (Pitfalls, "Missing NuGet DLLs stop the first open at a dialog").
Their licence entries are in `THIRD-PARTY.md` at the repository root (the Rql DLLs and the Quaternius
characters are marked there as owner decisions before a public release). This package does not ship
the DLLs: an npm or OpenUPM tarball leaves out `Sandbox~` (`package.json` `files`), and Unity imports
no folder that ends in `~`. A Git URL install still downloads the whole repository, `Sandbox~` included.

#### Packaging

`package.json` has `"files": ["*", "!Sandbox~"]`, the mechanism `npm pack` and `npm publish` honour (the
sibling packages `UIToolkit` and `UnityDots` publish with `npm publish` in their `release.yml` and have
no `.npmignore`; a root `.npmignore` would also work but is not needed). Proof, `npm pack --dry-run
--json` in a copy of the repository made outside it (`git ls-files --cached --others --exclude-standard`
into a temporary folder; no tarball was written and none is in the repository), 2026-11-27, npm 11.6.2:

| | Entries | Packed | Unpacked | `Sandbox~/` entries |
|---|---|---|---|---|
| without `files` | 349 | 9,030,727 bytes | 22,034,218 bytes | 133 |
| with `files` | 217 | 1,596,617 bytes | 3,243,813 bytes | 0 |

Nothing else left the tarball (0 removed outside `Sandbox~/`); `.gitignore` entered it (1 added: a
whitelist includes it; harmless). Still in: `Samples~/` (19), `Documentation~/` (4), `Schemas~/` (4),
`Tools~/` (3), `Library/` (7), `Effects/` (2), `Runtime/` (28), `Editor/` (67), `Tests/` (56) and their
`.meta` files (`Library.meta`, `Effects.meta`, `Runtime.meta`, `Editor.meta`, `Tests.meta`; `~` folders
have none). A Git URL install is not affected by `files`: it clones the repository. Run a real
`npm pack` from a clean checkout or such a copy, not from a working tree whose `Sandbox~/Library` is
large (npm walks the folder before it applies the exclusion).

#### Restoring the DLLs

The DLLs can leave the repository without breaking a fresh clone, because the plugin restores them
itself (it downloads the packages of its `.nuget-installed.json` list, 43 `[NuGet] Installed` lines in
the editor log) once the editor runs past the compile-errors dialog that the missing DLLs cause.
`setup-sandbox.py` automates exactly that:

```sh
python Tools~/setup-sandbox.py --restore-nuget   # restore, wait until ready, close the editor
python Tools~/setup-sandbox.py --open            # same restore when the folder is missing, editor stays open
```

- The check: `Assets/Plugins/NuGet/.nuget-installed.json` lists every package and its DLLs. The state is
  `complete` when all of them are there, `incomplete` when some are missing, and `absent` when the
  list itself is missing (then there is nothing to compare, and the plugin restores the full set).
  `python Tools~/setup-sandbox.py` prints it under `nuget` and warns when it is not `complete`.
- The restore start is `unity-mcp-cli open --launch-dismiss-timeout-ms 400000`, which clicks *Ignore* on
  the compile-errors dialog. "Ready" then needs the DLL set `complete` and Unity-MCP answering, which
  means the editor compiled with the restored DLLs. The memory guard runs the whole time. If the
  launcher (our own `unity-mcp-cli open`, with its dismiss watch of up to 400 s) is still running 5 s
  after ready, it is ended by exact PID, never as a process tree; in the first run it kept running
  until the 400 s were over, in the later runs it ended by itself, so that stop is NOT exercised by
  hand. The result is under `nuget_after`, `restore_nuget`, `ready_after_s` and `closed`.
- The restore needs network access (the plugin downloads from nuget.org; an offline run was NOT_RUN).
  If it ends at `not ready after N s`, the editor is left running: read `Sandbox~/Logs/Editor.log`
  (`error CS` lines, `[Unity-MCP DependencyResolver]` lines), then `--close`, then use the manual
  recovery in Pitfalls.
- Meta files: the restored folder, its `.meta` files and `.nuget-installed.json` were byte-identical to
  the committed ones (below), so a restore leaves `git status` clean.

Measured on 2026-11-27 (Sandbox editor closed first; the committed `Assets/Plugins/NuGet` and its
`.meta` moved to a folder outside the repository; `Sandbox~/Library` was warm, so this is not a
first-ever import):

| Run | Start | Ready after | Peak private / working set | After ready | Restored vs committed |
|---|---|---|---|---|---|
| `--restore-nuget` | folder absent | 52.9 s (126 s with the launcher wait that is now cut) | 2.56 GB / 1.78 GB (2.78 GB / 1.97 GB while closing) | editor closed by itself 6.0 s after the request, exit code 0 | 45 equal, 0 differing, 0 missing, 0 extra by name and SHA-256; `diff -r` of the folder, `NuGet.meta` identical |
| `--open` (auto restore) | folder absent | 44.0 s (45 s wall) | 2.55 GB / 1.77 GB | `isCompiling` false, console 0 errors (RQL), `Editor.log`: 2010 `error CS` lines (1005 errors printed twice) before the restore and none after it | the same: 45 equal, 0 differing, 0 missing; `diff -r` identical |
| `--open` (folder present, normal path) | complete | 20.7 s (43 s wall with `--hold 20`) | 2.49 GB / 1.67 GB | `isCompiling` false, console 0 errors, 0 `error CS` | not applicable |

Both restored sets were identical to the committed one, so a restore on a clone without the folder
gives the committed set (the same package versions: the plugin's own list pins them). Dropping the
folder from the repository also drops it from the CI copy of the Sandbox (`ci_sandbox.py` copies
`Sandbox~` as it is): a Unity job would then need the same restore step before it compiles, which is
NOT_RUN (no CI run was made).

## Agent interface: `Tools~/vfxcli.py`

Every verb runs through one command. The C# entry points behind it are
`Cuvara.VFX.Editor.VfxCli.Run(string verb, string jsonArgs)` (returns compact JSON, never throws) and
`Cuvara.VFX.Editor.VfxCli.Batch()` (for `-executeMethod`; reads `-vfxVerb`, `-vfxArgs <file>`,
`-vfxOut <file>`; exits the editor with 0 ok, 1 `"ok": false`, 2 usage). Agents use vfxcli.py,
which picks the path, waits for verbs that span play mode and maps path arguments.

```sh
python Tools~/vfxcli.py <verb> [--project <unity project>] [--args <file.json> | --json '<object>']
                               [--out <file.json>] [--mode auto|editor|batch] [--timeout <s>]
                               [--memory-limit-gb <GB>]
python Tools~/vfxcli.py catalog                                  # --project Sandbox~ is the default
python Tools~/vfxcli.py catalog --json '{"filter": "spawn", "include_sub_variants": true}'
python Tools~/vfxcli.py describe --json '{"asset": "Assets/Effects/Spark.vfx"}' --out spark.json
python Tools~/vfxcli.py describe --args args.json --mode batch  # force a batch-mode editor
```

| Option | Default | Meaning |
|---|---|---|
| `<verb>` | required | `catalog`, `describe`, `build`, `apply`, `import-texture`, `inspect`, `capture`; must match `[a-z][a-z0-9-]*` |
| `--project` | `Sandbox~` | the Unity project; `Sandbox~`, its full path and the open path all mean the Sandbox and resolve to the open path (`sandbox_paths.resolve_project`) |
| `--args` / `--json` | `{}` | the verb's arguments: a JSON file or inline JSON (one or the other); must be an object |
| `--out` | none | also write the result to this file (the same bytes as stdout) |
| `--mode` | `auto` | `editor`: through the open editor; `batch`: a batch-mode editor; `auto`: editor when this project's editor runs, else batch |
| `--timeout` | editor 280 s (at most 280), a pending verb 900 s, batch 1800 s | seconds for the verb; for a pending verb (`capture`) through the editor, the wait for its result |
| `--memory-limit-gb` | 12 (`CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB`) | batch mode: kill the batch editor above this many GB of private bytes |

The result is printed on stdout as indented UTF-8 JSON, and with `--out` the same text is written
to that file; progress (editor PID, batch time, peak memory, path rewrites) goes to stderr, so two
runs on the same input give the same bytes. Every result is
`{"ok": bool, "verb": str, "errors": [..], "warnings": [..], ...payload}`. An argument a verb does
not read becomes a warning; an unknown verb is an error that lists `valid_verbs`.

| Exit code | Meaning |
|---|---|
| 0 | `"ok": true` |
| 1 | `"ok": false` (the errors say why), or the call into Unity failed (script-execute error, no result file, the batch editor killed or exited without a result, a pending verb that gave no result in time or whose editor ended) |
| 2 | usage: no verb, a verb name that is not `[a-z][a-z0-9-]*`, unreadable or non-object arguments |
| 3 | no editor available: the open path or the package link is missing (the message names `python Tools~/setup-sandbox.py`), an editor has the project open but Unity-MCP does not answer or `unity-mcp-cli` is not on PATH, `--mode editor` with no editor open, a batch run was asked while an editor has the project open, or Unity 6000.6.9f1 is not installed |

### Verbs

| Verb | Input | Output (payload besides the envelope) | Section |
|---|---|---|---|
| `catalog` | `filter` (case-insensitive substring of a type name, full type name, library name, title or category), `include_sub_variants` (default false) | `contexts`, `blocks`, `operators`, `parameter_types`, each with type, variant, settings (name, type, allowed values, default) and slots; `recipe_ops`, `recipe_op_fields`, `texture_steps`, `texture_step_fields`, `recipe_schema`, `counts`, `vfx_graph`, `vfx_graph_bridge`, `unity` | [Learn the names](#learn-the-names-with-catalog) |
| `describe` | `asset`: a `.vfx`, `.vfxoperator` or `.vfxblock` | the graph: contexts with their blocks, operators, parameters, other models, settings, slot values, links, flow links, systems, sticky notes, groups; schema `Schemas~/vfx-describe.schema.json` (`cuvara.vfx-describe/1`) | [Read a graph](#read-a-graph-with-describe) |
| `build` | `recipe`: a `<Name>.vfxrecipe.json` | creates or replaces `<Name>.vfx` next to it; `asset`, `systems`, `compile_errors`, `describe_sha256`, hashes | [Recipe](#recipe-build-and-apply) |
| `apply` | `asset` (a `.vfx`), `ops` (recipe ops) | the same payload as `build`, for the changed asset | [Recipe](#recipe-build-and-apply) |
| `import-texture` | `source` (PNG), `target` (`.png` asset), `transform` (steps) | the written texture, its import settings, `source_sha256`, `output_sha256` | [Texture import](#texture-import) |
| `inspect` | `effect`: a `.vfx`, or a `.prefab` with one `VisualEffect` | static lint facts; schema `Schemas~/vfx-inspect.schema.json` (`cuvara.vfx-inspect/1`) | [Inspect](#inspect-an-effect) |
| `capture` | `effect`, `seed`, `times`, `camera`, `lighting`, `out` and optional tuning | `contact-sheet.png`, one PNG per sample and `stats.json` (`Schemas~/vfx-capture.schema.json`, `cuvara.vfx-capture/1`) in `out`; the stats fields in the result | [CAPTURE.md](CAPTURE.md) |

### How vfxcli.py runs a verb

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

## Learn the names with `catalog`

Run `catalog` before writing a recipe and use its names, never names remembered from the graph
window or another VFX Graph version: an unknown name is a build error. Save it once per session
(`--out catalog.json`, about 800 KB) and search it, or narrow it with `filter`.

```sh
python Tools~/vfxcli.py catalog --out catalog.json
python Tools~/vfxcli.py catalog --json '{"filter": "burst"}'
```

- `contexts`, `blocks`, `operators`: one entry per library entry with `kind`, `type` (the short
  CLR name a recipe uses, `VFXSpawnerBurst`), `type_full`, `name` (library markup such as
  `|Set|_Lifetime|Random Uniform`), `title` (plain, "Set Lifetime Random Uniform"), `category`,
  `variant` (`id`, `settings`: the recipe's `variant`), `settings` (`name`, `type`, `kind`,
  `allowed_values`, `default`, `visible_by_default`, `read_only`), `inputs` and `outputs` (slot
  names and types). Contexts add `context_type`, `input_data`, `output_data`, `flow_inputs`,
  `flow_outputs`; blocks add `compatible_contexts` and `compatible_data`. With
  `include_sub_variants` an entry also lists its `sub_variants`, else only `sub_variant_count`.
- `parameter_types`: the types `add_parameter` accepts, by `name` (`Vector 3`) and `type` (`Vector3`).
- `recipe_ops` and `recipe_op_fields` (required and optional fields per op), `texture_steps` and
  `texture_step_fields`: the recipe and import formats as the build reads them.
- A context or block lists the settings it forwards (Initialize: `capacity`, `boundsMode` and the
  other data settings), the same list `set_setting` accepts (Pitfalls).
- Experimental library entries are always left out, whatever the editor preference
  (Pitfalls, Other observations). Two short type names, `CustomHLSL` and `PositionDepth`, are both a
  block and an operator; catalog warns about them, and `type_full` tells them apart.

## Read a graph with `describe`

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
| `id` | yes | effect id, kebab-case (`chain-lightning`); the schema enforces the pattern `^[a-z0-9][a-z0-9-]*$`, `build` only requires a non-empty string, and CI (`check_recipes.py`) rejects a committed recipe that breaks the pattern |
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
unknown block type 'VFXSpawnerBurstt'; valid names (nearest first): VFXSpawnerBurst, ...`);
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
- **Gradient colours are stored and compiled as written.** DESIGN.md names no colour space for a
  recipe gradient. The rule: the `color_keys` of a recipe are the values VFX Graph 17.6 stores in
  the graph and hands to the shader, the same values the graph window's gradient field holds. In
  the Sandbox (Linear colour space) they are linear values; `[1, 0.6, 0.3]` is not converted from
  sRGB. `describe` prints the same values (`color_space` is the `Gradient.colorSpace` Unity stores,
  `Uninitialized` for a gradient made by script). This holds because a new graph is at the current
  resource version before the first op (Pitfalls: "Gradient colours of a built graph changed at
  the first compile").
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
3. Bring the graph to the installed graph version (`VFXGraph.CurrentVersion`, 19) and resource
   version (`VisualEffectResource.CurrentVersion`, 1) with VFX Graph's `VFXGraph.SanitizeGraph`,
   before the first op. A new graph is empty, so nothing is converted. An `apply` on a graph saved
   by an older VFX Graph upgrades it here, as the graph window would, and adds a warning that
   names the old resource version; below resource version 1 VFX Graph converts the graph's
   existing gradient colour keys with `Color.linear`. The ops then write their values into a
   current graph, so the values are not converted.
4. Run the ops through `VfxGraphBridge.GraphEdit` (`VFXModel.AddChild`, `SetSettingValue`,
   `VFXSlot.value`, `VFXSlot.Link`, `VFXContext.LinkTo`), place the new nodes, give each parameter
   its graph node, and write the file with `VisualEffectResource.WriteAssetWithSubAssets`. No `.vfx`
   text is written or patched.
5. Compile (`VfxGraphBridge.CompileReport`): the in-memory compile the graph window makes
   (`VFXGraph.Compile`), the errors VFX Graph reports per node, a re-import of the copy (which
   generates the shaders) with every error logged meanwhile, and the error messages of the
   generated shaders. Any error stops here; the real asset has not been touched. When VFX Graph
   logs "The source graph was out of date and has been upgraded before compilation" during the
   compile, the result gets a `compile:` warning that quotes it; for `build` it is an error
   (`compile_errors`), because a new graph is current before the first op.
6. Copy the compiled file over `<Name>.vfx` and re-import it. Its `.meta` is not written, so its
   GUID stays the same (`guid_stable`). The temporary folder is deleted in every case.

Result payload: `recipe`, `recipe_sha256`, `recipe_id` (build only), `asset` (`path`, `guid`),
`ops_applied`, `compile_errors`, `created`, `guid_stable`, `previous_sha256`, `sha256`,
`describe_sha256` (SHA-256 of the compact JSON `VfxCli.Run("describe")` returns for the asset) and
`systems` (`id`, `name`, `data_type`, `capacity`). Compile warnings become warnings of the result.

A recipe is deterministic in its graph, not in its bytes: two builds gave the same
`describe_sha256` (`1224b507...`, twice, before the resource-version fix; the committed fixture
now describes to `ee4addf6...`) and different `.vfx` SHA-256 (see Pitfalls). Compare
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

## Inspect an effect

`inspect` reads the static facts of one effect that a lint judges: what the graph costs and how it
looks before anyone plays it. It changes nothing. The output (schema
`Schemas~/vfx-inspect.schema.json`, id `cuvara.vfx-inspect/1`) has no timestamp and a fixed order,
so two calls on an unchanged effect give byte-identical JSON.

```sh
python Tools~/vfxcli.py inspect --json '{"effect": "Tests/Fixtures/BurstQuads/BurstQuads.vfx"}'
python Tools~/vfxcli.py inspect --json '{"effect": "Effects/Spark/Spark.prefab"}' --out spark-inspect.json
```

`effect` is a `.vfx` asset or a `.prefab` with exactly one `VisualEffect` (anywhere in its
hierarchy); a prefab with none or with several is an error (inspect each `.vfx` instead). The
facts come from `describe` (the same walk, not a second one) and from
`VfxGraphBridge.InspectOutputs` for what describe does not print.

| Field | What it holds |
|---|---|
| `effect`, `asset`, `prefab` | the effect as given (`kind` `vfx` or `prefab`), the `.vfx` read, and for a prefab its path, GUID and the hierarchy path of the `VisualEffect` (`InspectFixture/Fx`) |
| `recipe` | `<Name>.vfxrecipe.json` next to `<Name>.vfx` with the SHA-256 of its bytes (the `recipe_sha256` of `build`), or null |
| `vfx_graph`, `unity`, `build_target` | versions, and the active build target the texture formats were imported for |
| `describe_sha256` | SHA-256 of the compact JSON `describe` returns for the asset: the same value as the `describe_sha256` of `build` |
| `systems` | describe id, the name VFX Graph uses (the author's title, else `System`, `System (1)`, ...), `data_type`, `capacity` (null for a spawner), member contexts and outputs |
| `outputs` | per context of type `Output`: describe id, system, context type, name, label, `blend_mode` (null for a static mesh output), `shader`, `shader_graph`, `effective`, `render_settings` |
| `textures` | each texture once (ordered by path): path, GUID, type, size, `depth`, `format` (GraphicsFormat), `texture_format`, `srgb`, `mip_maps`, `mip_count`, `compression`, `crunched`, `compressed_format`, `max_size`, `readable`, `importer`, `estimated_bytes`, `used_by` |
| `exposed_properties` | exposed input parameters in describe order: id, exposed name, type, category, `default`, `overridden`, `overrides` (the prefab's property-sheet entries: name, sheet, value) |
| `hdr_colors` | colour values with a channel above 1: `source` (`parameter`, `slot`, `prefab_override`), `path`, `name`, `type`, `rgba`, `peak`, `intensity` |
| `totals` | counts, the sum of capacities, the sum of `estimated_bytes`, `hdr_peak` |

- **`shader`** is the shader the output renders with: the one VFX Graph generated, a sub-asset of
  the `.vfx` named `Hidden/VFX/<asset>/<system>/[<letter>/]<context label or name>` (`path` and
  `guid` are the `.vfx`'s), or, for a `VFXStaticMeshOutput`, the shader it was given. Null with a
  warning when the graph has not been compiled. `shader_graph` is the `.shadergraph` of a Shader
  Graph output.
- **`effective`** applies VFX Graph's own rules: `sorted` is true for `sort` `On`, or `Auto` with
  `Alpha` or `AlphaPremultiplied` blending (BurstQuads, additive with `Auto`: false);
  `soft_particles` needs a transparent output; sorting also forces `indirect_draw`. A feature the
  output type does not have is null.
- **`render_settings`** are the output's settings the inspector or the graph shows in its current
  configuration (`GetSettings` without hidden ones), by their VFX Graph names, in declaration
  order: `sort`, `useSoftParticle`, `castShadows`, `cullMode`, `zWriteMode`, `zTestMode`,
  `useAlphaClipping`, `primitiveType`, `uvMode`, `indirectDraw`, `computeCulling`, ... A setting
  hidden by another one (`sortMode` while `sort` is `Auto`) is left out.
- **`textures`** are the texture values of parameters, of input slots without a link (a linked
  slot's own value is not used) and of the prefab's overrides. `used_by` lists the describe
  address (`c3.mainTexture`), the parameter id (`p0`) or `prefab:<override name>`.
  `estimated_bytes` is the GPU size of all mip levels from the size, the mip count and the format
  (`GraphicsFormatUtility.ComputeMipmapSize`; times 6 for a cubemap, times the slices of an array):
  128 x 128 RGBA32 with 8 mips gives 87380. A `readable` texture keeps a CPU copy as well.
- **`hdr_colors`** come from `Color` and `Gradient` parameters and slots, and from `Vector3` /
  `Vector4` parameters and slots whose name contains `color` or `colour` (`_Color` of a
  `SetAttribute` color block). A gradient key adds `.color_keys.<index>` to the path. `peak` is the
  largest of r, g, b; `intensity` is `log2(peak)`, the Intensity of Unity's HDR colour picker,
  rounded to 4 decimals. Alpha is null for a `Vector3`.
- **Prefab overrides** are read from the `VisualEffect`'s serialised property sheet
  (`m_PropertySheet.<sheet>.m_Array`, entries with `m_Overridden` on), the place the
  `VisualEffect` inspector writes them. A struct property is stored as `<name>_<field>`
  (`Initial Velocity_vector`). An override whose name matches no exposed property (left behind
  after a property was renamed) is a warning; a texture it references is still listed.
- The bridge assembly references `Unity.ShaderGraph.Editor`, because the Shader Graph of an
  output is a `ShaderGraphVfxAsset` (`VFXShaderGraphHelpers.GetShaderGraph`).

BurstQuads (`python Tools~/vfxcli.py inspect`, 2026-11-13): system `System (1)` capacity 32, one
`VFXPlanarPrimitiveOutput` with `blend_mode` `Additive`, shader
`Hidden/VFX/BurstQuads/System (1)/Output Particle Unlit Quad`, `sorted` false, the texture
`DefaultDot.tga` (128 x 128, `R8G8B8A8_SRGB`, 87380 bytes) on `c3.mainTexture`, the exposed `Tint`
`[4, 2, 0.5, 1]` and one HDR value (peak 4, intensity 2), `describe_sha256` `1224b507...`; two runs
gave the same SHA-256 (`90ae59a0...`). On a scratch copy of the VFX Graph samples (deleted
afterwards) inspect ran on 36 graphs and 3 prefabs, each twice with identical output; 98 of 98
outputs had a shader, 15 a Shader Graph, and all 40 results validated against the schema with the
Python `jsonschema` package.

## Capture an effect

`capture` plays an effect in a neutral stage made from code, with a fixed seed and time step, and
writes one PNG per sample, `contact-sheet.png` and `stats.json`. The stage is the grid ground and
wall of `Library/Grid` with the Showcase's lighting and camera code; `character` and
`target_count` add a caster and targets as a scale reference and bind a chain-lightning effect to
them. Inputs, presets, stage, characters, outputs, determinism and limits: [CAPTURE.md](CAPTURE.md).

```sh
python Tools~/vfxcli.py capture --json '{"effect": "Tests/Fixtures/BurstQuads/BurstQuads.vfx", "seed": 1234}'
# default out: Sandbox~/Captures/BurstQuads/seed-1234-gameplay-dusk (gitignored)
```

The verb runs across play mode: through the editor vfxcli.py waits for the job's `result_file`
(about 15 s for BurstQuads), through a batch-mode editor for the editor to exit (about 30 s). The
editor must be in edit mode, compiled, and run no other capture. Afterwards it is in edit mode
again with the scenes it had. Look at the contact sheet yourself before reporting an effect.

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
  A second test on 2026-11-13 (the folder moved outside the repository, then
  `setup-sandbox.py --open`): the editor never became ready in 600 s, stayed flat at 1.24 GB private,
  `Sandbox~/Logs/Editor.log` had 1005 `error CS` lines (`CS0234 ... 'ReflectorNet'`, `CS0246
  'IMcpPlugin'`, `'HubConnectionState'`, `'ReactiveProperty<>'`), the folder was not recreated, and
  `--close` could not save (Unity-MCP never answered) and killed that PID after its 300 s timeout.
- **Cause:** the plugin restores its NuGet DLLs only once the editor runs past that dialog, and a
  plain `unity-mcp-cli open` (30 s dismiss watch) does not wait for it.
- **Fix:** keep `Assets/Plugins/NuGet` committed (45 DLLs, 17.8 MiB; commit `f0ede9a`; see Sandbox,
  "The committed NuGet DLLs"). Recovery without it: `python Tools~/setup-sandbox.py --restore-nuget`
  (and `--open`, which does the same when it finds the folder absent or incomplete against
  `.nuget-installed.json`) starts the editor with `--launch-dismiss-timeout-ms 400000`, waits for the
  complete DLL set and a ready editor, and, for `--restore-nuget`, closes it. Tested by hand on
  2026-11-27: ready after 52.9 s and 44.0 s, 45 of 45 DLLs equal to the committed ones by SHA-256
  (see Sandbox, "Restoring the DLLs"). By hand: `unity-mcp-cli open --launch-dismiss-timeout-ms 400000
  "$OPEN"` clicks *Ignore*, and the plugin restores the DLLs. If the restore ends at `not ready`,
  read `Sandbox~/Logs/Editor.log`, `--close`, and restore from the repository history
  (`git checkout <commit> -- Sandbox~/Assets/Plugins/NuGet`).
- **File:** `Sandbox~/Assets/Plugins/NuGet/`, `Sandbox~/Assets/Plugins/NuGet/.nuget-installed.json`.

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
- **File:** `Editor/Capture/VfxCaptureSession.cs` and `VfxCaptureStage.cs`: `capture` plays the effect in
  play mode and reads the camera's render target (Documentation~/CAPTURE.md).

### Play mode in an unfocused Sandbox editor does not advance

- **Symptom:** after `EditorApplication.EnterPlaymode()` from `script-execute`, `Time.frameCount`
  stayed 1 and `Time.time` 0 for more than 2 s; the effect stayed culled with no particle.
- **Cause:** the editor window has no focus (`Application.isFocused` false) and
  `Application.runInBackground` was false, so the player loop did not run.
- **Fix:** set `Application.runInBackground = true` in play mode: 2 s later `Time.frameCount` was
  543. `PlayerSettings.runInBackground` stayed `False` after leaving play mode, so the project is
  not changed. With it, BurstQuads showed 24 alive particles about 0.3 s after `Reinit()`, a spread
  of orange additive quads in the render, and 0 after its lifetime.
- **File:** `Editor/Capture/VfxCaptureSession.cs` (`Begin` sets it for the capture and restores it).

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
- **Cause:** `rql-query console` reads the plugin's own log buffer, which keeps old entries;
  clearing Unity's console (`LogEntries.Clear`) does not clear it.
- **Fix:** after a recompile, ask `script-execute` for `EditorUtility.scriptCompilationFailed`;
  count console errors only after an editor restart (`setup-sandbox.py --close`, `--open`). Expected
  errors that tests log on purpose (`LogAssert.Expect`, such as the `unknown effect id` errors of the
  `VfxPlayer` tests) and import errors of a deleted scratch folder also stay in the buffer until
  the restart.
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

### Generated shaders of a system with several outputs were not found

- **Symptom:** the first `inspect` over the VFX Graph sample graphs found no shader for 48 of 98
  outputs, with warnings such as `c5: no generated shader named
  'Hidden/VFX/FlipbookMode/Flibook Flame/Output Particle Unlit Quad'`, while the asset holds
  `Hidden/VFX/FlipbookMode/Flibook Flame/B/Output Particle Unlit Quad`. Single-output systems
  (BurstQuads) matched.
- **Cause:** VFX Graph 17.6 names a generated shader `Hidden/VFX/<asset>/<system>/<letter>/<context>`
  when a system has several contexts of one type (`VFXGraphCompiledData.SaveShaderFiles`). The
  letter (`VFXContext.letter`) is not serialised: `VFXGraph.PrepareGraph` assigns it with
  `SyncContextLetters()` before a compile, so a graph loaded from disk has none.
- **Fix:** `InspectOutputs` calls `graph.systemNames.Sync(graph)` and `graph.SyncContextLetters()`
  (both refresh in-memory state only) before it builds the names. The same run then matched 97 of
  98 outputs; the last one is the next entry.
- **File:** `Editor/Bridge/VfxGraphBridge.Inspect.cs` (`InspectOutputs`).

### A static mesh output's shader is not in its settings

- **Symptom:** for a `VFXStaticMeshOutput` (sample `CollisionAdvanced`, context `c3`), `describe`
  prints the setting `shader` as null and no generated shader matches the output's name.
- **Cause:** the output renders with a shader the author picks; its `shader` setting field is not
  serialised (`// not serialized here but in VFXDataMesh`) and the value lives on the system's
  `VFXDataMesh.shader`. VFX Graph generates no shader for that output.
- **Fix:** `InspectOutputs` reports `VFXDataMesh.shader` (here `Hidden/SG_Hand` from
  `SG_Hand.shadergraph`) with `expected_shader` null; `inspect` leaves `shader` out of
  `render_settings`. Then 98 of 98 sample outputs had a shader.
- **File:** `Editor/Bridge/VfxGraphBridge.Inspect.cs` (`InspectOutputs`), `Editor/Inspect/VfxInspect.cs`.

### `aliveParticleCount` lags by up to 60 frames

- **Symptom:** in play mode with `Time.captureDeltaTime` 1/60, BurstQuads (24 particles, lifetimes
  0.6 to 0.9 s) reported 24 from frame 5 to frame 64 and then 0, with no value in between, although
  particles die from 0.6 s. Held still (`playRate` 0) at its 40th simulated frame, the effect still
  reported 24 for 19 frames, then 19 until the log stopped at frame 400. A capture that read the count
  "when two frames agree" recorded stale counts.
- **Cause:** VFX Graph reads the alive count back from the GPU only every 60 frames (measured,
  not documented): the refresh frames stayed 5 and 65 with `captureDeltaTime` 1/30 instead of
  1/60, and with `Application.targetFrameRate` 30 instead of about 190 frames per second (0.3 s and
  2.1 s of real time), so the period counts frames, not game time or real time.
  `VFXParticleSystemInfo.aliveCount` and `HasAnySystemAwake()` come from the same readback;
  `VisualEffect` and `VFXManager` have no public or internal member to force it.
- **Fix:** `capture` holds the effect still (`playRate` 0) for `settle_frames` (default 70, at
  least 62) at every sample and probe, then reads the count: after a full period the readback is
  of the held state. A `Single` burst still fires at `playRate` 0 (24 alive at t = 0). Two
  captures with one seed then gave the same counts at every sample.
- **File:** `Editor/Capture/VfxCaptureSession.cs` (`StepCore`), `Editor/Capture/VfxCaptureRequest.cs`
  (`DefaultSettleFrames`, `MinSettleFrames`).

### The first capture stage was too dark to judge an effect

- **Symptom:** with the first dusk preset (key light 1.1 at 12 degrees, no post exposure) the ground
  of the BurstQuads frames read 0.002 linear after the effect had ended (mean luminance 0.002 to
  0.011): only the particles were visible. A key light of 2.0 at 50 degrees (day) gave a ground of
  0.073 linear, 10.0 gave 0.45.
- **Cause:** ACES tonemapping lowers the dark and middle range; a low key light on a horizontal
  ground lights it at a grazing angle.
- **Fix:** brighter presets (dusk key 1.6 at 20 degrees, brighter ambient) and post exposure
  (+1 EV day and dusk, +0.75 EV night). Mean luminance of the 0.3 s frame: day 0.188, dusk 0.031,
  night 0.017 (measured again after the gradient fix below; before it: 0.186, 0.029, 0.015); the
  ground and backdrop read in all three.
- **File:** `Runtime/Presets/VfxLightingPreset.cs`.

### Gradient colours of a built graph changed at the first compile

- **Symptom:** the BurstQuads fixture was saved with `m_ResourceVersion: 0`. Before
  `VFXGraph.PrepareGraph`, `describe` gave the colour-over-life keys `[1, 0.6, 0.3]` and
  `[0.6, 0.1, 0.05]`. After a compile, or after the graph window opened the graph, it gave
  `[1, 0.3185, 0.0732]` and `[0.3185, 0.0100, 0.0039]`. The console showed "The source graph was
  out of date and has been upgraded before compilation" at every compile and import (82 warnings
  in one QA session). The rendered colours were darker than the recipe's, `describe` of one asset
  depended on what had run before it, and a save from the graph window rewrote the stored gradient.
- **Cause:** VFX Graph's own `VisualEffectAssetEditorUtility.CreateNewAsset` (and `CreateNew<T>`
  for subgraphs) writes a graph with no `m_ResourceVersion`, so it is 0, while
  `VisualEffectResource.CurrentVersion` is 1 in VFX Graph 17.6.0. `VFXGraph.SanitizeGraph`
  (`Editor/Models/VFXGraph.cs`, lines 943 to 971 of the installed source), which `PrepareGraph`
  runs at every compile, treats a graph below resource version 1 as "before gradient interpreted
  as linear". It converts every `VFXSlotGradient` colour key with `Color.linear`, then sets the
  current versions in memory only. The builder had written the recipe's gradient into that
  version-0 graph and saved it unchanged, so every compile converted the authored values again.
- **Fix:** `GraphEdit.Create`, `CreateSubgraph` and `Open` call `VFXGraph.SanitizeGraph` before
  the first op when the graph's graph or resource version is below the installed one, then check
  that both are current. A new graph is empty, so nothing is converted, and the saved asset has
  `m_ResourceVersion: 1` and `m_GraphVersion: 19`. `apply` on an older asset upgrades it before the
  ops and warns. `CompileReport` returns `upgraded` and a warning when VFX Graph logs the upgrade;
  `build` treats it as an error. `describe` prints `resource_version` and warns on an out-of-date
  graph. The fixture was rebuilt; the 0.3 s luminance values of the capture presets went up
  (CAPTURE.md), and the alive counts did not change. Tests: `RecipeBuildTests`
  `Build_SavesTheCurrentGraphAndResourceVersions` and
  `Build_DescribeIsTheSameBeforeAndAfterPrepareGraph_AndAfterAReimport_WithTheAuthoredGradient`,
  `RecipeCoverageTests.Bridge_NewSubgraphs_AreSavedAtTheCurrentGraphAndResourceVersions`. With the
  version step disabled, they fail with `color_keys[1].color[1] Expected: 0.6 +/- 0.0001 But was:
  0.318546832`, with the upgrade warning in the build result, and with `Twice.vfxoperator resource
  version Expected: 1 But was: 0`.
- **File:** `Editor/Bridge/VfxGraphBridge.Edit.cs` (`BringToCurrentVersions`, `CompileReport`),
  `Editor/Recipe/VfxRecipeBuilder.cs` (`Run`), `Editor/Bridge/VfxGraphBridge.cs` (`Describe`).

### Material colour properties are sRGB values in the linear Sandbox

- **Symptom:** `VfxCaptureStage` documents its ground as "linear albedo" 0.30 and its backdrop as
  0.36, set with `Material.SetColor("_BaseColor", ...)`. A URP Unlit material with `_BaseColor`
  0.5, rendered in play mode into an `ARGBHalf` linear render texture without post-processing, read
  back 0.2140 (2026-11-27, Sandbox, `QualitySettings.activeColorSpace` Linear).
- **Cause:** in a project in linear colour space Unity treats a material colour property as an sRGB
  value and converts it to linear before the shader reads it (0.5 to 0.214). The capture stage's
  ground therefore has a linear albedo of about 0.073 and its backdrop about 0.106, not 0.30 and
  0.36.
- **Fix:** none needed for the look (the presets were tuned on what renders), but read every
  material colour as sRGB. The grid materials keep the first capture stage's property values (ground
  0.30, backdrop 0.36). Since 2026-11-27 the capture stage uses these materials itself;
  `VfxCaptureStage.GroundAlbedo` and `BackdropAlbedo`, which documented the values as linear, are
  removed.
- **File:** `Library/Grid/VfxGridGround.mat`, `Library/Grid/VfxGridBackdrop.mat`,
  `Editor/Capture/VfxCaptureStage.cs` (`GroundMaterialPath`, `BackdropMaterialPath`).

### A brown band at the top of every contact sheet cell

- **Symptom:** every cell of a capture contact sheet (BurstQuads, `gameplay`, `dusk`) had a flat
  brown band across the top, 28 of 360 pixels high, over a dark ground with no visible surface; a
  reviewer could take it for a rendering defect, and the ground gave no scale
  (`Sandbox~/Captures/pkg-showcase/capture-stage/before/gameplay-dusk`, gitignored).
- **Cause:** stage geometry and light. The `gameplay` camera (45 degrees down, 10 m, field of view
  40 degrees) sees down to 25 degrees below the horizon at the top of its frame; the backdrop stood
  8 m behind the origin, so the frame showed only its lowest 1.04 m, 8 % of the frame height (28
  pixels at 360). The backdrop and the ground were plain URP Lit grey with no texture, so the strip
  had no edge or line to read as a wall. The dusk key light (orange, 20 degrees up, heading -60)
  lights a wall facing the camera at a cosine of about 0.47 and the ground at 0.34, so the strip was
  brighter and more orange than the ground: a band.
- **Fix:** the stage uses the grid materials of `Library/Grid` for the ground and the backdrop, so the
  1 m and 5 m lines run across the ground and continue up the wall, and the backdrop stands 6 m
  behind the origin: the `gameplay` frame shows the wall's foot over its top sixth with the corner
  line and the grid on it, and reads as a floor with a wall behind it. The ground is 80 m and the wall
  80 by 20 m, so no camera preset sees past an edge. No camera preset value changed. Checked on the
  contact sheets of BurstQuads in day, dusk and night (`gameplay`) and dusk (`close`, `side`).
- **File:** `Editor/Capture/VfxCaptureStage.cs` (`BackdropDistance`, `GroundSize`, the materials).

### The night backdrop of the `side` camera was black

- **Symptom:** with the `night` preset and the `side` camera the wall behind the effect rendered near
  black (grid stage, empty stage at 1.00 s: mean luminance 0.0013, the wall's grid not visible), in
  the capture stage and in the shared-stage evidence (`night-side.png`).
- **Cause:** the backdrop faces the camera: -Z for `gameplay` and `close`, +X for `side`. A key light
  lights it only when it travels towards +Z (front of the -Z wall) and -X (front of the +X wall). The
  day (heading -30) and dusk (heading -60) keys do both; the night key had heading 30, travelled
  towards +X, and lit the `side` wall from behind, which left the ambient (0.03 linear) and the fill
  (0.10).
- **Fix:** the night key's heading is -45 degrees (`keyAngles` (40, -45)); it now lights the front of
  the wall in every camera preset, and the night `side` frame shows the wall's grid, dim (mean
  luminance of the empty stage 0.0029). The Showcase uses the same preset.
- **File:** `Runtime/Presets/VfxLightingPreset.cs`.

### Dark grid lines vanished at night

- **Symptom:** the first grid materials (lines 0.20 on a 0.30 ground, minor strength 0.6, major
  1.0) read well in day, but in the night gameplay frame the 1 m lines were invisible on the ground
  and the 5 m lines barely showed; in dusk the 1 m lines were faint
  (`Sandbox~/Captures/pkg-showcase/shared-stage/tuning/A-*.png`, gitignored).
- **Cause:** the line contrast is a contrast of albedo, so it scales with the light; with the night
  preset the ground is about 0.005 mean luminance after ACES, where a 20 % darker line is below
  what reads.
- **Fix:** stronger, darker lines: ground lines 0.10, backdrop lines 0.14, minor strength 0.75,
  major strength 0.8 (the shader's defaults too). Light lines (0.45 ground, 0.52 backdrop) read at
  night as well but were bright enough in day to compete with an effect (`tuning/B-*`); darker lines
  (`tuning/C-*`, then the final frames) read in all three presets and stay behind an additive
  effect. No preset value was changed.
- **File:** `Library/Grid/CuvaraVfxGrid.shader`, `Library/Grid/VfxGridGround.mat`,
  `Library/Grid/VfxGridBackdrop.mat`.

### A component's cleanup in `OnDestroy` is not done in the same frame in play mode

- **Symptom:** `VfxRigTests.LightingRig_LeavesTheVolumesOwnProfileUnchanged_AndDestroysItsProfile`
  failed with `the rig's profile is destroyed with the rig  Expected: True  But was: False` after
  `Object.DestroyImmediate(rig.gameObject)`.
- **Cause:** `VfxLightingRig.OnDestroy` destroys its run-time profile with `Object.Destroy` in play
  mode, which takes effect at the end of the frame.
- **Fix:** the test destroys the rig and waits one frame (`[UnityTest]`, `yield return null`) before
  it checks; the same holds for `VfxMannequin` and its material.
- **File:** `Tests/Runtime/VfxRigTests.cs`, `Tests/Runtime/VfxMannequinTests.cs`.

### Other observations

- In the Sandbox, with the night preset (key heading 30 degrees), a wall that faces +X is lit from
  behind and renders near black, so a backdrop turned towards the `side` camera (yaw -90) shows
  almost nothing at night; the ground grid still reads. Seen in
  `Sandbox~/Captures/pkg-showcase/shared-stage/night-side.png`.
- Never remove a link while an editor has the project open through it: the running editor
  recreated `ProfilerCaptures/`, `UserSettings/` and `Library/mcp-server/` as a real folder at the
  old link path. Close the editor first; remove links only with `setup-sandbox.py --remove`.
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
- Entering play mode reloads the scripting domain in the Sandbox (`EditorSettings.enterPlayModeOptionsEnabled`
  is false), so a verb cannot wait for play mode inside one `script-execute` call; `capture` stores
  its job in `SessionState` and finishes after the reload (CAPTURE.md, "How it runs"). In an EditMode
  test, `yield return new EnterPlayMode()` resumes the enumerator after the reload with its locals
  reset (the Test Framework restores only the enumerator's state number), so a test creates
  everything it needs after that yield (`CaptureTests`).
- In the Sandbox's player loop, `VFXUpdate` runs in `PostLateUpdate` before `FinishFrameRendering`; a
  step first in `EarlyUpdate` sees the previous frame simulated and rendered, and a `playRate` set
  there applies to that frame's update.
- NUnit in the Test Framework 1.8: `Is.Ordered.Using(StringComparer.Ordinal)` does not compile
  (`CS0121`, ambiguous between `Using(IComparer)` and `Using<T>(IComparer<T>)`); cast to
  `IComparer<string>`.
- Gradient key times come back quantised: a key written at `t` 0.5 is described as 0.5000076, and
  0.7 as 0.7000076 (Unity stores gradient key times in 16 bits). Two builds still describe equal.
- A `Color` parameter links to the `Vector3` input `_Color` of a `SetAttribute` `color` block
  (`p0.o -> c3.b1._Color` in BurstQuads) and the graph compiles.
- `build` fails on any error logged during its compile and import, also one from another source,
  so a busy editor can fail a clean graph; the log line is in the errors with the prefix `log:`.
- The prefabs of `Samples~/VFXLearningTemplates/Prefabs/VFXG` nest prefabs of other sample sets
  (`TurntableWithBackdrop_VFX` is a variant of a prefab that is not in the package). Copied alone
  into a scratch folder, 32 of them import with `Missing Nested Prefab Asset` errors and hold no
  `VisualEffect`; `inspect` reports `has no VisualEffect component`, which is right. Their import
  errors stay in the console buffer after the scratch folder is deleted: restart the editor.
- `describe_sha256` (in the results of `build`, `apply` and `inspect`) is the SHA-256 of the compact
  JSON `VfxCli.Run("describe")` returns. vfxcli.py prints and writes indented JSON, so the SHA-256 of
  a `vfxcli.py describe --out` file is a different number: compare `describe_sha256` values, not
  file hashes (`Editor/Recipe/VfxRecipeBuilder.cs`, `Tools~/vfxcli.py` `emit`).

## Differences from DESIGN.md

Every place where the built verbs, formats, schemas or layout differ from
[DESIGN.md](DESIGN.md), checked against the code on 2026-11-13 and updated on 2026-11-27 (three times). These
differences are not written into DESIGN.md; the run lead decides whether the contract adopts
them. Additions that keep every DESIGN field and behaviour are listed too, because rpg-factory
scripts read these formats.

### Layout

1. `Library/` holds only `Grid/`: the grid shader `Cuvara/VFX/Grid` (`CuvaraVfxGrid.shader`) and
   its two materials, `VfxGridGround.mat` and `VfxGridBackdrop.mat`. DESIGN lists shared subgraphs,
   gradients and textures there; none exists yet. `Effects/` holds only `README.md` (the layout of
   one effect): no effect exists yet, and each folder holds a file because git tracks no empty
   folder and `check_metas.py` fails on a folder `.meta` without a Unity-visible file.
   `Samples~/Showcase/` and `Documentation~/SHOWCASE.md` are built by the rest of the Showcase task
   (pkg-showcase). Proof: the repository tree (`git ls-files`).
2. More folders than DESIGN lists: `Editor/` is split into `Cli/`, `Verbs/`, `Recipe/`, `Texture/`,
   `Inspect/`, `Capture/` (the bridge stays alone in `Editor/Bridge/`); `Runtime/Presets/` holds the
   capture and Showcase presets; `Runtime/Showcase/` holds the pieces the Showcase scene and the
   capture stage share (`VfxStation`, `VfxLightingRig`, `VfxCameraRig`, `VfxMannequin`); `Tests/Fixtures/BurstQuads/` holds the fixture recipe and the
   `.vfx` it builds. Proof: those folders.
3. `Schemas~/` holds a fourth schema, `vfx-inspect.schema.json` (`cuvara.vfx-inspect/1`), besides
   the recipe, capture and describe schemas. Proof: `Schemas~/vfx-inspect.schema.json`.
4. `Sandbox~/Assets/Plugins/NuGet/` (the Unity-MCP plugin's 45 DLLs, 17.8 MiB) is committed and can be
   brought back by `setup-sandbox.py --restore-nuget`; the tarball leaves out `Sandbox~` (`package.json`
   `files`); DESIGN's Sandbox section mentions none of this. Proof:
   `Sandbox~/Assets/Plugins/NuGet/.nuget-installed.json`, `Tools~/setup-sandbox.py`, `package.json`.

### Runtime and assemblies

5. `Cuvara.VFX` and `Cuvara.VFX.Editor` also reference `Unity.RenderPipelines.Core.Runtime` and
   `Unity.RenderPipelines.Universal.Runtime`, for the lighting presets that set URP Volume
   components (DESIGN's "no dependency on other Cuvara packages" still holds). `Cuvara.VFX` and
   `Cuvara.VFX.Tests` also reference `Unity.InputSystem` by name, used only under the version define
   `CUVARA_VFX_INPUT_SYSTEM` (package `com.unity.inputsystem` present) together with
   `ENABLE_INPUT_SYSTEM`, for the key trigger of `VfxStation`; `package.json` gets no dependency. Proof:
   `Runtime/Cuvara.VFX.asmdef`, `Editor/Cuvara.VFX.Editor.asmdef`.
6. The runtime assembly has more public types than the four DESIGN names: `VfxHandle` (returned by
   `VfxPlayer.Play`, stops one instance), `VfxLightingPreset` and `VfxCameraPreset` (the presets
   DESIGN shares between the Showcase and the capture stage, placed in the runtime so the Showcase
   overlay can use them), and the shared Showcase pieces `VfxStation` (with `VfxStationTrigger`),
   `VfxLightingRig`, `VfxCameraRig` and `VfxMannequin`. `VfxPlayer.Play` also has a `Transform` overload. Proof:
   `Runtime/VfxHandle.cs`, `Runtime/Presets/`, `Runtime/Showcase/`, `Runtime/VfxPlayer.cs`.
7. `ChainLightningBinder` takes up to 8 targets (`MaxTargets`), not N, and writes the properties
   `SourcePosition`, `TargetPosition0` to `TargetPosition7` and `HopCount` (names configurable).
   Proof: `Runtime/ChainLightningBinder.cs`.
8. The bridge assembly `Unity.Testing.VisualEffectGraph.Editor` also references
   `Unity.ShaderGraph.Editor` (the Shader Graph of an output is a `ShaderGraphVfxAsset`); it still
   holds only `VfxGraphBridge` (three partial files). Proof:
   `Editor/Bridge/Unity.Testing.VisualEffectGraph.Editor.asmdef`.

### Agent interface and vfxcli.py

9. vfxcli.py has more options than DESIGN's `--project`, `--args`, `--out`: `--json` (inline
   arguments, exclusive with `--args`), `--mode auto|editor|batch`, `--timeout` and
   `--memory-limit-gb`. Proof: `Tools~/vfxcli.py` `parse_args`.
10. Exit code 1 also covers a failed call into Unity (script-execute error, no result file, a killed
    batch editor, a pending verb without a result); exit code 3 also covers "Unity-MCP does not
    answer", "`unity-mcp-cli` not on PATH", `--mode editor` without an editor, `--mode batch`
    while an editor has the project open, and "Unity not installed". Proof: `Tools~/vfxcli.py`
    (the `CliError` calls).
11. vfxcli.py rewrites path arguments (`recipe`, `asset`, `effect`, `source`, `target`, `out`,
    `file`, `texture`) written relative to the current directory into project paths
    (`Packages/com.cuvara.vfx/...`, `Assets/...`) or absolute paths; `VfxCli.Run` does not. Proof:
    `Tools~/vfxcli.py` `resolve_path_args`.
12. vfxcli.py prints indented JSON; `VfxCli.Run` returns compact JSON. Runner details go to stderr.
    Proof: `Tools~/vfxcli.py` `emit`, `Editor/Cli/VfxCli.cs`.
13. `VfxCli.Batch` exits the editor with 0, 1 or 2 (usage), and does not exit for a pending verb
    (the capture job exits it). Proof: `Editor/Cli/VfxCli.cs` (`ExitUsage`, `ExitPending`).
14. Envelope additions: an argument a verb does not read is a warning; an unknown verb also returns
    `valid_verbs`. Proof: `Editor/Cli/VfxCli.cs` `Execute`.

### catalog

15. Extra input `include_sub_variants` (default false). Proof: `Editor/Verbs/CatalogVerb.cs`.
16. Payload superset: per entry `kind`, `type` (short CLR name, the recipe name), `type_full`,
    `name` (library markup), `title`, `category`, `variant` (`id`, `settings`), settings with
    `kind`, `default`, `visible_by_default`, `read_only`; context and block extras
    (`context_type`, `input_data`, `output_data`, `flow_inputs`, `flow_outputs`,
    `compatible_contexts`, `compatible_data`, `sub_variant_count`); top-level `vfx_graph_bridge`,
    `unity`, `recipe_schema`, `recipe_ops`, `recipe_op_fields`, `texture_steps`,
    `texture_step_fields`, `filter`, `counts`. Experimental entries are always left out. Settings
    include those a model forwards (Initialize data settings). Proof: `Editor/Verbs/CatalogVerb.cs`,
    `Editor/Bridge/VfxGraphBridge.cs` (`Catalog`), `Editor/Bridge/VfxGraphBridge.Edit.cs`
    (`UserSettings`).
17. Key change: DESIGN says each entry carries "slots (name, type)". An entry has no `slots` key.
    It has `inputs` and `outputs`, and each slot in them has `name`, `type`, `ref` (the address a
    recipe uses), `type_full`, `direction` (`in` or `out`), `default` (inputs only) and `space`
    (spaceable slots only). Proof: `Editor/Bridge/VfxGraphBridge.cs` (`CatalogSlots`).

### describe

18. Payload superset: `schema` `cuvara.vfx-describe/1`, `asset`, `asset_kind`, `graph_version`,
    `resource_version` (with a warning when the graph is older than the installed VFX Graph),
    `other_models`, `sticky_notes`, `groups`; systems with `data_type`, `capacity` (null for a
    spawner) and their contexts; a parameter's `tooltip` only when set. Blocks are listed inside
    their context. Proof: `Schemas~/vfx-describe.schema.json`, `Editor/Bridge/VfxGraphBridge.cs`
    (`Describe`).
19. Ids are assigned by describe (`c0`, `c0.b0`, `o0`, `p0`, `x0`, `s0`); unnamed slots (math
    operator outputs) are addressed `<id>.#<index>`; links are listed once, from the input side,
    sorted. Proof: `Editor/Bridge/VfxGraphBridge.cs` (`AssignIds`, slot paths).
20. `describe` also takes `.vfxoperator` and `.vfxblock` subgraph assets and an absolute path inside
    a local package. Proof: `Editor/Bridge/VfxGraphBridge.cs`, `Editor/Verbs/DescribeVerb.cs`.

### Recipe, build and apply

21. Op fields are fixed by the implementation (DESIGN named only the ops): `set_setting` `node`,
    `name`, `value`; `set_slot` `slot`, `value`, `space`; `link_flow` `from`, `to`, `from_slot`,
    `to_slot`; `use_subgraph` `id`, `asset`, `context`, `slots` (no `settings`: the only setting
    of a subgraph node in 17.6 is its asset); `set_output` `node`, `texture`, `blend_mode`,
    `color_mapping`, `gradient`; `comment` `text`, `title`; `add_parameter` also `category`,
    `tooltip`; `add_context` also `label`. Proof: `Editor/Recipe/VfxRecipeOps.cs`,
    `Schemas~/vfx-recipe.schema.json`.
22. New optional field `variant` on `add_context`, `add_block`, `add_operator` (a catalog variant
    id). Proof: `Editor/Recipe/VfxRecipeOps.cs`.
23. Slot addresses: a slot without a name is `<id>.#<index>` (DESIGN: never by index); `<id>.out`
    is an alias for the only output of a node with exactly one (a parameter's real output slot is
    `o`), which makes DESIGN's `p_color.out` work. Proof: `Editor/Bridge/VfxGraphBridge.Edit.cs`
    (slot lookup).
24. `add_parameter.type` takes the catalog parameter `name` (`Color`, `Vector 3`) or `type`
    (`Vector3`). Proof: `Editor/Bridge/VfxGraphBridge.Edit.cs`.
25. Asset values: a project-relative path (as DESIGN says), or `{"guid": ...}` or `{"path": ...}`
    (both together must name the same asset). Proof: `Editor/Bridge/VfxGraphBridge.Edit.cs`
    (`LoadAsset`), `Schemas~/vfx-recipe.schema.json` (`assetRef`).
26. The schema requires a kebab-case `id`; `build` only requires a non-empty string. Unknown
    top-level and op fields are errors (DESIGN does not say). Proof:
    `Schemas~/vfx-recipe.schema.json`, `Editor/Recipe/VfxRecipeBuilder.cs`.
27. `apply` addresses existing nodes by the ids `describe` prints. Proof:
    `Editor/Recipe/VfxRecipeBuilder.cs`, `Editor/Bridge/VfxGraphBridge.cs` (`AssignIds`).
28. Payload additions of `build` and `apply`: `recipe_sha256`, `recipe_id` (build), `ops_applied`,
    `created`, `guid_stable`, `previous_sha256`, `sha256`, `describe_sha256`, and `op_error` on a
    failure. Proof: `Editor/Recipe/VfxRecipeBuilder.cs`.
29. Determinism holds at the `describe` level, not in the `.vfx` bytes (Unity gives sub-assets new
    local file ids on each write); DESIGN's "describe to the same JSON" holds. Proof: Pitfalls, "Two
    builds of the same recipe give different `.vfx` bytes".
30. DESIGN names no colour space for a recipe gradient. Rule: the colour keys are stored and
    compiled as written. A new graph is brought to the current graph and resource versions before
    the first op, and `apply` upgrades an older asset before its ops and warns. A VFX Graph upgrade
    logged during the compile is a warning in the result, and an error for `build`. The compile
    report adds `upgraded`. Proof: `Editor/Bridge/VfxGraphBridge.Edit.cs`
    (`BringToCurrentVersions`, `CompileReport`), Pitfalls, "Gradient colours of a built graph
    changed at the first compile".

### import-texture

31. Step parameters are fixed by the implementation: `luminance_to_alpha` `color` (`keep`, `white`,
    `unpremultiply`); `pack_channels` `r`, `g`, `b`, `a` (a constant or `{"file", "channel"}`);
    `resize_pot` `max_size`; `flipbook` `columns`, `rows`; `wrap` `u`, `v` or `mode`. A step is a
    name or an object `{"step": name, ...}`. Proof: `Editor/Texture/VfxTextureSteps.cs`.
32. `flipbook` checks and records the frame grid; it does not move pixels. Proof:
    `Editor/Texture/VfxTextureSteps.cs`.
33. Payload beyond the two hashes: `source_size`, `steps`, `inputs` (each `pack_channels` file with
    its SHA-256), `size`, `flipbook`, `created`, `previous_sha256`, `guid`, `guid_stable`,
    `import_settings`, and `step_error` on a failure. Proof: `Editor/Texture/VfxTextureImport.cs`.

### inspect

34. Output fields the DESIGN row does not list: `effect`, `asset`, `prefab`, `recipe`,
    `build_target`, `describe_sha256`, per output `effective` and `render_settings`, per texture
    `estimated_bytes`, `readable` and `used_by`, `exposed_properties[].overrides`,
    `hdr_colors[].intensity`, `totals`. Proof: `Schemas~/vfx-inspect.schema.json`.
35. A prefab must hold exactly one `VisualEffect`; with several it is an error (inspect each `.vfx`).
    Proof: `Editor/Inspect/VfxInspect.cs`.

### capture

36. `VfxCli.Run("capture")` returns a pending result (`pending`, `job`, `out`, `result_file`); the
    final result is in `result_file`. vfxcli.py waits for it. Proof: `Editor/Capture/VfxCaptureJob.cs`.
37. `effect` in `stats.json` is the project path (`Packages/com.cuvara.vfx/...`), not the path in the
    repository. Proof: `Editor/Capture/VfxCaptureRequest.cs` (`Parse`).
38. `stats.json` has three more fields, `resolution`, `probe_interval`, `settle_frames`; the final
    verb result adds `out`, `stats_file`, `probes`, `pending`, `job`. Proof:
    `Schemas~/vfx-capture.schema.json`, `Editor/Capture/VfxCaptureSession.cs`.
39. `mean_luminance` and `max_luminance` are measured after ACES tonemapping, so `max_luminance`
    stays near or below 1 (DESIGN's example shows 7.8). Proof: `Editor/Capture/VfxCaptureSession.cs`.
40. More optional inputs: `delta_time`, `width`, `height`, `columns`, `probe_interval`,
    `settle_frames`, `max_seconds`, `timeout_seconds`; defaults `seed` 1234, `times` 8 samples over
    the recipe's `meta.duration_seconds` (1 s without a recipe), `out`
    `<project>/Captures/<effect>/seed-<seed>-<camera>-<lighting>`. Proof:
    `Editor/Capture/VfxCaptureRequest.cs`.
41. `alive.peak` is the largest count over the samples and the probes (every `probe_interval`,
    default 0.05 s), not over every frame; each count is read after the effect is held still for
    `settle_frames`. Proof: `Editor/Capture/VfxCaptureSession.cs`, CAPTURE.md "Determinism".
42. `duration_seconds` and `bounds` may be null (an effect that did not end by `max_seconds`, or
    never had a particle). Proof: `Schemas~/vfx-capture.schema.json`.
43. The capture stage's ground and backdrop are the grid materials of `Library/Grid` (an 80 m ground,
    a wall 80 by 20 m standing 6 m behind the origin as seen from the camera), and the stage lights
    and frames through `VfxLightingRig.Apply` and `VfxCameraRig.Place`, the code of the Showcase.
    DESIGN names a backdrop and a ground only. Proof: `Editor/Capture/VfxCaptureStage.cs`,
    CAPTURE.md "Stage".
44. More optional inputs for characters: `character` (a prefab path or `placeholder`),
    `target_count`, `target_spacing`, `source_socket`, `target_socket`; they place a caster and
    targets in a lane and bind every `ChainLightningBinder` of the effect (a `.vfx` that exposes the
    binder's properties gets one). A prefab path that does not exist places the placeholder with a
    warning. Proof: `Editor/Capture/VfxCaptureRequest.cs` (`ParseCharacters`),
    `Editor/Capture/VfxCaptureCast.cs`, CAPTURE.md "Characters and targets".
45. `stats.json` has two more optional fields, `characters` (null without characters) and
    `binders` (empty without one); they are not required by the schema, so files written before
    them stay valid. Proof: `Schemas~/vfx-capture.schema.json`, `Editor/Capture/VfxCaptureSession.cs`.

### CI

46. The game-ci jobs open a copy of `Sandbox~` at `sandbox-home/Sandbox` next to the checkout at
    `sandbox-home/package`, not `Sandbox~` itself (Unity 6000.6 and `file:../../package`). Proof:
    `.github/workflows/ci.yml`, `.github/scripts/ci_sandbox.py`, CI.md.

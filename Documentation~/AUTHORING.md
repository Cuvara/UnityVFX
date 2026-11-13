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

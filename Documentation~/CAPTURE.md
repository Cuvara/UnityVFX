# Capture an effect

`capture` plays one effect in a neutral stage with a fixed seed and a fixed time step, and writes
evidence a reviewer or a lint can read: one PNG per sample, `contact-sheet.png` and `stats.json`
(schema [`Schemas~/vfx-capture.schema.json`](../Schemas~/vfx-capture.schema.json),
`cuvara.vfx-capture/1`). The contract is in [DESIGN.md](DESIGN.md), section "Capture"; the
differences from it are listed at the end of this file and, with those of the other verbs, in
[AUTHORING.md](AUTHORING.md#differences-from-designmd).

```sh
python Tools~/vfxcli.py capture --json '{"effect": "Tests/Fixtures/BurstQuads/BurstQuads.vfx", "seed": 1234}'
python Tools~/vfxcli.py capture --json '{"effect": "Effects/Spark/Spark.prefab", "seed": 7,
  "times": [0, 0.1, 0.2, 0.4, 0.8], "camera": "close", "lighting": "night", "out": "Sandbox~/Captures/Spark/night"}'
```

## Inputs

| Argument | Default | Meaning |
|---|---|---|
| `effect` | required | a `.prefab` with one or more `VisualEffect`s, or a `.vfx` |
| `seed` | `1234` | `VisualEffect.startSeed` of every `VisualEffect` (0 to 4294967295); `resetSeedOnPlay` is off |
| `times` | 8 times from 0 to the recipe's `meta.duration_seconds` (1 s without a recipe), rounded to 0.01 s | sample times in seconds; at least 0.01 s and one frame apart, at most 64, each at most `max_seconds` |
| `camera` | `gameplay` | `gameplay`, `close`, `side` |
| `lighting` | `dusk` | `day`, `dusk`, `night` |
| `out` | `<project>/Captures/<effect name>/seed-<seed>-<camera>-<lighting>` | output folder; for the Sandbox that is `Sandbox~/Captures/...` (gitignored). A folder Unity would import (under `Assets/` or a package, except below a `~` folder) is refused |
| `delta_time` | `1/60` | `Time.captureDeltaTime`, above 0 and at most 0.1 s |
| `width`, `height` | `640`, `360` | frame size, 64 to 2048 |
| `columns` | `4` | columns of the contact sheet, 1 to 16 |
| `probe_interval` | `0.05` | seconds between alive-count probes, from `delta_time` to 1 s (rounded to whole frames) |
| `settle_frames` | `70` | frames the effect is held still before a count is read, 62 to 600 (see Determinism) |
| `max_seconds` | `10` | upper bound of the simulated time, above 0 and at most 60 |
| `timeout_seconds` | `900` | wall-clock limit of the whole job, 30 to 3600 s |
| `character` | none | optional: a `.prefab` path of a character with `VfxSocket`s, or `placeholder` for the code-built `VfxMannequin`; places a caster (and the targets) in a lane, see [Characters and targets](#characters-and-targets). A `.prefab` path that does not exist places the placeholder with a warning; a path that is not a `.prefab` is an error |
| `target_count` | `0` | targets in the lane, 0 to 8 (`ChainLightningBinder.MaxTargets`); needs `character` |
| `target_spacing` | `3.5` | metres between two characters of the lane, 0.5 to 10; needs `character` |
| `source_socket` | `hand_r` | the caster's `VfxSocket` that is the binders' source; needs `character` |
| `target_socket` | `chest` | each target's `VfxSocket` that is a binder target; needs `character` |

`effect`, `character` and `out` may be written relative to the current directory: vfxcli.py
rewrites them (AUTHORING.md, "Agent interface"); a path inside `Samples~/Showcase` becomes
`Assets/Showcase/...`, where the Sandbox sees the sample:

```sh
python Tools~/vfxcli.py capture --json '{"effect": "Tests/Fixtures/ChainTargets/ChainTargets.vfx",
  "character": "Samples~/Showcase/Characters/VfxTestCharacterMale.prefab", "target_count": 3}'
python Tools~/vfxcli.py capture --json '{"effect": "Tests/Fixtures/BurstQuads/BurstQuads.vfx", "character": "placeholder"}'
```

`target_count`, `target_spacing`, `source_socket` or `target_socket` without `character` is an
error. A bad argument fails before play mode is entered, with every
problem in `errors` (an unknown `camera` or `lighting` lists the valid names).

## Presets

The presets are plain data in the runtime assembly, so the Showcase uses the same values:
`Cuvara.VFX.VfxLightingPreset` and `Cuvara.VFX.VfxCameraPreset` (`Runtime/Presets/`). `Get(name)`
returns a copy; `Names` lists them in order.

| Camera | Pitch | Yaw | Distance | FOV | Target height |
|---|---|---|---|---|---|
| `gameplay` | 45 | 0 | 10 m | 40 | 1 m |
| `close` | 15 | 0 | 4 m | 40 | 1 m |
| `side` | 5 | -90 (from +X) | 7 m | 40 | 1 m |

| Lighting | Key light | Fill | Ambient (trilight) | Post |
|---|---|---|---|---|
| `day` | warm white, 2.0, 50 degrees up | cool, 0.45 | bright sky | Bloom 0.6 (threshold 1.0), vignette 0.18, exposure +1 EV |
| `dusk` | orange, 1.6, 20 degrees up | blue, 0.35 | dim, purple | Bloom 0.8 (threshold 0.9), vignette 0.22, exposure +1 EV |
| `night` | moonlight blue, 0.5, 40 degrees up | 0.10 | very dark blue | Bloom 1.0 (threshold 0.8), vignette 0.25, exposure +0.75 EV |

All presets use ACES tonemapping. Every key light heading lies between -90 and 0 degrees, so the
key lights the front of the backdrop in every camera preset (the night key was at 30 degrees until
2026-11-27 and left the `side` backdrop near black; AUTHORING.md, Pitfalls).

## Stage

`VfxCaptureStage` (`Editor/Capture/VfxCaptureStage.cs`) builds the stage in a scene of its own:

- **Ground:** an 80 m square at y = 0, centred on the origin, material
  `Library/Grid/VfxGridGround.mat` (shader `Cuvara/VFX/Grid`: 1 m lines, stronger 5 m lines).
- **Backdrop:** a wall 80 m wide and 20 m high standing on the ground 6 m behind the origin as seen
  from the camera (turned to the camera's heading: it faces -Z for `gameplay` and `close`, +X for
  `side`), material `Library/Grid/VfxGridBackdrop.mat`. Its grid lines continue the ground's.
- **Lighting and camera** through the shared code of the Showcase:
  `VfxLightingRig.Apply(preset, key, fill, profile, camera)` (key light with soft shadows, fill
  light, trilight ambient of the stage scene, Bloom, ACES, vignette and post exposure in a global
  Volume with priority 1000, the solid background colour) and `VfxCameraRig.Place(camera, preset,
  origin)`. The camera renders with HDR, post-processing on and no anti-aliasing into an `ARGBHalf`
  linear render texture.
- **Effect** at the origin (the camera's focus); characters, when requested, in a lane around it.

What each camera sees: `gameplay` (45 degrees down at 10 m) shows the ground with the foot of the
wall in the top sixth of the frame; `close` (15 degrees at 4 m) shows the ground in the lower three
fifths and the wall above it; `side` (5 degrees at 7 m, from +X) shows the ground in the lower half
and the wall in the upper half. No preset sees past an edge of the ground or the wall, so the clear
colour never shows. The material colours are sRGB values (ground 0.30, backdrop 0.36; about 0.073
and 0.106 linear in the linear Sandbox, AUTHORING.md Pitfalls).

Measured mean luminance of the BurstQuads frame at 0.29 s, and of the empty stage at 1.00 s (seed
1234, 640x360, measured on 2026-11-27 with the grid stage):

| Camera, lighting | 0.29 s | 1.00 s (empty stage) |
|---|---|---|
| `gameplay`, `day` | 0.1809 | 0.1687 |
| `gameplay`, `dusk` | 0.0314 | 0.0167 |
| `gameplay`, `night` | 0.0166 | 0.0025 |
| `close`, `dusk` | 0.0904 | 0.0210 |
| `side`, `dusk` | 0.0714 | 0.0434 |
| `side`, `night` | 0.0297 | 0.0029 |

Before the grid stage (untextured ground and an 8 m backdrop) the 0.3 s figures were day 0.188, dusk
0.031, night 0.017, `close` dusk 0.092 and `side` dusk 0.072.

## Characters and targets

With `character` the capture places a caster and `target_count` targets (`VfxCaptureCast`,
`Editor/Capture/VfxCaptureCast.cs`):

- **Lane.** The effect stays at the origin, the camera's focus. The lane runs along +X, centred on
  the origin: with `n` targets `s` metres apart the caster stands at x = -n·s/2 facing +X and target
  `i` (1 to n) at x = -n·s/2 + i·s facing the caster, all at y = 0, z = 0. Without targets the
  caster stands at the origin, in the effect. A binder effect draws between world positions, so
  where its root is does not matter; any other effect plays at the lane's centre.
- **Frames.** With 3 targets 3.5 m apart (a lane of 10.5 m) the caster and every target are inside
  the `gameplay` frame at 16:9 (`characters[].in_frame` is true for all four;
  `Sandbox~/Captures/pkg-showcase/capture-stage/chain-male-gameplay-dusk-run2`). `close` shows the
  two middle characters and the chain between them; the caster and the last target are outside it.
  `side` looks down the lane from behind the last target, which then fills the middle of the frame
  and hides the others and the chain: use `gameplay` or `close` for a lane. A character not fully
  inside the frame gives a warning.
- **Characters.** A prefab is instantiated as is; `placeholder` is the code-built `VfxMannequin`
  (1.8 m, the five sockets). The package cannot reference the Showcase sample, so a sample
  character is given by path (`Assets/Showcase/Characters/VfxTestCharacterMale.prefab` in the
  Sandbox). A `.prefab` path that does not exist is not an error: the capture places the
  placeholder, the verb result has a warning, and `characters.placed` says `placeholder`.
- **Pose.** Each `Animator` of a placed character is evaluated once at time 0 (`Rebind`, then
  `Update(0)`) and then disabled: the pose is the first frame of the default state and stays fixed
  during the capture, so the characters do not make two captures with one seed differ.
- **Binding.** Every `ChainLightningBinder` under the effect gets the caster's `source_socket` as
  its source and the targets' `target_socket`s as its targets, in lane order, and binds at once,
  before the effect's first simulation step (so a burst at t = 0 reads the bound positions); it then
  binds every frame as usual. A `.vfx` has no binder: when characters are placed and its graph
  exposes `SourcePosition` or `TargetPosition0`, the capture adds one (`binders[].added`). A missing
  socket falls back to the character's root, with a warning.

The fixture `Tests/Fixtures/ChainTargets` exposes the binder's default properties and draws a burst
of 48 quads on the line from `SourcePosition` to `TargetPosition7` (the binder repeats the last
target in the unused slots).

## How it runs

1. `VfxCli.Run("capture", ...)` checks the arguments in edit mode, stores the job in
   `SessionState`, asks for play mode and returns `{"ok": true, "pending": true, "job", "out",
   "result_file"}` at once. Entering play mode reloads the scripting domain in the Sandbox (Enter
   Play Mode Options off), so a synchronous call cannot span it.
2. After the reload `VfxCaptureJob` (`[InitializeOnLoad]`) finds the job and starts a
   `VfxCaptureSession`: a scene of its own with the stage, cameras, lights and Volumes of other
   scenes disabled, `Application.runInBackground` on (an unfocused editor does not advance play mode
   otherwise), `Time.captureDeltaTime` set, the effect instantiated, every `VisualEffect` with
   `resetSeedOnPlay` off, `startSeed` set, `playRate` 0 and `Reinit()`.
3. A player-loop step at the start of every frame drives the capture (see Determinism). When the
   last sample is taken and the effect has ended (or nothing ever spawned, or `max_seconds` passed),
   the session writes the files, removes the stage and restores the time settings.
4. The job leaves play mode; Unity restores the scenes the editor had. Only then the job writes
   the final result to `result_file`, so the editor is in edit mode when the result appears.
   `vfxcli.py` waits for that file and prints the result (exit 0 when `ok`).

Batch path: `vfxcli.py --mode batch` (or `auto` with no editor open) starts a batch-mode editor
with graphics; `VfxCli.Batch` does not exit for a pending verb, the job writes `-vfxOut` and exits
with 0 or 1. Verified: a batch capture of BurstQuads took 30 s (peak 2.04 GB private) and wrote
the same PNG hashes and counts as the editor path.

## Outputs

- `frame-<t>.png` per sample (`t` with two decimals): the camera's render target after
  post-processing, converted from linear to sRGB, 8-bit, opaque.
- `contact-sheet.png`: the frames in sample order, left to right and top to bottom, `columns` per
  row, 4 px dark gaps. The first cell is the earliest sample.
- `stats.json`:

| Field | Meaning |
|---|---|
| `schema` | `cuvara.vfx-capture/1` |
| `effect` | the project path of the effect (`Packages/com.cuvara.vfx/...` or `Assets/...`) |
| `recipe_sha256` | SHA-256 of the `.vfxrecipe.json` next to the effect's `.vfx` (a prefab: its first `VisualEffect`'s asset), or null |
| `seed`, `delta_time`, `camera`, `lighting` | the inputs (`delta_time` rounded to 6 decimals) |
| `unity`, `vfx_graph` | editor and VFX Graph versions |
| `duration_seconds` | simulated time of the first probe after the peak with no particle alive and no system awake (resolution: `probe_interval`); null when the effect did not end by `max_seconds` (looping) or never had a particle |
| `alive.peak` | the largest alive count at any sample or probe |
| `alive.samples` | `{t, count}` per sample |
| `bounds` | world-space union of the bounds of each particle system at the checkpoints where it had particles (for `Manual` bounds that is the configured box); null when nothing was alive |
| `systems` | `{name, capacity, peak}` per particle system (prefixed with the `VisualEffect`'s path when a prefab has several) |
| `frames` | `{t, file, sha256, mean_luminance, max_luminance}`: Rec. 709 luminance of the linear render target after tonemapping |
| `contact_sheet` | `{file, sha256, columns}` |
| `resolution`, `probe_interval`, `settle_frames` | how the capture was taken |
| `characters` | added 2026-11-27, optional: null without characters; else `character` (the input), `placed` (`placeholder` or the prefab's project path: what was actually placed), `source_socket`, `target_socket`, `target_count`, `target_spacing`, `caster` and `targets[]`, each `{position, socket, socket_found, in_frame}` (world positions of the root and of the bound socket) |
| `binders` | added 2026-11-27, optional: one `{name, added, hop_count, graph_hop_count, source, targets}` per bound `ChainLightningBinder` (empty without one); `source`, `targets` and `graph_hop_count` are read back from the graph after binding (null, or fewer entries, when it does not expose the property) |

The final verb result holds the same fields plus `out`, `stats_file`, `probes` (number of
checkpoints), `pending` (false) and `job`. A warning says when nothing was alive or the effect did
not end.

## Determinism

- The effect's clock advances only while its `playRate` is 1, one frame of `delta_time` per player
  frame. The capture step runs first in `EarlyUpdate`, when the previous frame has simulated and
  rendered.
- At every sample frame (`round(t / delta_time)`) and every probe frame the effect is held still
  (`playRate` 0) for `settle_frames` frames, then the counts are read, and at a sample the render
  target is read. VFX Graph refreshes `aliveParticleCount` from the GPU only every 60 frames
  (AUTHORING.md, Pitfalls), so a count read sooner can be a count of an earlier frame. After 62 or
  more held frames the count is the count of the held state. A count of -1 extends the hold up to
  four times, then the capture fails; -1 is never recorded.
- t = 0 is the state after the first update with a zero time step: a `Single` burst with no delay
  has already spawned (BurstQuads: 24 at t = 0).
- Verified: BurstQuads with seed 1234 through vfxcli.py twice (`Sandbox~/Captures/BurstQuads/run-1`,
  `run-2`) and once through batch mode: `alive.peak` 24, counts 24, 24, 24, 24, 24, 14, 3, 0 at
  the 8 default times, `duration_seconds` 0.9, and the same SHA-256 for every frame and the
  contact sheet. Seed 98765: peak 24 (the burst count is fixed), counts 24, 24, 24, 24, 24, 14, 2,
  0, other frame hashes. The EditMode test `CaptureTests` checks the same with two captures.
  After the fixture was rebuilt at the current resource version (AUTHORING.md, Pitfalls, "Gradient
  colours of a built graph changed at the first compile"), seed 1234 gave the same counts and
  `alive.peak` 24 again; the frames are brighter and more orange, so their hashes changed.
- With the grid stage (2026-11-27): BurstQuads with seed 1234 twice gave `alive.peak` 24, the same
  counts (24, 24, 24, 24, 24, 14, 3, 0) and the same SHA-256 for all 8 frames and the contact sheet.
  ChainTargets with the male test character and 3 targets: `alive.peak` 48 and the same counts
  (48, 48, 48, 48, 48, 26, 6, 0) in 8 runs. The frame hashes took one of two values: 6 runs had
  one set, and the 2 runs that were the first character capture after an editor start or after the
  EditMode tests had the other set, equal to each other. The two sets differ in 6 of the 8 frames
  (the frames with particles), each in about 120 of 230400 pixels by 1 of 255, spread over the
  frame by the bloom, with the same mean luminance to 6 decimals. The cause was not established;
  BurstQuads without characters gave one set in every run, also right after the editor start.
- Equal PNG hashes are observed, not promised: they depend on the GPU and driver. `alive` counts
  are what two captures must agree on.

## Reading the evidence

- Look at the contact sheet first: does the effect read at the gameplay distance, does the colour
  change over life as intended, is it lost against the day backdrop or blown out at night?
- `alive.peak` against `systems[].capacity`: a peak at the capacity means particles were dropped.
- `alive.samples` against the recipe's `meta.phases` and `duration_seconds` against
  `meta.duration_seconds`: does the effect last as long as it should?
- `max_luminance` near 1 means pixels at the top of the tonemapping curve (bright, likely bloomed);
  `mean_luminance` compares a frame with the empty stage (the last sample of a finished effect).
- `bounds` against the effect's intended area; with `Manual` bounds it only repeats the box.

## Limits

- Overdraw, draw calls and frame time are not measured; they stay a human review item.
- Luminance is measured after ACES tonemapping, so `max_luminance` stays near or below 1; the HDR
  colour values of the graph are reported by `inspect` (`hdr_colors`).
- The alive count of a system that receives GPU events is not reliable unless it stores the
  `alive` attribute (VFX Graph's own profiler shows `-` for it); its peak may be off.
- Probes are `probe_interval` apart, so a peak shorter than that may be missed between probes;
  a smaller interval makes the capture slower (each checkpoint holds `settle_frames` frames:
  BurstQuads with defaults took 15 s through the editor).
- A prefab is instantiated as is: exposed properties keep the prefab's values. `ChainLightningBinder`
  targets are bound only with `character` and `target_count`; other scene references an effect may
  need (a mesh, a skinned mesh, a custom binder) are not set, and the Showcase covers those.
- The `side` camera looks down the character lane and sees the nearest target from behind; the lane
  reads in `gameplay` (all of it) and `close` (its middle).
- The stage disables the cameras, lights and Volumes of every other loaded scene
  (`VfxCaptureStage.DisableOtherScenes`), but not their renderers or components. With
  `Samples~/Showcase/Showcase.unity` open in the editor, the play-mode capture also runs the sample's
  `ShowcaseCharacterSpawn` components and shows the Showcase's characters around the effect (seen
  2026-11-27 in a BurstQuads capture without `character`). Open an empty scene before a capture.
- The capture needs the editor in edit mode with no compile error and no other capture pending.
  A script change during a capture reloads the domain and fails the job with an error result.

## Differences from DESIGN.md

- `capture` through `VfxCli.Run` returns a pending result (`pending`, `job`, `out`,
  `result_file`); the final result is in `result_file`. vfxcli.py hides this.
- `effect` in `stats.json` is the project path (`Packages/com.cuvara.vfx/...`), not the path
  inside the repository.
- `stats.json` has three more fields: `resolution`, `probe_interval`, `settle_frames`. The final
  verb result also has `out`, `stats_file`, `probes`, `pending` and `job`.
- `mean_luminance` and `max_luminance` are after tonemapping (DESIGN's example shows 7.8).
- More optional inputs: `delta_time`, `width`, `height`, `columns`, `probe_interval`,
  `settle_frames`, `max_seconds`, `timeout_seconds`.
- `alive.peak` is the largest count over the samples and the probes, not over every frame.
- `duration_seconds` and `bounds` may be null.
- Defaults: `seed` 1234, `times` 8 samples over the recipe's `meta.duration_seconds`, `out` under
  `<project>/Captures`.
- The presets live in the runtime assembly (`Runtime/Presets/`), not under `Editor/Capture/`, so
  the Showcase's runtime overlay can use them.
- The stage is a grid ground and a grid wall from `Library/Grid`, lit and framed through
  `VfxLightingRig` and `VfxCameraRig`, the code of the Showcase.
- Optional characters and binding: `character`, `target_count`, `target_spacing`, `source_socket`,
  `target_socket`; `stats.json` fields `characters` and `binders`.

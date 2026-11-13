# Capture an effect

`capture` plays one effect in a neutral stage with a fixed seed and a fixed time step, and writes
evidence a reviewer or a lint can read: one PNG per sample, `contact-sheet.png` and `stats.json`
(schema [`Schemas~/vfx-capture.schema.json`](../Schemas~/vfx-capture.schema.json),
`cuvara.vfx-capture/1`). The contract is in [DESIGN.md](DESIGN.md), section "Capture"; the
differences from it are listed at the end of this file.

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
| `columns` | `4` | columns of the contact sheet |
| `probe_interval` | `0.05` | seconds between alive-count probes (rounded to whole frames) |
| `settle_frames` | `70` | frames the effect is held still before a count is read (at least 62, see Determinism) |
| `max_seconds` | `10` | upper bound of the simulated time |
| `timeout_seconds` | `900` | wall-clock limit of the whole job |

`effect` and `out` may be written relative to the current directory: vfxcli.py rewrites them
(AUTHORING.md, "Agent interface").

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

All presets use ACES tonemapping. The stage: a grey ground (linear albedo 0.30), a grey backdrop
(0.36) 8 m behind the effect as seen from the camera, the key light with soft shadows, a global
Volume with priority 1000, and a camera with HDR, post-processing on, no anti-aliasing, rendering
into an `ARGBHalf` linear render texture. The effect sits at the origin. Measured mean luminance of
the BurstQuads frame at 0.3 s (`gameplay`): day 0.186, dusk 0.029, night 0.015; `close` dusk 0.085,
`side` dusk 0.069.

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
- A prefab is instantiated as is: exposed properties keep the prefab's values, and nothing binds
  `ChainLightningBinder` targets; the Showcase covers effects that need scene targets.
- The capture needs the editor in edit mode with no compile error and no other capture pending.
  A script change during a capture reloads the domain and fails the job with an error result.

## Differences from DESIGN.md

- `capture` through `VfxCli.Run` returns a pending result (`pending`, `job`, `out`,
  `result_file`); the final result is in `result_file`. vfxcli.py hides this.
- `effect` in `stats.json` is the project path (`Packages/com.cuvara.vfx/...`), not the path
  inside the repository.
- `stats.json` has three more fields: `resolution`, `probe_interval`, `settle_frames`.
- `mean_luminance` and `max_luminance` are after tonemapping (DESIGN's example shows 7.8).
- More optional inputs: `delta_time`, `width`, `height`, `columns`, `probe_interval`,
  `settle_frames`, `max_seconds`, `timeout_seconds`.
- The presets live in the runtime assembly (`Runtime/Presets/`), not under `Editor/Capture/`, so
  the Showcase's runtime overlay can use them.

# Showcase sample

The Showcase (`Samples~/Showcase`) is the scene where a person play-tests an effect that an agent
built. The agent's own evidence is the `capture` verb ([CAPTURE.md](CAPTURE.md)): a deterministic
contact sheet in a neutral stage. The Showcase is the same look in a real scene, in play mode and in
real time: a grey grid room, rigged test characters with `VfxSocket`s, four stations, the lighting
presets `day`, `dusk` and `night` and the camera presets `gameplay`, `close` and `side` of the
capture stage, and an overlay to pick an effect, replay it and change the time scale, the lighting
and the camera. The contract is DESIGN.md, section "Showcase scene"; this file describes the scene
as built. Where it differs from DESIGN.md, see [Differences from DESIGN.md](#differences-from-designmd).

## Open it

**In the Sandbox** (the way an agent or a package developer opens it). `python Tools~/setup-sandbox.py`
creates the junction (Windows) or symlink `Sandbox~/Assets/Showcase` -> `Samples~/Showcase`
(gitignored; [AUTHORING.md](AUTHORING.md), "Sandbox"), so the sample has one copy and its `.meta`
files are the committed ones. Start the editor with `python Tools~/setup-sandbox.py --open` (never
open `Sandbox~` directly), open `Assets/Showcase/Showcase.unity` and enter Play mode. The editor must
be focused, or `Application.runInBackground` must be on, for Play mode to advance
(AUTHORING.md, Pitfalls). With the Game View at 1920x1080 the panel and the labels are laid out for
a 1080p frame and scale with `Screen.height / 1080` (clamped to 0.5 to 3).

**As a consumer.** Add the package to a project (README, "Install"), then Window > Package Manager >
Cuvara VFX > Samples > Showcase > Import. Unity copies `Samples~/Showcase` to
`Assets/Samples/Cuvara VFX/<version>/Showcase`; open `Showcase.unity` there. The project needs URP
and VFX Graph (the package depends on both). Soft shadows of the key light show when the project's
URP asset supports soft shadows (`Sandbox_URP.asset` does: `m_SoftShadowsSupported: 1`). The sample
assembly `Cuvara.VFX.Samples.Showcase` references only `Cuvara.VFX`. Import through Package Manager
into a second project was NOT tested; the sample was only seen through the Sandbox junction.

## The room and the four stations

The room is a floor and four walls made of cubes with the two materials of `Library/Grid`
(`VfxGridGround.mat`, `VfxGridBackdrop.mat`, shader `Cuvara/VFX/Grid`: 1 m lines, stronger 5 m lines).
It has no skybox and no colliders. Measured from `Showcase.unity` (transforms and renderer bounds,
read in the Sandbox):

| Part | Value |
|---|---|
| Clear floor | x -12 to 12, z -13 to 55: 24 m by 68 m, top at y = 0 (the `Floor` cube is 24.8 by 68.8 m, 0.2 m thick, so its edge is under the walls) |
| Walls | `Wall North`, `Wall South`, `Wall East`, `Wall West`: 10 m high, 0.4 m thick, their inner faces on the edge of the clear floor |
| Stations | in a row along +Z at z = 0, 14, 28 and 42 (x = 0): 14 m apart, so no other station is in the gameplay frame of one |

The camera focus of each station is the station's own transform; the camera presets orbit it
(CAPTURE.md, "Presets"). Positions below are world metres (x, y, z).

| # | Station (label) | What stands there | Effect slot | Trigger |
|---|---|---|---|---|
| 1 | Chain lightning lane | A caster and three targets in a row turned -30 degrees about Y (the row points 30 degrees from +X towards +Z): caster (-4.22, 0, -1.24); targets (-1.41, 0, 0.39), (1.41, 0, 2.01), (4.22, 0, 3.64). Each is 3.25 m from the previous one (9.75 m from the caster to the last target). The caster faces along the row, the targets face it. | `chain-targets` | `Interval`, 2.5 s |
| 2 | Single target | A caster (-1.30, 0, 13.25) and one target (1.30, 0, 14.75), 3.00 m apart on the same -30 degree turn. | `burst-quads` | `Interval`, 2 s |
| 3 | Ground area | A disc on the floor at (0, 0.01, 28): a cylinder 5 m across, 0.01 m thick, material `ShowcaseAreaMarker.mat`. No character. | `burst-quads` | `Interval`, 2 s |
| 4 | Empty pedestal | A cylinder 1.2 m across and 0.9 m high at (0, 0.45, 42), material `ShowcasePedestal.mat`, and an `Effect Point` on its top at (0, 0.9, 42). No character. | none | `Interval`, 2 s (silent without an effect) |

How each station plays:

- Station 1 plays at the caster's `hand_r` socket without being parented to it (`attachToAnchor`
  false); the `ChainLightningBinder` on the effect gets the caster's `hand_r` as its source and the
  targets' `chest` sockets as its targets, so the chain runs from the caster's hand to the last target's
  chest.
- Station 2 plays under the caster's `hand_r` (parented). Its single target is the test
  character 3 m away, on its `chest`; the `burst-quads` fixture has no `ChainLightningBinder`, so the
  effect does not use the target (it stands there as a scale reference and for a binder effect).
- Station 3 plays at the station's own transform, the middle of the disc.
- Station 4 has `Effect Point` as its anchor and no effect id, so it shows its label and stays silent.

The rows run diagonally on purpose: a row along X fills the `side` frame with its last target, and a
row along Z hides the caster below the `gameplay` frame (AUTHORING.md, Pitfalls).

Until real effects exist, the stations play the package's test fixtures, not effects of `Effects/`:
`chain-targets` is `Tests/Fixtures/ChainTargets/ChainTargets.vfx` (48 additive quads on the line from
`SourcePosition` to `TargetPosition7`) and `burst-quads` is `Tests/Fixtures/BurstQuads/BurstQuads.vfx`
(24 additive quads, one burst). Two prefabs in `Samples~/Showcase/Effects/`
(`ShowcaseChainTargets.prefab`, `ShowcaseBurstQuads.prefab`) reference the `.vfx` files by GUID, so no
fixture is copied or rebuilt, and `ShowcaseLibrary.asset` lists them with the ids above (1 s each, tags
`fixture`; the lane's also `chain`). No effect is authored under `Effects/` by this task.

## `VfxStation`

`Cuvara.VFX.VfxStation` (`Runtime/Showcase/VfxStation.cs`, menu *Cuvara > VFX > VFX Station*) is part of
the runtime assembly because the capture stage shares the Showcase's rigs (see
[Lighting](#lighting-and-why-it-matches-the-capture-stage)). One component per station:

| Field (serialized) | Meaning |
|---|---|
| `label` (`Label`) | name shown by the overlay |
| `player` (`Player`) | the `VfxPlayer` that plays; empty: the first one in the scene |
| `effectId` (`EffectId`) | the effect slot: an id of the player's `VfxLibrary`, changeable at run time; null or empty: the station is silent (`PlayNow` returns `VfxHandle.None`, no log) |
| `anchor` (`Anchor`) | where the effect plays, for example the caster; empty: the station's own transform |
| `anchorSocket` (`AnchorSocket`) | a socket name looked up under the anchor (`hand_r`, `chest`, ...) with `VfxSocket.FindOrRoot`; empty: the anchor itself |
| `attachToAnchor` (`AttachToAnchor`) | true: the instance is parented to the resolved anchor; false: it is placed at the anchor's position and rotation |
| `targets` (`Targets`) | targets in hop order (transforms, usually the character places); null and inactive ones are skipped; at most `ChainLightningBinder.MaxTargets` (8) are used |
| `targetSocket` (`TargetSocket`) | a socket name looked up under each target; empty: the target itself |
| `trigger` (`Trigger`) | `VfxStationTrigger.Manual` (only `PlayNow` plays), `Interval` (plays when the station is enabled, then every `LoopInterval` seconds of scaled time) or `Key` |
| `loopInterval` (`LoopInterval`) | seconds between two plays of `Interval`, at least 0.05 |
| `key` (`Key`) | the `KeyCode` of `Key` |

`PlayNow()` plays the effect through `VfxPlayer` as a one-shot; when the played instance carries a
`ChainLightningBinder`, the station sets the anchor as its source and the valid targets as its
targets and binds at once. `ResetTimer()` restarts the interval so the next `Update` plays. The
event `Played` fires after every successful play; `LastHandle` and `PlayCount` read the last play.
The `Key` trigger reads the Input System when the project has `com.unity.inputsystem` and its active
input handling includes it, else the legacy Input Manager, else it never fires. The interval runs on
scaled time, so the overlay's time scale slows the interval together with the effect.
`WasPressedThisFrame(KeyCode)` is the static key read the overlay uses too.

## Test characters, sockets and the placeholder

**Characters.** `Samples~/Showcase/Characters/` holds `VfxTestCharacterMale.prefab` (1.79 m, measured 1.783 to 1.787 m standing) and
`VfxTestCharacterFemale.prefab` (1.75 m): the `Superhero_Male_FullBody.fbx` and
`Superhero_Female_FullBody.fbx` models of the Quaternius pack Universal Base Characters (Standard)
and one idle clip (`VfxTestCharacter_Idle.anim`, the `Armature|Idle_Loop` clip of the Quaternius
Universal Animation Library, Standard, 2.5 s, looping) in `VfxTestCharacter.controller`, plus two URP
Lit materials. Each prefab has a root, a child `Model` carrying the `Animator`, and five
`VfxSocket` objects on rig bones: `head` on `Head`, `chest` on `spine_03`, `hand_l` on `hand_l`,
`hand_r` on `hand_r`, `feet` on `root`. The characters face +Z of their place.

**Third-party notice.** The models and the idle clip are by Quaternius (https://quaternius.com) under
CC0 1.0 Universal, downloaded 2026-11-27. Source, author, licence text, archive hashes and what was
changed are in `THIRD-PARTY.md` at the repository root and in `Samples~/Showcase/Characters/LICENSE.md`.
`THIRD-PARTY.md` also records that the Quaternius site-wide licence page now shows a different
licence (QAL v1.0): whether the CC0 reading holds is an owner decision before a public release.
Everything else in the sample is under the package's MIT licence; no Unity sample asset is copied.

**Character places.** The scene holds no character model. Each of its six character places is a
`ShowcaseCharacterSpawn` (`Samples~/Showcase/Scripts/ShowcaseCharacterSpawn.cs`) whose `prefab`
reference is a test character (male and female alternate); in `Awake` (play mode only) it
instantiates the prefab as a child. Stations point at the places, not at the characters, and look up
the sockets under the place at every play.

**The placeholder mannequin.** When a place's `prefab` is empty, or the model is missing (the folder
`Samples~/Showcase/Characters` is absent, which is what happens when the third-party characters leave
the repository), the place instead builds `VfxMannequin.Create(transform, "Placeholder")`
(`Runtime/Showcase/VfxMannequin.cs`): a code-made capsule mannequin 1.8 m tall (two legs, torso, two
arms, a head and a visor marking the front), feet at y = 0, facing +Z, one grey URP Lit material,
no collider, with the same five sockets (`head` at 1.69 m, `chest` at 1.35 m on the front, the hands
at 0.82 m, `feet` at 0). It logs one information line per place
(`Showcase: '<place>' has no character prefab ...`), not a warning or an error. The capture stage
uses the same mannequin with `"character": "placeholder"` (CAPTURE.md, "Characters and targets").

To drop the third-party characters: delete `Samples~/Showcase/Characters` and its `.meta`, and the
Quaternius sections of `THIRD-PARTY.md`; the scene then plays with six mannequins. To replace them:
make a prefab with your own rigged model and five `VfxSocket` components named `head`, `chest`,
`hand_l`, `hand_r` and `feet`, and set it as the `prefab` of each `ShowcaseCharacterSpawn`
(or one of them), then add its source to `THIRD-PARTY.md`.

## Lighting, and why it matches the capture stage

`VfxLightingRig` (`Runtime/Showcase/VfxLightingRig.cs`) on the `Lighting` object holds the key
light, the fill light, the global Volume and the camera. `ApplyPreset(name)` sets, from one
`VfxLightingPreset`: the key (directional, soft shadows) and the fill (directional, no shadows),
the trilight ambient of the scene, the camera's solid background colour, and in the Volume's
profile Bloom, Tonemapping ACES, a light Vignette and post exposure. The rig gives the Volume a
profile made at run time and never writes the scene's profile asset
(`Samples~/Showcase/Settings/ShowcaseVolumeProfile.asset` holds the `dusk` values only for the look
in edit mode). The saved default preset is `dusk`. The Volume in the scene has priority 10; the
capture stage's has priority 1000.

The values of the three presets are the table in CAPTURE.md, "Presets"; they live once, in
`Runtime/Presets/VfxLightingPreset.cs` (`Get("day")`, `Get("dusk")`, `Get("night")`) and
`VfxCameraPreset.cs` (`Get("gameplay")`, `Get("close")`, `Get("side")`; `gameplay` is pitch 45 degrees,
10 m, FOV 40, looking at 1 m above the focus). The capture stage calls the same static
`VfxLightingRig.Apply(preset, key, fill, profile, camera)` and `VfxCameraRig.Place(camera, preset,
origin)` that the scene's rigs call, so a preset name gives the same lights, ambient, post and camera
in both; the scene differs only in what is in front of the camera (a real room, characters and the
stations' effects). The room's grid materials are the capture stage's ground and wall materials.
The camera has HDR and post-processing on (`UniversalAdditionalCameraData`). Every key heading lies
between -90 and 0 degrees, so the key lights the front of the walls in every camera preset; the
night key is (40, -45) since 2026-11-27.

## The overlay

`ShowcaseOverlay` (`Samples~/Showcase/Scripts/ShowcaseOverlay.cs`, object `Showcase Overlay`) draws a
panel along the bottom edge of the Game View (118 px high at 1080p) and a label above each station
(`<n>  <label>` and the effect id, or `no effect`; the selected one in amber and clamped to the
screen, the others only within 12 m of the camera). It uses IMGUI, so it works with the mouse and
needs no font or UI asset. Every action is a button of the panel and a key:

| Key | Action |
|---|---|
| 1 to 4 (to 9 with more stations) | select that station; the camera is placed around it with the current camera preset |
| Tab | select the next station |
| Q, E | previous or next effect of the selected station: "no effect", then the ids of the player's `VfxLibrary` in order; the effect replays |
| Space | replay the selected station's effect now (an `Interval` station restarts its interval) |
| T | next time scale: 0.1, 0.25, 0.5, 1, 2 |
| L | next lighting preset: `day`, `dusk`, `night` |
| C | next camera preset around the selected station: `gameplay`, `close`, `side` |
| H | hide or show the panel (the labels stay) |

The overlay has no lighting or camera number of its own: `SetLightingPreset(name)` calls
`VfxLightingPreset.Get(name)` and `lightingRig.ApplyPreset`; `SetCameraPreset(name)` sets the camera
rig's focus to the selected station and calls `VfxCameraPreset.Get(name)` and `cameraRig.ApplyPreset`.
The keys are read through `VfxStation.WasPressedThisFrame`, so the assembly compiles and runs with
the Input System, the legacy Input Manager or both. The time scale is restored when the overlay is
destroyed. At the start the first station is selected with the `gameplay` camera.

## Add an effect

1. Build the effect under `Effects/<Name>/` with `build` ([AUTHORING.md](AUTHORING.md)): its prefab
   must hold a `VisualEffect` (a `ChainLightningBinder` on it if it needs a source and targets).
2. Add an entry to a `VfxLibrary` asset: an id (kebab case), the prefab, the duration in seconds and
   tags. The Showcase's is `Samples~/Showcase/ShowcaseLibrary.asset`; a new library is *Assets > Create
   > Cuvara > VFX > Library*. `VfxLibrary.Validate()` reports empty or duplicate ids, a missing prefab,
   a prefab without a `VisualEffect` and a negative duration.
3. In the scene the `VFX Player` object's `Library` is `ShowcaseLibrary`. In Play mode pick the new id
   with Q, E or the `<` and `>` buttons on the station you want; it plays through the station.
4. To make it a station's default, set the `Effect Id` of that station's `VfxStation` in the scene (the
   id of the entry). To use another anchor, set `Anchor` and `Anchor Socket`; for a chain effect, list
   the targets in `Targets` and set `Target Socket`.

Edit the sample through the Sandbox junction (`Assets/Showcase`), so the `.meta` files Unity writes
are the committed ones. Let Unity write scenes, prefabs and assets; do not write their YAML by hand.

## Smoke tests

`Sandbox~/Assets/Tests/ShowcaseSmokeTests.cs` (assembly `Cuvara.VFX.Sandbox.ShowcaseTests`, PlayMode,
no compile reference to the sample, which it reaches by type name) has seven tests:
`Scene_LoadsAndPlays_WithNoErrorOrException`,
`Scene_HasTheFourStations_AndTheLaneHasACasterAndThreeTargets3To4MetresApart`,
`EveryCharacter_HasTheFiveSockets`, `SpawnPoint_WithoutAPrefab_PlacesThePlaceholderWithTheFiveSockets`,
`LightingPresets_ThroughTheOverlay_EqualTheSharedPresetData`,
`CameraPresets_ThroughTheOverlay_PlaceTheCameraAtTheSharedPresetForTheSelectedStation` and
`FixtureStation_HasAliveParticles_AfterItsTrigger`. They are ignored, with the reason, when
`Assets/Showcase` is absent, and the particle test also without a graphics device or compute shaders.
`.github/scripts/ci_sandbox.py` copies `Samples~/Showcase` into the CI project, so the tests run
there too. The package's own PlayMode tests of the runtime pieces are `VfxStationTests`,
`VfxRigTests` and `VfxMannequinTests` (`Tests/Runtime`).

## Limits and known gaps

- **The idle pose is a wide stance.** The characters' only animation is the genuine
  `Armature|Idle_Loop` of the Universal Animation Library. It has no root motion and the feet stay
  planted, but a wide, staggered stance is held through the whole 2.5 s loop (feet about 0.59 m apart,
  body turned about 17 degrees), so a character can look as if it is mid-lunge, and a frozen frame
  (the capture stage's pose at t = 0) looks the same. A relaxed clip would be another source and a
  `THIRD-PARTY.md` change.
- **The free tier has one body type.** Only the "Superhero" male and female proportions are in the
  Standard packs; the bodies have no clothes and no hair, and are drawn in one skin-tone material. The
  feet sink 1 to 2.4 cm below y = 0 in the idle.
- **`ScreenCapture` can return a black frame.** About 3 of 38 Game View screenshots taken while
  building the scene, and 1 of 8 taken for the evidence of this task (the first shot after a lighting
  switch, `dusk`), were fully black files of 27,260 bytes; a retake was normal. The cause was not established. Check
  the size of a screenshot and retake a black one.
- **In `close` the selected label sits on the top edge.** The label is clamped to the screen so it stays
  visible, and the `close` frame puts the station's anchor below the top, so the label can be away
  from its station.
- **The fixtures are placeholders.** `burst-quads` on the caster's `hand_r` flies along the bone's axes,
  so at the gameplay camera its quads can leave downwards under the panel; the lane's `chain-targets`
  is a line of quads, not lightning. They show that the sockets, the binder and the triggers work;
  they say nothing about a finished effect.
- **Night is dark by design.** The 1 m and 5 m grid lines are faint at night and nearly invisible in the
  `gameplay` frame; at `day` the characters are very light. The presets are shared with the capture
  stage, so the same holds there.
- **Capture frame hashes with characters take one of two reproducible sets** (CAPTURE.md,
  "Determinism"); counts are always equal.
- **Capture renders every open scene's meshes.** The capture stage disables the cameras, lights and
  Volumes of other loaded scenes but not their renderers, so a capture taken while `Showcase.unity` is
  open in the editor shows the Showcase's characters around the effect. Open an empty scene (or
  another without meshes) before a capture. See CAPTURE.md, "Limits".
- **Keys were not pressed by a person.** The evidence and the smoke tests call the overlay's public
  methods, which the keys and buttons call. Input with the legacy Input Manager only, or both
  backends, was not compiled (the Sandbox has the Input System only).
- **Import through Package Manager into a second project was not tested.**

## Differences from DESIGN.md

- The characters are not in the scene: each character place instantiates its prefab in play mode, or
  the code-built `VfxMannequin` when the model is missing, so the scene works without
  `Samples~/Showcase/Characters`.
- The stations play the test fixtures `chain-targets` and `burst-quads` until real effects exist; the
  pedestal is empty, as DESIGN says.
- Station rows run at -30 degrees, not along an axis.
- The sample has its own assembly `Cuvara.VFX.Samples.Showcase`; `VfxStation` and the rigs are in
  `Cuvara.VFX` so that the capture stage shares them.
- The overlay's presets are the shared presets of `Runtime/Presets/`, as DESIGN asks; the night key
  heading is (40, -45) instead of the first (40, 30).

The full list of differences between the built package and DESIGN.md is in AUTHORING.md, "Differences
from DESIGN.md".

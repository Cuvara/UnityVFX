# Showcase sample

A prototype scene to look at effects in context: a grey grid room with four effect stations, rigged
test characters with `VfxSocket`s, the lighting presets `day`, `dusk` and `night` (shared with the
`capture` verb's stage) and an overlay to pick, replay and inspect an effect.

## Import and open

1. Window > Package Manager > Cuvara VFX > Samples > Showcase > Import. Unity copies the sample to
   `Assets/Samples/Cuvara VFX/<version>/Showcase`.
2. Open `Showcase.unity` and enter Play mode.

The project needs URP (the scene uses URP Lit materials, a URP Volume and the package's grid shader)
and VFX Graph, which the package depends on. Soft shadows of the key light show when the project's
URP asset supports soft shadows. The scripts compile with the Input System, the legacy Input Manager
or both (assembly `Cuvara.VFX.Samples.Showcase`, it references only `Cuvara.VFX`).

## What is in the scene

| Station | What stands there | Effect slot |
|---|---|---|
| 1 Chain lightning lane | a caster and three targets in a diagonal row, 3.25 m apart; the effect starts at the caster's `hand_r` and binds the targets' `chest` sockets | `chain-targets` (fixture) |
| 2 Single target | a caster and one target, 3 m apart; the effect plays on the caster's `hand_r` | `burst-quads` (fixture) |
| 3 Ground area | a 5 m disc on the floor | `burst-quads` (fixture) |
| 4 Empty pedestal | a pedestal; the effect point is on its top | none |

Each station is a `VfxStation` with the `Interval` trigger (2 to 2.5 s). A station without an effect
shows its label and stays silent. The two effects are the package's test fixtures
(`Tests/Fixtures/BurstQuads`, `Tests/Fixtures/ChainTargets`), referenced by the prefabs in `Effects/`
and listed in `ShowcaseLibrary.asset`; they stand in until real effects exist. Add an effect by adding
an entry to the library and picking it in the overlay, or by setting a station's effect id.

The characters are spawned in play mode: every character place is a `ShowcaseCharacterSpawn` with a
prefab of `Characters/`. When the prefab is empty or missing, it places a code-built placeholder
mannequin with the same five sockets (`head`, `chest`, `hand_l`, `hand_r`, `feet`) and logs one
information line, so the scene still works without the character models.

Lighting is applied by a `VfxLightingRig` (key light with soft shadows, fill light, trilight ambient,
a global Volume with Bloom, ACES tonemapping, a light vignette and post exposure), with `dusk` saved as
the default. The rig gives the Volume a profile made at run time; `Settings/ShowcaseVolumeProfile.asset`
holds the `dusk` values for the look in edit mode.

## Controls (Play mode)

| Key | Action |
|---|---|
| 1 to 4, Tab | select a station; the camera moves to it |
| Q, E | previous or next effect of the selected station (from the library, or none) |
| Space | replay the selected station's effect |
| T | next time scale: 0.1, 0.25, 0.5, 1, 2 |
| L | next lighting preset: day, dusk, night |
| C | next camera preset around the selected station: gameplay (about 45 degrees at MMO distance), close, side |
| H | hide or show the panel |

Every action is also a button on the panel at the bottom of the Game View. The presets come from
`VfxLightingPreset.Get` and `VfxCameraPreset.Get`, the same data and code the `capture` verb uses, so a
frame of the Showcase and a capture contact sheet with the same preset names match.

## Third-party notice

The test character models and their idle animation in `Characters/` are third-party assets by
Quaternius. Their source, author and licence are in `Characters/LICENSE.md` and in `THIRD-PARTY.md`
at the root of the package. Everything else in this sample (scene, scripts, materials, library) is
part of the package and under its MIT licence; no Unity sample asset is copied.

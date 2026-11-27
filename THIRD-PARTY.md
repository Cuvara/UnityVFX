# Third-party material

This repository is MIT licensed (`LICENSE`), except for the third-party files listed here. Each
section names the files, where they come from, their licence and what was changed. Assets of
Unity's own samples are never copied into this repository (Unity Companion License).

## Showcase test characters

Rigged humanoid characters with an idle animation, used as a scale and lighting reference in the
Showcase sample (`Samples~/Showcase/Characters/`). The notice that travels with the sample is
`Samples~/Showcase/Characters/LICENSE.md`.

### Universal Base Characters (Standard)

| | |
|---|---|
| Author | Quaternius (https://quaternius.com) |
| Source | https://quaternius.com/packs/universalbasecharacters.html; the download is hosted at https://quaternius.itch.io/universal-base-characters (free "Standard" tier, upload `Universal Base Characters[Standard].zip`) |
| Licence | CC0 1.0 Universal, https://creativecommons.org/publicdomain/zero/1.0/ |
| Downloaded | 2026-11-27 |
| Archive | `Universal Base Characters[Standard].zip`, 128968391 bytes, SHA-256 `fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40` |

Licence statement in the archive, `Universal Base Characters[Standard]/License_Standard.txt`
(SHA-256 `0f4beaf0fe360a7732e58bbe3dbf60a2422367fbea60cb9ea4add968f383268e`), verbatim:

```
This is the standard FREE version of the Universal Base Characters Kit, which only contains 
a portion of the models . You can buy the SOURCE version which has all the models with rigged .blends,
and Unity(URP), Unreal Engine and Godot projects
with custom shaders already set up.

You can get the other version from the website https://quaternius.com

-------------------------------------------------------
License:
CC0 1.0 Universal (CC0 1.0) 
Public Domain Dedication
https://creativecommons.org/publicdomain/zero/1.0/

------------------------------------------------------
Models by @Quaternius
Consider supporting me on Patreon!

https://www.patreon.com/quaternius

-------------------------------------------------------
Join the Discord Server:
https://discord.gg/vJqnRUYRfT
```

Licence statement on the download pages on 2026-11-27: quaternius.com, "License: CC0" (linked to
https://creativecommons.org/publicdomain/zero/1.0/) and "Free to use in personal, educational and
commercial projects. (CC0 License)"; itch.io, "Asset license: Creative Commons Zero v1.0 Universal"
and "2 base models and 5 hairstyles completely free. Can be used in personal, educational and
commercial projects, CC0".

Files taken:

| File in the archive | In this repository | SHA-256 |
|---|---|---|
| `Base Characters/Unity/Superhero_Male_FullBody.fbx` | `Samples~/Showcase/Characters/Superhero_Male_FullBody.fbx` | `79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9` |
| `Base Characters/Unity/Superhero_Female_FullBody.fbx` | `Samples~/Showcase/Characters/Superhero_Female_FullBody.fbx` | `0727e7b236eeea4115531e07aeb2bb7690c1a58155f743bbf54282944fb97ea9` |

Changes: the FBX files are byte-for-byte copies. They are imported as Humanoid (avatar created from
the model), without their embedded materials and textures; the prefabs
`VfxTestCharacterMale.prefab` and `VfxTestCharacterFemale.prefab` replace the materials with the
neutral URP Lit materials `VfxTestCharacter_Body.mat` and `VfxTestCharacter_Detail.mat` (made for
this package), turn the model 180 degrees so it faces +Z, and add `VfxSocket` objects. No texture,
hairstyle or glTF file of the pack is included.

### Universal Animation Library (Standard)

| | |
|---|---|
| Author | Quaternius (https://quaternius.com) |
| Source | https://quaternius.com/packs/universalanimationlibrary.html; the download is hosted at https://quaternius.itch.io/universal-animation-library (free "Standard" tier, upload `Universal Animation Library[Standard].zip`) |
| Licence | CC0 1.0 Universal, https://creativecommons.org/publicdomain/zero/1.0/ |
| Downloaded | 2026-11-27 |
| Archive | `Universal Animation Library[Standard].zip`, 15904933 bytes, SHA-256 `cc73fc4e495b82958207316596317a3f40b9fa38065bde1027937452da537724` |

Licence statement in the archive, `Universal Animation Library[Standard]/License.txt`
(SHA-256 `6d01f55c6e4c49a2c9963e147e561945ae2c83958c8ca667d90a6bffdbfac061`), verbatim:

```
-------------------------------------------------------
License:
CC0 1.0 Universal (CC0 1.0) 
Public Domain Dedication
https://creativecommons.org/publicdomain/zero/1.0/

------------------------------------------------------
Models by @Quaternius
Consider supporting me on Patreon!

https://www.patreon.com/quaternius
```

Licence statement on the download page on 2026-11-27: quaternius.com, "License: CC0" (linked to
https://creativecommons.org/publicdomain/zero/1.0/) and "Free to use in personal, educational and
commercial projects. (CC0 License)".

Files taken:

| From the archive | In this repository |
|---|---|
| clip `Armature\|Idle_Loop` (0 to 2.5 s, 30 frames per second) of `Unity/UAL1_Standard.fbx` (SHA-256 `21b32d912da3cb93426d974fb945e86f5b2e86970acd2ce89905e0fbf9f1dcc2`) | `Samples~/Showcase/Characters/VfxTestCharacter_Idle.anim` |

Changes: the FBX itself is not in the repository (23.7 MB, 43 clips). It was imported as Humanoid in
a scratch folder of the Sandbox; Unity saved a copy of the one clip as the standalone Humanoid clip
`VfxTestCharacter_Idle.anim` (renamed, loop time on, root rotation and position baked into the pose).
`VfxTestCharacter.controller` (made for this package) plays it as its default state.

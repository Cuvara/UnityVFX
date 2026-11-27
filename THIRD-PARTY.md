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

### Licence note

As observed on 2026-11-27, the site-wide page https://quaternius.com/license.html shows "Quaternius
Asset License (QAL) v1.0" and "Last updated: 8/28/2026". It "governs your use of the digital content
(the "Assets") released by Quaternius under this License", and its section 3 a) reads "Resell or
redistribute the Assets themselves. You may not extract, repackage, sublicense, sell, or otherwise
redistribute the Assets (in original or modified form) as a standalone asset, asset pack, stock
file, template, or similar product, whether for free or for payment, and whether alone or bundled
with other assets." The two packs taken here state CC0 1.0 in the licence files inside their
archives and on their pack pages (quoted above), and CC0 1.0 is the licence this repository relies
on.

**Owner decision before public release.** Whether that reading holds, and so whether the two
Quaternius packs may stay in this repository, is open. The facts: the licence files in the archives and
the pack pages say CC0 1.0; the site-wide page says QAL v1.0 for assets "released by Quaternius under
this License". Nobody has made the legal reading. Nothing is removed now. The characters are cheap to
drop later: the placeholder `VfxMannequin` (`Runtime/Showcase/VfxMannequin.cs`) stands in when
`Samples~/Showcase/Characters` has no model, and removing that folder removes every Quaternius file.

## Unity-MCP NuGet DLLs in the Sandbox project

`Sandbox~/Assets/Plugins/NuGet/` holds 45 third-party .NET DLLs. They are the restore output of the
Unity-MCP plugin, which the Sandbox project uses to let an agent drive the editor; the list with
versions is `Sandbox~/Assets/Plugins/NuGet/.nuget-installed.json`. They are committed because the
plugin does not restore them on its own on a first open (`Documentation~/AUTHORING.md`, "The
committed NuGet DLLs"). They live only in the Sandbox project: the package does not ship them.
Unity does not import a folder whose name ends in `~` from a package, and no code of the package
references them. They are still files in this Git repository, so this repository redistributes
them, and this section is their notice.

**Owner decision before public release.** The three `Rql.*.dll` files (`com.IvanMurzak.Rql` 0.3.0) are
under the PolyForm Shield License 1.0.0, which is not an open source licence and has a "Noncompete"
section (see "Licences" below for the wording). Nobody has judged how far that clause reaches for a
repository that redistributes the DLLs. Nothing is removed now; the whole set can leave the
repository (see "What each install route downloads" next).

**What each install route downloads.**

| Route | Carries `Sandbox~/` and its 45 DLLs? |
|---|---|
| Git URL install (`https://github.com/<owner>/UnityVFX.git`, UPM clones the repository) | Yes: the whole repository is downloaded, `Sandbox~/` included (about 17.8 MiB of DLLs). Unity imports no folder whose name ends in `~`, so the DLLs are on disk in the package cache but are never imported or compiled. |
| npm or OpenUPM tarball (`npm pack`, `npm publish`) | No: `package.json` has `"files": ["*", "!Sandbox~"]`, which leaves `Sandbox~/` out. The tarball keeps `Samples~/`, `Documentation~/`, `Schemas~/`, `Tools~/`, `Library/`, `Effects/`, `Runtime/`, `Editor/`, `Tests/` and the root files. |
| Clone of the repository | Yes: the DLLs are committed files. |

The DLLs can leave the repository without breaking a fresh clone: `python Tools~/setup-sandbox.py
--restore-nuget` brings them back through the Unity-MCP plugin's own restore (also done by `--open` when
the folder is missing or incomplete against `.nuget-installed.json`). Steps and the measured run:
`Documentation~/AUTHORING.md`, "The committed NuGet DLLs". To remove them, delete
`Sandbox~/Assets/Plugins/NuGet` and `Sandbox~/Assets/Plugins/NuGet.meta` and the DLL section of this
file in one commit; a Git URL install then no longer downloads them either (history keeps them; a
history rewrite is a separate owner decision).

The plugin that needs them:

| Package | Version | Source | Copyright holder | Licence (SPDX) | Licence URL | Project | In this repository |
|---|---|---|---|---|---|---|---|
| com.ivanmurzak.unity.mcp ("AI Game Developer — MCP") | 1.7.0 | OpenUPM, https://openupm.com/packages/com.ivanmurzak.unity.mcp/ (tarball SHA-1 `1e9d277ddccd6bc1bf2b4a807f73113441b7f852`, repository revision `64dbb35f9447d84942eb7f1b828fee48d1636eef`) | Ivan Murzak | Apache-2.0 | https://github.com/IvanMurzak/Unity-MCP/blob/64dbb35f9447d84942eb7f1b828fee48d1636eef/LICENSE | https://github.com/IvanMurzak/Unity-MCP | referenced by `Sandbox~/Packages/manifest.json`; not committed (Unity downloads it into the Sandbox's `Library/`) |

The DLLs, one row per file. "Version" is the NuGet package version from `.nuget-installed.json`.
"Version in the DLL" is the DLL's product (informational) version without its commit suffix, and
its assembly version. "Identical to" is the folder inside the `.nupkg` of that exact version on
nuget.org whose file has the same SHA-256 as the committed DLL (checked for all 45 on 2026-11-27).
"Project" is the source repository the package names; for the four `dotnet/corefx` packages, which
name none, the repository in the DLL's build information; for `com.IvanMurzak.Rql`, whose `.nuspec`
names only https://ai-game.dev/, the repository that holds the same `LICENSE.md` at the package's
commit `c254c320050eb2a6e9e493e354b2e7e637678a49`.

| DLL | NuGet package | Version | Version in the DLL | Identical to | Copyright holder | Licence (SPDX) | Licence URL | Project |
|---|---|---|---|---|---|---|---|---|
| `McpPlugin.dll` | com.IvanMurzak.McpPlugin | 8.19.0 | 8.19.0 (assembly 8.19.0.0) | `lib/netstandard2.1` | Ivan Murzak | Apache-2.0 | https://www.nuget.org/packages/com.IvanMurzak.McpPlugin/8.19.0/License | https://github.com/IvanMurzak/MCP-Plugin-dotnet |
| `McpPlugin.Common.dll` | com.IvanMurzak.McpPlugin.Common | 8.19.0 | 8.19.0 (assembly 8.19.0.0) | `lib/netstandard2.1` | Ivan Murzak | Apache-2.0 | https://www.nuget.org/packages/com.IvanMurzak.McpPlugin.Common/8.19.0/License | https://github.com/IvanMurzak/MCP-Plugin-dotnet |
| `ReflectorNet.dll` | com.IvanMurzak.ReflectorNet | 5.5.1 | 5.5.1 (assembly 5.5.1.0) | `lib/netstandard2.1` | Ivan Murzak | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | https://github.com/IvanMurzak/ReflectorNet |
| `Rql.Contracts.dll` | com.IvanMurzak.Rql | 0.3.0 | 0.3.0 (assembly 0.3.0.0) | `lib/netstandard2.1` | Ivan Murzak | LicenseRef-PolyForm-Shield-1.0.0: **owner decision before public release** | https://www.nuget.org/packages/com.IvanMurzak.Rql/0.3.0/License | https://github.com/IvanMurzak/RQL |
| `Rql.Execution.dll` | com.IvanMurzak.Rql | 0.3.0 | 0.3.0 (assembly 0.3.0.0) | `lib/netstandard2.1` | Ivan Murzak | LicenseRef-PolyForm-Shield-1.0.0: **owner decision before public release** | https://www.nuget.org/packages/com.IvanMurzak.Rql/0.3.0/License | https://github.com/IvanMurzak/RQL |
| `Rql.Parser.dll` | com.IvanMurzak.Rql | 0.3.0 | 0.3.0 (assembly 0.3.0.0) | `lib/netstandard2.1` | Ivan Murzak | LicenseRef-PolyForm-Shield-1.0.0: **owner decision before public release** | https://www.nuget.org/packages/com.IvanMurzak.Rql/0.3.0/License | https://github.com/IvanMurzak/RQL |
| `Microsoft.AspNetCore.Connections.Abstractions.dll` | Microsoft.AspNetCore.Connections.Abstractions | 8.0.33 | 8.0.33 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.Http.Connections.Client.dll` | Microsoft.AspNetCore.Http.Connections.Client | 8.0.33 | 8.0.33 (assembly 8.0.33.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.Http.Connections.Common.dll` | Microsoft.AspNetCore.Http.Connections.Common | 8.0.33 | 8.0.33 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.SignalR.Client.dll` | Microsoft.AspNetCore.SignalR.Client | 8.0.33 | 8.0.33 (assembly 8.0.33.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.SignalR.Client.Core.dll` | Microsoft.AspNetCore.SignalR.Client.Core | 8.0.33 | 8.0.33 (assembly 8.0.33.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.SignalR.Common.dll` | Microsoft.AspNetCore.SignalR.Common | 8.0.33 | 8.0.33 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.AspNetCore.SignalR.Protocols.Json.dll` | Microsoft.AspNetCore.SignalR.Protocols.Json | 8.0.33 | 8.0.33 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.Bcl.AsyncInterfaces.dll` | Microsoft.Bcl.AsyncInterfaces | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Bcl.Memory.dll` | Microsoft.Bcl.Memory | 10.0.14 | 10.0.14 (assembly 10.0.0.14) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/dotnet |
| `Microsoft.Bcl.TimeProvider.dll` | Microsoft.Bcl.TimeProvider | 8.0.1 | 8.0.1 (assembly 8.0.0.1) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.CodeAnalysis.dll` | Microsoft.CodeAnalysis.Common | 4.8.0 | 4.8.0-7.23558.1 (assembly 4.8.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/roslyn |
| `Microsoft.CodeAnalysis.CSharp.dll` | Microsoft.CodeAnalysis.CSharp | 4.8.0 | 4.8.0-7.23558.1 (assembly 4.8.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/roslyn |
| `Microsoft.Extensions.Caching.Abstractions.dll` | Microsoft.Extensions.Caching.Abstractions | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Configuration.Abstractions.dll` | Microsoft.Extensions.Configuration.Abstractions | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.DependencyInjection.dll` | Microsoft.Extensions.DependencyInjection | 8.0.1 | 8.0.10 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.DependencyInjection.Abstractions.dll` | Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | 8.0.10 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Diagnostics.Abstractions.dll` | Microsoft.Extensions.Diagnostics.Abstractions | 8.0.1 | 8.0.10 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Features.dll` | Microsoft.Extensions.Features | 8.0.33 | 8.0.33 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/aspnetcore |
| `Microsoft.Extensions.FileProviders.Abstractions.dll` | Microsoft.Extensions.FileProviders.Abstractions | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Hosting.Abstractions.dll` | Microsoft.Extensions.Hosting.Abstractions | 8.0.1 | 8.0.10 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Logging.dll` | Microsoft.Extensions.Logging | 8.0.1 | 8.0.10 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Logging.Abstractions.dll` | Microsoft.Extensions.Logging.Abstractions | 8.0.3 | 8.0.13 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Options.dll` | Microsoft.Extensions.Options | 8.0.2 | 8.0.2 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `Microsoft.Extensions.Primitives.dll` | Microsoft.Extensions.Primitives | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `R3.dll` | R3 | 1.3.0 | 1.3.0 (assembly 1.3.0.0) | `lib/netstandard2.1` | Cysharp, Inc. | MIT | https://licenses.nuget.org/MIT | https://github.com/Cysharp/R3 |
| `System.Buffers.dll` | System.Buffers | 4.5.1 | 4.6.28619.01 (assembly 4.0.3.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | https://github.com/dotnet/corefx |
| `System.Collections.Immutable.dll` | System.Collections.Immutable | 7.0.0 | 7.0.0 (assembly 7.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.ComponentModel.Annotations.dll` | System.ComponentModel.Annotations | 5.0.0 | 5.0.0 (assembly 5.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Diagnostics.DiagnosticSource.dll` | System.Diagnostics.DiagnosticSource | 8.0.1 | 8.0.4 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.IO.Pipelines.dll` | System.IO.Pipelines | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Memory.dll` | System.Memory | 4.5.5 | 4.6.31308.01 (assembly 4.0.1.2) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | https://github.com/dotnet/corefx |
| `System.Numerics.Vectors.dll` | System.Numerics.Vectors | 4.4.0 | 4.6.25519.03 (assembly 4.1.3.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | https://github.com/dotnet/corefx |
| `System.Reflection.Metadata.dll` | System.Reflection.Metadata | 7.0.0 | 7.0.0 (assembly 7.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Runtime.CompilerServices.Unsafe.dll` | System.Runtime.CompilerServices.Unsafe | 6.1.2 | 6.1.2 (assembly 6.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/maintenance-packages |
| `System.Text.Encoding.CodePages.dll` | System.Text.Encoding.CodePages | 7.0.0 | 7.0.0 (assembly 7.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Text.Encodings.Web.dll` | System.Text.Encodings.Web | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Text.Json.dll` | System.Text.Json | 8.0.6 | 8.0.18 (assembly 8.0.0.0) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Threading.Channels.dll` | System.Threading.Channels | 8.0.0 | 8.0.0 (assembly 8.0.0.0) | `lib/netstandard2.1` | Microsoft Corporation | MIT | https://licenses.nuget.org/MIT | https://github.com/dotnet/runtime |
| `System.Threading.Tasks.Extensions.dll` | System.Threading.Tasks.Extensions | 4.5.4 | 4.6.28619.01 (assembly 4.2.0.1) | `lib/netstandard2.0` | Microsoft Corporation | MIT | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | https://github.com/dotnet/corefx |

`.nuget-installed.json` also lists `Microsoft.CodeAnalysis.Analyzers` 3.3.4 (MIT,
https://licenses.nuget.org/MIT) with no DLL: it is a build-time dependency of Roslyn and no file of
it is committed.

Versions. Every package version in the table equals the one in `.nuget-installed.json`, and every
DLL is byte-identical to the file in that package version. Where the version inside the DLL
differs, the difference comes from how Microsoft numbers its builds, not from another package:

- `Microsoft.Extensions.DependencyInjection` 8.0.1, `Microsoft.Extensions.DependencyInjection.Abstractions`
  8.0.2, `Microsoft.Extensions.Diagnostics.Abstractions` 8.0.1, `Microsoft.Extensions.Hosting.Abstractions`
  8.0.1 and `Microsoft.Extensions.Logging` 8.0.1 carry product version 8.0.10;
  `Microsoft.Extensions.Logging.Abstractions` 8.0.3 carries 8.0.13; `System.Diagnostics.DiagnosticSource`
  8.0.1 carries 8.0.4; `System.Text.Json` 8.0.6 carries 8.0.18. These package versions were built in
  a later .NET 8 servicing release, and the product version names that release.
- `Microsoft.CodeAnalysis.Common` and `Microsoft.CodeAnalysis.CSharp` 4.8.0 carry product version
  `4.8.0-7.23558.1`, the build label of the 4.8.0 release.
- `System.Buffers` 4.5.1, `System.Memory` 4.5.5, `System.Numerics.Vectors` 4.4.0 and
  `System.Threading.Tasks.Extensions` 4.5.4 are `dotnet/corefx` builds: their product version is a
  corefx build number (4.6.x), and their assembly versions are 4.0.3.0, 4.0.1.2, 4.1.3.0 and 4.2.0.1.
- The assembly version of most Microsoft DLLs stays fixed within a major version (8.0.0.0, 6.0.0.0
  and so on) and differs from the package version by design.

Licences, verified on 2026-11-27 against the nuget.org page of each exact version
(`https://www.nuget.org/packages/<id>/<version>`) and the `.nuspec` and `.nupkg` of that version,
and, for the packages that carry no licence file, against their repository at the package's commit:

- MIT, 39 DLLs: every `Microsoft.*` and `System.*` row and `R3`. For the four `dotnet/corefx` rows the
  package gives a licence URL instead of an expression; the `LICENSE.TXT` inside each of those
  packages is the MIT text below. `R3` 1.3.0 carries no licence file; the `LICENSE` of its repository
  at the package's commit `329ca7915713417f2e0837b0e0a80b4da074db4a` is MIT, "Copyright (c) 2024
  Cysharp, Inc.". `System.Runtime.CompilerServices.Unsafe` 6.1.2 carries no licence file; the
  `LICENSE` of https://github.com/dotnet/maintenance-packages at the package's commit
  `f62ca0009b038cab4725a720f386623a969d73ad` is the MIT text below.
- Apache-2.0, 3 DLLs: `McpPlugin.dll` and `McpPlugin.Common.dll` (licence file `LICENSE` in the
  package: the Apache License 2.0 with "Copyright 2025 Ivan Murzak" in its appendix, the same file
  as the repository's `LICENSE` at commit `38c79c31b8e569fc46b2e15b0ba133af9cd2b6fc`) and
  `ReflectorNet.dll` (licence expression `Apache-2.0`). ReflectorNet has a `NOTICE` file at the
  package's commit `141aa277be654e7d110bf3238c677121f52d8373`; MCP-Plugin-dotnet has none.
- PolyForm Shield License 1.0.0, 3 DLLs: `Rql.Contracts.dll`, `Rql.Execution.dll` and
  `Rql.Parser.dll` (licence file `LICENSE.md` in the package, identical to the repository's
  `LICENSE.md` at the package's commit). It is not on the SPDX licence list, hence the `LicenseRef-`
  id. It is not an open source licence: it permits use, changes and distribution with its notices
  for "any permitted purpose", and its "Noncompete" section reads "Any purpose is a permitted
  purpose, except for providing any product that competes with the software or any product the
  licensor or any of its affiliates provides using the software." The package's `PACKAGE.md` sums it
  up as "free to use, including in commercial products, except to provide a product that competes
  with the licensor's products".

Where the notices are satisfied. MIT and Apache-2.0 require the copyright and licence notice with
each copy, Apache-2.0 also the attribution notices of a `NOTICE` file, and PolyForm Shield a copy of
its terms or their URL plus every line that starts with `Required Notice:`. This file is that notice
for the DLLs: the tables above give the holder and the licence URL of each DLL, and the full text of
each distinct licence, the ReflectorNet `NOTICE` and the RQL required notice follow once below. The
Microsoft packages also carry a notice file for code they include from other projects
(`THIRD-PARTY-NOTICES.TXT`; `ThirdPartyNotices.rtf` in the two `Microsoft.CodeAnalysis` packages;
none in `System.Runtime.CompilerServices.Unsafe` 6.1.2). Those files are not copied here; each is in
the `.nupkg` of the listed version at
`https://api.nuget.org/v3-flatcontainer/<id in lower case>/<version>/<id in lower case>.<version>.nupkg`.

Copyright notices of the MIT-licensed DLLs:

- `Microsoft.*` and `System.*`: "Copyright (c) .NET Foundation and Contributors" (licence text);
  the DLLs and packages state "© Microsoft Corporation. All rights reserved."
- `R3`: "Copyright (c) 2024 Cysharp, Inc."

### MIT License

The `LICENSE.TXT` of the Microsoft packages, verbatim (identical in every package that carries
one). The R3 licence has the same terms under the title "MIT License" with the copyright line
"Copyright (c) 2024 Cysharp, Inc." and no "All rights reserved." line.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache License 2.0

The text published at https://www.apache.org/licenses/LICENSE-2.0.txt, verbatim. The licence files
of MCP-Plugin-dotnet and ReflectorNet are this text with "Copyright 2025 Ivan Murzak" in the
appendix.

```
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright [yyyy] [name of copyright owner]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
```

### ReflectorNet NOTICE

The `NOTICE` file of https://github.com/IvanMurzak/ReflectorNet at commit
`141aa277be654e7d110bf3238c677121f52d8373`, verbatim:

```
# ReflectorNet NOTICE

Copyright 2024-2025 Ivan Murzak

This product includes software developed by Ivan Murzak (<https://github.com/IvanMurzak>).

Licensed under the Apache License, Version 2.0 (the "License"). You may obtain a copy of the License at
<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
```

### PolyForm Shield License 1.0.0

The `LICENSE.md` of `com.IvanMurzak.Rql` 0.3.0, verbatim. Its last line is the licensor's required
notice.

```
# PolyForm Shield License 1.0.0

<https://polyformproject.org/licenses/shield/1.0.0>

## Acceptance

In order to get any license under these terms, you must agree to them as both strict obligations and conditions to all your licenses.

## Copyright License

The licensor grants you a copyright license for the software to do everything you might do with the software that would otherwise infringe the licensor's copyright in it for any permitted purpose.  However, you may only distribute the software according to [Distribution License](#distribution-license) and make changes or new works based on the software according to [Changes and New Works License](#changes-and-new-works-license).

## Distribution License

The licensor grants you an additional copyright license to distribute copies of the software.  Your license to distribute covers distributing the software with changes and new works permitted by [Changes and New Works License](#changes-and-new-works-license).

## Notices

You must ensure that anyone who gets a copy of any part of the software from you also gets a copy of these terms or the URL for them above, as well as copies of any plain-text lines beginning with `Required Notice:` that the licensor provided with the software.  For example:

> Required Notice: Copyright Yoyodyne, Inc. (http://example.com)

## Changes and New Works License

The licensor grants you an additional copyright license to make changes and new works based on the software for any permitted purpose.

## Patent License

The licensor grants you a patent license for the software that covers patent claims the licensor can license, or becomes able to license, that you would infringe by using the software.

## Noncompete

Any purpose is a permitted purpose, except for providing any product that competes with the software or any product the licensor or any of its affiliates provides using the software.

## Competition

Goods and services compete even when they provide functionality through different kinds of interfaces or for different technical platforms.  Applications can compete with services, libraries with plugins, frameworks with development tools, and so on, even if they're written in different programming languages or for different computer architectures.  Goods and services compete even when provided free of charge.  If you market a product as a practical substitute for the software or another product, it definitely competes.

## New Products

If you are using the software to provide a product that does not compete, but the licensor or any of its affiliates brings your product into competition by providing a new version of the software or another product using the software, you may continue using versions of the software available under these terms beforehand to provide your competing product, but not any later versions.

## Discontinued Products

You may begin using the software to compete with a product or service that the licensor or any of its affiliates has stopped providing, unless the licensor includes a plain-text line beginning with `Licensor Line of Business:` with the software that mentions that line of business.  For example:

> Licensor Line of Business: YoyodyneCMS Content Management System (http://example.com/cms)

## Sales of Business

If the licensor or any of its affiliates sells a line of business developing the software or using the software to provide a product, the buyer can also enforce [Noncompete](#noncompete) for that product.

## Fair Use

You may have "fair use" rights for the software under the law. These terms do not limit them.

## No Other Rights

These terms do not allow you to sublicense or transfer any of your licenses to anyone else, or prevent the licensor from granting licenses to anyone else.  These terms do not imply any other licenses.

## Patent Defense

If you make any written claim that the software infringes or contributes to infringement of any patent, your patent license for the software granted under these terms ends immediately. If your company makes such a claim, your patent license ends immediately for work on behalf of your company.

## Violations

The first time you are notified in writing that you have violated any of these terms, or done anything with the software not covered by your licenses, your licenses can nonetheless continue if you come into full compliance with these terms, and take practical steps to correct past violations, within 32 days of receiving notice.  Otherwise, all your licenses end immediately.

## No Liability

***As far as the law allows, the software comes as is, without any warranty or condition, and the licensor will not be liable to you for any damages arising out of these terms or the use or nature of the software, under any kind of legal claim.***

## Definitions

The **licensor** is the individual or entity offering these terms, and the **software** is the software the licensor makes available under these terms.

A **product** can be a good or service, or a combination of them.

**You** refers to the individual or entity agreeing to these terms.

**Your company** is any legal entity, sole proprietorship, or other kind of organization that you work for, plus all its affiliates.

**Affiliates** means the other organizations than an organization has control over, is under the control of, or is under common control with.

**Control** means ownership of substantially all the assets of an entity, or the power to direct its management and policies by vote, contract, or otherwise.  Control can be direct or indirect.

**Your licenses** are all the licenses granted to you for the software under these terms.

**Use** means anything you do with the software requiring one of your licenses.

Required Notice: Copyright (c) 2026 Ivan Murzak (https://github.com/IvanMurzak)
```

# Reference Assemblies Layout

**Path:**  
`CastleForge/ReferenceAssemblies/`

This directory holds shared external dependencies, build tooling, and supporting installers used across the CastleForge solution  
(game binaries, XNA, ImGui, Harmony, media libraries, packet tooling, BCL/runtime support, and content-pipeline components).

They are grouped by **responsibility** rather than by project.

Projects should reference assemblies from here instead of keeping their own random copies where possible.

---

## Folder Overview

### `Core/`

> Game + engine + XNA runtime

Assemblies that represent the **base game and primary runtime dependencies**:

- `CastleMinerZ.exe`
- `DNA.*.dll` (e.g. `DNA.Common.dll`, `DNA.Steam.dll`)
- `Microsoft.Xna.Framework*.dll`
  - `Microsoft.Xna.Framework.dll`
  - `Microsoft.Xna.Framework.Game.dll`
  - `Microsoft.Xna.Framework.Graphics.dll`
  - `Microsoft.Xna.Framework.Xact.dll`

These are the core runtime binaries that define the game’s types and the XNA runtime CastleForge patches and extends.

---

### `Media/`

> Imaging, model, and media processing stack

Assemblies related to **media handling and content/model processing**:

- `SixLabors.ImageSharp.dll`
- `SixLabors.ImageSharp.Drawing.dll`
- `SixLabors.Fonts.dll`
- `NLayer.dll`
- `SharpGLTF.Core.dll`
- `SharpGLTF.Runtime.dll`
- `SharpGLTF.Toolkit.dll`

Used mainly by mods and tools that need to load, manipulate, render, or convert external media/model assets.

---

### `Networking/`

> Packet capture, packet analysis, and capture prerequisites

Assemblies and related support files used for **network capture and packet decoding**:

- `PacketDotNet.dll`
- `SharpPcap.dll`
- `npcap-1.87.exe`

Used by mods like **NetworkSniffer** and any tooling that inspects CastleMinerZ network traffic.  
`npcap-1.87.exe` is included as a supporting installer dependency, not as a project reference assembly.

---

### `Patching/`

> Runtime patching framework

Assemblies used to **patch and hook** the game at runtime:

- `0Harmony.dll`

All mods that apply Harmony patches should conceptually trace back here, even if they embed their own copy of Harmony for distribution.

---

### `RuntimeSupport/`

> BCL / runtime support libraries not guaranteed on the target machine

Assemblies that extend or backfill .NET runtime functionality, usually for performance, compatibility, or dependency support:

- `Microsoft.Bcl.AsyncInterfaces.dll`
- `System.IO.Pipelines.dll`
- `System.Memory.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`
- `System.Text.Encoding.CodePages.dll`
- `System.Text.Encodings.Web.dll`
- `System.Text.Json.dll`
- `System.Threading.Tasks.Extensions.dll`

These are shared support libraries used by other dependencies  
(e.g. SixLabors, SharpGLTF, ImGui, JSON-based tooling, etc.) to ensure consistent runtime behavior.

---

### `UI/`

> ImGui / overlay UI stack

Assemblies used by **in-game overlays** and related UI layers:

- `ImGui.NET.dll`
- `cimgui.dll`

Primarily consumed by mods that render ImGui-based overlays, such as **CastleWallsMk2**.

---

### `XnaGameStudio/`

> XNA content pipeline and build-time tooling

Assemblies and support files used for **content import/build workflows**, rather than normal in-game runtime patching:

- `Microsoft.Build.Framework.dll`
- `Microsoft.Build.Utilities.v4.0.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.AudioImporters.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.EffectImporter.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.FBXImporter.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.TextureImporter.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.VideoImporters.dll`
- `Microsoft.Xna.Framework.Content.Pipeline.XImporter.dll`
- `Microsoft.Xna.Framework.dll`
- `XNA Game Studio Shared.msi`

This folder is mainly for tools and pipelines that build or transform content assets  
(for example model, texture, audio, or FBX/XNB workflows).

---

## Usage Guidelines

- **Single Source of Truth**  
  All CastleForge projects should reference third-party and game dependencies from this folder tree where practical.  
  Avoid copying DLLs into individual project folders unless they are intentionally embedded for runtime loading.

- **Adding New Dependencies**
  1. Choose the folder that best matches the dependency’s role:
     - `Core`
     - `Media`
     - `Networking`
     - `Patching`
     - `RuntimeSupport`
     - `UI`
     - `XnaGameStudio`
  2. Drop the file there.
  3. Update the relevant `.csproj` with the correct `HintPath` (or equivalent reference).

- **Runtime vs Tooling**
  - `Core` contains normal game/runtime references.
  - `XnaGameStudio` contains build/content-pipeline tooling and related support files.
  - Installers such as `npcap-1.87.exe` and `XNA Game Studio Shared.msi` are supporting assets, not normal assembly references.

- **Versioning**
  - When upgrading any library, update **all projects** that reference it.
  - Keep embedded copies (for example in `Embedded/` subfolders of mods) in sync with the versions stored here, or clearly document intentional mismatches.

This structure is meant to keep shared dependencies **discoverable**, **documented**, and **consistent** across the CastleForge solution.
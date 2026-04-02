# Reference Assemblies Layout

**Path:**  
`CMZModSuite/ReferenceAssemblies/`

This directory holds all shared, external assemblies used by the CMZModSuite solution (game binaries, XNA, ImGui, Harmony, media libraries, BCL extras, etc.).  
They are grouped by **responsibility** rather than by project.

Projects should reference assemblies from here instead of keeping their own random copies where possible.

---

## Folder Overview

### `Core/`

> Game + engine + XNA runtime

Assemblies that represent the **base game and primary engine dependencies**:

- `CastleMinerZ.exe`
- `DNA.*.dll` (e.g. `DNA.Common.dll`, `DNA.Steam.dll`)
- `Microsoft.Xna.Framework*.dll`  
  (core XNA, graphics, game, XACT, etc.)

These are the "must-have" binaries that define the game's types and the XNA runtime CMZModSuite patches and extends.

---

### `Media/`

> Imaging & media processing stack

Assemblies related to **media handling** (primarily images, but may include audio helpers used by the same pipeline):

- `SixLabors.ImageSharp.dll`
- `SixLabors.ImageSharp.Drawing.dll`
- `SixLabors.Fonts.dll`
- `NLayer.dll` (audio decoding used in some texture/media flows)

Used mainly by the **TexturePacks** and any tooling that needs to load, manipulate, or render external media assets.

---

### `Networking/`

> Packet capture & analysis

Assemblies that provide **network capture and packet decoding**:

- `PacketDotNet.dll`
- `SharpPcap.dll`

Used by mods like **NetworkSniffer** and any tooling that hooks into CastleMinerZ's network traffic.

---

### `Patching/`

> Runtime patching framework

Assemblies used to **patch and hook** the game at runtime:

- `0Harmony.dll` (Harmony patching library)

All mods that apply Harmony patches should conceptually trace back here, even if they embed their own copy of Harmony for distribution.

---

### `RuntimeSupport/`

> BCL / runtime support libraries not guaranteed on the target machine

Assemblies that extend or backfill .NET runtime functionality, usually for performance or compatibility:

- `System.Memory.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`
- `System.Text.Encoding.CodePages.dll`

These are shared "support" libraries used by other dependencies (e.g., SixLabors, ImGui, etc.) to ensure consistent behavior on the target runtime.

---

### `UI/`

> ImGui / overlay UI stack

Assemblies used by **in-game overlays** and other UI layers:

- `ImGui.NET.dll`
- `cimgui.dll`

Primarily consumed by the **CastleWallsMk2** overlay and any future mods that render ImGui-based UIs over the game.

---

## Usage Guidelines

- **Single Source of Truth**  
  All CMZModSuite projects (including individual mod DLLs) should reference third-party and game assemblies from this folder tree. Avoid copying DLLs into individual project folders unless they're intentionally embedded for runtime loading.

- **Adding New Assemblies**
  1. Choose the folder that best matches the assembly's role (`Core`, `Media`, `Networking`, `Patching`, `RuntimeSupport`, `UI`).
  2. Drop the DLL there.
  3. Update the relevant `.csproj` with the correct `HintPath` (or equivalent reference) so it points into this tree.

- **Versioning**
  - When upgrading any library, update **all projects** that reference it.
  - Keep embedded copies (in `Embedded/` subfolders of mods) in sync with the versions stored here, or clearly document intentional mismatches.

This structure is meant to keep the shared dependencies **discoverable**, **documented**, and **consistent** across the entire CMZModSuite.

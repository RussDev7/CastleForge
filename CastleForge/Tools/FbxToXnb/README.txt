FbxToXnbXna - ReadMe.txt
========================

What this is
------------
FbxToXnbXna is a small console tool that converts one (or more) .FBX model files into XNA
Content Pipeline .XNB output, using XNA Game Studio 4.0 pipeline reference DLLs.

It is intended for building XNB assets for games/tools that load XNA Model content.

Key features
------------
• Converts FBX -> XNB using the XNA 4.0 Content Pipeline (BuildContent task wrapper).
• Builds each FBX into its own isolated output folder to avoid dependency collisions.
• Supports drag-and-drop conversion and batch conversion.
• Can prompt-install XNA pipeline references if missing (via embedded MSI).

Output layout (important)
-------------------------
Each input FBX is built into a folder named after the FBX file stem:

Example input:
  C:\Models\0051_Pistol_model.fbx

Output:
  C:\Models\0051_Pistol_model\0051_Pistol_model.xnb
  C:\Models\0051_Pistol_model\texture.xnb            (only if a texture is referenced and built)

Why a per-asset folder?
  Many models reference generic names like "texture" which would produce "texture.xnb".
  If you build multiple models into the same directory, those dependencies can overwrite each other.
  Using a dedicated folder per model prevents that.

How textures are handled (sidecar rule)
---------------------------------------
If a PNG with the same base name as the FBX exists next to it, it is treated as a "sidecar" texture:

  <FBXName>.png

Example:
  0051_Pistol_model.fbx
  0051_Pistol_model.png

During the build, the sidecar PNG is copied into the TEMP build folder so the pipeline can find it.
This avoids permanently modifying your source directory while still letting the pipeline resolve
texture references.

Notes:
• If your FBX references a specific texture filename, that exact PNG name must be present in the
  TEMP build folder. This tool copies the sidecar using its original filename.
• The tool may also copy the sidecar as "texture.png" for compatibility with models that reference
  "texture.png".

How to use
----------
Option A: Drag-and-drop
  1) Run FbxToXnbXna.exe
  2) Drag one or more .fbx files into the console window
  3) Press Enter

Option B: Command line
  FbxToXnbXna.exe "C:\path\model.fbx"
  FbxToXnbXna.exe "C:\path\a.fbx" "C:\path\b.fbx"

Option C: Interactive mode (no args)
  If you run the program with no .fbx arguments, it enters interactive mode:

    Drag .fbx file(s) into this window and press Enter.
    Type 'exit' to quit.

  You can paste paths like:
    "C:\path\a.fbx" "C:\path\b.fbx"

Exit codes
----------
0  Success (all builds succeeded)
1  Failure (one or more builds failed)

Logs / troubleshooting
----------------------
If a build fails, the tool prints:
  • "Build failed. Check logfile.txt / builder errors."
  • The first builder error line (if available)

Where is logfile.txt?
  logfile.txt is produced by the pipeline build engine and is typically written in the TEMP
  working directory used for the build, or wherever the builder's log file path is configured.

Tip:
  If something fails, search your TEMP folder for:
    FbxToXnbXna_<guid>
    XNB_Inter_<guid>

Common failure causes
---------------------
1) Missing XNA Game Studio 4.0 pipeline references
   The converter needs the XNA *Content Pipeline* reference DLLs (not just the runtime).
   The usual location is:
     %ProgramFiles(x86)%\Microsoft XNA\XNA Game Studio\v4.0\References\Windows\x86

   If missing, the tool can prompt to install them using:
     XNA Game Studio Shared.msi (embedded)

2) Missing texture file referenced by the FBX
   If the FBX references a texture filename that cannot be found during the pipeline build,
   the build will fail.

   Make sure either:
   • A matching sidecar PNG exists next to the FBX (same base name), OR
   • The FBX's referenced texture filename exists and is available for the build.

3) Blender/FBX material naming differences
   Some exporters embed texture references in a way that expects a specific filename.
   If your model references "MyTexture.png", ensure that exact file name is provided.

System requirements
-------------------
• Windows
• .NET Framework 4.8.1 (or compatible runtime)
• XNA Game Studio 4.0 pipeline reference DLLs (Windows\x86)
  - Can be installed by the embedded MSI prompt if supported.

Credits / License
-----------------
This tool is part of CMZModSuite tooling.
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7

(If you redistribute this tool, keep license headers and comply with GPL terms.)
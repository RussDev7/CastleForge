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
• Calculates model Scale from TexturePacks FbxComp, matching larger round-trip exports.
• Uses ScaledModelProcessor when available so BarrelTip/socket root space, basis scale,
  and Blender/GLB round-trip orientation are corrected separately from visible mesh scale.
• Can remove optional TexturePacks GLB RootNode authoring Location/Quaternion values
  when converting edited FBX files back to XNB.
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
  FbxToXnbXna.exe --fbxComp 10.0 "C:\path\larger_round_trip_model.fbx"
  FbxToXnbXna.exe --scale 0.001 "C:\path\manual_scale_override.fbx"

Option C: Interactive mode (no args)
  If you run the program with no .fbx arguments, it enters interactive mode:

    Drag .fbx file(s) into this window and press Enter.
    Type 'exit' to quit.

  You can paste paths like:
    "C:\path\a.fbx" "C:\path\b.fbx"

Scale behavior
--------------
TexturePacks exports GLB models full-size by default with:
  [Models]
  FbxComp = 10.0

FbxToXnb then calculates the ModelProcessor / SkinedModelProcessor scale:

  Scale = 0.01 / FbxComp

So with FbxComp = 10.0, the converter uses:

  Scale = 0.001

User-friendly option:
  --fbxComp 10.0    Use the TexturePacks export setting and calculate Scale automatically

Override the default when needed:
  --scale 0.001     Explicit model scale
  --noScale         Do not auto-apply calculated scale
  --param Scale=1   Generic processor parameter form; overrides --fbxComp

Authoring transform behavior
----------------------------
TexturePacks can optionally export GLBs with a Blender-friendly RootNode transform:

  [Models]
  AuthoringLocation = 0, 0, 0
  AuthoringRotation = 0, 0, 0

If those values are changed, pass the same values when converting the edited FBX back:

  FbxToXnb.exe --fbxComp 10.0 --authoringLocation "0.64,-1.12,-0.58" --authoringRotation "0,0,0" "C:\path\model.fbx"

Use the values exactly as Blender shows them on the imported RootNode:
  --authoringLocation              Location X,Y,Z
  --authoringRotation              3 values = Euler degrees X,Y,Z
  --authoringRotation              4 values = Quaternion W,X,Y,Z
  --authoringRotationDegrees       Explicit Euler-degree alias
  --authoringRotationQuaternion    Explicit/legacy quaternion form

Advanced:
  --authoringLocationScale 100     Default FBX importer unit conversion; normally leave this alone

TexturePacks rigid mesh rotation cleanup:
  NormalizeRigidMeshRotation = true
  RigidMeshRotation = 180, 0, 0

  This is handled by the GLB extractor. It writes cleaner Blender-visible mesh rotation
  values, moves root-level helpers and secondary root-level mesh nodes with the
  visible mesh for authoring, and creates
  a matching .cmzrigid.ini sidecar with both original and authoring transforms; FbxToXnb applies a world-space restore delta during conversion. The delta includes the exported RootNode authoring transform so root-level meshes and sockets do not drift in hand.
  Keep that sidecar beside the edited FBX. FbxToXnb first tries an exact FBX-name match, then a texture/FBX-reference match, then a single-sidecar fallback. If more than one sidecar is present and none clearly matches, pass --rigidMeshRestoreFile explicitly. FbxToXnb restores those
  rotations before XNA builds the XNB.

Socket correction overrides:
  --param SocketDebugLog=True
                     Write BarrelTip bake details to the pipeline log
  --param SocketBakeToModelRoot=False
                     Disable root-space socket baking for a one-off asset
  --param SocketBasisScale=0.01
                     Manual override only; normal rigid default is 0.01
  --param SocketBasisTransform=BlenderGlbRoundTripForward
                     Manual override only; normal rigid default
  --param SocketTranslationScale=1
                     Manual override only; normal rigid default is 1.0
  --param SocketRotationCorrection=True
  --param SocketRotationCorrectionAxis=Y
  --param SocketRotationCorrectionDegrees=180
                     Optional one-off BarrelTip basis correction for unusual FBX exports

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
This tool is part of CastleForge tooling.
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7

(If you redistribute this tool, keep license headers and comply with GPL terms.)

AnimationClip FBX builds
------------------------
When DNA.SkinnedPipeline is available, FbxToXnb can also build standalone
DNA.Drawing.Animation.AnimationClip XNB files:

  FbxToXnb.exe --processor AnimationClipProcessor --pipelineDir "SkinedModelProcessor" --animName Reload "C:\Authoring\reload.fbx"

Useful flags:
  --animName <name>      Output AnimationClip name
  --sourceClip <name>    Source FBX take name
  --frameRate <fps>      Sample rate, usually 30
  --noReduce             Keep all sampled keys
  --param Name=Value     Generic processor parameter

The resulting XNB can be copied into a WeaponAddons pack, for example:
  WeaponAddons\Packs\Raygun\animations\reload.xnb

and referenced in .clag:
  $ANIM_RELOAD: animations\reload

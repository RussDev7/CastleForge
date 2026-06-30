# FbxToXnb

> Convert one or more `.fbx` models into XNA-ready `.xnb` output with a workflow built for CastleForge creators, drag-and-drop usage, isolated output folders, and optional custom pipeline processors.

![Preview](_Images/Preview.png)

---

## Overview

**FbxToXnb** is a CastleForge content-authoring tool that compiles **FBX model files into XNB assets** using the **XNA Game Studio 4.0 Content Pipeline**.

At its simplest, it gives creators an easy way to take a model like:

```text
MyWeapon.fbx
```

and turn it into something the XNA-era content pipeline can actually load at runtime:

```text
MyWeapon.xnb
```

But the tool goes beyond a one-shot converter.

It is designed to make the conversion workflow friendlier and safer by:

- supporting drag-and-drop,
- supporting interactive console usage,
- supporting batch conversion,
- compiling each model into its own output folder,
- staging texture files into a temporary build root,
- helping detect or install XNA pipeline dependencies,
- and allowing advanced creators to inject a **custom pipeline extension** such as **`DNA.SkinnedPipeline`**.

That makes it one of the most useful pieces in the CastleForge tooling stack for model authors.

---

## Why this tool stands out

### Drag-and-drop friendly
You can drop one or more `.fbx` files directly onto the included batch file and let the tool handle the rest.

### Interactive mode for repeated testing
If you launch the EXE with no FBX arguments, it enters an interactive mode where you can keep feeding it files and options without relaunching the tool every time.

### Isolated output folders per asset
Each FBX compiles into its own folder named after the source asset. That helps prevent common collisions with generic filenames like `texture.xnb`.

### Texture staging without messing up your source folder
The tool stages candidate texture files into a temporary build folder so the XNA pipeline can resolve them, while keeping your original source folder unchanged.

### Supports custom processors
For standard static assets, the default XNA `ModelProcessor` is fine. For skinned or DNA-specific assets, the tool can route the build through a custom processor like **`SkinedModelProcessor`** from **DNA.SkinnedPipeline**.

### Helps with missing XNA pipeline bits
If the required XNA Game Studio pipeline references are not installed, the tool can prompt to install them using an embedded MSI.

---

## What ships with FbxToXnb

This project includes:

- **`FbxToXnb.exe`**
- **`FbxToXnb_Drop_Normal.bat`**
- **`README.txt`**
- embedded **`XNA Game Studio Shared.msi`** for dependency setup
- the internal build wrapper around XNA’s `BuildContent` task
- optional support for custom pipeline DLLs / folders

This makes it both a simple end-user converter and a more advanced authoring utility.

---

## How it fits into CastleForge

Within the CastleForge layout, this belongs under:

```text
CastleForge/
└─ CastleForge/
   └─ Tools/
      └─ FbxToXnb/
         └─ README.md
```

This is not a gameplay mod. It is a **creator tool** that sits in the pipeline between raw art assets and mod-ready compiled content.

```mermaid
flowchart LR
    A[FBX + textures] --> B[FbxToXnb]
    B --> C[Compiled XNB folder]
    C --> D[CastleForge mod or pack]
```

It pairs especially well with:

- **WeaponAddons** for custom model-driven weapon packs,
- **DNA.SkinnedPipeline** for rigged or skinned assets,
- and any CastleForge workflow that needs XNA-compatible compiled content.

---

## Core feature breakdown

### 1) FBX to XNB conversion
The main job of the tool is straightforward:

- accept one or more `.fbx` files,
- build them through the XNA content pipeline,
- emit compiled `.xnb` output.

### 2) One output folder per source asset
Instead of dumping all compiled content into one shared directory, the tool builds each model into its own folder named after the FBX file stem.

That means something like:

```text
C:\Models\0051_Pistol_model.fbx
```

becomes:

```text
C:\Models\0051_Pistol_model\0051_Pistol_model.xnb
```

If the build also produces compiled dependency content, those files stay alongside it inside the same isolated folder.

This is a very practical quality-of-life choice because many models end up producing common dependency names like `texture.xnb`.

### 3) Temporary texture staging
Before the tool invokes the pipeline, it copies likely texture files into a temporary working folder.

That staging logic supports:

- textures next to the FBX,
- textures inside subfolders,
- multiple common texture extensions,
- preserved relative paths,
- and a legacy compatibility alias of `texture.png` when an `<AssetName>.png` sidecar exists.

That improves compatibility with exported FBX material references while keeping your original source folder untouched.

### 4) Interactive mode
If you start the EXE without passing any FBX files, the tool switches into interactive console mode.

That mode lets you:

- paste or drag paths into the console,
- keep options like pipeline directories and processor names loaded between commands,
- and repeatedly test builds without relaunching the program.

### 5) Custom pipeline support
Advanced users can pass one or more custom pipeline DLLs or directories using:

```text
--pipeline
--pipelineDir
```

This is how you hook in extensions such as:

- **DNA.SkinnedPipeline.dll**

That capability is what lets FbxToXnb handle both:

- normal rigid/static models, and
- custom CastleForge processing paths for skinned models.

### 6) BuildContent wrapper with logging
Internally, the tool wraps the XNA `BuildContent` pipeline task and captures:

- messages,
- warnings,
- errors,
- and optional log-file output.

When a build fails, it tries to surface the first useful error line to speed up troubleshooting.

### 7) XNA pipeline dependency setup
If the required XNA content pipeline DLLs are not detected, the tool can prompt to install them via the embedded **XNA Game Studio Shared** MSI.

That helps reduce one of the most common setup headaches for older XNA workflows.

---

## Quick start

### Drag-and-drop method
The included batch file is the easiest entry point for standard models:

```text
FbxToXnb_Drop_Normal.bat
```

Just drag one or more `.fbx` files onto it.

### Command-line method

```powershell
FbxToXnb.exe "C:\Path\To\MyModel.fbx"
```

You can also pass multiple files:

```powershell
FbxToXnb.exe "C:\Path\To\A.fbx" "C:\Path\To\B.fbx"
```

### Interactive mode
Run the EXE without FBX arguments:

```powershell
FbxToXnb.exe
```

Then drag or paste paths into the console window and press Enter.

Type:

```text
help
```

for flags, or:

```text
exit
```

to quit.

---

## Advanced usage

### Use a custom processor
For a skinned model or a custom CastleForge pipeline extension:

```powershell
FbxToXnb.exe --processor SkinedModelProcessor --pipelineDir "C:\Path\To\PipelineBin" "C:\Path\To\Alien.fbx"
```

### Use a direct DLL path instead of a folder

```powershell
FbxToXnb.exe --processor SkinedModelProcessor --pipeline "C:\Path\To\DNA.SkinnedPipeline.dll" "C:\Path\To\Alien.fbx"
```

### Scale full-size TexturePacks exports back to game size

TexturePacks exports GLB models larger by default (`FbxComp = 10.0`) so they are easier to inspect and edit in Blender. FbxToXnb now understands that extractor setting directly:

```powershell
FbxToXnb.exe --fbxComp 10.0 "C:\Path\To\MyModel.fbx"
```

For model processors, this calculates the XNA processor scale for you:

```text
Scale = 0.01 / FbxComp
Scale = 0.01 / 10.0
Scale = 0.001
```

The normal drag/drop batch now prefers `ScaledModelProcessor` when `SkinedModelProcessor\DNA.SkinnedPipeline.dll` is present. That processor still uses the normal XNA mesh scale, but it also fixes transform-only sockets such as `BarrelTip`: it bakes the socket to model-root space before the stock processor runs, then post-corrects the socket basis with the Blender/GLB round-trip transform. By default, `SocketBasisScale=0.01`, `SocketBasisTransform=BlenderGlbRoundTripForward`, and `SocketTranslationScale=1.0`. This keeps muzzle flashes/projectile origins at the barrel instead of inheriting a Blender/FBX parent transform near the trigger.

Manual overrides still work when needed:

```powershell
FbxToXnb.exe --scale 0.001 "C:\Path\To\MyModel.fbx"
FbxToXnb.exe --param Scale=0.001 "C:\Path\To\MyModel.fbx"
FbxToXnb.exe --noScale "C:\Path\To\AlreadyScaledModel.fbx"
```

### Remove a TexturePacks authoring transform

If the GLB was exported with TexturePacks `[Models] AuthoringLocation` or `AuthoringRotation`, pass the same values when converting the edited FBX back to XNB:

```powershell
FbxToXnb.exe --fbxComp 10.0 --authoringLocation "0.64,-1.12,-0.58" --authoringRotation "0,0,0" "C:\Path\To\MyModel.fbx"
```

Use the values exactly as Blender shows them on the imported **RootNode**:

- `--authoringLocation` uses Blender Location `X, Y, Z`.
- `--authoringRotation` with three values uses Blender Euler degrees `X, Y, Z`.
- `--authoringRotation` with four values uses Blender Quaternion `W, X, Y, Z`.

Quaternion example:

```powershell
FbxToXnb.exe --fbxComp 10.0 --authoringLocation "0.64,-1.12,-0.58" --authoringRotation "1,-1,0,0" "C:\Path\To\MyModel.fbx"
```

FbxToXnb converts Blender's displayed Z-up values to the imported FBX/glTF basis internally, then removes that authoring transform before `ModelProcessor` builds the XNB. The advanced `--authoringLocationScale` option defaults to `100` for Blender FBX importer units and normally should be left alone.

TexturePacks also has an extractor-only rigid mesh cleanup:

```ini
NormalizeRigidMeshRotation = true
RigidMeshRotation = 180, 0, 0
```

That setting writes cleaner Blender-visible mesh rotation for the primary rigid mesh node, moves root-level helper/socket nodes and secondary root-level mesh nodes with the visible mesh for authoring, and saves both the original and Blender-authoring transforms in a matching `.cmzrigid.ini` sidecar. Keep that file beside the edited FBX. FbxToXnb first looks for an exact sidecar name matching the FBX, such as `Raygun.cmzrigid.ini`. If the FBX was renamed after export, it can also use a single sidecar in the folder or a sidecar whose base name is referenced by the FBX/texture files, such as `0051_Pistol.cmzrigid.ini` beside `Raygun.fbx`. If more than one sidecar is present and none clearly matches, pass `--rigidMeshRestoreFile` explicitly. The selected sidecar is passed to the processor as `RigidMeshRestoreFile`, scaling the sidecar values into FBX importer units and restoring those transforms before XNA builds the XNB. The processor uses the original + authoring transform pair as a world-space delta that includes the exported RootNode authoring transform, so normalized rigid weapons do not compile sideways, upside down, too low, or offset from the hand.

Optional socket correction overrides:

```powershell
# Writes BarrelTip bake information to the pipeline log.
FbxToXnb.exe --param SocketDebugLog=True "C:\Path\To\MyModel.fbx"

# Only disable this for one-off assets that are already authored with root-space sockets.
FbxToXnb.exe --param SocketBakeToModelRoot=False "C:\Path\To\MyModel.fbx"

# Manual override only. The normal rigid default is SocketTranslationScale=1.0.
FbxToXnb.exe --param SocketTranslationScale=1 "C:\Path\To\MyModel.fbx"

# Manual override only. The normal rigid default is SocketBasisScale=0.01.
FbxToXnb.exe --param SocketBasisScale=0.01 "C:\Path\To\MyModel.fbx"

# Only enable this for unusual FBX exports that really need an extra socket basis flip.
FbxToXnb.exe --param SocketRotationCorrection=True --param SocketRotationCorrectionAxis=Y --param SocketRotationCorrectionDegrees=180 "C:\Path\To\MyModel.fbx"
```

### Use the environment variable for repeated sessions
The tool also supports:

```text
CMZ_PIPELINE=path1;path2;...
```

That makes it easier to keep your custom pipeline locations available automatically.

---

## Command-line reference

| Flag | Purpose |
|-----------------------------|-----------------------------------------------------------------------------------------------|
| `--pipeline <dllOrDir>`     | Adds a custom pipeline DLL or folder. Repeatable.                                             |
| `--pipelineDir <dir>`       | Same idea as `--pipeline`, but clearer when pointing at a folder.                             |
| `--processor <name>`        | Overrides the FBX processor name, such as `ScaledModelProcessor`, `SkinedModelProcessor`, or `AnimationClipProcessor`. |
| `--fbxComp <value>`         | Uses the TexturePacks `[Models] FbxComp` value and computes `Scale = 0.01 / FbxComp`.         |
| `--scale <value>`           | Manual FBX model processor `Scale` override.                                                  |
| `--authoringLocation <x,y,z>` | Removes a TexturePacks GLB authoring location before processing. Use Blender RootNode Location `X, Y, Z`. |
| `--authoringRotation <values>` | Removes a TexturePacks GLB authoring rotation before processing. Three values mean Blender Euler degrees `X, Y, Z`; four values mean Blender Quaternion `W, X, Y, Z`. |
| `--authoringRotationDegrees <x,y,z>` | Explicit Euler-degree alias for `--authoringRotation`. |
| `--authoringRotationQuaternion <w,x,y,z>` | Explicit/legacy quaternion form. Still supported. |
| `--authoringLocationScale <value>` | Advanced location unit conversion for imported FBX nodes. Defaults to `100`. |
| `--rigidMeshRestoreFile <path>` | Optional TexturePacks `.cmzrigid.ini` sidecar path. Usually auto-detected beside the FBX. |
| `--noScale`                 | Disables the automatic/calculated model round-trip compensation.                              |
| `--param Name=Value`        | Passes a generic processor parameter; `Scale` here overrides `--fbxComp`.                     |
| `--animName <name>`         | Shortcut for `AnimationClipProcessor` output clip name.                                       |
| `--sourceClip <name>`       | Shortcut for choosing a specific FBX take.                                                    |
| `--frameRate <fps>`         | Shortcut for `AnimationClipProcessor` sample rate; use `30` for vanilla-like clips.           |
| `--noReduce`                | Tells `AnimationClipProcessor` to keep all sampled keys.                                      |
| `--help`                    | Shows help text.                                                                              |

### Default processor behavior
If you do not provide a processor override, the build path now prefers:

```text
ScaledModelProcessor
```

when the bundled DNA pipeline DLL is available. That keeps normal/static builds socket-safe by correcting `BarrelTip`/helper transforms separately from visible mesh geometry. Mesh scale uses the calculated XNA processor scale, socket basis scale defaults to `0.01`, and socket translation defaults to `1.0` after the normal processor has already placed it. If the DLL is missing, the tool falls back to stock `ModelProcessor`. For model processors, FbxToXnb also adds a calculated model scale by default. The default assumes TexturePacks used `FbxComp = 10.0`, so the effective XNA processor value is `Scale=0.001`. Animation-only processors are excluded from this automatic scale.

---

## Recommended folder layout

A clean authoring folder might look like this:

```text
MyAsset/
├─ MyAsset.fbx
├─ MyAsset.png
├─ textures/
│  ├─ emissive.png
│  └─ trim.png
└─ materials/
   └─ detail.jpg
```

After conversion, you will typically get:

```text
MyAsset/
├─ MyAsset.fbx
├─ MyAsset.png
├─ textures/
│  ├─ emissive.png
│  └─ trim.png
├─ materials/
│  └─ detail.jpg
└─ MyAsset/
   ├─ MyAsset.xnb
   ├─ texture.xnb
   └─ other compiled dependency .xnb files
```

That nested output folder is intentional.

It keeps each model’s compiled output self-contained and avoids overwriting another model’s dependency files.

![Folder Layout](_Images/FolderLayout.png)

---

## How texture discovery works

The texture staging logic is one of the nicest quality-of-life parts of the tool.

It searches the source directory recursively for likely texture files, including:

- `.png`
- `.jpg`
- `.jpeg`
- `.bmp`
- `.tga`
- `.dds`

Those files are copied into a temporary working directory while preserving relative paths.

That means an FBX that references textures in subfolders has a much better chance of compiling cleanly.

### Sidecar texture compatibility
If a sidecar PNG exists with the same base name as the FBX, the tool also exposes a compatibility alias named:

```text
texture.png
```

inside the temp build root.

That is particularly helpful for exports that expect a generic texture name.

---

## Output behavior

### Example input

```text
C:\Models\Alien.fbx
```

### Example output

```text
C:\Models\Alien\
├─ Alien.xnb
├─ texture.xnb
└─ additional compiled dependency .xnb files
```

### Why this layout matters
Many model pipelines generate dependency names like:

- `texture.xnb`
- `texture_0.xnb`
- `texture_1.xnb`

If every asset compiled into the same shared output directory, those generic names would collide constantly.

The per-asset folder approach avoids that problem.

---

## Build behavior under the hood

<details>
<summary><strong>XNA target settings</strong></summary>

The tool builds content for:

- **Target Platform:** `Windows`
- **Target Profile:** `Reach`
- **Content Compression:** enabled

</details>

<details>
<summary><strong>Pipeline assembly resolution</strong></summary>

The tool resolves the required XNA Game Studio 4.0 content pipeline DLLs from:

1. a caller-provided location,
2. the default XNA install path,
3. or an app-local fallback.

It can also expand extra pipeline DLLs from a directory when you pass custom pipeline locations.

</details>

<details>
<summary><strong>Environment setup</strong></summary>

The tool ensures the `XNAGSv4` environment variable is available when possible, which helps older XNA tooling locate the expected Game Studio root.

</details>

<details>
<summary><strong>Cleanup behavior</strong></summary>

The converter stages builds through temporary work and intermediate directories, then restores the original working directory and attempts to clean the temporary folders on completion.

</details>

---

## Installation

### Requirements

- Windows
- .NET Framework 4.8.1
- XNA Game Studio 4.0 content pipeline reference DLLs

### If XNA references are missing
On startup, the tool can prompt to install the missing XNA pipeline references through the embedded **XNA Game Studio Shared.msi**.

### Typical working folder
Your built toolchain will commonly look something like this:

```text
!Mods/
└─ TexturePacks/
   └─ _FbxToXnb/
      ├─ FbxToXnb.exe
      ├─ FbxToXnb_Drop_Normal.bat
      ├─ FbxToXnb_Drop_Skinned.bat (via DNA.SkinnedPipeline)
      ├─ README.txt
      ├─ XNA-related dependencies
      └─ optional custom pipeline folders
```

---

## Best use cases

FbxToXnb is especially useful for:

- custom weapon model workflows,
- preparing `.xnb` assets for pack-based systems,
- converting rigid/static models for CastleForge mods,
- experimenting with XNA-compatible asset compilation,
- and pairing with **DNA.SkinnedPipeline** for more advanced skinned content.

---

## Standalone AnimationClip builds

For WeaponAddons custom handling animations, use `AnimationClipProcessor` from `DNA.SkinnedPipeline`.

Example:

```powershell
FbxToXnb.exe --processor AnimationClipProcessor --pipelineDir "C:\CastleForge\!Mods\TexturePacks\_FbxToXnb\SkinedModelProcessor" --animName Reload "C:\Authoring\reload.fbx"
```

The output keeps the normal per-asset layout:

```text
C:\Authoring\reload\
└─ reload.xnb
```

That `.xnb` is a standalone `DNA.Drawing.Animation.AnimationClip` asset. Put it in a WeaponAddons pack, for example:

```text
WeaponAddons\Packs\Raygun\animations\reload.xnb
```

and reference it without the `.xnb` extension:

```ini
$ANIM_RELOAD: animations\reload
```

Useful animation flags:

```text
--animName Reload       Sets the AnimationClip.Name written into the XNB.
--sourceClip "Take 001" Chooses a specific FBX take when the file has more than one.
--frameRate 30          Samples the FBX at 30 FPS.
--noReduce              Keeps constant channels uncompressed for debugging.
--param Name=Value      Passes any custom processor parameter.
```

Keep the imported/exported armature aligned with the CastleForge reference model. The runtime clip stores bone transforms by index, so changed bone order can make arms, hands, or shoulders animate incorrectly.

---

## Troubleshooting

<details>
<summary><strong>“Build failed. Check logfile.txt / builder errors.”</strong></summary>

When a build fails, look at the first surfaced builder error and check for:

- missing XNA content pipeline references,
- missing texture files,
- mismatched texture filenames referenced by the FBX,
- or a missing custom processor DLL.

</details>

<details>
<summary><strong>“Cannot find content processor”</strong></summary>

This usually means you requested a processor like `ScaledModelProcessor`, `SkinedModelProcessor`, or `AnimationClipProcessor` without also providing the required pipeline DLL or pipeline directory.

Pass `--pipeline` or `--pipelineDir`, or use the normal/skinned/animation drag-and-drop helper that matches the asset you are building.

</details>

<details>
<summary><strong>The FBX references textures, but the build still fails</strong></summary>

Check whether the exported FBX expects exact filenames or relative subfolder paths. The tool stages likely texture files recursively, but the exported material references still need to line up with the files you actually provide.

</details>

<details>
<summary><strong>I only want static model conversion. Do I need DNA.SkinnedPipeline too?</strong></summary>

Not always.

For plain rigid/static models with no helper sockets, the stock `ModelProcessor` path is usually enough. For TexturePacks weapon round-trips, use the bundled DNA pipeline so `ScaledModelProcessor` can preserve socket nodes such as `BarrelTip`.

</details>

---

## Summary

**FbxToXnb** is the CastleForge tool that turns raw `.fbx` files into **practical, XNA-ready `.xnb` assets** without making creators fight the content pipeline every single time.

It is simple enough for drag-and-drop use, but flexible enough to support custom processors, batch workflows, texture staging, and older XNA dependency handling when your authoring pipeline gets more advanced.

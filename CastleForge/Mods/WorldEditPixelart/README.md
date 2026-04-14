# WorldEditPixelart

<div align="center">
    <img src="_Images/Preview.gif" alt="WorldEditPixelart Preview">
</div>
<div align="center">
    <b>🖼️ Image ➔ 🎨 Pixel Art Editor ➔ 🏗️ Schematic.</b> Convert source images into CastleMiner Z block art directly inside the game, preview the result, tune the palette, and send the finished build to WorldEdit.
</div>

![Editor overview placeholder](_Images/EditorOverview.png)

> **Image suggestion:** Show the full editor open in-game with the source image on the left, converted output on the right, and the schematic / scaling controls visible.

---

## Table of contents

- [What this addon does](#what-this-addon-does)
- [Why this version stands out](#why-this-version-stands-out)
- [Requirements](#requirements)
- [Installation](#installation)
- [First launch and generated files](#first-launch-and-generated-files)
- [Configuration](#configuration)
- [Quick start](#quick-start)
- [How the workflow works](#how-the-workflow-works)
- [Editor feature tour](#editor-feature-tour)
- [Commands and hotkeys](#commands-and-hotkeys)
- [Color palettes and custom filters](#color-palettes-and-custom-filters)
- [Schematic export and WorldEdit clipboard workflow](#schematic-export-and-worldedit-clipboard-workflow)
- [Performance and rendering notes](#performance-and-rendering-notes)
- [Companion XML palette-building workflow](#companion-xml-palette-building-workflow)
- [Python palette visualization script](#python-palette-visualization-script)
- [Troubleshooting](#troubleshooting)
- [Credits](#credits)

---

## What this addon does

**WorldEditPixelart** is an in-game image conversion overlay for CastleForge that helps turn real images into block-based pixel art for CastleMiner Z.

It is designed to work alongside **WorldEdit**, letting you:

- open a full pixel-art editor from inside the game
- drag and drop an image directly into the tool
- drag and drop XML color-filter files into the same source area
- preview a converted block-art result before exporting
- choose how the image is resampled using multiple scaling algorithms
- manage the block color palette used for matching colors
- load, save, reset, and refine XML color filters used for matching
- add custom palette entries by sampling a source color and assigning it to a block ID
- remove unused or unwanted colors from the active palette
- rotate and orient the generated schematic before export
- copy the generated result directly into **WorldEdit's clipboard**
- save the result to disk as a `.schem` file
- save the converted preview image as a normal image file
- inspect total width, height, and block count before you build
- optionally show a progress bar and gather statistics during larger renders

This makes it much faster to go from **reference image** to **editable in-game build** without manually placing every pixel by hand.

---

## Why this version stands out

### Built into the game session
This is not a separate desktop converter that happens to target CMZ. The editor runs **inside the CastleMiner Z process** as a WinForms overlay and is opened directly from the game.

### Made for WorldEdit workflows
WorldEditPixelart is not just about producing an image preview. It is built around real **WorldEdit-friendly output**, including:

- direct copy into the WorldEdit clipboard
- `.schem` export for saved builds
- rotation and placement orientation controls
- dimensions and block-count feedback for planning

### Palette control instead of blind conversion
A big part of getting good-looking block art is the palette. This addon lets you:

- use the embedded default block palette
- load your own XML color filters
- save modified filters back out
- reduce the palette to unique colors only
- remove unused colors after a render
- sample a source-image color and assign it to a specific block ID
- build brand new `BlockColors.xml` files from screenshots with the companion workflow
- brighten an existing XML palette into an adjusted variant when needed

### Multiple scaling strategies
Different images look better with different resampling methods. The editor supports:

- Bilinear
- Nearest Neighbor
- Bicubic
- Lanczos
- Hermite
- Spline
- Gaussian

That gives you more control over whether the final block art looks smoother, sharper, or more stylized.

### In-game-friendly overlay behavior
The overlay is designed to temporarily capture input while open so you can safely work inside the editor without fighting the game's normal mouse / camera input.

---

## Requirements

- **CastleForge ModLoader**
- **CastleForge ModLoaderExtensions**
- **WorldEdit**
- CastleMiner Z with a working CastleForge mod setup
- A loaded world or active session before opening the editor

**Target framework in source:** `.NET Framework 4.8.1`

> WorldEditPixelart is a companion addon for WorldEdit. It is not a replacement for WorldEdit itself.

---

## Installation

### For players
1. Install **ModLoader** and **ModLoaderExtensions** first.
2. Install **WorldEdit**.
3. Add the **WorldEditPixelart** mod release to your CastleForge mods setup.
4. Launch the game once.
5. Enter a world or session.
6. Open the editor with `/pixelart` or one of its aliases.
7. Optionally configure a hotkey in the generated INI file for faster access.

### For creators / source users
If you are working from source rather than just installing the mod, the companion tools relevant to this workflow live under:

```text
Tools/ImageColorsToXml
Tools/DNA.SkinnedPipeline
```

`ImageColorsToXml` covers palette generation / brightness-adjustment workflows, while `DNA.SkinnedPipeline` is the actual skinned-model processor used by the repo's skinned FBX toolchain.

![Installation placeholder](_Images/Installation.png)

> **Image suggestion:** Show the CastleForge mod folder with `WorldEdit`, `WorldEditPixelart`, and the generated runtime `!Mods/WorldEditPixelart` folder visible.

---

## First launch and generated files

On first launch, the addon creates and/or uses content under:

```text
!Mods/WorldEditPixelart/
```

The most important runtime files are:

```text
!Mods/WorldEditPixelart/WorldEditPixelart.ini
!Mods/WorldEditPixelart/ColorPalette/BlockColors.xml
```

### What gets generated

- **`WorldEditPixelart.ini`** stores the overlay hotkey and window-hosting mode.
- **`ColorPalette/BlockColors.xml`** is the extracted default block palette used for color matching.

That palette file can also be replaced or supplemented through the editor's palette-management features.

---

## Configuration

WorldEditPixelart generates this config file automatically:

```ini
; WorldEditPixelart - Configuration
; Lines starting with ';' or '#' are comments.

[Hotkeys]
; ToggleKey opens the editor and freezes input; pressing it again in the editor closes/unfreezes.
; Set to 'None' to disable the hotkey entirely.
; Examples: F10, F4, Insert, Delete, OemTilde, None
ToggleKey=None

[Behavior]
; Overlay host mode:
;   true  = Embed inside the game window (child mode).
;           - Lives within the game's client area; can't be dragged outside.
;           - No taskbar entry; z-order follows the game.
;           - Best for windowed/borderless; avoids focus flicker.
;   false = Show as a separate window (owned by the game).
;           - Standard title bar + icon; can move to other monitors.
;           - May briefly steal/return focus when opened/closed.
;           - Use if you want a normal, freely movable window.
EmbedAsChild=true
```

### Config notes

#### `[Hotkeys]`
- `ToggleKey`  
  Optional keyboard shortcut for opening the editor. The default is `None`, meaning the hotkey is disabled until you set one.

#### `[Behavior]`
- `EmbedAsChild`  
  Controls whether the editor is embedded inside the game window or shown as a separate owned window.

![Configuration placeholder](_Images/Configuration.png)

> **Image suggestion:** Show the generated INI file open in a text editor with `ToggleKey` and `EmbedAsChild` highlighted.

---

## Quick start

This is the fastest path from a source image to a WorldEdit-ready result.

### 1) Open the editor
Use any of these commands in-game:

```text
/pixelart
/pixel
/pa
/imagetopixelart
/pixelarttool
```

### 2) Load an image
Use **Open New Image** or drag and drop an image onto the source preview area.

> **Tip:** The same source area can also accept an XML color filter, so you can swap palettes by dragging in a `BlockColors.xml`-style file.

### 3) Choose your conversion settings
Set the options you want before rendering, such as:

- scaling mode
- spacing
- ratio / zoom level
- grid visibility
- backdrop handling
- schematic generation
- rotation
- flat vs standing mode
- X-axis vs Y-axis output orientation
- whether to show a progress bar or gather statistics while rendering

### 4) Convert the image
Click:

```text
Convert To Pixel Art
```

The output preview will update using the current palette and scaling settings.

### 4.5) Tune the palette if needed
If the output colors feel wrong, do not assume the source image is the problem.

Try one or more of these before exporting:

- **Load Color Filter**
- **Save Color Filter**
- **Reset Colors**
- **Unique Colors**
- **Del Null Colors**
- **Delete Color**
- **Custom Color Picker**

### 5) Export or send to WorldEdit
Once the result looks right, you can:

- **Copy To Clipboard** to send the generated schematic to WorldEdit
- **Save Schematic To File** to write a `.schem` file
- **Overwrite Existing File** to quickly replace the last schematic you saved
- **Save Image** to keep the rendered preview as a normal image
- **Save Color Filter** to preserve the palette you used

![Quick start placeholder](_Images/QuickStart.png)

> **Image suggestion:** Show a 4-panel sequence: open editor → load source image → convert preview → copy to WorldEdit or save schematic.

---

## How the workflow works

WorldEditPixelart follows a simple pipeline:

1. **Load a source image**  
   You can open one manually or drag and drop it into the editor. The same drop area also accepts XML palette files.

2. **Choose a color palette**  
   The tool compares the source image against a CMZ block-color palette.

3. **Resample the image**  
   The selected scaling mode determines how image pixels are interpreted before matching them to blocks.

4. **Match colors to blocks**  
   Each rendered output cell is matched to the nearest color in the active palette.

5. **Optionally refine the palette**  
   If the output looks wrong, you can load a different XML filter, trim unused colors, remove a bad mapping, or add a hand-picked source color to block ID mapping.

6. **Optionally build schematic data while rendering**  
   Enabling **Generate Schematic** prepares data for clipboard copy or schematic save.

7. **Rotate/orient the output**  
   You can change standing vs flat mode, world axis, and rotation before exporting.

8. **Send the result to WorldEdit or save it to disk**  
   Once satisfied, you can move directly into the WorldEdit building workflow.

---

## Editor feature tour

### Image input and output
The editor is built around two main panes:

- **Image Input** for your source image
- **Image Output** for the converted block-art preview

The source pane supports drag-and-drop image loading and can also accept XML color-filter files, while the output pane reflects the currently selected conversion settings.

![Input-output placeholder](_Images/InputOutput.png)

> **Image suggestion:** Show the same image on the left and its converted block-art preview on the right.

### Basic configurations
The main configuration area includes controls for:

- spacing
- ratio / zoom refresh
- opening a new image
- converting the image
- saving the converted preview image
- generating and saving schematic data
- copying the build into WorldEdit's clipboard
- overwriting the last saved schematic
- showing the current image, current filter, and last save path
- optionally showing a progress bar and gathering statistics during larger renders

### Conversion state, progress, and convenience
The current editor build also includes several quality-of-life helpers around longer renders and exports:

- **Generate Schematic** to build export data during conversion
- **Progress Bar** to show render progress
- **Gather Statistics** to track totals while rendering
- **Current Image** and **Current Filter Name** readouts
- **Save Directory** tracking for the last schematic path
- **Overwrite Existing File** for quick repeat exports
- **Cancel Conversion** behavior while a render is already running

These features make it easier to iterate on a build without losing track of which image, filter, or schematic state you are looking at.

### Schematic rotation and orientation
The editor can rotate and orient the generated schematic before export.

Supported options include:

- **No Rotation**
- **90 Degrees**
- **180 Degrees**
- **270 Degrees**
- **Standing** mode
- **Flat** mode
- **X-Axis** output
- **Y-Axis** output

This makes it easier to prepare a build for different placement styles before pasting it into the world.

![Rotation placeholder](_Images/RotationAndOrientation.png)

> **Image suggestion:** Show the rotation controls with two small example outputs, one standing and one flat.

### Grid and preview options
Preview helpers include:

- **Show Grid**
- **Backdrop** for transparent areas
- **Grid Color** picker
- **Grid X Offset**
- **Grid Y Offset**

These options do not change the core idea of the image, but they make it much easier to inspect alignment and spacing before export.

### Scaling modes
The current implementation supports these conversion modes:

- Bilinear
- Nearest Neighbor
- Bicubic
- Lanczos
- Hermite
- Spline
- Gaussian

Additional tuning controls include:

- **A=** for Lanczos behavior
- **Sigma=** for Gaussian behavior

That makes the addon flexible enough for both crisp low-resolution sprite work and smoother photo-based conversions.

![Scaling placeholder](_Images/ScalingModes.png)

> **Image suggestion:** Show the same source image converted with 3 or 4 different scaling modes side by side.

### Statistics and progress
The tool can display or update:

- total height
- total width
- total blocks
- total colors
- filtered colors
- current filter name
- current image path
- last saved schematic path
- progress bar state during rendering

That helps estimate the size and cost of a build before you paste it.

---

## Commands and hotkeys

WorldEditPixelart is intentionally simple on the command side. Its main job is to launch the editor.

| Command | Aliases | What it does |
|---|---|---|
| `/pixelart` | `/pixel`, `/pa`, `/imagetopixelart`, `/pixelarttool` | Opens the WorldEditPixelart editor overlay. |

> The addon also registers `//` forms for the same commands.

### Example

```text
/pixelart
```

### Hotkey behavior
If you set a `ToggleKey` in the config, that key can also be used to open the editor. While the editor is focused, pressing the same key again closes it.

---

## Color palettes and custom filters

A big part of good pixel-art conversion is choosing the right block palette.

If two players use the same image but different `BlockColors.xml` files, they can get very different results. That means the quality of your pixel art is tied not just to the source image, but also to the XML palette behind the conversion.

### Default palette
On first run, the addon extracts the embedded default palette to:

```text
!Mods/WorldEditPixelart/ColorPalette/BlockColors.xml
```

### Palette manager features
The editor can:

- load an external XML color filter
- drag and drop an XML color filter directly into the source pane
- save the current active filter to XML
- reset back to the embedded default palette
- collapse duplicates with **Unique Colors**
- remove colors that were not used in the current render with **Del Null Colors**
- remove a specific rendered color interactively with **Delete Color**

### Custom Color Picker
The **Custom Color Picker** lets you:

1. click a source-image color
2. enter a target block ID
3. inject that new mapping into the active palette

This is useful when you want a very specific source color to map to a block of your choosing rather than relying only on nearest-match logic.

### When to adjust the palette
Consider adjusting your XML filter when:

- colors keep mapping to the wrong block even after changing scaling modes
- a certain block dominates the output too much
- you added modded/custom blocks and need them included in the palette
- you want a brighter or more stylized variant of the same palette
- you are building art for a different content pack or project

![Palette placeholder](_Images/PaletteTools.png)

> **Image suggestion:** Show the color filter manager, custom color picker flow, and a small XML palette preview.

---

## Schematic export and WorldEdit clipboard workflow

WorldEditPixelart supports two main output paths.

### 1) Copy directly into WorldEdit
When **Generate Schematic** is enabled and the current render is up to date, clicking **Copy To Clipboard** sends the generated schematic data into **WorldEdit's clipboard**.

That means you can go straight from image conversion to WorldEdit paste workflows without manually rebuilding anything.

### 2) Save a `.schem` file
You can also save the generated result to disk as a schematic file for reuse later.

### Extra export notes
- The tool warns you if your schematic data is no longer up to date with the current image/settings.
- You can overwrite the previously saved schematic file directly from the editor.
- Rotation and orientation settings affect how the schematic is produced.

![Export placeholder](_Images/ExportWorkflow.png)

> **Image suggestion:** Show the copy-to-clipboard button, a `.schem` save dialog, and the result pasted into the world with WorldEdit.

---

## Performance and rendering notes

Pixel-art conversion is usually fast, but a few settings can affect how heavy a render feels.

### Settings that can increase work
- very large source images
- high output ratios
- more complex scaling modes
- **Generate Schematic** enabled during conversion
- progress/stat gathering enabled during long jobs

### Practical advice
- Start with a smaller image or lower ratio first.
- Use simpler scaling modes when iterating quickly.
- Turn on **Generate Schematic** when you are ready to export, not only while experimenting.
- Use the preview image and block statistics to estimate how large the final build will be.

### Canceling a render
The convert button can switch into a cancel state while a render is running, so large jobs do not have to be left to complete if you already know you want different settings.

---

## Companion XML palette-building workflow

Your original project documentation also covered a lightweight workflow for generating XML color data from screenshots of blocks.

That is useful when you want to build or refine palettes for:

- new content packs
- modded blocks
- alternate games/projects
- brighter or rebalanced palette variants

### `ImageColorsToXml`
In the current CastleForge tree, the lightweight palette-builder tool lives under:

```text
Tools/ImageColorsToXml
```

This is the streamlined companion workflow for mass-gathering average colors from block screenshots and automatically writing them into an XML palette file.

### Naming format
The original workflow used filenames in this format:

```text
blockname,id.*
```

Examples:

```text
rock,6.png
ironwall,22.jpg
bloodstone,20.bmp
```

### Supported image formats
- `.jpg`
- `.png`
- `.bmp`

### Result
Those images can then be dropped onto the executable to build a `BlockColors.xml` file for later loading into the editor.

![Palette builder placeholder](_Images/PaletteBuilder.png)

> **Image suggestion:** Show a folder of block screenshots named `name,id.png`, plus the resulting XML palette file.

### Optional luminosity-adjustment workflow
The same lightweight workflow also supports adjusting palette brightness by reprocessing an existing XML file.

1. Drag and drop `BlockColors.xml` onto the executable.
2. Enter the luminosity percentage increase.
3. The tool writes an adjusted palette file.

### Output
The adjusted file is written as:

```text
AdjustedBlockColors.xml
```

This is helpful when your palette is technically correct but the in-game result is too dark and you want a brighter variant without manually rewriting every color entry.

![Luminosity tool placeholder](_Images/LuminosityTool.png)

> **Image suggestion:** Show `BlockColors.xml` being dropped into the tool and `AdjustedBlockColors.xml` appearing after the brightness increase.

---

## Python palette visualization script

Your original docs also included a simple Python script for previewing the colors inside either `BlockColors.xml` or `AdjustedBlockColors.xml`.

This is very useful when you want to visually inspect palette changes before loading the XML back into the editor.

<details>
  <summary><strong>Show Python script</strong></summary>

```py
import xml.etree.ElementTree as ET
import matplotlib.pyplot as plt

# Load and parse the XML file.
tree = ET.parse("BlockColors.xml")
root = tree.getroot()

# Extract color values, names, and IDs from the XML.
colors = []
names = []
ids = []

for block in root.find("Blocks").findall("Block"):
    color = block.get("Color")            # Get the color attribute.
    name = block.get("Name", "Unknown")   # Get the block name.
    block_id = block.get("Id", "??")      # Get the block ID.

    if color.startswith("#FF"):           # Remove alpha (FF).
        color = "#" + color[3:]

    colors.append(color)
    names.append(name)
    ids.append(block_id)

# Plot the colors as horizontal bars.
fig, ax = plt.subplots(figsize=(8, len(colors) * 0.4))
ax.set_ylim(0, len(colors))
ax.set_xlim(-0.5, 1.5)
ax.axis("off")

# Display each color as a horizontal bar.
for i, (color, name, block_id) in enumerate(zip(colors, names, ids)):
    ax.add_patch(plt.Rectangle((0, i), 1, 1, color=color))                                  # Draw horizontal color bar.
    ax.text(-0.1, i + 0.5, name, ha="right", va="center", fontsize=10)                      # Name on the left.
    ax.text(1.1, i + 0.5, block_id, ha="left", va="center", fontsize=10, fontweight="bold") # ID on the right.

# Invert the y-axis so that the first element is at the top.
ax.invert_yaxis()

plt.show()
```

</details>

![Python preview placeholder](_Images/PythonPalettePreview.png)

> **Image suggestion:** Show the generated horizontal color-bar preview with block names on the left and IDs on the right.

---

## DNA.SkinnedPipeline and skinned FBX authoring

The repo now also includes the actual skinned-model content-pipeline library used by the broader CastleForge asset workflow:

```text
Tools/DNA.SkinnedPipeline
```

This is **not required** for normal WorldEditPixelart usage, but it is important for source users working on skinned or rigged assets in the same repository.

### What it is for
`DNA.SkinnedPipeline` is the XNA content-pipeline processor used with the skinned FBX toolchain, including:

- the `FbxToXnb_Drop_Skinned.bat` workflow
- the repo's `_FbxToXnb/SkinedModelProcessor/` deployment path
- skeleton and inverse bind-pose metadata packaging for CMZ/DNA-style runtime loading

### What it does
At a high level, it lets creators process skinned FBX assets so they can be converted into XNB content using the runtime structures the game expects.

### Current limitation
The processor builds the minimum viable skinning metadata, but animation clip extraction is not implemented yet. In other words, it is the real skinned processor pipeline, but it is not a full animation-authoring replacement by itself.

![Skinned pipeline placeholder](_Images/SkinnedPipeline.png)

> **Image suggestion:** Show the `DNA.SkinnedPipeline` project beside the `_FbxToXnb` folder and `FbxToXnb_Drop_Skinned.bat`, with a simple rigged FBX authoring example.

---

## Troubleshooting

### "The editor does not open"
Check all of the following:

- you are currently in a loaded world/session
- the mod dependencies are installed correctly
- WorldEdit is also installed
- your configured hotkey is valid if you are using one
- try the command directly:

```text
/pixelart
```

### "My source image will not load"
Make sure the image file is valid and readable. You can also try loading it through **Open New Image** instead of drag and drop.

### "The palette seems wrong"
Try one or more of these:

- reset the palette with **Reset Colors**
- load the intended XML filter again
- drag the XML filter directly onto the source pane
- toggle **Unique Colors** depending on whether duplicates matter for your palette
- use **Delete Color** or **Del Null Colors** to refine the active set
- rebuild the palette with `ImageColorsToXml` if the source XML itself is the problem

### "The XML palette did not load"
Make sure the file contains `Block` entries with `Id`, `Name`, and `Color` attributes. You can load it through **Load Color Filter** or by dragging the XML file onto the source-image area.

### "Copy To Clipboard says the schematic is empty"
You need to render the image first, and you generally want **Generate Schematic** enabled before conversion if you plan to export schematic data.

### "It says the schematic is out of date"
That means your image/settings changed after the last schematic-generation pass. Re-render the image before copying or saving.

### "Overwrite Existing File" is not doing anything
That button only works after you have already saved a schematic once in the current session, because the editor needs a remembered target path before it can overwrite anything.

### "The overlay behavior feels awkward"
Try changing:

```ini
EmbedAsChild
```

- `true` keeps the editor inside the game window
- `false` makes it a separate movable window

### "My pasted build faces the wrong way"
Check the rotation, flat/standing mode, and X-axis/Y-axis settings before exporting.

---

## Credits

- **RussDev7** — original WorldEdit-CSharp tooling, CastleForge adaptation, CMZ integration, XML palette workflow, and companion source tooling
- **CastleForge / WorldEdit** — clipboard and schematic workflow foundation used by this addon
- Original project documentation and screenshots that inspired the editor workflow presentation
- The companion `ImageColorsToXml` and `DNA.SkinnedPipeline` tools included in the CastleForge source tree
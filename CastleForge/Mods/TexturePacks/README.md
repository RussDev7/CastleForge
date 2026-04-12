# TexturePacks

> **Re-skin CastleMiner Z far beyond simple block textures.**
>  
> **TexturePacks** is a full runtime content replacement system for CastleForge that lets players and pack authors swap **terrain**, **item icons**, **held-item skins**, **XNB models**, **HUD and inventory sprites**, **menu screens**, **fonts**, **sound effects**, **music**, **terrain shaders**, **skyboxes**, and more — all through a pack folder structure designed for real-world modding workflows.

---

## Overview

TexturePacks turns the game's `!Mods\TexturePacks\<PackName>\` folders into live content packs that can be applied at runtime. Instead of limiting authors to a single atlas replacement, this mod reaches into many parts of the game and safely restores vanilla baselines before applying the selected pack.

That means one pack can change the look and feel of nearly the entire game:

- world blocks and distant terrain
- item icons and held-item visuals
- player, enemy, and dragon textures
- full model geometry overrides through XNB swaps
- full-sheet door model textures
- menu backdrops, logos, load screens, HUD, and inventory art
- runtime-generated fonts from `.ttf` / `.otf`
- sound effects, ambience, and music shadow playback
- terrain shader overrides
- cubemap sky replacements
- pack picker icons and metadata
- author export tools for building packs from real game assets

![TexturePacks overview placeholder](docs/images/texturepacks/overview-hero.png)

**Suggested screenshot:** A polished hero image showing the main menu pack picker, a themed HUD, and a custom world view from the same pack.

---

## Why this mod stands out

TexturePacks is not just a folder of PNG replacements. It is a **content-pack framework** built for both **players** and **pack authors**.

### For players
- Pick and switch packs from an in-game **Texture Packs** menu.
- Reset back to vanilla at any time.
- Use optional pack icons and descriptions for a cleaner browsing experience.
- Reload packs without manually replacing game files.

### For pack authors
- Export reference assets directly from the game.
- Replace both **near** and **far** terrain atlases so distant terrain matches close-up terrain.
- Support clean filename-based lookups instead of brittle manual patching.
- Build packs that cover visuals, UI, fonts, sounds, shaders, skies, and models in one place.
- Use GLB export workflows to preserve important model node and bone names during round-tripping.

### For stability
- Captures vanilla baselines before replacement.
- Rebinds live entities before unloading pack-loaded models.
- Uses deferred reloads and GPU retire queues to reduce device/resource issues during hot-swaps.
- Falls back to vanilla when a pack asset is missing or incomplete.

---

## Showcase

### Main menu / pack picker
![Texture pack picker](docs/images/texturepacks/picker-main-menu.png)

**Image suggestion:** Show the Texture Packs menu opened from the main menu with at least three packs visible, including one with a custom icon and `about.json` description.

### In-game pack switching
![In-game pack switching](docs/images/texturepacks/picker-ingame.png)

**Image suggestion:** Open the in-game menu and highlight the injected **Texture Packs** entry above **Inventory**.

### Before / after environment comparison
![Before and after world comparison](docs/images/texturepacks/before-after-world.png)

**Image suggestion:** Split-screen comparison showing vanilla terrain on one side and a custom terrain/UI pack on the other.

### UI / HUD overhaul
![HUD and inventory reskin](docs/images/texturepacks/hud-inventory.png)

**Image suggestion:** Show the HUD, inventory, and crafting screen using replaced crosshair, health bars, selectors, and inventory panels.

### Model swap / item geometry example
![Custom held item model](docs/images/texturepacks/item-model-override.png)

**Image suggestion:** Show a vanilla item next to a custom geometry override of the same item.

### Sky / screen / audio-themed pack
![Sky and menu overhaul](docs/images/texturepacks/sky-menu-overhaul.png)

**Image suggestion:** Capture a custom menu backdrop, logo, load screen, and a visible skybox change in-world.

---

## Feature highlights

### Runtime pack switching
TexturePacks supports deferred pack reloads on the main thread. When the active pack changes, it restores vanilla baselines and re-applies the selected pack cleanly instead of stacking replacements on top of each other.

Reload flow at a glance:
- UI, commands, or config changes call a deferred reload request instead of swapping content immediately.
- The actual pack swap runs on the next safe update tick on the main thread.
- GPU-backed resources that cannot be destroyed mid-draw are pushed into a retire queue and disposed after the frame finishes.
- Terrain, icons, models, shaders, skies, audio, and UI assets are restored from captured vanilla baselines before the active pack is re-applied.

This keeps pack switching predictable, helps reduce device-state problems during hot-swaps, and makes it much clearer why a pack change happens one tick later instead of instantly.

### In-game pack picker
The mod injects a **Texture Packs** option into:
- the **main menu**
- the **in-game pause menu**

The picker supports:
- mouse wheel scrolling
- click selection
- double-click apply
- keyboard navigation with **Up / Down / Enter**
- **Apply**, **Refresh**, and **Close** buttons
- a built-in **(default)** option that restores vanilla

It also supports:
- pack icons from the pack root
- pack descriptions from `about.json`
- two-line formatted descriptions with `&` and `§` style/color codes

### Full terrain atlas replacement
TexturePacks patches:
- the **near terrain diffuse atlas**
- the **far / mip terrain diffuse atlas**
- the **near terrain normal/spec atlas** when `_normalspec` companions are present
- the **far / mip terrain normal/spec atlas** when `_normalspec` companions are present

This matters because it helps prevent ugly distance pop where nearby blocks look replaced but far terrain still uses vanilla art.

### Terrain `_normalspec` companions
For terrain blocks, a diffuse file like `Grass_top.png` can be paired with `Grass_top_normalspec.png`.

Use the exact same stem and face suffix, then append `_normalspec` before the extension:

```text
Grass_top.png
Grass_top_normalspec.png

TNT_side.png
TNT_side_normalspec.png

tile_2.png
tile_2_normalspec.png
```

These files are intended to travel together so the terrain shader can use matching surface detail for the same block face or tile index.

### Item icon replacement
Inventory icons support multiple naming patterns, including ID-based and name-based lookups, making packs easier to author and maintain.

### Held-item skin replacement
You can replace the textures used by held items without replacing their geometry.

### Held-item model geometry overrides
You can also replace the **entire item model** with custom compiled XNB model assets.

### Character and creature support
TexturePacks supports both **skin replacements** and **geometry overrides** for:
- player models
- enemy models
- dragon models

### Door model textures and geometry
TexturePacks supports door replacements through:

```text
Models\Doors\
  NormalDoor.png
  IronDoor.png
  DiamondDoor.png
  TechDoor.png

  NormalDoor.xnb
  IronDoor.xnb
  DiamondDoor.xnb
  TechDoor.xnb
```

### UI, menu, and HUD replacement
The mod can replace:
- menu background
- logo
- dialog background
- load screen
- HUD sprites
- inventory sprites
- menu ads and other UI visuals where applicable

### Runtime font packs
Fonts can be supplied as `.ttf` or `.otf` files and are turned into runtime `SpriteFont` replacements that propagate through UI trees.

### Sound and music packs
TexturePacks can replace:
- one-shot sound effects
- 2D / 3D sound cues
- ambient loops
- music through shadow playback that mirrors engine behavior

### Terrain shader override
A pack can provide a custom `Shaders\blockEffect.fxb` file to override the terrain shader.

### Skybox replacement
Cubemap skies can be replaced for:
- clear sky
- night sky
- sunset
- dawn

### Built-in author tooling
TexturePacks includes extraction tooling so authors can dump reference assets from the game and build packs against real asset names and structures.

---

## Installation

### Requirements
- CastleForge **ModLoader**
- CastleForge **ModLoaderExtensions**

TexturePacks declares **ModLoaderExtensions** as a required dependency.

### Install steps
1. Install the CastleForge loader and dependencies normally.
2. Place the built `TexturePacks` mod into your `!Mods` folder.
3. Launch the game once so the mod can initialize its folders and config.
4. Put your packs inside:

```text
!Mods\TexturePacks\<PackName>\
```

5. Start the game and open **Texture Packs** from the main menu or in-game menu.

![Installation placeholder](docs/images/texturepacks/install-layout.png)

**Image suggestion:** Show the `!Mods\TexturePacks\` folder in Explorer with at least one pack folder visible.

---

## Quick start

### Apply a pack from the UI
1. Open **Texture Packs**.
2. Select a pack from the list.
3. Click **Apply** or press **Enter**.
4. The mod saves your selection and reloads the pack on the next update tick.

### Apply a pack with commands
```text
/tpset MyPack
```

### Return to vanilla
```text
/tpreset
```

### Reload the current pack
```text
/tpreload
```

### Export reference assets for authoring
```text
/tpexportall
```

By default, the export hotkey is also:

```text
Ctrl+Shift+F3
```

![Quick start placeholder](docs/images/texturepacks/quick-start-ui.png)

**Image suggestion:** Show the pack picker with one pack selected and the Apply button highlighted.

---

## Commands

| Command | Description |
|---|---|
| `/tpset [PackName]` | Sets a new active texture pack, saves it to config, and requests a reload. |
| `/tpreset` | Clears the active pack and restores vanilla visuals. |
| `/tpreload` | Reloads the currently selected pack. |
| `/tpexportall` | Dumps game assets to `!Mods\TexturePacks\_Extracted\<timestamp>\` for pack-author reference. |

---

## Configuration

TexturePacks creates an INI config at:

```text
!Mods\TexturePacks\TexturePacks.Config.ini
```

### Default config example
```ini
; TexturePacks - Configuration
; Lines starting with ';' or '#' are comments.

[General]
ActivePack =
TileSize   = 64

[Hotkeys]
Export     = Ctrl+Shift+F3

[PickerUI]
RowH       = 64
RowPad     = 8
PanelPad   = 24
TitleGap   = 12
ButtonsGap = 18
IconPad    = 6
IconGap    = 10

[Models]
FbxComp    = 0.01
```

### Config keys

| Section | Key | Default | What it does |
|---|---|---:|---|
| `General` | `ActivePack` | blank | The currently selected pack folder. Blank restores vanilla. |
| `General` | `TileSize` | `64` | Terrain atlas tile size used for block atlas processing. |
| `Hotkeys` | `Export` | `Ctrl+Shift+F3` | Runs the full export pipeline. Use `None` to disable. |
| `PickerUI` | `RowH` | `64` | Pack row height in the picker UI. |
| `PickerUI` | `RowPad` | `8` | Vertical spacing between rows. |
| `PickerUI` | `PanelPad` | `24` | Inner padding of the picker panel. |
| `PickerUI` | `TitleGap` | `12` | Space under the picker title. |
| `PickerUI` | `ButtonsGap` | `18` | Space above the bottom button row. |
| `PickerUI` | `IconPad` | `6` | Inner padding around row icons. |
| `PickerUI` | `IconGap` | `10` | Gap between the icon and row text. |
| `Models` | `FbxComp` | `0.01` | Root scale compensation for GLB → Blender → FBX → XNB model round-trips. Set `1.0` to disable. |

![Config placeholder](docs/images/texturepacks/config-example.png)

**Image suggestion:** Show the generated config file with `ActivePack`, `TileSize`, and `FbxComp` highlighted.

---

## Pack folder structure

A typical pack lives here:

```text
!Mods\
└─ TexturePacks\
   └─ MyPack\
      ├─ about.json
      ├─ pack.png
      ├─ Blocks\
      ├─ HUD\
      ├─ Inventory\
      ├─ Items\
      ├─ Models\
      │  ├─ Doors\
      │  ├─ Items\
      │  ├─ Player\
      │  ├─ Enemies\
      │  └─ Dragons\
      ├─ Screens\
      ├─ Fonts\
      ├─ Sounds\
      ├─ Shaders\
      ├─ Skys\
      └─ ParticleEffects\
```

### Notes
- Pack folders beginning with `_` are ignored by the picker.
- Missing files do **not** break the pack. Only matching assets are replaced.
- The pack root can optionally include:
  - `icon.*`, `pack.*`, `preview.*`, or `logo.*`
  - `about.json`

![Pack structure placeholder](docs/images/texturepacks/pack-folder-structure.png)

**Image suggestion:** Show the Example Pack expanded in Explorer so the root folders are visible at a glance.

<details>
<summary><strong>Example Pack root files and folders</strong></summary>

```text
Example Pack\
├─ about.json
├─ pack.png
├─ TexturePackAssetReference.txt
├─ Blocks\
├─ Fonts\
├─ HUD\
├─ Inventory\
├─ Items\
├─ Models\
│  ├─ Doors\
│  ├─ Dragons\
│  ├─ Enemies\
│  ├─ Items\
│  └─ Player\
├─ ParticleEffects\
├─ Screens\
├─ Shaders\
├─ Skys\
└─ Sounds\
```

</details>

---

## Pack metadata and picker presentation

### Optional pack icon
Supported root filenames:
- `icon.png`
- `pack.png`
- `preview.png`
- `logo.png`

Also supports:
- `.jpg`
- `.jpeg`
- `.bmp`
- `.tga`

### Optional `about.json`
At the root of the pack:

```json
{
  "pack": {
    "description": "Line 1\nLine 2"
  }
}
```

### Description formatting
Descriptions support:
- `\n` line breaks
- up to **two displayed lines**
- `&` or `§` formatting codes

Supported styling:
- colors: `0-9`, `a-f`
- styles: `l` bold, `n` underline, `m` strikethrough, `o` italic, `r` reset

Example:

```json
{
  "pack": {
    "description": "&6Classic Pack\n&7Faithful vanilla-inspired visuals"
  }
}
```

<details>
<summary><strong>Exact <code>about.json</code> from the Example Pack</strong></summary>

```json
{
  "pack": {
    "description": "Example starter pack with folders and sample entries\nIncludes TexturePackAssetReference for naming help",
    "author": "Example",
    "version": "1.0.0"
  }
}
```

</details>

![Picker metadata placeholder](docs/images/texturepacks/picker-description-example.png)

**Image suggestion:** Show a pack entry with its root icon on the left and its `about.json` description visible on the right.

---

## Supported asset categories

<details>
<summary><strong>Blocks / terrain atlases</strong></summary>

TexturePacks supports direct tile index replacement and friendly block-name face replacement.

### Supported patterns
```text
Blocks\
  tile_2.png
  Grass_top.png
  Grass_side.png
  Grass_bottom.png
  Grass_all.png
```

### Notes
- `tile_###.png` is the fastest direct-index path for the color/diffuse tile.
- `tile_###_normalspec.png` targets the matching normal/spec slot for that same tile index.
- Friendly names mirror the engine's own face mappings.
- Top / side / bottom / all face naming is supported for both diffuse and `_normalspec` files.
- `_normalspec` files are optional companions. Use the same base name as the diffuse file and add `_normalspec` at the end.
- Near and far terrain atlases are both handled, and matching `_normalspec` companions are written to the terrain normal/spec atlases.

### Example files included in the Example Pack
```text
Blocks\
  TNT_bottom.png
  TNT_side.png
  TNT_top.png
  TNT_bottom_normalspec.png      // optional companion example
  TNT_side_normalspec.png        // optional companion example
  TNT_top_normalspec.png         // optional companion example
```

<details>
<summary><strong>Full Example Pack block reference list</strong></summary>

```text
Empty
Dirt
Grass
Sand
Lantern
FixedLantern
Rock
GoldOre
IronOre
CopperOre
CoalOre
DiamondOre
SurfaceLava
DeepLava
Bedrock
Snow
Ice
Log
Leaves
Wood
BloodStone
SpaceRock
IronWall
CopperWall
GoldenWall
DiamondWall
Torch
TorchPOSX
TorchNEGZ
TorchNEGX
TorchPOSZ
TorchPOSY
TorchNEGY
Crate
NormalLowerDoorClosedZ
NormalLowerDoorClosedX
NormalLowerDoor
NormalUpperDoorClosed
NormalLowerDoorOpenZ
NormalLowerDoorOpenX
NormalUpperDoorOpen
TNT
C4
Slime
SpaceRockInventory
GlassBasic
GlassIron
GlassStrong
GlassMystery
CrateStone
CrateCopper
CrateIron
CrateGold
CrateDiamond
CrateBloodstone
CrateSafe
SpawnPointBasic
SpawnPointBuilder
SpawnPointCombat
SpawnPointExplorer
StrongLowerDoorClosedZ
StrongLowerDoorClosedX
StrongLowerDoor
StrongUpperDoorClosed
StrongLowerDoorOpenZ
StrongLowerDoorOpenX
StrongUpperDoorOpen
LanternFancy
TurretBlock
LootBlock
LuckyLootBlock
BombBlock
EnemySpawnOn
EnemySpawnOff
EnemySpawnRareOn
EnemySpawnRareOff
EnemySpawnAltar
TeleportStation
CraftingStation
HellForge
AlienSpawnOn
AlienSpawnOff
HellSpawnOn
HellSpawnOff
BossSpawnOn
BossSpawnOff
EnemySpawnDim
EnemySpawnRareDim
AlienSpawnDim
HellSpawnDim
BossSpawnDim
AlienHordeOn
AlienHordeOff
AlienHordeDim
NumberOfBlocks
```

</details>

</details>

<details>
<summary><strong>Item icons</strong></summary>

Inventory icons can be replaced through multiple naming styles.

### Supported patterns
```text
Items\
  0007_Iron_Pickaxe.png
  Iron_Pickaxe.png
  0007.png
  Iron_Pickaxe_EnumName.png
  EnumName.png
```

### Notes
- Works with the game's 64px and 128px icon atlases.
- Supports ID + friendly-name + enum-name lookup patterns.

<details>
<summary><strong>Full Example Pack item icon reference list</strong></summary>

```text
BareHands
DirtBlock
SandBlock
RockBlock
LogBlock
WoodBlock
LanternBlock
BloodStoneBlock
SpaceRock
IronWall
CopperWall
GoldenWall
DiamondWall
Stick
Torch
Coal
CopperOre
IronOre
GoldOre
Diamond
Iron
Copper
Gold
StonePickAxe
CopperPickAxe
IronPickAxe
GoldPickAxe
DiamondPickAxe
BloodstonePickAxe
StoneSpade
CopperSpade
IronSpade
GoldSpade
DiamondSpade
StoneAxe
CopperAxe
IronAxe
GoldAxe
DiamondAxe
Compass
Clock
BrassCasing
IronCasing
GoldCasing
Bullets
IronBullets
GoldBullets
DiamondBullets
BloodStoneBullets
Knife
AssultRifle
Pistol
PumpShotgun
BoltActionRifle
SMGGun
GoldKnife
GoldAssultRifle
GoldPistol
GoldPumpShotgun
GoldBoltActionRifle
GoldSMGGun
DiamondKnife
DiamondAssultRifle
DiamondPistol
DiamondPumpShotgun
DiamondBoltActionRifle
DiamondSMGGun
BloodStoneKnife
BloodStoneAssultRifle
BloodStonePistol
BloodStonePumpShotgun
BloodStoneBoltActionRifle
BloodStoneSMGGun
Crate
Snow
Ice
Door
GPS
TeleportGPS
TNT
C4
SpaceKnife
IronSpaceAssultRifle
IronSpacePistol
IronSpacePumpShotgun
IronSpaceBoltActionRifle
IronSpaceSMGGun
CopperSpaceAssultRifle
CopperSpacePistol
CopperSpacePumpShotgun
CopperSpaceBoltActionRifle
CopperSpaceSMGGun
GoldSpaceAssultRifle
GoldSpacePistol
GoldSpacePumpShotgun
GoldSpaceBoltActionRifle
GoldSpaceSMGGun
DiamondSpaceAssultRifle
DiamondSpacePistol
DiamondSpacePumpShotgun
DiamondSpaceBoltActionRifle
DiamondSpaceSMGGun
Slime
RocketLauncher
RocketLauncherGuided
RocketAmmo
GunPowder
SpaceRockInventory
ExplosivePowder
LaserBullets
Grenade
DiamondCasing
RocketLauncherShotFired
RocketLauncherGuidedShotFired
IronLaserSword
CopperLaserSword
GoldLaserSword
DiamondLaserSword
BloodStoneLaserSword
LMGGun
GoldLMGGun
DiamondLMGGun
BloodStoneLMGGun
Chainsaw1
Chainsaw2
Chainsaw3
StickyGrenade
StoneContainer
CopperContainer
IronContainer
GoldContainer
DiamondContainer
BloodstoneContainer
SpawnBasic
SpawnCombat
SpawnExplorer
SpawnBuilder
GlassWindowWood
GlassWindowIron
GlassWindowGold
GlassWindowDiamond
BasicGrenadeLauncher
AdvancedGrenadeLauncher
LaserDrill
MegaPickAxe
Snowball
Iceball
DiamondDoor
LanternFancyBlock
PrecisionLaser
MultiLaser
TeleportStation
MonsterBlock
IronDoor
TechDoor
```

</details>

</details>

<details>
<summary><strong>Held item model skins</strong></summary>

These replace the texture used by held items without changing the actual model.

### Supported patterns
```text
Models\Items\
  0007_Iron_Pickaxe_model.png
  Iron_Pickaxe_model.png
  Iron_Pickaxe_EnumName_model.png
  EnumName_model.png
```

### Fallback-style matches
If no `*_model` file exists, some name-based fallbacks can still be treated as model skins.

<details>
<summary><strong>Full Example Pack held-item model reference list</strong></summary>

```text
BareHands
DirtBlock
SandBlock
RockBlock
LogBlock
WoodBlock
LanternBlock
BloodStoneBlock
SpaceRock
IronWall
CopperWall
GoldenWall
DiamondWall
Stick
Torch
Coal
CopperOre
IronOre
GoldOre
Diamond
Iron
Copper
Gold
StonePickAxe
CopperPickAxe
IronPickAxe
GoldPickAxe
DiamondPickAxe
BloodstonePickAxe
StoneSpade
CopperSpade
IronSpade
GoldSpade
DiamondSpade
StoneAxe
CopperAxe
IronAxe
GoldAxe
DiamondAxe
Compass
Clock
BrassCasing
IronCasing
GoldCasing
Bullets
IronBullets
GoldBullets
DiamondBullets
BloodStoneBullets
Knife
AssultRifle
Pistol
PumpShotgun
BoltActionRifle
SMGGun
GoldKnife
GoldAssultRifle
GoldPistol
GoldPumpShotgun
GoldBoltActionRifle
GoldSMGGun
DiamondKnife
DiamondAssultRifle
DiamondPistol
DiamondPumpShotgun
DiamondBoltActionRifle
DiamondSMGGun
BloodStoneKnife
BloodStoneAssultRifle
BloodStonePistol
BloodStonePumpShotgun
BloodStoneBoltActionRifle
BloodStoneSMGGun
Crate
Snow
Ice
Door
GPS
TeleportGPS
TNT
C4
SpaceKnife
IronSpaceAssultRifle
IronSpacePistol
IronSpacePumpShotgun
IronSpaceBoltActionRifle
IronSpaceSMGGun
CopperSpaceAssultRifle
CopperSpacePistol
CopperSpacePumpShotgun
CopperSpaceBoltActionRifle
CopperSpaceSMGGun
GoldSpaceAssultRifle
GoldSpacePistol
GoldSpacePumpShotgun
GoldSpaceBoltActionRifle
GoldSpaceSMGGun
DiamondSpaceAssultRifle
DiamondSpacePistol
DiamondSpacePumpShotgun
DiamondSpaceBoltActionRifle
DiamondSpaceSMGGun
Slime
RocketLauncher
RocketLauncherGuided
RocketAmmo
GunPowder
SpaceRockInventory
ExplosivePowder
LaserBullets
Grenade
DiamondCasing
RocketLauncherShotFired
RocketLauncherGuidedShotFired
IronLaserSword
CopperLaserSword
GoldLaserSword
DiamondLaserSword
BloodStoneLaserSword
LMGGun
GoldLMGGun
DiamondLMGGun
BloodStoneLMGGun
Chainsaw1
Chainsaw2
Chainsaw3
StickyGrenade
StoneContainer
CopperContainer
IronContainer
GoldContainer
DiamondContainer
BloodstoneContainer
SpawnBasic
SpawnCombat
SpawnExplorer
SpawnBuilder
GlassWindowWood
GlassWindowIron
GlassWindowGold
GlassWindowDiamond
BasicGrenadeLauncher
AdvancedGrenadeLauncher
LaserDrill
MegaPickAxe
Snowball
Iceball
DiamondDoor
LanternFancyBlock
PrecisionLaser
MultiLaser
TeleportStation
MonsterBlock
IronDoor
TechDoor
```

</details>

</details>

<details>
<summary><strong>Held item model geometry overrides</strong></summary>

These replace the actual XNB model used by held items.

### Recommended layout
```text
Models\Items\0051_Pistol_model\
  0051_Pistol_model.xnb
  texture.xnb
```

### Notes
- Per-item subfolders are strongly recommended.
- This helps avoid dependency collisions on generic names like `texture.xnb`.
- Scale, origin, and bounds should remain compatible with vanilla expectations.

![Held model placeholder](docs/images/texturepacks/example-held-model-layout.png)

**Image suggestion:** Show one item model folder expanded with its `.xnb` and dependency files next to each other.

</details>

<details>
<summary><strong>Door / Player / enemy / dragon model skins</strong></summary>

### Doors
```text
Models\Doors\
  NormalDoor.png
  IronDoor.png
  DiamondDoor.png
  TechDoor.png
```

### Player
```text
Models\Player\
  Player.png
  Player_model.png
  SWATMale.png
```

### Enemies
```text
Models\Enemies\
  ZOMBIE.png
  ZOMBIE_0_0.png
```

### Dragons
```text
Models\Dragons\
  FIRE.png
  FOREST.png
  ICE.png
  LIZARD.png
  SKELETON.png
```

<details>
<summary><strong>Full Example Pack player texture list</strong></summary>

```text
SWATMale
```

</details>

<details>
<summary><strong>Full Example Pack enemy texture list</strong></summary>

```text
ZOMBIE_0_0
ZOMBIE_0_1
ZOMBIE_0_2
ZOMBIE_0_3
ZOMBIE_0_4
ZOMBIE_0_5
ZOMBIE_0_6
ZOMBIE_1_0
ZOMBIE_1_1
ZOMBIE_1_2
ZOMBIE_1_3
ZOMBIE_1_4
ZOMBIE_1_5
ZOMBIE_1_6
ZOMBIE_2_0
ZOMBIE_2_1
ZOMBIE_2_3
ZOMBIE_2_4
ARCHER_0_0
ARCHER_0_1
ARCHER_0_2
ARCHER_0_3
ARCHER_0_4
ARCHER_1_0
ARCHER_1_1
ARCHER_1_2
SKEL_0_0
SKEL_0_1
SKEL_0_2
SKEL_0_3
SKEL_0_4
SKEL_SWORD_0_0
SKEL_1_0
SKEL_SWORD_0_1
SKEL_1_1
SKEL_SWORD_0_2
SKEL_1_2
SKEL_SWORD_0_3
SKEL_AXES_0_0
SKEL_SWORD_0_4
SKEL_AXES_0_1
SKEL_SWORD_1_0
SKEL_AXES_0_2
SKEL_SWORD_1_1
SKEL_AXES_0_3
SKEL_SWORD_1_2
SKEL_AXES_0_4
SKEL_AXES_1_0
SKEL_AXES_1_1
SKEL_AXES_1_2
FELGUARD
HELL_LORD
ALIEN
TREASURE_ZOMBIE
ANTLER_BEAST
REAPER
```

</details>

<details>
<summary><strong>Full Example Pack dragon texture list</strong></summary>

```text
FIRE
FOREST
LIZARD
ICE
SKELETON
```

</details>

</details>

<details>
<summary><strong>Player / enemy / dragon geometry overrides</strong></summary>

### Player
```text
Models\Player\
  Player.xnb
  Player_model.xnb
  SWATMale.xnb
  Player\Player.xnb
  Player\Model.xnb
  Player_model\Player_model.xnb
```

### Enemies
```text
Models\Enemies\
  ZOMBIE.xnb
  ZOMBIE\ZOMBIE.xnb
  ZOMBIE\Model.xnb
```

### Dragons
```text
Models\Dragons\
  DragonBody.xnb
  DragonFeet.xnb
  DragonBody\DragonBody.xnb
  DragonFeet\Model.xnb
```

### Notes
- Vanilla animation tags are grafted where needed so clip lookups remain stable.
- Live entities are rebound before old pack content is retired to reduce disposed-effect crashes.

</details>

<details>
<summary><strong>Screens and UI sprites</strong></summary>

### Screens
```text
Screens\
  MenuBack.png
  Logo.png
  DialogBack.png
  LoadScreen.png
```

### HUD
```text
HUD\
  DamageArrow.png
  HudGrid.png
  Selector.png
  CrossHair.png
  CrossHairTick.png
  StaminaBarEmpty.png
  HealthBarEmpty.png
  HealthBarFull.png
  BubbleBar.png
  SniperScope.png
  MissileLocking.png
  MissileLock.png
```

### Inventory
```text
Inventory\
  BlockUIBack.png
  Selector.png
  SingleGrid.png
  Tier2Back.png
  HudGrid.png
  CraftSelector.png
  InventoryGrid.png
```

<details>
<summary><strong>Full Example Pack screen list</strong></summary>

```text
MenuBack
Logo
DialogBack
LoadScreen
```

</details>

<details>
<summary><strong>Full Example Pack HUD list</strong></summary>

```text
DamageArrow
HudGrid
Selector
CrossHair
CrossHairTick
StaminaBarEmpty
HealthBarEmpty
HealthBarFull
BubbleBar
SniperScope
MissileLocking
MissileLock
```

</details>

<details>
<summary><strong>Full Example Pack inventory list</strong></summary>

```text
BlockUIBack
Selector
SingleGrid
Tier2Back
HudGrid
CraftSelector
InventoryGrid
```

</details>

![UI placeholder](docs/images/texturepacks/example-ui-assets.png)

**Image suggestion:** Show the Example Pack folder open beside a live screenshot of the matching HUD and inventory areas in-game.

</details>

<details>
<summary><strong>Fonts</strong></summary>

Fonts are generated at runtime from `.ttf` and `.otf`.

### Supported pattern
```text
Fonts\
  _consoleFont.ttf
  _largeFont.ttf
  _medFont.ttf
  _medLargeFont.ttf
  _myriadLarge.ttf
  _myriadMed.ttf
  _myriadSmall.ttf
  _nameTagFont.ttf
  _smallFont.ttf
  _systemFont.ttf
  DebugFont.ttf
  GameModeMenu.ttf
  MainMenu.ttf
```

### Notes
- Font spacing and line spacing are matched to vanilla as closely as possible.
- UI trees are rebound to the new fonts after pack switches.
- Italic formatting in picker descriptions can use an italic runtime font when available.

<details>
<summary><strong>Full Example Pack font field list</strong></summary>

```text
_consoleFont
_largeFont
_medFont
_medLargeFont
_myriadLarge
_myriadMed
_myriadSmall
_nameTagFont
_smallFont
_systemFont
DebugFont
GameModeMenu
MainMenu
```

</details>

</details>

<details>
<summary><strong>Sounds and music</strong></summary>

### Supported root
```text
Sounds\
  Click.wav
  Theme.mp3
```

### Notes
- Supports `.wav` and `.mp3`
- Handles one-shot SFX, ambience loops, and music shadow playback
- Cue names should match the game's sound names

<details>
<summary><strong>Full Example Pack sound cue list</strong></summary>

```text
Click
Error
Award
Popup
Teleport
Reload
BulletHitHuman
thunderBig
craft
dropitem
pickupitem
punch
punchMiss
arrow
AssaultReload
Shotgun
ShotGunReload
Song1
Song2
lostSouls
CreatureUnearth
HorrorStinger
Fireball
Iceball
DoorClose
DoorOpen
Song5
Song3
Song4
Song6
locator
Fuse
LaserGun1
LaserGun2
LaserGun3
LaserGun4
LaserGun5
Beep
SolidTone
RPGLaunch
Alien
SpaceTheme
GrenadeArm
RocketWhoosh
LightSaber
LightSaberSwing
GroundCrash
ZombieDig
ChainSawIdle
ChainSawSpinning
ChainSawCutting
Birds
FootStep
Theme
Pick
Place
Crickets
Drips
BulletHitDirt
GunShot1
GunShot2
GunShot3
GunShot4
BulletHitSpray
thunderLow
Sand
leaves
dirt
Skeleton
ZombieCry
ZombieGrowl
Hit
Fall
Douse
DragonScream
Explosion
WingFlap
DragonFall
Freeze
Felguard
```

</details>

</details>

<details>
<summary><strong>Shaders</strong></summary>

### Supported pattern
```text
Shaders\
  blockEffect.fxb
```

### Notes
- Overrides the terrain shader
- Falls back to the vanilla block effect when no override exists
- Uses safe effect replacement and retirement handling

<details>
<summary><strong>Exact Example Pack shader list</strong></summary>

```text
blockEffect
```

</details>

</details>

<details>
<summary><strong>Skies</strong></summary>

### Supported cubemaps
```text
Skys\
  ClearSky_px.png
  ClearSky_nx.png
  ClearSky_py.png
  ClearSky_ny.png
  ClearSky_pz.png
  ClearSky_nz.png
```

Also supports:
- `NightSky_*`
- `SunSet_*`
- `DawnSky_*`

### Notes
- All six faces are expected for a given cubemap override
- If a face is missing, that sky falls back to vanilla

<details>
<summary><strong>Full Example Pack sky list</strong></summary>

```text
ClearSky_px
ClearSky_nx
ClearSky_py
ClearSky_ny
ClearSky_pz
ClearSky_nz
NightSky_px
NightSky_nx
NightSky_py
NightSky_ny
NightSky_pz
NightSky_nz
SunSet_px
SunSet_nx
SunSet_py
SunSet_ny
SunSet_pz
SunSet_nz
DawnSky_px
DawnSky_nx
DawnSky_py
DawnSky_ny
DawnSky_pz
DawnSky_nz
```

</details>

</details>

<details>
<summary><strong>ParticleEffects</strong></summary>

### Supported content
```text
ParticleEffects\
  *.xnb
  SomeTexture.png
```

### Notes
- Export tooling can dump ParticleEffects XNBs from vanilla content
- If a ParticleEffects XNB is actually a `Texture2D`, it can also be exported as PNG
- Many ParticleEffects assets are data-driven rather than plain textures

<details>
<summary><strong>Exact Example Pack particle effects note</strong></summary>

```text
All: ParticleEffects asset names.
```

</details>

</details>

<details>
<summary><strong>Pack image and metadata reference</strong></summary>

### Pack image names
```text
icon
pack
preview
logo
```

### Supported pack image extensions
```text
.png
.jpg
.jpeg
.bmp
.tga
```

### Example Pack root image actually included
```text
pack.png
```

### Example asset reference file summary
```text
Blocks:     [.png]        tile_2, Grass_top, Grass_side, Grass_bottom, Grass_all
Fonts:      [.ttf | .otf] _consoleFont, _largeFont, _medFont, _medLargeFont, _myriadLarge, _myriadMed, _myriadSmall, _nameTagFont, _smallFont, _systemFont, DebugFont, GameModeMenu, MainMenu
HUD:        [.png]        DamageArrow, HudGrid, Selector, CrossHair, CrossHairTick, StaminaBarEmpty, HealthBarEmpty, HealthBarFull, BubbleBar, SniperScope, MissileLocking, MissileLock
Inventory:  [.png]        BlockUIBack, Selector, SingleGrid, Tier2Back, HudGrid, CraftSelector, InventoryGrid
Items:      [.png]        0025_Iron_Pickaxe, Iron_Pickaxe, 0025, Iron_Pickaxe_EnumName, EnumName
Screens:    [.png]        LoadScreen, MenuBack, Logo, DialogBack
Sounds:     [.mp3 | .wav] All supported cue names listed in the Example Pack
Models:
 * Doors:   [.png | .xnb] NormalDoor, IronDoor, DiamondDoor, TechDoor | PNG = full-sheet texture, XNB = geometry override; flat and per-folder XNB layouts supported
 * Items:   [.png | .xnb] 0025_Iron_Pickaxe_model, Iron_Pickaxe_model, Iron_Pickaxe, 0025, Iron_Pickaxe_EnumName_model, EnumName_model, EnumName
 * Player:  [.png | .xnb] Player, Player_model, SWATMale
 * Enemies: [.png | .xnb] ZOMBIE_0_0, ZOMBIE
 * Dragons: [.png | .xnb] FIRE, FOREST, ICE, LIZARD, SKELETON
Shaders:    [.fxb]        blockEffect
Skys:       [.png]        ClearSky_*, NightSky_*, SunSet_*, DawnSky_*
ParticleEffects: exported XNB assets and any Texture2D PNGs
PackAbout:  [.json]       about.json with description, styling, and picker notes
```

</details>

---

## In-game picker behavior

The picker is designed to feel like a proper front-end feature, not a debug menu.

### What it does
- lists pack folders under `!Mods\TexturePacks\`
- excludes folders beginning with `_`
- inserts a built-in **(default)** row at the top
- shows optional pack icons
- shows optional pack descriptions
- remembers the current active selection from config

### Controls
- **Mouse wheel**: scroll
- **Left click**: select
- **Double click**: apply
- **Up / Down**: move selection
- **Enter**: apply
- **Escape**: close

### Menu placement
- **Main Menu:** injects **Texture Packs** near the options area
- **In-Game Menu:** injects **Texture Packs** above **Inventory**

![Picker controls placeholder](docs/images/texturepacks/picker-controls.png)

**Image suggestion:** Show the picker with one row selected and the Apply / Refresh / Close buttons visible.

---

## Export tools for pack authors

TexturePacks includes a full export pipeline intended to make pack creation easier.

### Export command
```text
/tpexportall
```

### Default export hotkey
```text
Ctrl+Shift+F3
```

### Export root
```text
!Mods\TexturePacks\_Extracted\YYYYMMDD_HHMMSS\
```

### What gets exported
- Screens
- Blocks, including diffuse terrain references and optional `_normalspec` companion naming guidance
- HUD sprites
- item icons
- item model skins
- player / enemy / dragon model textures
- extra model references when discovered
- fonts
- sounds
- terrain shader FXB
- sky textures
- ParticleEffects XNBs and PNGs when applicable
- broader content XNB dumps

### Why it matters
Instead of guessing internal names or rebuilding lists by hand, authors can inspect actual exported references and build packs around real content.

![Export tools placeholder](docs/images/texturepacks/exported-assets.png)

**Image suggestion:** Show the `_Extracted` output folder with multiple categories visible after running `/tpexportall`.

### Companion authoring tools
TexturePacks also pairs well with the bundled **FBX → XNB** helper tooling used to turn edited models back into XNA-ready assets for packs.

![Author tools placeholder](docs/images/texturepacks/author-tools-fbxtoxnb.png)

**Image suggestion:** Show the `_FbxToXnb` folder opened in Explorer next to a custom FBX model and the generated per-model output folder.

**Multi-texture FBX note:** `_FbxToXnb` can stage multiple texture files for a single FBX.  
Keep referenced textures beside the `.fbx` file, and if the FBX was exported with relative paths like `textures/texture_0.png`, keep that folder structure as well. The converter is designed to preserve common relative texture lookups during the XNB build process.

#### What the companion tools are for
- converting edited `.fbx` models back into `.xnb`
- supporting drag-and-drop conversion for quick pack iteration
- building each model into its own output folder to avoid dependency collisions
- supporting both **normal** and **skinned** model workflows
- preserving CastleMiner Z / DNA runtime expectations for skinned content through the custom processor

<details>
<summary><strong>Bundled runtime tool layout under <code>TexturePacks\_FbxToXnb\</code></strong></summary>

```text
TEXTUREPACKS\
└── _FbxToXnb\
    │   FbxToXnb.exe
    │   FbxToXnb_Drop_Normal.bat
    │   FbxToXnb_Drop_Skinned.bat
    │   README.txt
    │
    └── SkinedModelProcessor\
            DNA.Common.dll
            DNA.SkinnedPipeline.dll
            Microsoft.Xna.Framework.Content.Pipeline.dll
            Microsoft.Xna.Framework.Game.dll
            Microsoft.Xna.Framework.Graphics.dll
            Microsoft.Xna.Framework.Xact.dll
```
</details>

<details>
<summary><strong>Source tool projects in the CastleForge repository</strong></summary>

```text
CastleForge\
└── Tools\
    ├── FbxToXnb\
    │   ├── Core\
    │   │   ├── BuildEngine.cs
    │   │   └── XNBBuilder.cs
    │   ├── Embedded\
    │   │   └── XNA Game Studio Shared.msi
    │   ├── Setup\
    │   │   └── XnaGseInstaller.cs
    │   ├── FbxToXnb.csproj
    │   ├── FbxToXnb_Drop_Normal.bat
    │   ├── Program.cs
    │   └── README.txt
    │
    └── DNA.SkinnedPipeline\
        ├── Processors\
        │   └── SkinedModelProcessor.cs
        ├── Writers\
        │   ├── AnimationClipWriter.cs
        │   └── SkeletonWriter.cs
        ├── DNA.SkinnedPipeline.csproj
        └── FbxToXnb_Drop_Skinned.bat
```
</details>

#### Tool workflow at a glance
1. Export or author a model in Blender or another DCC tool.
2. Save the mesh as `.fbx`.
3. Drop the file onto **`FbxToXnb_Drop_Normal.bat`** for standard models.
4. Drop the file onto **`FbxToXnb_Drop_Skinned.bat`** for skinned models that need the custom `SkinedModelProcessor` pipeline.
5. Copy the generated output folder into the matching TexturePacks model path.

#### Why the per-model output folder matters
The converter intentionally builds each FBX into its own folder. This avoids collisions on common dependency names such as `texture.xnb` or `texture_0.xnb`, which is especially important when a pack includes several custom item or creature models.

#### Skinned pipeline notes
The bundled `SkinedModelProcessor` extends the normal XNA `ModelProcessor` flow by attaching the minimum DNA/CMZ skinning metadata the runtime expects, including:
- skeleton data
- inverse bind pose matrices
- a runtime-safe animation clip dictionary placeholder

That makes it useful for models that need to behave like CastleMiner Z skinned assets instead of plain static geometry.

---

## Model authoring workflow

One of TexturePacks' strongest features is its support for **real model round-tripping**.

### Recommended workflow
1. Export a vanilla model as **GLB**
2. Import the GLB into Blender
3. Preserve required node and bone names
4. Edit meshes or textures
5. Export to FBX
6. Rebuild to XNB with an XNA-compatible content pipeline
7. Place the model and its dependencies into the appropriate pack folder

### Why GLB is recommended
GLB keeps:
- node hierarchy
- bone names
- mesh structure

That helps preserve required names such as `BarrelTip`, which may be looked up at runtime.

### Common causes of broken custom models
- missing required bone/node names
- broken pivot/origin alignment
- incorrect scale
- empty meshes or zero bounding radius
- shared dependency file name collisions across items

### Best practice
Prefer **per-model folders** such as:

```text
Models\Items\0051_Pistol_model\
  0051_Pistol_model.xnb
  texture_0.xnb
```

![Model workflow placeholder](docs/images/texturepacks/model-authoring-workflow.png)

**Image suggestion:** A three-part visual showing GLB export, Blender editing, and final XNB placement into the pack folder.

---

## Sound export notes

TexturePacks also contains sound export helpers for wavebanks.

### Optional FFmpeg support
An optional helper folder may be created here:

```text
!Mods\TexturePacks\_ffmpeg\
```

If `ffmpeg.exe` is available there, the exporter can better handle certain conversions such as xWMA workflows. If not, exports still proceed where possible and non-converted assets can be handled manually afterward.

---

## Files and folders created by this mod

### Runtime / user-facing
```text
!Mods\TexturePacks\
!Mods\TexturePacks\TexturePacks.Config.ini
!Mods\TexturePacks\_Extracted\
!Mods\TexturePacks\_ffmpeg\
```

### Pack-facing
```text
!Mods\TexturePacks\<PackName>\
```

### Notes
- `!Mods\TexturePacks\_Extracted\` holds author exports
- `!Mods\TexturePacks\_ffmpeg\` may contain an optional FFmpeg helper README and executable
- folders beginning with `_` are hidden from the pack picker

---

## Included example content

The mod package is accompanied by an **Example Pack** that demonstrates:
- pack root layout
- `about.json`
- root pack image usage (`pack.png` / `icon.*` style workflow)
- block, item, HUD, inventory, screen, sound, shader, sky, and particle-effect naming references
- model categories for doors, held items, player, enemies, and dragons
- starter text files that act as commented reference sheets rather than runtime-required files
- a simple real replacement example (`Blocks\TNT_top.png`, `TNT_side.png`, `TNT_bottom.png`)

This makes TexturePacks much easier to learn than a blank template.

![Example pack placeholder](docs/images/texturepacks/example-pack-overview.png)

**Image suggestion:** Show the Example Pack root beside a text editor open to `TexturePackAssetReference.txt`.

**What the reference files are for:** the `.txt` files inside the Example Pack are there to document naming patterns, common stems, and supported categories. They are safe to delete from a real pack once you no longer need the notes.

<details>
<summary><strong>Full Example Pack file listing</strong></summary>

```text
Example Pack/
├─ about.json
├─ Blocks/
│  ├─ BlockList.txt
│  ├─ TNT_bottom.png
│  ├─ TNT_side.png
│  └─ TNT_top.png
├─ Fonts/
│  └─ FontList.txt
├─ HUD/
│  └─ HUDList.txt
├─ Inventory/
│  └─ InventoryList.txt
├─ Items/
│  └─ ItemList.txt
├─ Models/
│  ├─ Doors/
│  │  └─ DoorsList.txt
│  ├─ Dragons/
│  │  └─ DragonList.txt
│  ├─ Enemies/
│  │  └─ EnemyList.txt
│  ├─ Items/
│  │  └─ ItemList.txt
│  └─ Player/
│     └─ PlayerList.txt
├─ pack.png
├─ ParticleEffects/
│  └─ ParticalEffectsList.txt
├─ Screens/
│  └─ ScreenList.txt
├─ Shaders/
│  └─ ShadersList.txt
├─ Skys/
│  └─ SkysList.txt
├─ Sounds/
│  └─ SoundList.txt
└─ TexturePackAssetReference.txt
```

</details>

---

## Developer / repository notes

TexturePacks also includes preserved converter utilities in `UnusedConverters`, such as:
- `Xwb360SoundExtractor`
- `CMZ360ToPCWorldConverter`

These are useful repository-side references but are not the main player-facing runtime features of the mod.

---

## Troubleshooting

<details>
<summary><strong>A pack shows up, but some assets do not change</strong></summary>

Usually this means the filenames do not match the patterns expected by the resolver, or the asset type is incomplete for that category.

Check:
- folder location
- file naming
- extension type
- capitalization consistency
- whether the asset requires multiple related files
- whether the asset is a skin replacement or a geometry override

</details>

<details>
<summary><strong>The pack picker does not list my pack</strong></summary>

Check:
- your pack is inside `!Mods\TexturePacks\`
- the folder does not begin with `_`
- the mod loaded successfully
- the pack folder is not nested one level too deep

</details>

<details>
<summary><strong>A custom model looks huge, tiny, offset, or broken in-hand</strong></summary>

The most common causes are:
- transform / pivot mismatch
- wrong export scale
- broken bounds
- missing expected bones/nodes
- dependency collisions from shared `texture.xnb` names

Try:
- applying transforms before export
- using the `FbxComp` setting
- exporting to a dedicated per-model folder
- verifying bone names exactly match vanilla expectations

</details>

<details>
<summary><strong>Switching packs causes model-related crashes</strong></summary>

This is exactly the sort of issue TexturePacks is built to guard against. It captures baselines, restores them, and rebinds live entities before old content managers are retired. If you still hit a crash, the most likely source is an invalid custom XNB, missing dependencies, or a malformed model export.

</details>

<details>
<summary><strong>My sky override only partially works</strong></summary>

Sky cubemaps need all required faces for a given override. If one or more faces are missing, that sky may fall back to vanilla.

</details>

---

## Summary

TexturePacks is one of the most ambitious CastleForge mods because it treats texture packs as full **content packs**, not just atlas edits. It gives players a cleaner way to browse and switch styles, while giving creators a serious pipeline for exporting, authoring, testing, and shipping complete visual overhauls for CastleMiner Z.
# VeinMiner

> Break one ore block and let the rest of the vein come with it.

![VeinMiner Preview](_Images/Preview.gif)

---

## Overview

**VeinMiner** is a quality-of-life mining mod for CastleMiner Z that automatically mines the rest of a connected ore vein after you successfully break the first matching block with a pick-type tool.

The goal is simple: reduce repetitive mining, keep resource gathering feeling smooth, and still stay controlled through clear safety limits and per-ore toggles.

Unlike a flashy content mod, VeinMiner focuses on one job and does it cleanly:

- Starts from the ore block you mined.
- Searches outward for connected matching ore blocks.
- Mines additional matching blocks automatically.
- Spawns drops using the tool’s normal vanilla drop behavior.
- Respects configurable safety caps so it does not run away through huge terrain sections.

---

## Why this mod stands out

- **Fast and lightweight** with no menus or command spam.
- **Automatic to use** once installed.
- **Pick-only behavior** keeps it tied to normal mining tools.
- **Per-ore toggles** let you decide what should vein-mine and what should not.
- **Safety-first limits** bound how far the search can go and how many extra blocks can be removed.
- **Hot-reload support** lets you update the config while in game without restarting.

![Vein Mining](_Images/VeinMining.gif)

---

## What VeinMiner currently supports

VeinMiner is currently set up to recognize these block types as vein-minable:

| Block Type | Enabled by Default | Notes |
|---|---:|---|
| Gold Ore | Yes | Can be turned off in config |
| Iron Ore | Yes | Can be turned off in config |
| Copper Ore | Yes | Can be turned off in config |
| Coal Ore | Yes | Can be turned off in config |
| Diamond Ore | Yes | Can be turned off in config |
| Slime | Yes | Treated as a vein-minable resource type |

### Tool requirement

VeinMiner currently only triggers when using a **pick-type inventory item**.

That means the mod is intended for normal mining tools, not every possible item in the game.

---

## How it works in play

1. You mine a supported ore block with a valid pick-type tool.
2. Once the game removes the original block, VeinMiner looks at the surrounding connected blocks.
3. It searches **orthogonally** in six directions:
   - left / right
   - up / down
   - forward / backward
4. Matching connected blocks of the same type are gathered into a vein.
5. The mod orders those blocks and mines extra ones up to your configured caps.
6. Item drops are spawned using the tool’s normal `CreatesWhenDug(...)` behavior for consistency with vanilla-style drops.

### Important behavior notes

- Vein connection is based on **6-direction adjacency**, not diagonals.
- The original block you manually mined is handled by vanilla first.
- The mod then mines **extra matching blocks** from the connected vein.
- The extra mined count is capped by your config.

---

## Feature breakdown

<details>
<summary><strong>Automatic vein mining</strong></summary>

Break one supported ore block with a valid pick and the mod automatically continues through the connected vein without needing commands, a separate tool mode, or an on-screen menu.

</details>

<details>
<summary><strong>Per-ore enable/disable toggles</strong></summary>

You can individually toggle these resource types on or off in the config:

- Gold Ore
- Iron Ore
- Copper Ore
- Coal Ore
- Diamond Ore
- Slime

This is useful if you want vein mining for common materials but prefer rarer resources to stay manual.

</details>

<details>
<summary><strong>Bounded flood-fill search</strong></summary>

The connected-vein search is intentionally limited so the mod stays controlled and predictable.

It uses three separate safety controls:

- **MaxTraversalCells** – maximum number of cells the search may visit.
- **MaxBlocksToMine** – maximum number of extra blocks VeinMiner may remove after the first block.
- **MaxAxisRadius** – maximum distance allowed from the original mined block on each axis.

These limits help prevent huge scans or excessive chain-mining through very large deposits.

</details>

<details>
<summary><strong>Near-first mining order</strong></summary>

After discovering a connected vein, VeinMiner orders the blocks before removal.

It prioritizes:

1. blocks nearest to the original mined block
2. higher blocks first when distance ties occur

This gives the behavior a more controlled and natural feel instead of removing blocks in a random order.

</details>

<details>
<summary><strong>Hot-reload config keybind</strong></summary>

The mod includes a configurable reload hotkey so you can edit the INI file and reapply settings while in game.

**Default:** `Ctrl+Shift+R`

When triggered, the mod reloads the config and reapplies runtime settings without requiring a full restart.

</details>

<details>
<summary><strong>Optional feedback and logging</strong></summary>

You can enable:

- **DoAnnouncement** – shows a player-facing in-game message after vein mining.
- **DoLogging** – writes vein mining details to the log.

By default, both are off for a cleaner experience.

</details>

---

## Installation

### Requirements

- CastleMiner Z
- CastleForge ModLoader
- Any required core dependencies used by your CastleForge setup

### Install steps

1. Install and verify your CastleForge ModLoader setup.
2. Place the VeinMiner mod assembly in your `!Mods` directory.
3. Launch the game once.
4. VeinMiner will create its config folder and config file automatically.
5. Edit the config if desired, then relaunch or hot-reload it in game.

### Expected config path

```text
!Mods\VeinMiner\VeinMiner.Config.ini
```

### Files created / used

```text
!Mods/
└── VeinMiner/
    └── VeinMiner.Config.ini
```

> Depending on your packaging flow, the main VeinMiner DLL itself may sit directly in `!Mods`, while VeinMiner’s config folder lives under `!Mods\VeinMiner`.

![Installation](_Images/VeinMinerInstall.png)

---

## Using the mod

VeinMiner does not require commands, extra setup inside the game, or a visible menu.

### Basic use

- Equip a valid **pick-type** tool.
- Break a supported ore/resource block.
- If the block type is enabled and connected blocks are found, VeinMiner will mine the rest of the vein up to your configured limits.

### When it will not trigger

VeinMiner will not run when:

- the mod is disabled in config
- the tool is not a pick-type tool
- the block type is not on the supported whitelist
- the dig was not considered effective
- the terrain is not ready
- no connected matching blocks are found

---

## Configuration

VeinMiner automatically creates an INI config file on first launch.

### Default config

```ini
# VeinMiner - Configuration
# Lines starting with ';' or '#' are comments.

[VeinMiner]
; Master toggle for the entire mod.
Enabled = true

[Safety]
; Maximum number of cells the bounded flood-fill may visit.
MaxTraversalCells = 512
; Hard cap on extra ore/resource blocks mined after the first block.
MaxBlocksToMine   = 384
; Maximum axis distance from the originally mined ore block.
MaxAxisRadius     = 24

[Ores]
; Toggle specific ore/resource types on or off.
MineGoldOre    = true
MineIronOre    = true
MineCopperOre  = true
MineCoalOre    = true
MineDiamondOre = true
MineSlime      = true

[Logging]
; Show an in-game announcement to the player.
DoAnnouncement = false
; Write the action to the log.
DoLogging      = false

[Hotkeys]
; Reload this config while in-game:
ReloadConfig = Ctrl+Shift+R
```

### Config reference

| Section | Key | Default | What it does |
|---|---|---|---|
| VeinMiner | `Enabled` | `true` | Master on/off switch for the mod |
| Safety | `MaxTraversalCells` | `512` | Maximum number of cells the search may visit |
| Safety | `MaxBlocksToMine` | `384` | Hard cap on extra blocks VeinMiner may mine after the first one |
| Safety | `MaxAxisRadius` | `24` | Per-axis search limit from the original mined block |
| Ores | `MineGoldOre` | `true` | Enables or disables Gold Ore vein mining |
| Ores | `MineIronOre` | `true` | Enables or disables Iron Ore vein mining |
| Ores | `MineCopperOre` | `true` | Enables or disables Copper Ore vein mining |
| Ores | `MineCoalOre` | `true` | Enables or disables Coal Ore vein mining |
| Ores | `MineDiamondOre` | `true` | Enables or disables Diamond Ore vein mining |
| Ores | `MineSlime` | `true` | Enables or disables Slime vein mining |
| Logging | `DoAnnouncement` | `false` | Shows an in-game message after a vein mining action |
| Logging | `DoLogging` | `false` | Writes vein mining actions to the mod log |
| Hotkeys | `ReloadConfig` | `Ctrl+Shift+R` | Reloads the config while in game |

### Valid config ranges

The mod clamps several safety values into safe ranges when loading:

- `MaxTraversalCells`: **32** to **8192**
- `MaxBlocksToMine`: **1** to **4096**
- `MaxAxisRadius`: **1** to **128**

---

## Recommended configuration examples

<details>
<summary><strong>Balanced everyday mining</strong></summary>

```ini
[VeinMiner]
Enabled = true

[Safety]
MaxTraversalCells = 512
MaxBlocksToMine   = 128
MaxAxisRadius     = 16

[Ores]
MineGoldOre    = true
MineIronOre    = true
MineCopperOre  = true
MineCoalOre    = true
MineDiamondOre = true
MineSlime      = false

[Logging]
DoAnnouncement = false
DoLogging      = false

[Hotkeys]
ReloadConfig = Ctrl+Shift+R
```

Good for a clean vanilla-plus feel without letting giant ore chains get too aggressive.

</details>

<details>
<summary><strong>High-speed mining sessions</strong></summary>

```ini
[VeinMiner]
Enabled = true

[Safety]
MaxTraversalCells = 2048
MaxBlocksToMine   = 512
MaxAxisRadius     = 32

[Ores]
MineGoldOre    = true
MineIronOre    = true
MineCopperOre  = true
MineCoalOre    = true
MineDiamondOre = true
MineSlime      = true

[Logging]
DoAnnouncement = true
DoLogging      = false

[Hotkeys]
ReloadConfig = Ctrl+Shift+R
```

Best for players who want a much faster resource-gathering loop.

</details>

<details>
<summary><strong>Rare-ore manual mode</strong></summary>

```ini
[VeinMiner]
Enabled = true

[Safety]
MaxTraversalCells = 512
MaxBlocksToMine   = 96
MaxAxisRadius     = 12

[Ores]
MineGoldOre    = true
MineIronOre    = true
MineCopperOre  = true
MineCoalOre    = true
MineDiamondOre = false
MineSlime      = false

[Logging]
DoAnnouncement = false
DoLogging      = true

[Hotkeys]
ReloadConfig = Ctrl+Shift+R
```

Useful if you want common materials to mine quickly but still prefer to harvest rarer finds by hand.

</details>

---

## Hot-reloading the config

One of VeinMiner’s nicest quality-of-life touches is that it supports **in-game config reloads**.

### Default hotkey

```text
Ctrl + Shift + R
```

### What happens when you press it

- the INI file is re-read from disk
- settings are reapplied to runtime statics
- a feedback message is shown confirming the reload path

### Why this is useful

You can tweak:

- safety caps
- enabled ores
- logging behavior
- the reload hotkey itself

without fully closing and relaunching the game each time.

---

## Safety model

VeinMiner is intentionally conservative in how it searches and mines.

### Search boundaries

The search uses a bounded flood-fill style traversal and only continues while:

- the visited cell count is below `MaxTraversalCells`
- candidate cells stay inside the configured `MaxAxisRadius`
- the terrain index is valid
- the discovered block still matches the original vein type

### Why that matters

This helps keep the mod:

- predictable
- easier to tune
- less likely to overreach in very large or unusual deposits

---

## Technical notes for advanced users

<details>
<summary><strong>Show implementation details</strong></summary>

### Trigger point

VeinMiner hooks the game’s dig flow and captures the target block before vanilla removes it.

### Post-dig execution

After the original dig succeeds, the mod attempts to mine the connected component of matching blocks around that original location.

### Connectivity model

The vein search is strictly **orthogonal**:

- `(+X, -X)`
- `(+Y, -Y)`
- `(+Z, -Z)`

Diagonal touching blocks are not treated as connected.

### Mining order

Discovered blocks are ordered by:

1. squared distance from the original mined block
2. descending Y for tie-breaking

### Drop behavior

Item drops are generated via the tool’s normal `CreatesWhenDug(...)` behavior and then spawned through the pickup manager.

### Runtime config application

The config is loaded into a runtime snapshot and applied to static settings, allowing hot-reload without needing a game restart.

### Patch lifecycle

The mod applies Harmony patches on startup and unpatches its own Harmony ID during shutdown.

</details>

---

## Compatibility and behavior notes

- VeinMiner is a **gameplay quality-of-life** mod, not a UI overhaul.
- It currently has **no dedicated command set** and **no standalone overlay/menu**.
- It is designed to feel close to normal mining rather than replacing the mining loop entirely.
- Because it uses the game’s normal block-alter flow for extra removals, it aims to stay in line with existing digging behavior instead of inventing a separate removal system.

---

## Suggested screenshots / media plan

If you are building out the GitHub page with images later, these would be great captures:

1. **Hero banner** – player mining into a rich ore wall underground
2. **Before / after comparison** – one block mined versus the resulting cleared vein
3. **Config file screenshot** – `VeinMiner.Config.ini` open with the important sections highlighted
4. **Hot-reload demo** – edit config, press the reload hotkey, show the confirmation message
5. **Ore showcase collage** – gold, iron, copper, coal, diamond, and slime examples

---

## Troubleshooting

### Vein mining is not triggering

Check the following:

- Are you using a **pick-type** tool?
- Is the target block one of the supported resource types?
- Is that ore enabled in the config?
- Is `Enabled = true`?
- Are your safety limits set too low for the vein you are testing?
- Did the dig count as an effective dig?

### I changed the config but nothing updated

Use the reload hotkey:

```text
Ctrl + Shift + R
```

Or verify that your edited file is the one at:

```text
!Mods\VeinMiner\VeinMiner.Config.ini
```

### The vein seems smaller than expected

That may be normal if:

- some blocks are only diagonally touching
- `MaxAxisRadius` is too low
- `MaxTraversalCells` is too low
- `MaxBlocksToMine` is capping the extra removals

---

## Uninstalling

To remove VeinMiner:

1. delete the VeinMiner mod DLL from your `!Mods` folder
2. optionally delete the config folder:

```text
!Mods\VeinMiner\
```

This removes the mod and its saved settings.

---

## Credits

- **Author:** RussDev7
- **Project:** CastleForge
- **License:** GPL-3.0-or-later

---

## Closing note

VeinMiner is one of those mods that immediately makes mining feel better without turning the game into something else. It stays simple, fast, configurable, and easy to understand—exactly the kind of quality-of-life feature players tend to keep installed once they try it.
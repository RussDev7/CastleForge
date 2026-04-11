# PhysicsEngine

> A configurable **lava simulation mod** for CastleMiner Z that turns placed lava into a live, expanding hazard instead of a static block.
>
> This build focuses on **incremental lava flow**, **bounded simulation budgets**, **runtime tuning**, and **clean in-game reloads** so you can dial the behavior in without restarting every time.

---

## Overview

PhysicsEngine adds a gameplay-focused lava system that reacts to placed and changed lava blocks, then simulates how that lava should continue to spread through the world over time. Rather than doing a giant world-wide recompute, it uses an incremental frontier-based solver that prefers stable forward movement, respects gravity first, and keeps the runtime bounded with configurable budgets.

In plain terms: place lava, let it fall, let it spread, and tune how aggressive or conservative that behavior should be.

![Preview](_Images/Preview.gif)

---

## Why this mod stands out

PhysicsEngine is not just a cosmetic lava tweak. It is built around a real tracked simulation loop with source ownership, removal handling, refill cooldowns, per-step safety limits, and a hot-reloadable config. That makes it useful for survival challenge servers, trap-heavy worlds, hazard maps, volcanic builds, custom events, or anyone who wants lava to feel more alive without turning the game into an unbounded physics experiment.

---

## Current scope

This version is focused specifically on **lava behavior**.

- It tracks **SurfaceLava** and **DeepLava**.
- It does **not** expose chat commands in the current build.
- It is driven primarily by **normal block interaction** plus a generated **INI config**.
- It includes a **runtime hotkey** to reload the config while in-game.

![Lava Environment](_Images/LavaEnvironment.gif)

---

## Feature breakdown

<details>
<summary><strong>Click to expand the full feature list</strong></summary>

### 1) Manual lava source tracking
When a player places a lava block, PhysicsEngine can register that placement as a tracked source and seed the simulation from it.

### 2) Observes real terrain changes
The mod listens for terrain-change messages so it can react when lava is placed, removed, or overwritten through normal gameplay. This also helps it adopt lava changes that were not created through the local placement hook alone.

### 3) Gravity-first flow
Lava prefers to move downward first. If a cell can continue falling, the solver prioritizes that before considering shelf-like sideways spread.

### 4) Edge spill behavior
If lava reaches an edge where a side cell is open and immediately drops downward, the solver favors that kind of spill before ordinary flat spreading.

### 5) Supported sideways flow
Flat horizontal spread only happens from supported cells. This helps keep shelf flow more controlled and intentional.

### 6) Horizontal distance limit
Sideways spread is capped by a configurable horizontal travel distance. That lets you keep lava compact or make it reach farther across flat ground.

### 7) Downward drops reset lateral distance
When lava finds a new downward path, its horizontal distance budget resets. This lets it continue traveling down slopes, cliffs, and uneven terrain without dying out too early.

### 8) Pending-write awareness
The simulation reasons about its own pending adds and removals before the world fully confirms them. This helps keep the solver responsive and reduces self-conflicts.

### 9) Conservative orphan cleanup
If a non-source lava cell is no longer fed by a valid path, the mod can remove it and recheck nearby cells. The design favors stable forward flow instead of aggressive global cleanup.

### 10) Refill cooldown after removal
When tracked lava is mined out or removed, the cell can be blocked from immediate refill for a configurable amount of time. This helps prevent instantly reappearing lava unless you explicitly want persistent refill behavior.

### 11) Optional “keep lava alive” mode
You can disable the refill delay behavior and allow lava to keep reclaiming removed cells more aggressively.

### 12) Idle shutdown
When enabled, the current simulation run can end cleanly once no frontier work and no pending writes remain. This keeps the runtime calmer when nothing is happening.

### 13) Runtime hot-reload
The config can be reloaded in-game through a hotkey, so you can tune timings and limits without relaunching the game.

### 14) Optional announcements and logging
You can enable player feedback and/or log output to make source adds, removals, and solver lifecycle events easier to monitor.

### 15) Safety budgets for performance control
The solver uses step timing, evaluation limits, write limits, owned-cell caps, and flow-distance caps so you can control how lightweight or how aggressive the simulation feels.

</details>

---

## How it works

PhysicsEngine follows a simple gameplay loop:

1. A lava block is placed or observed.
2. The mod registers or adopts that lava as part of its tracked simulation state.
3. Each tick, it waits for the configured interval and then processes a bounded amount of work.
4. Lava tries to fall first, then spill off edges, then spread sideways if supported.
5. If lava is removed, nearby cells are rechecked and the removed spot can be temporarily protected from instant refill.
6. When the queue is empty and idle cleanup is enabled, the active simulation run ends.

That means the mod feels dynamic, but still gives you levers to keep it predictable and manageable.

![Flow Sequence](_Images/FlowSequence.gif)

---

## Best use cases

PhysicsEngine is especially interesting for:

- **Survival challenge worlds** where lava should feel dangerous and active.
- **Trap builds** that rely on lava continuing to move after placement.
- **Adventure maps** with volcanic, industrial, or hazard-heavy themes.
- **Server events** where environmental danger is part of the gameplay.
- **World experiments** where you want to tune lava into a slow creep or a more aggressive spread.

---

## Installation

### Requirements
- CastleMiner Z
- CastleForge **ModLoader**

### Recommended companion mod
To make testing and using PhysicsEngine easier, it is strongly recommended to also install **TooManyItems**. Since PhysicsEngine revolves around live lava placement, TooManyItems gives you a quick way to obtain the lava block in-game.

This is especially useful for:
- quickly testing lava behavior after changing config values
- building showcase screenshots or demo worlds
- server-side verification that the simulation is working correctly

### Install steps
1. Install the CastleForge ModLoader.
2. Place `PhysicsEngine.dll` into your game's `!Mods` folder.
3. Launch the game once.
4. On first run, the mod generates its config folder and config file under:

```text
!Mods\PhysicsEngine\PhysicsEngine.Config.ini
```

5. Edit the config as needed.
6. Use the reload hotkey in-game to apply changes without restarting.

### Files the mod creates
On first launch, this mod may create and/or use:

```text
!Mods\PhysicsEngine\PhysicsEngine.Config.ini
!Mods\PhysicsEngine\
```

![PhysicsEngine Install](_Images/PhysicsEngineInstall.gif)

---

## Configuration

PhysicsEngine uses an INI file generated on first launch.

**Default path:**

```text
!Mods\PhysicsEngine\PhysicsEngine.Config.ini
```

**Default reload hotkey:**

```text
Ctrl+Shift+R
```

The hotkey parser is flexible and accepts formats like:

```text
F9
Ctrl+F3
Control Shift F12
Alt+0
Win+R
PageUp
A
```

### Default config

```ini
# PhysicsEngine - Configuration
# Lines starting with ';' or '#' are comments.

[PhysicsEngine]
; Master toggle for the entire mod.
Enabled = true

[Simulation]
; Milliseconds between lava simulation steps.
StepIntervalMs            = 125
; Maximum queued cells evaluated per simulation step.
MaxCellEvaluationsPerStep = 256
; Maximum terrain writes performed per simulation step.
MaxBlockWritesPerStep     = 12
; Safety cap for all lava cells currently managed by the simulation.
MaxOwnedCells             = 16384
; Maximum flat sideways travel from a supported cell.
MaxHorizontalFlowDistance = 5
; If false, mined/removed lava cells are temporarily blocked from immediate refill.
KeepLavaAlive             = false
; How long a mined lava cell should stay empty before refill is allowed again.
RemovedCellRespawnDelayMs = 2500
; If true, a lava run is considered finished when no frontier/pending writes remain.
EndSimulationWhenIdle     = true

[Logging]
; Show an in-game announcement to the player.
DoAnnouncement = false
; Write the action to the log.
DoLogging      = false

[Hotkeys]
; Reload this config while in-game:
ReloadConfig = Ctrl+Shift+R
```

<details>
<summary><strong>Click to expand what each config setting does</strong></summary>

### `[PhysicsEngine]`

#### `Enabled`
Master toggle for the entire mod. If this is false, the runtime simulation logic does not proceed.

### `[Simulation]`

#### `StepIntervalMs`
How often the simulation is allowed to advance.

- Lower values make the lava feel more responsive.
- Higher values make it feel slower and lighter on runtime work.

#### `MaxCellEvaluationsPerStep`
How many queued cells the solver can inspect in one simulation step.

- Higher values let larger lava events process faster.
- Lower values keep work more conservative.

#### `MaxBlockWritesPerStep`
How many actual terrain writes the solver can perform in one step.

- Higher values make growth look more aggressive.
- Lower values create a slower, more staged spread.

#### `MaxOwnedCells`
Hard cap for the lava cells actively managed by this simulation run.

- Useful as a safety ceiling.
- Important for keeping large lava events bounded.

#### `MaxHorizontalFlowDistance`
Maximum sideways distance lava can travel across supported flat ground before it stops shelf-spreading.

- Lower values keep lava compact.
- Higher values allow broader horizontal reach.

#### `KeepLavaAlive`
Controls refill persistence after lava is removed.

- `false`: removed cells can be temporarily blocked from instant refill.
- `true`: lava is allowed to reclaim removed spaces more aggressively.

#### `RemovedCellRespawnDelayMs`
How long a removed lava cell stays protected from immediate refill when `KeepLavaAlive` is disabled.

- `0` effectively disables the cooldown window.
- Higher values make mined lava stay cleared longer.

#### `EndSimulationWhenIdle`
Ends the current run when no queued work and no pending writes remain.

- Good for cleaner idle behavior.
- Usually sensible to keep on unless you have a very specific reason not to.

### `[Logging]`

#### `DoAnnouncement`
Shows lightweight in-game feedback when tracked lava sources are added or removed.

#### `DoLogging`
Writes useful log information for debugging and tuning.

### `[Hotkeys]`

#### `ReloadConfig`
Hotkey used to re-read and re-apply the config while in-game.

</details>

---

## Tuning ideas

If you want a quick starting point, these are good mental presets:

### Slow and controlled lava
Use this when you want lava to feel dangerous but readable.

```ini
StepIntervalMs            = 200
MaxCellEvaluationsPerStep = 128
MaxBlockWritesPerStep     = 6
MaxHorizontalFlowDistance = 3
KeepLavaAlive             = false
RemovedCellRespawnDelayMs = 3000
```

### Faster, more aggressive spread
Use this when you want lava to push farther and update more noticeably.

```ini
StepIntervalMs            = 75
MaxCellEvaluationsPerStep = 512
MaxBlockWritesPerStep     = 24
MaxHorizontalFlowDistance = 8
KeepLavaAlive             = true
```

### Balanced general gameplay
Close to the spirit of the defaults while still feeling active.

```ini
StepIntervalMs            = 125
MaxCellEvaluationsPerStep = 256
MaxBlockWritesPerStep     = 12
MaxHorizontalFlowDistance = 5
KeepLavaAlive             = false
RemovedCellRespawnDelayMs = 2500
```

---

## Player-facing behavior notes

A few details are worth knowing when you use the mod:

- Lava prefers **falling** over shelf spreading.
- Shelf spread only happens from **supported** cells.
- A new downward path effectively gives lava a fresh chance to continue traveling.
- Removed lava does not always refill instantly; that depends on your cooldown settings.
- The mod picks a visible lava type based on surrounding conditions, using **SurfaceLava** when the block above is open and **DeepLava** when it is covered.
- This build does **not** include slash commands for players.

![Surface Vs Deep Lava](_Images/SurfaceVsDeepLava.gif)

---

## In-game hot reload

One of the nicest quality-of-life features in this build is runtime config reloading.

After changing the config file, press the configured hotkey while in-game and the mod reloads the INI without requiring a relaunch. That makes it much easier to tune lava speed, spread distance, refill behavior, and debug logging while you test a world live.

This is especially useful when you are trying to find the sweet spot between:

- visual drama,
- gameplay danger,
- and performance-friendly limits.

---

## Logging and feedback

If enabled, PhysicsEngine can provide:

- in-game feedback when tracked lava sources are added or removed,
- log output for source changes,
- and solver lifecycle messages such as the simulation ending when idle.

For normal gameplay, you may prefer to leave both settings off. For testing, balancing, or diagnosing unexpected lava behavior, turning them on can be very helpful.

---

## Compatibility notes

PhysicsEngine is built as a CastleForge mod and expects the normal ModLoader environment. Because it modifies block behavior through live terrain changes, it is best thought of as a gameplay system mod rather than a purely visual addon.

If you are combining it with other mods that also alter terrain or block placement behavior, test the combination in a safe world first and tune the simulation budgets conservatively.

---

## Recommended screenshot list for this README

If you want this page to really sell the mod on GitHub, these are the screenshots worth capturing:

1. **Hero shot** — lava pouring through a dramatic cave, shaft, or fortress.
2. **Placement shot** — player placing lava at the source point.
3. **Flow sequence** — lava progressing over time.
4. **Config shot** — open config file with major settings highlighted.
5. **Surface vs deep shot** — exposed lava versus covered lava.
6. **Trap/world use case** — an example of lava integrated into gameplay.

---


## Under the hood

<details>
<summary><strong>Click to expand implementation notes</strong></summary>

### Patch points used by this build
This version installs Harmony patches around a few key game paths:

- `BlockInventoryItem.AlterBlock` — catches local lava placement and seeds tracking early.
- `CastleMinerZGame._processAlterBlocksMessage` — observes authoritative terrain changes and reacts to adds/removals.
- `InGameHUD.OnPlayerInput(...)` — polls the configurable reload hotkey on the main thread.

### Simulation model
The solver is built around a **frontier queue** instead of a full world recompute.

Internally it tracks:

- manual lava sources,
- owned lava cells,
- per-cell horizontal distance,
- pending add/remove writes,
- active simulation cells,
- and temporary refill-blocked cells.

That design helps the mod stay responsive while still enforcing clear limits.

### Embedded resource handling
The project includes embedded resource extraction and resolver helpers. On startup, the mod can extract embedded resources into its own `!Mods\PhysicsEngine` folder and initialize its embedded dependency handling automatically.

### Shutdown behavior
On game exit or unload, the mod attempts to unpatch only the Harmony hooks associated with this mod, leaving unrelated mods alone.

### Build notes
For anyone browsing the repository from a developer perspective, this project is configured as:

- **Assembly name:** `PhysicsEngine`
- **Target framework:** `.NET Framework 4.8.1`
- **Platform target:** `x86`
- **Embedded dependency:** `0Harmony.dll`

</details>

---

## Troubleshooting

### The config file is missing
Launch the game once with the mod installed. The mod generates the config on first run.

### My config changes are not taking effect
Use the configured hotkey while in-game to reload the config, or restart the game if you want to rule out bad key bindings.

### Lava is spreading too slowly
Lower `StepIntervalMs`, or raise `MaxCellEvaluationsPerStep` and `MaxBlockWritesPerStep`.

### Lava is spreading too aggressively
Raise `StepIntervalMs`, lower the per-step budgets, or reduce `MaxHorizontalFlowDistance`.

### Removed lava keeps coming back too quickly
Set `KeepLavaAlive = false` and increase `RemovedCellRespawnDelayMs`.

### Lava dies out too easily on shelves
Increase `MaxHorizontalFlowDistance`, or test layouts where lava has stronger supported paths.

---

## Uninstalling

To remove PhysicsEngine:

1. Delete `PhysicsEngine.dll` from your `!Mods` folder.
2. Delete the generated PhysicsEngine folder if you no longer want its config files.

```text
!Mods\PhysicsEngine\
```

This mod does not appear to require any special save cleanup beyond removing its files.

---

## License

PhysicsEngine is part of **CastleForge** and follows the repository's licensing and distribution terms.

---

## Final pitch

If you want lava in CastleMiner Z to feel less like a static decoration and more like a real gameplay hazard, PhysicsEngine is a strong foundation. It gives you dynamic spread, bounded simulation controls, refill behavior, live tuning, and enough configuration to shape the experience into anything from a slow environmental threat to a more aggressive world hazard.

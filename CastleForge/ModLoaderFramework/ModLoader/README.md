# ModLoader

![Preview](_Images/Preview.png)

> The **config-bootstrapped core loader** for **CastleForge** and **CastleMiner Z**.
>
> This page contains the low-level runtime and authoring details that were previously living in the repository root README.

This loader does **not** use native "DLL injection" (no external injector, no `CreateRemoteThread`, no code caves). Instead, it uses **CastleMinerZ.exe.config** so the .NET **CLR** (Common Language Runtime) loads a custom **AppDomainManager** at startup. That manager attaches a small XNA `GameComponent`, which then discovers, loads, and ticks mod DLLs.

For the full repository showcase, mod catalog, servers, and tools, go back to the **[root README](../../../README.md)**.

## What ModLoader is responsible for

| Area | Purpose |
|--------------------------------- |----------------------------------------------------------------------------------------------------------------------------------|
| **CLR bootstrap**                | Starts from `CastleMinerZ.exe.config` through a custom `AppDomainManager`, without relying on a native injector.                 |
| **Early assembly resolution**    | Installs a top-level `!Mods` assembly fallback so discovery still works when config probing is disabled.                         |
| **Main-thread bootstrap attach** | Waits for `CastleMinerZGame` and `TaskDispatcher`, then safely injects the runtime bootstrap component on the game thread.       |
| **Startup choice**               | Lets the player launch with mods, without mods, or exit, and can remember that choice in `ModLoader.ini`.                        |
| **Core hash guard**              | Optionally compares current core file hashes against the compiled-against baseline and prompts before continuing if they differ. |
| **Progress UI**                  | Can show a passive, non-activating load-progress window while mods are discovered and started.                                   |
| **Discovery and load order**     | Scans `!Mods`, resolves `ModBase` implementations, applies priorities, and honors required dependencies.                         |
| **Runtime ticking**              | Calls each loaded mod every frame and isolates per-mod failures as much as possible.                                             |
| **Logging**                      | Writes tag-aware loader logs to `!Logs\\ModLoader.log`.                                                                          |

## Key runtime pieces

| File | Role |
|----------------------------------------------|------------------------------------------------------------------------------------------------------------|
| `Core/Hosting/ModDomainManager.cs`           | CLR entry point. Installs early resolution and queues the bootstrap attach once the game is ready.         |
| `Core/Bootstrap/ModBootstrapComponent.cs`    | Handles startup mode selection, optional hash checks, one-time mod loading, and per-frame ticking.         |
| `Core/Loading/ModManager.cs`                 | Discovers mods, orders them deterministically, resolves dependencies, starts them, and ticks them.         |
| `Core/Resolution/ModsAssemblyResolver.cs`    | Minimal `AssemblyResolve` fallback for top-level assemblies inside `!Mods`.                                |
| `Startup/ModLoaderConfig.cs`                 | Persists launch choice, progress-window behavior, exception mode, and accepted core hashes.                |
| `Startup/UI/Startup/StartupPromptForm.cs`    | The small startup prompt used to launch with mods, without mods, or abort.                                 |
| `Startup/UI/Loading/ModLoadProgressForm.cs`  | Passive helper progress window using tool-window and no-activate styles to reduce fullscreen interference. |
| `Integrity/HashGuard.cs`                     | Hash verification and mismatch prompt for `CastleMinerZ.exe` and `DNA.Common.dll`.                         |
| `Logging/LogSystem.cs`                       | Thread-safe, tag-aware logging for loader startup and runtime diagnostics.                                 |

---

## Table of contents

- [What ModLoader is responsible for](#what-modloader-is-responsible-for)
- [Key runtime pieces](#key-runtime-pieces)
- [How it works](#how-it-works)
- [Folder layout](#folder-layout)
- [Installation](#installation)
- [Uninstall](#uninstall)
- [Writing a mod](#writing-a-mod)
  - [Using Harmony (runtime patching)](#using-harmony-runtime-patching)
  - [Bundling dependencies and assets (EmbeddedResolver / EmbeddedExporter)](#bundling-dependencies-and-assets-embeddedresolver--embeddedexporter)
- [Optional: ModLoaderExtensions (shared services)](#optional-modloaderextensions-shared-services)
  - [Slash commands and /help](#slash-commands-and-help)
  - [Exception capture](#exception-capture)
  - [Shared quality-of-life patches](#shared-quality-of-life-patches)
- [Load order and dependencies](#load-order-and-dependencies)
- [Startup prompt and ModLoader.ini](#startup-prompt-and-modloaderini)
- [Logging](#logging)

---

## How it works

### 1) Config‑based bootstrap (no injector)

The game is started normally. The only "bootstrap" step is adding an `AppDomainManager` entry to the game’s config so the CLR loads the loader assembly during startup.

Your current config looks like this:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <runtime>
    <!-- Point to the mod-loader's custom AppDomainManager type and its assembly. -->
    <appDomainManagerType value="ModLoader.ModDomainManager" />
    <appDomainManagerAssembly value="ModLoader, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />

    <!-- Optional: Allow the CLR to probe !Mods for assemblies (ex: if you place ModLoader.dll inside !Mods). -->
    <!--
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <probing privatePath="!Mods" />
    </assemblyBinding>
    -->
  </runtime>
</configuration>
```

**How `CastleMinerZ.exe.config` works (plain English):**

- For .NET Framework apps, the CLR looks for a file named **`<exe name>.config`** next to the EXE (here: `CastleMinerZ.exe.config`).
- The CLR reads this file during startup to configure managed runtime behavior (assembly binding, version redirects, startup hooks, etc.).
- It does **not** patch or rewrite `CastleMinerZ.exe`. It only influences how the CLR loads/starts managed code inside the process.

> **CLR (Common Language Runtime):** The .NET runtime that loads assemblies, JIT-compiles IL, manages memory/GC, and executes managed code.

Notes:
- If you keep `ModLoader.dll` next to `CastleMinerZ.exe`, you can leave the probing block commented out.
- If you want to store `ModLoader.dll` (or other referenced assemblies) under `!Mods`, uncomment the probing block (or use `codeBase` hints).
- Probing affects **CLR dependency resolution only**. It does **not** control where CastleForge scans for mods (CastleForge scans `!Mods`).

**Dependency resolution without `<probing>` (CastleForge fallback):**
- CastleForge installs a minimal `AssemblyResolve` fallback early (via the AppDomainManager) so the CLR can locate common referenced assemblies during mod discovery.
- This fallback is **top-level only**: it checks `!Mods\<AssemblyName>.dll` and does **not** scan subfolders.
- If you disable `<probing>`, place shared dependencies (ex: `HarmonyLib.dll`) directly in `!Mods` (or embed dependencies inside each mod via `EmbeddedResolver`).

### 2) AppDomainManager attaches the bootstrap component

When the CLR initializes the AppDomain, `ModDomainManager.InitializeNewDomain(...)` runs. It queues a background retry loop that waits for `CastleMinerZGame.Instance`, then injects a `ModBootstrapComponent` into the game’s `Components` collection.

### 3) Bootstrap component initializes once, then ticks mods every frame

`ModBootstrapComponent.Update(...)` is called every frame by XNA.

On the **first** update it:
- Determines whether to launch **with mods**, **without mods**, or **abort** (exit).
- Optionally performs a "core hash guard" check (if present) and can fall back to *without mods*.
- Loads mods from `!Mods` (relative to the game directory) via `ModManager.LoadMods(...)`.

On **subsequent** updates it:
- Calls `ModManager.TickAll(...)` once per frame (only if you launched with mods).

### 4) Mod discovery, ordering, and dependency‑aware loading

`ModManager.LoadMods("<GameDir>\\!Mods")`:

- Scans `!Mods` for `*.dll` (non‑recursive).
- `Assembly.LoadFrom(...)` loads each DLL.
- Finds public, non-abstract types deriving from `ModBase`.
- Reads optional `[Priority(...)]` (defaults to `Priority.Normal`).
- Performs a dependency‑aware load pass using `[RequiredDependencies(...)]`.
- Instantiates mods and calls `Start()`.

### 5) Mod startup + Harmony patching (hooks beyond the frame loop)

After a mod is instantiated, CastleForge calls `Start()` once. This is where mods typically:
- Initialize state, read configs, register UI/commands, etc.
- **Apply Harmony patches** to hook CastleMiner Z methods (Prefix/Postfix/Transpiler).

Harmony is important because it lets mods run code **at the exact moment a target method executes** (not just once per frame). Once patched, those hooks fire whenever the game calls the method-input handlers, networking, crafting, terrain edits, HUD rendering, and so on.

**How early can a mod patch?** CastleForge itself is bootstrapped at CLR startup via `AppDomainManager`, but this implementation waits until `CastleMinerZGame.Instance` exists (so it can add the XNA component) and then loads mods on the first `Update(...)`. In practice that’s "early in runtime" (before most gameplay activity), and you can ensure a mod patches as early as possible by giving it a high `[Priority(...)]` (or using a bootstrap utility mod like `ModLoaderExtensions`).

### 6) Runtime execution: per-frame `Tick(...)` + patched entrypoints

Each frame, `ModManager.TickAll(...)` calls `Tick(...)` for each loaded mod. Exceptions are caught per-mod so one failing mod doesn’t necessarily take down the whole loader (though mods still run in-process, so hard crashes are always possible).

Separately, **Harmony patches execute wherever their target methods run**, which can be multiple times per frame-or not tied to the frame loop at all. This means a mod can be purely patch-driven (no `Tick`) or use both: `Tick` for ongoing behavior + Harmony for precise hooks.

---

## Folder layout

Typical layout next to `CastleMinerZ.exe`:

```
<CastleMinerZ Install Dir>
│  CastleMinerZ.exe
│  CastleMinerZ.exe.config         <-- contains the AppDomainManager bootstrap entries
│  ModLoader.dll                   <-- the loader assembly (contains ModDomainManager)
│  ModLoader.ini                   <-- optional; stores remembered launch choice, loader preferences, and accepted core-hash state
│
├─ !Mods\                          <-- mods you want to load (scanned by ModManager)
│   ├─ ModLoaderExtensions.dll     <-- optional companion mod (recommended)
│   ├─ SomeMod.dll
│   └─ AnotherMod.dll
│
└─ !Logs\                          <-- created automatically
    └─ ModLoader.log
```

If you prefer placing `ModLoader.dll` inside `!Mods`, enable the commented `<probing privatePath="!Mods" />` in the config (or use an explicit `<codeBase>`). The loader will still scan mods from `!Mods`.

---

## Installation

1) Back up your original `CastleMinerZ.exe.config`.
2) Copy `ModLoader.dll` next to `CastleMinerZ.exe`.
3) Edit `CastleMinerZ.exe.config` to include your `appDomainManagerType` and `appDomainManagerAssembly` entries (see snippet above).
4) Create `!Mods\` next to the EXE and drop mod DLLs inside.
5) Launch the game.

---

## Uninstall

- Restore/remove the `appDomainManagerType` / `appDomainManagerAssembly` entries from `CastleMinerZ.exe.config`.
- Delete `ModLoader.dll`.
- Optionally delete `!Mods\`, `!Logs\`, and `ModLoader.ini`.

---

## Writing a mod

Mods are .NET assemblies that contain a concrete class deriving from `ModBase`.

Minimal example:

```csharp
using System;
using Microsoft.Xna.Framework;
using DNA.Input;
using ModLoader;

public sealed class ExampleMod : ModBase
{
    public ExampleMod() : base("ExampleMod", new Version(1,0,0,0)) { }

    public override void Start()
    {
        // One-time initialization.
    }

    public override void Tick(InputManager inputManager, GameTime gameTime)
    {
        // Runs every frame.
    }
}
```

> **Note:** In the current implementation, `Tick(...)` is called with `inputManager = null`, so mods should null-check or fetch input through their own game references.

Drop the compiled `ExampleMod.dll` into `!Mods\`.

### Using Harmony (runtime patching)

**Harmony** is the standard way to *hook* CastleMiner Z’s compiled methods at runtime without modifying the game files.

At a high level:
- **Prefix**: run your code *before* the original method (optionally skip the original).
- **Postfix**: run your code *after* the original method.
- **Transpiler**: rewrite the method’s IL for deeper changes (advanced, but powerful).

CastleForge doesn’t mandate how you patch, but a clean pattern (used in example mods) is:
1) Keep all patches in a dedicated `GamePatches` helper.
2) Give each mod its own **unique Harmony ID** so unpatching is scoped to that mod.
3) Patch at `Start()` (when the game instance is live), and unpatch during shutdown.

Example pattern (simplified):

```csharp
// Unique ID per mod to avoid collisions.
_harmonyId = $"castleminerz.mods.{typeof(GamePatches).Namespace}.patches";
_harmony   = new Harmony(_harmonyId);

// Patch every class in this assembly marked with [HarmonyPatch].
foreach (var patchType in EnumeratePatchTypes(typeof(GamePatches).Assembly))
    _harmony.CreateClassProcessor(patchType).Patch();

// Later (shutdown): only undo our own patches.
_harmony.UnpatchAll(_harmonyId);
```

Nice-to-haves:
- **Best-effort patching**: patch each `[HarmonyPatch]` container inside a try/catch so one bad patch doesn’t block the rest.
- **Patch reporting**: list what methods were patched by *this* Harmony ID.
- **Noisy containers**: optionally hide "utility" patch containers from reports (example: a custom marker attribute).

### Bundling dependencies and assets (EmbeddedResolver / EmbeddedExporter)

CastleForge mods run in-process, so **keeping each mod self-contained** makes installs and updates dramatically simpler.

Two helpers commonly used inside mods:

#### EmbeddedResolver

`EmbeddedResolver` is a **unified dependency loader** for embedded DLL resources:

- **Native DLLs** (P/Invoke) must be on disk for Windows to load them. The resolver:
  - Scans embedded `*.dll` resources.
  - Detects whether each payload is managed or native by reading PE headers.
  - Extracts native DLLs under `!Mods/<YourNamespace>/Natives`.
  - Calls `LoadLibrary(...)` to preload/pin the exact native copy so P/Invoke binds reliably.

- **Managed DLLs** can be loaded from memory. The resolver:
  - Hooks `AppDomain.CurrentDomain.AssemblyResolve`.
  - When the CLR can’t find a dependency, it loads embedded managed DLL bytes via `Assembly.Load(bytes)`.
  - Reuses already-loaded assemblies by full name to reduce duplication.

In practice, that means you can ship a **single mod DLL** that includes its own managed dependencies, while still supporting native dependencies when required.

#### EmbeddedExporter

Some payloads are not DLLs at all (PNG icons, fonts, default INI files, JSON, etc.) and the game (or your mod) may need them **on disk**.

`EmbeddedExporter` provides a simple "extract a folder from embedded resources" utility:
- Compile files as **Embedded Resource**.
- At runtime, call `ExtractFolder(folderName, destRoot)`.
- Dot-separated manifest names are converted into directories, and the real extension (text after the last dot) is preserved.

**Result:** each mod stays "one DLL to install”, while still supporting disk-based assets when needed.

---


## Optional: ModLoaderExtensions (shared services)

`ModLoaderExtensions` is an **optional companion mod** that you can drop into `!Mods\` like any other mod DLL. It is designed to load **first** (it uses `Priority.Bootstrap`) and provide shared, cross-mod infrastructure that is annoying to duplicate in every project.

When present, it effectively acts as a "services layer" on top of CastleForge:

- **Self-contained dependencies/assets**: calls `EmbeddedResolver.Init()` early (so the mod can embed its managed/native dependencies) and can extract embedded files to `!Mods/<Namespace>` via `EmbeddedExporter`.
- **Central slash commands**: installs a Harmony prefix on the game’s chat send method so `/commands` can be handled locally instead of being broadcast.
- **Built-in `/help`**: aggregates commands across mods and renders a paged help list in chat.
- **Diagnostics**: can arm centralized exception capture/logging (caught-only or first-chance) based on `ModLoader.ini`.
- **Common patch hub**: a natural home for shared Harmony patches (QoL, stability, instrumentation) used by many mods.

### Slash commands and /help

`ModLoaderExtensions` implements a shared "slash command" pipeline:

1) A **Harmony** prefix patches `BroadcastTextMessage.Send(...)` to intercept chat before it goes to the server.
2) Messages starting with `/` are treated as commands.
3) The interceptor fans the command string out to registered handlers; if any handler returns `true`, the original chat send is suppressed.
4) If nobody handles it, the player sees "Unknown command.” (and it’s still suppressed so it doesn’t spam the server).

Command handlers are simple methods discovered by reflection:

- Methods are annotated with `[Command("/name")]`.
- Supported signatures are `void Method()` and `void Method(string[] args)`.
- A small dispatcher splits the raw string (`"/help 2"`) into `command` + `args` and invokes the attributed method.

It also ships a built-in `/help` command that:
- Lists commands across all mods.
- Supports pagination.
- Supports filtering by mod name.

If a mod wants to appear in `/help`, it can register its commands into the shared registry:

```csharp
// In your mod's Start():
HelpRegistry.Register(
    modName: Name,
    items: new (string command, string description)[]
    {
        ("/foo", "Does the thing."),
        ("/bar <n>", "Does the other thing."),
    }
);
```

> Note: using the shared chat interceptor / registry requires referencing the companion assembly (or copying the small helper classes into your own mod).

### Exception capture

`ModLoaderExtensions` can arm a centralized exception logger based on `ModLoader.ini`:
- **Off**: no extra hooks.
- **CaughtOnly**: logs unhandled/unobserved/task/UI thread exceptions.
- **FirstChance**: logs every thrown exception (very noisy; throttled).

This is intended to help diagnose modded sessions and (optionally) suppress upstream crash-reporting behavior.

### Shared quality-of-life patches

`ModLoaderExtensions` can also apply a bundle of "core" Harmony patches (a `GamePatches.ApplyAllPatches()` style hub). This is where you put:
- global stability fixes,
- shared hooks used by many mods,
- chat/UI tweaks,
- instrumentation (diagnostics),
- and any other "foundation" patches you don’t want every mod re-implementing.

Because these patches live in one optional companion mod, you can keep CastleForge itself lean, and users can opt-in to the extra behavior by dropping one DLL into `!Mods\`.


---

## Load order and dependencies

### Priority

Use `[Priority(...)]` to influence load ordering (higher loads earlier). If omitted, the loader treats it as `Priority.Normal`.

Recommended bands include:
- `Priority.Bootstrap` (reserved)
- `Priority.First`, `VeryHigh`, `High` ... `Normal` ... `Low`, `VeryLow`, `Last`

### Dependencies

Use `[RequiredDependencies("SomeOtherModName")]` to require another mod to be loaded *first*. Dependencies are matched against `ModBase.Name` (case‑insensitive).

---

## Startup prompt and ModLoader.ini

On first launch (or when not remembered), the loader shows a small WinForms dialog:
- **Play CastleMiner Z** (with mods)
- **Play CastleMiner Z (no mods)**
- Optional: **Always use this option**

That choice is resolved by `StartupModeSelector` (dialog runs on a temporary STA thread), and can be persisted into `ModLoader.ini` in the game directory.

---

## Logging

The loader writes logs to `!Logs\ModLoader.log` using a tag‑aware, column‑aligned logger. Logging is designed to be thread‑safe and "never‑throw" (logging failures are swallowed so they don’t break gameplay).
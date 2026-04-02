/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Central place to store example scripts that the "Code Injector" tab can
    /// load into the editor. The UI simply copies one of these strings into the
    /// editor buffer; the ScriptEngine then compiles/runs it.
    ///
    /// Two patterns are shown below:
    /// 1) Snippet mode (default): Lines of code that run inside ScriptEngine's template.
    ///    - You can reference game types directly (e.g., CastleMinerZGame.Instance).
    ///    - Return a value with 'return "OK";' to show something in the output panel.
    ///
    /// 2) Full-source mode: Include a line with: // @full
    ///    - Provide your own usings/namespace/class/method - usually a Script.Run().
    ///    - Useful when you need 'using' directives, attributes, Harmony patches, etc.
    ///
    /// Notes:
    /// - Prefer running scripts on the main thread if you touch CMZ/XNA objects.
    /// - These strings are plain C#; if you need multi-line readability,
    ///   consider verbatim strings (@"...") or keep using string.Join(Environment.NewLine, ...).
    /// - Keep each example self-contained and return a short result string.
    /// </summary>
    internal static class ExampleScripts
    {
        #region == Placeholder Script ==

        /// <summary>
        /// A tiny starter script to show users the pattern (snippet mode).
        /// Tip: This runs inside the ScriptEngine template; no extra 'using' needed.
        /// </summary>
        public static readonly string Script_Placeholder = string.Join(Environment.NewLine, new[]
        {
            @"// Example: Give yourself 64 dirt.",
            @"var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;",
            @"var item = DNA.CastleMinerZ.Inventory.InventoryItem.CreateItem(DNA.CastleMinerZ.Inventory.InventoryItemIDs.DirtBlock, 64);",
            @"game.LocalPlayer.PlayerInventory.AddInventoryItem(item);",
            @"return ""OK""; // Return any object/string.",
        });
        #endregion

        #region Examples: Simple

        /// <summary> Simple, safe, snippet-sized examples. Avoid heavy reflection/Harmony here. </summary>

        #region Teleport Player

        public static readonly string Example_Simple_TeleportPlayer = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): Teleport the *local* player to a fixed world position.",
            @"/// Tip: Run this on the MAIN THREAD (The Code-Injector has a checkbox) since it touches game objects.",
            @"/// </summary>",
            @"",
            @"// Grab the singleton game instance for CastleMiner Z.",
            @"// This object gives you access to the current session, local player, etc.",
            @"var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;",
            @"",
            @"// Convenience handles:",
            @"// - Net:    Current network session (null if you're not actually in a world yet).",
            @"// - Player: The local player controller/entity (null in menus or before spawn).",
            @"var net    = game.CurrentNetworkSession;",
            @"var player = game.LocalPlayer;",
            @"",
            @"// Safety check: If you're not in-world yet (e.g., sitting in the main menu),",
            @"// there's nothing to move. Return a message the code tab can display.",
            @"if (net == null || player == null)",
            @"    return ""No valid session or player yet (are you in-world?)"";",
            @"",
            @"// Do the teleport. Vector3 uses the XNA convention: (X, Y, Z)",
            @"//   X = east/west (horizontal), Y = up/down (vertical), Z = north/south (horizontal).",
            @"// Here we set the player to X=0, Y=100, Z=0 - i.e., 100 blocks up at the origin.",
            @"// (Fully-qualified type name used so you don't need a using directive in snippet mode.)",
            @"player.LocalPosition = new Microsoft.Xna.Framework.Vector3(0, 100, 0);",
            @"",
            @"// Return a short status string so your code editor shows a friendly result.",
            @"return ""Teleport player completed."";"
        });
        #endregion

        #region List All Gamers

        public static readonly string Example_Simple_ListAllGamers = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): List all gamers in the current network session.",
            @"/// Returns a comma-separated string of Gamertags, or a friendly message if you're not in-world yet.",
            @"/// Tip: This is read-only and safe to run on the MAIN THREAD (the Code-Injector's default).",
            @"/// </summary>",
            @"",
            @"// Grab the CastleMiner Z singleton.",
            @"// From here you can reach networking, players, world, etc.",
            @"var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;",
            @"",
            @"// Handle to the active network session (null if you're not in a world yet).",
            @"var net  = game.CurrentNetworkSession;",
            @"",
            @"// If you're still in menus or not connected, bail out with a friendly message",
            @"// so the code tab shows something useful instead of throwing.",
            @"if (net == null)",
            @"    return ""No valid session yet (are you in-world?)"";",
            @"",
            @"// Build a simple list of gamer names (Gamertags) from the session.",
            @"System.Collections.Generic.List<string> gamers = new System.Collections.Generic.List<string>();",
            @"",
            @"// AllGamers includes everyone in the session (local + remote).",
            @"foreach (var gamer in net.AllGamers)",
            @"    gamers.Add(gamer.Gamertag);",
            @"",
            @"// Join them into a single comma-separated string so the result is readable.",
            @"return string.Join("", "", gamers);"
        });
        #endregion

        #region Play Sound

        public static readonly string Example_Simple_PlaySound = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): Play a one-shot sound effect through the game's global",
            @"/// SoundManager. Returns a short status string so the code tab shows",
            @"/// something human-readable.",
            @"/// ",
            @"/// Notes:",
            @"/// - Run on the MAIN THREAD (your code injector's default).",
            @"/// - Sound names are the content pipeline asset names (case-sensitive).",
            @"/// - This uses fire-and-forget playback (PlayInstance)-no handle to keep.",
            @"/// </summary>",
            @"",
            @"// Get the global sound system (same singleton the game uses for UI/menu SFX).",
            @"var soundMgr = DNA.Audio.SoundManager.Instance;",
            @"",
            @"// Be polite: Early in boot, the sound system might not be ready.",
            @"if (soundMgr == null)",
            @"    return ""SoundManager not ready (try again after the game finishes loading)."";",
            @"",
            @"// Choose a sound to play (content asset name). Change this string as you like.",
            @"var soundName = ""DragonScream"";",
            @"",
            @"/// <summary>",
            @"/// Available sounds:",
            @"/// Use exactly as listed (asset names are typically case-sensitive).",
            @"/// ",
            @"/// Click, Error, Award, Popup, Teleport, Reload, BulletHitHuman, thunderBig,",
            @"/// craft, dropitem, pickupitem, punch, punchMiss, arrow, AssaultReload, Shotgun,",
            @"/// ShotGunReload, Song1, Song2, lostSouls, CreatureUnearth, HorrorStinger,",
            @"/// Fireball, Iceball, DoorClose, DoorOpen, Song5, Song3, Song4, Song6, locator,",
            @"/// Fuse, LaserGun1, LaserGun2, LaserGun3, LaserGun4, LaserGun5, Beep, SolidTone,",
            @"/// RPGLaunch, Alien, SpaceTheme, GrenadeArm, RocketWhoosh, LightSaber,",
            @"/// LightSaberSwing, GroundCrash, ZombieDig, ChainSawIdle, ChainSawSpinning,",
            @"/// ChainSawCutting, Birds, FootStep, Theme, Pick, Place, Crickets, Drips,",
            @"/// BulletHitDirt, GunShot1, GunShot2, GunShot3, GunShot4, BulletHitSpray,",
            @"/// thunderLow, Sand, leaves, dirt, Skeleton, ZombieCry, ZombieGrowl, Hit, Fall,",
            @"/// Douse, DragonScream, Explosion, WingFlap, DragonFall, Freeze, Felguard, ",
            @"/// </summary>",
            @"",
            @"// Try to play it. If the asset name is wrong or missing, catch and report.",
            @"try",
            @"{",
            @"    // Fire-and-forget one-shot SFX.",
            @"    soundMgr.PlayInstance(soundName);",
            @"    return ""Played '"" + soundName + ""'."";",
            @"}",
            @"catch (Exception ex)",
            @"{",
            @"    // Most commonly this is a missing asset name or the sound bank hasn't loaded yet.",
            @"    return ""Could not play '"" + soundName + ""': "" + ex.Message;",
            @"}",
        });
        #endregion

        #region Show Position

        public static readonly string Example_Simple_ShowPosition = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): Show the local player's current position, block position,",
            @"/// and current time-of-day value.",
            @"/// Tip: Useful as a quick sanity check when testing movement, teleports, or world scripts.",
            @"/// </summary>",
            @"",
            @"var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;",
            @"var player = game.LocalPlayer;",
            @"",
            @"if (player == null)",
            @"    return ""No local player yet (are you in-world?)"";",
            @"",
            @"var pos = player.LocalPosition;",
            @"int blockX = (int)Math.Floor(pos.X);",
            @"int blockY = (int)Math.Floor(pos.Y);",
            @"int blockZ = (int)Math.Floor(pos.Z);",
            @"float tod = (game.GameScreen != null) ? game.GameScreen.TimeOfDay : -1f;",
            @"",
            @"return string.Format(",
            @"""Pos X={0:0.00}, Y={1:0.00}, Z={2:0.00} | Block [{3}, {4}, {5}] | TOD={6:0.000}"",",
            @"    pos.X, pos.Y, pos.Z, blockX, blockY, blockZ, tod);"
        });
        #endregion

        #region Place Loot Boxes

        public static readonly string Example_Simple_PlaceLootBoxes = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): Place a random mix of Loot Blocks and Lucky Loot Blocks",
            @"/// in a ring around the local player.",
            @"///",
            @"/// What it does:",
            @"/// - Places 8 blocks around the player's current block position.",
            @"/// - Each position randomly becomes either LootBlock or LuckyLootBlock.",
            @"/// </summary>",
            @"",
            @"var game    = DNA.CastleMinerZ.CastleMinerZGame.Instance;",
            @"var player  = game.LocalPlayer;",
            @"var terrain = DNA.CastleMinerZ.Terrain.BlockTerrain.Instance;",
            @"var rng     = new System.Random();",
            @"",
            @"if (player == null)",
            @"    return ""No local player yet (are you in-world?)"";",
            @"",
            @"if (terrain == null || !terrain.IsReady)",
            @"    return ""Terrain not ready yet."";",
            @"",
            @"if (!terrain.RegionIsLoaded(player.LocalPosition))",
            @"    return ""Player region is not loaded yet."";",
            @"",
            @"var pos = player.LocalPosition;",
            @"int baseX = (int)System.Math.Floor(pos.X);",
            @"int baseY = (int)System.Math.Floor(pos.Y);",
            @"int baseZ = (int)System.Math.Floor(pos.Z);",
            @"",
            @"int[,] ring = new int[,]",
            @"{",
            @"    { -1,  0, -1 },",
            @"    {  0,  0, -1 },",
            @"    {  1,  0, -1 },",
            @"    { -1,  0,  0 },",
            @"    {  1,  0,  0 },",
            @"    { -1,  0,  1 },",
            @"    {  0,  0,  1 },",
            @"    {  1,  0,  1 }",
            @"};",
            @"",
            @"int placed = 0;",
            @"int lootCount = 0;",
            @"int luckyCount = 0;",
            @"",
            @"for (int i = 0; i < ring.GetLength(0); i++)",
            @"{",
            @"    var world = new DNA.IntVector3(",
            @"        baseX + ring[i, 0],",
            @"        baseY + ring[i, 1],",
            @"        baseZ + ring[i, 2]);",
            @"",
            @"    var blockType = (rng.Next(2) == 0)",
            @"        ? DNA.CastleMinerZ.Terrain.BlockTypeEnum.LootBlock",
            @"        : DNA.CastleMinerZ.Terrain.BlockTypeEnum.LuckyLootBlock;",
            @"",
            @"    if (terrain.SetBlock(world, blockType))",
            @"    {",
            @"        placed++;",
            @"",
            @"        if (blockType == DNA.CastleMinerZ.Terrain.BlockTypeEnum.LootBlock)",
            @"            lootCount++;",
            @"        else",
            @"            luckyCount++;",
            @"    }",
            @"}",
            @"",
            @"return ""Placed/queued "" + placed + "" blocks around the player (Loot: "" + lootCount + "", Lucky: "" + luckyCount + "")."";"
        });
        #endregion

        #region Give Builder Kit

        public static readonly string Example_Simple_GiveBuilderKit = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Simple): Give the local player a small building starter kit.",
            @"///",
            @"/// What it gives:",
            @"/// - 200 Wood Blocks",
            @"/// - 200 Rock Blocks",
            @"/// - 64 Torches",
            @"///",
            @"/// Tip: Easy to edit later if you want different items or amounts.",
            @"/// </summary>",
            @"",
            @"var game   = CastleMinerZGame.Instance;",
            @"var player = game.LocalPlayer;",
            @"",
            @"if (player == null || player.PlayerInventory == null)",
            @"    return ""No valid player inventory yet (are you in-world?)"";",
            @"",
            @"var inv = player.PlayerInventory;",
            @"",
            @"inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.WoodBlock, 200), false);",
            @"inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RockBlock, 200), false);",
            @"inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch, 64), false);",
            @"",
            @"return ""Gave builder kit: 200 Wood, 200 Rock, 64 Torches."";"
        });
        #endregion

        #endregion

        #region Examples: Advanced

        /// <summary>
        /// Advanced examples can show full-source mode (// @full) and techniques like Harmony.
        /// Use sparingly and explain clearly-these run in-process with full trust.
        /// </summary>

        #region Disable Controls

        public static readonly string Example_Advanced_DisableControls = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Advanced): Apply a Harmony patch that short-circuits",
            @"/// ScreenGroup.ProcessInput(InputManager, GameTime).",
            @"/// This sets __result = true and returns false in a Prefix,",
            @"/// preventing the game's UI stack from handling input for that frame",
            @"/// (handy when an overlay wants to capture input).",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Owner ID: Uses a stable Harmony owner ID (""cmz.script.demo/"") so the same script",
            @"/// can safely re-run without stacking patches (calls UnpatchAll(h.Id) first).</para>",
            @"/// <para>Target: Explicitly patches ScreenGroup.ProcessInput(InputManager, GameTime)",
            @"/// with parameter types supplied (C#5-friendly; no nameof required).</para>",
            @"/// <para>Gating: In a real mod, guard the Prefix with your overlay flag:",
            @"/// if (!MyOverlay.InputGate) return true; to allow the original when your UI is closed.</para>",
            @"/// <para>Safety: Run on the main thread. Requires Harmony to be loaded in the AppDomain.",
            @"/// Only affects this game; other mods remain intact.</para>",
            @"/// </remarks>",
            @"/// <example>",
            @"/// To undo this patch later, run the companion ""Unpatch"" script that calls",
            @"/// new Harmony(""cmz.script.demo"").UnpatchAll(""cmz.script.demo"").",
            @"/// </example>",
            @"",
            @"// @full",
            @"// ^ Tells your code-injection tab to compile this file *as-is* (full source),",
            @"//   instead of wrapping it inside a default Script.Run() snippet. This is required",
            @"//   when you define types, Harmony patch classes, namespaces, etc.",
            @"",
            @"using System;",
            @"using HarmonyLib;              // Harmony patching library (Prefix/Postfix/Transpiler).",
            @"using DNA.Drawing.UI;          // ScreenGroup is defined here (not in DNA.CastleMinerZ.UI).",
            @"using DNA.Input;               // InputManager (arg to ScreenGroup.ProcessInput).",
            @"using Microsoft.Xna.Framework; // GameTime (arg to ScreenGroup.ProcessInput).",
            @"",
            @"namespace UserScript",
            @"{",
            @"    /// <summary>",
            @"    /// Entry point that your editor calls at runtime.",
            @"    /// Must be public static and return object so the engine can display a result.",
            @"    /// </summary>",
            @"    public static class Script",
            @"    {",
            @"        /// <summary>",
            @"        /// Compiles and applies all Harmony patches declared in this *same* assembly.",
            @"        /// We also unpatch our previous run (by owner ID) to avoid stacking duplicates.",
            @"        /// </summary>",
            @"        public static object Run()",
            @"        {",
            @"            // Unique owner string for this script's patches.",
            @"            // Use a stable value so you can later unpatch just *this* script's work.",
            @"            var h = new Harmony(""cmz.script.demo"");",
            @"",
            @"            // Safety: prevents stacking the same postfix/prefix multiple times when re-running.",
            @"            // This only removes patches that were applied with *this* owner ID (h.Id).",
            @"            h.UnpatchAll(h.Id);",
            @"",
            @"            // Scan this compiled assembly for [HarmonyPatch]-decorated classes and apply them.",
            @"            // Here, that means the nested class UserScript.Patches.BlockInput (below).",
            @"            h.PatchAll(typeof(Patches).Assembly);",
            @"",
            @"            // Return something printable for your UI (""Patched 2025-10-12T..."").",
            @"            return $""Patched {DateTime.Now.ToString(""s"")}."";",
            @"        }",
            @"    }",
            @"",
            @"    /// <summary>",
            @"    /// Container for one or more Harmony patch classes. Keeping them under a single",
            @"    /// type is purely organizational-Harmony only cares about the attributes.",
            @"    /// </summary>",
            @"    internal static class Patches",
            @"    {",
            @"        /// <summary>",
            @"        /// Patch target:",
            @"        ///   bool ScreenGroup.ProcessInput(InputManager input, GameTime time)",
            @"        ///",
            @"        /// Why this method?",
            @"        ///   ScreenGroup walks the active UI stack and lets each Screen consume input.",
            @"        ///   By intercepting here, you can allow or deny the whole pipeline for a frame.",
            @"        ///",
            @"        /// Attribute breakdown:",
            @"        /// [HarmonyPatch(typeof(ScreenGroup), ...)]",
            @"        ///   -> tells Harmony which *type* we're targeting",
            @"        /// ""ProcessInput""",
            @"        ///   -> method name (use a string instead of nameof for C#5 compatibility)",
            @"        /// new Type[] { typeof(InputManager), typeof(GameTime) }",
            @"        ///   -> exact parameter types so Harmony picks the right overload",
            @"        /// </summary>",
            @"        [HarmonyPatch(typeof(ScreenGroup), ""ProcessInput"", new Type[] { typeof(InputManager), typeof(GameTime) })]",
            @"        static class BlockInput",
            @"        {",
            @"            /// <summary>",
            @"            /// Prefix runs before the original method. Its return value controls execution:",
            @"            ///   - return true  -> let the original ProcessInput run normally.",
            @"            ///   - return false -> skip the original entirely (short-circuit).",
            @"            ///",
            @"            /// __result is the ""fake"" return value that callers will see if we skip the original.",
            @"            /// ProcessInput returns bool, so set __result to whatever the game expects (usually ""handled"").",
            @"            ///",
            @"            /// IMPORTANT:",
            @"            ///   This patch blocks input ALWAYS. In a real mod, you'd likely gate this with your overlay:",
            @"            ///     if (!ModNamespace.InputGate) return true; // Allow original if your overlay isn't capturing.",
            @"            /// </summary>",
            @"            static bool Prefix(ref bool __result)",
            @"            {",
            @"                // If you want to block only when your overlay is open, add:",
            @"                // if (!YourOverlayPublicGate) return true;",
            @"",
            @"                __result = true; // Pretend the input was ""handled"" by someone.",
            @"                return false;    // Skip the original ProcessInput this frame.",
            @"            }",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #region Enable Controls

        public static readonly string Example_Advanced_EnableControls = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Companion (Cleanup): Unpatch all Harmony patches applied by the advanced example.",
            @"/// Calls UnpatchAll(""cmz.script.demo"") to remove only patches owned by that ID,",
            @"/// leaving other mods and game patches untouched.",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Idempotent: Safe to run repeatedly; if nothing is patched under that owner, it's a no-op.</para>",
            @"/// <para>Use case: Clean up after testing/iterating on patch scripts, or to restore normal input handling.</para>",
            @"/// <para>Threading: Run on the main thread.</para>",
            @"/// </remarks>",
            @"",
            @"// @full",
            @"using System;",
            @"using HarmonyLib;",
            @"",
            @"namespace UserScript",
            @"{",
            @"    public static class Script",
            @"    {",
            @"        public static object Run()",
            @"        {",
            @"            // Use the same owner string you used to patch.",
            @"            var h = new Harmony(""cmz.script.demo"");",
            @"",
            @"            // Remove only patches belonging to this owner. Other mods remain intact.",
            @"            h.UnpatchAll(h.Id);",
            @"",
            @"            return $""Unpatched {DateTime.Now.ToString(""s"")}."";",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #region Infinite Vitals

        public static readonly string Example_Advanced_InfiniteVitals = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Advanced): Keep the local player's vitals full every HUD update.",
            @"///",
            @"/// What it does:",
            @"/// - Refills Health to 1.0f every frame.",
            @"/// - Refills Stamina to 1.0f every frame.",
            @"/// - Refills Oxygen to 1.0f every frame.",
            @"///",
            @"/// Notes:",
            @"/// - Uses a Harmony postfix on InGameHUD.OnUpdate(...), so it runs AFTER the game's",
            @"///   normal damage / stamina / oxygen logic and immediately restores the bars.",
            @"/// - This is stronger than patching just one damage path, because it also covers",
            @"///   stamina drain and underwater oxygen drain.",
            @"/// - Safe to re-run; it first removes any prior patch with the same Harmony ID.",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Owner ID: cmz.script.fullvitals</para>",
            @"/// <para>Threading: Run on the main thread.</para>",
            @"/// <para>Cleanup: Use the companion restore script to remove only this patch.</para>",
            @"/// </remarks>",
            @"",
            @"// @full",
            @"using System;",
            @"using HarmonyLib;",
            @"using DNA;",
            @"using DNA.CastleMinerZ.UI;",
            @"using DNA.Drawing;",
            @"using Microsoft.Xna.Framework;",
            @"",
            @"namespace UserScript",
            @"{",
            @"    public static class Script",
            @"    {",
            @"        public static object Run()",
            @"        {",
            @"            var h = new Harmony(""cmz.script.fullvitals"");",
            @"",
            @"            // Prevent duplicate ownership patches if the script is run again.",
            @"            h.UnpatchAll(h.Id);",
            @"",
            @"            // Apply all [HarmonyPatch] classes in this compiled script assembly.",
            @"            h.PatchAll(typeof(Patches).Assembly);",
            @"",
            @"            return ""Infinite vitals patch applied at "" + DateTime.Now.ToString(""s"") + ""."";",
            @"        }",
            @"    }",
            @"",
            @"    internal static class Patches",
            @"    {",
            @"        /// <summary>",
            @"        /// Patch target:",
            @"        ///   protected override void InGameHUD.OnUpdate(DNAGame game, GameTime gameTime)",
            @"        ///",
            @"        /// Why postfix?",
            @"        ///   The HUD's update performs oxygen drain, health recovery, stamina recovery,",
            @"        ///   and related frame-based state changes. By running AFTER vanilla logic,",
            @"        ///   we can simply overwrite the vitals to full each frame.",
            @"        /// </summary>",
            @"        [HarmonyPatch(typeof(InGameHUD), ""OnUpdate"", new Type[] { typeof(DNAGame), typeof(GameTime) })]",
            @"        static class Patch_InGameHUD_OnUpdate_FullVitals",
            @"        {",
            @"            static void Postfix(InGameHUD __instance)",
            @"            {",
            @"                if (__instance == null)",
            @"                    return;",
            @"",
            @"                // Avoid fighting the normal death / respawn flow once dead.",
            @"                if (__instance.LocalPlayer == null || __instance.LocalPlayer.Dead)",
            @"                    return;",
            @"",
            @"                __instance.PlayerHealth  = 1f;",
            @"                __instance.PlayerStamina = 1f;",
            @"                __instance.PlayerOxygen  = 1f;",
            @"            }",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #region Restore Vitals

        public static readonly string Example_Advanced_RestoreVitals = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Companion (Cleanup): Remove the infinite vitals Harmony patch.",
            @"/// Only unpatches work owned by this script's Harmony ID.",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Idempotent: Safe to run multiple times.</para>",
            @"/// <para>Owner ID: cmz.script.fullvitals</para>",
            @"/// <para>Threading: Run on the main thread.</para>",
            @"/// </remarks>",
            @"",
            @"// @full",
            @"using System;",
            @"using HarmonyLib;",
            @"",
            @"namespace UserScript",
            @"{",
            @"    public static class Script",
            @"    {",
            @"        public static object Run()",
            @"        {",
            @"            var h = new Harmony(""cmz.script.fullvitals"");",
            @"            h.UnpatchAll(h.Id);",
            @"            return ""Infinite vitals patch removed at "" + DateTime.Now.ToString(""s"") + ""."";",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #region Freeze Noon

        public static readonly string Example_Advanced_FreezeNoon = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Example (Advanced): Freeze the world's time at noon.",
            @"///",
            @"/// What it does:",
            @"/// - Hooks GameScreen.Update(...).",
            @"/// - After the vanilla time-of-day tick runs, it forces Day = 0.50f every frame.",
            @"///",
            @"/// Notes:",
            @"/// - Noon is used here because it is bright and easy to visually verify.",
            @"/// - If you're the host, the game's normal time sync will keep pushing this time outward",
            @"///   on its regular network update cadence.",
            @"/// - Safe to re-run; it first removes any prior patch with the same Harmony ID.",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Owner ID: cmz.script.freezenoon</para>",
            @"/// <para>Threading: Run on the main thread.</para>",
            @"/// <para>Cleanup: Use the companion restore script to remove only this patch.</para>",
            @"/// </remarks>",
            @"",
            @"// @full",
            @"using System;",
            @"using HarmonyLib;",
            @"using DNA;",
            @"using DNA.CastleMinerZ;",
            @"using DNA.Drawing;",
            @"using Microsoft.Xna.Framework;",
            @"",
            @"namespace UserScript",
            @"{",
            @"    public static class Script",
            @"    {",
            @"        public static object Run()",
            @"        {",
            @"            var h = new Harmony(""cmz.script.freezenoon"");",
            @"",
            @"            // Prevent duplicate ownership patches if the script is run again.",
            @"            h.UnpatchAll(h.Id);",
            @"",
            @"            // Apply all [HarmonyPatch] classes in this compiled script assembly.",
            @"            h.PatchAll(typeof(Patches).Assembly);",
            @"",
            @"            return ""Freeze-noon patch applied at "" + DateTime.Now.ToString(""s"") + ""."";",
            @"        }",
            @"    }",
            @"",
            @"    internal static class Patches",
            @"    {",
            @"        /// <summary>",
            @"        /// Patch target:",
            @"        ///   public override void GameScreen.Update(DNAGame game, GameTime gameTime)",
            @"        ///",
            @"        /// Why postfix?",
            @"        ///   GameScreen.Update advances Day based on elapsed time. By forcing the value",
            @"        ///   AFTER the original update, we effectively pin the world clock to noon.",
            @"        /// </summary>",
            @"        [HarmonyPatch(typeof(GameScreen), ""Update"", new Type[] { typeof(DNAGame), typeof(GameTime) })]",
            @"        static class Patch_GameScreen_Update_FreezeNoon",
            @"        {",
            @"            static void Postfix(GameScreen __instance)",
            @"            {",
            @"                if (__instance == null)",
            @"                    return;",
            @"",
            @"                __instance.Day = 0.50f;",
            @"            }",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #region Restore Time

        public static readonly string Example_Advanced_RestoreTime = string.Join(Environment.NewLine, new[]
        {
            @"/// <summary>",
            @"/// Companion (Cleanup): Remove the freeze-noon Harmony patch.",
            @"/// Only unpatches work owned by this script's Harmony ID.",
            @"/// </summary>",
            @"/// <remarks>",
            @"/// <para>Idempotent: Safe to run multiple times.</para>",
            @"/// <para>Owner ID: cmz.script.freezenoon</para>",
            @"/// <para>Threading: Run on the main thread.</para>",
            @"/// </remarks>",
            @"",
            @"// @full",
            @"using System;",
            @"using HarmonyLib;",
            @"",
            @"namespace UserScript",
            @"{",
            @"    public static class Script",
            @"    {",
            @"        public static object Run()",
            @"        {",
            @"            var h = new Harmony(""cmz.script.freezenoon"");",
            @"            h.UnpatchAll(h.Id);",
            @"            return ""Freeze-noon patch removed at "" + DateTime.Now.ToString(""s"") + ""."";",
            @"        }",
            @"    }",
            @"}"
        });
        #endregion

        #endregion
    }
}
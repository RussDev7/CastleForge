/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using System.Collections.Concurrent;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection.Emit;
using System.Threading.Tasks;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using DNA.Timers;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).

namespace TacticalNuke
{
    /// <summary>
    /// All Harmony patches in one place. Using ApplyAllPatches()
    /// will scan this assembly for nested [HarmonyPatch] classes
    /// and apply them, then log exactly what got patched.
    /// </summary>
    class GamePatches
    {
        #region Patcher Initiation

        // Keep a handle to this Harmony instance so we can unpatch later.
        private static Harmony _harmony;
        private static string _harmonyId;

        /// <summary>
        /// Best-effort Harmony bootstrap:
        /// - Scans this assembly for all classes marked with [HarmonyPatch].
        /// - All classes marked with the additional [HarmonySilent] attribute will have logging silenced.
        /// - Patches each class independently inside a try/catch (one bad target won't kill the rest).
        /// - Logs a per-class result and a final summary of methods actually patched by our Harmony ID.
        /// - Leaves your UI wiring call in place after patching.
        /// </summary>
        public static void ApplyAllPatches()
        {
            Log("[Harmony] Starting game patching.");

            // Create a stable, unique Harmony ID for this mod. Using the namespace helps avoid collisions.
            _harmonyId = $"castleminerz.mods.{typeof(GamePatches).Namespace}.patches"; // Unique ID based on namespace.
            _harmony = new Harmony(_harmonyId);                                      // Create & store the Harmony instance.

            // Choose which assembly to scan for patch classes.
            // If you split patches across multiple assemblies, call this routine for each assembly.
            Assembly asm = typeof(GamePatches).Assembly;

            int successCount = 0;
            int failCount = 0;

            // Enumerate every class that has at least one [HarmonyPatch] attribute,
            // and patch it independently (best-effort).
            foreach (var patchType in EnumeratePatchTypes(asm))
            {
                try
                {
                    // Create a processor for this patch class and apply all of its prefixes/postfixes/transpilers.
                    var proc = _harmony.CreateClassProcessor(patchType);
                    var targets = proc?.Patch(); // List<MethodBase> of target methods Harmony hooked (may be null).
                    successCount++;

                    /*
                    // NOTE: Don't show silent patch containers.
                    if (!IsSilent(patchType))
                    {
                        int targetCount = targets?.Count ?? 0;
                        Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                    }
                    */

                    int targetCount = targets?.Count ?? 0;
                    Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"[Harmony] FAILED patching {patchType.FullName}: {ex.GetType().Name}: {ex.Message}.");
                }
            }

            // Summarize what we actually patched (filter by Owner == our Harmony ID).
            var ours = _harmony.GetPatchedMethods()
                               .Where(m =>
                               {
                                   var info = Harmony.GetPatchInfo(m);
                                   return info != null && (info.Owners?.Contains(_harmonyId) ?? false);
                               })
                               .ToList();

            // Print per-method details, but filter out any silent patches FIRST.
            foreach (var m in ours)
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) continue;

                // Filter out silent patches before printing anything.
                var prefixes = Filter(info.Prefixes).ToList();
                var postfixes = Filter(info.Postfixes).ToList();
                var transpilers = Filter(info.Transpilers).ToList();

                // If nothing remains (all were silent), don't log this method at all.
                if (prefixes.Count == 0 && postfixes.Count == 0 && transpilers.Count == 0) continue;

                // Show filtered counts (not the raw/total counts).
                Log($"[Harmony] Patched method: {Describe(m)} | " +
                    $"[Prefixes={prefixes.Count}] [Postfixes={postfixes.Count}] [Transpilers={transpilers.Count}].");

                foreach (var p in prefixes) Log($"  • Prefix    : {Describe(p.PatchMethod)}.");
                foreach (var p in postfixes) Log($"  • Postfix   : {Describe(p.PatchMethod)}.");
                foreach (var p in transpilers) Log($"  • Transpiler: {Describe(p.PatchMethod)}.");
            }

            Log($"[Harmony] Patching complete. Success={successCount}, Failed={failCount}, MethodsPatchedByUs={ours.Count}.");
        }

        /// <summary>
        /// Unpatch everything applied by this mod's Harmony ID only
        /// (restores original game methods without touching other mods).
        /// </summary>
        public static void DisableAll()
        {
            if (_harmony != null)
            {
                Log($"[Harmony] Unpatching all ({_harmonyId}).");
                _harmony.UnpatchAll(_harmonyId);
            }
        }

        #region Silent Attribute

        /// <summary>
        /// Lets you tag a whole patch class or a single method so the patch-reporting logger will ignore it.
        /// </summary>
        [System.AttributeUsage(AttributeTargets.Class | System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
        internal sealed class HarmonySilentAttribute : Attribute { };

        #endregion

        #region Patcher Helpers

        /// <summary>
        /// Return true if the method or its declaring type is marked with [HarmonySilent].
        /// </summary>
        static bool IsSilent(MemberInfo mi)
        {
            if (mi == null) return false;

            // Respect [HarmonySilent] on the member itself.
            if (mi.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            // Respect [HarmonySilent] on declaring type.
            var dt = (mi as MethodBase)?.DeclaringType ?? mi as Type;
            if (dt != null && dt.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            return false;
        }

        /// <summary>
        /// Filters out patches whose patch method (or its declaring type) is marked "silent".
        /// </summary>
        static IEnumerable<Patch> Filter(IEnumerable<Patch> src)
            => (src ?? Enumerable.Empty<Patch>()).Where(p => !IsSilent(p.PatchMethod));

        /// <summary>
        /// Finds all types that are Harmony patch containers in the given assembly
        /// (i.e., classes marked with [HarmonyPatch]). Using an attribute scan keeps us
        /// from trying to patch non-patch helper classes accidentally.
        /// </summary>
        private static IEnumerable<Type> EnumeratePatchTypes(Assembly asm)
        {
            // AccessTools.GetTypesFromAssembly is defensive (skips type-load failures).
            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
            {
                if (t == null || !t.IsClass) continue;

                // Harmony 2.x attribute name is "HarmonyLib.HarmonyPatch".
                // Compare by FullName or simple Name to stay robust across versions/builds.
                bool hasPatchAttr = t.GetCustomAttributes(inherit: true)
                                    .Any(a => a != null &&
                                              (a.GetType().FullName == "HarmonyLib.HarmonyPatch" ||
                                               a.GetType().Name == "HarmonyPatch"));
                if (hasPatchAttr)
                    yield return t;
            }
        }

        /// <summary>
        /// Nice method formatter for log output: TypeName.MethodName(T0, T1, ...).
        /// </summary>
        private static string Describe(MethodBase m)
        {
            if (m == null) return "(null)";
            try
            {
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "(global)";
                string pars = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                return $"{type}.{m.Name}({pars})";
            }
            catch
            {
                // Fallback if reflection blows up for any reason.
                return m.ToString();
            }
        }
        #endregion

        #endregion

        #region Patches

        // =================================================================================================
        // Tactical Nuke - Patch Pack
        // -------------------------------------------------------------------------------------------------
        // Overview / Flow:
        //
        // • NukeRegistry:  Central toggles and IDs (NukeId, TextureSurrogate, DoBlockRetexture).
        // • NukePlaceGate: (Not currently used below) small gate helper for brief swap windows.
        // • NukeMap:       Tracks our placed nuke voxels (using the TextureSurrogate type).
        //
        // • Cleanup & Detonate:
        //   - Patch_SetBlock_CleanupNukeMap: Purges NukeMap when a surrogate voxel is overwritten.
        //   - Patch_Explosive_Update_AllowTextureSurrogateDetonate: Allows our surrogate to detonate when its
        //                                                           fuse expires, mirroring vanilla behavior.
        //
        // • Input / Arming:
        //   - Patch_ActivateTextureSurrogateLikeC4: Right-click detonates our nukes regardless of held item;
        //                                           left-click passes to vanilla (prevents accidental fusing).
        //   - Patch_ArmNextPlacementIfNuke:         Records a one-shot "arm next placement" intent.
        //   - NukeHelpers:                          One-shot arming flag (prevents swap bleed into next C4).
        //
        // • Mining / Pickup / Content Tuning:
        //   - Patch_Spade_Mines_Nuke_ToPickup: Spade mining returns the Nuke item from our voxels.
        //   - Patch_MakeTextureSurrogateMineable_CMZ + NukeBlockTuning: Make surrogate behave like C4 (mineable,
        //                                                               interactable, etc.).
        //
        // • Multiplayer safety (vanilla-safe surrogate pickups):
        //   - NukePickupNet: Classifies "unsafe" item IDs (outside enum OR known unimplemented vanilla slots).
        //   - Patch_CreatePickupMessage_LocalOnlyForSyntheticNuke:       If dropping our configured NukeId and it's unsafe,
        //                                                                spawn pickup locally and skip network send.
        //   - Patch_ConsumePickupMessage_Send_LocalOnlyForSyntheticNuke: If consuming our unsafe Nuke pickup, resolve
        //                                                                locally and skip broadcast to other clients.
        //   - Patch_RequestPickupMessage_Send_LocalOnlyForSyntheticNuke: If picking up our unsafe Nuke pickup (non-host),
        //                                                                grant/remove locally and skip requesting from host.
        //
        // • Projectile-triggered detonation:
        //   - Patch_Tracer_Update_DetonateNukesOnHit / Patch_Bullets_Detonate_Nukes: Shooting a nuke
        //     voxel triggers a detonation (network-original C4 type).
        //
        // • Daisy chaining / Blast tables:
        //   - Patch_Explosive_DaisyChain_TextureSurrogates: Searches neighbors in the blast radius and triggers
        //                                                   any tracked nuke voxels.
        //   - Patch_BlockWithinLevelBlastRange:             Replaces vanilla block-hardness distance test to
        //                                                   respect temporarily modified radii.
        //   - Patch_Explosive_NukeRadiusPerDetonation (two variants below): Temporarily expands C4 blast
        //                                             for the specific detonation at a marked position.
        //                                             Tables are restored immediately after.
        //
        // • Skinning (block textures) & item registration:
        //   - Patch_NukeSkin_OnSecondaryLoad + NukeTextureSurrogateSkin: Writes top/side/bottom textures to the
        //                                                                surrogate's distinct atlas tiles.
        //   - EnsureUniqueTextureSurrogateFaceSlots: Ensures TextureSurrogate has unique atlas slots per face.
        //   - Patch_RegisterNuke_Once_And_PaintIcon: Registers the item class, paints icon cells, and
        //                                            (optionally) applies block skin once icons exist.
        //   - Patch_SetBlock_MarkNukePlacement: Marks a placed surrogate voxel as ours and clears arming.
        //
        //
        // • Config-driven cookbook + UI text, hot-reloadable:
        //   - NukeRecipeInjector: Injects/updates a custom Explosives-tab recipe for the Nuke into Recipe.CookBook.
        //                       Safe to call repeatedly; removes old entries first and can insert after vanilla C4.
        //   - NukeSurrogateNameInjector: Optionally overrides the TextureSurrogate BlockType.Name (ex: "Nuke").
        //                              Blank config restores the original localized name (ex: Strings.Space_Goo).
        //   - NukeSurrogateDescriptionInjector: Optionally overrides the Nuke inventory tooltip/description by
        //                                     patching InventoryItemClass._description via Harmony FieldRef.
        //                                     Blank config restores the original captured description.
        //   - Patch_Hotkey_ReloadConfig_TacticalNuke: Hot-reloads config on the main thread and reapplies recipe,
        //                                           name/description, and (optionally) forces retexture safely
        //                                           via WithNoBindings(...) to avoid GraphicsDevice binding errors.
        //   * Note: Changing NukeId at runtime requires restart to re-register the item class + repaint icon.
        //
        // Notes:
        // - Multiplayer: Detonations are originated by the local player via network messages,
        //   preserving vanilla replication.
        // - Safety: All reflective lookups are defensive; failures fall back to vanilla.
        //
        // =================================================================================================

        #region TacticalNuke - Core (Registry / FuseConfig / AsyncExplosionManagerConfig / AnnouncementConfig / NukeMap & CraterConfig / NukeRecipeConfig / VanillaExplosiveConfig)

        #region NukeRegistry

        /// <summary>
        /// Global registry for Nuke configuration and texture behavior.
        /// </summary>
        internal static class NukeRegistry
        {
            /// <summary>Inventory item ID used for the Nuke (icon & selection). Must be valid in your build.</summary>
            public static int           NukeId               = (int)InventoryItemIDs.SpaceKnife;

            /// <summary>
            /// The block used as visual/placement surrogate for a Nuke in world (typically TextureSurrogate).
            /// All texture painting and world checks key off this type.
            /// </summary>
            public static BlockTypeEnum TextureSurrogate     = BlockTypeEnum.BombBlock;

            /// <summary>
            /// Master switch: If false, texture painting/skinning is skipped (useful for debugging).
            /// </summary>
            public static bool  DoBlockRetexture = true;

            public static int   NUKE_BLOCK_RADIUS = 50;
            public static float NUKE_KILL_RADIUS  = 32f;
            public static float NUKE_DMG_RADIUS   = 128f;

            public static bool NukeIgnoresHardness = true; // true = break ANY block during a nuke detonation.

            public static readonly HashSet<BlockTypeEnum> ExtraBreakables = new HashSet<BlockTypeEnum>
            {
                BlockTypeEnum.Bedrock,
                BlockTypeEnum.FixedLantern,
                // ...
            };
        }
        #endregion

        #region NukeFuseConfig

        /// <summary>
        /// Config / small helpers.
        /// </summary>
        internal static class NukeFuseConfig
        {
            // Tweak to taste:
            public static int   NukeFuseSeconds      = 24;  // Total fuse for nukes.
            public static float FastBlinkLastSeconds = 6f;  // Last N seconds = fastest blink.
            public static float MidBlinkLastSeconds  = 12f; // Last N seconds = medium blink.

            // Blink intervals (seconds):
            public static float SlowBlink = 0.50f;
            public static float MidBlink  = 0.25f;
            public static float FastBlink = 0.125f;

            // Fuse noise:
            public static float FuseVolume = 0.30f;
        }
        #endregion

        #region AsyncExplosionManagerConfig

        /// <summary>
        /// Global perf knob for how many block clears we allow per frame during async explosion application.
        /// Increase to process faster (but risk frame spikes); decrease for smoother frames.
        /// </summary>
        internal static class AsyncExplosionManagerConfig
        {
            public static bool Enabled           = true;
            public static bool IncludeDefaults   = true;
            public static int  MaxBlocksPerFrame = 2000;
        }
        #endregion

        #region AnnouncementConfig

        internal static class AnnouncementConfig
        {
            // Suppress one "Disarmed" announce on the current thread (used around detonation SetBlock).
            internal static class AnnounceGuard
            {
                [ThreadStatic] public static bool SuppressDisarmedOnce;
            }

            /// <summary>
            /// Master enable. If false, all announce calls no-op immediately.
            /// </summary>
            public static bool Enabled           = false;

            /// <summary>
            /// If true -> send via BroadcastTextMessage (server/global). If false -> SendFeedback (local HUD).
            /// </summary>
            public static bool AnnounceToServer  = false;

            /// <summary>
            /// Minimum seconds between repeated messages of the same type at the same block to avoid spam.
            /// </summary>
            public static float MinRepeatSeconds = 2f;

            // Expandable event kinds.
            public enum MessageType
            {
                Armed,        // Fuse armed on a nuke.
                Removed,      // Fuse cancelled / block removed.
                Detonated,    // Nuke blast occurred.
                ChainTrigger, // Chain reaction started (detonated -> triggered neighbors).
                Countdown10,  // 10s warning.
                Countdown5,   // 5s warning.
                Countdown1,   // Final 1s tick.
                Disarmed      // Explicit disarm (if you add a flow separate from "Removed").
            }

            // Simple last-seen timestamps to avoid flooding chat/logs.
            private static readonly Dictionary<string, long> _lastStampMs = new Dictionary<string, long>();

            /// <summary>
            /// Main entry point (keeps your original signature).
            /// </summary>
            /// <param name="messageType">Event type (Armed/Removed/...)</param>
            /// <param name="nukeLocation">Block coords; optional</param>
            public static void Announce(MessageType messageType, IntVector3 nukeLocation = default)
                => Announce(messageType, nukeLocation, chainCount: 0, actor: null);

            /// <summary>
            /// Overload with optional metadata (non-breaking): include who triggered it and chain size.
            /// </summary>
            /// <param name="messageType">Event type</param>
            /// <param name="nukeLocation">World coords</param>
            /// <param name="chainCount">If ChainTrigger, how many nukes were queued/triggered</param>
            /// <param name="actor">Display name/ID of the player (if known)</param>
            public static void Announce(
                MessageType messageType,
                IntVector3 nukeLocation = default,
                int chainCount = 0,
                string actor = null)
            {
                if (!Enabled)
                    return;

                // Throttle repeats: Same (type+pos) within MinRepeatSeconds -> drop.
                var key = $"{messageType}@{nukeLocation.X},{nukeLocation.Y},{nukeLocation.Z}";
                long now = Environment.TickCount;
                if (MinRepeatSeconds > 0)
                {
                    if (_lastStampMs.TryGetValue(key, out var last) &&
                        now - last < (long)(MinRepeatSeconds * 1000f))
                        return;

                    _lastStampMs[key] = now;
                }

                string msg = ComposeMessage(messageType, nukeLocation, chainCount, actor);
                if (string.IsNullOrEmpty(msg))
                    return;

                try
                {
                    if (AnnounceToServer)
                    {
                        BroadcastTextMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, msg);
                        Log(msg);
                    }
                    else
                    {
                        SendFeedback(msg);
                    }
                }
                catch
                {
                    // Never crash the game for chat UX; silently ignore delivery errors.
                }
            }

            /// <summary>
            /// Builds the final string from a template table. Swap wording/emoji centrally here.
            /// </summary>
            private static string ComposeMessage(MessageType type, IntVector3 pos, int chainCount, string actor)
            {
                // Pull knobs from the existing registry/config so text always matches gameplay:
                int radius = Math.Max(NukeRegistry.NUKE_BLOCK_RADIUS, (int)NukeRegistry.NUKE_KILL_RADIUS);
                int secs   = NukeFuseConfig.NukeFuseSeconds;

                string p   = $"[{pos.X},{pos.Y},{pos.Z}]";
                string who = string.IsNullOrEmpty(actor) ? CastleMinerZGame.Instance.MyNetworkGamer.Gamertag : actor;

                switch (type)
                {
                    case MessageType.Armed:
                        return $"DANGER: Nuke armed at {p}! Detonation in {secs}s - Evacuate {radius}+ blocks.";
                    case MessageType.Removed: // Not used.
                        return $"NOTICE: Nuke at {p} disarmed/removed.";
                    case MessageType.Detonated:
                        return $"NUKE DETONATED at {p}. Stay clear of the blast zone.";
                    case MessageType.ChainTrigger:
                        return (chainCount > 0)
                            ? $"CHAIN REACTION: {chainCount} additional nukes triggered near {p}."
                            : $"CHAIN REACTION: Additional nukes triggered near {p}.";
                    case MessageType.Countdown10:
                        return $"WARNING: 10 seconds to detonation at {p}.";
                    case MessageType.Countdown5:
                        return $"WARNING: 5 seconds to detonation at {p}.";
                    case MessageType.Countdown1:
                        return $"WARNING: 1 second to detonation at {p}.";
                    case MessageType.Disarmed:
                        return $"Nuke at {p} was disarmed by {who}.";
                }

                return null;
            }
        }
        #endregion

        #region NukeMap

        /// <summary>
        /// Tracks world positions of our Nuke voxels (placed using <see cref="TextureSurrogate"/>).
        /// Prevents accidental triggering on unrelated TextureSurrogates.
        /// </summary>
        internal static class NukeMap
        {
            private static readonly HashSet<IntVector3> _set = new HashSet<IntVector3>();

            /// <summary>Record that a world cell now hosts our Nuke surrogate voxel.</summary>
            public static void Add(IntVector3 p)    => _set.Add(p);

            /// <summary>Forget a world cell (e.g., overwritten by other blocks or detonated).</summary>
            public static void Remove(IntVector3 p) => _set.Remove(p);

            /// <summary>True if we believe the world cell contains our Nuke surrogate.</summary>
            public static bool Has(IntVector3 p)    => _set.Contains(p);
        }

        /// <summary>
        /// Tunables for the crater generator. All fields are live-read static values
        /// so you can edit them from a menu/INI and see changes next explosion.
        /// </summary>
        internal static class CraterConfig
        {
            public static CraterShape Shape      = CraterShape.Sphere;
            public static bool        Hollow     = false; // If true, only the outer shell is removed.
            public static int         ShellThick = 2;     // Shell thickness in blocks when <see cref="Hollow"/> is true.
            public static float       YScale     = 0.70f; // Vertical scale; < 1 = flatter, > 1 = taller.
            public static float       XZScale    = 1.00f; // Horizontal scale applied to X and Z axes.
            public static float       EdgeJitter = 0.15f; // Rim scallop amount (0..0.5). Acts only near the edge band; higher = more ragged rim.
            public static int         NoiseSeed  = 1337;  // Seed for deterministic jitter across a given blast.
        }
        #endregion

        #region NukeConfig (Name / Description)

        /// <summary>
        /// Holds the user-configurable text shown for the Nuke's surrogate.
        /// - Name:        The display name shown in UI lists/tooltips (when we override the block name).
        /// - Description: The item tooltip/description string used by the surrogate InventoryItemClass.
        /// These values are populated by TNConfig.ApplyToStatics() and can be refreshed on config reload.
        /// </summary>
        internal static class NukeConfig
        {
            /// <summary>
            /// Optional display name override for the TextureSurrogate block.
            /// If blank/whitespace, we keep the game's default localized name (ex: Strings.Space_Goo).
            /// </summary>
            public static string TextureSurrogateName        = "Tactical Nuke";

            /// <summary>
            /// Tooltip/description text for the surrogate's inventory item entry.
            /// If blank/whitespace, we keep (or restore) the original default description.
            /// </summary>
            public static string TextureSurrogateDescription = "Extreme yield demolition device. Detonate with caution.";
        }
        #endregion

        #region NukeRecipeConfig

        /// <summary>
        /// Config mapping for the Nuke crafting recipe (Explosives tab).
        /// The recipe produces the Nuke item at <see cref="NukeRegistry.NukeId"/>.
        /// </summary>
        internal static class NukeRecipeConfig
        {
            /// <summary>Master toggle for adding/updating the Nuke recipe in <see cref="Recipe.CookBook"/>.</summary>
            public static bool Enabled       = true;

            /// <summary>Output stack size for the crafted Nuke.</summary>
            public static int  OutputCount   = 1;

            /// <summary>If true, insert right after the vanilla C4 recipe; otherwise append to end.</summary>
            public static bool InsertAfterC4 = true;

            /// <summary>
            /// Ingredients list format: "ItemIdOrName:Count, ItemIdOrName:Count, ..."
            /// Names are matched case-insensitively and ignore spaces/underscores.
            /// </summary>
            public static string Ingredients =
                "ExplosivePowder:30, C4:30, TNT:30, SandBlock:30, Coal:30, SpaceRockInventory:30";
        }
        #endregion

        #region VanillaExplosiveConfig

        /// <summary>
        /// Config mapping for vanilla TNT/C4 radii. Values are "in-game" radii.
        /// Destruction radii are written as (value + 1) into cDestructionRanges to
        /// match how CastleMiner Z uses those tables.
        /// </summary>
        internal static class VanillaExplosiveConfig
        {
            /// <summary>Master toggle for applying TNT/C4 tweaks at SecondaryLoad.</summary>
            public static bool Enabled = false;

            /// <summary>Logical destruction radii (in blocks) for TNT/C4, before +1 fudge.</summary>
            public static int TNT_BlockRadius = 2;
            public static int C4_BlockRadius  = 3;

            /// <summary>Damage radii (world units) for TNT/C4.</summary>
            public static float TNT_DmgRadius = 6f;
            public static float C4_DmgRadius  = 12f;

            /// <summary>Kill radii (world units) for TNT/C4.</summary>
            public static float TNT_KillRadius = 3f;
            public static float C4_KillRadius  = 6f;
        }
        #endregion

        #endregion

        #region Hotkey: Reload Config (Configurable)

        /// <summary>
        /// SUMMARY
        /// -------
        /// Adds a configurable hotkey (Ctrl/Alt/Shift/Win + 1 main key) to hot-reload the
        /// mod's config at runtime. We hook inside InGameHUD.OnPlayerInput so it runs on
        /// the main game thread (safe for content ops and Harmony-driven skin updates).
        ///
        /// DESIGN NOTES
        /// ------------
        /// • Parsing: Forgiving tokenizer; accepts "Ctrl+Shift+F3", "ctrl f3", "Control+F3",
        ///   "Win+R", "Alt+0", "A", "F12", etc. Case-insensitive. Unknown tokens are ignored.
        /// • Binding: Keys.None disables the hotkey.
        /// • Detection: Rising-edge detector (fires once when keys go from "not pressed" -> "pressed").
        /// • Input source: XNA KeyboardState (polling). The Windows key is checked via
        ///   LeftWindows/RightWindows-be aware some OS/game overlays swallow Win keys.
        /// • Threading: Runs in the HUD input tick (game thread). Keep work lightweight.
        ///
        /// USAGE
        /// -----
        /// TNHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (TNHotkeys.ReloadPressedThisFrame()) { TNConfig.LoadApply(); ... }
        ///
        /// EXAMPLES
        /// --------
        /// "F9"                 -> F9.
        /// "Ctrl+F3"            -> Ctrl + F3.
        /// "Control Shift F12"  -> Ctrl + Shift + F12.
        /// "Win+R"              -> Windows + R.
        /// "Alt+0"              -> Alt + D0 (top-row zero).
        /// "" or null           -> Disabled (Keys.None).
        /// </summary>

        #region Hotkey Binding Model

        /// <summary>
        /// Minimal (Ctrl/Alt/Shift/Win) + one main key binding.
        /// <para>Use <see cref="Parse(string)"/> to create from strings like: "Ctrl+Shift+F3".</para>
        /// </summary>
        internal struct HotkeyBinding
        {
            /// <summary>Modifier flags. Plain fields on purpose (no recursion in property setters).</summary>
            public bool Ctrl, Alt, Shift, Win;

            /// <summary>Main key; Keys.None disables the binding.</summary>
            public Microsoft.Xna.Framework.Input.Keys Key;

            /// <summary>
            /// Parses a human-friendly hotkey like "Ctrl+Shift+F3", "Alt+0", "Win+R".
            /// Unknown tokens are ignored; if no main key is recognized -> Keys.None.
            /// </summary>
            /// <remarks>
            /// Accepts: "ctrl/control", "alt", "shift", "win/windows", F1..F24, A..Z, 0..9, or any <see cref="Microsoft.Xna.Framework.Input.Keys"/> name.
            /// </remarks>
            public static HotkeyBinding Parse(string s)
            {
                var hk = new HotkeyBinding { Key = Microsoft.Xna.Framework.Input.Keys.None };
                if (string.IsNullOrWhiteSpace(s)) return hk;

                var tokens = s.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in tokens)
                {
                    var t = raw.Trim().ToLowerInvariant();
                    switch (t)
                    {
                        case "ctrl":
                        case "control": hk.Ctrl  = true; break;
                        case "alt":     hk.Alt   = true; break;
                        case "shift":   hk.Shift = true; break;
                        case "win":
                        case "windows": hk.Win   = true; break;

                        default:
                            // F-keys (F1..F24).
                            if (t.Length >= 2 && t[0] == 'f' && int.TryParse(t.Substring(1), out var f) && f >= 1 && f <= 24)
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.F1 + (f - 1));
                            }
                            // A..Z.
                            else if (t.Length == 1 && t[0] >= 'a' && t[0] <= 'z')
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.A + (t[0] - 'a'));
                            }
                            // 0..9 (top row).
                            else if (t.Length == 1 && t[0] >= '0' && t[0] <= '9')
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.D0 + (t[0] - '0'));
                            }
                            // Any XNA Keys enum name (e.g., "PageUp", "Insert").
                            else if (Enum.TryParse(raw, ignoreCase: true, out Microsoft.Xna.Framework.Input.Keys k))
                            {
                                hk.Key = k;
                            }
                            break;
                    }
                }
                return hk;
            }

            /// <summary>
            /// Returns true while the binding is currently depressed in the given <see cref="KeyboardState"/>.
            /// Checks both left/right modifier variants (e.g., LeftControl/RightControl).
            /// </summary>
            public bool IsDown(Microsoft.Xna.Framework.Input.KeyboardState ks)
            {
                if (Key == Microsoft.Xna.Framework.Input.Keys.None) return false;

                bool ctrl  = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl);
                bool alt   = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightAlt);
                bool shift = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
                bool win   = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftWindows) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightWindows);

                if (Ctrl  && !ctrl)  return false;
                if (Alt   && !alt)   return false;
                if (Shift && !shift) return false;
                if (Win   && !win)   return false;

                return ks.IsKeyDown(Key);
            }
        }
        #endregion

        #region Hotkey Utility (Edge Detection + Binding)

        /// <summary>
        /// Runtime hotkey manager for "reload config".
        /// <para>Call <see cref="SetReloadBinding(string)"/> after reading INI, then poll <see cref="ReloadPressedThisFrame"/> each HUD tick.</para>
        /// </summary>
        internal static class TNHotkeys
        {
            private static HotkeyBinding _reload;
            private static bool _hasPrev;
            private static Microsoft.Xna.Framework.Input.KeyboardState _prev;

            /// <summary>
            /// Sets (or disables) the reload binding. Resets the edge detector to avoid a spurious trigger right after change.
            /// </summary>
            public static void SetReloadBinding(string s)
            {
                _reload = HotkeyBinding.Parse(s);
                _hasPrev = false; // Reset edge detector so we don't fire instantly after changing binding.
                Log($"[Nuke] Reload hotkey set to \"{s}\".");
            }

            /// <summary>
            /// Returns true exactly once when the binding transitions to pressed this frame.
            /// </summary>
            public static bool ReloadPressedThisFrame()
            {
                var now = Microsoft.Xna.Framework.Input.Keyboard.GetState();
                if (!_hasPrev) { _prev = now; _hasPrev = true; return false; }

                bool nowDown = _reload.IsDown(now);
                bool prevDown = _reload.IsDown(_prev);
                _prev = now;

                return nowDown && !prevDown; // Rising edge -> one-shot.
            }
        }
        #endregion

        #region Hotkey: Reload Config (Main-thread, Safe Repaint Via WithNoBindings)

        /// <summary>
        /// Listens for the reload hotkey inside InGameHUD.OnPlayerInput so all work executes on the main thread.
        /// Keeps the body small; heavy lifting should be inside TNConfig.LoadApply().
        ///
        /// Safety:
        /// • When repainting atlases we wrap the work in <see cref="WithNoBindings(GraphicsDevice, Action)"/>
        ///   which calls SetRenderTarget(null) and unbinds all texture slots before doing any SetData(),
        ///   preventing "resource is actively set on the GraphicsDevice" errors.
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_TacticalNuke
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI and optionally re-apply block skins in-place (no restart).
            /// </summary>
            /// <remarks>
            /// We best-effort repaint the nuke skin by resetting its internal "done" flag and calling Apply()
            /// inside a device-unsafe section that we make safe via WithNoBindings.
            /// Failures are swallowed so input stays resilient.
            /// </remarks>
            static void Postfix(InGameHUD __instance)
            {
                if (!TNHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // 1) Reload INI and apply runtime statics.
                    //    NOTE: This updates config-backed values (radius, damage, fuse, recipe text, etc.)
                    //    but does NOT re-register the Nuke item class if NukeId changes.
                    //    Rebinding IDs requires a restart (see step 7).
                    int oldId = NukeRegistry.NukeId;
                    TNConfig.LoadApply();

                    // 2) Update the vanilla explosive properties.
                    UpdateVanillaExplosives();

                    // 3) Add/update the Nuke crafting recipe (config-driven).
                    NukeRecipeInjector.ApplyOrUpdate();

                    // 4) Add/update the Nuke surrogates name (config-driven).
                    NukeSurrogateNameInjector.ApplyOrUpdate();

                    // 5) Add/update the Nuke surrogates description (config-driven).
                    NukeSurrogateDescriptionInjector.ApplyOrUpdate();

                    // 6) If block retexture is enabled, force re-apply the skin (even if it ran once),
                    //    but do it inside a WithNoBindings() guard to unbind RTs/textures during SetData().
                    if (NukeRegistry.DoBlockRetexture)
                    {
                        try
                        {
                            var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                            if (gd != null)
                            {
                                // Force NukeTextureSurrogateSkin.Apply() to run again.
                                var skinType = typeof(GamePatches).GetNestedType("NukeTextureSurrogateSkin", BindingFlags.NonPublic);
                                var doneFI = AccessTools.Field(skinType, "_done");
                                doneFI?.SetValue(null, false);

                                // === GPU-safe repaint: No RTs, no bound textures during SetData ===
                                WithNoBindings(gd, () => NukeTextureSurrogateSkin.Apply());
                            }
                        }
                        catch { /* Best-effort repaint. */ }
                    }

                    // 7) If the user changed NukeId, we cannot safely rebind the registered InventoryItemClass
                    //    or repaint the icon cell at runtime because FinishInitialization + our one-time
                    //    registration path won't run again. Warn and revert so the current session stays stable.
                    if (NukeRegistry.NukeId != oldId)
                    {
                        SendFeedback("[Nuke] NukeId changed. This requires a game restart to re-register the item + repaint icon.");
                        NukeRegistry.NukeId = oldId;
                    }

                    // Optional click feedback (uses engine cue name "Click" if present).
                    try { DNA.Audio.SoundManager.Instance?.PlayInstance("Click"); } catch { }

                    SendFeedback($"[Nuke] Config hot-reloaded from \"{PathShortener.ShortenForLog(TNConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[Nuke] Hot-reload failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Path Helper (Logs)

        /// <summary>
        /// Shortens absolute paths for logs (prefers trimming to \!Mods\... if present).
        /// </summary>
        internal static class PathShortener
        {
            public static string ShortenForLog(string fullPath)
            {
                if (string.IsNullOrEmpty(fullPath))
                    return string.Empty;

                // Normalize slashes.
                var p = fullPath.Replace('/', '\\');

                // Prefer showing from "\!Mods\..."
                int idx = p.IndexOf(@"\!Mods\", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return p.Substring(idx);

                // Fallback: Full path.
                return p;
            }
        }
        #endregion

        #endregion

        #region ModContent: Disk-First, Embedded-Resource Fallback

        internal static class ModContent
        {
            // Physical root:  ...\CastleMiner Z\!Mods\TacticalNuke
            public static readonly string DiskRoot =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "TacticalNuke");

            /// <summary>
            /// Opens a stream for a content file using disk-first strategy, then embedded resource fallback.
            /// Pass a relative path like @"Textures\Blocks\Nuke_top.png" or @"Sounds\Nuke_Fuse.wav".
            /// Returns null if neither exists.
            /// </summary>
            /// <remarks>
            /// Caller owns the returned <see cref="Stream"/> and must dispose it.
            /// Disk path assembled via <see cref="Path.Combine"/>; embedded lookup scans manifest names
            /// and picks the first that ends with the dotted suffix (case-insensitive).
            /// </remarks>
            public static Stream OpenStream(string relativePath)
            {
                try
                {
                    // 1) Disk: !Mods\TacticalNuke\{relativePath}
                    //    Note: We assume relativePath is actually relative. If you ever accept user input,
                    //    consider rejecting rooted paths or ".." segments to avoid traversal outside DiskRoot.
                    var abs = Path.Combine(DiskRoot, relativePath);
                    if (File.Exists(abs))
                        return File.OpenRead(abs);

                    // 2) Embedded: find a manifest resource whose name ends with the dotted suffix
                    //    Example: "Textures\Blocks\Nuke_top.png" -> ".Textures.Blocks.Nuke_top.png"
                    string suffix = "." + relativePath
                        .Replace('\\', '/')
                        .TrimStart('/')
                        .Replace('/', '.');

                    var asm = Assembly.GetExecutingAssembly();

                    // NOTE: Linear scan is fine at this scale; cache if this shows up in a profile.
                    var name = asm.GetManifestResourceNames()
                                  .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                    if (name != null)
                        return asm.GetManifestResourceStream(name);

                    Log($"[Nuke] Missing content: {relativePath} (disk + embedded).");
                    return null;
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] OpenStream({relativePath}) failed: {ex.Message}.");
                    return null;
                }
            }

            /// <summary>
            /// Loads a <see cref="Texture2D"/> using disk-first, then embedded fallback.
            /// </summary>
            /// <remarks>
            /// Returns null on failure; caller owns and must dispose the returned texture.
            /// </remarks>
            public static Texture2D LoadTexture(GraphicsDevice gd, string relativePath)
            {
                try
                {
                    using (var s = OpenStream(relativePath))
                    {
                        if (s == null) return null;
                        return Texture2D.FromStream(gd, s);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] Texture load failed: {relativePath}: {ex.Message}.");
                    return null;
                }
            }

            /// <summary>
            /// Loads a <see cref="SoundEffect"/> using disk-first, then embedded fallback.
            /// </summary>
            /// <remarks>
            /// Expects PCM WAV data. Returns null on failure; caller owns the returned sound.
            /// </remarks>
            public static SoundEffect LoadSound(string relativePath)
            {
                try
                {
                    using (var s = OpenStream(relativePath))
                    {
                        if (s == null) return null;
                        return SoundEffect.FromStream(s);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] Sound load failed: {relativePath}: {ex.Message}.");
                    return null;
                }
            }
        }
        #endregion

        #region Nuke Fuse Control (Cancel Old Fuse/Flash/Audio)

        /// <summary>
        /// Small helper to clear any pending nuke fuse/flash/audio at a voxel and
        /// prune HUD bookkeeping so the block doesn't detonate later "by surprise".
        /// </summary>
        internal static class NukeFuseController
        {
            /// <summary>
            /// Cached HUD instance to reach private waiting lists without re-searching each time.
            /// </summary>
            private static InGameHUD _lastHud;

            /// <summary>
            /// Remember a HUD instance when one is available (e.g., from an OnPlayerInput patch).
            /// Safe to call repeatedly; ignores nulls.
            /// </summary>
            public static void RememberHud(InGameHUD hud) { if (hud != null) _lastHud = hud; }

            /// <summary>
            /// Cancels any pending fuse/flash/audio at <paramref name="pos"/> and prunes the HUD's
            /// internal waiting list so the old fuse will not trigger later.
            /// </summary>
            /// <param name="pos">World voxel position of the explosive.</param>
            public static void CancelPendingAt(IntVector3 pos)
            {
                try
                {
                    // Our bookkeeping (visual/audio systems owned by this mod).
                    NukeFlashBook.Clear(pos);
                    NukeFuseAudio3D.StopLoop(pos);

                    // Try to get a HUD reference: Prefer the cached one, or fall back to a common singleton pattern.
                    var hud = _lastHud;
                    if (hud == null)
                    {
                        var pi = AccessTools.Property(typeof(InGameHUD), "Instance");
                        if (pi != null) hud = pi.GetValue(null, null) as InGameHUD;
                    }

                    // Remove any Explosive at 'pos' from the HUD's pending list so it won't auto-detonate later.
                    if (hud != null)
                    {
                        var listFI = AccessTools.Field(typeof(InGameHUD), "_tntWaitingToExplode"); // List<Explosive>.
                        if (listFI?.GetValue(hud) is List<Explosive> list)
                        {
                            for (int i = list.Count - 1; i >= 0; i--)
                                if (list[i].Position == pos)
                                    list.RemoveAt(i); // Cancel the old fuse entirely.
                        }
                    }

                    // Best-effort: Remove any lingering flash entity in the active game screen.
                    var gs = CastleMinerZGame.Instance?.GameScreen as GameScreen;
                    AccessTools.Method(typeof(GameScreen), "RemoveExplosiveFlashModel", new[] { typeof(IntVector3) })
                               ?.Invoke(gs, new object[] { pos });
                }
                catch
                {
                    // Best-effort: Never throw from a per-frame input/patch path.
                }
            }
        }
        #endregion

        #region Custom Fuse Time (Flash/Detonation) For Nuke Blocks

        /// <summary>
        /// Tiny timestamp registry keyed by block position for nuke fuses.
        /// We only store when the flash started so later patches can compute
        /// "how long has it been flashing?" and gate removal / blink speed.
        /// </summary>
        /// <remarks>
        /// • Key = world voxel position (IntVector3)
        /// • Value = Environment.TickCount (ms) at flash start
        /// • Resilient to tick wrap (age clamped ≥ 0)
        /// </remarks>
        internal static class NukeFlashBook
        {
            private static readonly Dictionary<IntVector3, int> _startMs = new Dictionary<IntVector3, int>();

            /// <summary>Record/overwrite the start time for a position.</summary>
            public static void Start(IntVector3 pos) => _startMs[pos] = Environment.TickCount;

            /// <summary>Check if a position has an active nuke.</summary>
            public static bool Has(IntVector3 pos)   => _startMs.ContainsKey(pos);

            /// <summary>Forget the timestamp for a position (e.g., after detonation).</summary>
            public static void Clear(IntVector3 pos) => _startMs.Remove(pos);

            /// <summary>
            /// Returns how many seconds have elapsed since <see cref="Start(IntVector3)"/> for <paramref name="pos"/>.
            /// </summary>
            public static bool TryGetAgeSeconds(IntVector3 pos, out float ageSec)
            {
                if (_startMs.TryGetValue(pos, out var start))
                {
                    ageSec = (Environment.TickCount - start) / 1000f;
                    if (ageSec < 0) ageSec = 0; // handle tick wrap
                    return true;
                }
                ageSec = 0;
                return false;
            }
        }

        // -----------------------------------------------------------------------------
        // 1) When the fuse is armed, extend the timer for nukes only.
        //    Target: InGameHUD.SetFuseForExplosive - vanilla creates an Explosive and
        //    enqueues it into a private list. We reach in, find the matching entry,
        //    and bump its Timer.MaxTime to our configurable nuke fuse length.
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Extends the fuse timer for nuke placements (keeps TNT/C4 vanilla).
        /// Also starts the flash stopwatch for this voxel so UI patches can reference it.
        /// </summary>
        /// <notes>
        /// • We only act if (type == C4) AND this voxel is one of our nukes (NukeMap.Has).
        /// • Accesses private _tntWaitingToExplode list (List<Explosive>).
        /// • Safe no-op if list/entity not found.
        /// </notes>
        [HarmonyPatch(typeof(InGameHUD), nameof(InGameHUD.SetFuseForExplosive))]
        static class Patch_SetFuseForExplosive_NukeFuse
        {
            static void Postfix(InGameHUD __instance, IntVector3 location, ExplosiveTypes explosiveType)
            {
                // Remember a HUD so CancelPendingAt can reach the list.
                NukeFuseController.RememberHud(__instance);

                // Only touch entries that are our TextureSurrogate-nukes (or however you mark nukes)
                if (explosiveType != ExplosiveTypes.C4) return;
                if (!NukeMap.Has(location)) return;

                // Grab the newly-added Explosive with matching position+type
                var listFI = AccessTools.Field(typeof(InGameHUD), "_tntWaitingToExplode"); // private List<Explosive>
                var list = (List<Explosive>)listFI.GetValue(__instance);
                var e = list.LastOrDefault(x => x.Position == location && x.ExplosiveType == explosiveType);
                if (e == null) return;

                // Extend its timer to NukeFuseSeconds
                e.Timer.MaxTime = TimeSpan.FromSeconds(NukeFuseConfig.NukeFuseSeconds);   // default is 4.0s in engine

                // We'll also need flash to last that long; remember when this flash starts
                NukeFlashBook.Start(location);
            }
        }

        // -----------------------------------------------------------------------------
        // 2) When the flash entity is spawned, remember its start so we can hold it.
        //    Target: GameScreen.AddExplosiveFlashModel - called on fuse arm.
        //    We just mark the timestamp for the voxel to coordinate with other patches.
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Records a start time when the engine spawns the flash entity for a nuke.
        /// </summary>
        /// <notes>
        /// • Cheap: just writes to <see cref="NukeFlashBook"/> if position is a nuke.
        /// • Non-nukes remain untouched (vanilla lifetimes).
        /// </notes>
        [HarmonyPatch(typeof(GameScreen), nameof(GameScreen.AddExplosiveFlashModel))]
        static class Patch_AddExplosiveFlashModel_MarkStart
        {
            static void Postfix(IntVector3 position)
            {
                if (NukeMap.Has(position))
                {
                    // Start the stopwatch for the extended-lifetime gate.
                    NukeFlashBook.Start(position);
                }
            }
        }

        // -----------------------------------------------------------------------------
        // 3) Prevent early removal of the flash for nukes until the long fuse elapses.
        //    Target: GameScreen.RemoveExplosiveFlashModel - vanilla may pull the flash
        //    ~8s in. We intercept and keep it around until our NukeFuseSeconds is up.
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Gates flash removal for nuke voxels so the visual stays alive for the longer fuse.
        /// </summary>
        /// <returns>
        /// false = block removal (keep flashing/smoking); true = allow vanilla removal.
        /// </returns>
        /// <notes>
        /// • Once our elapsed ≥ NukeFuseSeconds we clear the timestamp and allow removal.
        /// • Non-nukes: fast-path true (no behavioral change).
        /// </notes>
        [HarmonyPatch(typeof(GameScreen), nameof(GameScreen.RemoveExplosiveFlashModel))]
        static class Patch_RemoveExplosiveFlashModel_GateForNukes
        {
            static bool Prefix(IntVector3 position)
            {
                if (NukeMap.Has(position) && NukeFlashBook.TryGetAgeSeconds(position, out var age))
                {
                    if (age < NukeFuseConfig.NukeFuseSeconds)
                        return false;  // skip removal (keep flashing + smoking)

                    // fuse complete - allow removal and forget
                    NukeFlashBook.Clear(position);
                }
                return true; // vanilla removal
            }
        }

        // -----------------------------------------------------------------------------
        // 4) (Optional polish) Adjust blink rate as detonation approaches.
        //    Target: ExplosiveFlashEntity.Update - vanilla ramps blink after ~3s.
        //    We drive the cadence by "time remaining" relative to NukeFuseSeconds.
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Dynamically adjusts the blink cadence of the flash entity for nukes
        /// (slow -> medium -> fast) based on remaining fuse time.
        /// </summary>
        /// <notes>
        /// • Non-nukes are untouched.
        /// • Uses NukeFuseConfig.{SlowBlink,MidBlink,FastBlink} and their thresholds.
        /// • Safe reflection: best-effort try/catch.
        /// </notes>
        [HarmonyPatch(typeof(ExplosiveFlashEntity), "Update")]
        static class Patch_ExplosiveFlashEntity_Update_BlinkCadence
        {
            static readonly FieldInfo TimerFI = AccessTools.Field(typeof(ExplosiveFlashEntity), "_timer");
            static readonly FieldInfo PosFI = AccessTools.Field(typeof(ExplosiveFlashEntity), "BlockPosition");

            static void Postfix(object __instance)
            {
                try
                {
                    var pos = (IntVector3)PosFI.GetValue(__instance);
                    if (!NukeMap.Has(pos)) return;

                    if (!NukeFlashBook.TryGetAgeSeconds(pos, out var age)) return;

                    float left = Math.Max(0, NukeFuseConfig.NukeFuseSeconds - age);
                    var timer = (OneShotTimer)TimerFI.GetValue(__instance);

                    // Slow -> Mid -> Fast as time runs out
                    if (left <= NukeFuseConfig.FastBlinkLastSeconds) timer.MaxTime = TimeSpan.FromSeconds(NukeFuseConfig.FastBlink);
                    else if (left <= NukeFuseConfig.MidBlinkLastSeconds) timer.MaxTime = TimeSpan.FromSeconds(NukeFuseConfig.MidBlink);
                    else timer.MaxTime = TimeSpan.FromSeconds(NukeFuseConfig.SlowBlink);
                }
                catch { /* best effort */ }
            }
        }

        // -----------------------------------------------------------------------------
        // 5) Clean up our flash/timestamp once a nuke detonates.
        //    Target: Explosive.HandleDetonateExplosiveMessage - the authoritative event.
        //    We drop the entry from the book (and NukeMap if you also track there).
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Placeholder to avoid accidental duplicate Harmony annotations across files.
        /// </summary>
        [HarmonyPatch(typeof(Explosive), nameof(Explosive.HandleDetonateRocketMessage))] // not this one; we patch the TNT/C4 handler below
        static class Noop { } // placeholder to keep Harmony happy if you combine files

        /// <summary>
        /// Clears per-voxel flash bookkeeping on detonation so visual cleanup can proceed.
        /// </summary>
        /// <notes>
        /// • Always clears the timestamp for msg.Location.
        /// • Also removes from NukeMap if you use it to track active nuke voxels.
        /// </notes>
        [HarmonyPatch(typeof(Explosive), "HandleDetonateExplosiveMessage")]
        static class Patch_ClearFlashGateOnDet
        {
            static void Postfix(DNA.CastleMinerZ.Net.DetonateExplosiveMessage msg)
            {
                NukeFlashBook.Clear(msg.Location);
                NukeMap.Remove(msg.Location);
            }
        }
        #endregion

        #region Audio: 3D Nuke Fuse Loops (Extend The Fuse Sounds For Nukes) + Announcement Hook

        /// <summary>
        /// Spatial, per-voxel loop manager for a custom "nuke fuse" sound.
        /// Design:
        /// • Loads one <see cref="SoundEffect"/> from !Mods\TacticalNuke\Sounds\Nuke_Fuse.wav (lazy).
        /// • Keeps a looping <see cref="SoundEffectInstance"/> + <see cref="AudioEmitter"/> per fuse position
        ///   (keyed by <see cref="IntVector3"/> exact block coords).
        /// • Applies 3D via the engine's SoundManager listener if available; otherwise synthesizes a
        ///   listener at the local player's eye and updates spatialization every frame.
        /// Lifecycle:
        /// • Call <see cref="StartLoop"/> when a nuke fuse is armed (we do this automatically in the
        ///   ExplosiveFlashEntity ctor patch when the voxel is in NukeMap).
        /// • <see cref="UpdateAll3D"/> should be called each frame (the ExplosiveFlashEntity.Update
        ///   postfix provided below does this).
        /// • Call <see cref="StopLoop"/> when that voxel detonates or is cancelled; <see cref="StopAll"/> on
        ///   world unload / content reset to avoid dangling instances.
        /// Behavior & notes:
        /// • Idempotent: starting/stopping the same position is safe and cheap.
        /// • Not streaming: loops are short; instances are disposed on stop.
        /// • Volume is fixed in code (0.80f). Mix under your master volume elsewhere if needed.
        /// • If the WAV file is missing or fails to load, we log once and gracefully no-op (vanilla SFX will
        ///   be muted by our ctor patch-see below-so consider shipping the file).
        /// • Threading: dictionary access is expected on the main thread; no locks provided.
        /// • We explicitly stop the vanilla ExplosiveFlashEntity "Fuse" cue for nukes to avoid double-audio.
        /// File expectations:
        /// • Recommended WAV: 16-bit PCM, 44.1 kHz, mono (stereo also works; XACT will fold to 3D).
        /// </summary>
        internal static class NukeFuseAudio3D
        {
            private sealed class Loop
            {
                public SoundEffectInstance Inst;
                public AudioEmitter Emitter;
            }

            private static readonly Dictionary<IntVector3, Loop> _loops = new Dictionary<IntVector3, Loop>();
            private static SoundEffect _sfx;
            private static bool _triedLoad;

            // Load your custom fuse (once). Path: !Mods\TacticalNuke\Sounds\Nuke_Fuse.wav
            private static bool EnsureLoaded()
            {
                if (_sfx != null) return true;
                if (_triedLoad) return false;
                _triedLoad = true;

                _sfx = ModContent.LoadSound(@"Sounds\Nuke_Fuse.wav");
                if (_sfx == null)
                    Log("[Nuke] Fuse SFX not found on disk or embedded.");

                return _sfx != null;
            }

            public static void StartLoop(IntVector3 pos)
            {
                if (_loops.ContainsKey(pos)) return;
                if (!EnsureLoaded()) return;

                // Anchor emitter at block center (mirrors vanilla flash entity)
                var emitter = new AudioEmitter { Position = new Vector3(pos.X + 0.5f, pos.Y - 0.002f, pos.Z + 0.5f) };

                var inst = _sfx.CreateInstance();
                inst.IsLooped = true;
                inst.Volume = NukeFuseConfig.FuseVolume;

                // Initial 3D set
                var listener = GetListener();
                if (listener != null) inst.Apply3D(listener, emitter);

                try { inst.Play(); } catch { /* ignore audio device hiccups */ }

                _loops[pos] = new Loop { Inst = inst, Emitter = emitter };
            }

            public static void StopLoop(IntVector3 pos)
            {
                if (_loops.TryGetValue(pos, out var loop))
                {
                    try { loop.Inst.Stop(); loop.Inst.Dispose(); } catch { }
                    _loops.Remove(pos);
                }
            }

            public static void StopAll()
            {
                foreach (var kv in _loops)
                    try { kv.Value.Inst.Stop(); kv.Value.Inst.Dispose(); } catch { }
                _loops.Clear();
            }

            // Call each frame to refresh distance/panning vs the current listener
            public static void UpdateAll3D()
            {
                var listener = GetListener();
                if (listener == null) return;

                foreach (var kv in _loops)
                {
                    var loop = kv.Value;
                    // Emitter is static for a block; listener moves
                    try { loop.Inst.Apply3D(listener, loop.Emitter); } catch { }
                }
            }

            // Try to borrow the engine's listener; fall back to player eye
            private static AudioListener GetListener()
            {
                try
                {
                    var sm = DNA.Audio.SoundManager.Instance;
                    // Try common field/property names
                    var t = sm.GetType();
                    var fi = AccessTools.Field(t, "_listener") ?? AccessTools.Field(t, "Listener");
                    if (fi != null && fi.GetValue(sm) is AudioListener l0) return l0;

                    var pi = AccessTools.Property(t, "Listener");
                    if (pi != null && pi.GetValue(sm, null) is AudioListener l1) return l1;
                }
                catch { /* fall through */ }

                try
                {
                    var listener = new AudioListener();
                    var lp = CastleMinerZGame.Instance?.LocalPlayer;
                    var eye = lp?.FPSCamera?.WorldPosition ?? Vector3.Zero;
                    listener.Position = eye;
                    listener.Forward = Vector3.Forward;
                    listener.Up = Vector3.Up;
                    return listener;
                }
                catch { return null; }
            }
        }

        #region Audio Hooks: Start/Update 3D Fuse + Announcement Hook

        /// <summary>
        /// Starts our spatial loop when the flash entity is spawned for a voxel that is a mapped Nuke.
        /// Also silences the vanilla fuse cue to prevent phasing/doubling.
        /// Preconditions:
        /// • The block position must be present in NukeMap.
        /// • If the sound asset fails to load, this becomes a no-op (logs once).
        /// </summary>
        [HarmonyPatch(typeof(ExplosiveFlashEntity), MethodType.Constructor, new[] { typeof(IntVector3) })]
        static class Patch_Flash_ctor_StartNukeFuse
        {
            static void Postfix(ExplosiveFlashEntity __instance, IntVector3 position)
            {
                // Only if this voxel is one of *our* nukes
                if (!NukeMap.Has(position)) return;

                // Stop vanilla "Fuse" so you don't get two sounds at once
                try
                {
                    var cueFI = AccessTools.Field(typeof(ExplosiveFlashEntity), "_fuseCue");
                    (cueFI?.GetValue(__instance) as DNA.Audio.SoundCue3D)?.Stop(AudioStopOptions.Immediate);
                }
                catch { }

                // Start our custom spatial loop tied to this block
                NukeFuseAudio3D.StartLoop(position);

                // Hook the arm announcement.
                AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Armed, position);
            }
        }

        /// <summary>
        /// Per-frame spatialization refresh for all active fuse loops.
        /// We re-apply 3D using the current listener (engine SoundManager's listener if available,
        /// otherwise a synthetic listener at the player camera).
        /// Notes:
        /// • Cheap: Just Apply3D calls; instances remain looped and anchored at block centers.
        /// • Safe to call every frame; does nothing if there are no active loops.
        /// </summary>
        [HarmonyPatch(typeof(ExplosiveFlashEntity), nameof(ExplosiveFlashEntity.Update))]
        static class Patch_Flash_update_Update3D
        {
            static void Postfix() => NukeFuseAudio3D.UpdateAll3D();
        }
        #endregion

        #endregion

        #region World-Set Cleanup And Fuse-Expiry Detonation

        /// <summary>
        /// When any block is set, if it is not our surrogate, clear the NukeMap entry for that cell.
        /// Keeps the map in sync with world changes.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), "SetBlock", new[] { typeof(IntVector3), typeof(BlockTypeEnum) })]
        static class Patch_SetBlock_CleanupNukeMap
        {
            static void Postfix(IntVector3 worldIndex, BlockTypeEnum type)
            {
                if (type != NukeRegistry.TextureSurrogate)
                {
                    // Only announce "Disarmed" when a live fuse existed AND we are NOT inside a detonation SetBlock.
                    if (!AnnouncementConfig.AnnounceGuard.SuppressDisarmedOnce && NukeFlashBook.Has(worldIndex))
                    {
                        AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Disarmed, worldIndex);
                    }

                    NukeMap.Remove(worldIndex);
                    NukeFuseController.CancelPendingAt(worldIndex);
                    NukeFuseAudio3D.StopLoop(worldIndex);
                }
            }
        }

        /// <summary>
        /// Allows our surrogate (TextureSurrogate) to detonate when its timer expires (mirrors vanilla Explosive.Update).
        /// Vanilla only detonates TNT/C4; we add a branch for the surrogate if it's one of ours.
        /// </summary>
        [HarmonyPatch(typeof(Explosive), "Update", new[] { typeof(TimeSpan) })]
        static class Patch_Explosive_Update_AllowTextureSurrogateDetonate
        {
            static void Postfix(Explosive __instance)
            {
                try
                {
                    var bt = BlockTerrain.Instance;
                    bool isNuke =
                        bt != null &&
                        bt.GetBlockWithChanges(__instance.Position) == NukeRegistry.TextureSurrogate &&
                        NukeMap.Has(__instance.Position);

                    // Only do countdown / nuke detonation logic for nukes.
                    if (!isNuke)
                        return;

                    if (__instance.Timer.MaxTime.Seconds - __instance.Timer.ElaspedTime.Seconds == 10) AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Countdown10, __instance.Position);
                    if (__instance.Timer.MaxTime.Seconds - __instance.Timer.ElaspedTime.Seconds == 5)  AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Countdown5,  __instance.Position);
                    if (__instance.Timer.MaxTime.Seconds - __instance.Timer.ElaspedTime.Seconds == 1)  AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Countdown1,  __instance.Position);

                    // Mirror vanilla gate but accept our TextureSurrogate (if it's one of ours).
                    if (!__instance.Timer.Expired) return;

                    var blockType = bt.GetBlockWithChanges(__instance.Position);
                    if (blockType == NukeRegistry.TextureSurrogate && NukeMap.Has(__instance.Position))
                    {
                        // Detonate "as C4/TNT" (whichever the fuse was armed with; you arm it as C4).
                        DetonateExplosiveMessage.Send(
                            CastleMinerZGame.Instance.MyNetworkGamer,
                            __instance.Position,
                            true,
                            __instance.ExplosiveType);

                        AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Detonated, __instance.Position);
                    }
                }
                catch { /* Keep ticking even if reflection/env differs. */ }
            }
        }

        #endregion

        #region Input: Activate/Detonate Nukes By Left-Right-Click

        /// <summary>
        /// Intercepts HUD input: right-click detonates our Nuke voxels (TextureSurrogate in NukeMap) like C4.
        /// Left-click is explicitly passed to vanilla, so LMB won't arm nukes.
        /// </summary>
        [HarmonyPatch]
        static class Patch_ActivateTextureSurrogateLikeC4
        {
            // protected override bool OnPlayerInput(InputManager, GameController, KeyboardInput, GameTime)
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            static bool Prefix(InGameHUD __instance,
                               InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                var mouse  = inputManager?.Mouse;
                bool left  = mouse?.LeftButtonPressed == true;
                bool right = mouse?.RightButtonPressed == true;

                // Nothing to do this frame.
                if (!left && !right) return true;

                // Probe + aimed block.
                var probeObj = AccessTools.Field(typeof(InGameHUD), "ConstructionProbe")?.GetValue(__instance);
                if (probeObj == null) return true;

                var aimedIdx = (IntVector3)(AccessTools.Field(probeObj.GetType(), "_worldIndex")?.GetValue(probeObj) ?? default(IntVector3));
                var getBlock = AccessTools.Method(typeof(InGameHUD), "GetBlock", new[] { typeof(IntVector3) });
                if (getBlock == null) return true;

                var aimedType = (BlockTypeEnum)getBlock.Invoke(null, new object[] { aimedIdx });

                // Let vanilla handle real TNT/C4 as usual.
                if (aimedType == BlockTypeEnum.TNT || aimedType == BlockTypeEnum.C4)
                    return true;

                // Obtain the active inventory item class.
                var gm        = CastleMinerZGame.Instance;
                var lp        = gm?.LocalPlayer;
                var item      = lp?.PlayerInventory?.ActiveInventoryItem;
                var itemClass = item?.ItemClass;

                // If LEFT click: Only detonate if we are NOT about to place a block.
                if (left)
                {
                    if (itemClass is SpadeInventoryClass)   return true;
                    if (itemClass is GunInventoryItemClass) return true;
                    if (itemClass is BlockInventoryItemClass)
                    {
                        // If the HUD says we can place, let vanilla do it (don't intercept to detonate).
                        if (InGameHUD.Instance.ConstructionProbe.AbleToBuild)
                        {
                            // Whether it's the same cell or an adjacent place index, we assume placement is intended.
                            return true;
                        }
                    }
                }

                // If RIGHT click: When shouldering, let vanilla do it (don't intercept to detonate).
                if (right && lp.Shouldering) return true;

                // LMB or RMB on OUR nuke -> arm the fuse like C4.
                if ((left || right) && aimedType == NukeRegistry.TextureSurrogate && NukeMap.Has(aimedIdx))
                {
                    __instance.SetFuseForExplosive(aimedIdx, ExplosiveTypes.C4); // Vanilla fuse path.
                    __result = true; // Handled input.
                    return false;    // Skip original to avoid double-processing.
                }

                return true;
            }
        }
        #endregion

        #region Mining/Pickup: Make Surrogate Act Like C4 (Mineable) When Dug With Spade

        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.ProcessInput))]
        internal static class Patch_ProcessInput_SpadeTreatsBedrockAsC4_AndInstantMine
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                var list = new List<CodeInstruction>(instructions);

                var miTimeToDig = AccessTools.Method(typeof(InventoryItem), nameof(InventoryItem.TimeToDig), new[] { typeof(BlockTypeEnum) });
                var fiTimeSpanZero = AccessTools.Field(typeof(TimeSpan), nameof(TimeSpan.Zero));

                var tSpade = typeof(SpadeInventoryItem);

                // Locals to juggle the call args safely
                var locArg = il.DeclareLocal(typeof(BlockTypeEnum));   // the arg to TimeToDig
                var locThis = il.DeclareLocal(typeof(InventoryItem));  // the tool ("this")

                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];

                    // We only rewrite at the TimeToDig call site
                    if (!ins.Calls(miTimeToDig))
                    {
                        yield return ins;
                        continue;
                    }

                    // Stack before callvirt: [ this, arg ]
                    // We'll store them, optionally rewrite arg if (this is Spade && arg == Bedrock),
                    // then also allow InstantMine to short-circuit, else call the original.

                    var lblDoCall   = il.DefineLabel();
                    var lblAfterSub = il.DefineLabel();
                    var lblCallReal = il.DefineLabel();
                    var lblEnd      = il.DefineLabel();

                    // Save arg + this to locals (note eval stack order!)
                    yield return new CodeInstruction(OpCodes.Stloc, locArg);   // pop arg
                    yield return new CodeInstruction(OpCodes.Stloc, locThis);  // pop this

                    // if (!(this is SpadeInventoryItem)) goto DoCall;
                    yield return new CodeInstruction(OpCodes.Ldloc, locThis);
                    yield return new CodeInstruction(OpCodes.Isinst, tSpade);
                    yield return new CodeInstruction(OpCodes.Brfalse_S, lblDoCall);

                    // if (arg == Bedrock) arg = C4;
                    yield return new CodeInstruction(OpCodes.Ldloc, locArg);
                    yield return new CodeInstruction(OpCodes.Ldc_I4, (int)NukeRegistry.TextureSurrogate);
                    yield return new CodeInstruction(OpCodes.Bne_Un_S, lblAfterSub);
                    yield return new CodeInstruction(OpCodes.Ldc_I4, (int)BlockTypeEnum.C4);
                    yield return new CodeInstruction(OpCodes.Stloc, locArg);

                    // after-substitution label
                    var after = new CodeInstruction(OpCodes.Nop);
                    after.labels.Add(lblAfterSub);
                    yield return after;

                    // DoCall:
                    var doCall = new CodeInstruction(OpCodes.Nop);
                    doCall.labels.Add(lblDoCall);
                    yield return doCall;

                    // Real call: TimeToDig(modifiedArg)
                    var callReal = new CodeInstruction(OpCodes.Ldloc, locThis);
                    callReal.labels.Add(lblCallReal);
                    yield return callReal;
                    yield return new CodeInstruction(OpCodes.Ldloc, locArg);
                    yield return new CodeInstruction(OpCodes.Callvirt, miTimeToDig);

                    var end = new CodeInstruction(OpCodes.Nop);
                    end.labels.Add(lblEnd);
                    yield return end;
                }
            }
        }
        #endregion

        #region Synthetic Pickup Network Filters (Local-Only Drops / Consumes) - Nuke Surrogate

        /// <summary>
        /// Multiplayer crash guard for vanilla clients:
        /// - If <see cref="NukeRegistry.NukeId"/> is "vanilla-network-unsafe" (either NOT a defined <see cref="InventoryItemIDs"/> value
        ///   OR one of the enum slots that exist but are unimplemented in vanilla, e.g. <see cref="InventoryItemIDs.SpaceKnife"/>),
        ///   then vanilla clients can crash when they receive pickup packets
        ///   (<see cref="CreatePickupMessage"/>, <see cref="RequestPickupMessage"/>, <see cref="ConsumePickupMessage"/>)
        ///   during deserialize / item-class lookup.
        ///
        /// Fix:
        /// - Mirror the "Synthetic Pickup Network Filters" pattern used in TMI:
        ///   • CreatePickupMessage: For our synthetic Nuke item, spawn the pickup locally and skip sending.
        ///   • ConsumePickupMessage: On the host, resolve the consume locally and skip broadcasting.
        ///   • RequestPickupMessage: On non-host clients, grant/remove locally and skip requesting from host.
        ///
        /// Result:
        /// - Modded players can drop/pickup their Nuke item locally without exposing unsafe/unimplemented IDs
        ///   to unmodded players in the session.
        /// </summary>
        internal static class NukePickupNet
        {
            /// <summary>
            /// IDs that exist in the <see cref="InventoryItemIDs"/> enum but are unimplemented in vanilla.
            /// Sending these over the network can crash vanilla clients.
            /// </summary>
            private static readonly HashSet<InventoryItemIDs> VanillaUnimplementedIds = new HashSet<InventoryItemIDs>
            {
                InventoryItemIDs.SpaceKnife,
                InventoryItemIDs.Chainsaw2,
                InventoryItemIDs.Chainsaw3,
                InventoryItemIDs.SpawnCombat,
                InventoryItemIDs.SpawnExplorer,
                InventoryItemIDs.SpawnBuilder,
                InventoryItemIDs.AdvancedGrenadeLauncher,
                InventoryItemIDs.MegaPickAxe,
                InventoryItemIDs.Snowball,
                InventoryItemIDs.Iceball,
                InventoryItemIDs.MultiLaser,
                InventoryItemIDs.MonsterBlock
            };

            /// <summary>
            /// Returns true if this ID is unsafe to send to vanilla clients.
            /// </summary>
            public static bool IsVanillaNetworkUnsafeId(InventoryItemIDs id)
            {
                // Outside the enum entirely => always unsafe.
                if (!Enum.IsDefined(typeof(InventoryItemIDs), id))
                    return true;

                // Defined, but unimplemented slot => unsafe.
                return VanillaUnimplementedIds.Contains(id);
            }

            /// <summary>
            /// True if an InventoryItemID is OUR configured Nuke ID AND vanilla clients won't be able to
            /// resolve it safely. These IDs are kept off the wire.
            /// </summary>
            public static bool IsSyntheticNukeId(InventoryItemIDs id)
            {
                // Must match our configured NukeId exactly.
                if ((int)id != NukeRegistry.NukeId)
                    return false;

                return IsVanillaNetworkUnsafeId(id);
            }
        }

        #region CreatePickupMessage - Local-Only Nuke Drops

        /// <summary>
        /// Prevents CreatePickupMessage from broadcasting our synthetic Nuke item ID.
        /// Instead, we create the pickup entity locally on this machine only.
        /// </summary>
        [HarmonyPatch(typeof(CreatePickupMessage), nameof(CreatePickupMessage.Send))]
        internal static class Patch_CreatePickupMessage_LocalOnlyForSyntheticNuke
        {
            /// <summary>
            /// Prefix:
            /// - Vanilla IDs -> allow original Send (networked).
            /// - Synthetic Nuke ID -> local-only spawn, skip Send.
            /// </summary>
            static bool Prefix(
                LocalNetworkGamer from,
                Vector3 pos,
                Vector3 vec,
                int pickupID,
                InventoryItem item,
                bool dropped,
                bool displayOnPickup)
            {
                var cls = item?.ItemClass;
                if (cls == null)
                {
                    // Nothing we can do; let vanilla handle it.
                    return true;
                }

                var id = cls.ID;

                // Only intercept the custom/synthetic Nuke item ID.
                if (!NukePickupNet.IsSyntheticNukeId(id)) return true;

                // Synthetic Nuke item -> local-only spawn (no packets).
                TrySpawnLocalPickup(from, pos, vec, pickupID, item, dropped, displayOnPickup);

                // Skip original CreatePickupMessage.Send so nothing hits the network.
                return false;
            }

            /// <summary>
            /// Replicates PickupManager.HandleCreatePickupMessage but without going through the message system.
            /// </summary>
            private static void TrySpawnLocalPickup(
                LocalNetworkGamer from,
                Vector3 pos,
                Vector3 vec,
                int pickupID,
                InventoryItem item,
                bool dropped,
                bool displayOnPickup)
            {
                var pm = PickupManager.Instance;
                if (pm == null || item == null)
                    return;

                try
                {
                    // Vanilla uses msg.Sender.Id as the SpawnerID.
                    int spawnerID = (from != null) ? (int)from.Id : 0;

                    var entity = new PickupEntity(item, pickupID, spawnerID, dropped, pos)
                    {
                        // Match HandleCreatePickupMessage behaviour: DisplayOnPickup + velocity + +0.5f offset.
                        Item = { DisplayOnPickup = displayOnPickup }
                    };

                    entity.PlayerPhysics.LocalVelocity = vec;
                    entity.LocalPosition = pos + new Vector3(0.5f, 0.5f, 0.5f);

                    pm.Pickups.Add(entity);

                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        scene.Children.Add(entity);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] ERROR: Local-only pickup spawn failed: {ex}.");
                }
            }
        }
        #endregion

        #region ConsumePickupMessage - Host-Local Nuke Consumes

        /// <summary>
        /// Prevents the host from broadcasting ConsumePickupMessage containing our synthetic Nuke item ID.
        /// Host resolves the pickup locally (remove entity, optionally grant to local player), then skips Send.
        /// </summary>
        [HarmonyPatch(typeof(ConsumePickupMessage), nameof(ConsumePickupMessage.Send))]
        internal static class Patch_ConsumePickupMessage_Send_LocalOnlyForSyntheticNuke
        {
            /// <summary>
            /// Prefix:
            /// - Vanilla IDs -> allow original Send (networked).
            /// - Synthetic Nuke ID -> host-local consume, skip Send.
            /// </summary>
            static bool Prefix(
                LocalNetworkGamer from,
                byte pickerupper,
                Vector3 pos,
                int spawnerID,
                int pickupID,
                InventoryItem item,
                bool displayOnPickup)
            {
                // No item / no class -> nothing special to do.
                if (item == null || item.ItemClass == null)
                    return true;

                var id = item.ItemClass.ID;

                // Not our synthetic Nuke ID? Let vanilla message flow.
                if (!NukePickupNet.IsSyntheticNukeId(id))
                    return true;

                var pm = PickupManager.Instance;
                var game = CastleMinerZGame.Instance;
                if (pm == null || game == null)
                    return false; // Fail-safe: don't send anything.

                // -----------------------------------------------------------------
                // 1) Find which Player picked this up (from pickerupper id).
                // -----------------------------------------------------------------
                Player player = null;
                var session = game.CurrentNetworkSession;
                if (session != null)
                {
                    foreach (NetworkGamer nwg in session.AllGamers)
                    {
                        if (nwg != null && nwg.Id == pickerupper)
                        {
                            player = nwg.Tag as Player;
                            break;
                        }
                    }
                }

                // -----------------------------------------------------------------
                // 2) Remove the pickup entity from the world on this host.
                // -----------------------------------------------------------------
                for (int i = 0; i < pm.Pickups.Count; i++)
                {
                    var candidate = pm.Pickups[i];
                    if (candidate.PickupID == pickupID && candidate.SpawnerID == spawnerID)
                    {
                        pm.Pickups.RemoveAt(i);

                        // Some builds keep a pending list; remove if present (best-effort).
                        try { pm.PendingPickupList.Remove(candidate); } catch { }

                        var scene = pm.Scene;
                        if (scene != null && scene.Children != null)
                        {
                            scene.Children.Remove(candidate);
                        }
                        break;
                    }
                }

                // -----------------------------------------------------------------
                // 3) If this machine owns the picking player, grant the item.
                // -----------------------------------------------------------------
                if (player != null && player == game.LocalPlayer &&
                    game.GameScreen != null && game.GameScreen.HUD != null)
                {
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(item, displayOnPickup);
                    try { DNA.Audio.SoundManager.Instance?.PlayInstance("pickupitem"); } catch { }
                }

                // -----------------------------------------------------------------
                // 4) Optional: Local flying pickup effect (purely cosmetic).
                // -----------------------------------------------------------------
                if (player != null)
                {
                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        var fpe = new FlyingPickupEntity(item, player, pos);
                        scene.Children.Add(fpe);
                    }
                }

                // Returning false skips the original Send => no packet with invalid ID hits vanilla clients.
                return false;
            }
        }
        #endregion

        #region RequestPickupMessage - Client-Local Nuke Consumes

        /// <summary>
        /// For non-host clients, our synthetic Nuke pickups are spawned locally only.
        /// This patch lets the client pick them up without notifying the host (who doesn't know they exist).
        /// </summary>
        [HarmonyPatch(typeof(RequestPickupMessage), nameof(RequestPickupMessage.Send))]
        internal static class Patch_RequestPickupMessage_Send_LocalOnlyForSyntheticNuke
        {
            /// <summary>
            /// Prefix:
            /// - Host -> let vanilla flow (host-side guarded by Consume patch).
            /// - Client:
            ///   • If pickup is vanilla -> send request as normal.
            ///   • If pickup is synthetic Nuke -> grant/remove locally and skip send.
            /// </summary>
            static bool Prefix(LocalNetworkGamer from, int spawnerID, int pickupID)
            {
                if (from == null)
                    return true;

                // Host should keep the normal request/consume flow; host-side is already guarded.
                if (from.IsHost)
                    return true;

                var pm = PickupManager.Instance;
                var game = CastleMinerZGame.Instance;
                if (pm == null || game == null)
                    return true;

                // Locate the pickup we just walked into.
                PickupEntity pickup = null;
                for (int i = 0; i < pm.Pickups.Count; i++)
                {
                    var cand = pm.Pickups[i];
                    if (cand.PickupID == pickupID && cand.SpawnerID == spawnerID)
                    {
                        pickup = cand;
                        break;
                    }
                }

                // If we can't find it, or it has no item, let vanilla try.
                if (pickup == null || pickup.Item == null || pickup.Item.ItemClass == null)
                    return true;

                var id = pickup.Item.ItemClass.ID;

                // Vanilla item? Use normal network pickup logic.
                if (!NukePickupNet.IsSyntheticNukeId(id))
                    return true;

                // -----------------------------------------------------------------
                // Synthetic pickup on a NON-HOST client:
                //   - Grant item locally.
                //   - Remove the pickup entity.
                //   - (Optionally) play the flying pickup effect.
                //   - Do NOT send a RequestPickupMessage to the host.
                // -----------------------------------------------------------------
                Player localPlayer = game.LocalPlayer;
                if (localPlayer != null && game.GameScreen != null && game.GameScreen.HUD != null)
                {
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(
                        pickup.Item,
                        pickup.Item.DisplayOnPickup
                    );

                    try { DNA.Audio.SoundManager.Instance?.PlayInstance("pickupitem"); } catch { }

                    // Cosmetic flying pickup effect for the local player.
                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        Vector3 pos = pickup.GetActualGraphicPos();
                        var fpe = new FlyingPickupEntity(pickup.Item, localPlayer, pos);
                        scene.Children.Add(fpe);
                    }
                }

                // Remove the pickup from the local world.
                pm.RemovePickup(pickup);

                // Skip original send so the host never sees this synthetic pickup.
                return false;
            }
        }
        #endregion

        #endregion

        #region Projectile-Triggered Detonation (Tracer + Blaster)

        /// <summary>
        /// Makes tracers trigger nukes they hit (reads probe collision, detonates our surrogate).
        /// </summary>
        [HarmonyPatch(typeof(TracerManager.Tracer), "Update")]
        static class Patch_Tracer_Update_DetonateNukesOnHit
        {
            static void Postfix()
            {
                try
                {
                    // TracerManager.Tracer has a private static TraceProbe: "tp"
                    var tpField = AccessTools.Field(typeof(TracerManager.Tracer), "tp");
                    var tp = tpField?.GetValue(null);
                    if (tp == null) return;

                    // Read collision + hit voxel index from the probe (private fields)
                    var collides = (bool)(AccessTools.Field(tp.GetType(), "_collides")?.GetValue(tp) ?? false);
                    if (!collides) return;

                    var hitIndex = (IntVector3)(AccessTools.Field(tp.GetType(), "_worldIndex")?.GetValue(tp) ?? default(IntVector3));

                    // Only trigger if this voxel is one of OUR nukes (TextureSurrogate skinned & tracked)
                    var bt = BlockTerrain.Instance;
                    if (bt == null) return;

                    var btType = bt.GetBlockWithChanges(hitIndex);
                    if (btType == NukeRegistry.TextureSurrogate && NukeMap.Has(hitIndex))
                    {
                        // Fire a normal C4 detonation message - your radius patch will upscope it to NUKE
                        DetonateExplosiveMessage.Send(
                            CastleMinerZGame.Instance.MyNetworkGamer,
                            hitIndex,
                            true,
                            ExplosiveTypes.C4);
                    }
                }
                catch
                {
                    // best-effort; never break gunfire
                }
            }
        }

        /// <summary>
        /// Makes blaster shots detonate nukes they collide with (local shooter originates the network message).
        /// </summary>
        [HarmonyPatch]
        static class Patch_Bullets_Detonate_Nukes
        {
            // private void HandleCollision(Vector3 normal, Vector3 location, bool bounce, bool destroyBlock, IntVector3 blockToDestroy)
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(BlasterShot), "HandleCollision",
                    new[] { typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(IntVector3) });

            static void Prefix(object __instance, Vector3 collisionNormal, Vector3 collisionLocation,
                               bool bounce, bool destroyBlock, IntVector3 blockToDestroy)
            {
                try
                {
                    var bt = BlockTerrain.Instance;
                    if (bt == null) return;

                    // Only react to our nuke voxels (TextureSurrogate that we placed & tracked)
                    if (bt.GetBlockWithChanges(blockToDestroy) != NukeRegistry.TextureSurrogate) return;
                    if (!NukeMap.Has(blockToDestroy)) return;

                    // Only the local shooter should originate the network detonation
                    var shooterField = AccessTools.Field(typeof(BlasterShot), "_shooter");
                    byte shooter = shooterField != null ? (byte)shooterField.GetValue(__instance) : (byte)0;
                    if (!CastleMinerZGame.Instance.IsLocalPlayerId(shooter)) return;

                    // Boom: use C4 type so your existing radius-boost & daisy-chain logic kicks in
                    DetonateExplosiveMessage.Send(
                        CastleMinerZGame.Instance.MyNetworkGamer,
                        blockToDestroy,
                        true,                 // OriginalExplosion so vanilla removes block & finds neighbors
                        ExplosiveTypes.C4);   // your patches expand this when NukeMap.Has(pos)
                }
                catch { /* best effort; never break bullets */ }
            }
        }
        #endregion

        #region HUD Arming & Placement Swap / Marking

        /// <summary>
        /// Postfix that mirrors "nuke selected + used" and sets a one-shot "arm next placement" intent.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_ArmNextPlacementIfNuke
        {
            // Resolve the protected override bool OnPlayerInput(InputManager, GameController, KeyboardInput, GameTime)
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            // Postfix signature must mirror the target args; __instance is optional but helpful
            static void Postfix(InGameHUD __instance,
                                InputManager inputManager,
                                GameController controller,
                                KeyboardInput chatPad,
                                GameTime gameTime)
            {
                // your existing logic that marks "next fuse is a nuke" when Nuke is selected
                NukeHelpers.TryArmNextPlacementAsNuke(__instance, inputManager);
            }
        }

        /// <summary>
        /// Prefix: If a C4 is about to be placed AND we armed the next placement as nuke, swap it to the surrogate
        /// and remember its position in NukeMap (visual-only). Protects from infinite recursion.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_SetBlock_SwapC4ToTextureSurrogate_ForNuke
        {
            // Some builds also have overloads - bind the exact (IntVector3, BlockTypeEnum) signature
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(BlockTerrain), "SetBlock",
                    new[] { typeof(IntVector3), typeof(BlockTypeEnum) });

            // Prefix lets you change the 'type' being placed for this call only
            static void Prefix(IntVector3 worldIndex, ref BlockTypeEnum type)
            {
                // Only when the HUD armed the next placement as a nuke AND the game is about to place C4...
                if (type == BlockTypeEnum.C4 && NukeHelpers.ConsumeIfArmed(worldIndex))
                {
                    // ...place TextureSurrogate instead (has its own tile indices), which you've re-skinned as the Nuke.
                    type = NukeRegistry.TextureSurrogate;
                    NukeMap.Add(worldIndex);
                }
            }
        }
        #endregion

        #region Arming Helper (One-Shot) Used By HUD Postfix

        /// <summary>
        /// One-shot arming flag consumed when the block is actually placed. Also clears itself when we mark.
        /// </summary>
        internal static class NukeHelpers
        {
            private static bool _armed;
            public static void Clear()   => _armed = false;

            // Track last seen item so we can disarm when leaving Nuke.
            private static int _lastItemId = -1;

            // Call this once per input tick.
            public static void TryArmNextPlacementAsNuke(InGameHUD hud, InputManager im)
            {
                // Observe slot changes and disarm if we left the Nuke slot.
                int curId = (int?)hud?.ActiveInventoryItem?.ItemClass?.ID ?? -1;
                if (curId != _lastItemId)
                {
                    if (_lastItemId == NukeRegistry.NukeId && curId != NukeRegistry.NukeId)
                        _armed = false; // Left Nuke -> kill any latent arm.
                    _lastItemId = curId;
                }

                // Only arm when actually trying to place a block:
                //   • Holding Nuke.
                //   • Left-click this frame (placement trigger).
                //   • HUD says we can build here (prevents arming during right-click detonations, UI clicks, etc.).
                if (curId == NukeRegistry.NukeId)
                {
                    bool left = im?.Mouse?.LeftButtonPressed ?? false;
                    bool canBuild = false;
                    try { canBuild = InGameHUD.Instance?.ConstructionProbe?.AbleToBuild ?? false; } catch { }

                    if (left && canBuild)
                        _armed = true;
                }
            }

            // Consumed in SetBlock prefix.
            public static bool ConsumeIfArmed(IntVector3 pos)
            {
                if (!_armed) return false;
                _armed = false;
                return true;
            }
        }
        #endregion

        #region Clear Tags On Detonate (Legacy Marker Used By Some Radius Patches)

        /// <summary>
        /// Detonation handler: currently re-tags the location; keep or convert to Clear depending on your usage.
        /// </summary>
        [HarmonyPatch(typeof(Explosive), nameof(Explosive.HandleDetonateExplosiveMessage))]
        static class Patch_ClearTagOnDetonate
        {
            static void Postfix(DetonateExplosiveMessage msg)
            {
                NukeDetonation.ClearTagFuse(msg.Location); // or Clear(msg.Location) if you named it that

                // NEW: stop the fuse loop at this voxel
                NukeFuseAudio3D.StopLoop(msg.Location);
            }
        }
        #endregion

        #region Texture Skin Application On Secondary Load

        /// <summary>
        /// Entry point for block texture skinning (if enabled). Runs once after content load.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        static class Patch_NukeSkin_OnSecondaryLoad
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if (!NukeRegistry.DoBlockRetexture) return;

                NukeTextureSurrogateSkin.Apply();
            }
        }

        /// <summary>
        /// Lightweight container for distinct TextureSurrogate face slots (top/bottom/side).
        /// </summary>
        private sealed class FaceSlots { public int Top, Bottom, Side; }

        /// <summary>
        /// Ensures TextureSurrogate uses unique atlas tile indices for top, bottom, and sides.
        /// If faces share or are invalid, allocates free indices from the tail of the atlas.
        /// Updates TextureSurrogate.TileIndices in-place.
        /// </summary>
        /// <remarks>Prevents "one image on all faces" when painting different textures per face.</remarks>
        private static FaceSlots EnsureUniqueTextureSurrogateFaceSlots(Texture2D nearAtlas, int tileSize)
        {
            var bt = BlockType.GetType(NukeRegistry.TextureSurrogate);
            var idx = bt?.TileIndices;
            if (idx == null || idx.Length < 6) { Log($"[Nuke] {NukeRegistry.TextureSurrogate} indices missing."); return null; }

            // Current face indices.
            int top = idx[(int)BlockFace.POSY];
            int bot = idx[(int)BlockFace.NEGY];
            int[] sideFaces = {
                idx[(int)BlockFace.POSX],
                idx[(int)BlockFace.NEGZ],
                idx[(int)BlockFace.NEGX],
                idx[(int)BlockFace.POSZ],
            };
            int side = sideFaces.FirstOrDefault(i => i >= 0);

            // Atlas layout.
            int cols  = Math.Max(1, nearAtlas.Width / tileSize);
            int rows  = Math.Max(1, nearAtlas.Height / tileSize);
            int total = cols * rows;

            // Build used set from ALL blocks.
            var used = new bool[total];
            foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                if (e == BlockTypeEnum.NumberOfBlocks) continue;
                var ti = BlockType.GetType(e)?.TileIndices;
                if (ti == null) continue;
                foreach (var i in ti)
                    if (i >= 0 && i < total) used[i] = true;
            }

            // allocator (grab from the tail to reduce chance of collisions with vanilla).
            int Alloc()
            {
                for (int i = total - 1; i >= 0; i--)
                    if (!used[i]) { used[i] = true; return i; }
                return -1;
            }

            bool needUniqueSide = (side < 0) || side == top || side == bot;
            bool needUniqueTop = (top < 0) || top == side || top == bot;
            bool needUniqueBot = (bot < 0) || bot == side || bot == top;

            // Make side distinct first (so top/bottom can reuse their original when possible).
            if (needUniqueSide)
            {
                int n = Alloc();
                if (n >= 0) side = n;
                else Log("[Nuke] No free atlas slot for Side; faces will share.");
            }
            if (needUniqueTop)
            {
                int n = Alloc();
                if (n >= 0) top = n;
                else Log("[Nuke] No free atlas slot for Top; faces will share.");
            }
            if (needUniqueBot)
            {
                int n = Alloc();
                if (n >= 0) bot = n;
                else Log("[Nuke] No free atlas slot for Bottom; faces will share.");
            }

            // Write back to TextureSurrogate.TileIndices so the renderer/chunk mesher uses distinct slots.
            idx[(int)BlockFace.POSY] = top;
            idx[(int)BlockFace.NEGY] = bot;
            idx[(int)BlockFace.POSX] = side;
            idx[(int)BlockFace.NEGZ] = side;
            idx[(int)BlockFace.NEGX] = side;
            idx[(int)BlockFace.POSZ] = side;

            Log($"[Nuke] TextureSurrogate face slots -> top:{top}, bottom:{bot}, side:{side} (atlas tiles={cols}x{rows}).");
            return new FaceSlots { Top = top, Bottom = bot, Side = side };
        }

        /// <summary>
        /// Paints the TextureSurrogate's top/bottom/side textures into distinct atlas slots (near+mip),
        /// after guaranteeing unique tile indices. Can be guarded by DoBlockRetexture.
        /// </summary>
        internal static class NukeTextureSurrogateSkin
        {
            static bool _done;

            /// <summary>True if item icon atlases are already initialized (used to time Apply in registration).</summary>
            public static bool IconsReady()
            {
                var small = AccessTools.Field(typeof(InventoryItem), "_2DImages")?.GetValue(null) as Texture2D;
                var large = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge")?.GetValue(null) as Texture2D;
                return small != null || large != null;
            }

            public static void Apply()
            {
                if (_done) return;

                var bt = BlockTerrain.Instance;
                if (bt == null) return;

                var near = GetTex(bt, "_diffuseAlpha") ?? FindTex(bt, "diffuse", "alpha");
                var mip = GetTex(bt, "_mipMapDiffuse") ?? FindTex(bt, "mipmap", "diffuse");
                if (near == null) { Log("[Nuke] No near terrain atlas."); return; }

                int tsNear = near.Width / 8;                 // e.g., 2048/8 = 256
                int tsMip = (mip != null) ? mip.Width / 8 : 0;

                // NEW: ensure TextureSurrogate uses distinct tiles for top/bottom/side
                var slots = EnsureUniqueTextureSurrogateFaceSlots(near, tsNear);
                if (slots == null) return;

                // New (disk-first, embedded fallback; pass RELATIVE paths):
                using (var top = ModContent.LoadTexture(near.GraphicsDevice, @"Textures\Blocks\Nuke_top.png"))
                using (var side = ModContent.LoadTexture(near.GraphicsDevice, @"Textures\Blocks\Nuke_side.png"))
                using (var bottom = ModContent.LoadTexture(near.GraphicsDevice, @"Textures\Blocks\Nuke_bottom.png"))
                {
                    if (top == null || side == null || bottom == null) { Log("[Nuke] Missing Nuke_* PNGs."); return; }

                    // Paint near atlas
                    WriteFaceAllMips(near, tsNear, slots.Top, top);
                    WriteFaceAllMips(near, tsNear, slots.Bottom, bottom);
                    WriteFaceAllMips(near, tsNear, slots.Side, side);

                    // Paint mip/distance atlas
                    if (mip != null && tsMip > 0)
                    {
                        WriteFaceAllMips(mip, tsMip, slots.Top, top);
                        WriteFaceAllMips(mip, tsMip, slots.Bottom, bottom);
                        WriteFaceAllMips(mip, tsMip, slots.Side, side);
                    }

                    Log($"[Nuke] TextureSurrogate skinned (Top={slots.Top}, Bottom={slots.Bottom}, Side={slots.Side}).");
                    _done = true;
                }
            }


            // ===== helpers (unchanged from your version, keep these) =====
            static Texture2D GetTex(object o, string field)
                => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o) as Texture2D;

            static Texture2D FindTex(object o, params string[] needles)
            {
                foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(Texture2D).IsAssignableFrom(f.FieldType)) continue;
                    var n = f.Name.ToLowerInvariant();
                    if (needles.All(n.Contains)) return f.GetValue(o) as Texture2D;
                }
                return null;
            }

            static Texture2D Load(GraphicsDevice gd, string path)
            {
                try { using (var s = File.OpenRead(path)) return Texture2D.FromStream(gd, s); }
                catch { return null; }
            }

            static void WriteFaceAllMips(Texture2D atlas, int tileSize, int tileIndex, Texture2D src)
            {
                if (atlas == null || tileIndex < 0 || src == null || src.IsDisposed) return;

                int cols = Math.Max(1, atlas.Width / tileSize);
                int tx = tileIndex % cols;
                int ty = tileIndex / cols;
                int levels = Math.Max(1, atlas.LevelCount);

                var srcFull = Read(src);

                for (int level = 0; level < levels; level++)
                {
                    int s = Math.Max(1, tileSize >> level);
                    int wL = Math.Max(1, atlas.Width >> level);
                    int hL = Math.Max(1, atlas.Height >> level);

                    var dest = new Rectangle(tx * s, ty * s, s, s);
                    dest.X = Clamp(dest.X, 0, Math.Max(0, wL - dest.Width));
                    dest.Y = Clamp(dest.Y, 0, Math.Max(0, hL - dest.Height));

                    var write = (src.Width == s && src.Height == s) ? srcFull : ScaleNearest(srcFull, src.Width, src.Height, s, s);

                    var orig = new Color[dest.Width * dest.Height];
                    atlas.GetData(level, dest, orig, 0, orig.Length);
                    for (int i = 0; i < write.Length; i++) write[i].A = orig[i].A;

                    atlas.SetData(level, dest, write, 0, write.Length);
                }
            }

            static int AllocateFreeTileIndex(Texture2D atlas, int tileSize)
            {
                int cols = Math.Max(1, atlas.Width / tileSize);
                int rows = Math.Max(1, atlas.Height / tileSize);
                int max = cols * rows;

                var used = new HashSet<int>();
                foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
                {
                    if (e == BlockTypeEnum.NumberOfBlocks) continue;
                    var ti = BlockType.GetType(e)?.TileIndices;
                    if (ti == null) continue;
                    foreach (var k in ti) if (k >= 0) used.Add(k);
                }
                for (int i = 0; i < max; i++) if (!used.Contains(i)) return i;
                return -1;
            }

            public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
            public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);
            public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

            static Color[] Read(Texture2D tex)
            {
                var buf = new Color[tex.Width * tex.Height];
                tex.GetData(buf);
                return buf;
            }

            static Color[] ScaleNearest(Color[] src, int sw, int sh, int dw, int dh)
            {
                var dst = new Color[dw * dh];
                for (int y = 0; y < dh; y++)
                {
                    int sy = (int)((y / (float)dh) * sh); if (sy >= sh) sy = sh - 1;
                    for (int x = 0; x < dw; x++)
                    {
                        int sx = (int)((x / (float)dw) * sw); if (sx >= sw) sx = sw - 1;
                        dst[y * dw + x] = src[sy * sw + sx];
                    }
                }
                return dst;
            }
        }
        #endregion

        #region Item Registration & Icon Painting (And Optional Skin Apply When Icons Exist)

        #region Safety Helpers: Device / Icon Painting

        #region Device Safety Helpers (Unbind RTs & Textures Safely)

        /// <summary>
        /// Pixel (PS) sampler slot count for current device.
        /// Reach: 4, HiDef: 16.
        /// </summary>
        private static int GetPixelSamplerSlots(GraphicsDevice gd)
        {
            // Defensive default: Reach->4
            if (gd == null) return 4;
            return gd.GraphicsProfile == GraphicsProfile.HiDef ? 16 : 4;
        }

        /// <summary>
        /// Vertex (VS) sampler slot count for current device.
        /// Reach: 0 (no vertex texture fetch), HiDef: 4.
        /// </summary>
        private static int GetVertexSamplerSlots(GraphicsDevice gd)
        {
            if (gd == null) return 0;
            return gd.GraphicsProfile == GraphicsProfile.HiDef ? 4 : 0;
        }

        /// <summary>
        /// Unbinds all textures from PS/VS slots within the valid range for the current profile.
        /// Safe on both XNA and MonoGame. Avoids using Textures.Count (doesn't exist in XNA).
        /// </summary>
        private static void UnbindAllTextures(GraphicsDevice gd)
        {
            if (gd == null) return;

            // Pixel shader textures.
            var ps = GetPixelSamplerSlots(gd);
            var px = gd.Textures; // TextureCollection.
            for (int i = 0; i < ps; i++)
            {
                try { px[i] = null; } catch { break; } // Break if a platform has fewer than expected.
            }

            // Vertex textures only on HiDef (Reach has 0).
            var vs = GetVertexSamplerSlots(gd);
            if (vs > 0)
            {
                var vx = gd.VertexTextures;
                for (int i = 0; i < vs; i++)
                {
                    try { vx[i] = null; } catch { break; }
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> with no render targets bound and all sampler slots unbound,
        /// preventing "SetData while bound" and "TextureCollection index out of range" issues.
        /// </summary>
        private static void WithNoBindings(GraphicsDevice gd, Action body)
        {
            if (gd == null) { body?.Invoke(); return; }

            // We're usually calling this from Update/input (not inside a draw),
            // so we don't bother restoring RTs; we just guarantee a clean device.
            try
            {
                gd.SetRenderTarget(null); // Unbind any RT.
            }
            catch { /* ignore */ }

            UnbindAllTextures(gd);        // Unbind all PS/VS sampler slots safely.

            body?.Invoke();
        }
        #endregion

        #region CPU-Safe Icon Painting (CPU Nearest-Neighbor)

        /// <summary>
        /// Writes the Nuke icon into an inventory atlas cell using **only** GetData/SetData.
        /// This avoids SpriteBatch.Begin nesting and RenderTarget lifetime issues.
        /// </summary>
        private static void WriteIconCpu(Texture2D atlas, int cellSize, Texture2D src, int itemId)
        {
            if (atlas == null || src == null || atlas.IsDisposed || src.IsDisposed) return;

            int cols = Math.Max(1, atlas.Width / cellSize);
            var cell = new Rectangle(
                (itemId % cols) * cellSize,
                (itemId / cols) * cellSize,
                cellSize, cellSize
            );

            // Fast-path: Exact size -> direct copy into the cell.
            if (src.Width == cellSize && src.Height == cellSize)
            {
                var pixels = new Color[cellSize * cellSize];
                src.GetData(pixels);
                atlas.SetData(0, cell, pixels, 0, pixels.Length);
                return;
            }

            // CPU nearest-neighbor scale: Read source -> scale -> write to atlas cell.
            var srcW   = src.Width;
            var srcH   = src.Height;
            var srcBuf = new Color[srcW * srcH];
            src.GetData(srcBuf);

            var dstW   = cellSize;
            var dstH   = cellSize;
            var dstBuf = new Color[dstW * dstH];

            // Center-of-pixel sampling to reduce jaggies a bit vs pure floor().
            for (int y = 0; y < dstH; y++)
            {
                int sy = (int)((y + 0.5f) * srcH / (float)dstH);
                if (sy >= srcH) sy = srcH - 1;

                for (int x = 0; x < dstW; x++)
                {
                    int sx = (int)((x + 0.5f) * srcW / (float)dstW);
                    if (sx >= srcW) sx = srcW - 1;

                    dstBuf[y * dstW + x] = srcBuf[sy * srcW + sx];
                }
            }

            atlas.SetData(0, cell, dstBuf, 0, dstBuf.Length);
        }
        #endregion

        #endregion

        /// <summary>
        /// Registers the Nuke item class, paints its icon(s), and (optionally) applies block skin once icons are ready.
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), "FinishInitialization")]
        static class Patch_RegisterNuke_Once_And_PaintIcon
        {
            static bool _didRegister;

            static void Postfix(GraphicsDevice device)
            {
                if (_didRegister)
                {
                    PaintIcon(device);
                    NukeRecipeInjector.ApplyOrUpdate();
                    NukeSurrogateNameInjector.ApplyOrUpdate();
                    NukeSurrogateDescriptionInjector.ApplyOrUpdate();
                    return;
                }

                // Choose an in-range ID.
                int id = NukeRegistry.NukeId;
                if (id < 0) { Log("[Nuke] No free atlas slots for item icons."); return; }
                NukeRegistry.NukeId = id;

                // Create BlockInventoryItemClass(id, block, name, weight)
                // (Use reflection to avoid version fragility.)
                var invIdsType = typeof(InventoryItemIDs);
                var blockType  = typeof(BlockTypeEnum);
                var bicType    = AccessTools.TypeByName("BlockInventoryItemClass");

                string desc    = NukeConfig.TextureSurrogateDescription;
                if (string.IsNullOrWhiteSpace(desc))
                    desc = "Nuke";
                else
                    desc = desc.Trim();

                var ctor  = AccessTools.Constructor(bicType, new[] { invIdsType, blockType, typeof(string), typeof(float) });
                var klass = (InventoryItem.InventoryItemClass)ctor.Invoke(new object[]
                {
                    Enum.ToObject(invIdsType, id),
                    NukeRegistry.TextureSurrogate,
                    desc,
                    1.0f
                });

                // Register.
                var reg = AccessTools.Method(typeof(InventoryItem), "RegisterItemClass", new[] { typeof(InventoryItem.InventoryItemClass) });
                reg.Invoke(null, new object[] { klass });

                Log($"[Nuke] Registered InventoryItemClass for ID {id}.");

                _didRegister = true;

                // Immediately paint the icon into the atlases (see #3).
                PaintIcon(device);

                // Add/update the Nuke crafting recipe once our item class exists.
                NukeRecipeInjector.ApplyOrUpdate();

                // Add/update the Nuke surrogates name once our item class exists.
                NukeSurrogateNameInjector.ApplyOrUpdate();

                // Add/update the Nuke surrogates description once our item class exists.
                NukeSurrogateDescriptionInjector.ApplyOrUpdate();

                // Safe: Icons are baked now.
                if (NukeTextureSurrogateSkin.IconsReady())
                    NukeTextureSurrogateSkin.Apply();
            }

            /// <summary>
            /// Paints the item icon cell(s) in the small/large inventory atlases for <see cref="NukeRegistry.NukeId"/>.
            /// </summary>
            static void PaintIcon(GraphicsDevice device)
            {
                try
                {
                    if (NukeRegistry.NukeId < 0) return;

                    var smallAtlas = AccessTools.Field(typeof(InventoryItem), "_2DImages")?.GetValue(null) as Texture2D;
                    var largeAtlas = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge")?.GetValue(null) as Texture2D;
                    if (smallAtlas == null && largeAtlas == null)
                    {
                        Log("[Nuke] Icon atlases not available yet.");
                        return;
                    }

                    using (var src = ModContent.LoadTexture(device, @"Textures\Icons\Nuke.png"))
                    {
                        if (src == null) { Log("[Nuke] Icon not found (disk or embedded)."); return; }

                        // CPU-only writes: safe during Draw, safe across device resets (no RT, no Begin()).
                        WriteIconCpu(largeAtlas, 128, src, NukeRegistry.NukeId);
                        WriteIconCpu(smallAtlas, 64,  src, NukeRegistry.NukeId);

                        Log($"[Nuke] Wrote icon for ID {NukeRegistry.NukeId} into atlases (CPU scaler).");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] Icon paint failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Placement Marking (Post-Set) - Prevents "First C4 After Nuke" From Swapping

        /// <summary>
        /// When our surrogate block is set and the player had the Nuke item selected, mark the cell and
        /// clear the one-shot arming flag so the *next* C4 placement is vanilla again.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), "SetBlock", new[] { typeof(IntVector3), typeof(BlockTypeEnum) })]
        static class Patch_SetBlock_MarkNukePlacement
        {
            static void Postfix(IntVector3 worldIndex, BlockTypeEnum type)
            {
                if (type != NukeRegistry.TextureSurrogate) return;

                // Always record our surrogate placement.
                NukeMap.Add(worldIndex);

                // ALWAYS disarm the one-shot so the next C4 is vanilla.
                NukeHelpers.Clear();
            }
        }
        #endregion

        #region Legacy Placement Tracker + HUD Catch (For Compatibility With Other Radius Patches)

        /// <summary>
        /// Tracks positions where we armed the next fuse as a nuke (legacy tagger used by some variants).
        /// </summary>
        internal static class NukePlacementTracker
        {
            private static readonly HashSet<IntVector3> _nukePositions = new HashSet<IntVector3>();
            private static bool _armingNextFuseIsNuke;

            public static bool IsNukeAt(IntVector3 pos) => _nukePositions.Contains(pos);

            public static void MarkWillArmNextFuseAsNuke() => _armingNextFuseIsNuke = true;

            public static void OnFuseArmed(IntVector3 pos)
            {
                if (_armingNextFuseIsNuke)
                {
                    _nukePositions.Add(pos);
                    _armingNextFuseIsNuke = false;
                    Log($"[Nuke] Fuse armed at {pos}.");
                }
            }

            public static void OnDetonated(IntVector3 pos)
            {
                _nukePositions.Remove(pos);
                Log($"[Nuke] Detonated at {pos}.");
            }

            public static void Tag(IntVector3 p) => _nukePositions.Add(p);
            public static void Clear(IntVector3 p) => _nukePositions.Remove(p);
        }

        /// <summary>
        /// Postfix that flags the "next fuse" as nuke when player is holding the Nuke item and uses place action.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput",
            new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        internal static class Patch_HUD_CatchNukePlace
        {
            static void Postfix(InGameHUD __instance, InputManager inputManager, GameController controller, KeyboardInput chatPad, GameTime gameTime)
            {
                var active = __instance?.ActiveInventoryItem?.ItemClass?.ID ?? (InventoryItemIDs)(-1);
                if ((int)active == NukeRegistry.NukeId)
                {
                    var map = CastleMinerZGame.Instance?._controllerMapping;
                    // "Use" + either mouse press or pad use - matches how blocks are placed in HUD input.
                    bool userWantsPlace = (map?.Use.Held ?? false) || (map?.Use.Pressed ?? false) ||
                                          (inputManager?.Mouse?.LeftButtonPressed == true) ||
                                          (inputManager?.Mouse?.RightButtonPressed == true);
                    if (userWantsPlace)
                    {
                        NukePlacementTracker.MarkWillArmNextFuseAsNuke();
                    }
                }
            }
        }
        #endregion

        #region Per-Detonation Radius Expansion (Variant A: Legacy Tracker) + Block-Hardness Test

        /// <summary>
        /// Replacement for the vanilla block-in-range test that honors temporarily edited radii and hardness.
        /// </summary>
        /// <summary>
        /// Replacement for the vanilla block-in-range test that (a) honors temporarily edited radii
        /// and (b) lets ExtraBreakables be broken during a nuke regardless of hardness flags.
        /// For everything else we fall back to vanilla by returning true (so we don't second-guess it).
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_BlockWithinLevelBlastRange
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Explosive), "BlockWithinLevelBlastRange",
                    new Type[] { typeof(IntVector3), typeof(BlockTypeEnum), typeof(ExplosiveTypes) });

            static bool Prefix(IntVector3 offset, BlockTypeEnum block, ExplosiveTypes explosiveType, ref bool __result)
            {
                var ranges     = (int[])AccessTools.Field(typeof(Explosive),   "cDestructionRanges").GetValue(null);
                var L1         = (bool[])AccessTools.Field(typeof(Explosive),  "Level1Hardness").GetValue(null);
                var L2         = (bool[])AccessTools.Field(typeof(Explosive),  "Level2Hardness").GetValue(null);
                var breakTable = (bool[,])AccessTools.Field(typeof(Explosive), "BreakLookup").GetValue(null);

                int typeIdx    = (int)explosiveType;
                int bi         = (int)block;
                int r          = (ranges != null) ? ranges[typeIdx] : 2;

                // If engine says this explosive cannot break this block at all, bail.
                if (breakTable != null && bi >= 0 && bi < breakTable.GetLength(1) && breakTable[typeIdx, bi])
                {
                    __result = false;
                    return false;
                }

                // Shrink for hard / very hard (do NOT fail for "neither hard nor very hard").
                if      (L2 != null && bi >= 0 && bi < L2.Length && L2[bi]) r = Math.Max(1, r / 3);
                else if (L1 != null && bi >= 0 && bi < L1.Length && L1[bi]) r = Math.Max(1, r / 2);

                __result = Math.Abs(offset.X) <= r && Math.Abs(offset.Y) <= r && Math.Abs(offset.Z) <= r;
                return false;
            }
        }
        #endregion

        #region Per-Detonation Radius Expansion (Variant B: Keyed By World State & NukeMap)

        /// <summary>
        /// Variant that expands blast only if the detonation occurs at a surrogate voxel we own (NukeMap).
        /// Restores tables immediately after. Also clears NukeMap at that cell.
        /// </summary>
        internal static class NukeDetonation
        {
            [ThreadStatic] private static bool              _insideNuke;
            public static bool IsInsideNuke                 => _insideNuke;

            private static readonly HashSet<IntVector3>     _nukeFuses = new HashSet<IntVector3>();
            public static void TagFuse(IntVector3 pos)      => _nukeFuses.Add(pos);
            public static void ClearTagFuse(IntVector3 pos) => _nukeFuses.Remove(pos);
            public static bool IsFuseTagged(IntVector3 pos) => _nukeFuses.Contains(pos);

            // Unified "is this a nuke detonation?" helper that works even if the block is gone.
            public static bool IsNukeLocation(IntVector3 pos)
            {
                // If we explicitly tagged this position for a nuke, that wins.
                if (IsFuseTagged(pos))
                    return true;

                // Fallback: Live world state (TextureSurrogate block that we still track).
                var bt = BlockTerrain.Instance;
                if (bt == null)
                    return false;

                return bt.GetBlockWithChanges(pos) == NukeRegistry.TextureSurrogate &&
                       NukeMap.Has(pos);
            }

            [HarmonyPatch(typeof(Explosive), nameof(Explosive.HandleDetonateExplosiveMessage))]
            internal static class Patch_Explosive_NukeRadiusPerDetonation
            {
                private struct State
                {
                    public bool           ModifiedTables;
                    public ExplosiveTypes Type;
                    public int            OldRange;
                    public float          OldDmg;
                    public float          OldKill;

                    // Per-nuke detonation flips we applied to BreakLookup[typeIdx, blockIdx].
                    public List<(int BlockIdx, bool Old)> BreakFlips;
                }

                static void Prefix(DetonateExplosiveMessage msg, ref State __state)
                {
                    __state      = default;
                    __state.Type = msg.ExplosiveType;

                    // Only treat as a NUKE if it's the surrogate at a position we own.
                    bool isNukeHere = IsNukeLocation(msg.Location);
                    if (!isNukeHere) return;

                    // Bump radii for just this detonation.
                    var ranges    = (int[])AccessTools.Field(typeof(Explosive), "cDestructionRanges").GetValue(null);
                    var dmgRanges = (float[])AccessTools.Field(typeof(Explosive), "cDamageRanges").GetValue(null);
                    var killRange = (float[])AccessTools.Field(typeof(Explosive), "cKillRanges").GetValue(null);
                    int idx       = (int)msg.ExplosiveType;

                    __state.OldRange = ranges[idx];
                    __state.OldDmg   = dmgRanges[idx];
                    __state.OldKill  = killRange[idx];

                    ranges[idx]    = NukeRegistry.NUKE_BLOCK_RADIUS;
                    dmgRanges[idx] = NukeRegistry.NUKE_DMG_RADIUS;
                    killRange[idx] = NukeRegistry.NUKE_KILL_RADIUS;

                    __state.ModifiedTables = true;

                    // While this detonation runs, tell the engine that our extra blocks are breakable by this explosive.
                    _insideNuke = true;
                    try
                    {
                        // === BreakLookup overrides with whitelist-first precedence ===
                        var breakLookupObj = AccessTools.Field(typeof(Explosive), "BreakLookup")?.GetValue(null);
                        if (breakLookupObj is bool[,] breakLookup)
                        {
                            int typeIdx    = (int)msg.ExplosiveType;   // Row for this explosive type.
                            int blockCount = breakLookup.GetLength(1);

                            // If ExtraBreakables has entries -> ONLY those blocks are force-breakable.
                            if (NukeRegistry.ExtraBreakables.Count > 0)
                            {
                                __state.BreakFlips = new List<(int, bool)>(NukeRegistry.ExtraBreakables.Count);
                                foreach (var bte in NukeRegistry.ExtraBreakables)
                                {
                                    int bi = (int)bte;
                                    if (bi < 0 || bi >= blockCount) continue;

                                    bool old = breakLookup[typeIdx, bi];
                                    if (old) // only flip if previously "blocked"
                                    {
                                        __state.BreakFlips.Add((bi, old));
                                        breakLookup[typeIdx, bi] = false;  // Allow breaking whitelist entries.
                                    }
                                }
                            }
                            // Else if NukeIgnoresHardness -> allow ALL blocks for this detonation.
                            else if (NukeRegistry.NukeIgnoresHardness)
                            {
                                __state.BreakFlips = new List<(int, bool)>(blockCount);
                                for (int bi = 0; bi < blockCount; bi++)
                                {
                                    bool old = breakLookup[typeIdx, bi];
                                    if (old)
                                    {
                                        __state.BreakFlips.Add((bi, old));
                                        breakLookup[typeIdx, bi] = false; // Allow all.
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Best-effort: Even if BreakLookup shape changes, our radius+Prefix above still lets Extras through.
                    }

                    Log($"[Nuke] NUKE detonation at {msg.Location} (type={msg.ExplosiveType}).");
                }

                static void Postfix(DetonateExplosiveMessage msg, State __state)
                {
                    try
                    {
                        // Restore BreakLookup flips (per-detonation).
                        if (__state.BreakFlips != null && __state.BreakFlips.Count > 0)
                        {
                            // === NEW: restore BreakLookup flips
                            var breakLookupObj = AccessTools.Field(typeof(Explosive), "BreakLookup")?.GetValue(null);
                            if (breakLookupObj is bool[,] breakLookup && __state.BreakFlips != null)
                            {
                                int typeIdx = (int)__state.Type;
                                foreach (var (blockIdx, old) in __state.BreakFlips)
                                {
                                    // Defensive bounds check (tables can vary across builds)
                                    if (blockIdx >= 0 && blockIdx < breakLookup.GetLength(1))
                                        breakLookup[typeIdx, blockIdx] = old;
                                }
                            }
                        }

                        // Restore the radius tables.
                        if (__state.ModifiedTables)
                        {
                            var ranges     = (int[])AccessTools.Field(typeof(Explosive),   "cDestructionRanges").GetValue(null);
                            var dmgRanges  = (float[])AccessTools.Field(typeof(Explosive), "cDamageRanges").GetValue(null);
                            var killRange  = (float[])AccessTools.Field(typeof(Explosive), "cKillRanges").GetValue(null);
                            int idx = (int)__state.Type;

                            ranges[idx]    = __state.OldRange;
                            dmgRanges[idx] = __state.OldDmg;
                            killRange[idx] = __state.OldKill;
                        }
                    }
                    finally
                    {
                        _insideNuke = false;
                        NukeMap.Remove(msg.Location); // This voxel is gone.
                    }
                }
            }

            /// <summary>
            /// When IncludeDefaults is false, TNT/C4 still use vanilla explosions.
            /// This helper just looks for nukes in the vanilla destruction radius and
            /// chains them as C4 detonations.
            /// </summary>
            public static void TryChainFromVanillaExplosion(DetonateExplosiveMessage msg)
            {
                var game = CastleMinerZGame.Instance;
                var bt = BlockTerrain.Instance;
                if (game == null || bt == null)
                    return;

                // Only care about TNT / C4 here.
                if (msg.ExplosiveType != ExplosiveTypes.TNT &&
                    msg.ExplosiveType != ExplosiveTypes.C4)
                    return;

                // Use the engine's vanilla destruction ranges for this explosive.
                var ranges = (int[])AccessTools
                    .Field(typeof(Explosive), "cDestructionRanges")
                    .GetValue(null);

                int idx = (int)msg.ExplosiveType;
                int baseR = (ranges != null && idx >= 0 && idx < ranges.Length)
                             ? ranges[idx]
                             : 2;

                int r = Math.Max(1, baseR);
                IntVector3 c = msg.Location;

                // Simple Chebyshev cube scan: |dx|,|dy|,|dz| <= r.
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            var p = new IntVector3(c.X + dx, c.Y + dy, c.Z + dz);

                            var block = bt.GetBlockWithChanges(p);
                            if (block != NukeRegistry.TextureSurrogate || !NukeMap.Has(p))
                                continue;

                            // Mark this voxel as a pending nuke detonation so we still treat it as a nuke
                            // even if vanilla TNT/C4 deletes the block before the message is handled.
                            TagFuse(p);

                            // Chain this nuke as a new C4 detonation at p.
                            DetonateExplosiveMessage.Send(
                                game.MyNetworkGamer,
                                p,
                                true,
                                ExplosiveTypes.C4);
                        }
            }
        }
        #endregion

        #region Async Explosion Queue (Producer/Consumer)

        /*
           =======================================================================================================
           TacticalNuke - Async Explosion Pipeline (Organized)
           -------------------------------------------------------------------------------------------------------
           Purpose:
           • Offload block collection for explosions (especially nukes) to a background worker, then apply
             a bounded number of block edits per frame to avoid stalls.
           • Preserve vanilla splash-damage behavior while allowing per-explosion radius boosts for nukes.

           Threading Model:
           • Background thread builds a list of offset voxels ONLY (no world reads/writes off the main thread).
           • Main thread (PumpApply) consumes queued work and performs BlockTerrain mutations.

           Integration Points:
           • Patch_Explosive_HandleDetonate_Async (Prefix):      Decides radius, queues async job, handles splash.
           • Patch_DNAGame_Update_PumpAsyncExplosions (Postfix): Pumps the per-frame application budget.

           Performance Knobs:
           • AsyncExplosionManager_Settings.MaxBlocksPerFrame caps per-frame edits (tune to fix hitching vs. cleanup speed).

           Safety Notes:
           • Never touch BlockTerrain, Inventory, or GraphicsDevice off-thread.
           • Keep all Harmony patches in sync with the game build signatures.
           =======================================================================================================
        */

        /// <summary>
        /// Manages async preparation of explosion voxel offsets and paced in-frame application.
        /// </summary>
        internal static class AsyncExplosionManager
        {
            #region Job Container

            /// <summary>
            /// Immutable inputs + mutable results for one explosion's voxel work.
            /// NOTE: OffsetsToApply is filled on a worker thread; BlockTerrain must not be touched there.
            /// </summary>
            private sealed class Job
            {
                public IntVector3     Center;
                public ExplosiveTypes Type;
                public int            BaseRadius;     // Already nuke-aware at enqueue time.
                public bool           IsNuke;

                public volatile bool  Computed;
                public readonly List<IntVector3> OffsetsToApply = new List<IntVector3>(4096);

                public int            TriggeredTotal; // Total nukes this job has triggered so far.
            }
            #endregion

            private static readonly ConcurrentQueue<Job> _readyToApply = new ConcurrentQueue<Job>();

            /// <summary>
            /// Clears all queued explosion jobs so none will be applied.
            /// </summary>
            public static void ClearAllPending()
            {
                while (_readyToApply.TryDequeue(out _)) { } // GC will reclaim Job + OffsetsToApply.
            }

            /// <summary>
            /// Schedule the heavy scan on a background thread (offset enumeration only).
            /// </summary>
            public static void Enqueue(IntVector3 center, ExplosiveTypes type, int radius, bool nukeAware)
            {
                var job = new Job { Center = center, Type = type, BaseRadius = Math.Max(1, radius), IsNuke = nukeAware };

                // Build offsets in the worker; never touch BlockTerrain off-thread.
                Task.Run(() =>
                {
                    int r = job.BaseRadius;

                    // Pre-size to avoid repeated reallocations for large radii.
                    job.OffsetsToApply.Capacity =
                        Math.Max(job.OffsetsToApply.Capacity, (2 * r + 1) * (2 * r + 1) * (2 * r + 1));

                    for (int x = -r; x <= r; x++)
                        for (int y = -r; y <= r; y++)
                            for (int z = -r; z <= r; z++)
                                job.OffsetsToApply.Add(new IntVector3(x, y, z));

                    job.Computed = true;
                    _readyToApply.Enqueue(job);
                });
            }

            /// <summary>
            /// Apply a limited number of blocks per frame on the main thread, honoring hardness.
            /// </summary>
            /// <summary>
            /// Apply a limited number of blocks per frame on the main thread, honoring hardness and ExtraBreakables.
            /// </summary>
            /// <param name="maxBlocksPerFrame">Per-frame budget; prefer <see cref="AsyncExplosionManagerConfig.MaxBlocksPerFrame"/>.</param>
            /// <param name="onNukeVoxelHit">
            /// Optional callback invoked before clearing a voxel that is one of our nukes. Useful for daisy-chaining.
            /// </param>
            public static void PumpApply(int maxBlocksPerFrame = 500, Action<IntVector3> onNukeVoxelHit = null)
            {
                if (!AsyncExplosionManagerConfig.Enabled) return;

                // Nothing ready or the head job hasn't finished preparing offsets yet.
                if (!_readyToApply.TryPeek(out var head) || !head.Computed)
                    return;

                var bt = BlockTerrain.Instance;
                if (bt == null) { _readyToApply.TryDequeue(out _); return; }

                // Pull the same tables vanilla uses.
                var L1          = (bool[])AccessTools.Field(typeof(Explosive),  "Level1Hardness").GetValue(null);
                var L2          = (bool[])AccessTools.Field(typeof(Explosive),  "Level2Hardness").GetValue(null);
                var BreakLookup = (bool[,])AccessTools.Field(typeof(Explosive), "BreakLookup").GetValue(null);
                // BreakLookup[type, block] == true -> BLOCKED (do not break).
                // BreakLookup[type, block] == false -> allowed to break (subject to radius / hardness).

                // == unified decision ==
                bool ShouldBreak(BlockTypeEnum block, IntVector3 off, ExplosiveTypes type, int baseR, bool isNuke)
                {
                    int bi = (int)block;
                    if (block == BlockTypeEnum.Empty || bi < 0) return false;

                    // Helper for shape/jitter/hollow tests at a given radius.
                    bool PassMasks(int rr)
                    {
                        if (!InCraterShape(off, rr)) return false;
                        if (!PassHollow(off, rr))    return false;
                        if (!PassJitter(off, rr))    return false;
                        return true;
                    }

                    // -------------------------------
                    // NUKE path.
                    // -------------------------------
                    if (isNuke && CraterConfig.Shape != CraterShape.Vanilla) // Vanilla uses the games normal explosion pipeline.
                    {
                        // 1) Global override: nuke breaks everything inside crater.
                        if (NukeRegistry.NukeIgnoresHardness)
                            return PassMasks(baseR);

                        // 2) Whitelist: always break these, even if engine blocks them.
                        if (NukeRegistry.ExtraBreakables != null && NukeRegistry.ExtraBreakables.Contains(block))
                            return PassMasks(baseR);

                        // 3) Respect engine "cannot break" list (BreakLookup[explosive,block] == true => blocked).
                        try
                        {
                            if (BreakLookup != null)
                            {
                                int ti = (int)type;
                                if (ti >= 0 && ti < BreakLookup.GetLength(0) && bi >= 0 && bi < BreakLookup.GetLength(1))
                                {
                                    if (BreakLookup[ti, bi]) return false; // Explicitly blocked by engine.
                                }
                            }
                        }
                        catch { /* Defensive. */ }

                        // 4) Ignore L1/L2 radius shrink when NukeIgnoresHardness == false
                        //    (so it still "feels like it's on"): We only apply crater masks at full base radius.
                        return PassMasks(baseR);
                    }

                    // -------------------------------
                    // NON-NUKE (vanilla) path.
                    // -------------------------------

                    // Fast cube check helper (Chebyshev).
                    bool InCube(IntVector3 v, int radius)
                        => Math.Abs(v.X) <= radius && Math.Abs(v.Y) <= radius && Math.Abs(v.Z) <= radius;

                    // Vanilla gating: If this explosive cannot break this block at all, bail.
                    try
                    {
                        if (BreakLookup != null)
                        {
                            int ti = (int)type;
                            if (ti >= 0 && ti < BreakLookup.GetLength(0) && bi >= 0 && bi < BreakLookup.GetLength(1))
                            {
                                if (BreakLookup[ti, bi]) return false; // Explicitly blocked.
                            }
                        }
                    }
                    catch { /* Defensive. */ }

                    // Vanilla hardness: Shrink radius for hard / very hard blocks.
                    int  r        = baseR;
                    bool veryHard = (L2 != null && bi < L2.Length && L2[bi]);
                    bool hard     = (L1 != null && bi < L1.Length && L1[bi]);

                    if (veryHard)  r = Math.Max(1, r / 3);
                    else if (hard) r = Math.Max(1, r / 2);

                    return InCube(off, r);
                }

                int done = 0;
                int triggeredThisPump = 0; // How many nukes we chained *this* pump step.
                while (done < maxBlocksPerFrame && head.OffsetsToApply.Count > 0)
                {
                    // Pop from end (cheaper than removing from front).
                    int last = head.OffsetsToApply.Count - 1;
                    var off = head.OffsetsToApply[last];
                    head.OffsetsToApply.RemoveAt(last);

                    var p = new IntVector3(head.Center.X + off.X,
                                           head.Center.Y + off.Y,
                                           head.Center.Z + off.Z);

                    var block = bt.GetBlockWithChanges(p);

                    // 1) Daisy-chain nukes BEFORE hardness / BreakLookup.
                    // Notify BEFORE clearing if this voxel is one of our nukes (to daisy-chain).
                    if (block == NukeRegistry.TextureSurrogate && NukeMap.Has(p))
                    {
                        NukeDetonation.TagFuse(p);
                        head.TriggeredTotal++;
                        triggeredThisPump++;
                        onNukeVoxelHit?.Invoke(p);

                        // Do NOT clear this voxel here; the upcoming detonation will.
                        done++;
                        continue; // Important: Remove from OffsetsToApply, but don't SetBlock.
                    }

                    // 2) Normal hardness / BreakLookup for everything else.
                    if (!ShouldBreak(block, off, head.Type, head.BaseRadius, head.IsNuke))
                        continue;

                    // Preserve vanilla chain for TNT/C4.
                    if (AsyncExplosionManagerConfig.IncludeDefaults &&
                        (block == BlockTypeEnum.TNT || block == BlockTypeEnum.C4))
                    {
                        var typeToSend = (block == BlockTypeEnum.TNT) ? ExplosiveTypes.TNT : ExplosiveTypes.C4;

                        // Don't clear; let the detonation handler do it (and chain further).
                        DetonateExplosiveMessage.Send(
                            CastleMinerZGame.Instance.MyNetworkGamer,
                            p,
                            true,
                            typeToSend);
                        continue;
                    }

                    // bt.SetBlock(p, BlockTypeEnum.Empty);                                                   // BlockTerrain.SetBlock only updates client side.
                    AlterBlockMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, p, BlockTypeEnum.Empty); // Announce block changes globally via AlterBlockMessage.
                    done++;
                }

                // Announce once per pump, at the ROOT location of this explosion job.
                if (triggeredThisPump > 0)
                {
                    AnnouncementConfig.Announce(
                        AnnouncementConfig.MessageType.ChainTrigger,
                        head.Center,
                        chainCount: triggeredThisPump);
                }

                // Finished this job; dequeue and move on.
                if (head.OffsetsToApply.Count == 0)
                    _readyToApply.TryDequeue(out _);
            }
        }

        #region Harmony Patch: Explosives (Vanilla Radii Tweaks)

        /// <summary>
        /// Post-load patch that increases vanilla TNT/C4 blast/damage/kill radii.
        /// Runs once after DNAGame.SecondaryLoad finishes loading content.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        static class Patch_TweakVanillaExplosiveRadii
        {
            /// <summary>
            /// One-time post-load hook that applies TNT/C4 tweaks to the engine's
            /// static explosion tables after <see cref="DNAGame.SecondaryLoad"/>.
            /// </summary>
            /// <remarks>
            /// Notes:
            /// • Defers all logic to <see cref="UpdateVanillaExplosives"/> so hot-reload
            ///   can reuse the exact same path.
            /// • Exits early inside UpdateVanillaExplosives if the radius tables are
            ///   missing in this build.
            /// • Uses config-driven values when VanillaExplosiveConfig.Enabled is
            ///   true, or restores CastleMiner Z vanilla TNT/C4 radii when false.
            /// • Emits a log line describing the before/after values for debugging.
            /// </remarks>
            static void Postfix()
            {
                UpdateVanillaExplosives();
                NukeRecipeInjector.ApplyOrUpdate();
                NukeSurrogateNameInjector.ApplyOrUpdate();
                NukeSurrogateDescriptionInjector.ApplyOrUpdate();
            }
        }

        /// <summary>
        /// Applies the current TNT/C4 settings to the engine's static explosion tables.
        /// Called once after SecondaryLoad and again on config hot-reload so vanilla
        /// explosives always reflect the latest values (or are restored to vanilla).
        /// </summary>
        public static void UpdateVanillaExplosives()
        {
            var ranges     = (int[])AccessTools.Field(typeof(Explosive),   "cDestructionRanges").GetValue(null);
            var dmgRanges  = (float[])AccessTools.Field(typeof(Explosive), "cDamageRanges").GetValue(null);
            var killRanges = (float[])AccessTools.Field(typeof(Explosive), "cKillRanges").GetValue(null);
            if (ranges == null) return;

            int TNT = (int)ExplosiveTypes.TNT;
            int C4  = (int)ExplosiveTypes.C4;

            if (VanillaExplosiveConfig.Enabled)
            {
                // Capture original values for logging.
                int   oldTntRange = ranges[TNT],            oldC4Range = ranges[C4];
                float oldTntDmg   = dmgRanges?[TNT]  ?? 0f, oldC4Dmg   = dmgRanges?[C4]  ?? 0f;
                float oldTntKill  = killRanges?[TNT] ?? 0f, oldC4Kill  = killRanges?[C4] ?? 0f;

                // Configured "in-game" destruction radii (blocks); engine uses +1 internally.
                int tntBlocks = VanillaExplosiveConfig.TNT_BlockRadius;
                int c4Blocks  = VanillaExplosiveConfig.C4_BlockRadius;

                ranges[TNT] = (tntBlocks > 0) ? tntBlocks + 1 : 0;
                ranges[C4]  = (c4Blocks  > 0) ? c4Blocks  + 1 : 0;

                if (dmgRanges != null && killRanges != null)
                {
                    dmgRanges[TNT]  = VanillaExplosiveConfig.TNT_DmgRadius;
                    killRanges[TNT] = VanillaExplosiveConfig.TNT_KillRadius;

                    dmgRanges[C4]   = VanillaExplosiveConfig.C4_DmgRadius;
                    killRanges[C4]  = VanillaExplosiveConfig.C4_KillRadius;
                }

                Log(
                    "[Nuke] Tweaked TNT/C4 values | " +
                    $"Destruction radii: TNT={oldTntRange}➜{ranges[TNT]}, C4={oldC4Range}➜{ranges[C4]} | " +
                    (dmgRanges != null && killRanges != null
                        ? $"Damage / Kill: TNT=({oldTntDmg}➜{dmgRanges[TNT]},{oldTntKill}➜{killRanges[TNT]}), " +
                          $"C4=({oldC4Dmg}➜{dmgRanges[C4]},{oldC4Kill}➜{killRanges[C4]})"
                        : "Damage / Kill: (tables missing).")
                );
            }
            else
            {
                // Capture original values.
                int   oldTntRange = ranges[TNT],            oldC4Range = ranges[C4];
                float oldTntDmg   = dmgRanges?[TNT]  ?? 0f, oldC4Dmg   = dmgRanges?[C4]  ?? 0f;
                float oldTntKill  = killRanges?[TNT] ?? 0f, oldC4Kill  = killRanges?[C4] ?? 0f;

                // Destruction radii: TNT=2, C4=3.
                // Tweak TNT/C4 back to its vanilla destruction ranges.
                ranges[TNT] = 2 + 1; // Was 2.
                ranges[C4]  = 3 + 1; // Was 3.

                if (dmgRanges != null && killRanges != null)
                {
                    // Damage/Kill: TNT=(6,3), C4=(12,6).
                    dmgRanges[TNT] = 6f;  killRanges[TNT] = 3f;
                    dmgRanges[C4]  = 12f; killRanges[C4]  = 6f;
                }

                // Original values:
                // Destruction radii: TNT=2, C4=3.
                // Damage / Kill:     TNT=(6,3), C4=(12,6).
                Log("[Nuke] Restored original TNT/C4 values | " +
                    $"Destruction radii: TNT={oldTntRange}➜{ranges[TNT]}, C4={oldC4Range}➜{ranges[C4]} | " +
                    (dmgRanges != null && killRanges != null
                        ? $"Damage / Kill: TNT=({oldTntDmg}➜{dmgRanges[TNT]},{oldTntKill}➜{killRanges[TNT]}), " +
                          $"C4=({oldC4Dmg}➜{dmgRanges[C4]},{oldC4Kill}➜{killRanges[C4]})"
                        : "Damage / Kill: (tables missing).")
                );
            }
        }
        #endregion

        #region Crater Shaping Utilities

        /*
           =============================================================
           Crater shaping utilities (shape, hollow shells, rim jitter).
           Deterministic & allocation-free; safe to call inside hot loops.
           Typical usage:
             if (InCraterShape(off, R) && PassHollow(off, R) && PassJitter(off, R))
                 BreakBlock(world + off);
           =============================================================
        */

        #region Crater Config (Shape & Parameters)

        /// <summary>
        /// Supported crater volume primitives used to carve out the blast.
        /// </summary>
        internal enum CraterShape
        {
            Vanilla,
            Cube,
            Sphere,
            Diamond,
            CylinderY,
            Ellipsoid,
            Bowl
        }
        #endregion

        #region Deterministic Noise Helper

        /// <summary>
        /// Tiny, fast, deterministic 3D hash -> [0,1]. No allocations; stable across runs and platforms.
        /// </summary>
        static float Noise01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 982451653 + seed * 1274126177);
                h ^= h >> 13; h *= 1274126177; h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }
        #endregion

        #region Shape Membership Tests

        /// <summary>
        /// Tests if an offset <paramref name="v"/> (relative to blast center) lies inside the
        /// configured crater primitive of radius <paramref name="r"/> (before hollow/jitter masks).
        /// </summary>
        /// <remarks>
        /// Shapes:
        /// • Cube:      Axis-aligned cube (Chebyshev ball).
        /// • Sphere:    True sphere (respects <see cref="CraterConfig.XZScale"/> / <see cref="CraterConfig.YScale"/>).
        /// • Diamond:   |x|+|y|+|z| ≤ 1 (octahedron / Manhattan ball).
        /// • CylinderY: Infinite cylinder along Y (clamped to |Y| ≤ r).
        /// • Ellipsoid: Sphere with XZ/Y scaling.
        /// • Bowl:      Lower hemisphere like scoop; shallower near rim.
        /// </remarks>
        static bool InCraterShape(IntVector3 v, int r)
        {
            // Scales for ellipsoid/bowl.
            float sx = CraterConfig.XZScale;
            float sy = CraterConfig.YScale;
            float x = v.X / (r * sx);
            float y = v.Y / (r * sy);
            float z = v.Z / (r * sx);

            switch (CraterConfig.Shape)
            {
                case CraterShape.Vanilla:
                case CraterShape.Cube:
                    return Math.Abs(v.X) <= r && Math.Abs(v.Y) <= r && Math.Abs(v.Z) <= r;

                case CraterShape.Sphere:
                    return (x * x + y * y + z * z) <= 1f;

                case CraterShape.Diamond: // |x|+|y|+|z| ≤ 1.
                    return (Math.Abs(x) + Math.Abs(y) + Math.Abs(z)) <= 1f;

                case CraterShape.CylinderY: // vertical cylinder, squashed by XZScale; full height r.
                    return (x * x + z * z) <= 1f && Math.Abs(v.Y) <= r;

                case CraterShape.Ellipsoid:
                    return (x * x + y * y + z * z) <= 1f; // Same as Sphere but Y/XZ scaled by config.

                case CraterShape.Bowl:
                    // Only carve below center, shallower near top. Square falloff for a "scoop".
                    if (v.Y > 0) return false;
                    // Reduce allowed radius as Y rises (curved profile).
                    float ry = r * (1f - (Math.Abs(y)));    // Smaller radius near rim.
                    float xx = v.X / Math.Max(1f, ry * sx);
                    float zz = v.Z / Math.Max(1f, ry * sx);
                    return (xx * xx + zz * zz) <= 1f;

                default: return true;
            }
        }
        #endregion

        #region Hollow/Shell Mask

        /// <summary>
        /// When <see cref="CraterConfig.Hollow"/> is enabled, keeps only the outer shell band
        /// of thickness <see cref="CraterConfig.ShellThick"/>; otherwise passes through.
        /// </summary>
        /// <returns>
        /// True if the voxel should be removed for hollow shell (or hollow disabled),
        /// false if it should be kept (interior when hollow is on).
        /// </returns>
        /// <remarks>
        /// Call after <see cref="InCraterShape"/>; this method assumes <paramref name="v"/> is
        /// within the outer shape and filters out the interior region.
        /// </remarks>
        static bool PassHollow(IntVector3 v, int r)
        {
            if (!CraterConfig.Hollow || CraterConfig.ShellThick <= 0) return true;

            // Inside outer surface?
            bool outer = InCraterShape(v, r);
            if (!outer) return false;

            // Also inside INNER surface? If yes, keep (do not break) to make it hollow.
            int inner = Math.Max(1, r - CraterConfig.ShellThick);
            bool innerHit = InCraterShape(v, inner);
            return !innerHit; // Only break the shell band.
        }
        #endregion

        #region Edge Jitter Mask

        /// <summary>
        /// Adds a simple, deterministic "scallop" to the crater rim so it isn't perfectly smooth.
        /// Only affects the outer edge band sized by CraterConfig.EdgeJitter.
        /// </summary>
        /// <param name="v">Offset from blast center (voxel-space).</param>
        /// <param name="r">Crater radius (voxels).</param>
        /// <returns>
        /// True if this voxel passes the jitter mask and should be removed; otherwise false.
        /// </returns>
        static bool PassJitter(IntVector3 v, int r)
        {
            float j = Clamp(CraterConfig.EdgeJitter, 0f, 0.5f);
            if (j <= 0f) return true;

            // Use a shape-consistent normalized "radius" (1.0 at the outer surface)
            float norm = EdgeNorm01(v, r);

            // Only randomize in the outer band [1 - j, 1]
            if (norm < (1f - j)) return true;

            float n = Noise01(v.X, v.Y, v.Z, CraterConfig.NoiseSeed);
            float t = (norm - (1f - j)) / j;   // 0 at band start -> 1 at rim
            return n > t;
        }

        /// <summary>
        /// Computes a shape-aware, normalized edge distance proxy for the current crater type.
        /// </summary>
        /// <param name="v">Offset from blast center (voxel-space).</param>
        /// <param name="r">Crater radius (voxels).</param>
        /// <returns>
        /// Fraction of "how close to the edge" this voxel is:
        /// • ~0 at center, ~1 at the surface, >1 outside (for most shapes).
        /// Special cases mirror <see cref="InCraterShape"/> geometry.
        /// </returns>
        static float EdgeNorm01(IntVector3 v, int r)
        {
            // normalized coords (same scaling you use in InCraterShape)
            float sx = CraterConfig.XZScale;
            float sy = CraterConfig.YScale;
            float x = v.X / (r * sx);
            float y = v.Y / (r * sy);
            float z = v.Z / (r * sx);

            switch (CraterConfig.Shape)
            {
                case CraterShape.Vanilla:
                case CraterShape.Sphere:
                case CraterShape.Ellipsoid:
                    // Euclidean distance to rim (1.0 at the surface)
                    return (float)Math.Sqrt(x * x + y * y + z * z);

                case CraterShape.CylinderY:
                    // Radial in XZ, ignore Y (clamp Y elsewhere as you do)
                    return (float)Math.Sqrt(x * x + z * z);

                case CraterShape.Cube:
                    // Chebyshev matches the cube surface
                    return Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
                case CraterShape.Diamond:
                    // L1 norm matches the octahedron surface
                    return Math.Abs(x) + Math.Abs(y) + Math.Abs(z);

                case CraterShape.Bowl:
                    // Only below center; radius shrinks with +Y like your bowl shape
                    if (y > 0) return float.PositiveInfinity; // outside bowl
                    float ry = 1f - Math.Abs(y);             // allowed radius fraction at this Y
                    float xx = x / Math.Max(ry, 1e-5f);
                    float zz = z / Math.Max(ry, 1e-5f);
                    return (float)Math.Sqrt(xx * xx + zz * zz);

                default:
                    return (float)Math.Sqrt(x * x + y * y + z * z);
            }
        }

        /// <summary>
        /// Fast scalar clamp with inclusive bounds.
        /// </summary>
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);
        #endregion

        #endregion

        #region Harmony: Explode -> Async Prepare + Paced Apply

        /// <summary>
        /// Replaces vanilla detonation handling with:
        /// 1) Immediate world cleanup / effects / splash (main thread),
        /// 2) Async enumeration of voxels to clear,
        /// 3) Paced block application on subsequent frames.
        /// </summary>
        [HarmonyPatch(typeof(Explosive), nameof(Explosive.HandleDetonateExplosiveMessage))]
        static class Patch_Explosive_HandleDetonate_Async
        {
            // Light-weight parts stay on the main thread immediately.
            static bool Prefix(DetonateExplosiveMessage msg)
            {
                if (!AsyncExplosionManagerConfig.Enabled) return true; // Let vanilla handle everything.
                var bt = BlockTerrain.Instance;

                // Only the local originator computes block destruction (matches vanilla).
                // Some builds don't set Sender.IsLocal=true on the send-side re-entrant handler.
                // Use the game helper (works in SP and host MP) and still require OriginalExplosion to dedupe.
                bool isLocalSender = CastleMinerZGame.Instance?.IsLocalPlayerId(msg.Sender.Id) ?? true;

                // Is this voxel a NUKE (TextureSurrogate we placed)?
                bool isNukeHere = NukeDetonation.IsNukeLocation(msg.Location);

                // When defaults are disabled: TNT/C4 use vanilla crater but we still run the sidecar nuke scan.
                if (!AsyncExplosionManagerConfig.IncludeDefaults && !isNukeHere)
                {
                    if (isLocalSender)
                    {
                        NukeDetonation.TryChainFromVanillaExplosion(msg);
                    }

                    return true;    // vanilla explosion pipeline for non-nukes
                }

                // IMPORTANT: For vanilla TNT/C4, don't intercept; keep stock chain reaction.
                if (!AsyncExplosionManagerConfig.IncludeDefaults && !isNukeHere) return true;

                // --- Decide block-destruction radius before touching the world ---
                var ranges    = (int[])AccessTools.Field(typeof(Explosive),   "cDestructionRanges").GetValue(null);
                var dmgRanges = (float[])AccessTools.Field(typeof(Explosive), "cDamageRanges").GetValue(null);
                var killRange = (float[])AccessTools.Field(typeof(Explosive), "cKillRanges").GetValue(null);
                int idx       = (int)msg.ExplosiveType;

                int baseRadius = ranges != null ? ranges[idx] : 2;
                if (isNukeHere)
                    baseRadius = Math.Max(baseRadius, NukeRegistry.NUKE_BLOCK_RADIUS);

                // Clear any pending fuse/flash/audio BEFORE SetBlock so SetBlock_Cleanup does NOT think this was a disarm.
                AnnouncementConfig.AnnounceGuard.SuppressDisarmedOnce = true;
                try
                {
                    NukeFuseController.CancelPendingAt(msg.Location); // Clears NukeFlashBook + stops loop + prunes HUD list.
                    bt.SetBlock(msg.Location, BlockTypeEnum.Empty);   // Now cleanup won't announce "Disarmed".
                }
                finally
                {
                    AnnouncementConfig.AnnounceGuard.SuppressDisarmedOnce = false;
                }

                CastleMinerZGame.Instance.GameScreen?.RemoveExplosiveFlashModel(msg.Location);
                if (msg.OriginalExplosion)
                    Explosive.AddEffects(msg.Location, true);

                // Announce the detonation.
                if (isNukeHere)
                    AnnouncementConfig.Announce(AnnouncementConfig.MessageType.Detonated, msg.Location);

                // TEMPORARILY widen damage / kill radii for this one nuke splash.
                float oldDmg = 0, oldKill = 0; bool widened = false;
                if (isNukeHere && dmgRanges != null && killRange != null)
                {
                    oldDmg = dmgRanges[idx];
                    oldKill = killRange[idx];
                    dmgRanges[idx] = NukeRegistry.NUKE_DMG_RADIUS;
                    killRange[idx] = NukeRegistry.NUKE_KILL_RADIUS;
                    widened = true;
                }

                // Local-player splash damage (vanilla private static) - now uses our widened tables.
                if (CastleMinerZGame.Instance.LocalPlayer != null &&
                    CastleMinerZGame.Instance.LocalPlayer.ValidLivingGamer)
                {
                    var splash = AccessTools.Method(
                        typeof(Explosive),
                        "ApplySplashDamageToLocalPlayerAndZombies",
                        new[] { typeof(Vector3), typeof(ExplosiveTypes), typeof(InventoryItemIDs), typeof(byte) });

                    splash?.Invoke(null, new object[]
                    {
                        (Vector3)msg.Location,
                        msg.ExplosiveType,
                        (msg.ExplosiveType == ExplosiveTypes.TNT) ? InventoryItemIDs.TNT : InventoryItemIDs.C4,
                        msg.Sender.Id
                    });
                }

                // Restore the vanilla damage / kill radii immediately after the splash call.
                if (widened)
                {
                    dmgRanges[idx] = oldDmg;
                    killRange[idx] = oldKill;
                }

                if (msg.OriginalExplosion && isLocalSender)
                {
                    AsyncExplosionManager.Enqueue(
                        center:    msg.Location,
                        type:      msg.ExplosiveType,
                        radius:    baseRadius,      // Already boosted to NUKE when isNukeHere.
                        nukeAware: isNukeHere);
                }

                // Skip original (we handled it).
                return false;
            }
        }
        #endregion

        #region Harmony: Per-Frame Pump (Drives Paced Application)

        /// <summary>
        /// Each frame, apply a bounded chunk of pending explosion edits (to avoid frame spikes).
        /// Also triggers daisy-chaining by sending detonation messages when nuke voxels are hit.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "Update")]
        static class Patch_DNAGame_Update_PumpAsyncExplosions
        {
            static void Postfix(GameTime gameTime)
            {
                if (!AsyncExplosionManagerConfig.Enabled) return;
                AsyncExplosionManager.PumpApply(
                    maxBlocksPerFrame: AsyncExplosionManagerConfig.MaxBlocksPerFrame,
                    onNukeVoxelHit: pos =>
                    {
                        // IMPORTANT: Do NOT NukeMap.Remove(pos) here.
                        // Let the detonation Prefix see the mark and set NUKE radius,
                        // and then the Postfix/cleanup can remove it.

                        DetonateExplosiveMessage.Send(
                            CastleMinerZGame.Instance.MyNetworkGamer,
                            pos,
                            true,
                            ExplosiveTypes.C4);
                    });
            }
        }
        #endregion

        #endregion

        #region Nuke Crafting & UI Text Injectors

        #region Nuke Recipe Injection

        /// <summary>
        /// -----------------------------------------------------------------------------
        /// NUKE RECIPE INJECTION (what / why)
        /// - We add a custom crafting recipe for the Nuke item into the game's vanilla
        ///   Recipe.CookBook at runtime (no need to edit the game's Recipe.cs).
        /// - The recipe is config-driven and safe to re-run: we remove the previous
        ///   recipe first, then insert the updated one.
        /// - We intentionally delay adding until the Nuke item class is registered,
        ///   otherwise InventoryItem.CreateItem(...) can throw.
        /// -----------------------------------------------------------------------------
        /// </summary>

        /// <summary>
        /// Adds/updates the Nuke crafting recipe in the vanilla cookbook.
        /// Safe to call multiple times; it removes any prior Nuke recipe(s) first.
        /// </summary>
        internal static class NukeRecipeInjector
        {
            /// <summary>
            /// Tracks the last Nuke ID we applied a recipe for.
            /// This matters if the mod re-registers the Nuke under a different ID
            /// (or the user changes config) so we can remove the stale recipe too.
            /// </summary>
            private static int _lastAppliedNukeId = int.MinValue;

            /// <summary>
            /// "Signature" string representing the last applied recipe config.
            /// If nothing changed, we skip work to avoid repeatedly touching the cookbook.
            /// </summary>
            private static string _lastAppliedSig;

            /// <summary>
            /// Lazily-built lookup table to resolve user-friendly ingredient names
            /// (spaces/underscores ignored) into InventoryItemIDs.
            /// </summary>
            private static Dictionary<string, InventoryItemIDs> _idLookup;

            /// <summary>
            /// Applies (or updates) the recipe based on <see cref="NukeRecipeConfig"/>.
            /// If disabled, removes any existing Nuke recipe and exits.
            /// </summary>
            public static void ApplyOrUpdate()
            {
                try
                {
                    int nukeId = NukeRegistry.NukeId;

                    // Disabled -> remove and bail.
                    // Summary: Keeps the cookbook clean when the feature is turned off.
                    if (!NukeRecipeConfig.Enabled)
                    {
                        if (nukeId >= 0) RemoveRecipesFor((InventoryItemIDs)nukeId);
                        if (_lastAppliedNukeId != int.MinValue && _lastAppliedNukeId != nukeId)
                            RemoveRecipesFor((InventoryItemIDs)_lastAppliedNukeId);

                        _lastAppliedSig    = null;
                        _lastAppliedNukeId = int.MinValue;
                        return;
                    }

                    // Need a valid output id.
                    // Summary: If our Nuke ID isn't assigned yet, we can't build a recipe result.
                    if (nukeId < 0) return;

                    // We can't add a recipe for an item class that isn't registered yet.
                    // Summary: Prevents CreateItem from throwing before the mod registers the surrogate class.
                    if (!IsRegistered((InventoryItemIDs)nukeId))
                        return;

                    // Build a "changed?" signature so repeated calls are cheap.
                    // Summary: Avoids re-adding the same recipe every frame / reload hook.
                    string sig =
                        nukeId.ToString()                         + "|" +
                        NukeRecipeConfig.OutputCount.ToString()   + "|" +
                        NukeRecipeConfig.InsertAfterC4.ToString() + "|" +
                        (NukeRecipeConfig.Ingredients ?? "");

                    if (sig == _lastAppliedSig)
                        return;

                    // Remove any existing recipes for current and previous IDs.
                    // Summary: Guarantees we never accumulate duplicates after hot reloads.
                    RemoveRecipesFor((InventoryItemIDs)nukeId);
                    if (_lastAppliedNukeId != int.MinValue && _lastAppliedNukeId != nukeId)
                        RemoveRecipesFor((InventoryItemIDs)_lastAppliedNukeId);

                    // Parse ingredient list from config text.
                    // Summary: Only valid, known items with positive counts are kept.
                    var ingredients = ParseIngredients(NukeRecipeConfig.Ingredients);
                    if (ingredients.Count == 0)
                    {
                        Log("[Nuke] Recipe not added: no valid ingredients.");
                        _lastAppliedSig = sig;
                        _lastAppliedNukeId = nukeId;
                        return;
                    }

                    // Build the recipe result item.
                    int outCount = Math.Max(1, NukeRecipeConfig.OutputCount);
                    var result = InventoryItem.CreateItem((InventoryItemIDs)nukeId, outCount);

                    // Create an Explosives-tab recipe entry.
                    var recipe = new Recipe(Recipe.RecipeTypes.Explosives, result, ingredients.ToArray());

                    var book = Recipe.CookBook;
                    if (book == null)
                    {
                        Log("[Nuke] Recipe not added: Recipe.CookBook was null.");
                        _lastAppliedSig = sig;
                        _lastAppliedNukeId = nukeId;
                        return;
                    }

                    // Insert after the vanilla C4 recipe (Explosives) if requested.
                    // Summary: Keeps the Nuke recipe in a sensible spot in the craft list.
                    int insertAt = -1;
                    if (NukeRecipeConfig.InsertAfterC4)
                    {
                        for (int i = 0; i < book.Count; i++)
                        {
                            var r = book[i];
                            if (r == null || r.Type != Recipe.RecipeTypes.Explosives) continue;

                            var res = r.Result;
                            if (res?.ItemClass?.ID == InventoryItemIDs.C4)
                            {
                                insertAt = i + 1;
                                break;
                            }
                        }
                    }

                    if (insertAt >= 0 && insertAt <= book.Count)
                        book.Insert(insertAt, recipe);
                    else
                        book.Add(recipe);

                    _lastAppliedSig    = sig;
                    _lastAppliedNukeId = nukeId;

                    Log("[Nuke] Added/updated crafting recipe (Explosives tab).");
                }
                catch (Exception ex)
                {
                    Log($"[Nuke] Failed to add/update recipe: {ex.Message}.");
                }
            }

            /// <summary>
            /// Removes any Explosives-tab recipes that output the given item ID.
            /// Summary: Our "de-dupe" + "disable feature" cleanup path.
            /// </summary>
            private static void RemoveRecipesFor(InventoryItemIDs id)
            {
                try
                {
                    var book = Recipe.CookBook;
                    if (book == null) return;

                    for (int i = book.Count - 1; i >= 0; i--)
                    {
                        var r = book[i];
                        if (r == null || r.Type != Recipe.RecipeTypes.Explosives) continue;

                        var resId = r.Result?.ItemClass?.ID;
                        if (resId == id)
                            book.RemoveAt(i);
                    }
                }
                catch { /* Best-effort. */ }
            }

            /// <summary>
            /// Returns true if the item class for this ID is registered/constructible.
            /// Summary: Protects recipe creation from calling CreateItem on an unregistered surrogate.
            /// </summary>
            private static bool IsRegistered(InventoryItemIDs id)
            {
                // Safe check: GetClass returns null when the ID isn't registered.
                return InventoryItem.GetClass(id) != null;
            }

            /// <summary>
            /// Parses a config string like:
            ///   "ExplosivePowder:30, C4:30, TNT:30"
            /// Supports ':', '=', '*', or "x" as separators.
            /// Summary: Turns user text into real InventoryItem ingredient instances.
            /// </summary>
            private static List<InventoryItem> ParseIngredients(string text)
            {
                var list = new List<InventoryItem>();
                if (string.IsNullOrWhiteSpace(text)) return list;

                foreach (var raw in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var token = raw.Trim();
                    if (token.Length == 0) continue;

                    string namePart;
                    string countPart;

                    int sep = token.IndexOf(':');
                    if (sep < 0) sep = token.IndexOf('=');
                    if (sep < 0) sep = token.IndexOf('*');

                    if (sep >= 0)
                    {
                        namePart  = token.Substring(0, sep).Trim();
                        countPart = token.Substring(sep + 1).Trim();
                    }
                    else
                    {
                        // Allow "Item x 30"
                        var parts = token.Split(new[] { 'x', 'X' }, 2);
                        if (parts.Length == 2)
                        {
                            namePart  = parts[0].Trim();
                            countPart = parts[1].Trim();
                        }
                        else
                        {
                            Log($"[Nuke] Recipe ingredient ignored (missing count): \"{token}\".");
                            continue;
                        }
                    }

                    // Summary: non-positive counts let you "comment out" entries without deleting them.
                    if (!int.TryParse(countPart, out var count) || count <= 0)
                        continue;

                    // Summary: resolves enum name / normalized name / numeric ID into an InventoryItemIDs value.
                    if (!TryParseInventoryItemId(namePart, out var id))
                    {
                        Log($"[Nuke] Recipe ingredient ignored (unknown item): \"{namePart}\".");
                        continue;
                    }

                    try
                    {
                        list.Add(InventoryItem.CreateItem(id, count));
                    }
                    catch
                    {
                        Log($"[Nuke] Recipe ingredient ignored (CreateItem failed): {id} x{count}.");
                    }
                }

                return list;
            }

            /// <summary>
            /// Converts a user-provided string into an InventoryItemIDs value.
            /// Accepts numeric IDs, enum names, or normalized names (spaces/underscores ignored).
            /// </summary>
            private static bool TryParseInventoryItemId(string raw, out InventoryItemIDs id)
            {
                id = default;
                if (string.IsNullOrWhiteSpace(raw)) return false;

                string s = raw.Trim();

                // Numeric.
                if (int.TryParse(s, out var i))
                {
                    id = (InventoryItemIDs)i;
                    return true;
                }

                // Enum name (case-insensitive).
                if (Enum.TryParse(s, true, out id))
                    return true;

                EnsureLookup();
                return _idLookup.TryGetValue(Normalize(s), out id);
            }

            /// <summary>
            /// Builds a normalization map of every InventoryItemIDs enum name so we can match:
            ///  "Explosive Powder" -> ExplosivePowder
            ///  "space_rock_inventory" -> SpaceRockInventory
            /// Summary: makes configs forgiving for humans.
            /// </summary>
            private static void EnsureLookup()
            {
                if (_idLookup != null) return;

                _idLookup = new Dictionary<string, InventoryItemIDs>(StringComparer.OrdinalIgnoreCase);
                foreach (InventoryItemIDs e in Enum.GetValues(typeof(InventoryItemIDs)))
                    _idLookup[Normalize(e.ToString())] = e;
            }

            /// <summary>
            /// Normalizes a string for lookup by stripping underscores/spaces and lowercasing.
            /// </summary>
            private static string Normalize(string s)
                => (s ?? string.Empty).Replace("_", "").Replace(" ", "").ToLowerInvariant();
        }
        #endregion

        #region Nuke Surrogate Block Name Injection

        /// <summary>
        /// -----------------------------------------------------------------------------
        /// NUKE SURROGATE BLOCK NAME (what / why)
        /// - The TextureSurrogate uses a *vanilla* BlockType (ex: Slime/Space Goo),
        ///   which means its display name defaults to the game's localized string
        ///   (ex: Strings.Space_Goo).
        /// - We optionally override BlockType.Name at runtime to show a custom label
        ///   (ex: "Nuke") without touching the game's BlockType table.
        /// - Blank config restores the original localized name.
        /// - We cache the default once so toggling/reloading config is reversible.
        /// -----------------------------------------------------------------------------
        /// </summary>
        internal static class NukeSurrogateNameInjector
        {
            /// <summary>
            /// Stores the original (vanilla/localized) name for each surrogate we touch,
            /// so we can revert cleanly when the config is blank or the surrogate changes.
            /// </summary>
            private static readonly Dictionary<BlockTypeEnum, string> _defaults =
                new Dictionary<BlockTypeEnum, string>();

            /// <summary>
            /// Tracks which surrogate block we last renamed.
            /// If the enum changes, we restore the previous one before applying the new.
            /// </summary>
            private static BlockTypeEnum _lastSurrogate = BlockTypeEnum.NumberOfBlocks;

            /// <summary>
            /// Applies the configured name override (or restores vanilla if blank).
            /// Safe to call multiple times (ex: on load + config reload).
            /// </summary>
            public static void ApplyOrUpdate()
            {
                var current = NukeRegistry.TextureSurrogate;

                // If the surrogate enum changed, restore the old one first.
                if (_lastSurrogate != BlockTypeEnum.NumberOfBlocks && _lastSurrogate != current)
                    Restore(_lastSurrogate);

                var bt = BlockType.GetType(current);
                if (bt == null) return;

                // Cache the vanilla/default name once (localized).
                if (!_defaults.ContainsKey(current))
                    _defaults[current] = bt.Name;

                var ov  = NukeConfig.TextureSurrogateName;

                // Blank => revert to default. Otherwise force override.
                bt.Name = string.IsNullOrWhiteSpace(ov) ? _defaults[current] : ov.Trim();

                _lastSurrogate = current;
            }

            /// <summary>
            /// Restores the original name for a previously-modified surrogate.
            /// Best-effort; if we never cached it, we do nothing.
            /// </summary>
            private static void Restore(BlockTypeEnum t)
            {
                if (!_defaults.TryGetValue(t, out var def)) return;
                var bt = BlockType.GetType(t);
                if (bt != null) bt.Name = def;
            }
        }
        #endregion

        #region Nuke Surrogate Inventory Description Injection

        /// <summary>
        /// -----------------------------------------------------------------------------
        /// NUKE SURROGATE ITEM DESCRIPTION (what / why)
        /// - The "Nuke" is implemented as an InventoryItemClass wrapping a vanilla
        ///   block (TextureSurrogate). The tooltip/description text is stored in the
        ///   item class (private field "_description").
        /// - We set this during initial registration, but we also support hot-reload:
        ///   when config changes, we patch the already-registered class in place.
        /// - Blank config restores the original description we captured the first time.
        /// -----------------------------------------------------------------------------
        /// </summary>
        internal static class NukeSurrogateDescriptionInjector
        {
            /// <summary>
            /// AccessTools field-ref to the private InventoryItemClass "_description" field.
            /// This lets us update the tooltip string without re-registering the class.
            /// </summary>
            private static readonly AccessTools.FieldRef<InventoryItem.InventoryItemClass, string>
                DescRef = AccessTools.FieldRefAccess<InventoryItem.InventoryItemClass, string>("_description");

            /// <summary>
            /// Original description captured once so we can revert when config is blank.
            /// </summary>
            private static string _defaultDesc;

            /// <summary>
            /// Guard so we only capture the default once (prevents "default" drifting).
            /// </summary>
            private static bool   _defaultCaptured;

            /// <summary>
            /// Applies the configured description override (or restores vanilla if blank).
            /// Safe to call repeatedly (ex: load + config reload).
            /// </summary>
            public static void ApplyOrUpdate()
            {
                int id = NukeRegistry.NukeId;
                if (id < 0) return;

                var klass = InventoryItem.GetClass((InventoryItemIDs)id);
                if (klass == null) return;

                if (!_defaultCaptured)
                {
                    _defaultDesc     = DescRef(klass);
                    _defaultCaptured = true;
                }

                var ov = NukeConfig.TextureSurrogateDescription;

                // Blank => revert to original/default description.
                DescRef(klass) = string.IsNullOrWhiteSpace(ov) ? _defaultDesc : ov.Trim();
            }
        }
        #endregion

        #endregion

        #endregion
    }
}
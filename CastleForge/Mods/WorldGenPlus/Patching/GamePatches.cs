/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060            // Silence IDE0060.
using DNA.CastleMinerZ.Terrain.WorldBuilders;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;                             // Harmony patching library.
using System;
using DNA;

using static ModLoader.LogSystem;             // For Log(...).

namespace WorldGenPlus
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
        private static string  _harmonyId;

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
            _harmony   = new Harmony(_harmonyId);                                      // Create & store the Harmony instance.

            // Choose which assembly to scan for patch classes.
            // If you split patches across multiple assemblies, call this routine for each assembly.
            Assembly asm = typeof(GamePatches).Assembly;

            int successCount = 0;
            int failCount    = 0;

            // Enumerate every class that has at least one [HarmonyPatch] attribute,
            // and patch it independently (best-effort).
            foreach (var patchType in EnumeratePatchTypes(asm))
            {
                try
                {
                    // Create a processor for this patch class and apply all of its prefixes/postfixes/transpilers.
                    var proc    = _harmony.CreateClassProcessor(patchType);
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

            // Install NetSync patches with the SAME Harmony instance/ID.
            int netSyncCount = 0;
            try
            {
                netSyncCount = WorldGenPlusNetSync.ApplyPatches(_harmony);
                Log($"[Harmony] NetSync patches applied ({netSyncCount} target(s)).");
            }
            catch (Exception ex)
            {
                Log($"[Harmony] NetSync patching FAILED: {ex.GetType().Name}: {ex.Message}.");
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
                var prefixes    = Filter(info.Prefixes).ToList();
                var postfixes   = Filter(info.Postfixes).ToList();
                var transpilers = Filter(info.Transpilers).ToList();

                // If nothing remains (all were silent), don't log this method at all.
                if (prefixes.Count == 0 && postfixes.Count == 0 && transpilers.Count == 0) continue;

                // Show filtered counts (not the raw/total counts).
                Log($"[Harmony] Patched method: {Describe(m)} | " +
                    $"[Prefixes={prefixes.Count}] [Postfixes={postfixes.Count}] [Transpilers={transpilers.Count}].");

                foreach (var p in prefixes)    Log($"  • Prefix    : {Describe(p.PatchMethod)}.");
                foreach (var p in postfixes)   Log($"  • Postfix   : {Describe(p.PatchMethod)}.");
                foreach (var p in transpilers) Log($"  • Transpiler: {Describe(p.PatchMethod)}.");
            }

            Log($"[Harmony] Patching complete. Success={successCount}, Failed={failCount}, MethodsPatchedByUs={ours.Count}, NetSyncTargets={netSyncCount}.");
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
        [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
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
                                               a.GetType().Name     == "HarmonyPatch"));
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

        /// <summary>
        /// WorldGenPlus - Harmony patch pack.
        /// Summary:
        /// - BlockTerrain.Init:      Swaps the WorldBuilder to WorldGenPlusBuilder when enabled.
        /// - ChooseSavedWorldScreen: Injects "Custom Biomes" UI button and maintains layout.
        /// - Boot/Seed fixes:        Forces terrain reload in startWorld and applies seeded world creation/seed override.
        /// </summary>

        #region BlockTerrain: Swap World Builder When Enabled (Host + Synced Client)

        /// <summary>
        /// Patch: BlockTerrain.Init
        /// Summary:
        /// - Host: Reads WGConfig, snapshots settings, applies seed override (best-effort),
        ///   then swaps the internal _worldBuilder field to WorldGenPlusBuilder.
        /// - Client: Uses WGP only if a host override was received (and user allows syncing).
        /// - Uses __state to carry the snapshot from Prefix -> Postfix without recomputing.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), nameof(BlockTerrain.Init))]
        internal static class Patch_BlockTerrain_Init_WorldGenPlus
        {
            private static readonly AccessTools.FieldRef<BlockTerrain, WorldBuilder> WorldBuilderRef =
                AccessTools.FieldRefAccess<BlockTerrain, WorldBuilder>("_worldBuilder");

            /// <summary>
            /// Determines whether a non-host client should use the host-provided override.
            /// Summary: Requires both "sync enabled" and "override present".
            /// </summary>
            private static bool ClientShouldUseHostOverride()
            {
                // Client only uses WGP if:
                //  - user allows syncing
                //  - AND we actually received a host override (trailer)
                if (!WGConfig.Current.Multiplayer_SyncFromHost) return false;
                if (!WGConfig.HasNetworkOverride) return false;
                return true;
            }

            private static void Prefix(WorldInfo worldInfo, bool host, ref WorldGenPlusSettings __state)
            {
                __state = null;
                if (worldInfo == null) return;

                // Ensure config is loaded (do NOT call LoadOrCreate() and ignore its return).
                WGConfig.Load();

                var snap = WGConfig.Snapshot();
                if (snap == null) return;

                if (host)
                {
                    // Host path: local config is authoritative.
                    if (!snap.Enabled) return;

                    // Host-only seed override (your other seed patches are host-gated already; this is fine).
                    WGConfig.TryApplySeedOverride(worldInfo);

                    __state = snap;
                    return;
                }

                // Client path: ONLY use WGP if host override was received.
                if (!ClientShouldUseHostOverride()) return;

                // snap is already the host's settings because ApplyNetworkOverride overwrote _current in-memory.
                if (!snap.Enabled) return;

                __state = snap;
            }

            private static void Postfix(BlockTerrain __instance, WorldInfo worldInfo, ref WorldGenPlusSettings __state)
            {
                if (__instance == null) return;
                if (worldInfo == null) return;
                if (__state == null || !__state.Enabled) return;

                try
                {
                    WorldBuilderRef(__instance) = new WorldGenPlusBuilder(worldInfo, __state);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Builder swap failed: {ex}.");
                }
            }
        }
        #endregion

        #region ChooseSavedWorldScreen: Inject "Custom Biomes" Button (Host Online / Saved Worlds)

        /// <summary>
        /// Button label used for the injected control on ChooseSavedWorldScreen.
        /// </summary>
        private const string SavedButtonText = "Custom Biomes";

        /// <summary>
        /// Cache: Screen instance -> injected button (so we don't re-add every time).
        /// Note:  ConditionalWeakTable avoids keeping screens alive if they get GC'd.
        /// </summary>
        private static readonly ConditionalWeakTable<object, FrameButtonControl> _savedCustomBtns
            = new ConditionalWeakTable<object, FrameButtonControl>();

        /// <summary>
        /// Creates (or returns) the injected "Custom Biomes" button on ChooseSavedWorldScreen.
        /// Summary:
        /// - Uses the game's runtime "_renameButton" instance as the style/template.
        /// - Adds a Pressed handler that pushes <see cref="WorldGenPlusConfigScreen"/>.
        /// - Leaves layout finalization to the OnUpdate patch (handles resolution/scale changes).
        /// </summary>
        private static FrameButtonControl GetOrCreateSavedCustomButton(object inst)
        {
            if (inst == null) return null;

            if (_savedCustomBtns.TryGetValue(inst, out var cached) && cached != null)
                return cached;

            // ChooseSavedWorldScreen derives from ScrollingListScreen.
            if (!(inst is ScrollingListScreen screen))
                return null;

            var t = inst.GetType();

            // Use the game's existing button as a style template (Rename is a FrameButtonControl at runtime).
            if (!(AccessTools.Field(t, "_renameButton")?.GetValue(inst) is FrameButtonControl rename)) return null;

            // If something already added it (older build), reuse it.
            foreach (var c in screen.Controls)
                if (c is FrameButtonControl fb && string.Equals(fb.Text, SavedButtonText, StringComparison.Ordinal))
                    return fb;

            int pad = (int)(5f * Screen.Adjuster.ScaleFactor.X);

            var btn = new FrameButtonControl
            {
                Text          = SavedButtonText,
                Size          = rename.Size,
                Font          = rename.Font,
                Frame         = rename.Frame,
                ButtonColor   = rename.ButtonColor,
                TextAlignment = rename.TextAlignment,
                Scale         = rename.Scale,

                // Temporary position; OnUpdate patch will finalize.
                LocalPosition = new Point(rename.LocalPosition.X, rename.LocalPosition.Y + rename.Size.Height + pad),
            };

            btn.Pressed += (s, e) =>
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.FrontEnd?._uiGroup != null)
                    gm.FrontEnd._uiGroup.PushScreen(new WorldGenPlusConfigScreen(gm));
            };

            screen.Controls.Add(btn);
            _savedCustomBtns.Add(inst, btn);
            return btn;
        }

        /// <summary>
        /// Patch: ChooseSavedWorldScreen..ctor
        /// Summary: Ensure config exists and inject the button once the screen is constructed.
        /// </summary>
        [HarmonyPatch]
        private static class Patch_ChooseSavedWorldScreen_Ctor_AddCustomBiomesButton
        {
            private static MethodBase TargetMethod()
            {
                // IMPORTANT: ChooseSavedWorldScreen ctor is parameterless.
                var t = typeof(CastleMinerZGame).Assembly.GetType("DNA.CastleMinerZ.UI.ChooseSavedWorldScreen", throwOnError: false);
                return AccessTools.Constructor(t, Type.EmptyTypes);
            }

            private static void Postfix(object __instance)
            {
                try
                {
                    WGConfig.LoadOrCreate();
                    GetOrCreateSavedCustomButton(__instance);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to inject ChooseSavedWorldScreen button: {ex}.");
                }
            }
        }

        /// <summary>
        /// Patch: ChooseSavedWorldScreen.OnUpdate
        /// Summary:
        /// - Finalizes position/sizing every frame (safe against resolution/scale changes).
        /// - Inserts our button between Rename and Erase, then moves Erase down.
        /// Note: Silent catch because this runs every frame.
        /// </summary>
        [HarmonyPatch]
        private static class Patch_ChooseSavedWorldScreen_OnUpdate_PositionCustomButton
        {
            private static MethodBase TargetMethod()
            {
                var t = typeof(CastleMinerZGame).Assembly.GetType("DNA.CastleMinerZ.UI.ChooseSavedWorldScreen", throwOnError: false);
                return AccessTools.Method(t, "OnUpdate", new[] { typeof(DNAGame), typeof(GameTime) });
            }

            private static void Postfix(object __instance)
            {
                try
                {
                    var custom = GetOrCreateSavedCustomButton(__instance);
                    if (custom == null) return;

                    var t = __instance.GetType();

                    if (!(AccessTools.Field(t, "_renameButton")?.GetValue(__instance) is FrameButtonControl rename) || !(AccessTools.Field(t, "_eraseSaves")?.GetValue(__instance) is FrameButtonControl erase)) return;

                    // Mirror the template button's SCALE (text + frame rendering depends on Scale).
                    float s = rename.Scale;
                    if (s <= 0f) s = 1f;

                    // IMPORTANT: FrameButtonControl.Size is scaled (returns _size * Scale).
                    // To match rename exactly, set:
                    //   custom.Scale = rename.Scale.
                    //   custom._size (via setter) = rename.Size / rename.Scale.
                    var rs       = rename.Size; // Scaled size.
                    custom.Scale = s;
                    custom.Size  = new Size(
                        (int)Math.Round(rs.Width / s),
                        (int)Math.Round(rs.Height / s));

                    // Keep style consistent.
                    custom.Font          = rename.Font;
                    custom.Frame         = rename.Frame;
                    custom.ButtonColor   = rename.ButtonColor;
                    custom.TextAlignment = rename.TextAlignment;

                    // Spacing: do NOT derive from erase (because we move erase).
                    // Use rename's height + a scaled padding.
                    int pad   = (int)Math.Round(5f * s);
                    int stepY = rename.Size.Height + pad; // Rename.Size.Height is scaled already.

                    // Insert custom between Rename and Erase.
                    custom.LocalPosition = new Point(rename.LocalPosition.X, rename.LocalPosition.Y + stepY);
                    erase.LocalPosition  = new Point(rename.LocalPosition.X, rename.LocalPosition.Y + stepY + stepY);
                }
                catch { /* Silent; runs every frame. */ }
            }
        }
        #endregion

        #region Boot / Seed Override / Force Reload Fixes

        /// <summary>
        /// Fix pack:
        /// Summary:
        /// A) "Needs two reloads": startWorld() only reloads terrain if terrainVersion changes. Force a mismatch first.
        /// B) Seed override:
        ///    - BeginLoadTerrain(null, host) creates a random-seed world. Replace with CreateNewWorld(null, seed).
        ///    - When info != null, set WorldInfo's private _seed directly (Seed property is not settable).
        /// Also patches the "allowed" flow so we only override seed during preloaded/new-world paths, not arbitrary loaded worlds.
        /// </summary>
        [HarmonyPatch]
        internal static class WorldGenPlusBootPatches
        {
            #region Shared Helpers

            private static readonly FieldInfo SeedField = AccessTools.Field(typeof(WorldInfo), "_seed");

            private sealed class Marker { }

            // Marks WorldInfo instances where we allow seed override (preloaded/new-world flow).
            private static readonly ConditionalWeakTable<WorldInfo, Marker> SeedOverrideAllowed =
                new ConditionalWeakTable<WorldInfo, Marker>();

            /// <summary>
            /// Marks a WorldInfo instance as eligible for seed overriding (preloaded/new-world flow only).
            /// </summary>
            private static void MarkSeedOverrideAllowed(WorldInfo wi)
            {
                if (wi == null) return;
                try { SeedOverrideAllowed.GetValue(wi, _ => new Marker()); } catch { /* Ignore. */ }
            }

            /// <summary>
            /// Returns true if a WorldInfo instance was previously marked as seed-override eligible.
            /// </summary>
            private static bool IsSeedOverrideAllowed(WorldInfo wi)
            {
                if (wi == null) return false;
                try { return SeedOverrideAllowed.TryGetValue(wi, out _); }
                catch { return false; }
            }

            /// <summary>
            /// Best-effort setter for WorldInfo._seed (private field).
            /// Note: The game stores seed in _seed; the public Seed property is not settable.
            /// </summary>
            private static void TrySetSeed(WorldInfo wi, int seed)
            {
                if (wi == null) return;
                if (SeedField == null) return;

                try { SeedField.SetValue(wi, seed); }
                catch { /* Ignore. */ }
            }
            #endregion

            #region Force A Terrain Reload When Starting A World

            // ============================================================
            // Force a terrain reload when starting a world.
            // ============================================================
            //
            // FrontEndScreen.startWorld():
            //   previous = CurrentWorld._terrainVersion;
            //   CurrentWorld._terrainVersion = CastleMinerZ;
            //   if (previous != CurrentWorld._terrainVersion) BeginLoadTerrain(CurrentWorld, true);
            //
            // If it was already CastleMinerZ, the if is false and it reuses the preloaded world.
            //
            // ============================================================

            [HarmonyPatch(typeof(FrontEndScreen), "startWorld")]
            [HarmonyPrefix]
            private static void FrontEndScreen_startWorld_Prefix_ForceReload(FrontEndScreen __instance)
            {
                try
                {
                    var game = AccessTools.Field(typeof(FrontEndScreen), "_game")?.GetValue(__instance) as CastleMinerZGame;
                    var wi = game?.CurrentWorld;
                    if (wi == null) return;

                    // Ensure previous != CastleMinerZ when startWorld captures it.
                    wi._terrainVersion = (WorldTypeIDs)(-1);

                    // If SeedOverride is enabled, allow it for this world instance (preloaded flow).
                    // (BeginLoadTerrain will receive this same WorldInfo reference.)
                    var snap = WGConfig.Snapshot();

                    // Uncomment to make "Seed Override" tied to "Enabled" (remove the code below).
                    // if (snap != null && snap.Enabled && snap.SeedOverrideEnabled)
                    //     MarkSeedOverrideAllowed(wi);

                    if (snap != null && snap.SeedOverrideEnabled)
                        MarkSeedOverrideAllowed(wi);
                }
                catch
                {
                    // Swallow - avoid blocking startWorld.
                }
            }
            #endregion

            #region Apply SeedOverride At BeginLoadTerrain (Host Only)

            // ============================================================
            // Apply SeedOverride at BeginLoadTerrain (host only).
            // ============================================================
            //
            // CastleMinerZGame.BeginLoadTerrain():
            //   if (info == null) CurrentWorld = WorldInfo.CreateNewWorld(null); // Random seed.
            //
            // WorldInfo has an overload CreateNewWorld(null, seed).
            //
            // ============================================================

            [HarmonyPatch(typeof(CastleMinerZGame), nameof(CastleMinerZGame.BeginLoadTerrain))]
            [HarmonyPrefix]
            private static void CastleMinerZGame_BeginLoadTerrain_Prefix_SeedOverride(ref WorldInfo info, bool host)
            {
                // Never touch client-side loads (join path uses host=false).
                if (!host) return;

                WorldGenPlusSettings snap;
                try { snap = WGConfig.Snapshot(); }
                catch { return; }

                // Uncomment to make "Seed Override" tied to "Enabled".
                // if (!snap.Enabled) return;
                if (snap == null || !snap.SeedOverrideEnabled) return;

                int seed = snap.SeedOverride;

                try
                {
                    if (info == null)
                    {
                        // New/random world path -> create seeded.
                        info = WorldInfo.CreateNewWorld(null, seed);

                        // Optional, only if other code later needs to know it was "allowed".
                        MarkSeedOverrideAllowed(info);
                        return;
                    }

                    // Only override seeds on preloaded/new-world flow (NOT arbitrary loaded worlds).
                    if (!IsSeedOverrideAllowed(info))
                        return;

                    TrySetSeed(info, seed);
                }
                catch
                {
                    // Swallow.
                }
            }
            #endregion
        }
        #endregion

        #endregion
    }
}
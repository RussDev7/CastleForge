/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060      // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using System.Globalization;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Resources;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).

namespace RenderDistancePlus
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

        /*
            Extended Draw Distance + UI Slider (GraphicsTab) - Harmony Patch Pack

            Purpose:
            - Allow draw distance beyond vanilla "Ultra" by removing BlockTerrain's 0..1 clamp.
            - Drive terrain draw distance from the saved PlayerStats.DrawDistance (render-setting),
              using an Option-B mapping: terrain.DrawDistance = renderSetting / 12f.
            - Replace the in-game GraphicsTab view distance dropdown with a TrackBar that exposes
              10 distinct steps (collapsing duplicates) while still saving the underlying render-setting
              values into the vanilla stats file.
            - Prevent the hidden vanilla DropList from overwriting the extended value (0..4 tiers).

            Notes:
            - This code intentionally stores the real render-setting in PlayerStats.DrawDistance:
                1,2,3,5,6,8,9,11,12,14  (step 0..9)
              and derives the actual terrain scalar each frame via UiMax (12f).
            - If you increase UiMax / mapping behavior, keep RenderSettingSnapper consistent.
            - The optional radius-order offsets patch allows the renderer to attempt farther candidates,
              but cache/engine limits may still cap practical rendering.
        */

        #region Config

        /// <summary>
        /// Central knobs for the extended draw distance system.
        /// Summary:
        /// - Enabled: master kill-switch for all patches.
        /// - UiMax: Option-B mapping divisor used in PreDrawMain (renderSetting / UiMax).
        /// - MaxChunkRadius: safety cap for computed radius (prevents overflow / excessive loops).
        /// - RadiusOrderLimit: controls BuildRadiusOrderOffsets range (bigger => more candidate offsets).
        /// </summary>
        /// <remarks>
        /// IMPORTANT:
        /// - UiMax is used by <see cref="GameScreen_PreDrawMain_DrawDistanceOverride"/>.
        /// - RadiusOrderLimit must be >= 14 if you want render-setting 14 to include +13 offsets
        ///   (because loops are -limit .. limit-1).
        /// </remarks>
        internal static class ExtendedDrawDistanceConfig
        {
            /// <summary>
            /// Master switch for the extended draw distance patches.
            /// </summary>
            public static bool Enabled = true;

            /// <summary>
            /// Option-B mapping divisor:
            /// terrain.DrawDistance = renderSetting / UiMax.
            /// </summary>
            public const float UiMax = 12f;

            /// <summary>
            /// Safety cap on the computed chunk radius (i = floor(8*toset)+4).
            /// </summary>
            public static int MaxChunkRadius = 64;

            /// <summary>
            /// Radius-order offset limit (vanilla is 13).
            /// If you want render-setting 14 to reach +13 offsets, ensure this is >= 14.
            /// </summary>
            public static int RadiusOrderLimit = 14;
        }
        #endregion

        #region Patch: BlockTerrain.set_DrawDistance (Remove Clamp)

        /// <summary>
        /// Removes the 0..1 clamp from <see cref="BlockTerrain.DrawDistance"/> setter.
        /// Summary:
        /// - Vanilla computes farthest draw distance squared from Clamp(0..1),
        ///   so values above 1.0 show up in the backing field but do not increase range.
        /// - This prefix recomputes the internal radius using the raw value (allowing > 1.0)
        ///   and updates the backing fields directly.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - We keep the vanilla radius formula:
        ///     i = floor(8 * toset) + 4
        /// - We apply safety caps to avoid overflow and absurd values.
        /// - Returning false skips the original clamped setter.
        /// </remarks>
        [HarmonyPatch(typeof(BlockTerrain), "set_DrawDistance")]
        internal static class BlockTerrain_set_DrawDistance_Unclamp
        {
            private static readonly AccessTools.FieldRef<BlockTerrain, float> _drawDistance =
                AccessTools.FieldRefAccess<BlockTerrain, float>("_drawDistance");

            private static readonly AccessTools.FieldRef<BlockTerrain, int> _farthestDrawDistanceSQ =
                AccessTools.FieldRefAccess<BlockTerrain, int>("_farthestDrawDistanceSQ");

            private const int MaxSafeI = 46340; // sqrt(int.MaxValue).

            private static bool Prefix(BlockTerrain __instance, float value)
            {
                if (!ExtendedDrawDistanceConfig.Enabled)
                    return true;

                float toset = value;
                if (float.IsNaN(toset) || float.IsInfinity(toset)) toset = 0f;
                if (toset < 0f) toset = 0f;

                int i = (int)Math.Floor(8f * toset) + 4;
                if (i < 0) i = 0;

                if (ExtendedDrawDistanceConfig.MaxChunkRadius > 0 && i > ExtendedDrawDistanceConfig.MaxChunkRadius)
                    i = ExtendedDrawDistanceConfig.MaxChunkRadius;

                if (i > MaxSafeI) i = MaxSafeI;

                _farthestDrawDistanceSQ(__instance) = i * i;
                _drawDistance(__instance) = toset;

                return false;
            }
        }
        #endregion

        #region Patch: GameScreen.PreDrawMain (Drive Terrain Distance From Render-Setting)

        /// <summary>
        /// Ensures terrain draw distance is driven from the saved render-setting each frame.
        /// Summary:
        /// - Vanilla assigns terrain.DrawDistance from PlayerStats.DrawDistance in PreDrawMain.
        /// - We re-apply our Option-B mapping after vanilla runs:
        ///     terrain.DrawDistance = renderSetting / UiMax
        /// - The unclamped setter patch makes values > 1.0 meaningful.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - We "snap" unknown values down to the nearest supported render-setting so
        ///   accidental saves / older configs do not create out-of-range UI issues.
        /// - This is also why "setting DrawDistance to 10" can appear to stick in the property,
        ///   but not increase render distance in vanilla (because vanilla clamps SQ calculation).
        /// </remarks>
        [HarmonyPatch(typeof(GameScreen), "PreDrawMain")]
        internal static class GameScreen_PreDrawMain_DrawDistanceOverride
        {
            private static readonly AccessTools.FieldRef<GameScreen, BlockTerrain> _terrain =
                AccessTools.FieldRefAccess<GameScreen, BlockTerrain>("_terrain");

            private static void Postfix(GameScreen __instance)
            {
                if (!ExtendedDrawDistanceConfig.Enabled)
                    return;

                BlockTerrain terrain = _terrain(__instance);
                if (terrain == null) return;

                int renderSetting = CastleMinerZGame.Instance.PlayerStats.DrawDistance;

                // Keep it sane; we'll also "snap" unknown values down to the nearest supported one.
                renderSetting = RenderSettingSnapper.SnapRenderSetting(renderSetting);

                // Option B mapping (NO clamp to 1.0f, unclamped setter handles >1):
                terrain.DrawDistance = renderSetting / ExtendedDrawDistanceConfig.UiMax;
            }
        }
        #endregion

        #region Patch: GraphicsTab.OnSelected (Prevent Vanilla SelectedIndex Crash)

        /// <summary>
        /// Makes vanilla GraphicsTab.OnSelected safe when PlayerStats.DrawDistance contains extended values.
        /// Summary:
        /// - Vanilla does: _viewDistanceDropList.SelectedIndex = PlayerStats.DrawDistance
        /// - When DrawDistance is 11/12/14 etc, vanilla would crash (index out of range).
        /// - This patch temporarily swaps the stat to a safe 0..4 tier index for the OnSelected call,
        ///   then restores our real saved value immediately afterward.
        /// </summary>
        /// <remarks>
        /// IMPORTANT:
        /// - This is a "compat shim" so vanilla code can run without exceptions.
        /// - The real render-setting continues to be saved and used by PreDrawMain.
        /// </remarks>
        [HarmonyPatch(typeof(GraphicsTab), nameof(GraphicsTab.OnSelected))]
        internal static class GraphicsTab_OnSelected_SafeVanillaIndex
        {
            private static void Prefix(out int __state)
            {
                var stats = CastleMinerZGame.Instance?.PlayerStats;
                __state = stats?.DrawDistance ?? 0;

                if (stats != null)
                {
                    // Feed vanilla a safe 0..4 index so it can set SelectedIndex safely.
                    stats.DrawDistance = RenderSettingSnapper.RenderSettingToVanillaIndex(__state);
                }
            }

            private static void Postfix(int __state)
            {
                var stats = CastleMinerZGame.Instance?.PlayerStats;
                if (stats != null)
                {
                    // Restore our real saved render-setting immediately.
                    stats.DrawDistance = __state;
                }
            }
        }
        #endregion

        #region Optional Patch: BlockTerrain.BuildRadiusOrderOffsets (Ensure Offsets Include +13)

        /// <summary>
        /// Expands the radius-order offsets table used to decide which chunk offsets to consider.
        /// Summary:
        /// - Vanilla uses a fixed limit (commonly 13), creating a 26x26 offset grid.
        /// - Increasing the limit allows the engine to consider farther offsets.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This does not magically remove cache / streaming limits; it only expands candidates.
        /// - Keep RadiusOrderLimit in sync with our maximum intended render-setting behavior.
        /// </remarks>
        [HarmonyPatch(typeof(BlockTerrain), "BuildRadiusOrderOffsets")]
        internal static class BlockTerrain_BuildRadiusOrderOffsets_Extend
        {
            private static readonly AccessTools.FieldRef<BlockTerrain, IntVector3[]> _radiusOrderOffsets =
                AccessTools.FieldRefAccess<BlockTerrain, IntVector3[]>("_radiusOrderOffsets");

            private static bool Prefix(BlockTerrain __instance)
            {
                if (!ExtendedDrawDistanceConfig.Enabled)
                    return true;

                int limit = ExtendedDrawDistanceConfig.RadiusOrderLimit;
                if (limit < 13) limit = 13;

                int side = limit * 2; // [-limit .. limit-1].
                int count = side * side;

                var list = new List<IntVector3>(count);
                for (int z = -limit; z < limit; z++)
                {
                    for (int x = -limit; x < limit; x++)
                        list.Add(new IntVector3(x, 0, z));
                }

                list.Sort((a, b) =>
                {
                    int ra = Math.Max(Math.Abs(a.X), Math.Abs(a.Z));
                    int rb = Math.Max(Math.Abs(b.X), Math.Abs(b.Z));
                    if (ra != rb) return ra.CompareTo(rb);

                    int da = a.X * a.X + a.Z * a.Z;
                    int db = b.X * b.X + b.Z * b.Z;
                    return da.CompareTo(db);
                });

                _radiusOrderOffsets(__instance) = list.ToArray();
                return false;
            }
        }
        #endregion

        #region UI Patch: GraphicsTab View Distance -> TrackBar (1..10 Unique Steps)

        /// <summary>
        /// Helper that maps:
        /// - Slider steps (0..9) <-> "render-setting" values saved in PlayerStats.DrawDistance.
        /// Summary:
        /// - Collapses duplicated tiers so each slider step creates a distinct visible change.
        /// - Provides translation into vanilla tier indices (0..4) for compatibility shims.
        /// </summary>
        /// <remarks>
        /// Mapping:
        /// - Step 0..9 => RenderSettingByStep:
        ///     1,2,3,5,6,8,9,11,12,14
        /// - Step pairs correspond to tiers:
        ///     (0-1)=Lowest, (2-3)=Low, (4-5)=Medium, (6-7)=High, (8-9)=Ultra
        /// </remarks>
        internal static class RenderSettingSnapper
        {
            // Step 0..9 -> saved render-setting values.
            internal static readonly int[] RenderSettingByStep =
            {
                1, 2, 3, 5, 6, 8, 9, 11, 12, 14
            };

            internal static readonly string[] NameKeysByStep =
            {
                "Lowest", "Lowest", "Low", "Low", "Medium", "Medium", "High", "High", "Ultra", "Ultra"
            };

            internal static readonly bool[] PlusByStep =
            {
                false, true, false, true, false, true, false, true, false, true
            };

            // Vanilla tier defaults if user changes the 0..4 menu:
            // 0=Lowest,1=Low,2=Medium,3=High,4=Ultra
            internal static readonly int[] DefaultRenderSettingByVanillaIndex =
            {
                1, 3, 6, 9, 12
            };

            /// <summary>
            /// Snaps an arbitrary saved value down to the nearest supported render-setting value.
            /// </summary>
            internal static int SnapRenderSetting(int v)
            {
                if (v <= RenderSettingByStep[0]) return RenderSettingByStep[0];

                for (int i = RenderSettingByStep.Length - 1; i >= 0; i--)
                    if (v >= RenderSettingByStep[i])
                        return RenderSettingByStep[i];

                return RenderSettingByStep[0];
            }

            /// <summary>
            /// Converts a saved render-setting value to the slider step index (0..9).
            /// </summary>
            internal static int RenderSettingToStep(int renderSetting)
            {
                int snapped = SnapRenderSetting(renderSetting);

                for (int i = 0; i < RenderSettingByStep.Length; i++)
                    if (RenderSettingByStep[i] == snapped)
                        return i;

                return 0;
            }

            /// <summary>
            /// Converts a slider step index (0..9) to the saved render-setting value.
            /// </summary>
            internal static int StepToRenderSetting(int step)
            {
                if (step < 0) step = 0;
                if (step >= RenderSettingByStep.Length) step = RenderSettingByStep.Length - 1;
                return RenderSettingByStep[step];
            }

            /// <summary>
            /// Converts a saved render-setting into a vanilla tier index (0..4) for safe UI interop.
            /// </summary>
            internal static int RenderSettingToVanillaIndex(int renderSetting)
            {
                int step = RenderSettingToStep(renderSetting);
                return step / 2;
            }

            /// <summary>
            /// Converts a vanilla tier index (0..4) to the default render-setting value for that tier.
            /// </summary>
            internal static int VanillaIndexToRenderSetting(int vanillaIndex)
            {
                if (vanillaIndex < 0) vanillaIndex = 0;
                if (vanillaIndex > 4) vanillaIndex = 4;
                return DefaultRenderSettingByVanillaIndex[vanillaIndex];
            }

            /// <summary>
            /// Builds a localized display name for a step (e.g. "High+").
            /// </summary>
            internal static string StepToDisplayName(int step)
            {
                if (step < 0) step = 0;
                if (step >= RenderSettingByStep.Length) step = RenderSettingByStep.Length - 1;

                string baseName = CMZText.Get(NameKeysByStep[step], NameKeysByStep[step]);
                return PlusByStep[step] ? (baseName + "+") : baseName;
            }
        }

        /// <summary>
        /// Replaces the GraphicsTab view distance dropdown with a TrackBar + value label.
        /// Summary:
        /// - Injects controls on ctor / OnSelected / OnUpdate (self-healing in case of cached tabs).
        /// - Saves the real render-setting (1..14-ish values) into PlayerStats.DrawDistance.
        /// - Updates the label with "1..10 (TierName)".
        /// - Keeps layout aligned with the vanilla tab layout rows.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - The injected controls are stored in a ConditionalWeakTable keyed by the tab instance,
        ///   allowing safe reuse without leaking.
        /// - The vanilla DropList is removed from Children, but a separate patch blocks its event handler
        ///   to prevent it from overwriting the stat.
        /// </remarks>
        [HarmonyPatch]
        internal static class GraphicsTab_ViewDistance_TrackBar
        {
            private const int MinStep = 0;
            private const int MaxStep = 9;

            private sealed class State
            {
                public TrackBarControl Track;
                public TextControl ValueLabel;
            }

            private static readonly ConditionalWeakTable<GraphicsTab, State> _state =
                new ConditionalWeakTable<GraphicsTab, State>();

            [HarmonyPatch(typeof(GraphicsTab), MethodType.Constructor)]
            [HarmonyPatch(new[] { typeof(bool), typeof(ScreenGroup) })]
            [HarmonyPostfix]
            private static void Ctor_Postfix(GraphicsTab __instance)
            {
                if (!ExtendedDrawDistanceConfig.Enabled) return;
                EnsureInjected(__instance);
            }

            [HarmonyPatch(typeof(GraphicsTab), nameof(GraphicsTab.OnSelected))]
            [HarmonyPostfix]
            private static void OnSelected_Postfix(GraphicsTab __instance)
            {
                if (!ExtendedDrawDistanceConfig.Enabled) return;

                EnsureInjected(__instance);

                if (_state.TryGetValue(__instance, out var s))
                {
                    int currentRenderSetting = CastleMinerZGame.Instance.PlayerStats.DrawDistance;
                    int step = RenderSettingSnapper.RenderSettingToStep(currentRenderSetting);

                    s.Track.Value = ClampInt(step, MinStep, MaxStep);
                    UpdateValueLabel(s.ValueLabel, s.Track.Value);
                    Layout(__instance);
                }
            }

            [HarmonyPatch(typeof(GraphicsTab), "OnUpdate")]
            [HarmonyPostfix]
            private static void OnUpdate_Postfix(GraphicsTab __instance)
            {
                if (!ExtendedDrawDistanceConfig.Enabled) return;

                EnsureInjected(__instance);

                if (!_state.TryGetValue(__instance, out var s))
                    return;

                bool selectedTab = Traverse.Create(__instance).Property("SelectedTab").GetValue<bool>();
                if (selectedTab)
                {
                    int step = ClampInt(s.Track.Value, MinStep, MaxStep);
                    int renderSetting = RenderSettingSnapper.StepToRenderSetting(step);

                    CastleMinerZGame.Instance.PlayerStats.DrawDistance = renderSetting;

                    UpdateValueLabel(s.ValueLabel, step);
                }

                Layout(__instance);
            }

            /// <summary>
            /// Creates and installs the TrackBar + label once per GraphicsTab instance.
            /// </summary>
            private static void EnsureInjected(GraphicsTab tab)
            {
                if (_state.TryGetValue(tab, out _))
                    return; // Already injected.

                var controlsFont = (SpriteFont)Traverse.Create(tab).Field("_controlsFont").GetValue();
                Color menuGreen = new Color(78, 177, 61);

                var track = new TrackBarControl
                {
                    MinValue = MinStep,
                    MaxValue = MaxStep,
                    FillColor = menuGreen,
                };

                var valueLabel = new TextControl("", controlsFont);

                // Add our controls.
                AddChild(tab, track);
                AddChild(tab, valueLabel);

                // Remove vanilla dropdown if present.
                var drop = Traverse.Create(tab).Field("_viewDistanceDropList").GetValue();
                if (drop != null)
                    RemoveChild(tab, drop);

                // Init from current saved render-setting.
                int currentRenderSetting = CastleMinerZGame.Instance.PlayerStats.DrawDistance;
                int step = RenderSettingSnapper.RenderSettingToStep(currentRenderSetting);

                track.Value = ClampInt(step, MinStep, MaxStep);
                UpdateValueLabel(valueLabel, track.Value);

                _state.Add(tab, new State { Track = track, ValueLabel = valueLabel });

                Layout(tab);
            }

            /// <summary>
            /// Updates the value label to show the visible "1..10" step index + tier name.
            /// </summary>
            private static void UpdateValueLabel(TextControl label, int step)
            {
                step = ClampInt(step, MinStep, MaxStep);
                string name = RenderSettingSnapper.StepToDisplayName(step);
                SetText(label, $"{step + 1} ({name})");
            }

            /// <summary>
            /// Positions injected controls to match the vanilla "View Distance" row location.
            /// </summary>
            private static void Layout(GraphicsTab tab)
            {
                if (!_state.TryGetValue(tab, out var s))
                    return;

                float scaleY = Screen.Adjuster.ScaleFactor.Y;

                int height = (int)(50f * scaleY);
                int btnOffset = (int)(215f * scaleY);

                Point loc = new Point(0, (int)(75f * scaleY));
                loc.Y += height * 3;

                s.Track.Size = new Size((int)(185f * scaleY), height);
                s.Track.LocalPosition = new Point(loc.X + btnOffset, loc.Y + (int)(10f * scaleY));

                s.ValueLabel.Scale = scaleY;

                int labelX = s.Track.LocalPosition.X + s.Track.Size.Width + (int)(15f * scaleY);
                s.ValueLabel.LocalPosition = new Point(labelX, loc.Y);
            }

            /// <summary>
            /// Clamps an integer to a min/max inclusive range.
            /// </summary>
            private static int ClampInt(int v, int min, int max)
            {
                if (v < min) return min;
                if (v > max) return max;
                return v;
            }

            /// <summary>
            /// Adds a child control to the tab's Children collection (via reflection).
            /// </summary>
            private static void AddChild(object parent, object child)
            {
                object children = Traverse.Create(parent).Property("Children").GetValue();
                children.GetType().GetMethod("Add")?.Invoke(children, new[] { child });
            }

            /// <summary>
            /// Removes a child control from the tab's Children collection (via reflection).
            /// </summary>
            private static void RemoveChild(object parent, object child)
            {
                object children = Traverse.Create(parent).Property("Children").GetValue();
                var remove = children.GetType().GetMethod("Remove", new[] { child.GetType() })
                           ?? children.GetType().GetMethod("Remove");
                remove?.Invoke(children, new[] { child });
            }

            /// <summary>
            /// Sets TextControl text in a version-tolerant way (property first, then backing field).
            /// </summary>
            private static void SetText(object textControl, string text)
            {
                var prop = textControl.GetType().GetProperty("Text");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(textControl, text, null);
                    return;
                }
                Traverse.Create(textControl).Field("_text").SetValue(text);
            }
        }
        #endregion

        #region Patch: GraphicsTab Dropdown Handler (Prevent Overwriting Extended Values)

        /// <summary>
        /// Prevents the vanilla view distance dropdown from overwriting PlayerStats.DrawDistance.
        /// Summary:
        /// - Even after removing the DropList from Children, it may still receive SelectedIndex writes
        ///   and fire its SelectedIndexChanged handler.
        /// - When enabled, we block the handler so only the slider controls the stat.
        /// </summary>
        [HarmonyPatch(typeof(GraphicsTab), "_viewDistanceDropList_SelectedIndexChanged")]
        internal static class GraphicsTab_ViewDistanceDropList_SelectedIndexChanged_NoOverwrite
        {
            private static bool Prefix() => !ExtendedDrawDistanceConfig.Enabled;
        }
        #endregion

        #region Localization Helper (Strings.cs Is Internal)

        /// <summary>
        /// Minimal localization helper for CMZ resource strings.
        /// Summary:
        /// - The game's generated Strings class is internal/inaccessible to mods.
        /// - This wrapper pulls from the same resource base-name:
        ///     "DNA.CastleMinerZ.Globalization.Strings"
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Returns fallback (or key) if resource is missing.
        /// - Use for small UI labels like "Lowest", "Medium", etc.
        /// </remarks>
        internal static class CMZText
        {
            // Same base-name the internal Strings class uses internally.
            private static readonly ResourceManager _rm =
                new ResourceManager("DNA.CastleMinerZ.Globalization.Strings", typeof(CastleMinerZGame).Assembly);

            /// <summary>
            /// Fetches a localized string by key, falling back safely if missing.
            /// </summary>
            public static string Get(string key, string fallback = null)
            {
                try
                {
                    string s = _rm.GetString(key, CultureInfo.CurrentUICulture);
                    return string.IsNullOrEmpty(s) ? (fallback ?? key) : s;
                }
                catch
                {
                    return fallback ?? key;
                }
            }
        }
        #endregion

        #endregion
    }
}
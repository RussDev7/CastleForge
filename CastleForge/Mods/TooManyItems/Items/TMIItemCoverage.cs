/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Collections;
using System.Linq;
using HarmonyLib;
using System;

#region Infrastructure / Helpers

/// <summary>
/// Reference-based equality comparer (object identity). Useful when the instance identity
/// is the key (e.g., BlockType instances), not their value semantics.
/// </summary>
internal sealed class ReferenceEq<T> : IEqualityComparer<T> where T : class
{
    public static readonly ReferenceEq<T> Instance = new ReferenceEq<T>();
    public bool Equals(T x, T y)   => ReferenceEquals(x, y);
    public int  GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
#endregion

/// <summary>
/// Purpose:
///   Discover which BlockTypeEnums are already represented by vanilla inventory items,
///   so the TooManyItems injector can avoid creating duplicate/block items.
///
/// Usage:
///   • Call TMIItemCoverage.AlreadyHasItemFor(bt) before injecting a block-item for 'bt'.
///   • Make sure InventoryItem.Initalize(...) has run first (the vanilla item table must exist).
///
/// Notes:
///   • ConfigGlobals etc. are used elsewhere in the mod; this file has no dependency on them.
///   • Uses Harmony AccessTools for reflection and a reference-equality lookup to map BlockType instances -> enums.
/// </summary>
internal static class TMIItemCoverage
{
    #region State (Lazy-Built Once)

    // Set on first successful Build(); guarded by _gate.
    private static HashSet<BlockTypeEnum>            _covered;
    private static Dictionary<object, BlockTypeEnum> _blockInstToEnum;     // BlockType instance -> enum (by reference).
    private static readonly object                   _gate = new object();

    /// <summary>
    /// Read-only view of discovered covered blocks (empty if Build never ran).
    /// </summary>
    public static IReadOnlyCollection<BlockTypeEnum> Covered =>
        _covered ?? (IReadOnlyCollection<BlockTypeEnum>)Array.Empty<BlockTypeEnum>();

    #endregion

    #region Public API

    /// <summary>
    /// Returns true if vanilla already exposes an item for the given block enum
    /// (directly or via a known family/unification rule).
    /// </summary>
    public static bool AlreadyHasItemFor(BlockTypeEnum block)
    {
        Build();
        return _covered.Contains(block);
    }
    #endregion

    #region Build / Discovery

    /// <summary>
    /// Builds the coverage table once:
    ///   1) Map BlockType instance -> BlockTypeEnum.
    ///   2) Scan vanilla InventoryItem classes for fields/properties that reference blocks.
    ///   3) Apply family unifications (torches, doors, spawn toggles).
    ///   4) Apply small name-based fallbacks for known mismatches.
    /// </summary>
    public static void Build()
    {
        if (_covered != null) return;     // Fast path (already built).

        lock (_gate)
        {
            if (_covered != null) return; // Double-checked.

            _covered = new HashSet<BlockTypeEnum>();
            _blockInstToEnum = new Dictionary<object, BlockTypeEnum>(ReferenceEq<object>.Instance);

            // 1) Map BlockType instance -> BlockTypeEnum once (by reference identity).
            foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                var inst = BlockType.GetType(e);
                if (inst != null && !_blockInstToEnum.ContainsKey(inst))
                    _blockInstToEnum[inst] = e;
            }

            // 2) Walk the already-registered vanilla items.
            //    InventoryItem.AllItems : Dictionary<InventoryItemIDs, InventoryItemClass>
            var allItemsFld = AccessTools.Field(typeof(InventoryItem), "AllItems");
            if (!(allItemsFld?.GetValue(null) is IDictionary dict))
                return; // Can't proceed (not initialized).

            foreach (DictionaryEntry kv in dict)
            {
                var cls = kv.Value;
                if (cls == null) continue;

                var t = cls.GetType();
                var fields = AccessTools.GetDeclaredFields(t);
                var props  = AccessTools.GetDeclaredProperties(t);

                // 2a) Direct BlockTypeEnum fields/properties on the item class.
                foreach (var f in fields)
                    if (f.FieldType == typeof(BlockTypeEnum))
                        _covered.Add((BlockTypeEnum)f.GetValue(cls));

                foreach (var p in props)
                    if (p.PropertyType == typeof(BlockTypeEnum) && p.GetIndexParameters().Length == 0 && p.CanRead)
                        _covered.Add((BlockTypeEnum)p.GetValue(cls, null));

                // 2b) BlockType instance fields/properties -> map back to enum via reference table.
                foreach (var f in fields)
                    if (typeof(BlockType).IsAssignableFrom(f.FieldType))
                    {
                        var inst = f.GetValue(cls);
                        if (inst != null && _blockInstToEnum.TryGetValue(inst, out var e))
                            _covered.Add(e);
                    }

                foreach (var p in props)
                    if (typeof(BlockType).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0 && p.CanRead)
                    {
                        var inst = p.GetValue(cls, null);
                        if (inst != null && _blockInstToEnum.TryGetValue(inst, out var e))
                            _covered.Add(e);
                    }
            }

            // 3) Family/unification rules where vanilla uses ONE item for MANY block enums.
            UnifyFamilies(_covered);

            // 4) Name-based fallbacks for small, well-known mismatches.
            ApplySafeNameFallbacks(_covered);
        }
    }
    #endregion

    #region Family / Unification Rules

    /// <summary>
    /// Treat variant enums as covered when their canonical base is covered.
    /// (e.g., torch orientations, door halves/orientations, spawn on/off/dim families)
    /// </summary>
    private static void UnifyFamilies(HashSet<BlockTypeEnum> covered)
    {
        // Torch orientations -> Torch item covers all.
        var torchVariants = new[]
        {
            BlockTypeEnum.TorchPOSX, BlockTypeEnum.TorchNEGX,
            BlockTypeEnum.TorchPOSZ, BlockTypeEnum.TorchNEGZ,
            BlockTypeEnum.TorchPOSY, BlockTypeEnum.TorchNEGY
        };
        if (covered.Contains(BlockTypeEnum.Torch))
            foreach (var v in torchVariants) covered.Add(v);

        // Door variants (normal & strong families) -> door items cover placement.
        foreach (var e in Enum.GetValues(typeof(BlockTypeEnum)).Cast<BlockTypeEnum>())
            if (IsDoorVariant(e) && (covered.Contains(BlockTypeEnum.NormalLowerDoor) ||
                                     covered.Contains(BlockTypeEnum.StrongLowerDoor)))
                covered.Add(e);

        // Spawn toggles: Treat On/Off/Dim as a single family we do not want to inject.
        UnifyOnOffDimFamily(covered, "EnemySpawn");
        UnifyOnOffDimFamily(covered, "AlienSpawn");
        UnifyOnOffDimFamily(covered, "HellSpawn");
        UnifyOnOffDimFamily(covered, "BossSpawn");
    }

    private static bool IsDoorVariant(BlockTypeEnum e)
        => e.ToString().IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void UnifyOnOffDimFamily(HashSet<BlockTypeEnum> covered, string prefix)
    {
        var all = Enum.GetValues(typeof(BlockTypeEnum)).Cast<BlockTypeEnum>()
                      .Where(b => b.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                      .ToArray();

        if (all.Any(b => covered.Contains(b)))
            foreach (var b in all) covered.Add(b);
    }
    #endregion

    #region Name-based Safe Fallbacks

    /// <summary>
    /// A tiny, conservative name mapping for well-known item vs block name mismatches.
    /// Keeps this list small (low risk): only cases we know vanilla supports.
    /// </summary>
    private static void ApplySafeNameFallbacks(HashSet<BlockTypeEnum> covered)
    {
        // If there's an InventoryItemIDs whose ID clearly matches a block name, consider it covered.
        // (e.g., Dirt -> DirtBlock, LanternFancy -> LanternFancyBlock, etc.)
        var itemIdNames = Enum.GetNames(typeof(InventoryItemIDs)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (BlockTypeEnum b in Enum.GetValues(typeof(BlockTypeEnum)))
        {
            string n = b.ToString();

            // Basic "*Block" heuristic (only if that exact InventoryItemIDs exists).
            if (itemIdNames.Contains(n + "Block"))
                covered.Add(b);

            // Specific well-known pairs:
            // Snow / Ice items match block names exactly.
            if (n == "Snow" && itemIdNames.Contains("Snow")) covered.Add(b);
            if (n == "Ice"  && itemIdNames.Contains("Ice"))  covered.Add(b);

            // Glass* (block) -> GlassWindow* (item).
            if (n == "GlassBasic"   && itemIdNames.Contains("GlassWindowWood"))     covered.Add(b);
            if (n == "GlassIron"    && itemIdNames.Contains("GlassWindowIron"))     covered.Add(b);
            if (n == "GlassStrong"  && itemIdNames.Contains("GlassWindowGold"))     covered.Add(b);
            if (n == "GlassMystery" && itemIdNames.Contains("GlassWindowDiamond"))  covered.Add(b);

            // Crate* (block) -> *Container (item).
            if (n == "CrateStone"      && itemIdNames.Contains("StoneContainer"))      covered.Add(b);
            if (n == "CrateCopper"     && itemIdNames.Contains("CopperContainer"))     covered.Add(b);
            if (n == "CrateIron"       && itemIdNames.Contains("IronContainer"))       covered.Add(b);
            if (n == "CrateGold"       && itemIdNames.Contains("GoldContainer"))       covered.Add(b);
            if (n == "CrateDiamond"    && itemIdNames.Contains("DiamondContainer"))    covered.Add(b);
            if (n == "CrateBloodstone" && itemIdNames.Contains("BloodstoneContainer")) covered.Add(b);
        }
    }
    #endregion
}
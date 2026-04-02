/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Linq;
using HarmonyLib;
using System;

using static ModLoader.LogSystem;
using static TooManyItems.TMILog;

/// <summary>
/// Purpose:
/// - Reflectively register missing "block as item" entries so TooManyItems can expose every block in the UI.
/// - Avoid duplicates by consulting dynamic coverage (TMIItemCoverage.AlreadyHasItemFor).
/// - Skip problematic/special blocks via a small filter (ShouldSkipEntirely + _skip).
///
/// Notes:
/// - This class only depends on vanilla's private RegisterItemClass(InventoryItemClass).
/// - ConfigGlobals are used across the whole mod (not referenced here, but keep in mind for future knobs).
/// </summary>
namespace TooManyItems
{
    /// <summary>
    /// Injects synthetic InventoryItemIDs for blocks that vanilla doesn't expose as items.
    /// The dedupe logic lives in <see cref="TMIItemCoverage.AlreadyHasItemFor(BlockTypeEnum)"/>.
    /// </summary>
    internal static class TMIItemInjector
    {
        #region Public API

        /// <summary>
        /// One-shot registration entrypoint. Postfix this after vanilla item registration.
        /// Will enumerate BlockTypeEnum and add missing "block items".
        /// </summary>
        public static void Register()
        {
            if (IsRegistered) return; // (Optional) guard if Register() can run more than once.
            TMIItemCoverage.Build();  // Build vanilla coverage (maps existing item ↔ block exposure).

            foreach (BlockTypeEnum bt in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                if (_skip.Contains(bt)) continue;                    // Manual skiplist.
                if (ShouldSkipEntirely(bt)) continue;                // Structural/admin/liquid/etc.
                if (TMIItemCoverage.AlreadyHasItemFor(bt)) continue; // Dynamic dedupe vs. vanilla/items added by other mods.

                // Safe to expose as a plain block item.
                AddBlock(bt, bt.ToString());
            }

            IsRegistered = true;
        }

        /// <summary>
        /// All known item IDs combining vanilla + our synthetic IDs.
        /// Useful for UI lists and sanity checks.
        /// </summary>
        public static IEnumerable<InventoryItemIDs> AllIds
        {
            get
            {
                // 1) Anything actually registered (includes WeaponAddons synthetic IDs).
                var registered = GetAllUsableItemIds();

                // 2) Things we explicitly add (TMI extras) + enum-defined vanilla.
                var known = Enum.GetValues(typeof(InventoryItemIDs)).Cast<InventoryItemIDs>()
                    .Concat(_extras)
                    .Concat(registered);

                // 3) Scan 0..max (and optionally a little above) to include "invalid" IDs too.
                int max = known.Select(id => (int)id).DefaultIfEmpty(0).Max();

                const int EXTRA_INVALID_TAIL = 0;     // Set to 0 if you only want holes up to max.
                const int HARD_CAP           = 2048;  // Safety if some mod picks a huge ID.
                int scanMax = Math.Min(max + EXTRA_INVALID_TAIL, HARD_CAP);

                return Enumerable.Range(0, scanMax + 1)
                    .Select(i => (InventoryItemIDs)i) // Includes undefined/unregistered IDs.
                    .Concat(known)                    // Keeps any IDs > scanMax (if HARD_CAP hits).
                    .Distinct()
                    .OrderBy(id => (int)id);
            }
        }

        /// <summary>
        /// True once we've attempted to register (basic re-entrancy guard).
        /// </summary>
        public static bool IsRegistered { get; private set; } = false;

        #endregion

        #region State & Caches

        // Synthetic IDs we created this session (for AllIds / UI).
        private static readonly List<InventoryItemIDs> _extras = new List<InventoryItemIDs>();

        // Mapping convenience: Which InventoryItemIDs did we create for a given block?
        private static readonly Dictionary<BlockTypeEnum, InventoryItemIDs> _byBlock = new Dictionary<BlockTypeEnum, InventoryItemIDs>();

        // First unused numeric enum value (computed lazily once).
        private static int _nextId = -1;
        private static int NextId()
        {
            if (_nextId >= 0) return _nextId;
            _nextId = Enum.GetValues(typeof(InventoryItemIDs)).Cast<int>().Max() + 1;
            return _nextId;
        }

        /// <summary>
        /// Returns true if the given item ID was created by TMI (i.e. one of the
        /// extra synthetic IDs injected at runtime), false for vanilla IDs.
        /// </summary>
        internal static bool IsSynthetic(InventoryItemIDs id)
        {
            return _extras.Contains(id);
        }

        /// <summary>
        /// Tries to get the synthetic InventoryItemID that TMI registered for the
        /// given block type.
        /// </summary>
        internal static bool TryGetSyntheticId(BlockTypeEnum block, out InventoryItemIDs id)
        {
            return _byBlock.TryGetValue(block, out id);
        }
        #endregion

        #region Reflection Binding (Private Vanilla API)

        // Late-bind these each call to be resilient (could be cached statically).
        private static readonly Type _invItemType        = typeof(InventoryItem);
        private static readonly Type _blockItemClassType = typeof(BlockInventoryItemClass);

        /// <summary>
        /// Registers a new block-backed inventory item by invoking vanilla's private
        /// InventoryItem.RegisterItemClass(new BlockInventoryItemClass(...)).
        /// </summary>
        private static void AddBlock(BlockTypeEnum block, string name, float weight = 0.01f)
        {
            if (_byBlock.ContainsKey(block)) return;

            var register = AccessTools.Method(_invItemType, "RegisterItemClass");
            if (register == null)
            {
                if (GetLoggingMode() != LoggingType.None)
                    Log("ERROR: InventoryItem.RegisterItemClass not found (reflection).");
                return;
            }

            var ctor = _blockItemClassType.GetConstructor(new[]
            {
                typeof(InventoryItemIDs),
                typeof(BlockTypeEnum),
                typeof(string),
                typeof(float),
            });

            if (ctor == null)
            {
                if (GetLoggingMode() != LoggingType.None)
                    Log("ERROR: BlockInventoryItemClass(..) ctor not found (reflection).");
                return;
            }

            var id = NewSyntheticId();
            var itemClass = ctor.Invoke(new object[] { id, block, name, weight });

            // Register it with the real private method.
            register.Invoke(null, new[] { itemClass });

            _byBlock[block] = id;
        }

        /// <summary>
        /// Allocates a new enum slot for a synthetic item ID and tracks it.
        /// </summary>
        private static InventoryItemIDs NewSyntheticId()
        {
            var id = (InventoryItemIDs)NextId(); // Take current head.
            _nextId++;                           // Bump pointer.
            _extras.Add(id);
            return id;
        }
        #endregion

        #region Filters & Heuristics

        /// <summary>
        /// Manual skip list for blocks that should never be exposed as plain items.
        /// Add internal helper blocks here as you encounter them.
        /// </summary>
        private static readonly HashSet<BlockTypeEnum> _skip = new HashSet<BlockTypeEnum>()
        {
            // e.g. BlockTypeEnum.Air, BlockTypeEnum.Water, BlockTypeEnum.Lava, ...
            // Keep this intentionally small; prefer dynamic dedupe (AlreadyHasItemFor).
        };

        /// <summary>
        /// Catch "special" names that are very likely item-blocks or structural halves.
        /// Expand as needed (Torch orientations, doors halves, admin toggles, liquids, etc.).
        /// </summary>
        private static bool ShouldSkipEntirely(BlockTypeEnum bt)
        {
            string n = bt.ToString();

            // Don't expose structural halves/orientation blocks or doors as plain items.
            if (n.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }
        #endregion

        #region Unused Helpers (Kept For Debugging / Future Features)

        // Heuristic: "already covered" if a vanilla InventoryItemIDs has the same name as the block.
        // (Prefer using TMIItemCoverage.AlreadyHasItemFor for the real dedupe.)
        private static bool LooksAlreadyCovered(BlockTypeEnum block)
        {
            string n = block.ToString();
            foreach (InventoryItemIDs vid in Enum.GetValues(typeof(InventoryItemIDs)))
                if (string.Equals(vid.ToString(), n, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // Returns currently registered item IDs by peeking InventoryItem.AllItems via reflection.
        private static InventoryItemIDs[] GetAllUsableItemIds()
        {
            var f = AccessTools.Field(typeof(InventoryItem), "AllItems");
            if (f?.GetValue(null) is System.Collections.IDictionary dict)
                return dict.Keys.Cast<InventoryItemIDs>().ToArray();
            return Array.Empty<InventoryItemIDs>();
        }
        #endregion
    }
}
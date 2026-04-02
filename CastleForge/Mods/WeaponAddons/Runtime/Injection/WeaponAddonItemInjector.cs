/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Reflection;
using System;

using static ModLoader.LogSystem;

namespace WeaponAddons
{
    /// <summary>
    /// Register "synthetic" InventoryItemIDs at runtime by cloning a vanilla
    /// InventoryItemClass and assigning it a new ID.
    ///
    /// Summary:
    /// - Creates runtime-only ItemIDs beyond the compiled InventoryItemIDs enum.
    /// - Keeps the game stable by registering a real InventoryItemClass for that ID
    ///   inside InventoryItem's internal AllItems dictionary.
    /// - Tracks a synthetic -> base mapping used by:
    ///   • vanilla-safe networking (remap outgoing packets)
    ///   • optional UI icon fallback (synthetic -> base icon cell)
    ///
    /// Notes:
    /// - Cloning preserves behavior type (ex: LaserGunInventoryItemClass stays "laser-like").
    /// - If an ID already exists, we DO NOT replace the class instance (important for live items).
    /// </summary>
    internal static class WeaponAddonItemInjector
    {
        // =====================================================================================
        // STATE (MAPPINGS + REFLECTION CACHES)
        // =====================================================================================

        #region State

        /// <summary>
        /// Synthetic -> base mapping (used by net safety + icon remap).
        /// </summary>
        private static readonly Dictionary<InventoryItemIDs, InventoryItemIDs> _syntheticToBase
            = new Dictionary<InventoryItemIDs, InventoryItemIDs>();

        /// <summary>
        /// Tracks what we registered this session (useful for debugging / optional cleanup).
        /// </summary>
        private static readonly HashSet<InventoryItemIDs> _registeredThisSession
            = new HashSet<InventoryItemIDs>();

        // Cached reflection handles (initialized once).
        private static MethodInfo _miRegisterItemClass;
        private static MethodInfo _miMemberwiseClone;
        private static FieldInfo  _fiAllItems;

        private static bool _init;

        #endregion

        /// <summary>
        /// =====================================================================================
        /// PUBLIC API
        /// =====================================================================================
        ///
        /// Summary:
        /// - IsSynthetic / TryGetBaseId: lookup helpers used by patches (network + UI).
        /// - EnsureCloneRegistered: main entrypoint used by WeaponAddonManager when a pack
        ///   requests AS_NEW_ITEM / NEW_ITEM behavior.
        /// =====================================================================================
        /// </summary>

        #region Public API

        /// <summary>
        /// Returns true if the given id is a synthetic (runtime-injected) ID.
        /// </summary>
        public static bool IsSynthetic(InventoryItemIDs id)
            => _syntheticToBase.ContainsKey(id);

        /// <summary>
        /// Resolves a synthetic id back to its base (slot) id.
        /// </summary>
        public static bool TryGetBaseId(InventoryItemIDs id, out InventoryItemIDs baseId)
            => _syntheticToBase.TryGetValue(id, out baseId);

        /// <summary>
        /// Ensure a synthetic clone exists for this pack.
        ///
        /// Summary:
        /// - baseId: the vanilla class to clone (behavior source).
        /// - desiredId: pinned numeric ID (if > 0), else allocate next free ID.
        /// - debugName: pack name (for logs).
        /// - itemClass: returns the class instance registered for the chosen ID.
        ///
        /// Notes:
        /// - If desiredId is already registered, we DO NOT replace the class instance:
        ///   existing inventory items hold a reference to the class they were created with.
        ///   Instead, the caller should apply stats to the existing class instance in-place.
        /// - If desiredId is 0, we allocate the next free ID and return it.
        /// - Collision handling is "walk forward" (unless pinned, where we log loudly).
        /// </summary>
        public static InventoryItemIDs EnsureCloneRegistered(
            InventoryItemIDs baseId,
            int desiredId,
            string debugName,
            out InventoryItem.InventoryItemClass itemClass)
        {
            EnsureInit();

            var all = GetAllItems();
            int idInt = desiredId > 0 ? desiredId : AllocateNextId(all);

            // Collision handling: Walk forward until we find a free slot (unless user explicitly pinned it).
            if (all.ContainsKey((InventoryItemIDs)idInt))
            {
                if (desiredId > 0)
                {
                    // Pinned ID already taken -> we will still walk, but we log loudly.
                    int start = idInt;
                    while (idInt < short.MaxValue && all.ContainsKey((InventoryItemIDs)idInt))
                        idInt++;

                    Log($"[Injector] ITEM_ID {start} already used; reassigned -> {idInt} ({debugName}).");
                }
                else
                {
                    while (idInt < short.MaxValue && all.ContainsKey((InventoryItemIDs)idInt))
                        idInt++;
                }
            }

            var newId = (InventoryItemIDs)idInt;

            // If something already exists at this ID, keep the existing instance and just update mapping.
            var existing = InventoryItem.GetClass(newId);
            if (existing != null)
            {
                itemClass = existing;
                _syntheticToBase[newId] = baseId;
                return newId;
            }

            // Clone from the base class (keeps behavior type: laser guns stay laser guns, etc).
            var src = InventoryItem.GetClass(baseId);
            if (src == null)
            {
                itemClass = null;
                Log($"[Injector] Base item class not found for {baseId} ({(int)baseId}).");
                return InventoryItemIDs.BareHands;
            }

            var cloneObj = _miMemberwiseClone.Invoke(src, null);
            if (!(cloneObj is InventoryItem.InventoryItemClass clone))
            {
                itemClass = null;
                Log($"[Injector] Failed to clone class for {baseId} ({(int)baseId}).");
                return InventoryItemIDs.BareHands;
            }

            // Assign the synthetic ID.
            clone.ID = newId;

            // Register (adds to InventoryItem.AllItems dictionary).
            _miRegisterItemClass.Invoke(null, new object[] { clone });

            itemClass = clone;
            _syntheticToBase[newId] = baseId;
            _registeredThisSession.Add(newId);

            Log($"[Injector] Registered synthetic item {newId} ({(int)newId}) from base {baseId} ({(int)baseId}) [{debugName}].");
            return newId;
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// INTERNALS (REFLECTION + ID ALLOCATION)
        /// =====================================================================================
        ///
        /// Summary:
        /// - EnsureInit caches reflection handles:
        ///   • object.MemberwiseClone (for cloning the base class)
        ///   • InventoryItem.RegisterItemClass (to register the new class)
        ///   • InventoryItem.AllItems / _allItems field (for allocation + existence checks)
        /// - GetAllItems returns the AllItems dictionary (best-effort).
        /// - AllocateNextId picks max existing ID + 1 (best-effort).
        /// =====================================================================================
        /// </summary>

        #region Internals

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            _miMemberwiseClone =
                typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

            _miRegisterItemClass =
                typeof(InventoryItem).GetMethod("RegisterItemClass", BindingFlags.Static | BindingFlags.NonPublic);

            _fiAllItems =
                typeof(InventoryItem).GetField("AllItems", BindingFlags.Static | BindingFlags.NonPublic)
                ?? typeof(InventoryItem).GetField("_allItems", BindingFlags.Static | BindingFlags.NonPublic);

            // Notes:
            // - These are best-effort: Depending on build/obfuscation, names may differ.
            // - If any of these are null, injection can't function and will log warnings.
            if (_miMemberwiseClone == null) Log("[Injector] ERROR: MemberwiseClone not found.");
            if (_miRegisterItemClass == null) Log("[Injector] ERROR: InventoryItem.RegisterItemClass not found.");
            if (_fiAllItems == null) Log("[Injector] ERROR: InventoryItem.AllItems field not found.");
        }

        private static Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> GetAllItems()
        {
            // Notes:
            // - Returning a new empty dict is safe but disables collision detection/allocation quality.
            // - Callers handle this as best-effort; gameplay should not crash.
            var dict = _fiAllItems?.GetValue(null) as Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>;
            return dict ?? new Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>();
        }

        private static int AllocateNextId(Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> all)
        {
            // Summary:
            // - Scans existing IDs to find max and returns max+1.
            // - This keeps synthetic IDs above vanilla ranges (usually).
            int max = 0;
            foreach (var k in all.Keys)
            {
                int v = (int)k;
                if (v > max) max = v;
            }
            return Math.Max(max + 1, 1);
        }
        #endregion
    }
}
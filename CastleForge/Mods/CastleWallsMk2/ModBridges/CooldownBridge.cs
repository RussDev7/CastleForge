/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using DNA.Timers;
using HarmonyLib;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Helper that abstracts access to an item's internal cooldown timer(s).
    /// It prefers the InventoryItem.CoolDownTimer *property* (if present),
    /// then falls back to a likely backing field, then finally any OneShotTimer
    /// fields found on the instance via reflection.
    /// </summary>
    internal static class CooldownBridge
    {
        // Cache of discovered OneShotTimer fields for each concrete InventoryItem type,
        // so we don't pay reflection cost every frame.
        private static readonly Dictionary<Type, FieldInfo[]>     _timerFieldsByType  = new Dictionary<Type, FieldInfo[]>();

        // Delegate to invoke the InventoryItem.CoolDownTimer property getter (private or public).
        // Some builds expose the timer as a PROPERTY rather than a field - use that first.
        private static readonly Func<InventoryItem, OneShotTimer> _cooldownPropGetter = CreatePropGetter();

        /// <summary>
        /// Locate the CoolDownTimer property getter (private/public) and build a fast delegate to it.
        /// Returns null if the property/getter isn't found.
        /// </summary>
        private static Func<InventoryItem, OneShotTimer> CreatePropGetter()
        {
            var pi     = AccessTools.Property(typeof(InventoryItem), "CoolDownTimer");
            var getter = pi?.GetGetMethod(nonPublic: true) ?? pi?.GetGetMethod(); // Prefer non-public, else public.
            return getter != null
                ? (Func<InventoryItem, OneShotTimer>)Delegate.CreateDelegate(
                        typeof(Func<InventoryItem, OneShotTimer>), getter)
                : null;
        }

        /// <summary>
        /// Try to fetch the "main" cooldown timer for an item.
        /// Priority:
        ///  1) CoolDownTimer property.
        ///  X) Backing field named "CoolDownTimer".
        ///  X) First OneShotTimer found via reflection.
        /// Returns null if none found.
        /// </summary>
        public static OneShotTimer TryGetBaseTimer(InventoryItem item)
        {
            if (item == null) return null;

            // 1) Property (preferred & most compatible across builds)
            if (_cooldownPropGetter != null)
            {
                try { return _cooldownPropGetter(item); } catch { /* fall through to other strategies */ }
            }
            return null;
        }

        /// <summary>
        /// Enumerate ALL OneShotTimer instances present on the item.
        /// Yields the property timer first (if any), then any additional fields
        /// discovered via reflection and cached per type.
        /// </summary>
        public static IEnumerable<OneShotTimer> EnumerateAllTimers(InventoryItem item)
        {
            if (item == null) yield break;

            // Yield the "base" property-backed timer first (so callers see the primary cooldown first).
            var baseT = _cooldownPropGetter?.Invoke(item);
            if (baseT != null) yield return baseT;

            // Scan for other OneShotTimer fields only once per concrete type.
            var t = item.GetType();
            if (!_timerFieldsByType.TryGetValue(t, out var fields))
            {
                fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          .Where(f => f.FieldType == typeof(OneShotTimer))
                          .ToArray();
                _timerFieldsByType[t] = fields;
            }

            // Yield any additional timers, skipping the same instance as the base property (if equal).
            foreach (var f in fields)
            {
                var ot = (OneShotTimer)f.GetValue(item);
                if (ot != null && ot != baseT) yield return ot;
            }
        }

        // Small helpers to interact with OneShotTimer safely (null-tolerant).

        /// <summary>Get the configured maximum duration for this timer (or 0 if null).</summary>
        public static TimeSpan GetMaxTime(OneShotTimer t) => t?.MaxTime ?? TimeSpan.Zero;

        /// <summary>Set the maximum duration for this timer (no-op if null).</summary>
        public static void SetMaxTime(OneShotTimer t, TimeSpan v) { if (t != null) t.MaxTime = v; }

        /// <summary>
        /// Returns true when the timer has elapsed. For null timers, we treat as "not expired"
        /// so calling code won't accidentally bypass rate limits.
        /// </summary>
        public static bool IsExpired(OneShotTimer t) => t?.Expired ?? false;

        /// <summary>Reset/restart the timer from now (no-op if null).</summary>
        public static void Reset(OneShotTimer t) { t?.Reset(); }
    }
}

/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ;
using DNA.Timers;
using System;
using DNA;

namespace CastleWallsMk2
{
    internal static class WeaponExtensions
    {
        #region Flags & Runtime State

        // Feature toggles controlled by the UI/console/etc.
        public static bool _rapidFireOn, _superGunStatsOn;

        // For rapid-fire: Remember each OneShotTimer's original MaxTime so we can restore it.
        private static readonly Dictionary<OneShotTimer, TimeSpan> _savedTimerMax =
            new Dictionary<OneShotTimer, TimeSpan>();

        #endregion

        #region Factory Snapshot (True Originals)

        // Snapshot of immutable "class stats" we temporarily modify (damage, recoil, automatic, etc.).
        // We store the very first, clean copy per GunInventoryItemClass so we can revert later.
        private struct GunFactory
        {
            public float EnemyDamage, Velocity, FlightTime, ItemSelfDamagePerUse;
            public Angle Recoil, MinInnaccuracy, MaxInnaccuracy, ShoulderedMaxAccuracy, ShoulderedMinAccuracy;
            public int   ClipCapacity;
            public bool  Automatic;
        }

        private static readonly Dictionary<GunInventoryItemClass, GunFactory> _factory =
            new Dictionary<GunInventoryItemClass, GunFactory>(128);

        // Take a single, clean snapshot of a gun class.
        private static GunFactory Snapshot(GunInventoryItemClass c) => new GunFactory
        {
            EnemyDamage            = c.EnemyDamage,
            Velocity               = c.Velocity,
            FlightTime             = c.FlightTime,
            Recoil                 = c.Recoil,
            MinInnaccuracy         = c.MinInnaccuracy,
            MaxInnaccuracy         = c.MaxInnaccuracy,
            ShoulderedMaxAccuracy  = c.ShoulderedMaxAccuracy,
            ShoulderedMinAccuracy  = c.ShoulderedMinAccuracy,
            ClipCapacity           = c.ClipCapacity,
            ItemSelfDamagePerUse   = c.ItemSelfDamagePerUse,
            Automatic              = c.Automatic
        };

        /// <summary>
        /// Walk current session and snapshot any gun classes we haven't seen yet.
        /// Call this right before you start mutating stats/flags.
        /// </summary>
        private static void CaptureFactoryForAllGuns()
        {
            var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
            if (session == null) return;

            foreach (NetworkGamer g in session.AllGamers)
            {
                var player = g?.Tag as Player;
                var inv    = player?.PlayerInventory;
                if (inv == null) continue;

                if (inv.ActiveInventoryItem is GunInventoryItem gi)
                {
                    var cls = gi.GunClass;
                    if (cls != null && !_factory.ContainsKey(cls))
                        _factory[cls] = Snapshot(cls);
                }

                // Optional: Iterate entire inventory if you want comprehensive coverage.
                // foreach (var it in inv.Inventory) if (it is GunInventoryItem gi2) { ... }
            }
        }
        #endregion

        #region Factory Accessors

        internal static float GetOriginalVelocityOr(GunInventoryItemClass cls, float fallback)
        {
            if (cls != null && _factory.TryGetValue(cls, out var f))
                return f.Velocity;
            return fallback;
        }
        #endregion

        #region Public Toggles (RapidFire / SuperStats)

        /// <summary>
        /// Enable/disable Rapid Fire. When disabling, restores any timers and 'Automatic' flags we changed.
        /// </summary>
        public static void SetRapidFire(bool enabled)
        {
            _rapidFireOn = enabled;

            if (enabled)
            {
                // Before we touch class flags/timers, snapshot originals.
                CaptureFactoryForAllGuns();
            }
            else
            {
                // Restore only the 'Automatic' flag to factory values.
                RestoreRapidFireOnly();

                // Restore the per-item cooldown MaxTime values we shrank.
                foreach (var kv in _savedTimerMax)
                {
                    var t   = kv.Key;
                    var max = kv.Value;
                    CooldownBridge.SetMaxTime(t, max);
                    CooldownBridge.Reset(t);
                }
                _savedTimerMax.Clear();
            }
        }

        /// <summary>
        /// Enable/disable "super" gun stats (damage, recoil=0, accuracy, etc.). Never changes 'Automatic'.
        /// </summary>
        public static void SetSuperGunStats(bool enabled)
        {
            if (enabled)
            {
                CaptureFactoryForAllGuns(); // Freeze originals before mutating.
                _superGunStatsOn = true;
            }
            else
            {
                _superGunStatsOn = false;
                RestoreSuperStatsOnly();    // Revert only the "super" fields (not Automatic).
            }
        }
        #endregion

        #region Per-Frame Patcher (Tick)

        /// <summary>
        /// Call this every frame (or often) to keep the currently held weapon patched per active features.
        /// - Ensures class.Automatic matches the RapidFire toggle for the current gun.
        /// - While RapidFire is ON, shrinks all cooldown timers on the current item (once each).
        /// </summary>
        public static void Tick()
        {
            // Make sure you can always take a shot when infinite ammo is on.
            EnsureOneRoundIfInfiniteAmmo();

            // Always keep the current local gun class in sync with feature toggles.
            var gi = CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory?.ActiveInventoryItem as GunInventoryItem;
            if (gi?.GunClass != null)
                PatchClass(gi.GunClass);

            // Rapid-fire: Shrink the ACTIVE item's timers (don't replace, just reduce MaxTime once).
            if (_rapidFireOn)
            {
                var item = CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory?.ActiveInventoryItem;
                if (item != null)
                {
                    foreach (var t in CooldownBridge.EnumerateAllTimers(item))
                    {
                        if (t == null) continue;

                        if (!_savedTimerMax.ContainsKey(t))
                            _savedTimerMax[t] = CooldownBridge.GetMaxTime(t);

                        // Use a tiny-but-nonzero MaxTime to avoid edge-cases in timer math.
                        if (CooldownBridge.GetMaxTime(t) > TimeSpan.FromMilliseconds(0.01))
                            CooldownBridge.SetMaxTime(t, TimeSpan.FromMilliseconds(0.01));

                        // IMPORTANT: Do NOT Reset here. Let the game reset naturally when a shot occurs.
                    }
                }
            }
        }

        #region Infinite Ammo: Safety Top-Up

        /// <summary>
        /// If "infinite ammo" is enabled and the active gun's clip is empty,
        /// quietly add a single round so the next click/hold can fire.
        /// </summary>
        private static void EnsureOneRoundIfInfiniteAmmo()
        {
            // Only do anything when the infinite-ammo toggle is active.
            if (!CastleWallsMk2._noConsumeAmmo) return;

            // Resolve local inventory and ensure the active item is a gun.
            var game = CastleMinerZGame.Instance;
            var inv = game?.LocalPlayer?.PlayerInventory;
            if (!(inv?.ActiveInventoryItem is GunInventoryItem gi)) return;

            // Cache the player (for flags) and compute a safe clip capacity.
            var lp = game.LocalPlayer;
            int cap = Math.Max(1, gi.GunClass?.ClipCapacity ?? 1);

            // If the clip is empty, top it up to exactly one round.
            if (gi.RoundsInClip == 0)
            {
                gi.RoundsInClip = Math.Min(1, cap); // Guarantee at least 1 without exceeding capacity.
                lp.Reloading = false;               // Prevent the animation state machine from forcing a reload.
            }

            // NOTE: Launchers/rockets use StackCount instead of RoundsInClip.
        }
        #endregion

        #endregion

        #region Internal Patch Helpers

        /// <summary>
        /// Apply feature toggles to a gun class:
        /// - SuperStats: Boost numbers (never touch Automatic here).
        /// - RapidFire : Set Automatic=true while enabled; restore to factory when disabled.
        /// </summary>
        private static void PatchClass(GunInventoryItemClass cls)
        {
            if (cls == null) return;

            // Snapshot once per class as soon as we touch it.
            if (!_factory.ContainsKey(cls))
                _factory[cls] = Snapshot(cls);

            // Super stats: Massive damage, no recoil, perfect accuracy, etc.
            // (Leave Automatic alone here.)
            if (_superGunStatsOn)
            {
                cls.EnemyDamage           = float.MaxValue;
                cls.Velocity              = 500000f;
                cls.FlightTime            = 500000f;
                cls.Recoil                = default;
                cls.MinInnaccuracy        = default;
                cls.MaxInnaccuracy        = default;
                cls.ShoulderedMaxAccuracy = default;
                cls.ShoulderedMinAccuracy = default;
                cls.ItemSelfDamagePerUse  = 0f;
            }

            // Rapid fire controls only the Automatic flag.
            cls.Automatic = _rapidFireOn || _factory[cls].Automatic;
        }

        /// <summary>Restore only the 'Automatic' flag on every class we've touched.</summary>
        private static void RestoreRapidFireOnly()
        {
            foreach (var kv in _factory)
            {
                var cls = kv.Key;
                var f   = kv.Value;
                cls.Automatic = f.Automatic;
            }
        }

        /// <summary>Restore ONLY the super-stat fields (do not change 'Automatic').</summary>
        private static void RestoreSuperStatsOnly()
        {
            foreach (var kv in _factory)
            {
                var c = kv.Key; var f = kv.Value;
                c.EnemyDamage           = f.EnemyDamage;
                c.Velocity              = f.Velocity;
                c.FlightTime            = f.FlightTime;
                c.Recoil                = f.Recoil;
                c.MinInnaccuracy        = f.MinInnaccuracy;
                c.MaxInnaccuracy        = f.MaxInnaccuracy;
                c.ShoulderedMaxAccuracy = f.ShoulderedMaxAccuracy;
                c.ShoulderedMinAccuracy = f.ShoulderedMinAccuracy;
                c.ClipCapacity          = f.ClipCapacity;
                c.ItemSelfDamagePerUse  = f.ItemSelfDamagePerUse;
                // NOTE: Do NOT touch c.Automatic here.
            }
        }
        #endregion
    }

    #region LocalPlayer (Message Hook)

    /// <summary>
    /// Example: React to the game's "LocalPlayerFiredGun" message.
    /// Currently only handles infinite ammo bookkeeping.
    /// </summary>
    internal sealed class LocalPlayer : IGameMessageHandler
    {
        public void HandleMessage(GameMessageType type, object data, object sender)
        {
            if (type != GameMessageType.LocalPlayerFiredGun) return;

            var inv = CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory;
            if (!(inv?.ActiveInventoryItem is GunInventoryItem gi)) return;

            int cap = Math.Max(1, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);

            if (CastleWallsMk2._noConsumeAmmo)
            {
                // Infinite ammo: Refund the consumed round/stack, but never exceed capacity.
                if (gi.GunClass is RocketLauncherInventoryItemClass || gi.GunClass is RocketLauncherGuidedInventoryItemClass)
                {
                    if (gi.StackCount <= gi.GunClass.MaxStackCount)
                        inv.ActiveInventoryItem.StackCount++;
                }
                else
                {
                    if (gi.RoundsInClip <= cap)
                        gi.RoundsInClip++;
                }
            }
        }
    }
    #endregion
}
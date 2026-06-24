/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Avatars;
using HarmonyLib;
using System;

namespace WeaponAddons
{
    /// <summary>
    /// Logical animation slots supported by WeaponAddons .clag files.
    /// Summary: These map onto the vanilla animation names selected by Player.UpdateAnimation.
    /// </summary>
    internal enum WeaponAddonAnimationKind
    {
        Idle,
        Walk,
        Shoot,
        Reload,
        Shoulder,
        ShoulderIdle,
        ShoulderWalk,
        ShoulderShoot
    }

    /// <summary>
    /// Runtime animation routing for WeaponAddons.
    ///
    /// Summary:
    /// - Manager registers custom AnimationClip assets under unique WA_* names.
    /// - This router maps vanilla animation names to those custom names per final ItemId.
    /// - Harmony patches ask this router whether the current player's held item has a custom route.
    ///
    /// Notes:
    /// - Routes are keyed by final ItemId, so synthetic items can have custom animations without
    ///   changing the base SLOT_ID globally.
    /// - AnimationPlayer.Name is restored to the vanilla name by the Play postfix so the vanilla
    ///   Player.UpdateAnimation state checks do not restart custom clips every frame.
    /// </summary>
    internal static class WeaponAddonAnimationRouter
    {
        #region State

        // ItemId -> (Vanilla animation name -> Custom registered animation name).
        private static readonly Dictionary<InventoryItemIDs, Dictionary<string, string>> _routesByItem =
            new Dictionary<InventoryItemIDs, Dictionary<string, string>>();

        // Player -> current held item ID. Tracked from Player.PutItemInHand.
        private static readonly Dictionary<Player, InventoryItemIDs> _heldItemByPlayer =
            new Dictionary<Player, InventoryItemIDs>();

        private static readonly FieldInfo _avatarField =
            AccessTools.Field(typeof(AvatarAnimationCollection), "_avatar");

        #endregion

        #region Registration / Reset

        /// <summary>
        /// Clears active routes before a reload.
        /// Summary: Registered AvatarAnimationManager clips remain harmless; without routes they are unused.
        /// </summary>
        public static void SoftResetRouting()
        {
            _routesByItem.Clear();
        }

        /// <summary>
        /// Creates a stable per-item animation name for AvatarAnimationManager.
        /// </summary>
        public static string MakeAnimationName(InventoryItemIDs itemId, WeaponAddonAnimationKind kind)
            => "WA_" + ((int)itemId).ToString() + "_" + kind.ToString();

        /// <summary>
        /// Registers a vanilla-name -> custom-name route for one final item ID.
        /// </summary>
        public static void RegisterRoute(InventoryItemIDs itemId, string vanillaName, string customName)
        {
            if (string.IsNullOrWhiteSpace(vanillaName) || string.IsNullOrWhiteSpace(customName))
                return;

            if (!_routesByItem.TryGetValue(itemId, out var routes) || routes == null)
            {
                routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _routesByItem[itemId] = routes;
            }

            routes[vanillaName] = customName;
        }
        #endregion

        #region Held Item Tracking

        /// <summary>
        /// Records the item currently shown in a player's hand.
        /// Summary: Called by the Player.PutItemInHand postfix.
        /// </summary>
        public static void TrackHeldItem(Player player, InventoryItemIDs itemId)
        {
            if (player == null)
                return;

            _heldItemByPlayer[player] = itemId;
        }
        #endregion

        #region Runtime Remap

        /// <summary>
        /// Attempts to remap a vanilla animation name to a custom WeaponAddons animation name.
        ///
        /// Summary:
        /// - Reads the AvatarAnimationCollection's private _avatar field.
        /// - Uses avatar.Tag to recover the owning Player.
        /// - Looks up that player's tracked held ItemId.
        /// - If that item has a route for the requested vanilla name, returns the custom name.
        /// </summary>
        public static bool TryRemap(AvatarAnimationCollection collection, string vanillaName, out string customName)
        {
            customName = null;

            try
            {
                if (collection == null || string.IsNullOrWhiteSpace(vanillaName))
                    return false;

                var player = TryGetPlayer(collection);
                if (player == null)
                    return false;

                if (!_heldItemByPlayer.TryGetValue(player, out var itemId))
                    return false;

                if (!_routesByItem.TryGetValue(itemId, out var routes) || routes == null)
                    return false;

                return routes.TryGetValue(vanillaName, out customName) &&
                       !string.IsNullOrWhiteSpace(customName);
            }
            catch
            {
                customName = null;
                return false;
            }
        }

        private static Player TryGetPlayer(AvatarAnimationCollection collection)
        {
            try
            {
                var avatar = _avatarField?.GetValue(collection) as Avatar;
                return avatar?.Tag as Player;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Vanilla Animation Name Mapping

        /// <summary>
        /// Resolves the vanilla animation name used by the final item ID for a logical animation kind.
        /// Summary: Uses the item's PlayerAnimationMode so synthetic clones inherit the correct base animation set.
        /// </summary>
        public static bool TryGetVanillaAnimationName(InventoryItemIDs itemId, WeaponAddonAnimationKind kind, out string name)
        {
            name = null;

            try
            {
                var cls = InventoryItem.GetClass(itemId);
                if (cls == null)
                    return false;

                return TryGetVanillaAnimationName(cls.PlayerAnimationMode, kind, out name);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetVanillaAnimationName(PlayerMode mode, WeaponAddonAnimationKind kind, out string name)
        {
            name = null;

            switch (mode)
            {
                case PlayerMode.Assault:
                    return Pick(kind, "GunIdle", "GunRun", "GunShoot", "GunReload",
                                     "GunShoulder", "GunShoulderIdle", "GunShoulderWalk", "GunShoulderShoot", out name);

                case PlayerMode.BoltRifle:
                    return Pick(kind, "RifleIdle", "RifleWalk", "RifleShoot", "RifleReload",
                                     "RifleShoulder", "RifleShoulderIdle", "RifleShoulderWalk", "RifleShoulderShoot", out name);

                case PlayerMode.Pistol:
                    return Pick(kind, "PistolIdle", "PistolWalk", "PistolShoot", "PistolReload",
                                     "PistolShoulder", "PistolShoulderIdle", "PistolShoulderWalk", "PistolShoulderShoot", out name);

                case PlayerMode.PumpShotgun:
                    return Pick(kind, "PumpShotgunIdle", "PumpShotgunRun", "PumpShotgunShoot", "PumpShotgunReload",
                                     "PumpShotgunShoulder", "PumpShotgunShoulderIdle", "PumpShotgunShoulderWalk", "PumpShotgunShoulderShoot", out name);

                case PlayerMode.SMG:
                    return Pick(kind, "SMGIdle", "SMGWalk", "SMGShoot", "SMGReload",
                                     "SMGShoulder", "SMGShoulderIdle", "SMGShoulderWalk", "SMGShoulderShoot", out name);

                case PlayerMode.LMG:
                    return Pick(kind, "LMGIdle", "LMGWalk", "LMGShoot", "LMGReload",
                                     "LMGShoulder", "LMGShoulderIdle", "LMGShoulderWalk", "LMGShoulderShoot", out name);

                case PlayerMode.SpaceAssault:
                    return Pick(kind, "LaserGunIdle", "LaserGunRun", "LaserGunShoot", "LaserGunReload",
                                     "LaserGunShoulder", "LaserGunShoulderIdle", "LaserGunShoulderWalk", "LaserGunShoulderShoot", out name);

                case PlayerMode.SpaceBoltRifle:
                    return Pick(kind, "LaserRifleIdle", "LaserRifleRun", "LaserRifleShoot", "LaserRifleReload",
                                     "LaserRifleShoulder", "LaserRifleShoulderIdle", "LaserRifleShoulderWalk", "LaserRifleShoulderShoot", out name);

                case PlayerMode.SpacePistol:
                    return Pick(kind, "LaserPistolIdle", "LaserPistolRun", "LaserPistolShoot", "LaserPistolReload",
                                     "LaserPistolShoulder", "LaserPistolShoulderIdle", "LaserPistolShoulderWalk", "LaserPistolShoulderShoot", out name);

                case PlayerMode.SpacePumpShotgun:
                    return Pick(kind, "LaserGunIdle", "LaserGunRun", "LaserShotgunShoot", "LaserShotgunReload",
                                     "LaserGunShoulder", "LaserGunShoulderIdle", "LaserGunShoulderWalk", "LaserShotgunShoulderShoot", out name);

                case PlayerMode.SpaceSMG:
                    return Pick(kind, "LaserSMGIdle", "LaserSMGRun", "LaserSMGShoot", "LaserSMGReload",
                                     "LaserSMGShoulder", "LaserSMGShoulderIdle", "LaserSMGShoulderWalk", "LaserSMGShoulderShoot", out name);

                case PlayerMode.RPG:
                    return Pick(kind, "RPGIdle", "RPGWalk", "RPGShoot", "PumpShotgunReload",
                                     "GunShoulder", "GunShoulderIdle", "GunShoulderWalk", "PumpShotgunShoulderShoot", out name);

                case PlayerMode.LaserDrill:
                    return Pick(kind, "LaserDrillIdle", "LaserDrillRun", "LaserDrillShoot", "LaserDrillReload",
                                     "LaserDrillShoulder", "LaserDrillShoulderIdle", "LaserDrillShoulderWalk", "LaserDrillShoulderShoot", out name);

                default:
                    return false;
            }
        }

        private static bool Pick(
            WeaponAddonAnimationKind kind,
            string idle,
            string walk,
            string shoot,
            string reload,
            string shoulder,
            string shoulderIdle,
            string shoulderWalk,
            string shoulderShoot,
            out string name)
        {
            switch (kind)
            {
                case WeaponAddonAnimationKind.Idle:
                    name = idle;
                    return true;

                case WeaponAddonAnimationKind.Walk:
                    name = walk;
                    return true;

                case WeaponAddonAnimationKind.Shoot:
                    name = shoot;
                    return true;

                case WeaponAddonAnimationKind.Reload:
                    name = reload;
                    return true;

                case WeaponAddonAnimationKind.Shoulder:
                    name = shoulder;
                    return true;

                case WeaponAddonAnimationKind.ShoulderIdle:
                    name = shoulderIdle;
                    return true;

                case WeaponAddonAnimationKind.ShoulderWalk:
                    name = shoulderWalk;
                    return true;

                case WeaponAddonAnimationKind.ShoulderShoot:
                    name = shoulderShoot;
                    return true;

                default:
                    name = null;
                    return false;
            }
        }
        #endregion
    }
}
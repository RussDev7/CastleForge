/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using System.Reflection;
using DNA.CastleMinerZ;
using HarmonyLib;
using DNA.Net;
using System;
using DNA;

namespace CastleWallsMk2
{
    /// <summary>
    /// Provides vanilla-client compatible teleport and spawn-moving helpers for CastleWalls Mk2.
    /// </summary>
    /// <remarks>
    /// This runtime intentionally avoids custom network messages so vanilla clients can still process
    /// the packets. It reuses vanilla inventory/spawn and private damage messages to force vanilla behavior.
    ///
    /// Supported button flows:
    ///
    /// - Teleport Player Here:
    ///   Temporarily gives the target a one-time personal spawn at the local player's location,
    ///   silently damages them through vanilla fireball damage, then host restores inventory/spawn state.
    ///
    /// - Move Players Spawn:
    ///   Permanently changes the selected player's personal spawn to the local player's location.
    ///   This does not kill, respawn, or immediately teleport the target.
    ///
    /// - Teleport All Here:
    ///   Runs Teleport Player Here for every remote player.
    ///
    /// - Move World Spawn:
    ///   Moves the local static world default spawn and pushes the same location as a personal spawn
    ///   to current players for vanilla-client compatibility.
    ///
    /// Important behavior:
    /// - Host usage preserves remote inventories because the host can snapshot target inventories.
    /// - Non-host usage is destructive/test-only because non-hosts cannot read remote inventories.
    /// - Remote vanilla clients do not need CastleWallsMk2 installed.
    /// </remarks>
    internal static class PlayerTeleportRuntime
    {
        #region Settings

        /// <summary>
        /// Offset added to the local player's position when pulling players here.
        /// This prevents targets from respawning directly inside the caller.
        /// </summary>
        private static readonly Vector3 HereOffset = new Vector3(1.5f, 0f, 1.5f);

        /// <summary>
        /// Number of private fireball damage packets sent during a forced teleport respawn.
        /// </summary>
        private const int SilentFireballDamagePackets = 20;

        /// <summary>
        /// Controls how host-safe one-time teleports restore the target's personal spawn after respawn.
        /// </summary>
        /// <remarks>
        /// true:
        ///   After the forced teleport respawn, the target's personal spawn is cleared so future deaths
        ///   use the world/default spawn.
        ///
        /// false:
        ///   After the forced teleport respawn, the target's previous personal spawn item is restored.
        /// </remarks>
        private const bool ClearSpawnAfterHostTeleport = true;

        #endregion

        #region Reflected Vanilla Message Handles

        /// <summary>
        /// Reflected vanilla InventoryRetrieveFromServerMessage type.
        /// </summary>
        /// <remarks>
        /// This message is used because vanilla clients already understand it. Direct-sending it lets this
        /// runtime replace a target's inventory payload without introducing a custom modded message type.
        /// </remarks>
        private static readonly Type InvRetrieveMsgType =
            AccessTools.TypeByName("DNA.CastleMinerZ.Net.InventoryRetrieveFromServerMessage");

        /// <summary>
        /// Reflected InventoryRetrieveFromServerMessage.Inventory field.
        /// </summary>
        private static readonly FieldInfo InvField_Inventory =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "Inventory") : null;

        /// <summary>
        /// Reflected InventoryRetrieveFromServerMessage.playerID field.
        /// </summary>
        private static readonly FieldInfo InvField_PlayerID =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "playerID") : null;

        /// <summary>
        /// Reflected InventoryRetrieveFromServerMessage.Default field.
        /// </summary>
        private static readonly FieldInfo InvField_Default =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "Default") : null;

        /// <summary>
        /// Reflected BlockInventoryItem._pointToLocation field.
        /// </summary>
        /// <remarks>
        /// Vanilla spawn/teleport station items store their target position in this private field.
        /// </remarks>
        private static readonly FieldInfo BlockItem_PointToLocation =
            AccessTools.Field(typeof(BlockInventoryItem), "_pointToLocation");

        #endregion

        #region Host Restore State

        /// <summary>
        /// Tracks one host-safe teleport restore operation.
        /// </summary>
        /// <remarks>
        /// During host-safe teleport, the target temporarily receives an empty inventory with only a
        /// spawn point. This state stores the real inventory payload so it can be restored after the
        /// target has gone through the vanilla dead -> alive respawn flow.
        /// </remarks>
        private sealed class PendingHostRestore
        {
            /// <summary>
            /// Network gamer ID of the target whose inventory must be restored.
            /// </summary>
            public byte TargetId;

            /// <summary>
            /// Local gamer sending the restore payload.
            /// </summary>
            public LocalNetworkGamer From;

            /// <summary>
            /// Network gamer receiving the restore payload.
            /// </summary>
            public NetworkGamer Target;

            /// <summary>
            /// Host-side Player object for the target.
            /// </summary>
            public Player TargetPlayer;

            /// <summary>
            /// Host-snapshotted inventory payload to restore after respawn.
            /// </summary>
            public PlayerInventory RestorePayload;

            /// <summary>
            /// UTC time when the restore was queued.
            /// </summary>
            public DateTime StartedUtc;

            /// <summary>
            /// True after the host observes the target in the Dead state at least once.
            /// </summary>
            public bool SawDead;
        }

        /// <summary>
        /// Pending host-safe inventory restores waiting for a target dead -> alive transition.
        /// </summary>
        private static readonly List<PendingHostRestore> _pendingHostRestores =
            new List<PendingHostRestore>();

        /// <summary>
        /// Safety timeout for restoring inventory if the target never completes the expected respawn flow.
        /// </summary>
        /// <remarks>
        /// This avoids leaving a player on the temporary empty teleport inventory if they disconnect,
        /// refuse to respawn, or the host never observes the dead -> alive transition.
        /// </remarks>
        private static readonly TimeSpan HostRestoreTimeout = TimeSpan.FromSeconds(90);

        #endregion

        #region Public Entrypoints - Teleport

        /// <summary>
        /// Pulls one selected player to the local player's current position.
        /// </summary>
        /// <param name="target">Target gamer to pull here.</param>
        /// <param name="result">Human-readable success or failure message.</param>
        /// <returns>true if the teleport flow was started successfully; otherwise false.</returns>
        /// <remarks>
        /// Used by the "Teleport Player Here" button.
        /// </remarks>
        public static bool TryTeleportHere(NetworkGamer target, out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: true, out Player me, out _, out result))
                return false;

            if (target == null)
            {
                result = "No target selected.";
                return false;
            }

            Vector3 destination = me.LocalPosition + HereOffset;
            return TryTeleportTo(target, destination, out result);
        }

        /// <summary>
        /// Pulls every remote player to the local player's current position.
        /// </summary>
        /// <param name="result">Human-readable summary of how many players were targeted.</param>
        /// <returns>The number of remote players that had the teleport flow started.</returns>
        /// <remarks>
        /// Used by the "Teleport All Here" button.
        /// Local player is skipped because the point of this flow is to pull remote players to the caller.
        /// </remarks>
        public static int TeleportAllHere(out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: true, out Player me, out _, out result))
                return 0;

            Vector3 destination = me.LocalPosition + HereOffset;

            int sent = 0;
            foreach (NetworkGamer gamer in game.CurrentNetworkSession.AllGamers)
            {
                if (gamer == null || gamer.IsLocal)
                    continue;

                if (TryTeleportTo(gamer, destination, out _))
                    sent++;
            }

            result = IsLocalHost(game)
                ? $"Started host-safe teleport for {sent} player(s). Inventories will restore after respawn."
                : $"Started non-host teleport for {sent} player(s). WARNING: inventories are not preserved as non-host.";

            return sent;
        }

        /// <summary>
        /// Teleports one target to an explicit destination using the vanilla respawn fallback.
        /// </summary>
        /// <param name="target">Target gamer to teleport.</param>
        /// <param name="destination">Destination position used as the temporary respawn point.</param>
        /// <param name="result">Human-readable success or failure message.</param>
        /// <returns>true if the teleport flow was started successfully; otherwise false.</returns>
        /// <remarks>
        /// Remote vanilla clients cannot be directly moved with PlayerUpdateMessage. This method instead:
        /// 1. Sends a temporary inventory payload with a spawn item pointing to <paramref name="destination"/>.
        /// 2. Sends repeated private DetonateFireballMessage damage packets to force vanilla death/respawn.
        /// 3. If local player is host, restores the target inventory after respawn.
        /// </remarks>
        public static bool TryTeleportTo(NetworkGamer target, Vector3 destination, out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: true, out _, out LocalNetworkGamer from, out result))
                return false;

            if (target == null)
            {
                result = "No target selected.";
                return false;
            }

            if (target.IsLocal)
            {
                game.GameScreen.TeleportToLocation(destination, false);
                result = "Teleported local player.";
                return true;
            }

            if (!(target.Tag is Player targetPlayer))
            {
                result = $"Target '{target.Gamertag}' is not ready.";
                return false;
            }

            bool localPlayerIsHost = IsLocalHost(game);

            if (localPlayerIsHost)
            {
                if (HasPendingHostRestore(target.Id))
                {
                    result = $"A teleport restore is already pending for {target.Gamertag}. Wait for them to respawn first.";
                    return false;
                }

                PlayerInventory restorePayload =
                    BuildInventoryPayloadCopy(
                        targetPlayer,
                        targetPlayer.PlayerInventory,
                        clearSpawnPoint: ClearSpawnAfterHostTeleport);

                if (restorePayload == null)
                {
                    result = $"Could not snapshot inventory for {target.Gamertag}.";
                    return false;
                }

                PlayerInventory tempPayload =
                    BuildEmptyTeleportInventoryPayload(targetPlayer, destination);

                if (tempPayload == null)
                {
                    result = "Failed to build temporary teleport payload.";
                    return false;
                }

                QueueHostRestore(from, target, targetPlayer, restorePayload);

                // Send temporary empty inventory with only the desired spawn point.
                SendInventoryRetrieveDirect(from, target, tempPayload, target.Id);

                // Silent-style forced death/respawn.
                SendPrivateFireballDamageBurst(from, target, DragonTypeEnum.SKELETON);

                result = $"Started host-safe teleport for {target.Gamertag}. Inventory will restore after respawn.";
                return true;
            }

            // Non-host/test fallback.
            PlayerInventory destructiveTempPayload =
                BuildEmptyTeleportInventoryPayload(targetPlayer, destination);

            if (destructiveTempPayload == null)
            {
                result = "Failed to build temporary teleport payload.";
                return false;
            }

            SendInventoryRetrieveDirect(from, target, destructiveTempPayload, target.Id);

            // Silent-style forced death/respawn.
            SendPrivateFireballDamageBurst(from, target, DragonTypeEnum.SKELETON);

            result = $"Started non-host teleport for {target.Gamertag}. WARNING: target inventory is not preserved.";
            return true;
        }
        #endregion

        #region Public Entrypoints - Move Spawn

        /// <summary>
        /// Permanently moves one selected player's personal spawn to the local player's current position.
        /// </summary>
        /// <param name="target">Target gamer whose personal spawn should be moved.</param>
        /// <param name="result">Human-readable success or failure message.</param>
        /// <returns>true if the spawn payload was sent/applied successfully; otherwise false.</returns>
        /// <remarks>
        /// Used by the selected-player "Move Players Spawn" button.
        /// This does not kill, damage, respawn, or teleport the target immediately.
        /// </remarks>
        public static bool TryMovePlayerSpawnHere(NetworkGamer target, out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: false, out Player me, out _, out result))
                return false;

            if (target == null)
            {
                result = "No target selected.";
                return false;
            }

            Vector3 destination = me.LocalPosition + HereOffset;
            return TryMovePlayerSpawnTo(target, destination, out result);
        }

        /// <summary>
        /// Permanently moves one selected player's personal spawn to an explicit destination.
        /// </summary>
        /// <param name="target">Target gamer whose personal spawn should be moved.</param>
        /// <param name="destination">New personal spawn location.</param>
        /// <param name="result">Human-readable success or failure message.</param>
        /// <returns>true if the spawn payload was sent/applied successfully; otherwise false.</returns>
        /// <remarks>
        /// Host path copies the target inventory and replaces only the personal spawn item.
        /// Non-host path copies the local player's inventory because the target inventory is not readable,
        /// so non-host usage is destructive/test-only.
        ///
        /// This method intentionally does not call SendPrivateFireballDamageBurst.
        /// </remarks>
        public static bool TryMovePlayerSpawnTo(NetworkGamer target, Vector3 destination, out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: false, out Player me, out LocalNetworkGamer from, out result))
                return false;

            if (target == null)
            {
                result = "No target selected.";
                return false;
            }

            if (target.IsLocal)
            {
                if (me.PlayerInventory == null)
                {
                    result = "Local inventory is not ready.";
                    return false;
                }

                me.PlayerInventory.InventorySpawnPointTeleport = CreateSpawnPointItem(destination);
                result = "Moved local player's personal spawn.";
                return true;
            }

            if (!(target.Tag is Player targetPlayer))
            {
                result = $"Target '{target.Gamertag}' is not ready.";
                return false;
            }

            bool localPlayerIsHost = IsLocalHost(game);

            PlayerInventory sourceInventory = localPlayerIsHost
                ? targetPlayer.PlayerInventory
                : me.PlayerInventory;

            if (sourceInventory == null)
            {
                result = "Source inventory is not available.";
                return false;
            }

            PlayerInventory payload =
                BuildInventoryPayloadWithSpawn(targetPlayer, sourceInventory, destination);

            if (payload == null)
            {
                result = "Failed to build spawn payload.";
                return false;
            }

            SendInventoryRetrieveDirect(from, target, payload, target.Id);

            result = localPlayerIsHost
                ? $"Moved {target.Gamertag}'s personal spawn. Inventory preserved."
                : $"Moved {target.Gamertag}'s personal spawn. WARNING: non-host path cannot preserve inventory.";

            return true;
        }

        /// <summary>
        /// Permanently moves every current player's personal spawn to the local player's current position.
        /// </summary>
        /// <param name="result">Human-readable summary of how many player spawns were moved.</param>
        /// <returns>true if the operation completed its loop; otherwise false.</returns>
        /// <remarks>
        /// Suggested callback: OnMoveAllPlayersSpawn.
        /// Includes the local player and all remote players.
        ///
        /// This method does not kill, damage, respawn, or teleport players immediately.
        /// </remarks>
        public static bool TryMoveAllPlayerSpawnsHere(out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: false, out Player me, out _, out result))
                return false;

            Vector3 destination = me.LocalPosition + HereOffset;

            int moved = 0;
            foreach (NetworkGamer gamer in game.CurrentNetworkSession.AllGamers)
            {
                if (gamer == null)
                    continue;

                if (TryMovePlayerSpawnTo(gamer, destination, out _))
                    moved++;
            }

            result = IsLocalHost(game)
                ? $"Moved personal spawn for {moved} player(s). Inventories preserved."
                : $"Moved personal spawn for {moved} player(s). WARNING: non-host path cannot preserve remote inventories.";

            return true;
        }

        /// <summary>
        /// Moves the local world default spawn and pushes the same location as a personal spawn
        /// to all current players.
        /// </summary>
        /// <param name="result">Human-readable success or failure message.</param>
        /// <returns>true if the world spawn move and player-spawn push completed; otherwise false.</returns>
        /// <remarks>
        /// Used by the "Move World Spawn" button.
        /// WorldInfo.DefaultStartLocation is local/static, so vanilla remote clients will not automatically
        /// receive the static world spawn change. To support vanilla clients, this also sends a personal
        /// spawn payload to each current player.
        ///
        /// This method does not kill, damage, respawn, or teleport players immediately.
        /// </remarks>
        public static bool TryMoveWorldSpawnHere(out string result)
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (!TryGetLocalState(game, requireGameScreen: false, out Player me, out _, out result))
                return false;

            Vector3 destination = me.LocalPosition + HereOffset;

            // This updates the local/static default spawn used by this game instance.
            // Vanilla remote clients will not automatically receive this static value,
            // so we also push a personal spawn to current players below.
            WorldInfo.DefaultStartLocation = destination;

            int moved = 0;
            foreach (NetworkGamer gamer in game.CurrentNetworkSession.AllGamers)
            {
                if (gamer == null)
                    continue;

                if (TryMovePlayerSpawnTo(gamer, destination, out _))
                    moved++;
            }

            result = IsLocalHost(game)
                ? $"Moved local world default spawn and pushed personal spawn to {moved} player(s). Inventories preserved."
                : $"Moved local world default spawn and pushed personal spawn to {moved} player(s). WARNING: non-host path cannot preserve remote inventories.";

            return true;
        }
        #endregion

        #region Public Entrypoints - Tick

        /// <summary>
        /// Processes pending host-safe teleport restores.
        /// </summary>
        /// <remarks>
        /// Must be called once per mod tick.
        /// The restore is delayed until the host sees the target go dead -> alive so the temporary spawn
        /// remains active long enough for vanilla RespawnPlayer() to use it.
        /// </remarks>
        public static void Tick()
        {
            if (_pendingHostRestores.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;

            for (int i = _pendingHostRestores.Count - 1; i >= 0; i--)
            {
                PendingHostRestore pending = _pendingHostRestores[i];

                if (pending == null ||
                    pending.Target == null ||
                    pending.Target.HasLeftSession)
                {
                    _pendingHostRestores.RemoveAt(i);
                    continue;
                }

                if (pending.Target.Tag is Player currentTargetPlayer)
                    pending.TargetPlayer = currentTargetPlayer;

                if (pending.TargetPlayer != null)
                {
                    if (pending.TargetPlayer.Dead)
                        pending.SawDead = true;

                    // Restore only after the target has gone dead -> alive.
                    // Restoring too early would clear the temporary spawn before vanilla uses it.
                    if (pending.SawDead && !pending.TargetPlayer.Dead)
                    {
                        RestorePendingHostInventory(pending);
                        _pendingHostRestores.RemoveAt(i);
                        continue;
                    }
                }

                if (now - pending.StartedUtc > HostRestoreTimeout)
                {
                    RestorePendingHostInventory(pending);
                    _pendingHostRestores.RemoveAt(i);
                }
            }
        }
        #endregion

        #region Host Restore Helpers

        /// <summary>
        /// Checks whether a host-safe teleport restore is already pending for a target.
        /// </summary>
        /// <param name="targetId">Network gamer ID to check.</param>
        /// <returns>true if a pending restore exists for the target; otherwise false.</returns>
        private static bool HasPendingHostRestore(byte targetId)
        {
            for (int i = 0; i < _pendingHostRestores.Count; i++)
            {
                PendingHostRestore pending = _pendingHostRestores[i];
                if (pending != null && pending.TargetId == targetId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Queues a host-side inventory restore for a target after a forced teleport respawn.
        /// </summary>
        /// <param name="from">Local host gamer that will send the restore payload.</param>
        /// <param name="target">Remote gamer that should receive the restore payload.</param>
        /// <param name="targetPlayer">Host-side player object for the target.</param>
        /// <param name="restorePayload">Inventory payload to restore after respawn.</param>
        private static void QueueHostRestore(
            LocalNetworkGamer from,
            NetworkGamer target,
            Player targetPlayer,
            PlayerInventory restorePayload)
        {
            if (from == null || target == null || targetPlayer == null || restorePayload == null)
                return;

            _pendingHostRestores.Add(new PendingHostRestore
            {
                TargetId = target.Id,
                From = from,
                Target = target,
                TargetPlayer = targetPlayer,
                RestorePayload = restorePayload,
                StartedUtc = DateTime.UtcNow,
                SawDead = false
            });
        }

        /// <summary>
        /// Sends a pending host-snapshotted inventory payload back to its target.
        /// </summary>
        /// <param name="pending">Pending restore entry to process.</param>
        private static void RestorePendingHostInventory(PendingHostRestore pending)
        {
            if (pending == null ||
                pending.From == null ||
                pending.Target == null ||
                pending.RestorePayload == null)
                return;

            SendInventoryRetrieveDirect(
                pending.From,
                pending.Target,
                pending.RestorePayload,
                pending.Target.Id);
        }

        #endregion

        #region Inventory Payload Construction

        /// <summary>
        /// Builds a temporary empty inventory payload with only a personal spawn point.
        /// </summary>
        /// <param name="owner">Player object that owns the created inventory payload.</param>
        /// <param name="destination">Temporary spawn destination.</param>
        /// <returns>A temporary inventory payload, or null if creation failed.</returns>
        /// <remarks>
        /// Used by Teleport Player Here and Teleport All Here.
        /// The payload is intentionally empty so forced death does not drop/duplicate the real inventory.
        /// Host mode restores the real inventory afterward.
        /// </remarks>
        private static PlayerInventory BuildEmptyTeleportInventoryPayload(Player owner, Vector3 destination)
        {
            if (owner == null)
                return null;

            PlayerInventory payload = new PlayerInventory(owner, false);

            ClearInventoryLayout(payload);
            payload.InventorySpawnPointTeleport = CreateSpawnPointItem(destination);

            payload.DiscoverRecipies();
            return payload;
        }

        /// <summary>
        /// Builds a full copy of an inventory payload.
        /// </summary>
        /// <param name="owner">Player object that owns the created inventory payload.</param>
        /// <param name="sourceInventory">Inventory to copy from.</param>
        /// <param name="clearSpawnPoint">
        /// true to clear the restored personal spawn; false to preserve the source personal spawn.
        /// </param>
        /// <returns>A copied inventory payload, or null if creation failed.</returns>
        /// <remarks>
        /// Used by the host restore path after forced teleport respawn.
        /// </remarks>
        private static PlayerInventory BuildInventoryPayloadCopy(
            Player owner,
            PlayerInventory sourceInventory,
            bool clearSpawnPoint)
        {
            if (owner == null || sourceInventory == null)
                return null;

            PlayerInventory payload = new PlayerInventory(owner, false);

            ClearInventoryLayout(payload);
            CopyInventoryLayout(sourceInventory, payload);
            CopyTeleportStations(sourceInventory, payload);

            payload.InventorySpawnPointTeleport = clearSpawnPoint
                ? null
                : CloneItem(sourceInventory.InventorySpawnPointTeleport) as BlockInventoryItem;

            payload.DiscoverRecipies();
            return payload;
        }

        /// <summary>
        /// Builds a full inventory payload while replacing the personal spawn point.
        /// </summary>
        /// <param name="owner">Player object that owns the created inventory payload.</param>
        /// <param name="sourceInventory">Inventory to copy before replacing the spawn point.</param>
        /// <param name="destination">New personal spawn destination.</param>
        /// <returns>A copied inventory payload with a replaced personal spawn, or null if creation failed.</returns>
        /// <remarks>
        /// Used by Move Players Spawn and Move World Spawn.
        /// </remarks>
        private static PlayerInventory BuildInventoryPayloadWithSpawn(
            Player owner,
            PlayerInventory sourceInventory,
            Vector3 destination)
        {
            if (owner == null || sourceInventory == null)
                return null;

            PlayerInventory payload = new PlayerInventory(owner, false);

            ClearInventoryLayout(payload);
            CopyInventoryLayout(sourceInventory, payload);
            CopyTeleportStations(sourceInventory, payload);

            payload.InventorySpawnPointTeleport = CreateSpawnPointItem(destination);

            payload.DiscoverRecipies();
            return payload;
        }

        /// <summary>
        /// Creates a vanilla SpawnBasic inventory item pointing at a specific world position.
        /// </summary>
        /// <param name="destination">World position the spawn item should point to.</param>
        /// <returns>A configured spawn item, or null if creation failed.</returns>
        private static BlockInventoryItem CreateSpawnPointItem(Vector3 destination)
        {
            if (!(InventoryItem.CreateItem(InventoryItemIDs.SpawnBasic, 1) is BlockInventoryItem spawn))
                return null;

            BlockItem_PointToLocation?.SetValue(spawn, destination);
            return spawn;
        }

        /// <summary>
        /// Clears backpack slots, tray slots, teleport stations, and the personal spawn item.
        /// </summary>
        /// <param name="inventory">Inventory payload to clear.</param>
        private static void ClearInventoryLayout(PlayerInventory inventory)
        {
            if (inventory == null)
                return;

            if (inventory.Inventory != null)
            {
                for (int i = 0; i < inventory.Inventory.Length; i++)
                    inventory.Inventory[i] = null;
            }

            if (inventory.TrayManager != null &&
                inventory.TrayManager.Trays != null)
            {
                int xLen = inventory.TrayManager.Trays.GetLength(0);
                int yLen = inventory.TrayManager.Trays.GetLength(1);

                for (int x = 0; x < xLen; x++)
                {
                    for (int y = 0; y < yLen; y++)
                        inventory.TrayManager.Trays[x, y] = null;
                }
            }

            inventory.TeleportStationObjects?.Clear();
            inventory.InventorySpawnPointTeleport = null;
        }

        /// <summary>
        /// Copies backpack and tray items from one inventory payload to another.
        /// </summary>
        /// <param name="src">Source inventory.</param>
        /// <param name="dst">Destination inventory.</param>
        private static void CopyInventoryLayout(PlayerInventory src, PlayerInventory dst)
        {
            if (src == null || dst == null)
                return;

            if (src.Inventory != null && dst.Inventory != null)
            {
                int len = Math.Min(src.Inventory.Length, dst.Inventory.Length);
                for (int i = 0; i < len; i++)
                    dst.Inventory[i] = CloneItem(src.Inventory[i]);
            }

            if (src.TrayManager != null &&
                dst.TrayManager != null &&
                src.TrayManager.Trays != null &&
                dst.TrayManager.Trays != null)
            {
                int xLen = Math.Min(src.TrayManager.Trays.GetLength(0), dst.TrayManager.Trays.GetLength(0));
                int yLen = Math.Min(src.TrayManager.Trays.GetLength(1), dst.TrayManager.Trays.GetLength(1));

                for (int x = 0; x < xLen; x++)
                {
                    for (int y = 0; y < yLen; y++)
                        dst.TrayManager.Trays[x, y] = CloneItem(src.TrayManager.Trays[x, y]);
                }
            }
        }

        /// <summary>
        /// Copies teleport station objects from one inventory payload to another.
        /// </summary>
        /// <param name="src">Source inventory.</param>
        /// <param name="dst">Destination inventory.</param>
        private static void CopyTeleportStations(PlayerInventory src, PlayerInventory dst)
        {
            if (src == null || dst == null || src.TeleportStationObjects == null)
                return;

            dst.TeleportStationObjects.Clear();

            foreach (BlockInventoryItem station in src.TeleportStationObjects)
            {
                if (station == null)
                    continue;

                if (!(InventoryItem.CreateItem(station.ItemClass.ID, station.StackCount) is BlockInventoryItem copy))
                    continue;

                BlockItem_PointToLocation?.SetValue(copy, station.PointToLocation);

                copy.StackCount = station.StackCount;
                copy.ItemHealthLevel = station.ItemHealthLevel;

                dst.TeleportStationObjects.Add(copy);
            }
        }

        /// <summary>
        /// Clones an inventory item while preserving common vanilla item state.
        /// </summary>
        /// <param name="item">Item to clone.</param>
        /// <returns>A cloned item, or null if the source is null or creation failed.</returns>
        /// <remarks>
        /// This preserves common fields used by stackable items, gun ammo state, durability/health,
        /// and block item point-to-location data.
        /// </remarks>
        private static InventoryItem CloneItem(InventoryItem item)
        {
            if (item == null)
                return null;

            InventoryItem copy = InventoryItem.CreateItem(item.ItemClass.ID, item.StackCount);
            if (copy == null)
                return null;

            copy.StackCount = item.StackCount;
            copy.ItemHealthLevel = item.ItemHealthLevel;

            if (item is GunInventoryItem srcGun && copy is GunInventoryItem dstGun)
                dstGun.RoundsInClip = srcGun.RoundsInClip;

            if (item is BlockInventoryItem srcBlock && copy is BlockInventoryItem dstBlock)
                BlockItem_PointToLocation?.SetValue(dstBlock, srcBlock.PointToLocation);

            return copy;
        }
        #endregion

        #region Vanilla Direct Sends

        /// <summary>
        /// Direct-sends a vanilla InventoryRetrieveFromServerMessage to one target.
        /// </summary>
        /// <param name="from">Local gamer sending the message.</param>
        /// <param name="to">Target gamer receiving the message.</param>
        /// <param name="inventory">Inventory payload to apply on the target.</param>
        /// <param name="playerId">Target player ID written into the vanilla message.</param>
        /// <remarks>
        /// This avoids the vanilla static Send helper because the static helper broadcasts.
        /// The direct send is what makes selected-player targeting possible.
        /// </remarks>
        private static void SendInventoryRetrieveDirect(
            LocalNetworkGamer from,
            NetworkGamer to,
            PlayerInventory inventory,
            byte playerId)
        {
            if (from == null || to == null || inventory == null)
                return;

            if (InvRetrieveMsgType == null ||
                InvField_Inventory == null ||
                InvField_PlayerID == null ||
                InvField_Default == null)
                return;

            Message msg = MessageBridge.Get(InvRetrieveMsgType);
            if (msg == null)
                return;

            InvField_Inventory.SetValue(msg, inventory);
            InvField_PlayerID.SetValue(msg, playerId);
            InvField_Default.SetValue(msg, false);

            MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, to });
        }

        /// <summary>
        /// Sends repeated private vanilla fireball damage packets to force a target through vanilla death/respawn.
        /// </summary>
        /// <param name="from">Local gamer sending the damage packets.</param>
        /// <param name="to">Target gamer receiving the damage packets.</param>
        /// <param name="dragonType">Dragon/fireball damage type to use.</param>
        /// <remarks>
        /// This replaces the older private C4 detonation path. It mirrors the quieter style used by
        /// CastleWalls Mk2's existing private fireball damage helpers.
        /// </remarks>
        private static void SendPrivateFireballDamageBurst(
            LocalNetworkGamer from,
            NetworkGamer to,
            DragonTypeEnum dragonType = DragonTypeEnum.SKELETON)
        {
            if (from == null || to == null)
                return;

            for (int i = 0; i < SilentFireballDamagePackets; i++)
                SendFireballDamagePrivate(from, to, dragonType);
        }

        /// <summary>
        /// Sends a private vanilla explosive damage packet to force the target through vanilla death/respawn.
        /// </summary>
        /// <param name="from">Local gamer sending the message.</param>
        /// <param name="to">Target gamer receiving the message.</param>
        /// <param name="targetPosition">Target's current position used as the blast location.</param>
        /// <remarks>
        /// Respawn is the vanilla-compatible part that actually moves the target to the temporary spawn.
        /// </remarks>
        private static void SendPrivateC4Damage(LocalNetworkGamer from, NetworkGamer to, Vector3 targetPosition)
        {
            if (from == null || to == null)
                return;

            DetonateExplosiveMessage msg = MessageBridge.Get<DetonateExplosiveMessage>();

            msg.Location = IntVector3.FromVector3(targetPosition + Vector3.Up);
            msg.OriginalExplosion = false;
            msg.ExplosiveType = ExplosiveTypes.C4;

            MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, to });
        }

        /// <summary>
        /// Sends one private vanilla fireball damage packet to a specific gamer.
        /// </summary>
        /// <param name="from">Local gamer sending the message.</param>
        /// <param name="to">Target gamer receiving the message.</param>
        /// <param name="dragonType">Dragon/fireball damage type to use.</param>
        private static void SendFireballDamagePrivate(
            LocalNetworkGamer from,
            NetworkGamer to,
            DragonTypeEnum dragonType = DragonTypeEnum.SKELETON)
        {
            if (from == null || to == null)
                return;

            DetonateFireballMessage msg = MessageBridge.Get<DetonateFireballMessage>();

            msg.Location = ((Player)to.Tag)?.LocalPosition ?? Vector3.Zero;
            msg.Index = -1;
            msg.NumBlocks = 0;
            msg.BlocksToRemove = new IntVector3[] { };
            msg.EType = dragonType;

            MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, to });
        }
        #endregion

        #region Shared Helpers

        /// <summary>
        /// Validates and returns common local game state required by this runtime.
        /// </summary>
        /// <param name="game">CastleMinerZGame instance to validate.</param>
        /// <param name="requireGameScreen">Whether GameScreen must be available.</param>
        /// <param name="localPlayer">Resolved local player.</param>
        /// <param name="localGamer">Resolved local network gamer.</param>
        /// <param name="result">Failure message if validation fails.</param>
        /// <returns>true if all requested local state is available; otherwise false.</returns>
        private static bool TryGetLocalState(
            CastleMinerZGame game,
            bool requireGameScreen,
            out Player localPlayer,
            out LocalNetworkGamer localGamer,
            out string result)
        {
            localPlayer = null;
            localGamer = null;
            result = string.Empty;

            if (game == null || game.CurrentNetworkSession == null)
            {
                result = "No active network session.";
                return false;
            }

            localPlayer = game.LocalPlayer;
            if (localPlayer == null)
            {
                result = "Local player is not ready.";
                return false;
            }

            if (requireGameScreen && game.GameScreen == null)
            {
                result = "Game screen is not ready.";
                return false;
            }

            localGamer = game.MyNetworkGamer as LocalNetworkGamer;
            if (localGamer == null)
            {
                result = "Local network gamer is not ready.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns whether the local gamer is currently the session host.
        /// </summary>
        /// <param name="game">CastleMinerZGame instance to inspect.</param>
        /// <returns>true if the local gamer exists and is host; otherwise false.</returns>
        private static bool IsLocalHost(CastleMinerZGame game)
        {
            return game != null &&
                   game.MyNetworkGamer != null &&
                   game.MyNetworkGamer.IsHost;
        }
        #endregion
    }
}
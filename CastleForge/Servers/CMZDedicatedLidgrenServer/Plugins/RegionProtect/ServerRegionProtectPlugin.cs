/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer.Plugins.RegionProtect
{
    /// <summary>
    /// Dedicated-server implementation of RegionProtect.
    /// Intercepts host/world mutation packets before the server applies or relays them.
    /// </summary>
    /// <remarks>
    /// This plugin is intended to run inside the dedicated server process, not the client ModLoader.
    /// It protects configured regions from mining, placing, explosions, crate item edits, and crate breaks.
    /// </remarks>
    internal sealed class ServerRegionProtectPlugin : IServerWorldPlugin
    {
        #region Fields

        /// <summary>
        /// Loaded cuboid protection regions for the active world.
        /// </summary>
        private readonly List<ProtectedRegion> _regions = [];

        /// <summary>
        /// Tracks packet types that already failed position lookup so the log is not spammed repeatedly.
        /// </summary>
        private readonly HashSet<string> _missingPositionWarnings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks the most recent warning sent to each player for warning cooldown behavior.
        /// </summary>
        private readonly Dictionary<string, DateTime> _lastWarningByPlayer = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Server log callback supplied by the plugin host.
        /// </summary>
        private Action<string> _log = _ => { };

        /// <summary>
        /// General RegionProtect settings loaded from RegionProtect.Config.ini.
        /// </summary>
        private ServerRegionProtectConfig _config = new();

        /// <summary>
        /// Spawn protection settings loaded from the active world's regions file.
        /// </summary>
        private SpawnProtection _spawn = new();

        /// <summary>
        /// Root plugin directory under the server executable directory.
        /// </summary>
        private string _pluginDir;

        /// <summary>
        /// Full path to the general RegionProtect config file.
        /// </summary>
        private string _configPath;

        /// <summary>
        /// Full path to the active world's RegionProtect regions file.
        /// </summary>
        private string _regionsPath;

        #endregion

        #region Properties

        /// <summary>
        /// Display name used by the server plugin manager.
        /// </summary>
        public string Name => "RegionProtect";

        #endregion

        #region Plugin Lifecycle

        /// <summary>
        /// Initializes RegionProtect, creates config folders/files if needed, and loads world regions.
        /// </summary>
        /// <param name="context">Server-provided plugin context containing base directory, world key, and logger.</param>
        public void Initialize(ServerPluginContext context)
        {
            _log = context?.Log ?? (_ => { });

            string serverDir = context?.BaseDir;
            if (string.IsNullOrWhiteSpace(serverDir))
                serverDir = AppDomain.CurrentDomain.BaseDirectory;

            serverDir = Path.GetFullPath(serverDir);

            // This creates:
            // CMZDedicatedLidgrenServer\Plugins\RegionProtect
            _pluginDir = Path.Combine(serverDir, "Plugins", "RegionProtect");

            string worldKey = string.IsNullOrWhiteSpace(context?.WorldGuid)
                ? "default"
                : context.WorldGuid.Trim();

            string worldDir = Path.Combine(_pluginDir, "Worlds", SafeDirName(worldKey));

            Directory.CreateDirectory(_pluginDir);
            Directory.CreateDirectory(worldDir);

            _configPath = Path.Combine(_pluginDir, "RegionProtect.Config.ini");
            _regionsPath = Path.Combine(worldDir, "RegionProtect.Regions.ini");

            LoadAll();

            _log($"[RegionProtect] Config: {_configPath}.");
            _log($"[RegionProtect] Regions: {_regionsPath}.");
            _log($"[RegionProtect] Loaded {_regions.Count} region(s).");
        }

        /// <summary>
        /// Entry point called by the server before host/world packets are applied or relayed.
        /// </summary>
        /// <param name="context">Message context supplied by the dedicated server world handler.</param>
        /// <returns>
        /// True when RegionProtect consumed the packet; false when normal server handling should continue.
        /// </returns>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            if (context == null || context.Payload == null)
                return false;

            if (_config == null || !_config.Enabled)
                return false;

            return context.TypeName switch
            {
                "DNA.CastleMinerZ.Net.AlterBlockMessage" => HandleAlterBlockMessage(context),
                "DNA.CastleMinerZ.Net.DetonateExplosiveMessage" => HandleDetonateExplosiveMessage(context),
                "DNA.CastleMinerZ.Net.RemoveBlocksMessage" => HandleRemoveBlocksMessage(context),
                "DNA.CastleMinerZ.Net.ItemCrateMessage" => HandleItemCrateMessage(context),
                "DNA.CastleMinerZ.Net.DestroyCrateMessage" => HandleDestroyCrateMessage(context),
                _ => false,
            };
        }

        /// <summary>
        /// Loads the general config and active-world region data.
        /// </summary>
        private void LoadAll()
        {
            _config = ServerRegionProtectConfig.LoadOrCreate(_configPath);
            LoadRegionsOrCreate();
        }
        #endregion

        #region Host Message Handlers

        /// <summary>
        /// Handles block mining and block placement protection.
        /// </summary>
        /// <param name="context">Host message context for an AlterBlockMessage.</param>
        /// <returns>True when the packet was denied and consumed; otherwise false.</returns>
        private bool HandleAlterBlockMessage(HostMessageContext context)
        {
            object msg = context.DeserializeGameMessage(context.TypeName, context.Payload);

            if (!TryReadNamedBlockPosition(
                    context,
                    msg,
                    out BlockPos blockPos,
                    "BlockLocation",
                    "Location",
                    "_blockLocation",
                    "_location"))
            {
                WarnMissingPositionOnce(context.TypeName);
                return false;
            }

            object blockTypeObj = GetFirstMemberValue(context, msg, "BlockType", "_blockType", "blockType");
            if (blockTypeObj == null)
                return false;

            int blockTypeValue = Convert.ToInt32(blockTypeObj, CultureInfo.InvariantCulture);

            // Current server code applies RemoveBlocksMessage as block type 0.
            // So treat BlockType 0 as mining/removal and non-zero as placing/replacing.
            ProtectAction action = blockTypeValue == 0
                ? ProtectAction.Mine
                : ProtectAction.Build;

            return DenyAndCorrectIfProtected(context, blockPos, action, blockTypeValue);
        }

        /// <summary>
        /// Blocks protected explosions before the client/server flow reaches RemoveBlocksMessage.
        /// This is required because RemoveBlocksMessage only contains positions, not original block types.
        /// </summary>
        /// <param name="context">Host message context for a DetonateExplosiveMessage.</param>
        /// <returns>True when the detonation was denied and consumed; otherwise false.</returns>
        private bool HandleDetonateExplosiveMessage(HostMessageContext context)
        {
            object msg = context.DeserializeGameMessage(context.TypeName, context.Payload);

            if (!TryReadNamedBlockPosition(
                    context,
                    msg,
                    out BlockPos explosionPos,
                    "Location",
                    "_location"))
            {
                WarnMissingPositionOnce(context.TypeName);
                return false;
            }

            object explosiveTypeObj = GetFirstMemberValue(context, msg, "ExplosiveType", "_explosiveType", "explosiveType");

            int explosiveTypeValue = explosiveTypeObj == null
                ? 0
                : Convert.ToInt32(explosiveTypeObj, CultureInfo.InvariantCulture);

            if (!ShouldDenyExplosionArea(
                    context.SenderName,
                    explosionPos,
                    explosiveTypeValue,
                    out string reason))
            {
                return false;
            }

            if (_config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] Denied explosive detonation from {SafePlayer(context.SenderName, context.SenderId)} at {explosionPos} because {reason}.");
            }

            SendWarningIfNeeded(context, ProtectAction.Explosion, reason);

            // If the explosive itself was TNT/C4, restore that block visually.
            SendExplosiveBlockCorrection(context, explosionPos, explosiveTypeValue);

            // Also resync the chunk as a fallback.
            context.ResyncChunkForBlock?.Invoke(explosionPos.X, explosionPos.Y, explosionPos.Z);

            // Consume the detonation packet.
            // This prevents the client from running FindBlocksToRemove and sending/removing the blast.
            return true;
        }

        /// <summary>
        /// Handles block removal batches such as explosions or other multi-block destruction packets.
        /// </summary>
        /// <param name="context">Host message context for a RemoveBlocksMessage.</param>
        /// <returns>True when the block removal batch was denied and consumed; otherwise false.</returns>
        private bool HandleRemoveBlocksMessage(HostMessageContext context)
        {
            object msg = context.DeserializeGameMessage(context.TypeName, context.Payload);

            object numBlocksObj = GetFirstMemberValue(context, msg, "NumBlocks", "_numBlocks", "numBlocks");
            object blocksObj = GetFirstMemberValue(context, msg, "BlocksToRemove", "_blocksToRemove", "blocksToRemove");

            if (blocksObj is not Array blocks)
                return false;

            int numBlocks = numBlocksObj == null
                ? blocks.Length
                : Convert.ToInt32(numBlocksObj, CultureInfo.InvariantCulture);

            int count = Math.Min(numBlocks, blocks.Length);

            bool denied = false;
            List<BlockPos> deniedPositions = [];
            string firstReason = null;

            for (int i = 0; i < count; i++)
            {
                object location = blocks.GetValue(i);

                if (!TryReadBlockPosition(context, location, out BlockPos blockPos))
                    continue;

                if (ShouldDeny(context.SenderName, blockPos, ProtectAction.Explosion, out string reason))
                {
                    denied = true;
                    deniedPositions.Add(blockPos);

                    firstReason ??= reason;
                }
            }

            if (!denied)
                return false;

            if (_config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] Denied explosion block removal from {SafePlayer(context.SenderName, context.SenderId)}, protected blocks={deniedPositions.Count}.");
            }

            SendWarningIfNeeded(context, ProtectAction.Explosion, firstReason ?? "protected region");

            foreach (BlockPos pos in deniedPositions)
            {
                SendTerrainCorrection(context, pos, ProtectAction.Explosion, -1);
            }

            // Consume the whole explosion packet.
            return true;
        }

        /// <summary>
        /// Handles crate inventory/item edit protection.
        /// </summary>
        /// <param name="context">Host message context for an ItemCrateMessage.</param>
        /// <returns>True when the crate edit was denied and consumed; otherwise false.</returns>
        private bool HandleItemCrateMessage(HostMessageContext context)
        {
            if (!TryReadItemCrateHeader(
                    context.Payload,
                    out BlockPos cratePos,
                    out int index,
                    out bool hasItem))
            {
                WarnMissingPositionOnce(context.TypeName);
                return false;
            }

            if (!ShouldDeny(context.SenderName, cratePos, ProtectAction.UseCrate, out string reason))
                return false;

            if (_config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] Denied crate item edit from {SafePlayer(context.SenderName, context.SenderId)} at {cratePos}, slot={index}, hasItem={hasItem} because {reason}.");
            }

            SendWarningIfNeeded(context, ProtectAction.UseCrate, reason);

            context.ResyncChunkForBlock?.Invoke(cratePos.X, cratePos.Y, cratePos.Z);

            return true;
        }

        /// <summary>
        /// Handles crate destruction protection.
        /// </summary>
        /// <param name="context">Host message context for a DestroyCrateMessage.</param>
        /// <returns>True when the crate break was denied and consumed; otherwise false.</returns>
        private bool HandleDestroyCrateMessage(HostMessageContext context)
        {
            object msg = context.DeserializeGameMessage(context.TypeName, context.Payload);

            if (!TryReadNamedBlockPosition(
                    context,
                    msg,
                    out BlockPos cratePos,
                    "Location",
                    "CrateLocation",
                    "BlockLocation",
                    "_location",
                    "_crateLocation"))
            {
                if (!TryFindAnyBlockPosition(context, msg, out cratePos))
                {
                    WarnMissingPositionOnce(context.TypeName);
                    return false;
                }
            }

            if (!ShouldDeny(context.SenderName, cratePos, ProtectAction.BreakCrate, out string reason))
                return false;

            if (_config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] Denied crate break from {SafePlayer(context.SenderName, context.SenderId)} at {cratePos} because {reason}.");
            }

            SendWarningIfNeeded(context, ProtectAction.BreakCrate, reason);

            context.ResyncChunkForBlock?.Invoke(cratePos.X, cratePos.Y, cratePos.Z);

            return true;
        }
        #endregion

        #region Denial / Correction Flow

        /// <summary>
        /// Checks whether an action should be denied, then warns and corrects the client when needed.
        /// </summary>
        /// <param name="context">Host message context for the denied packet.</param>
        /// <param name="blockPos">Target block position.</param>
        /// <param name="action">Protection action being evaluated.</param>
        /// <param name="attemptedBlockTypeValue">Block type sent by the client, when available.</param>
        /// <returns>True when the packet should be consumed; otherwise false.</returns>
        private bool DenyAndCorrectIfProtected(
            HostMessageContext context,
            BlockPos blockPos,
            ProtectAction action,
            int attemptedBlockTypeValue = -1)
        {
            if (!ShouldDeny(context.SenderName, blockPos, action, out string reason))
                return false;

            string player = SafePlayer(context.SenderName, context.SenderId);

            if (_config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] Denied {DescribeAction(action)} from {player} at {blockPos} because {reason}.");
            }

            SendWarningIfNeeded(context, action, reason);

            SendTerrainCorrection(context, blockPos, action, attemptedBlockTypeValue);

            // true means consume the original packet.
            // Server does not apply it and does not relay it.
            return true;
        }

        /// <summary>
        /// Sends a player warning if warnings are enabled and the warning cooldown has elapsed.
        /// </summary>
        /// <param name="context">Host message context for the denied action.</param>
        /// <param name="action">Protection action that was denied.</param>
        /// <param name="reason">Human-readable denial reason.</param>
        private void SendWarningIfNeeded(HostMessageContext context, ProtectAction action, string reason)
        {
            if (!_config.WarnPlayers)
                return;

            string key = NormalizePlayer(context.SenderName);

            DateTime now = DateTime.UtcNow;

            if (_lastWarningByPlayer.TryGetValue(key, out DateTime last) &&
                (now - last).TotalSeconds < _config.WarningCooldownSeconds)
            {
                return;
            }

            _lastWarningByPlayer[key] = now;

            context.SendWarningToPlayer?.Invoke(BuildWarningMessage(action, reason));
        }

        /// <summary>
        /// Attempts to visually correct denied terrain edits on the offending client.
        /// </summary>
        /// <remarks>
        /// Build denial sends air back to remove the local fake placement.
        /// Mining/explosion denial attempts to restore a known original block type, then falls back to chunk resync.
        /// </remarks>
        /// <param name="context">Host message context for the denied action.</param>
        /// <param name="blockPos">Position to correct.</param>
        /// <param name="action">Denied protection action.</param>
        /// <param name="attemptedBlockTypeValue">
        /// Block type attempted by the client when available.
        /// 0 usually means removal/mining.
        /// Greater than 0 usually means placement.
        /// -1 means unknown/not supplied, such as RemoveBlocksMessage.
        /// </param>
        private void SendTerrainCorrection(
            HostMessageContext context,
            BlockPos blockPos,
            ProtectAction action,
            int attemptedBlockTypeValue)
        {
            bool sentAlterBlock = false;

            // Denied placement:
            // If the client tried to place a non-air block, remove that local fake placement.
            if (action == ProtectAction.Build && attemptedBlockTypeValue > 0)
            {
                context.SendAlterBlockToPlayer?.Invoke(
                    blockPos.X,
                    blockPos.Y,
                    blockPos.Z,
                    0);

                sentAlterBlock = true;
            }
            else
            {
                // Denied mining/explosion, or an unusual build correction:
                // Prefer saved delta, then recent DigMessage cache.
                if (context.TryGetOriginalBlockType != null &&
                    context.TryGetOriginalBlockType(
                        context.SenderId,
                        blockPos.X,
                        blockPos.Y,
                        blockPos.Z,
                        out int restoreBlockType) &&
                    restoreBlockType > 0)
                {
                    context.SendAlterBlockToPlayer?.Invoke(
                        blockPos.X,
                        blockPos.Y,
                        blockPos.Z,
                        restoreBlockType);

                    sentAlterBlock = true;
                }
            }

            // Keep chunk resync as fallback, but AlterBlock is the real-time visual fix.
            context.ResyncChunkForBlock?.Invoke(blockPos.X, blockPos.Y, blockPos.Z);

            if (!sentAlterBlock && _config.LogDenied)
            {
                context.Log?.Invoke($"[RegionProtect] No restore block type was available for {blockPos}. Action={action}, AttemptedBlockType={attemptedBlockTypeValue}. Used chunk resync fallback.");
            }
        }

        /// <summary>
        /// Restores the explosive block itself when the denied detonation was TNT or C4.
        /// </summary>
        /// <param name="context">Host message context for the denied detonation.</param>
        /// <param name="explosionPos">Explosive block position.</param>
        /// <param name="explosiveTypeValue">Raw explosive type enum value.</param>
        private void SendExplosiveBlockCorrection(
            HostMessageContext context,
            BlockPos explosionPos,
            int explosiveTypeValue)
        {

            // Prefer the real saved/original block type when available.
            if (context.TryGetOriginalBlockType != null &&
                context.TryGetOriginalBlockType(
                    context.SenderId,
                    explosionPos.X,
                    explosionPos.Y,
                    explosionPos.Z,
                    out int restoreBlockType) &&
                restoreBlockType > 0)
            {
                context.SendAlterBlockToPlayer?.Invoke(
                    explosionPos.X,
                    explosionPos.Y,
                    explosionPos.Z,
                    restoreBlockType);

                return;
            }

            // Fallback based on ExplosiveTypes:
            // BlockTypeEnum.TNT = 41
            // BlockTypeEnum.C4  = 42
            if (explosiveTypeValue == 0)
            {
                restoreBlockType = 41;
            }
            else if (explosiveTypeValue == 1)
            {
                restoreBlockType = 42;
            }
            else
            {
                // Rocket / grenade explosions do not have a TNT/C4 block to restore.
                return;
            }

            context.SendAlterBlockToPlayer?.Invoke(
                explosionPos.X,
                explosionPos.Y,
                explosionPos.Z,
                restoreBlockType);
        }
        #endregion

        #region Protection Rules

        /// <summary>
        /// Checks whether a player action at a block position should be denied by spawn or region protection.
        /// </summary>
        /// <param name="playerName">Player name from the server context.</param>
        /// <param name="blockPos">Block position being tested.</param>
        /// <param name="action">Protection action being tested.</param>
        /// <param name="reason">Human-readable denial reason when denied.</param>
        /// <returns>True when the action should be denied; otherwise false.</returns>
        private bool ShouldDeny(string playerName, BlockPos blockPos, ProtectAction action, out string reason)
        {
            reason = null;

            if (!IsActionProtected(action))
                return false;

            string player = NormalizePlayer(playerName);

            if (_spawn.Enabled && _spawn.Range > 0)
            {
                long dx = blockPos.X;
                long dz = blockPos.Z;
                long range = _spawn.Range;

                if ((dx * dx) + (dz * dz) <= range * range)
                {
                    if (!_spawn.AllowedPlayers.Contains(player))
                    {
                        reason = "spawn protection";
                        return true;
                    }
                }
            }

            foreach (ProtectedRegion region in _regions)
            {
                if (!region.Contains(blockPos))
                    continue;

                if (!region.AllowedPlayers.Contains(player))
                {
                    reason = "region '" + region.Name + "'";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether the supplied action type is enabled in the general config.
        /// </summary>
        /// <param name="action">Protection action to check.</param>
        /// <returns>True when the action is protected by config; otherwise false.</returns>
        private bool IsActionProtected(ProtectAction action)
        {
            return action switch
            {
                ProtectAction.Mine => _config.ProtectMining,
                ProtectAction.Build => _config.ProtectPlacing,
                ProtectAction.Explosion => _config.ProtectExplosions,
                ProtectAction.UseCrate => _config.ProtectCrateItems,
                ProtectAction.BreakCrate => _config.ProtectCrateMining,
                _ => false,
            };
        }

        /// <summary>
        /// Returns true when an explosion center/radius would affect protected blocks.
        /// </summary>
        /// <param name="playerName">Player name from the server context.</param>
        /// <param name="center">Explosion center position.</param>
        /// <param name="explosiveTypeValue">Raw explosive type enum value.</param>
        /// <param name="reason">Human-readable denial reason when denied.</param>
        /// <returns>True when any block inside the explosion radius is protected; otherwise false.</returns>
        private bool ShouldDenyExplosionArea(
            string playerName,
            BlockPos center,
            int explosiveTypeValue,
            out string reason)
        {
            reason = null;

            if (!_config.ProtectExplosions)
                return false;

            int radius = GetExplosionDestructionRadius(explosiveTypeValue);

            for (int x = center.X - radius; x <= center.X + radius; x++)
            {
                for (int y = center.Y - radius; y <= center.Y + radius; y++)
                {
                    for (int z = center.Z - radius; z <= center.Z + radius; z++)
                    {
                        BlockPos pos = new(x, y, z);

                        if (ShouldDeny(playerName, pos, ProtectAction.Explosion, out reason))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Matches CastleMiner Z's Explosive.cDestructionRanges:
        /// TNT=0 -> 2
        /// C4=1 -> 3
        /// Rocket=2 -> 3
        /// Laser=3 -> 0
        /// HEGrenade=4 -> 1
        /// Harvest=5 -> 0
        /// </summary>
        /// <param name="explosiveTypeValue">Raw explosive type enum value.</param>
        /// <returns>Destruction radius for the explosive type.</returns>
        private static int GetExplosionDestructionRadius(int explosiveTypeValue)
        {
            return explosiveTypeValue switch
            {
                // TNT
                0 => 2,
                // C4
                1 => 3,
                // Rocket
                2 => 3,
                // HEGrenade
                4 => 1,
                _ => 0,
            };
        }
        #endregion

        #region Region Loading

        /// <summary>
        /// Creates the active world's regions file if missing, then loads spawn and cuboid protection data.
        /// </summary>
        private void LoadRegionsOrCreate()
        {
            if (!File.Exists(_regionsPath))
            {
                File.WriteAllLines(_regionsPath,
                [
                    "# RegionProtect - Server Regions",
                    "# This file is used by the dedicated server plugin.",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[SpawnProtection]",
                    "Enabled        = false",
                    "Range          = 16",
                    "AllowedPlayers =",
                    "",
                    "# Example:",
                    "# [Region:SpawnTown]",
                    "# Min            = -50,0,-50",
                    "# Max            =  50,80, 50",
                    "# AllowedPlayers = RussDev7",
                    "",
                ]);
            }

            SimpleIni ini = SimpleIni.Load(_regionsPath);

            _regions.Clear();

            SimpleIni.IniSection spawnSec = ini.GetSection("SpawnProtection");
            _spawn = new SpawnProtection
            {
                Enabled = spawnSec.GetBool("Enabled", false),
                Range = Clamp(spawnSec.GetInt("Range", 16), 0, 4096),
                AllowedPlayers = ParsePlayers(spawnSec.GetString("AllowedPlayers", ""))
            };

            foreach (string sectionName in ini.SectionNames)
            {
                if (!sectionName.StartsWith("Region:", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = sectionName.Substring("Region:".Length).Trim();
                if (name.Length == 0)
                    continue;

                SimpleIni.IniSection section = ini.GetSection(sectionName);

                if (!TryParseBlockPos(section.GetString("Min", ""), out BlockPos min))
                    continue;

                if (!TryParseBlockPos(section.GetString("Max", ""), out BlockPos max))
                    continue;

                ProtectedRegion region = new()
                {
                    Name = name,
                    Min = BlockPos.Min(min, max),
                    Max = BlockPos.Max(min, max),
                    AllowedPlayers = ParsePlayers(section.GetString("AllowedPlayers", ""))
                };

                _regions.Add(region);
            }
        }
        #endregion

        #region Reflection Helpers

        /// <summary>
        /// Reads the first available reflected member from a message object using a list of possible names.
        /// </summary>
        /// <param name="context">Host message context containing the reflection helper.</param>
        /// <param name="instance">Message object to inspect.</param>
        /// <param name="names">Possible field/property names to check in order.</param>
        /// <returns>The first non-null member value found; otherwise null.</returns>
        private static object GetFirstMemberValue(HostMessageContext context, object instance, params string[] names)
        {
            foreach (string name in names)
            {
                object value = context.GetMemberValue(instance, name);
                if (value != null)
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Reads a named IntVector3-like block position from a reflected message object.
        /// </summary>
        /// <param name="context">Host message context containing reflection/vector helpers.</param>
        /// <param name="instance">Message object to inspect.</param>
        /// <param name="blockPos">Parsed block position when successful.</param>
        /// <param name="names">Possible field/property names that may contain the position.</param>
        /// <returns>True when a block position was read; otherwise false.</returns>
        private static bool TryReadNamedBlockPosition(
            HostMessageContext context,
            object instance,
            out BlockPos blockPos,
            params string[] names)
        {
            object value = GetFirstMemberValue(context, instance, names);
            return TryReadBlockPosition(context, value, out blockPos);
        }

        /// <summary>
        /// Searches all reflected fields/properties on a message object for the first IntVector3-like block position.
        /// </summary>
        /// <param name="context">Host message context containing reflection/vector helpers.</param>
        /// <param name="instance">Message object to inspect.</param>
        /// <param name="blockPos">Parsed block position when successful.</param>
        /// <returns>True when any block position was found; otherwise false.</returns>
        private static bool TryFindAnyBlockPosition(HostMessageContext context, object instance, out BlockPos blockPos)
        {
            blockPos = default;

            if (instance == null)
                return false;

            Type type = instance.GetType();

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                object value;

                try
                {
                    value = property.GetValue(instance, null);
                }
                catch
                {
                    continue;
                }

                if (TryReadBlockPosition(context, value, out blockPos))
                    return true;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;

                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (TryReadBlockPosition(context, value, out blockPos))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Converts an IntVector3-like reflected value into a RegionProtect block position.
        /// </summary>
        /// <param name="context">Host message context containing the IntVector3 helper.</param>
        /// <param name="value">Reflected value to parse.</param>
        /// <param name="blockPos">Parsed block position when successful.</param>
        /// <returns>True when the value could be parsed as a block position; otherwise false.</returns>
        private static bool TryReadBlockPosition(HostMessageContext context, object value, out BlockPos blockPos)
        {
            blockPos = default;

            if (value == null || context.TryReadIntVector3 == null)
                return false;

            if (!context.TryReadIntVector3(value, out int x, out int y, out int z))
                return false;

            blockPos = new BlockPos(x, y, z);
            return true;
        }

        /// <summary>
        /// Logs a missing-position warning once per message type.
        /// </summary>
        /// <param name="typeName">Packet type name that could not be parsed.</param>
        private void WarnMissingPositionOnce(string typeName)
        {
            if (!_missingPositionWarnings.Add(typeName))
                return;

            _log($"[RegionProtect] Could not find a block/crate position in {typeName}. Protection for this packet type may need one more reflected field name.");
        }

        /// <summary>
        /// Reads the fixed ItemCrateMessage header without deserializing InventoryItem data.
        /// </summary>
        /// <remarks>
        /// ItemCrateMessage layout:
        /// [0]      message id
        /// [1..12]  IntVector3 Location
        /// [13..16] int Index
        /// [17]     bool HasItem
        /// [18..N]  InventoryItem data when HasItem is true
        /// [last]   checksum
        /// </remarks>
        private static bool TryReadItemCrateHeader(
            byte[] payload,
            out BlockPos cratePos,
            out int index,
            out bool hasItem)
        {
            cratePos = default;
            index = 0;
            hasItem = false;

            // Minimum packet:
            // msg id + IntVector3 + int index + bool hasItem + checksum
            if (payload == null || payload.Length < 19)
                return false;

            int x = BitConverter.ToInt32(payload, 1);
            int y = BitConverter.ToInt32(payload, 5);
            int z = BitConverter.ToInt32(payload, 9);

            index = BitConverter.ToInt32(payload, 13);
            hasItem = payload[17] != 0;

            cratePos = new BlockPos(x, y, z);
            return true;
        }
        #endregion

        #region Parsing / Formatting Helpers

        /// <summary>
        /// Parses a comma-separated list of player names into a normalized allow-list set.
        /// </summary>
        /// <param name="csv">Comma-separated player names.</param>
        /// <returns>Normalized player-name set.</returns>
        private static HashSet<string> ParsePlayers(string csv)
        {
            HashSet<string> players = new(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(csv))
                return players;

            string[] parts = csv.Split([','], StringSplitOptions.RemoveEmptyEntries);

            foreach (string raw in parts)
            {
                string player = NormalizePlayer(raw);
                if (player.Length > 0)
                    players.Add(player);
            }

            return players;
        }

        /// <summary>
        /// Parses a block position from the config format "x,y,z".
        /// </summary>
        /// <param name="raw">Raw position string.</param>
        /// <param name="pos">Parsed block position when successful.</param>
        /// <returns>True when the position was parsed; otherwise false.</returns>
        private static bool TryParseBlockPos(string raw, out BlockPos pos)
        {
            pos = default;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string[] parts = raw.Split(',');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x))
                return false;

            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return false;

            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                return false;

            pos = new BlockPos(x, y, z);
            return true;
        }

        /// <summary>
        /// Normalizes player names for case-insensitive allow-list comparisons.
        /// </summary>
        /// <param name="name">Raw player name.</param>
        /// <returns>Trimmed uppercase player name, or an empty string.</returns>
        private static string NormalizePlayer(string name)
        {
            return (name ?? string.Empty).Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Returns a readable player name or a fallback sender id label.
        /// </summary>
        /// <param name="name">Player name from the server context.</param>
        /// <param name="senderId">Server sender id.</param>
        /// <returns>Readable player label.</returns>
        private static string SafePlayer(string name, byte senderId)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return "Player" + senderId;
        }

        /// <summary>
        /// Builds the player-facing RegionProtect denial message.
        /// </summary>
        /// <param name="action">Denied protection action.</param>
        /// <param name="reason">Human-readable protection reason.</param>
        /// <returns>Formatted denial message.</returns>
        private static string BuildWarningMessage(ProtectAction action, string reason)
        {
            string actionText = DescribePlayerAction(action);
            string verb = UsesPluralVerb(action) ? "were" : "was";

            return
                $"[RegionProtect] Protected by {reason}. {actionText} {verb} blocked. Client-only desync; not saved to server.";
        }

        /// <summary>
        /// Converts a protection action into a fluent player-facing action phrase.
        /// </summary>
        /// <param name="action">Denied protection action.</param>
        /// <returns>Readable player-facing action phrase.</returns>
        private static string DescribePlayerAction(ProtectAction action)
        {
            return action switch
            {
                ProtectAction.Mine => "Breaking blocks here",
                ProtectAction.Build => "Placing blocks here",
                ProtectAction.Explosion => "Explosions here",
                ProtectAction.UseCrate => "Editing crates here",
                ProtectAction.BreakCrate => "Breaking crates here",
                _ => "That action",
            };
        }

        /// <summary>
        /// Returns whether the player-facing action phrase should use a plural verb.
        /// </summary>
        /// <param name="action">Denied protection action.</param>
        /// <returns>True when the message should use "were"; otherwise false for "was".</returns>
        private static bool UsesPluralVerb(ProtectAction action)
        {
            return action switch
            {
                ProtectAction.Explosion => true,
                _ => false,
            };
        }

        /// <summary>
        /// Converts a protection action into a readable message fragment.
        /// </summary>
        /// <param name="action">Protection action.</param>
        /// <returns>Readable action description.</returns>
        private static string DescribeAction(ProtectAction action)
        {
            return action switch
            {
                ProtectAction.Mine => "mining",
                ProtectAction.Build => "placing",
                ProtectAction.Explosion => "explosion",
                ProtectAction.UseCrate => "crate item edit",
                ProtectAction.BreakCrate => "crate break",
                _ => "protected action",
            };
        }

        /// <summary>
        /// Floors a world coordinate down to a 16-block chunk boundary.
        /// </summary>
        /// <param name="worldCoord">World coordinate.</param>
        /// <returns>Chunk-aligned coordinate.</returns>
        private static int FloorToChunk(int worldCoord)
        {
            return (int)Math.Floor(worldCoord / 16.0) * 16;
        }

        /// <summary>
        /// Clamps an integer value between a minimum and maximum value.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <param name="min">Minimum allowed value.</param>
        /// <param name="max">Maximum allowed value.</param>
        /// <returns>Clamped value.</returns>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        /// <summary>
        /// Converts a world key into a file-system-safe directory name.
        /// </summary>
        /// <param name="value">Raw world key.</param>
        /// <returns>Safe directory name.</returns>
        private static string SafeDirName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "default";

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value;
        }
        #endregion

        #region Chunk Resync Helpers

        /// <summary>
        /// Resyncs each touched chunk once for a set of block positions.
        /// </summary>
        /// <param name="context">Host message context containing the resync callback.</param>
        /// <param name="positions">Block positions that were denied or corrected.</param>
        private void ResyncTouchedChunks(HostMessageContext context, List<BlockPos> positions)
        {
            HashSet<string> sentChunks = new(StringComparer.OrdinalIgnoreCase);

            foreach (BlockPos pos in positions)
            {
                int chunkX = FloorToChunk(pos.X);
                int chunkY = FloorToChunk(pos.Y);
                int chunkZ = FloorToChunk(pos.Z);

                string key = chunkX + "_" + chunkY + "_" + chunkZ;

                if (!sentChunks.Add(key))
                    continue;

                context.ResyncChunkForBlock?.Invoke(pos.X, pos.Y, pos.Z);
            }
        }
        #endregion

        #region Nested Types

        /// <summary>
        /// Protection action categories controlled by RegionProtect settings.
        /// </summary>
        private enum ProtectAction
        {
            Mine,
            Build,
            Explosion,
            UseCrate,
            BreakCrate
        }

        /// <summary>
        /// Integer block position used by RegionProtect checks and config parsing.
        /// </summary>
        private readonly struct BlockPos(int x, int y, int z)
        {
            public readonly int X = x;
            public readonly int Y = y;
            public readonly int Z = z;

            public static BlockPos Min(BlockPos a, BlockPos b)
            {
                return new BlockPos(
                    Math.Min(a.X, b.X),
                    Math.Min(a.Y, b.Y),
                    Math.Min(a.Z, b.Z));
            }

            public static BlockPos Max(BlockPos a, BlockPos b)
            {
                return new BlockPos(
                    Math.Max(a.X, b.X),
                    Math.Max(a.Y, b.Y),
                    Math.Max(a.Z, b.Z));
            }

            public override readonly string ToString()
            {
                return "(" + X + "," + Y + "," + Z + ")";
            }
        }

        /// <summary>
        /// Cuboid region definition loaded from the active world's region config file.
        /// </summary>
        private sealed class ProtectedRegion
        {
            public string Name;
            public BlockPos Min;
            public BlockPos Max;
            public HashSet<string> AllowedPlayers = new(StringComparer.OrdinalIgnoreCase);

            public bool Contains(BlockPos pos)
            {
                return pos.X >= Min.X && pos.X <= Max.X &&
                       pos.Y >= Min.Y && pos.Y <= Max.Y &&
                       pos.Z >= Min.Z && pos.Z <= Max.Z;
            }
        }

        /// <summary>
        /// Spawn-radius protection definition loaded from the active world's region config file.
        /// </summary>
        private sealed class SpawnProtection
        {
            public bool Enabled;
            public int Range;
            public HashSet<string> AllowedPlayers = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// General RegionProtect server config loaded from RegionProtect.Config.ini.
        /// </summary>
        private sealed class ServerRegionProtectConfig
        {
            public bool Enabled = true;
            public bool ProtectMining = true;
            public bool ProtectPlacing = true;
            public bool ProtectExplosions = true;
            public bool ProtectCrateItems = true;
            public bool ProtectCrateMining = true;
            public bool LogDenied = true;
            public bool WarnPlayers = true;
            public int WarningCooldownSeconds = 2;

            /// <summary>
            /// Loads the general config file or creates a default config file when missing.
            /// </summary>
            /// <param name="path">Full config file path.</param>
            /// <returns>Loaded server RegionProtect config.</returns>
            public static ServerRegionProtectConfig LoadOrCreate(string path)
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                {
                    File.WriteAllLines(path,
                    [
                        "# RegionProtect - Dedicated Server Config",
                        "# Stored beside the server exe under Plugins\\RegionProtect.",
                        "",
                        "[General]",
                        "Enabled                = true",
                        "ProtectMining          = true",
                        "ProtectPlacing         = true",
                        "ProtectExplosions      = true",
                        "ProtectCrateItems      = true",
                        "ProtectCrateMining     = true",
                        "WarnPlayers            = true",
                        "WarningCooldownSeconds = 2",
                        "LogDenied              = true",
                    ]);
                }

                SimpleIni ini = SimpleIni.Load(path);
                SimpleIni.IniSection general = ini.GetSection("General");

                return new ServerRegionProtectConfig
                {
                    Enabled = general.GetBool("Enabled", true),
                    ProtectMining = general.GetBool("ProtectMining", true),
                    ProtectPlacing = general.GetBool("ProtectPlacing", true),
                    ProtectExplosions = general.GetBool("ProtectExplosions", true),
                    ProtectCrateItems = general.GetBool("ProtectCrateItems", true),
                    ProtectCrateMining = general.GetBool("ProtectCrateMining", true),
                    LogDenied = general.GetBool("LogDenied", true),
                    WarnPlayers = general.GetBool("WarnPlayers", true),
                    WarningCooldownSeconds = Clamp(general.GetInt("WarningCooldownSeconds", 2), 0, 60),
                };
            }
        }

        /// <summary>
        /// Small INI reader used to avoid adding extra dependencies to the dedicated server.
        /// </summary>
        private sealed class SimpleIni
        {
            private readonly Dictionary<string, Dictionary<string, string>> _data =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Names of all loaded INI sections.
            /// </summary>
            public IEnumerable<string> SectionNames => _data.Keys;

            /// <summary>
            /// Loads a simple INI file into memory.
            /// </summary>
            /// <param name="path">INI file path.</param>
            /// <returns>Loaded INI data.</returns>
            public static SimpleIni Load(string path)
            {
                SimpleIni ini = new();
                string section = string.Empty;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? string.Empty).Trim();

                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();

                        if (!ini._data.ContainsKey(section))
                            ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (!ini._data.TryGetValue(section, out Dictionary<string, string> values))
                    {
                        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ini._data[section] = values;
                    }

                    values[key] = value;
                }

                return ini;
            }

            /// <summary>
            /// Gets a section wrapper for the requested section name.
            /// </summary>
            /// <param name="name">Section name.</param>
            /// <returns>Section wrapper, empty when the section does not exist.</returns>
            public IniSection GetSection(string name)
            {
                if (!_data.TryGetValue(name, out Dictionary<string, string> values))
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                return new IniSection(values);
            }

            /// <summary>
            /// Wrapper around a loaded INI section with typed getters.
            /// </summary>
            public sealed class IniSection(Dictionary<string, string> values)
            {
                private readonly Dictionary<string, string> _values = values;

                /// <summary>
                /// Reads a string value from the section.
                /// </summary>
                /// <param name="key">INI key.</param>
                /// <param name="defaultValue">Fallback value.</param>
                /// <returns>Configured or fallback string value.</returns>
                public string GetString(string key, string defaultValue)
                {
                    return _values.TryGetValue(key, out string value)
                        ? value
                        : defaultValue;
                }

                /// <summary>
                /// Reads an integer value from the section.
                /// </summary>
                /// <param name="key">INI key.</param>
                /// <param name="defaultValue">Fallback value.</param>
                /// <returns>Configured or fallback integer value.</returns>
                public int GetInt(string key, int defaultValue)
                {
                    string value = GetString(key, defaultValue.ToString(CultureInfo.InvariantCulture));

                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                        ? result
                        : defaultValue;
                }

                /// <summary>
                /// Reads a boolean value from the section.
                /// </summary>
                /// <param name="key">INI key.</param>
                /// <param name="defaultValue">Fallback value.</param>
                /// <returns>Configured or fallback boolean value.</returns>
                public bool GetBool(string key, bool defaultValue)
                {
                    string value = GetString(key, defaultValue ? "true" : "false");

                    if (bool.TryParse(value, out bool result))
                        return result;

                    if (int.TryParse(value, out int intResult))
                        return intResult != 0;

                    if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return defaultValue;
                }
            }
        }
        #endregion
    }
}
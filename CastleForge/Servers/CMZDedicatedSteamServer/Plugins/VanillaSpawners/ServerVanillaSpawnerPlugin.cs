/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Plugins.VanillaSpawners
{
    #region Runtime Config Shared With Harmony Patches

    /// <summary>
    /// Runtime switches used by the VanillaSpawners plugin and Harmony patches.
    /// </summary>
    /// <remarks>
    /// Defaults preserve vanilla behavior until the plugin config is loaded.
    /// </remarks>
    internal static class ServerVanillaSpawnerRuntime
    {
        /// <summary>
        /// When false, newly generated terrain will not place vanilla spawner blocks.
        /// </summary>
        public static volatile bool GenerateSpawnerBlocks = true;

        /// <summary>
        /// When false, existing vanilla spawner blocks are treated as non-clickable by patched host-side code.
        /// </summary>
        public static volatile bool AllowSpawnerActivation = true;
    }
    #endregion

    #region Plugin

    /// <summary>
    /// Controls vanilla spawner generation and optional existing spawner activation behavior.
    /// </summary>
    /// <remarks>
    /// Summary:
    /// - Patches cave and hell terrain generation so new vanilla spawners can be disabled.
    /// - Optionally consumes spawner-origin enemy spawns from vanilla clients.
    /// - Optionally blocks spawner block-state AlterBlockMessage updates.
    /// - Stores config under Plugins\VanillaSpawners.
    /// </remarks>
    internal sealed class ServerVanillaSpawnerPlugin : IServerWorldPlugin
    {
        #region Fields

        private Action<string> _log = _ => { };

        private readonly VanillaSpawnerConfig _config = new();

        private string _pluginDir;
        private string _configPath;

        #endregion

        #region Properties

        /// <summary>
        /// Display name used by the plugin manager.
        /// </summary>
        public string Name => "VanillaSpawners";

        #endregion

        #region Plugin Lifecycle

        /// <summary>
        /// Initializes the plugin, creates the default config if needed, and applies runtime switches.
        /// </summary>
        public void Initialize(ServerPluginContext context)
        {
            _log = context?.Log ?? (_ => { });

            string serverDir = context?.BaseDir;
            if (string.IsNullOrWhiteSpace(serverDir))
                serverDir = AppDomain.CurrentDomain.BaseDirectory;

            serverDir = Path.GetFullPath(serverDir);

            _pluginDir = Path.Combine(serverDir, "Plugins", "VanillaSpawners");
            Directory.CreateDirectory(_pluginDir);

            _configPath = Path.Combine(_pluginDir, "VanillaSpawners.Config.ini");

            EnsureDefaultConfig();
            LoadConfig();
            ApplyRuntimeConfig();

            _log($"[VanillaSpawners] Config: {_configPath}.");
            _log(
                $"[VanillaSpawners] Enabled={_config.Enabled}, " +
                $"GenerateSpawnerBlocks={_config.GenerateSpawnerBlocks}, " +
                $"AllowSpawnerActivation={_config.AllowSpawnerActivation}.");
        }

        /// <summary>
        /// Optionally consumes spawner-origin packets before normal host handling or relay.
        /// </summary>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            if (context == null || !_config.Enabled)
                return false;

            if (_config.AllowSpawnerActivation)
                return false;

            return context.TypeName switch
            {
                "DNA.CastleMinerZ.Net.AlterBlockMessage" => HandleAlterBlockMessage(context),
                "DNA.CastleMinerZ.Net.SpawnEnemyMessage" => HandleSpawnEnemyMessage(context),
                _ => false,
            };
        }
        #endregion

        #region Packet Handling

        /// <summary>
        /// Blocks spawner block-state changes when activation is disabled.
        /// </summary>
        /// <remarks>
        /// This catches changes such as EnemySpawnOff -> EnemySpawnOn/Dim and similar spawner states.
        /// It does not try to block normal mining/placing of non-spawner blocks.
        /// </remarks>
        private bool HandleAlterBlockMessage(HostMessageContext context)
        {
            try
            {
                object msg = context.DeserializeGameMessage?.Invoke(context.TypeName, context.Payload);
                if (msg == null)
                    return false;

                object blockTypeObj = context.GetMemberValue?.Invoke(msg, "BlockType");
                if (blockTypeObj == null)
                    return false;

                int blockTypeValue = Convert.ToInt32(blockTypeObj);

                if (!IsVanillaSpawnerBlockType(blockTypeValue))
                    return false;

                object location = context.GetMemberValue?.Invoke(msg, "BlockLocation");

                if (location != null &&
                    context.TryReadIntVector3 != null &&
                    context.TryReadIntVector3(location, out int x, out int y, out int z))
                {
                    if (context.TryGetSavedBlockType != null &&
                        context.TryGetSavedBlockType(x, y, z, out int savedBlockType))
                    {
                        context.SendAlterBlockToPlayer?.Invoke(x, y, z, savedBlockType);
                    }
                    else
                    {
                        context.ResyncChunkForBlock?.Invoke(x, y, z);
                    }
                }

                context.SendWarningToPlayer?.Invoke("Vanilla spawner activation is disabled on this server.");

                if (_config.LogBlockedActivation)
                    context.Log?.Invoke($"[VanillaSpawners] Blocked spawner AlterBlockMessage from {context.SenderName} ({context.SenderId}).");

                return true;
            }
            catch (Exception ex)
            {
                context.Log?.Invoke("[VanillaSpawners] Failed inspecting AlterBlockMessage: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Blocks enemy spawns that were created by a vanilla spawner.
        /// </summary>
        /// <remarks>
        /// Vanilla spawner enemies use SpawnValue and SpawnerPosition.
        /// Normal distance/random enemy spawns should have SpawnValue=0 and SpawnerPosition=Vector3.Zero.
        /// </remarks>
        private bool HandleSpawnEnemyMessage(HostMessageContext context)
        {
            try
            {
                object msg = context.DeserializeGameMessage?.Invoke(context.TypeName, context.Payload);
                if (msg == null)
                    return false;

                int spawnValue = 0;
                object spawnValueObj = context.GetMemberValue?.Invoke(msg, "SpawnValue");
                if (spawnValueObj != null)
                    spawnValue = Convert.ToInt32(spawnValueObj);

                object spawnerPosition = context.GetMemberValue?.Invoke(msg, "SpawnerPosition");

                bool looksLikeSpawnerSpawn =
                    spawnValue > 0 ||
                    IsNonZeroVector3(spawnerPosition);

                if (!looksLikeSpawnerSpawn)
                    return false;

                context.SendWarningToPlayer?.Invoke("Vanilla spawner activation is disabled on this server.");

                if (_config.LogBlockedActivation)
                    context.Log?.Invoke($"[VanillaSpawners] Blocked spawner SpawnEnemyMessage from {context.SenderName} ({context.SenderId}).");

                return true;
            }
            catch (Exception ex)
            {
                context.Log?.Invoke("[VanillaSpawners] Failed inspecting SpawnEnemyMessage: " + ex.Message);
                return false;
            }
        }
        #endregion

        #region Config

        /// <summary>
        /// Writes the default plugin config if it does not already exist.
        /// </summary>
        private void EnsureDefaultConfig()
        {
            if (File.Exists(_configPath))
                return;

            string text =
@"[General]
Enabled = true

# Allows new vanilla cave / alien / hell / boss spawner blocks to generate.
# false prevents NEW spawner blocks from being placed in newly generated terrain.
# Existing chunks and saves are not deleted or modified.
GenerateSpawnerBlocks = true

# Allows existing vanilla spawner blocks to be activated.
# false consumes spawner-origin enemy spawns and spawner block-state changes server-side.
AllowSpawnerActivation = true

# Logs each blocked spawner activation packet.
# Useful for debugging, but noisy if players keep trying old spawners.
LogBlockedActivation = false
";

            File.WriteAllText(_configPath, text);
        }

        /// <summary>
        /// Loads VanillaSpawners.Config.ini.
        /// </summary>
        private void LoadConfig()
        {
            Dictionary<string, string> values = ReadIni(_configPath);

            _config.Enabled = GetBool(values, "General.Enabled", true);
            _config.GenerateSpawnerBlocks = GetBool(values, "General.GenerateSpawnerBlocks", true);
            _config.AllowSpawnerActivation = GetBool(values, "General.AllowSpawnerActivation", true);
            _config.LogBlockedActivation = GetBool(values, "General.LogBlockedActivation", false);
        }

        /// <summary>
        /// Applies file-backed config to the static runtime switches used by Harmony patches.
        /// </summary>
        private void ApplyRuntimeConfig()
        {
            // Disabled plugin means vanilla behavior.
            ServerVanillaSpawnerRuntime.GenerateSpawnerBlocks =
                !_config.Enabled || _config.GenerateSpawnerBlocks;

            ServerVanillaSpawnerRuntime.AllowSpawnerActivation =
                !_config.Enabled || _config.AllowSpawnerActivation;
        }
        #endregion

        #region INI Helpers

        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;

            if (!File.Exists(path))
                return values;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? string.Empty).Trim();

                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                string fullKey = string.IsNullOrWhiteSpace(section) ? key : section + "." + key;

                values[fullKey] = value;
            }

            return values;
        }

        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out string raw) && bool.TryParse(raw, out bool value)
                ? value
                : fallback;
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Returns true for vanilla monster / alien / hell / boss spawner block types.
        /// </summary>
        private static bool IsVanillaSpawnerBlockType(int blockTypeValue)
        {
            return blockTypeValue switch
            {
                // Enemy spawner states.
                72    // EnemySpawnOn
                or 73 // EnemySpawnOff
                or 74 // EnemySpawnRareOn
                or 75 // EnemySpawnRareOff
                or 86 // EnemySpawnDim
                or 87 // EnemySpawnRareDim

                // Alien spawner states.
                or 80 // AlienSpawnOn
                or 81 // AlienSpawnOff
                or 88 // AlienSpawnDim

                // Hell spawner states.
                or 82 // HellSpawnOn
                or 83 // HellSpawnOff
                or 89 // HellSpawnDim

                // Boss spawner states.
                or 84 // BossSpawnOn
                or 85 // BossSpawnOff
                or 90 // BossSpawnDim

                // Alien horde states.
                or 91 // AlienHordeOn
                or 92 // AlienHordeOff
                or 93 // AlienHordeDim
                  => true,
                _ => false,
            };
        }

        /// <summary>
        /// Returns true when a reflected Vector3-like value is not approximately zero.
        /// </summary>
        private static bool IsNonZeroVector3(object value)
        {
            if (value == null)
                return false;

            if (!TryReadFloatMember(value, "X", out float x))
                return false;

            if (!TryReadFloatMember(value, "Y", out float y))
                return false;

            if (!TryReadFloatMember(value, "Z", out float z))
                return false;

            const float epsilon = 0.001f;

            return Math.Abs(x) > epsilon ||
                   Math.Abs(y) > epsilon ||
                   Math.Abs(z) > epsilon;
        }

        private static bool TryReadFloatMember(object value, string name, out float result)
        {
            result = 0f;

            try
            {
                Type type = value.GetType();

                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    result = Convert.ToSingle(property.GetValue(value, null));
                    return true;
                }

                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    result = Convert.ToSingle(field.GetValue(value));
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
        #endregion

        #region Nested Types

        /// <summary>
        /// File-backed VanillaSpawners config.
        /// </summary>
        private sealed class VanillaSpawnerConfig
        {
            public bool Enabled = true;
            public bool GenerateSpawnerBlocks = true;
            public bool AllowSpawnerActivation = true;
            public bool LogBlockedActivation = false;
        }
        #endregion
    }
    #endregion

    #region Harmony Patches

    /// <summary>
    /// Prevents cave generation from placing normal / rare / alien spawner blocks.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_CaveBiome_GetEnemyBlock_ServerVanillaSpawners
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("DNA.CastleMinerZ.Terrain.WorldBuilders.CaveBiome");
            return type == null ? null : AccessTools.Method(type, "GetEnemyBlock");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool Prefix(ref int __result)
        {
            if (ServerVanillaSpawnerRuntime.GenerateSpawnerBlocks)
                return true;

            // BlockTypeEnum.Empty is 0 and Block.SetType(0, Empty) resolves to 0 in vanilla.
            __result = 0;
            return false;
        }
    }

    /// <summary>
    /// Prevents hell floor generation from placing boss spawner blocks.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_HellFloorBiome_CheckForBossSpawns_ServerVanillaSpawners
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome");
            return type == null ? null : AccessTools.Method(type, "CheckForBossSpawns");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool Prefix()
        {
            return ServerVanillaSpawnerRuntime.GenerateSpawnerBlocks;
        }
    }

    /// <summary>
    /// Makes host-side BlockType.IsSpawnerClickable return false when activation is disabled.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_BlockType_IsSpawnerClickable_ServerVanillaSpawners
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("DNA.CastleMinerZ.Terrain.BlockType");
            return type == null ? null : AccessTools.Method(type, "IsSpawnerClickable");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool Prefix(ref bool __result)
        {
            if (ServerVanillaSpawnerRuntime.AllowSpawnerActivation)
                return true;

            __result = false;
            return false;
        }
    }
    #endregion
}
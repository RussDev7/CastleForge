/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Globalization;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using DNA.CastleMinerZ;
using System.Linq;
using System;
using DNA;

using static CastleWallsMk2.CastleWallsMk2;
using static CastleWallsMk2.FeedbackRouter;
using static CastleWallsMk2.CryptoRng;

namespace CastleWallsMk2
{
    /// <summary>
    /// Host-side public/server chat command registry and execution helpers.
    ///
    /// Purpose:
    /// - Defines which public chat commands exist.
    /// - Stores enable states for each command.
    /// - Tracks promoted admins for the current session.
    /// - Resolves chat text into command dispatch.
    /// - Handles per-command execution, validation, and private replies.
    ///
    /// Notes:
    /// - This class is designed to work with the ImGui server-command tab and
    ///   the _processBroadcastTextMessage intercept path.
    /// - Admin membership is tracked by Gamertag for the current session.
    /// - The registry is the shared source of truth for both UI and runtime handling.
    /// </summary>
    internal class ServerCommands
    {
        #region Public Toggles / Command Enable States

        // Host-side public chat commands.
        public static bool _serverCommandsAnnounceStateChanges = true;
        public static bool _serverCommandsReplyWhenDisabled    = false;
        public static bool _serverCmdItemIdsEnabled            = true;
        public static bool _serverCmdEnemyIdsEnabled           = true;
        public static bool _serverCmdBlockIdsEnabled           = true;
        public static bool _serverCommandsEnabled              = false;
        public static bool _serverCommandsHideHandledChat      = true;
        public static bool _serverCmdSummonEnabled             = true;
        public static bool _serverCmdItemEnabled               = true;
        public static bool _serverCmdTimeEnabled               = true;
        public static bool _serverCmdButcherEnabled            = true;
        public static bool _serverCmdHelpEnabled               = true;
        public static bool _serverCmdSpawnEnabled              = true;
        public static bool _serverCmdSuicideEnabled            = true;
        public static bool _serverCmdLootboxEnabled            = true;
        public static bool _serverCmdDragonEnabled             = true;
        public static bool _serverCmdWhoAmIEnabled             = true;
        public static bool _serverCmdKickEnabled               = true;
        public static bool _serverCmdBanEnabled                = true;

        #endregion

        #region Session / Permission State

        // Session/server role mapping.
        // Store by Gamertag instead of transient gamer-id so admin selections
        // survive leave/rejoin during the same client session more reliably.
        private static readonly HashSet<string> _adminGamertags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Client-Side Server Commands

        /// <summary>
        /// Server permission tiers used to gate public chat commands.
        ///
        /// Levels:
        /// - Member: Default remote player access.
        /// - Admin: Elevated permissions configured by the host.
        /// - Host:  Local host/operator authority.
        /// </summary>
        internal enum ServerPermissionLevel
        {
            Member = 0,
            Admin  = 1,
            Host   = 2,
        }

        /// <summary>
        /// Metadata for a remotely invokable client-side chat command.
        /// The same registry drives both the ImGui scrollbox and the
        /// _processBroadcastTextMessage intercept.
        /// </summary>
        internal sealed class ServerCommandDefinition
        {
            #region Fields

            public readonly string                Name;
            public readonly string                Usage;
            public readonly string                Description;
            public readonly ServerPermissionLevel RequiredPermission;
            public readonly Func<bool>            GetEnabled;
            public readonly Action<bool>          SetEnabled;

            #endregion

            #region Construction

            public ServerCommandDefinition(
                string                name,
                string                usage,
                string                description,
                ServerPermissionLevel requiredPermission,
                Func<bool>            getEnabled,
                Action<bool>          setEnabled)
            {
                Name               = name               ?? string.Empty;
                Usage              = usage              ?? string.Empty;
                Description        = description        ?? string.Empty;
                RequiredPermission = requiredPermission;
                GetEnabled         = getEnabled;
                SetEnabled         = setEnabled;
            }
            #endregion

            #region Derived Display Values

            public string DisplaySyntax
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(Usage))
                        return $"/{Name}";
                    return $"/{Name} {Usage}";
                }
            }
            #endregion
        }

        /// <summary>
        /// Client-authoritative remote chat commands handled from regular broadcast chat.
        ///
        /// Responsibilities:
        /// - Defines the command registry.
        /// - Validates permissions and enabled states.
        /// - Parses incoming command lines.
        /// - Dispatches to the appropriate command handler.
        ///
        /// Notes:
        /// - Only processes remote players, not the local slash-command path.
        /// - Supports both "/command" and "!command" prefixes.
        /// - Returns success to the Harmony prefix so handled commands can be
        ///   hidden from public chat when desired.
        /// </summary>
        internal static class ServerCommandRegistry
        {
            #region Constants

            private const int HelpPageSize = 5;
            private const int EnumPageSize = 5;

            #endregion

            #region Command Registry

            private static readonly ServerCommandDefinition[] _definitions = new ServerCommandDefinition[]
            {
                // Member commands.
                new ServerCommandDefinition("help",     "[page]",                    "Displays a list of public server commands.",                     ServerPermissionLevel.Member, () => _serverCmdHelpEnabled,     value => _serverCmdHelpEnabled     = value),
                new ServerCommandDefinition("whoami",   string.Empty,                "Displays your current server permission level.",                 ServerPermissionLevel.Member, () => _serverCmdWhoAmIEnabled,   value => _serverCmdWhoAmIEnabled   = value),
                new ServerCommandDefinition("itemids",  "[page]",                    "Displays item enum ids.",                                        ServerPermissionLevel.Member, () => _serverCmdItemIdsEnabled,  value => _serverCmdItemIdsEnabled  = value),
                new ServerCommandDefinition("enemyids", "[page]",                    "Displays enemy enum ids.",                                       ServerPermissionLevel.Member, () => _serverCmdEnemyIdsEnabled, value => _serverCmdEnemyIdsEnabled = value),
                new ServerCommandDefinition("blockids", "[page]",                    "Displays block enum ids.",                                       ServerPermissionLevel.Member, () => _serverCmdBlockIdsEnabled, value => _serverCmdBlockIdsEnabled = value),
                new ServerCommandDefinition("summon",   "[id] [amount] [offset]",    "Allows another user to summon a monster.",                       ServerPermissionLevel.Member, () => _serverCmdSummonEnabled,   value => _serverCmdSummonEnabled   = value),
                new ServerCommandDefinition("item",     "[id] [amount]",             "Allows another user to give themselves items.",                  ServerPermissionLevel.Member, () => _serverCmdItemEnabled,     value => _serverCmdItemEnabled     = value),
                new ServerCommandDefinition("time",     "[day|noon|night|midnight]", "Changes world time to a preset: day, noon, night, or midnight.", ServerPermissionLevel.Member, () => _serverCmdTimeEnabled,     value => _serverCmdTimeEnabled     = value),
                new ServerCommandDefinition("butcher",  string.Empty,                "Butchers all enemies including dragons.",                        ServerPermissionLevel.Member, () => _serverCmdButcherEnabled,  value => _serverCmdButcherEnabled  = value),
                new ServerCommandDefinition("spawn",    string.Empty,                "Returns the player who ran the command back to spawn.",          ServerPermissionLevel.Member, () => _serverCmdSpawnEnabled,    value => _serverCmdSpawnEnabled    = value),
                new ServerCommandDefinition("suicide",  string.Empty,                "Kills the player who ran the command. Host is excluded.",        ServerPermissionLevel.Member, () => _serverCmdSuicideEnabled,  value => _serverCmdSuicideEnabled  = value),
                new ServerCommandDefinition("lootbox",  "[amount]",                  "Spawns lucky loot boxes above the player.",                      ServerPermissionLevel.Member, () => _serverCmdLootboxEnabled,  value => _serverCmdLootboxEnabled  = value),
                new ServerCommandDefinition("dragon",   "[id]",                      "Spawns the selected dragon type using the host's authority.",    ServerPermissionLevel.Member, () => _serverCmdDragonEnabled,   value => _serverCmdDragonEnabled   = value),

                // Admin commands.
                new ServerCommandDefinition("kick",     "[player]",                  "Kicks the selected player from the session.",                    ServerPermissionLevel.Admin,  () => _serverCmdKickEnabled,     value => _serverCmdKickEnabled     = value),
                new ServerCommandDefinition("ban",      "[player]",                  "Bans and kicks the selected player from the session.",           ServerPermissionLevel.Admin,  () => _serverCmdBanEnabled,      value => _serverCmdBanEnabled      = value),
            };

            private static readonly Dictionary<string, ServerCommandDefinition> _lookup =
                _definitions.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Exposes the command registry to UI consumers such as the ImGui tab.
            /// </summary>
            public static IReadOnlyList<ServerCommandDefinition> Definitions => _definitions;

            #endregion

            #region Admin / Permission Helpers

            /// <summary>
            /// Normalizes Gamertag values before storage/comparison.
            /// </summary>
            private static string NormalizeGamertag(string gamertag)
            {
                return (gamertag ?? string.Empty).Trim();
            }

            /// <summary>
            /// Returns whether the supplied Gamertag is currently promoted to admin.
            /// </summary>
            public static bool IsAdminGamertag(string gamertag)
            {
                string key = NormalizeGamertag(gamertag);
                return key.Length > 0 && _adminGamertags.Contains(key);
            }

            /// <summary>
            /// Adds or removes a Gamertag from the current-session admin registry.
            /// </summary>
            public static void SetAdminGamertag(string gamertag, bool enabled)
            {
                string key = NormalizeGamertag(gamertag);
                if (key.Length == 0)
                    return;

                if (enabled) _adminGamertags.Add(key);
                else _adminGamertags.Remove(key);
            }

            /// <summary>
            /// Clears the current-session admin registry.
            /// </summary>
            public static void ClearAdminGamertags()
            {
                _adminGamertags.Clear();
            }

            /// <summary>
            /// Resolves the effective permission level for the supplied gamer.
            /// </summary>
            public static ServerPermissionLevel GetPermissionLevel(NetworkGamer gamer)
            {
                if (gamer == null)
                    return ServerPermissionLevel.Member;

                if (ReferenceEquals(gamer, CastleMinerZGame.Instance?.MyNetworkGamer))
                    return ServerPermissionLevel.Host;

                return IsAdminGamertag(gamer.Gamertag)
                    ? ServerPermissionLevel.Admin
                    : ServerPermissionLevel.Member;
            }

            /// <summary>
            /// Converts a permission level to a friendly display label.
            /// </summary>
            public static string GetPermissionLabel(ServerPermissionLevel level)
            {
                switch (level)
                {
                    case ServerPermissionLevel.Host:  return "Host";
                    case ServerPermissionLevel.Admin: return "Admin";
                    default:                          return "Member";
                }
            }

            /// <summary>
            /// Returns whether a gamer may use the supplied command definition.
            /// </summary>
            public static bool CanUseCommand(NetworkGamer gamer, ServerCommandDefinition def)
                => def != null && (int)GetPermissionLevel(gamer) >= (int)def.RequiredPermission;

            /// <summary>
            /// No-op initializer used to force static field construction from external callers.
            /// </summary>
            public static void EnsureInitialized()
            {
                // Intentionally empty.
                // Calling this forces the static constructor/field initialization path
                // so the UI and patch can safely reference the registry immediately.
            }
            #endregion

            #region Chat Intercept / Dispatch

            /// <summary>
            /// Attempts to parse and handle an incoming broadcast chat message as a server command.
            ///
            /// Flow:
            /// - Validate runtime state.
            /// - Ignore local-player slash commands.
            /// - Extract command line from the broadcast text.
            /// - Resolve the command definition from the registry.
            /// - Apply enable-state and permission checks.
            /// - Dispatch to the matching command handler.
            ///
            /// Returns:
            /// - true  if the command path was claimed/handled.
            /// - false if the message should continue through normal chat flow.
            /// </summary>
            public static bool TryHandleIncomingChatCommand(BroadcastTextMessage btm, NetworkGamer sender, out bool suppressOriginal)
            {
                suppressOriginal = false;

                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game == null || btm == null || sender == null)
                        return false;

                    // Only the host should execute public server commands.
                    // if (!(game.MyNetworkGamer?.IsHost ?? false)) return false;

                    // Let our own local slash-command system keep working normally.
                    if (ReferenceEquals(sender, game.MyNetworkGamer))
                        return false;

                    if (!TryExtractCommandLine(btm.Message, sender, out string commandLine))
                        return false;

                    string[] parts = SplitArgs(commandLine);
                    if (parts.Length == 0)
                        return false;

                    string commandName = (parts[0] ?? string.Empty).TrimStart('!', '/');
                    if (!_lookup.TryGetValue(commandName, out ServerCommandDefinition def))
                        return false;

                    suppressOriginal = _serverCommandsHideHandledChat;

                    if (!_serverCommandsEnabled)
                    {
                        if (_serverCommandsReplyWhenDisabled)
                            ReplyPrivate(sender, "[Server] Public server commands are currently disabled.");

                        SendLog($"Blocked remote server command while disabled: '{commandName}' from '{sender.Gamertag}'.");
                        return true;
                    }

                    if (!(def.GetEnabled?.Invoke() ?? false))
                    {
                        ReplyPrivate(sender, $"[Server] '{def.DisplaySyntax}' is disabled.");
                        SendLog($"Blocked disabled remote server command: '{commandName}' from '{sender.Gamertag}'.");
                        return true;
                    }

                    if (!CanUseCommand(sender, def))
                    {
                        ReplyPrivate(sender, $"[Server] '{def.DisplaySyntax}' requires {GetPermissionLabel(def.RequiredPermission)} permissions.");
                        SendLog($"Blocked remote server command due to permissions: '{commandName}' from '{sender.Gamertag}'.");
                        return true;
                    }

                    if (!(sender.Tag is Player senderPlayer))
                    {
                        ReplyPrivate(sender, "[Server] ERROR: Could not resolve your player object.");
                        return true;
                    }

                    string[] args = new string[Math.Max(0, parts.Length - 1)];
                    if (args.Length > 0)
                        Array.Copy(parts, 1, args, 0, args.Length);

                    bool handled;
                    switch (commandName.ToLowerInvariant())
                    {
                        case "itemids":  handled = HandleItemIds(sender, args);  break;
                        case "enemyids": handled = HandleEnemyIds(sender, args); break;
                        case "blockids": handled = HandleBlockIds(sender, args); break;
                        case "summon":   handled = HandleSummon(sender, args);   break;
                        case "item":     handled = HandleItem(sender, args);     break;
                        case "time":     handled = HandleTime(sender, args);     break;
                        case "butcher":  handled = HandleButcher(sender);        break;
                        case "help":     handled = HandleHelp(sender, args);     break;
                        case "whoami":   handled = HandleWhoAmI(sender);         break;
                        case "spawn":    handled = HandleSpawn(sender);          break;
                        case "kick":     handled = HandleKick(sender, args);     break;
                        case "ban":      handled = HandleBan(sender, args);      break;
                        case "suicide":  handled = HandleSuicide(sender);        break;
                        case "lootbox":  handled = HandleLootbox(sender, args);  break;
                        case "dragon":   handled = HandleDragon(sender, args);   break;
                        default: handled = false; break;
                    }

                    return handled;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (sender != null)
                            ReplyPrivate(sender, $"[Server] ERROR: {ex.Message}");
                    }
                    catch { }

                    SendLog($"Server command intercept error: {ex.GetType().Name}: {ex.Message}");
                    return true; // Swallow on hard failures once we claimed the command path.
                }
            }
            #endregion

            #region Core Member Commands

            /// <summary>
            /// Displays the paged list of commands the caller can currently use.
            /// </summary>
            private static bool HandleHelp(NetworkGamer sender, string[] args)
            {
                int page = 1;
                if (args.Length > 0 && !int.TryParse(args[0], out page))
                {
                    ReplyUsage(sender, "help", "[page]");
                    return true;
                }

                if (page < 1)
                    page = 1;

                var enabled = _definitions.Where(x => (x.GetEnabled?.Invoke() ?? false) && CanUseCommand(sender, x)).ToList();
                int totalPages = Math.Max(1, (enabled.Count + HelpPageSize - 1) / HelpPageSize);
                if (page > totalPages)
                    page = totalPages;

                int start = (page - 1) * HelpPageSize;
                int count = Math.Min(HelpPageSize, Math.Max(0, enabled.Count - start));

                ReplyPrivate(sender, $"==== Public Server Commands ({page}/{totalPages}) ====");
                for (int i = 0; i < count; i++)
                {
                    var def = enabled[start + i];
                    ReplyPrivate(sender, $"{def.DisplaySyntax} - {def.Description}");
                }

                if (enabled.Count == 0)
                    ReplyPrivate(sender, "[Server] No commands are currently enabled.");
                else if (page < totalPages)
                    ReplyPrivate(sender, $"Type '/help {page + 1}' for the next page.");

                return true;
            }

            /// <summary>
            /// Displays the caller's current permission level.
            /// </summary>
            private static bool HandleWhoAmI(NetworkGamer sender)
            {
                ServerPermissionLevel level = GetPermissionLevel(sender);
                ReplyPrivate(sender, $"[Server] You are: {GetPermissionLabel(level)}.");
                SendLog($"ServerCmd whoami: {sender.Gamertag} -> {GetPermissionLabel(level)}");
                return true;
            }
            #endregion

            #region Enum Listing Commands

            /// <summary>
            /// Displays paged item enum ids.
            /// </summary>
            private static bool HandleItemIds(NetworkGamer sender, string[] args)
            {
                var entries = Enum.GetNames(typeof(InventoryItemIDs))
                    .Select(name => new EnumEntry
                    {
                        Name = name,
                        Id = (int)Enum.Parse(typeof(InventoryItemIDs), name)
                    })
                    .OrderBy(x => x.Id)
                    .ToList();

                return HandleEnumList(sender, args, "itemids", "Item IDs", entries);
            }

            /// <summary>
            /// Displays paged enemy enum ids.
            /// </summary>
            private static bool HandleEnemyIds(NetworkGamer sender, string[] args)
            {
                var entries = Enum.GetNames(typeof(EnemyTypeEnum))
                    .Where(name =>
                        !string.Equals(name, "COUNT", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("NUM_", StringComparison.OrdinalIgnoreCase))
                    .Select(name => new EnumEntry
                    {
                        Name = name,
                        Id = (int)Enum.Parse(typeof(EnemyTypeEnum), name)
                    })
                    .OrderBy(x => x.Id)
                    .ToList();

                return HandleEnumList(sender, args, "enemyids", "Enemy IDs", entries);
            }

            /// <summary>
            /// Displays paged block enum ids.
            /// </summary>
            private static bool HandleBlockIds(NetworkGamer sender, string[] args)
            {
                var entries = Enum.GetNames(typeof(BlockTypeEnum))
                    .Where(name =>
                        !string.Equals(name, "NumberOfBlocks", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, "Uninitialized", StringComparison.OrdinalIgnoreCase))
                    .Select(name => new EnumEntry
                    {
                        Name = name,
                        Id = (int)Enum.Parse(typeof(BlockTypeEnum), name)
                    })
                    .OrderBy(x => x.Id)
                    .ToList();

                return HandleEnumList(sender, args, "blockids", "Block IDs", entries);
            }
            #endregion

            #region Gameplay / Spawn Commands

            /// <summary>
            /// Summons the requested enemy type/count near the caller.
            /// </summary>
            private static bool HandleSummon(NetworkGamer sender, string[] args)
            {
                if (args.Length < 1)
                {
                    ReplyUsage(sender, "summon", "[id] [amount] (offset)");
                    return true;
                }

                if (!int.TryParse(args[0], out int rawEnemyId) || !TryGetEnumValue(rawEnemyId, out EnemyTypeEnum enemyType))
                {
                    ReplyPrivate(sender, "[Server] ERROR: Invalid enemy id.");
                    return true;
                }

                int amount = 1;
                if (args.Length > 1 && !int.TryParse(args[1], out amount))
                {
                    ReplyUsage(sender, "summon", "[id] [amount] (offset)");
                    return true;
                }

                int offset = 5;
                if (args.Length > 2 && !int.TryParse(args[2], out offset))
                {
                    ReplyUsage(sender, "summon", "[id] [amount] (offset)");
                    return true;
                }

                amount = ClampInt(amount, 1, 100);
                offset = ClampInt(offset, 0, 25);

                SpawnMob(sender, enemyType, amount, offset, false);

                ReplyPrivate(sender, $"[Server] Summoned {amount} {enemyType} (o:{offset}).");
                SendLog($"ServerCmd summon: {sender.Gamertag} -> {enemyType} x{amount} (o:{offset})");
                return true;
            }

            /// <summary>
            /// Gives the caller the requested item and amount.
            /// </summary>
            private static bool HandleItem(NetworkGamer sender, string[] args)
            {
                if (args.Length < 1)
                {
                    ReplyUsage(sender, "item", "[id] [amount]");
                    return true;
                }

                if (!int.TryParse(args[0], out int rawItemId) || !TryGetEnumValue(rawItemId, out InventoryItemIDs itemId))
                {
                    ReplyPrivate(sender, "[Server] ERROR: Invalid item id.");
                    return true;
                }

                int amount = 1;
                if (args.Length > 1 && !int.TryParse(args[1], out amount))
                {
                    ReplyUsage(sender, "item", "[id] [amount]");
                    return true;
                }

                amount = ClampInt(amount, 1, 2500);

                int remainingAmount = amount;
                try
                {
                    while (remainingAmount > 999)
                    {
                        GivePlayerItems((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, sender, itemId, 999);
                        remainingAmount -= 999;
                    }
                    GivePlayerItems((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, sender, itemId, remainingAmount);
                }
                catch { }

                ReplyPrivate(sender, $"[Server] Gave you {amount} {itemId}.");
                SendLog($"ServerCmd item: {sender.Gamertag} -> {itemId} x{amount}");
                return true;
            }

            /// <summary>
            /// Sets world time to one of the supported named presets.
            /// </summary>
            private static bool HandleTime(NetworkGamer sender, string[] args)
            {
                if (args.Length < 1)
                {
                    ReplyUsage(sender, "time", "[day|noon|night|midnight]");
                    return true;
                }

                try
                {
                    // Preserve the current integer day and only change the fractional time.
                    float currentDay = (float)(int)CastleMinerZGame.Instance.GameScreen.Day;

                    float timeFraction;
                    string timeName;

                    switch ((args[0] ?? string.Empty).Trim().ToLowerInvariant())
                    {
                        case "day":
                        case "morning":
                            timeFraction = 0.40f;
                            timeName = "day";
                            break;

                        case "noon":
                            timeFraction = 0.50f;
                            timeName = "noon";
                            break;

                        case "night":
                        case "sunset":
                            timeFraction = 0.75f;
                            timeName = "night";
                            break;

                        case "midnight":
                            timeFraction = 0.95f;
                            timeName = "midnight";
                            break;

                        default:
                            ReplyUsage(sender, "time", "[day|noon|night|midnight]");
                            return true;
                    }

                    float newTimeValue = currentDay + timeFraction;

                    // Keep your existing network path, but also sync local immediately.
                    TimeOfDayMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, newTimeValue);
                    CastleMinerZGame.Instance.GameScreen.Day = newTimeValue;

                    ReplyPrivate(sender, $"[Server] World time set to {timeName}.");
                    SendLog($"ServerCmd time: {sender.Gamertag} -> {timeName} ({newTimeValue.ToString("0.00", CultureInfo.InvariantCulture)})");
                    return true;
                }
                catch
                {
                    ReplyUsage(sender, "time", "[day|noon|night|midnight]");
                    return true;
                }
            }

            /// <summary>
            /// Kills all active monsters.
            /// </summary>
            private static bool HandleButcher(NetworkGamer sender)
            {
                KillAllMonsters();
                ReplyPrivate(sender, "[Server] Butchered all enemies.");
                SendLog($"ServerCmd butcher: {sender.Gamertag}");
                return true;
            }

            /// <summary>
            /// Sends the caller back to spawn.
            /// </summary>
            private static bool HandleSpawn(NetworkGamer sender)
            {
                RestartLevelPrivate(CastleMinerZGame.Instance?.MyNetworkGamer, sender);
                ReplyPrivate(sender, "[Server] Returned you to spawn.");
                SendLog($"ServerCmd spawn: {sender.Gamertag}");
                return true;
            }

            /// <summary>
            /// Kills the caller through the existing selected-player helper path.
            /// </summary>
            private static bool HandleSuicide(NetworkGamer sender)
            {
                KillSelectedPlayer(sender);

                ReplyPrivate(sender, "[Server] You have been slain.");
                SendLog($"ServerCmd suicide: {sender.Gamertag}");
                return true;
            }

            /// <summary>
            /// Spawns one or more loot boxes above the caller if space is available.
            /// </summary>
            private static bool HandleLootbox(NetworkGamer sender, string[] args)
            {
                int amount = 1;
                if (args.Length > 0 && !int.TryParse(args[0], out amount))
                {
                    ReplyUsage(sender, "lootbox", "[amount]");
                    return true;
                }

                amount = ClampInt(amount, 1, 10);

                IntVector3 aboveHead = new IntVector3(
                    (int)Math.Floor(((Player)sender?.Tag)?.LocalPosition.X     ?? 0),
                    (int)Math.Floor(((Player)sender?.Tag)?.LocalPosition.Y + 1 ?? 0),
                    (int)Math.Floor(((Player)sender?.Tag)?.LocalPosition.Z     ?? 0));

                if (InGameHUD.GetBlock(aboveHead) != BlockTypeEnum.Empty)
                {
                    ReplyPrivate(sender, "[Server] ERROR: No room exists above your head.");
                    return true;
                }

                for (int i = 0; i < amount; i++)
                {
                    // Get lootbox type.
                    BlockTypeEnum lootboxType = (BlockTypeEnum)GenerateRandomNumberInclusive(69, 70); // ID 69: Lootbox, ID 70: LuckyLootbox.

                    // Spawn loot in via vanilla processing.
                    PossibleLootType.ProcessLootBlockOutput(lootboxType, (IntVector3)(((Player)sender?.Tag)?.LocalPosition ?? IntVector3.Zero));
                }

                ReplyPrivate(sender, $"[Server] Spawned {amount} lucky loot box{(amount == 1 ? string.Empty : "'s")}.");
                SendLog($"ServerCmd lootbox: {sender.Gamertag} -> {amount}");
                return true;
            }

            /// <summary>
            /// Spawns the requested dragon type if no dragon is already active.
            /// </summary>
            private static bool HandleDragon(NetworkGamer sender, string[] args)
            {
                if (args.Length < 1)
                {
                    ReplyUsage(sender, "dragon", "[id]");
                    return true;
                }

                if (!int.TryParse(args[0], out int rawDragonId) || !TryGetEnumValue(rawDragonId, out DragonTypeEnum dragonType) || dragonType == DragonTypeEnum.COUNT)
                {
                    ReplyPrivate(sender, "[Server] ERROR: Invalid dragon id.");
                    return true;
                }

                if (EnemyManager.Instance?.DragonIsActive ?? false)
                {
                    ReplyPrivate(sender, "[Server] ERROR: Existing dragon is already active.");
                    return true;
                }

                SpawnDragonMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, sender?.Id ?? 0, dragonType, false, -1f);
                ReplyPrivate(sender, $"[Server] Spawned {dragonType} dragon.");
                SendLog($"ServerCmd dragon: {sender.Gamertag} -> {dragonType}");
                return true;
            }
            #endregion

            #region Admin Commands

            /// <summary>
            /// Kicks the requested player from the session.
            /// </summary>
            private static bool HandleKick(NetworkGamer sender, string[] args)
            {
                if (!TryResolveTargetPlayer(sender, args, "kick", out NetworkGamer target))
                    return true;

                KickPlayerPrivate(CastleMinerZGame.Instance?.MyNetworkGamer, target, false);
                ReplyPrivate(sender, $"[Server] Kicked {target.Gamertag}.");
                SendLog($"ServerCmd kick: {sender.Gamertag} -> {target.Gamertag}");
                return true;
            }

            /// <summary>
            /// Bans and kicks the requested player from the session.
            /// </summary>
            private static bool HandleBan(NetworkGamer sender, string[] args)
            {
                if (!TryResolveTargetPlayer(sender, args, "ban", out NetworkGamer target))
                    return true;

                // If a promoted admin is banned, revoke admin immediately.
                SetAdminGamertag(target.Gamertag, false);

                KickPlayerPrivate(CastleMinerZGame.Instance?.MyNetworkGamer, target, true);
                ReplyPrivate(sender, $"[Server] Banned {target.Gamertag}.");
                SendLog($"ServerCmd ban: {sender.Gamertag} -> {target.Gamertag}");
                return true;
            }
            #endregion

            #region Target Resolution / Parsing Helpers

            /// <summary>
            /// Attempts to resolve a player target from the supplied argument list.
            ///
            /// Matching rules:
            /// - Prefer exact Gamertag matches.
            /// - Fall back to partial contains matches.
            /// - Reject ambiguous or missing results.
            ///
            /// Safety:
            /// - Rejects self-targeting for kick/ban.
            /// - Rejects targeting the host / server operator.
            /// </summary>
            private static bool TryResolveTargetPlayer(NetworkGamer sender, string[] args, string commandName, out NetworkGamer target)
            {
                target = null;

                if (args == null || args.Length == 0)
                {
                    ReplyUsage(sender, commandName, "[player]");
                    return false;
                }

                string wanted = string.Join(" ", args).Trim();
                if (string.IsNullOrWhiteSpace(wanted))
                {
                    ReplyUsage(sender, commandName, "[player]");
                    return false;
                }

                var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
                if (session == null)
                {
                    ReplyPrivate(sender, "[Server] ERROR: No active session.");
                    return false;
                }

                var exactMatches = new List<NetworkGamer>();
                var partialMatches = new List<NetworkGamer>();

                foreach (NetworkGamer g in session.AllGamers)
                {
                    if (g == null)
                        continue;

                    string tag = g.Gamertag ?? string.Empty;
                    if (tag.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                        exactMatches.Add(g);
                    else if (tag.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                        partialMatches.Add(g);
                }

                if (exactMatches.Count == 1) target = exactMatches[0];
                else if (exactMatches.Count > 1)
                {
                    ReplyPrivate(sender, $"[Server] ERROR: Multiple exact matches found for '{wanted}'.");
                    return false;
                }
                else if (partialMatches.Count == 1) target = partialMatches[0];
                else if (partialMatches.Count > 1)
                {
                    ReplyPrivate(sender, $"[Server] ERROR: Multiple players match '{wanted}'. Be more specific.");
                    return false;
                }

                if (target == null)
                {
                    ReplyPrivate(sender, $"[Server] ERROR: Could not find player '{wanted}'.");
                    return false;
                }

                if (ReferenceEquals(target, sender))
                {
                    ReplyPrivate(sender, $"[Server] ERROR: Use '/suicide' if you want to target yourself.");
                    return false;
                }

                if (sender.IsHost || ReferenceEquals(target, CastleMinerZGame.Instance?.MyNetworkGamer))
                {
                    if (sender.IsHost)
                        ReplyPrivate(sender, "[Server] ERROR: Cannot target the host.");
                    else if (ReferenceEquals(target, CastleMinerZGame.Instance?.MyNetworkGamer))
                        ReplyPrivate(sender, "[Server] ERROR: Cannot target the server-op.");
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Attempts to extract a command line from raw broadcast chat text.
            /// </summary>
            private static bool TryExtractCommandLine(string rawMessage, NetworkGamer sender, out string commandLine)
            {
                commandLine = string.Empty;

                if (string.IsNullOrWhiteSpace(rawMessage) || sender == null)
                    return false;

                string text = rawMessage.Trim();

                string prefix = (sender.Gamertag ?? string.Empty) + ": ";
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                    text = text.Substring(prefix.Length).TrimStart();

                if (text.Length == 0)
                    return false;

                char c = text[0];
                if (c != '!' && c != '/')
                    return false;

                commandLine = text;
                return true;
            }

            /// <summary>
            /// Splits a command line into simple space-delimited arguments.
            /// </summary>
            private static string[] SplitArgs(string commandLine)
            {
                if (string.IsNullOrWhiteSpace(commandLine))
                    return Array.Empty<string>();

                return commandLine
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            #endregion

            #region Reply / Validation Helpers

            /// <summary>
            /// Sends a standard usage reply for a command.
            /// </summary>
            private static void ReplyUsage(NetworkGamer to, string command, string usage)
            {
                ReplyPrivate(to, $"[Server] Usage: /{command} {usage}".TrimEnd());
            }

            /// <summary>
            /// Sends a private chat message from the local host/operator to the target gamer.
            /// </summary>
            private static void ReplyPrivate(NetworkGamer to, string message)
            {
                var from = CastleMinerZGame.Instance?.MyNetworkGamer;
                if (from == null || to == null || string.IsNullOrWhiteSpace(message))
                    return;

                var sendInstance     = MessageBridge.Get<BroadcastTextMessage>();
                sendInstance.Message = message;
                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }

            /// <summary>
            /// Clamps an integer to the supplied inclusive range.
            /// </summary>
            private static int ClampInt(int value, int min, int max)
            {
                if (value < min) return min;
                if (value > max) return max;
                return value;
            }

            /// <summary>
            /// Clamps a float to the supplied inclusive range.
            /// </summary>
            private static float ClampFloat(float value, float min, float max)
            {
                if (value < min) return min;
                if (value > max) return max;
                return value;
            }

            /// <summary>
            /// Attempts to validate and cast a raw integer value into an enum member.
            /// </summary>
            private static bool TryGetEnumValue<TEnum>(int rawValue, out TEnum value) where TEnum : struct
            {
                if (Enum.IsDefined(typeof(TEnum), rawValue))
                {
                    value = (TEnum)Enum.ToObject(typeof(TEnum), rawValue);
                    return true;
                }

                value = default;
                return false;
            }
            #endregion

            #region Enum Paging Helpers

            /// <summary>
            /// Simple enum row used for paged id/name display.
            /// </summary>
            private sealed class EnumEntry
            {
                public int Id;
                public string Name;
            }

            /// <summary>
            /// Displays a paged list of enum entries to the caller.
            /// </summary>
            private static bool HandleEnumList(
                NetworkGamer sender,
                string[] args,
                string commandName,
                string title,
                List<EnumEntry> entries)
            {
                int page = 1;
                if (args.Length > 0 && !int.TryParse(args[0], out page))
                {
                    ReplyUsage(sender, commandName, "[page]");
                    return true;
                }

                if (page < 1)
                    page = 1;

                if (entries == null || entries.Count == 0)
                {
                    ReplyPrivate(sender, $"[Server] No {title.ToLowerInvariant()} ids are available.");
                    return true;
                }

                int totalPages = Math.Max(1, (entries.Count + EnumPageSize - 1) / EnumPageSize);
                if (page > totalPages)
                    page = totalPages;

                int start = (page - 1) * EnumPageSize;
                int count = Math.Min(EnumPageSize, Math.Max(0, entries.Count - start));

                ReplyPrivate(sender, $"==== {title} ({page}/{totalPages}) ====");

                for (int i = 0; i < count; i++)
                {
                    EnumEntry entry = entries[start + i];
                    ReplyPrivate(sender, $"{entry.Id} = {entry.Name}");
                }

                if (page < totalPages)
                    ReplyPrivate(sender, $"Type '/{commandName} {page + 1}' for the next page.");

                return true;
            }
            #endregion
        }
        #endregion
    }
}
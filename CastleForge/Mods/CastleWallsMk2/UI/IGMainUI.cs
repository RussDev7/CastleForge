/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text.RegularExpressions;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Globalization;
using System.Windows.Forms;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using System.Numerics;
using ModLoaderExt;
using System.Linq;
using System.Text;
using HarmonyLib;
using System.IO;
using ImGuiNET;
using System;

using static CastleWallsMk2.NetCallsMessagePatches;
using static CastleWallsMk2.CastleWallsMk2;
using static CastleWallsMk2.ExampleScripts;
using static CastleWallsMk2.FeedbackRouter;
using static CastleWallsMk2.ServerCommands;
using static CastleWallsMk2.GamePatches;
using static CastleWallsMk2.ModConfig;
using static ModLoader.LogSystem;

using Vector3 = Microsoft.Xna.Framework.Vector3;
using Color = Microsoft.Xna.Framework.Color;

// Packages:
// ImGui.Net: https://www.nuget.org/packages/ImGui.NET/
// cimgui   : https://github.com/ImGuiNET/ImGui.NET-nativebuild

namespace CastleWallsMk2
{
    /// <summary>
    /// ImGui re-creation of the WinForms panel in the screenshot.
    /// - Top row: "Player" and "Target" group boxes.
    /// - Bottom: "Player List" listbox.
    /// Wire up behavior via the static Callbacks below.
    /// Toggle visibility from the overlay (e.g., _toggleKey in ImGuiHud).
    /// </summary>
    internal static class IGMainUI
    {
        #region Static Initialization

        /// <summary>
        /// One-time static initialization for IGMainUI.
        /// Registers a callback with the ImGui renderer so it knows when any
        /// "force-visible" panels (e.g., pinned debug overlays) are active,
        /// allowing them to be drawn even when the main overlay is hidden.
        /// </summary>
        static IGMainUI()
        {
            // Specify what panels should be allowed to force draw to the renderer.
            ImGuiXnaRenderer.HasForceVisiblePanels = () => TestStatsVisible;
        }
        #endregion

        #region Configuration Helper

        // Purpose:
        //   Centralize where Dear ImGui reads/writes its layout state ("imgui.ini").
        //   Redirect ImGui to a per-mod folder: !Mods\<Namespace>\ImGui\imgui.ini
        //   (Namespace comes from IGMainUI's namespace so this stays correct.)

        static class ImGuiConfigPath
        {
            /// <summary>
            /// Directory where we store ImGui settings. Example:
            ///   C:\Game\...\!Mods\CastleWallsMk2\ImGui
            /// </summary>
            public static readonly string Dir =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(IGMainUI).Namespace, "ImGui");

            /// <summary>Full path to the INI file (imgui.ini) under <see cref="Dir"/>.</summary>
            public static readonly string File = Path.Combine(Dir, "imgui.ini");
        }

        /// <summary>
        /// Guards the one-time bootstrap of ImGui settings I/O.
        /// We deliberately allow ImGuiSettings_InitOnce() to be called every frame,
        /// but it will only run the expensive init once per context.
        /// </summary>
        private static bool _imguiSettingsBootstrapped;

        /// <summary>
        /// Ensure ImGui settings are redirected to !Mods\...\ImGui\imgui.ini
        /// and loaded exactly once per ImGui context. Safe to call every frame.
        /// </summary>
        public static void ImGuiSettings_InitOnce()
        {
            if (_imguiSettingsBootstrapped) return;
            ImGuiSettings_Init();              // Actual bootstrap work.
            _imguiSettingsBootstrapped = true; // Prevent repeated init.

            // Determine if to show the menu on startup based on the config.
            var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();
            try { ImGuiXnaRenderer.Visible = cfg.ShowMenuOnLaunch; } catch { }

            // Load the network sniffer settings.
            try
            {
                NetCaptureConfig.PreferredName  = cfg.NetCapturePreferredAdapter ?? "";
                NetCaptureConfig.PreferredIndex = cfg.NetCapturePreferredIndex;
                NetCaptureConfig.HideOwnIp      = cfg.NetCaptureHideOwnIp;
            }
            catch { /* Non-fatal if sniffer class missing in some builds. */ }
        }

        /// <summary>
        /// Bootstrap ImGui settings I/O:
        ///  • Creates our target directory if missing.
        ///  • Disables ImGui's default auto load/save (by nulling io.IniFilename).
        ///  • Loads settings from the custom path.
        /// </summary>
        public static void ImGuiSettings_Init()
        {
            // Make sure the target folder exists (OK if it already does).
            Directory.CreateDirectory(ImGuiConfigPath.Dir);

            unsafe
            {
                // Turn OFF Dear ImGui's default auto-load/save (which uses "imgui.ini" in the working dir).
                // We will manage loading/saving explicitly to our own path.
                ImGui.GetIO().NativePtr->IniFilename = null;
            }

            // Load settings from our custom file (if the file doesn't exist yet, load is a no-op).
            ImGui.LoadIniSettingsFromDisk(ImGuiConfigPath.File);
        }

        /// <summary>
        /// Save current ImGui settings to our custom INI path.
        /// Recommended to call on orderly shutdown (<see cref="Shutdown()"/>).
        /// </summary>
        public static void ImGuiSettings_Save()
        {
            ImGui.SaveIniSettingsToDisk(ImGuiConfigPath.File);
        }
        #endregion

        #region [*] Fields & State (Window + Widgets)

        public  static bool IsOpen;
        private static bool _tabPlayerOpen, _tabEnemiesOpen, _tabDragonOpen = false;
        private static bool _showDemoWindow;

        // --- UI state ---
        // Player group: Toggles mirror the WinForms checkboxes.

        private static TriState          _worldTime;
        private static PlayerSelectScope _shootBlockScope, _shootAmmoScope, _explosiveLasersScope;
        private static VanishSelectScope _vanishScope;
        public  static bool              _spawnMobSamePos;      // For the 'target' section.
        private static bool _god,
                            _host,
                            _noKick,
                            _noEnemies,
                            _infItems,
                            _infDurability,
                            _flyMode,
                            _fullBright,
                            _creativeMode,
                            _infJump,
                            _infAmmo,
                            _infClips,
                            _rapidFire,
                            _superGunStats,
                            _noTarget,
                            _tracers,
                            _hitboxes,
                            _nametags,
                            _playerAimbot,
                            _dragonAimbot,
                            _mobAimbot,
                            _aimbotBulletDrop,
                            _aimbotRequireLos,
                            _aimbotFaceEnemy,
                            _noGunCooldown,
                            _noGunRecoil,
                            _vanish,
                            _vanishIsDead,
                            _playerPosition,
                            _movementSpeed,
                            _pickupRange,
                            _softCrash,
                            _xray,
                            _instantMine,
                            _rapidTools,
                            _noMobBlocking,
                            _multiColorAmmo,
                            _multiColorRNG,
                            _shootBlocks,
                            _shootHostAmmo,
                            _shootGrenadeAmmo,
                            _shootRocketAmmo,
                            _freezeLasers,
                            _extendLaserTime,
                            _infiLaserPath,
                            _infiLaserBounce,
                            _explosiveLasers,
                            _noTPOnServerRestart,
                            _corruptOnKick,
                            _projectileTuning,
                            _freeFlyCamera,
                            _rideDragon,
                            _shootBowAmmo,
                            _noClip,
                            _explodingOres,
                            _shootFireballAmmo,
                            _rocketSpeed,
                            _forceRespawn,
                            _disableInvRetrieval,
                            _gravity,
                            _cameraXyz,
                            _mute,
                            _muteWarnOffender,
                            _muteShowMessage,
                            _trail,
                            _trailPrivate,
                            _shower,
                            _dragonCounter,
                            _hat,
                            _boots,
                            _rapidItems,
                            _disableControls,
                            _itemVortex,
                            _beaconMode,
                            _chaosMode,
                            _clockChaos,
                            _dragonChaos,
                            _ignoreChatNewlines,
                            _hug,
                            _noLavaVisuals,
                            _reliableFlood,
                            _blockEsp,
                            _blockEspHideTracers,
                            _spamTextShow,
                            _spamTextStart,
                            _spamTextSudo,
                            _spamTextExpandBox,
                            _chaoticAim,
                            _disableItemPickups,
                            _ghostMode,
                            _ghostModeHideName,
                            _alwaysDaySky,
                            _changeGameTitle,
                            _rapidPlace,
                            _sudoPlayer,
                            _doorSpam,
                            _allHarvest,
                            _pvpThorns,
                            _trailMode,
                            _deathAura,
                            _begoneAura,
                            _blockNuker;

                            // Debugging.
                            // _test;

        public static void ResetToggleStates()
        {
            // Assigns false to all mod-related flags:
            // These booleans control various cheat/debug features.
            _god = _host = _noEnemies = _infItems = _infDurability = _flyMode = _creativeMode =
            _infJump = _infAmmo = _infClips = _rapidFire = _superGunStats = _noTarget = _aimbotBulletDrop =
            _tracers = _hitboxes = _playerAimbot = _dragonAimbot = _mobAimbot = _aimbotRequireLos =
            _aimbotFaceEnemy = _fullBright = _noKick = _vanish = _vanishIsDead = _playerPosition =
            _movementSpeed = _pickupRange = _softCrash = _xray = _instantMine = _rapidTools = _noMobBlocking =
            _multiColorAmmo = _multiColorRNG = _noGunCooldown = _noGunRecoil = _shootBlocks =
            _shootHostAmmo = _shootGrenadeAmmo = _shootRocketAmmo = _freezeLasers = _extendLaserTime =
            _infiLaserPath = _infiLaserBounce = _explosiveLasers = _noTPOnServerRestart =
            _corruptOnKick = _projectileTuning = _freeFlyCamera = _rideDragon = _shootBowAmmo =
            _noClip = _explodingOres = _shootFireballAmmo = _rocketSpeed = _forceRespawn =
            _disableInvRetrieval = _gravity = _cameraXyz = _mute = _muteWarnOffender = _trail =
            _trailPrivate = _shower = _dragonCounter = _hat = _boots = _rapidItems = _disableControls =
            _itemVortex = _beaconMode = _chaosMode = _clockChaos = _dragonChaos = _ignoreChatNewlines = _hug =
            _noLavaVisuals = _reliableFlood = _blockEsp = _blockEspHideTracers = _nametags = _muteShowMessage =
            _spamTextShow = _spamTextStart = _spamTextSudo = _spamTextExpandBox = _chaoticAim = _disableItemPickups =
            _alwaysDaySky = _changeGameTitle = _rapidPlace = _doorSpam = _allHarvest = _pvpThorns = _trailMode =
            _deathAura = _begoneAura = _blockNuker

            // Debugging.
            // = _test

            // All set to false in one grouped assignment.
            = false;

            // Special assignment for tri-state checkboxes.
            _worldTime  = TriState.Off;

            // Special assignment for PlayerSelectScope radiobuttons.
            _shootBlockScope      = PlayerSelectScope.Personal;
            _shootAmmoScope       = PlayerSelectScope.Personal;
            _explosiveLasersScope = PlayerSelectScope.Personal;

            // Special assignment for TargetedPlayer dropdowns.

            // Single Target
            _hugTargetIndex         = 0;

            // Multi Target
            _forceRespawnMultiTarg  = false;
            _forceRespawnMultiTargetNetIds?.Clear();
            _muteMultiTarg          = false;
            _muteMultiTargetNetIds?.Clear();
            _disableContMultiTarg   = false;
            _disableContMultiTargetNetIds?.Clear();
            _rapidItemsMultiTarg    = false;
            _rapidItemsMultiTargetNetIds?.Clear();
            _showerMultiTarg        = false;
            _showerMultiTargetNetIds?.Clear();
            _trailMultiTarg         = false;
            _trailMultiTargetNetIds?.Clear();
            _reliableFloodMultiTarg = false;
            _reliableFloodMultiTargetNetIds?.Clear();
        }

        // Slider & Textbox original states.
        public static float                        _pickupScale             = 4.00f;
        public static int                          _timeDay                 = 1;
        public static float                        _timeScale               = 0.00f;
        public static float                        _speedScale              = 1.00f;
        private static string                      _nameInput               = string.Empty;
        private static int                         _tpRandomOffset          = 5;            // 1..20 (blocks).
        public static int                          _projectileTuningValue   = 1;
        public static float                        _rocketSpeedValue        = 25.00f;
        public static float                        _guidedRocketSpeedValue  = 50.00f;
        public static int                          _aimbotRandomDelayValue  = 200;
        public static int                          _rapidItemsTimerValue    = 100;
        public static int                          _showerTimerValue        = 100;
        public static int                          _choasTimerValue         = 100;
        public static int                          _beaconHeightValue       = 48;
        public static int                          _itemVortexTimerValue    = 100;
        public static int                          _lootboxTimesValue       = 1;
        public static float                        _gravityValue            = -20f;
        public static float                        _cameraXValue            = 0f;
        public static float                        _cameraYValue            = 0f;
        public static float                        _cameraZValue            = 0f;
        public static int                          _hugSpreadValue          = 0;
        public static int                          _reliableFloodBurstValue = 1000;
        private static int                         _dragonHealthMultiplier  = 1;
        private static int                         _blockEspChunkRadValue   = 2;
        private static int                         _blockEspMaxBoxesValue   = 256;
        public static int                          _spamTextTimerValue      = 100;
        private static string                      _spamTextInput           = string.Empty;
        private static string                      _sudoPlayerTextInput     = string.Empty;
        private static string                      _gameTitleTextInput      = string.IsNullOrEmpty(_gameTitleTextInput) ? CastleMinerZGame.Instance?.Window.Title ?? string.Empty : string.Empty;
        public static int                          _deathAuraRangeValue     = 50;
        public static int                          _begoneAuraRangeValue    = 50;
        private static int                         _blockNukerRangeValue    = 0;

        // Player list: Backing data + selection.
        private static readonly List<NetworkGamer> _players     = new List<NetworkGamer>();
        private static int                         _playerIndex = -1;

        // Selected item helpers.
        private static NetworkGamer SelectedGamer         => (_playerIndex >= 0 && _playerIndex < _players.Count) ? _players[_playerIndex] : null;
        private static string       SelectedGamertag      => SelectedGamer?.Gamertag ?? "(none)";
        private static string DisplayName(NetworkGamer g) =>
            g == null ? "(null)" :
            g.Gamertag
            + (g.IsHost ? " [Host]" : string.Empty)
            + (g.IsLocal ? " (You)" : string.Empty);

        // Window layout hint only once (avoid fighting user-resizes).
        private static bool _siMobOnce;
        private static bool _applyConfigOnce;

        #region Picker States

        #region Enemy Picker States

        private static readonly EnemyTypeEnum[] _enemyOptions =
            Enum.GetValues(typeof(EnemyTypeEnum))
                .Cast<EnemyTypeEnum>()
                .Where(e => e != EnemyTypeEnum.COUNT           &&
                            e != EnemyTypeEnum.ANTLER_BEAST    &&
                            e != EnemyTypeEnum.REAPER          &&
                            e != EnemyTypeEnum.TREASURE_ZOMBIE &&
                            e != EnemyTypeEnum.NUM_ZOMBIES     &&
                            e != EnemyTypeEnum.NUM_ARCHERS     &&
                            e != EnemyTypeEnum.NUM_SKELETONS
                )
                .ToArray();

        private static int _enemyIndex       = 0;
        private static int _spawnEnemyAmount = 1; // 1..999.

        private static string EnemyLabel(EnemyTypeEnum e)
        {
            // Pretty-up enum names: ZOMBIE_0_0 -> "Zombie 0 0".
            var s = e.ToString().Replace('_', ' ').ToLowerInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static EnemyTypeEnum GetEnemyEnum(BaseZombie z)
        {
            if (z?.EType == null) return EnemyTypeEnum.COUNT;

            foreach (EnemyTypeEnum e in Enum.GetValues(typeof(EnemyTypeEnum)))
            {
                if (object.ReferenceEquals(EnemyType.GetEnemyType(e), z.EType))
                    return (e);
            }
            return EnemyTypeEnum.COUNT;
        }
        #endregion

        #region Dragon Picker States

        private static readonly DragonTypeEnum[] _dragonOptions =
            Enum.GetValues(typeof(DragonTypeEnum))
                .Cast<DragonTypeEnum>()
                .Where(e => e != DragonTypeEnum.COUNT
                )
                .ToArray();

        private static int _dragonIndex = 0;
        private static int _spawnDragonAmount = 1; // 1..999.

        private static string DragonLabel(DragonTypeEnum id) => id.ToString();
        #endregion

        #region Item Picker States

        // One-time item list (sorted nicely).
        static readonly InventoryItemIDs[] _itemOptions =
            Enum.GetValues(typeof(InventoryItemIDs)).Cast<InventoryItemIDs>()
                .OrderBy(ItemLabel, StringComparer.OrdinalIgnoreCase)
                .Where(e => e != InventoryItemIDs.SpaceKnife              &&
                            e != InventoryItemIDs.Chainsaw2               &&
                            e != InventoryItemIDs.Chainsaw3               &&
                            e != InventoryItemIDs.SpawnCombat             &&
                            e != InventoryItemIDs.SpawnExplorer           &&
                            e != InventoryItemIDs.SpawnBuilder            &&
                            e != InventoryItemIDs.AdvancedGrenadeLauncher &&
                            e != InventoryItemIDs.MegaPickAxe             &&
                            e != InventoryItemIDs.Snowball                &&
                            e != InventoryItemIDs.Iceball                 &&
                            e != InventoryItemIDs.MultiLaser              &&
                            e != InventoryItemIDs.MonsterBlock
                )
                .ToArray();

        private static int _itemIndex      = 0;
        private static int _itemGiveAmount = 1;

        // Player-tab combobox pickers.
        // private static int _itemComboboxIndex = 0;
        private static int _rapidIItemComboIndex = 0;
        private static int _showerItemComboIndex = 0;
        private static int _vortexItemComboIndex = 37; // Diamond.

        private static string ItemLabel(InventoryItemIDs id) => id.ToString().Replace('_', ' ');

        // Humanize a token: Underscores -> spaces, then insert spaces at split boundaries.
        static string ItemLabel(string item)
        {
            if (string.IsNullOrEmpty(item)) return item;
            item = item.Replace('_', ' ');           // Keep if enums use underscores.
            item = ItemWordSplit.Replace(item, " "); // Insert spaces at regex boundaries.
            return item.Trim();
        }

        // Split boundaries:
        //  1) lower->UPPER       : "eP" in "BloodstonePickaxe" => "Bloodstone Pickaxe".
        //  2) ACRONYM->StartCase : "SMG" + "Gun" in "SMGGun"   => "SMG Gun" (UPPER then Upper+lower).
        //  3) letter->digit      : "V2"                        => "V 2".
        //  4) digit->letter      : "2D"                        => "2 D".
        static readonly Regex ItemWordSplit = new Regex(
            @"(?<=\p{Ll})(?=\p{Lu})"        // (1).
          + @"|(?<=\p{Lu})(?=\p{Lu}\p{Ll})" // (2).
          + @"|(?<=\p{L})(?=\p{Nd})"        // (3).
          + @"|(?<=\p{Nd})(?=\p{L})",       // (4).
            RegexOptions.Compiled
        );
        #endregion

        #region Teleport Picker States

        private static float _teleportX = 0f;
        private static float _teleportY = 0f;
        private static float _teleportZ = 0f;

        private static string _teleportXText = "0.0";
        private static string _teleportYText = "0.0";
        private static string _teleportZText = "0.0";

        private static bool _teleportSpawnOnTop = true;

        /// <summary>
        /// Attempts to parse the teleport X, Y, and Z text values, update the cached float values,
        /// and invoke the teleport callback if all three values are valid.
        /// </summary>
        private static bool TrySubmitTeleport()
        {
            if (!float.TryParse(_teleportXText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                return false;

            if (!float.TryParse(_teleportYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return false;

            if (!float.TryParse(_teleportZText, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return false;

            _teleportX = x;
            _teleportY = y;
            _teleportZ = z;

            // Optional: normalize the text after successful parse.
            _teleportXText = x.ToString("0.0", CultureInfo.InvariantCulture);
            _teleportYText = y.ToString("0.0", CultureInfo.InvariantCulture);
            _teleportZText = z.ToString("0.0", CultureInfo.InvariantCulture);

            Callbacks.OnTeleportToLocation?.Invoke(
                new Vector3(_teleportX, _teleportY, _teleportZ),
                _teleportSpawnOnTop
            );

            return true;
        }
        #endregion

        #region Block Picker States

        static readonly BlockTypeEnum[] _blockOptions =
            Enum.GetValues(typeof(BlockTypeEnum)).Cast<BlockTypeEnum>()
                .OrderBy(BlockLabel, StringComparer.OrdinalIgnoreCase)
                .Where(e =>
                    e != BlockTypeEnum.Empty &&
                    e != BlockTypeEnum.Uninitialized)
                .ToArray();

        // private static int _blockIndex      = 0;
        // private static int _blockGiveAmount = 1;

        // Player-tab combobox pickers.
        // private static int _blockComboboxIndex = 0;
        private static int _wearBlockComboIndex   = 70; // BlockTypeEnum.SpawnPointBasic.
        private static int _trailBlockComboIndex = 84; // BlockTypeEnum.Torch.

        private static string BlockLabel(BlockTypeEnum id) => id.ToString().Replace('_', ' ');

        // Humanize a token: Underscores -> spaces, then insert spaces at split boundaries.
        static string BlockLabel(string item)
        {
            if (string.IsNullOrEmpty(item)) return item;
            item = item.Replace('_', ' ');            // Keep if enums use underscores.
            item = BlockWordSplit.Replace(item, " "); // Insert spaces at regex boundaries.
            return item.Trim();
        }

        // Split boundaries:
        //  1) lower->UPPER       : "eP" in "BloodstonePickaxe" => "Bloodstone Pickaxe".
        //  2) ACRONYM->StartCase : "SMG" + "Gun" in "SMGGun"   => "SMG Gun" (UPPER then Upper+lower).
        //  3) letter->digit      : "V2"                        => "V 2".
        //  4) digit->letter      : "2D"                        => "2 D".
        static readonly Regex BlockWordSplit = new Regex(
            @"(?<=\p{Ll})(?=\p{Lu})"        // (1).
          + @"|(?<=\p{Lu})(?=\p{Lu}\p{Ll})" // (2).
          + @"|(?<=\p{L})(?=\p{Nd})"        // (3).
          + @"|(?<=\p{Nd})(?=\p{L})",       // (4).
            RegexOptions.Compiled
        );
        #endregion

        #region Multi Block Picker States

        // Multi-block selection state (shared UI + features).
        private static readonly HashSet<BlockTypeEnum> _blockEspSelectedTypes = new HashSet<BlockTypeEnum> { };

        // Scratch (avoid allocs when firing callback).
        private static readonly List<BlockTypeEnum> _tmpMultiBlocks = new List<BlockTypeEnum>();

        #endregion

        #region Difficuty Picker States

        // One-time difficulty list (sorted nicely).
        static readonly GameDifficultyTypes[] _difficultyOptions =
            Enum.GetValues(typeof(GameDifficultyTypes))
                .Cast<GameDifficultyTypes>()
                .ToArray();

        public static int _difficultyIndex = 0;

        private static string DifficultyLabel(GameDifficultyTypes id) => id.ToString();

        #endregion

        #region GameMode Picker States

        // One-time gamemode list (sorted nicely).
        static readonly GameModeTypes[] _gameModeOptions =
            Enum.GetValues(typeof(GameModeTypes))
                .Cast<GameModeTypes>()
                .ToArray();

        public static int _gameModeIndex = 0;

        private static string GameModeLabel(GameModeTypes id) => id.ToString();

        #endregion

        #region Fireball Picker States

        // Custom fireball enum.
        public enum FireballTypes
        {
            Fireball,
            Iceball
        }

        // One-time fireball list (sorted nicely).
        static readonly FireballTypes[] _fireballOptions =
            Enum.GetValues(typeof(FireballTypes))
                .Cast<FireballTypes>()
                .ToArray();

        public static int _fireballIndex = 0;

        private static string FireballLabel(FireballTypes id) => id.ToString();

        #endregion

        #region Player Target Picker States

        /*
        /// <summary>
        /// Player Target Selection
        /// -----------------------
        /// Shared selection state used by UI + features.
        /// - None: no target selected.
        /// - Player: a specific NetworkGamer by NetworkId.
        /// - AllPlayers: target everyone.
        /// </summary>
        public enum PlayerTargetMode : byte
        {
            None       = 0,
            Player     = 1,
            AllPlayers = 2
        }
        */

        // private static int _playerTargetIndex = 0; // 0 = (None) by default.
        private static int _hugTargetIndex        = 0;
        private static int _spamTextTargetIndex   = 0;
        private static int _sudoPlayerTargetIndex = 0;

        public struct PlayerTargetOption
        {
            public PlayerTargetMode Mode;  // None / Player / AllPlayers
            public NetworkGamer     Gamer; // Only set when Mode == Player
        }

        private static readonly List<PlayerTargetOption> _playerTargetOptions = new List<PlayerTargetOption>(32);

        #endregion

        #region Multi Player Target Picker States

        // Multi-target selection state (shared UI + features).
        private static bool                   _serverAdminAllPlayers          = false; // If true => ignore per-player selections.
        private static readonly HashSet<byte> _serverAdminMultiTargetNetIds   = new HashSet<byte>(32);
        private static bool                   _forceRespawnMultiTarg          = false;
        private static readonly HashSet<byte> _forceRespawnMultiTargetNetIds  = new HashSet<byte>(32);
        private static bool                   _muteMultiTarg                  = false;
        private static readonly HashSet<byte> _muteMultiTargetNetIds          = new HashSet<byte>(32);
        private static bool                   _disableContMultiTarg           = false;
        private static readonly HashSet<byte> _disableContMultiTargetNetIds   = new HashSet<byte>(32);
        private static bool                   _rapidItemsMultiTarg            = false;
        private static readonly HashSet<byte> _rapidItemsMultiTargetNetIds    = new HashSet<byte>(32);
        private static bool                   _showerMultiTarg                = false;
        private static readonly HashSet<byte> _showerMultiTargetNetIds        = new HashSet<byte>(32);
        private static bool                   _trailMultiTarg                 = false;
        private static readonly HashSet<byte> _trailMultiTargetNetIds         = new HashSet<byte>(32);
        private static bool                   _reliableFloodMultiTarg         = false;
        private static readonly HashSet<byte> _reliableFloodMultiTargetNetIds = new HashSet<byte>(32);
        private static bool                   _itemVortexMultiTarg            = false;
        private static readonly HashSet<byte> _itemVortexMultiTargetNetIds    = new HashSet<byte>(32);
        private static bool                   _doorSpamMultiTarg              = false;
        private static readonly HashSet<byte> _doorSpamMultiTargetNetIds      = new HashSet<byte>(32);

        // Scratch (avoid allocs when firing callback).
        private static readonly List<byte> _tmpMultiIds  = new List<byte>(32);
        private static readonly List<byte> _tmpRemoveIds = new List<byte>(16);

        #endregion

        #endregion

        #region Log Tab - State & Constants

        // Autoscroll + pause + one-shot "jump to bottom" nudge after unpause.
        static bool                  _logAutoScroll         = true;
        static bool                  _logPaused             = false;
        static bool                  _scrollBottomOnUnpause = false;

        // Command box state & simple history (pos = -1 means "live typing").
        static string                _cmdInput              = string.Empty;
        static readonly List<string> _cmdHistory            = new List<string>();
        static int                   _cmdHistoryPos         = -1;           // -1 = live typing (not browsing).
        static string                _cmdHistoryDraft       = string.Empty; // Text the user was typing before pressing Up.
        static bool                  _focusCmdNextFrame     = false;

        // Optional hook for local slash-commands (return true if handled to avoid chat send).
        // public static Func<string, bool> OnCommand;

        // "Send Raw" toggle (unchecked = prepend "username: " for normal chat).
        public static bool _cmdRaw = false;

        // ImGui built-in text filter for the log list.
        private static unsafe ImGuiTextFilterPtr _logFilter = new ImGuiTextFilterPtr(ImGuiNative.ImGuiTextFilter_ImGuiTextFilter(null));

        // Consistent colors for each stream (used by list + legend).
        static readonly Vector4 COLOR_INBOUND     = new Vector4(0.85f, 1.00f, 0.85f, 1f); // Greenish.
        static readonly Vector4 COLOR_OUTBOUND    = new Vector4(0.75f, 0.85f, 1.00f, 1f); // Bluish.
        static readonly Vector4 COLOR_CONSOLE     = new Vector4(1.00f, 0.95f, 0.60f, 1f); // Yellowish.
        static readonly Vector4 COLOR_CONSOLE_ERR = new Vector4(0.95f, 0.35f, 0.35f, 1f); // Reddish.

        #endregion

        #region Network-Calls Tab - State & Constants

        // Autoscroll + pause + one-shot "jump to bottom" nudge after unpause.
        static bool _netAutoScroll            = true;
        static bool _netPaused                = false;
        static bool _netScrollBottomOnUnpause = false;

        // ImGui built-in text filter for the net calls list.
        private static unsafe ImGuiTextFilterPtr _netFilter =
            new ImGuiTextFilterPtr(ImGuiNative.ImGuiTextFilter_ImGuiTextFilter(null));

        // Consistent colors for each stream (used by list + legend).
        static readonly Vector4 COLOR_NET_RX  = new Vector4(0.85f, 1.00f, 0.85f, 1f); // RX (greenish).
        static readonly Vector4 COLOR_NET_TX  = new Vector4(0.75f, 0.85f, 1.00f, 1f); // TX (bluish).
        static readonly Vector4 COLOR_NET_RAW = new Vector4(1.00f, 0.95f, 0.60f, 1f); // RAW (yellowish).
        static readonly Vector4 COLOR_NET_ERR = new Vector4(0.95f, 0.35f, 0.35f, 1f); // ERR (reddish).

        #endregion

        #region UI Visibility Helpers

        /// <summary>
        /// Controls whether UI elements that are currently not enabled should be completely hidden.
        /// </summary>
        private static readonly bool _hideDisabledUi = true;

        /// <summary>
        /// Determines whether a control should be hidden based on its enabled state.
        /// </summary>
        private static bool ShouldHideControl(bool enabled)
        {
            return _hideDisabledUi && !enabled;
        }
        #endregion

        #endregion

        #region [*] Callbacks (External Actions)

        // Assign these from the mod bootstrap (e.g., in EnsureInit or WireUi).
        public static class Callbacks
        {
            #region Player

            // Checkboxes.
            public static Action<bool>                                        OnGod;
            public static Action<bool>                                        OnHost;
            public static Action<bool>                                        OnNoKick;
            public static Action<bool>                                        OnNoEnemies;
            public static Action<bool>                                        OnInfiniteItems;
            public static Action<bool>                                        OnInfiniteDurability;
            public static Action<bool>                                        OnFlyMode;
            public static Action<bool>                                        OnFullBright;
            public static Action<bool>                                        OnCreativeMode;
            public static Action<bool>                                        OnInfiniteJump;
            public static Action<bool>                                        OnInfiniteAmmo;
            public static Action<bool>                                        OnInfiniteClips;
            public static Action<bool>                                        OnRapidFire;
            public static Action<bool>                                        OnSuperGunStats;
            public static Action<bool>                                        OnNoTarget;
            public static Action<bool>                                        OnTracers;
            public static Action<bool>                                        OnHitboxes;
            public static Action<bool>                                        OnNametags;
            public static Action<Color>                                       OnESPColor;
            public static Action<bool>                                        OnPlayerAimbot;
            public static Action<bool>                                        OnDragonAimbot;
            public static Action<bool>                                        OnMobAimbot;
            public static Action<bool>                                        OnAimbotBulletDrop;
            public static Action<bool>                                        OnAimbotRequireLos;
            public static Action<bool>                                        OnAimbotFaceEnemy;
            public static Action<int>                                         OnAimbotRandomDelayValue;
            public static Action<bool>                                        OnNoGunCooldown;
            public static Action<bool>                                        OnNoGunRecoil;
            public static Action<bool>                                        OnVanish;
            public static Action<bool>                                        OnVanishIsDead;
            public static Action<VanishSelectScope>                           OnVanishScope;
            public static Action<bool>                                        OnPlayerPosition;
            public static Action<bool>                                        OnMovementSpeed;
            public static Action<float>                                       OnSpeedScale;
            public static Action<bool>                                        OnPickupRange;
            public static Action<float>                                       OnPickupRangeScale;
            public static Action<TriState>                                    OnWorldTime;
            public static Action<int>                                         OnWorldTimeDay;
            public static Action<float>                                       OnWorldTimeScale;
            public static Action<bool>                                        OnSoftCrash;
            public static Action<bool>                                        OnXray;
            public static Action<bool>                                        OnInstantMine;
            public static Action<bool>                                        OnRapidTools;
            public static Action<bool>                                        OnNoMobBlocking;
            public static Action<bool>                                        OnMultiColorAmmo;
            public static Action<bool>                                        OnMultiColorRNG;
            public static Action<bool>                                        OnShootBlocks;
            public static Action<PlayerSelectScope>                           OnShootBlockScope;
            public static Action<bool>                                        OnShootHostAmmo;
            public static Action<bool>                                        OnShootGrenades;
            public static Action<bool>                                        OnShootRockets;
            public static Action<PlayerSelectScope>                           OnShootAmmoScope;
            public static Action<bool>                                        OnFreezeLasers;
            public static Action<bool>                                        OnExtendLaserTime;
            public static Action<bool>                                        OnInfiLaserPath;
            public static Action<bool>                                        OnInfiLaserBounce;
            public static Action<bool>                                        OnExplosiveLasers;
            public static Action<PlayerSelectScope>                           OnExplosiveLasersScope;
            public static Action<bool>                                        OnNoTPOnServerRestart;
            public static Action<bool>                                        OnCorruptOnKick;
            public static Action<bool>                                        OnProjectileTuning;
            public static Action<int>                                         OnProjectileTuningValue;
            public static Action<bool>                                        OnFreeFlyCamera;
            public static Action<bool>                                        OnRideDragon;
            public static Action<bool>                                        OnShootBowAmmo;
            public static Action<bool>                                        OnNoClip;
            public static Action<bool>                                        OnExplodingOres;
            public static Action<bool>                                        OnShootFireballAmmo;
            public static Action<bool>                                        OnRocketSpeed;
            public static Action<float>                                       OnRocketSpeedValue;
            public static Action<float>                                       OnGuidedRocketSpeedValue;
            public static Action<bool>                                        OnForceRespawn;
            public static Action<bool>                                        OnIgnoreChatNewlines;
            public static Action<bool>                                        OnDisableInvRetrieval;
            public static Action<bool>                                        OnGravity;
            public static Action<float>                                       OnGravityValue;
            public static Action<bool>                                        OnCamera;
            public static Action<float>                                       OnCameraXValue;
            public static Action<float>                                       OnCameraYValue;
            public static Action<float>                                       OnCameraZValue;
            public static Action<bool>                                        OnMute;
            public static Action<bool>                                        OnMuteWarnOffender;
            public static Action<bool>                                        OnMuteShowMessage;
            public static Action<bool>                                        OnRapidItems;
            public static Action<int>                                         OnRapidItemsValue;
            public static Action<bool>                                        OnShower;
            public static Action<int>                                         OnShowerValue;
            public static Action<bool>                                        OnTrail;
            public static Action<bool>                                        OnTrailPrivate;
            public static Action<bool>                                        OnDragonCounter;
            public static Action<bool>                                        OnHat;
            public static Action<bool>                                        OnBoots;
            public static Action<bool>                                        OnDisableControls;
            public static Action<bool>                                        OnItemVortex;
            public static Action<bool>                                        OnBeaconMode;
            public static Action<int>                                         OnBeaconHeightValue;
            public static Action<int>                                         OnItemVortexValue;
            public static Action<bool>                                        OnChaosMode;
            public static Action<bool>                                        OnClockChaos;
            public static Action<bool>                                        OnDragonChaos;
            public static Action<int>                                         OnChaosValue;
            public static Action<bool>                                        OnHug;
            public static Action<int>                                         OnHugSpreadValue;
            public static Action<bool>                                        OnNoLavaVisuals;
            public static Action<bool>                                        OnReliableFlood;
            public static Action<int>                                         OnReliableFloodValue;
            public static Action<bool>                                        OnBlockEsp;
            public static Action<int>                                         OnBlockEspChunkRadValue;
            public static Action<int>                                         OnBlockEspMaxBoxesValue;
            public static Action<bool>                                        OnBlockEspHideTracers;
            public static Action<bool>                                        OnSpamTextShow;
            public static Action<bool>                                        OnSpamTextStart;
            public static Action<bool>                                        OnSpamTextSudo;
            public static Action<int>                                         OnSpamTextValue;
            public static Action<string>                                      OnSpamTextMessage;
            public static Action<bool>                                        OnChaoticAim;
            public static Action<bool>                                        OnDisableItemPickups;
            public static Action<bool>                                        OnGhostMode;
            public static Action<bool>                                        OnGhostModeHideName;
            public static Action<bool>                                        OnAlwaysDaySky;
            public static Action<string>                                      OnGameTitleTextMessage;
            public static Action<bool>                                        OnRapidPlace;
            public static Action<bool>                                        OnSudoPlayer;
            public static Action<string>                                      OnSudoCustomName;
            public static Action<bool>                                        OnDoorSpam;
            public static Action<bool>                                        OnAllGunsHarvest;
            public static Action<bool>                                        OnPvpThorns;
            public static Action<bool>                                        OnTrailMode;
            public static Action<bool>                                        OnDeathAura;
            public static Action<int>                                         OnDeathAuraRangeValue;
            public static Action<bool>                                        OnBegoneAura;
            public static Action<int>                                         OnBegoneAuraRangeValue;
            public static Action<bool>                                        OnBlockNuker;
            public static Action<int>                                         OnBlockNukerRangeValue;

            // Dropdowns.
            public static Action<EnemyTypeEnum>                               OnEnemy          = null;
            public static Action<DragonTypeEnum>                              OnDragon         = null;
            public static Action<InventoryItemIDs>                            OnItem           = null;
            public static Action<GameDifficultyTypes>                         OnDifficulty;
            public static Action<GameModeTypes>                               OnGameMode;
            public static Action<FireballTypes>                               OnFireball;
            public static Action<PlayerTargetMode, byte[]>                    OnRespawnPlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnMutePlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnTrailPlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnShowerPlayer;
            public static Action<InventoryItemIDs>                            OnRapidItemsType = null;
            public static Action<InventoryItemIDs>                            OnShowerType     = null;
            public static Action<PlayerTargetMode, byte[]>                    OnRapidItemsPlayer;
            public static Action<BlockTypeEnum>                               OnWearType        = null;
            public static Action<PlayerTargetMode, byte[]>                    OnDisableControlsPlayer;
            public static Action<InventoryItemIDs>                            OnItemVortexType  = null;
            public static Action<PlayerTargetMode, byte>                      OnHugPlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnReliableFloodPlayer;
            public static Action<BlockTypeEnum[]>                             OnBlockEspTypes = null;
            public static Action<BlockTypeEnum>                               OnTrailType     = null;
            public static Action<PlayerTargetMode, byte>                      OnSpamTextSudoPlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnItemVortexPlayer;
            public static Action<PlayerTargetMode, byte>                      OnSudoPlayerPlayer;
            public static Action<PlayerTargetMode, byte[]>                    OnDoorSpamPlayer;

            // Buttons.
            public static Action                                              OnApplyTitle     = null;

            // Debugging.
            public static Action<bool>                                        OnTest;
            public static Action                                              OnButton         = null;

            #endregion

            #region Target

            // Dropdown actions.
            public static Action<NetworkGamer, EnemyTypeEnum, int, int, bool> OnSpawnMobs;
            public static Action<NetworkGamer, EnemyTypeEnum, int>            OnSpawnMobsAtCrosshair;
            public static Action<NetworkGamer, DragonTypeEnum, int, int>      OnSpawnDragons;
            public static Action<NetworkGamer, InventoryItemIDs, int>         OnGiveItems;
            public static Action<Vector3, bool>                               OnTeleportToLocation;

            // Checkboxes.
            public static Action<bool>                                        OnSpawnMobSamePos = null;

            // Buttons.
            public static Action<NetworkGamer>                                OnFreezeSelectedPlayer;
            public static Action<NetworkGamer>                                OnCrashSelectedPlayer;
            public static Action<NetworkGamer>                                OnKillSelectedPlayer;
            public static Action                                              OnKillAllPlayers;
            public static Action                                              OnKillAllMobs;
            public static Action<NetworkGamer>                                OnTpToPlayer;
            public static Action<NetworkGamer>                                OnViewSteamAccount;
            public static Action<NetworkGamer>                                OnRestartSelectedPlayer;
            public static Action                                              OnRestartAllPlayers;
            public static Action<NetworkGamer>                                OnCorruptSelectedPlayer;
            public static Action                                              OnCorruptAllPlayers;
            public static Action<NetworkGamer>                                OnKickSelectedPlayer;
            public static Action                                              OnKickAllPlayers;
            public static Action<NetworkGamer>                                OnTpAllMobsToCrosshair;
            public static Action<NetworkGamer>                                OnDetonate100GrenadesSelectedPlayer;
            public static Action<NetworkGamer>                                OnGrenadeRainSelectedPlayer;
            public static Action                                              OnClearGroundItems;
            public static Action                                              OnDropAllItems;
            public static Action                                              OnRevivePlayer;
            public static Action                                              OnUnlockAllAchievements;
            public static Action                                              OnActivateSpawners;
            public static Action<NetworkGamer>                                OnLootboxSelectedPlayer;
            public static Action<NetworkGamer>                                OnGrenadeCollisionLagSelectedPlayer;
            public static Action<NetworkGamer>                                OnInvalidDMigrationSelectedPlayer;
            public static Action                                              OnRemoveDragonMsg;
            public static Action                                              OnFreezeAllPlayers;
            public static Action                                              OnCrashAllPlayers;
            public static Action                                              OnUnlockAllModes;
            public static Action                                              OnMaxStackAllItems;
            public static Action                                              OnMaxHealthAllItems;
            public static Action                                              OnCurrentDragonHP;
            public static Action                                              OnClearProjectiles;
            public static Action                                              OnCaveLighter;
            public static Action<int>                                         OnCaveLighterMaxDistanceValue;
            public static Action<NetworkGamer>                                OnBoxInSelectedPlayer;
            public static Action<NetworkGamer>                                OnForcePlayerUpdateSelectedPlayer;
            public static Action<NetworkGamer>                                OnHoleSelectedPlayer;

            // Debugging.
            public static Action                                              OnStatsWindow;

            #endregion

            #region Set Name

            public static Action<string>                                      OnSetName;

            #endregion

            // Optional heartbeat if you need periodic polling while the UI is open.
            // public static Action                                   OnTick;
        }
        #endregion

        // =============================== //

        #region Render: Root Window

        private static string BuildTitle()
        {
            // US-layout mapper for symbols/letters/digits.
            ImGuiXnaRenderer.TryKeyToChar(ImGuiXnaRenderer._toggleKey, true, out char ch);

            var ver   = typeof(CastleWallsMk2).Assembly.GetName().Version?.ToString(3);
            var state = ImGuiXnaRenderer.Visible ? $"Hide ({ch})" : $"Show ({ch})";
            var fps   = $"FPS ({ImGui.GetIO().Framerate:0})";
            var size  = $"[{_lastWindowSize.X:0}x{_lastWindowSize.Y:0}]";

            return $"CastleWalls Mk2 v{ver} - Remastered By RussDev7 | {state} | {fps} | {size}";
        }

        // Note:
        //   The following can be used to disable/enable controls when not in an active game.
        //   Enable:  if (!CastleWallsMk2.IsInGame() && !AllowOutOfGameSettingEdits()) ImGui.BeginDisabled();
        //   Disable: if (!CastleWallsMk2.IsInGame() && !AllowOutOfGameSettingEdits()) ImGui.EndDisabled();
        public static void Draw()
        {
            // Debug.
            if (_showDemoWindow)
                ImGui.ShowDemoWindow(ref _showDemoWindow);

            // ImGui settings I/O bootstrap (runs once per context).
            ImGuiSettings_InitOnce();

            // Always pump the capture queues even if the overlay is hidden/minimiMob.
            ChatLog.DrainConsoleQueues();

            // What should be drawn this frame?
            bool showMainWindow = ImGuiXnaRenderer.Visible;
            bool showTestPanel  = TestStatsVisible;

            // If nothing wants to be drawn, or game is inactive/minimized, bail out.
            if ((!showMainWindow && !showTestPanel) ||
                !CastleMinerZGame.Instance.IsActive ||
                GameIsMinimiMob())
            {
                ImGuiXnaRenderer.CancelFrame();
                ImGuiXnaRenderer._toggleEdge = ImGuiXnaRenderer.Edge.Up;
                return;
            }

            // Keep the main window's "open" flag in sync with the overlay visibility,
            // but do NOT tie the test stats panel to this.
            IsOpen = showMainWindow;

            #region Reworked: Overlay Visibility

            /*
            // Keep the window's "open" flag in sync with the overlay visibility.
            IsOpen = ImGuiXnaRenderer.Visible;

            // Early-out while hidden / Inactive / MinimiMob / Not in-game.
            if (!ImGuiXnaRenderer.Visible           ||
                !CastleMinerZGame.Instance.IsActive ||
                GameIsMinimiMob()
                )
            {
                ImGuiXnaRenderer.CancelFrame();                          // Cleanly end any in-progress ImGui frame.
                ImGuiXnaRenderer._toggleEdge = ImGuiXnaRenderer.Edge.Up; // Re-arm hotkey edge (optional).
                return;
            }

            // Early-out only if overlay is hidden.
            if (!ImGuiXnaRenderer.Visible)
            {
                ImGuiXnaRenderer.CancelFrame(); // It's ok if a frame was started elsewhere this tick.
                ImGuiXnaRenderer._toggleEdge = ImGuiXnaRenderer.Edge.Up;
                return;
            }
            */
            #endregion

            if (showMainWindow)
            {
                // Size hint only on first open; allows user to resize afterwards.
                if (!_siMobOnce)
                {
                    // Match the WinForms client size (800x540) and prevent the "1px line" issue.
                    ImGui.SetNextWindowSize(new Vector2(900, 640), ImGuiCond.Always);
                    _siMobOnce = true;
                }

                // Always call Begin with ref IsOpen (so the X button can flip it).
                ImGui.Begin(BuildTitle() + "###CastleWallsMk2_MAIN", ref IsOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

                #region Set UI Configs

                if (!_applyConfigOnce)
                {
                    // Get the current config.
                    var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();

                    // Apply theme from the config.
                    switch (cfg.Theme)
                    {
                        case Style.Classic: ImGui.StyleColorsClassic(); break;
                        case Style.Light:   ImGui.StyleColorsLight();   break;
                        default:            ImGui.StyleColorsDark();    break;
                    }

                    // Apply scale (global) from the config (default 1.0f).
                    var io = ImGui.GetIO();
                    io.FontGlobalScale = (cfg.Scale > 0f) ? cfg.Scale : 1.0f;

                    // Prevent the config from being accessed twice.
                    _applyConfigOnce = true;
                }
                #endregion

                // If the user clicked the X this frame, IsOpen is now false.
                // Mirror that into the renderer state immediately and end this window cleanly.
                if (!IsOpen)
                {
                    if (ImGuiXnaRenderer.Visible)
                    {
                        ImGuiXnaRenderer.Visible = false;
                        ImGuiXnaRenderer._toggleEdge = ImGuiXnaRenderer.Edge.Up; // Reset the hotkey edge detector so next press works.
                    }

                    ImGui.End(); // Close the window we just began.
                    return;      // Do NOT call EndFrame() here.
                }

                // Keep window within the main viewport (protect against bad imgui.ini coords).
                KeepWindowOnScreen();

                // Optional heartbeat (e.g., refresh data every frame or throttled inside handler).
                // Callbacks.OnTick?.Invoke();

                if (ImGui.BeginTabBar("MainTabs", ImGuiTabBarFlags.None))
                {
                    // Main.
                    if (ImGui.BeginTabItem("Main"))
                    {
                        DrawMainTabContent();
                        ImGui.EndTabItem();
                    }

                    // Editors (hosts nested tab bar).
                    if (ImGui.BeginTabItem("Editors"))
                    {
                        if (ImGui.BeginTabBar("EditorsTabs", ImGuiTabBarFlags.None))
                        {
                            _tabPlayerOpen  = false;
                            _tabEnemiesOpen = false;
                            _tabDragonOpen  = false;

                            if (ImGui.BeginTabItem("Player"))
                            {
                                _tabPlayerOpen = true;
                                DrawPlayerEditorTab();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("World"))
                            {
                                DrawWorldEditorTab();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Enemies"))
                            {
                                _tabEnemiesOpen = true;
                                DrawEnemyEditorTab();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Dragon"))
                            {
                                _tabDragonOpen = true;
                                DrawDragonEditorTab();
                                ImGui.EndTabItem();
                            }

                            ImGui.EndTabBar();
                        }

                        ImGui.EndTabItem();
                    }

                    // Code tab.
                    if (ImGui.BeginTabItem("Code-Injector"))
                    {
                        DrawCodeTab();
                        ImGui.EndTabItem();
                    }

                    // Network sniffer tab.
                    if (ImGui.BeginTabItem("Network-Sniffer"))
                    {
                        DrawNetworkSnifferTab();
                        ImGui.EndTabItem();
                    }

                    // Network calls tab.
                    if (ImGui.BeginTabItem("Network-Calls"))
                    {
                        DrawNetworkCallsTab();
                        ImGui.EndTabItem();
                    }

                    // Server commands tab.
                    if (ImGui.BeginTabItem("Server-Commands"))
                    {
                        DrawServerCommandsTab();
                        ImGui.EndTabItem();
                    }

                    // Server history tab.
                    if (ImGui.BeginTabItem("Server-History"))
                    {
                        DrawServerHistoryTab();
                        ImGui.EndTabItem();
                    }

                    // Player enforcement tab.
                    if (ImGui.BeginTabItem("Player-Enforcement"))
                    {
                        DrawPlayerEnforcementTab();
                        ImGui.EndTabItem();
                    }

                    // Log.
                    if (ImGui.BeginTabItem("Log"))
                    {
                        DrawLogTabContent();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                // Capture current window size for the title.
                _lastWindowSize = ImGui.GetWindowSize();

                ImGui.End();
            }

            // After the main window (or even if it's hidden), draw the test stats panel.
            DrawTestStatsPanel();
        }
        #endregion

        #region Tab: [*] Main

        #region Main Layout

        // Draws the "Main" tab content:
        // - Top half: a two-column table ("Player" | "Target").
        // - Middle: a draggable splitter that changes how many rows the Player group can show.
        // - Bottom: the Player List.
        private static void DrawMainTabContent()
        {
            // Layout - top row (player | target). //

            // Two resizable columns:
            //   left  = Player controls (60%).
            //   right = Target controls (40%).
            if (ImGui.BeginTable("topRow", 2,
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0.62f);
                ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 0.38f);

                ImGui.TableNextColumn();
                DrawPlayerGroup(); // Left column content.

                ImGui.TableNextColumn();
                DrawTargetGroup(); // Right column content.

                ImGui.EndTable();
            }

            ImGui.Spacing();
            // ImGui.Separator(); // (optional) visual break if you want a hard rule here.

            // Splitter - adjusts visible rows in player group. //

            // Invisible handle users can drag up/down to resize the area above.
            ImGui.PushID("TopToListSplitter");

            const float splitterH = 4f; // Thin hit area; purely visual below with a 2px line.
            ImGui.InvisibleButton("##row_splitter", new Vector2(-1, splitterH));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                // Positive Y = drag downward -> more rows; negative Y -> fewer rows.
                float dy   = ImGui.GetIO().MouseDelta.Y;
                float rowH = ImGui.GetFrameHeightWithSpacing();
                _playerRowsF = Clamp(_playerRowsF + dy / rowH, MIN_PLAYER_ROWS, MAX_PLAYER_ROWS);
            }
            ImGui.PopID();

            // Draw a thin centered line to indicate the splitter's position.
            var p  = ImGui.GetItemRectMin();
            var sz = ImGui.GetItemRectSize();
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(p.X, p.Y + sz.Y * 0.5f - 1f),
                new Vector2(p.X + sz.X, p.Y + sz.Y * 0.5f + 1f),
                ImGui.GetColorU32(ImGuiCol.Separator)
            );

            // Player list - bottom area //

            // Scrollable list of gamers; keeps one selected if the list is non-empty.
            DrawPlayerList();
        }
        #endregion

        #region [*] Sections - Player Groupbox

        private static void DrawPlayerGroup()
        {
            ImGui.BeginGroup();
            ImGui.Text("Player");
            ImGui.Separator();

            #region (Padding)

            // Geometry.
            float frameH   = ImGui.GetFrameHeight();
            float spacingY = ImGui.GetStyle().ItemSpacing.Y;
            float rowPitch = frameH + spacingY; // Since we'll zero vertical CellPadding.

            int maxRows = Clamp((int)Math.Round(_playerRowsF), MIN_PLAYER_ROWS, MAX_PLAYER_ROWS);

            // Tiny uniform padding around all sides of the child.
            float edgePad  = 5f;

            // Make room for exactly maxRows rows + our uniform padding.
            float childH   = (maxRows * rowPitch - spacingY) + (edgePad * 2f);

            // Apply the small padding to this child only.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edgePad, edgePad));

            #endregion

            bool childOpen = ImGui.BeginChild(
                "player_toggles_scroll",
                new Vector2(0, childH),
                ImGuiChildFlags.Borders
                /*, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse */
            );

            if (childOpen)
            {
                #region (Padding)

                // Keep vertical cell padding at 0 so row math stays exact.
                var style = ImGui.GetStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.CellPadding.X, 0f));

                // Table does the scrolling + clipping.
                var tflags = ImGuiTableFlags.SizingStretchProp
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.RowBg;

                #endregion

                // Here we use the same 'childH' minus the child's inner padding.
                ImGui.SetNextItemWidth(0); // Harmless; just keeping API symmetry.
                if (ImGui.BeginTable("playerGrid", 3, tflags, new Vector2(0, childH - ImGui.GetStyle().WindowPadding.Y * 2f)))
                {
                    // Give each column equal weight so they expand evenly.
                    ImGui.TableSetupColumn("c0", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("c1", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("c2", ImGuiTableColumnFlags.WidthStretch, 1f);

                    bool inGame              = CastleWallsMk2.IsInGame();
                    bool allowOutOfGameEdits = AllowOutOfGameSettingEdits();
                    bool outOfGameLocked     = !inGame && !allowOutOfGameEdits;

                    if (outOfGameLocked) ImGui.BeginDisabled();

                    #region [*] Checkbox Content

                    #region Column 1

                    ImGui.TableNextColumn();
                    ImGui.PushItemWidth(-1);

                    // ===================== [Player] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Player]", Color.Gold);

                    CB_Checkbox    ("God (+Stamina)",    ref _god,                    Callbacks.OnGod);
                    CB_Checkbox    ("No Kick",           ref _noKick,                 Callbacks.OnNoKick);
                    CB_Checkbox    ("No TP On Restart",  ref _noTPOnServerRestart,    Callbacks.OnNoTPOnServerRestart);
                    CB_Checkbox    ("No Inv Retrieval",  ref _disableInvRetrieval,    Callbacks.OnDisableInvRetrieval);
                    CB_Checkbox    ("No Item Pickups",   ref _disableItemPickups,     Callbacks.OnDisableItemPickups);
                    CB_Checkbox    ("No Chat Newlines",  ref _ignoreChatNewlines,     Callbacks.OnIgnoreChatNewlines);
                    CB_Checkbox    ("Host",              ref _host,                   Callbacks.OnHost);
                    CB_Checkbox    ("Creative Mode",     ref _creativeMode,           Callbacks.OnCreativeMode);
                    CB_Checkbox    ("Fly Mode",          ref _flyMode,                Callbacks.OnFlyMode);
                    CB_Checkbox    ("Player Position",   ref _playerPosition,         Callbacks.OnPlayerPosition);

                    CB_Checkbox    ("Movement Speed:",   ref _movementSpeed,          Callbacks.OnMovementSpeed);
                    CB_Slider      ("##playerSpeedMul",  ref _speedScale,             Callbacks.OnSpeedScale, min: 0.25f, max: 100.0f, enabled: _movementSpeed, format: "%.2fx");

                    CB_Checkbox    ("Infi Jump",         ref _infJump,                Callbacks.OnInfiniteJump);
                    CB_Checkbox    ("Vanish",            ref _vanish,                 Callbacks.OnVanish);
                    CB_Checkbox    (" - Player Is Dead", ref _vanishIsDead,           Callbacks.OnVanishIsDead, enabled: _vanish);
                    if (_vanish)
                    {
                        CB_RadioButton("V_IP", "InPlace",ref _vanishScope,            Callbacks.OnVanishScope, VanishSelectScope.InPlace);
                        ImGui.SameLine();
                        CB_RadioButton("V_S", "Spawn",   ref _vanishScope,            Callbacks.OnVanishScope, VanishSelectScope.Spawn);
                        CB_RadioButton("V_D", "Distant", ref _vanishScope,            Callbacks.OnVanishScope, VanishSelectScope.Distant);
                        ImGui.SameLine();
                        CB_RadioButton("V_Z", "Zero",    ref _vanishScope,            Callbacks.OnVanishScope, VanishSelectScope.Zero);
                    }
                    if (!_vanish) { _vanishScope = VanishSelectScope.InPlace; }

                    CB_Checkbox    ("Pickup Range:",     ref _pickupRange,            Callbacks.OnPickupRange);
                    CB_Slider      ("##pickupRange",     ref _pickupScale,            Callbacks.OnPickupRangeScale, min: 0f, max: 1000.0f, enabled: _pickupRange);

                    CB_Checkbox    ("Corrupt On Kick",   ref _corruptOnKick,          Callbacks.OnCorruptOnKick);
                    CB_Checkbox    ("Free Camera",       ref _freeFlyCamera,          Callbacks.OnFreeFlyCamera);
                    CB_Checkbox    ("No Clip",           ref _noClip,                 Callbacks.OnNoClip);

                    CB_Checkbox    ("Hat",               ref _hat,                    Callbacks.OnHat);
                    CB_Checkbox    ("or Boots!",         ref _boots,                  Callbacks.OnBoots, enabled: _hat);
                    CB_BlockCombo  ("##hatBlockCombo",   ref _wearBlockComboIndex,    Callbacks.OnWearType, enabled: _hat || _boots);

                    // ===================== [Admin & Punishments] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Admin & Punishments]", Color.Gold);

                    CB_Checkbox    ("Mute:",             ref _mute,                   Callbacks.OnMute);
                    CB_Checkbox    (" - Warn Offender:", ref _muteWarnOffender,       Callbacks.OnMuteWarnOffender, enabled: _mute);
                    CB_Checkbox    (" - Show Message:",  ref _muteShowMessage,        Callbacks.OnMuteShowMessage,  enabled: _mute);
                    CB_MultiPCombo ("##mutePlrCombo",    ref _muteMultiTarg,          Callbacks.OnMutePlayer,            _muteMultiTargetNetIds,          _mute);
                    if (!_mute) { _muteMultiTarg = false; _muteMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Disable Controls:", ref _disableControls,        Callbacks.OnDisableControls);
                    CB_MultiPCombo ("##disContPlrCombo", ref _disableContMultiTarg,   Callbacks.OnDisableControlsPlayer, _disableContMultiTargetNetIds,   _disableControls);
                    if (!_disableControls) { _disableContMultiTarg = false; _disableContMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Force Respawn:",    ref _forceRespawn,           Callbacks.OnForceRespawn);
                    CB_MultiPCombo ("##forceRePlrCombo", ref _forceRespawnMultiTarg,  Callbacks.OnRespawnPlayer,         _forceRespawnMultiTargetNetIds,  _forceRespawn);
                    if (!_forceRespawn) { _forceRespawnMultiTarg = false; _forceRespawnMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Rapid Items:",      ref _rapidItems,             Callbacks.OnRapidItems);
                    CB_MultiPCombo ("##rapidIPlrCombo",  ref _rapidItemsMultiTarg,    Callbacks.OnRapidItemsPlayer,      _rapidItemsMultiTargetNetIds,    _rapidItems);
                    CB_ItemCombo   ("##rapidIItemCombo", ref _rapidIItemComboIndex,   Callbacks.OnRapidItemsType, enabled: _rapidItems);
                    CB_Slider      ("##rapidItemsValue", ref _rapidItemsTimerValue,   Callbacks.OnRapidItemsValue, min: 1, max: 1000, enabled: _rapidItems, format: "Speed: %d ms");
                    if (!_rapidItems) { _rapidItemsMultiTarg = false; _rapidItemsMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Shower Items:",     ref _shower,                 Callbacks.OnShower);
                    CB_MultiPCombo ("##showerPlrCombo",  ref _showerMultiTarg,        Callbacks.OnShowerPlayer,          _showerMultiTargetNetIds,        _shower);
                    CB_ItemCombo   ("##showerItemCombo", ref _showerItemComboIndex,   Callbacks.OnShowerType, enabled: _shower);
                    CB_Slider      ("##showerValue",     ref _showerTimerValue,       Callbacks.OnShowerValue, min: 1, max: 1000, enabled: _shower, format: "Speed: %d ms");
                    if (!_shower) { _showerMultiTarg = false; _showerMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Block Trail:",      ref _trail,                  Callbacks.OnTrail);
                    CB_Checkbox    (" - Make Private",   ref _trailPrivate,           Callbacks.OnTrailPrivate, enabled: _trail);
                    CB_MultiPCombo ("##trailPlrCombo",   ref _trailMultiTarg,         Callbacks.OnTrailPlayer,           _trailMultiTargetNetIds,         _trail);
                    CB_BlockCombo  ("##trailBlockCombo", ref _trailBlockComboIndex,   Callbacks.OnTrailType,    enabled: _trail);
                    if (!_trail)  { _trailMultiTarg = false; _trailMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Hug:",              ref _hug,                    Callbacks.OnHug);
                    CB_SinglePCombo("##hugPlrCombo",     ref _hugTargetIndex,         Callbacks.OnHugPlayer, ref _hugTargetMode, ref _hugTargetNetid, _hug);
                    CB_Slider      ("##hugSpreadValue",  ref _hugSpreadValue,         Callbacks.OnHugSpreadValue, min: 0, max: 100, enabled: _hug, format: "Random Spread: %d");
                    if (!_hug) { _hugTargetIndex = 0; }

                    CB_Checkbox    ("App-Layer DoS:",    ref _reliableFlood,          Callbacks.OnReliableFlood);
                    CB_MultiPCombo ("##floodPlrCombo",   ref _reliableFloodMultiTarg, Callbacks.OnReliableFloodPlayer,   _reliableFloodMultiTargetNetIds, _reliableFlood);
                    CB_Slider      ("##floodBurstValue", ref _reliableFloodBurstValue,Callbacks.OnReliableFloodValue, min: 1, max: 10000, enabled: _reliableFlood, format: "Burst: %d");
                    if (!_reliableFlood) { _reliableFloodMultiTarg = false; _reliableFloodMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("Soft Crash",        ref _softCrash,              Callbacks.OnSoftCrash);

                    CB_Checkbox    ("Spam Text:",        ref _spamTextShow,           Callbacks.OnSpamTextShow);
                    CB_Checkbox    ("[ Start Spam ]",    ref _spamTextStart,          Callbacks.OnSpamTextStart, enabled: _spamTextShow);
                    CB_Checkbox    (" - Sudo Player:",   ref _spamTextSudo,           Callbacks.OnSpamTextSudo, enabled: _spamTextShow);
                    CB_SinglePCombo("##stSudoPlrCombo",  ref _spamTextTargetIndex,    Callbacks.OnSpamTextSudoPlayer, ref _spamTextTargetMode, ref _spamTextTargetNetid, _spamTextSudo);
                    CB_Slider      ("##spamTextValue",   ref _spamTextTimerValue,     Callbacks.OnSpamTextValue, min: 1, max: 1000, format: "Speed: %d ms", enabled: _spamTextShow);
                    CB_Checkbox    ("Expand Text Box",   ref _spamTextExpandBox,      null, enabled: _spamTextShow);
                    CB_TextArea    ("##spamTextInput",   ref _spamTextInput,          Callbacks.OnSpamTextMessage, rowsTall: _spamTextExpandBox ? 6 : 1, enabled: _spamTextShow, hint: "Type text here...");
                    if (!_spamTextShow || !_spamTextSudo) { _spamTextTargetIndex = 0; }
                    if (!_spamTextShow) { _spamTextStart = false; }

                    CB_Checkbox    ("Sudo Player:",      ref _sudoPlayer,             Callbacks.OnSudoPlayer);
                    CB_SinglePCombo("##sudoPlrCombo",    ref _sudoPlayerTargetIndex,  Callbacks.OnSudoPlayerPlayer, ref _sudoPlayerTargetMode, ref _sudoPlayerTargetNetid, _sudoPlayer);
                    CB_TextArea    ("##sudoPlayerInput", ref _sudoPlayerTextInput,    Callbacks.OnSudoCustomName, rowsTall: 1, enabled: _sudoPlayer, hint: "Type name override...");
                    if (!_sudoPlayer) { _sudoPlayerTargetNetid = 0; }

                    CB_Checkbox    ("Door Spam:",        ref _doorSpam,               Callbacks.OnDoorSpam);
                    CB_MultiPCombo ("##dSpamPlrCombo",   ref _doorSpamMultiTarg,      Callbacks.OnDoorSpamPlayer,        _doorSpamMultiTargetNetIds, _doorSpam);
                    if (!_doorSpam) { _doorSpamMultiTarg = false; _doorSpamMultiTargetNetIds.Clear(); }

                    // ===================== [Homes] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Homes]", Color.Gold);
                    HomesUI.DrawHomesWidget();

                    #endregion

                    #region Column 2

                    ImGui.TableNextColumn();
                    ImGui.PushItemWidth(-1);

                    // ===================== [World & Visuals] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[World & Visuals]", Color.Gold);

                    CB_Checkbox    ("No Enemies",        ref _noEnemies,              Callbacks.OnNoEnemies);
                    CB_Checkbox    ("No Target",         ref _noTarget,               Callbacks.OnNoTarget);
                    CB_Checkbox    ("Xray",              ref _xray,                   Callbacks.OnXray);
                    CB_Checkbox    ("Full Bright",       ref _fullBright,             Callbacks.OnFullBright);
                    CB_Checkbox    ("Tracers",           ref _tracers,                Callbacks.OnTracers);
                    CB_Checkbox    ("Hitboxes",          ref _hitboxes,               Callbacks.OnHitboxes);
                    CB_Checkbox    ("Nametags",          ref _nametags,               Callbacks.OnNametags);
                    DrawColorEditor("ESP Color",         _tracers || _hitboxes || _nametags,
                                   Callbacks.OnESPColor, CastleWallsMk2._espColor);
                    CB_Checkbox    ("Multi-Color Ammo",  ref _multiColorAmmo,         Callbacks.OnMultiColorAmmo);
                    CB_Checkbox    (" - Random Colors",  ref _multiColorRNG,          Callbacks.OnMultiColorRNG, enabled: _multiColorAmmo);
                    CB_Checkbox    ("Shoot Blocks",      ref _shootBlocks,            Callbacks.OnShootBlocks);
                    if (_shootBlocks)
                    {
                        CB_RadioButton("SB_Y", "You",      ref _shootBlockScope, Callbacks.OnShootBlockScope, PlayerSelectScope.Personal, enabled: _shootBlocks);
                        ImGui.SameLine();
                        CB_RadioButton("SB_E", "Everyone", ref _shootBlockScope, Callbacks.OnShootBlockScope, PlayerSelectScope.Everyone, enabled: _shootBlocks);
                    }
                    CB_Checkbox    ("Shoot Grenades",    ref _shootGrenadeAmmo,       Callbacks.OnShootGrenades);
                    CB_Checkbox    ("Shoot Rockets",     ref _shootRocketAmmo,        Callbacks.OnShootRockets);
                    if (_shootRocketAmmo)
                    {
                        CB_RadioButton("SA_Y", "You",      ref _shootAmmoScope, Callbacks.OnShootAmmoScope, PlayerSelectScope.Personal, enabled: _shootGrenadeAmmo || _shootRocketAmmo);
                        ImGui.SameLine();
                        CB_RadioButton("SA_E", "Everyone", ref _shootAmmoScope, Callbacks.OnShootAmmoScope, PlayerSelectScope.Everyone, enabled: _shootGrenadeAmmo || _shootRocketAmmo);
                    }
                    CB_Checkbox    ("Freeze Lasers",     ref _freezeLasers,           Callbacks.OnFreezeLasers);
                    CB_Checkbox    ("Extend Laser Time", ref _extendLaserTime,        Callbacks.OnExtendLaserTime);
                    CB_Checkbox    ("Infi Laser Path",   ref _infiLaserPath,          Callbacks.OnInfiLaserPath);
                    CB_Checkbox    ("Infi Laser Bounce", ref _infiLaserBounce,        Callbacks.OnInfiLaserBounce);
                    CB_Checkbox    ("Explosive Lasers",  ref _explosiveLasers,        Callbacks.OnExplosiveLasers);
                    if (_explosiveLasers)
                    {
                        CB_RadioButton("EL_Y", "You",      ref _explosiveLasersScope, Callbacks.OnExplosiveLasersScope, PlayerSelectScope.Personal, enabled: _explosiveLasers);
                        ImGui.SameLine();
                        CB_RadioButton("EL_E", "Everyone", ref _explosiveLasersScope, Callbacks.OnExplosiveLasersScope, PlayerSelectScope.Everyone, enabled: _explosiveLasers);
                    }

                    CB_Checkbox    ("Ride Dragon",       ref _rideDragon,             Callbacks.OnRideDragon);

                    CB_Checkbox    ("Gravity",           ref _gravity,                Callbacks.OnGravity);
                    CB_Slider      ("##gravityYValue",   ref _gravityValue,           Callbacks.OnGravityValue, min: -50.00f, max: 0f, enabled: _gravity, format: "%.2fx");

                    CB_Checkbox    ("Camera XYZ",        ref _cameraXyz,              Callbacks.OnCamera);
                    CB_TextDisabled("X", enabled: _cameraXyz, sameLineAfter: true);
                    CB_Slider      ("##cameraXValue",    ref _cameraXValue,           Callbacks.OnCameraXValue, min: -100f, max: 100f, enabled: _cameraXyz, format: "%.0f");
                    CB_TextDisabled("Y", enabled: _cameraXyz, sameLineAfter: true);
                    CB_Slider      ("##cameraYValue",    ref _cameraYValue,           Callbacks.OnCameraYValue, min: -100f, max: 100f, enabled: _cameraXyz, format: "%.0f");
                    CB_TextDisabled("Z", enabled: _cameraXyz, sameLineAfter: true);
                    CB_Slider      ("##cameraZValue",    ref _cameraZValue,           Callbacks.OnCameraZValue, min: -100f, max: 100f, enabled: _cameraXyz, format: "%.0f");

                    CB_Checkbox    ("Item Vortex",       ref _itemVortex,             Callbacks.OnItemVortex);
                    CB_Checkbox    (" - Beacon Mode",    ref _beaconMode,             Callbacks.OnBeaconMode, enabled: _itemVortex);
                    CB_MultiPCombo ("##iVortexPlrCombo", ref _itemVortexMultiTarg,    Callbacks.OnItemVortexPlayer, _itemVortexMultiTargetNetIds, _itemVortex && !_beaconMode);
                    CB_ItemCombo   ("##vortexICombo",    ref _vortexItemComboIndex,   Callbacks.OnItemVortexType, enabled: _itemVortex);
                    CB_StepSlider  ("##beaconHValue",    ref _beaconHeightValue,      Callbacks.OnBeaconHeightValue, min: 0, max: 64, enabled: _itemVortex && _beaconMode, format: "Height: %d");
                    CB_Slider      ("##vortexValue",     ref _itemVortexTimerValue,   Callbacks.OnItemVortexValue, min: 1, max: 1000, enabled: _itemVortex, format: "Speed: %d ms");
                    if (!_itemVortex) { _itemVortexMultiTarg = false; _itemVortexMultiTargetNetIds.Clear(); }

                    CB_Checkbox    ("DE Dragon Counter", ref _dragonCounter,          Callbacks.OnDragonCounter);
                    CB_Checkbox    ("No Lava Visuals",   ref _noLavaVisuals,          Callbacks.OnNoLavaVisuals);

                    CB_Checkbox    ("Block ESP",         ref _blockEsp,               Callbacks.OnBlockEsp);
                    CB_MultiBCombo ("##MultiBESPCombo",  _blockEspSelectedTypes,      Callbacks.OnBlockEspTypes, enabled: _blockEsp);
                    CB_Slider      ("##bEspChunkRValue", ref _blockEspChunkRadValue,  Callbacks.OnBlockEspChunkRadValue, min: 1, max: 24, enabled: _blockEsp, format: "Max Chunks: %d");
                    CB_Slider      ("##bEspMaxBoxValue", ref _blockEspMaxBoxesValue,  Callbacks.OnBlockEspMaxBoxesValue, min: 1, max: 10000, enabled: _blockEsp, format: "Max Matches: %d");
                    CB_Checkbox    ("Hide Tracers",      ref _blockEspNoTraceEnabled, Callbacks.OnBlockEspHideTracers, enabled: _blockEsp);

                    CB_Checkbox    ("Always Day Sky",    ref _alwaysDaySky,           Callbacks.OnAlwaysDaySky);
                    CB_Checkbox    ("Change Game Title", ref _changeGameTitle,        null);
                    CB_Button      ("##changeTitleBttn", "Apply",                     Callbacks.OnApplyTitle, enabled: _changeGameTitle);
                    CB_TextArea    ("##gTitleTextInput", ref _gameTitleTextInput,     Callbacks.OnGameTitleTextMessage, rowsTall: 1, enabled: _changeGameTitle, hint: "Type title here...");

                    // ===================== [Building & Mining] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Building & Mining]", Color.Gold);

                    CB_Checkbox    ("Instant Mine",      ref _instantMine,            Callbacks.OnInstantMine);
                    CB_Checkbox    ("Rapid Place",       ref _rapidPlace,             Callbacks.OnRapidPlace);
                    CB_Checkbox    ("Rapid Tools",       ref _rapidTools,             Callbacks.OnRapidTools);
                    CB_Checkbox    ("Infi Items",        ref _infItems,               Callbacks.OnInfiniteItems);
                    CB_Checkbox    ("Infi Durability",   ref _infDurability,          Callbacks.OnInfiniteDurability);
                    CB_Checkbox    ("All Guns Harvest",  ref _allHarvest,             Callbacks.OnAllGunsHarvest);

                    CB_Checkbox    ("Block Nuker:",      ref _blockNuker,             Callbacks.OnBlockNuker);
                    CB_Slider      ("##bNukerRValue",    ref _blockNukerRangeValue,   Callbacks.OnBlockNukerRangeValue, min: 0, max: 20, enabled: _blockNuker, format: "Range: %d");

                    // ===================== [Ghost Mode] =====================
                    if (outOfGameLocked) ImGui.EndDisabled();
                    ImGui.AlignTextToFramePadding(); CenterText("[Ghost Mode]", Color.Gold);

                    CB_Checkbox    ("Ghost Mode",        ref _ghostMode,              Callbacks.OnGhostMode);
                    CB_Checkbox    (" - Hide Join Msg",  ref _ghostModeHideName,      Callbacks.OnGhostModeHideName, enabled: _ghostMode);

                    if (outOfGameLocked) ImGui.BeginDisabled();

                    // ===================== [Test / Debug] =====================
                    if (outOfGameLocked) ImGui.EndDisabled();
                    ImGui.AlignTextToFramePadding(); CenterText("[Test / Debug]", Color.Gold);

                    CB_Checkbox    ("Trial Mode",        ref _trailMode,              Callbacks.OnTrailMode);

                    if (outOfGameLocked) ImGui.BeginDisabled();

                    #endregion

                    #region Column 3

                    ImGui.TableNextColumn();
                    ImGui.PushItemWidth(-1);

                    // ===================== [Combat & Aimbot] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Combat & Aimbot]", Color.Gold);

                    CB_Checkbox    ("Super Gun Stats",   ref _superGunStats,          Callbacks.OnSuperGunStats);
                    CB_Checkbox    ("Infi Ammo",         ref _infAmmo,                Callbacks.OnInfiniteAmmo);
                    CB_Checkbox    ("Infi Clips",        ref _infClips,               Callbacks.OnInfiniteClips);
                    CB_Checkbox    ("Rapid Fire",        ref _rapidFire,              Callbacks.OnRapidFire);
                    CB_Checkbox    ("No Gun Cooldown",   ref _noGunCooldown,          Callbacks.OnNoGunCooldown);
                    CB_Checkbox    ("No Gun Recoil",     ref _noGunRecoil,            Callbacks.OnNoGunRecoil);
                    CB_Checkbox    ("Chaotic Aim",       ref _chaoticAim,             Callbacks.OnChaoticAim);

                    CB_Checkbox    ("Projectile Tuning", ref _projectileTuning,       Callbacks.OnProjectileTuning);
                    CB_StepSlider  ("##projTuning",      ref _projectileTuningValue,  Callbacks.OnProjectileTuningValue, min: 1, max: ProjectileOutputTuning.MaxMultiplierClamp, step: 1,
                                   enabled: _projectileTuning);

                    CB_Checkbox    ("Player Aimbot",     ref _playerAimbot,           Callbacks.OnPlayerAimbot);
                    CB_Checkbox    ("Dragon Aimbot",     ref _dragonAimbot,           Callbacks.OnDragonAimbot);
                    CB_Checkbox    ("Mob Aimbot",        ref _mobAimbot,              Callbacks.OnMobAimbot);
                    CB_Checkbox    ("[AB] Bullet Drop",  ref _aimbotBulletDrop,       Callbacks.OnAimbotBulletDrop);
                    CB_Checkbox    ("[AB] Require LoS",  ref _aimbotRequireLos,       Callbacks.OnAimbotRequireLos);
                    CB_Checkbox    ("[AB] Face Enemy",   ref _aimbotFaceEnemy,        Callbacks.OnAimbotFaceEnemy);
                    CB_Slider      ("##AbRanDelayValue", ref _aimbotRandomDelayValue, Callbacks.OnAimbotRandomDelayValue, min: 0, max: 500, format: "Random Delay: %d ms");

                    CB_Checkbox    ("No Mob Blocking",   ref _noMobBlocking,          Callbacks.OnNoMobBlocking);
                    CB_Checkbox    ("Shoot Host Ammo",   ref _shootHostAmmo,          Callbacks.OnShootHostAmmo);
                    CB_Checkbox    ("Shoot Bow Ammo",    ref _shootBowAmmo,           Callbacks.OnShootBowAmmo);

                    CB_Checkbox    ("Shoot Fireballs:",  ref _shootFireballAmmo,      Callbacks.OnShootFireballAmmo);
                    CB_FireBCombo  ("##fireBallCombo",   ref _fireballIndex, enabled: _shootFireballAmmo);

                    CB_Checkbox    ("Rocket Speed:",     ref _rocketSpeed,            Callbacks.OnRocketSpeed);
                    CB_TextDisabled("R", enabled: _rocketSpeed, sameLineAfter: true);
                    CB_Slider      ("##rocketSpeedMul",  ref _rocketSpeedValue,       Callbacks.OnRocketSpeedValue,       min: 0.00f, max: 500.0f, enabled: _rocketSpeed, format: "%.2fx");
                    CB_TextDisabled("G", enabled: _rocketSpeed, sameLineAfter: true);
                    CB_Slider      ("##gRocketSpeedMul", ref _guidedRocketSpeedValue, Callbacks.OnGuidedRocketSpeedValue, min: 0.00f, max: 500.0f, enabled: _rocketSpeed, format: "%.2fx");

                    CB_Checkbox    ("PvP Thorns",        ref _pvpThorns,              Callbacks.OnPvpThorns);
                    CB_Checkbox    ("Death Aura:",       ref _deathAura,              Callbacks.OnDeathAura);
                    CB_Slider      ("##dAuraRangeValue", ref _deathAuraRangeValue,    Callbacks.OnDeathAuraRangeValue, min: 0, max: 200, enabled: _deathAura, format: "Aura Range: %d");
                    CB_Checkbox    ("Begone Aura:",      ref _begoneAura,             Callbacks.OnBegoneAura);
                    CB_Slider      ("##bAuraRangeValue", ref _begoneAuraRangeValue,   Callbacks.OnBegoneAuraRangeValue, min: 0, max: 200, enabled: _begoneAura, format: "Begone Range: %d");

                    // ===================== [Blacklists] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[Blacklists]", Color.Gold);
                    BlacklistUI.DrawBlacklistWidget();

                    // ===================== [World Rules] =====================
                    ImGui.AlignTextToFramePadding(); CenterText("[World Rules]", Color.Gold);

                    ImGui.AlignTextToFramePadding(); CenterText("Game Difficulty:");
                    CB_DiffCombo   ("##difficultyCombo", ref _difficultyIndex);

                    ImGui.AlignTextToFramePadding(); CenterText("Game Mode:");
                    CB_GameMCombo  ("##gameModeCombo",   ref _gameModeIndex);

                    CB_TriCheckbox ("Set World Time:",   ref _worldTime,              Callbacks.OnWorldTime);
                    CB_StepSlider  ("##worldTimeDay",    ref _timeDay,                Callbacks.OnWorldTimeDay,   min: 1, max: 10000, step: 1,
                                   enabled: _worldTime == TriState.On || _worldTime == TriState.Mixed);
                    CB_Slider      ("##worldTime",       ref _timeScale,              Callbacks.OnWorldTimeScale, min: 0f, max: 100.0f,
                                   enabled: _worldTime == TriState.On || _worldTime == TriState.Mixed);

                    CB_Checkbox    ("Exploding Ores",    ref _explodingOres,          Callbacks.OnExplodingOres);

                    CB_Checkbox    ("Discord:",          ref _chaosMode,              Callbacks.OnChaosMode);
                    CB_StepSlider  ("##chaosValue",      ref _choasTimerValue,        Callbacks.OnChaosValue, min: 1, max: 5000, enabled: _chaosMode);
                    CB_Checkbox    (" - Auto Clock",     ref _clockChaos,             Callbacks.OnClockChaos,  enabled: _chaosMode);
                    CB_Checkbox    (" - Auto Dragon",    ref _dragonChaos,            Callbacks.OnDragonChaos, enabled: _chaosMode);

                    #endregion

                    #endregion

                    if (outOfGameLocked) ImGui.EndDisabled();
                    ImGui.EndTable();
                }
                ImGui.PopStyleVar(); // CellPadding.
            }
            ImGui.EndChild();
            ImGui.PopStyleVar();     // WindowPadding.
            ImGui.Spacing();

            #region Set Name

            // "Set Name" row.
            ImGui.PushItemWidth(220);
            if (ImGui.Button("Set Name"))
                Callbacks.OnSetName?.Invoke(_nameInput ?? string.Empty);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);

            var flags =
                ImGuiInputTextFlags.NoHorizontalScroll |
                ImGuiInputTextFlags.EnterReturnsTrue   | // Optional but nice.
                ImGuiInputTextFlags.CtrlEnterForNewLine; // IMPORTANT: Enter submits, Ctrl+Enter newline.

            var size = new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() + 2);

            // Draw the input first.
            bool submit = ImGui.InputTextMultiline("##name", ref _nameInput, 65535, size, flags);

            // If empty, draw a hint on top (non-interactive).
            if (string.IsNullOrEmpty(_nameInput))
            {
                // Put cursor back where the item started, then offset a little for padding.
                var min = ImGui.GetItemRectMin();
                var pad = ImGui.GetStyle().FramePadding;

                // Use disabled-text color so it looks like a placeholder.
                uint col = ImGui.GetColorU32(ImGuiCol.TextDisabled);

                ImGui.GetWindowDrawList().AddText(
                    new Vector2(min.X + pad.X, min.Y + pad.Y),
                    col,
                    "Type name... (Enter = set, Ctrl+Enter = newline; HTML supported)"
                );
            }

            if (submit)
                Callbacks.OnSetName?.Invoke(_nameInput ?? string.Empty);

            ImGui.PopItemWidth();

            #endregion

            ImGui.EndGroup();
        }
        #endregion

        #region [+] Sections - Target Groupbox

        private static void DrawTargetGroup()
        {
            ImGui.BeginGroup();
            ImGui.Text("Target");
            ImGui.Separator();

            #region (Padding)

            // Geometry.
            float frameH   = ImGui.GetFrameHeight();
            float spacingY = ImGui.GetStyle().ItemSpacing.Y;
            float rowPitch = frameH + spacingY; // Since we'll zero vertical CellPadding.

            int maxRows = Clamp((int)Math.Round(_playerRowsF), MIN_PLAYER_ROWS, MAX_PLAYER_ROWS);

            // Tiny uniform padding around all sides of the child.
            float edgePad   = 5f;
            float bottomPad = 0f;

            // Height of the scroll area that mirrors the left side's rows:
            float childH = (maxRows * rowPitch - spacingY) + (edgePad * 2f);

            // Matches the spacing before, and the single row ("Set Name" + Input) after the scroll.
            float leftFooterHeight = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeight();

            // Stretch to match the left column's extra "Set Name" row (so bottoms align with the player list).
            float targetChildH = childH + leftFooterHeight - bottomPad;

            // Apply the small padding to this child only.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edgePad, edgePad));

            #endregion

            bool childOpen = ImGui.BeginChild(
                "target_scroll",
                new Vector2(0, targetChildH),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            );

            if (childOpen)
            {
                #region (Padding)

                // Keep vertical cell padding at 0 so row math stays exact.
                var style = ImGui.GetStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.CellPadding.X, 0f));

                // Table does the scrolling + clipping.
                var tflags = ImGuiTableFlags.SizingFixedFit
                           | ImGuiTableFlags.ScrollY
                           | ImGuiTableFlags.RowBg;

                #endregion

                // Here we use the same 'childH' minus the child's inner padding.
                ImGui.SetNextItemWidth(0); // Harmless; just keeping API symmetry.
                if (ImGui.BeginTable("target_container", 1, tflags, new Vector2(0, targetChildH - ImGui.GetStyle().WindowPadding.Y * 2f)))
                {
                    bool inGame              = CastleWallsMk2.IsInGame();
                    bool allowOutOfGameEdits = AllowOutOfGameSettingEdits();
                    bool outOfGameLocked     = !inGame && !allowOutOfGameEdits;

                    if (outOfGameLocked) ImGui.BeginDisabled();

                    ImGui.TableSetupColumn("MainContent", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableNextColumn();

                    // Exact pixel width of the "Spawn Mobs" button (col0).
                    float _spawnMobsButtonW = 0;

                    #region [+] Main Controls

                    #region Spawn Mobs

                    #region Row0: Spawn, Type, Amount

                    if (ImGui.BeginTable("spawnMobCols", 3, ImGuiTableFlags.SizingStretchProp, new Vector2()))
                    {
                        ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthStretch, 2f);
                        ImGui.TableSetupColumn("EnemyType", ImGuiTableColumnFlags.WidthStretch, 3f);
                        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthStretch, 1f);

                        // Button (col 0).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);

                        bool spawnPressed = ImGui.Button("Spawn Mobs", new Vector2(-1, 0));

                        // Cache exact width *after* the item is submitted.
                        _spawnMobsButtonW = ImGui.GetItemRectSize().X;

                        if (spawnPressed)
                        {
                            var gamer     = SelectedGamer;
                            var enemyType = _enemyOptions.Length > 0 ? _enemyOptions[_enemyIndex] : EnemyTypeEnum.ZOMBIE_0_0;
                            Callbacks.OnSpawnMobs?.Invoke(gamer, enemyType, _spawnEnemyAmount, _tpRandomOffset, _spawnMobSamePos);
                        }

                        // Enemy combo (col 1).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        string currentEnemy = _enemyOptions.Length > 0 ? EnemyLabel(_enemyOptions[_enemyIndex]) : "(none)";
                        if (ImGui.BeginCombo("##enemyCombo", currentEnemy))
                        {
                            for (int i = 0; i < _enemyOptions.Length; i++)
                            {
                                bool sel = (i == _enemyIndex);
                                if (ImGui.Selectable(EnemyLabel(_enemyOptions[i]), sel))
                                    _enemyIndex = i;
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // Amount (col 2).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);

                        int v = _spawnEnemyAmount;
                        if (ImGui.InputInt("##spawnAmount",
                                           ref v,
                                           0, 0, // Step=0 => Hides stepper buttons.
                                           ImGuiInputTextFlags.CharsDecimal
                                         | ImGuiInputTextFlags.AutoSelectAll))
                        { /* Do nothing here to avoid fighting user while typing */ }

                        // Commit when the user presses Enter, tabs away, or clicks elsewhere.
                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            v = Clamp(v, 1, 999);
                            _spawnEnemyAmount = v;
                        }

                        ImGui.EndTable();
                    }
                    #endregion

                    #region Row1: Offset, SamePos, AtCursor, ToCursor

                    ImGui.PushID("spawnMobs_crosshairRow");

                    // Give this mini-row normal padding even if we're currently in CellPadding(Y=0) mode.
                    var st = ImGui.GetStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(st.CellPadding.X, st.CellPadding.Y));

                    float spacing = ImGui.GetStyle().ItemSpacing.X;
                    float fullW   = ImGui.GetContentRegionAvail().X;

                    // Slider should be EXACTLY the same width as the "Spawn Mobs" button.
                    float sliderW = _spawnMobsButtonW > 1f ? _spawnMobsButtonW : fullW * (2f / 6f); // Fallback first frame.
                    if (sliderW > fullW) sliderW = fullW;

                    float rightW = Math.Max(0f, fullW - sliderW - spacing);

                    // Checkbox is a square roughly frame-height wide.
                    float cbW = ImGui.GetFrameHeight();

                    // Two buttons share remaining evenly.
                    float btnW = Math.Max(1f, (rightW - cbW - spacing * 2f) / 2f);

                    // --- Col0: Random offset slider (left).
                    ImGui.SetNextItemWidth(sliderW);
                    int off = _tpRandomOffset;
                    if (ImGui.SliderInt("##tpRandomOffset", ref off, 0, 20, "Offset: %d"))
                        _tpRandomOffset = Clamp(off, 0, 20);

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                        ImGui.SetTooltip(
                            "Spawn radius (blocks).\n" +
                            "Chooses a random X/Z point on the\n" +
                            "ring around the target player."
                        );

                    // --- Right group (checkbox + two buttons).
                    ImGui.SameLine(0f, spacing);

                    // Col1: Same position checkbox (no text).
                    if (ImGui.Checkbox("##spawnMobSamePos", ref _spawnMobSamePos))
                        Callbacks.OnSpawnMobSamePos?.Invoke(_spawnMobSamePos);

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                        ImGui.SetTooltip(
                            "When enabled, all spawns usen\n" +
                            "one shared random location."
                        );

                    // Ensure the buttons start after a fixed checkbox width, even if checkbox draws narrower.
                    ImGui.SameLine(0f, spacing);

                    // Col2: Button: At -> Cursor.
                    if (ImGui.Button("At->Cursor", new Vector2(btnW, 0)))
                    {
                        var gamer     = SelectedGamer;
                        var enemyType = _enemyOptions.Length > 0 ? _enemyOptions[_enemyIndex] : EnemyTypeEnum.ZOMBIE_0_0;
                        Callbacks.OnSpawnMobsAtCrosshair?.Invoke(gamer, enemyType, _spawnEnemyAmount);
                    }

                    ImGui.SameLine(0f, spacing);

                    // Col3: Button: To -> Cursor.
                    if (ImGui.Button("To->Cursor", new Vector2(btnW, 0)))
                    {
                        var gamer = SelectedGamer;
                        Callbacks.OnTpAllMobsToCrosshair?.Invoke(gamer);
                    }

                    ImGui.PopStyleVar();
                    ImGui.PopID();

                    ImGui.Separator();

                    #endregion

                    #endregion

                    #region Spawn Dragons

                    #region Row0: Spawn, Type, Amount

                    if (ImGui.BeginTable("spawnDragonCols", 3, ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthStretch, 2f);
                        ImGui.TableSetupColumn("EnemyType", ImGuiTableColumnFlags.WidthStretch, 3f);
                        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthStretch, 1f);

                        // Button (col 0).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.Button("Spawn Dragon", new Vector2(-1, 0)))
                        {
                            var gamer = SelectedGamer;
                            var dragonType = _dragonOptions.Length > 0 ? _dragonOptions[_dragonIndex] : DragonTypeEnum.FOREST;
                            Callbacks.OnSpawnDragons?.Invoke(gamer, dragonType, _spawnDragonAmount, _dragonHealthMultiplier);
                        }

                        // Enemy combo (col 1).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        string currentDragon = _dragonOptions.Length > 0 ? DragonLabel(_dragonOptions[_dragonIndex]) : "(none)";
                        if (ImGui.BeginCombo("##dragonCombo", currentDragon))
                        {
                            for (int i = 0; i < _dragonOptions.Length; i++)
                            {
                                bool sel = (i == _dragonIndex);
                                if (ImGui.Selectable(DragonLabel(_dragonOptions[i]), sel))
                                    _dragonIndex = i;
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // Amount (col 2).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);

                        int v = _spawnDragonAmount;
                        if (ImGui.InputInt("##spawnAmount",
                                           ref v,
                                           0, 0, // Step=0 => Hides stepper buttons.
                                           ImGuiInputTextFlags.CharsDecimal
                                         | ImGuiInputTextFlags.AutoSelectAll))
                        { /* Do nothing here to avoid fighting user while typing */ }

                        // Commit when the user presses Enter, tabs away, or clicks elsewhere.
                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            v = Clamp(v, 1, 999);
                            _spawnDragonAmount = v;
                        }

                        ImGui.EndTable();
                    }
                    #endregion

                    #region Row1: Health

                    ImGui.SetNextItemWidth(-1);
                    int hp = _dragonHealthMultiplier;
                    if (ImGui.SliderInt("##dragonHealthMultiplier", ref hp, 1, 250, "Health Multiplier: %dx"))
                        _dragonHealthMultiplier = Clamp(hp, 1, 250);

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                        ImGui.SetTooltip(
                            "Health Multiplier.\n" +
                            "The base dragon HP gets multiplied\n" +
                            "by this value when spawned."
                        );
                    #endregion

                    ImGui.Separator();

                    #endregion

                    #region Give Items

                    if (ImGui.BeginTable("giveItemsCols", 3, ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("Btn", ImGuiTableColumnFlags.WidthStretch, 2f);
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3f);
                        ImGui.TableSetupColumn("Amt", ImGuiTableColumnFlags.WidthStretch, 1f);

                        // Button.
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.Button("Give Items", new Vector2(-1, 0)))
                        {
                            var gamer = SelectedGamer;
                            var item = _itemOptions.Length > 0 ? _itemOptions[_itemIndex] : InventoryItemIDs.DirtBlock;
                            Callbacks.OnGiveItems?.Invoke(gamer, item, _itemGiveAmount);
                        }

                        // Item combo.
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        string currentItem = _itemOptions.Length > 0 ? ItemLabel(_itemOptions[_itemIndex].ToString()) : "(none)";
                        if (ImGui.BeginCombo("##itemCombo", currentItem))
                        {
                            for (int i = 0; i < _itemOptions.Length; i++)
                            {
                                bool sel = (i == _itemIndex);
                                if (ImGui.Selectable(ItemLabel(_itemOptions[i].ToString()), sel))
                                    _itemIndex = i;
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // Amount (text-only int input, 1-999).
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);

                        int v = _itemGiveAmount;
                        if (ImGui.InputInt("##giveAmount",
                                           ref v,
                                           0, 0, // Step=0 => Hides stepper buttons.
                                           ImGuiInputTextFlags.CharsDecimal
                                         | ImGuiInputTextFlags.AutoSelectAll))
                        { /* Do nothing here to avoid fighting user while typing */ }

                        // Commit when the user presses Enter, tabs away, or clicks elsewhere.
                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            v = Clamp(v, 1, 99999);
                            _itemGiveAmount = v;
                        }

                        ImGui.EndTable();
                    }
                    #endregion

                    #region Teleport To Location

                    if (ImGui.BeginTable("teleportOuterCols", 2, ImGuiTableFlags.SizingStretchProp))
                    {
                        float totalAvail = ImGui.GetContentRegionAvail().X;
                        float innerSpacing = ImGui.GetStyle().ItemSpacing.X;
                        float teleportButtonWidth = ((totalAvail - (innerSpacing * 2f)) * (2f / 6f));

                        ImGui.TableSetupColumn("Btn", ImGuiTableColumnFlags.WidthFixed, teleportButtonWidth);
                        ImGui.TableSetupColumn("Rest", ImGuiTableColumnFlags.WidthStretch, 1f);

                        bool doTeleport = false;

                        // [Teleport].
                        ImGui.TableNextColumn();
                        if (ImGui.Button("Teleport", new Vector2(-1, 0)))
                            doTeleport = true;

                        // [X] [Y] [Z] [Checkbox].
                        ImGui.TableNextColumn();
                        if (ImGui.BeginTable("teleportInnerCols", 4, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthStretch, 1f);
                            ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthStretch, 1f);
                            ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthStretch, 1f);
                            ImGui.TableSetupColumn("Chk", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());

                            // X.
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputText("##teleportX", ref _teleportXText, 32, ImGuiInputTextFlags.EnterReturnsTrue))
                                doTeleport = true;

                            // Y.
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputText("##teleportY", ref _teleportYText, 32, ImGuiInputTextFlags.EnterReturnsTrue))
                                doTeleport = true;

                            // Z.
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputText("##teleportZ", ref _teleportZText, 32, ImGuiInputTextFlags.EnterReturnsTrue))
                                doTeleport = true;

                            // Checkbox.
                            ImGui.TableNextColumn();

                            float chkAvail = ImGui.GetContentRegionAvail().X;
                            float chkSize  = ImGui.GetFrameHeight();

                            if (chkAvail > chkSize)
                                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (chkAvail - chkSize));

                            ImGui.Checkbox("##TeleportSpawnOnTop", ref _teleportSpawnOnTop);

                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Spawn On Top");

                            ImGui.EndTable();
                        }

                        if (doTeleport)
                            TrySubmitTeleport();

                        ImGui.EndTable();
                    }
                    #endregion

                    #region (Padding)

                    ImGui.PopStyleVar(); // Pop the CellPadding(Y=0).
                    ImGui.Spacing();

                    // Give ONLY the action grid some vertical padding between rows:
                    var style2 = ImGui.GetStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style2.CellPadding.X, style2.CellPadding.Y));

                    #endregion

                    #region [+] Action Buttons (Categorized)

                    ActionSection("Selected Player", disabled: false, draw: CellButton =>
                    {
                        CellButton("Kill Selected",        () => Callbacks.OnKillSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Teleport To Player",   () => Callbacks.OnTpToPlayer?.Invoke(SelectedGamer));

                        CellButton("View Steam Account",   () => Callbacks.OnViewSteamAccount?.Invoke(SelectedGamer));
                        CellButton("Give Rand Lootboxes",  () => Callbacks.OnLootboxSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Restart Selected",     () => Callbacks.OnRestartSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Silent Kick Selected", () => Callbacks.OnKickSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Freeze Selected",      () => Callbacks.OnFreezeSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Nade Collision Lag",   () => Callbacks.OnGrenadeCollisionLagSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Det x100 Grenades",    () => Callbacks.OnDetonate100GrenadesSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Rain Grenades",        () => Callbacks.OnGrenadeRainSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Box Player In Air",    () => Callbacks.OnBoxInSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Make Hole To Bedrock", () => Callbacks.OnHoleSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Force Player Update",  () => Callbacks.OnForcePlayerUpdateSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Invalid D-Migration",  () => Callbacks.OnInvalidDMigrationSelectedPlayer?.Invoke(SelectedGamer));

                        CellButton("Crash Selected",       () => Callbacks.OnCrashSelectedPlayer?.Invoke(SelectedGamer));
                        CellButton("Corrupt Selected",     () => Callbacks.OnCorruptSelectedPlayer?.Invoke(SelectedGamer));
                    });

                    ActionSection("All Players", disabled: false, draw: CellButton =>
                    {
                        CellButton("Kill All Players",     () => Callbacks.OnKillAllPlayers?.Invoke());
                        CellButton("Restart All Players",  () => Callbacks.OnRestartAllPlayers?.Invoke());

                        CellButton("Kick All Players",     () => Callbacks.OnKickAllPlayers?.Invoke());
                        CellButton("Freeze All Players",   () => Callbacks.OnFreezeAllPlayers?.Invoke());

                        CellButton("Crash All Players",    () => Callbacks.OnCrashAllPlayers?.Invoke());
                        CellButton("Corrupt All Players",  () => Callbacks.OnCorruptAllPlayers?.Invoke());
                    });

                    ActionSection("Local Player", disabled: false, draw: CellButton =>
                    {
                        CellButton("Drop All Items",       () => Callbacks.OnDropAllItems?.Invoke());
                        CellButton("Revive Player",        () => Callbacks.OnRevivePlayer?.Invoke());
                        CellButton("Unlock All Modes",     () => Callbacks.OnUnlockAllModes?.Invoke());
                        CellButton("Unlock All Achiev's",  () => Callbacks.OnUnlockAllAchievements?.Invoke());
                        CellButton("Max Stack All Items",  () => Callbacks.OnMaxStackAllItems?.Invoke());
                        CellButton("Repair All Items",     () => Callbacks.OnMaxHealthAllItems?.Invoke());
                    });

                    ActionSection("World / Entities", disabled: false, useFinalSpacing: false, draw: CellButton =>
                    {
                        CellButton("Kill All Mobs",        () => Callbacks.OnKillAllMobs?.Invoke());
                        CellButton("Clear Ground Items",   () => Callbacks.OnClearGroundItems?.Invoke());
                        CellButton("Activate Spawners",    () => Callbacks.OnActivateSpawners?.Invoke());
                        CellButton("Remove Dragon Msg",    () => Callbacks.OnRemoveDragonMsg?.Invoke());
                        CellButton("Current Dragon HP",    () => Callbacks.OnCurrentDragonHP?.Invoke());
                        CellButton("Clear Projectiles",    () => Callbacks.OnClearProjectiles?.Invoke());
                    });

                    ActionSectionCustom("cNoEvil", disabled: false, useHeader: false, draw: Cell =>
                    {
                        Cell(() => { if (ImGui.Button("cNoEvil (Cave Lighter)", new Vector2(-1, 0))) Callbacks.OnCaveLighter?.Invoke(); });
                        Cell(() => { CB_Slider("##caveLighterMaxDistance", ref _caveLighterMaxDistanceValue, Callbacks.OnCaveLighterMaxDistanceValue, min: 1, max: 150, format: "Max Distance: %d"); });
                    });

                    ActionSection("Debug", disabled: false, draw: CellButton =>
                    {
                        if (outOfGameLocked) ImGui.EndDisabled();

                        CellButton("Show Demo Window",     () => _showDemoWindow = true);
                        CellButton("Show Stats Window",    () => Callbacks.OnStatsWindow?.Invoke());

                        if (outOfGameLocked) ImGui.BeginDisabled();

                        // Generate more buttons to visualize the scrollbar.
                        // for (int b = 0; b < 20; b++) CellButton($"{b}", null);
                    });

                    ActionSectionCustom("Session Settings", disabled: false, draw: Cell =>
                    {
                        if (outOfGameLocked) ImGui.EndDisabled();
                        var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();

                        Cell(() =>
                        {
                            bool value = cfg.PreserveTogglesWhenLeavingGame;
                            if (ImGui.Checkbox("Preserve toggles", ref value))
                            {
                                cfg.PreserveTogglesWhenLeavingGame = value;
                                cfg.Save();
                            }

                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Keeps toggle states enabled when leaving a world/session.");
                        });

                        Cell(() =>
                        {
                            bool value = cfg.AllowOutOfGameSettingEdits;
                            if (ImGui.Checkbox("Out-of-game edits", ref value))
                            {
                                cfg.AllowOutOfGameSettingEdits = value;
                                cfg.Save();
                            }

                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Lets you edit stored setting values while not in a live game.");
                        });

                        if (outOfGameLocked) ImGui.BeginDisabled();
                    });
                    #endregion

                    #endregion

                    if (outOfGameLocked) ImGui.EndDisabled();
                    ImGui.EndTable();

                }
                // Always pop whichever CellPadding style is currently active:
                // - If BeginTable failed, this pops the original zero-padding push.
                // - If BeginTable succeeded and later swapped padding, this pops the replacement push.
                ImGui.PopStyleVar();
            }
            ImGui.EndChild();    // MUST always be called.
            ImGui.PopStyleVar(); // WindowPadding.
            ImGui.EndGroup();
        }

        #region ActionSection Helpers

        /// <summary>
        /// Helper for drawing a titled, centered-header "section" that contains:
        /// - A centered separator header.
        /// - An optional disabled scope.
        /// - A 2-column button grid where each button stretches to the full cell width.
        /// - Optional final spacing after the section.
        ///
        /// Why this exists:
        /// - Keeps category blocks consistent (header + grid + spacing).
        /// - Makes adding/removing actions easy (just edit the caller's list).
        /// - Centralizes disabling logic (ex: SelectedGamer required).
        /// </summary>
        static void ActionSection(string title, bool disabled, Action<Action<string, Action>> draw, bool useHeader = true, bool useFinalSpacing = true)
        {
            // Header.
            if (useHeader)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.SeparatorTextAlign, new Vector2(0.5f, 0.5f)); // Center.
                ImGui.SeparatorText(title);
                ImGui.PopStyleVar();
            }

            // Make the internal table ID ("grid") unique per section.
            ImGui.PushID(title);

            if (disabled) ImGui.BeginDisabled();

            if (ImGui.BeginTable("grid", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("left", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("right", ImGuiTableColumnFlags.WidthStretch, 1f);

                // Full-width button in the current cell.
                void CellButton(string label, Action onClick)
                {
                    ImGui.TableNextColumn();

                    // If you want "blank" cells, allow empty label.
                    if (string.IsNullOrEmpty(label))
                    {
                        ImGui.Dummy(new Vector2(0, 0));
                        return;
                    }

                    if (ImGui.Button(label, new Vector2(-1, 0)))
                        onClick?.Invoke();
                }

                // Caller supplies rows via repeated CellButton(...) calls.
                draw(CellButton);

                ImGui.EndTable();
            }

            if (disabled) ImGui.EndDisabled();

            ImGui.PopID();
            if (useFinalSpacing) ImGui.Spacing();
        }

        /// <summary>
        /// Helper for drawing a titled "section" that contains:
        /// - An optional disabled scope.
        /// - A 2-column custom-content grid where each cell can render arbitrary ImGui UI.
        /// - Optional final spacing after the section.
        ///
        /// Why this exists:
        /// - Keeps category blocks consistent when a section needs more than just buttons.
        /// - Allows mixing custom controls per cell (ex: sliders, checkboxes, buttons, labels).
        /// - Centralizes disabling logic (ex: SelectedGamer required).
        /// - Reuses the same 2-column layout style as ActionSection while allowing full custom cell rendering.
        /// </summary>
        static void ActionSectionCustom(string title, bool disabled, Action<Action<Action>> draw, bool useHeader = true, bool useFinalSpacing = true)
        {
            // Header.
            if (useHeader)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.SeparatorTextAlign, new Vector2(0.5f, 0.5f)); // Center.
                ImGui.SeparatorText(title);
                ImGui.PopStyleVar();
            }

            ImGui.PushID(title);

            if (disabled) ImGui.BeginDisabled();

            if (ImGui.BeginTable("grid", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("left", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("right", ImGuiTableColumnFlags.WidthStretch, 1f);

                void Cell(Action drawCell)
                {
                    ImGui.TableNextColumn();
                    drawCell?.Invoke();
                }

                draw(Cell);

                ImGui.EndTable();
            }

            if (disabled) ImGui.EndDisabled();

            ImGui.PopID();
            if (useFinalSpacing) ImGui.Spacing();
        }
        #endregion

        #endregion

        #region Sections - Player List

        private static void DrawPlayerList()
        {
            ImGui.TextUnformatted($"Player List ({_players.Count})"); // Header label with live count.

            /// <summary>
            /// Exact-rows sizing:
            ///  - Compute how many full Selectable rows fit in the remaining height.
            ///  - Give the child a height that snaps to whole rows (no half-visible items).
            ///  - Use a tiny uniform padding and zero vertical spacing inside the child.
            /// </summary>
            float availH   = ImGui.GetContentRegionAvail().Y; // Space left below the header in the current window.
            var   style    = ImGui.GetStyle();
            float edgePad  = 5f;                              // Tiny uniform padding inside the child.
            float frameH   = ImGui.GetFrameHeight();          // Height of a Selectable row (includes vertical frame padding).
            float spacingY = style.ItemSpacing.Y;             // Default vertical spacing between items.
            float rowPitch = frameH + spacingY;               // Row-to-row pitch.

            // How many rows "fit" in the available height (after padding)?
            // Add +spacingY in the numerator so the last row doesn't "half show".
            float visibleRows = (availH - edgePad * 2 + spacingY) / rowPitch;

            // Exact height for N rows: N * pitch - spacing + top/bottom padding.
            // The "- spacing" cancels the trailing spacing after the last row.
            float childH = (visibleRows * rowPitch - spacingY) + (edgePad * 2f);

            // Apply small padding just to this child; also zero vertical spacing so the math stays exact.
            // NOTE: With ItemSpacing.Y = 0 inside the child, rows pack tightly and the sizing math remains stable.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edgePad, edgePad));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(style.ItemSpacing.X, 0f));

            // Scrollable list region.
            bool childOpen = ImGui.BeginChild(
                "playerlist_scroll",
                new Vector2(0, childH),
                ImGuiChildFlags.Borders /* , ImGuiWindowFlags.AlwaysVerticalScrollbar */
            );

            if (childOpen)
            {
                // Slightly increase the font size for all children in this frame.
                ImGui.SetWindowFontScale(1.10f); // 1.0 = default.

                if (_players.Count == 0)
                {
                    ImGui.TextDisabled("(none)"); // Empty state.
                }
                else
                {
                    for (int i = 0; i < _players.Count; i++)
                    {
                        var g         = _players[i];
                        bool selected = (i == _playerIndex);

                        // Keep a stable ID using the network id (## suffix hides it from the label).
                        // This avoids Dear ImGui ID collisions if two players share the same display name.
                        if (ImGui.Selectable($"[{g.Id}] {DisplayName(g)}##player{g.Id}", selected))
                            _playerIndex = i; // Update selection on click.

                        if (selected) ImGui.SetItemDefaultFocus(); // Ensure the selected row is scrolled into view on open/frame.
                    }
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(2); // ItemSpacing, WindowPadding.

            // Keep selection valid (guard against list shrinking/clearing).
            if (_players.Count > 0)
                _playerIndex = Clamp(_playerIndex, 0, _players.Count - 1);
            else
                _playerIndex = -1;
        }
        #endregion

        #endregion

        #region Tab: Editors

        #region Tab: Player

        /// <summary>
        /// PLAYER EDITOR TAB
        ///
        /// Purpose:
        ///   - Inspect and edit the local player's inventory: Hotbar 1, Hotbar 2, and Backpack.
        ///   - Swap/create items and adjust Stack, Health (0..1), and Rounds (for guns).
        ///   - Push changes through the normal HUD/Save pipeline ("Upload to server").
        ///
        /// Controls:
        ///   - Auto-read (live mirror) with short suppression while controls are active.
        ///   - "Download from server" requests inventory snapshot and refreshes from live objects.
        ///   - Collapsible/closable sections (Hotbar 1, Hotbar 2, Backpack) that auto-size to fill space.
        ///
        /// Notes:
        ///   - No reflection: Uses CastleMinerZGame.Instance -> LocalPlayer -> PlayerInventory/TrayManager.
        ///   - Safe accessors with bounds checks; null items display as "(empty)" and can be selected.
        ///   - Item list is built once from InventoryItemIDs and sorted by live item names.
        ///   - CreateClampedItem() enforces MaxStackCount and health clamping; guns clamp clip capacity.
        ///   - Each section uses a bordered child as the sole scroll owner to clip table contents.
        ///   - Edits write directly to live objects; "Upload to server" packages HUD inventory via SaveData.
        /// </summary>

        #region UI State & Helpers

        // Auto-read gating: Mirrors live objects unless user is actively editing.
        static bool _plAutoRead = true;
        static int  _plSuppressReadFrames = 0;

        // Unsafe clip override (UI clamp only). When true, sliders/buttons cap at 5000.
        static bool _allowUnsafeStack  = false;
        static bool _allowUnsafeHealth = false;
        static bool _allowUnsafeClip   = false;
        const int   UNSAFE_STACK_CAP   = 100000;
        const float UNSAFE_HEALTH_MAX  = 100000.0f;
        const int   UNSAFE_CLIP_CAP    = 100000;

        // Returns the max stack the UI should enforce for this item.
        static int EffectiveStackCap(InventoryItem it)
        {
            int baseCap = Math.Max(1, it?.MaxStackCount ?? 1);
            return _allowUnsafeStack ? Math.Max(baseCap, UNSAFE_STACK_CAP) : baseCap;
        }

        // Returns the max health the UI should enforce for this item.
        static float EffectiveHealthCap(/* InventoryItem it */)
        {
            const float baseCap = 1f; // Game-native health range.
            return _allowUnsafeHealth ? Math.Max(baseCap, UNSAFE_HEALTH_MAX) : baseCap;
        }

        // Returns the clip capacity the UI should enforce for this gun.
        static int EffectiveClipCap(GunInventoryItem gi)
        {
            int baseCap = Math.Max(1, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);
            return _allowUnsafeClip ? Math.Max(baseCap, UNSAFE_CLIP_CAP) : baseCap;
        }

        // Briefly pause auto-read (adds a cushion so sliders/combos feel stable).
        static void PL_Suppress(int frames = 2)
        {
            frames = Math.Max(frames + 1, 2);
            if (frames > _plSuppressReadFrames) _plSuppressReadFrames = frames;
        }

        static bool PL_IsTabOpen()      => IsOpen && _tabPlayerOpen;
        static bool PL_ShouldAutoRead() => PL_IsTabOpen() && _plAutoRead && _plSuppressReadFrames == 0;

        // Item list (enum -> label), built once then reused.
        static InventoryItemIDs[] _plItemOptions = Array.Empty<InventoryItemIDs>();
        static string[]           _plItemLabels  = Array.Empty<string>();
        static bool               _plItemsBuilt  = false;

        static void PL_BuildItemListOnce()
        {
            if (_plItemsBuilt) return;
            _plItemsBuilt = true;

            var ids = Enum.GetValues(typeof(InventoryItemIDs)).Cast<InventoryItemIDs>().ToArray();

            var pairs = new List<(InventoryItemIDs id, string label)>(ids.Length);
            foreach (var id in ids)
                pairs.Add((id, ItemLabel(id.ToString()))); // Label comes from the Humanize/ItemLabel.

            // Alphabetical UX.
            pairs.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
            _plItemOptions = pairs.Select(p => p.id).ToArray();
            _plItemLabels  = pairs.Select(p => p.label).ToArray();
        }

        // Lookup helper: Enum -> index into _plItemOptions.
        static int PL_IndexOfId(InventoryItemIDs id)
        {
            for (int i = 0; i < _plItemOptions.Length; i++)
                if (_plItemOptions[i].Equals(id)) return i;
            return -1;
        }

        // Friendly fallbacks for null items / missing descriptions.
        static string SafeName(InventoryItem item) => item?.Name ?? "(empty)";
        static string SafeDesc(InventoryItem item) => item?.Description ?? "-";

        // Wider, wrapped tooltip for item name/description.
        static void ShowItemTooltip(InventoryItem item)
        {
            if (item == null || !ImGui.IsItemHovered()) return;

            // Reasonable width constraints; wrap at ~56 "columns".
            float wrapCols = ImGui.GetFontSize() * 56.0f;
            float vpW      = ImGui.GetMainViewport().Size.X;
            float minW     = Math.Min(380f, vpW * 0.5f);
            float maxW     = Math.Min(640f, vpW * 0.6f);

            ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(minW, 0f),
                                               new System.Numerics.Vector2(maxW, float.MaxValue));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(10f, 10f));

            if (ImGui.BeginItemTooltip())
            {
                ImGui.PushTextWrapPos(wrapCols);
                ImGui.TextUnformatted(item.Name ?? "(unnamed)");
                ImGui.Separator();
                ImGui.TextUnformatted($"{item.Description}." ?? "No description.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
            ImGui.PopStyleVar(); // WindowPadding.
        }
        #endregion

        #region Core Access (Live Objects)

        // Shortcuts to core game objects (no reflection here).
        static CastleMinerZGame     Game  => CastleMinerZGame.Instance;
        static Player               Me    => Game?.LocalPlayer;
        static PlayerInventory      Inv   => Me?.PlayerInventory;
        static InventoryTrayManager Trays => Inv?.TrayManager;

        // Safe tray access (guards range/null).
        static InventoryItem GetTrayItem(int tray, int slot)
            => (Trays != null && tray >= 0 && tray < 2 && slot >= 0 && slot < 8) ? Trays.Trays[tray, slot] : null;

        static void SetTrayItem(int tray, int slot, InventoryItem item)
        {
            if (Trays == null || tray < 0 || tray >= 2 || slot < 0 || slot >= 8) return;
            Trays.Trays[tray, slot] = item;
        }

        // Safe backpack access (guards range/null).
        static InventoryItem GetBackpackItem(int index)
            => (Inv != null && Inv.Inventory != null && index >= 0 && index < Inv.Inventory.Length) ? Inv.Inventory[index] : null;

        static void SetBackpackItem(int index, InventoryItem item)
        {
            if (Inv?.Inventory == null || index < 0 || index >= Inv.Inventory.Length) return;
            Inv.Inventory[index] = item;
        }

        // Create items while clamping stack/health; guns respect clip capacity.
        static InventoryItem CreateClampedItem(InventoryItemIDs id, int desiredStack, float desiredHealth = 1f)
        {
            var it = InventoryItem.CreateItem(id, Math.Max(1, desiredStack));
            if (it == null) return null;

            int stackCap  = EffectiveStackCap(it);
            it.StackCount = Math.Min(Math.Max(0, desiredStack), stackCap);
            float health  = float.IsNaN(desiredHealth) ? 1f : desiredHealth;
            float hCap    = EffectiveHealthCap();

            it.StackCount      = Math.Min(Math.Max(0, desiredStack), stackCap);
            it.ItemHealthLevel = Math.Max(0f, Math.Min(hCap, health));

            if (it is GunInventoryItem gi)
            {
                int clipMax = Math.Max(0, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);
                gi.RoundsInClip = Math.Min(gi.RoundsInClip, clipMax);
            }
            return it;
        }

        // Network helpers: Request/upload inventory via the game's normal messages/SaveData.
        static void RequestInventoryFromServer()
        {
            if (Game?.GameScreen?.HUD?.PlayerInventory == null) return;
            var ourLocalGamer = CastleMinerZGame.Instance?.MyNetworkGamer;
            var type          = AccessTools.TypeByName("DNA.CastleMinerZ.Net.RequestInventoryMessage");
            var _miReqInvSend = AccessTools.Method(type, "Send", new[] { typeof(LocalNetworkGamer) });
            _miReqInvSend?.Invoke(null, new object[] { ourLocalGamer });
        }

        static void UploadInventoryToServer()
        {
            var hudInv = Game?.GameScreen?.HUD?.PlayerInventory;
            hudInv?.RemoveEmptyItems(); // HUD inventory is what SaveData packages.
            Game?.SaveData();           // Online: Triggers InventoryStoreOnServerMessage.Send.
        }
        #endregion

        #region Row Renderers (Table Cells)

        // Combo for item class; preserves stack/health on swap when possible.
        static void DrawItemCell(ref InventoryItem itemRef, string idStr)
        {
            var id   = itemRef?.ItemClass.ID ?? default;
            int idx  = PL_IndexOfId(id);
            string preview = (idx >= 0 && idx < _plItemLabels.Length) ? _plItemLabels[idx] : SafeName(itemRef);

            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo($"##{idStr}", preview))
            {
                // Suppress auto-read while interacting.
                if (ImGui.IsItemActive() || ImGui.IsItemHovered()) PL_Suppress(3);

                // (empty) choice clears the slot.
                bool noneSel = (itemRef == null);
                if (ImGui.Selectable("(empty)", noneSel))
                {
                    itemRef = null;
                    PL_Suppress(2);
                }
                if (noneSel) ImGui.SetItemDefaultFocus();

                for (int i = 0; i < _plItemOptions.Length; i++)
                {
                    bool sel = (idx == i);
                    if (ImGui.Selectable(_plItemLabels[i], sel))
                    {
                        int   prevStack  = itemRef?.StackCount      ?? 1;
                        float prevHealth = itemRef?.ItemHealthLevel ?? 1f;
                        try { itemRef = CreateClampedItem(_plItemOptions[i], prevStack, prevHealth); } catch { } // Catch invalid / unimplemented items.
                        PL_Suppress(2);
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Tooltip: Name + description.
            if (itemRef != null) ShowItemTooltip(itemRef);
        }

        // Int slider for Stack (0..MaxStack).
        static void DrawStackCell(InventoryItem itemRef, string idStr)
        {
            if (itemRef == null) { ImGui.TextDisabled("-"); return; }
            int maxStack = EffectiveStackCap(itemRef);
            int sc       = itemRef.StackCount;

            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt($"##{idStr}", ref sc, 0, maxStack))
            {
                itemRef.StackCount = Math.Max(0, Math.Min(maxStack, sc));
                PL_Suppress(2);
            }
        }

        // Float slider for Health (0..1).
        static void DrawHealthCell(InventoryItem itemRef, string idStr)
        {
            if (itemRef == null) { ImGui.TextDisabled("-"); return; }

            float hCap = EffectiveHealthCap();
            float h    = itemRef.ItemHealthLevel;

            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat($"##{idStr}", ref h, 0f, hCap, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            {
                itemRef.ItemHealthLevel = Math.Max(0f, Math.Min(hCap, h));
                PL_Suppress(2);
            }
        }

        // Rounds (guns only): [-] slider [+] with clip capacity clamp.
        static void DrawRoundsCell(InventoryItem itemRef, string sliderId, string minusId, string plusId)
        {
            if (!(itemRef is GunInventoryItem gi)) { ImGui.TextDisabled("-"); return; }

            int clip    = gi.RoundsInClip;
            int clipMax = EffectiveClipCap(gi);

            float totalW  = ImGui.GetContentRegionAvail().X;
            float btnW    = Math.Min(28f, totalW * 0.2f);
            float sliderW = Math.Max(10f, totalW - (btnW * 2f) - ImGui.GetStyle().ItemSpacing.X * 2);

            ImGui.PushID(minusId);
            if (ImGui.Button($"-##{minusId}", new System.Numerics.Vector2(btnW, 0)))
            {
                gi.RoundsInClip = Math.Max(0, gi.RoundsInClip - 1);
                PL_Suppress(2);
            }
            ImGui.PopID();

            ImGui.SameLine();
            ImGui.SetNextItemWidth(sliderW);
            if (ImGui.SliderInt($"##{sliderId}", ref clip, 0, Math.Max(1, clipMax), "%d", ImGuiSliderFlags.AlwaysClamp))
            {
                gi.RoundsInClip = Math.Max(0, Math.Min(clipMax, clip));
                PL_Suppress(2);
            }

            ImGui.SameLine();
            ImGui.PushID(plusId);
            if (ImGui.Button($"+##{plusId}", new System.Numerics.Vector2(btnW, 0)))
            {
                gi.RoundsInClip = Math.Min(Math.Max(1, clipMax), gi.RoundsInClip + 1);
                PL_Suppress(2);
            }
            ImGui.PopID();
        }

        // One row across all 5 columns.
        static void DrawSlotRow(ref InventoryItem itemRef, string rowPrefix, int slotIndex)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(slotIndex.ToString("00"));

            ImGui.TableNextColumn();
            DrawItemCell(ref itemRef, $"{rowPrefix}_item_{slotIndex}");

            ImGui.TableNextColumn();
            DrawStackCell(itemRef, $"{rowPrefix}_stack_{slotIndex}");

            ImGui.TableNextColumn();
            DrawHealthCell(itemRef, $"{rowPrefix}_health_{slotIndex}");

            ImGui.TableNextColumn();
            DrawRoundsCell(itemRef, $"{rowPrefix}_rnd_{slotIndex}", $"{rowPrefix}_minus_{slotIndex}", $"{rowPrefix}_plus_{slotIndex}");
        }
        #endregion

        #region Top Controls (Toolbar)

        /// <summary>
        /// Player Editor toolbar.
        ///
        /// Responsibilities:
        ///   - Auto-read toggle and server round-trip (download/upload) buttons.
        ///   - Target player dropdown (NetworkGamer list).
        ///   - Host-only "Load Player -> Me", "Send My Inventory -> Player", "Clear Inventory" actions.
        ///   - Unsafe editor toggles (stack, health, clip size).
        ///
        /// Notes:
        ///   - Networking helpers live below in the "Player Networking & Helpers" region.
        ///   - All ImGui.BeginDisabled / EndDisabled pairs are balanced to avoid stack asserts.
        /// </summary>
        static void DrawTopBar()
        {
            #region Auto-Read + Server Roundtrip

            ImGui.Checkbox("Auto-read (live)", ref _plAutoRead);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Mirror the live PlayerInventory each frame unless you're actively editing.");

            ImGui.SameLine();
            if (ImGui.Button("Download from server"))
            {
                RequestInventoryFromServer();
                _plSuppressReadFrames = 0; // Allow immediate refresh post-download.
            }

            ImGui.SameLine();
            if (ImGui.Button("Upload to server"))
            {
                UploadInventoryToServer();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("| Edits are immediate; 'Upload' syncs to host.");

            #endregion

            ImGui.Separator();

            #region Target Player Dropdown + Load/Send/Clear Actions

            // Player dropdown.
            PL_RebuildGamerList();

            string preview = "(no session)";
            if (PL_TargetAllPlayers)
            {
                preview = "(All Players)";
            }
            else if (PL_TargetGamer != null)
            {
                var g = PL_TargetGamer;
                preview = g.IsLocal ? $"{g.Gamertag} (You)" : g.Gamertag;
            }

            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("##pl_target_player", preview))
            {
                for (int i = 0; i < _plGamerCache.Length; i++)
                {
                    var g = _plGamerCache[i];
                    string label = g.IsLocal ? $"{g.Gamertag} (You)" : g.Gamertag;
                    bool selected = (i == _plSelectedGamerIndex);

                    if (ImGui.Selectable(label, selected))
                    {
                        _plSelectedGamerIndex = i;
                        PL_Suppress(4); // Avoid auto-read thrash when swapping targets.
                    }
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.Separator();

                // Virtual entry: Target ALL non-local players.
                bool allSel = PL_TargetAllPlayers;
                if (ImGui.Selectable("(All Players)", allSel))
                {
                    _plSelectedGamerIndex = PL_ALL_PLAYERS_INDEX;
                    PL_Suppress(4); // Avoid auto-read thrash when swapping targets.
                }
                if (allSel)
                    ImGui.SetItemDefaultFocus();

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            var game       = Game;
            var target     = PL_TargetGamer;
            bool targetAll = PL_TargetAllPlayers;
            bool canSend   = (game?.MyNetworkGamer is LocalNetworkGamer && (targetAll ? _plGamerCache.Length > 1 : (target != null && !target.IsLocal)));
            bool canLoad   = (!targetAll && target != null && !target.IsLocal && Me != null && game.IsGameHost);
            bool canClear  = (game?.MyNetworkGamer is LocalNetworkGamer && (targetAll ? _plGamerCache.Length > 1 : (target != null && target.Tag is Player)));

            // "Load Player -> Me"
            ImGui.SameLine();
            ImGui.BeginDisabled(!canLoad);
            if (ImGui.Button("Load Player -> Me (Host Only)"))
            {
                try
                {
                    var targetName = target != null ? target.Gamertag : "(null)";
                    Log($"Cloning inventory from '{targetName}' (Id={target?.Id}) to local player.");

                    LoadSelectedPlayerInventoryToMe();
                }
                catch (Exception ex)
                {
                    Log($"[PlayerTab] LoadSelectedPlayerInventoryToMe failed: {ex}.");
                }
            }
            ImGui.EndDisabled();

            if (ImGui.IsItemHovered())
            {
                if (canLoad)
                    ImGui.SetTooltip("Clone the selected player's current inventory to you, then sync it to the server.");
                else
                    ImGui.SetTooltip("Select another player in the dropdown to copy their inventory to you.");
            }

            // Send My Inventory -> Player.
            // Always call BeginDisabled / EndDisabled in a pair so ImGui can't get unbalanced.
            ImGui.SameLine();
            ImGui.BeginDisabled(!canSend);
            if (ImGui.Button("Send My Inventory -> Player"))
            {
                try
                {
                    // Log who we're trying to send to.
                    var targetName = targetAll ? "(All Players)" : (target != null ? target.Gamertag : "(null)");
                    SendLog(targetAll
                        ? $"Sending local inventory to ALL players."
                        : $"Sending local inventory to '{targetName}' (Id={target?.Id}).");

                    // Copies your current inventory layout to the selected player
                    // and pushes it via InventoryRetrieveFromServerMessage.
                    SendMyInventoryToSelectedPlayer();
                }
                catch (Exception ex)
                {
                    // Swallow to keep ImGui stack intact; log if you have a mod logger.
                    Log($"[PlayerTab] SendMyInventoryToSelectedPlayer failed: {ex}.");
                }
            }
            ImGui.EndDisabled();

            if (ImGui.IsItemHovered())
            {
                if (canSend)
                    if (targetAll)
                        ImGui.SetTooltip("Send your current inventory layout to ALL players (except you).");
                    else
                        ImGui.SetTooltip("Send your current inventory layout to the selected player.");
                else
                    ImGui.SetTooltip("Requires a network session and a selected non-local player.");
            }

            // Clear Selected Inventory (dropdown-aware).
            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            ImGui.BeginDisabled(!canClear);
            if (ImGui.Button("Delete Bag"))
            {
                try
                {
                    PL_ClearSelectedInventory();
                }
                catch (Exception ex)
                {
                    Log($"[PlayerTab] PL_ClearSelectedInventory failed: {ex}.");
                }
            }
            ImGui.EndDisabled();

            if (ImGui.IsItemHovered())
            {
                if (target == null)
                {
                    ImGui.SetTooltip(
                        "Select a player in the dropdown to clear their inventory.\n" +
                        "Host can clear anyone; clients can only clear themselves.");
                }
                else if (!target.IsLocal)
                {
                    ImGui.SetTooltip(
                        $"Clear {target.Gamertag}'s entire inventory (hotbars + backpack) " +
                        "and sync the change to all clients.");
                }
                else if (targetAll)
                {
                    ImGui.SetTooltip("Clear EVERYONE'S inventory (except you).");
                }
                else
                {
                    ImGui.SetTooltip(
                        "Clear your own inventory (hotbars + backpack), then upload to the host.");
                }
            }
            #endregion

            ImGui.Separator();

            #region Save Slots (Local Snapshots)

            // Vertically align the label with the buttons.
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Save slots:");
            ImGui.SameLine();

            for (int i = 0; i < PL_SAVE_SLOT_COUNT; i++)
            {
                ImGui.PushID(i);

                bool hasSnap = _plSaveSlots.ContainsKey(i);

                // Label: Plain "N" when empty, "[N]" when populated.
                string label = hasSnap
                    ? $"[{i + 1}]"
                    : (i + 1).ToString();

                // Compute width to fit text + frame padding.
                float textW = ImGui.CalcTextSize(label).X;
                float padX  = ImGui.GetStyle().FramePadding.X;
                float btnW  = textW + padX * 2f + 4f; // +4f for a tiny breathing room.
                btnW        = Math.Max(28f, btnW);    // Keep a sensible minimum.

                // Left-click: Load snapshot (replace trays + backpack).
                bool leftClick   = ImGui.Button(label, new Vector2(btnW, 0f));

                // Right / middle click handled via hovered item.
                bool rightClick  = ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                bool middleClick = ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Middle);

                if (leftClick)
                {
                    PL_LoadSlot(i);
                }

                if (rightClick)
                {
                    PL_SaveSlot(i);
                    hasSnap = true;
                }

                if (middleClick)
                {
                    PL_ClearSlot(i);
                    hasSnap = false;
                }

                if (ImGui.IsItemHovered())
                {
                    string tip = hasSnap
                        ? $"Slot {i + 1}:\n" +
                          "  Left-click   = load this slot into your inventory.\n" +
                          "  Right-click  = save your current inventory into this slot.\n" +
                          "  Middle-click = clear this slot."
                        : $"Slot {i + 1} (empty):\n" +
                          "  Right-click  = save your current inventory into this slot.";
                    ImGui.SetTooltip(tip);
                }

                ImGui.PopID();
                ImGui.SameLine();
            }
            ImGui.NewLine();

            #endregion

            ImGui.Separator();

            #region Unsafe Editor Toggles

            ImGui.Checkbox("Allow Unsafe Stack Size", ref _allowUnsafeStack);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Overrides MaxStackCount in the editor to {UNSAFE_STACK_CAP}.");

            ImGui.SameLine();
            ImGui.Checkbox("Allow Unsafe Health", ref _allowUnsafeHealth);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Lets the editor set ItemHealthLevel above 1.0 (up to {UNSAFE_HEALTH_MAX}).");

            ImGui.SameLine();
            ImGui.Checkbox("Allow Unsafe Clip Size", ref _allowUnsafeClip);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Overrides clip capacity in the editor to {UNSAFE_CLIP_CAP}.");

            #endregion
        }
        #endregion

        #region Section Content (Tables)

        /// <summary>
        /// Renders the hotbar N table (8 slots) for the given tray index.
        /// Caller controls the child height; this function just fills the table.
        /// </summary>
        static void DrawHotbarContent(int trayIndex)
        {
            if (Trays == null) { ImGui.TextDisabled("(no inventory)"); return; }

            float innerH = Math.Max(0f, ImGui.GetContentRegionAvail().Y); // Child's current inner height.
            var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.ScrollY;

            if (ImGui.BeginTable($"pl_table_tray_{trayIndex}", 5, flags, new System.Numerics.Vector2(0, innerH)))
            {
                ImGui.TableSetupColumn("#",      ImGuiTableColumnFlags.WidthFixed, 28f);
                ImGui.TableSetupColumn("Item",   ImGuiTableColumnFlags.WidthStretch, 0.50f);
                ImGui.TableSetupColumn("Stack",  ImGuiTableColumnFlags.WidthStretch, 0.20f);
                ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthStretch, 0.20f);
                ImGui.TableSetupColumn("Rounds", ImGuiTableColumnFlags.WidthStretch, 0.30f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < 8; i++)
                {
                    var slotItem = GetTrayItem(trayIndex, i);
                    DrawSlotRow(ref slotItem, $"tray{trayIndex}", i);
                    SetTrayItem(trayIndex, i, slotItem);
                }
                ImGui.EndTable();
            }
        }

        /// <summary>
        /// Renders the backpack table (all inventory slots).
        /// </summary>
        static void DrawBackpackContent()
        {
            if (Inv?.Inventory == null) { ImGui.TextDisabled("(no backpack)"); return; }

            float innerH = Math.Max(0f, ImGui.GetContentRegionAvail().Y);
            var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.ScrollY;

            if (ImGui.BeginTable("pl_table_bag", 5, flags, new System.Numerics.Vector2(0, innerH)))
            {
                ImGui.TableSetupColumn("#",      ImGuiTableColumnFlags.WidthFixed, 28f);
                ImGui.TableSetupColumn("Item",   ImGuiTableColumnFlags.WidthStretch, 0.50f);
                ImGui.TableSetupColumn("Stack",  ImGuiTableColumnFlags.WidthStretch, 0.20f);
                ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthStretch, 0.20f);
                ImGui.TableSetupColumn("Rounds", ImGuiTableColumnFlags.WidthStretch, 0.30f);
                ImGui.TableHeadersRow();

                int count = Inv.Inventory.Length;
                for (int i = 0; i < count; i++)
                {
                    var slotItem = GetBackpackItem(i);
                    DrawSlotRow(ref slotItem, "bag", i);
                    SetBackpackItem(i, slotItem);
                }
                ImGui.EndTable();
            }
        }
        #endregion

        #region Section Visibility & Layout

        // Visible (close button) and Expanded (arrow) states for each section.
        static bool _secHotbar1 = true, _secHotbar2 = true,  _secBackpack = true;
        static bool _expHotbar1 = true, _expHotbar2 = false, _expBackpack = false;

        /// <summary>
        /// Draws a framed collapsing header with open/closed tracking.
        /// </summary>
        static bool HeaderWithClose(string label, ref bool visible, ref bool expanded)
        {
            ImGui.SetNextItemOpen(expanded, ImGuiCond.Always);
            var flags = ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
            bool isOpen = ImGui.CollapsingHeader(label /*, ref visible */, flags);
            if (!visible) return false;

            if (ImGui.IsItemToggledOpen()) expanded = !expanded; else expanded = isOpen;
            return expanded;
        }

        /// <summary>
        /// Counts how many sections are expanded from the given index down.
        /// Used for proportional height allocation.
        /// </summary>
        static int ExpandedCountFrom(int idx)
        {
            int c = 0;
            if (idx <= 0 && _secHotbar1 && _expHotbar1) c++;
            if (idx <= 1 && _secHotbar2 && _expHotbar2) c++;
            if (idx <= 2 && _secBackpack && _expBackpack) c++;
            return c;
        }

        /// <summary>
        /// Counts visible section headers below the given index.
        /// Used to reserve vertical space for remaining headers.
        /// </summary>
        static int VisibleHeadersBelow(int idx)
        {
            int c = 0;
            if (idx < 1 && _secHotbar2) c++;
            if (idx < 2 && _secBackpack) c++;
            return c;
        }

        /// <summary>
        /// Begins a bordered child window used as the scroll owner for a section.
        /// </summary>
        static bool BeginSectionChild(string id, float height)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(6f, 6f));
            return ImGui.BeginChild(id, new System.Numerics.Vector2(0, height), ImGuiChildFlags.Borders);
        }

        /// <summary>
        /// Ends a section child and restores padding.
        /// </summary>
        static void EndSectionChild() { ImGui.EndChild(); ImGui.PopStyleVar(); }

        #endregion

        #region Main Draw

        /// <summary>
        /// Entry point for the Player Editor tab.
        ///
        /// Flow:
        ///   - Build item list once.
        ///   - Apply auto-read gating for live mirror.
        ///   - Draw toolbar (including player targeting and network actions).
        ///   - Layout and draw Hotbar1, Hotbar2, and Backpack sections with dynamic heights.
        /// </summary>
        public static void DrawPlayerEditorTab()
        {
            PL_BuildItemListOnce();
            PL_LoadSlotsOnce();

            // Live mirror gate: Read directly unless we're mid-edit.
            if (PL_ShouldAutoRead())
            {
                // No-op: Reference live instances each frame.
            }
            if (_plSuppressReadFrames > 0) _plSuppressReadFrames--;

            // Toolbar.
            DrawTopBar();
            ImGui.Separator();

            float headerUnit = ImGui.GetFrameHeightWithSpacing(); // Header + default spacing.
            float minBodyH   = 50f;                               // Safety floor for tiny windows.

            // Hotbar 1 (index 0).
            if (_secHotbar1)
            {
                bool expanded = HeaderWithClose("Hotbar 1", ref _secHotbar1, ref _expHotbar1);
                if (expanded)
                {
                    int  headersBelow = VisibleHeadersBelow(0);
                    int  expFromHere  = ExpandedCountFrom(0);
                    bool lastExpanded = (expFromHere == 1);

                    float avail         = ImGui.GetContentRegionAvail().Y;
                    float reservedBelow = headersBelow * headerUnit;

                    float childH = lastExpanded
                        ? (headersBelow == 0 ? 0f : Math.Max(minBodyH, avail - reservedBelow))
                        : Math.Max(minBodyH, (avail - reservedBelow) / expFromHere);

                    if (BeginSectionChild("pl_hotbar_0", childH))
                        DrawHotbarContent(0);
                    EndSectionChild();
                }
            }

            // Hotbar 2 (index 1).
            if (_secHotbar2)
            {
                bool expanded = HeaderWithClose("Hotbar 2", ref _secHotbar2, ref _expHotbar2);
                if (expanded)
                {
                    int  headersBelow = VisibleHeadersBelow(1);
                    int  expFromHere  = ExpandedCountFrom(1);
                    bool lastExpanded = (expFromHere == 1);

                    float avail         = ImGui.GetContentRegionAvail().Y;
                    float reservedBelow = headersBelow * headerUnit;

                    float childH = lastExpanded
                        ? (headersBelow == 0 ? 0f : Math.Max(minBodyH, avail - reservedBelow))
                        : Math.Max(minBodyH, (avail - reservedBelow) / expFromHere);

                    if (BeginSectionChild("pl_hotbar_1", childH))
                        DrawHotbarContent(1);
                    EndSectionChild();
                }
            }

            // Backpack (index 2).
            if (_secBackpack)
            {
                bool expanded = HeaderWithClose("Backpack (Inventory)", ref _secBackpack, ref _expBackpack);
                if (expanded)
                {
                    // Bottom-most: Auto-fills remaining vertical space.
                    if (BeginSectionChild("pl_backpack", 0f))
                        DrawBackpackContent();
                    EndSectionChild();
                }
            }
        }
        #endregion

        #region Player Networking & Helpers

        /// <summary>
        /// Currently selected NetworkGamer in the dropdown, or null if none / (All Players).
        /// </summary>
        static NetworkGamer PL_TargetGamer =>
            (_plGamerCache != null &&
             _plSelectedGamerIndex >= 0 &&
             _plSelectedGamerIndex < _plGamerCache.Length)
                ? _plGamerCache[_plSelectedGamerIndex]
                : null;

        // Virtual selection index: send inventory to ALL non-local players.
        const int PL_ALL_PLAYERS_INDEX = -2;

        /// <summary>
        /// True when the dropdown is targeting "(All Players)".
        /// </summary>
        static bool PL_TargetAllPlayers => _plSelectedGamerIndex == PL_ALL_PLAYERS_INDEX;

        static int            _plSelectedGamerIndex = -1;
        static NetworkGamer[] _plGamerCache         = Array.Empty<NetworkGamer>();

        /// <summary>
        /// Rebuilds the cached gamer list from the current NetworkSession and
        /// keeps the selected index in a valid range (preferring the local gamer).
        /// </summary>
        static void PL_RebuildGamerList()
        {
            var session = Game?.CurrentNetworkSession;
            if (session == null)
            {
                _plGamerCache = Array.Empty<NetworkGamer>();
                _plSelectedGamerIndex = -1;
                return;
            }

            var all = session.AllGamers;
            int count = all.Count;

            // Resize cache if needed.
            if (_plGamerCache == null || _plGamerCache.Length != count)
                _plGamerCache = new NetworkGamer[count];

            for (int i = 0; i < count; i++)
                _plGamerCache[i] = all[i];

            // Keep '(All Players)' selection across rebuilds.
            if (_plSelectedGamerIndex == PL_ALL_PLAYERS_INDEX)
                return;

            // Default to local gamer if index is invalid.
            if (_plSelectedGamerIndex < 0 || _plSelectedGamerIndex >= _plGamerCache.Length)
            {
                var my = Game.MyNetworkGamer;
                int idx = -1;
                for (int i = 0; i < _plGamerCache.Length; i++)
                {
                    if (_plGamerCache[i] == my)
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx >= 0) _plSelectedGamerIndex = idx;
                else if (_plGamerCache.Length > 0) _plSelectedGamerIndex = 0;
                else _plSelectedGamerIndex = -1;
            }
        }

        /// <summary>
        /// Send our current inventory layout to the selected player
        /// via InventoryRetrieveFromServerMessage (using the game's static Send(...) helper).
        /// </summary>
        static void SendMyInventoryToSelectedPlayer()
        {
            var game       = Game;
            var allPlayers = PL_TargetAllPlayers;
            var target     = PL_TargetGamer;

            if (!(game?.MyNetworkGamer is LocalNetworkGamer from) || !allPlayers && target == null)
                return;

            // "My inventory" = the local player's current PlayerInventory.
            var srcPlayer = Me; // CastleMinerZGame.Instance.LocalPlayer.
            var srcInv    = srcPlayer?.PlayerInventory;
            if (srcInv == null)
                return;

            if (allPlayers)
            {
                var session = game.CurrentNetworkSession;
                var all     = session?.AllGamers;
                if (all == null) return;

                // Send to every non-local gamer.
                foreach (NetworkGamer gamer in all)
                {
                    // No need to give to ourselves.
                    if (gamer == null || gamer == srcPlayer.Gamer) continue;

                    SendInventoryToPlayer(from, gamer, srcInv);
                }
                return;
            }

            // Host-side intent: Take your current inventory layout and
            // push it to the selected remote player using the reflected
            // InventoryRetrieveFromServerMessage.
            SendInventoryToPlayer(from, target, srcInv);
        }

        // Runtime type for the internal message.
        static readonly Type InvRetrieveMsgType =
            AccessTools.TypeByName("DNA.CastleMinerZ.Net.InventoryRetrieveFromServerMessage");

        // Static Send(from, Player, bool) method.
        static readonly MethodInfo InvRetrieveSendMethod =
            InvRetrieveMsgType != null
                ? AccessTools.Method(
                    InvRetrieveMsgType,
                    "Send",
                    new[] { typeof(LocalNetworkGamer), typeof(Player), typeof(bool) })
                : null;

        // Internal fields we want to set.
        static readonly FieldInfo InvField_Inventory =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "Inventory") : null;

        static readonly FieldInfo InvField_PlayerID =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "playerID") : null;

        static readonly FieldInfo InvField_Default =
            InvRetrieveMsgType != null ? AccessTools.Field(InvRetrieveMsgType, "Default") : null;

        /// <summary>
        /// Shallow clone for InventoryItem that preserves class, stack, health,
        /// and gun clip count (if applicable).
        /// </summary>
        static InventoryItem CloneItem(InventoryItem it)
        {
            if (it == null) return null;

            var copy = InventoryItem.CreateItem(it.ItemClass.ID, it.StackCount);
            if (copy == null) return null;

            copy.StackCount = it.StackCount;
            copy.ItemHealthLevel = it.ItemHealthLevel;

            if (it is GunInventoryItem giSrc && copy is GunInventoryItem giDst)
                giDst.RoundsInClip = giSrc.RoundsInClip;

            return copy;
        }

        /// <summary>
        /// Copies trays + backpack layout from one PlayerInventory to another,
        /// cloning items to avoid shared references.
        /// </summary>
        static void CopyInventoryLayout(PlayerInventory src, PlayerInventory dst)
        {
            if (src == null || dst == null) return;

            var srcTrays = src.TrayManager;
            var dstTrays = dst.TrayManager;

            if (srcTrays != null && dstTrays != null &&
                srcTrays.Trays != null && dstTrays.Trays != null)
            {
                int t0 = Math.Min(srcTrays.Trays.GetLength(0), dstTrays.Trays.GetLength(0));
                int t1 = Math.Min(srcTrays.Trays.GetLength(1), dstTrays.Trays.GetLength(1));

                for (int x = 0; x < t0; x++)
                    for (int y = 0; y < t1; y++)
                        dstTrays.Trays[x, y] = CloneItem(srcTrays.Trays[x, y]);
            }

            if (src.Inventory != null && dst.Inventory != null)
            {
                int len = Math.Min(src.Inventory.Length, dst.Inventory.Length);
                for (int i = 0; i < len; i++)
                    dst.Inventory[i] = CloneItem(src.Inventory[i]);
            }
        }

        /// <summary>
        /// Host-only helper that:
        ///   - Copies a source PlayerInventory layout into 'to' player's inventory.
        ///   - Broadcasts the new inventory to all clients via InventoryRetrieveFromServerMessage.Send.
        /// </summary>
        static void SendInventoryToPlayer(LocalNetworkGamer from, NetworkGamer to, PlayerInventory inv)
        {
            // Must have type, method, sender, target, and source inventory.
            if (InvRetrieveSendMethod == null || from == null || to == null || inv == null)
                return;

            // Need the actual Player object for the target gamer (host process owns all Players).
            if (!(to.Tag is Player targetPlayer))
                return;

            var dstInv = targetPlayer.PlayerInventory;
            if (dstInv == null)
                return;

            // Copy the source layout (inv) into the target player's real inventory first.
            // This makes the host's authoritative state match what you're about to broadcast.
            CopyInventoryLayout(inv, dstInv);

            // Now let the game's own static Send(...) build and send the message.
            // This will broadcast an InventoryRetrieveFromServerMessage to all clients,
            // tagged with targetPlayer.Gamer.Id.
            InvRetrieveSendMethod.Invoke(null, new object[] { from, targetPlayer, false });
        }

        /// <summary>
        /// Clones the selected player's inventory into the local player and then
        /// syncs that updated inventory upstream (host vs client paths differ).
        /// </summary>
        static void LoadSelectedPlayerInventoryToMe()
        {
            var game     = Game;
            var target   = PL_TargetGamer;
            var myPlayer = Me;

            if (target == null || myPlayer == null)
                return;

            var srcPlayer = target.Tag as Player;
            var srcInv    = srcPlayer?.PlayerInventory;
            var dstInv    = myPlayer.PlayerInventory;

            if (srcInv == null || dstInv == null)
                return;

            // Copy selected player's layout into ours.
            CopyInventoryLayout(srcInv, dstInv);

            // Sync our updated inventory upstream.
            if (game?.MyNetworkGamer is LocalNetworkGamer local)
            {
                if (local.IsHost)
                {
                    // Host: Our PlayerInventory is already authoritative.
                    // Optionally persist to disk:
                    game.SaveData();
                }
                else
                {
                    // Client: Push our new inventory to the host via the normal store message.
                    UploadInventoryToServer();
                }
            }
        }

        /// <summary>
        /// Clears the inventory of the currently selected gamer in the dropdown.
        /// </summary>
        static void PL_ClearSelectedInventory()
        {
            var game       = Game;
            var target     = PL_TargetGamer;
            bool targetAll = PL_TargetAllPlayers;

            if (game == null || !PL_TargetAllPlayers && target == null)
                return;

            if (!(game.MyNetworkGamer is LocalNetworkGamer local))
                return;

            // (All Players): Clear all NON-local players.
            if (targetAll)
            {
                var session = game.CurrentNetworkSession;
                var all = session?.AllGamers;
                if (all == null)
                    return;

                int cleared = 0;

                // Clear every non-local gamer.
                foreach (NetworkGamer gamer in all)
                {
                    // No need to give to ourselves.
                    if (gamer == null || gamer == Me.Gamer) continue;

                    // Get the player object from networkgamer.
                    if (!(gamer.Tag is Player player))
                        continue;

                    var inv = player.PlayerInventory;
                    if (inv == null)
                        continue;

                    // Mutate authoritative inventory.
                    PL_ClearInventoryLayout(inv);

                    // Broadcast this player's cleared inventory to all clients.
                    if (InvRetrieveSendMethod != null)
                    {
                        try { InvRetrieveSendMethod.Invoke(null, new object[] { local, player, false }); }
                        catch { /* Swallow.*/ }
                    }

                    cleared++;
                }

                SendLog($"Cleared inventory for {cleared} player(s).");
                return;
            }

            bool isSelf = target.IsLocal;

            if (!(target.Tag is Player targetPlayer))
                return;

            var inv2 = targetPlayer.PlayerInventory;
            if (inv2 == null)
                return;

            // Mutate the authoritative inventory object.
            PL_ClearInventoryLayout(inv2);

            if (!isSelf)
            {
                // Broadcast the cleared inventory to all clients for this player.
                if (InvRetrieveSendMethod != null)
                {
                    try
                    {
                        InvRetrieveSendMethod.Invoke(null, new object[] { local, targetPlayer, false });
                        SendLog($"Cleared inventory for player '{target.Gamertag}'.");
                    }
                    catch (Exception ex)
                    {
                        SendLog($"ClearSelectedInventory failed: {ex}.");
                    }
                }
            }
            else
            {
                // Push to host via the normal inventory store path.
                UploadInventoryToServer();
                SendLog("Cleared local inventory.");
            }
        }

        /// <summary>
        /// Clears all trays and backpack entries for the given PlayerInventory.
        /// Does NOT do any network sync by itself.
        /// </summary>
        static void PL_ClearInventoryLayout(PlayerInventory inv)
        {
            if (inv == null)
                return;

            // Clear hotbars (all trays for this inventory).
            var trays = inv.TrayManager;
            if (trays?.Trays != null)
            {
                int t0 = trays.Trays.GetLength(0);
                int t1 = trays.Trays.GetLength(1);

                for (int t = 0; t < t0; t++)
                {
                    for (int s = 0; s < t1; s++)
                    {
                        trays.Trays[t, s] = null;
                    }
                }
            }

            // Clear backpack.
            if (inv.Inventory != null)
            {
                for (int i = 0; i < inv.Inventory.Length; i++)
                {
                    inv.Inventory[i] = null;
                }
            }
        }
        #endregion

        #region Save Slots (Local Inventory Snapshots + Persistence)

        /// <summary>
        /// Per-item snapshot for the Player tab save slots.
        /// </summary>
        sealed class PL_SlotItem
        {
            public InventoryItemIDs Id;
            public int Stack;
            public float Health;
            public int? Clip;
        }

        /// <summary>
        /// One snapshot = trays (2x8) + backpack array.
        /// </summary>
        sealed class PL_SlotSnapshot
        {
            public PL_SlotItem[,] Trays = new PL_SlotItem[2, 8];
            public PL_SlotItem[] Bag;
        }

        // Number of slots exposed in the Player tab.
        const int PL_SAVE_SLOT_COUNT    = 15;
        const int PL_SLOT_STATE_VERSION = 1;

        static readonly Dictionary<int, PL_SlotSnapshot> _plSaveSlots =
            new Dictionary<int, PL_SlotSnapshot>();

        // INI path: <Game>\!Mods\CastleWallsMk2\CastleWallsMk2.UserData.ini
        static string PL_SlotDir  => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "CastleWallsMk2");
        static string PL_SlotPath => Path.Combine(PL_SlotDir, "CastleWallsMk2.UserData.ini");

        // Guard so we only hit the disk once per process.
        static bool _plSlotsLoadedOnce;

        /// <summary>
        /// Capture a live InventoryItem into a serializable PL_SlotItem.
        /// </summary>
        static PL_SlotItem PL_CaptureItem(InventoryItem it)
        {
            if (it == null || it.ItemClass == null)
                return null;

            var d = new PL_SlotItem
            {
                Id     = it.ItemClass.ID,
                Stack  = it.StackCount,
                Health = it.ItemHealthLevel
            };

            if (it is GunInventoryItem gi)
                d.Clip = gi.RoundsInClip;

            return d;
        }

        /// <summary>
        /// Rebuild an InventoryItem from a PL_SlotItem using the same clamping rules
        /// as the editor (CreateClampedItem + clip clamp).
        /// </summary>
        static InventoryItem PL_RebuildItem(PL_SlotItem d)
        {
            if (d == null)
                return null;

            var it = CreateClampedItem(d.Id, d.Stack, d.Health);
            if (it is GunInventoryItem gi && d.Clip.HasValue)
            {
                int clipMax = Math.Max(0, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);
                gi.RoundsInClip = Math.Max(0, Math.Min(clipMax, d.Clip.Value));
            }
            return it;
        }

        /// <summary>
        /// Save current local inventory into the given slot index (in-memory + disk).
        /// </summary>
        static void PL_SaveSlot(int index)
        {
            if (Inv == null || index < 0 || index >= PL_SAVE_SLOT_COUNT)
                return;

            var snap = new PL_SlotSnapshot
            {
                Bag = Inv.Inventory != null
                    ? new PL_SlotItem[Inv.Inventory.Length]
                    : Array.Empty<PL_SlotItem>()
            };

            // Trays (2 x 8).
            if (Trays?.Trays != null)
            {
                int t0 = Math.Min(2, Trays.Trays.GetLength(0));
                int t1 = Math.Min(8, Trays.Trays.GetLength(1));

                for (int t = 0; t < t0; t++)
                    for (int s = 0; s < t1; s++)
                        snap.Trays[t, s] = PL_CaptureItem(GetTrayItem(t, s));
            }

            // Backpack.
            if (Inv.Inventory != null)
            {
                for (int i = 0; i < Inv.Inventory.Length; i++)
                    snap.Bag[i] = PL_CaptureItem(GetBackpackItem(i));
            }

            _plSaveSlots[index] = snap;
            SendLog($"Saved inventory snapshot to slot {index + 1}.");

            PL_SaveAllSlots();
        }

        /// <summary>
        /// Load the slot index into local inventory (replace trays + backpack) and upload to server.
        /// </summary>
        static void PL_LoadSlot(int index)
        {
            if (Inv == null || index < 0 || index >= PL_SAVE_SLOT_COUNT)
                return;

            if (!_plSaveSlots.TryGetValue(index, out var snap) || snap == null)
            {
                SendLog($"Slot {index + 1} is empty.");
                return;
            }

            // Trays.
            if (Trays?.Trays != null && snap.Trays != null)
            {
                int t0 = Math.Min(2, Trays.Trays.GetLength(0));
                int t1 = Math.Min(8, Trays.Trays.GetLength(1));

                for (int t = 0; t < t0; t++)
                {
                    for (int s = 0; s < t1; s++)
                    {
                        var d = snap.Trays[t, s];
                        SetTrayItem(t, s, PL_RebuildItem(d));
                    }
                }
            }

            // Backpack.
            if (Inv.Inventory != null)
            {
                int max = Math.Min(Inv.Inventory.Length, snap.Bag?.Length ?? 0);

                // Fill from snapshot.
                for (int i = 0; i < max; i++)
                    SetBackpackItem(i, PL_RebuildItem(snap.Bag[i]));

                // Clear any extra slots if the snapshot bag was smaller.
                for (int i = max; i < Inv.Inventory.Length; i++)
                    SetBackpackItem(i, null);
            }

            // Sync to server / host using your existing helper.
            UploadInventoryToServer();
            SendLog($"Loaded inventory snapshot from slot {index + 1}.");
        }

        /// <summary>
        /// Clear snapshot in slot index (does not touch live inventory) + save file.
        /// </summary>
        static void PL_ClearSlot(int index)
        {
            if (index < 0 || index >= PL_SAVE_SLOT_COUNT)
                return;

            if (_plSaveSlots.Remove(index))
            {
                SendLog($"Cleared inventory snapshot in slot {index + 1}.");
                PL_SaveAllSlots();
            }
        }

        /// <summary>
        /// Encode one item as "id,stack,health[,clip]" or empty for null.
        /// </summary>
        static string PL_EncodeItem(PL_SlotItem d)
        {
            if (d == null) return string.Empty;

            var h = d.Health.ToString("0.###", CultureInfo.InvariantCulture);
            return d.Clip.HasValue
                ? $"{(int)d.Id},{d.Stack},{h},{d.Clip.Value}"
                : $"{(int)d.Id},{d.Stack},{h}";
        }

        /// <summary>
        /// Parse "id,stack,health[,clip]" into PL_SlotItem (null on malformed).
        /// </summary>
        static PL_SlotItem PL_ParseItem(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var p = raw.Split(',');
            if (p.Length < 3) return null;

            if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return null;

            if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stack))
                stack = 1;
            stack = Math.Max(0, stack);

            if (!float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float health))
                health = 1f;

            int? clip = null;
            if (p.Length >= 4 &&
                int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
            {
                clip = c;
            }

            return new PL_SlotItem
            {
                Id = (InventoryItemIDs)id,
                Stack = stack,
                Health = health,
                Clip = clip
            };
        }

        /// <summary>
        /// Load all slot snapshots from the INI once per process.
        /// Safe to call every frame; does nothing after first successful load.
        /// </summary>
        static void PL_LoadSlotsOnce()
        {
            if (_plSlotsLoadedOnce)
                return;

            _plSlotsLoadedOnce = true;

            try
            {
                if (!File.Exists(PL_SlotPath))
                    return;

                var ini = SimpleIni.Load(PL_SlotPath);

                int declaredCount = 0;
                try
                {
                    declaredCount = ini.GetInt("PlayerSlots", "Count", PL_SAVE_SLOT_COUNT);
                }
                catch
                {
                    declaredCount = PL_SAVE_SLOT_COUNT;
                }

                int upTo = Math.Min(PL_SAVE_SLOT_COUNT, Math.Max(0, declaredCount));

                for (int i = 0; i < upTo; i++)
                {
                    string sec = $"PlayerSlot{i}";

                    // If BagLen is missing, assume slot section doesn't exist -> skip.
                    int bagLen;
                    try
                    {
                        bagLen = ini.GetInt(sec, "BagLen", -1);
                    }
                    catch
                    {
                        bagLen = -1;
                    }

                    if (bagLen < 0)
                        continue;

                    var snap = new PL_SlotSnapshot
                    {
                        Trays = new PL_SlotItem[2, 8],
                        Bag = new PL_SlotItem[Math.Max(0, bagLen)]
                    };

                    // Trays.
                    for (int t = 0; t < 2; t++)
                    {
                        for (int s = 0; s < 8; s++)
                        {
                            var raw = ini.GetString(sec, $"Tray{t}{s}", "");
                            snap.Trays[t, s] = PL_ParseItem(raw);
                        }
                    }

                    // Bag.
                    for (int b = 0; b < snap.Bag.Length; b++)
                    {
                        var raw = ini.GetString(sec, $"Bag{b}", "");
                        snap.Bag[b] = PL_ParseItem(raw);
                    }

                    // Only keep non-empty slots (at least one non-null item).
                    bool any = false;

                    for (int t = 0; t < 2 && !any; t++)
                        for (int s = 0; s < 8 && !any; s++)
                            if (snap.Trays[t, s] != null) any = true;

                    for (int b = 0; b < snap.Bag.Length && !any; b++)
                        if (snap.Bag[b] != null) any = true;

                    if (any)
                        _plSaveSlots[i] = snap;
                }
            }
            catch (Exception ex)
            {
                Log($"[PlayerTab] PL_LoadSlotsOnce failed: {ex}.");
            }
        }

        /// <summary>
        /// Write all current slots out to CastleWallsMk2.UserData.ini.
        /// </summary>
        static void PL_SaveAllSlots()
        {
            try
            {
                if (!Directory.Exists(PL_SlotDir))
                    Directory.CreateDirectory(PL_SlotDir);

                var sb = new StringBuilder(4096);
                sb.AppendLine("; CastleWallsMk2 - Player tab save slots (auto-generated).");
                sb.AppendLine("; Delete this file to reset saved slot state.");
                sb.AppendLine();

                sb.AppendLine("[State]");
                sb.AppendLine($"Version={PL_SLOT_STATE_VERSION}");
                sb.AppendLine();

                sb.AppendLine("[PlayerSlots]");
                sb.AppendLine($"Count={PL_SAVE_SLOT_COUNT}");
                sb.AppendLine();

                foreach (var kv in _plSaveSlots)
                {
                    int idx = kv.Key;
                    if (idx < 0 || idx >= PL_SAVE_SLOT_COUNT)
                        continue;

                    var snap = kv.Value;
                    if (snap == null)
                        continue;

                    string sec = $"PlayerSlot{idx}";
                    sb.AppendLine($"[{sec}]");

                    // Trays 2 x 8.
                    for (int t = 0; t < 2; t++)
                    {
                        for (int s = 0; s < 8; s++)
                        {
                            var d = snap.Trays[t, s];
                            sb.AppendLine($"Tray{t}{s}={PL_EncodeItem(d)}");
                        }
                    }

                    // Bag.
                    int bl = snap.Bag?.Length ?? 0;
                    sb.AppendLine($"BagLen={bl}");
                    for (int b = 0; b < bl; b++)
                        sb.AppendLine($"Bag{b}={PL_EncodeItem(snap.Bag[b])}");

                    sb.AppendLine();
                }

                File.WriteAllText(PL_SlotPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log($"[PlayerTab] PL_SaveAllSlots failed: {ex}.");
            }
        }
        #endregion

        #endregion

        #region Tab: World

        /// <summary>
        /// WORLD EDITOR TAB
        ///
        /// Purpose:
        ///   - Inspect + edit the live WorldInfo for the currently loaded world.
        ///   - Provide an explicit Apply / Save-to-disk flow (so typing in a textbox doesn't spam disk writes).
        ///
        /// Key UX rules:
        ///   - "Apply" pushes NON-seed edits (and any safe/private fields) to the live WorldInfo.
        ///   - Seed has its own explicit "Apply Seed (SHIFT)" and "Rebuild Terrain (SHIFT)" controls.
        ///   - Dangerous edits (like WorldID) are gated behind SHIFT.
        ///   - After any apply, WI_RecomputeDirtyFromLive(wi) is the source of truth for the main Apply label.
        ///
        /// Notes:
        ///   - This edits CastleMinerZGame.Instance.CurrentWorld directly (host/single-player authoritative).
        ///   - In multiplayer as a non-host, changes may be local-only unless you also add a sync message.
        ///   - Save uses WorldInfo.SaveToStorage(...) with a best-effort SignedInGamer lookup.
        /// </summary>
        public static void DrawWorldEditorTab()
        {
            #region Bind & Session State

            var game = CastleMinerZGame.Instance;
            var wi   = game?.CurrentWorld;

            if (wi == null)
            {
                ImGui.TextDisabled("No world is currently loaded.");
                return;
            }

            // Re-sync editor fields when switching worlds.
            if (!_wiSyncedOnce || !ReferenceEquals(_wiBoundWorldRef, wi))
                WI_ReadFromWorld(wi);

            bool inSession = game?.CurrentNetworkSession != null;
            bool amHost    = false;
            try { amHost = game?.MyNetworkGamer?.IsHost ?? false; } catch { }

            // SHIFT = "dangerous confirmation" modifier (seed/worldid/terrain rebuild, etc).
            bool shift = ImGui.GetIO().KeyShift;

            ImGui.TextUnformatted("Edits apply to the live WorldInfo object. Use Save to persist to world.info.");
            if (inSession && !amHost)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.25f, 1f),
                    "NOTE: You are not host. These edits may not affect the actual server world."); // ' Unless you add host-side syncing'.
            }

            ImGui.Spacing();

            #endregion

            #region Top Action Row (Reload / Apply / Save)

            // Top action row.
            if (ImGui.Button("Reload"))
            {
                WI_ReadFromWorld(wi);
                _wiStatus = "Reloaded from live world.";
            }

            ImGui.SameLine();

            // Main apply reflects _wiDirty (which must be recomputed after any live changes).
            string applyLabel = _wiDirty ? "Apply *" : "Apply";
            if (ImGui.Button(applyLabel))
            {
                // Applies all "normal" edits + safe-ish private fields.
                // Seed is intentionally NOT committed here; it remains "pending" until its own button is used.
                WI_ApplyToWorld(wi, allowDangerous: shift);

                // Recompute dirty after applying live changes.
                // This is what keeps the Apply label accurate (and prevents "stuck" Apply * after seed-only actions).
                WI_RecomputeDirtyFromLive(wi);

                if (!_wiDirty)
                    _wiStatus = "Applied to live world.";
                else if (_wiSeed != _wiSeedOriginal)
                    _wiStatus = "Applied (seed pending: use 'Apply Seed (SHIFT)' or 'Rebuild Terrain (SHIFT)').";
                else
                    _wiStatus = "Applied (some edits still pending; hold SHIFT for dangerous changes or fix invalid fields).";
            }

            ImGui.SameLine();

            if (ImGui.Button("Save world.info"))
            {
                bool fully = WI_ApplyToWorld(wi, allowDangerous: shift);
                _wiDirty = !fully;
                if (fully)
                    WI_SaveToDisk(wi);
                else
                    _wiStatus = "Save blocked: hold SHIFT to commit pending dangerous edits first.";
            }

            if (!string.IsNullOrEmpty(_wiStatus))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(_wiStatus);
            }

            ImGui.Separator();

            #endregion

            #region Section Layout Constants (shared "Player tab" style)

            float headerUnit = ImGui.GetFrameHeightWithSpacing(); // Header + spacing.
            float minBodyH = 70f;                                 // Safety floor for tiny windows.

            #endregion

            #region Section: Identity (Idx 0)

            // ---------- Identity (idx 0) ----------
            if (_secWI_Identity)
            {
                bool expanded = WI_Header("Identity", ref _secWI_Identity, ref _expWI_Identity);
                if (expanded)
                {
                    int headersBelow  = WI_VisibleHeadersBelow(0);
                    int expFromHere   = WI_ExpandedCountFrom(0);
                    bool lastExpanded = (expFromHere == 1);

                    float avail         = ImGui.GetContentRegionAvail().Y;
                    float reservedBelow = headersBelow * headerUnit;

                    float childH = lastExpanded
                        ? (headersBelow == 0 ? 0f : Math.Max(minBodyH, avail - reservedBelow))
                        : Math.Max(minBodyH, (avail - reservedBelow) / expFromHere);

                    if (WI_BeginSectionChild("wi_sec_identity", childH))
                    {
                        if (ImGui.BeginTable("wi_identity", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);

                            #region Identity Fields

                            // Name (editable).
                            EE_FormRow("Name", () =>
                            {
                                string v = _wiName ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_name", ref v, 128))
                                {
                                    _wiName = v;
                                    _wiDirty = true;
                                }
                            }, labelW: 180f);

                            // WorldID (GUID).
                            // NOTE: Parsed + validated in WI_ApplyToWorld (dangerous gated by SHIFT).
                            EE_FormRow("WorldID (GUID)", () =>
                            {
                                string s = _wiWorldIdText ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_worldid", ref s, 64))
                                {
                                    _wiWorldIdText = s;
                                    _wiDirty = true;
                                }

                                // Inline validation hint.
                                if (!Guid.TryParse(_wiWorldIdText, out _))
                                {
                                    ImGui.SameLine();
                                    ImGui.TextDisabled(" (invalid)");
                                }
                            }, 180f);

                            // Seed (separate apply/rebuild buttons)
                            // NOTE:
                            //   - Seed is intentionally "pending" until Apply Seed / Rebuild Terrain is used.
                            //   - The "(pending)" text is rendered inline; spacing/width is reserved so it has room to appear.
                            EE_FormRow("Seed", () =>
                            {
                                int v = _wiSeed;

                                // Lay out: [InputInt..............][Apply Seed][Rebuild Terrain].
                                float totalW = ImGui.GetContentRegionAvail().X;
                                float spacing = ImGui.GetStyle().ItemSpacing.X;

                                float btn1W = ImGui.CalcTextSize("Apply Seed").X + ImGui.GetStyle().FramePadding.X * 2f + 6f;
                                float btn2W = ImGui.CalcTextSize("Rebuild Terrain").X + ImGui.GetStyle().FramePadding.X * 2f + 6f;

                                bool seedPending = (_wiSeed != _wiSeedOriginal);

                                float pendingW = seedPending
                                    ? (ImGui.CalcTextSize(" (pending)").X + spacing)
                                    : 0f;

                                float inputW = Math.Max(90f, totalW - (btn1W + btn2W + spacing * 2f + pendingW));

                                ImGui.SetNextItemWidth(inputW);
                                if (ImGui.InputInt("##wi_seed", ref v))
                                {
                                    _wiSeed = v;
                                    _wiDirty = true;
                                }

                                // Host-only in session (optional safety gate).
                                bool canUse = (!inSession || amHost);

                                ImGui.SameLine();

                                if (!canUse) ImGui.BeginDisabled(true);
                                if (ImGui.Button("Apply Seed##wi_seed_apply"))
                                    WI_ApplySeedOnly(wi, shift);
                                if (!canUse) ImGui.EndDisabled();

                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(canUse
                                        ? "Commit the seed to WorldInfo.\nHold SHIFT to confirm."
                                        : "Host only (seed changes should be done by the host).");
                                }

                                ImGui.SameLine();

                                if (!canUse) ImGui.BeginDisabled(true);
                                if (ImGui.Button("Rebuild Terrain##wi_seed_rebuild"))
                                    WI_RebuildTerrainFromSeed(wi, shift, amHost);
                                if (!canUse) ImGui.EndDisabled();

                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(canUse
                                        ? "Rebuild terrain around you (AsyncInit).\nIf seed is pending, it will be applied first.\nHold SHIFT to confirm."
                                        : "Host only (terrain rebuild should be done by the host).");
                                }

                                if (seedPending)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextDisabled(" (pending)");
                                }
                            }, 180f);

                            // Owner tag.
                            EE_FormRow("Owner GamerTag", () =>
                            {
                                string v = _wiOwnerGamerTag ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_owner", ref v, 64))
                                {
                                    _wiOwnerGamerTag = v;
                                    _wiDirty = true;
                                }
                            }, 180f);

                            // Creator tag.
                            EE_FormRow("Creator GamerTag", () =>
                            {
                                string v = _wiCreatorGamerTag ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_creator", ref v, 64))
                                {
                                    _wiCreatorGamerTag = v;
                                    _wiDirty = true;
                                }
                            }, 180f);

                            // Created date (Now button + editable text).
                            EE_FormRow("Created (UTC)", () =>
                            {
                                // Editable text (u-format is easiest).
                                string t = _wiCreatedDateText ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_created", ref t, 32))
                                {
                                    _wiCreatedDateText = t;
                                    _wiDirty = true;
                                }

                                ImGui.SameLine();
                                if (ImGui.Button("Now##wi_created_now"))
                                {
                                    _wiCreatedDate = DateTime.UtcNow;
                                    _wiCreatedDateText = _wiCreatedDate.ToString("u");
                                    _wiDirty = true;
                                }

                                // Parse hint (optional).
                                if (!TryParseUtc(_wiCreatedDateText ?? "", out _))
                                {
                                    ImGui.SameLine();
                                    ImGui.TextDisabled(" (unparsed)");
                                }
                            }, 180f);

                            // Last played (editable via buttons).
                            EE_FormRow("Last Played", () =>
                            {
                                ImGui.TextUnformatted(_wiLastPlayedDate.ToString("u"));

                                ImGui.SameLine();
                                if (ImGui.Button("Now##wi_lp_now"))
                                {
                                    _wiLastPlayedDate = DateTime.UtcNow;
                                    _wiDirty = true;
                                }

                                ImGui.SameLine();
                                if (ImGui.Button("=Created##wi_lp_created"))
                                {
                                    _wiLastPlayedDate = wi.CreatedDate;
                                    _wiDirty = true;
                                }
                            }, 180f);

                            #endregion

                            ImGui.EndTable();
                        }
                    }
                    WI_EndSectionChild();
                }
            }
            #endregion

            #region Section: World & Gameplay (Idx 1)

            // ---------- World / Gameplay (idx 1) ----------
            if (_secWI_World)
            {
                bool expanded = WI_Header("World & Gameplay", ref _secWI_World, ref _expWI_World);
                if (expanded)
                {
                    int headersBelow = WI_VisibleHeadersBelow(1);
                    int expFromHere = WI_ExpandedCountFrom(1);
                    bool lastExpanded = (expFromHere == 1);

                    float avail = ImGui.GetContentRegionAvail().Y;
                    float reservedBelow = headersBelow * headerUnit;

                    float childH = lastExpanded
                        ? (headersBelow == 0 ? 0f : Math.Max(minBodyH, avail - reservedBelow))
                        : Math.Max(minBodyH, (avail - reservedBelow) / expFromHere);

                    if (WI_BeginSectionChild("wi_sec_world", childH))
                    {
                        if (ImGui.BeginTable("wi_worldgame", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);

                            // Terrain version (unsafe-ish while loaded; but it *is* in WorldInfo).
                            EE_FormRow("Terrain Version", () =>
                            {
                                WI_BuildTerrainOptionsOnce();

                                string preview = _wiTerrainVersion.ToString();
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.BeginCombo("##wi_terrain", preview))
                                {
                                    for (int i = 0; i < _wiTerrainOptions.Length; i++)
                                    {
                                        var opt = _wiTerrainOptions[i];
                                        bool sel = opt.Equals(_wiTerrainVersion);
                                        if (ImGui.Selectable(_wiTerrainLabels[i], sel))
                                        {
                                            _wiTerrainVersion = opt;
                                            _wiDirty = true;
                                        }
                                        if (sel) ImGui.SetItemDefaultFocus();
                                    }
                                    ImGui.EndCombo();
                                }
                            }, 180f);

                            // Last position.
                            EE_FormRow("Last Position (XYZ)", () =>
                            {
                                var v = _wiLastPos3;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##wi_lastpos", ref v))
                                {
                                    _wiLastPos3 = v;
                                    _wiDirty = true;
                                }

                                // Quick button to set to your current position (nice for "spawn where I am" style).
                                ImGui.SameLine();
                                if (ImGui.Button("=Me##wi_lastpos_me"))
                                {
                                    try
                                    {
                                        var me = game?.LocalPlayer;
                                        if (me != null)
                                        {
                                            _wiLastPos3 = ToSysVec(me.LocalPosition);
                                            _wiDirty = true;
                                        }
                                    }
                                    catch { }
                                }
                            }, 180f);

                            // Infinite resources.
                            EE_FormRow("Infinite Resources", () =>
                            {
                                bool v = _wiInfiniteResourceMode;
                                if (ImGui.Checkbox("##wi_inf", ref v))
                                {
                                    _wiInfiniteResourceMode = v;
                                    _wiDirty = true;
                                }
                            }, 180f);

                            // Hell boss tracking.
                            EE_FormRow("HellBossesSpawned", () =>
                            {
                                int v = _wiHellBossesSpawned;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputInt("##wi_hb_spawned", ref v))
                                {
                                    _wiHellBossesSpawned = Math.Max(0, v);
                                    _wiDirty = true;
                                }
                            }, 180f);

                            EE_FormRow("MaxHellBossSpawns", () =>
                            {
                                int v = _wiMaxHellBossSpawns;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputInt("##wi_hb_max", ref v))
                                {
                                    _wiMaxHellBossSpawns = Math.Max(0, v);
                                    _wiDirty = true;
                                }
                            }, 180f);

                            ImGui.EndTable();
                        }
                    }
                    WI_EndSectionChild();
                }
            }
            #endregion

            #region Section: Server Settings (Idx 2)

            // ---------- Server (idx 2) ----------
            if (_secWI_Server)
            {
                bool expanded = WI_Header("Server Settings", ref _secWI_Server, ref _expWI_Server);
                if (expanded)
                {
                    int headersBelow  = WI_VisibleHeadersBelow(2);
                    int expFromHere   = WI_ExpandedCountFrom(2);
                    bool lastExpanded = (expFromHere == 1);

                    float avail         = ImGui.GetContentRegionAvail().Y;
                    float reservedBelow = headersBelow * headerUnit;

                    float childH = lastExpanded
                        ? (headersBelow == 0 ? 0f : Math.Max(minBodyH, avail - reservedBelow))
                        : Math.Max(minBodyH, (avail - reservedBelow) / expFromHere);

                    if (WI_BeginSectionChild("wi_sec_server", childH))
                    {
                        if (ImGui.BeginTable("wi_server", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);

                            EE_FormRow("Server Message", () =>
                            {
                                string v = _wiServerMessage ?? "";
                                float h  = ImGui.GetTextLineHeightWithSpacing() * 8f;

                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputTextMultiline(
                                        "##wi_srvmsg",
                                        ref v,
                                        2048,
                                        new Vector2(-1, h),
                                        ImGuiInputTextFlags.None))
                                {
                                    _wiServerMessage = v;
                                    _wiDirty         = true;
                                }
                            }, 180f);

                            EE_FormRow("Server Password", () =>
                            {
                                string v = _wiServerPassword ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText("##wi_srvpw", ref v, 64))
                                {
                                    _wiServerPassword = v;
                                    _wiDirty          = true;
                                }
                            }, 180f);

                            ImGui.EndTable();
                        }
                    }
                    WI_EndSectionChild();
                }
            }
            #endregion

            #region Section: Entities (idx 3, Bottom-Most Fills Remainder)

            // ---------- Entities (idx 3, bottom-most fills remainder) ----------
            if (_secWI_Entities)
            {
                bool expanded = WI_Header("World Entities (Crates / Doors / Spawners)", ref _secWI_Entities, ref _expWI_Entities);
                if (expanded)
                {
                    // Bottom-most: fill remainder by using height 0f
                    if (WI_BeginSectionChild("wi_sec_entities", 0f))
                    {
                        ImGui.TextUnformatted($"Crates:   {wi.Crates?.Count ?? 0}");
                        ImGui.TextUnformatted($"Doors:    {wi.Doors?.Count ?? 0}");
                        ImGui.TextUnformatted($"Spawners: {wi.Spawners?.Count ?? 0}");

                        ImGui.Spacing();
                        ImGui.TextDisabled("Hold SHIFT while clicking to clear (danger).");

                        if (ImGui.Button("Clear Crates##wi_clear_crates"))
                        {
                            if (shift) { wi.Crates?.Clear(); _wiStatus = "Cleared Crates."; }
                            else _wiStatus = "Hold SHIFT to confirm (Crates).";
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Clear Doors##wi_clear_doors"))
                        {
                            if (shift) { wi.Doors?.Clear(); _wiStatus = "Cleared Doors."; }
                            else _wiStatus = "Hold SHIFT to confirm (Doors).";
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Clear Spawners##wi_clear_spawners"))
                        {
                            if (shift) { wi.Spawners?.Clear(); _wiStatus = "Cleared Spawners."; }
                            else _wiStatus = "Hold SHIFT to confirm (Spawners).";
                        }
                    }
                    WI_EndSectionChild();
                }
            }
            #endregion

            #region Section: Session (Not Saved) (Idx 4)

            if (_secWI_Session)
            {
                bool expanded = WI_Header("Session (Join Policy / PvP)", ref _secWI_Session, ref _expWI_Session);
                if (expanded)
                {
                    // Bottom-most: fill remainder by using height 0f
                    if (WI_BeginSectionChild("wi_sec_session", 0f))
                    {
                        ImGui.TextDisabled("These affect the current session only. They are NOT stored in world.info and will reset on reload/rehhost.");
                        ImGui.Spacing();

                        if (ImGui.BeginTable("wi_session_opts", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);

                            bool canUse = (inSession && amHost);

                            // Join policy.
                            EE_FormRow("Join Game Policy", () =>
                            {
                                var v = _wiJoinGamePolicy;

                                if (!canUse) ImGui.BeginDisabled(true);

                                string preview = v.ToString();
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.BeginCombo("##wi_joinpolicy", preview))
                                {
                                    foreach (JoinGamePolicy opt in Enum.GetValues(typeof(JoinGamePolicy))
                                        .Cast<JoinGamePolicy>().Where(p => p != JoinGamePolicy.COUNT))
                                    {
                                        bool sel = (opt == v);
                                        if (ImGui.Selectable(opt.ToString(), sel))
                                        {
                                            _wiJoinGamePolicy = opt;
                                            _wiDirty = true;
                                        }
                                        if (sel) ImGui.SetItemDefaultFocus();
                                    }
                                    ImGui.EndCombo();
                                }

                                if (!canUse) ImGui.EndDisabled();

                                bool pending = false;
                                try { pending = (game != null && game.JoinGamePolicy != _wiJoinGamePolicy); } catch { }
                                if (pending)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextDisabled(" (pending)");
                                }

                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip(canUse ? "Applies to the current online session only.\nNot saved on reload." : "Host-only (online session required).");
                            }, 180f);

                            // PvP state.
                            EE_FormRow("PvP State", () =>
                            {
                                var v = _wiPvpState;

                                if (!canUse) ImGui.BeginDisabled(true);

                                string preview = v.ToString();
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.BeginCombo("##wi_pvp", preview))
                                {
                                    foreach (CastleMinerZGame.PVPEnum opt in Enum.GetValues(typeof(CastleMinerZGame.PVPEnum)))
                                    {
                                        bool sel = (opt == v);
                                        if (ImGui.Selectable(opt.ToString(), sel))
                                        {
                                            _wiPvpState = opt;
                                            _wiDirty = true;
                                        }
                                        if (sel) ImGui.SetItemDefaultFocus();
                                    }
                                    ImGui.EndCombo();
                                }

                                if (!canUse) ImGui.EndDisabled();

                                bool pending = false;
                                try { pending = (game != null && game.PVPState != _wiPvpState); } catch { }
                                if (pending)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextDisabled(" (pending)");
                                }

                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip(canUse ? "Applies to the current online session only.\nNot saved on reload." : "Host-only (online session required).");
                            }, 180f);

                            ImGui.EndTable();
                        }
                    }
                    WI_EndSectionChild();
                }
            }
            #endregion
        }

        #region World Editor: State & Helpers

        // -----------------------------------------------------------------------------
        // UI binding state
        // -----------------------------------------------------------------------------

        static bool      _wiSyncedOnce;
        static WorldInfo _wiBoundWorldRef;
        static bool      _wiDirty;
        static string    _wiStatus = "";

        // -----------------------------------------------------------------------------
        // Editable fields (copied from live -> UI, then applied back on demand).
        // -----------------------------------------------------------------------------

        static string                   _wiName           = "";
        static string                   _wiServerMessage  = "";
        static string                   _wiServerPassword = "";
        static bool                     _wiInfiniteResourceMode;
        static int                      _wiHellBossesSpawned;
        static int                      _wiMaxHellBossSpawns;
        static DateTime                 _wiLastPlayedDate;
        static System.Numerics.Vector3  _wiLastPos3;
        static WorldTypeIDs             _wiTerrainVersion;

        // Identity (editable private fields).
        static string                   _wiWorldIdText     = "";
        static Guid                     _wiWorldIdOriginal;
        static int                      _wiSeed;
        static int                      _wiSeedOriginal;
        static string                   _wiOwnerGamerTag   = "";
        static string                   _wiCreatorGamerTag = "";
        static DateTime                 _wiCreatedDate;
        static string                   _wiCreatedDateText = "";

        // Session-only (NOT saved to world.info; resets on reload/rehhost).
        static JoinGamePolicy           _wiJoinGamePolicy;
        static CastleMinerZGame.PVPEnum _wiPvpState;

        // -----------------------------------------------------------------------------
        // Terrain combo cache
        // -----------------------------------------------------------------------------

        static bool           _wiTerrainBuilt;
        static WorldTypeIDs[] _wiTerrainOptions = Array.Empty<WorldTypeIDs>();
        static string[]       _wiTerrainLabels  = Array.Empty<string>();

        static void WI_BuildTerrainOptionsOnce()
        {
            if (_wiTerrainBuilt) return;
            _wiTerrainBuilt = true;

            var vals = Enum.GetValues(typeof(WorldTypeIDs)).Cast<WorldTypeIDs>().ToArray();
            _wiTerrainOptions = vals;
            _wiTerrainLabels  = vals.Select(v => v.ToString()).ToArray();
        }

        /// <summary>
        /// Copy live WorldInfo values into the UI model.
        /// This is the ONLY place that resets "original" values (seed/worldid) and clears dirty.
        /// </summary>
        static void WI_ReadFromWorld(WorldInfo wi)
        {
            _wiSyncedOnce           = true;
            _wiBoundWorldRef        = wi;
            _wiDirty                = false;

            var wid                 = WI_GetWorldID(wi);
            _wiWorldIdOriginal      = wid;
            _wiWorldIdText          = wid.ToString();
            _wiSeed                 = WI_GetSeed(wi);
            _wiSeedOriginal         = _wiSeed;
            _wiOwnerGamerTag        = WI_GetOwnerTag(wi) ?? "";
            _wiCreatorGamerTag      = WI_GetCreatorTag(wi) ?? "";
            _wiCreatedDate          = WI_GetCreatedDate(wi);
            _wiCreatedDateText      = _wiCreatedDate.ToString("u"); // Editable textbox format.
            _wiName                 = wi.Name ?? "";
            _wiServerMessage        = wi.ServerMessage ?? "";
            _wiServerPassword       = wi.ServerPassword ?? "";
            _wiInfiniteResourceMode = wi.InfiniteResourceMode;
            _wiHellBossesSpawned    = wi.HellBossesSpawned;
            _wiMaxHellBossSpawns    = wi.MaxHellBossSpawns;
            _wiLastPlayedDate       = wi.LastPlayedDate;
            _wiLastPos3             = ToSysVec(wi.LastPosition);
            _wiTerrainVersion       = wi._terrainVersion;

            // Session-only live options (not persisted in world.info).
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game != null)
                {
                    _wiJoinGamePolicy = game.JoinGamePolicy;
                    _wiPvpState       = game.PVPState;
                }
            }
            catch { }

            _wiStatus = "";
        }

        /// <summary>
        /// Apply current UI model into the live WorldInfo.
        /// Returns "fully applied" (no pending dangerous / invalid fields).
        ///
        /// IMPORTANT:
        ///   - Seed is intentionally not applied here; it stays pending until seed buttons are used.
        ///   - WorldID is gated behind allowDangerous (SHIFT).
        /// </summary>
        static bool WI_ApplyToWorld(WorldInfo wi, bool allowDangerous)
        {
            bool pendingDangerous = false;

            // Only write what is actually settable/safe on WorldInfo.
            wi.Name                 = _wiName ?? "";
            wi.ServerMessage        = _wiServerMessage ?? "";
            wi.ServerPassword       = _wiServerPassword ?? "";
            wi.InfiniteResourceMode = _wiInfiniteResourceMode;
            wi.HellBossesSpawned    = Math.Max(0, _wiHellBossesSpawned);
            wi.MaxHellBossSpawns    = Math.Max(0, _wiMaxHellBossSpawns);
            wi.LastPlayedDate       = _wiLastPlayedDate;
            wi.LastPosition         = ToXnaVec(_wiLastPos3);
            wi._terrainVersion      = _wiTerrainVersion;

            // Keep host/session options in sync with WorldInfo when hosting.
            // Mirrors HostOptionsTab behavior (server message + password).
            WI_ApplyHostOptionsIfHosting();

            // --- Owner / Creator / CreatedDate (safe-ish) ---
            WI_SetOwnerTag(wi, _wiOwnerGamerTag ?? "");
            WI_SetCreatorTag(wi, _wiCreatorGamerTag ?? "");

            if (TryParseUtc(_wiCreatedDateText ?? "", out var createdUtc))
                _wiCreatedDate = createdUtc; // Keep UI model in sync.
            WI_SetCreatedDate(wi, _wiCreatedDate);

            // --- WorldID (dangerous, recommended gate) ---
            if (Guid.TryParse(_wiWorldIdText ?? "", out var newWid))
            {
                if (newWid != _wiWorldIdOriginal)
                {
                    if (allowDangerous)
                    {
                        WI_SetWorldID(wi, newWid);
                        _wiWorldIdOriginal = newWid;
                    }
                    else
                    {
                        pendingDangerous = true;
                        _wiStatus = "WorldID change pending: hold SHIFT while clicking Apply/Save to commit.";
                    }
                }
            }
            else
            {
                // Invalid GUID -> don't apply, keep dirty.
                pendingDangerous = true;
                _wiStatus = "WorldID invalid (expected a GUID). Change not applied.";
            }

            // --- Seed (dangerous, requested gate) ---
            // Note: Main Apply does NOT apply seed anymore; seed is handled by dedicated buttons next to the field.
            if (_wiSeed != _wiSeedOriginal)
            {
                pendingDangerous = true;
                if (string.IsNullOrEmpty(_wiStatus))
                    _wiStatus = "Seed pending: use 'Apply Seed (SHIFT)' or 'Rebuild Terrain (SHIFT)' next to Seed.";
            }

            // --- Session-only (NOT saved to world.info) ---
            // These setters are the "real" game path (they update session state when online/host).
            try
            {
                var game = CastleMinerZGame.Instance;

                bool canSessionApply = false;
                try
                {
                    canSessionApply =
                        (game?.CurrentNetworkSession != null) &&
                        (game?.MyNetworkGamer?.IsHost ?? false);
                }
                catch { }

                if (canSessionApply && game != null)
                {
                    var wantJoin = _wiJoinGamePolicy;
                    var wantPvp  = _wiPvpState;

                    bool joinChanged = (game.JoinGamePolicy != wantJoin);
                    bool pvpChanged  = (game.PVPState != wantPvp);

                    if (joinChanged || pvpChanged)
                    {
                        WI_ApplyJoinAndPvp(
                            game,
                            joinChanged  ? wantJoin : game.JoinGamePolicy, // Avoid redundant setter call.
                            pvpChanged   ? wantPvp  : game.PVPState,       // Avoid redundant setter call.
                            broadcastPvp : pvpChanged                      // Match HostOptionsTab: only broadcast on change.
                        );
                    }
                }
            }
            catch { }

            return !pendingDangerous;
        }

        static void WI_SaveToDisk(WorldInfo wi)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                var dev  = game?.SaveDevice;
                if (dev == null)
                {
                    _wiStatus = "Save failed: SaveDevice is null.";
                    return;
                }

                SignedInGamer sg = null;

                // Prefer local gamer's SignedInGamer (if available).
                try { sg = game?.MyNetworkGamer?.SignedInGamer; } catch { }

                // Fallback: First signed-in gamer.
                if (sg == null)
                {
                    try
                    {
                        foreach (var g in SignedInGamer.SignedInGamers)
                            if (g != null) { sg = g; break; }
                    }
                    catch { }
                }

                // Last-ditch: Screen.CurrentGamer might be a SignedInGamer in some builds.
                if (sg == null)
                {
                    try { sg = DNA.Drawing.UI.Screen.CurrentGamer as SignedInGamer; } catch { }
                }

                wi.SaveToStorage(sg, dev);

                try { dev.Flush(); } catch { } // Not always required / available.

                _wiStatus = "Saved to disk (world.info).";
            }
            catch (Exception ex)
            {
                _wiStatus = $"Save failed: {ex.Message}.";
            }
        }

        /// <summary>
        /// Requests a terrain rebuild around the local player using AsyncInit.
        /// Returns false when terrain isn't ready / available.
        /// </summary>
        static bool WI_RegenTerrainAroundMe(WorldInfo wi, bool amHost)
        {
            var game = CastleMinerZGame.Instance;
            var terrain = game?._terrain;
            if (terrain == null)
                return false;

            // Don't spam resets if it's already mid-reset/rebuild.
            if (!terrain.IsReady)
                return false;

            // Recreate builder so it picks up the new seed immediately.
            try { terrain._worldBuilder = wi.GetBuilder(); } catch { }

            // Re-init around our current position (without permanently altering LastPosition).
            Vector3 oldLast = wi.LastPosition;
            try
            {
                var me = game?.LocalPlayer;
                if (me != null)
                    wi.LastPosition = me.LocalPosition;

                terrain.AsyncInit(wi, host: amHost, callback: null);
                return true;
            }
            finally
            {
                wi.LastPosition = oldLast;
            }
        }

        /// <summary>
        /// Seed-only commit, gated behind SHIFT.
        /// Also recomputes dirty so the main Apply label stays accurate.
        /// </summary>
        static void WI_ApplySeedOnly(WorldInfo wi, bool shift)
        {
            if (!shift) { _wiStatus = "Hold SHIFT to apply seed."; return; }

            if (_wiSeed == _wiSeedOriginal)
            {
                _wiStatus = "Seed already applied.";
                return;
            }

            WI_SetSeed(wi, _wiSeed);
            _wiSeedOriginal = _wiSeed;

            // IMPORTANT: Refresh dirty so the main Apply label updates.
            WI_RecomputeDirtyFromLive(wi);

            _wiStatus = !_wiDirty
                ? $"Seed applied: {_wiSeed}."
                : $"Seed applied: {_wiSeed}. (Other edits still pending)";
        }

        /// <summary>
        /// Terrain rebuild button:
        ///   - SHIFT gated.
        ///   - If seed is pending, commits seed first.
        ///   - Requests AsyncInit rebuild and recomputes dirty.
        /// </summary>
        static void WI_RebuildTerrainFromSeed(WorldInfo wi, bool shift, bool amHost)
        {
            if (!shift) { _wiStatus = "Hold SHIFT to rebuild terrain."; return; }

            if (_wiSeed != _wiSeedOriginal)
            {
                WI_SetSeed(wi, _wiSeed);
                _wiSeedOriginal = _wiSeed;
            }

            bool ok = WI_RegenTerrainAroundMe(wi, amHost);

            WI_RecomputeDirtyFromLive(wi);

            _wiStatus = ok
                ? "Terrain rebuild requested (AsyncInit)."
                : "Terrain rebuild skipped: terrain not ready or unavailable.";
        }

        /// <summary>
        /// Recomputes whether the UI differs from the live WorldInfo.
        /// This is the authoritative source for the main "Apply *" label.
        /// </summary>
        static void WI_RecomputeDirtyFromLive(WorldInfo wi)
        {
            bool d = false;

            // Public fields we edit.
            if ((wi.Name ?? "")           != (_wiName ?? ""))                   d = true;
            if ((wi.ServerMessage ?? "")  != (_wiServerMessage ?? ""))          d = true;
            if ((wi.ServerPassword ?? "") != (_wiServerPassword ?? ""))         d = true;
            if (wi.InfiniteResourceMode   != _wiInfiniteResourceMode)           d = true;
            if (wi.HellBossesSpawned      != Math.Max(0, _wiHellBossesSpawned)) d = true;
            if (wi.MaxHellBossSpawns      != Math.Max(0, _wiMaxHellBossSpawns)) d = true;
            if (wi.LastPlayedDate         != _wiLastPlayedDate)                 d = true;
            if (wi._terrainVersion        != _wiTerrainVersion)                 d = true;

            // Last position (tolerance).
            var lp = ToSysVec(wi.LastPosition);
            var dp = lp - _wiLastPos3;
            if (dp.LengthSquared() > 0.0001f) d = true;

            // Private fields we edit.
            if (WI_GetSeed(wi) != _wiSeed) d = true;

            Guid widLive = WI_GetWorldID(wi);
            if (!Guid.TryParse(_wiWorldIdText ?? "", out var widUi) || widUi != widLive) d = true;

            if ((WI_GetOwnerTag(wi) ?? "") != (_wiOwnerGamerTag ?? "")) d = true;
            if ((WI_GetCreatorTag(wi) ?? "") != (_wiCreatorGamerTag ?? "")) d = true;

            // Created date: Compare UTC.
            var cdLive = WI_GetCreatedDate(wi);
            var cdUi   = _wiCreatedDate;
            if (cdLive.ToUniversalTime() != cdUi.ToUniversalTime()) d = true;

            // Session-only (not saved).
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game != null)
                {
                    if (game.JoinGamePolicy != _wiJoinGamePolicy) d = true;
                    if (game.PVPState       != _wiPvpState)       d = true;
                }
            }
            catch { }

            _wiDirty = d;
        }

        /// <summary>
        /// Mirrors the game's HostOptionsTab behavior for host/session-visible settings.
        /// - ServerMessage: Sets DNAGame.ServerMessage (which forwards to NetworkSession.ServerMessage).
        /// - Password:      Calls UpdateHostSession(...) and sets NetworkSession.Password.
        ///
        /// NOTE:
        ///   This only runs when you are the host and a session exists.
        /// </summary>
        static void WI_ApplyHostOptionsIfHosting()
        {
            var game    = CastleMinerZGame.Instance;
            var session = game?.CurrentNetworkSession;
            if (session == null)
                return;

            /*
            bool amHost  = false;
            try { amHost = game?.MyNetworkGamer?.IsHost ?? false; } catch { }
            if (!amHost)
                return;
            */

            // --- Password (HostOptionsTab: UpdateHostSession + CurrentNetworkSession.Password = ...)
            try
            {
                string pw = string.IsNullOrEmpty(_wiServerPassword) ? null : _wiServerPassword;

                // Matches HostOptionsTab._setPassword:
                // UpdateHostSession(null, new bool?(!string.IsNullOrWhiteSpace(_password)), null, null);
                if (CastleMinerZGame.Instance.IsOnlineGame)
                    session.UpdateHostSession(null, new bool?(!string.IsNullOrEmpty(_wiServerPassword)), null, null);

                session.Password = pw;
            }
            catch { }

            // --- Server Message (HostOptionsTab: _game.ServerMessage = ...)
            try
            {
                var sm = _wiServerMessage ?? string.Empty;

                // Matches ServerMessage.Set:
                // base.CurrentNetworkSession.UpdateHostSession(value, null, null, null);
                if (CastleMinerZGame.Instance.IsOnlineGame)
                    session.UpdateHostSession(sm, null, null, null);

                game.ServerMessage = sm;
            }
            catch { }
        }
        #endregion

        #region World Tab: Section Visibility & Layout

        // Visible (close) + Expanded (arrow) states.
        static bool _secWI_Identity = true, _secWI_World = true, _secWI_Server = true,  _secWI_Entities = true,  _secWI_Session = true;
        static bool _expWI_Identity = true, _expWI_World = true, _expWI_Server = false, _expWI_Entities = false, _expWI_Session = false;

        /// <summary>
        /// Draws a framed collapsing header and tracks the expanded state.
        /// Mirrors your Player tab pattern.
        /// </summary>
        static bool WI_Header(string label, ref bool visible, ref bool expanded)
        {
            ImGui.SetNextItemOpen(expanded, ImGuiCond.Always);
            var flags = ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;

            bool isOpen = ImGui.CollapsingHeader(label /*, ref visible */, flags);
            if (!visible) return false;

            if (ImGui.IsItemToggledOpen()) expanded = !expanded;
            else expanded = isOpen;

            return expanded;
        }

        static int WI_ExpandedCountFrom(int idx)
        {
            int c = 0;
            if (idx <= 0 && _secWI_Identity && _expWI_Identity) c++;
            if (idx <= 1 && _secWI_World && _expWI_World) c++;
            if (idx <= 2 && _secWI_Server && _expWI_Server) c++;
            if (idx <= 3 && _secWI_Entities && _expWI_Entities) c++;
            if (idx <= 4 && _secWI_Session && _expWI_Session) c++;
            return c;
        }

        static int WI_VisibleHeadersBelow(int idx)
        {
            int c = 0;
            if (idx < 1 && _secWI_World) c++;
            if (idx < 2 && _secWI_Server) c++;
            if (idx < 3 && _secWI_Entities) c++;
            if (idx < 4 && _secWI_Session) c++;
            return c;
        }

        /// <summary>
        /// Begins a bordered child window used as the scroll owner for a World section.
        /// Uses ImGuiChildFlags (newer ImGui.NET) like your Player tab.
        /// </summary>
        static bool WI_BeginSectionChild(string id, float height)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
            return ImGui.BeginChild(id, new Vector2(0, height), ImGuiChildFlags.Borders);
        }

        static void WI_EndSectionChild()
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        #endregion

        #region World Editor: WorldInfo Private Field Access

        // FieldRefs (Preferred: Can write even if initonly in many runtimes).
        static readonly AccessTools.FieldRef<WorldInfo, Guid> FR_WorldID =
            AccessTools.FieldRefAccess<WorldInfo, Guid>("_worldID");
        static readonly AccessTools.FieldRef<WorldInfo, int> FR_Seed =
            AccessTools.FieldRefAccess<WorldInfo, int>("_seed");
        static readonly AccessTools.FieldRef<WorldInfo, string> FR_OwnerTag =
            AccessTools.FieldRefAccess<WorldInfo, string>("_ownerGamerTag");
        static readonly AccessTools.FieldRef<WorldInfo, string> FR_CreatorTag =
            AccessTools.FieldRefAccess<WorldInfo, string>("_creatorGamerTag");
        static readonly AccessTools.FieldRef<WorldInfo, DateTime> FR_CreatedDate =
            AccessTools.FieldRefAccess<WorldInfo, DateTime>("_createdDate");

        // Reflection fallback.
        static readonly FieldInfo FI_WorldID     = typeof(WorldInfo).GetField("_worldID", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FI_Seed        = typeof(WorldInfo).GetField("_seed", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FI_OwnerTag    = typeof(WorldInfo).GetField("_ownerGamerTag", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FI_CreatorTag  = typeof(WorldInfo).GetField("_creatorGamerTag", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FI_CreatedDate = typeof(WorldInfo).GetField("_createdDate", BindingFlags.Instance | BindingFlags.NonPublic);

        static Guid WI_GetWorldID(WorldInfo wi)
        {
            try { return FR_WorldID(wi);                                                    } catch { }
            try { return (FI_WorldID != null) ? (Guid)FI_WorldID.GetValue(wi) : wi.WorldID; } catch { }
            return wi.WorldID;
        }

        static int WI_GetSeed(WorldInfo wi)
        {
            try { return FR_Seed(wi); } catch { }
            try { return (FI_Seed != null) ? (int)FI_Seed.GetValue(wi) : wi.Seed; } catch { }
            return wi.Seed;
        }

        static string WI_GetOwnerTag(WorldInfo wi)
        {
            try { return FR_OwnerTag(wi);                                                             } catch { }
            try { return (FI_OwnerTag != null) ? (string)FI_OwnerTag.GetValue(wi) : wi.OwnerGamerTag; } catch { }
            return wi.OwnerGamerTag;
        }

        static string WI_GetCreatorTag(WorldInfo wi)
        {
            try { return FR_CreatorTag(wi);                                                                 } catch { }
            try { return (FI_CreatorTag != null) ? (string)FI_CreatorTag.GetValue(wi) : wi.CreatorGamerTag; } catch { }
            return wi.CreatorGamerTag;
        }

        static DateTime WI_GetCreatedDate(WorldInfo wi)
        {
            try { return FR_CreatedDate(wi);                                                                } catch { }
            try { return (FI_CreatedDate != null) ? (DateTime)FI_CreatedDate.GetValue(wi) : wi.CreatedDate; } catch { }
            return wi.CreatedDate;
        }

        static void WI_SetWorldID(WorldInfo wi, Guid value)
        {
            try { FR_WorldID(wi) = value; return;  } catch { }
            try { FI_WorldID?.SetValue(wi, value); } catch { }
        }

        static void WI_SetSeed(WorldInfo wi, int value)
        {
            try { FR_Seed(wi) = value; return;  } catch { }
            try { FI_Seed?.SetValue(wi, value); } catch { }
        }

        static void WI_SetOwnerTag(WorldInfo wi, string value)
        {
            try { FR_OwnerTag(wi) = value; return;  } catch { }
            try { FI_OwnerTag?.SetValue(wi, value); } catch { }
        }

        static void WI_SetCreatorTag(WorldInfo wi, string value)
        {
            try { FR_CreatorTag(wi) = value; return;  } catch { }
            try { FI_CreatorTag?.SetValue(wi, value); } catch { }
        }

        static void WI_SetCreatedDate(WorldInfo wi, DateTime valueUtc)
        {
            // Store as UTC to keep it consistent with the existing display format ("u").
            if (valueUtc.Kind != DateTimeKind.Utc)
                valueUtc = DateTime.SpecifyKind(valueUtc.ToUniversalTime(), DateTimeKind.Utc);

            try { FR_CreatedDate(wi) = valueUtc; return;  } catch { }
            try { FI_CreatedDate?.SetValue(wi, valueUtc); } catch { }
        }

        static bool TryParseUtc(string s, out DateTime utc)
        {
            // Accepts "2026-02-14 22:10:00Z" or "2026-02-14 22:10:00" etc.
            if (DateTime.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt))
            {
                utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return true;
            }

            utc = default;
            return false;
        }

        static void WI_ApplyJoinAndPvp(CastleMinerZGame game, JoinGamePolicy join, CastleMinerZGame.PVPEnum pvp, bool broadcastPvp)
        {
            if (game == null)
                return;

            // These property setters already push changes into the live session:
            // - JoinGamePolicy => UpdateHostSessionJoinPolicy(...)
            // - PVPState       => SetProperty(5, ...) + UpdateHostSession(...)
            game.JoinGamePolicy = join;
            game.PVPState       = pvp;

            if (!broadcastPvp)
                return;

            // Match HostOptionsTab's broadcast behavior exactly.
            string txt = "";
            switch (game.PVPState)
            {
                case CastleMinerZGame.PVPEnum.Off:
                    txt = "PVP: " + CMZStrings.Get("Off");
                    break;
                case CastleMinerZGame.PVPEnum.Everyone:
                    txt = "PVP: " + CMZStrings.Get("Everyone");
                    break;
                case CastleMinerZGame.PVPEnum.NotFriends:
                    txt = "PVP: " + CMZStrings.Get("Non_Friends_Only");
                    break;
            }

            BroadcastTextMessage.Send(game.MyNetworkGamer, txt);
        }
        #endregion

        #endregion

        #region Tab: Enemies

        /// <summary>
        /// ENEMY EDITOR TAB
        ///
        /// Purpose:
        ///   - Inspect and live-edit properties of spawned enemies (BaseZombie instances).
        ///   - Safe to open even when no EnemyManager is present.
        ///   - Includes auto-read mirroring (reads from the live object into the UI fields).
        ///
        /// Key ideas:
        ///   - Auto-read is suppressed while the user is actively editing to avoid flicker.
        ///   - "Apply now" optionally thaws a frozen enemy for a few frames, then restores state.
        ///   - Two-pane UI: left = list, right = details/editor.
        /// </summary>

        #region Accessors

        // Reflection accessors (EnemyManager._enemies).
        static readonly System.Reflection.FieldInfo _fiEnemies =
            typeof(EnemyManager).GetField("_enemies",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Safe read of EnemyManager._enemies; returns an empty list if unavailable.
        private static List<BaseZombie> EnemiesRef(EnemyManager mgr)
        {
            var list = _fiEnemies?.GetValue(mgr) as List<BaseZombie>;
            return list ?? s_emptyZombies;
        }
        static readonly List<BaseZombie> s_emptyZombies = new List<BaseZombie>(0);

        // Convenience: Get currently selected zombie (or null).
        private static BaseZombie GetSelectedZombie()
        {
            var mgr  = EnemyManager.Instance;
            if (mgr == null) return null;
            var list = EnemiesRef(mgr);
            return (_enemySelIndex >= 0 && _enemySelIndex < list.Count) ? list[_enemySelIndex] : null;
        }
        #endregion

        #region Enemy Tab State

        // [Apply] state.
        static bool _eeApplyPending;
        static int  _eeApplyDelayFrames;
        static bool _eeRestoreFrozenAfter;
        static int  _applyEnemySkipFrames = 4;       // Frames to temporarily thaw when applying while frozen.

        // Auto-read state.
        static bool _eeAutoRead = true;              // When true, mirror live values into form fields.
        static int  _eeSuppressReadFrames = 0;       // > 0 blocks auto-read for N frames (editing cushion).

        // Track the currently mirrored instance to handle selection changes explicitly.
        static BaseZombie _eeTrackedRef = null;


        // Selection & filter.
        static int    _enemySelIndex = -1;           // Index into _enemies list (or -1 if none).
        static string _enemyFilter   = string.Empty;

        #endregion

        #region Fields

        // Cached UI fields (mirrored from/to the selected enemy).
        static float  _eeHealth      = 100f;
        static float  _eeScaleUni    = 1f;    // Optional uniform scale slider (drives _eeScale3).
        static int    _eeDistLimit   = 25;    // PlayerDistanceLimit.
        static float  _eeCurrSpeed   = 0f;
        static float  _eeFrustration = 15f;
        static float  _eeStateTimer  = 0f;
        static int    _eeAnimIndex   = 0;
        static int    _eeHitCount    = 0;
        static int    _eeMissCount   = 0;
        static float  _eeTimeFast    = 0f;
        static float  _eeTimeRunFast = 0f;
        static float  _eeSoundTimer  = 0f;
        static bool   _eeBlocking    = false;
        static bool   _eeHittable    = false;
        static bool   _eeMovingFast  = false;
        static bool   _eeFrozen      = false; // DoUpdate == false.

        // Vector fields (System.Numerics for ImGui; convert to XNA on apply).
        static System.Numerics.Vector3 _eePos3;
        static System.Numerics.Vector3 _eeScale3;
        static System.Numerics.Vector3 _eeAmb;
        static System.Numerics.Vector3 _eeDirLightCol0, _eeDirLightCol1;
        static System.Numerics.Vector3 _eeDirLightDir0, _eeDirLightDir1;
        static System.Numerics.Vector3 _eeAabbMin, _eeAabbMax;

        #endregion

        #region Helpers

        // Suppress auto-read for a small window. Adds a +1 safety cushion.
        static void EE_SuppressAutoRead(int frames = 4)
        {
            frames = Math.Max(frames + 1, 2);
            if (frames > _eeSuppressReadFrames)
                _eeSuppressReadFrames = frames;
        }

        // Decide if we should auto-read on this frame.
        static bool EE_IsTabOpen() => IsOpen && _tabEnemiesOpen;
        static bool EE_ShouldAutoRead()
            => EE_IsTabOpen() && (!_eeAutoRead || _eeSuppressReadFrames <= 0) && _eeAutoRead && _eeSuppressReadFrames == 0;

        // Rotation (Euler degrees: Yaw, Pitch, Roll) backing field for UI input.
        static System.Numerics.Vector3 _eeEulerDeg; // (Y, X, Z) = (Yaw, Pitch, Roll).

        // Math helpers.
        private static float  ClampF(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static float  CopySign(float magnitude, float sign)
            => (sign >= 0f || float.IsNaN(sign)) ? Math.Abs(magnitude) : -Math.Abs(magnitude);
        private static double CopySign(double magnitude, double sign)
            => (sign >= 0.0 || double.IsNaN(sign)) ? Math.Abs(magnitude) : -Math.Abs(magnitude);

        // Quaternion <-> Euler (degrees). Convention: Yaw(Y), Pitch(X), Roll(Z).
        private static System.Numerics.Vector3 QuatToEulerDeg(Microsoft.Xna.Framework.Quaternion q)
        {
            q.Normalize();
            float w = q.W, x = q.X, y = q.Y, z = q.Z;

            // Roll (Z).
            float sinr_cosp = 2f * (w * z + x * y);
            float cosr_cosp = 1f - 2f * (z * z + y * y);
            float roll = (float)Math.Atan2(sinr_cosp, cosr_cosp);

            // Pitch (X).
            float sinp = 2f * (w * x - y * z);
            float pitch = (Math.Abs(sinp) >= 1f)
                ? (float)CopySign(Math.PI / 2.0, sinp) // Clamp at 90°.
                : (float)Math.Asin(sinp);

            // Yaw (Y).
            float siny_cosp = 2f * (w * y + z * x);
            float cosy_cosp = 1f - 2f * (y * y + x * x);
            float yaw = (float)Math.Atan2(siny_cosp, cosy_cosp);

            const float Rad2Deg = 57.2957795f;
            return new System.Numerics.Vector3(yaw * Rad2Deg, pitch * Rad2Deg, roll * Rad2Deg);
        }

        private static Microsoft.Xna.Framework.Quaternion EulerDegToQuat(System.Numerics.Vector3 eulerDeg)
        {
            const float Deg2Rad = 0.01745329252f;
            float yaw   = eulerDeg.X * Deg2Rad; // Y
            float pitch = eulerDeg.Y * Deg2Rad; // X
            float roll  = eulerDeg.Z * Deg2Rad; // Z

            var qYaw   = Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(Microsoft.Xna.Framework.Vector3.Up, yaw);
            var qPitch = Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(Microsoft.Xna.Framework.Vector3.Right, pitch);
            var qRoll  = Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(Microsoft.Xna.Framework.Vector3.Forward, roll);

            var q = qYaw * qPitch * qRoll; // Y * X * Z
            q.Normalize();
            return q;
        }

        // Conversions: XNA <-> System.Numerics.
        public static System.Numerics.Vector3 ToSysVec(Microsoft.Xna.Framework.Vector3 v)
            => new System.Numerics.Vector3(v.X, v.Y, v.Z);
        public static Microsoft.Xna.Framework.Vector3 ToXnaVec(System.Numerics.Vector3 v)
            => new Microsoft.Xna.Framework.Vector3(v.X, v.Y, v.Z);

        // Mirror live object -> editor fields.
        private static void EE_ReadFromSelected()
        {
            var z = GetSelectedZombie();
            if (z == null) return;

            _eeHealth      = z.Health;
            _eeScaleUni    = z.LocalScale.X; // Assume uniform by default.
            _eeDistLimit   = z.PlayerDistanceLimit;
            _eeCurrSpeed   = z.CurrentSpeed;
            _eeFrustration = z.FrustrationCount;
            _eeStateTimer  = z.StateTimer;
            _eeAnimIndex   = z.AnimationIndex;
            _eeHitCount    = z.HitCount;
            _eeMissCount   = z.MissCount;
            _eeTimeFast    = z.TimeLeftTilFast;
            _eeTimeRunFast = z.TimeLeftTilRunFast;
            _eeSoundTimer  = z.SoundUpdateTimer;
            _eeBlocking    = z.IsBlocking;
            _eeHittable    = z.IsHittable;
            _eeMovingFast  = z.IsMovingFast;
            _eeFrozen      = !z.DoUpdate;

            _eePos3        = ToSysVec(z.LocalPosition);
            _eeEulerDeg    = QuatToEulerDeg(z.LocalRotation);
            _eeScale3      = ToSysVec(z.LocalScale);
            _eeAmb         = ToSysVec(z.AmbientLight);

            // Arrays are size 2 in ctor; be defensive anyway.
            if (z.DirectLightColor != null && z.DirectLightColor.Length >= 2)
            {
                _eeDirLightCol0 = ToSysVec(z.DirectLightColor[0]);
                _eeDirLightCol1 = ToSysVec(z.DirectLightColor[1]);
            }
            if (z.DirectLightDirection != null && z.DirectLightDirection.Length >= 2)
            {
                _eeDirLightDir0 = ToSysVec(z.DirectLightDirection[0]);
                _eeDirLightDir1 = ToSysVec(z.DirectLightDirection[1]);
            }

            _eeAabbMin     = ToSysVec(z.PlayerAABB.Min);
            _eeAabbMax     = ToSysVec(z.PlayerAABB.Max);
        }

        // UI helper: label on the left, control on the right.
        private static void EE_FormRow(string label, Action drawControl, float labelW = 150f)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, labelW - ImGui.CalcTextSize(label).X));
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
            drawControl?.Invoke();
        }

        // Short string truncation helper for list rows.
        private static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= n) return s ?? string.Empty;
            return s.Substring(0, Math.Max(0, n - 1)) + "...";
        }
        #endregion

        #region Main Tab Draw

        // Main draw for the Enemy Editor tab.
        private static void DrawEnemyEditorTab()
        {
            // Read from live object first (unless auto-read is suppressed).
            if (EE_ShouldAutoRead())
                EE_ReadFromSelected();

            // Decrement suppression AFTER use.
            if (_eeSuppressReadFrames > 0)
                _eeSuppressReadFrames--;

            // Defensive: Can run while not in-game.
            var mgr = EnemyManager.Instance;
            var zombies = (mgr != null) ? EnemiesRef(mgr) : s_emptyZombies;

            // Header controls.
            ImGui.TextDisabled("Client-side editor (host authoritative)");
            ImGui.SameLine();
            ImGui.TextUnformatted("| Filter:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            ImGui.InputText("##ee_filter", ref _enemyFilter, 64);
            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
                _enemySelIndex = Math.Min(Math.Max(_enemySelIndex, -1), Math.Max(0, zombies.Count - 1));

            ImGui.SameLine();
            ImGui.TextDisabled($"| Enemies: {zombies.Count}");

            ImGui.Separator();

            // Split view: list (left), editor (right).
            if (ImGui.BeginTable("ee_split", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("list",   ImGuiTableColumnFlags.WidthStretch, 0.45f);
                ImGui.TableSetupColumn("editor", ImGuiTableColumnFlags.WidthStretch, 0.55f);

                #region Left List

                // Left List //
                ImGui.TableNextColumn();

                float availH = ImGui.GetContentRegionAvail().Y;
                float edge   = 5f;
                var   style  = ImGui.GetStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edge, edge));
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(style.ItemSpacing.X, 2f));

                if (ImGui.BeginChild("ee_list", new Vector2(0, availH), ImGuiChildFlags.Borders))
                {
                    ImGui.TextDisabled("ID   Type                     Dist   HP");
                    ImGui.Separator();

                    // Normalize filter.
                    string f = (_enemyFilter ?? string.Empty).Trim().ToLowerInvariant();

                    for (int i = 0; i < zombies.Count; i++)
                    {
                        var z = zombies[i];
                        if (z == null) continue;

                        int    id   = z.EnemyID;
                        string type = $"{z.GetType().Name} ({EnemyLabel(GetEnemyEnum(z))})";
                        float  hp   = z.Health;

                        // Distance to local player.
                        var me = CastleMinerZGame.Instance?.LocalPlayer;
                        float dist = (me != null)
                            ? Vector3.Distance(me.LocalPosition, z.LocalPosition)
                            : 0f;

                        string row = $"{id,4} {Trunc(type, 23),-23}  {dist,5:0}  {hp,5:0}";

                        // Filter check.
                        if (f.Length > 0)
                        {
                            var idStr = id.ToString();
                            var key   = (idStr + ":" + type).ToLowerInvariant();
                            if (!(type.ToLowerInvariant().Contains(f) || idStr.Contains(f) || key.Contains(f)))
                                continue;
                        }

                        bool selected = (i == _enemySelIndex);
                        if (ImGui.Selectable($"{row}##ee_{id}", selected))
                        {
                            _enemySelIndex = i;
                            _eeSuppressReadFrames = 0; // Allow immediate sync on selection change.
                            EE_ReadFromSelected();
                        }
                        if (selected) ImGui.SetItemDefaultFocus();
                    }

                    if (zombies.Count == 0) { _enemySelIndex = -1; _eeTrackedRef = null; }
                    if (zombies.Count == 0)
                        ImGui.TextDisabled("(no enemies - not in-game or nothing spawned)");
                }
                ImGui.EndChild();
                ImGui.PopStyleVar(2);

                #endregion

                #region Right Editor

                // Right Editor //
                ImGui.TableNextColumn();

                float rightAvail = ImGui.GetContentRegionAvail().Y;

                // Scrollable editor.
                if (ImGui.BeginChild("ee_editor_scroll", new Vector2(0, rightAvail), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                {
                    var sel = GetSelectedZombie();

                    // Opportunistic auto-read while idle or on selection change.
                    if (sel != null && EE_ShouldAutoRead())
                    {
                        if (!ReferenceEquals(sel, _eeTrackedRef))
                        {
                            _eeTrackedRef = sel;
                            EE_ReadFromSelected();
                        }
                        else
                        {
                            bool userEditing =
                                ImGui.IsAnyItemActive() ||
                                ImGui.IsMouseDown(ImGuiMouseButton.Left) ||
                                ImGui.IsMouseDown(ImGuiMouseButton.Right);

                            if (!userEditing)
                                EE_ReadFromSelected();
                        }
                    }

                    if (sel == null)
                    {
                        ImGui.TextDisabled("Select an enemy on the left...");
                    }
                    else
                    {
                        // Summary.
                        ImGui.Text($"Enemy ID: {sel.EnemyID}");
                        ImGui.Text($"Type:     {sel.GetType().Name}");
                        var me = CastleMinerZGame.Instance?.LocalPlayer;
                        if (me != null)
                        {
                            float dist = Microsoft.Xna.Framework.Vector3.Distance(me.LocalPosition, sel.LocalPosition);
                            ImGui.Text($"Distance: {dist:0.0}");
                        }
                        ImGui.Separator();

                        #region Buttons

                        // Basic actions.
                        if (ImGui.Button("Kill"))
                            sel?.Kill();

                        ImGui.SameLine();
                        if (ImGui.Button("TP Enemy To You"))
                            if (sel != null)
                                sel.LocalPosition = CastleMinerZGame.Instance.LocalPlayer.LocalPosition;

                        ImGui.SameLine();
                        if (ImGui.Button("TP You To Enemy"))
                            if (sel != null)
                                CastleMinerZGame.Instance.LocalPlayer.LocalPosition = sel.LocalPosition;

                        ImGui.Separator();

                        // Auto-read toggles.
                        ImGui.Checkbox("Auto-read (live)", ref _eeAutoRead);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Continuously mirror the selected enemy into these fields when you're not actively editing.");

                        ImGui.SameLine();
                        if (ImGui.Button("Read from selection"))
                            EE_ReadFromSelected();

                        ImGui.SameLine();
                        if (ImGui.Button("Read now"))
                        {
                            _eeSuppressReadFrames = 0;
                            EE_ReadFromSelected();
                        }

                        ImGui.Separator();

                        // Apply pipeline (optionally thaws for a few frames).
                        if (ImGui.Button("Apply now"))
                        {
                            if (sel != null)
                            {
                                _eeApplyPending = true;
                                _eeApplyDelayFrames = (!sel.DoUpdate) ? _applyEnemySkipFrames : 0; // Give window if frozen.
                                _eeRestoreFrozenAfter = _eeFrozen;

                                if (!sel.DoUpdate) sel.DoUpdate = true;                            // Unfreeze immediately.

                                // Pause auto-read during the delay + safety cushion.
                                EE_SuppressAutoRead(_eeApplyDelayFrames + _applyEnemySkipFrames);
                            }
                        }

                        ImGui.SameLine();
                        ImGui.TextUnformatted("Thaw Frames:");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.SliderInt("##en-thaw-frames", ref _applyEnemySkipFrames, 0, 60))
                            _applyEnemySkipFrames = Math.Max(0, Math.Min(60, _applyEnemySkipFrames));

                        // Pending apply tick.
                        if (_eeApplyPending)
                        {
                            if (_eeApplyDelayFrames-- <= 0)
                            {
                                if (sel != null)
                                {
                                    // Apply properties.
                                    sel.IsBlocking           = _eeBlocking;
                                    sel.IsHittable           = _eeHittable;
                                    sel.IsMovingFast         = _eeMovingFast;
                                    sel.Health               = _eeHealth;
                                    sel.CurrentSpeed         = _eeCurrSpeed;
                                    sel.PlayerDistanceLimit  = _eeDistLimit;
                                    sel.FrustrationCount     = _eeFrustration;
                                    sel.StateTimer           = _eeStateTimer;
                                    sel.AnimationIndex       = _eeAnimIndex;
                                    sel.HitCount             = _eeHitCount;
                                    sel.MissCount            = _eeMissCount;
                                    sel.TimeLeftTilFast      = _eeTimeFast;
                                    sel.TimeLeftTilRunFast   = _eeTimeRunFast;
                                    sel.SoundUpdateTimer     = _eeSoundTimer;
                                    sel.LocalScale           = ToXnaVec(_eeScale3);
                                    sel.LocalPosition        = ToXnaVec(_eePos3);
                                    sel.LocalRotation        = EulerDegToQuat(_eeEulerDeg);
                                    sel.PlayerAABB           = new Microsoft.Xna.Framework.BoundingBox(ToXnaVec(_eeAabbMin), ToXnaVec(_eeAabbMax));
                                    sel.AmbientLight         = ToXnaVec(_eeAmb);
                                    if (sel.DirectLightColor != null && sel.DirectLightColor.Length >= 2)
                                    {
                                        sel.DirectLightColor[0] = ToXnaVec(_eeDirLightCol0);
                                        sel.DirectLightColor[1] = ToXnaVec(_eeDirLightCol1);
                                    }
                                    if (sel.DirectLightDirection  != null && sel.DirectLightDirection.Length >= 2)
                                    {
                                        sel.DirectLightDirection[0] = ToXnaVec(_eeDirLightDir0);
                                        sel.DirectLightDirection[1] = ToXnaVec(_eeDirLightDir1);
                                    }

                                    // Respect current freeze toggle.
                                    sel.DoUpdate = !_eeFrozen;
                                }

                                _eeApplyPending = false;
                                EE_SuppressAutoRead(_applyEnemySkipFrames);
                            }
                            else
                            {
                                EE_SuppressAutoRead(_applyEnemySkipFrames);
                            }
                        }

                        #endregion

                        ImGui.Separator();

                        // Property grid.
                        if (ImGui.BeginTable("ee_form", 2, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, 150f);
                            ImGui.TableSetupColumn("ctrl",  ImGuiTableColumnFlags.WidthStretch, 1f);

                            #region Toggles

                            EE_FormRow("Freeze (DoUpdate=false)", () =>
                            {
                                bool v = _eeFrozen;
                                if (ImGui.Checkbox("##ee_freeze", ref v))
                                {
                                    _eeFrozen   = v;
                                    sel.DoUpdate = !v;
                                    if (v) sel.PlayerPhysics.WorldVelocity = Microsoft.Xna.Framework.Vector3.Zero;
                                }
                            });

                            EE_FormRow("Blocking", () =>
                            {
                                bool v = _eeBlocking;
                                if (ImGui.Checkbox("##ee_blocking", ref v))
                                {
                                    _eeBlocking = v;
                                    sel.IsBlocking = v;
                                }
                            });

                            EE_FormRow("Hittable", () =>
                            {
                                bool v = _eeHittable;
                                if (ImGui.Checkbox("##ee_hittable", ref v))
                                {
                                    _eeHittable = v;
                                    sel.IsHittable = v;
                                }
                            });

                            EE_FormRow("Moving Fast", () =>
                            {
                                bool v = _eeMovingFast;
                                if (ImGui.Checkbox("##ee_fast", ref v))
                                {
                                    _eeMovingFast = v;
                                    sel.IsMovingFast = v;
                                }
                            });

                            ImGui.Separator();

                            // Numeric fields.
                            EE_FormRow("Health", () =>
                            {
                                float v = _eeHealth;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.SliderFloat("##ee_hp", ref v, 0f, 5000f, "%.0f", ImGuiSliderFlags.AlwaysClamp))
                                {
                                    _eeHealth = v;
                                    sel.Health = v;
                                }
                            });

                            EE_FormRow("Current Speed", () =>
                            {
                                float v = _eeCurrSpeed;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.SliderFloat("##ee_spd", ref v, 0f, 200f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                                {
                                    _eeCurrSpeed = v;
                                    sel.CurrentSpeed = v;
                                }
                            });

                            EE_FormRow("PlayerDistanceLimit", () =>
                            {
                                int v = _eeDistLimit;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.SliderInt("##ee_dist", ref v, 0, 10000, "%d", ImGuiSliderFlags.AlwaysClamp))
                                {
                                    _eeDistLimit = v;
                                    sel.PlayerDistanceLimit = v;
                                }
                            });

                            EE_FormRow("Frustration Count", () =>
                            {
                                float v = _eeFrustration;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat("##ee_frustration", ref v))
                                {
                                    _eeFrustration = v;
                                    sel.FrustrationCount = v;
                                }
                            });

                            EE_FormRow("State Timer", () =>
                            {
                                float v = _eeStateTimer;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat("##ee_st", ref v))
                                {
                                    _eeStateTimer = v;
                                    sel.StateTimer = v;
                                }
                            });

                            EE_FormRow("Animation Index", () =>
                            {
                                int v = _eeAnimIndex;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputInt("##ee_anim", ref v))
                                {
                                    _eeAnimIndex = v;
                                    sel.AnimationIndex = v;
                                }
                            });

                            EE_FormRow("Hit Count", () =>
                            {
                                int v = _eeHitCount;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputInt("##ee_hit", ref v))
                                {
                                    _eeHitCount = v;
                                    sel.HitCount = v;
                                }
                            });

                            EE_FormRow("Miss Count", () =>
                            {
                                int v = _eeMissCount;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputInt("##ee_miss", ref v))
                                {
                                    _eeMissCount = v;
                                    sel.MissCount = v;
                                }
                            });

                            EE_FormRow("Time Left (Fast)", () =>
                            {
                                float v = _eeTimeFast;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat("##ee_tfast", ref v))
                                {
                                    _eeTimeFast = v;
                                    sel.TimeLeftTilFast = v;
                                }
                            });

                            EE_FormRow("Time Left (RunFast)", () =>
                            {
                                float v = _eeTimeRunFast;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat("##ee_trun", ref v))
                                {
                                    _eeTimeRunFast = v;
                                    sel.TimeLeftTilRunFast = v;
                                }
                            });

                            EE_FormRow("Sound Update Timer", () =>
                            {
                                float v = _eeSoundTimer;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat("##ee_snd", ref v))
                                {
                                    _eeSoundTimer = v;
                                    sel.SoundUpdateTimer = v;
                                }
                            });

                            ImGui.Separator();

                            // Transforms / vectors.
                            EE_FormRow("Uniform Scale", () =>
                            {
                                float v = _eeScaleUni;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.SliderFloat("##ee_scaleu", ref v, 0.05f, 50f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                                {
                                    _eeScaleUni = v;
                                    _eeScale3   = new System.Numerics.Vector3(v, v, v);
                                    sel.LocalScale = ToXnaVec(_eeScale3);
                                }
                            });

                            EE_FormRow("Scale (XYZ)", () =>
                            {
                                var v = _eeScale3;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_scale3", ref v))
                                {
                                    _eeScale3 = v;
                                    _eeScaleUni = (v.X + v.Y + v.Z) / 3f; // Keep the uniform slider roughly in sync.
                                    sel.LocalScale = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Position (XYZ)", () =>
                            {
                                var v = _eePos3;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_pos", ref v))
                                {
                                    _eePos3 = v;
                                    sel.LocalPosition = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Rotation (deg Y/P/R)", () =>
                            {
                                var v = _eeEulerDeg;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_euler", ref v))
                                {
                                    v.X = ClampF(v.X, -10000f, 10000f);
                                    v.Y = ClampF(v.Y, -10000f, 10000f);
                                    v.Z = ClampF(v.Z, -10000f, 10000f);

                                    _eeEulerDeg = v;
                                    sel.LocalRotation = EulerDegToQuat(v);
                                }
                            });

                            EE_FormRow("AABB Min (XYZ)", () =>
                            {
                                var v = _eeAabbMin;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_aabbmin", ref v))
                                {
                                    _eeAabbMin = v;
                                    sel.PlayerAABB = new Microsoft.Xna.Framework.BoundingBox(ToXnaVec(_eeAabbMin), ToXnaVec(_eeAabbMax));
                                }
                            });

                            EE_FormRow("AABB Max (XYZ)", () =>
                            {
                                var v = _eeAabbMax;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_aabbmax", ref v))
                                {
                                    _eeAabbMax = v;
                                    sel.PlayerAABB = new Microsoft.Xna.Framework.BoundingBox(ToXnaVec(_eeAabbMin), ToXnaVec(_eeAabbMax));
                                }
                            });

                            ImGui.Separator();

                            // Lighting.
                            EE_FormRow("Ambient Light (0..1)", () =>
                            {
                                var v = _eeAmb;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_amb", ref v))
                                {
                                    _eeAmb = v;
                                    sel.AmbientLight = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Dir Light 0 Color", () =>
                            {
                                var v = _eeDirLightCol0;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_dlc0", ref v))
                                {
                                    _eeDirLightCol0 = v;
                                    if (sel.DirectLightColor != null && sel.DirectLightColor.Length >= 1)
                                        sel.DirectLightColor[0] = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Dir Light 0 Direction", () =>
                            {
                                var v = _eeDirLightDir0;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_dld0", ref v))
                                {
                                    _eeDirLightDir0 = v;
                                    if (sel.DirectLightDirection != null && sel.DirectLightDirection.Length >= 1)
                                        sel.DirectLightDirection[0] = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Dir Light 1 Color", () =>
                            {
                                var v = _eeDirLightCol1;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_dlc1", ref v))
                                {
                                    _eeDirLightCol1 = v;
                                    if (sel.DirectLightColor != null && sel.DirectLightColor.Length >= 2)
                                        sel.DirectLightColor[1] = ToXnaVec(v);
                                }
                            });

                            EE_FormRow("Dir Light 1 Direction", () =>
                            {
                                var v = _eeDirLightDir1;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat3("##ee_dld1", ref v))
                                {
                                    _eeDirLightDir1 = v;
                                    if (sel.DirectLightDirection != null && sel.DirectLightDirection.Length >= 2)
                                        sel.DirectLightDirection[1] = ToXnaVec(v);
                                }
                            });

                            #endregion

                            ImGui.EndTable();
                        }

                        ImGui.Separator();
                        ImGui.TextDisabled("Note: Client-side only; host may override next tick.");
                    }

                    ImGui.EndChild();
                }

                ImGui.EndTable();

                #endregion
            }

            // Keep selection valid if list clears.
            if (zombies.Count == 0) _enemySelIndex = -1;
        }
        #endregion

        #endregion

        #region Tab: Dragons

        /// <summary>
        /// DRAGON EDITOR TAB
        ///
        /// Purpose:
        ///   - Inspect/drive DragonEntity (server intent) and DragonClientEntity (rendered client).
        ///   - Supports freeze (pose lock), thaw-on-apply, live editing with auto-read suppression.
        ///
        /// Notes:
        ///   - "Target" is editable via combo: None (null) + players snapshot from CurrentNetworkSession.
        ///   - Client (DragonClientEntity) is used for visible transform/anim; server (DragonEntity) holds
        ///     flight logic and timers.
        /// </summary>

        #region Accessors

        // Accessors (Harmony).
        public static readonly AccessTools.FieldRef<EnemyManager, DragonEntity> DragonEntityRef =
            AccessTools.FieldRefAccess<EnemyManager, DragonEntity>("_dragon");
        public static readonly AccessTools.FieldRef<EnemyManager, DragonClientEntity> DragonClientEntityRef =
            AccessTools.FieldRefAccess<EnemyManager, DragonClientEntity>("_dragonClient");

        // Helpers to fetch instances (safe when not in-game).
        public static DragonEntity GetDragon() =>
            EnemyManager.Instance != null ? DragonEntityRef(EnemyManager.Instance) : null;
        public static DragonClientEntity GetDragonClient() =>
            EnemyManager.Instance != null ? DragonClientEntityRef(EnemyManager.Instance) : null;

        #endregion

        #region Dragon Tab State

        // Dragon tab state.
        static bool _drAutoRead = true;                 // Live mirror into UI fields
        static int  _drSuppressReadFrames = 0;          // Temporary pause on auto-read
        static bool _drApplyPending;
        static int  _drApplyDelayFrames;
        const int   DR_APPLY_WAIT = 0;                  // No DoUpdate gating for dragons

        // Freeze (client pose lock)
        public static bool _drFrozen = false;
        public static System.Numerics.Vector3 _drFreezePos;
        public static Microsoft.Xna.Framework.Quaternion _drFreezeRot;     // Exact rotation snapshot.

        // Thaw-on-apply configuration/state
        public static int _drThawOnApplyFrames   = 4;                      // How many frames to let it move after Apply.
        public static int _drThawFramesRemaining = 0;                      // Countdown while temporarily thawed.

        // Cached UI fields (client transform/anim; server intent/logic).
        static System.Numerics.Vector3 _drPos;
        static float _drYawDeg, _drPitchDeg, _drRollDeg;                   // Client rotation (deg).
        static float _drTargetYawDeg, _drTargetPitchDeg, _drTargetRollDeg; // Server yaw/pitch/roll targets.
        static float _drVel, _drTargetVel, _drTargetAlt;                   // Server velocity/altitude.
        static bool  _drVisible;
        static int   _drAnimIndex;                                         // DragonAnimEnum (index).
        static float _drClipSpeed;
        static int   _drShotsLeft, _drLoitersLeft;
        static float _drTimeBeforeNextShot, _drTimeTilShotsHeard;
        static float _drDragonTime;
        static int   _drTargetIdx = 0;                                     // 0=None; 1..N = players list.

        static readonly List<Player> _drPlayers = new List<Player>(8);     // Populated from session.

        // LocalScale controls (client + server).
        static float _drScaleUni = 1f;                                     // Uniform convenience.
        static System.Numerics.Vector3 _drScale3 = new System.Numerics.Vector3(1f, 1f, 1f);

        static readonly string[] _drAnimNames = DragonEntity.AnimNames;

        #endregion

        #region Small Helpers

        // Small helpers.
        static void  DR_Suppress(int frames = 4)
        {
            frames = Math.Max(frames + 1, 2);
            if (frames > _drSuppressReadFrames) _drSuppressReadFrames = frames;
        }
        static bool  DR_ShouldAutoReadIdle()
        {
            bool uiBusy = ImGui.IsAnyItemActive() || ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right);
            return _drAutoRead && _drSuppressReadFrames == 0 && !uiBusy;
        }
        static void  DR_PauseAutoReadShort() => DR_Suppress(2);
        static void  DR_StartThawIfFrozen(int frames)
        {
            if (_drFrozen && frames > 0)
                _drThawFramesRemaining = Math.Max(_drThawFramesRemaining, frames);
        }
        static bool  DR_IsTabOpen()      => IsOpen && _tabDragonOpen;
        static bool  DR_ShouldAutoRead() => DR_IsTabOpen() && _drAutoRead && _drSuppressReadFrames == 0;
        static int   ClampI(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        static float ToRadians(float degrees) => degrees * 0.017453292f;
        static float ToDegrees(float radians) => radians * 57.295776f;

        #endregion

        #region Read Dragon State

        // Read live state -> UI fields.
        static void DR_ReadFromDragon()
        {
            var d  = GetDragon();
            var dc = GetDragonClient();
            if (dc == null && d == null) return;

            // Prefer client entity for transform (that's what renders).
            if (dc != null)
            {
                _drPos = ToSysVec(dc.LocalPosition);

                var e = QuatToEulerDeg(dc.LocalRotation); // (Yaw, Pitch, Roll) in degrees.
                _drYawDeg   = e.X;
                _drPitchDeg = e.Y;
                _drRollDeg  = e.Z;

                _drVisible   = dc.Visible;
                _drClipSpeed = dc.ClipSpeed;
                _drAnimIndex = (int)dc.CurrentAnimation;
            }

            // Server-side driving numbers (intent/logic).
            if (d != null)
            {
                _drScale3   = ToSysVec(d.LocalScale);
                _drScaleUni = (_drScale3.X + _drScale3.Y + _drScale3.Z) / 3f;

                _drTargetYawDeg   = ToDegrees(d.TargetYaw);
                _drTargetPitchDeg = ToDegrees(d.TargetPitch);
                _drTargetRollDeg  = ToDegrees(d.TargetRoll);

                _drVel         = d.Velocity;
                _drTargetVel   = d.TargetVelocity;
                _drTargetAlt   = d.TargetAltitude;

                _drShotsLeft          = d.ShotsLeft;
                _drLoitersLeft        = d.LoitersLeft;
                _drTimeBeforeNextShot = d.TimeLeftBeforeNextShot;
                _drTimeTilShotsHeard  = d.TimeLeftTilShotsHeard;

                _drDragonTime         = d.DragonTime;
            }

            // Target list + selection index.
            DR_RebuildPlayers();
            _drTargetIdx = 0; // Default: None (null).
            if (d != null && d.Target != null)
            {
                int ix = _drPlayers.IndexOf(d.Target);
                if (ix >= 0) _drTargetIdx = ix + 1;
                else
                {
                    _drPlayers.Add(d.Target);        // Edge case: Include unknown target so UI can show it.
                    _drTargetIdx = _drPlayers.Count;
                }
            }
        }
        #endregion

        #region Apply To Dragon

        // Apply UI fields -> live state.
        static void DR_ApplyToDragon()
        {
            var d  = GetDragon();
            var dc = GetDragonClient();
            if (dc == null && d == null) return;

            // Basic sanity clamps.
            _drScale3.X = ClampF(_drScale3.X, 0.01f, 100f);
            _drScale3.Y = ClampF(_drScale3.Y, 0.01f, 100f);
            _drScale3.Z = ClampF(_drScale3.Z, 0.01f, 100f);

            // Client visuals/controls.
            if (dc != null)
            {
                dc.Visible       = _drVisible;
                dc.LocalPosition = ToXnaVec(_drPos);
                dc.LocalRotation = EulerDegToQuat(new System.Numerics.Vector3(_drYawDeg, _drPitchDeg, _drRollDeg));
                dc.LocalScale    = ToXnaVec(_drScale3);

                var newAnim = (DragonAnimEnum)ClampI(_drAnimIndex, 0, _drAnimNames.Length - 1);
                if (dc.CurrentAnimation != newAnim)
                    dc.CurrentAnimation = newAnim;

                dc.ClipSpeed = _drClipSpeed;
            }

            // Server intent/state.
            if (d != null)
            {
                Player newTarget = null;
                if (_drTargetIdx > 0)
                {
                    int pi = _drTargetIdx - 1;
                    if (pi >= 0 && pi < _drPlayers.Count) newTarget = _drPlayers[pi];
                }
                d.Target = newTarget;

                d.LocalPosition = ToXnaVec(_drPos);
                d.LocalRotation = EulerDegToQuat(new System.Numerics.Vector3(_drYawDeg, _drPitchDeg, _drRollDeg));
                d.LocalScale    = ToXnaVec(_drScale3);

                d.TargetYaw      = ToRadians(_drTargetYawDeg);
                d.TargetPitch    = ToRadians(_drTargetPitchDeg);
                d.TargetRoll     = ToRadians(_drTargetRollDeg);
                d.Velocity       = _drVel;
                d.TargetVelocity = _drTargetVel;
                d.TargetAltitude = _drTargetAlt;

                d.ShotsLeft               = _drShotsLeft;
                d.LoitersLeft             = _drLoitersLeft;
                d.TimeLeftBeforeNextShot  = _drTimeBeforeNextShot;
                d.TimeLeftTilShotsHeard   = _drTimeTilShotsHeard;

                var newAnim = (DragonAnimEnum)ClampI(_drAnimIndex, 0, _drAnimNames.Length - 1);
                if (d.CurrentAnimation != newAnim)
                    d.CurrentAnimation = newAnim;
            }
        }
        #endregion

        #region Build Player List

        // Rebuild player list (None + AllGamers). Local player first.
        static void DR_RebuildPlayers()
        {
            _drPlayers.Clear();
            var seen = new HashSet<Player>();
            var game = CastleMinerZGame.Instance;
            if (game == null) return;

            void Add(Player p)
            {
                if (p != null && seen.Add(p))
                    _drPlayers.Add(p);
            }

            // Local player first (if available).
            Add(game.LocalPlayer);

            var session = game.CurrentNetworkSession;
            if (session != null)
            {
                foreach (NetworkGamer g in session.AllGamers)
                {
                    if (g != null && (Player)g.Tag != game.LocalPlayer)
                        _drPlayers.Add((Player)g.Tag);
                }
            }
        }
        #endregion

        #region Snapshot Helper

        // Freeze snapshot / toggle.
        static void DR_SetFrozen(bool frozen)
        {
            if (_drFrozen == frozen) return;
            _drFrozen = frozen;

            var dc = GetDragonClient();
            if (dc == null) return;

            if (frozen)
            {
                _drFreezePos = ToSysVec(dc.LocalPosition);
                _drFreezeRot = dc.LocalRotation;
                dc.Visible   = true; // Keep mesh rendered while static.
            }
            else
            {
                // Let interpolation resume naturally; nothing else needed.
            }
        }
        #endregion

        #region Main Tab Draw

        // Main draw for the Dragon Editor tab.
        static void DrawDragonEditorTab()
        {
            ImGui.TextDisabled("Client-side editor (host authoritative)");

            // Auto-read first, then decrement suppression.
            if (DR_ShouldAutoRead()) DR_ReadFromDragon();
            if (_drSuppressReadFrames > 0) _drSuppressReadFrames--;

            var d  = GetDragon();
            var dc = GetDragonClient();
            if (dc == null && d == null)
            {
                ImGui.Separator();
                ImGui.TextDisabled("(no active dragon - not in-game or nothing spawned)");
                return;
            }

            // Summary header.
            var animName = (dc != null) ? dc.CurrentAnimation.ToString() : (d != null ? d.CurrentAnimation.ToString() : "N/A");
            var etype    = (d  != null) ? d.EType.EType.ToString() : "Unknown";
            var dtime    = (d  != null) ? d.DragonTime : 0f;

            ImGui.SameLine();
            ImGui.Text($"| Type: {etype}   Anim: {animName}   Time: {dtime:0.00}s");
            ImGui.Separator();

            #region Buttons (Actions + Top Controls)

            // Actions.
            if (ImGui.Button("Kill")) dc?.Kill(true);

            ImGui.SameLine();
            if (ImGui.Button("TP Dragon To You"))
            {
                var me = CastleMinerZGame.Instance?.LocalPlayer;
                if (me != null)
                {
                    _drPos = ToSysVec(me.LocalPosition); // Stage in UI model.
                    _drApplyPending = true;              // Apply through normal pipeline.
                    _drApplyDelayFrames = DR_APPLY_WAIT;
                    DR_Suppress(DR_APPLY_WAIT);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("TP You To Dragon"))
            {
                var me = CastleMinerZGame.Instance?.LocalPlayer;
                if (me != null)
                {
                    me.LocalPosition = ToXnaVec(_drPos); // Move local player to dragon snapshot.
                    _drApplyPending  = true;
                    _drApplyDelayFrames = DR_APPLY_WAIT;
                    DR_Suppress(DR_APPLY_WAIT);
                }
            }

            ImGui.Separator();

            // Top toggles.
            ImGui.Checkbox("Auto-read (live)", ref _drAutoRead);

            ImGui.SameLine();
            if (ImGui.Button("Read now")) { _drSuppressReadFrames = 0; DR_ReadFromDragon(); }

            ImGui.SameLine();
            if (ImGui.Button("Apply now"))
            {
                _drApplyPending     = true;
                _drApplyDelayFrames = DR_APPLY_WAIT;

                // If frozen, allow it to run for N frames after apply.
                if (_drFrozen && _drThawOnApplyFrames > 0)
                    _drThawFramesRemaining = _drThawOnApplyFrames;

                // Cushion so auto-read doesn't echo over edits.
                DR_Suppress(_drThawOnApplyFrames + 2);
            }

            ImGui.SameLine();
            ImGui.TextUnformatted("Thaw Frames:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##dr-thaw-frames", ref _drThawOnApplyFrames, 0, 60))
                _drThawOnApplyFrames = Math.Max(0, Math.Min(60, _drThawOnApplyFrames));

            // Pending apply tick.
            if (_drApplyPending)
            {
                if (_drApplyDelayFrames-- <= 0)
                {
                    DR_ApplyToDragon();
                    _drApplyPending = false;
                    DR_Suppress(DR_APPLY_WAIT);
                }
                else
                {
                    DR_Suppress(DR_APPLY_WAIT);
                }
            }
            #endregion

            ImGui.Separator();

            // Scrollable property grid.
            if (ImGui.BeginChild("dr_form_scroll", new System.Numerics.Vector2(0, 0),
                                 ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                if (ImGui.BeginTable("dr_form", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, 175f);
                    ImGui.TableSetupColumn("ctrl",  ImGuiTableColumnFlags.WidthStretch, 1f);

                    void Row(string label, Action draw) {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(label);
                        ImGui.TableNextColumn();
                        draw();
                    }

                    #region Toggles & Target

                    // Visibility / Freeze.
                    Row("Visible", () => {
                        bool v = _drVisible;
                        if (ImGui.Checkbox("##dr_vis", ref v)) _drVisible = v;
                    });

                    Row("Freeze (client pose lock)", () =>
                    {
                        bool v = _drFrozen;
                        if (ImGui.Checkbox("##dr_freeze", ref v))
                        {
                            DR_SetFrozen(v);
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Locks the DragonClient pose/position. Keeps full model visible while paused.");
                    });

                    ImGui.Separator();

                    // Target selection (server DragonEntity.Target).
                    Row("Target", () =>
                    {
                        // Rebuild every draw to keep list fresh.
                        DR_RebuildPlayers();

                        string PlayerLabel(Player p)
                        {
                            if (p == null) return "None (null)";
                            var me = CastleMinerZGame.Instance?.LocalPlayer;
                            string name = p.Gamer.Gamertag ?? "Player";
                            return (p == me ? "[Local] " : "") + name;
                        }

                        string preview = _drTargetIdx == 0 ? "None (null)" : PlayerLabel(_drPlayers[_drTargetIdx - 1]);
                        ImGui.SetNextItemWidth(-1);
                        bool opened = ImGui.BeginCombo("##dr_target", preview);

                        // While open/active, suppress auto-read so selection isn't overwritten.
                        if (ImGui.IsItemActive() || ImGui.IsItemHovered()) DR_Suppress(2);

                        if (opened)
                        {
                            bool selNone = (_drTargetIdx == 0);
                            if (ImGui.Selectable("None (null)", selNone)) { _drTargetIdx = 0; DR_Suppress(2); }
                            if (selNone) ImGui.SetItemDefaultFocus();

                            for (int i = 0; i < _drPlayers.Count; i++)
                            {
                                bool sel = (_drTargetIdx == i + 1);
                                if (ImGui.Selectable(PlayerLabel(_drPlayers[i]), sel))
                                {
                                    _drTargetIdx = i + 1;
                                    DR_Suppress(_drThawOnApplyFrames);
                                }
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // Quick helpers.
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Set Local"))
                        {
                            var lp = CastleMinerZGame.Instance?.LocalPlayer;
                            int ix = (lp == null) ? -1 : _drPlayers.IndexOf(lp);
                            _drTargetIdx = (ix >= 0) ? ix + 1 : 0;
                            DR_Suppress(2);
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Clear")) { _drTargetIdx = 0; DR_Suppress(2); }
                    });

                    // Client transform - live editing.
                    Row("Position (XYZ)", () => {
                        var v = _drPos;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat3("##dr_pos", ref v)) _drPos = v;

                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            var rotQ = EulerDegToQuat(new System.Numerics.Vector3(_drYawDeg, _drPitchDeg, _drRollDeg));
                            var dc2  = GetDragonClient(); var d2 = GetDragon();
                            if (dc2 != null) { dc2.LocalPosition = ToXnaVec(_drPos); dc2.LocalRotation = rotQ; }
                            if (d2  != null) { d2.LocalPosition  = ToXnaVec(_drPos); d2.LocalRotation  = rotQ; }
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    Row("Yaw / Pitch / Roll (deg)", () => {
                        var ypr = new System.Numerics.Vector3(_drYawDeg, _drPitchDeg, _drRollDeg);
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat3("##dr_ypr", ref ypr))
                        {
                            _drYawDeg   = ypr.X;
                            _drPitchDeg = ypr.Y;
                            _drRollDeg  = ypr.Z;
                        }
                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            var rotQ = EulerDegToQuat(new System.Numerics.Vector3(_drYawDeg, _drPitchDeg, _drRollDeg));
                            var dc2  = GetDragonClient(); var d2 = GetDragon();
                            if (dc2 != null) dc2.LocalRotation = rotQ;
                            if (d2  != null) d2.LocalRotation  = rotQ;
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    // Scale - live.
                    Row("Uniform Scale", () => {
                        float v = _drScaleUni;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.SliderFloat("##dr_scale_u", ref v, 0f, 50f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                        {
                            _drScaleUni = v;
                            _drScale3   = new System.Numerics.Vector3(v, v, v);
                        }
                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            _drScale3.X = ClampF(_drScale3.X, 0.01f, 100f);
                            _drScale3.Y = ClampF(_drScale3.Y, 0.01f, 100f);
                            _drScale3.Z = ClampF(_drScale3.Z, 0.01f, 100f);
                            var dc2 = GetDragonClient(); var d2 = GetDragon();
                            if (dc2 != null) dc2.LocalScale = ToXnaVec(_drScale3);
                            if (d2  != null) d2.LocalScale  = ToXnaVec(_drScale3);
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    Row("Scale (XYZ)", () => {
                        var v = _drScale3;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat3("##dr_scale_3", ref v))
                        {
                            _drScale3   = v;
                            _drScaleUni = (v.X + v.Y + v.Z) / 3f;
                        }
                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            _drScale3.X = ClampF(_drScale3.X, 0.01f, 100f);
                            _drScale3.Y = ClampF(_drScale3.Y, 0.01f, 100f);
                            _drScale3.Z = ClampF(_drScale3.Z, 0.01f, 100f);
                            var dc2 = GetDragonClient(); var d2 = GetDragon();
                            if (dc2 != null) dc2.LocalScale = ToXnaVec(_drScale3);
                            if (d2  != null) d2.LocalScale  = ToXnaVec(_drScale3);
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    ImGui.Separator();

                    // Server "intent" - live writes.
                    Row("Target Y/P/R (deg)", () => {
                        var ypr = new System.Numerics.Vector3(_drTargetYawDeg, _drTargetPitchDeg, _drTargetRollDeg);
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat3("##dr_typr", ref ypr))
                        {
                            _drTargetYawDeg   = ypr.X;
                            _drTargetPitchDeg = ypr.Y;
                            _drTargetRollDeg  = ypr.Z;
                        }
                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            var d2 = GetDragon();
                            if (d2 != null)
                            {
                                d2.TargetYaw   = ToRadians(_drTargetYawDeg);
                                d2.TargetPitch = ToRadians(_drTargetPitchDeg);
                                d2.TargetRoll  = ToRadians(_drTargetRollDeg);
                            }
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    Row("Velocity / TargetVel", () => {
                        ImGui.SetNextItemWidth(-1);
                        float v  = _drVel;
                        bool ch0 = ImGui.InputFloat("##dr_vel", ref v);
                        if (ch0) _drVel = Math.Max(0, v);

                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1);
                        float tv = _drTargetVel;
                        bool ch1 = ImGui.InputFloat("##dr_tvel", ref tv);
                        if (ch1) _drTargetVel = Math.Max(0, tv);

                        if ((ImGui.IsItemActive() || ImGui.IsItemEdited()) && (ch0 || ch1))
                        {
                            var d2 = GetDragon();
                            if (d2 != null)
                            {
                                d2.Velocity       = _drVel;
                                d2.TargetVelocity = _drTargetVel;
                            }
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    Row("Target Altitude", () => {
                        float v = _drTargetAlt;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat("##dr_alt", ref v)) _drTargetAlt = v;

                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            var d2 = GetDragon();
                            if (d2 != null) d2.TargetAltitude = _drTargetAlt;
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    // Animation (client) - change on selection.
                    Row("Animation", () => {
                        string cur = _drAnimNames[ClampI(_drAnimIndex, 0, _drAnimNames.Length - 1)];
                        if (ImGui.BeginCombo("##dr_anim", cur))
                        {
                            for (int i = 0; i < _drAnimNames.Length; i++)
                            {
                                bool sel = (i == _drAnimIndex);
                                if (ImGui.Selectable(_drAnimNames[i], sel))
                                {
                                    _drAnimIndex = i;
                                    var newAnim  = (DragonAnimEnum)ClampI(_drAnimIndex, 0, _drAnimNames.Length - 1);
                                    var dc2 = GetDragonClient(); var d2 = GetDragon();
                                    if (dc2 != null && dc2.CurrentAnimation != newAnim) dc2.CurrentAnimation = newAnim;
                                    if (d2  != null && d2.CurrentAnimation  != newAnim) d2.CurrentAnimation  = newAnim;
                                    DR_PauseAutoReadShort();
                                    DR_StartThawIfFrozen(_drThawOnApplyFrames);
                                }
                                if (sel) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                    });

                    Row("Clip Speed", () => {
                        float v = _drClipSpeed;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.SliderFloat("##dr_clipspeed", ref v, 0f, 3f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                            _drClipSpeed = v;

                        if (ImGui.IsItemActive() || ImGui.IsItemEdited())
                        {
                            var dc2 = GetDragonClient();
                            if (dc2 != null) dc2.ClipSpeed = _drClipSpeed;
                            DR_PauseAutoReadShort();
                            DR_StartThawIfFrozen(_drThawOnApplyFrames);
                        }
                    });

                    ImGui.Separator();

                    // Combat-ish (server).
                    Row("Shots Left", () => {
                        int v = _drShotsLeft;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputInt("##dr_shots", ref v)) _drShotsLeft = Math.Max(0, v);
                    });
                    Row("Time Before Next Shot", () => {
                        float v = _drTimeBeforeNextShot;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat("##dr_tbs", ref v)) _drTimeBeforeNextShot = Math.Max(0f, v);
                    });
                    Row("Time Til Shots Heard", () => {
                        float v = _drTimeTilShotsHeard;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat("##dr_ttsh", ref v)) _drTimeTilShotsHeard = Math.Max(0f, v);
                    });
                    Row("Loiters Left", () => {
                        int v = _drLoitersLeft;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputInt("##dr_loiters", ref v)) _drLoitersLeft = Math.Max(0, v);
                    });

                    // Debug.
                    Row("Dragon Time (read-only)", () => {
                        ImGui.TextDisabled($"{_drDragonTime:0.000}s");
                    });
                    #endregion

                    ImGui.EndTable();
                }

                ImGui.Separator();
                ImGui.TextDisabled("Note: Client-side only; host may override next tick.");

                ImGui.EndChild();
            }
        }
        #endregion

        #endregion

        #endregion

        #region Tab: Code-Injector

        /// <summary>
        /// CODE-INJECTOR TAB
        ///
        /// What this tab does:
        /// • Provides a multiline C# editor to run code live against the running game.
        /// • Optionally runs code on the CMZ main thread (safer for most game APIs).
        /// • Can keep/reuse the compiled assembly between runs for faster re-exec.
        /// • Shows result / exception output in a scrollable panel.
        ///
        /// Safety notes:
        /// • This is powerful and dangerous: User code runs in-process with full trust.
        /// • Prefer "Run on main thread" when touching XNA/CMZ objects (graphics/input/game state).
        /// • Disable hot-reload/keep assembly when you edit referenced types frequently.
        /// </summary>

        #region Tab: Static Data & UI State (Additions)

        // Editor buffer seeded with an example placeholder script.
        static string _codeBuffer = Script_Placeholder;

        // Execution options (live-toggled from the UI).
        static bool _runOnMainThread = true;  // MAIN THREAD: Good for game API calls (input, UI, graphics).
        static bool _keepAssembly    = false; // Reuse compiled assembly until you edit (faster re-runs).

        // UI state: Whether the editor was focused this frame (for Ctrl+Enter hotkey).
        static bool _editorFocused;

        // Scripts directory for load/save (*.cs).
        static string ScriptsDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(ScriptEngine).Namespace, "!CodeInjector");

        // Track edits so we can prompt before overwriting with examples/loads
        static string    _baselineText    = _codeBuffer; // Last "committed" content (on load/save/example).
        static bool      _isDirty         = false;       // Set true whenever user edits the buffer.

        // Load popup state
        static string[]  _scriptFiles     = Array.Empty<string>();
        static int       _loadSelectionIx = -1;

        // Window toggles.
        static bool      _showLoadWin     = false;
        static bool      _showSaveWin     = false;

        // Confirm/overwrite window.
        static bool      _showConfirmWin  = false;
        static string    _confirmTitle    = "";
        static string    _confirmMessage  = "";
        static Action    _confirmOnYes    = null;

        // Preview state.
        static string    _previewPath  = null;
        static string    _previewText  = "";   // What we render.
        static bool      _previewWrap  = true; // Text wrap toggle.
        static long      _previewSize  = 0;    // Bytes on disk.
        static DateTime  _previewMtime;        // Last write time.
        static Exception _previewError = null;

        // Extra save-confirm state (place near your other UI state fields).
        static bool   _saveConfirmOpen = false;
        static string _saveConfirmPath = null;

        const int MAX_PREVIEW_CHARS    = 256 * 1024;  // Cap preview size (256 KB).

        // Save popup state.
        static string _filenameBuffer  = "Script.cs"; // User-editable; default name.

        static void EnsureScriptsDir()
        {
            try { Directory.CreateDirectory(ScriptsDir); } catch { /* ignore */ }
        }

        static void MarkCommitted() { _baselineText = _codeBuffer; _isDirty = false; }
        static bool IsEdited() => !string.Equals(_codeBuffer ?? "", _baselineText ?? "", StringComparison.Ordinal);

        // Replace editor text with given script (and mark it clean).
        static void ReplaceEditorText(string script) { _codeBuffer = script ?? string.Empty; MarkCommitted(); }

        #region Confirm Window

        // Open a "Are you sure?" modal (runs action if confirmed).
        static void OpenConfirm(string title, string msg, Action onYes)
        {
            _confirmTitle = title ?? "Confirm";
            _confirmMessage = msg ?? "Are you sure?";
            _confirmOnYes = onYes;
            _showConfirmWin = true;
        }

        // Render shared confirm modal; call once per frame near the end of DrawCodeTab()
        static void DrawConfirmWindow()
        {
            if (!_showConfirmWin) return;

            // "Modal-like" behavior: always on top & focused when appearing
            ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.Appearing);
            ImGui.SetNextWindowFocus();

            // NoResize + NoCollapse helps keep it modal-ish
            if (ImGui.Begin(_confirmTitle, ref _showConfirmWin,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
            {
                ImGui.TextWrapped(_confirmMessage);
                ImGui.Separator();
                if (ImGui.Button("Overwrite", new Vector2(120, 0)))
                {
                    var action = _confirmOnYes;
                    _confirmOnYes = null;
                    _showConfirmWin = false;
                    action?.Invoke();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    _confirmOnYes = null;
                    _showConfirmWin = false;
                }
            }
            ImGui.End();
        }
        #endregion

        #region Preview Window

        // Load first N chars safely; allow file to be open by other apps
        static void LoadPreview(string path)
        {
            _previewPath = path;
            _previewText = "";
            _previewError = null;
            _previewSize = 0;
            _previewMtime = default;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                var fi = new FileInfo(path);
                _previewSize = fi.Length;
                _previewMtime = fi.LastWriteTime;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true))
                {
                    // read up to MAX_PREVIEW_CHARS
                    var buf = new char[Math.Min(MAX_PREVIEW_CHARS, (int)Math.Min(fi.Length, MAX_PREVIEW_CHARS))];
                    int read = sr.ReadBlock(buf, 0, buf.Length);
                    _previewText = new string(buf, 0, read);
                }

                if (_previewSize > MAX_PREVIEW_CHARS)
                    _previewText += "\n\n... (truncated preview) ...";
            }
            catch (Exception ex)
            {
                _previewError = ex;
                _previewText = ex.Message;
            }
        }
        #endregion

        #region Load Window

        // PREP the load content and open the popup
        static void OpenLoadWindow()
        {
            EnsureScriptsDir();
            _scriptFiles = Directory.EnumerateFiles(ScriptsDir, "*.cs", SearchOption.TopDirectoryOnly)
                                    .OrderBy(Path.GetFileName)
                                    .ToArray();
            _loadSelectionIx = _scriptFiles.Length > 0 ? 0 : -1;
            _showLoadWin = true;
        }

        // Render every frame (no custom _loadPopupOpen flag)
        static void DrawLoadWindow()
        {
            if (!_showLoadWin) return;

            ImGui.SetNextWindowSize(new Vector2(840, 560), ImGuiCond.Appearing);
            if (ImGui.Begin("Load Script", ref _showLoadWin,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
            {
                // Empty state
                if ((_scriptFiles?.Length ?? 0) == 0)
                {
                    ImGui.TextWrapped($"Folder:\n{ScriptsDir}");
                    ImGui.Separator();
                    ImGui.TextDisabled("No *.cs files found.");
                    ImGui.SameLine();
                    if (ImGui.Button("Refresh"))
                    {
                        EnsureScriptsDir();
                        _scriptFiles = Directory.EnumerateFiles(ScriptsDir, "*.cs")
                                                .OrderBy(Path.GetFileName)
                                                .ToArray();
                        _loadSelectionIx = _scriptFiles.Length > 0 ? 0 : -1;
                        if (_loadSelectionIx >= 0) LoadPreview(_scriptFiles[_loadSelectionIx]);
                    }
                    ImGui.End();
                    return;
                }

                // First-time selection -> ensure preview
                if (_loadSelectionIx < 0 && _scriptFiles.Length > 0)
                {
                    _loadSelectionIx = 0;
                    LoadPreview(_scriptFiles[0]);
                }

                if (ImGui.BeginTable("load_split", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("files", ImGuiTableColumnFlags.WidthFixed, 300f);
                    ImGui.TableSetupColumn("preview", ImGuiTableColumnFlags.WidthStretch);

                    // =========================
                    // LEFT COLUMN: file browser
                    // =========================
                    ImGui.TableNextColumn();

                    ImGui.TextDisabled("Folder:");
                    ImGui.PushTextWrapPos();
                    ImGui.TextWrapped(ScriptsDir);
                    ImGui.PopTextWrapPos();

                    ImGui.Separator();

                    // File list uses most of the left column; reserve space for 2 full-width buttons
                    var style = ImGui.GetStyle();
                    float btnH = ImGui.GetFrameHeight() + style.ItemSpacing.Y;
                    float listH = Math.Max(120f, ImGui.GetContentRegionAvail().Y - (btnH * 2 + style.ItemSpacing.Y));

                    if (ImGui.BeginChild("load_list", new Vector2(-1, listH), ImGuiChildFlags.Borders))
                    {
                        for (int i = 0; i < _scriptFiles.Length; i++)
                        {
                            string name = Path.GetFileName(_scriptFiles[i]);
                            bool selected = (i == _loadSelectionIx);

                            if (ImGui.Selectable(name, selected))
                            {
                                _loadSelectionIx = i;
                                LoadPreview(_scriptFiles[i]); // live preview on single-click
                            }

                            // Double-click to load immediately
                            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                void DoLoad()
                                {
                                    string text = File.ReadAllText(_scriptFiles[_loadSelectionIx]);
                                    ReplaceEditorText(text);
                                }
                                if (IsEdited())
                                    OpenConfirm("Confirm Overwrite", "Your current script has unsaved edits.\nLoad anyway?", DoLoad);
                                else
                                    DoLoad();

                                _showLoadWin = false;
                            }
                        }
                    }
                    ImGui.EndChild();

                    bool canLoad = _loadSelectionIx >= 0 && _loadSelectionIx < _scriptFiles.Length;

                    // Full-width buttons (stacked)
                    float fullW = ImGui.GetContentRegionAvail().X;
                    if (ImGui.Button("Load", new Vector2(fullW, 0)) && canLoad)
                    {
                        void DoLoad()
                        {
                            string text = File.ReadAllText(_scriptFiles[_loadSelectionIx]);
                            ReplaceEditorText(text);
                        }
                        if (IsEdited())
                            OpenConfirm("Confirm Overwrite", "Your current script has unsaved edits.\nLoad anyway?", DoLoad);
                        else
                            DoLoad();

                        _showLoadWin = false;
                    }

                    if (ImGui.Button("Cancel", new Vector2(fullW, 0)))
                        _showLoadWin = false;

                    // ==========================
                    // RIGHT COLUMN: file preview
                    // ==========================
                    ImGui.TableNextColumn();

                    // Header row: controls (left) + wrapped file meta (right)
                    if (ImGui.BeginTable("preview_header", 2, ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("controls", ImGuiTableColumnFlags.WidthFixed, 180f);
                        ImGui.TableSetupColumn("meta", ImGuiTableColumnFlags.WidthStretch);

                        // Controls
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Preview");
                        ImGui.SameLine();
                        if (ImGui.Button("Reload") && canLoad)
                            LoadPreview(_scriptFiles[_loadSelectionIx]);
                        ImGui.SameLine();
                        ImGui.Checkbox("Wrap", ref _previewWrap);

                        // Meta (wrapped inside its column)
                        ImGui.TableNextColumn();
                        if (!string.IsNullOrEmpty(_previewPath))
                        {
                            ImGui.PushTextWrapPos(); // wrap to column width
                            ImGui.TextDisabled(
                                $"{Path.GetFileName(_previewPath)} - {_previewSize:n0} bytes - {_previewMtime:g}\n{_previewPath}"
                            );
                            ImGui.PopTextWrapPos();
                        }

                        ImGui.EndTable();
                    }

                    ImGui.Separator();

                    // Preview content area
                    if (ImGui.BeginChild("preview_child",
                                         new Vector2(0, 0),
                                         ImGuiChildFlags.Borders,
                                         ImGuiWindowFlags.AlwaysVerticalScrollbar |
                                         (_previewWrap ? 0 : ImGuiWindowFlags.HorizontalScrollbar)))
                    {
                        // Simple, read-only preview. TextWrapped respects PushTextWrapPos.
                        if (_previewWrap) ImGui.PushTextWrapPos();
                        string tmp = _previewText ?? "(empty)";
                        ImGui.TextUnformatted(tmp);
                        if (_previewWrap) ImGui.PopTextWrapPos();

                        // Show preview error detail, if any.
                        if (_previewError != null)
                        {
                            ImGui.Separator();
                            ImGui.TextColored(new Vector4(1, 0.7f, 0.2f, 1), "Preview error:");
                            ImGui.PushTextWrapPos();
                            ImGui.TextWrapped(_previewError.ToString());
                            ImGui.PopTextWrapPos();
                        }
                    }
                    ImGui.EndChild();

                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }
        #endregion

        #region Save Window

        // Small helper to write & finish (common to normal-save and overwrite-save)
        static void SaveScriptTo(string path)
        {
            EnsureScriptsDir();
            File.WriteAllText(path, _codeBuffer ?? string.Empty);
            MarkCommitted();
            _lastResultHeader = "Result:";
            _lastResultText = $"Saved: {path}";
            _showSaveWin = false;
        }

        // Overwrite confirm modal (called from DrawSaveWindow)
        static void DrawSaveConfirmModal()
        {
            if (!_saveConfirmOpen) return;

            bool open = true;
            if (ImGui.BeginPopupModal("Overwrite file?", ref open,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.TextWrapped("A file with this name already exists. Overwrite it?");
                ImGui.Separator();
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(_saveConfirmPath ?? "(null)");
                ImGui.PopTextWrapPos();

                ImGui.Separator();

                // Full-width buttons inside the modal
                float fullW = ImGui.GetContentRegionAvail().X;
                if (ImGui.Button("Overwrite", new Vector2(fullW, 0)))
                {
                    try { SaveScriptTo(_saveConfirmPath); }
                    catch (Exception ex) { _lastResultHeader = "Error:"; _lastResultText = ex.ToString(); }
                    _saveConfirmOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.Button("Cancel", new Vector2(fullW, 0)))
                {
                    _saveConfirmOpen = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
            if (!open) _saveConfirmOpen = false;
        }

        // PREP and open
        static void OpenSaveWindow()
        {
            EnsureScriptsDir();
            if (string.IsNullOrWhiteSpace(_filenameBuffer))
                _filenameBuffer = "Script.cs";
            _showSaveWin = true;
        }

        // Render every frame
        static void DrawSaveWindow()
        {
            if (!_showSaveWin) return;

            ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
            if (ImGui.Begin("Save Script As", ref _showSaveWin,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
            {
                // Folder (wrapped)
                ImGui.TextDisabled("Folder:");
                ImGui.PushTextWrapPos();
                ImGui.TextWrapped(ScriptsDir);
                ImGui.PopTextWrapPos();

                ImGui.Separator();

                // Filename entry
                ImGui.InputText("Filename (*.cs)", ref _filenameBuffer, 256);

                // Resolve final name (.cs enforced only at save time)
                string finalName = _filenameBuffer?.Trim() ?? "";
                if (!string.IsNullOrEmpty(finalName) &&
                    !finalName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    // Don't mutate the user buffer; just compute the path with .cs appended
                    finalName += ".cs";
                }

                // Validate filename (basic)
                bool isEmpty = string.IsNullOrWhiteSpace(finalName);
                bool hasInvalid = !isEmpty && finalName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

                // Show full target path (wrapped)
                string targetPath = isEmpty || hasInvalid ? null : Path.Combine(ScriptsDir, finalName);
                ImGui.Separator();
                ImGui.TextDisabled("Target:");
                ImGui.PushTextWrapPos();
                if (hasInvalid)
                    ImGui.TextColored(new Vector4(1, 0.6f, 0.2f, 1), "Filename contains invalid characters.");
                else if (isEmpty)
                    ImGui.TextDisabled("(enter a file name)");
                else
                    ImGui.TextWrapped(targetPath);
                ImGui.PopTextWrapPos();

                ImGui.Separator();

                // Full-width stacked buttons
                float fullW = ImGui.GetContentRegionAvail().X;

                ImGui.BeginDisabled(isEmpty || hasInvalid);
                if (ImGui.Button("Save", new Vector2(fullW, 0)))
                {
                    try
                    {
                        // Create folder & decide overwrite
                        EnsureScriptsDir();
                        bool exists = File.Exists(targetPath);
                        if (exists)
                        {
                            _saveConfirmPath = targetPath;
                            _saveConfirmOpen = true;
                            ImGui.OpenPopup("Overwrite file?");
                        }
                        else
                        {
                            SaveScriptTo(targetPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastResultHeader = "Error:";
                        _lastResultText = ex.ToString();
                    }
                }
                ImGui.EndDisabled();

                if (ImGui.Button("Cancel", new Vector2(fullW, 0)))
                    _showSaveWin = false;

                // Draw confirm modal last so it floats above
                DrawSaveConfirmModal();
            }
            ImGui.End();
        }
        #endregion

        #endregion

        #region [+] Main Draw

        private unsafe static void DrawCodeTab()
        {
            #region Top Row: Help And Toggles

            /// <summary>
            /// Small helper row:
            /// • "Run on main thread"     - Posts execution to CMZ's game thread (safer for XNA/CMZ APIs).
            /// • "Keep compiled assembly" - Reuse last compiled DLL for faster re-runs if the code didn't change.
            /// </summary>

            ImGui.TextWrapped("C# evaluator (dangerous!). Use at your own risk.");
            if (ImGui.BeginTable("code_top_controls", 1, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("text", ImGuiTableColumnFlags.WidthFixed, -1f);

                ImGui.TableNextColumn();
                ImGui.Checkbox("Run on main thread", ref _runOnMainThread);  // Safer for CMZ/XNA APIs.
                ImGui.SameLine();
                ImGui.Checkbox("Keep compiled assembly", ref _keepAssembly); // Faster re-runs (no recompile).
                ImGui.AlignTextToFramePadding(); ImGui.SameLine();
                ImGui.TextDisabled("|");         ImGui.SameLine();
                ImGui.TextDisabled("Ctrl+Enter runs while editor focused."); // Quick keyboard workflow.

                ImGui.EndTable();
            }
            ImGui.Separator();

            #endregion

            #region [+] Examples Row: Simple / Advanced (Two Rows)

            /// <summary>
            /// Two-row "Examples" section:
            /// • Left column is a label ("Simple" / "Advanced").
            /// • Right column renders a row of equal-width buttons that fill available space.
            /// • Clicking replaces editor text with a pre-baked script.
            /// • If the editor has unsaved edits, we show a confirm dialog before overwriting.
            /// </summary>

            ImGui.TextWrapped("Examples:");
            if (ImGui.BeginTable("code_examples", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("label",   ImGuiTableColumnFlags.WidthFixed, 0f);
                ImGui.TableSetupColumn("content", ImGuiTableColumnFlags.WidthStretch);

                // Confirm dialog strings reused by all example buttons.
                const string openConfirmTitle = "Confirm Overwrite";
                const string openConfirmMsg   = "Your current script has unsaved edits.\nReplace with Simple example?";

                // Row 1: Simple examples.
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Simple   |");
                ImGui.TableNextColumn();

                #region Buttons: Simple Examples

                /// <summary>
                /// DrawEqualButtonsRow => computes per-button width from the current column's free width
                /// and lays them out as equally-sized buttons that expand/shrink with the panel.
                /// </summary>
                DrawEqualButtonsRow(new (string, Action)[]
                {
                    ("Teleport Player",  () => { void load() => ReplaceEditorText(Example_Simple_TeleportPlayer); if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("List All Gamers",  () => { void load() => ReplaceEditorText(Example_Simple_ListAllGamers);  if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Play Sound",       () => { void load() => ReplaceEditorText(Example_Simple_PlaySound);      if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Show Position",    () => { void load() => ReplaceEditorText(Example_Simple_ShowPosition);   if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Place Loot Boxes", () => { void load() => ReplaceEditorText(Example_Simple_PlaceLootBoxes); if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Give Builder Kit", () => { void load() => ReplaceEditorText(Example_Simple_GiveBuilderKit); if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                });
                #endregion

                // Row 2: Advanced examples.
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Advanced |");
                ImGui.TableNextColumn();

                #region Buttons: Advanced Examples

                DrawEqualButtonsRow(new (string, Action)[]
                {
                    ("Disable Control",  () => { void load() => ReplaceEditorText(Example_Advanced_DisableControls); if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Restore Control",  () => { void load() => ReplaceEditorText(Example_Advanced_EnableControls);  if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Infinite Vitals",  () => { void load() => ReplaceEditorText(Example_Advanced_InfiniteVitals);  if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Restore Vitals",   () => { void load() => ReplaceEditorText(Example_Advanced_RestoreVitals);   if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Freeze Noon",      () => { void load() => ReplaceEditorText(Example_Advanced_FreezeNoon);      if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                    ("Restore Time",     () => { void load() => ReplaceEditorText(Example_Advanced_RestoreTime);     if (IsEdited()) OpenConfirm(openConfirmTitle, openConfirmMsg, load); else load(); }),
                });
                #endregion

                ImGui.EndTable();
            }
            ImGui.Separator();

            #endregion

            #region Utilities Row: (Load / Save / Run / Clear / Copy buttons)

            /// <summary>
            /// A slim, 4-column layout:
            /// [Actions:] [Execute | Clear | Copy Script | Copy Output] | [File:] [Load Script | Save Script]
            /// </summary>
            if (ImGui.BeginTable("code_top_utility_buttons", 4, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("label_file",      ImGuiTableColumnFlags.WidthFixed, 0f);
                ImGui.TableSetupColumn("buttons_file",    ImGuiTableColumnFlags.WidthFixed, 0f);
                ImGui.TableSetupColumn("label_actions",   ImGuiTableColumnFlags.WidthFixed, 0f);
                ImGui.TableSetupColumn("buttons_utility", ImGuiTableColumnFlags.WidthFixed, -1f);

                // Left cell: Section label, vertically aligned to buttons.
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Actions:");

                // File buttons cell.
                ImGui.TableNextColumn();
                {
                    float cellW = ImGui.GetColumnWidth();
                    float btnW  = 0; // Use the buttons text width.
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float total = btnW * 3 + gap * 2; // Run | Clear | Copy Script | (Copy Output is auto-sized).

                    // Push the cursor so the group hugs the right edge of this column.
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (cellW - total - ImGui.GetStyle().CellPadding.X));

                    ImGui.SameLine();
                    if (ImGui.Button("Execute", new Vector2(btnW, 0))) RunCurrentCode();
                    ImGui.SameLine();
                    if (ImGui.Button("Clear", new Vector2(btnW, 0))) _codeBuffer = string.Empty;
                    ImGui.SameLine();
                    if (ImGui.Button("Copy Script", new Vector2(btnW, 0))) ImGui.SetClipboardText(_codeBuffer ?? string.Empty);
                    ImGui.SameLine();
                    if (ImGui.Button("Copy Output")) ImGui.SetClipboardText(_lastResultText ?? string.Empty);
                }

                // File label cell.
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("|"); ImGui.SameLine(); ImGui.Text("File:");

                // Right cell: Right-aligned buttons.
                ImGui.TableNextColumn();
                {
                    float cellW = ImGui.GetColumnWidth();
                    float btnW  = 0; // Use the buttons text width.
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float total = btnW * 3 + gap * 2; // Load | Save.

                    // Push the cursor so the group hugs the right edge of this column.
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (cellW - total - ImGui.GetStyle().CellPadding.X));

                    ImGui.SameLine();
                    if (ImGui.Button("Load Script", new Vector2(btnW, 0))) OpenLoadWindow();
                    ImGui.SameLine();
                    if (ImGui.Button("Save Script", new Vector2(btnW, 0))) OpenSaveWindow();
                }

                ImGui.EndTable();
            }
            ImGui.Separator();

            #endregion

            #region Code Editor Panel

            /// <summary>
            /// The editor lives in its own scrollable child with a transparent InputText frame so
            /// the child's background shows through. We size the input to content to make the child scroll,
            /// not the InputTextMultiline itself (nicer UX).
            /// </summary>

            // Height budgeting.
            var   style = ImGui.GetStyle();
            float avail = ImGui.GetContentRegionAvail().Y;

            // Reserve height for the output panel under the editor.
            const float OUTPUT_CHILD_H = 140f;
            float sepY        = 2f + style.ItemSpacing.Y;
            float outputH     = ImGui.CalcTextSize("Result:").Y + style.ItemSpacing.Y + OUTPUT_CHILD_H;
            float editorAvail = Math.Max(0f, avail - (sepY + outputH));

            // Give the child a themed background. (Packed uint; avoids overload confusion.)
            uint childBg = ImGui.GetColorU32(ImGuiCol.FrameBg);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, childBg);

            // Padding: Keep content away from borders; 0 vertical spacing inside the child
            // so the "exact size" math behaves (user controls scrollbars on the child, not the input).
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5f, 5f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(style.ItemSpacing.X, 0f));

            if (ImGui.BeginChild("code_editor_scroll",
                                 new Vector2(0, editorAvail),
                                 ImGuiChildFlags.Borders,
                                 ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.PushFont(ImGui.GetIO().FontDefault);

                // Content-driven sizing so the CHILD scrolls (not the InputTextMultiline frame).
                float contentW = CalcContentWidth(_codeBuffer);  // Measure longest line.
                float contentH = CalcContentHeight(_codeBuffer); // Rows * line-height.

                // Fill the child horizontally; add -1 to avoid hugging the border (cosmetic).
                float desiredW = Math.Max(ImGui.GetContentRegionAvail().X, contentW) - 1f;
                // Make the widget tall enough so it doesn't spawn its own vertical scrollbar.
                float desiredH = Math.Max(ImGui.GetContentRegionAvail().Y + 1f, contentH);

                // Make the input background transparent so the child background shows.
                // NOTE: In some bindings PushStyleColor expects a packed uint - use GetColorU32/IM_COL32 if needed.
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0f));

                var flags    = ImGuiInputTextFlags.AllowTabInput | ImGuiInputTextFlags.NoHorizontalScroll;
                bool changed = ImGui.InputTextMultiline("##code",
                                         ref _codeBuffer,
                                         64 * 1024,
                                         new Vector2(desiredW, desiredH),
                                         flags);

                ImGui.PopStyleColor(); // FrameBg (transparent).

                if (changed) _isDirty = !string.Equals(_codeBuffer, _baselineText, StringComparison.Ordinal);

                // Record focus to enable Ctrl+Enter.
                _editorFocused = ImGui.IsItemActive() || ImGui.IsItemFocused();
                if (_editorFocused && ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.Enter, true))
                    RunCurrentCode();

                ImGui.PopFont();
            }
            ImGui.EndChild();

            ImGui.PopStyleColor(); // ChildBg.
            ImGui.PopStyleVar(2);  // WindowPadding, ItemSpacing.

            #endregion

            #region Output Panel

            /// <summary>
            /// "Result:" header + a dedicated scrollable region that only scrolls the output,
            /// so the overall window doesn't jump around.
            /// </summary>

            ImGui.Separator();
            ImGui.TextUnformatted(_lastResultHeader);

            // A separate child so only the output scrolls (keeps main window stable).
            if (ImGui.BeginChild("code_result",
                                 new Vector2(0, OUTPUT_CHILD_H),
                                 ImGuiChildFlags.Borders,
                                 ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.TextUnformatted(_lastResultText ?? "(no output)");
            }
            ImGui.EndChild();

            #endregion

            // Popups / Modals rendered last so they stack above everything else.
            DrawLoadWindow();
            DrawSaveWindow();
            DrawConfirmWindow();
        }
        #endregion

        #region Execution: Compile & Run Current Code

        // Output state shown in the bottom panel.
        static string _lastResultHeader = "Result:";
        static string _lastResultText   = "";

        /// <summary>
        /// RunCurrentCode()
        /// • Compiles & runs the user code (ScriptEngine.Run), optionally on the main thread.
        /// • Captures result/exception text and writes it to the output panel.
        /// • Emits short log entries to your ChatLog/Log sinks.
        /// </summary>
        static void RunCurrentCode()
        {
            try
            {
                if (_runOnMainThread)
                {
                    // Post work to the game thread; most CMZ/XNA APIs are not thread-safe.
                    MainThread.Post(() =>
                    {
                        try
                        {
                            ChatLog.Append("Script", "Executing script."); Log("[Script] Executing script.");
                            var (ok, result, log) = ScriptEngine.Run(_codeBuffer, keepAssembly: _keepAssembly);

                            _lastResultHeader = ok ? "Result:" : "Error:";
                            _lastResultText   = (result ?? "(null)") + (string.IsNullOrEmpty(log) ? "" : "\n" + log);

                            if (ok) { ChatLog.Append("Script", $"Script result: {result}."); Log($"[Script] Script result: {result}"); }
                            else    { ChatLog.Append("Error",  $"Stacktrace - {log}.");      Log("[Script] Script result: Error.");    }
                        }
                        catch (Exception ex)
                        {
                            _lastResultHeader = "Error:";
                            _lastResultText   = ex.ToString();
                        }
                    });
                }
                else
                {
                    // Background thread mode (only safe for pure logic / no game API calls).
                    var (ok, result, log) = ScriptEngine.Run(_codeBuffer, keepAssembly: _keepAssembly);
                    _lastResultHeader     = ok ? "Result:" : "Error:";
                    _lastResultText       = (result ?? "(null)") + (string.IsNullOrEmpty(log) ? "" : "\n" + log);
                }
            }
            catch (Exception ex)
            {
                _lastResultHeader = "Error:";
                _lastResultText   = ex.ToString();
            }
        }
        #endregion

        #region Helper: Equal Table Buttons

        /// <summary>
        /// Draws a single horizontal row of buttons that share the available width equally.
        /// • Uses the current column's remaining width (ImGui.GetContentRegionAvail().X).
        /// • Respects ImGui item spacing between buttons.
        /// • Enforces a minimum width per button (minWidth).
        /// • Calls each button's onClick when pressed.
        /// Tip: Call this while you're inside the target table/column you want to fill.
        /// </summary>
        static void DrawEqualButtonsRow((string label, Action onClick)[] buttons, float minWidth = 90f)
        {
            // Defensive: Nothing to draw.
            int n = buttons?.Length ?? 0;
            if (n == 0) return;

            // How much horizontal space is left in THIS column right now.
            float avail   = ImGui.GetContentRegionAvail().X;

            // Horizontal spacing ImGui will place between items when we use SameLine().
            float spacing = ImGui.GetStyle().ItemSpacing.X;

            // Compute each button's width so n buttons + (n-1) spacings fill the column.
            // Clamp to a minimum so tiny layouts don't produce unusable buttons.
            float width   = Math.Max(minWidth, (avail - spacing * (n - 1)) / n);

            // Emit buttons side-by-side.
            for (int i = 0; i < n; i++)
            {
                // Draw the button with the calculated width.
                if (ImGui.Button(buttons[i].label, new Vector2(width, 0)))
                    buttons[i].onClick?.Invoke(); // Fire the action if clicked.

                // Add spacing between buttons (but not after the last one).
                if (i < n - 1)
                    ImGui.SameLine();
            }
        }
        #endregion

        #endregion

        #region Tab: Network-Sniffer

        /// <summary>
        /// ======================================================================================
        /// Network-Sniffer
        /// --------------------------------------------------------------------------------------
        /// SUMMARY:
        ///   Displays a passive, read-only view of CastleMinerZ / Steam UDP/TCP traffic that
        ///   the IpResolver correlated to local processes/ports. Supports masking your local IP,
        ///   quick CSV/JSON exports, per-row context menu (copy fields / open maps), and simple
        ///   host/peer heuristics when you are a client.
        ///
        /// KEY IDEAS:
        ///   • Single shared IpResolver instance (unique rows by composite key inside resolver).
        ///   • "Hide my IP" masks local endpoints in both UI and export (optional).
        ///   • If not host, view is filtered to the guessed host/server endpoint.
        ///   • Columns are resizable (ImGuiTableFlags.Resizable); rows newest-first.
        ///   • Right-click a row to copy cell values or open a Google Maps lookup.
        ///   • "Reset" clears sniffer rows, geo cache, refreshes ports, and re-derives host/my IP.
        ///
        /// NOTES:
        ///   • UI code assumes XNA/WinForms context is STA for SaveFileDialog.
        ///   • Geo lookups are synchronous through IpGeoCache.GetOrFetch (cached w/ TTL).
        ///   • Save "Quick" writes to !Mods/<Namespace>/!Logs with timestamped filenames.
        ///   • No blocking network calls occur on the UI thread except cached Geo when exporting.
        /// ======================================================================================
        /// </summary>

        #region UI State & Fields

        static readonly IpResolver _ipResolver         = new IpResolver();  // Shared resolver used by this tab (and other test hooks).
        static bool                _nsEnableCapture    = false;             // Master enable/disable for packet capture (UI-controlled).
        static bool                _nsHideMyIp         = true;              // If true, the UI masks the user's own local IP.
        static string              _nsMyIpGuess        = null;              // Heuristically-learned local (LAN) IP (used when masking).
        static string              _nsHostIpGuess      = null;              // Heuristically-learned host/server public IP (when not hosting).
        static readonly DateTime   _nsLastRefresh      = DateTime.MinValue; // Not used in this snippet (reserved for future throttling).
        private static bool        _nsSaveMaskedInFile = true;              // When saving, mirror the UI's "Hide my IP" unless overridden here.
        private static string      _nsLastSavePath     = null;              // Tracks the last chosen path (Save/Save As) for convenience.
        static bool                _nsSavePopupPrimed  = false;             // Marks that the save popup's defaults have been applied for the current open.

        #endregion

        #region Network-Sniffer Control API (Patch-safe)

        /// <summary>
        /// Hard-stop packet capture and optionally clear volatile state.
        /// Safe to call from Harmony patches (disconnect/session ended).
        /// </summary>
        internal static void NetSniffer_Stop(bool clearRows = false, bool clearGeoCache = false)
        {
            // Flip the UI toggle so it visibly turns off.
            _nsEnableCapture = false;

            // Stop capture (safe even if not started).
            try { _ipResolver.Stop(); } catch { }

            // Forget heuristics so we don't attach stale host/my-ip.
            _nsHostIpGuess = null;
            _nsMyIpGuess   = null;

            if (clearRows)
            {
                try { _ipResolver.ClearRows(); } catch { }
            }

            if (clearGeoCache)
            {
                try { IpGeoCache.Clear(); } catch { }
            }
        }
        #endregion

        /// <summary>
        /// Draws the Network-Sniffer tab.
        /// - Passive capture & display of UDP/TCP flows (unique rows).
        /// - Honors "Hide my IP".
        /// - If not host, focuses view on the host/server IP to reduce noise.
        /// - Provides Reset/Clear, Save, and per-row right-click actions.
        /// </summary>
        private static void DrawNetworkSnifferTab()
        {
            #region Environment Check

            var game    = CastleMinerZGame.Instance;
            bool inGame = CastleWallsMk2.IsInGame(); // (currently informational).
            bool amHost = false;
            if (game != null && game.MyNetworkGamer != null)
                amHost = game.MyNetworkGamer.IsHost;

            #endregion

            #region Top Toolbar

            // Summary banner + hosting hint.
            ImGui.TextWrapped("Passive capture of CastleMinerZ / Steam UDP traffic.");
            ImGui.SameLine(); ImGui.TextDisabled("|");
            ImGui.SameLine(); ImGui.TextDisabled(amHost
                ? "Hosting: All peers are visible."
                : "Client: Only the host/server endpoint is discoverable.");

            // Capture toggle + ensure Npcap is installed.
            bool enable = _nsEnableCapture;
            if (ImGui.Checkbox("Enable capture", ref enable))
            {
                _nsEnableCapture = enable;
                if (enable)
                {
                    // Log all embedded resourceName's.
                    // foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames()) SendLog("[Res] " + n);

                    if (!NpcapRuntime.IsCaptureLibraryAvailable(out string why))
                    {
                        var res = MessageBox.Show(
                            "Npcap is required to enable packet capture on Windows.\n\n" +
                            "Install Npcap now?\n\n" +
                            "Note: This will show a Windows UAC prompt (admin required).",
                            "Npcap required",
                            MessageBoxButtons.OKCancel,
                            System.Windows.Forms.MessageBoxIcon.Information);

                        if (res == DialogResult.OK)
                        {
                            if (NpcapInstaller.LaunchEmbeddedNpcapInstaller("CastleWallsMk2.Embedded.npcap-1.87.exe", out string err))
                                SendLog("[Net] Npcap installer launched. Finish install, then re-enable capture.");
                            else
                                SendLog($"[Net] Npcap install not started: {err}.");
                        }

                        // Force toggle back off.
                        enable = false;
                        _nsEnableCapture = false;
                        return; // Bail out so we don't call _ipResolver.Start().
                    }

                    // Npcap present -> Start capture.
                    _nsEnableCapture = true;
                    try { _ipResolver.Start(); }
                    catch (Exception ex)
                    {
                        _nsEnableCapture = false;
                        enable = false;
                        SendLog($"[Net] start failed: {ex.Message}.");
                    }
                }
                else
                {
                    NetSniffer_Stop(clearRows: false, clearGeoCache: false);
                }
            }

            // Hide-my-IP toggle (mirrors NetCaptureConfig flag).
            ImGui.SameLine();
            if (ImGui.Checkbox("Hide my IP", ref _nsHideMyIp))
            {
                // Keep runtime sniffer config coherent with UI preference.
                NetCaptureConfig.HideOwnIp = _nsHideMyIp;
            }

            // Refresh ports (forces resolver to rebuild port map; resets heuristics).
            ImGui.SameLine();
            if (ImGui.Button("Refresh Ports"))
            {
                _ipResolver.RefreshPorts();
                _nsHostIpGuess = null; // Re-learn.
                _nsMyIpGuess   = null;
            }

            // One-click reset (rows + geo cache + port map + heuristics).
            ImGui.SameLine();
            if (ImGui.Button("Reset (clear & re-learn)"))
            {
                // Clear sniffer rows + refresh port map.
                try { _ipResolver.Reset(refreshPorts: true); } catch { }

                // Nuke geo cache so new lookups happen.
                try { IpGeoCache.Clear(); } catch { }

                // Forget heuristics so they re-derive on new traffic.
                _nsHostIpGuess = null;
                _nsMyIpGuess = null;

                SendLog("[Net] Reset: Cleared rows, geo cache, and refreshed ports.");
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Clears captured rows, wipes geo cache, refreshes ports, and re-derives host/my IP on next packets.");

            // Clear rows only (keep caches/ports intact).
            ImGui.SameLine();
            if (ImGui.Button("Clear Rows"))
            {
                _ipResolver.ClearRows();
                _nsHostIpGuess = null;
                _nsMyIpGuess   = null;
            }

            // Save menu trigger.
            ImGui.SameLine();
            if (ImGui.Button("Save..."))
            {
                _nsSavePopupPrimed = false; // Re-prime defaults next open.
                ImGui.OpenPopup("net_sniff_save_popup");
            }

            // Device/row status line.
            {
                var info = _ipResolver.GetInfo();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1f),
                    string.Format("Device: {0} | Ports: {1} | Rows: {2}",
                        info.DeviceName ?? "(none)",
                        info.PortCount,
                        info.RowCount));
            }

            ImGui.Separator();

            #endregion

            #region Snapshot & Filter

            // Immutable snapshot for this frame.
            var rows = _ipResolver.GetSnapshot(); // List<IpResolver.SniffRow>.

            // Learn my LAN IP (used for masking).
            if (_nsHideMyIp && string.IsNullOrEmpty(_nsMyIpGuess))
            {
                _nsMyIpGuess = GuessMyIp(rows);
            }

            // Client mode: Filter down to host/server endpoint until we know it.
            if (!amHost)
            {
                if (string.IsNullOrEmpty(_nsHostIpGuess))
                    _nsHostIpGuess = GuessHostIp(rows);

                if (!string.IsNullOrEmpty(_nsHostIpGuess))
                {
                    rows = rows
                        .Where(r =>
                               string.Equals(r.RemoteIp, _nsHostIpGuess, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(r.LocalIp, _nsHostIpGuess, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    // Empty state until enough traffic reveals host.
                    rows = new List<IpResolver.SniffRow>();
                }
            }
            #endregion

            #region Table Rendering

            var avail = ImGui.GetContentRegionAvail();
            if (ImGui.BeginTable("netSniffTable", 9,
                ImGuiTableFlags.RowBg
              | ImGuiTableFlags.Borders
              | ImGuiTableFlags.ScrollY
              | ImGuiTableFlags.Resizable // NOTE: Column edges are draggable.
              | ImGuiTableFlags.SizingFixedFit,
                new Vector2(avail.X, avail.Y)))
            {
                // Column schema (order mirrors CSV save).
                ImGui.TableSetupColumn("#",            ImGuiTableColumnFlags.WidthFixed,   15f);
                ImGui.TableSetupColumn("Proc",         ImGuiTableColumnFlags.WidthFixed,   35f);
                ImGui.TableSetupColumn("PID",          ImGuiTableColumnFlags.WidthFixed,   35f);
                ImGui.TableSetupColumn("Dir",          ImGuiTableColumnFlags.WidthFixed,   25f);
                ImGui.TableSetupColumn("Local",        ImGuiTableColumnFlags.WidthStretch, 0.35f);
                ImGui.TableSetupColumn("Remote",       ImGuiTableColumnFlags.WidthStretch, 0.85f);
                ImGui.TableSetupColumn("Geo-Location", ImGuiTableColumnFlags.WidthStretch, 1.3f);
                ImGui.TableSetupColumn("ISP",          ImGuiTableColumnFlags.WidthStretch, 0.75f);
                ImGui.TableSetupColumn("Seen",         ImGuiTableColumnFlags.WidthFixed,   60f);
                ImGui.TableHeadersRow();

                // Newest-first view (stable per-frame).
                rows = rows.OrderByDescending(r => r.LastSeenUtc).ToList();

                int idx = 0;
                foreach (var r in rows)
                {
                    ImGui.TableNextRow();

                    #region Row Context Menu

                    ImGui.TableSetColumnIndex(0);
                    ImGui.PushID(idx); // Unique per row.

                    // Span-all-columns invisible selectable to attach context menu to entire row area.
                    float rowStartY = ImGui.GetCursorPosY();
                    ImGui.Selectable("##row_span", false,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap,
                        new Vector2(0, 0));
                    ImGui.SetCursorPosY(rowStartY);

                    // Context menu (copy/lookup helpers).
                    if (ImGui.BeginPopupContextItem())
                    {
                        // Copy individual cells (respects masking for Local/Remote).
                        if (ImGui.MenuItem("Copy Process")) ImGui.SetClipboardText(r.Process ?? "");
                        if (ImGui.MenuItem("Copy PID")) ImGui.SetClipboardText(r.Pid.ToString());
                        if (ImGui.MenuItem("Copy Dir")) ImGui.SetClipboardText(r.Dir ?? "");

                        string localDisplayed  = MaskIp(r.LocalIp, r.LocalPort);
                        string remoteDisplayed = MaskIp(r.RemoteIp, r.RemotePort);
                        string geoDisplayed    = GetRowGeo(r);
                        string ispDisplayed    = GetRowIsp(r);

                        if (ImGui.MenuItem("Copy Local")) ImGui.SetClipboardText(localDisplayed);
                        if (ImGui.MenuItem("Copy Remote")) ImGui.SetClipboardText(remoteDisplayed);
                        if (ImGui.MenuItem("Copy Seen")) ImGui.SetClipboardText(r.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss"));
                        if (ImGui.MenuItem("Copy Geo")) ImGui.SetClipboardText(geoDisplayed ?? "");
                        if (ImGui.MenuItem("Copy ISP")) ImGui.SetClipboardText(ispDisplayed ?? "");

                        ImGui.Separator();

                        // Geo lookups (prefer remote, skip private IPs).
                        bool canRemote = !string.IsNullOrEmpty(r.RemoteIp) && !IsPrivate(r.RemoteIp);
                        if (canRemote && ImGui.MenuItem("Lookup Geo (remote) in Google Maps"))
                        {
                            var info = IpGeoCache.GetOrFetch(r.RemoteIp);
                            string place = $"{info.City}, {info.Region}, {info.Country}" ?? r.RemoteIp;
                            IpGeoCache.OpenInGoogleMaps(info?.Lat, info?.Lon, place);
                            ImGui.CloseCurrentPopup();
                        }

                        // Allow local too if it's public (rare on WAN NAT).
                        bool canLocal = !string.IsNullOrEmpty(r.LocalIp) && !IsPrivate(r.LocalIp);
                        if (canLocal && ImGui.MenuItem("Lookup Geo (local) in Google Maps"))
                        {
                            var info = IpGeoCache.GetOrFetch(r.LocalIp);
                            string place = $"{info.City}, {info.Region}, {info.Country}" ?? r.LocalIp;
                            IpGeoCache.OpenInGoogleMaps(info?.Lat, info?.Lon, place);
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.Separator();

                        // Combined copies.
                        if (ImGui.MenuItem("Copy line (CSV)")) ImGui.SetClipboardText(RowToCsv(r, localDisplayed, remoteDisplayed, geoDisplayed, ispDisplayed));
                        if (ImGui.MenuItem("Copy line (JSON)")) ImGui.SetClipboardText(RowToJson(r, localDisplayed, remoteDisplayed, geoDisplayed, ispDisplayed));
                        if (ImGui.MenuItem("Copy summary"))     ImGui.SetClipboardText($"{r.Process} (PID {r.Pid}) {r.Dir} {localDisplayed} -> {remoteDisplayed}");

                        // Unmasked variants.
                        ImGui.Separator();
                        if (ImGui.MenuItem("Copy Local (raw)"))   ImGui.SetClipboardText($"{r.LocalIp}:{r.LocalPort}");
                        if (ImGui.MenuItem("Copy Remote (raw)"))  ImGui.SetClipboardText($"{r.RemoteIp}:{r.RemotePort}");

                        ImGui.EndPopup();
                    }
                    #endregion

                    #region Row Cells

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted((++idx).ToString());

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(r.Process ?? "?");

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(r.Pid.ToString());

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(r.Dir ?? "?");

                    // Local endpoint (masked if requested).
                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(MaskIp(r.LocalIp, r.LocalPort));

                    // Remote endpoint (masked if requested).
                    ImGui.TableSetColumnIndex(5);
                    ImGui.TextUnformatted(MaskIp(r.RemoteIp, r.RemotePort));

                    // Geo-Location (resolver-populated; "-" if private or no lookup yet).
                    ImGui.TableSetColumnIndex(6);
                    ImGui.TextUnformatted(string.IsNullOrEmpty(r.Geo) ? "-" : r.Geo);

                    // ISP.
                    ImGui.TableSetColumnIndex(7);
                    ImGui.TextUnformatted(string.IsNullOrEmpty(r.Isp) ? "-" : r.Isp);

                    // Last seen (local time HH:mm:ss).
                    ImGui.TableSetColumnIndex(8);
                    ImGui.TextUnformatted(r.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss"));

                    ImGui.PopID();

                    #endregion
                }

                ImGui.EndTable();
            }
            #endregion

            #region Save Popup

            if (ImGui.BeginPopup("net_sniff_save_popup"))
            {
                // One-time default per open: Mirror the live UI state when the popup first appears.
                // After this, the user can freely toggle without it snapping back.
                if (!_nsSavePopupPrimed)
                {
                    _nsSaveMaskedInFile = _nsHideMyIp; // Default matches current "Hide my IP".
                    _nsSavePopupPrimed  = true;
                }

                ImGui.Checkbox("Mask my IP in file", ref _nsSaveMaskedInFile);

                if (ImGui.MenuItem("Quick Save (CSV)"))
                {
                    if (SaveNetSnifferCsvQuick(rows, _nsSaveMaskedInFile, out string path))
                    {
                        _nsLastSavePath = path;
                        SendLog($"[Net] Saved {rows.Count} rows to {path}.");
                    }
                    else
                    {
                        SendLog("[Net] Save failed (see log).");
                    }
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.MenuItem("Quick Save (JSON)"))
                {
                    if (SaveNetSnifferJsonQuick(rows, _nsSaveMaskedInFile, out string path))
                    {
                        _nsLastSavePath = path;
                        SendLog($"[Net] Saved {rows.Count} rows to {path}.");
                    }
                    else
                    {
                        SendLog("[Net] Save failed (see log).");
                    }
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                // Save As... (WinForms dialog).
                if (ImGui.MenuItem("Save As..."))
                {
                    try
                    {
                        using (var dlg = new SaveFileDialog())
                        {
                            dlg.Title = "Save Network-Sniffer rows";
                            dlg.Filter = "CSV file (*.csv)|*.csv|JSON file (*.json)|*.json";
                            dlg.FileName = $"NetSniffer-{DateTime.Now:yyyyMMdd-HHmmss}";
                            dlg.AddExtension = true;

                            if (dlg.ShowDialog() == DialogResult.OK)
                            {
                                var chosen = dlg.FileName;
                                var ext = Path.GetExtension(chosen)?.ToLowerInvariant();

                                bool ok = (ext == ".json")
                                    ? SaveNetSnifferJsonTo(rows, _nsSaveMaskedInFile, chosen)
                                    : SaveNetSnifferCsvTo(rows, _nsSaveMaskedInFile, EnsureExt(chosen, ".csv"));

                                if (ok) { _nsLastSavePath = chosen; SendLog($"[Net] Saved {rows.Count} rows to {chosen}."); }
                                else SendLog("[Net] Save failed.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SendLog($"[Net] Save As failed: {ex.Message}.");
                    }
                    ImGui.CloseCurrentPopup();
                }

                if (!string.IsNullOrEmpty(_nsLastSavePath) && ImGui.MenuItem("Copy last path"))
                    ImGui.SetClipboardText(_nsLastSavePath);

                ImGui.EndPopup();
            }

            // Ensure next open re-primes if the popup is closed.
            if (!ImGui.IsPopupOpen("net_sniff_save_popup"))
                _nsSavePopupPrimed = false;

            #endregion

            #region Local Helpers

            /// <summary>
            /// Masks an endpoint when it is identified as the user's own IP (or RFC1918) and
            /// masking is enabled; otherwise returns "ip[:port]" or "-".
            /// </summary>
            string MaskIp(string ip, int port)
            {
                if (string.IsNullOrEmpty(ip))
                    return "-";

                bool isMine = false;
                if (_nsHideMyIp && !string.IsNullOrEmpty(_nsMyIpGuess))
                    isMine = string.Equals(ip, _nsMyIpGuess, StringComparison.OrdinalIgnoreCase);

                // Treat RFC1918/loopback as "mine" if hiding is on.
                if (_nsHideMyIp && !isMine && IsPrivate(ip))
                    isMine = true;

                if (isMine)
                    return "[hidden]";

                if (port > 0)
                    return ip + ":" + port.ToString();

                return ip;
            }

            /// <summary>
            /// Heuristic: prefer the first private LocalIp seen; fall back to any LocalIp.
            /// </summary>
            string GuessMyIp(List<IpResolver.SniffRow> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (!string.IsNullOrEmpty(r.LocalIp) && IsPrivate(r.LocalIp))
                        return r.LocalIp;
                }
                // Fallback: 1st local.
                for (int i = 0; i < list.Count; i++)
                {
                    if (!string.IsNullOrEmpty(list[i].LocalIp))
                        return list[i].LocalIp;
                }
                return null;
            }

            /// <summary>
            /// Heuristic: prefer CastleMinerZ OUT traffic to a public IP; fall back to Steam.
            /// </summary>
            string GuessHostIp(List<IpResolver.SniffRow> list)
            {
                if (list == null || list.Count == 0) return null;

                // Score ISP so we prefer non-Valve if possible.
                int IspScore(string isp)
                {
                    if (string.IsNullOrWhiteSpace(isp)) return 1;                    // unknown
                    if (IsValveIsp(isp)) return 0;                                   // Valve (lowest)
                    return 2;                                                        // known non-Valve (best)
                }

                string PickBest(IEnumerable<IpResolver.SniffRow> rowsForCandidates, Func<IpResolver.SniffRow, string> ipSelector)
                {
                    // Group by IP and rank:
                    //  1) non-Valve > unknown > Valve
                    //  2) most hits
                    //  3) most recent
                    var groups = new Dictionary<string, (int score, int hits, DateTime last)>(StringComparer.OrdinalIgnoreCase);

                    foreach (var r in rowsForCandidates)
                    {
                        string ip = ipSelector(r);
                        if (string.IsNullOrEmpty(ip) || IsPrivate(ip)) continue;

                        string isp = GetRowIsp(r);
                        int score = IspScore(isp);
                        if (!groups.TryGetValue(ip, out var g))
                            groups[ip] = (score, 1, r.LastSeenUtc);
                        else
                            groups[ip] = (Math.Max(g.score, score), g.hits + 1, (r.LastSeenUtc > g.last ? r.LastSeenUtc : g.last));
                    }

                    if (groups.Count == 0) return null;

                    return groups
                        .OrderByDescending(kv => kv.Value.score)
                        .ThenByDescending(kv => kv.Value.hits)
                        .ThenByDescending(kv => kv.Value.last)
                        .Select(kv => kv.Key)
                        .FirstOrDefault();
                }

                // 1) Prefer CastleMinerZ OUT traffic to public remote IP.
                var cmzOut = list.Where(r =>
                    string.Equals(r.Process, "CastleMinerZ", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.Dir, "OUT", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(r.RemoteIp) &&
                    !IsPrivate(r.RemoteIp));

                string best = PickBest(cmzOut, r => r.RemoteIp);
                if (!string.IsNullOrEmpty(best))
                    return best;

                // 2) Fallback: steam OUT traffic to public remote IP.
                var steamOut = list.Where(r =>
                    string.Equals(r.Process, "steam", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.Dir, "OUT", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(r.RemoteIp) &&
                    !IsPrivate(r.RemoteIp));

                best = PickBest(steamOut, r => r.RemoteIp);
                return best;
            }

            /// <summary>Simple RFC1918/loopback check adequate for LAN usage.</summary>
            bool IsPrivate(string ip)
            {
                if (string.IsNullOrEmpty(ip)) return false;
                return ip.StartsWith("192.168.") ||
                       ip.StartsWith("10.") ||
                       ip.StartsWith("172.16.") || ip.StartsWith("172.17.") ||
                       ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
                       ip.StartsWith("172.20.") || ip.StartsWith("172.21.") ||
                       ip.StartsWith("172.22.") || ip.StartsWith("172.23.") ||
                       ip.StartsWith("172.24.") || ip.StartsWith("172.25.") ||
                       ip.StartsWith("172.26.") || ip.StartsWith("172.27.") ||
                       ip.StartsWith("172.28.") || ip.StartsWith("172.29.") ||
                       ip.StartsWith("172.30.") || ip.StartsWith("172.31.") ||
                       ip == "127.0.0.1";
            }

            /// <summary>Appends <paramref name="ext"/> if <paramref name="path"/> has no extension.</summary>
            string EnsureExt(string path, string ext)
                => string.IsNullOrEmpty(Path.GetExtension(path)) ? path + ext : path;

            /// <summary>CSV one-liner matching the visible column order (sans row index).</summary>
            string RowToCsv(IpResolver.SniffRow r, string localDisplayed, string remoteDisplayed, string geoDisplayed, string ispDisplayed)
            {
                // CSV order matches the columns: # (omit), Proc, PID, Dir, Local, Remote, Seen.
                string seen = r.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                return string.Join(",", new[]
                {
                     Csv(r.Process),
                     r.Pid.ToString(),
                     Csv(r.Dir),
                     Csv(localDisplayed),
                     Csv(remoteDisplayed),
                     Csv(string.IsNullOrEmpty(geoDisplayed) ? "-" : geoDisplayed),
                     Csv(string.IsNullOrEmpty(ispDisplayed) ? "-" : ispDisplayed),
                     Csv(seen)
                });
                string Csv(string s)
                {
                    if (s == null) return "";
                    bool needQuotes = s.Contains(",") || s.Contains("\"") || s.Contains("\n");
                    if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
                    return needQuotes ? $"\"{s}\"" : s;
                }
            }

            /// <summary>Compact JSON object for a single row (with masked endpoints if requested).</summary>
            string RowToJson(IpResolver.SniffRow r, string localDisplayed, string remoteDisplayed, string geoDisplayed, string ispDisplayed)
            {
                string seen = r.LastSeenUtc.ToLocalTime().ToString("o"); // ISO 8601.
                return "{"
                     + $"\"process\":\"{J(r.Process)}\","
                     + $"\"pid\":{r.Pid},"
                     + $"\"dir\":\"{J(r.Dir)}\","
                     + $"\"local\":\"{J(localDisplayed)}\","
                     + $"\"remote\":\"{J(remoteDisplayed)}\","
                     + $"\"geo\":\"{J(string.IsNullOrEmpty(geoDisplayed) ? "" : geoDisplayed)}\","
                     + $"\"isp\":\"{J(string.IsNullOrEmpty(ispDisplayed) ? "" : ispDisplayed)}\","
                     + $"\"seen\":\"{J(seen)}\""
                     + "}";
                string J(string s)
                {
                    if (s == null) return "";
                    return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
                }
            }

            /// <summary>Builds a default path under !Mods/<Namespace>/!Logs with timestamp.</summary>
            string BuildDefaultLogPath(string ext)
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          "!Mods", typeof(IGMainUI).Namespace, "!Logs");
                string file = $"NetSniffer-{DateTime.Now:yyyyMMdd-HHmmss}{ext}";
                return Path.Combine(dir, file);
            }

            bool IsValveIsp(string isp)
            {
                return !string.IsNullOrWhiteSpace(isp) &&
                       string.Equals(isp.Trim(), "Valve Corporation", StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Best-effort ISP for the row:
            /// - Prefer resolver-populated r.Isp
            /// - If empty and remote IP is public, fetch from geo cache on-demand (context menu safe)
            /// </summary>
            string GetRowIsp(IpResolver.SniffRow r)
            {
                if (!string.IsNullOrEmpty(r.Isp)) return r.Isp;

                try
                {
                    if (!string.IsNullOrEmpty(r.RemoteIp) && !IsPrivate(r.RemoteIp))
                    {
                        var info = IpGeoCache.GetOrFetch(r.RemoteIp);
                        if (info != null && !string.IsNullOrEmpty(info.Isp))
                            return info.Isp;
                    }
                }
                catch { }

                return "";
            }

            /// <summary>
            /// Best-effort Geo string for the row:
            /// - Prefer resolver-populated r.Geo
            /// - If empty and remote IP is public, fetch from geo cache on-demand
            /// </summary>
            string GetRowGeo(IpResolver.SniffRow r)
            {
                if (!string.IsNullOrEmpty(r.Geo)) return r.Geo;

                try
                {
                    if (!string.IsNullOrEmpty(r.RemoteIp) && !IsPrivate(r.RemoteIp))
                    {
                        var info = IpGeoCache.GetOrFetch(r.RemoteIp);
                        if (info != null)
                            return $"{info.City}, {info.Region}, {info.Country}";
                    }
                }
                catch { }

                return "";
            }
            #endregion

            #region Save Helpers

            /// <summary>Quick CSV save to the default logs folder.</summary>
            bool SaveNetSnifferCsvQuick(List<IpResolver.SniffRow> list, bool maskMine, out string path)
            {
                path = BuildDefaultLogPath(".csv");
                return SaveNetSnifferCsvTo(list, maskMine, path);
            }

            /// <summary>Quick JSON save to the default logs folder.</summary>
            bool SaveNetSnifferJsonQuick(List<IpResolver.SniffRow> list, bool maskMine, out string path)
            {
                path = BuildDefaultLogPath(".json");
                return SaveNetSnifferJsonTo(list, maskMine, path);
            }

            /// <summary>Writes CSV to an explicit file path, optionally masking local endpoints.</summary>
            bool SaveNetSnifferCsvTo(List<IpResolver.SniffRow> list, bool maskMine, string filePath)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    var sb = new StringBuilder();
                    sb.AppendLine("Proc,PID,Dir,Local,Remote,Seen,Geo,Pos,ISP");
                    foreach (var r in list.OrderByDescending(x => x.LastSeenUtc))
                    {
                        string localShown = maskMine ? MaskIp(r.LocalIp, r.LocalPort) : AsEp(r.LocalIp, r.LocalPort);
                        string remoteShown = maskMine ? MaskIp(r.RemoteIp, r.RemotePort) : AsEp(r.RemoteIp, r.RemotePort);
                        string geo = "", pos = "", isp = "";
                        if (!string.IsNullOrEmpty(r.RemoteIp) && !IsPrivate(r.RemoteIp))
                        {
                            var info = IpGeoCache.GetOrFetch(r.RemoteIp); // synchronous cache
                            if (info != null) { geo = $"{info.City}, {info.Region}, {info.Country}"; pos = $"{info.Lat}, {info.Lon}"; isp = info.Isp; }
                        }
                        string seen = r.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                        sb.AppendLine(string.Join(",", new[]
                        {
                            Csv(r.Process), r.Pid.ToString(), Csv(r.Dir),
                            Csv(localShown), Csv(remoteShown), Csv(seen),
                            Csv(geo), Csv(pos), Csv(isp.Replace("\n", ""))
                        }));
                    }
                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                    return true;
                }
                catch (Exception ex) { SendLog($"[Net] CSV save error: {ex.Message}."); return false; }

                string Csv(string s)
                {
                    if (string.IsNullOrEmpty(s)) return "";
                    bool quote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
                    if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
                    return quote ? $"\"{s}\"" : s;
                }
                string AsEp(string ip, int port) => string.IsNullOrEmpty(ip) ? "-" : (port > 0 ? $"{ip}:{port}" : ip);
            }

            /// <summary>Writes JSON to an explicit file path, optionally masking local endpoints.</summary>
            bool SaveNetSnifferJsonTo(List<IpResolver.SniffRow> list, bool maskMine, string filePath)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    var sb = new StringBuilder().Append('[');
                    bool first = true;
                    foreach (var r in list.OrderByDescending(x => x.LastSeenUtc))
                    {
                        string localShown = maskMine ? MaskIp(r.LocalIp, r.LocalPort) : AsEp(r.LocalIp, r.LocalPort);
                        string remoteShown = maskMine ? MaskIp(r.RemoteIp, r.RemotePort) : AsEp(r.RemoteIp, r.RemotePort);
                        string geo = "", pos = "", isp = "";
                        if (!string.IsNullOrEmpty(r.RemoteIp) && !IsPrivate(r.RemoteIp))
                        {
                            var info = IpGeoCache.GetOrFetch(r.RemoteIp); // synchronous cache
                            if (info != null) { geo = $"{info.City}, {info.Region}, {info.Country}"; pos = $"{info.Lat}, {info.Lon}"; isp = info.Isp; }
                        }
                        string seen = r.LastSeenUtc.ToLocalTime().ToString("o");

                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{")
                          .Append($"\"process\":\"{J(r.Process)}\",")
                          .Append($"\"pid\":{r.Pid},")
                          .Append($"\"dir\":\"{J(r.Dir)}\",")
                          .Append($"\"local\":\"{J(localShown)}\",")
                          .Append($"\"remote\":\"{J(remoteShown)}\",")
                          .Append($"\"seen\":\"{J(seen)}\",")
                          .Append($"\"geo\":\"{J(geo)}\",")
                          .Append($"\"pos\":\"{J(pos)}\",")
                          .Append($"\"isp\":\"{J(isp.Replace("\n", ""))}\"")
                          .Append("}");
                    }
                    sb.Append(']');
                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                    return true;
                }
                catch (Exception ex) { SendLog($"[Net] JSON save error: {ex.Message}."); return false; }

                string J(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string AsEp(string ip, int port) => string.IsNullOrEmpty(ip) ? "-" : (port > 0 ? $"{ip}:{port}" : ip);
            }
            #endregion
        }
        #endregion

        #region Tab: Network-Calls

        /// <summary>
        /// This tab is split into two sub-tabs:
        ///
        /// 1) Calls
        ///    - Live log view of SendData / Receive(Recieve)Data activity.
        ///    - Start/Stop toggles capture (patches remain installed; logging is gated).
        ///    - Mirrors Log tab UX: Clear / Save / Auto-scroll / Pause / Filter / Max Lines.
        ///    - Session auto-log file path is displayed (hover to see full path).
        ///
        /// 2) Rules
        ///    - Per-message block rules (Both / In / Out / None).
        ///    - Preset buttons respect the current filter (only apply to visible rows).
        ///    - Uses ImGuiListClipper when NOT filtering (performance) and a full scan when filtering (avoids clipper asserts).
        ///
        /// Notes:
        /// - Rules are intended to disable the underlying send/receive behaviors (not just logging).
        /// - Filters are native ImGuiTextFilter objects; free them on UI teardown to avoid leaks.
        /// </summary>

        #region Main Layout (Sub-Tab Router)

        /// <summary>
        /// Network-Calls parent tab renderer.
        /// Hosts two sub-tabs:
        /// - Calls: live network call log view.
        /// - Rules: per-message block rules.
        /// </summary>
        private static void DrawNetworkCallsTab()
        {
            if (ImGui.BeginTabBar("NetCallsSubTabs"))
            {
                if (ImGui.BeginTabItem("Calls"))
                {
                    DrawNetworkCallsTab_Calls();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Rules"))
                {
                    DrawNetworkCallsTab_Rules();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        #endregion

        #region Draw: Calls

        /// <summary>
        /// In-game network message call logger:
        /// - Hooks message SendData / Receive(Recieve)Data via Harmony (see NetCallsMessagePatches).
        /// - Start/Stop only toggles capture; leaving the tab does NOT stop capture.
        /// - When enabled, lines are streamed to: !Mods\CastleWallsMk2\!NetLogs
        /// - UI controls mirror the Log tab: Clear/Save/Auto-scroll/Pause/Filter/Max Lines.
        /// </summary>
        /// <remarks>
        /// Layout:
        /// - Top controls table (Start/Stop, Clear/Save, Auto-scroll/Pause, Filter, Max Lines).
        /// - Status line (state + row count + auto file name).
        /// - Scrollable list (filterable, selectable, context menu).
        /// - Legend (color key).
        /// </remarks>
        private static void DrawNetworkCallsTab_Calls()
        {
            // Read pause state to detect an edge (unpause) later and do a one-time jump-to-bottom.
            bool pausedBefore = _netPaused;

            bool capturing = NetCallsController.IsEnabled;

            #region Top Controls Row

            if (ImGui.BeginTable("netcalls_top_controls", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("left", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("middle", ImGuiTableColumnFlags.WidthFixed, 1f);
                ImGui.TableSetupColumn("right", ImGuiTableColumnFlags.WidthFixed, 192f);

                // LEFT: Start/Stop + Clear/Save + Auto-scroll/Pause + Filter.
                ImGui.TableNextColumn();

                // Start/Stop does NOT depend on tab visibility.
                if (!capturing)
                {
                    if (ImGui.Button("Start"))
                    {
                        NetCallsController.Start();
                        capturing = true;
                        _netScrollBottomOnUnpause = true;
                    }
                }
                else
                {
                    if (ImGui.Button("Stop"))
                    {
                        NetCallsController.Stop();
                        capturing = false;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Clear")) NetCallsLog.Clear();
                ImGui.SameLine();
                if (ImGui.Button("Save")) SaveNetCallsWithDialog();
                ImGui.SameLine();
                ImGui.Checkbox("Auto-scroll", ref _netAutoScroll);
                ImGui.SameLine();
                ImGui.Checkbox("Pause", ref _netPaused);
                ImGui.SameLine();
                ImGui.TextDisabled("|"); ImGui.SameLine();
                ImGui.TextUnformatted("Filter:"); ImGui.SameLine();
                _netFilter.Draw("##netfilter", -1);

                // MIDDLE: visual separator.
                ImGui.TableNextColumn();
                ImGui.TextDisabled("|");

                // RIGHT: Max Lines (right-aligned).
                ImGui.TableNextColumn();
                {
                    var style = ImGui.GetStyle();
                    string label = "Max Lines:";
                    float labelW = ImGui.CalcTextSize(label).X;
                    float inputW = 110f;
                    float totalW = labelW + style.ItemInnerSpacing.X + inputW;

                    float cellW = ImGui.GetColumnWidth();
                    float x = ImGui.GetCursorPosX() + (cellW - totalW - style.CellPadding.X);
                    ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));

                    ImGui.TextUnformatted(label);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(inputW);

                    int cap = NetCallsLog.MaxEntries;
                    if (ImGui.InputInt("##net_maxLines", ref cap, 500, 5000))
                        NetCallsLog.MaxEntries = cap; // clamps & trims inside.
                }

                ImGui.EndTable();
            }
            #endregion

            // If we just unpaused, do a one-time jump to bottom (unless user is filtering).
            if (pausedBefore && !_netPaused)
                _netScrollBottomOnUnpause = true;

            #region Status Line

            // Status line (auto-file is created per Start()).
            string autoPath = NetCallsFileStreamer.AutoPath;
            string fileName = string.IsNullOrEmpty(autoPath) ? "(none)" : Path.GetFileName(autoPath);

            ImGui.TextDisabled($"State: {(capturing ? "Capturing" : "Stopped")} | Rows: {NetCallsLog.Count} | Auto file: {fileName}");
            if (!string.IsNullOrEmpty(autoPath) && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip(autoPath);

            ImGui.Separator();

            #endregion

            #region Scrollable Log List (Exact-Rows Sizing + Filtering)

            // Reserve space for the legend so only the child scrolls.
            var styleMain = ImGui.GetStyle();
            float availH  = ImGui.GetContentRegionAvail().Y;

            const float SEP_H   = 2f;
            float sepAndSpacing = SEP_H + styleMain.ItemSpacing.Y;

            float legendRowH    = ImGui.GetFrameHeight();
            const float EPS     = 2.0f;
            float reservedBelow = sepAndSpacing + legendRowH + EPS;

            float logAvail = Math.Max(0f, availH - reservedBelow);

            // Exact-rows sizing (mirrors Log tab).
            float edgePad     = 5f;
            float textH       = ImGui.GetFrameHeight();
            float spacingY    = styleMain.ItemSpacing.Y;
            float rowPitch    = textH + spacingY;

            float visibleRows = (logAvail - edgePad * 2 + spacingY) / rowPitch;
            float childH      = (visibleRows * rowPitch - spacingY) + (edgePad * 2f);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edgePad, edgePad));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(styleMain.ItemSpacing.X, 0f));

            var winFlags = ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.HorizontalScrollbar;

            if (ImGui.BeginChild("netcalls_scroll", new Vector2(0, childH), ImGuiChildFlags.Borders, winFlags))
            {
                var backgroundColor = new Vector4(0, 0, 0, 0);

                ImGui.PushStyleColor(ImGuiCol.Header,        backgroundColor);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, backgroundColor);
                ImGui.PushStyleColor(ImGuiCol.HeaderActive,  backgroundColor);

                long? deleteSeq = null;

                var snapshot = NetCallsLog.SnapshotSorted();
                foreach (var line in snapshot)
                {
                    string pretty = $"[{line.TimeUtc:HH:mm:ss}] {line.Tag} {line.TypeName}: {line.Text}";

                    if (!_netFilter.PassFilter(pretty))
                        continue;

                    var color =
                        (line.Tag == "RX")  ? COLOR_NET_RX :
                        (line.Tag == "TX")  ? COLOR_NET_TX :
                        (line.Tag == "RXRAW" || line.Tag == "TXRAW") ? COLOR_NET_RAW :
                        (line.Tag == "ERR") ? COLOR_NET_ERR :
                        COLOR_NET_RAW;

                    ImGui.PushStyleColor(ImGuiCol.Text, color);

                    ImGui.Selectable($"{pretty}##{line.Sequence}", false,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);

                    if (ImGui.BeginPopupContextItem())
                    {
                        if (ImGui.MenuItem("Copy line"))
                            ImGui.SetClipboardText(pretty);

                        if (ImGui.MenuItem("Delete line"))
                            deleteSeq = line.Sequence;

                        ImGui.EndPopup();
                    }

                    ImGui.PopStyleColor();
                }

                if (deleteSeq.HasValue)
                    NetCallsLog.Remove(deleteSeq.Value);

                ImGui.PopStyleColor(3);

                bool filterActive = _netFilter.CountGrep > 0;
                if (_netScrollBottomOnUnpause)
                {
                    ImGui.SetScrollY(ImGui.GetScrollMaxY());
                    _netScrollBottomOnUnpause = false;
                }
                else if (_netAutoScroll && !_netPaused && !filterActive)
                {
                    if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5.0f)
                        ImGui.SetScrollHereY(1.0f);
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleVar(2);

            #endregion

            #region Legend

            ImGui.Spacing();
            ImGui.Separator();

            ImGui.Spacing();
            ImGui.BeginGroup();
            {
                ImGui.TextDisabled("Legend:"); ImGui.SameLine();
                DrawNetLegendItem("Incoming (RX)", COLOR_NET_RX,  "Messages received by the game (Receive/RecieveData)."); ImGui.SameLine();
                DrawNetLegendItem("Outgoing (TX)", COLOR_NET_TX,  "Messages sent by the game (SendData).");                ImGui.SameLine();
                DrawNetLegendItem("Raw Bytes",     COLOR_NET_RAW, "Optional raw packet/byte preview lines.");              ImGui.SameLine();
                DrawNetLegendItem("Errors",        COLOR_NET_ERR, "Patch/logging errors (exceptions, hook issues, etc.).");
            }
            ImGui.EndGroup();

            #endregion
        }
        #endregion

        #region Draw: Rules

        #region Rules - Filter State

        /// <summary>
        /// Native ImGui text filter for the Rules list.
        /// </summary>
        /// <remarks>
        /// IMPORTANT:
        /// - This is a native resource (ImGuiTextFilter*) and should be destroyed on UI teardown.
        /// - (You already do this pattern for _netFilter; do the same for _netRulesFilter when you add teardown.)
        /// </remarks>
        private static readonly unsafe ImGuiTextFilterPtr _netRulesFilter =
            new ImGuiTextFilterPtr(ImGuiNative.ImGuiTextFilter_ImGuiTextFilter(null));

        #endregion

        #region Rules - Preset Target Helper

        /// <summary>
        /// Returns the set of message Types that are currently "visible" in the Rules list.
        /// </summary>
        /// <remarks>
        /// This is used by preset buttons so that when a filter is active,
        /// "Allow All / Block IN / Block OUT / Block BOTH" apply only to the rows shown by the filter.
        /// </remarks>
        private static IEnumerable<Type> GetRulesPresetTargets(IReadOnlyList<NetCallMessageRow> rows)
        {
            bool filterActive = _netRulesFilter.CountGrep > 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i].Type;
                if (t == null) continue;

                // Match what the Rules UI shows (you currently filter by t.Name).
                if (filterActive && !_netRulesFilter.PassFilter(t.Name))
                    continue;

                yield return t;
            }
        }
        #endregion

        #region Rules - Main Table (Rows + Per-Message Dropdown)

        /// <summary>
        /// Renders the Rules sub-tab:
        /// - Preset buttons
        /// - Filter input
        /// - Per-message table with block-mode dropdown
        /// </summary>
        /// <remarks>
        /// Performance:
        /// - When not filtering, uses ImGuiListClipper for large lists.
        /// - When filtering, iterates all rows (avoids clipper assertion issues when skipping rows).
        /// </remarks>
        private unsafe static void DrawNetworkCallsTab_Rules()
        {
            // Presets row
            var rows = NetCallsMessagePatches.Rows;

            if (ImGui.Button("Allow All"))
                NetCallsBlockRules.SetAll(GetRulesPresetTargets(rows), NetCallBlockMode.None);

            ImGui.SameLine();
            if (ImGui.Button("Block IN"))
                NetCallsBlockRules.SetAll(GetRulesPresetTargets(rows), NetCallBlockMode.In);

            ImGui.SameLine();
            if (ImGui.Button("Block OUT"))
                NetCallsBlockRules.SetAll(GetRulesPresetTargets(rows), NetCallBlockMode.Out);

            ImGui.SameLine();
            if (ImGui.Button("Block BOTH"))
                NetCallsBlockRules.SetAll(GetRulesPresetTargets(rows), NetCallBlockMode.Both);

            ImGui.SameLine();
            ImGui.TextDisabled("|"); ImGui.SameLine();
            ImGui.TextUnformatted("Filter:"); ImGui.SameLine();
            _netRulesFilter.Draw("##netcalls_rules_filter", -1);

            ImGui.Separator();

            if (ImGui.BeginTable("netcalls_rules_table", 4,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, 0)))
            {
                ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("RX",      ImGuiTableColumnFlags.WidthFixed,   45f);
                ImGui.TableSetupColumn("TX",      ImGuiTableColumnFlags.WidthFixed,   45f);
                ImGui.TableSetupColumn("Block",   ImGuiTableColumnFlags.WidthFixed,   120f);
                ImGui.TableHeadersRow();

                bool filterActive = _netRulesFilter.CountGrep > 0;

                if (filterActive)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i];
                        var t = r.Type;
                        if (t == null) continue;

                        string name = t.Name;

                        if (!_netRulesFilter.PassFilter(name))
                            continue;

                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(name);

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.HasRx ? "Yes" : "-");

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(r.HasTx ? "Yes" : "-");

                        ImGui.TableNextColumn();
                        var cur = NetCallsBlockRules.Get(t);

                        int idx =
                            (cur == NetCallBlockMode.Both) ? 0 :
                            (cur == NetCallBlockMode.In)   ? 1 :
                            (cur == NetCallBlockMode.Out)  ? 2 :
                                                             3;

                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.Combo($"##blk_{i}", ref idx, _blockOpts, _blockOpts.Length))
                            NetCallsBlockRules.Set(t, _blockVals[idx]);
                    }
                }
                else
                {
                    // Use clipper only when NOT filtering.
                    ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(rows.Count);

                    while (clipper.Step())
                    {
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            var r = rows[i];
                            var t = r.Type;
                            if (t == null) continue;

                            string name = t.Name;

                            ImGui.TableNextRow();

                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(name);

                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(r.HasRx ? "Yes" : "-");

                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(r.HasTx ? "Yes" : "-");

                            ImGui.TableNextColumn();
                            var cur = NetCallsBlockRules.Get(t);

                            int idx =
                                (cur == NetCallBlockMode.Both) ? 0 :
                                (cur == NetCallBlockMode.In)   ? 1 :
                                (cur == NetCallBlockMode.Out)  ? 2 :
                                                                 3;

                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.Combo($"##blk_{i}", ref idx, _blockOpts, _blockOpts.Length))
                                NetCallsBlockRules.Set(t, _blockVals[idx]);
                        }
                    }

                    clipper.End();
                    ImGuiNative.ImGuiListClipper_destroy(clipper.NativePtr);
                }

                ImGui.EndTable();
            }
        }
        #endregion

        #endregion

        #region Save Dialog

        /// <summary>
        /// Opens a Save File dialog on a background STA thread and saves the current
        /// in-memory Network-Calls log to a user-selected file.
        /// </summary>
        private static void SaveNetCallsWithDialog()
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using (var dlg = new SaveFileDialog())
                    {
                        dlg.Title           = "Save Network Calls Log";
                        dlg.Filter          = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                        dlg.DefaultExt      = "log";
                        dlg.AddExtension    = true;
                        dlg.CheckPathExists = true;

                        try
                        {
                            var autoDir = NetCallsFileStreamer.AutoDir;
                            if (!string.IsNullOrEmpty(autoDir) && Directory.Exists(autoDir))
                                dlg.InitialDirectory = autoDir;
                        }
                        catch { }

                        dlg.FileName = $"NetCalls_{DateTime.Now:yyyyMMdd_HHmmss}.log";

                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            var text = NetCallsLog.DumpText(includeTimestamps: true, includeTag: true);
                            File.WriteAllText(dlg.FileName, text, Encoding.UTF8);
                            NetCallsLog.Append("UI", $"Saved net log to: {dlg.FileName}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    NetCallsLog.Append("UI", $"Failed to save net log: {ex.Message}.");
                }
            })
            { IsBackground = true };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        #endregion

        #region UI Helpers

        /// <summary>
        /// User-friendly legend entry for the Network-Calls tab.
        /// Keeps the same color swatch style but uses clearer text + tooltip.
        /// </summary>
        private static void DrawNetLegendItem(string label, Vector4 color, string tooltip = null)
        {
            DrawLegendSwatch(label, color);

            if (!string.IsNullOrWhiteSpace(tooltip) &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            {
                ImGui.SetTooltip(tooltip);
            }
        }
        #endregion

        #region Filter Lifetime

        /// <summary>
        /// Disposes the Network-Calls tab ImGui text filter and clears its pointer.
        /// </summary>
        public static unsafe void DisposeNetLogFilter()
        {
            // Free the Network-Calls tab filter.
            if (_netFilter.NativePtr != null)
            {
                ImGuiNative.ImGuiTextFilter_destroy(_netFilter.NativePtr);
                _netFilter = new ImGuiTextFilterPtr(null);
            }
        }
        #endregion

        #region Rules: Block Mode UI Options

        /// <summary>
        /// UI-facing string labels for the per-message block dropdown.
        /// </summary>
        /// <remarks>
        /// Order must match _blockVals and the idx mapping logic in the Rules table.
        /// </remarks>
        static readonly string[] _blockOpts = { "Both", "In", "Out", "None" };

        /// <summary>
        /// Enum values corresponding to the dropdown options.
        /// </summary>
        static readonly NetCallBlockMode[] _blockVals =
        {
            NetCallBlockMode.Both,
            NetCallBlockMode.In,
            NetCallBlockMode.Out,
            NetCallBlockMode.None
        };

        /// <summary>
        /// Block modes for a given message type.
        /// </summary>
        /// <remarks>
        /// Meaning:
        /// - None: allow both directions
        /// - In:   block Recieve/Receive side
        /// - Out:  block Send side
        /// - Both: block both directions
        /// </remarks>
        internal enum NetCallBlockMode : byte
        {
            None = 0,
            In   = 1,
            Out  = 2,
            Both = 3
        }
        #endregion

        #region Rules: Block Rules Storage (Per-Message)

        /// <summary>
        /// Central rules store for message block modes.
        /// </summary>
        /// <remarks>
        /// Keying:
        /// - Uses Type.FullName when available to remain stable across discovery runs.
        /// - Thread-safe via a single lock (UI writes, patch paths read).
        /// </remarks>
        internal static class NetCallsBlockRules
        {
            // Key by full name to keep it stable across reloads/discovery.
            // (RuntimeTypeHandle also works; string is easiest for UI + persistence later.)
            private static readonly Dictionary<string, NetCallBlockMode> _rules =
                new Dictionary<string, NetCallBlockMode>(StringComparer.Ordinal);

            private static readonly object _lock = new object();

            /// <summary>
            /// Returns the current block mode for a message type (defaults to None).
            /// </summary>
            public static NetCallBlockMode Get(Type t)
            {
                if (t == null) return NetCallBlockMode.None;
                var key = t.FullName ?? t.Name;

                lock (_lock)
                    return _rules.TryGetValue(key, out var m) ? m : NetCallBlockMode.None;
            }

            /// <summary>
            /// Sets the block mode for a message type.
            /// </summary>
            public static void Set(Type t, NetCallBlockMode mode)
            {
                if (t == null) return;
                var key = t.FullName ?? t.Name;

                lock (_lock)
                    _rules[key] = mode;
            }

            /// <summary>
            /// True if the given type is blocked on the IN (receive) direction.
            /// </summary>
            public static bool BlockIn(Type t)
            {
                var m = Get(t);
                return (m == NetCallBlockMode.In || m == NetCallBlockMode.Both);
            }

            /// <summary>
            /// True if the given type is blocked on the OUT (send) direction.
            /// </summary>
            public static bool BlockOut(Type t)
            {
                var m = Get(t);
                return (m == NetCallBlockMode.Out || m == NetCallBlockMode.Both);
            }

            /// <summary>
            /// Bulk-sets a block mode across a set of types (used by preset buttons).
            /// </summary>
            public static void SetAll(IEnumerable<Type> types, NetCallBlockMode mode)
            {
                if (types == null) return;
                lock (_lock)
                {
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        var key = t.FullName ?? t.Name;
                        _rules[key] = mode;
                    }
                }
            }
        }
        #endregion

        #endregion

        #region Tab: Server-Commands

        /// <summary>
        /// Draws the "Server-Commands" ImGui tab.
        ///
        /// Purpose:
        /// - Configures host-side public chat commands.
        /// - Allows enabling/disabling the full server-command system.
        /// - Allows hiding handled commands from public chat.
        /// - Allows selecting admins who can run admin-only commands.
        /// - Displays command groups by permission level inside a scrollable child region.
        ///
        /// Notes:
        /// - Remote players may use either "/command" or "!command" in chat.
        /// - Actual interception/handling occurs on the host in the message patch path.
        /// - This tab is only the configuration surface for that system.
        /// </summary>
        public static void DrawServerCommandsTab()
        {
            #region Header / Intro Text

            ImGui.TextWrapped("Public chat commands. Remote players can type either '/command' or '!command' in chat.");
            ImGui.Spacing();

            #endregion

            #region Master Toggles

            if (ImGui.BeginTable("##server_commands_master_toggles", 2, ImGuiTableFlags.SizingStretchSame))
            {
                #region Row 1

                ImGui.TableNextRow();

                // Left column.
                ImGui.TableSetColumnIndex(0);

                // Master enable for the entire host-side server command system.
                bool masterEnabled = _serverCommandsEnabled;
                if (ImGui.Checkbox("Enable Public Server Commands", ref masterEnabled))
                {
                    bool previousValue = _serverCommandsEnabled;
                    _serverCommandsEnabled = masterEnabled;

                    // Optionally announce state changes to the full server.
                    if (_serverCommandsAnnounceStateChanges && previousValue != masterEnabled)
                    {
                        BroadcastTextMessage.Send(
                            CastleMinerZGame.Instance?.MyNetworkGamer,
                            masterEnabled
                                ? "[Server] Public chat commands are now enabled. Type /help for available commands."
                                : "[Server] Public chat commands are now disabled.");
                    }
                }

                // Right column.
                ImGui.TableSetColumnIndex(1);

                // Optional: announce when public server commands are enabled / disabled.
                bool announceStateChanges = _serverCommandsAnnounceStateChanges;
                if (ImGui.Checkbox("Announce Enable / Disable To Server", ref announceStateChanges))
                    _serverCommandsAnnounceStateChanges = announceStateChanges;

                #endregion

                #region Row 2

                ImGui.TableNextRow();

                // Left column.
                ImGui.TableSetColumnIndex(0);

                // Optional: reply to players when public server commands are disabled.
                bool replyWhenDisabled = _serverCommandsReplyWhenDisabled;
                if (ImGui.Checkbox("Reply When Commands Are Disabled", ref replyWhenDisabled))
                    _serverCommandsReplyWhenDisabled = replyWhenDisabled;

                // Right column.
                ImGui.TableSetColumnIndex(1);

                // When enabled, handled commands are hidden from normal public chat output.
                bool hideHandled = _serverCommandsHideHandledChat;
                if (ImGui.Checkbox("Hide Handled Commands From Public Chat", ref hideHandled))
                    _serverCommandsHideHandledChat = hideHandled;

                #endregion

                ImGui.EndTable();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Tip: The intercept runs on the host inside _processBroadcastTextMessage.");

            #endregion

            #region Admin Selection

            ImGui.TextUnformatted("Admins");
            DrawServerAdminsCombo();
            ImGui.TextDisabled("Selected admins can run admin-only chat commands like /kick, /ban, and /whoami will show their level.");

            #endregion

            #region Command List / Scrollbox

            if (ImGui.BeginChild("server_commands_scroll", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                // Member-accessible commands.
                DrawServerCommandSection("Member Commands", ServerCommands.ServerPermissionLevel.Member);

                ImGui.Spacing();

                // Admin-only commands.
                DrawServerCommandSection("Admin Commands", ServerCommands.ServerPermissionLevel.Admin);

                ImGui.EndChild();
            }
            #endregion
        }

        /// <summary>
        /// Draws a grouped list of server commands for the requested permission level.
        ///
        /// Purpose:
        /// - Filters the registry by permission level.
        /// - Displays one checkbox row per command.
        /// - Shows command syntax and description under each entry.
        ///
        /// Notes:
        /// - Uses the shared command registry as the source of truth.
        /// - Intended for use inside the Server Commands scroll region.
        /// </summary>
        /// <param name="title">Header text shown above the section.</param>
        /// <param name="permission">Permission tier to display.</param>
        private static void DrawServerCommandSection(string title, ServerCommands.ServerPermissionLevel permission)
        {
            #region Section Header

            ImGui.TextUnformatted(title);
            ImGui.Separator();

            #endregion

            #region Command Rows

            foreach (var def in ServerCommandRegistry.Definitions)
            {
                // Only draw commands that belong to the requested permission group.
                if (def.RequiredPermission != permission)
                    continue;

                ImGui.PushID(def.Name);

                #region Enabled Checkbox

                bool enabled = def.GetEnabled?.Invoke() ?? false;
                if (ImGui.Checkbox("##enabled", ref enabled))
                    def.SetEnabled?.Invoke(enabled);

                ImGui.SameLine();

                #endregion

                #region Command Text / Description

                ImGui.BeginGroup();

                // Primary syntax line.
                ImGui.TextUnformatted(def.DisplaySyntax);

                // Secondary wrapped description line.
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(def.Description);
                ImGui.PopTextWrapPos();

                ImGui.EndGroup();

                #endregion

                #region Row Separator / Cleanup

                ImGui.Separator();
                ImGui.PopID();

                #endregion
            }
            #endregion
        }

        /// <summary>
        /// Draws the admin multi-player selection combo.
        ///
        /// Purpose:
        /// - Keeps the visible UI selection synchronized with the authoritative admin registry.
        /// - Reuses the existing multi-player combo helper so behavior matches the rest of the UI.
        ///
        /// Notes:
        /// - UI selection is driven by byte player ids.
        /// - Authoritative admin membership is stored separately in the server-command registry.
        /// </summary>
        private static void DrawServerAdminsCombo()
        {
            #region Sync UI Selection From Registry

            // Rebuild the visible byte-id selection from the authoritative admin registry.
            SyncServerAdminUiSelectionFromRegistry();

            #endregion

            #region Draw Combo

            float width = Math.Max(260f, ImGui.GetContentRegionAvail().X);

            CB_MultiPCombo(
                "##server_admins_combo",
                ref _serverAdminAllPlayers,
                OnServerAdminsChanged,
                _serverAdminMultiTargetNetIds,
                enabled: true,
                width: width);

            #endregion
        }

        /// <summary>
        /// Synchronizes the visible admin UI selection with the authoritative server-command admin registry.
        ///
        /// Purpose:
        /// - Clears the current UI-side selected id list.
        /// - Rebuilds it by scanning current players and checking whether each gamertag is marked as admin.
        ///
        /// Notes:
        /// - This keeps the combo preview and check states accurate.
        /// - Uses current connected players only.
        /// </summary>
        private static void SyncServerAdminUiSelectionFromRegistry()
        {
            #region Reset UI State

            _serverAdminMultiTargetNetIds.Clear();
            _serverAdminAllPlayers = false;

            #endregion

            #region Rebuild From Connected Players

            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;

                if (ServerCommands.ServerCommandRegistry.IsAdminGamertag(g.Gamertag))
                    _serverAdminMultiTargetNetIds.Add(g.Id);
            }
            #endregion
        }

        /// <summary>
        /// Callback fired when the admin multi-player combo changes.
        ///
        /// Purpose:
        /// - Clears the current admin registry.
        /// - Rebuilds the authoritative admin list from the current selection.
        /// - Notifies players when they are promoted to admin.
        ///
        /// Notes:
        /// - "All Players" promotes all currently connected players.
        /// - Normal per-player selection promotes only the selected player ids.
        /// - Admin membership is stored by gamertag in the registry.
        /// - Notifications are only sent when a player's admin state actually changes.
        /// </summary>
        /// <param name="mode">The current target selection mode returned by the combo helper.</param>
        /// <param name="ids">Selected player ids when mode is Player.</param>
        private static void OnServerAdminsChanged(PlayerTargetMode mode, byte[] ids)
        {
            #region Snapshot Existing Admin Registry

            // Snapshot current admin state so we can notify only on actual changes.
            HashSet<string> previousAdmins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;
                if (string.IsNullOrWhiteSpace(g.Gamertag)) continue;

                if (ServerCommands.ServerCommandRegistry.IsAdminGamertag(g.Gamertag))
                    previousAdmins.Add(g.Gamertag);
            }
            #endregion

            #region Clear Existing Admin Registry

            ServerCommands.ServerCommandRegistry.ClearAdminGamertags();

            #endregion

            #region All Players Mode

            if (mode == PlayerTargetMode.AllPlayers)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    var g = _players[i];
                    if (g == null) continue;
                    if (string.IsNullOrWhiteSpace(g.Gamertag)) continue;

                    ServerCommands.ServerCommandRegistry.SetAdminGamertag(g.Gamertag, true);
                }

                NotifyAdminChanges(previousAdmins);
                return;
            }
            #endregion

            #region Explicit Player Selection Mode

            if (mode != PlayerTargetMode.Player || ids == null || ids.Length == 0)
            {
                NotifyAdminChanges(previousAdmins);
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                NetworkGamer g = FindPlayerById(ids[i]);
                if (g == null) continue;
                if (string.IsNullOrWhiteSpace(g.Gamertag)) continue;

                ServerCommands.ServerCommandRegistry.SetAdminGamertag(g.Gamertag, true);
            }

            NotifyAdminChanges(previousAdmins);

            #endregion
        }

        /// <summary>
        /// Compares the previous admin snapshot with the new registry state and sends
        /// promotion/demotion notices only to players whose state changed.
        ///
        /// Notes:
        /// - Promotion notice is the primary use-case.
        /// - Demotion notice is included as a quality-of-life follow-up.
        /// - Uses current connected players only.
        /// </summary>
        private static void NotifyAdminChanges(HashSet<string> previousAdmins)
        {
            #region Validate

            if (previousAdmins == null)
                return;

            #endregion

            #region Notify Current Connected Players

            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;
                if (string.IsNullOrWhiteSpace(g.Gamertag)) continue;

                bool wasAdmin = previousAdmins.Contains(g.Gamertag);
                bool isAdmin = ServerCommands.ServerCommandRegistry.IsAdminGamertag(g.Gamertag);

                // Newly promoted.
                if (!wasAdmin && isAdmin)
                {
                    SendServerAdminNotice(g, "[Server] You have been promoted to Admin. You may now use admin-only commands such as /kick, and /ban.");
                }
                // Optional: Newly demoted.
                else if (wasAdmin && !isAdmin)
                {
                    SendServerAdminNotice(g, "[Server] Your Admin permissions have been removed.");
                }
            }
            #endregion
        }

        /// <summary>
        /// Sends a private server/admin notice to the supplied player.
        ///
        /// Notes:
        /// - Mirrors the direct private-message path used elsewhere by the server command system.
        /// - Sends from the local host/operator to the requested target gamer.
        /// </summary>
        private static void SendServerAdminNotice(NetworkGamer to, string message)
        {
            #region Validate

            var from = CastleMinerZGame.Instance?.MyNetworkGamer;
            if (from == null || to == null || string.IsNullOrWhiteSpace(message))
                return;

            #endregion

            #region Send Private Message

            var sendInstance = MessageBridge.Get<BroadcastTextMessage>();
            sendInstance.Message = message;
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });

            #endregion
        }
        #endregion

        #region Tab: Server-History

        /// <summary>
        /// ======================================================================================
        /// Server-History
        /// --------------------------------------------------------------------------------------
        /// SUMMARY:
        ///   Shows a history of server hosts you have connected to.
        ///   - Identity: AlternateAddress (PlayerID removed)
        ///   - last-known-password: updated ONLY after successful join when a password was used
        ///   - IP: DATA ONLY, filled by network-sniffer heuristic; NEVER used to join
        ///
        /// UI:
        ///   • Search bar (filters name/alt/ip)
        ///   • Connect (matches against current session browser by alt then name)
        ///   • Optional password textbox (uses last-known-password if empty)
        ///   • Hide Passwords checkbox (default ON)
        ///   • Delete / Remove All
        ///
        /// FILE:
        ///   !Mods\CastleWallsMk2\CastleWallsMk2.ServerHistory.ini
        /// ======================================================================================
        /// </summary>
        ///
        /// NOTES:
        /// - Join is a 2-step pipeline:
        ///     (1) GetNetworkSessions (async callback, not guaranteed UI thread)
        ///     (2) QueueJoin -> DrainPendingJoinAndStatus -> ExecuteJoinOnUiThread
        /// - IP is "DATA ONLY": never used for join logic.
        /// - Join/search in-flight guard prevents spamming multiple searches/joins.
        /// - Manual double-click detection is used (ImGui.NET double-click can be inconsistent in tables).
        ///

        #region UI State & Fields

        private static string   _srvSearchText    = "";
        private static string   _srvPasswordText  = "";
        private static bool     _srvHidePasswords = true; // Default ON.
        private static string   _srvSelectedKey   = null;
        private static string   _srvLastStatus    = "";
        private static DateTime _srvLastStatusUtc = DateTime.MinValue;
        private static double   _srvLastRowClickTime;
        private static string   _srvLastRowClickKey;

        // Join/search in-flight protection (prevents double-click / spam breaking joins).
        private static bool     _srvJoinSearchInFlight;
        private static int      _srvJoinSearchToken;
        private static string   _srvJoinSearchForKey;
        private static string   _srvJoinSearchForName;

        #endregion

        #region Join Queue (Marshal GetNetworkSessions -> UI Thread)

        private static readonly object _srvJoinGate = new object();

        private static AvailableNetworkSession _srvJoinPendingSession;
        private static string                  _srvJoinPendingPassword;
        private static bool                    _srvJoinPendingUseFrontEndFlow;

        private static string                  _srvJoinPendingStatus; // Set from any thread, applied on UI thread.

        // Cache private FrontEndScreen.JoinGame(AvailableNetworkSession, string).
        private static MethodInfo _miFrontEndJoinGame;

        #endregion

        /// <summary>
        /// Draws the "Server" tab.
        /// </summary>
        private static void DrawServerHistoryTab()
        {
            #region 0) Apply Queued Status/Join Requests (UI Thread)

            // Apply any queued status/join requests (runs on UI thread).
            DrainPendingJoinAndStatus();

            #endregion

            #region 1) Load History + (Optional) Sniffer IP Attachment (DATA ONLY)

            ServerHistory.Load();

            // DATA ONLY: If sniffer capture is enabled, try to attach guessed host IP to current host entry.
            string snifferHostIp = null;
            if (_nsEnableCapture)
            {
                try
                {
                    var rows = _ipResolver.GetSnapshot();
                    snifferHostIp = GuessHostIp(rows);
                    if (!string.IsNullOrEmpty(snifferHostIp))
                        ServerHistory.TryUpdateCurrentHostIp(snifferHostIp);
                }
                catch { }
            }
            #endregion

            #region 2) Header / Status Line

            // Header / status.
            ImGui.TextUnformatted("Server Host History");
            ImGui.SameLine();
            ImGui.TextDisabled($"({ServerHistory.GetSnapshot().Count})");

            if (!string.IsNullOrEmpty(snifferHostIp))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted($"Captured Host IP: {snifferHostIp}");
            }

            if (!string.IsNullOrEmpty(_srvLastStatus))
            {
                ImGui.Spacing();
                ImGui.TextDisabled(_srvLastStatus);
            }

            ImGui.Spacing();

            #endregion

            #region 3) Search / File Actions

            // Search + actions row.
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Search:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(340f);
            ImGui.InputTextWithHint("##srv_search", "name / alt / ip", ref _srvSearchText, 256);

            ImGui.SameLine();
            if (ImGui.Button("Reload"))
            {
                ServerHistory.Reload();
                SetSrvStatus("Reloaded history from disk.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                ServerHistory.SaveIfDirty();
                SetSrvStatus($"Saved: {ServerHistory.FilePath}");
            }

            ImGui.SameLine();
            if (ImGui.Button("Remove All"))
                ImGui.OpenPopup("srv_clear_confirm");

            #endregion

            #region 4) Remove-All Confirmation Modal (No Dim Overlay)

            // Confirm clear popup.
            bool open = true;

            // Make this modal NOT dim the screen, and keep the popup itself fully opaque.
            var style = ImGui.GetStyle();
            var popBg = style.Colors[(int)ImGuiCol.PopupBg];
            popBg.W   = 1.0f;

            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0f)); // No screen overlay.
            ImGui.PushStyleColor(ImGuiCol.PopupBg,          popBg);                       // Opaque popup bg.
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha,         1.0f);                        // Ensure no inherited alpha.
            ImGui.SetNextWindowBgAlpha(1.0f);                                             // Belt + suspenders.

            if (ImGui.BeginPopupModal("srv_clear_confirm", ref open,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.TextWrapped("Remove ALL server history entries? This cannot be undone.");
                ImGui.Spacing();

                if (ImGui.Button("Yes, clear", new Vector2(120, 0)))
                {
                    ServerHistory.Clear();
                    ServerHistory.SaveIfDirty();
                    _srvSelectedKey = null;
                    SetSrvStatus("Cleared all server history.");
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                    ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);

            #endregion

            ImGui.Separator();

            #region 5) Connection Row (Disconnect + Cancel Search)

            // Connection row.
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Server:");

            bool   inSession = CastleMinerZGame.Instance != null && CastleMinerZGame.Instance.CurrentNetworkSession != null;
            bool   searching;
            string searchingName;
            lock (_srvJoinGate) { searching = _srvJoinSearchInFlight; searchingName = _srvJoinSearchForName; }

            if (!inSession) ImGui.BeginDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Disconnect", new Vector2(110, 0)))
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game != null && game.CurrentNetworkSession != null)
                    {
                        // Proper CMZ flow (handles UI/screens/cleanup).
                        game.EndGame(saveData: false); // Set true if you want to force-save on manual disconnect.
                    }
                }
                catch (Exception ex)
                {
                    SendLog($"[Server] Disconnect failed: {ex.Message}.");
                }
            }

            if (searching)
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel Search"))
                {
                    lock (_srvJoinGate)
                    {
                        _srvJoinSearchToken++;          // Invalidate callback.
                        _srvJoinSearchInFlight = false;
                        _srvJoinSearchForKey   = null;
                        _srvJoinSearchForName  = null;
                    }
                    SetSrvStatus("Canceled search.");
                }
            }

            if (!inSession) ImGui.EndDisabled();

            #endregion

            ImGui.Separator();

            #region 6) Build Filtered View (Search Text)

            // Pull + filter snapshot.
            var all = ServerHistory.GetSnapshot();

            var filtered = new List<ServerHistory.Entry>(all.Count);
            string q = (_srvSearchText ?? "").Trim();

            if (q.Length == 0)
            {
                filtered.AddRange(all);
            }
            else
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (Match(e, q))
                        filtered.Add(e);
                }
            }
            #endregion

            #region 7) Table: History List + Row Selection/Double-click Join

            // Table.
            ImGui.TextDisabled($"File: {ShortenForLog(ServerHistory.FilePath)}");
            ImGui.Spacing();

            var tableFlags =
                ImGuiTableFlags.Resizable   |
                ImGuiTableFlags.Reorderable |
                ImGuiTableFlags.Hideable    |
                ImGuiTableFlags.Sortable    |
                ImGuiTableFlags.RowBg       |
                ImGuiTableFlags.Borders     |
                ImGuiTableFlags.ScrollY;

            float availH = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 110f);

            bool joinBusy;
            lock (_srvJoinGate) joinBusy = _srvJoinSearchInFlight || _srvJoinPendingSession != null;
            bool canJoinFromRow = CastleMinerZGame.Instance != null && CastleMinerZGame.Instance.CurrentNetworkSession == null;

            if (ImGui.BeginTable("srv_history_table", 6, tableFlags, new Vector2(0, availH)))
            {
                ImGui.TableSetupColumn("Name",     ImGuiTableColumnFlags.WidthStretch, 0.32f);
                ImGui.TableSetupColumn("Alt",      ImGuiTableColumnFlags.WidthStretch, 0.20f);
                ImGui.TableSetupColumn("IP",       ImGuiTableColumnFlags.WidthStretch, 0.14f); // DATA ONLY.
                ImGui.TableSetupColumn("Password", ImGuiTableColumnFlags.WidthStretch, 0.14f);
                ImGui.TableSetupColumn("Last",     ImGuiTableColumnFlags.WidthStretch, 0.14f);
                ImGui.TableSetupColumn("Count",    ImGuiTableColumnFlags.WidthFixed,   64f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < filtered.Count; i++)
                {
                    var e = filtered[i];

                    bool selected = !string.IsNullOrEmpty(_srvSelectedKey) &&
                                    string.Equals(_srvSelectedKey, e.Key, StringComparison.OrdinalIgnoreCase);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    bool pressed = ImGui.Selectable(
                        $"{(string.IsNullOrEmpty(e.Name) ? "-" : e.Name)}##srv_{e.Key}_{i}",
                        selected,
                        ImGuiSelectableFlags.SpanAllColumns
                    );

                    if (pressed)
                    {
                        _srvSelectedKey = e.Key;

                        // Manual double-click detection (same key within time window).
                        double now = ImGui.GetTime();
                        bool isDouble =
                            string.Equals(_srvLastRowClickKey, e.Key, StringComparison.OrdinalIgnoreCase) &&
                            (now - _srvLastRowClickTime) <= 0.75;

                        _srvLastRowClickKey  = e.Key;
                        _srvLastRowClickTime = now;

                        if (isDouble)
                        {
                            // NOTE: Double-click here triggers a join request (Shift = ghost join).
                            if (joinBusy)
                            {
                                SetSrvStatus("Join in progress... please wait.");
                            }
                            else if (canJoinFromRow)
                            {
                                string typed = (_srvPasswordText ?? "").Trim();
                                string used  = !string.IsNullOrEmpty(typed) ? typed : (e.LastKnownPassword ?? "");

                                bool ghost = ImGui.GetIO().KeyShift;
                                BeginJoinQueued(e, used, useFrontEndFlow: !ghost);
                            }
                            else
                            {
                                SetSrvStatus("Join is disabled while you are already in a network session.");
                            }
                        }
                    }

                    // Alt.
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.AltAddress == 0 ? "-" : e.AltAddress.ToString(CultureInfo.InvariantCulture));

                    // IP (DATA ONLY).
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(string.IsNullOrEmpty(e.Ip) ? "-" : e.Ip);

                    // Password.
                    ImGui.TableNextColumn();
                    if (string.IsNullOrEmpty(e.LastKnownPassword))
                        ImGui.TextUnformatted("-");
                    else
                        ImGui.TextUnformatted(_srvHidePasswords ? "(set)" : e.LastKnownPassword);

                    // Last.
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.LastSeenUtc == DateTime.MinValue
                        ? "-"
                        : e.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

                    // Count.
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(e.TimesConnected.ToString(CultureInfo.InvariantCulture));

                    // Context menu on row (copy).
                    if (ImGui.BeginPopupContextItem($"srv_ctx_{e.Key}"))
                    {
                        if (ImGui.MenuItem("Copy Name")) ImGui.SetClipboardText(e.Name ?? "");
                        if (ImGui.MenuItem("Copy Alt")) ImGui.SetClipboardText(e.AltAddress.ToString(CultureInfo.InvariantCulture));
                        if (ImGui.MenuItem("Copy IP")) ImGui.SetClipboardText(e.Ip ?? "");

                        if (!_srvHidePasswords && !string.IsNullOrEmpty(e.LastKnownPassword))
                        {
                            if (ImGui.MenuItem("Copy Password")) ImGui.SetClipboardText(e.LastKnownPassword);
                        }
                        else
                        {
                            ImGui.BeginDisabled();
                            ImGui.MenuItem("Copy Password");
                            ImGui.EndDisabled();
                        }

                        ImGui.EndPopup();
                    }
                }

                ImGui.EndTable();
            }
            #endregion

            ImGui.Separator();

            #region 8) Selected Entry Actions (Connect / Ghost / Password / Steam / Delete)

            // Action row for selected entry.
            var selectedEntry = FindByKey(all, _srvSelectedKey);

            if (selectedEntry == null)
            {
                ImGui.TextDisabled("Select an entry above to enable Connect/Delete.");
            }
            else
            {
                ImGui.TextUnformatted($"Selected: {selectedEntry.Name}");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted($"Alt: {(selectedEntry.AltAddress == 0 ? "-" : selectedEntry.AltAddress.ToString(CultureInfo.InvariantCulture))}");

                ImGui.Spacing();

                // Hide passwords toggle (default ON).
                ImGui.Checkbox("Hide Passwords", ref _srvHidePasswords);

                // NOTE:
                // - joinBusy is meant to disable buttons while searching/queued.
                // - If you ever notice buttons are not disabling during joinBusy, double-check the boolean expression here.
                bool canJoin = joinBusy || CastleMinerZGame.Instance != null && CastleMinerZGame.Instance.CurrentNetworkSession == null;

                if (!canJoin) ImGui.BeginDisabled();

                // Connect (proper) + Connect (ghost-cli) + password box row.
                if (ImGui.Button("Connect", new Vector2(110, 0)))
                {
                    string typed = (_srvPasswordText ?? "").Trim();
                    string used = !string.IsNullOrEmpty(typed) ? typed : (selectedEntry.LastKnownPassword ?? "");

                    BeginJoinQueued(selectedEntry, used, useFrontEndFlow: true);
                }

                if (joinBusy && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Join in progress... please wait.");

                ImGui.SameLine();
                if (ImGui.Button("Connect (ghost-cli)", new Vector2(140, 0)))
                {
                    string typed = (_srvPasswordText ?? "").Trim();
                    string used = !string.IsNullOrEmpty(typed) ? typed : (selectedEntry.LastKnownPassword ?? "");

                    BeginJoinQueued(selectedEntry, used, useFrontEndFlow: false);
                }

                if (joinBusy && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Join in progress... please wait.");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(260f);
                var pwFlags = _srvHidePasswords ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
                ImGui.InputTextWithHint("##srv_pw", "password (optional)", ref _srvPasswordText, 128, pwFlags);

                if (!canJoin) ImGui.EndDisabled();

                ImGui.SameLine();
                bool hasSteamId = selectedEntry.AltAddress != 0;
                if (!hasSteamId) ImGui.BeginDisabled();
                if (ImGui.Button("View Players Steam", new Vector2(160, 0)))
                {
                    OpenUsersSteamProfile(selectedEntry.AltAddress);
                }
                if (!hasSteamId) ImGui.EndDisabled();
                if (!hasSteamId && ImGui.IsItemHovered())
                    ImGui.SetTooltip("No HostSteamID recorded yet (Alt=0). Join it once so the SteamID can be captured.");

                ImGui.SameLine();
                if (ImGui.Button("Delete", new Vector2(110, 0)))
                {
                    if (ServerHistory.Delete(selectedEntry.Key))
                    {
                        ServerHistory.SaveIfDirty();
                        _srvSelectedKey = null;
                        SetSrvStatus("Deleted entry.");
                    }
                }
            }
            #endregion

            ImGui.Spacing();
            ImGui.TextDisabled("Note: Join is disabled while you are already in a network session.");

            #region Local Helpers

            #region Sniffer -> Host IP Heuristic (DATA ONLY)

            // Match sniffer heuristic: Prefer CMZ OUT to public; then steam OUT to public.
            string GuessHostIp(List<IpResolver.SniffRow> list)
            {
                if (list == null) return null;

                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (string.Equals(r.Process, "CastleMinerZ", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Dir, "OUT", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(r.RemoteIp)
                        && !IsPrivate(r.RemoteIp))
                    {
                        return r.RemoteIp;
                    }
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (string.Equals(r.Process, "steam", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Dir, "OUT", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(r.RemoteIp)
                        && !IsPrivate(r.RemoteIp))
                    {
                        return r.RemoteIp;
                    }
                }

                return null;
            }

            bool IsPrivate(string ip)
            {
                if (string.IsNullOrEmpty(ip)) return false;
                return ip.StartsWith("192.168.") ||
                       ip.StartsWith("10.") ||
                       ip.StartsWith("172.16.") || ip.StartsWith("172.17.") ||
                       ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
                       ip.StartsWith("172.20.") || ip.StartsWith("172.21.") ||
                       ip.StartsWith("172.22.") || ip.StartsWith("172.23.") ||
                       ip.StartsWith("172.24.") || ip.StartsWith("172.25.") ||
                       ip.StartsWith("172.26.") || ip.StartsWith("172.27.") ||
                       ip.StartsWith("172.28.") || ip.StartsWith("172.29.") ||
                       ip.StartsWith("172.30.") || ip.StartsWith("172.31.") ||
                       ip == "127.0.0.1";
            }
            #endregion

            #region Filtering / Selection Helpers

            bool Match(ServerHistory.Entry e, string query)
            {
                if (e == null) return false;
                if (string.IsNullOrEmpty(query)) return true;

                var qc = query.ToLowerInvariant();

                if ((e.Name ?? "").ToLowerInvariant().Contains(qc)) return true;

                if (e.AltAddress != 0 &&
                    e.AltAddress.ToString(CultureInfo.InvariantCulture).Contains(qc))
                    return true;

                if ((e.Ip ?? "").ToLowerInvariant().Contains(qc)) return true;

                return false;
            }

            ServerHistory.Entry FindByKey(List<ServerHistory.Entry> list, string key)
            {
                if (list == null || string.IsNullOrEmpty(key)) return null;

                for (int i = 0; i < list.Count; i++)
                    if (string.Equals(list[i].Key, key, StringComparison.OrdinalIgnoreCase))
                        return list[i];

                return null;
            }
            #endregion

            #region Status + Join Pipeline (Search -> Queue -> Drain -> Execute)

            void SetSrvStatus(string s)
            {
                _srvLastStatus    = s ?? "";
                _srvLastStatusUtc = DateTime.UtcNow;
                SendLog("[Server] " + _srvLastStatus);
            }

            void BeginJoinQueued(ServerHistory.Entry entry, string passwordToUse, bool useFrontEndFlow)
            {
                if (entry == null) return;

                var game = CastleMinerZGame.Instance;
                if (game == null) { SetSrvStatus("ERROR: Game instance is null."); return; }
                if (game.CurrentNetworkSession != null) { SetSrvStatus("ERROR: Leave current session first."); return; }

                var signedIn = GetAnySignedInGamer();
                if (signedIn == null)
                {
                    SetSrvStatus("ERROR: No SignedInGamer found yet (profile not initialized). Try opening Multiplayer once, or host/leave once.");
                    return;
                }

                string pw = string.IsNullOrWhiteSpace(passwordToUse) ? null : passwordToUse.Trim();

                int token;
                lock (_srvJoinGate)
                {
                    // If we already queued a join, don't allow more until it drains.
                    if (_srvJoinPendingSession != null)
                    {
                        SetSrvStatus("Join already queued... (wait)");
                        return;
                    }

                    // If a search is already running, ignore new attempts (or optionally restart).
                    if (_srvJoinSearchInFlight)
                    {
                        SetSrvStatus($"Already searching sessions for {_srvJoinSearchForName ?? "server"}...");
                        return;
                    }

                    _srvJoinSearchInFlight = true;
                    _srvJoinSearchForKey   = entry.Key;
                    _srvJoinSearchForName  = entry.Name;

                    token = ++_srvJoinSearchToken; // token for THIS request
                }

                SetSrvStatus($"Searching sessions for {entry.Name}...");

                game.GetNetworkSessions(sessions =>
                {
                    try
                    {
                        // Ignore stale callbacks (in case you later add "restart search" behavior).
                        lock (_srvJoinGate)
                        {
                            if (token != _srvJoinSearchToken) return;
                            _srvJoinSearchInFlight = false;
                            _srvJoinSearchForKey   = null;
                            _srvJoinSearchForName  = null;
                        }

                        if (sessions == null)
                        {
                            QueueSrvStatus("Join error: session query returned null.");
                            return;
                        }

                        var match = FindSession(sessions, entry);
                        if (match == null)
                        {
                            QueueSrvStatus("No matching session found.");
                            return;
                        }

                        // IMPORTANT: Do NOT call JoinGame here (this callback is not guaranteed to be UI thread).
                        QueueJoin(match, pw, useFrontEndFlow);
                    }
                    catch (Exception ex)
                    {
                        QueueSrvStatus("Join error: " + ex.Message);
                    }
                });
            }

            AvailableNetworkSession FindSession(AvailableNetworkSessionCollection sessions, ServerHistory.Entry entry)
            {
                if (sessions == null || entry == null) return null;

                ulong wantAlt   = entry.AltAddress;          // == HostSteamID.
                string wantName = (entry.Name ?? "").Trim(); // == HostGamertag.

                // 1) SteamID match (preferred / stable).
                if (wantAlt != 0)
                {
                    foreach (AvailableNetworkSession s in sessions)
                    {
                        if (s == null) continue;

                        // HostSteamID is a public field.
                        if (s.HostSteamID != 0 && s.HostSteamID == wantAlt)
                            return s;
                    }
                }

                // 2) Name match fallback.
                if (!string.IsNullOrEmpty(wantName))
                {
                    foreach (AvailableNetworkSession s in sessions)
                    {
                        if (s == null) continue;

                        // HostGamertag is a public property.
                        string sname = s.HostGamertag;

                        if (!string.IsNullOrEmpty(sname) &&
                            string.Equals(sname, wantName, StringComparison.OrdinalIgnoreCase))
                            return s;
                    }
                }

                return null;
            }

            void DrainPendingJoinAndStatus()
            {
                AvailableNetworkSession pendingSession = null;
                string                  pendingPw      = null;
                bool                    pendingUseFE   = false;
                string                  pendingStatus  = null;

                lock (_srvJoinGate)
                {
                    pendingSession = _srvJoinPendingSession;
                    pendingPw      = _srvJoinPendingPassword;
                    pendingUseFE   = _srvJoinPendingUseFrontEndFlow;

                    _srvJoinPendingSession         = null;
                    _srvJoinPendingPassword        = null;
                    _srvJoinPendingUseFrontEndFlow = false;

                    pendingStatus = _srvJoinPendingStatus;
                    _srvJoinPendingStatus = null;
                }

                if (!string.IsNullOrEmpty(pendingStatus))
                    SetSrvStatus(pendingStatus);

                if (pendingSession != null)
                    ExecuteJoinOnUiThread(pendingSession, pendingPw, pendingUseFE);
            }

            void QueueSrvStatus(string s)
            {
                lock (_srvJoinGate)
                    _srvJoinPendingStatus = s ?? "";
            }

            void QueueJoin(AvailableNetworkSession s, string pw, bool useFrontEndFlow)
            {
                lock (_srvJoinGate)
                {
                    _srvJoinPendingSession         = s;
                    _srvJoinPendingPassword        = pw; // Can be null.
                    _srvJoinPendingUseFrontEndFlow = useFrontEndFlow;
                }
            }

            void ExecuteJoinOnUiThread(AvailableNetworkSession match, string pw, bool useFrontEndFlow)
            {
                var game = CastleMinerZGame.Instance;
                if (game == null) { SetSrvStatus("ERROR: game instance is null."); return; }
                if (game.CurrentNetworkSession != null) { SetSrvStatus("ERROR: Leave current session first."); return; }

                if (useFrontEndFlow)
                {
                    // Proper join: Uses FrontEndScreen private JoinGame(session, password).
                    var fe = game.FrontEnd;
                    if (fe == null) { SetSrvStatus("ERROR: FrontEnd is null."); return; }

                    if (_miFrontEndJoinGame == null)
                    {
                        _miFrontEndJoinGame = AccessTools.Method(
                            fe.GetType(),
                            "JoinGame",
                            new Type[] { typeof(AvailableNetworkSession), typeof(string) }
                        );
                    }

                    if (_miFrontEndJoinGame == null)
                    {
                        SetSrvStatus("ERROR: FrontEndScreen.JoinGame(session, password) not found.");
                        return;
                    }

                    try
                    {
                        _miFrontEndJoinGame.Invoke(fe, new object[] { match, pw });
                        SetSrvStatus("Joining (UI flow)...");
                    }
                    catch (Exception ex)
                    {
                        SetSrvStatus($"Join error (UI flow): {ex.Message}.");
                    }

                    return;
                }

                // Ghost join: Directly call game.JoinGame (no connecting/loading screens).
                try
                {
                    var signedIn = GetAnySignedInGamer();
                    game.JoinGame(
                        session:  match,
                        gamers:   new SignedInGamer[] { signedIn },
                        callback: (ok, message) =>
                        {
                            // Callback thread not guaranteed => queue status.
                            if (ok) QueueSrvStatus("Join started (ghost-cli).");
                            else QueueSrvStatus($"Join failed.{(string.IsNullOrEmpty(message) ? "" : ($" {message}"))}");
                        },
                        gameName: "CastleMinerZSteam",
                        version:  4,
                        password: pw
                    );

                    SetSrvStatus("Joining (ghost-cli)...");
                }
                catch (Exception ex)
                {
                    SetSrvStatus($"Join error (ghost-cli): {ex.Message}.");
                }
            }

            SignedInGamer GetAnySignedInGamer()
            {
                try
                {
                    var game   = CastleMinerZGame.Instance;
                    var fromMe = game?.MyNetworkGamer?.SignedInGamer;
                    if (fromMe != null) return fromMe;
                }
                catch { }

                // Fallback: Global list (usually populated earlier / more reliably).
                try
                {
                    foreach (var g in SignedInGamer.SignedInGamers)
                        if (g != null) return g;
                }
                catch { }

                // Last-ditch: Screen.CurrentGamer might be a SignedInGamer in some builds.
                try
                {
                    if (DNA.Drawing.UI.Screen.CurrentGamer is SignedInGamer sg)
                        return sg;
                }
                catch { }

                return null;
            }
            #endregion

            #region External Actions (Steam Profile)

            void OpenUsersSteamProfile(ulong steamId)
            {
                if (steamId == 0)
                {
                    SetSrvStatus("No SteamID recorded for this entry yet (Alt=0).");
                    return;
                }

                var uri = "steam://url/SteamIDPage/" + steamId.ToString(CultureInfo.InvariantCulture);

                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                catch
                {
                    try { Process.Start(uri); }
                    catch (Exception ex2) { SetSrvStatus($"Failed to open Steam profile: {ex2.Message}."); }
                }
            }
            #endregion

            #region Misc Helpers (Logging)

            /// <summary>
            /// Shorten a path for logging (e.g., C:\...\!Mods\...\CastleWallsMk2.ServerHistory.ini ->
            /// \!Mods\...\CastleWallsMk2.ServerHistory.ini).
            /// </summary>
            string ShortenForLog(string fullPath)
            {
                if (string.IsNullOrEmpty(fullPath))
                    return string.Empty;

                // Normalize slashes.
                var p = fullPath.Replace('/', '\\');

                // Prefer showing from "\!Mods\...".
                int idx = p.IndexOf(@"\!Mods\", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return p.Substring(idx);

                // Fallback: Full path.
                return p;
            }

            #endregion

            #endregion
        }
        #endregion

        #region Tab: Player Enforcement

        #region UI State

        /// <summary>
        /// Currently selected remote player ID in the lobby table.
        /// </summary>
        private static int _peSelectedPlayerId = -1;

        /// <summary>
        /// Currently selected persistent ban SteamID in the bans table.
        /// </summary>
        private static ulong _peSelectedBanSteamId;

        /// <summary>
        /// Currently selected persistent ban Gamertag in the bans table.
        /// </summary>
        private static string _peSelectedBanGamertag = "";

        /// <summary>
        /// Cached textbox contents for the host hard-ban deny message.
        /// </summary>
        private static string _peHardBanDenyText = "";

        /// <summary>
        /// Tracks whether the settings textbox cache has been initialized from config.
        /// </summary>
        private static bool _peConfigLoaded;

        #endregion

        #region Tab Draw Entry

        /// <summary>
        /// Draws the Player Enforcement tab.
        ///
        /// Summary:
        /// - Loads persistent bans and settings.
        /// - Draws the left-side lobby/actions pane.
        /// - Draws the right-side settings + persistent bans pane.
        ///
        /// Notes:
        /// - The tab is visible for both host and non-host users.
        /// - Host mode enables authoritative SteamID-based enforcement.
        /// - Non-host mode is intended for local config / local private-kick workflows.
        /// </summary>
        private static void DrawPlayerEnforcementTab()
        {
            PersistentBanStore.LoadOnce();
            PlayerEnforcementConfig.LoadOnce();

            if (!_peConfigLoaded)
            {
                _peHardBanDenyText = PlayerEnforcementConfig.HardBanDenyMessage ?? "Host Kicked Us";
                _peConfigLoaded = true;
            }

            CastleMinerZGame game    = CastleMinerZGame.Instance;
            NetworkSession   session = game?.CurrentNetworkSession;
            NetworkGamer     me      = game?.MyNetworkGamer;

            bool isHost = me != null && me.IsHost;

            ImGui.TextUnformatted("Player Enforcement");
            ImGui.SameLine();
            ImGui.TextDisabled(isHost ? "(Host)" : "(Local config / viewing)");

            ImGui.Spacing();

            float totalW = ImGui.GetContentRegionAvail().X;
            // float spacing = ImGui.GetStyle().ItemSpacing.X;
            float leftW = Math.Max(360f, totalW * 0.56f);
            // float rightW = Math.Max(320f, totalW - leftW - spacing);

            if (ImGui.BeginChild("##pe_left", new Vector2(leftW, 0)))
            {
                DrawTab_PlayerEnforcement_LobbyPane(session, isHost);
                ImGui.EndChild();
            }

            ImGui.SameLine();

            if (ImGui.BeginChild("##pe_right", new Vector2(0, 0)))
            {
                DrawTab_PlayerEnforcement_SettingsPane(isHost);
                ImGui.Separator();
                DrawTab_PlayerEnforcement_BanPane();
                ImGui.EndChild();
            }
        }
        #endregion

        #region Left Pane - Current Players / Actions

        /// <summary>
        /// Draws the current-players pane and action buttons.
        ///
        /// Summary:
        /// - Lists all remote players in the current session.
        /// - Shows basic per-player state indicators.
        /// - Enables/disables action buttons based on host state, checkbox settings,
        ///   and whether the selected target is the remote session host.
        ///
        /// Notes:
        /// - Non-host users may still use local/private workflows when enabled.
        /// - When a remote host is selected while not host, kick actions are disabled,
        ///   while ban actions may switch into "store only" behavior.
        /// </summary>
        private static void DrawTab_PlayerEnforcement_LobbyPane(NetworkSession session, bool isHost)
        {
            ImGui.TextUnformatted("Current Players");
            ImGui.Separator();

            float actionAreaHeight = 96f;
            float tableHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - actionAreaHeight - 8f);

            if (ImGui.BeginTable("pe_players", 4,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.ScrollY,
                new Vector2(0, tableHeight)))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0.42f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 56f);
                ImGui.TableSetupColumn("SteamID", ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthStretch, 0.24f);
                ImGui.TableHeadersRow();

                if (session != null)
                {
                    foreach (NetworkGamer gamer in session.AllGamers)
                    {
                        if (gamer == null || gamer.IsLocal)
                            continue;

                        ulong steamId = gamer.AlternateAddress;
                        bool selected = _peSelectedPlayerId == gamer.Id;

                        bool hardBlocked = ForcedDisconnectRuntime.IsBlockedSteamId(steamId);
                        bool persistBanned = PersistentBanStore.IsBanned(steamId);
                        bool tagBlocked = ForcedDisconnectRuntime.IsBlockedGamertag(gamer.Gamertag);

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        if (ImGui.Selectable($"{gamer.Gamertag}##pe_{gamer.Id}", selected, ImGuiSelectableFlags.SpanAllColumns))
                            _peSelectedPlayerId = gamer.Id;

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(gamer.Id.ToString(CultureInfo.InvariantCulture));

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(steamId == 0UL ? "-" : steamId.ToString(CultureInfo.InvariantCulture));

                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(
                            persistBanned ? "Persist Ban" :
                            hardBlocked ? "Hard Blocked" :
                            tagBlocked ? "Tag Blocked" :
                                            "Normal");
                    }
                }

                ImGui.EndTable();
            }

            NetworkGamer selectedGamer = FindSelectedRemoteGamer(session, _peSelectedPlayerId);

            ImGui.Spacing();

            bool hasTarget = selectedGamer != null;
            bool selectedIsRemoteHost = hasTarget && !isHost && IsRemoteSessionHost(selectedGamer);

            bool canVanillaKick = hasTarget &&
                                  (isHost || PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost) &&
                                  !selectedIsRemoteHost;

            bool canHardKick = hasTarget &&
                               (isHost || PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost) &&
                               !selectedIsRemoteHost;

            bool canVanillaBan = hasTarget &&
                                 (isHost || PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost);

            bool canHardBan = hasTarget &&
                              (isHost || PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost);

            string vanillaKickLabel =
                isHost ? "Vanilla Kick" :
                selectedIsRemoteHost ? "Vanilla Kick (Host Blocked)" :
                "Vanilla Kick (Private)";

            string vanillaBanLabel =
                isHost ? "Vanilla Ban" :
                selectedIsRemoteHost ? "Vanilla Ban (Store Only)" :
                "Vanilla Ban (Local)";

            string hardKickLabel =
                isHost ? "Hard Kick" :
                selectedIsRemoteHost ? "Hard Kick (Host Blocked)" :
                "Hard Kick (GT/Priv)";

            string hardBanLabel =
                isHost ? "Hard Ban" :
                selectedIsRemoteHost ? "Hard Ban (Store Only)" :
                "Hard Ban (GT/Priv)";

            float gap = ImGui.GetStyle().ItemSpacing.X;
            float buttonW = (ImGui.GetContentRegionAvail().X - gap) * 0.5f;

            if (!canVanillaKick) ImGui.BeginDisabled();
            if (ImGui.Button(vanillaKickLabel, new Vector2(buttonW, 0)))
                DoVanillaKick(selectedGamer);
            if (!canVanillaKick) ImGui.EndDisabled();

            ImGui.SameLine();

            if (!canVanillaBan) ImGui.BeginDisabled();
            if (ImGui.Button(vanillaBanLabel, new Vector2(buttonW, 0)))
                DoVanillaBan(selectedGamer);
            if (!canVanillaBan) ImGui.EndDisabled();

            if (!canHardKick) ImGui.BeginDisabled();
            if (ImGui.Button(hardKickLabel, new Vector2(buttonW, 0)))
                DoHardKick(selectedGamer);
            if (!canHardKick) ImGui.EndDisabled();

            ImGui.SameLine();

            if (!canHardBan) ImGui.BeginDisabled();
            if (ImGui.Button(hardBanLabel, new Vector2(buttonW, 0)))
                DoHardBan(selectedGamer);
            if (!canHardBan) ImGui.EndDisabled();

            if (!isHost)
            {
                ImGui.Spacing();

                ImGui.TextDisabled(
                    PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost
                        ? "Off-host: Vanilla actions use private kick/local save.\nHard actions use Gamertag + private-kick enforcement."
                        : "Off-host: Vanilla actions still work locally.\nEnable the checkbox for Hard GT/private enforcement.");
            }
        }
        #endregion

        #region Right Pane - Persistent Bans

        /// <summary>
        /// Draws the persistent bans pane.
        ///
        /// Summary:
        /// - Lists all saved persistent bans from the merged SteamID/Gamertag store.
        /// - Allows reloading and saving the store.
        /// - Allows removing the currently selected ban entry.
        ///
        /// Notes:
        /// - Selection supports SteamID-backed bans and Gamertag-only bans.
        /// - The remove button becomes enabled when either a SteamID or Gamertag ban is selected.
        /// </summary>
        private static void DrawTab_PlayerEnforcement_BanPane()
        {
            var bans = PersistentBanStore.GetSnapshot();

            ImGui.TextUnformatted("Persistent Bans");
            ImGui.SameLine();
            ImGui.TextDisabled($"({bans.Count})");

            ImGui.SameLine();
            if (ImGui.Button("Reload"))
                PersistentBanStore.Reload();

            ImGui.SameLine();
            if (ImGui.Button("Save"))
                PersistentBanStore.SaveIfDirty();

            ImGui.Separator();

            float tableHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 120f);

            if (ImGui.BeginTable("pe_bans", 4,
                ImGuiTableFlags.RowBg   |
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.ScrollY,
                new Vector2(0, tableHeight)))
            {
                ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("SteamID", ImGuiTableColumnFlags.WidthStretch, 0.33f);
                ImGui.TableSetupColumn("Mode",    ImGuiTableColumnFlags.WidthStretch, 0.16f);
                ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthStretch, 0.17f);
                ImGui.TableHeadersRow();

                foreach (var ban in bans)
                {
                    bool selected =
                        _peSelectedBanSteamId == ban.SteamId &&
                        string.Equals(_peSelectedBanGamertag ?? "", ban.MatchGamertag ?? "", StringComparison.OrdinalIgnoreCase);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    string displayName = string.IsNullOrWhiteSpace(ban.Name) ? "-" : ban.Name;
                    if (ImGui.Selectable(
                        $"{displayName}##ban_{ban.SteamId}_{ban.MatchGamertag}",
                        selected,
                        ImGuiSelectableFlags.SpanAllColumns))
                    {
                        _peSelectedBanSteamId = ban.SteamId;
                        _peSelectedBanGamertag = ban.MatchGamertag ?? "";
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ban.SteamId.ToString(CultureInfo.InvariantCulture));

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(string.IsNullOrWhiteSpace(ban.Mode) ? "-" : ban.Mode);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(
                        ban.CreatedUtc == DateTime.MinValue
                            ? "-"
                            : ban.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            bool hasSelectedBan =
                _peSelectedBanSteamId != 0UL ||
                !string.IsNullOrWhiteSpace(_peSelectedBanGamertag);

            if (!hasSelectedBan)
                ImGui.BeginDisabled();

            if (ImGui.Button("Remove Ban", new Vector2(140, 0)))
            {
                DoRemovePersistentBan(_peSelectedBanSteamId, _peSelectedBanGamertag);

                _peSelectedBanSteamId = 0UL;
                _peSelectedBanGamertag = "";
            }

            if (!hasSelectedBan)
                ImGui.EndDisabled();
        }
        #endregion

        #region Right Pane - Settings

        /// <summary>
        /// Draws the Player Enforcement settings pane.
        ///
        /// Summary:
        /// - Loads and edits non-host private-kick enforcement settings.
        /// - Exposes the host hard-ban deny message textbox.
        /// - Supports manual config reload/save.
        ///
        /// Notes:
        /// - The deny textbox is cached locally so it only initializes from config once.
        /// - Textbox edits mark the config dirty, but do not save immediately unless the user chooses save
        ///   or another save path later commits the config.
        /// </summary>
        private static void DrawTab_PlayerEnforcement_SettingsPane(bool isHost)
        {
            PlayerEnforcementConfig.LoadOnce();

            if (!_peConfigLoaded)
            {
                _peHardBanDenyText = PlayerEnforcementConfig.HardBanDenyMessage ?? "Host Kicked Us";
                _peConfigLoaded = true;
            }

            ImGui.TextUnformatted("Settings");
            ImGui.Separator();

            bool useTagMode = PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost;
            if (ImGui.Checkbox(PlayerEnforcementConfig.CheckboxLabel, ref useTagMode))
            {
                PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost = useTagMode;
                PlayerEnforcementConfig.MarkDirty();
            }

            ImGui.TextWrapped(PlayerEnforcementConfig.CheckboxHelp);

            ImGui.Spacing();
            ImGui.TextUnformatted(PlayerEnforcementConfig.HardBanMessageLabel);

            ImGui.PushItemWidth(-1f);
            if (ImGui.InputTextMultiline(
                "##pe_hardBanDenyText",
                ref _peHardBanDenyText,
                4096,
                new Vector2(-1f, 100f),
                ImGuiInputTextFlags.AllowTabInput))
            {
                PlayerEnforcementConfig.HardBanDenyMessage = _peHardBanDenyText ?? "";
                PlayerEnforcementConfig.MarkDirty();
            }
            ImGui.PopItemWidth();

            float gap = ImGui.GetStyle().ItemSpacing.X;
            float half = (ImGui.GetContentRegionAvail().X - gap) * 0.5f;

            if (ImGui.Button("Reload Settings", new Vector2(half, 0)))
            {
                PlayerEnforcementConfig.Reload();
                _peHardBanDenyText = PlayerEnforcementConfig.HardBanDenyMessage ?? "Host Kicked Us";
            }

            ImGui.SameLine();
            if (ImGui.Button("Save Settings", new Vector2(half, 0)))
                PlayerEnforcementConfig.SaveIfDirty();

            ImGui.Spacing();
            ImGui.TextDisabled(PlayerEnforcementConfig.ModeFooter(isHost));
        }
        #endregion

        #region Player Enforcement - Actions / Helpers

        #region Common Helpers

        /// <summary>
        /// Attempts to get the local host gamer.
        ///
        /// Summary:
        /// - Returns true only when the local player exists and is also the session host.
        /// </summary>
        private static bool TryGetHostLocal(out LocalNetworkGamer hostLocal)
        {
            hostLocal = CastleMinerZGame.Instance?.MyNetworkGamer as LocalNetworkGamer;
            return hostLocal != null && hostLocal.IsHost;
        }

        /// <summary>
        /// Returns true when the target is a non-local remote gamer.
        /// </summary>
        private static bool IsValidRemoteTarget(NetworkGamer gamer)
        {
            return gamer != null && !gamer.IsLocal;
        }

        /// <summary>
        /// Finds a selected remote gamer by runtime player ID.
        ///
        /// Notes:
        /// - Only searches remote/non-local players.
        /// </summary>
        private static NetworkGamer FindSelectedRemoteGamer(NetworkSession session, int playerId)
        {
            if (session == null || playerId < 0)
                return null;

            foreach (NetworkGamer gamer in session.AllGamers)
            {
                if (gamer != null && !gamer.IsLocal && gamer.Id == playerId)
                    return gamer;
            }

            return null;
        }

        /// <summary>
        /// Returns true when the given remote gamer is the current session host.
        ///
        /// Notes:
        /// - Used to prevent non-host private-kick flows from targeting the server host.
        /// </summary>
        private static bool IsRemoteSessionHost(NetworkGamer gamer)
        {
            if (gamer == null || gamer.IsLocal)
                return false;

            return gamer == gamer.Session?.Host || gamer.IsHost;
        }

        /// <summary>
        /// Attempts to resolve a known SteamID for a ban target.
        ///
        /// Summary:
        /// - Uses the target's AlternateAddress when available.
        /// - Falls back to the session host's AlternateAddress for off-host host-selection cases.
        ///
        /// Notes:
        /// - This is useful when non-host users want to store the real host SteamID locally
        ///   for future enforcement when they become host.
        /// </summary>
        private static ulong TryGetKnownSteamIdForBanTarget(NetworkGamer gamer)
        {
            if (gamer == null)
                return 0UL;

            // Normal case.
            if (gamer.AlternateAddress != 0UL)
                return gamer.AlternateAddress;

            // Off-host special case:
            // the selected gamer may be the session host, and the host object can still expose the real SteamID.
            if (gamer == gamer.Session?.Host || gamer.IsHost)
            {
                NetworkGamer host = gamer.Session?.Host;
                if (host != null && host.AlternateAddress != 0UL)
                    return host.AlternateAddress;
            }

            return 0UL;
        }
        #endregion

        #region Action: Vanilla Kick

        /// <summary>
        /// Sends the stock vanilla kick only.
        ///
        /// Summary:
        /// - Host: sends the normal KickMessage directly.
        /// - Non-host: may use local/private kick mode when enabled.
        ///
        /// Notes:
        /// - Non-host users cannot privately kick the session host.
        /// </summary>
        private static void DoVanillaKick(NetworkGamer gamer)
        {
            if (gamer == null || gamer.IsLocal)
                return;

            if (!(CastleMinerZGame.Instance?.MyNetworkGamer is LocalNetworkGamer local))
                return;

            try
            {
                if (local.IsHost)
                {
                    KickMessage.Send(local, gamer, false);
                    SendLog($"Vanilla Kicked Player: '{gamer.Gamertag}'.");
                    return;
                }

                if (!PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost)
                {
                    SendLog("Vanilla Kick skipped: Non-host private-kick mode is disabled.");
                    return;
                }

                if (IsRemoteSessionHost(gamer))
                {
                    SendLog($"Vanilla Kick skipped: session host cannot be privately kicked by a non-host.");
                    return;
                }

                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: false);
                SendLog($"Vanilla Kicked Player (private): '{gamer.Gamertag}'.");
            }
            catch (Exception ex)
            {
                SendLog($"Vanilla Kick failed for '{gamer?.Gamertag}': {ex.Message}.");
            }
        }
        #endregion

        #region Action: Vanilla Ban

        /// <summary>
        /// Adds the target to the persistent ban store and vanilla BanList,
        /// then sends the stock vanilla ban/kick.
        ///
        /// Summary:
        /// - Host: stores by SteamID, updates vanilla BanList, and sends KickMessage.
        /// - Non-host: stores locally by SteamID when known, otherwise by Gamertag.
        /// - Non-host host-selection becomes "store only" and does not try to private-kick the host.
        ///
        /// Notes:
        /// - When non-host and a real SteamID is known, it is preferred for persistence.
        /// </summary>
        private static void DoVanillaBan(NetworkGamer gamer)
        {
            if (gamer == null || gamer.IsLocal)
                return;

            if (!(CastleMinerZGame.Instance?.MyNetworkGamer is LocalNetworkGamer local))
                return;

            try
            {
                if (local.IsHost)
                {
                    ulong steamId = gamer.AlternateAddress;

                    if (steamId != 0UL)
                    {
                        PersistentBanStore.AddOrUpdate(
                            steamId,
                            gamer.Gamertag,
                            "VanillaBan");
                        PersistentBanStore.SaveIfDirty();

                        ForcedDisconnectRuntime.AddVanillaBan(steamId);
                    }

                    KickMessage.Send(local, gamer, true);
                    SendLog($"Vanilla Banned Player: '{gamer.Gamertag}'.");
                    return;
                }

                if (!PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost)
                {
                    SendLog("Vanilla Ban skipped: Non-host private-kick mode is disabled.");
                    return;
                }

                ulong knownSteamId = TryGetKnownSteamIdForBanTarget(gamer);

                if (knownSteamId != 0UL)
                {
                    PersistentBanStore.AddOrUpdate(
                        knownSteamId,
                        gamer.Gamertag,
                        "VanillaBan");
                }
                else
                {
                    PersistentBanStore.AddOrUpdateGamertag(
                        gamer.Gamertag,
                        gamer.Gamertag,
                        "VanillaBan");
                }

                PersistentBanStore.SaveIfDirty();

                if (IsRemoteSessionHost(gamer))
                {
                    SendLog($"Vanilla Banned Player (store only): '{gamer.Gamertag}' [SteamID: {knownSteamId}].");
                    return;
                }

                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: true);
                SendLog($"Vanilla Banned Player (local/private): '{gamer.Gamertag}'.");
            }
            catch (Exception ex)
            {
                SendLog($"Vanilla Ban failed for '{gamer?.Gamertag}': {ex.Message}.");
            }
        }
        #endregion

        #region Action: Hard Kick

        /// <summary>
        /// Uses the hard-kick flow.
        ///
        /// Summary:
        /// - Host: uses authoritative forced disconnect.
        /// - Non-host: uses local Gamertag block + private kick when enabled.
        ///
        /// Notes:
        /// - Non-host users cannot privately kick the session host.
        /// - Hard Kick does not create a persistent ban entry here.
        /// </summary>
        private static void DoHardKick(NetworkGamer gamer)
        {
            if (gamer == null || gamer.IsLocal)
                return;

            if (!(CastleMinerZGame.Instance?.MyNetworkGamer is LocalNetworkGamer local))
                return;

            try
            {
                if (local.IsHost)
                {
                    ForcedDisconnectRuntime.ForceDisconnectRemote(gamer, ban: false);
                    SendLog($"Hard Kicked Player: '{gamer.Gamertag}'.");
                    return;
                }

                if (!PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost)
                {
                    SendLog("Hard Kick skipped: Non-host private-kick mode is disabled.");
                    return;
                }

                if (IsRemoteSessionHost(gamer))
                {
                    SendLog($"Hard Kick skipped: session host cannot be privately kicked by a non-host.");
                    return;
                }

                ForcedDisconnectRuntime.BlockGamertag(gamer.Gamertag);
                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: false);
                SendLog($"Hard Kicked Player (GT/private): '{gamer.Gamertag}'.");
            }
            catch (Exception ex)
            {
                SendLog($"Hard Kick failed for '{gamer?.Gamertag}': {ex.Message}.");
            }
        }
        #endregion

        #region Action: Hard Ban

        /// <summary>
        /// Adds the target to the persistent ban store,
        /// then uses the hard-ban flow.
        ///
        /// Summary:
        /// - Host: stores by SteamID and performs authoritative forced disconnect.
        /// - Non-host: stores locally by SteamID when known, otherwise by Gamertag.
        /// - Non-host host-selection becomes "store only" and does not try to private-kick the host.
        ///
        /// Notes:
        /// - When non-host and a real SteamID is known, it is preferred for persistence.
        /// - Non-host hard-ban also records the configured hard-ban deny message in the stored entry.
        /// </summary>
        private static void DoHardBan(NetworkGamer gamer)
        {
            if (gamer == null || gamer.IsLocal)
                return;

            if (!(CastleMinerZGame.Instance?.MyNetworkGamer is LocalNetworkGamer local))
                return;

            try
            {
                if (local.IsHost)
                {
                    ulong steamId = gamer.AlternateAddress;

                    if (steamId != 0UL)
                    {
                        PersistentBanStore.AddOrUpdate(
                            steamId,
                            gamer.Gamertag,
                            "HardBan",
                            PlayerEnforcementConfig.HardBanDenyMessage);
                        PersistentBanStore.SaveIfDirty();
                    }

                    ForcedDisconnectRuntime.ForceDisconnectRemote(gamer, ban: true);
                    SendLog($"Hard Banned Player: '{gamer.Gamertag}'.");
                    return;
                }

                if (!PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost)
                {
                    SendLog("Hard Ban skipped: Non-host private-kick mode is disabled.");
                    return;
                }

                ulong knownSteamId = TryGetKnownSteamIdForBanTarget(gamer);

                if (knownSteamId != 0UL)
                {
                    PersistentBanStore.AddOrUpdate(
                        knownSteamId,
                        gamer.Gamertag,
                        "HardBan",
                        PlayerEnforcementConfig.HardBanDenyMessage);
                }
                else
                {
                    PersistentBanStore.AddOrUpdateGamertag(
                        gamer.Gamertag,
                        gamer.Gamertag,
                        "HardBan",
                        PlayerEnforcementConfig.HardBanDenyMessage);
                }

                PersistentBanStore.SaveIfDirty();

                if (IsRemoteSessionHost(gamer))
                {
                    SendLog($"Hard Banned Player (store only): '{gamer.Gamertag}' [SteamID: {knownSteamId}].");
                    return;
                }

                ForcedDisconnectRuntime.BlockGamertag(gamer.Gamertag);
                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: true);
                SendLog($"Hard Banned Player (GT/private): '{gamer.Gamertag}'.");
            }
            catch (Exception ex)
            {
                SendLog($"Hard Ban failed for '{gamer?.Gamertag}': {ex.Message}.");
            }
        }
        #endregion

        #region Action: Remove Persistent Ban

        /// <summary>
        /// Removes a persistent ban by SteamID and/or Gamertag.
        ///
        /// Summary:
        /// - Removes the persistent store entry when identifiers are present.
        /// - Clears runtime SteamID/Gamertag block state as applicable.
        /// - Saves the store when any removal actually occurred.
        ///
        /// Notes:
        /// - Supports mixed SteamID-backed and Gamertag-backed entries.
        /// </summary>
        private static void DoRemovePersistentBan(ulong steamId, string gamertag = null)
        {
            try
            {
                bool removedAny = false;

                if (steamId != 0UL)
                {
                    removedAny |= PersistentBanStore.Remove(steamId);
                    ForcedDisconnectRuntime.ClearBanState(steamId);
                }

                if (!string.IsNullOrWhiteSpace(gamertag))
                {
                    removedAny |= PersistentBanStore.RemoveByGamertag(gamertag);
                    ForcedDisconnectRuntime.UnblockGamertag(gamertag);
                }

                if (removedAny)
                {
                    PersistentBanStore.SaveIfDirty();
                    SendLog($"Removed ban: SteamID={steamId}, Gamertag='{gamertag ?? ""}'.");
                }
            }
            catch (Exception ex)
            {
                SendLog($"Remove ban failed: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #endregion

        #region Tab: Log

        #region Main Layout

        private static void DrawLogTabContent()
        {
            // Top controls row (Table: Left = controls, right = Max lines).
            // Read the pause state to detect an edge (unpause) later and do a one-time jump-to-bottom.
            bool pausedBefore = _logPaused;

            if (ImGui.BeginTable("log_top_controls", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("left",   ImGuiTableColumnFlags.WidthStretch, 1f);   // bulk of the controls.
                ImGui.TableSetupColumn("middle", ImGuiTableColumnFlags.WidthFixed,   1f);   // thin visual separator "|".
                ImGui.TableSetupColumn("right",  ImGuiTableColumnFlags.WidthFixed,   192f); // Max Lines input.

                // LEFT CELL: Clear/Save/Auto-scroll/Pause/Filter.
                ImGui.TableNextColumn();
                if (ImGui.Button("Clear"))        ChatLog.Clear();     ImGui.SameLine();
                if (ImGui.Button("Save"))         SaveLogWithDialog(); ImGui.SameLine();
                ImGui.Checkbox("Auto-scroll",     ref _logAutoScroll); ImGui.SameLine();
                ImGui.Checkbox("Pause",           ref _logPaused);     ImGui.SameLine();
                ImGui.TextDisabled("|");          ImGui.SameLine();
                ImGui.TextUnformatted("Filter:"); ImGui.SameLine();
                _logFilter.Draw("##logfilter", -1); // Width -1 = Take the rest of the cell.

                // MIDDLE CELL: Add separator (spacer column so we can right-align the next cell cleanly).
                ImGui.TableNextColumn();
                ImGui.TextDisabled("|");

                // RIGHT CELL: Right-aligned "Max lines" numeric up/down.
                ImGui.TableNextColumn();
                {
                    // Manually compute the label+input width and push the cursor to the cell's right edge
                    // so the input box hugs the right margin regardless of the overall window width.
                    var maxLinesStyle = ImGui.GetStyle();
                    string label      = "Max Lines:";
                    float labelW      = ImGui.CalcTextSize(label).X;
                    float inputW      = 110f; // Width for the InputInt field (enough for steppers + 6 digits).
                    float totalW      = labelW + maxLinesStyle.ItemInnerSpacing.X + inputW;

                    // Right-align inside the current table cell.
                    float cellW       = ImGui.GetColumnWidth();
                    float x           = ImGui.GetCursorPosX() + (cellW - totalW - maxLinesStyle.CellPadding.X);
                    ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));

                    ImGui.TextUnformatted(label);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(inputW);

                    int cap = ChatLog.MaxEntries;
                    // Step=500 shows +/- steppers; Step_fast=5000 for Ctrl+click power users.
                    if (ImGui.InputInt("##maxLines", ref cap, 500, 5000))
                    {
                        // ChatLog clamps, trims, and enforces bounds internally.
                        ChatLog.MaxEntries = cap; // Clamps & trims immediately.
                    }
                }

                ImGui.EndTable();
            }

            // If user just unpaused, force one jump-to-bottom once (sticky autoscroll is otherwise conditional).
            if (pausedBefore && !_logPaused)
                _scrollBottomOnUnpause = true;

            ImGui.Separator();

            /// <summary>
            /// Height math: Reserve space (command row + legend) so the MAIN WINDOW never scrolls.
            /// Compute an exact height for the scrolling child so only the child scrollbars move.
            /// </summary>
            var chatlogStyle = ImGui.GetStyle();
            float availH = ImGui.GetContentRegionAvail().Y; // Vertical space left in the window after the header table.

            const float SEP_H   = 2f;   // Separator thickness fudge (varies a bit across DPI/layouts).
            float sepAndSpacing = SEP_H + chatlogStyle.ItemSpacing.Y;

            float cmdRowH       = ImGui.GetFrameHeight() + chatlogStyle.CellPadding.Y * 2.0f; // Input/Send row fixed height.
            float legendRowH    = ImGui.GetFrameHeight();                                     // Colored swatches row.

            const float EPS     = 2.0f; // Extra cushion to avoid a 1px overflow on some DPI setups.
            float reservedBelow = sepAndSpacing + cmdRowH + chatlogStyle.ItemSpacing.Y + legendRowH + EPS;

            // What's left for the scrollable log?
            float logAvail = Math.Max(0f, availH - reservedBelow);

            /// <summary>
            /// Exact-rows sizing (same concept as PlayerList):
            ///   - Apply a tiny uniform padding within the child.
            ///   - Zero vertical ItemSpacing inside the child for tight packing / predictable math.
            ///   - Compute a child height that fits only whole "rows".
            /// </summary>
            float edgePad  = 5f;                         // Small uniform padding inside the child.
            float textH    = ImGui.GetFrameHeight();     // Single-line text height (including frame padding).
            float spacingY = chatlogStyle.ItemSpacing.Y; // Default vertical spacing (we'll zero it inside the child).
            float rowPitch = textH + spacingY;           // Pitch = row height + spacing.

            // How many rows "fit" in the available height (after padding)?
            // Include +spacingY in the numerator so the last row doesn't "half show".
            float visibleRows = (logAvail - edgePad * 2 + spacingY) / rowPitch;

            // Exact height for N rows: N * pitch - spacing + top/bottom padding.
            // This cancels the trailing spacing after the last row.
            float childH = (visibleRows * rowPitch - spacingY) + (edgePad * 2f);

            // Apply small padding just to this child; also zero vertical spacing so the math stays exact.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(edgePad, edgePad));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,  new Vector2(chatlogStyle.ItemSpacing.X, 0f));

            // Prefer no wrapping (lines can be long); give a horizontal scrollbar for discoverability.
            var winFlags = ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.HorizontalScrollbar;

            if (ImGui.BeginChild("chatlog_scroll", new Vector2(0, childH),
                                 ImGuiChildFlags.Borders, winFlags))
            {
                // Turn off the Selectable highlight fills so hover/active don't paint blue bars.
                // Using transparent keeps the content looking like flat text while retaining Selectable row padding.
                var backgroundColor = new Vector4(0,0,0,0); // Transparent.

                ImGui.PushStyleColor(ImGuiCol.Header,        backgroundColor); // Selected fill.
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, backgroundColor); // Hover fill.
                ImGui.PushStyleColor(ImGuiCol.HeaderActive,  backgroundColor); // Active fill.

                // Queue deletion and apply it after the draw loop to avoid mutation during enumeration.
                long? deleteSeq     = null;

                // SnapshotSorted() returns a copy sorted by (TimeUtc, Sequence) for stable rendering.
                // NOTE: If this becomes a perf hot-spot (very large logs + high FPS), consider incremental diffing.
                var snapshot = ChatLog.SnapshotSorted();
                foreach (var line in snapshot)
                {
                    // Prefix operator legend:
                    //   !!  = Console-ERR (stderr).
                    //   ==  = Console (stdout).
                    //   >>  = Outbound chat.
                    //   <<  = Inbound chat.
                    string op =
                        line.IsConsole
                            ? (string.Equals(line.Source, "Console-ERR", StringComparison.Ordinal) ? "!!" : "==")
                            : (line.Outbound ? ">>" : "<<");

                    // Override check for "Error" source.
                    if (string.Equals(line.Source, "ERR", StringComparison.Ordinal)) op = "!!";

                    // Pretty-printed row text (this is what the user sees and what we copy on demand).
                    string pretty = $"[{line.TimeUtc:HH:mm:ss}] {op} {line.Source}: {line.Text}";

                    // ImGuiTextFilter: Cheap client-side grep. Skip rows that don't pass.
                    if (!_logFilter.PassFilter(pretty)) continue;

                    // Color coding mirrors the legend under the list.
                    var color =
                        line.IsConsole
                            ? COLOR_CONSOLE        // Console (yellowish).
                        : line.Outbound
                            ? COLOR_OUTBOUND       // Outbound (bluish).
                            : COLOR_INBOUND;       // Inbound (greenish).

                    if ((line.IsConsole && string.Equals(line.Source, "Console-ERR", StringComparison.Ordinal)) ||
                                           string.Equals(line.Source, "ERR", StringComparison.Ordinal))
                        color = COLOR_CONSOLE_ERR; // Console-ERR (reddish).

                    // Render as a Selectable to get consistent row padding and hit-testing,
                    // but give it a hidden unique ID suffix to avoid ID collisions when texts repeat.
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.Selectable($"{pretty}##{line.Sequence}", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                    ImGui.PopStyleColor();

                    // Right-click context menu on this row. Add local ItemSpacing so the menu
                    // has breathing room even though the child uses ItemSpacing.Y = 0.
                    if (ImGui.BeginPopupContextItem($"ctx{line.Sequence}"))
                    {
                        var contextMenuStyle = ImGui.GetStyle();
                        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(contextMenuStyle.ItemSpacing.X, 5f)); // Vertical gap within the popup.

                        if (ImGui.MenuItem("Copy line"))     ImGui.SetClipboardText(pretty); ImGui.Separator();
                        if (ImGui.MenuItem("Copy source"))   ImGui.SetClipboardText(line.Source); ImGui.Separator();
                        if (ImGui.MenuItem("Copy sequence")) ImGui.SetClipboardText(line.Sequence.ToString()); ImGui.Separator();
                        if (ImGui.MenuItem("Copy time"))     ImGui.SetClipboardText(line.TimeUtc.ToString()); ImGui.Separator();
                        if (ImGui.MenuItem("Delete line"))   deleteSeq = line.Sequence; // Queued delete; applied after loop.

                        ImGui.PopStyleVar();
                        ImGui.EndPopup();
                    }

                    // NOTE: For double-click-to-copy, uncomment:
                    //   if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0)) ImGui.SetClipboardText(pretty);
                }

                // Apply deletion after iterating the snapshot (safe against collection mutation).
                if (deleteSeq.HasValue) ChatLog.Remove(deleteSeq.Value);

                ImGui.PopStyleColor(3); // Restore Header*, HeaderHovered, HeaderActive to previous values.

                /// <summary>
                /// Sticky autoscroll:
                ///  - If unpaused this frame, force-jump to bottom once (_scrollBottomOnUnpause).
                ///  - Otherwise, if Auto-scroll is on AND we're near bottom AND no filter is active,
                ///    keep the view glued to the newest content.
                /// </summary>
                bool filterActive = _logFilter.CountGrep > 0;
                if (_scrollBottomOnUnpause)
                {
                    ImGui.SetScrollY(ImGui.GetScrollMaxY());
                    _scrollBottomOnUnpause = false;
                }
                else if (_logAutoScroll && !_logPaused && !filterActive)
                {
                    if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5.0f)
                        ImGui.SetScrollHereY(1.0f);
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(2); // ItemSpacing, WindowPadding (restore globals for the rest of the window).

            ImGui.Spacing();
            ImGui.Separator();

            /// <summary>
            /// Bottom command row: [Input | Send | Raw].
            /// Using a 3-column table lets us keep the Send/Raw controls sized consistently,
            /// while the input box stretches and grabs the remaining width.
            /// </summary>
            if (ImGui.BeginTable("log_cmd_row", 3, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("input", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("send",  ImGuiTableColumnFlags.WidthFixed, 90f);
                ImGui.TableSetupColumn("raw",   ImGuiTableColumnFlags.WidthFixed, 100f);

                bool send = false;

                //if (!CastleWallsMk2.IsInGame() && !AllowOutOfGameSettingEdits()) ImGui.BeginDisabled(); // Disable when not in-game to avoid accidental submits.

                // INPUT (Enter to send - Up/Down for history).
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1); // Stretch to fill.

                // If we requested focus last frame, put it on the very next item (this InputText).
                if (_focusCmdNextFrame)
                {
                    ImGui.SetKeyboardFocusHere(); // Focus the next item submitted.
                    _focusCmdNextFrame = false;   // Consume the request.
                }

                var flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory;
                unsafe
                {
                    if (ImGui.InputTextWithHint("##cmd", "Type /command or chat... (use /p for private messaging)", ref _cmdInput, 512, flags, CmdHistoryCallback))
                        send = true;
                }

                // SEND BUTTON.
                ImGui.TableNextColumn();
                if (ImGui.Button("Send", new Vector2(-1, 0)))
                    send = true;

                // if (!CastleWallsMk2.IsInGame() && !AllowOutOfGameSettingEdits()) ImGui.EndDisabled();

                // RAW CHECKBOX (controls username prefix & slash normalization policy).
                ImGui.TableNextColumn();
                ImGui.TextDisabled("|"); ImGui.SameLine();
                ImGui.Checkbox("Send Raw", ref _cmdRaw);

                ImGui.EndTable();

                if (send)
                {
                    var cmd = _cmdInput?.Trim();
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        // Avoid duplicate consecutive entries (common when spamming the same command).
                        if (_cmdHistory.Count == 0 || _cmdHistory[_cmdHistory.Count - 1] != cmd)
                            _cmdHistory.Add(cmd);

                        // Optional: Cap history length to keep memory in check.
                        /*
                        const int MaxHist = 500;
                        if (_cmdHistory.Count > MaxHist)
                            _cmdHistory.RemoveRange(0, _cmdHistory.Count - MaxHist);
                        */
                    }

                    // Reset browsing state for the next entry.
                    _cmdHistoryPos   = -1;
                    _cmdHistoryDraft = string.Empty;

                    // Normalize + dispatch the command, clear the box, and refocus for fast iteration.
                    SendCommandBox(_cmdInput?.Trim());
                    _cmdInput              = string.Empty;
                    ImGui.SetKeyboardFocusHere(-1); // Refocus input on next frame (after this item).
                    _focusCmdNextFrame     = true;  // Request focus next frame.
                    _scrollBottomOnUnpause = true;  // Nudge list to bottom after our own send.
                }
            }

            // Legend row (colored swatches + labels). Mirrors the in-list color coding.
            ImGui.Spacing();
            ImGui.BeginGroup();
            {
                ImGui.TextDisabled("Legend:"); ImGui.SameLine();
                DrawLegendSwatch("Inbound (<<)"    , COLOR_INBOUND);     ImGui.SameLine();
                DrawLegendSwatch("Outbound (>>)"   , COLOR_OUTBOUND);    ImGui.SameLine();
                DrawLegendSwatch("Console (==)"    , COLOR_CONSOLE);     ImGui.SameLine();
                DrawLegendSwatch("Console-ERR (!!)", COLOR_CONSOLE_ERR);
            }
            ImGui.EndGroup();
        }
        #endregion

        #region Log Tab - Legend Helper

        // Tiny colored square + label; used for the legend row at the bottom.
        private static void DrawLegendSwatch(string label, System.Numerics.Vector4 color)
        {
            float h = ImGui.GetFrameHeight();       // Square ~ line height.
            var   sz = new Vector2(h - 4f, h - 4f); // Slight inset looks nicer.
            ImGui.ColorButton("##leg_" + label, color,
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoBorder,
                sz);
            ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.TextUnformatted(label);
        }
        #endregion

        #region Log Tab - Save Dialog

        private static void SaveLogWithDialog()
        {
            // Use STA thread for WinForms dialog; write UTF-8 file on success.
            var thread = new Thread(() =>
            {
                try
                {
                    using (var dlg = new SaveFileDialog())
                    {
                        dlg.Title = "Save Log";
                        dlg.Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                        dlg.DefaultExt = "log";
                        dlg.AddExtension = true;
                        dlg.CheckPathExists = true;
                        dlg.FileName = $"CMZ_{DateTime.Now:yyyyMMdd_HHmmss}.log";

                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            var text = ChatLog.DumpText(includeTimestamps: true, includeTrafficDirection: true);
                            File.WriteAllText(dlg.FileName, text, Encoding.UTF8);
                            ChatLog.Append("UI", $"Saved log to: {dlg.FileName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ChatLog.Append("UI", "Failed to save log: " + ex.Message);
                }
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        #endregion

        #region Log Tab - Filter Lifetime

        /// <summary>
        /// Disposes the Log tab ImGui text filter and clears its pointer.
        /// </summary>
        public static unsafe void DisposeLogFilter()
        {
            // Free ImGuiTextFilter when tearing down UI.
            if (_logFilter.NativePtr != null)
            {
                ImGuiNative.ImGuiTextFilter_destroy(_logFilter.NativePtr);
                _logFilter = new ImGuiTextFilterPtr(null);
            }
        }
        #endregion

        #region Log Tab - Input History Callback

        // Enable by adding ImGuiInputTextFlags.CallbackHistory and passing this callback
        // to InputTextWithHint. Up/Down will swap buffer text to history entries.
        private static unsafe int CmdHistoryCallback(ImGuiInputTextCallbackData* data)
        {
            // Only handle history browsing; bail fast otherwise.
            if (data->EventFlag != ImGuiInputTextFlags.CallbackHistory || _cmdHistory.Count == 0)
                return 0;

            if (data->EventKey == ImGuiKey.UpArrow)
            {
                // First time entering history from live typing: stash current edit.
                if (_cmdHistoryPos == -1)
                {
                    _cmdHistoryDraft = _cmdInput;           // Keep what the user was typing.
                    _cmdHistoryPos = _cmdHistory.Count - 1; // Jump to newest.
                    ReplaceBuffer(data, _cmdHistory[_cmdHistoryPos]);
                }
                else if (_cmdHistoryPos > 0)
                {
                    _cmdHistoryPos--;
                    ReplaceBuffer(data, _cmdHistory[_cmdHistoryPos]);
                }
                // else: Already at oldest; stay put.
            }
            else if (data->EventKey == ImGuiKey.DownArrow)
            {
                if (_cmdHistoryPos != -1)
                {
                    if (_cmdHistoryPos < _cmdHistory.Count - 1)
                    {
                        _cmdHistoryPos++;
                        ReplaceBuffer(data, _cmdHistory[_cmdHistoryPos]);
                    }
                    else
                    {
                        // Past the newest history item: Exit history back to the live draft.
                        _cmdHistoryPos = -1;
                        ReplaceBuffer(data, _cmdHistoryDraft ?? string.Empty);
                    }
                }
                // else: Already in live typing; nothing to do.
            }

            return 0;
        }

        // Overwrite ImGui input buffer with a UTF-8 string; marks buffer dirty so UI updates.
        private static unsafe void ReplaceBuffer(ImGuiInputTextCallbackData* data, string text)
        {
            int max = data->BufSize - 1;
            int len = 0;
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                len = Math.Min(bytes.Length, max);
                for (int i = 0; i < len; i++)
                    data->Buf[i] = (byte)(sbyte)bytes[i];
            }
            data->Buf[len]         = 0;
            data->BufTextLen       = len;
            data->CursorPos        = len;
            data->SelectionStart   = data->SelectionEnd = len;
            data->BufDirty         = 1;
        }
        #endregion

        #region Log Tab - Send / Normalization Rules

        // Normalize + send chat/commands based on Raw toggle and the session.
        // - RAW: Never prepend username; if message begins with "/" normalize to ": /" so the
        //        game's command interpreter picks it up. Else send as-is.
        // - NON-RAW: If message begins with "/", normalize to ": /" (no username). Otherwise,
        //            prepend "username: " to body before sending.
        private static void SendCommandBox(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Save to history (skip dup-in-a-row).
            if (_cmdHistory.Count == 0 || _cmdHistory[_cmdHistory.Count - 1] != text)
                _cmdHistory.Add(text);

            // Identify local gamer for username prefix.
            var me     = CastleMinerZGame.Instance?.MyNetworkGamer as LocalNetworkGamer;
            var myName = me?.SignedInGamer?.Gamertag ?? me?.Gamertag ?? "You";

            string normaliMob       = text;
            bool beginsSlash        = normaliMob.StartsWith("/");
            bool beginsColonSlash   = normaliMob.StartsWith(": /");

            if (_cmdRaw)
            {
                // RAW: Never add username; still map "/cmd" -> ": /cmd" so CMZ command parser accepts it.
                if (CastleMinerZGame.Instance.CurrentNetworkSession != null)
                    if (beginsSlash && !beginsColonSlash)
                        normaliMob = ": " + normaliMob; // "/foo" -> ":/foo"

                if (me != null && CastleMinerZGame.Instance.CurrentNetworkSession != null)
                {
                    BroadcastTextMessage.Send(me, normaliMob);
                }
                else
                {
                    Console.WriteLine(normaliMob); // offline echo
                }
                return;
            }

            // NON-RAW.
            if (beginsSlash)
            {
                // Commands: Send as ": /cmd" (no username prefix).
                if (CastleMinerZGame.Instance.CurrentNetworkSession != null)
                    if (!beginsColonSlash)
                        normaliMob = ": " + normaliMob;
            }
            else
            {
                // Normal chat: "username: message".
                normaliMob = $"{myName}: {normaliMob}";
            }

            if (me != null && CastleMinerZGame.Instance.CurrentNetworkSession != null)
            {
                BroadcastTextMessage.Send(me, normaliMob);
            }
            else
            {
                Console.WriteLine(normaliMob); // Offline echo.
            }
        }
        #endregion

        #endregion

        // =============================== //

        #region Render: Stats Window (Test Window)

        /// <summary>
        /// Debug / test: Small always-on stats panel, controlled by Callbacks.OnTest.
        /// </summary>
        internal static bool TestStatsVisible = false;

        /// <summary>
        /// Small always-on panel with basic game + enemy spawn stats.
        /// • Controlled by Callbacks.OnTest (via TestStatsVisible).
        /// • Does NOT have an [X] button - it only hides when Test is unchecked.
        /// </summary>
        private static void DrawTestStatsPanel()
        {
            if (!TestStatsVisible)
                return;

            var io = ImGui.GetIO();
            var game = CastleMinerZGame.Instance;
            var time = game?.CurrentGameTime;

            var vp = ImGui.GetMainViewport();
            var pos = vp.WorkPos + new Vector2(vp.WorkSize.X - 260f, 10f); // Top-right-ish.

            ImGui.SetNextWindowPos(pos, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowBgAlpha(0.65f);

            // NOTE: No ref bool -> no close button, it only hides when TestStatsVisible = false.
            if (ImGui.Begin("Test Stats###CastleWallsMk2_TestStats",
                            ImGuiWindowFlags.NoSavedSettings |
                            ImGuiWindowFlags.NoCollapse |
                            ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextDisabled("Peaceful-Glitch Investigator");
                ImGui.Separator();

                // --- Frame / timing ---
                ImGui.Text($"FPS: {io.Framerate:0}");
                if (time != null)
                {
                    ImGui.Text($"Total:   {time.TotalGameTime.TotalSeconds:0.0}s");
                    ImGui.Text($"Elapsed: {time.ElapsedGameTime.TotalMilliseconds:0} ms");
                }
                ImGui.Separator();

                // --- Enemy manager snapshot ---
                int totalEnemies          = 0;
                int baseZombieTargetedUs  = 0;
                int localSurfTargetingUs  = 0;
                int localCaveTargetingUs  = 0;
                int localAlienTargetingUs = 0;
                int distanceBudget        = 0;
                bool zombieFest           = false;

                var enemyMgr = EnemyManager.Instance;
                var localTag = game?.LocalPlayer?.Gamer.Tag;

                if (enemyMgr != null)
                {
                    // Total enemies in the manager (reflection helper already exists).
                    var allEnemies = EnemiesRef(enemyMgr);
                    if (allEnemies != null)
                    {
                        totalEnemies = allEnemies.Count;

                        foreach (BaseZombie z in allEnemies)
                        {
                            if (z == null)
                                continue;

                            // "WantedBy" semantics - same as your previous code: compare to Gamer.Tag.
                            if (localTag != null && Equals(z.Target, localTag))
                            {
                                baseZombieTargetedUs++;

                                // Mirror RemoveZombie() logic for local counters, but in our HUD:
                                //  • ALIEN -> localAlien
                                //  • ABOVEGROUND -> localSurface
                                //  • otherwise -> localCave
                                if (z.EType != null)
                                {
                                    if (z.EType.EType == EnemyTypeEnum.ALIEN)
                                    {
                                        localAlienTargetingUs++;
                                    }
                                    else if (z.EType.FoundIn == EnemyType.FoundInEnum.ABOVEGROUND)
                                    {
                                        localSurfTargetingUs++;
                                    }
                                    else
                                    {
                                        localCaveTargetingUs++;
                                    }
                                }
                            }
                        }
                    }

                    // Direct public fields/flags from EnemyManager.
                    distanceBudget = enemyMgr._distanceEnemiesLeftToSpawn;
                    zombieFest = enemyMgr.ZombieFestIsOn;
                }

                ImGui.TextDisabled("Enemy Manager");
                ImGui.Text($"Total Enemies:    {totalEnemies}");
                ImGui.Text($"WantedBy (us):    {baseZombieTargetedUs}");
                ImGui.Text($"  * Surf / Cave / Alien: {localSurfTargetingUs} / {localCaveTargetingUs} / {localAlienTargetingUs}");
                ImGui.Text($"Distance Budget:  {distanceBudget}");
                ImGui.Text($"ZombieFest:       {(zombieFest ? "ON" : "off")}");

                ImGui.Separator();

                // --- Player snapshot ---
                ImGui.TextDisabled("Player");
                ImGui.Text($"Alive:    {game.LocalPlayer.Dead == false}");
                ImGui.Text($"Gamemode: {game.GameMode}");
            }

            ImGui.End();
        }
        #endregion

        // =============================== //

        #region Data Feed (Players)

        // Lightweight data provider for the ImGui panel.
        // Caches player names and refreshes them at a small, fixed cadence to avoid
        // doing expensive session queries every frame (which could be 60+ Hz).
        internal static class ImGuiDataFeed
        {
            // Small working cache reused across calls (avoids GC churn).
            private static readonly List<NetworkGamer> _cache = new List<NetworkGamer>(16);

            // Next timestamp (in seconds) when we are allowed to refresh the cache.
            private static double _nextRefreshSec;       // Throttle clock.

            // How often to refresh (seconds). 0.25s = 4 times per second.
            private const double REFRESH_PERIOD = 0.25; // 4 Hz.

            /// <summary>
            /// Returns a (potentially cached) list of player display names.
            /// Pass force=true to bypass the throttle and rebuild immediately.
            /// </summary>
            public static IReadOnlyList<NetworkGamer> GetPlayers(bool force = false)
            {
                double now = GetNowSeconds();

                // Throttle: If we're still inside the quiet period, return the previous list.
                if (!force && now < _nextRefreshSec)
                    return _cache;

                // Set next allowed refresh time and rebuild the cache.
                _nextRefreshSec = now + REFRESH_PERIOD;
                _cache.Clear();

                try
                {
                    var game = CastleMinerZGame.Instance;
                    var session = game?.CurrentNetworkSession;
                    if (session != null)
                    {
                        foreach (var g in session.AllGamers)
                        {
                            _cache.Add(g);
                        }
                    }
                }
                catch
                {
                    // Never let the UI crash due to transient session/game issues.
                    // We simply keep whatever was in the cache (possibly empty).
                }

                // Keep the list tidy and predictable (local is already first if present).
                if (_cache.Count > 1)
                {
                    // Sort the users, keeping us at the top.
                    var me = CastleMinerZGame.Instance?.MyNetworkGamer;
                    _cache.Sort((a, b) =>
                    {
                        bool aIsMe = ReferenceEquals(a, me);
                        bool bIsMe = ReferenceEquals(b, me);
                        if (aIsMe != bIsMe) return aIsMe ? -1 : 1;
                        return StringComparer.OrdinalIgnoreCase.Compare(a?.Gamertag ?? "", b?.Gamertag ?? "");
                    });

                    // Sort all users alphabetically.
                    /*
                    if (_cache.Count > 1)
                        _cache.Sort((a, b) =>
                            StringComparer.OrdinalIgnoreCase.Compare(a?.Gamertag ?? string.Empty,
                                                                     b?.Gamertag ?? string.Empty));
                    */
                }
                return _cache;
            }

            /// <summary>
            /// Returns a monotonic-ish "now" in seconds.
            /// Prefer the game's TotalGameTime when available; Otherwise fall back to
            /// Environment.TickCount to keep working outside the game loop.
            /// </summary>
            private static double GetNowSeconds()
            {
                var gameTime = CastleMinerZGame.Instance?.CurrentGameTime;
                return (gameTime != null)
                    ? gameTime.TotalGameTime.TotalSeconds
                    : Environment.TickCount / 1000.0;
            }
        }
        #endregion

        #region Public API (Data Feed From Game)

        public static void SetPlayers(IReadOnlyList<NetworkGamer> gamers)
        {
            // Remember previous selection.
            var prev       = SelectedGamer;
            byte? prevId   = prev?.Id;
            string prevTag = prev?.Gamertag;

            _players.Clear();
            if (gamers != null)
                _players.AddRange(gamers.Where(g => g != null));

            // Try restore by Id, then by Gamertag.
            int idx = -1;
            if (prevId.HasValue)
                idx = _players.FindIndex(g => g.Id == prevId.Value);

            if (idx < 0 && !string.IsNullOrEmpty(prevTag))
                idx = _players.FindIndex(g => string.Equals(g.Gamertag, prevTag, StringComparison.Ordinal));

            if (idx >= 0) _playerIndex = idx;
            else if (_playerIndex >= _players.Count) _playerIndex = _players.Count - 1;

            // Ensure valid selection
            if (_players.Count > 0 && _playerIndex < 0) _playerIndex = 0;
            if (_players.Count == 0) _playerIndex = -1;
        }
        #endregion

        #region Helpers: UI State / Out-of-Game Edit Helpers

        /// <summary>
        /// Returns whether the UI is allowed to edit stored setting values while the player
        /// is not currently inside an active game world/session.
        /// </summary>
        private static bool AllowOutOfGameSettingEdits()
        {
            var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();
            return cfg.AllowOutOfGameSettingEdits;
        }

        /// <summary>
        /// Returns true when a setting change should update only the stored UI/config value
        /// for now, and defer any live in-game apply callback until a game session is active.
        /// </summary>
        private static bool ShouldDeferLiveApply()
        {
            return !CastleWallsMk2.IsInGame() && AllowOutOfGameSettingEdits();
        }
        #endregion

        #region Helpers: Window Placement / Size Tracking / Splitter

        // Title shows version + FPS + current visibility toggle text.
        // NOTE: We append a stable ID after "###" in Draw() so the window identity never changes
        // (prevents flicker/reset if the title text changes due to FPS or other dynamic parts).
        private static Vector2 _lastWindowSize = new Vector2(0, 0);

        // Tiny clamp helpers (no System.Math.Clamp on older frameworks).
        private static int   Clamp(int v  , int lo  , int hi)   => v < lo ? lo : (v > hi ? hi : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        // Splitter-controlled row height for the Player group's scroll area.
        private static float _playerRowsF    = 15f; // Live row count as float (drag-adjusted).
        private const int    MIN_PLAYER_ROWS = 2;
        private const int    MAX_PLAYER_ROWS = 20;

        // Prevent the window from drifting off-screen (especially if users change resolution).
        private static void KeepWindowOnScreen()
        {
            var vp = ImGui.GetMainViewport();
            var screenMin = vp.WorkPos;
            var screenMax = vp.WorkPos + vp.WorkSize;

            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();

            // Nudge window back in if any edge is outside.
            float x = Math.Max(screenMin.X, Math.Min(pos.X, screenMax.X - Math.Max(100f, size.X)));
            float y = Math.Max(screenMin.Y, Math.Min(pos.Y, screenMax.Y - Math.Max(60f, size.Y)));

            if (x != pos.X || y != pos.Y)
                ImGui.SetWindowPos(new Vector2(x, y));
        }

        public static bool GameIsMinimiMob()
        {
            var g = CastleMinerZGame.Instance;
            if (g?.Window == null) return true;
            var b = g.Window.ClientBounds;
            return b.Width <= 0 || b.Height <= 0;
        }

        /// <summary>
        /// Returns a width large enough to fit the widest line of 'text',
        /// including a little padding so the caret doesn't hug the edge.
        /// Intended for sizing an InputTextMultiline so the CHILD owns scrollbars.
        /// </summary>
        static float CalcContentWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            // ImGui measures single lines; normalize CRLF -> LF, then scan all lines.
            float max = 0f;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                max = Math.Max(max, ImGui.CalcTextSize(line).X);

            // Add frame padding so text doesn't touch the widget's inner border,
            // plus a small buffer for the caret / visual breathing room.
            var st = ImGui.GetStyle();
            return max + st.FramePadding.X * 2f + 10f; // +10f = caret/headroom.
        }

        /// <summary>
        /// Estimates the full height needed to show all lines (no wrapping),
        /// including top/bottom frame padding. Use when you want the child
        /// to handle vertical scrolling (make the input taller than its content).
        /// </summary>
        static float CalcContentHeight(string text)
        {
            // Count '\n' occurrences to get number of lines (no wrap).
            int lines = 1 + (string.IsNullOrEmpty(text) ? 0 : text.Count(c => c == '\n'));

            // One line of text (matches current font/scale) + vertical padding.
            float lineH = ImGui.GetTextLineHeight();
            var st = ImGui.GetStyle();
            return lines * lineH + st.FramePadding.Y * 2f;
        }

        /// <summary>
        /// Center a single-line text label within the current content width
        /// (column/group/window). Use after TableNextColumn() to center in that cell.
        /// </summary>
        public static void CenterText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Width of the text and currently available width in this region.
            float w = ImGui.CalcTextSize(text).X;
            float avail = ImGui.GetContentRegionAvail().X;

            // Shift the cursor by half the leftover space (if any).
            float off = (avail - w) * 0.5f;
            if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);

            // Draw without extra formatting (honors current color/style).
            ImGui.TextUnformatted(text);
        }

        /// <summary>
        /// Center a single-line label within the current region and (optionally) tint it.
        /// Pass null to use the current ImGui style color. Use after TableNextColumn() to center in that cell.
        /// </summary>
        public static void CenterText(string text, Color? xnaColor)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Width of the text and currently available width in this region.
            float w = ImGui.CalcTextSize(text).X;
            float avail = ImGui.GetContentRegionAvail().X;

            // Shift the cursor by half the leftover space (if any).
            float off = (avail - w) * 0.5f;
            if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);

            if (xnaColor.HasValue)
            {
                // Draw with extra color formatting (honors current style).
                var c  = xnaColor.Value;
                var v4 = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
                ImGui.TextColored(v4, text);
            }
            else
            {
                // Draw without extra formatting (honors current color/style).
                ImGui.TextUnformatted(text);
            }
        }

        /// <summary>
        /// Center the next item (e.g., combo, input, button) by pre-setting its X
        /// and telling ImGui how wide it will be. Pass your target width in pixels.
        /// </summary>
        static void CenterNextItem(float itemWidth)
        {
            float avail = ImGui.GetContentRegionAvail().X;

            // Shift so the next item starts centered; then set its width.
            float off = (avail - itemWidth) * 0.5f;
            if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);

            ImGui.SetNextItemWidth(itemWidth);
        }
        #endregion

        #region Block ESP Color Picker Helpers

        /// <summary>
        /// Converts an XNA/MonoGame <see cref="Color"/> into an ImGui-friendly <see cref="Vector4"/>.
        /// Output channels are normalized from 0..255 into 0..1 for RGB + alpha.
        /// </summary>
        static Vector4 ToImVec4(Color c)
            => new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        /// <summary>
        /// Converts an ImGui color vector (0..1 per channel) back into an XNA/MonoGame <see cref="Color"/>.
        /// Channels are clamped to a safe range before constructing the runtime color.
        /// </summary>
        static Color FromImVec4(Vector4 v)
            => new Color(
                Microsoft.Xna.Framework.MathHelper.Clamp(v.X, 0f, 1f),
                Microsoft.Xna.Framework.MathHelper.Clamp(v.Y, 0f, 1f),
                Microsoft.Xna.Framework.MathHelper.Clamp(v.Z, 0f, 1f),
                Microsoft.Xna.Framework.MathHelper.Clamp(v.W, 0f, 1f));

        /// <summary>
        /// Draws a small clickable color dot for a block row and opens a popup color picker when pressed.
        /// Rendering is clipped to the visible child-region so off-screen rows do not spill draw calls
        /// outside the combo scroll area. Returns true if the user changed the color this frame.
        /// </summary>
        static bool DrawColorDotPicker(string id, ref Vector4 color, Vector2 clipMin, Vector2 clipMax, Vector2 size)
        {
            bool changed = false;

            ImGui.PushID(id);

            if (ImGui.InvisibleButton("##dotBtn", size))
                ImGui.OpenPopup("picker");

            Vector2 itemMin = ImGui.GetItemRectMin();
            Vector2 itemMax = ImGui.GetItemRectMax();

            bool visible =
                itemMax.X >= clipMin.X &&
                itemMax.Y >= clipMin.Y &&
                itemMin.X <= clipMax.X &&
                itemMin.Y <= clipMax.Y;

            if (visible)
            {
                Vector2 center = (itemMin + itemMax) * 0.5f;
                float radius = Math.Min(size.X, size.Y) * 0.5f - 1.5f;

                var dl = ImGui.GetWindowDrawList();
                dl.PushClipRect(clipMin, clipMax, true);
                dl.AddCircleFilled(center, radius, ImGui.ColorConvertFloat4ToU32(color), 16);
                dl.AddCircle(center, radius, ImGui.GetColorU32(ImGuiCol.Border), 16, 1f);
                dl.PopClipRect();
            }

            if (ImGui.BeginPopup("picker"))
            {
                ImGui.TextUnformatted("Tracer Color");
                ImGui.Separator();

                Vector4 tmp = color;
                if (ImGui.ColorPicker4("##picker", ref tmp,
                    ImGuiColorEditFlags.DisplayRGB |
                    ImGuiColorEditFlags.AlphaBar |
                    ImGuiColorEditFlags.PickerHueBar))
                {
                    color = tmp;
                    changed = true;
                }

                ImGui.EndPopup();
            }

            ImGui.PopID();
            return changed;
        }
        #endregion

        #region Helpers: Widgets

        #region Checkbox With Callback

        #region Basic: Checkbox

        // Helper: Only fire callback when the checkbox value actually changes.
        private static void CB_Checkbox(string label, ref bool value, Action<bool> cb, bool enabled = true)
        {
            if (ShouldHideControl(enabled))
                return;

            bool before = value;
            if (!enabled) ImGui.BeginDisabled();
            ImGui.Checkbox(label, ref value);
            if (!enabled) ImGui.EndDisabled();

            // Only invoke when the control is enabled and actually changed.
            if (enabled && value != before) cb?.Invoke(value);
        }
        #endregion

        #region Unique ID: Checkbox

        /// <summary>
        /// Checkbox helper that:
        /// • Makes IDs unique via "label##idSuffix" so the same visible label can be reused elsewhere.
        /// • Only fires the callback when the value actually changes.
        /// • Supports disabled rendering/interaction with a single flag.
        /// </summary>
        /// <param name="idSuffix">Optional unique tail for the ImGui ID (keeps visible text clean).</param>
        /// <param name="label">Visible text for the checkbox (before the '##').</param>
        /// <param name="value">Ref bool being edited.</param>
        /// <param name="cb">Callback invoked when 'value' changes.</param>
        /// <param name="enabled">When false: renders grayed and blocks interaction.</param>
        /// <returns>true if ImGui reported a change this frame.</returns>
        static bool CB_Checkbox(
            string idSuffix,
            string label,
            ref bool value,
            Action<bool> cb,
            bool enabled = true)
        {
            if (ShouldHideControl(enabled))
                return false;

            // ImGui ID rule: "Shown Text##HiddenId". Everything after "##" is the unique ID.
            string id = idSuffix == null ? label : $"{label}##{idSuffix}";

            if (!enabled) ImGui.BeginDisabled();

            bool before  = value;
            bool changed = ImGui.Checkbox(id, ref value);

            if (!enabled) ImGui.EndDisabled();

            // Only notify when the stored value actually changed.
            if (changed && value != before)
                cb?.Invoke(value);

            return changed;
        }
        #endregion

        #endregion

        #region Thri-State Checkbox With Callback

        /// <summary>
        /// Tri-state checkbox without relying on ImGuiItemFlags.MixedValue.
        /// Visuals
        /// - Renders a normal checkbox (checked when On, empty when Off/Mixed).
        /// - If Mixed, draws a centered filled square inside the checkbox box.
        /// Behavior
        /// - If cycleAllStates = true, clicks/activations cycle Off -> On -> Mixed -> Off.
        /// - If cycleAllStates = false, behaves like a 2-way toggle (Mixed counts as Off; first click Mixed -> On).
        /// Implementation notes
        /// - The Mixed square is drawn immediately after the checkbox and is clipped to the item rect,
        ///   so it won't bleed through when scrolled out or covered by other widgets.
        /// - Uses squared drawing with the window draw list (not foreground) to inherit table/child clipping.
        /// Returns true if the state changed this frame.
        /// </summary>
        public enum TriState { Off, On, Mixed }

        private static bool CB_TriCheckbox(string label, ref TriState state, Action<TriState> cb, bool cycleAllStates = true)
        {
            // What the checkbox should display as (no checkmark for Mixed).
            bool renderOn = (state == TriState.On);

            // Draw checkbox (this also defines the item rect for overlay drawing).
            ImGui.Checkbox(label, ref renderOn);

            // Draw only if the item is visible and we're in Mixed state.
            if (state == TriState.Mixed && ImGui.IsItemVisible())
            {
                var draw       = ImGui.GetWindowDrawList(); // Use window list so normal clipping applies.
                var min        = ImGui.GetItemRectMin();    // Top-left of the checkbox+label item.
                var max        = ImGui.GetItemRectMax();    // Bottom-right.

                // Clip strictly to this item so the overlay never bleeds outside (e.g., when scrolled).
                draw.PushClipRect(min, max, true);

                // The checkbox square is a szxsz box at (min.X, min.Y).
                float sz       = ImGui.GetFrameHeight();                      // Matches native checkbox height/width.
                float inset    = (float)Math.Max(1f, Math.Round(sz * 0.25f)); // Inner padding for our square.

                // Outer checkbox box (for reference).
                var boxMin     = new Vector2(min.X, min.Y);
                var boxMax     = new Vector2(min.X + sz, min.Y + sz);

                // Inner square ("indeterminate" glyph). Inset so it looks balanced.
                var rMin       = new Vector2(boxMin.X + inset, boxMin.Y + inset);
                var rMax       = new Vector2(boxMax.X - inset, boxMax.Y - inset);

                // Colors: CheckMark gives good contrast on most themes.
                uint fill      = ImGui.GetColorU32(ImGuiCol.CheckMark);
                // uint border = ImGui.GetColorU32(ImGuiCol.Border);

                float round = (float)Math.Round(ImGui.GetStyle().FrameRounding * 0.35f);

                draw.AddRectFilled(rMin, rMax, fill, round);       // Filled inner square.
                // draw.AddRect(rMin, rMax, border, round, 0, 1f); // Optional: Thin outline.

                draw.PopClipRect();
            }

            // Click/activation handling.
            TriState newState = state;

            // Mouse click or keyboard activation (Space/Enter) toggles.
            bool activated = ImGui.IsItemClicked() || ImGui.IsItemActivated();

            if (activated)
            {
                if (cycleAllStates)
                {
                    newState = state == TriState.Off ? TriState.On
                             : state == TriState.On  ? TriState.Mixed
                             : /* Mixed */             TriState.Off;
                }
                else
                {
                    // Two-way toggle; Mixed behaves like Off (first click -> On).
                    newState = (state == TriState.On) ? TriState.Off : TriState.On;
                }
            }

            if (newState != state)
            {
                state = newState;
                cb?.Invoke(state);
                return true;
            }
            return false;
        }
        #endregion

        #region Slider With Callback

        #region Float: Slider

        /// <summary>
        /// Draws a float slider and invokes <paramref name="onChanged"/> only when the value actually changes.
        /// Returns true if the ImGui widget reported a change (same semantics as ImGui sliders).
        /// </summary>
        private static bool CB_Slider(
            string           label,
            ref float        value,
            Action<float>    onChanged = null,
            float            min       = 0.25f,
            float            max       = 5f,
            float            width     = -1,
            bool             enabled   = true,
            string           format    = null, // e.g. "%.2fx".
            bool             sameLine  = false,
            ImGuiSliderFlags flags     = ImGuiSliderFlags.AlwaysClamp)
        {
            if (ShouldHideControl(enabled))
                return false;

            float w = (width <= 0f) ? ImGui.GetContentRegionAvail().X : width;
            ImGui.SetNextItemWidth(w);

            if (sameLine) ImGui.SameLine();
            if (!enabled) ImGui.BeginDisabled();

            float before = value;
            bool changed = ImGui.SliderFloat(label, ref value, min, max, format ?? "%.3f", flags);

            if (!enabled) ImGui.EndDisabled();

            if (changed && value != before)
                onChanged?.Invoke(value);

            return changed;
        }
        #endregion

        #region Float: Stepper Slider

        /// <summary>
        /// Float variant of <see cref="CB_StepSlider(string, ref int, Action(int), int, int, int, int?, bool, string, ImGuiSliderFlags)"/>.
        /// </summary>
        private static bool CB_StepSlider(
            string           id,
            ref float        value,
            Action<float>    onChanged = null,
            float            min       = 0,
            float            max       = 100,
            float            step      = 1,
            float            width     = -1, // Total control width; null => CalcItemWidth().
            bool             enabled   = true,
            string           format    = "%d",
            ImGuiSliderFlags flags     = ImGuiSliderFlags.AlwaysClamp)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (!enabled) ImGui.BeginDisabled();

            var   style   = ImGui.GetStyle();
            float frameH  = ImGui.GetFrameHeight();
            float btnW    = frameH;                 // Square +/- buttons.
            float spacing = style.ItemSpacing.X;

            // Total width for [-][slider][+].
            float w = (width <= 0f) ? ImGui.GetContentRegionAvail().X : width;
            ImGui.SetNextItemWidth(w);

            float totalW = w;
            float sliderW = Math.Max(1f, 1 - (btnW * 2f) - (spacing * 2f));

            bool  changed = false;
            float before  = value;

            ImGui.PushID(id);

            // [-].
            if (ImGui.Button("-", new Vector2(btnW, 0f)))
                value = Math.Max(min, value - step);

            // [slider].
            ImGui.SameLine(0f, spacing);
            ImGui.SetNextItemWidth(sliderW);
            if (ImGui.SliderFloat("##slider", ref value, min, max, format, flags))
                changed = true;

            // [+].
            ImGui.SameLine(0f, spacing);
            if (ImGui.Button("+", new Vector2(btnW, 0f)))
                value = Math.Min(max, value + step);

            ImGui.PopID();

            if (!enabled) ImGui.EndDisabled();

            if (value != before) { changed = true; onChanged?.Invoke(value); }
            return changed;
        }
        #endregion

        #region Int: Slider

        /// <summary>
        /// Int variant of <see cref="CB_Slider(string, ref float, Action(float), float, float, float?, bool, string, ImGuiSliderFlags)"/>.
        /// </summary>
        private static bool CB_Slider(
            string           label,
            ref int          value,
            Action<int>      onChanged = null,
            int              min       = 0,
            int              max       = 5,
            float            width     = -1,
            bool             enabled   = true,
            string           format    = null, // e.g. "%.2fx".
            bool             sameLine  = false,
            ImGuiSliderFlags flags     = ImGuiSliderFlags.AlwaysClamp)
        {
            if (ShouldHideControl(enabled))
                return false;

            float w = (width <= 0f) ? ImGui.GetContentRegionAvail().X : width;
            ImGui.SetNextItemWidth(w);

            if (sameLine) ImGui.SameLine();
            if (!enabled) ImGui.BeginDisabled();

            int before = value;
            bool changed = ImGui.SliderInt(label, ref value, min, max, format ?? "%d", flags);

            if (!enabled) ImGui.EndDisabled();

            if (changed && value != before)
                onChanged?.Invoke(value);

            return changed;
        }
        #endregion

        #region Int: Stepper Slider

        /// <summary>
        /// Int variant of <see cref="CB_StepSlider(string, ref float, Action(float), float, float, float, float?, bool, string, ImGuiSliderFlags)"/>.
        /// </summary>
        private static bool CB_StepSlider(
            string           id,
            ref int          value,
            Action<int>      onChanged = null,
            int              min       = 0,
            int              max       = 100,
            int              step      = 1,
            float            width     = -1, // Total control width; null => CalcItemWidth().
            bool             enabled   = true,
            string           format    = "%d",
            ImGuiSliderFlags flags     = ImGuiSliderFlags.AlwaysClamp)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (!enabled) ImGui.BeginDisabled();

            var   style   = ImGui.GetStyle();
            float frameH  = ImGui.GetFrameHeight();
            float btnW    = frameH;                 // Square +/- buttons.
            float spacing = style.ItemSpacing.X;

            // Total width for [-][slider][+].
            float w = (width <= 0f) ? ImGui.GetContentRegionAvail().X : width;
            ImGui.SetNextItemWidth(w);

            float totalW = w;
            float sliderW = Math.Max(1f, totalW - (btnW * 2f) - (spacing * 2f));

            bool changed  = false;
            int  before   = value;

            ImGui.PushID(id);

            // [-].
            if (ImGui.Button("-", new Vector2(btnW, 0f)))
                value = Math.Max(min, value - step);

            // [slider].
            ImGui.SameLine(0f, spacing);
            ImGui.SetNextItemWidth(sliderW);
            if (ImGui.SliderInt("##slider", ref value, min, max, format, flags))
                changed = true;

            // [+].
            ImGui.SameLine(0f, spacing);
            if (ImGui.Button("+", new Vector2(btnW, 0f)))
                value = Math.Min(max, value + step);

            ImGui.PopID();

            if (!enabled) ImGui.EndDisabled();

            if (value != before) { changed = true; onChanged?.Invoke(value); }
            return changed;
        }
        #endregion

        #endregion

        #region Color Editor Callback

        /// <summary>
        /// Draws a simple color editor for any runtime color-backed setting.
        /// Optionally indents the control so it can appear as a child setting.
        /// Invokes the supplied callback when the color changes.
        /// </summary>
        static void DrawColorEditor(
            string              label,
            bool                enabled,
            Action<Color>       onChanged,
            Color               currentColor,
            bool                indent = true,
            ImGuiColorEditFlags flags  = ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.NoInputs)
        {
            if (ShouldHideControl(enabled))
                return;

            if (!enabled)
                return;

            if (indent)
                ImGui.Indent();

            Vector4 color = ToImVec4(currentColor);

            if (ImGui.ColorEdit4(label, ref color, flags))
                onChanged?.Invoke(FromImVec4(color));

            if (indent)
                ImGui.Unindent();
        }
        #endregion

        #region Combobox With Callback

        #region Combox Wrappers

        /// <summary>
        /// Thin convenience wrappers for specific datasets.
        /// Each one selects the right options[] and label converter, then forwards to CB_Combo/CB_ComboValue.
        /// </summary>

        #region Enemy

        /// <summary>Turn an EnemyTypeEnum into a user-facing name.</summary>
        static string EnemyName(EnemyTypeEnum e) => EnemyLabel(e);

        /// <summary>Index-based enemy combo. Keep a ref int index.</summary>
        static bool CB_EnemyCombo(string id, ref int index, float width = -1f, bool enabled = true)
            => CB_Combo(id, _enemyOptions, ref index, EnemyName, Callbacks.OnEnemy, width, enabled);

        /// <summary>Value-based enemy combo. Keep a ref EnemyTypeEnum value.</summary>
        static bool CB_EnemyComboV(string id, ref EnemyTypeEnum value, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _enemyOptions, ref value, EnemyName, Callbacks.OnEnemy, width, enabled);

        #endregion

        #region Dragon

        /// <summary>Turn a DragonTypeEnum into a user-facing name.</summary>
        static string DragonName(DragonTypeEnum d) => DragonLabel(d);

        /// <summary>Index-based dragon combo. Keep a ref int index.</summary>
        static bool CB_DragonCombo(string id, ref int index, float width = -1f, bool enabled = true)
            => CB_Combo(id, _dragonOptions, ref index, DragonName, Callbacks.OnDragon, width, enabled);

        /// <summary>Value-based dragon combo. Keep a ref DragonTypeEnum value.</summary>
        static bool CB_DragonCombo(string id, ref DragonTypeEnum value, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _dragonOptions, ref value, DragonName, Callbacks.OnDragon, width, enabled);

        #endregion

        #region Item

        /// <summary>Turn an InventoryItemIDs into a user-facing name.</summary>
        static string ItemName(InventoryItemIDs id) => ItemLabel(id.ToString());

        /// <summary>Index-based item combo. Keep a ref int index.</summary>
        static bool CB_ItemCombo(string id, ref int index, Action<InventoryItemIDs> onChanged, float width = -1f, bool enabled = true)
            => CB_Combo(id, _itemOptions, ref index, ItemName, onChanged, width, enabled);

        /// <summary>Value-based item combo. Keep a ref InventoryItemIDs value.</summary>
        static bool CB_ItemComboV(string id, ref InventoryItemIDs value, Action<InventoryItemIDs> onChanged, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _itemOptions, ref value, ItemName, onChanged, width, enabled);

        #endregion

        #region Block

        /// <summary>Turn an BlockTypeEnum into a user-facing name.</summary>
        static string BlockName(BlockTypeEnum id) => BlockLabel(id.ToString());

        /// <summary>Index-based block combo. Keep a ref int index.</summary>
        static bool CB_BlockCombo(string id, ref int index, Action<BlockTypeEnum> onChanged, float width = -1f, bool enabled = true)
            => CB_Combo(id, _blockOptions, ref index, BlockName, onChanged, width, enabled);

        /// <summary>Value-based block combo. Keep a ref BlockTypeEnum value.</summary>
        static bool CB_BlockComboV(string id, ref BlockTypeEnum value, Action<BlockTypeEnum> onChanged, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _blockOptions, ref value, BlockName, onChanged, width, enabled);

        #endregion

        #region Block Multi-Target

        /// <summary>Checkbox-based multi-block combo.</summary>
        static bool CB_MultiBCombo(string id, HashSet<BlockTypeEnum> selectedTypes, Action<BlockTypeEnum[]> onChanged, bool enabled = true, float width = -1f)
            => CB_BlockMultiCombo(id, selectedTypes, onChanged, enabled, width);

        #endregion

        #region Difficulty

        /// <summary>Turn a GameDifficultyTypes into a user-facing name.</summary>
        static string DifficultyName(GameDifficultyTypes id) => DifficultyLabel(id);

        /// <summary>Index-based difficulty combo.</summary>
        static bool CB_DiffCombo(string id, ref int index, float width = -1f, bool enabled = true)
            => CB_Combo(id, _difficultyOptions, ref index, DifficultyName, Callbacks.OnDifficulty, width, enabled);

        /// <summary>Value-based difficulty combo.</summary>
        static bool CB_DiffComboV(string id, ref GameDifficultyTypes value, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _difficultyOptions, ref value, DifficultyName, Callbacks.OnDifficulty, width, enabled);

        #endregion

        #region GameMode

        /// <summary>Turn a GameModeTypes into a user-facing name.</summary>
        static string GameModeName(GameModeTypes id) => GameModeLabel(id);

        /// <summary>Index-based game mode combo.</summary>
        static bool CB_GameMCombo(string id, ref int index, float width = -1f, bool enabled = true)
            => CB_Combo(id, _gameModeOptions, ref index, GameModeName, Callbacks.OnGameMode, width, enabled);

        /// <summary>Value-based game mode combo.</summary>
        static bool CB_GameMComboV(string id, ref GameModeTypes value, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _gameModeOptions, ref value, GameModeName, Callbacks.OnGameMode, width, enabled);

        #endregion

        #region Fireball

        /// <summary>Turn a FireballTypes into a user-facing name.</summary>
        static string FireballName(FireballTypes id) => FireballLabel(id);

        /// <summary>Index-based game mode combo.</summary>
        static bool CB_FireBCombo(string id, ref int index, float width = -1f, bool enabled = true)
            => CB_Combo(id, _fireballOptions, ref index, FireballName, Callbacks.OnFireball, width, enabled);

        /// <summary>Value-based game mode combo.</summary>
        static bool CB_FireBComboV(string id, ref FireballTypes value, float width = -1f, bool enabled = true)
            => CB_ComboValue(id, _fireballOptions, ref value, FireballName, Callbacks.OnFireball, width, enabled);

        #endregion

        #region Player Target

        /// <summary>Index-based game mode combo.</summary>
        static bool CB_IndexPCombo(string id, ref int index, Action<PlayerTargetMode, byte> onChanged, PlayerTargetMode modeNow, byte idNow, bool enabled = true, float width = -1f)
            => CB_PlayerCombo(id, ref index, onChanged, modeNow, idNow, enabled, width);

        /// <summary>
        /// Single-player target combo (None / specific Player / All Players).
        /// ---------------------------------------------------------------
        /// - Rebuilds the option list every frame from the current _players list.
        /// - Resolves the visible combo index from the authoritative state (mode + netId),
        ///   so selection stays stable across join/leave/reorder.
        /// - Uses locals (newMode/newId) to avoid capturing ref params in the lambda.
        /// - If the user changes selection, writes back mode/netId and fires onChanged.
        /// </summary>
        static bool CB_SinglePCombo(
            string                         id,
            ref int                        index,
            Action<PlayerTargetMode, byte> onChanged,
            ref PlayerTargetMode           mode,
            ref byte                       netId,
            bool                           enabled = true,
            float                          width   = -1f)
        {
            if (ShouldHideControl(enabled))
                return false;

            // Build options: (None), players, (All Players).
            _playerTargetOptions.Clear();

            _playerTargetOptions.Add(new PlayerTargetOption { Mode = PlayerTargetMode.None, Gamer = null });
            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;
                _playerTargetOptions.Add(new PlayerTargetOption { Mode = PlayerTargetMode.Player, Gamer = g });
            }
            _playerTargetOptions.Add(new PlayerTargetOption { Mode = PlayerTargetMode.AllPlayers, Gamer = null });

            // Resolve display index from current mode/id (keeps stable across join/leave/reorder).
            index = ResolvePlayerTargetIndex(mode, netId);

            // IMPORTANT: Don't capture ref params in lambda, use locals.
            var newMode = mode;
            var newId   = netId;

            bool changed = CB_Combo(
                id,
                _playerTargetOptions,
                ref index,
                PlayerTargetLabel,
                onChanged: opt =>
                {
                    if (opt.Mode == PlayerTargetMode.Player && opt.Gamer != null)
                    {
                        newMode = PlayerTargetMode.Player;
                        newId   = opt.Gamer.Id;
                    }
                    else
                    {
                        newMode = opt.Mode; // None / AllPlayers.
                        newId   = 0;
                    }
                },
                width: width,
                enabled: enabled);

            if (changed)
            {
                mode = newMode;
                netId = newId;
                onChanged?.Invoke(mode, netId);
            }

            return changed;
        }
        #endregion

        #region Player Multi-Target

        /// <summary>Checkbox-based game mode combo.</summary>
        static bool CB_MultiPCombo(string id, ref bool allPlayers, Action<PlayerTargetMode, byte[]> onChanged, HashSet<byte> selectedIds, bool enabled = true, float width = -1f)
            => CB_PlayerMultiCombo(id, ref allPlayers, onChanged, selectedIds, enabled, width);

        #endregion

        #endregion

        #region Index Based: Combobox

        /// <summary>
        /// Generic index-based combo:
        /// • Store selection as an index (ref int index).
        /// • ToLabel maps an option to the visible text.
        /// • OnChanged fires only when the selection actually changes.
        /// • Width: -1 (auto), 0 (don't set), >0 (fixed).
        /// • Enabled=false grays out and blocks interaction.
        /// Returns true if the selection changed.
        /// </summary>
        static bool CB_Combo<T>(
            string           id,
            IReadOnlyList<T> options,
            ref int          index,
            Func<T, string>  toLabel,
            Action<T>        onChanged = null,
            float            width     = -1f,
            bool             enabled   = true)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (options == null || options.Count == 0)
                return false;

            // Keep index in range so preview is always valid.
            index = Clamp(index, 0, options.Count - 1);
            string preview = toLabel(options[index]) ?? "(null)";

            if (width != 0) ImGui.SetNextItemWidth(width); // Width == 0 => call nothing (use current layout).
            if (!enabled) ImGui.BeginDisabled();

            bool changed = false;
            if (ImGui.BeginCombo(id, preview))
            {
                for (int i = 0; i < options.Count; i++)
                {
                    bool selected = (i == index);
                    string label = toLabel(options[i]) ?? "(null)";
                    if (ImGui.Selectable(label, selected))
                    {
                        if (index != i)
                        {
                            index   = i;
                            changed = true;
                        }
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            if (!enabled) ImGui.EndDisabled();

            if (changed) onChanged?.Invoke(options[index]);
            return changed;
        }
        #endregion

        #region Value Based: Combobox

        /// <summary>
        /// Generic value-based combo:
        /// • Store selection as the value (ref T value), great for enums.
        /// • Internally finds the current index, draws an index combo, then writes value back.
        /// • onChanged fires after value changes.
        /// Returns true if the selection changed.
        /// </summary>
        static bool CB_ComboValue<T>(
            string           id,
            IReadOnlyList<T> options,
            ref T            value,
            Func<T, string>  toLabel,
            Action<T>        onChanged = null,
            float            width     = -1f,
            bool             enabled   = true)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (options == null || options.Count == 0)
                return false;

            // Find current index for preview (O(n) once per draw; fine for small lists).
            int idx = 0;
            for (int i = 0; i < options.Count; i++)
                if (EqualityComparer<T>.Default.Equals(options[i], value)) { idx = i; break; }

            bool changed = CB_Combo(id, options, ref idx, toLabel, null, width, enabled);
            if (changed && idx >= 0 && idx < options.Count)
            {
                value = options[idx];
                onChanged?.Invoke(value);
            }
            return changed;
        }
        #endregion

        #region Index Based: Target Combobox

        /// <summary>
        /// Options:
        /// - (None)            [index 0]
        /// - [id] PlayerName   [index 1..N]
        /// - (All Players)     [last]
        ///
        /// Notes:
        /// - Reflects the same underlying _players list used by DrawPlayerList().
        /// - Index is re-synced from the shared tick state (_forceRespawnTargetMode/_forceRespawnTargetNetid)
        ///   so it stays stable if players join/leave or reorder.
        /// - On change, fires Callbacks.OnRespawnPlayer(mode, id).
        /// </summary>
        static bool CB_PlayerCombo(
            string                         id,
            ref int                        index,
            Action<PlayerTargetMode, byte> onChanged,
            PlayerTargetMode               modeNow,
            byte                           idNow,
            bool                           enabled = true,
            float                          width   = -1f)
        {
            if (ShouldHideControl(enabled))
                return false;

            // Build options: (None), players, (All Players).
            _playerTargetOptions.Clear();

            _playerTargetOptions.Add(new PlayerTargetOption
            {
                Mode  = PlayerTargetMode.None,
                Gamer = null
            });

            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;

                _playerTargetOptions.Add(new PlayerTargetOption
                {
                    Mode  = PlayerTargetMode.Player,
                    Gamer = g
                });
            }

            _playerTargetOptions.Add(new PlayerTargetOption
            {
                Mode  = PlayerTargetMode.AllPlayers,
                Gamer = null
            });

            index = ResolvePlayerTargetIndex(modeNow, idNow);

            // Draw combo and fire callback on user change.
            bool changed = CB_Combo(
                id,
                _playerTargetOptions,
                ref index,
                PlayerTargetLabel,
                onChanged: opt =>
                {
                    var cb = onChanged;
                    if (cb == null) return;

                    if (opt.Mode == PlayerTargetMode.Player && opt.Gamer != null)
                        cb(opt.Mode, opt.Gamer.Id);
                    else
                        cb(opt.Mode, 0); // None / AllPlayers => id = 0.
                },
                width: width,
                enabled: enabled
            );

            return changed;
        }

        static int ResolvePlayerTargetIndex(PlayerTargetMode mode, byte wantId)
        {
            // 0 = None; last = All; 1..N = player match by id.
            if (mode == PlayerTargetMode.None)
                return 0;

            if (mode == PlayerTargetMode.AllPlayers)
                return _playerTargetOptions.Count - 1;

            // Player mode: Find matching id in entries (1..N).
            for (int i = 1; i < _playerTargetOptions.Count - 1; i++)
            {
                var g = _playerTargetOptions[i].Gamer;
                if (g != null && g.Id == wantId)
                    return i;
            }

            // Player left / not found => fall back to (None).
            return 0;
        }

        static string PlayerTargetLabel(PlayerTargetOption opt)
        {
            if (opt.Mode == PlayerTargetMode.None)
                return "(None)";

            if (opt.Mode == PlayerTargetMode.AllPlayers)
                return "(All Players)";

            if (opt.Gamer == null)
                return "(invalid)";

            // Add an invisible unique suffix to prevent ImGui ID collisions on same-name players.
            return $"[{opt.Gamer.Id}] {DisplayName(opt.Gamer)}##pt{opt.Gamer.Id}";
        }
        #endregion

        #region Checkbox Based: Multi-Block Combobox

        /// <summary>
        /// Multi-select block combo for Block ESP.
        /// Lets the user check/uncheck multiple block types, preview the selection,
        /// optionally select none/all, and edit each block's runtime tracer color.
        /// Fires the callback only when the selected set changes.
        /// </summary>
        static bool CB_BlockMultiCombo(
            string id,
            HashSet<BlockTypeEnum>  selectedTypes,
            Action<BlockTypeEnum[]> onChanged,
            bool                    enabled = true,
            float                   width   = -1f)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (selectedTypes == null)
                return false;

            if (width > 0f)
                ImGui.SetNextItemWidth(width);

            string preview = BuildBlockMultiPreview(selectedTypes);
            bool changed = false;

            if (!enabled) ImGui.BeginDisabled(true);

            float comboW = (width > 0f) ? width : ImGui.GetContentRegionAvail().X;
            if (comboW <= 0f) comboW = 220f;

            // Make the popup width stable.
            float popupW = Math.Max(comboW, CalcMultiBlockPopupWidth());

            // Make the popup wide enough for the largest item.
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(popupW, 0f),
                new Vector2(popupW, 320f));

            if (ImGui.BeginCombo(id, preview))
            {
                // (None) => clear all.
                if (ImGui.Selectable("(None)", selectedTypes.Count == 0))
                {
                    selectedTypes.Clear();
                    changed = true;
                }

                // Select every listed block.
                if (ImGui.Selectable("(Select All Blocks)", false))
                {
                    selectedTypes.Clear();

                    for (int i = 0; i < _blockOptions.Length; i++)
                        selectedTypes.Add(_blockOptions[i]);

                    changed = true;
                }

                ImGui.Separator();

                float listH = Math.Max(180f, Math.Min(260f, _blockOptions.Length * 22f));
                float childW = Math.Max(1f, ImGui.GetContentRegionAvail().X);

                if (ImGui.BeginChild("##multi_blocks", new Vector2(childW, listH), ImGuiChildFlags.Borders))
                {
                    // Visible child rect in screen space.
                    Vector2 childClipMin = ImGui.GetWindowPos();
                    Vector2 childClipMax = childClipMin + ImGui.GetWindowSize();

                    for (int i = 0; i < _blockOptions.Length; i++)
                    {
                        BlockTypeEnum block = _blockOptions[i];
                        bool isSel = selectedTypes.Contains(block);

                        float rowStartY = ImGui.GetCursorPosY();

                        bool cb = isSel;
                        if (ImGui.Checkbox($"##mb_chk_{(int)block}", ref cb))
                        {
                            if (cb) selectedTypes.Add(block);
                            else selectedTypes.Remove(block);
                            changed = true;
                        }

                        ImGui.SameLine();

                        // Match the checkbox/frame height.
                        float frameH    = ImGui.GetFrameHeight();
                        Vector2 dotSize = new Vector2(14f, 14f);

                        // Center the dot vertically within the row/frame.
                        ImGui.SetCursorPosY(rowStartY + (frameH - dotSize.Y) * 0.5f);

                        Color runtimeColor = CastleWallsMk2.GetBlockEspTracerColor(block, i, _blockOptions.Length);
                        Vector4 uiColor = ToImVec4(runtimeColor);

                        if (DrawColorDotPicker($"mb_col_{(int)block}", ref uiColor, childClipMin, childClipMax, dotSize))
                        {
                            CastleWallsMk2.SetBlockEspTracerColor(block, FromImVec4(uiColor));
                        }

                        ImGui.SameLine();

                        // Put the label back on the normal row baseline.
                        ImGui.SetCursorPosY(rowStartY);

                        string label = $"{BlockName(block)}##mb_{(int)block}";
                        if (ImGui.Selectable(label, cb, ImGuiSelectableFlags.NoAutoClosePopups))
                        {
                            cb = !cb;
                            if (cb) selectedTypes.Add(block);
                            else selectedTypes.Remove(block);
                            changed = true;
                        }
                    }
                }
                ImGui.EndChild();

                ImGui.EndCombo();
            }
            if (!enabled) ImGui.EndDisabled();

            if (changed && onChanged != null)
                FireBlockMultiCallback(onChanged, selectedTypes);

            return changed;
        }

        /// <summary>
        /// Builds and fires the multi-block callback using a stable block order.
        /// Selected blocks are copied into a temporary list in _blockOptions order
        /// before being sent to the caller as an array.
        /// </summary>
        static void FireBlockMultiCallback(Action<BlockTypeEnum[]> cb, HashSet<BlockTypeEnum> selectedTypes)
        {
            _tmpMultiBlocks.Clear();

            // Stable order: use _blockOptions order.
            for (int i = 0; i < _blockOptions.Length; i++)
            {
                BlockTypeEnum block = _blockOptions[i];
                if (selectedTypes.Contains(block))
                    _tmpMultiBlocks.Add(block);
            }

            cb(_tmpMultiBlocks.ToArray());
        }

        /// <summary>
        /// Builds the combo preview text for the current multi-block selection.
        /// Shows "(None)" when empty, otherwise shows up to the first two selected
        /// block names and a +N suffix when additional blocks are selected.
        /// </summary>
        static string BuildBlockMultiPreview(HashSet<BlockTypeEnum> selectedTypes)
        {
            if (selectedTypes == null || selectedTypes.Count == 0)
                return "(None)";

            _tmpMultiBlocks.Clear();

            for (int i = 0; i < _blockOptions.Length && _tmpMultiBlocks.Count < 2; i++)
            {
                BlockTypeEnum block = _blockOptions[i];
                if (selectedTypes.Contains(block))
                    _tmpMultiBlocks.Add(block);
            }

            string a = (_tmpMultiBlocks.Count > 0) ? BlockName(_tmpMultiBlocks[0]) : "";
            string b = (_tmpMultiBlocks.Count > 1) ? BlockName(_tmpMultiBlocks[1]) : "";

            int shown = _tmpMultiBlocks.Count;
            int extra = selectedTypes.Count - shown;

            if (shown == 1) return extra > 0 ? $"{a} +{extra}" : a;
            if (shown == 2) return extra > 0 ? $"{a}, {b} +{extra}" : $"{a}, {b}";

            return $"{selectedTypes.Count} selected";
        }

        /// <summary>
        /// Calculates a popup width that can fit the widest block entry (checkbox + color dot + label)
        /// including window padding and scrollbar space, so the multi-block dropdown does not truncate
        /// long names or feel cramped.
        /// </summary>
        static float CalcMultiBlockPopupWidth()
        {
            // Longest visible label width.
            float maxLabelW = 0f;

            // Include the special entries too.
            maxLabelW = Math.Max(maxLabelW, ImGui.CalcTextSize("(None)").X);
            maxLabelW = Math.Max(maxLabelW, ImGui.CalcTextSize("(Select All Blocks)").X);

            for (int i = 0; i < _blockOptions.Length; i++)
                maxLabelW = Math.Max(maxLabelW, ImGui.CalcTextSize(BlockName(_blockOptions[i])).X);

            var style = ImGui.GetStyle();

            float checkboxW = ImGui.GetFrameHeight();   // Checkbox roughly square.
            float dotW = 14f;                           // Dot hitbox.
            float gaps = style.ItemInnerSpacing.X * 3f; // chk->dot, dot->label, etc.

            // Popup window padding + potential scrollbar.
            float padW    = style.WindowPadding.X * 2f;
            float scrollW = style.ScrollbarSize;

            // Total width needed for one row.
            float needed = checkboxW + dotW + gaps + maxLabelW + padW + scrollW;

            // Small safety buffer so text never kisses the edge.
            needed += 15f;

            return needed;
        }
        #endregion

        #region Checkbox Based: Multi-Target Combobox

        static bool CB_PlayerMultiCombo(
            string                           id,
            ref bool                         allPlayers,
            Action<PlayerTargetMode, byte[]> onChanged,
            HashSet<byte>                    selectedIds,
            bool                             enabled = true,
            float                            width   = -1f)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (selectedIds == null) return false;

            // Prune ids that no longer exist (player left).
            PruneMissingPlayers(selectedIds);

            if (width > 0f)
                ImGui.SetNextItemWidth(width);

            string preview = BuildMultiPreview(selectedIds, allPlayers);

            bool changed = false;

            if (!enabled) ImGui.BeginDisabled(true);
            if (ImGui.BeginCombo(id, preview))
            {
                // (None) => Clear everything.
                if (ImGui.Selectable("(None)", selectedIds.Count == 0 && !allPlayers))
                {
                    selectedIds.Clear();
                    allPlayers = false;
                    changed    = true;
                }

                // Optional helpers.
                if (ImGui.Selectable("(Select All Players)", false))
                {
                    allPlayers = false;
                    selectedIds.Clear();
                    for (int i = 0; i < _players.Count; i++)
                    {
                        var g = _players[i];
                        if (g == null) continue;
                        selectedIds.Add(g.Id);
                    }
                    changed = true;
                }

                if (ImGui.Selectable("(All Players)", allPlayers))
                {
                    allPlayers = !allPlayers;
                    if (allPlayers) selectedIds.Clear(); // Clean semantic: AllPlayers ignores per-player list.
                    changed = true;
                }

                ImGui.Separator();

                // Scrollable list inside combo.
                float listH = Math.Max(180f, Math.Min(260f, _players.Count * 22f));
                if (ImGui.BeginChild("##multi_players", new Vector2(0, listH)))
                {
                    // If AllPlayers is active, disable per-player toggles.
                    if (allPlayers) ImGui.BeginDisabled(true);

                    for (int i = 0; i < _players.Count; i++)
                    {
                        var g = _players[i];
                        if (g == null) continue;

                        byte idNet = g.Id;
                        bool isSel = selectedIds.Contains(idNet);

                        // Checkbox.
                        bool cb = isSel;
                        if (ImGui.Checkbox($"##mp_chk_{idNet}", ref cb))
                        {
                            if (cb) selectedIds.Add(idNet);
                            else selectedIds.Remove(idNet);
                            changed = true;
                        }

                        ImGui.SameLine();

                        // Clicking the name toggles too (and keeps popup open).
                        string label = $"{DisplayName(g)}##mp_{idNet}";
                        if (ImGui.Selectable(label, cb, ImGuiSelectableFlags.NoAutoClosePopups))
                        {
                            cb = !cb;
                            if (cb) selectedIds.Add(idNet);
                            else selectedIds.Remove(idNet);
                            changed = true;
                        }
                    }

                    if (allPlayers) ImGui.EndDisabled();
                }
                ImGui.EndChild();

                ImGui.EndCombo();
            }
            if (!enabled) ImGui.EndDisabled();

            // Fire callback once per edit.
            if (changed && onChanged != null)
                FireMultiCallback(onChanged, selectedIds, allPlayers);

            return changed;
        }

        static void FireMultiCallback(Action<PlayerTargetMode, byte[]> cb, HashSet<byte> selectedIds, bool allPlayers)
        {
            if (allPlayers)
            {
                cb(PlayerTargetMode.AllPlayers, Array.Empty<byte>());
                return;
            }

            if (selectedIds == null || selectedIds.Count == 0)
            {
                cb(PlayerTargetMode.None, Array.Empty<byte>());
                return;
            }

            // Stable ordering: iterate _players order, add if selected.
            _tmpMultiIds.Clear();
            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g == null) continue;
                if (selectedIds.Contains(g.Id))
                    _tmpMultiIds.Add(g.Id);
            }

            cb(PlayerTargetMode.Player, _tmpMultiIds.ToArray());
        }

        static void PruneMissingPlayers(HashSet<byte> selectedIds)
        {
            if (selectedIds.Count == 0) return;

            _tmpRemoveIds.Clear();

            foreach (var id in selectedIds)
            {
                bool exists = false;
                for (int i = 0; i < _players.Count; i++)
                {
                    var g = _players[i];
                    if (g != null && g.Id == id) { exists = true; break; }
                }
                if (!exists) _tmpRemoveIds.Add(id);
            }

            for (int i = 0; i < _tmpRemoveIds.Count; i++)
                selectedIds.Remove(_tmpRemoveIds[i]);
        }

        static string BuildMultiPreview(HashSet<byte> selectedIds, bool allPlayers)
        {
            if (allPlayers) return "(All Players)";
            if (selectedIds == null || selectedIds.Count == 0) return "(None)";

            // Show up to 2 names then "+N".
            int shown = 0;
            _tmpMultiIds.Clear();

            // Use _players order for names.
            for (int i = 0; i < _players.Count && shown < 2; i++)
            {
                var g = _players[i];
                if (g == null) continue;
                if (!selectedIds.Contains(g.Id)) continue;

                _tmpMultiIds.Add(g.Id);
                shown++;
            }

            string a = (_tmpMultiIds.Count > 0) ? DisplayName(FindPlayerById(_tmpMultiIds[0])) : "";
            string b = (_tmpMultiIds.Count > 1) ? DisplayName(FindPlayerById(_tmpMultiIds[1])) : "";

            int extra = selectedIds.Count - shown;
            if (shown == 1) return extra > 0 ? $"{a} +{extra}" : a;
            if (shown == 2) return extra > 0 ? $"{a}, {b} +{extra}" : $"{a}, {b}";
            return $"{selectedIds.Count} selected";
        }

        static NetworkGamer FindPlayerById(byte id)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var g = _players[i];
                if (g != null && g.Id == id) return g;
            }
            return null;
        }
        #endregion

        #endregion

        #region Button With Callback

        /// <summary>
        /// Simple, reusable button helper.
        /// • Width < 0   => stretch to fill remaining space in the current column/region.
        /// • Height == 0 => default frame height.
        /// • Enabled == false => renders disabled and ignores clicks.
        /// • idSuffix    => optional hidden ImGui ID suffix so multiple buttons can share the same visible label.
        /// Returns true if the button was clicked.
        /// </summary>
        static bool CB_Button(
            string idSuffix,
            string label,
            Action onClick = null,
            float  width   = -1f,
            float  height  = 0f,
            bool   enabled = true,
            string tooltip = null)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (width < 0f)
                width = ImGui.GetContentRegionAvail().X; // Fill to the right edge.

            // Visible label stays the same, but ImGui gets a unique internal ID.
            string imguiLabel = string.IsNullOrEmpty(idSuffix)
                ? label
                : $"{label}##{idSuffix}";

            if (!enabled) ImGui.BeginDisabled();
            bool clicked = ImGui.Button(imguiLabel, new Vector2(width, height));
            if (!enabled) ImGui.EndDisabled();

            // Optional tooltip (shows after a short hover delay).
            if (!string.IsNullOrEmpty(tooltip) &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            {
                ImGui.SetTooltip(tooltip);
            }

            if (clicked)
            {
                // Prefer per-button callback; fall back to global hook if provided.
                onClick?.Invoke();
            }

            return clicked;
        }
        #endregion

        #region Radiobutton With Callback

        // RadioButton enums.
        // Used to scope who an action applies to.
        public enum PlayerSelectScope { Personal, Everyone            }
        public enum VanishSelectScope { InPlace, Spawn, Distant, Zero }

        /// <summary>
        /// Generic radio button helper for enum-like values:
        /// • Works with any enum (or value type) by comparing to 'thisValue'.
        /// • Fires the callback only when the selection changes.
        /// • Accepts an 'idSuffix' to avoid duplicate IDs when reusing labels.
        /// </summary>
        /// <typeparam name="T">Enum (or value-type) used as the selection.</typeparam>
        /// <param name="idSuffix">Optional unique tail for the ImGui ID.</param>
        /// <param name="label">Visible text for this radio option.</param>
        /// <param name="current">Ref current selection value.</param>
        /// <param name="cb">Callback invoked with the new value when selection changes.</param>
        /// <param name="thisValue">The value represented by this specific radio button.</param>
        /// <param name="enabled">When false: renders grayed and blocks interaction.</param>
        /// <returns>true if this radio button was clicked and changed the selection.</returns>
        static bool CB_RadioButton<T>(
            string idSuffix,
            string label,
            ref T current,
            Action<T> cb,
            T thisValue,
            bool enabled = true) where T : struct, Enum
        {
            if (ShouldHideControl(enabled))
                return false;

            string id = idSuffix == null ? label : $"{label}##{idSuffix}";

            bool selected = EqualityComparer<T>.Default.Equals(current, thisValue);

            if (!enabled) ImGui.BeginDisabled();
            bool clicked = ImGui.RadioButton(id, selected);
            if (!enabled) ImGui.EndDisabled();

            if (clicked && !selected)
            {
                current = thisValue;
                cb?.Invoke(current);
                return true;
            }
            return false;
        }
        #endregion

        #region Multiline Textbox With Callback

        /// <summary>
        /// Summary:
        /// Reusable multiline textbox helper for ImGui.
        /// - Uses InputTextMultiline (plain text; not true WinForms RichTextBox formatting).
        /// - Height is expressed in checkbox-row units so it fits this UI naturally.
        /// - Fires the callback only when the text actually changes.
        ///
        /// Notes:
        /// - Good for chat text, notes, scripts, names, templates, etc.
        /// - Placeholder text is drawn when the box is empty.
        /// </summary>
        static bool CB_TextArea(
            string              id,
            ref string          value,
            Action<string>      onChanged = null,
            int                 maxChars  = 65535,
            int                 rowsTall  = 4,
            bool                enabled   = true,
            string              hint      = null,
            ImGuiInputTextFlags flags     = ImGuiInputTextFlags.AllowTabInput |
                                                ImGuiInputTextFlags.NoHorizontalScroll)
        {
            if (ShouldHideControl(enabled))
                return false;

            if (value == null)
                value = string.Empty;

            // Approximate "N checkbox rows tall".
            float rowH   = ImGui.GetFrameHeightWithSpacing();
            float height = Math.Max(ImGui.GetFrameHeight(),
                                    (rowsTall * rowH) - ImGui.GetStyle().ItemSpacing.Y);

            string before = value;

            if (!enabled) ImGui.BeginDisabled();

            bool changed = ImGui.InputTextMultiline(
                id,
                ref value,
                (uint)maxChars,
                new Vector2(-1f, height),
                flags);

            if (!enabled) ImGui.EndDisabled();

            // Placeholder / hint.
            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(hint))
            {
                var min = ImGui.GetItemRectMin();
                var pad = ImGui.GetStyle().FramePadding;

                ImGui.GetWindowDrawList().AddText(
                    new Vector2(min.X + pad.X, min.Y + pad.Y),
                    ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    hint
                );
            }

            if (changed && !string.Equals(before, value, StringComparison.Ordinal))
                onChanged?.Invoke(value);

            return changed;
        }
        #endregion

        #region UI Text Helpers

        /// <summary>
        /// Draws disabled-styled text and optionally keeps the next item on the same line.
        ///
        /// Purpose:
        /// - Wraps small inline text elements (such as "X", hints, labels, or markers)
        ///   in the same shared UI-helper style as the other CB_* methods.
        /// - Respects the shared "hide disabled UI" rule through ShouldHideControl(...).
        ///
        /// Notes:
        /// - The enabled parameter is used only to decide whether this text should be shown
        ///   when tied to a parent feature/state.
        /// - The text itself is always drawn with ImGui.TextDisabled(...).
        /// - Set sameLineAfter to true when the next control should remain on the same line.
        /// </summary>
        private static void CB_TextDisabled(
            string text,
            bool   enabled             = true,
            bool   sameLineAfter       = false,
            bool   alignToFramePadding = true)
        {
            if (ShouldHideControl(enabled))
                return;

            if (alignToFramePadding)
                ImGui.AlignTextToFramePadding();

            ImGui.TextDisabled(text);

            if (sameLineAfter)
                ImGui.SameLine();
        }
        #endregion

        #endregion
    }
}
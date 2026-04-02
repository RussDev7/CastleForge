/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace Restore360Water
{
    /// <summary>
    /// Restores the dormant 360-style surface water path using biome-aware water bands.
    ///
    /// Summary:
    /// - Re-enables water-world state on terrain initialization.
    /// - Recreates the missing water plane after content is loaded.
    /// - Reattaches the water plane and keeps reflection compatibility optional/stability-gated.
    /// - Resolves active water bands from the classic CMZ ring-biome distance math.
    ///
    /// Scope:
    /// - Re-enables water-world state on terrain initialization.
    /// - Recreates the missing water plane after content is loaded.
    /// - Reattaches the water plane and keeps reflection compatibility optional/stability-gated.
    /// - Resolves active water bands from the classic CMZ ring-biome distance math.
    ///
    /// Non-goals:
    /// - This does not add true flowing voxel water.
    /// - This does not currently reintroduce the surrogate static Murky Water block path.
    /// </summary>
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class Restore360Water : ModBase
    {
        #region Fields / Command Registration

        /// <summary>
        /// Chat command dispatcher used to route raw chat input into this mod's command handlers.
        /// </summary>
        private readonly CommandDispatcher _dispatcher;

        #endregion

        #region Construction / Lifetime Wiring

        /// <summary>
        /// Creates the mod instance, initializes embedded dependency resolution,
        /// prepares the command dispatcher, and hooks game shutdown when available.
        /// </summary>
        public Restore360Water() : base("Restore360Water", new Version("0.3.2"))
        {
            EmbeddedResolver.Init();
            _dispatcher = new CommandDispatcher(this);

            var game = CastleMinerZGame.Instance;
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        #endregion

        #region Mod Startup / Shutdown / Tick

        /// <summary>
        /// Starts the mod and performs one-time initialization.
        ///
        /// Flow:
        /// - Verifies the game instance exists.
        /// - Extracts embedded mod assets to the !Mods folder.
        /// - Initializes sound runtime support.
        /// - Applies Harmony / runtime patches.
        /// - Loads and applies configuration.
        /// - Registers chat command dispatch and help text.
        /// </summary>
        public override void Start()
        {
            var game = CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            var ns    = typeof(Restore360Water).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            R360WSoundRuntime.Initialize();

            GamePatches.ApplyAllPatches();
            R360WConfig.LoadApply();

            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded.");
        }

        /// <summary>
        /// Shuts down runtime systems owned by this mod.
        ///
        /// Notes:
        /// - Disposes sound runtime resources.
        /// - Disables applied hooks / patches where supported.
        /// - Swallows nested cleanup failures so shutdown remains best-effort.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                try { R360WSoundRuntime.Dispose(); } catch { }
                try { GamePatches.DisableAll(); } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}."); }
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            {
                Log($"Error shutting down mod: {ex}.");
            }
        }

        /// <summary>
        /// Per-frame mod tick.
        ///
        /// Notes:
        /// - Currently forwards update timing into the water sound runtime.
        /// - This method intentionally does not depend on inputManager for its current behavior.
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            R360WSoundRuntime.Update(gameTime);
        }
        #endregion

        #region Chat Command Metadata

        /// <summary>
        /// Help entries registered with the global help registry for this mod.
        /// </summary>
        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            ("r360water reload", "Reload Restore360Water.Config.ini and reapply live water state."),
            ("r360water status", "Show the current Restore360Water runtime status."),
            ("r360water on",     "Temporarily enable restored world water for the current session."),
            ("r360water off",    "Temporarily disable restored world water for the current session.")
        };
        #endregion

        #region Chat Commands

        /// <summary>
        /// Primary chat command entry point for Restore360Water.
        ///
        /// Supported subcommands:
        /// - reload : reload config and reapply live state
        /// - status : print current runtime biome / water status
        /// - on     : enable the restored water system for the current session
        /// - off    : disable the restored water system for the current session
        ///
        /// Notes:
        /// - This is a session/runtime control surface.
        /// - Persistent config values still come from the mod config file.
        /// </summary>
        [Command("/r360water")]
        [Command("/r360")]
        private static void ExecuteRestore360Water(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    SendFeedback("Usage: /r360water [reload|status|on|off]");
                    return;
                }

                switch (args[0].ToLowerInvariant())
                {
                    case "reload":
                        R360WConfig.LoadApply();
                        GamePatches.ApplyLiveState();
                        SendFeedback("Restore360Water config reloaded and reapplied.");
                        return;

                    case "status":
                    {
                        GamePatches.UpdateEffectiveWaterState();
                        var terrain = GamePatches.TryGetTerrain();
                        string live = (terrain != null && terrain.IsWaterWorld) ? "true" : "false";
                        float level = (terrain != null) ? terrain.WaterLevel : R360W_Settings.CurrentWaterMaxY;
                        SendFeedback(
                            $"Restore360Water: Enabled={R360W_Settings.Enabled}, " +
                            $"Biome={R360W_Settings.CurrentBiomeName}, " +
                            $"BiomeWater={R360W_Settings.CurrentBiomeEnabled}, " +
                            $"MinY={R360W_Settings.CurrentWaterMinY:0.##}, " +
                            $"MaxY={R360W_Settings.CurrentWaterMaxY:0.##}, " +
                            $"WaterLevel={level:0.##}, " +
                            $"LiveIsWaterWorld={live}, " +
                            $"AttachPlane={R360W_Settings.AttachWaterPlane}, " +
                            $"Reflection={R360W_Settings.EnableReflection}, " +
                            $"VanillaUnderwater={R360W_Settings.UseVanillaUnderwaterEngine}");
                        return;
                    }

                    case "on":
                        R360W_Settings.Enabled = true;
                        GamePatches.ApplyLiveState();
                        SendFeedback("Restore360Water enabled for this session.");
                        return;

                    case "off":
                        R360W_Settings.Enabled = false;
                        GamePatches.ApplyLiveState();
                        SendFeedback("Restore360Water disabled for this session.");
                        return;

                    default:
                        SendFeedback("Usage: /r360water [reload|status|on|off]");
                        return;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}");
            }
        }
        #endregion
    }
}
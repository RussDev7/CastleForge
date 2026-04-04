/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Audio;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace Restore360Water
{
    /// <summary>
    /// Loads and plays custom Restore360Water water sound effects from:
    /// !Mods\Restore360Water\Sounds
    ///
    /// Behavior:
    /// - Plays a one-shot enter splash when the local player enters water.
    /// - Plays a one-shot exit splash when the local player leaves water.
    /// - Plays a repeated shallow-water wade sound while moving through shallow water.
    /// - Plays a looped underwater swim movement sound while swimming.
    ///
    /// Notes:
    /// - Uses SoundEffect.FromStream, so no XACT cue registration is required.
    /// - This is local-player driven and does not currently spatialize remote-player splashes.
    /// - Sound enablement and file names are driven by R360W_Settings.
    /// </summary>
    internal static class R360WSoundRuntime
    {
        #region Sound Cache / Runtime State

        /// <summary>
        /// Loaded one-shot sound assets keyed by file name.
        /// </summary>
        private static readonly Dictionary<string, SoundEffect> _sounds =
            new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Active transient sound instances for one-shot playback.
        /// Instances are cleaned up after they stop.
        /// </summary>
        private static readonly List<SoundEffectInstance> _activeInstances =
            new List<SoundEffectInstance>();

        /// <summary>
        /// Dedicated loop instance for underwater swim motion.
        /// </summary>
        private static SoundEffectInstance _swimLoopInstance;

        /// <summary>
        /// Tracks whether the swim loop is currently considered active.
        /// </summary>
        private static bool _swimLoopPlaying;

        /// <summary>
        /// Tracks whether the sound runtime has been initialized.
        /// </summary>
        private static bool _initialized;

        /// <summary>
        /// Cached previous-frame water state for enter / exit edge detection.
        /// </summary>
        private static bool _lastInWater;

        /// <summary>
        /// Reserved cached underwater state for future transition-specific behavior.
        /// </summary>
        private static bool _lastUnderwater;

        /// <summary>
        /// Reuse timer for repeated shallow-water wade playback.
        /// </summary>
        private static float _wadeTimer;

        #endregion

        #region Timing / Paths

        /// <summary>
        /// Repetition interval for shallow-water walking.
        /// </summary>
        private const float WadeIntervalWalk = 0.48f;

        /// <summary>
        /// Reserved run cadence constant for future faster wade timing.
        /// </summary>
        private const float WadeIntervalRun = 0.36f;

        /// <summary>
        /// Root folder for mod-local custom WAV files.
        /// </summary>
        private static string SoundsFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "Restore360Water", "Sounds");

        #endregion

        #region Lifecycle

        /// <summary>
        /// Creates the sounds folder if needed and loads configured water sounds.
        /// Safe to call repeatedly.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            try
            {
                Directory.CreateDirectory(SoundsFolder);

                // Expected defaults:
                // !Mods\Restore360Water\Sounds\water_enter.wav
                // !Mods\Restore360Water\Sounds\water_exit.wav
                // !Mods\Restore360Water\Sounds\water_wade.wav
                // !Mods\Restore360Water\Sounds\water_swim.wav

                if (R360W_Settings.EnableWaterEnter) Load(R360W_Settings.WaterEnterFile);
                if (R360W_Settings.EnableWaterExit)  Load(R360W_Settings.WaterExitFile);
                if (R360W_Settings.EnableWaterWade)  Load(R360W_Settings.WaterWadeFile);
                if (R360W_Settings.EnableWaterSwim)  Load(R360W_Settings.WaterSwimFile);

                _initialized = true;
                Log("Sound runtime initialized.");
            }
            catch (Exception ex)
            {
                Log($"Sound init failed: {ex.Message}.");
            }
        }

        /// <summary>
        /// Stops active playback, disposes cached sound instances, and resets runtime state.
        /// </summary>
        public static void Dispose()
        {
            try
            {
                StopSwimLoop();

                try
                {
                    _swimLoopInstance?.Dispose();
                    _swimLoopInstance = null;
                }
                catch { }

                foreach (var inst in _activeInstances)
                {
                    try
                    {
                        inst.Stop();
                        inst.Dispose();
                    }
                    catch { }
                }

                _activeInstances.Clear();

                foreach (var pair in _sounds)
                {
                    try { pair.Value.Dispose(); } catch { }
                }

                _sounds.Clear();

                _swimLoopPlaying = false;
                _initialized     = false;
                _lastInWater     = false;
                _lastUnderwater  = false;
                _wadeTimer = 0f;
            }
            catch (Exception ex)
            {
                Log($"Sound dispose failed: {ex.Message}.");
            }
        }
        #endregion

        #region Runtime Update

        /// <summary>
        /// Evaluates the local player's water state and plays the configured water sounds.
        /// </summary>
        public static void Update(GameTime gameTime)
        {
            if (!_initialized || !R360W_Settings.Enabled || !R360W_Settings.EnableCustomSounds)
                return;

            CleanupFinishedInstances();

            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            if (player == null || player.Dead)
                return;

            bool inWater;
            bool underwater;

            try
            {
                // Use biome-aware helpers so audio follows Restore360Water band logic.
                inWater = GamePatches.GetBandLimitedInWater(player);
                underwater = GamePatches.GetBandLimitedUnderwater(player);
            }
            catch
            {
                return;
            }

            bool moving =
                player.PlayerPhysics != null &&
                player.PlayerPhysics.WorldVelocity.LengthSquared() > 0.20f * 0.20f;

            bool shallowWading =
                inWater     &&
                !underwater &&
                moving      &&
                player.InContact;

            // Enter water.
            if (!_lastInWater && inWater && R360W_Settings.EnableWaterEnter)
                Play3D(R360W_Settings.WaterEnterFile, player);

            // Exit water.
            if (_lastInWater && !inWater)
            {
                StopSwimLoop();

                if (R360W_Settings.EnableWaterExit)
                    Play3D(R360W_Settings.WaterExitFile, player);
            }

            if (shallowWading)
            {
                StopSwimLoop();

                if (R360W_Settings.EnableWaterWade)
                {
                    _wadeTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                    if (_wadeTimer <= 0f)
                    {
                        Play3D(R360W_Settings.WaterWadeFile, player);
                        _wadeTimer = WadeIntervalWalk;
                    }
                }
                else
                {
                    _wadeTimer = 0f;
                }
            }
            else if (underwater && moving && R360W_Settings.EnableWaterSwim)
            {
                StartSwimLoop(player);
                UpdateSwimLoop3D(player);
            }
            else
            {
                StopSwimLoop();

                // Only clear the wade cadence once we are truly out of water.
                if (!inWater)
                    _wadeTimer = 0f;
            }

            _lastInWater = inWater;
            _lastUnderwater = underwater;
        }
        #endregion

        #region Sound Loading / One-Shot Playback

        /// <summary>
        /// Loads a WAV file from the Restore360Water Sounds folder.
        /// If the loaded file matches the configured swim loop file, a dedicated looped instance is created.
        /// </summary>
        private static void Load(string fileName)
        {
            string path = Path.Combine(SoundsFolder, fileName);
            if (!File.Exists(path))
                return;

            using (var fs = File.OpenRead(path))
            {
                _sounds[fileName] = SoundEffect.FromStream(fs);
            }

            if (fileName.Equals(R360W_Settings.WaterSwimFile, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _swimLoopInstance?.Dispose();
                    _swimLoopInstance          = _sounds[fileName].CreateInstance();
                    _swimLoopInstance.IsLooped = true;
                    _swimLoopInstance.Volume   = R360W_Settings.SoundVolume;
                }
                catch (Exception ex)
                {
                    Log($"Failed creating swim loop instance: {ex.Message}.");
                }
            }

            Log($"Loaded sound: {fileName}.");
        }

        /// <summary>
        /// Plays a one-shot 3D sound from the loaded sound cache at the local player's position.
        /// </summary>
        private static void Play3D(string fileName, Player player)
        {
            if (player == null)
                return;

            if (!_sounds.TryGetValue(fileName, out SoundEffect sfx) || sfx == null)
                return;

            try
            {
                var game = CastleMinerZGame.Instance;
                if (game == null)
                    return;

                var inst = sfx.CreateInstance();
                inst.Volume = R360W_Settings.SoundVolume;

                var emitter      = player.SoundEmitter;
                emitter.Position = player.WorldPosition;
                emitter.Forward  = Vector3.Forward;
                emitter.Up       = Vector3.Up;

                inst.Apply3D(game.Listener, emitter);
                inst.Play();

                _activeInstances.Add(inst);
            }
            catch (Exception ex)
            {
                Log($"Failed to play {fileName}: {ex.Message}.");
            }
        }

        /// <summary>
        /// Disposes stopped one-shot instances so the active instance list stays clean.
        /// </summary>
        private static void CleanupFinishedInstances()
        {
            for (int i = _activeInstances.Count - 1; i >= 0; i--)
            {
                var inst = _activeInstances[i];
                if (inst == null)
                {
                    _activeInstances.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (inst.State == SoundState.Stopped)
                    {
                        inst.Dispose();
                        _activeInstances.RemoveAt(i);
                    }
                }
                catch
                {
                    try { inst.Dispose(); } catch { }
                    _activeInstances.RemoveAt(i);
                }
            }
        }
        #endregion

        #region Swim Loop Helpers

        /// <summary>
        /// Starts the configured swim loop and applies the player's 3D audio position.
        /// </summary>
        private static void StartSwimLoop(Player player)
        {
            if (_swimLoopInstance == null || player == null)
                return;

            try
            {
                var game = CastleMinerZGame.Instance;
                if (game == null)
                    return;

                var emitter      = player.SoundEmitter;
                emitter.Position = player.WorldPosition;
                emitter.Forward  = Vector3.Forward;
                emitter.Up       = Vector3.Up;

                _swimLoopInstance.Volume = R360W_Settings.SoundVolume;
                _swimLoopInstance.Apply3D(game.Listener, emitter);

                if (_swimLoopInstance.State != SoundState.Playing)
                    _swimLoopInstance.Play();

                _swimLoopPlaying = true;
            }
            catch (Exception ex)
            {
                Log($"Failed starting swim loop: {ex.Message}.");
            }
        }

        /// <summary>
        /// Refreshes the swim loop's 3D emitter position while the player moves underwater.
        /// </summary>
        private static void UpdateSwimLoop3D(Player player)
        {
            if (!_swimLoopPlaying || _swimLoopInstance == null || player == null)
                return;

            try
            {
                var game = CastleMinerZGame.Instance;
                if (game == null)
                    return;

                var emitter      = player.SoundEmitter;
                emitter.Position = player.WorldPosition;
                emitter.Forward  = Vector3.Forward;
                emitter.Up       = Vector3.Up;

                _swimLoopInstance.Volume = R360W_Settings.SoundVolume;
                _swimLoopInstance.Apply3D(game.Listener, emitter);
            }
            catch { }
        }

        /// <summary>
        /// Stops the swim loop if it is currently playing.
        /// </summary>
        private static void StopSwimLoop()
        {
            if (_swimLoopInstance == null)
                return;

            try
            {
                if (_swimLoopInstance.State != SoundState.Stopped)
                    _swimLoopInstance.Stop();

                _swimLoopPlaying = false;
            }
            catch (Exception ex)
            {
                Log($"Failed stopping swim loop: {ex.Message}.");
            }
        }
        #endregion
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using DNA.CastleMinerZ;
using System;

using static ModLoader.LogSystem;

namespace MoreAchievements
{
    /// <summary>
    /// Central helper for achievement-related artwork.
    ///
    /// Responsibilities:
    /// • Lazily load the shared frame texture from CMZ Content ("frame_0").
    /// • Expose the loaded texture via <see cref="Frame"/> for any HUD/overlay code.
    /// • Fail soft (log + null) if the asset is missing or Content loading fails.
    /// </summary>
    internal static class AchievementArt
    {

        private static bool _triedFrame; // Guard so we only attempt to load once per process.
        private static Texture2D _frame; // Cached frame texture loaded from Content\frame_0.xnb (can remain null on failure).

        /// <summary>
        /// Achievement frame texture from Content\frame_0.xnb.
        /// May be null if <see cref="EnsureLoaded"/> failed or hasn't been called yet.
        /// </summary>
        public static Texture2D Frame => _frame;

        /// <summary>
        /// Ensure <see cref="Frame"/> is loaded from the CMZ Content manager.
        ///
        /// Notes:
        /// • No-op after the first call (whether load succeeded or failed).
        /// • Logs a success message when the texture is found.
        /// • Logs a short error message on failure and leaves <see cref="Frame"/> as null.
        /// </summary>
        public static void EnsureLoaded()
        {
            // Already tried (success or failure) -> skip further work.
            if (_triedFrame)
                return;

            _triedFrame = true;

            try
            {
                var gm = CastleMinerZGame.Instance;
                var cm = gm?.Content;
                if (cm != null)
                {
                    _frame = cm.Load<Texture2D>("frame_0");
                    Log("Loaded achievement frame texture 'frame_0'.");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load frame_0: {ex.Message}.");
                _frame = null;
            }
        }
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ;
using ImGuiNET;

namespace CastleWallsMk2
{
    /// <summary>
    /// Centralizes overlay ⇄ game input handoff.
    /// Call OverlayController.Update() once per frame (e.g., via a Harmony Prefix on GameScreen.Update).
    ///
    /// Behavior:
    /// - When the ImGui overlay becomes visible, we:
    ///   • Freeze gameplay input (HUD.AcceptInput = false)
    ///   • Release the game's mouse capture so the OS/ImGui can use the cursor
    ///   • Show the cursor (both game-level and ImGui-level)
    ///
    /// - When the overlay is hidden again, we restore the HUD's prior input flags
    ///   so gameplay resumes exactly as it was.
    ///
    /// Implementation notes:
    /// - The logic is edge-triggered: runs only when visibility flips (hidden -> shown or shown -> hidden).
    /// - We save/restore the HUD flags only once per "show" cycle, guarded by _haveSaved.
    /// - Clearing controller mapping prevents "stuck" edge-triggered actions when grabbing/releasing focus.
    /// </summary>
    internal static class OverlayController
    {
        static bool _lastVisible;                                       // Last observed overlay visibility; used for edge detection so we only run on state changes.
        static bool _savedHudAccept, _savedHudCapture, _savedHudCursor; // Snapshot of HUD input flags taken the moment the overlay is shown.
        static bool _haveSaved;                                         // Whether we currently hold a valid snapshot to restore.

        /// <summary>
        /// Must be called once per frame.
        /// Detects overlay visibility changes and toggles game/overlay input accordingly.
        /// </summary>
        public static void Update()
        {
            // Single read of the overlay visible flag; Compare with last frame for edge-triggered behavior.
            bool visible = ImGuiXnaRenderer.Visible;
            if (visible == _lastVisible)
                return; // No change this frame; Nothing to do.

            // Record new state so we only react once per transition.
            _lastVisible = visible;

            // Grab game and HUD; If either is unavailable, bail safely.
            var game = CastleMinerZGame.Instance;
            var hud  = game?.GameScreen?.HUD;
            if (hud == null)
                return;

            var io = ImGui.GetIO();

            if (visible)
            {
                // First time we become visible in this cycle: Save HUD input flags for perfect restoration later.
                if (!_haveSaved)
                {
                    _savedHudAccept  = hud.AcceptInput;
                    _savedHudCapture = hud.CaptureMouse;
                    _savedHudCursor  = hud.ShowMouseCursor;
                    _haveSaved       = true;
                }

                // ---- Hand input to the overlay ----
                hud.AcceptInput     = false; // Stop the HUD from processing gameplay input.
                hud.CaptureMouse    = false; // Let the OS/ImGui own the mouse (no game recentering/locking).
                hud.ShowMouseCursor = true;  // Show a cursor for UI interaction at the game level...
                io.MouseDrawCursor  = true;  // ...and also instruct ImGui to draw its own cursor (useful if the game hides the OS cursor).

                // Clear any latched controller/keyboard actions so nothing "bleeds" into the overlay frame.
                game._controllerMapping.ClearAllControls();
            }
            else
            {
                // ---- Return input to the game ----
                if (_haveSaved)
                {
                    // Restore the exact HUD input flags we had before showing the overlay.
                    hud.AcceptInput     = _savedHudAccept;
                    hud.CaptureMouse    = _savedHudCapture;
                    hud.ShowMouseCursor = _savedHudCursor;

                    // Mark the snapshot as consumed.
                    _haveSaved = false;
                }

                // ImGui no longer draws its cursor once we hide the overlay.
                io.MouseDrawCursor = false;
            }
        }
    }
}
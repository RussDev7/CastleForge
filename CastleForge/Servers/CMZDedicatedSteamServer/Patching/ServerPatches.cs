/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Reflection;
using HarmonyLib;

namespace CMZDedicatedSteamServer
{
    /// <summary>
    /// Central runtime patch bootstrap for the dedicated server host.
    ///
    /// Purpose:
    /// - Creates the Harmony instance used by this project.
    /// - Applies all Harmony patches found in the executing assembly.
    ///
    /// Notes:
    /// - Patch application is guarded so it only happens once per process.
    /// - This class does not remove or re-apply patches after initialization.
    /// - The Harmony ID should remain stable so patch ownership is consistent.
    /// </summary>
    internal static class ServerPatches
    {
        #region Fields

        /// <summary>
        /// Cached Harmony instance for this host process.
        ///
        /// Notes:
        /// - Null means patches have not been applied yet.
        /// - Non-null means patch discovery/application has already run.
        /// </summary>
        private static Harmony _harmony;

        #endregion

        #region Patch Bootstrap

        /// <summary>
        /// Applies all Harmony patches declared in the current assembly.
        ///
        /// Purpose:
        /// - Initializes Harmony once.
        /// - Scans the executing assembly for patch classes/attributes.
        ///
        /// Notes:
        /// - Safe to call multiple times; subsequent calls are ignored.
        /// - Logic is intentionally minimal so startup behavior stays predictable.
        /// </summary>
        public static void ApplyAllPatches()
        {
            if (_harmony != null)
                return;

            _harmony = new Harmony("cmz.serverhost.runtime");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        #endregion
    }
}
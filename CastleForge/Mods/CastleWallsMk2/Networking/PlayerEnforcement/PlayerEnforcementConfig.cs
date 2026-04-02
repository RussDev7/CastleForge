/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Persistent settings store for the Player Enforcement tab.
    ///
    /// Summary:
    /// - Persists whether off-host enforcement should use local Gamertag + private kick behavior.
    /// - Persists the host-side hard-ban deny message used by Steam deny enforcement.
    ///
    /// Notes:
    /// - Settings are stored in a simple INI-style text file under !Mods/CastleWallsMk2.
    /// - The hard-ban deny message is Base64 encoded so multiline text can be stored safely.
    /// - Supports both the current config key and an older legacy key for backward compatibility.
    /// - This class intentionally keeps load/save behavior lightweight and silent on failure.
    /// </summary>
    internal static class PlayerEnforcementConfig
    {
        #region Backing State

        /// <summary>
        /// Synchronizes access to config state during load/save operations.
        /// </summary>
        private static readonly object _sync = new object();

        /// <summary>
        /// Tracks whether settings have already been loaded for this process lifetime.
        /// </summary>
        private static bool _loadedOnce;

        /// <summary>
        /// Tracks whether settings have been modified and need to be saved.
        /// </summary>
        private static bool _dirty;

        #endregion

        #region Stored Settings

        /// <summary>
        /// When true, non-host clients may use local Gamertag matching plus private KickMessage enforcement.
        ///
        /// Notes:
        /// - This is local-only behavior.
        /// - It does not modify the real host's SteamID-based ban state or vanilla BanList.
        /// </summary>
        internal static bool UseGamertagPrivateKickWhenNotHost = false;

        /// <summary>
        /// Host-side deny message shown when a hard-banned Steam peer is denied by Steam enforcement.
        ///
        /// Notes:
        /// - This value is persisted Base64 encoded so multiline text is preserved.
        /// - Falls back to "Host Kicked Us" when missing or invalid.
        /// </summary>
        internal static string HardBanDenyMessage = "Host Kicked Us";

        #endregion

        #region UI Labels / Help Text

        /// <summary>
        /// Checkbox label shown in the Player Enforcement settings UI.
        /// </summary>
        internal const string CheckboxLabel =
            "Use Gamertag + quiet-kick enforcement when not host";

        /// <summary>
        /// Help text shown beneath the non-host enforcement checkbox.
        /// </summary>
        internal const string CheckboxHelp =
            "When enabled, off-host enforcement matches by Gamertag and sends a private KickMessage. " +
            "This is local-only and does not modify the session host's real ban list.";

        /// <summary>
        /// Label shown above the host hard-ban deny message textbox.
        /// </summary>
        internal const string HardBanMessageLabel =
            "Host hard-ban deny message:";

        /// <summary>
        /// Returns a small footer string describing the current mode for the settings UI.
        ///
        /// Summary:
        /// - Host mode describes SteamID-based enforcement.
        /// - Client mode describes local Gamertag/private-kick enforcement.
        /// </summary>
        internal static string ModeFooter(bool isHost)
        {
            return isHost
                ? "Host mode: SteamID enforcement + custom hard-ban deny text."
                : "Client mode: Local Gamertag/private-kick enforcement.";
        }

        #endregion

        #region File Paths

        /// <summary>
        /// Base directory for Player Enforcement config files.
        /// </summary>
        internal static string DirPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "CastleWallsMk2");

        /// <summary>
        /// Full path to the Player Enforcement settings file.
        /// </summary>
        internal static string FilePath =>
            Path.Combine(DirPath, "CastleWallsMk2.PlayerEnforcement.ini");

        #endregion

        #region Encoding Helpers

        /// <summary>
        /// Encodes plain text as Base64.
        ///
        /// Summary:
        /// - Used to safely store multiline text in a single INI value.
        /// </summary>
        private static string Encode(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
        }

        /// <summary>
        /// Decodes a Base64 string back into UTF-8 text.
        ///
        /// Notes:
        /// - Returns an empty string for null/blank input.
        /// - Returns an empty string if decode fails.
        /// </summary>
        private static string Decode(string b64)
        {
            if (string.IsNullOrWhiteSpace(b64))
                return "";

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch
            {
                return "";
            }
        }
        #endregion

        #region Load / Save

        /// <summary>
        /// Loads the config once for the lifetime of the process.
        ///
        /// Summary:
        /// - Applies defaults first.
        /// - Attempts to read the config file if it exists.
        /// - Supports both current and legacy key names.
        ///
        /// Notes:
        /// - Safe to call repeatedly; only the first call performs work unless Reload() is used.
        /// - Failures are intentionally swallowed to avoid disrupting runtime behavior.
        /// </summary>
        internal static void LoadOnce()
        {
            if (_loadedOnce)
                return;

            _loadedOnce = true;

            lock (_sync)
            {
                // Defaults
                UseGamertagPrivateKickWhenNotHost = false;
                HardBanDenyMessage = "Host Kicked Us";

                try
                {
                    if (!File.Exists(FilePath))
                        return;

                    foreach (string raw in File.ReadAllLines(FilePath))
                    {
                        string line = (raw ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                            continue;

                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                            continue;

                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();

                        switch (key)
                        {
                            // Current key name.
                            case "UseGamertagPrivateKickWhenNotHost":
                                bool.TryParse(val, out UseGamertagPrivateKickWhenNotHost);
                                break;

                            // Legacy key name retained for backward compatibility.
                            case "AllowNonHostHardEnforcement":
                                bool.TryParse(val, out UseGamertagPrivateKickWhenNotHost);
                                break;

                            // Base64 encoded multiline deny message.
                            case "HardBanDenyMessage_B64":
                                HardBanDenyMessage = Decode(val);
                                if (string.IsNullOrWhiteSpace(HardBanDenyMessage))
                                    HardBanDenyMessage = "Host Kicked Us";
                                break;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Marks the config as dirty so the next SaveIfDirty() will write it to disk.
        /// </summary>
        internal static void MarkDirty()
        {
            lock (_sync)
                _dirty = true;
        }

        /// <summary>
        /// Forces the config to reload from disk on the next load pass.
        ///
        /// Summary:
        /// - Clears the one-time load guard.
        /// - Immediately reloads current values from file.
        /// </summary>
        internal static void Reload()
        {
            _loadedOnce = false;
            LoadOnce();
        }

        /// <summary>
        /// Saves the config to disk only when changes are pending.
        ///
        /// Summary:
        /// - Creates the config directory if needed.
        /// - Writes the current settings in INI-style format.
        /// - Stores the deny message Base64 encoded.
        ///
        /// Notes:
        /// - Safe to call frequently.
        /// - Failures are intentionally swallowed and leave runtime state unchanged.
        /// </summary>
        internal static void SaveIfDirty()
        {
            LoadOnce();

            lock (_sync)
            {
                if (!_dirty)
                    return;

                try
                {
                    Directory.CreateDirectory(DirPath);

                    using (var sw = new StreamWriter(FilePath, false, Encoding.UTF8))
                    {
                        sw.WriteLine("; CastleWallsMk2 player enforcement settings");
                        sw.WriteLine("; Off-host mode = local Gamertag + private kick only");
                        sw.WriteLine($"UseGamertagPrivateKickWhenNotHost={UseGamertagPrivateKickWhenNotHost}");
                        sw.WriteLine($"HardBanDenyMessage_B64={Encode(HardBanDenyMessage ?? "")}");
                    }

                    _dirty = false;
                }
                catch
                {
                }
            }
        }
        #endregion
    }
}
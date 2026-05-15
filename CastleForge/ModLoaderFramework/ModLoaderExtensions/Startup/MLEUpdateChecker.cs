/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text.RegularExpressions;
using System.Threading;
using System.Net;
using System;

using static ModLoader.LogSystem;

namespace ModLoaderExt
{
    /// <summary>
    /// Performs a lightweight one-time GitHub update check for ModLoaderExtensions.
    /// </summary>
    /// <remarks>
    /// The checker compares the locally installed ModLoaderExtensions version against
    /// the version declared in the main CastleForge GitHub source file.
    ///
    /// This is intentionally non-blocking:
    /// - The check runs once on a ThreadPool worker.
    /// - Failures are logged but ignored.
    /// - Menu rendering and mod startup should never depend on this succeeding.
    ///
    /// The cached result is intended for UI consumers, such as the main-menu update icon.
    /// </remarks>
    internal static class MLEUpdateChecker
    {
        #region Constants

        /// <summary>
        /// Raw GitHub source URL used to locate the latest ModLoaderExtensions constructor version.
        /// </summary>
        /// <remarks>
        /// This URL intentionally points to raw.githubusercontent.com instead of the normal GitHub page,
        /// making the response plain source text that can be parsed without HTML handling.
        /// </remarks>
        private const string LatestSourceUrl =
            "https://raw.githubusercontent.com/RussDev7/CastleForge/main/CastleForge/ModLoaderFramework/ModLoaderExtensions/ModLoaderExtensions.cs";

        #endregion

        #region Synchronization / State

        /// <summary>
        /// Synchronizes startup so the update checker can only be queued once.
        /// </summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// Tracks whether the background update check has already been started.
        /// </summary>
        private static bool _started;

        /// <summary>
        /// Tracks whether the background update check has completed.
        /// </summary>
        private static bool _hasChecked;

        /// <summary>
        /// Locally installed ModLoaderExtensions version supplied by startup.
        /// </summary>
        private static Version _installedVersion;

        /// <summary>
        /// Latest ModLoaderExtensions version discovered from the GitHub source file.
        /// </summary>
        private static Version _latestVersion;

        #endregion

        #region Public State

        /// <summary>
        /// Installed local version of ModLoaderExtensions.
        /// </summary>
        public static Version InstalledVersion
        {
            get { return _installedVersion; }
        }

        /// <summary>
        /// Latest version discovered from GitHub, if the check succeeded.
        /// </summary>
        public static Version LatestVersion
        {
            get { return _latestVersion; }
        }

        /// <summary>
        /// True only after the checker has completed and found a newer GitHub version.
        /// </summary>
        /// <remarks>
        /// This property is safe for menu/UI code to poll.
        /// It returns false until a successful check discovers a valid newer version.
        /// </remarks>
        public static bool IsUpdateAvailable
        {
            get
            {
                Version installed = _installedVersion;
                Version latest = _latestVersion;

                return _hasChecked &&
                       installed != null &&
                       latest != null &&
                       installed.CompareTo(latest) < 0;
            }
        }
        #endregion

        #region Startup

        /// <summary>
        /// Starts a single background update check.
        /// </summary>
        /// <remarks>
        /// Safe to call multiple times; only the first call queues work.
        /// The installed version is cached before the worker starts so comparison results
        /// remain consistent for the lifetime of the process.
        /// </remarks>
        /// <param name="installedVersion">Currently installed ModLoaderExtensions version.</param>
        public static void Start(Version installedVersion)
        {
            lock (Sync)
            {
                if (_started)
                    return;

                _started = true;
                _installedVersion = installedVersion ?? new Version(0, 0, 0, 0);
            }

            ThreadPool.QueueUserWorkItem(_ => CheckForUpdate());
        }
        #endregion

        #region GitHub Check

        /// <summary>
        /// Downloads the latest ModLoaderExtensions source file and extracts its declared version.
        /// </summary>
        /// <remarks>
        /// This method is intentionally defensive. Update checks are optional quality-of-life UI data,
        /// so network failures, parse failures, or GitHub availability issues should never interrupt
        /// startup or main-menu rendering.
        /// </remarks>
        private static void CheckForUpdate()
        {
            try
            {
                // GitHub requires modern TLS on older .NET Framework installs.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] =
                        "CastleForge-ModLoaderExtensions-UpdateChecker";

                    string source = client.DownloadString(LatestSourceUrl);
                    Version latest = ExtractModLoaderExtensionsVersion(source);

                    if (latest != null)
                    {
                        _latestVersion = latest;

                        if (IsUpdateAvailable)
                        {
                            Log($"[ModLoaderExtensions] Update available: installed {_installedVersion}, latest {latest}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Optional update check should never break startup/menu rendering.
                Log($"[ModLoaderExtensions] Update check failed: {ex.Message}");
            }
            finally
            {
                _hasChecked = true;
            }
        }
        #endregion

        #region Version Parsing

        /// <summary>
        /// Extracts the ModLoaderExtensions version declared in the source constructor.
        /// </summary>
        /// <remarks>
        /// Expected source pattern:
        /// public ModLoaderExtensions() : base("ModLoaderExtensions", new Version("0.1.0.0"))
        ///
        /// Returns null when the source is empty, the constructor pattern is not found,
        /// or the captured version string cannot be parsed by <see cref="Version"/>.
        /// </remarks>
        /// <param name="source">Raw ModLoaderExtensions.cs source text downloaded from GitHub.</param>
        /// <returns>The parsed version when found; otherwise null.</returns>
        private static Version ExtractModLoaderExtensionsVersion(string source)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            Match match = Regex.Match(
                source,
                @"base\s*\(\s*""ModLoaderExtensions""\s*,\s*new\s+Version\s*\(\s*""(?<version>[^""]+)""\s*\)\s*\)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            if (Version.TryParse(match.Groups["version"].Value, out Version version))
                return version;

            return null;
        }
        #endregion
    }
}
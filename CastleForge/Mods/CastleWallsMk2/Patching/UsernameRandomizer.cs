/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using System.Text;
using System.IO;
using System;

using static CastleWallsMk2.CryptoRng;
using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    /// <summary>
    /// Purpose: On game boot, optionally set the player's Gamertag to a random line
    /// from CastleWallsMk2/UsernameList.txt. If the folder is missing, create it.
    /// If the file is missing or contains no valid names, do nothing.
    /// </summary>
    internal static class UsernameRandomizer
    {
        private static readonly string ModRoot   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(UsernameRandomizer).Namespace); // Paths: Mod root and the optional username list file.
        private static readonly string ListPath  = Path.Combine(ModRoot, "UsernameList.txt");                                                          //
        private static bool            _didOnce;                                                                                                       // Idempotence guard: Only run once per process.

        /// <summary>
        /// Try to set a random username immediately after the game boots.
        /// Creates the folder if needed; only touches the name if the list file exists and has at least one valid line.
        /// Safe to call multiple times; it only runs once.
        /// </summary>
        public static void TryApplyAtStartup()
        {
            if (_didOnce) return;
            _didOnce = true;

            try
            {
                // Ensure the mod folder exists (no error if already present).
                try { Directory.CreateDirectory(ModRoot); } catch { /* ignore */ }

                // If the list file is missing, leave the current name unchanged.
                if (!File.Exists(ListPath))
                {
                    Log($"[Name] '\\{typeof(EmbeddedResolver).Namespace}\\UsernameList.txt' missing; leaving current gamertag alone.");
                    return;
                }

                // Read all lines; If read fails, don't change anything.
                string[] lines;
                try { lines = File.ReadAllLines(ListPath); }
                catch
                {
                    Log($"[Name] Failed to read '\\{typeof(EmbeddedResolver).Namespace}\\UsernameList.txt'; leaving current gamertag alone.");
                    return;
                }

                // Normalize + filter:
                // - Trim whitespace.
                // - Skip empty lines.
                // - Skip comments starting with '#' or '//'.
                // - Deduplicate case-insensitively.
                var pool = lines
                    .Select(s => (s ?? string.Empty).Trim())
                    .Where(s => s.Length > 0 && !s.StartsWith("#") && !s.StartsWith("//"))
                    .Select(UnescapeTextFileEscapes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                // Nothing usable? Do nothing.
                if (pool.Length == 0)
                {
                    Log($"[Name] '\\{typeof(EmbeddedResolver).Namespace}\\UsernameList.txt' is empty/no valid names; leaving current gamertag alone.");
                    return;
                }

                // Pick a random entry and apply it.
                var pick = pool[GenerateRandomNumber(0, pool.Length)];
                ApplyName(pick);
            }
            catch { }
        }

        /// <summary>
        /// Converts simple text-file escape sequences into real characters.
        /// Example: "\n" -> newline, "\t" -> tab, "\\\\" -> backslash.
        /// Unknown escapes are kept as literal text.
        /// </summary>
        private static string UnescapeTextFileEscapes(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Supports: \n \r \t \\ (unknown escapes are left as-is).
            var sb = new StringBuilder(s.Length);

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        default:
                            sb.Append('\\').Append(n); // Keep unknown escapes literally.
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Apply the chosen name to both MyNetworkGamer and SignedInGamers if possible,
        // then randomize network IDs to help avoid collisions.
        private static void ApplyName(string name)
        {
            try
            {
                name = name ?? string.Empty;

                if (CastleMinerZGame.Instance.MyNetworkGamer != null)
                    try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch { }
                try     { Gamer.SignedInGamers[PlayerIndex.One].Gamertag    = name; } catch { }
                // try  { RandomizeID(); } catch { } // See functions note bellow.

                Log($"[Name] '\\{typeof(EmbeddedResolver).Namespace}\\UsernameList.txt' was populated. Set name: '{name}' (random).");
            }
            catch { }
        }

        /// <summary>
        /// NOTE:
        /// Patching SteamPlayerID or modifying _steamId does NOT allow you to
        /// bypass bans or change your identity in multiplayer.
        ///
        /// Reason:
        /// The game does NOT use this managed field or property to identify players.
        /// Instead, CastleMiner Z receives the authenticated SteamID directly from
        /// steam_api.dll via SteamNetworking. That Steam-provided ID is cryptographically
        /// verified and becomes SenderId, which is what the host checks against its
        /// BanList.
        ///
        /// Therefore:
        /// Overriding SteamPlayerID only changes what your client displays. It does
        /// NOT change the actual Steam identity seen by other players or used for ban
        /// enforcement, and cannot be used to avoid bans.
        /// </summary>

        // Re-roll internal network identifiers via reflection to appear as a "new" peer.
        public static void RandomizeID(byte? id = null)
        {
            Assembly.GetAssembly(typeof(NetworkGamer)).GetType("DNA.Net.GamerServices.NetworkGamer").GetField("_alternateAddress", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(CastleMinerZGame.Instance.MyNetworkGamer, Convert.ToUInt64(GenerateRandomNumberInclusive(1, int.MaxValue)));
            Assembly.GetAssembly(typeof(NetworkGamer)).GetType("DNA.Net.GamerServices.NetworkGamer").GetField("_globalId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(CastleMinerZGame.Instance.MyNetworkGamer, (id != null) ? id : new byte?((byte)GenerateRandomNumberInclusive(0, 255)));
        }
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Tiny in-memory cache for IP -> (Geo, Pos, ISP) lookups using ip-api.com (CSV endpoint).
    /// Design:
    /// • <see cref="GetOrFetch(string)"/> first checks a local dictionary (case-insensitive).
    /// • On miss, it calls the CSV endpoint: Country, regionName, city, pos, isp.
    /// • Private/LAN/broadcast IPs are ignored (returns (null, null, null)).
    /// Thread-safety:
    /// • A single <see cref="_lock"/> guards all reads/writes to the cache dictionary.
    /// Notes:
    /// • Uses System.Net.WebClient inline (no explicit timeouts in this minimal version).
    /// • Free tier of ip-api.com has rate limits; avoid calling per-frame.
    /// • Consider upgrading to HttpClient with per-request timeouts if needed later.
    /// </summary>
    internal static class GeoIpCache
    {
        #region State

        // Global lock to protect the cache dictionary.
        private static readonly object _lock = new object();

        // Cache: IP -> (geo, pos, isp). Case-insensitive for safety.
        private static readonly Dictionary<string, (string geo, string pos, string isp)> _map
            = new Dictionary<string, (string geo, string pos, string isp)>(128, StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public API

        /// <summary>
        /// Returns cached (geo, pos, isp) if present; otherwise fetches from ip-api.com and caches it.
        /// Returns (null, null, null) on failure or when the IP is private/loopback/broadcast.
        /// </summary>
        /// <param name="ip">IPv4 string (e.g., "xxx.xxx.xxx.xx").</param>
        /// <returns>Tuple (geo, pos, isp) or (null, null, null) on failure.</returns>
        public static (string geo, string pos, string isp) GetOrFetch(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (null, null, null);

            // Do not query for private/LAN/loopback/broadcast.
            if (IsPrivate(ip))
                return (null, null, null);

            // Fast path: cache hit.
            lock (_lock)
            {
                if (_map.TryGetValue(ip, out var hit))
                    return hit;
            }

            // Miss: call ip-api.com CSV endpoint.
            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    // CSV order per request: country,regionName,city,isp
                    string csv = wc.DownloadString("http://ip-api.com/csv/" + ip + "?fields=country,regionName,city,lat,lon,isp");
                    if (string.IsNullOrEmpty(csv))
                        return (null, null, null);

                    // Minimal CSV handling (quotes removed; split by comma).
                    var parts      = csv.Replace("\"", "").Split(',');
                    string country = parts.Length > 0 ? parts[0] : "";
                    string region  = parts.Length > 1 ? parts[1] : "";
                    string city    = parts.Length > 2 ? parts[2] : "";
                    string latStr  = parts.Length > 3 ? parts[3] : "";
                    string lonStr  = parts.Length > 4 ? parts[4] : "";
                    string isp     = parts.Length > 5 ? parts[5] : "";

                    string geo = BuildGeo(city, region, country);
                    string pos = null;

                    // Only record pos if both coords parse.
                    if (double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                        double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                    {
                        // Pretty string; keep a space after comma for readability.
                        pos = lat.ToString(CultureInfo.InvariantCulture) + ", " +
                              lon.ToString(CultureInfo.InvariantCulture);
                    }

                    // Publish to cache.
                    lock (_lock)
                    {
                        _map[ip] = (geo, pos, isp);
                    }
                    return (geo, pos, isp);
                }
            }
            catch
            {
                // Swallow network/parse errors and return empty result.
                return (null, null, null);
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Builds a human-readable location string like "Borrego Springs, California, United States",
        /// gracefully skipping empty components.
        /// </summary>
        private static string BuildGeo(string city, string region, string country)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(city))    parts.Add(city);
            if (!string.IsNullOrWhiteSpace(region))  parts.Add(region);
            if (!string.IsNullOrWhiteSpace(country)) parts.Add(country);
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        /// <summary>
        /// Minimal guard for private/loopback/broadcast patterns.
        /// NOTE: The "172.2" check below is intentionally coarse (20-29),
        /// which also matches addresses outside RFC1918 (e.g., 172.200.x.x).
        /// Keep as-is for now (matches original behavior); refine later if needed.
        /// </summary>
        private static bool IsPrivate(string ip)
        {
            // Super small / fast guard.
            if (ip.StartsWith("10.") ||
                ip.StartsWith("192.168.") ||
                ip.StartsWith("172.16.") || ip.StartsWith("172.17.") || ip.StartsWith("172.18.") ||
                ip.StartsWith("172.19.") || ip.StartsWith("172.2")   || // 20-29 (coarse).
                ip.StartsWith("127.") ||
                ip == "0.0.0.0")
                return true;
            return false;
        }
        #endregion
    }
}

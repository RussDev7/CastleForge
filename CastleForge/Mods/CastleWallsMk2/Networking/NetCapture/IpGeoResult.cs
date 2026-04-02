/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    #region Models

    /// <summary>
    /// DTO describing a resolved IP geo record (used by other components).
    /// Notes:
    /// • <see cref="Lat"/> / <see cref="Lon"/> are nullable if provider returned no coords.
    /// • <see cref="Success"/> indicates whether provider returned usable data.
    /// • <see cref="Pending"/> is a sentinel for "fetch in progress" (not used by this file directly).
    /// </summary>
    internal sealed class IpGeoResult
    {
        public string   Ip         = default;
        public string   Country    = default;
        public string   Region     = default;
        public string   City       = default;
        public string   Isp        = default;
        public double?  Lat        = default; // Null if unknown.
        public double?  Lon        = default; // Null if unknown.
        public DateTime FetchedUtc = default;
        public bool     Success    = default;

        // Marker instance some callers may use to indicate a non-final state.
        public static readonly IpGeoResult Pending = new IpGeoResult { Success = false };
    }
    #endregion

    #region IpGeoCache

    /// <summary>
    /// Thread-safe, non-blocking geo cache around ip-api.com (CSV endpoint).
    /// Design:
    /// • <see cref="GetOrFetch(string)"/> returns quickly: it uses an in-memory cache guarded by a lock.
    /// • On cache miss or expiry, it performs a direct HTTP fetch (synchronously) and updates the cache.
    /// • Separate TTLs for success (<see cref="OkTtl"/>) and failure (<see cref="FailTtl"/>) avoid hot-looping a dead IP.
    /// Configuration:
    /// • Timeouts are read once from <see cref="ModConfig"/>: GeoConnectTimeoutMs / GeoReadTimeoutMs.
    /// • LAN/private addresses are never looked up (returns a long-expiry "(LAN)" entry).
    /// Notes:
    /// • Endpoint used: http://ip-api.com/csv/{ip}?fields=country,regionName,city,lat,lon,isp.
    /// • Free tier has rate limits; callers should avoid spamming lookups.
    /// • This file keeps code minimal and defensive; IO failures return a "failed" entry with a short TTL.
    /// </summary>
    internal static class IpGeoCache
    {
        #region State & Configuration

        private static readonly object _lock = new object();

        // Cache: Ip -> materialized geo entry (with TTL).
        private static readonly Dictionary<string, GeoEntry> _cache =
            new Dictionary<string, GeoEntry>(StringComparer.OrdinalIgnoreCase);

        // TTLs: Successful lookups live longer; failed lookups retry sooner.
        private static readonly TimeSpan FailTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan OkTtl   = TimeSpan.FromHours(6);

        // Lazy-initialized timeouts from ModConfig (guarded by EnsureTimeouts()).
        private static int _connectMs = -1;
        private static int _readMs    = -1;

        /// <summary>
        /// Internal materialized cache entry (no IP field needed; key is the IP).
        /// </summary>
        internal sealed class GeoEntry
        {
            public string   Country;
            public string   Region;
            public string   City;
            public double?  Lat;        // Null if unknown.
            public double?  Lon;        // Null if unknown.
            public string   Isp;
            public DateTime UtcExpires; // Absolute expiry time for this entry.
            public bool     Failed;     // True if last fetch failed.
        }

        /// <summary>
        /// Reads & clamps timeouts from <see cref="ModConfig"/> once.
        /// </summary>
        private static void EnsureTimeouts()
        {
            if (_connectMs >= 0) return;

            var cfg    = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();
            _connectMs = cfg.GeoConnectTimeoutMs;
            _readMs    = cfg.GeoReadTimeoutMs;

            // Guardrails in case config was hand-edited.
            _connectMs = (_connectMs <  250) ?  250 : (_connectMs > 10000 ? 10000 : _connectMs);
            _readMs    = (_readMs    <  250) ?  250 : (_readMs    > 10000 ? 10000 : _readMs);
        }
        #endregion

        #region Public API

        /// <summary>
        /// Clears the entire in-memory geo cache. Thread-safe.
        /// </summary>
        public static void Clear()
        {
            lock (_lock) _cache.Clear();
        }

        /// <summary>
        /// Returns a cached geo entry for the given IP if valid; otherwise fetches it now.
        /// Behavior:
        /// • Private/loopback IPs return a "(LAN)" entry with a long expiry (no external call).
        /// • On cache hit and not expired -> returned as-is.
        /// • On miss/expired -> fetch via <see cref="TryFetch(string)"/>, store, and return.
        /// </summary>
        public static GeoEntry GetOrFetch(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            // Skip private ranges / localhost: No network call, return a friendly label.
            if (IsPrivate(ip))
                return new GeoEntry
                {
                    City       = "(LAN)",
                    Region     = "",
                    Country    = "",
                    Isp        = "",
                    UtcExpires = DateTime.UtcNow.AddYears(10) // Effectively permanent for our purposes.
                };

            // Fast path: cache hit and still valid.
            lock (_lock)
            {
                if (_cache.TryGetValue(ip, out var cached))
                {
                    if (cached.UtcExpires > DateTime.UtcNow)
                        return cached;
                    // else: expired - fall through to refetch
                }
            }

            // Fetch (defensive; never throws out to the caller).
            var fresh = TryFetch(ip);

            // Publish into cache.
            lock (_lock)
            {
                _cache[ip] = fresh;
            }

            return fresh;
        }
        #endregion

        #region Fetch (Internal)

        /// <summary>
        /// Attempts a CSV fetch from ip-api.com for the specified IP.
        /// Returns a populated <see cref="GeoEntry"/> on success, or a "failed" entry with short TTL.
        /// Never throws (errors are mapped to a failed entry).
        /// </summary>
        private static GeoEntry TryFetch(string ip)
        {
            EnsureTimeouts();

            // Default failure payload (short TTL so we retry later).
            var fail = new GeoEntry
            {
                City       = "",
                Region     = "",
                Country    = "",
                Lat        = null,
                Lon        = null,
                Isp        = "",
                Failed     = true,
                UtcExpires = DateTime.UtcNow + FailTtl
            };

            try
            {
                // HttpWebRequest lets us set per-request timeouts (connect & read/write).
                var req = (HttpWebRequest)WebRequest.Create(
                    "http://ip-api.com/csv/" + ip + "?fields=country,regionName,city,lat,lon,isp");

                req.Timeout         = _connectMs; // Connect timeout.
                req.ReadWriteTimeout = _readMs;   // Response read timeout.

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr   = new StreamReader(resp.GetResponseStream()))
                {
                    // Example CSV: "United States,California,Borrego Springs,33.256,-116.375,Comcast".
                    var csv   = sr.ReadToEnd();
                    var parts = csv.Replace("\"", "").Split(',');

                    var ok = new GeoEntry
                    {
                        Country    = parts.Length > 0 ? parts[0] : "",
                        Region     = parts.Length > 1 ? parts[1] : "",
                        City       = parts.Length > 2 ? parts[2] : "",
                        Lat        = parts.Length > 3
                                       ? (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ? lat : (double?)null)
                                       : null,
                        Lon        = parts.Length > 4
                                       ? (double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) ? lon : (double?)null)
                                       : null,
                        Isp        = parts.Length > 5 ? parts[5] : "",
                        Failed     = false,
                        UtcExpires = DateTime.UtcNow + OkTtl
                    };
                    return ok;
                }
            }
            catch (WebException)
            {
                // Remote didn't answer / blocked / DNS / rate-limit -> short TTL failure.
                return fail;
            }
            catch (IOException)
            {
                return fail;
            }
            catch (System.Net.Sockets.SocketException)
            {
                return fail;
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Minimal RFC1918 + loopback check. Fits typical CMZ LAN usage.
        /// </summary>
        private static bool IsPrivate(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            return ip.StartsWith("192.168.") ||
                   ip.StartsWith("10.")      ||
                   ip.StartsWith("172.16.") || ip.StartsWith("172.17.") ||
                   ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
                   ip.StartsWith("172.20.") || ip.StartsWith("172.21.") ||
                   ip.StartsWith("172.22.") || ip.StartsWith("172.23.") ||
                   ip.StartsWith("172.24.") || ip.StartsWith("172.25.") ||
                   ip.StartsWith("172.26.") || ip.StartsWith("172.27.") ||
                   ip.StartsWith("172.28.") || ip.StartsWith("172.29.") ||
                   ip.StartsWith("172.30.") || ip.StartsWith("172.31.") ||
                   ip == "127.0.0.1";
        }

        /// <summary>
        /// Opens Google Maps for a lat/lon if available; otherwise searches by a human-readable fallback (e.g., "City, Region, Country").
        /// Uses <see cref="Process.Start(string)"/> with UseShellExecute = true.
        /// </summary>
        public static void OpenInGoogleMaps(double? lat, double? lon, string placeFallback)
        {
            string url;
            if (lat.HasValue && lon.HasValue)
            {
                url = "https://www.google.com/maps/search/?api=1&query="
                    + lat.Value.ToString(CultureInfo.InvariantCulture)
                    + ","
                    + lon.Value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var q = string.IsNullOrWhiteSpace(placeFallback) ? "" : Uri.EscapeDataString(placeFallback);
                url = "https://www.google.com/maps/search/?api=1&query=" + q;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SendFeedback($"[Net] Open Maps failed: {ex.Message}.");
            }
        }
        #endregion
    }
    #endregion
}
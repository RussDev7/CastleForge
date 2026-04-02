/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using PacketDotNet;
using System.Linq;
using SharpPcap;
using System;

using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    #region NetCaptureConfig

    /// <summary>
    /// Adapter-picking preferences for the passive sniffer.
    /// Order of precedence:
    /// 1) <see cref="PreferredIndex"/> when within bounds.
    /// 2) <see cref="PreferredName"/> partial (case-insensitive).
    /// 3) Heuristic shortlist (Intel/Wi-Fi/Realtek/Ethernet/GbE).
    /// </summary>
    internal static class NetCaptureConfig
    {
        // Prefer by (partial) name - case-insensitive.
        // Ex: "intel", "realtek", "wireless", "wi-fi", "9462".
        public static string PreferredName = "";

        // Or prefer by index from the printed list (0..N-1).
        // Ex: 5 means "use device #5 from the log".
        public static int PreferredIndex = -1;

        // Extra flag carried by UI; respected by table masking/logic elsewhere.
        public static bool HideOwnIp = true;
    }
    #endregion

    #region IpResolver (Sniffer Core)

    /// <summary>
    /// Captures packets and correlates them to local processes by port ownership.
    /// Primary goal: Reveal remote public endpoints (host/server) for CMZ/Steam,
    /// even when UDP tables show "-".
    ///
    /// Notes:
    /// - Threading: SharpPcap raises OnPacketArrival on its own thread.
    ///   We guard _rows with _rowsLock.
    /// - Uniqueness: Rows are keyed by a composite string to avoid duplicates.
    /// - Geo/ISP: LogNetworkHit enriches rows via IpGeoCache.
    /// </summary>
    internal sealed class IpResolver
    {
        #region Fields & State

        private ICaptureDevice _device;
        private Dictionary<int, (int pid, string proc)> _portMap;
        private bool _running;

        private readonly object _rowsLock = new object();
        private readonly Dictionary<string, SniffRow> _rows = new Dictionary<string, SniffRow>(256);

        private string _lastDeviceDesc = null;
        private int    _lastPortCount  = 0;

        #endregion

        #region Public API

        /// <summary>
        /// Starts capture on the best adapter according to <see cref="NetCaptureConfig"/>.
        /// Sets BPF filter to "udp or tcp".
        /// </summary>
        public void Start()
        {
            if (_running) return;

            _portMap = PortSnapshot.GetInterestingUdpPorts();
            _lastPortCount = _portMap != null ? _portMap.Count : 0;

            // SendFeedback("[Net] initial port map count = " + _lastPortCount);

            var devices = CaptureDeviceList.Instance;
            if (devices == null || devices.Count == 0)
                throw new InvalidOperationException("No capture devices found.");

            // Pick device (config -> name -> heuristic -> first non-junk -> [0]).
            var picked = PickBestDevice(devices);
            _device = picked;
            _lastDeviceDesc = picked.Description;

            // SendFeedback("[Net] Using device: " + _lastDeviceDesc);

            _device.OnPacketArrival += Device_OnPacketArrival;
            _device.Open();
            _device.Filter = "udp or tcp";
            _device.StartCapture();
            _running = true;

            // SendFeedback("[Net] capture started.");
        }

        /// <summary>Stops capture and closes the current device.</summary>
        public void Stop()
        {
            if (!_running) return;
            if (_device != null)
            {
                _device.StopCapture();
                _device.Close();
                _device = null;
            }
            _running = false;
        }

        /// <summary>
        /// Rebuilds the UDP port snapshot (CMZ + Steam, etc.).
        /// Safe to call while capturing.
        /// </summary>
        public void RefreshPorts()
        {
            _portMap = PortSnapshot.GetInterestingUdpPorts();
            _lastPortCount = _portMap != null ? _portMap.Count : 0;
        }

        /// <summary>
        /// Returns a stable snapshot of current unique rows.
        /// </summary>
        public List<SniffRow> GetSnapshot()
        {
            lock (_rowsLock)
            {
                return _rows.Values.ToList();
            }
        }

        /// <summary>
        /// Lightweight status for UI header (device, #ports, #rows).
        /// </summary>
        public SnifferInfo GetInfo()
        {
            var info = new SnifferInfo
            {
                DeviceName = _lastDeviceDesc,
                PortCount = _lastPortCount
            };
            lock (_rowsLock) { info.RowCount = _rows.Count; }
            return info;
        }

        /// <summary>Clears current rows (does not touch capture or ports).</summary>
        public void ClearRows()
        {
            lock (_rowsLock)
            {
                _rows.Clear();
            }
        }

        /// <summary>
        /// Optional: clears rows and optionally refreshes the port snapshot.
        /// If capturing, briefly pauses/restarts to ensure a clean slate.
        /// </summary>
        /// <param name="refreshPorts">If true, rebuild the UDP port map.</param>
        public void Reset(bool refreshPorts = true)
        {
            bool wasRunning = _running;
            if (wasRunning)
            {
                try { _device?.StopCapture(); } catch { }
            }

            if (refreshPorts)
                _portMap = PortSnapshot.GetInterestingUdpPorts();

            lock (_rowsLock) { _rows.Clear(); }

            if (wasRunning)
            {
                try { _device?.StartCapture(); } catch (Exception ex) { SendFeedback($"[Net] restart failed: {ex.Message}."); }
            }
        }
        #endregion

        #region DTOs

        internal struct SnifferInfo
        {
            public string DeviceName;
            public int    PortCount;
            public int    RowCount;
        }

        internal struct SniffRow
        {
            public string   Process;
            public int      Pid;
            public string   Dir;
            public string   LocalIp;
            public int      LocalPort;
            public string   RemoteIp;
            public int      RemotePort;
            public DateTime LastSeenUtc;
            public string   Geo;
            public string   Isp;
        }
        #endregion

        #region Capture Pipeline (Parsing -> Dispatch)

        /// <summary>
        /// SharpPcap callback. Parses the packet and dispatches to IPv4 handler.
        /// </summary>
        private void Device_OnPacketArrival(object sender, PacketCapture e)
        {
            var raw = e.GetPacket();
            if (raw == null) return;

            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);

            var ip = packet.Extract<IPv4Packet>();
            if (ip != null)
            {
                HandleIpV4(ip, packet);
                return;
            }
        }

        /// <summary>
        /// Extracts UDP/TCP from an IPv4 packet and routes to per-proto handler.
        /// </summary>
        private void HandleIpV4(IPv4Packet ip, Packet basePacket)
        {
            var udp = basePacket.Extract<UdpPacket>();
            if (udp != null)
            {
                HandleUdp(ip, udp);
                return;
            }

            var tcp = basePacket.Extract<TcpPacket>();
            if (tcp != null)
            {
                HandleTcp(ip, tcp);
            }
        }

        /// <summary>
        /// UDP correlation using the current port snapshot.
        /// Direction is inferred from which side matches local-owned ports.
        /// </summary>
        private void HandleUdp(IPv4Packet ip, UdpPacket udp)
        {
            if (_portMap == null || _portMap.Count == 0)
                return;

            int sport = udp.SourcePort;
            int dport = udp.DestinationPort;

            bool matched = false;
            (int pid, string proc) owner = default;

            string dir = null;
            string localIp = null;
            int localPort = 0;
            string remoteIp = null;
            int remotePort = 0;

            if (_portMap.TryGetValue(sport, out var srcOwner))
            {
                matched = true;
                owner = srcOwner;
                dir = "OUT";
                localIp = ip.SourceAddress.ToString();
                localPort = sport;
                remoteIp = ip.DestinationAddress.ToString();
                remotePort = dport;
            }
            else if (_portMap.TryGetValue(dport, out var dstOwner))
            {
                matched = true;
                owner = dstOwner;
                dir = "IN";
                localIp = ip.DestinationAddress.ToString();
                localPort = dport;
                remoteIp = ip.SourceAddress.ToString();
                remotePort = sport;
            }

            if (!matched)
                return;

            LogNetworkHit(owner.proc, owner.pid, dir, localIp, localPort, remoteIp, remotePort);
        }

        /// <summary>
        /// TCP correlation. Re-uses the UDP snapshot (good enough for CMZ/Steam),
        /// but tags direction as "IN-TCP"/"OUT-TCP" so the UI can distinguish.
        /// </summary>
        private void HandleTcp(IPv4Packet ip, TcpPacket tcp)
        {
            // For now we reuse the same UDP port map (CMZ + Steam) to decide
            // whether this TCP packet is "interesting". Later we can build a
            // separate TCP-specific snapshot if we want.
            if (_portMap == null || _portMap.Count == 0)
                return;

            int sport = tcp.SourcePort;
            int dport = tcp.DestinationPort;

            bool matched = false;
            (int pid, string proc) owner = default;

            string dir = null;
            string localIp = null;
            int localPort = 0;
            string remoteIp = null;
            int remotePort = 0;

            // OUT: Our process is the sender.
            if (_portMap.TryGetValue(sport, out var srcOwner))
            {
                matched = true;
                owner = srcOwner;
                dir = "OUT-TCP";
                localIp = ip.SourceAddress.ToString();
                localPort = sport;
                remoteIp = ip.DestinationAddress.ToString();
                remotePort = dport;
            }
            // IN: Our process is the receiver.
            else if (_portMap.TryGetValue(dport, out var dstOwner))
            {
                matched = true;
                owner = dstOwner;
                dir = "IN-TCP";
                localIp = ip.DestinationAddress.ToString();
                localPort = dport;
                remoteIp = ip.SourceAddress.ToString();
                remotePort = sport;
            }

            if (!matched)
                return;

            // (future) we can gate on flags to reduce noise:
            // bool isSyn      = tcp.Synchronize && !tcp.Ack;
            // bool isFin      = tcp.Finished;
            // bool hasPayload = tcp.PayloadData != null && tcp.PayloadData.Length > 0;
            // if (!hasPayload) return; // Example filter.

            LogNetworkHit(owner.proc, owner.pid, dir,
                          localIp, localPort,
                          remoteIp, remotePort);
        }
        #endregion

        #region Row Logging & Enrichment

        /// <summary>
        /// Builds/updates a unique row and enriches it with Geo + ISP from <see cref="IpGeoCache"/>.
        /// Remote endpoint is preferred for lookups.
        /// </summary>
        private void LogNetworkHit(string proc, int pid, string dir,
                                   string localIp, int localPort,
                                   string remoteIp, int remotePort)
        {
            string geo = "";
            string isp = "";

            // Only try remote (that's what we care about).
            var geoEntry = IpGeoCache.GetOrFetch(remoteIp);
            if (geoEntry != null && !geoEntry.Failed)
            {
                // Show "City, Region, Country".
                geo = string.IsNullOrEmpty(geoEntry.City)
                    ? geoEntry.Country
                    : $"{geoEntry.City}, {geoEntry.Region}, {geoEntry.Country}".Trim(' ', ',');
                isp = geoEntry.Isp ?? "";
            }

            string key = proc + "|" + dir + "|" + localIp + ":" + localPort + "|" + remoteIp + ":" + remotePort;
            var row = new SniffRow
            {
                Process = proc,
                Pid = pid,
                Dir = dir,
                LocalIp = localIp,
                LocalPort = localPort,
                RemoteIp = remoteIp,
                RemotePort = remotePort,
                LastSeenUtc = DateTime.UtcNow,
                Geo = geo,
                Isp = isp,
            };
            lock (_rowsLock)
            {
                _rows[key] = row;
            }
        }
        #endregion

        #region Adapter Selection

        /// <summary>
        /// Chooses a capture adapter using config, name match, and heuristics.
        /// Skips loopback/VM/monitor/Bluetooth adapters when possible.
        /// </summary>
        private static ICaptureDevice PickBestDevice(CaptureDeviceList devices)
        {
            ICaptureDevice picked = null;

            // ------------------------------------------
            // Config: Pick by index (takes priority).
            // ------------------------------------------
            if (NetCaptureConfig.PreferredIndex >= 0 &&
                NetCaptureConfig.PreferredIndex < devices.Count)
            {
                picked = devices[NetCaptureConfig.PreferredIndex];
                return picked;
            }

            // ------------------------------------------
            // Config: Pick by partial name.
            // ------------------------------------------
            string pref = NetCaptureConfig.PreferredName;
            if (!string.IsNullOrWhiteSpace(pref))
            {
                string needle = pref.Trim().ToLowerInvariant();
                foreach (var d in devices)
                {
                    string hay = ((d.Description ?? "") + " " + (d.Name ?? "")).ToLowerInvariant();
                    if (hay.Contains(needle))
                    {
                        picked = d;
                        return picked;
                    }
                }
                // If we get here, the requested name wasn't found - we'll fall through.
                SendFeedback($"[Net] Preferred adapter '{pref}' not found, falling back...");
            }

            // ------------------------------------------
            // Heuristic.
            // ------------------------------------------
            string[] preferred =
            {
                "intel", "wireless", "wi-fi",
                "realtek", "ethernet", "gbe"
            };

            foreach (var d in devices)
            {
                var hay = ((d.Description ?? "") + " " + (d.Name ?? "")).ToLowerInvariant();

                // skip junk
                if (hay.Contains("loopback"))        continue;
                if (hay.Contains("wan miniport"))    continue;
                if (hay.Contains("network monitor")) continue;
                if (hay.Contains("virtualbox"))      continue;
                if (hay.Contains("hyper-v"))         continue;
                if (hay.Contains("vmware"))          continue;
                if (hay.Contains("bluetooth"))       continue;

                bool isPreferred = preferred.Any(p => hay.Contains(p));
                if (isPreferred)
                {
                    picked = d;
                    break;
                }
            }

            // ------------------------------------------
            // Last resort: First non-junk.
            // ------------------------------------------
            if (picked == null)
            {
                foreach (var d in devices)
                {
                    var hay = ((d.Description ?? "") + " " + (d.Name ?? "")).ToLowerInvariant();
                    if (hay.Contains("loopback"))        continue;
                    if (hay.Contains("wan miniport"))    continue;
                    if (hay.Contains("network monitor")) continue;
                    if (hay.Contains("virtualbox"))      continue;
                    if (hay.Contains("hyper-v"))         continue;
                    if (hay.Contains("vmware"))          continue;
                    if (hay.Contains("bluetooth"))       continue;

                    picked = d;
                    break;
                }
            }

            // ------------------------------------------
            // Truly ultimate fallback.
            // ------------------------------------------
            if (picked == null)
                picked = devices[0];

            return picked;
        }
        #endregion
    }
    #endregion
}
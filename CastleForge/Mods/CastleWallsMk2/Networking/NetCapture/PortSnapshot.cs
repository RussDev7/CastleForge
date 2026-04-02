/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System;

namespace CastleWallsMk2
{
    #region PortSnapshot (Filters Processes -> Builds UDP Port Map)

    /// <summary>
    /// Builds a map of local UDP port -> (pid, processName) only for
    /// interesting processes (CastleMinerZ + Steam family).
    ///
    /// Notes:
    /// • Relies on <see cref="NetPortHelper.GetAllByPid(int)"/> which enumerates
    ///   TCP/UDP across IPv4/IPv6 via iphlpapi (GetExtended*Table).
    /// • Unknown / inaccessible processes are skipped (try/catch).
    /// • If a process owns multiple UDP ports, all are added.
    /// • If multiple processes own the same port (rare), first-writer wins.
    /// </summary>
    internal static class PortSnapshot
    {
        /// <summary>
        /// Returns a dictionary mapping UDP ports to (PID, ProcessName) for
        /// CastleMinerZ + Steam processes.
        /// </summary>
        public static Dictionary<int, (int pid, string proc)> GetInterestingUdpPorts()
        {
            var result = new Dictionary<int, (int, string)>();

            // Target process names (case-insensitive). Tweak/extend as needed.
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CastleMinerZ",
                "steam",
                "steamwebhelper"
            };

            foreach (var p in Process.GetProcesses())
            {
                if (!targets.Contains(p.ProcessName))
                    continue;

                try
                {
                    // Enumerate ALL endpoints owned by this PID (TCP/UDP, v4/v6).
                    foreach (var e in NetPortHelper.GetAllByPid(p.Id))
                    {
                        // We only care about UDP here (UDP has no kernel-exposed remote EP by default).
                        if (e.Protocol != "UDP")
                            continue;

                        int port = e.Local.Port;
                        if (!result.ContainsKey(port))
                            result[port] = (p.Id, p.ProcessName);
                    }
                }
                catch
                {
                    // Some system processes (or rapidly exiting ones) may throw; ignore and continue.
                }
            }

            return result;
        }
    }
    #endregion

    #region NetPortHelper (P/Invoke Wrappers: GetExtendedTcp/UdpTable)

    /// <summary>
    /// Thin interop layer over iphlpapi to enumerate TCP/UDP endpoints with owning PIDs.
    ///
    /// Notes & Caveats:
    /// • The "extended" tables provide owning PID per row (no admin required in typical setups).
    /// • IPv4 vs IPv6 use different row layouts; this file handles both for UDP.
    /// • For TCP, this implementation calls GetExtendedTcpTable with the v4 row struct for both AFs
    ///   (this mirrors your existing code). On some Windows versions, true IPv6 TCP requires the
    ///   MIB_TCP6ROW_OWNER_PID layout and a different parse path; consider a separate implementation
    ///   if you need full TCPv6 fidelity.
    /// • Ports are converted from network byte order to host order; invalid/bogus rows are skipped.
    /// • UDP remote endpoint is generally null (kernel doesn't bind a remote on UDP).
    /// </summary>
    internal static class NetPortHelper
    {
        #region Public API

        /// <summary>
        /// Enumerates all TCP/UDP (v4 and v6) entries owned by a PID.
        /// The caller can filter later (e.g., keep only UDP).
        /// </summary>
        public static IEnumerable<NetEntry> GetAllByPid(int pid)
        {
            // TCP v4.
            foreach (var e in GetTcpTableOwnerPid(AF_INET))
                if (e.Pid == pid)
                    yield return e;

            // TCP v6.
            foreach (var e in GetTcpTableOwnerPid(AF_INET6))
                if (e.Pid == pid)
                    yield return e;

            // UDP v4.
            foreach (var e in GetUdpTableOwnerPid(AF_INET))
                if (e.Pid == pid)
                    yield return e;

            // UDP v6.
            foreach (var e in GetUdpTableOwnerPid(AF_INET6))
                if (e.Pid == pid)
                    yield return e;
        }
        #endregion

        #region Shared types/const

        private const int AF_INET  = 2;
        private const int AF_INET6 = 23;

        /// <summary>
        /// Unified view of a kernel socket/endpoint row.
        /// For UDP, <see cref="Remote"/> is typically null and <see cref="State"/> is <see cref="TcpState.Unknown"/>.
        /// </summary>
        internal struct NetEntry
        {
            public string      Protocol; // "TCP" / "UDP".
            public IPEndPoint  Local;
            public IPEndPoint  Remote;   // Null for UDP.
            public TcpState    State;    // TcpState.Unknown for UDP.
            public int         Pid;
        }

        #endregion

        #region TCP (GetExtendedTcpTable)

        /// <summary>
        /// Table class requesting rows with owner PID.
        /// </summary>
        private enum TCP_TABLE_CLASS : int
        {
            TCP_TABLE_OWNER_PID_ALL = 5
        }

        /// <summary>
        /// NOTE: This is the IPv4 row layout (MIB_TCPROW_OWNER_PID).
        /// For true IPv6 rows, Windows exposes a different struct (MIB_TCP6ROW_OWNER_PID).
        /// This implementation intentionally mirrors the user's existing code and uses this layout
        /// for both AF_INET and AF_INET6 calls; if you need robust TCPv6, consider a dedicated path.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,            // AF_INET or AF_INET6.
            TCP_TABLE_CLASS tblClass,
            uint reserved = 0);

        /// <summary>
        /// Enumerates TCP rows for a given address family (AF_INET or AF_INET6).
        /// Buffer size is requested first (0 call), then the buffer is allocated and filled.
        ///
        /// Implementation notes:
        /// • Row count is a 32-bit int at the start of the buffer; rows follow immediately.
        /// • Ports are in network byte order and are converted to host order.
        /// • Invalid rows (bad/zero ports) are skipped.
        /// • For AF_INET6, this uses the IPv4 row struct per original code (see note above).
        /// </summary>
        private static IEnumerable<NetEntry> GetTcpTableOwnerPid(int af)
        {
            int buffSize = 0;
            _ = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, af,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            IntPtr buff = Marshal.AllocHGlobal(buffSize);
            try
            {
                uint res = GetExtendedTcpTable(buff, ref buffSize, true, af,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                if (res != 0)
                    yield break;

                int rowCount = Marshal.ReadInt32(buff);
                IntPtr rowPtr = IntPtr.Add(buff, 4);
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);

                    int localPort  = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort);
                    int remotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.remotePort);

                    // Skip invalid ports.
                    if (localPort <= 0 || localPort > 65535)
                    {
                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                        continue;
                    }

                    // For AF_INET, the uint maps to IPv4.
                    // For AF_INET6 here we still reinterpret the 32-bit fields (per original code).
                    IPAddress localIP = (af == AF_INET)
                        ? new IPAddress(row.localAddr)
                        : new IPAddress(row.localAddr);

                    IPAddress remoteIP = (af == AF_INET)
                        ? new IPAddress(row.remoteAddr)
                        : new IPAddress(row.remoteAddr);

                    var localEp  = new IPEndPoint(localIP,  localPort);
                    var remoteEp = remotePort > 0 ? new IPEndPoint(remoteIP, remotePort) : null;

                    yield return new NetEntry
                    {
                        Protocol = "TCP",
                        Local    = localEp,
                        Remote   = remoteEp,
                        State    = (TcpState)row.state,
                        Pid      = (int)row.owningPid
                    };

                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buff);
            }
        }
        #endregion

        #region UDP (GetExtendedUdpTable)

        /// <summary>
        /// UDP table class requesting rows with owner PID.
        /// </summary>
        private enum UDP_TABLE_CLASS : int
        {
            UDP_TABLE_OWNER_PID = 1
        }

        /// <summary>
        /// IPv4 UDP row (owner PID).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }

        /// <summary>
        /// IPv6 UDP row (owner PID).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint   localScopeId;
            public uint   localPort;
            public uint   owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            UDP_TABLE_CLASS tblClass,
            uint reserved = 0);

        /// <summary>
        /// Enumerates UDP rows for a given family (v4/v6). Converts local port to host order.
        /// Remote is null by design for UDP. State is <see cref="TcpState.Unknown"/>.
        /// </summary>
        private static IEnumerable<NetEntry> GetUdpTableOwnerPid(int af)
        {
            int buffSize = 0;
            _ = GetExtendedUdpTable(IntPtr.Zero, ref buffSize, true, af,
                UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            IntPtr buff = Marshal.AllocHGlobal(buffSize);
            try
            {
                uint res = GetExtendedUdpTable(buff, ref buffSize, true, af,
                    UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
                if (res != 0)
                    yield break;

                int rowCount = Marshal.ReadInt32(buff);
                IntPtr rowPtr = IntPtr.Add(buff, 4);

                if (af == AF_INET)
                {
                    // IPv4 UDP rows.
                    int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);

                        int localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort);
                        if (localPort <= 0 || localPort > 65535)
                        {
                            rowPtr = IntPtr.Add(rowPtr, rowSize);
                            continue;
                        }

                        var localIP = new IPAddress(row.localAddr);
                        yield return new NetEntry
                        {
                            Protocol = "UDP",
                            Local    = new IPEndPoint(localIP, localPort),
                            Remote   = null,
                            State    = TcpState.Unknown,
                            Pid      = (int)row.owningPid
                        };

                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
                else // AF_INET6.
                {
                    // IPv6 UDP rows.
                    int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);

                        int localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort);
                        if (localPort <= 0 || localPort > 65535)
                        {
                            rowPtr = IntPtr.Add(rowPtr, rowSize);
                            continue;
                        }

                        // Scope ID included for link-local addresses; ctor handles it.
                        var localIP = new IPAddress(row.localAddr, (long)row.localScopeId);
                        yield return new NetEntry
                        {
                            Protocol = "UDP",
                            Local    = new IPEndPoint(localIP, localPort),
                            Remote   = null,
                            State    = TcpState.Unknown,
                            Pid      = (int)row.owningPid
                        };

                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buff);
            }
        }
        #endregion
    }
    #endregion
}
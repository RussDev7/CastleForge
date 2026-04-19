/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;

namespace CMZDedicatedSteamServer.Hosting
{
    /// <summary>
    /// Tracks SteamID/GID ownership and connected peer identity for the dedicated Steam host.
    ///
    /// Notes:
    /// - GID 0 remains the authoritative host.
    /// - Remote peers begin at GID 1.
    /// - We keep the original Gamer object from approval so we can send ConnectedMessage/NewPeer
    ///   with the same data shape the stock Steam provider expects.
    /// </summary>
    internal sealed class SteamPeerRegistry
    {
        private readonly Dictionary<ulong, ConnectedSteamPeer> _steamIdToPeer = [];
        private readonly Dictionary<byte, ulong> _gidToSteamId = [];
        private byte _nextGid = 1;

        public bool TryGetGid(ulong steamId, out byte gid)
        {
            if (_steamIdToPeer.TryGetValue(steamId, out ConnectedSteamPeer peer))
            {
                gid = peer.Gid;
                return true;
            }

            gid = 0;
            return false;
        }

        public bool TryGetConnectedPeer(ulong steamId, out ConnectedSteamPeer peer)
        {
            return _steamIdToPeer.TryGetValue(steamId, out peer);
        }

        public bool TryGetSteamId(byte gid, out ulong steamId) => _gidToSteamId.TryGetValue(gid, out steamId);

        public byte AllocateGid(ulong steamId)
        {
            if (_steamIdToPeer.TryGetValue(steamId, out ConnectedSteamPeer existing))
                return existing.Gid;

            byte gid;
            do
            {
                if (_nextGid == 0)
                    _nextGid = 1;

                gid = _nextGid++;
            }
            while (_gidToSteamId.ContainsKey(gid));

            return gid;
        }

        public ConnectedSteamPeer AddConnectedPeer(ulong steamId, byte gid, string gamertag, object gamer)
        {
            ConnectedSteamPeer peer = new(steamId, gid, gamertag, gamer);
            _steamIdToPeer[steamId] = peer;
            _gidToSteamId[gid] = steamId;
            return peer;
        }

        public IReadOnlyList<ConnectedSteamPeer> GetConnectedPeersSnapshot()
        {
            return [.. _steamIdToPeer.Values];
        }

        public bool RemoveBySteamId(ulong steamId, out ConnectedSteamPeer peer)
        {
            if (!_steamIdToPeer.TryGetValue(steamId, out peer))
                return false;

            _steamIdToPeer.Remove(steamId);
            _gidToSteamId.Remove(peer.Gid);
            return true;
        }
    }

    internal sealed class ConnectedSteamPeer(ulong steamId, byte gid, string gamertag, object gamer)
    {
        public ulong SteamId { get; } = steamId;
        public byte Gid { get; } = gid;
        public string Gamertag { get; } = gamertag;
        public object Gamer { get; } = gamer;
    }
}

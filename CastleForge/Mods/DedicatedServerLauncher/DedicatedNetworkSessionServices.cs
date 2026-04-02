/*
SPDX-License-Identifier: GPL-3.0-or-later
*/

using System;
using DNA.Net.MatchMaking;

namespace DedicatedServerLauncher
{
    /// <summary>
    /// Minimal session-services shim for Lidgren hosting.
    /// This avoids depending on Steam matchmaking/session reporting for the dedicated instance.
    /// </summary>
    internal sealed class DedicatedNetworkSessionServices : NetworkSessionServices
    {
        public DedicatedNetworkSessionServices(Guid productId, int networkVersion)
            : base(productId, networkVersion)
        {
        }

        public override HostSessionInfo CreateNetworkSession(CreateSessionInfo sessionInfo)
        {
            HostSessionInfo info = new HostSessionInfo();
            info.Name = sessionInfo.Name;
            info.PasswordProtected = sessionInfo.PasswordProtected;
            info.SessionProperties = sessionInfo.SessionProperties;
            info.JoinGamePolicy = sessionInfo.JoinGamePolicy;
            return info;
        }

        public override void CloseNetworkSession(HostSessionInfo hostSession) { }

        public override void UpdateHostSession(HostSessionInfo hostSession) { }

        public override void UpdateClientInfo(ClientSessionInfo clientSession) { }

        public override void ReportClientJoined(HostSessionInfo hostSession, string userName) { }

        public override void ReportClientLeft(HostSessionInfo hostSession, string userName) { }

        public override void ReportSessionAlive(HostSessionInfo hostSession) { }

        public override ClientSessionInfo[] QueryClientInfo(QuerySessionInfo queryInfo)
        {
            return new ClientSessionInfo[0];
        }
    }
}
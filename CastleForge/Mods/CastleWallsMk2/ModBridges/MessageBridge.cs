/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.Net.GamerServices;
using System.Reflection;
using HarmonyLib;
using DNA.Net;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Reflection-friendly helper layer for creating and sending internal DNA.Net Message instances.
    ///
    /// Summary:
    /// - Provides a generic helper to grab a send-instance of a Message type.
    /// - Provides a non-generic helper that supports internal message types via reflection.
    /// - Caches MethodInfo handles for DoSend overloads (broadcast vs direct send).
    ///
    /// Notes:
    /// - This class inteHntionally extends Message to reuse protected/internal Message plumbing (GetSendInstance).
    /// - The "new" DoSend field hides Message.DoSend members (if any) and provides a stable reflection handle.
    /// - AccessTools is used to avoid hardcoding binding flags and to keep patch code concise.
    /// </summary>
    internal abstract class MessageBridge : Message
    {
        #region Construction (GetSendInstance elpers)

        /// <summary>
        /// Returns a send-instance of the given Message type.
        ///
        /// Summary:
        /// - Convenience wrapper over Message.GetSendInstance<T>().
        ///
        /// Notes:
        /// - Intended for message types that are accessible at compile time.
        /// </summary>
        public static T Get<T>() where T : Message => GetSendInstance<T>();

        /// <summary>
        /// Reflection-based send-instance retrieval for Message types that are internal/unknown at compile time.
        ///
        /// Summary:
        /// - Locates Message.GetSendInstance<T> via Harmony AccessTools.
        /// - Closes it with MakeGenericMethod(messageType).
        /// - Invokes it to return a send-instance as Message.
        ///
        /// Notes:
        /// - Returns null if messageType is null.
        /// - Assumes GetSendInstance has no parameters (Invoke(null, null)).
        /// - This is a key helper when bridging into internal net messages without direct references.
        /// </summary>
        public static Message Get(Type messageType)
        {
            if (messageType == null) return null;

            var mi = AccessTools.Method(typeof(Message), "GetSendInstance")
                                .MakeGenericMethod(messageType);

            return (Message)mi.Invoke(null, null);
        }
        #endregion

        #region Send Methods (Cached MethodInfo Handles)

        /// <summary>
        /// DoSend (broadcast)
        /// ------------------
        /// Cached MethodInfo for Message.DoSend(LocalNetworkGamer).
        ///
        /// Summary:
        /// - 1-arg broadcast send (normal network message send path).
        ///
        /// Notes:
        /// - Declared as "new" to hide any base members named DoSend.
        /// - Useful when you need to invoke the method via reflection against an internal message instance.
        /// </summary>
        public static new readonly MethodInfo DoSend = AccessTools.Method(typeof(Message), "DoSend",
            new[] { typeof(LocalNetworkGamer) });

        /// <summary>
        /// DoSendDirect (targeted)
        /// ----------------------
        /// Cached MethodInfo for Message.DoSend(LocalNetworkGamer, NetworkGamer).
        ///
        /// Summary:
        /// - 2-arg send directly to a specific recipient.
        ///
        /// Notes:
        /// - Useful for private messages / point-to-point messages when supported by the underlying net layer.
        /// </summary>
        public static readonly MethodInfo DoSendDirect = AccessTools.Method(typeof(Message), "DoSend",
            new[] { typeof(LocalNetworkGamer), typeof(NetworkGamer) });

        #endregion
    }
}
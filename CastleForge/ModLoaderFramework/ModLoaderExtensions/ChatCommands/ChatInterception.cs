/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using HarmonyLib;                 /* https://github.com/pardeike/Harmony */
using ModLoader;
using System;

namespace ModLoaderExt
{
    /// <summary>
    /// Captures outgoing chat calls to BroadcastTextMessage.Send(...)
    /// and routes them into your own handler.  If the handler returns true,
    /// the real Send is suppressed (so slash-commands never reach the server).
    /// </summary>
    public static class ChatInterceptor
    {
        // Delegate signature for your command handler.
        // Return true to indicate you consumed the message (and want to block the original send).
        public delegate bool CommandHandler(string message);
        private static readonly List<CommandHandler> _handlers = new List<CommandHandler>(); // All handlers registered by individual mods.
        private static Harmony                       _harmony;                               // The Harmony instance we use to patch BroadcastTextMessage.Send.

        /// <summary>
        /// Installs the chat interceptor exactly once per AppDomain.
        /// Sets up a Harmony prefix on BroadcastTextMessage.Send(LocalNetworkGamer, string).
        /// </summary>
        /// <param name="handler">
        /// A function that takes the stripped chat string and returns true to consume it.
        /// </param>
        public static void Install()
        {
            // Idempotence guard: Don't double-patch.
            if (_harmony != null)
                return; // Already installed.
                                                                                                                       // Unique names prevent overpatching.
            _harmony = new Harmony($"castleminerz.mods.{MethodBase.GetCurrentMethod().DeclaringType.Namespace}.chat"); // We use '.DeclaringType.Namespace' to make the patch name unique per project.

            // Locate the exact overload:
            //   public static void BroadcastTextMessage.Send(LocalNetworkGamer from, string message).
            var original = AccessTools.Method(typeof(BroadcastTextMessage), "Send",
                new Type[] { typeof(LocalNetworkGamer), typeof(string) });

            if (original == null)
            {
                LogSystem.Log("[ChatInt] Failed to find BroadcastTextMessage.Send method.");
                return;
            }

            // Prepare our private static Prefix method as a HarmonyMethod.
            var prefix = new HarmonyMethod(typeof(ChatInterceptor).GetMethod(nameof(Prefix),
                BindingFlags.Static | BindingFlags.NonPublic));

            // Apply the patch.
            _harmony.Patch(original, prefix: prefix);
            LogSystem.Log("[ChatInt] Patched BroadcastTextMessage.Send.");
        }

        /// <summary>
        /// Registers one mod's command handler.
        /// Call this in your plugin's Start(), after Install().
        /// </summary>
        public static void RegisterHandler(CommandHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _handlers.Add(handler);
        }

        /// <summary>
        /// Harmony Prefix on BroadcastTextMessage.Send(...).
        /// Runs before the real Send; Fans the raw chat string out to all registered handlers.
        /// If any returns true, the broadcast is suppressed. Otherwise we print
        /// a single "Unknown command." and still suppress it.
        /// </summary>
        /// <param name="message">
        /// The chat text parameter, passed by ref so you could even modify it if desired.
        /// </param>
        /// <returns>
        /// False to suppress the real BroadcastTextMessage.Send; true to let it proceed.
        /// </returns>
        static bool Prefix(ref string message)
        {
            try
            {
                // If empty or whitespace, just let the normal send happen.
                if (string.IsNullOrWhiteSpace(message))
                    return true;

                // Strip off the "PlayerName: " prefix, if present.
                string content = message;
                int colon = content.IndexOf(": ");
                if (colon != -1 && colon + 2 < content.Length)
                    content = content.Substring(colon + 2); // Replaces range operator.

                // Only intercept slash-commands.
                if (!content.StartsWith("/"))
                    return true;

                // Offer the content to each registered handler.
                foreach (var h in _handlers)
                {
                    try
                    {
                        if (h(content))
                        {
                            // One handler consumed it -> suppress broadcast.
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handler threw-log and continue to next.
                        LogSystem.Log($"[ChatInt] Handler exception: {ex}.");
                    }
                }

                // Unknown command -> give user feedback, then suppress the chat send.
                LogSystem.SendFeedback("Unknown command.");
                return false;
            }
            catch (Exception ex)
            {
                // Log any errors in our prefix so we don't break chat.
                LogSystem.Log($"[ChatInt] Prefix exception: {ex}.");
                return true; // On error, don't interrupt normal chat.

            }
        }
    }
}

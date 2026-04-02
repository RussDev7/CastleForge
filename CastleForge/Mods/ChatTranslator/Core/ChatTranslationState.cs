/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using DNA.Net;
using System;

using static ModLoader.LogSystem;

namespace ChatTranslator
{
    /// <summary>
    /// Runtime state + tiny sent-message cache for ChatTranslator:
    /// - Tracks baseline and remote languages.
    /// - Supports MANUAL mode (fixed remote language).
    /// - Supports AUTO mode (auto-detect incoming and reply in last detected).
    /// - Rewrites outgoing BroadcastTextMessage.Send.
    /// - Rewrites incoming BroadcastTextMessage payloads for local display
    ///   with tags like "[EN->ES]" and "[ES->EN]".
    /// </summary>
    internal static class ChatTranslationState
    {
        #region State & Config

        /// <summary>
        /// Global lock protecting all shared translation state in this class.
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// True while auto-translate mode is enabled (detect incoming, reply in last detected).
        /// </summary>
        private static bool _autoMode = false;

        /// <summary>
        /// Last language code detected from other players while in auto mode (e.g. "es").
        /// </summary>
        private static string _lastDetectedLanguage = null;

        /// <summary>
        /// The player's baseline language (what they type/read in), e.g. "en".
        /// </summary>
        public static string BaseLanguage { get; private set; } = "en";

        /// <summary>
        /// The manually selected remote language in manual mode, e.g. "es" or "de".
        /// Null when manual translation is off.
        /// </summary>
        public static string RemoteLanguage { get; private set; } = null;

        /// <summary>
        /// True if auto-translate mode is currently enabled.
        /// </summary>
        public static bool AutoMode
        {
            get
            {
                lock (_lock)
                {
                    return _autoMode;
                }
            }
        }

        /// <summary>
        /// Last language code detected from incoming messages while in auto mode.
        /// Used as the reply language.
        /// </summary>
        public static string LastDetectedLanguage
        {
            get
            {
                lock (_lock)
                {
                    return _lastDetectedLanguage;
                }
            }
        }

        /// <summary>
        /// True if any translation mode is currently active (auto or manual).
        /// </summary>
        public static bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    if (_autoMode)
                        return true;

                    return !string.IsNullOrEmpty(RemoteLanguage) &&
                           !string.Equals(RemoteLanguage, BaseLanguage, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        #endregion

        #region Sent-Message Cache (For Fixing Up Our Own Messages Locally)

        /// <summary>
        /// Tracks one outgoing translated chat message so that when the translated
        /// text comes back over the network, we can recover the original baseline
        /// text and the language it was sent in.
        /// </summary>
        private class SentMessage
        {
            public string   SenderGamertag;
            public string   OriginalText;
            public string   TranslatedText;
            public string   RemoteLanguage;
            public DateTime TimeUtc;
        }

        /// <summary>
        /// Small rolling cache of recently sent translated messages.
        /// </summary>
        private static readonly System.Collections.Generic.List<SentMessage> _recentSent =
            new System.Collections.Generic.List<SentMessage>();

        /// <summary>
        /// Maximum number of entries to keep in the sent-message cache.
        /// Oldest entries are dropped once this limit is exceeded.
        /// </summary>
        private const int MaxSentCache = 32;

        #endregion

        #region Main-Thread Work Queue (Non-Blocking Outgoing Sends)

        /// <summary>
        /// Small cross-thread work queue:
        /// - Heavy / slow work (HTTP translate) happens off-thread.
        /// - Final game-thread sensitive actions (network send) are marshaled back here.
        ///
        /// Why this exists:
        /// - BroadcastTextMessage.Send is invoked on the game thread.
        /// - Blocking that thread (even for a few ms) causes visible hitches.
        /// </summary>
        private static readonly ConcurrentQueue<Action> _mainThreadWork = new ConcurrentQueue<Action>();

        // Cheap counter so PumpMainThreadWork() can bail without touching the queue.
        private static int _mainThreadWorkCount;

        /// <summary>
        /// Enqueue an action to be executed on the game thread.
        /// The ChatTranslator mod calls PumpMainThreadWork() once per tick.
        /// </summary>
        private static void EnqueueMainThread(Action a)
        {
            if (a == null) return;
            _mainThreadWork.Enqueue(a);
            Interlocked.Increment(ref _mainThreadWorkCount);
        }

        /// <summary>
        /// Execute a small batch of queued game-thread actions. Safe to call every tick.
        /// </summary>
        public static void PumpMainThreadWork(int maxActions = 8)
        {
            // Fast-path: nothing queued.
            if (Volatile.Read(ref _mainThreadWorkCount) <= 0) return;

            for (int i = 0; i < maxActions; i++)
            {
                if (!_mainThreadWork.TryDequeue(out Action a))
                    break;

                Interlocked.Decrement(ref _mainThreadWorkCount);

                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    Log($"Main-thread work item failed: {ex.Message}.");
                }
            }
        }

        // ---------------------------------------------------------------------------------
        // Outgoing send re-entry guard
        // ---------------------------------------------------------------------------------
        //
        // When we defer an outgoing send (translate off-thread, then call Send() later),
        // that deferred Send() will hit our Harmony patch again. We need a tiny bypass
        // so the deferred send can pass through untouched without re-queuing itself.
        //
        // This is thread-static because Send() is called from the game thread.

        [ThreadStatic]
        private static int _outgoingBypassDepth;

        private static bool IsOutgoingBypassActive => _outgoingBypassDepth > 0;

        private sealed class OutgoingBypassScope : IDisposable
        {
            public OutgoingBypassScope() { _outgoingBypassDepth++; }
            public void Dispose() { _outgoingBypassDepth--; }
        }

        // ---------------------------------------------------------------------------------
        // Incoming re-entry guard + original method invoker
        // ---------------------------------------------------------------------------------
        //
        // For incoming chat translation we sometimes suppress the original processing, translate
        // off-thread, then invoke CastleMinerZGame._processBroadcastTextMessage ourselves later.
        // That invoke will hit our Harmony prefix again, so we need a bypass scope just like outgoing.

        [ThreadStatic]
        private static int _incomingBypassDepth;

        private static bool IsIncomingBypassActive => _incomingBypassDepth > 0;

        private sealed class IncomingBypassScope : IDisposable
        {
            public IncomingBypassScope() { _incomingBypassDepth++; }
            public void Dispose() { _incomingBypassDepth--; }
        }

        // Cached MethodInfo for the private chat processing method (used for deferred display).
        private static readonly MethodInfo _miProcessBroadcastTextMessage =
            typeof(CastleMinerZGame).GetMethod("_processBroadcastTextMessage", BindingFlags.Instance | BindingFlags.NonPublic);

        #endregion

        #region Config & Mode Control

        /// <summary>
        /// Loads ChatTranslator config from disk, normalizes the baseline and
        /// default remote languages, and resets auto-mode state.
        /// Falls back to sane defaults if anything fails.
        /// </summary>
        public static void LoadConfig()
        {
            lock (_lock)
            {
                try
                {
                    BaseLanguage = NormalizeLanguageCode(CTRuntimeConfig.BaseLanguage);

                    if (!string.IsNullOrWhiteSpace(CTRuntimeConfig.DefaultRemoteLanguage))
                    {
                        RemoteLanguage = NormalizeLanguageCode(CTRuntimeConfig.DefaultRemoteLanguage);

                        if (string.Equals(BaseLanguage, RemoteLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            RemoteLanguage = null;
                        }
                    }

                    // Auto-mode defaults to OFF on startup; you'll enable it with /t.
                    _autoMode = false;
                    _lastDetectedLanguage = null;
                }
                catch (Exception ex)
                {
                    Log($"Failed to load config: {ex.Message}.");
                    BaseLanguage          = "en";
                    RemoteLanguage        = null;
                    _autoMode             = false;
                    _lastDetectedLanguage = null;
                }
            }
        }

        /// <summary>
        /// Persists the current baseline and manual remote language to disk.
        /// Must be called while holding <see cref="_lock"/>.
        /// </summary>
        private static void SaveConfig_NoLock()
        {
            try
            {
                CTConfig.Save();
            }
            catch (Exception ex)
            {
                Log($"Failed to save config: {ex.Message}.");
            }
        }

        /// <summary>
        /// Sets the baseline language (what you type/read in), normalizes the code,
        /// clears the manual remote language if it matches the new baseline,
        /// and writes the change back to the config file.
        /// </summary>
        public static void SetBaseLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
            {
                lang = "en";
            }

            lock (_lock)
            {
                BaseLanguage = NormalizeLanguageCode(lang);

                // If they made base == remote, just disable manual translation.
                if (!string.IsNullOrEmpty(RemoteLanguage) &&
                    string.Equals(BaseLanguage, RemoteLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    RemoteLanguage = null;
                }

                SaveConfig_NoLock();
            }
        }

        /// <summary>
        /// Toggle remote language (MANUAL mode). Returns true if manual translation
        /// is ON after the toggle, false if it's OFF. Also disables AUTO mode.
        /// </summary>
        public static bool ToggleRemoteLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
            {
                return false;
            }

            lang = NormalizeLanguageCode(lang);

            lock (_lock)
            {
                // Switching remote language puts us in MANUAL mode.
                _autoMode = false;

                if (string.Equals(RemoteLanguage, lang, StringComparison.OrdinalIgnoreCase))
                {
                    // Same language again -> toggle OFF.
                    RemoteLanguage = null;
                    SaveConfig_NoLock();
                    return false;
                }

                RemoteLanguage = lang;

                if (string.Equals(BaseLanguage, RemoteLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    // Degenerate case: Turning on with same as base just disables.
                    RemoteLanguage = null;
                    SaveConfig_NoLock();
                    return false;
                }

                SaveConfig_NoLock();
                return true;
            }
        }

        /// <summary>
        /// Toggles AUTO mode. When ON:
        /// - Incoming messages are translated with sl=auto -> BaseLanguage.
        /// - The last detected language from OTHER players is saved.
        /// - Outgoing messages (from you) are translated to that last detected language.
        /// Manual mode remains configured but is ignored while auto mode is ON.
        /// </summary>
        public static bool ToggleAutoMode()
        {
            lock (_lock)
            {
                _autoMode = !_autoMode;

                if (_autoMode)
                {
                    // When enabling auto mode, we keep any manual RemoteLanguage
                    // as a "remembered" setting but don't use it until auto is off.
                    // LastDetectedLanguage starts null and will be filled from
                    // the first remote message we see.
                    _lastDetectedLanguage = null;
                }

                return _autoMode;
            }
        }

        /// <summary>
        /// Turns OFF manual translation only:
        /// - Clears RemoteLanguage.
        /// - Leaves AutoMode, BaseLanguage, and LastDetectedLanguage alone.
        /// This means:
        ///   • If AutoMode is false, translation is effectively off.
        ///   • If AutoMode is true, auto mode continues working.
        /// </summary>
        public static void DisableManualTranslation()
        {
            lock (_lock)
            {
                RemoteLanguage = null;
                SaveConfig_NoLock();
            }
        }

        /// <summary>
        /// Clears the last auto-detected language without changing auto/manual mode.
        /// The next incoming message in AUTO mode will pick a new language to reply in.
        /// </summary>
        public static void ResetDetectedLanguage()
        {
            lock (_lock)
            {
                _lastDetectedLanguage = null;
            }
        }

        /// <summary>
        /// Clears the manually selected remote language so that manual
        /// translation is turned off, while leaving the baseline and
        /// auto-translate settings unchanged.
        /// </summary>
        public static void ResetRemoteLanguage()
        {
            lock (_lock)
            {
                // Clear the manual remote language (MANUAL mode off).
                RemoteLanguage = null;

                // Persist it so the next launch also has manual translation off.
                SaveConfig_NoLock();
            }
        }

        /// <summary>
        /// Completely disables translation:
        /// - Turns AUTO mode OFF.
        /// - Clears manual RemoteLanguage.
        /// - Clears last detected language.
        /// Baseline language is left alone.
        /// </summary>
        public static void DisableAllTranslation()
        {
            lock (_lock)
            {
                _autoMode = false;
                RemoteLanguage = null;
                _lastDetectedLanguage = null;

                // Persist that manual translation is off.
                SaveConfig_NoLock();
            }

            // Also stop logging.
            CTTranslationLogger.Disable();
        }
        #endregion

        #region Language Helpers

        /// <summary>
        /// Normalizes a language token into something suitable for Google
        /// Translate: Primarily ISO codes (en, es, ru, zh, de, etc.).
        /// </summary>
        public static string NormalizeLanguageCode(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
            {
                return "en";
            }

            lang = lang.Trim();
            string lower = lang.ToLowerInvariant();

            switch (lower)
            {
                case "english":
                case "en-us":
                case "en-gb":
                case "en_uk":
                case "en_au":
                    return "en";

                case "spanish":
                case "es-es":
                case "es-mx":
                case "es_la":
                    return "es";

                case "russian":
                case "ru-ru":
                    return "ru";

                case "chinese":
                case "zh-cn":
                case "zh-hans":
                case "zh-hant":
                case "zh-tw":
                    return "zh";

                case "german":
                case "de-de":
                case "de_at":
                case "de_ch":
                    return "de";

                default:
                    // For anything else (fr, it, etc.) just return the lower-case code.
                    return lower;
            }
        }

        /// <summary>
        /// Splits a chat line of the form "Sender: Message" into its sender and text parts.
        /// Returns true on success; false if the format is invalid.
        /// </summary>
        private static bool TrySplitSenderAndText(string message, out string sender, out string text)
        {
            sender = null;
            text = null;

            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            int idx = message.IndexOf(": ", StringComparison.Ordinal);
            if (idx <= 0 || idx + 2 >= message.Length)
            {
                return false;
            }

            sender = message.Substring(0, idx);
            text = message.Substring(idx + 2);
            return true;
        }
        #endregion

        #region Sent-Message Cache Helpers

        /// <summary>
        /// Stores a sent message in a small cache so we can later recover the
        /// original baseline text and remote language from the translated text.
        /// </summary>
        private static void RecordSent(string senderGamertag, string originalText, string translatedText, string remoteLanguage)
        {
            if (string.IsNullOrEmpty(senderGamertag) ||
                string.IsNullOrEmpty(originalText) ||
                string.IsNullOrEmpty(translatedText))
            {
                return;
            }

            lock (_lock)
            {
                _recentSent.Add(new SentMessage
                {
                    SenderGamertag = senderGamertag,
                    OriginalText   = originalText,
                    TranslatedText = translatedText,
                    RemoteLanguage = NormalizeLanguageCode(remoteLanguage),
                    TimeUtc        = DateTime.UtcNow
                });

                if (_recentSent.Count > MaxSentCache)
                {
                    int removeCount = _recentSent.Count - MaxSentCache;
                    _recentSent.RemoveRange(0, removeCount);
                }
            }
        }

        /// <summary>
        /// Looks up the original baseline text and remote language for a given
        /// sender + translated text pair from the sent-message cache.
        /// </summary>
        private static bool TryFindOriginalFor(string senderGamertag, string translatedText, out string originalText, out string remoteLanguage)
        {
            originalText = null;
            remoteLanguage = null;

            if (string.IsNullOrEmpty(senderGamertag) || string.IsNullOrEmpty(translatedText))
            {
                return false;
            }

            lock (_lock)
            {
                for (int i = _recentSent.Count - 1; i >= 0; i--)
                {
                    SentMessage sm = _recentSent[i];
                    if (string.Equals(sm.SenderGamertag, senderGamertag, StringComparison.Ordinal) &&
                        string.Equals(sm.TranslatedText, translatedText, StringComparison.Ordinal))
                    {
                        originalText = sm.OriginalText;
                        remoteLanguage = sm.RemoteLanguage;
                        return true;
                    }
                }
            }

            return false;
        }
        #endregion

        #region Outgoing / Incoming Hooks

        #region Outgoing

        /// <summary>
        /// Called from the Harmony patch on BroadcastTextMessage.Send.
        /// MANUAL: Rewrites the chat payload to be translated from BaseLanguage ->
        ///         RemoteLanguage.
        /// AUTO  : Rewrites the chat payload from BaseLanguage ->
        ///         LastDetectedLanguage (if any).
        /// Also caches the original so we can reconstruct "[EN->ES] Original" locally.
        ///
        /// Hitch-free outgoing translation:
        /// - If translation is OFF, allow the original Send() to proceed.
        /// - If translation is ON, *suppress* the original Send() and translate off-thread.
        /// - When the translation completes, we marshal back to the game thread and Send() the
        ///   translated payload (with a bypass guard so we don't re-queue ourselves).
        /// </summary>
        public static bool OnOutgoingSendPrefix(LocalNetworkGamer from, ref string message)
        {
            // Allow the deferred send to pass through untouched.
            if (IsOutgoingBypassActive)
                return true;

            if (from == null)
                return true;

            string baseLang;
            string manualRemote;
            bool   autoMode;
            string lastDetected;

            lock (_lock)
            {
                baseLang     = BaseLanguage;
                manualRemote = RemoteLanguage;
                autoMode     = _autoMode;
                lastDetected = _lastDetectedLanguage;
            }

            string targetLang = autoMode ? lastDetected : manualRemote;

            if (string.IsNullOrEmpty(targetLang) ||
                string.Equals(baseLang, targetLang, StringComparison.OrdinalIgnoreCase))
            {
                // Translation effectively OFF for outgoing.
                return true;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            if (!TrySplitSenderAndText(message, out string sender, out string text))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string trimmed = text.TrimStart();
            if (trimmed.Length > 0 && trimmed[0] == '/')
            {
                // Probably a command. Commands are handled by ChatInterceptor and should
                // not be translated or altered.
                return true;
            }

            // Defer translation + send to avoid hitching the game thread.
            QueueOutgoingTranslatedSend(from, sender, text, baseLang, targetLang);

            // Suppress original Send() call (we'll re-send after translation completes).
            return false;
        }

        /// <summary>
        /// Background translate + main-thread send for outgoing chat.
        /// </summary>
        private static void QueueOutgoingTranslatedSend(LocalNetworkGamer from, string sender, string originalText, string baseLang, string targetLang)
        {
            // Snapshot fields we may need (avoid touching live state off-thread).
            string gamerTag = from?.Gamertag;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string translated = null;

                try
                {
                    translated = TranslationService.Translate(originalText, baseLang, targetLang);
                }
                catch (Exception ex)
                {
                    Log($"Outgoing translation failed (async): {ex.Message}.");
                    translated = null;
                }

                if (string.IsNullOrEmpty(translated))
                    translated = originalText;

                // Marshal final send back to the game thread.
                EnqueueMainThread(() =>
                {
                    try
                    {
                        // Cache the pair so when this message bounces back locally we can restore
                        // the original baseline text and show the correct [EN->XX] tag.
                        RecordSent(gamerTag, originalText, translated, targetLang);

                        using (new OutgoingBypassScope())
                        {
                            // Send translated payload to everyone else.
                            BroadcastTextMessage.Send(from, sender + ": " + translated);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Outgoing async send failed: {ex.Message}.");
                    }
                });
            });
        }

        #region One-Shot Send (/send)

        /// <summary>
        /// Sends a single message in a specific language without changing
        /// AUTO (/t) or MANUAL (/l) translation state.
        ///
        /// Design:
        /// - Translate from BaseLanguage -> targetLanguage (best-effort).
        /// - Record original + translated so our incoming self hook can show:
        ///     [BASE->TARGET] OriginalText
        /// - Bypass the outgoing translation hook for the final Send() so it
        ///   won't be re-translated into the current remote language.
        /// </summary>
        public static void SendOneShotMessage(string targetLanguage, string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText))
                return;

            CastleMinerZGame game = CastleMinerZGame.Instance;
            LocalNetworkGamer me = game?.MyNetworkGamer;
            if (me == null)
                return;

            string baseLang;
            lock (_lock) { baseLang = BaseLanguage ?? "en"; }

            string targetLang = NormalizeLanguageCode(targetLanguage);
            string senderTag = me.Gamertag;

            // Fast-path: Same language - no translation needed.
            if (string.IsNullOrEmpty(targetLang) ||
                string.Equals(baseLang, targetLang, StringComparison.OrdinalIgnoreCase))
            {
                EnqueueMainThread(() =>
                {
                    try
                    {
                        RecordSent(senderTag, originalText, originalText, targetLang);
                        using (new OutgoingBypassScope())
                        {
                            BroadcastTextMessage.Send(me, senderTag + ": " + originalText);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"One-shot send failed: {ex.Message}.");
                    }
                });
                return;
            }

            // Off-thread translate, then send on the game thread.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string translated = null;

                try
                {
                    translated = TranslationService.Translate(originalText, baseLang, targetLang);
                }
                catch (Exception ex)
                {
                    Log($"One-shot translation failed (async): {ex.Message}.");
                    translated = null;
                }

                if (string.IsNullOrEmpty(translated))
                    translated = originalText;

                EnqueueMainThread(() =>
                {
                    try
                    {
                        RecordSent(senderTag, originalText, translated, targetLang);

                        using (new OutgoingBypassScope())
                        {
                            // Important: Bypass our normal outgoing translation filter so this message
                            // stays in the explicit target language.
                            BroadcastTextMessage.Send(me, senderTag + ": " + translated);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"One-shot send failed: {ex.Message}.");
                    }
                });
            });
        }
        #endregion

        #endregion

        #region Incoming

        /// <summary>
        /// Called from the Harmony prefix on CastleMinerZGame._processBroadcastTextMessage.
        ///
        /// Hitch-free incoming translation:
        /// - Messages from other players are translated off-thread and displayed once ready.
        /// - We suppress the original display to avoid showing two lines (original + translated).
        ///
        /// Notes:
        /// - If reflection fails (private method not found), we fall back to the legacy blocking path.
        /// - Messages from *self* are fast-path (cache lookup, no HTTP) and are allowed through normally.
        /// </summary>
        public static bool OnIncomingMessagePrefix(CastleMinerZGame game, Message message)
        {
            // Allow deferred display invoke to pass through untouched.
            if (IsIncomingBypassActive)
                return true;

            if (game == null || message == null)
                return true;

            // If we can't re-invoke the private method later, use the legacy blocking flow.
            if (_miProcessBroadcastTextMessage == null)
            {
                OnIncomingMessage(message);
                return true;
            }

            if (!(message is BroadcastTextMessage chat))
                return true;

            // If translation is OFF, let the game handle chat normally.
            if (!IsActive)
                return true;

            string baseLang;
            string manualRemote;
            bool autoMode;
            string lastDetected;

            lock (_lock)
            {
                baseLang = BaseLanguage;
                manualRemote = RemoteLanguage;
                autoMode = _autoMode;
                lastDetected = _lastDetectedLanguage;
            }

            string full = chat.Message;
            if (string.IsNullOrEmpty(full))
                return true;

            if (!TrySplitSenderAndText(full, out string sender, out string text))
                return true;

            // Determine direction (self vs remote).
            bool fromMe = (message.Sender == game.MyNetworkGamer);

            if (fromMe)
            {
                // Self messages should be a cheap cache lookup (we recorded original+translated before sending).
                string gamerTag = game.MyNetworkGamer?.Gamertag;

                string finalText = text;
                string remoteUsed = autoMode ? lastDetected : manualRemote;
                string dirTag = string.Empty;

                if (TryFindOriginalFor(gamerTag, text, out string original, out string remoteCache))
                {
                    finalText = original;
                    remoteUsed = remoteCache;
                }

                if (!string.IsNullOrEmpty(remoteUsed) &&
                    !string.Equals(baseLang, remoteUsed, StringComparison.OrdinalIgnoreCase))
                {
                    dirTag = $"[{baseLang.ToUpperInvariant()}->{remoteUsed.ToUpperInvariant()}] ";
                }

                chat.Message = sender + ": " + dirTag + finalText;

                CTTranslationLogger.LogTranslation(sender, dirTag.Trim(), finalText, text);

                return true;
            }

            // Remote -> Base translation is required.
            // Manual: translate from manualRemote -> baseLang
            // Auto: detect remote and translate -> baseLang

            string effectiveRemote = autoMode ? lastDetected : manualRemote;

            // If we don't know the remote language (auto mode with no detection yet), we still translate-with-detect.
            if (!autoMode && (string.IsNullOrEmpty(effectiveRemote) ||
                string.Equals(baseLang, effectiveRemote, StringComparison.OrdinalIgnoreCase)))
            {
                // Manual mode disabled or already same language; let original display.
                return true;
            }

            // Defer translation + display.
            QueueIncomingTranslatedDisplay(game, message, chat, sender, text, baseLang, manualRemote, autoMode);

            // Suppress original display (we will display translated version later).
            return false;
        }

        /// <summary>
        /// Background translate + deferred display for incoming remote chat.
        /// </summary>
        private static void QueueIncomingTranslatedDisplay(
            CastleMinerZGame game,
            Message originalMsg,
            BroadcastTextMessage chat,
            string sender,
            string originalText,
            string baseLang,
            string manualRemote,
            bool autoMode)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string translated = null;
                string remoteUsed = manualRemote;

                try
                {
                    if (autoMode)
                    {
                        translated = TranslationService.TranslateWithDetection(originalText, baseLang, out string detected);
                        remoteUsed = NormalizeLanguageCode(detected);
                    }
                    else
                    {
                        translated = TranslationService.Translate(originalText, manualRemote, baseLang);
                        remoteUsed = manualRemote;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Incoming translation failed (async): {ex.Message}.");
                    translated = null;
                }

                if (string.IsNullOrEmpty(translated))
                    translated = originalText;

                // Marshal display back to the game thread.
                EnqueueMainThread(() =>
                {
                    try
                    {
                        // Update auto-mode detection result (best-effort).
                        if (autoMode && !string.IsNullOrEmpty(remoteUsed))
                        {
                            lock (_lock) { _lastDetectedLanguage = remoteUsed; }
                        }

                        string dirTag = string.Empty;
                        if (!string.IsNullOrEmpty(remoteUsed) &&
                            !string.Equals(baseLang, remoteUsed, StringComparison.OrdinalIgnoreCase))
                        {
                            dirTag = $"[{remoteUsed.ToUpperInvariant()}->{baseLang.ToUpperInvariant()}] ";
                        }

                        chat.Message = sender + ": " + dirTag + translated;

                        CTTranslationLogger.LogTranslation(sender, dirTag.Trim(), originalText, translated);

                        using (new IncomingBypassScope())
                        {
                            _miProcessBroadcastTextMessage.Invoke(game, new object[] { originalMsg });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Deferred incoming display failed: {ex.Message}.");
                    }
                });
            });
        }

        /// <summary>
        /// Called from the Harmony patch on CastleMinerZGame._processBroadcastTextMessage.
        /// Mutates BroadcastTextMessage.Message before the game prints it, so that:
        /// - MANUAL:
        ///     Remote chat shows as "[ES->EN] Translated..." in your baseline language.
        ///     Your own messages show as "[EN->ES] Original..." in your baseline language.
        /// - AUTO:
        ///     Remote chat uses auto-detection "[DE->EN]" etc.
        ///     Your replies use the last detected language for outgoing and still show
        ///     "[EN->DE] Original..." on your screen.
        /// </summary>
        public static void OnIncomingMessage(Message message)
        {
            string baseLang;
            string manualRemote;
            bool autoMode;
            string lastDetected;

            lock (_lock)
            {
                baseLang = BaseLanguage;
                manualRemote = RemoteLanguage;
                autoMode = _autoMode;
                lastDetected = _lastDetectedLanguage;
            }

            if (!(message is BroadcastTextMessage chat))
            {
                return;
            }

            string full = chat.Message;
            if (string.IsNullOrWhiteSpace(full))
            {
                return;
            }

            if (!TrySplitSenderAndText(full, out string sender, out string text))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                CastleMinerZGame game = CastleMinerZGame.Instance;
                bool fromMe = game != null && message.Sender == game.MyNetworkGamer;

                // If both MANUAL and AUTO are effectively off, bail.
                if (!autoMode &&
                    (string.IsNullOrEmpty(manualRemote) ||
                     string.Equals(baseLang, manualRemote, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                string dirTag;
                string finalText;

                if (fromMe)
                {
                    // Try to recover the exact original baseline text we cached when sending.

                    if (game != null &&
                        TryFindOriginalFor(game.MyNetworkGamer.Gamertag, text, out string original, out string remoteUsed))
                    {
                        // Normal case: We know exactly what we typed + what language we sent it in.
                        finalText = original;

                        string tagRemote = !string.IsNullOrEmpty(remoteUsed)
                            ? NormalizeLanguageCode(remoteUsed).ToUpperInvariant()
                            : (autoMode && !string.IsNullOrEmpty(lastDetected)
                                ? NormalizeLanguageCode(lastDetected).ToUpperInvariant()
                                : (!string.IsNullOrEmpty(manualRemote)
                                    ? NormalizeLanguageCode(manualRemote).ToUpperInvariant()
                                    : "??"));

                        dirTag = "[" + baseLang.ToUpperInvariant() + "->" + tagRemote.ToUpperInvariant() + "] ";
                    }
                    else
                    {
                        // Fallback: DO NOT auto-detect from our own messages.
                        // We only auto-detect from other players.

                        if (autoMode)
                        {
                            // In auto mode, assume the remote language is whatever we last detected
                            // from other players. If we don't have one yet, just show the text as-is.
                            string remote = !string.IsNullOrEmpty(lastDetected)
                                ? NormalizeLanguageCode(lastDetected)
                                : baseLang;

                            if (!string.Equals(remote, baseLang, StringComparison.OrdinalIgnoreCase))
                            {
                                finalText = TranslationService.Translate(text, remote, baseLang);
                                dirTag = "[" + remote.ToUpperInvariant() + "->" + baseLang.ToUpperInvariant() + "] ";
                            }
                            else
                            {
                                // No good guess - just show the raw text.
                                finalText = text;
                                dirTag = "[" + baseLang.ToUpperInvariant() + "->" + baseLang.ToUpperInvariant() + "] ";
                            }
                        }
                        else
                        {
                            // Manual mode fallback: treat as manualRemote -> base.
                            string translated = TranslationService.Translate(text, manualRemote, baseLang);
                            if (string.IsNullOrEmpty(translated))
                            {
                                return;
                            }

                            finalText = translated;
                            dirTag = "[" + manualRemote.ToUpperInvariant() + "->" + baseLang.ToUpperInvariant() + "] ";
                        }
                    }
                }
                else
                {
                    // Message from other player:
                    if (autoMode)
                    {
                        string translated = TranslationService.TranslateWithDetection(text, baseLang, out string detected);
                        if (string.IsNullOrEmpty(translated))
                        {
                            return;
                        }

                        string normDet = !string.IsNullOrEmpty(detected)
                            ? NormalizeLanguageCode(detected)
                            : (!string.IsNullOrEmpty(lastDetected)
                                ? NormalizeLanguageCode(lastDetected)
                                : "auto");

                        // Update last detected language for next outgoing message.
                        lock (_lock)
                        {
                            _lastDetectedLanguage = normDet;
                        }

                        finalText = translated;
                        dirTag = "[" + normDet.ToUpperInvariant() + "->" + baseLang.ToUpperInvariant() + "] ";
                    }
                    else
                    {
                        // Manual remote: Assume remote->base.
                        string translated = TranslationService.Translate(text, manualRemote, baseLang);
                        if (string.IsNullOrEmpty(translated))
                        {
                            return;
                        }

                        finalText = translated;
                        dirTag = "[" + manualRemote.ToUpperInvariant() + "->" + baseLang.ToUpperInvariant() + "] ";
                    }
                }

                // When logging, treat "before -> after" as:
                //   - Others: remote -> baseline (text -> finalText).
                //   - You   : baseline -> remote (finalText -> text).
                if (fromMe)
                {
                    // I typed baseline (finalText), and the translated wire text is 'text'.
                    CTTranslationLogger.LogTranslation(dirTag, sender, finalText, text);
                }
                else
                {
                    // Other player: They typed 'text' in remote, we see 'finalText' in baseline.
                    CTTranslationLogger.LogTranslation(dirTag, sender, text, finalText);
                }

                // Finally, show what the player should see in chat.
                chat.Message = sender + ": " + dirTag + finalText;
            }
            catch (Exception ex)
            {
                Log($"Incoming translation failed: {ex.Message}.");
            }
        }
        #endregion

        #endregion
    }
}

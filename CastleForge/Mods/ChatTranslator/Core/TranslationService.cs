/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Web;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace ChatTranslator
{
    /// <summary>
    /// Small wrapper around the Google Translate endpoint.
    ///
    /// Important design note:
    /// ChatTranslationState already calls this service from a worker thread for normal
    /// incoming/outgoing chat. Do not wrap the HTTP request in another Task and abandon
    /// it on timeout, because the abandoned request can fail later and spam first-chance
    /// System.Net / TlsStream exceptions.
    /// </summary>
    internal static class TranslationService
    {
        #region Settings

        /// <summary>
        /// Max time the worker thread waits for Google to answer.
        /// 1000ms was too aggressive and caused frequent fallback/original-text sends.
        /// </summary>
        public const int TranslationTimeoutMs = 3500;

        /// <summary>
        /// Translation cache size (best-effort). Helps keep repeated chat lines snappy.
        /// </summary>
        public const int TranslationCacheMaxEntries = 512;

        /// <summary>
        /// When using the non-blocking API, we do NOT wait on the game thread.
        /// (Kept as a named knob in case you want to add a tiny wait budget later.)
        /// </summary>
        public const int NonBlockingWaitBudgetMs = 0;

        /// <summary>
        /// Prevents a dead/unreachable endpoint from printing the same warning every chat line.
        /// </summary>
        private const int NetworkWarningThrottleSeconds = 30;

        private static readonly object _networkWarningLock = new object();
        private static DateTime _lastNetworkWarningUtc = DateTime.MinValue;

        #endregion

        #region Construction

        static TranslationService()
        {
            try
            {
                // CastleMiner Z is a .NET Framework game; be explicit so HTTPS works reliably.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                if (ServicePointManager.DefaultConnectionLimit < 8)
                    ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch
            {
                // Best-effort only. Translation will still fall back safely if setup fails.
            }
        }
        #endregion

        #region Public API

        /// <summary>
        /// Simple known-source to target translation. Does NOT auto-detect;
        /// use TranslateWithDetection for that.
        /// </summary>
        public static string Translate(string text, string fromLang, string toLang)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (string.IsNullOrWhiteSpace(fromLang))
                fromLang = "auto";

            if (string.IsNullOrWhiteSpace(toLang))
                toLang = "en";

            try
            {
                string result = DoTranslate(text, fromLang, toLang);
                return string.IsNullOrEmpty(result) ? text : result;
            }
            catch (Exception ex) when (IsExpectedNetworkException(ex))
            {
                LogNetworkWarning("Translate", ex);
                return text;
            }
            catch (Exception ex)
            {
                Log("Translate() failed: " + ex.Message);
                return text;
            }
        }

        /// <summary>
        /// Auto-detects the source language (sl=auto) and translates to targetLang.
        /// Returns the translated text, and outputs the detected source language code.
        /// </summary>
        public static string TranslateWithDetection(string text, string targetLang, out string detectedSourceLang)
        {
            detectedSourceLang = null;

            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (string.IsNullOrWhiteSpace(targetLang))
                targetLang = "en";

            try
            {
                DetectionResult result = DoTranslateWithDetection(text, targetLang);
                detectedSourceLang = result.SourceLanguage;

                return string.IsNullOrEmpty(result.TranslatedText)
                    ? text
                    : result.TranslatedText;
            }
            catch (Exception ex) when (IsExpectedNetworkException(ex))
            {
                LogNetworkWarning("TranslateWithDetection", ex);
                detectedSourceLang = null;
                return text;
            }
            catch (Exception ex)
            {
                Log($"TranslateWithDetection() failed: {ex.Message}.");
                detectedSourceLang = null;
                return text;
            }
        }
        #endregion

        #region Internal HTTP Helpers

        /// <summary>
        /// Simple HTTP GET helper with a hard timeout.
        /// Returns null on expected transport failures so callers can safely fall back.
        /// </summary>
        private static string DownloadStringWithTimeout(string url)
        {
            HttpWebRequest req;

            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = TranslationTimeoutMs;
                req.ReadWriteTimeout = TranslationTimeoutMs;
                req.KeepAlive = false; // Avoid stale pooled TLS connections / disposed TlsStream reuse.
                req.UserAgent = "CastleForge-ChatTranslator/1.0";

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex) when (IsExpectedNetworkException(ex))
            {
                LogNetworkWarning("Translation HTTP", ex);
                return null;
            }
        }

        /// <summary>
        /// Synchronous worker that performs the actual HTTP GET and parsing.
        /// Called from ChatTranslationState's background worker thread during normal chat.
        /// </summary>
        private static string DoTranslate(string text, string fromLang, string toLang)
        {
            string url = string.Format(
                "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}",
                Uri.EscapeDataString(fromLang),
                Uri.EscapeDataString(toLang),
                HttpUtility.UrlEncode(text));

            string result = DownloadStringWithTimeout(url);
            if (string.IsNullOrEmpty(result))
                return text;

            var tokens = ExtractStringTokens(result);
            if (tokens.Count == 0)
                return text;

            string translated = tokens[0];
            return string.IsNullOrEmpty(translated) ? text : translated;
        }

        /// <summary>
        /// Result container for auto-detect translation.
        /// </summary>
        private struct DetectionResult
        {
            public string TranslatedText;
            public string SourceLanguage;
        }

        /// <summary>
        /// Synchronous worker that performs auto-detect + translation.
        /// Called from ChatTranslationState's background worker thread during normal chat.
        /// </summary>
        private static DetectionResult DoTranslateWithDetection(string text, string targetLang)
        {
            string url = string.Format(
                "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={0}&dt=t&q={1}",
                Uri.EscapeDataString(targetLang),
                HttpUtility.UrlEncode(text));

            string result = DownloadStringWithTimeout(url);

            if (string.IsNullOrEmpty(result))
            {
                return new DetectionResult
                {
                    TranslatedText = text,
                    SourceLanguage = null
                };
            }

            var tokens = ExtractStringTokens(result);
            if (tokens.Count == 0)
            {
                return new DetectionResult
                {
                    TranslatedText = text,
                    SourceLanguage = null
                };
            }

            // Heuristic: first token is translated text, last token is detected source language.
            string translated = tokens[0];
            string detected = tokens.Count >= 2 ? tokens[tokens.Count - 1] : null;

            return new DetectionResult
            {
                TranslatedText = string.IsNullOrEmpty(translated) ? text : translated,
                SourceLanguage = detected
            };
        }
        #endregion

        #region Network Failure Handling

        /// <summary>
        /// True for expected transient web/TLS/socket failures from the translation endpoint.
        /// These should fall back to original text instead of looking like mod/game crashes.
        /// </summary>
        private static bool IsExpectedNetworkException(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is WebException)
                return true;

            if (ex is IOException)
                return true;

            if (ex is ObjectDisposedException)
                return true;

            // SocketException lives in System.dll for .NET Framework, but keeping this generic
            // avoids adding another using and still catches nested socket failures.
            if (ex.GetType().FullName == "System.Net.Sockets.SocketException")
                return true;

            return IsExpectedNetworkException(ex.InnerException);
        }

        /// <summary>
        /// Logs one compact network warning at most every few seconds.
        /// </summary>
        private static void LogNetworkWarning(string operation, Exception ex)
        {
            lock (_networkWarningLock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastNetworkWarningUtc).TotalSeconds < NetworkWarningThrottleSeconds)
                    return;

                _lastNetworkWarningUtc = now;
            }

            Log($"ChatTranslator: {operation} timed out or failed; using original text. ({ex.GetType().Name}: {ex.Message})");
        }
        #endregion

        #region JSON Helpers

        /// <summary>
        /// Very small JSON string-token extractor for the translate.googleapis result.
        /// We just walk the response and collect all unescaped "..." sequences.
        /// </summary>
        private static List<string> ExtractStringTokens(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(json))
                return list;

            bool inString = false;
            var sb = new StringBuilder();

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '\\')
                {
                    if (i + 1 < json.Length)
                    {
                        char next = json[++i];
                        if (next == '"' || next == '\\' || next == '/')
                            sb.Append(next);
                        else if (next == 'n')
                            sb.Append('\n');
                        else if (next == 't')
                            sb.Append('\t');
                        else
                            sb.Append(next);
                    }
                    continue;
                }

                if (c == '"')
                {
                    if (inString)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                        inString = false;
                    }
                    else
                    {
                        inString = true;
                    }

                    continue;
                }

                if (inString)
                    sb.Append(c);
            }

            return list;
        }
        #endregion
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Net;
using System.Web;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace ChatTranslator
{
    /// <summary>
    /// Very small wrapper around the Google Translate endpoint.
    /// Uses a worker Task with a timeout, so the calling thread will block
    /// at most TranslationTimeoutMs and then fall back to the original text.
    /// </summary>
    internal static class TranslationService
    {
        #region Settings

        /// <summary>
        /// Max time we'll wait for Google to answer (milliseconds).
        /// If exceeded, we just return the original text.
        /// </summary>
        public const int TranslationTimeoutMs = 1000;

        /// <summary>
        /// Translation cache size (best-effort). Helps keep repeated chat lines snappy.
        /// </summary>
        public const int TranslationCacheMaxEntries = 512;

        /// <summary>
        /// When using the non-blocking API, we do NOT wait on the game thread.
        /// (Kept as a named knob in case you want to add a tiny wait budget later.)
        /// </summary>
        public const int NonBlockingWaitBudgetMs = 0;

        #endregion

        #region Public API

        /// <summary>
        /// Simple "known source -> target" translation. Does NOT auto-detect;
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
                // Run the HTTP call on a background Task and wait with a timeout.
                var task = Task.Run(() =>
                    DoTranslate(text, fromLang, toLang));

                if (!task.Wait(TranslationTimeoutMs))
                {
                    task.ContinueWith(t => { var _ = t.Exception; },
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
                    return text; // Timed out - fall back to original.
                }

                return string.IsNullOrEmpty(task.Result) ? text : task.Result;
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
                var task = System.Threading.Tasks.Task.Run(() =>
                    DoTranslateWithDetection(text, targetLang));

                if (!task.Wait(TranslationTimeoutMs))
                {
                    task.ContinueWith(t => { var _ = t.Exception; },
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
                    return text; // Timed out - fall back to original.
                }

                var result = task.Result;
                detectedSourceLang = result.SourceLanguage;
                return string.IsNullOrEmpty(result.TranslatedText)
                    ? text
                    : result.TranslatedText;
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
        /// Simple HTTP GET helper with a hard timeout (ms) so background requests
        /// can't hang forever and accumulate.
        /// </summary>
        private static string DownloadStringWithTimeout(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = TranslationTimeoutMs;
            req.ReadWriteTimeout = TranslationTimeoutMs;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Synchronous worker that performs the actual HTTP GET and parsing.
        /// Called from a background Task.
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
        /// Called from a background Task.
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

        /// <summary>
        /// Very small JSON "string token" extractor for the translate.googleapis result.
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
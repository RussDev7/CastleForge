/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace ChatTranslator
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class ChatTranslator : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public ChatTranslator() : base("ChatTranslator", new Version("0.0.1"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        /// <summary>
        /// Called once when the mod is first loaded by the ModLoader.
        /// Good place to:
        /// 1) Verify the game is running.
        /// 2) Install any Harmony patches or interceptors.
        /// 3) Register your command handlers.
        /// </summary>
        public override void Start()
        {
            // Acquire game and world references.
            var game = CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(ChatTranslator).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            CTConfig.LoadApply();

            // Register this plugin's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            // Also register this plugin's command list to the global help registry.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded.");
        }

        /// <summary>
        /// Called when the game exits or mod is unloaded.
        /// Used to safely dispose patches and resources.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                try { GamePatches.DisableAll(); } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}."); } // Unpatch Harmony.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { Log($"Error shutting down mod: {ex}."); }
        }

        /// <summary>
        /// Called once per game tick.
        /// Pump queued main-thread work (non-blocking translations).
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            // Keep the hot path light: a small, capped drain each tick.
            // This prevents background translation completions from touching the game thread directly.
            ChatTranslationState.PumpMainThreadWork();
        }
        #endregion

        /// <summary>
        /// Chat commands for this mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            ("translate", "Toggle auto-translate mode (detect incoming language, reply in last detected)."),
            ("t",         "Toggle auto-translate mode (detect incoming language, reply in last detected)."),
            ("language",  "Toggle manual translation with an arbitrary language code, e.g. /language fr."),
            ("lang",      "Toggle manual translation with an arbitrary language code, e.g. /lang fr."),
            ("l",         "Toggle manual translation with an arbitrary language code, e.g. /l fr."),
            ("es",        "Toggle manual translation using Spanish as the remote language (/es)."),
            ("en",        "Toggle manual translation using English as the remote language (/en)."),
            ("ru",        "Toggle manual translation using Russian as the remote language (/ru)."),
            ("zh",        "Toggle manual translation using Chinese as the remote language (/zh)."),
            ("de",        "Toggle manual translation using German as the remote language (/de)."),
            ("baselang",  "Set your baseline language used for reading chat, e.g. /baselang en."),
            ("bl",        "Set your baseline language used for reading chat, e.g. /bl en."),
            ("tclear",    "Clear the last manual & auto-detected language (next incoming message picks a new one)."),
            ("tc",        "Clear the last manual & auto-detected language (next incoming message picks a new one)."),
            ("toff",      "Turn ALL translation off (auto + manual)."),
            ("tstatus",   "Show ChatTranslator baseline/remote status and mode."),
            ("sl",        "Send one message in an explicit language without changing /t or /l mode (e.g. /sendlang de Hello)."),
            ("s",         "Send one message in the base language without changing /t or /l mode (e.g. /send Hello)."),
            ("ttest",     "Test translation in both directions without sending to chat.")
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /baselang

        /// <summary>
        /// (/baselang or /bl) [languageCode]
        /// Sets your "baseline" language - the language you type in and want to
        /// read chat in (e.g. en, es, ru, zh, de).
        /// </summary>
        [Command("/baselang")]
        [Command("/bl")]
        private static void ExecuteBaseLang(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                SendFeedback("Usage: /baselang <languageCode> (e.g. en, es, ru, zh, de).");
                return;
            }

            try
            {
                string raw        = args[0];
                string normalized = ChatTranslationState.NormalizeLanguageCode(raw);
                ChatTranslationState.SetBaseLanguage(normalized);
                SendFeedback($"ChatTranslator: Caseline language set to '{normalized}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ChatTranslator ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /language + Short Aliases (/es, /en, /ru, /zh, /de)

        /// <summary>
        /// (/language or /lang or /l) [languageCode]
        /// Manual remote-language toggle for ChatTranslator.
        ///
        /// Behavior:
        ///   • /lang <languageCode>
        ///       - Enables manual translation for the given code (and disables auto mode),
        ///         e.g. /lang es, /lang de.
        ///       - Running /lang with the same code again toggles manual translation OFF.
        ///
        ///   • /lang
        ///       - If manual translation is currently ON, turns manual translation OFF
        ///         (auto mode is left unchanged).
        ///       - If manual translation is already OFF, prints the usage hint instead.
        ///
        /// Use ISO language codes such as en, es, ru, zh, de, fr, etc.
        /// </summary>
        [Command("/language")]
        [Command("/lang")]
        [Command("/l")]
        private static void ExecuteLanguage(string[] args)
        {
            // No suffix: "/lang"
            if (args == null || args.Length < 1)
            {
                // If manual is currently ON, turn it OFF.
                if (!string.IsNullOrEmpty(ChatTranslationState.RemoteLanguage))
                {
                    ChatTranslationState.DisableManualTranslation();
                    SendFeedback("ChatTranslator: Manual translation OFF (auto mode unchanged).");
                }
                else
                {
                    // Manual already off: show usage instead.
                    SendFeedback("Usage: /lang <languageCode> (e.g. es, ru, zh, de, fr).");
                }
                return;
            }

            ToggleRemoteLang(args[0]);
        }

        #region Languge Codes

        /// <summary>/es - Quick toggle for Spanish remote language (manual mode).</summary>
        [Command("/es")]
        private static void ExecuteEs()
        {
            ToggleRemoteLang("es");
        }

        /// <summary>/en - Quick toggle for English remote language (manual mode).</summary>
        [Command("/en")]
        private static void ExecuteEn()
        {
            ToggleRemoteLang("en");
        }

        /// <summary>/ru - Quick toggle for Russian remote language (manual mode).</summary>
        [Command("/ru")]
        private static void ExecuteRu()
        {
            ToggleRemoteLang("ru");
        }

        /// <summary>/zh - Quick toggle for Chinese remote language (manual mode).</summary>
        [Command("/zh")]
        private static void ExecuteZh()
        {
            ToggleRemoteLang("zh");
        }

        /// <summary>/de - Quick toggle for German remote language (manual mode).</summary>
        [Command("/de")]
        private static void ExecuteDe()
        {
            ToggleRemoteLang("de");
        }
        #endregion

        /// <summary>
        /// Shared helper for /lang and the short aliases.
        /// </summary>
        private static void ToggleRemoteLang(string raw)
        {
            try
            {
                string normalized = ChatTranslationState.NormalizeLanguageCode(raw);
                bool enabled      = ChatTranslationState.ToggleRemoteLanguage(normalized);

                if (enabled)
                {
                    SendFeedback($"ChatTranslator: Manual translation ON ({ChatTranslationState.BaseLanguage.ToUpperInvariant()} <-> {ChatTranslationState.RemoteLanguage.ToUpperInvariant()}). Auto mode OFF.");
                }
                else
                {
                    SendFeedback("ChatTranslator: Manual translation OFF.");
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ChatTranslator ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /translate (Auto-Translate Toggle)

        /// <summary>
        /// /translate or /t
        /// Toggles auto-translate mode:
        ///   - Incoming messages use Google auto-detect and are translated to your baseline.
        ///   - The last detected language from OTHER players is remembered.
        ///   - Your outgoing messages are translated from baseline -> last detected language.
        /// Manual /lang and /es, /de, etc. turn auto mode OFF again.
        /// </summary>
        [Command("/translate")]
        [Command("/t")]
        private static void ExecuteTranslateToggle()
        {
            bool nowOn = ChatTranslationState.ToggleAutoMode();

            if (nowOn)
            {
                SendFeedback("ChatTranslator: Auto-translate ON. Incoming language will be detected and replies will use the last detected language.");
            }
            else
            {
                SendFeedback("ChatTranslator: Auto-translate OFF. Use /lang or /es, /de, etc. for manual translation.");
            }
        }
        #endregion

        #region /tclear

        /// <summary>
        /// /tclear or /tc
        /// Clears both the last auto-detected language and any manually selected
        /// remote language. This turns manual translation OFF, and if auto mode
        /// is enabled, the next incoming message will establish a new reply language.
        /// </summary>
        [Command("/tclear")]
        [Command("/tc")]
        private static void ExecuteTClear()
        {
            ChatTranslationState.ResetDetectedLanguage();
            ChatTranslationState.ResetRemoteLanguage();
            SendFeedback("ChatTranslator: Manual & auto-detected languages cleared. Next incoming message will pick a new reply language.");
        }
        #endregion

        #region /toff

        /// <summary>
        /// /toff
        /// Turns ALL translation off:
        ///   - AUTO mode OFF
        ///   - Manual remote OFF
        ///   - Detected language cleared
        /// Baseline stays unchanged.
        /// </summary>
        [Command("/toff")]
        private static void ExecuteCtOff()
        {
            ChatTranslationState.DisableAllTranslation();
            SendFeedback("ChatTranslator: All translation disabled (auto + manual).");
        }
        #endregion

        #region /tstatus

        /// <summary>
        /// /tstatus
        /// Prints the current baseline + remote language and whether translation is active,
        /// plus whether we're in AUTO or MANUAL mode.
        /// </summary>
        [Command("/tstatus")]
        private static void ExecuteCtStatus()
        {
            string baseLang = ChatTranslationState.BaseLanguage ?? "en";
            string remote   = ChatTranslationState.RemoteLanguage;
            bool   auto     = ChatTranslationState.AutoMode;
            bool   active   = ChatTranslationState.IsActive;
            string last     = ChatTranslationState.LastDetectedLanguage;

            string baseUp   = baseLang.ToUpperInvariant();
            string remoteUp = string.IsNullOrEmpty(remote) ? "NONE" : remote.ToUpperInvariant();
            string lastUp   = string.IsNullOrEmpty(last)   ? "NONE" : last.ToUpperInvariant();

            string mode;
            if (!active)
            {
                mode = "OFF";
            }
            else if (auto)
            {
                mode = "ON (AUTO - reply in last detected language)";
            }
            else
            {
                mode = $"ON (MANUAL {baseUp}<->{remoteUp})";
            }

            SendFeedback($"ChatTranslator status: {mode}.");
            SendFeedback($"Baseline: {baseUp}, ManualRemote: {remoteUp}, LastDetected: {lastUp}.");
        }
        #endregion

        #region /sendlang

        /// <summary>
        /// /sendlang <languageCode> <message...>
        /// Sends ONE message translated into the requested language without changing
        /// your current /t (AUTO) or /l (MANUAL) translation mode.
        ///
        /// Examples:
        ///   /sendlang de Hello everyone
        ///   /sendlang spanish Good morning
        /// </summary>
        [Command("/sendlang")]
        [Command("/sl")]
        private static void ExecuteSendLang(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                SendFeedback("Usage: /sendlang <languageCode> <message...> (e.g. /sendlang de Hello).");
                return;
            }

            try
            {
                string target = ChatTranslationState.NormalizeLanguageCode(args[0]);
                string text = string.Join(" ", args, 1, args.Length - 1);

                if (string.IsNullOrWhiteSpace(text))
                {
                    SendFeedback("ChatTranslator: Nothing to send.");
                    return;
                }

                ChatTranslationState.SendOneShotMessage(target, text.Trim());
                // SendFeedback($"ChatTranslator: Sent one-off message in '{target}'. Modes unchanged.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ChatTranslator ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /send

        /// <summary>
        /// /send <message...>
        /// Sends ONE message translated into the base language without changing
        /// your current /t (AUTO) or /l (MANUAL) translation mode.
        ///
        /// Examples:
        ///   /send Hello everyone
        ///   /send Good morning
        /// </summary>
        [Command("/send")]
        [Command("/s")]
        private static void ExecuteSend(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                SendFeedback("Usage: /send <message...> (e.g. /send Hello).");
                return;
            }

            try
            {
                string target = ChatTranslationState.NormalizeLanguageCode(CTRuntimeConfig.BaseLanguage);
                string text = string.Join(" ", args);

                if (string.IsNullOrWhiteSpace(text))
                {
                    SendFeedback("ChatTranslator: Nothing to send.");
                    return;
                }

                ChatTranslationState.SendOneShotMessage(target, text.Trim());
                // SendFeedback($"ChatTranslator: Sent one-off message in '{target}'. Modes unchanged.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ChatTranslator ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /ttest

        /// <summary>
        /// /ttest [text...]
        /// Runs a "dry-run" translation using the current baseline/remote language
        /// settings and prints the results locally without sending them to chat.
        /// Uses MANUAL or AUTO mode depending on what is currently active.
        /// </summary>
        [Command("/ttest")]
        private static void ExecuteCtTest(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                SendFeedback("Usage: /ttest <text to translate>");
                return;
            }

            string input    = string.Join(" ", args);
            string baseLang = ChatTranslationState.BaseLanguage ?? "en";
            string remote   = ChatTranslationState.RemoteLanguage;
            bool   auto     = ChatTranslationState.AutoMode;

            if (!auto &&
                (string.IsNullOrEmpty(remote) ||
                 string.Equals(baseLang, remote, StringComparison.OrdinalIgnoreCase)))
            {
                SendFeedback("ChatTranslator: Translation is currently OFF. Use /t for auto or /l <code> for manual.");
                return;
            }

            try
            {
                if (auto)
                {
                    string toBase   = TranslationService.TranslateWithDetection(input, baseLang, out string detected);
                    string normDet  = ChatTranslationState.NormalizeLanguageCode(detected ?? "auto");
                    string fromBase = TranslationService.Translate(input, baseLang, normDet);

                    SendFeedback($"[AUTO TEST {normDet.ToUpperInvariant()}->{baseLang.ToUpperInvariant()}] {toBase}.");
                    SendFeedback($"[AUTO TEST {baseLang.ToUpperInvariant()}->{normDet.ToUpperInvariant()}] {fromBase}.");
                }
                else
                {
                    string toRemote = TranslationService.Translate(input, baseLang, remote);
                    string toBase   = TranslationService.Translate(input, remote, baseLang);

                    SendFeedback($"[MANUAL TEST {baseLang.ToUpperInvariant()}->{remote.ToUpperInvariant()}] {toRemote}.");
                    SendFeedback($"[MANUAL TEST {remote.ToUpperInvariant()}->{baseLang.ToUpperInvariant()}] {toBase}.");
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ChatTranslator test failed: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #endregion
    }
}
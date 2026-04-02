/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using HarmonyLib;
using System;

namespace CastleWallsMk2
{
    #region CMZDialogBridge - Overview & Usage

    /// <summary>
    /// ================================================================================
    ///  CMZDialogBridge
    /// --------------------------------------------------------------------------------
    ///  Pushes a PC-style dialog onto the correct UI stack (FrontEnd vs In-Game) while
    ///  preserving CastleMiner Z's original look (DialogScreenImage, _myriadMed font,
    ///  ButtonFrame) and behavior (OK / Cancel semantics).
    ///
    ///  KEY IDEAS
    ///  • Uses the game's own PCDialogScreen and art assets -> consistent UI.
    ///  • Chooses the in-game UI stack when a world is active; else the front-end.
    ///  • If in-game ScreenGroup is private, it uses reflection to access _uiGroup.
    ///  • Cancel semantics:
    ///      - OptionSelected == -1 means "canceled/dismissed" (ESC/close/cancel).
    ///      - OptionSelected != -1 treated as "confirmed/OK".
    ///
    ///  TYPEABLE PROMPTS
    ///  • Adds PCKeyboardInputScreen-based prompts (1-3 input lines) via ShowTextPrompt*.
    ///  • Uses the same "choose the right stack" logic as ShowConfirm/ShowInfo.
    ///  • Keeps vanilla behavior and visuals (same panel art, font, frame, and dismiss rules).
    ///
    ///  DEPENDENCIES
    ///  • DNA.CastleMinerZ.* types (CastleMinerZGame, PCDialogScreen).
    ///  • DNA.Drawing.UI.ScreenGroup.
    ///  • HarmonyLib AccessTools (reflection helper) already used elsewhere in project.
    ///
    ///  NOTES
    ///  • Button labels use the game's default strings (no custom text here).
    ///  • If FrontEnd is null and in-game group cannot be located, Show* returns false.
    ///  • preferInGame = true tries in-game first; otherwise always uses front-end.
    ///  • PCKeyboardInputScreen (vanilla) always includes Cancel (constructors hard-code it).
    ///  • If you need input validation that "blocks closing" on invalid input, you'll need
    ///    a tiny patch or a reopen loop (see notes under ShowTextPrompt).
    ///
    /// --------------------------------------------------------------------------------
    ///  USAGE EXAMPLES (GENERAL)
    /// --------------------------------------------------------------------------------
    ///
    ///  // 1) Simple informational alert (front-end/title screen)
    ///  CMZDialogBridge.ShowInfo(
    ///      title: "Welcome!",
    ///      body:  "Thanks for installing the mod. Check Options -> Controls to bind keys.",
    ///      onOK:  () => { /* nothing to do */ },
    ///      preferInGame: false // Force it onto FrontEnd.
    ///  );
    ///
    ///  // 2) In-game "Are you sure?" with OK/Cancel.
    ///  CMZDialogBridge.ShowConfirm(
    ///      title:        "Quit to Main Menu?",
    ///      body:         "Unsaved progress will be lost.",
    ///      onOK:         () => { /* perform quit */ },
    ///      onCancel:     () => { /* keep playing */ },
    ///      showCancel:   true,
    ///      preferInGame: true
    ///  );
    ///
    ///  // 3) In-game one-button notification (OK only).
    ///  CMZDialogBridge.ShowConfirm(
    ///      title:        "Checkpoint Reached",
    ///      body:         "Your progress has been saved.",
    ///      onOK:         () => { /* toast, sound, etc. */ },
    ///      onCancel:     null,
    ///      showCancel:   false, // <- Single OK button.
    ///      preferInGame: true
    ///  );
    ///
    ///  // 4) Front-end confirmation (e.g., delete save).
    ///  CMZDialogBridge.ShowConfirm(
    ///      title:        "Delete Save?",
    ///      body:         "This action cannot be undone.",
    ///      onOK:         () => { /* delete logic */ },
    ///      onCancel:     () => { /* abort */ },
    ///      showCancel:   true,
    ///      preferInGame: false // Put on FrontEnd stack.
    ///  );
    ///
    ///  // 5) Info that triggers a follow-up async action.
    ///  CMZDialogBridge.ShowInfo(
    ///      title:        "Patch Applied",
    ///      body:         "Restart recommended for best performance.",
    ///      onOK:         () => { /* schedule restart prompt later */ },
    ///      preferInGame: true
    ///  );
    ///
    ///  // 6) Typeable prompt (example: Server Message)
    ///  //    - Pre-fills the current value.
    ///  //    - "Blank means no change".
    ///  //    - TrimResult keeps the stored string tidy.
    ///  CMZDialogBridge.ShowTextPrompt(
    ///      title:           "Server Message",
    ///      description:     "Enter the server message shown to players:",
    ///      defaultText:     CastleMinerZGame.Instance.ServerMessage,
    ///      onOK: (text) =>
    ///      {
    ///          if (!string.IsNullOrWhiteSpace(text))
    ///              CastleMinerZGame.Instance.ServerMessage = text; // already trimmed if trimResult=true
    ///      },
    ///      onCancel:        () => { /* user aborted */ },
    ///      preferInGame:    true,
    ///      trimResult:      true,
    ///      startCursorLine: 1
    ///  );
    ///
    /// NOTE:
    /// • Automatically choose the correct screen stack
    ///   via "preferInGame: (CastleMinerZGame.Instance.CurrentNetworkSession != null)".
    /// • Optionally, the "IsInGame()" helper can be used for preferInGame.
    ///
    /// ================================================================================
    /// </summary>

    #endregion

    /// <summary>
    /// Push PC-style dialogs onto the correct UI stack (front-end or in-game).
    /// Uses original panel art (DialogScreenImage), fonts, and button frame.
    /// </summary>
    internal static class CMZDialogBridge
    {
        #region Public API - OK/Cancel Prompts

        /// <summary>
        /// Show an OK/Cancel dialog. If <paramref name="preferInGame"/> and a game is running,
        /// shows on the in-game stack; otherwise on the title/front-end stack.
        /// </summary>
        /// <param name="title">Dialog window title text.</param>
        /// <param name="body">Dialog body/description text (supports multi-line).</param>
        /// <param name="onOK">Callback invoked when the user confirms (non -1 selection).</param>
        /// <param name="onCancel">Callback invoked when the user cancels/dismisses (OptionSelected == -1).</param>
        /// <param name="showCancel">
        /// If true, the dialog exposes a Cancel option; if false, it is a single-button "OK" dialog.
        /// </param>
        /// <param name="preferInGame">
        /// If true and a world is active, pushes onto the in-game UI stack; otherwise falls back to front-end.
        /// </param>
        /// <returns>
        /// True if the dialog was successfully created and pushed onto some ScreenGroup; false if no suitable group existed.
        /// </returns>
        public static bool ShowConfirm(
            string title,
            string body,
            Action onOK         = null,
            Action onCancel     = null,
            bool   showCancel   = true,
            bool   preferInGame = true
        )
        {
            var gm = CastleMinerZGame.Instance;
            if (gm == null) return false;

            // Build a PCDialogScreen that matches the game's look.
            var dlg = new PCDialogScreen(
                title:       title,
                description: body,
                options:     null,                 // Default options (OK/Cancel).
                printCancel: showCancel,           // CanCancel.
                bgImage:     gm.DialogScreenImage, // Panel art.
                font:        gm._myriadMed,        // Dialog font.
                drawBehind:  true,                 // DrawBehind.
                frame:       gm.ButtonFrame        // Button frame skin.
            );
            dlg.UseDefaultValues();

            // NOTE: OptionSelected convention:
            //   -1 == canceled/dismissed (ESC/close), otherwise treated as OK/confirm.
            void CloseThunk()
            {
                if (dlg.OptionSelected == -1) onCancel?.Invoke();
                else onOK?.Invoke();
            }

            // Resolve which UI stack we should present on (in-game preferred).
            if (TryGetTargetGroup(gm, preferInGame, out ScreenGroup group))
            {
                group.ShowPCDialogScreen(dlg, CloseThunk);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Simple informational dialog (OK only).
        /// </summary>
        /// <param name="title">Dialog window title text.</param>
        /// <param name="body">Dialog body/description text.</param>
        /// <param name="onOK">Callback invoked when the user closes the message.</param>
        /// <param name="preferInGame">If true, prefer pushing to the in-game UI stack when available.</param>
        /// <returns>True if the dialog was successfully displayed; otherwise false.</returns>
        public static bool ShowInfo(string title, string body, Action onOK = null, bool showCancel = false, bool preferInGame = true)
            => ShowConfirm(title, body, onOK, null, showCancel, preferInGame);
        #endregion

        #region Public API - Typeable Prompts

        /// <summary>
        /// Show a 1-line typeable prompt (OK/Cancel). The typed text is returned via callbacks.
        ///
        /// This uses the game's built-in <see cref="PCKeyboardInputScreen"/> so it matches
        /// vanilla CMZ UI:
        /// - Same panel art (DialogScreenImage)
        /// - Same font (_myriadMed)
        /// - Same button frame (ButtonFrame)
        ///
        /// BEHAVIOR / SEMANTICS
        /// - The screen is still a "dialog" at the UI stack level (ShowPCDialogScreen).
        /// - Cancel/dismiss is detected via OptionSelected == -1.
        /// - Confirm/OK is anything else (OptionSelected != -1).
        ///
        /// IMPORTANT VANILLA QUIRKS
        /// - PCKeyboardInputScreen constructors hard-code Cancel to be present (printCancel=true).
        ///   So unlike ShowConfirm(showCancel:false), you cannot make it truly "OK only"
        ///   without patching the screen or button list.
        /// - ErrorMessage is purely cosmetic (red line). It does NOT prevent closing.
        ///   If you need validation that blocks OK until input is valid, you have two common patterns:
        ///     (A) Re-open the prompt with ErrorMessage set (simple, but UI flickers).
        ///     (B) Patch PopMe()/OK handler to refuse closing when invalid (cleanest UX).
        ///
        /// </summary>
        /// <param name="title">Dialog title line (top header).</param>
        /// <param name="description">Prompt description/instructions shown above the input field.</param>
        /// <param name="defaultText">Initial text inserted into the input field (null -> empty).</param>
        /// <param name="onOK">
        /// Invoked when the user confirms.
        /// Receives the (optionally trimmed) text. May be empty string if user confirmed without typing.
        /// </param>
        /// <param name="onCancel">
        /// Invoked when the user cancels/dismisses (ESC/Cancel/close).
        /// This is NOT invoked for an empty-string confirm; only true cancel.
        /// </param>
        /// <param name="preferInGame">
        /// If true, attempt to push onto the in-game UI stack when available; otherwise use FrontEnd.
        /// </param>
        /// <param name="trimResult">
        /// If true, the result string is trimmed before being passed to <paramref name="onOK"/>.
        /// Recommended for settings fields and "name" inputs.
        /// </param>
        /// <param name="startCursorLine">
        /// Which input line is active on open.
        /// For 1-line prompts, valid values are typically 1.
        /// (The screen generally clamps/ignores invalid values, but keep it sane.)
        /// </param>
        /// <param name="initialError">
        /// Optional red error line displayed under the input(s).
        /// Useful when re-opening after validation failure.
        /// </param>
        /// <returns>
        /// True if a ScreenGroup was found and the prompt was pushed; false if no suitable UI stack existed.
        /// </returns>
        public static bool ShowTextPrompt(
            string title,
            string description,
            string defaultText,
            Action<string> onOK,
            Action onCancel        = null,
            bool   preferInGame    = true,
            bool   trimResult      = true,
            int    startCursorLine = 1,
            string initialError    = null
        )
        {
            var gm = CastleMinerZGame.Instance;
            if (gm == null) return false;

            // Build a keyboard input screen with the same look/feel as CMZ's own dialogs.
            var prompt = new PCKeyboardInputScreen(
                game:        gm,
                title:       title,
                description: description,
                bgImage:     gm.DialogScreenImage,
                font:        gm._myriadMed,
                drawBehind:  true,
                frame:       gm.ButtonFrame
            )
            {
                // Pre-fill initial text. (Null -> Empty keeps downstream code simpler.)
                DefaultText = defaultText ?? string.Empty
            };

            // Optional red error line. Cosmetic only (does not block OK).
            if (!string.IsNullOrEmpty(initialError))
                prompt.ErrorMessage = initialError;

            // Choose which input line is active when the screen opens.
            // For 1-line prompts, this is typically 1.
            prompt.SetCursor(startCursorLine);

            // NOTE: OptionSelected convention:
            //   -1 == canceled/dismissed (ESC/close), otherwise treated as OK/confirm.
            void CloseThunk()
            {
                if (prompt.OptionSelected == -1)
                {
                    onCancel?.Invoke();
                    return;
                }

                // TextInput is the typed value (may be empty). Normalize null to empty for safety.
                string text = prompt.TextInput ?? string.Empty;

                // Optional cleanup: Most config-backed fields want trimmed strings.
                if (trimResult) text = text.Trim();

                onOK?.Invoke(text);
            }

            // Use our unified stack selector (in-game preferred, else front-end).
            if (TryGetTargetGroup(gm, preferInGame, out ScreenGroup group))
            {
                group.ShowPCDialogScreen(prompt, CloseThunk);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Show a 2-line typeable prompt (OK/Cancel) using <see cref="PCKeyboardInputScreen"/>.
        ///
        /// This is the same flow as <see cref="ShowTextPrompt"/>, except it exposes two independent
        /// input boxes (TextInput + TextInput2) and accepts two description strings.
        ///
        /// Typical use cases:
        /// - "Name" + "Value"
        /// - "Width" + "Height"
        /// - "X" + "Y"
        ///
        /// Notes:
        /// - Cancel semantics are identical: OptionSelected == -1.
        /// - ErrorMessage remains cosmetic only (does not block OK).
        /// - startCursorLine is typically 1 or 2.
        /// </summary>
        /// <param name="title">Dialog title line (top header).</param>
        /// <param name="description1">Line 1 instructions for input #1.</param>
        /// <param name="description2">Line 2 instructions for input #2.</param>
        /// <param name="defaultText1">Initial value for input #1 (null -> empty).</param>
        /// <param name="defaultText2">Initial value for input #2 (null -> empty).</param>
        /// <param name="onOK">Invoked on confirm; receives the (optionally trimmed) input pair.</param>
        /// <param name="onCancel">Invoked on cancel/dismiss (ESC/Cancel/close).</param>
        /// <param name="preferInGame">If true, prefer in-game UI stack when available.</param>
        /// <param name="trimResult">If true, trims both inputs before invoking <paramref name="onOK"/>.</param>
        /// <param name="startCursorLine">Which input is active on open (typically 1 or 2).</param>
        /// <param name="initialError">Optional red error line shown under the inputs.</param>
        /// <returns>True if pushed to a ScreenGroup; otherwise false.</returns>
        public static bool ShowTextPrompt2(
            string title,
            string description1,
            string description2,
            string defaultText1,
            string defaultText2,
            Action<string, string> onOK,
            Action onCancel        = null,
            bool   preferInGame    = true,
            bool   trimResult      = true,
            int    startCursorLine = 1,
            string initialError    = null
        )
        {
            var gm = CastleMinerZGame.Instance;
            if (gm == null) return false;

            var prompt = new PCKeyboardInputScreen(
                game:         gm,
                title:        title,
                description1: description1,
                description2: description2,
                bgImage:      gm.DialogScreenImage,
                font:         gm._myriadMed,
                drawBehind:   true,
                frame:        gm.ButtonFrame
            )
            {
                DefaultText  = defaultText1 ?? string.Empty,
                DefaultText2 = defaultText2 ?? string.Empty
            };

            if (!string.IsNullOrEmpty(initialError))
                prompt.ErrorMessage = initialError;

            prompt.SetCursor(startCursorLine);

            void CloseThunk()
            {
                if (prompt.OptionSelected == -1)
                {
                    onCancel?.Invoke();
                    return;
                }

                string a = prompt.TextInput ?? string.Empty;
                string b = prompt.TextInput2 ?? string.Empty;

                if (trimResult) { a = a.Trim(); b = b.Trim(); }

                onOK?.Invoke(a, b);
            }

            if (TryGetTargetGroup(gm, preferInGame, out ScreenGroup group))
            {
                group.ShowPCDialogScreen(prompt, CloseThunk);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Show a 3-line typeable prompt (OK/Cancel) using <see cref="PCKeyboardInputScreen"/>.
        ///
        /// Same flow as <see cref="ShowTextPrompt"/> and <see cref="ShowTextPrompt2"/>,
        /// but provides three independent text inputs (TextInput/TextInput2/TextInput3).
        ///
        /// Typical use cases:
        /// - 3D coordinates (X/Y/Z)
        /// - RGB input
        /// - "Header / Body / Footer" style prompts
        ///
        /// Notes:
        /// - Cancel semantics are identical: OptionSelected == -1.
        /// - ErrorMessage remains cosmetic only (does not block OK).
        /// - startCursorLine is typically 1..3.
        /// </summary>
        /// <param name="title">Dialog title line (top header).</param>
        /// <param name="description1">Instructions line for input #1.</param>
        /// <param name="description2">Instructions line for input #2.</param>
        /// <param name="description3">Instructions line for input #3.</param>
        /// <param name="defaultText1">Initial value for input #1 (null -> empty).</param>
        /// <param name="defaultText2">Initial value for input #2 (null -> empty).</param>
        /// <param name="defaultText3">Initial value for input #3 (null -> empty).</param>
        /// <param name="onOK">Invoked on confirm; receives the (optionally trimmed) triple.</param>
        /// <param name="onCancel">Invoked on cancel/dismiss (ESC/Cancel/close).</param>
        /// <param name="preferInGame">If true, prefer in-game UI stack when available.</param>
        /// <param name="trimResult">If true, trims all three inputs before invoking <paramref name="onOK"/>.</param>
        /// <param name="startCursorLine">Which input is active on open (typically 1..3).</param>
        /// <param name="initialError">Optional red error line shown under the inputs.</param>
        /// <returns>True if pushed to a ScreenGroup; otherwise false.</returns>
        public static bool ShowTextPrompt3(
            string title,
            string description1,
            string description2,
            string description3,
            string defaultText1,
            string defaultText2,
            string defaultText3,
            Action<string, string, string> onOK,
            Action onCancel        = null,
            bool   preferInGame    = true,
            bool   trimResult      = true,
            int    startCursorLine = 1,
            string initialError    = null
        )
        {
            var gm = CastleMinerZGame.Instance;
            if (gm == null) return false;

            var prompt = new PCKeyboardInputScreen(
                game:         gm,
                title:        title,
                description1: description1,
                description2: description2,
                description3: description3,
                bgImage:      gm.DialogScreenImage,
                font:         gm._myriadMed,
                drawBehind:   true,
                frame:        gm.ButtonFrame
            )
            {
                DefaultText  = defaultText1 ?? string.Empty,
                DefaultText2 = defaultText2 ?? string.Empty,
                DefaultText3 = defaultText3 ?? string.Empty
            };

            if (!string.IsNullOrEmpty(initialError))
                prompt.ErrorMessage = initialError;

            prompt.SetCursor(startCursorLine);

            void CloseThunk()
            {
                if (prompt.OptionSelected == -1)
                {
                    onCancel?.Invoke();
                    return;
                }

                string a = prompt.TextInput  ?? string.Empty;
                string b = prompt.TextInput2 ?? string.Empty;
                string c = prompt.TextInput3 ?? string.Empty;

                if (trimResult) { a = a.Trim(); b = b.Trim(); c = c.Trim(); }

                onOK?.Invoke(a, b, c);
            }

            if (TryGetTargetGroup(gm, preferInGame, out ScreenGroup group))
            {
                group.ShowPCDialogScreen(prompt, CloseThunk);
                return true;
            }

            return false;
        }
        #endregion

        #region Internal Helpers

        /// <summary>
        /// Resolve the <see cref="ScreenGroup"/> we should push dialogs onto.
        ///
        /// Stack selection rules:
        /// 1) If <paramref name="preferInGame"/> and a world/game screen exists:
        ///    - Try to use the in-game ScreenGroup first (GameScreen._uiGroup).
        ///    - This field is private in vanilla, so we fetch it via Harmony's AccessTools.
        /// 2) Otherwise, or if in-game lookup fails:
        ///    - Fall back to FrontEnd._uiGroup (public) for title/front-end UI.
        ///
        /// Why this exists:
        /// - Prevents duplicating reflection logic across every Show* method.
        /// - Keeps "where do we show the dialog?" consistent everywhere.
        /// - Centralizes future changes (e.g., new screen stacks, null-guards, etc.).
        ///
        /// Returns:
        /// - True if we found a usable group.
        /// - False if neither in-game nor front-end groups were available.
        /// </summary>
        /// <param name="gm">The active CastleMinerZGame instance.</param>
        /// <param name="preferInGame">If true, try in-game first when available.</param>
        /// <param name="group">The resolved ScreenGroup to use when successful.</param>
        private static bool TryGetTargetGroup(CastleMinerZGame gm, bool preferInGame, out ScreenGroup group)
        {
            group = null;
            if (gm == null) return false;

            // Prefer in-game UI stack when a world is active.
            if (preferInGame && gm.GameScreen != null)
            {
                // GameScreen._uiGroup is private in vanilla; reflection via AccessTools.
                var f = AccessTools.Field(gm.GameScreen.GetType(), "_uiGroup");
                if (f?.GetValue(gm.GameScreen) is ScreenGroup inGameGroup)
                {
                    group = inGameGroup;
                    return true;
                }
            }

            // Front-end stack (public).
            if (gm.FrontEnd?._uiGroup is ScreenGroup frontEndGroup)
            {
                group = frontEndGroup;
                return true;
            }

            return false;
        }
        #endregion
    }
}
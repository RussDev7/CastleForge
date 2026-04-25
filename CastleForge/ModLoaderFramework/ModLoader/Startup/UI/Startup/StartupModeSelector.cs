/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Windows.Forms;
using System.Threading;

namespace ModLoader
{
    /// <summary>
    /// Resolves the launch mode (with mods / without mods / abort) by:
    /// 1) Reading ModLoader.ini if "Remember" was set previously.
    /// 2) Creating a default ModLoader.ini on first successful launch if one does not exist.
    /// 3) Otherwise showing <see cref="StartupPromptForm"/> on a temporary STA thread.
    ///
    /// Notes:
    /// • Dialogs must run on an STA thread; many hosts are MTA by default.
    /// • 'Abort' is never persisted (user exit should not stick).
    /// • Thread.Join() blocks until the prompt closes (simple & deterministic).
    /// • If no ModLoader.ini exists yet, choosing With Mods / Without Mods once will create it.
    /// • "Remember" only controls whether the prompt is skipped next time.
    /// </summary>
    internal static class StartupModeSelector
    {
        public static LaunchMode ResolveLaunchMode()
        {
            // Load the config once so we can:
            // - Respect remembered choices.
            // - Know whether this is the first launch with no ModLoader.ini yet.
            bool hasConfig = ModLoaderConfig.TryLoad(out var cfg);

            // Fast-path: Respect remembered choice if present.
            if (hasConfig && cfg.Remember)
                return cfg.Mode;

            // Otherwise block on a tiny STA thread that shows the dialog.
            LaunchMode mode = LaunchMode.Abort; // Default if user just hits close ("X").
            bool remember   = false;

            var thread = new Thread(() =>
            {
                // Run 'Application' methods here.
                //
                // Application.EnableVisualStyles();
                // Application.SetCompatibleTextRenderingDefault(false);

                using (var dialog = new StartupPromptForm())
                {
                    dialog.StartPosition = FormStartPosition.CenterScreen;
                    dialog.ShowInTaskbar = true;
                    dialog.TopMost       = true;

                    switch (dialog.ShowDialog())
                    {
                        case DialogResult.Abort:
                            mode = LaunchMode.Abort; // Caller will exit the game.
                            return;

                        case DialogResult.OK:
                            mode = dialog.SelectedMode;
                            remember = dialog.RememberChoice;
                            break;
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA); // WinForms requirement.
            thread.IsBackground = true;                   // Don't block process exit on this thread.
            thread.Start();
            thread.Join();                                // Wait synchronously.

            // Only persist WithMods/WithoutMods; never persist Abort.
            if (mode != LaunchMode.Abort)
            {
                // If the user checked "Always use this option", save the remembered choice.
                if (remember)
                {
                    new ModLoaderConfig
                    {
                        Remember = true,
                        Mode     = mode
                    }.Save();
                }
                // If this is the first successful launch and no ModLoader.ini exists yet,
                // create a default editable config without skipping the prompt next time.
                else if (!hasConfig)
                {
                    new ModLoaderConfig
                    {
                        Remember = false,
                        Mode     = mode
                    }.Save();
                }
            }

            return mode;
        }
    }
}
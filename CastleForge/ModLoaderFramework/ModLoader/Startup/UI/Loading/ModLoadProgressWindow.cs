/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Windows.Forms;
using System.Threading;
using System;

namespace ModLoader
{
    /// <summary>
    /// Thread-safe wrapper around <see cref="ModLoadProgressForm"/> used to display
    /// temporary mod loading progress on a dedicated STA UI thread.
    ///
    /// Responsibilities:
    /// - Creates and owns the progress form on its own WinForms message loop.
    /// - Accepts cross-thread progress updates via <see cref="IProgress{T}"/>.
    /// - Shuts the form down safely when loading is complete.
    /// </summary>
    internal sealed class ModLoadProgressWindow : IProgress<ModLoadProgressInfo>, IDisposable
    {
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        private readonly Thread     _uiThread;
        private ModLoadProgressForm _form;
        private volatile bool       _closed;

        /// <summary>
        /// Starts the dedicated STA UI thread and blocks until the progress window
        /// has been created and shown.
        /// </summary>
        public ModLoadProgressWindow()
        {
            _uiThread = new Thread(UIThreadMain)
            {
                IsBackground = true,
                Name = "ModLoadProgressWindow"
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _ready.Wait();
        }

        /// <summary>
        /// Entry point for the dedicated WinForms UI thread.
        /// Creates the form, signals readiness once shown, and runs the message loop
        /// until the form is closed.
        /// </summary>
        private void UIThreadMain()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new ModLoadProgressForm())
            {
                _form = form;
                form.Shown += (s, e) => _ready.Set();
                Application.Run(form);
            }

            _closed = true;
            _ready.Set();
        }

        /// <summary>
        /// Applies the latest progress snapshot to the loading form.
        /// Safe to call from any thread. No-ops if the window has already closed.
        /// </summary>
        /// <param name="value">Current mod loading progress information.</param>
        public void Report(ModLoadProgressInfo value)
        {
            if (value == null || _closed)
                return;

            Post(() => _form.Apply(value));
        }

        /// <summary>
        /// Marshals work onto the form's UI thread if needed.
        /// Failures are swallowed because progress UI should never break mod loading.
        /// </summary>
        /// <param name="action">UI work to execute on the progress form thread.</param>
        private void Post(Action action)
        {
            try
            {
                var form = _form;
                if (form == null || form.IsDisposed)
                    return;

                if (form.InvokeRequired)
                    form.BeginInvoke(action);
                else
                    action();
            }
            catch
            {
                // Non-fatal: Progress reporting must never interrupt mod loading.
            }
        }

        /// <summary>
        /// Closes the loading window and waits briefly for the UI thread to exit.
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            if (_closed)
                return;

            try
            {
                Post(() => _form.Close());
                _uiThread.Join(1500);
            }
            catch
            {
                // Ignore shutdown failures; window teardown is best-effort only.
            }
            finally
            {
                _closed = true;
                _ready.Dispose();
            }
        }
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Windows.Forms;
using System.Drawing;
using System;

namespace ModLoader
{
    /// <summary>
    /// Minimal designer-lite form that displays temporary mod loading progress.
    /// Used by <see cref="ModLoadProgressWindow"/> while mods are being discovered
    /// and started on the loader thread.
    ///
    /// Window-behavior notes:
    /// - Styled as a passive helper/tool window instead of a normal app window.
    /// - Shown without activation so it does not steal focus from the game.
    /// - Uses tool-window / no-activate extended styles to reduce interference
    ///   with fullscreen startup and taskbar / Alt+Tab behavior.
    /// </summary>
    internal partial class ModLoadProgressForm : Form
    {
        private Label       _phaseLabel;
        private Label       _countLabel;
        private Label       _itemLabel;
        private ProgressBar _progressBar;

        /// <summary>
        /// Native extended style that marks this form as a small helper/tool window
        /// rather than a normal primary application window.
        /// </summary>
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>
        /// Native extended style that prevents this form from activating when shown.
        /// This helps avoid stealing focus or disturbing the game's startup window state.
        /// </summary>
        private const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>
        /// Applies passive helper-window styles before the native HWND is created.
        ///
        /// This reduces the chance of the progress UI interfering with fullscreen
        /// startup or causing Windows to treat it like a competing top-level window.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        /// <summary>
        /// Initializes the temporary mod loading progress form.
        /// </summary>
        public ModLoadProgressForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Applies OS dark title bar styling once the native window handle exists.
        /// Non-fatal on unsupported Windows versions.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                DarkTitleBarHelper.Apply(this.Handle, enabled: true);
            }
            catch
            {
                // Optional visual polish only; never let this break the form.
            }
        }

        /// <summary>
        /// Updates the form with the latest loading progress snapshot.
        /// Handles both indeterminate discovery mode and determinate per-mod loading.
        /// </summary>
        /// <param name="info">Current mod loading progress information.</param>
        public void Apply(ModLoadProgressInfo info)
        {
            _phaseLabel.Text = string.IsNullOrWhiteSpace(info.Phase)
                ? "Loading mods..."
                : info.Phase;

            if (info.Total > 0)
            {
                _countLabel.Text = $"Mods Loaded ({info.Loaded} of {info.Total})";

                if (info.IsIndeterminate)
                {
                    if (_progressBar.Style != ProgressBarStyle.Marquee)
                        _progressBar.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    if (_progressBar.Style != ProgressBarStyle.Continuous)
                        _progressBar.Style = ProgressBarStyle.Continuous;

                    _progressBar.Minimum = 0;
                    _progressBar.Maximum = Math.Max(1, info.Total);
                    _progressBar.Value   = Math.Max(0, Math.Min(info.Processed, info.Total));
                }
            }
            else
            {
                _countLabel.Text = "Discovering mods...";

                if (_progressBar.Style != ProgressBarStyle.Marquee)
                    _progressBar.Style = ProgressBarStyle.Marquee;
            }

            _itemLabel.Text = string.IsNullOrWhiteSpace(info.CurrentItem)
                ? $"Processed: {info.Processed}  Failed: {info.Failed}"
                : $"{info.CurrentItem}\r\nProcessed: {info.Processed}  Failed: {info.Failed}";
        }

        /// <summary>
        /// Minimal hand-written InitializeComponent implementation for the loading form.
        /// Keeps layout self-contained without requiring a separate .Designer.cs file.
        /// </summary>
        private void InitializeComponent()
        {
            _phaseLabel  = new Label();
            _countLabel  = new Label();
            _itemLabel   = new Label();
            _progressBar = new ProgressBar();

            SuspendLayout();

            // _phaseLabel
            _phaseLabel.AutoSize  = true;
            _phaseLabel.Font      = new Font("Arial", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _phaseLabel.ForeColor = Color.White;
            _phaseLabel.Location  = new Point(12, 12);
            _phaseLabel.Name      = "_phaseLabel";
            _phaseLabel.Size      = new Size(115, 18);
            _phaseLabel.Text      = "Loading mods...";

            // _countLabel
            _countLabel.AutoSize  = true;
            _countLabel.Font      = new Font("Arial", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _countLabel.ForeColor = Color.Gainsboro;
            _countLabel.Location  = new Point(12, 40);
            _countLabel.Name      = "_countLabel";
            _countLabel.Size      = new Size(145, 16);
            _countLabel.Text      = "Discovering mods...";

            // _progressBar
            _progressBar.Location = new Point(15, 68);
            _progressBar.Name     = "_progressBar";
            _progressBar.Size     = new Size(404, 18);
            _progressBar.Style    = ProgressBarStyle.Marquee;

            // _itemLabel
            _itemLabel.Font       = new Font("Arial", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _itemLabel.ForeColor  = Color.Silver;
            _itemLabel.Location   = new Point(12, 95);
            _itemLabel.Name       = "_itemLabel";
            _itemLabel.Size       = new Size(407, 42);
            _itemLabel.Text       = "Preparing...";

            // ModLoadProgressForm
            AutoScaleDimensions   = new SizeF(96F, 96F);
            AutoScaleMode         = AutoScaleMode.Dpi;
            BackColor             = Color.FromArgb(37, 40, 46);
            ClientSize            = new Size(434, 170);
            ControlBox            = false;
            FormBorderStyle       = FormBorderStyle.FixedDialog;
            MaximizeBox           = false;
            MinimizeBox           = false;
            ShowInTaskbar         = true;
            StartPosition         = FormStartPosition.CenterScreen;
            Text                  = "CastleMiner Z - Loading Mods";
            TopMost               = true;

            Controls.Add(_phaseLabel);
            Controls.Add(_countLabel);
            Controls.Add(_progressBar);
            Controls.Add(_itemLabel);

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
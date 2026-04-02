/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Windows.Forms;

namespace ModLoader
{
    /// <summary>
    /// Simple modal dialog shown at process start to choose how this session should launch:
    /// - "Launch with mods" (default).
    /// - "Launch game without mods".
    /// Optional: "Remember my option" - persisted by ModLoaderConfig.
    ///
    /// Notes:
    /// • Uses DialogResult.OK (Continue) or DialogResult.Abort (Exit).
    /// • AcceptButton/CancelButton wired so Enter/Esc work naturally.
    /// • FixedDialog size with center screen start for predictability.
    /// </summary>
    internal sealed class StartupPromptForm : Form
    {
        #region Controls (Designer-Lite)

        private GroupBox    LaunchOption_GroupBox;
        private RadioButton WithMods_RadioButton;
        private RadioButton WithoutMods_RadioButton;
        private CheckBox    RememberChoice_CheckBox;
        private Label       Checkbox_Label;
        private Button      Play_Button;
        private Button      Cancel_Button;

        #endregion

        #region Results

        /// <summary>The chosen launch mode; defaults to WithMods if user presses Enter immediately.</summary>
        public LaunchMode SelectedMode => WithoutMods_RadioButton.Checked ? LaunchMode.WithoutMods : LaunchMode.WithMods;

        /// <summary>Whether to persist the choice to ModLoader.ini.</summary>
        public bool RememberChoice     => RememberChoice_CheckBox.Checked;

        #endregion

        public StartupPromptForm()
        {
            InitializeComponent();

            // Set the dark themed titlebar.
            DarkTitleBarHelper.Apply(this.Handle, enabled: true);

            // Quality-of-life: Enter -> Continue, Esc -> Exit.
            AcceptButton = Play_Button;
            CancelButton = Cancel_Button;
        }

        /// <summary>
        /// Minimal InitializeComponent to avoid a .Designer.cs.
        /// Layout can be edited via the design-editor or hand-written.
        /// Layout is simple and stable; no dynamic scaling required.
        /// </summary>
        private void InitializeComponent()
        {
            this.WithMods_RadioButton                    = new System.Windows.Forms.RadioButton();
            this.WithoutMods_RadioButton                 = new System.Windows.Forms.RadioButton();
            this.RememberChoice_CheckBox                 = new System.Windows.Forms.CheckBox();
            this.Checkbox_Label                          = new System.Windows.Forms.Label();
            this.Play_Button                             = new System.Windows.Forms.Button();
            this.Cancel_Button                           = new System.Windows.Forms.Button();
            this.LaunchOption_GroupBox                   = new System.Windows.Forms.GroupBox();
            this.LaunchOption_GroupBox.SuspendLayout();
            this.SuspendLayout();
            //
            // LaunchOption_GroupBox
            //
            this.LaunchOption_GroupBox.Anchor            = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                                                           | System.Windows.Forms.AnchorStyles.Right)));
            this.LaunchOption_GroupBox.Controls.Add(this.WithMods_RadioButton);
            this.LaunchOption_GroupBox.Controls.Add(this.WithoutMods_RadioButton);
            this.LaunchOption_GroupBox.Font              = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.LaunchOption_GroupBox.ForeColor         = System.Drawing.Color.White;
            this.LaunchOption_GroupBox.Location          = new System.Drawing.Point(12, 7);
            this.LaunchOption_GroupBox.Name              = "LaunchOption_GroupBox";
            this.LaunchOption_GroupBox.Size              = new System.Drawing.Size(310, 75);
            this.LaunchOption_GroupBox.TabIndex          = 6;
            this.LaunchOption_GroupBox.TabStop           = false;
            this.LaunchOption_GroupBox.Text              = "Select Launch Option";
            //
            // WithMods_RadioButton
            //
            this.WithMods_RadioButton.AutoSize           = true;
            this.WithMods_RadioButton.Checked            = true;
            this.WithMods_RadioButton.Font               = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.WithMods_RadioButton.Location           = new System.Drawing.Point(24, 20);
            this.WithMods_RadioButton.Name               = "WithMods_RadioButton";
            this.WithMods_RadioButton.Size               = new System.Drawing.Size(135, 20);
            this.WithMods_RadioButton.TabIndex           = 1;
            this.WithMods_RadioButton.TabStop            = true;
            this.WithMods_RadioButton.Text               = "Play CastleMiner Z";
            //
            // WithoutMods_RadioButton
            //
            this.WithoutMods_RadioButton.Anchor          = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.WithoutMods_RadioButton.AutoSize        = true;
            this.WithoutMods_RadioButton.Font            = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.WithoutMods_RadioButton.Location        = new System.Drawing.Point(24, 45);
            this.WithoutMods_RadioButton.Name            = "WithoutMods_RadioButton";
            this.WithoutMods_RadioButton.Size            = new System.Drawing.Size(197, 20);
            this.WithoutMods_RadioButton.TabIndex        = 2;
            this.WithoutMods_RadioButton.TabStop         = true;
            this.WithoutMods_RadioButton.Text            = "Play CastleMiner Z (no mods)";
            //
            // RememberChoice_CheckBox
            //
            this.RememberChoice_CheckBox.AutoSize        = true;
            this.RememberChoice_CheckBox.Font            = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RememberChoice_CheckBox.ForeColor       = System.Drawing.Color.LightGray;
            this.RememberChoice_CheckBox.Location        = new System.Drawing.Point(12, 88);
            this.RememberChoice_CheckBox.Name            = "RememberChoice_CheckBox";
            this.RememberChoice_CheckBox.Size            = new System.Drawing.Size(157, 20);
            this.RememberChoice_CheckBox.TabIndex        = 3;
            this.RememberChoice_CheckBox.Text            = "Always use this option";
            //
            // Checkbox_Label
            //
            this.Checkbox_Label.AutoSize                 = true;
            this.Checkbox_Label.Font                     = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Checkbox_Label.ForeColor                = System.Drawing.Color.Gray;
            this.Checkbox_Label.Location                 = new System.Drawing.Point(9, 110);
            this.Checkbox_Label.Name                     = "Checkbox_Label";
            this.Checkbox_Label.Size                     = new System.Drawing.Size(315, 30);
            this.Checkbox_Label.TabIndex                 = 0;
            this.Checkbox_Label.Text                     = "You can view launch options and edit your selection from\r\n" +
                                                           "the modloader.ini file at your games install directory.";
            //
            // Play_Button
            //
            this.Play_Button.Anchor                      = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.Play_Button.BackColor                   = System.Drawing.Color.FromArgb(((int)(((byte)(26)))), ((int)(((byte)(159)))), ((int)(((byte)(255)))));
            this.Play_Button.DialogResult                = System.Windows.Forms.DialogResult.OK;
            this.Play_Button.FlatAppearance.BorderSize   = 0;
            this.Play_Button.FlatStyle                   = System.Windows.Forms.FlatStyle.Flat;
            this.Play_Button.Font                        = new System.Drawing.Font("Arial", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Play_Button.ForeColor                   = System.Drawing.Color.White;
            this.Play_Button.Location                    = new System.Drawing.Point(156, 151);
            this.Play_Button.Name                        = "Play_Button";
            this.Play_Button.Size                        = new System.Drawing.Size(80, 28);
            this.Play_Button.TabIndex                    = 4;
            this.Play_Button.Text                        = "Play";
            this.Play_Button.UseVisualStyleBackColor     = false;
            //
            // Cancel_Button
            //
            this.Cancel_Button.Anchor                    = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.Cancel_Button.BackColor                 = System.Drawing.Color.FromArgb(((int)(((byte)(61)))), ((int)(((byte)(68)))), ((int)(((byte)(80)))));
            this.Cancel_Button.DialogResult              = System.Windows.Forms.DialogResult.Abort;
            this.Cancel_Button.FlatAppearance.BorderSize = 0;
            this.Cancel_Button.FlatStyle                 = System.Windows.Forms.FlatStyle.Flat;
            this.Cancel_Button.Font                      = new System.Drawing.Font("Arial", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Cancel_Button.ForeColor                 = System.Drawing.Color.White;
            this.Cancel_Button.Location                  = new System.Drawing.Point(242, 151);
            this.Cancel_Button.Name                      = "Cancel_Button";
            this.Cancel_Button.Size                      = new System.Drawing.Size(80, 28);
            this.Cancel_Button.TabIndex                  = 5;
            this.Cancel_Button.Text                      = "Cancel";
            this.Cancel_Button.UseVisualStyleBackColor   = false;
            //
            // StartupPromptForm
            //
            this.AutoScaleDimensions                     = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode                           = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor                               = System.Drawing.Color.FromArgb(((int)(((byte)(37)))), ((int)(((byte)(40)))), ((int)(((byte)(46)))));
            this.ClientSize                              = new System.Drawing.Size(334, 191);
            this.Controls.Add(this.LaunchOption_GroupBox);
            this.Controls.Add(this.RememberChoice_CheckBox);
            this.Controls.Add(this.Checkbox_Label);
            this.Controls.Add(this.Play_Button);
            this.Controls.Add(this.Cancel_Button);
            this.FormBorderStyle                         = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox                             = false;
            this.MinimizeBox                             = false;
            this.Name                                    = "StartupPromptForm";
            this.StartPosition                           = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text                                    = "CastleMiner Z - ModLoader";
            this.LaunchOption_GroupBox.ResumeLayout(false);
            this.LaunchOption_GroupBox.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
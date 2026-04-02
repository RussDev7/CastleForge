namespace WE_ImageToPixelart
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.PickGridColor = new System.Windows.Forms.Button();
            this.SourceImage = new System.Windows.Forms.PictureBox();
            this.ConvertedImage = new System.Windows.Forms.PictureBox();
            this.ShowGrid = new System.Windows.Forms.CheckBox();
            this.GridY = new System.Windows.Forms.NumericUpDown();
            this.label4 = new System.Windows.Forms.Label();
            this.GridOptionsGroupBox = new System.Windows.Forms.GroupBox();
            this.Backdrop = new System.Windows.Forms.CheckBox();
            this.label5 = new System.Windows.Forms.Label();
            this.GridX = new System.Windows.Forms.NumericUpDown();
            this.MainProgressBar = new System.Windows.Forms.ProgressBar();
            this.ColorFilterManagerGroupBox = new System.Windows.Forms.GroupBox();
            this.ResetColors = new System.Windows.Forms.Button();
            this.FilterName = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.SaveColorFilter = new System.Windows.Forms.Button();
            this.LoadColorFilter = new System.Windows.Forms.Button();
            this.label7 = new System.Windows.Forms.Label();
            this.PixelArtStatisticsGroupBox = new System.Windows.Forms.GroupBox();
            this.ShowProgress = new System.Windows.Forms.CheckBox();
            this.GenerateSchematic = new System.Windows.Forms.CheckBox();
            this.SaveDirectory = new System.Windows.Forms.Label();
            this.SaveDirectoryData = new System.Windows.Forms.TextBox();
            this.CurrentImage = new System.Windows.Forms.Label();
            this.CurrentImageData = new System.Windows.Forms.TextBox();
            this.GatherStatistics = new System.Windows.Forms.CheckBox();
            this.TotalBlocksData = new System.Windows.Forms.Label();
            this.TotalWidthData = new System.Windows.Forms.Label();
            this.TotalHeightData = new System.Windows.Forms.Label();
            this.TotalBlocks = new System.Windows.Forms.Label();
            this.TotalHeight = new System.Windows.Forms.Label();
            this.TotalWidth = new System.Windows.Forms.Label();
            this.ColorFilterToolsGroupBox = new System.Windows.Forms.GroupBox();
            this.DeleteColor = new System.Windows.Forms.Button();
            this.DelNullColors = new System.Windows.Forms.Button();
            this.SchematicRotationGroupBox = new System.Windows.Forms.GroupBox();
            this.SchematicModeGroupBox = new System.Windows.Forms.GroupBox();
            this.Flat = new System.Windows.Forms.RadioButton();
            this.Standing = new System.Windows.Forms.RadioButton();
            this.SchematicAxisGroupBox = new System.Windows.Forms.GroupBox();
            this.YAxis = new System.Windows.Forms.RadioButton();
            this.XAxis = new System.Windows.Forms.RadioButton();
            this.Rotation270 = new System.Windows.Forms.RadioButton();
            this.Rotation180 = new System.Windows.Forms.RadioButton();
            this.Rotation90 = new System.Windows.Forms.RadioButton();
            this.Rotation0 = new System.Windows.Forms.RadioButton();
            this.BasicConfigurationGroupBox = new System.Windows.Forms.GroupBox();
            this.OverwriteExistingFile = new System.Windows.Forms.Button();
            this.NewRatioPercentLevelData = new System.Windows.Forms.Label();
            this.CustomColorPicker = new System.Windows.Forms.Button();
            this.NewRatioPercentLevel = new System.Windows.Forms.Label();
            this.RefreshRatio = new System.Windows.Forms.Button();
            this.NewRatioPercentLevelValue = new System.Windows.Forms.NumericUpDown();
            this.SpacingLabel = new System.Windows.Forms.Label();
            this.OpenNewImage = new System.Windows.Forms.Button();
            this.ConvertToPixelArt = new System.Windows.Forms.Button();
            this.SpacingValue = new System.Windows.Forms.NumericUpDown();
            this.SaveImage = new System.Windows.Forms.Button();
            this.SaveSchemToFile = new System.Windows.Forms.Button();
            this.ScalingModeGroupBox = new System.Windows.Forms.GroupBox();
            this.SigmaEquals = new System.Windows.Forms.NumericUpDown();
            this.AEquals = new System.Windows.Forms.NumericUpDown();
            this.Gaussian = new System.Windows.Forms.RadioButton();
            this.Spline = new System.Windows.Forms.RadioButton();
            this.Hermite = new System.Windows.Forms.RadioButton();
            this.Lanczos = new System.Windows.Forms.RadioButton();
            this.Bicubic = new System.Windows.Forms.RadioButton();
            this.NearestNeighbor = new System.Windows.Forms.RadioButton();
            this.Bilinear = new System.Windows.Forms.RadioButton();
            this.ColorFilterSettingsGroupBox = new System.Windows.Forms.GroupBox();
            this.UniqueColors = new System.Windows.Forms.CheckBox();
            this.ColorFilterDataGroupBox = new System.Windows.Forms.GroupBox();
            this.FilteredColorsData = new System.Windows.Forms.Label();
            this.TotalColorsData = new System.Windows.Forms.Label();
            this.FilteredColors = new System.Windows.Forms.Label();
            this.TotalColors = new System.Windows.Forms.Label();
            this.CopyToClipboard = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.SourceImage)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ConvertedImage)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.GridY)).BeginInit();
            this.GridOptionsGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.GridX)).BeginInit();
            this.ColorFilterManagerGroupBox.SuspendLayout();
            this.PixelArtStatisticsGroupBox.SuspendLayout();
            this.ColorFilterToolsGroupBox.SuspendLayout();
            this.SchematicRotationGroupBox.SuspendLayout();
            this.SchematicModeGroupBox.SuspendLayout();
            this.SchematicAxisGroupBox.SuspendLayout();
            this.BasicConfigurationGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NewRatioPercentLevelValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpacingValue)).BeginInit();
            this.ScalingModeGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SigmaEquals)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.AEquals)).BeginInit();
            this.ColorFilterSettingsGroupBox.SuspendLayout();
            this.ColorFilterDataGroupBox.SuspendLayout();
            this.SuspendLayout();
            //
            // PickGridColor
            //
            this.PickGridColor.BackColor = System.Drawing.Color.Red;
            this.PickGridColor.FlatAppearance.BorderSize = 0;
            this.PickGridColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.PickGridColor.Location = new System.Drawing.Point(6, 41);
            this.PickGridColor.Name = "PickGridColor";
            this.PickGridColor.Size = new System.Drawing.Size(157, 26);
            this.PickGridColor.TabIndex = 19;
            this.PickGridColor.Text = "Grid Color";
            this.PickGridColor.UseVisualStyleBackColor = false;
            this.PickGridColor.Click += new System.EventHandler(this.PickGridColor_Click);
            //
            // SourceImage
            //
            this.SourceImage.AllowDrop = true;
            this.SourceImage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)));
            this.SourceImage.BackgroundImage = global::WorldEditPixelart.Properties.Resources.etp;
            this.SourceImage.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.SourceImage.Image = global::WorldEditPixelart.Properties.Resources.globeTest;
            this.SourceImage.InitialImage = global::WorldEditPixelart.Properties.Resources.globeTest;
            this.SourceImage.Location = new System.Drawing.Point(10, 11);
            this.SourceImage.Name = "SourceImage";
            this.SourceImage.Size = new System.Drawing.Size(874, 689);
            this.SourceImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.SourceImage.TabIndex = 2;
            this.SourceImage.TabStop = false;
            this.SourceImage.DragDrop += new System.Windows.Forms.DragEventHandler(this.SourceImage_DragDrop);
            this.SourceImage.DragEnter += new System.Windows.Forms.DragEventHandler(this.SourceImage_DragEnter);
            //
            // ConvertedImage
            //
            this.ConvertedImage.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ConvertedImage.BackgroundImage = global::WorldEditPixelart.Properties.Resources.etp;
            this.ConvertedImage.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.ConvertedImage.Location = new System.Drawing.Point(891, 11);
            this.ConvertedImage.Name = "ConvertedImage";
            this.ConvertedImage.Size = new System.Drawing.Size(874, 689);
            this.ConvertedImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.ConvertedImage.TabIndex = 3;
            this.ConvertedImage.TabStop = false;
            //
            // ShowGrid
            //
            this.ShowGrid.AutoSize = true;
            this.ShowGrid.Location = new System.Drawing.Point(6, 19);
            this.ShowGrid.Name = "ShowGrid";
            this.ShowGrid.Size = new System.Drawing.Size(67, 16);
            this.ShowGrid.TabIndex = 17;
            this.ShowGrid.Text = "Show Grid";
            this.ShowGrid.UseVisualStyleBackColor = true;
            //
            // GridY
            //
            this.GridY.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.GridY.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.GridY.ForeColor = System.Drawing.Color.Snow;
            this.GridY.Location = new System.Drawing.Point(6, 88);
            this.GridY.Name = "GridY";
            this.GridY.Size = new System.Drawing.Size(157, 17);
            this.GridY.TabIndex = 20;
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(3, 72);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(60, 12);
            this.label4.TabIndex = 17;
            this.label4.Text = "Grid Y Offset:";
            //
            // GridOptionsGroupBox
            //
            this.GridOptionsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.GridOptionsGroupBox.Controls.Add(this.Backdrop);
            this.GridOptionsGroupBox.Controls.Add(this.label5);
            this.GridOptionsGroupBox.Controls.Add(this.GridX);
            this.GridOptionsGroupBox.Controls.Add(this.label4);
            this.GridOptionsGroupBox.Controls.Add(this.GridY);
            this.GridOptionsGroupBox.Controls.Add(this.ShowGrid);
            this.GridOptionsGroupBox.Controls.Add(this.PickGridColor);
            this.GridOptionsGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.GridOptionsGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.GridOptionsGroupBox.Location = new System.Drawing.Point(696, 712);
            this.GridOptionsGroupBox.Name = "GridOptionsGroupBox";
            this.GridOptionsGroupBox.Size = new System.Drawing.Size(169, 159);
            this.GridOptionsGroupBox.TabIndex = 19;
            this.GridOptionsGroupBox.TabStop = false;
            this.GridOptionsGroupBox.Text = "Grid Options:";
            //
            // Backdrop
            //
            this.Backdrop.AutoSize = true;
            this.Backdrop.Location = new System.Drawing.Point(88, 19);
            this.Backdrop.Name = "Backdrop";
            this.Backdrop.Size = new System.Drawing.Size(63, 16);
            this.Backdrop.TabIndex = 18;
            this.Backdrop.Text = "Backdrop";
            this.Backdrop.UseVisualStyleBackColor = true;
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(3, 110);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(61, 12);
            this.label5.TabIndex = 19;
            this.label5.Text = "Grid X Offset:";
            //
            // GridX
            //
            this.GridX.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.GridX.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.GridX.ForeColor = System.Drawing.Color.Snow;
            this.GridX.Location = new System.Drawing.Point(6, 127);
            this.GridX.Name = "GridX";
            this.GridX.Size = new System.Drawing.Size(157, 17);
            this.GridX.TabIndex = 21;
            //
            // MainProgressBar
            //
            this.MainProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.MainProgressBar.Location = new System.Drawing.Point(99, 130);
            this.MainProgressBar.Name = "MainProgressBar";
            this.MainProgressBar.Size = new System.Drawing.Size(252, 21);
            this.MainProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.MainProgressBar.TabIndex = 20;
            //
            // ColorFilterManagerGroupBox
            //
            this.ColorFilterManagerGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ColorFilterManagerGroupBox.Controls.Add(this.ResetColors);
            this.ColorFilterManagerGroupBox.Controls.Add(this.FilterName);
            this.ColorFilterManagerGroupBox.Controls.Add(this.label6);
            this.ColorFilterManagerGroupBox.Controls.Add(this.SaveColorFilter);
            this.ColorFilterManagerGroupBox.Controls.Add(this.LoadColorFilter);
            this.ColorFilterManagerGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ColorFilterManagerGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.ColorFilterManagerGroupBox.Location = new System.Drawing.Point(1237, 712);
            this.ColorFilterManagerGroupBox.Name = "ColorFilterManagerGroupBox";
            this.ColorFilterManagerGroupBox.Size = new System.Drawing.Size(165, 159);
            this.ColorFilterManagerGroupBox.TabIndex = 21;
            this.ColorFilterManagerGroupBox.TabStop = false;
            this.ColorFilterManagerGroupBox.Text = "Color Filter Manager";
            //
            // ResetColors
            //
            this.ResetColors.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.ResetColors.FlatAppearance.BorderSize = 0;
            this.ResetColors.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ResetColors.ForeColor = System.Drawing.Color.Snow;
            this.ResetColors.Location = new System.Drawing.Point(6, 78);
            this.ResetColors.Name = "ResetColors";
            this.ResetColors.Size = new System.Drawing.Size(151, 23);
            this.ResetColors.TabIndex = 33;
            this.ResetColors.Text = "Reset Colors";
            this.ResetColors.UseVisualStyleBackColor = false;
            this.ResetColors.Click += new System.EventHandler(this.ResetColors_Click);
            //
            // FilterName
            //
            this.FilterName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.FilterName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.FilterName.ForeColor = System.Drawing.Color.Snow;
            this.FilterName.Location = new System.Drawing.Point(6, 128);
            this.FilterName.Name = "FilterName";
            this.FilterName.ReadOnly = true;
            this.FilterName.Size = new System.Drawing.Size(151, 17);
            this.FilterName.TabIndex = 0;
            this.FilterName.Text = "Default";
            //
            // label6
            //
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(3, 112);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(89, 12);
            this.label6.TabIndex = 22;
            this.label6.Text = "Current Filter Name:";
            //
            // SaveColorFilter
            //
            this.SaveColorFilter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.SaveColorFilter.FlatAppearance.BorderSize = 0;
            this.SaveColorFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.SaveColorFilter.ForeColor = System.Drawing.Color.Snow;
            this.SaveColorFilter.Location = new System.Drawing.Point(6, 49);
            this.SaveColorFilter.Name = "SaveColorFilter";
            this.SaveColorFilter.Size = new System.Drawing.Size(151, 23);
            this.SaveColorFilter.TabIndex = 35;
            this.SaveColorFilter.Text = "Save Color Filter";
            this.SaveColorFilter.UseVisualStyleBackColor = false;
            this.SaveColorFilter.Click += new System.EventHandler(this.SaveColorFilter_Click);
            //
            // LoadColorFilter
            //
            this.LoadColorFilter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.LoadColorFilter.FlatAppearance.BorderSize = 0;
            this.LoadColorFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.LoadColorFilter.ForeColor = System.Drawing.Color.Snow;
            this.LoadColorFilter.Location = new System.Drawing.Point(6, 20);
            this.LoadColorFilter.Name = "LoadColorFilter";
            this.LoadColorFilter.Size = new System.Drawing.Size(151, 23);
            this.LoadColorFilter.TabIndex = 34;
            this.LoadColorFilter.Text = "Load Color Filter";
            this.LoadColorFilter.UseVisualStyleBackColor = false;
            this.LoadColorFilter.Click += new System.EventHandler(this.LoadColorFilter_Click);
            //
            // label7
            //
            this.label7.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label7.AutoSize = true;
            this.label7.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(51)))), ((int)(((byte)(51)))));
            this.label7.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F);
            this.label7.ForeColor = System.Drawing.Color.Snow;
            this.label7.Location = new System.Drawing.Point(6, 131);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(45, 12);
            this.label7.TabIndex = 22;
            this.label7.Text = "Progress:";
            //
            // PixelArtStatisticsGroupBox
            //
            this.PixelArtStatisticsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.PixelArtStatisticsGroupBox.Controls.Add(this.MainProgressBar);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.ShowProgress);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.GenerateSchematic);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.SaveDirectory);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.SaveDirectoryData);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.CurrentImage);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.CurrentImageData);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.GatherStatistics);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.label7);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalBlocksData);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalWidthData);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalHeightData);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalBlocks);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalHeight);
            this.PixelArtStatisticsGroupBox.Controls.Add(this.TotalWidth);
            this.PixelArtStatisticsGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PixelArtStatisticsGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.PixelArtStatisticsGroupBox.Location = new System.Drawing.Point(1408, 712);
            this.PixelArtStatisticsGroupBox.Name = "PixelArtStatisticsGroupBox";
            this.PixelArtStatisticsGroupBox.Size = new System.Drawing.Size(357, 159);
            this.PixelArtStatisticsGroupBox.TabIndex = 23;
            this.PixelArtStatisticsGroupBox.TabStop = false;
            this.PixelArtStatisticsGroupBox.Text = "Pixel Art Statistics:";
            //
            // ShowProgress
            //
            this.ShowProgress.AutoSize = true;
            this.ShowProgress.Checked = true;
            this.ShowProgress.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ShowProgress.Location = new System.Drawing.Point(225, 55);
            this.ShowProgress.Name = "ShowProgress";
            this.ShowProgress.Size = new System.Drawing.Size(77, 16);
            this.ShowProgress.TabIndex = 38;
            this.ShowProgress.Text = "Progress Bar";
            this.ShowProgress.UseVisualStyleBackColor = true;
            //
            // GenerateSchematic
            //
            this.GenerateSchematic.AutoSize = true;
            this.GenerateSchematic.Checked = true;
            this.GenerateSchematic.CheckState = System.Windows.Forms.CheckState.Checked;
            this.GenerateSchematic.Location = new System.Drawing.Point(225, 15);
            this.GenerateSchematic.Name = "GenerateSchematic";
            this.GenerateSchematic.Size = new System.Drawing.Size(108, 16);
            this.GenerateSchematic.TabIndex = 36;
            this.GenerateSchematic.Text = "Generate Schematic";
            this.GenerateSchematic.UseVisualStyleBackColor = true;
            //
            // SaveDirectory
            //
            this.SaveDirectory.AutoSize = true;
            this.SaveDirectory.Location = new System.Drawing.Point(6, 108);
            this.SaveDirectory.Name = "SaveDirectory";
            this.SaveDirectory.Size = new System.Drawing.Size(70, 12);
            this.SaveDirectory.TabIndex = 29;
            this.SaveDirectory.Text = "Save Directory:";
            //
            // SaveDirectoryData
            //
            this.SaveDirectoryData.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.SaveDirectoryData.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SaveDirectoryData.ForeColor = System.Drawing.Color.Snow;
            this.SaveDirectoryData.Location = new System.Drawing.Point(99, 105);
            this.SaveDirectoryData.Name = "SaveDirectoryData";
            this.SaveDirectoryData.ReadOnly = true;
            this.SaveDirectoryData.Size = new System.Drawing.Size(252, 17);
            this.SaveDirectoryData.TabIndex = 0;
            this.SaveDirectoryData.Text = "None";
            //
            // CurrentImage
            //
            this.CurrentImage.AutoSize = true;
            this.CurrentImage.Location = new System.Drawing.Point(6, 85);
            this.CurrentImage.Name = "CurrentImage";
            this.CurrentImage.Size = new System.Drawing.Size(67, 12);
            this.CurrentImage.TabIndex = 27;
            this.CurrentImage.Text = "Current Image:";
            //
            // CurrentImageData
            //
            this.CurrentImageData.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.CurrentImageData.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.CurrentImageData.ForeColor = System.Drawing.Color.Snow;
            this.CurrentImageData.Location = new System.Drawing.Point(99, 82);
            this.CurrentImageData.Name = "CurrentImageData";
            this.CurrentImageData.ReadOnly = true;
            this.CurrentImageData.Size = new System.Drawing.Size(252, 17);
            this.CurrentImageData.TabIndex = 0;
            this.CurrentImageData.Text = "Default";
            //
            // GatherStatistics
            //
            this.GatherStatistics.AutoSize = true;
            this.GatherStatistics.Checked = true;
            this.GatherStatistics.CheckState = System.Windows.Forms.CheckState.Checked;
            this.GatherStatistics.Location = new System.Drawing.Point(225, 35);
            this.GatherStatistics.Name = "GatherStatistics";
            this.GatherStatistics.Size = new System.Drawing.Size(93, 16);
            this.GatherStatistics.TabIndex = 37;
            this.GatherStatistics.Text = "Gather Statistics";
            this.GatherStatistics.UseVisualStyleBackColor = true;
            //
            // TotalBlocksData
            //
            this.TotalBlocksData.AutoSize = true;
            this.TotalBlocksData.Location = new System.Drawing.Point(117, 56);
            this.TotalBlocksData.Name = "TotalBlocksData";
            this.TotalBlocksData.Size = new System.Drawing.Size(10, 12);
            this.TotalBlocksData.TabIndex = 5;
            this.TotalBlocksData.Text = "0";
            //
            // TotalWidthData
            //
            this.TotalWidthData.AutoSize = true;
            this.TotalWidthData.Location = new System.Drawing.Point(117, 38);
            this.TotalWidthData.Name = "TotalWidthData";
            this.TotalWidthData.Size = new System.Drawing.Size(10, 12);
            this.TotalWidthData.TabIndex = 4;
            this.TotalWidthData.Text = "0";
            //
            // TotalHeightData
            //
            this.TotalHeightData.AutoSize = true;
            this.TotalHeightData.Location = new System.Drawing.Point(117, 20);
            this.TotalHeightData.Name = "TotalHeightData";
            this.TotalHeightData.Size = new System.Drawing.Size(10, 12);
            this.TotalHeightData.TabIndex = 3;
            this.TotalHeightData.Text = "0";
            //
            // TotalBlocks
            //
            this.TotalBlocks.AutoSize = true;
            this.TotalBlocks.Location = new System.Drawing.Point(6, 56);
            this.TotalBlocks.Name = "TotalBlocks";
            this.TotalBlocks.Size = new System.Drawing.Size(102, 12);
            this.TotalBlocks.TabIndex = 2;
            this.TotalBlocks.Text = "Total Blocks ---------------";
            //
            // TotalHeight
            //
            this.TotalHeight.AutoSize = true;
            this.TotalHeight.Location = new System.Drawing.Point(6, 20);
            this.TotalHeight.Name = "TotalHeight";
            this.TotalHeight.Size = new System.Drawing.Size(101, 12);
            this.TotalHeight.TabIndex = 0;
            this.TotalHeight.Text = "Total Height ---------------";
            //
            // TotalWidth
            //
            this.TotalWidth.AutoSize = true;
            this.TotalWidth.Location = new System.Drawing.Point(6, 38);
            this.TotalWidth.Name = "TotalWidth";
            this.TotalWidth.Size = new System.Drawing.Size(101, 12);
            this.TotalWidth.TabIndex = 1;
            this.TotalWidth.Text = "Total Width ----------------";
            //
            // ColorFilterToolsGroupBox
            //
            this.ColorFilterToolsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ColorFilterToolsGroupBox.Controls.Add(this.DeleteColor);
            this.ColorFilterToolsGroupBox.Controls.Add(this.DelNullColors);
            this.ColorFilterToolsGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ColorFilterToolsGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.ColorFilterToolsGroupBox.Location = new System.Drawing.Point(1028, 820);
            this.ColorFilterToolsGroupBox.Name = "ColorFilterToolsGroupBox";
            this.ColorFilterToolsGroupBox.Size = new System.Drawing.Size(203, 51);
            this.ColorFilterToolsGroupBox.TabIndex = 27;
            this.ColorFilterToolsGroupBox.TabStop = false;
            this.ColorFilterToolsGroupBox.Text = "Color Filter Tools:";
            //
            // DeleteColor
            //
            this.DeleteColor.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.DeleteColor.FlatAppearance.BorderSize = 0;
            this.DeleteColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.DeleteColor.ForeColor = System.Drawing.Color.Snow;
            this.DeleteColor.Location = new System.Drawing.Point(104, 18);
            this.DeleteColor.Name = "DeleteColor";
            this.DeleteColor.Size = new System.Drawing.Size(92, 23);
            this.DeleteColor.TabIndex = 34;
            this.DeleteColor.Text = "Delete Color";
            this.DeleteColor.UseVisualStyleBackColor = false;
            this.DeleteColor.Click += new System.EventHandler(this.DeleteColor_Click);
            //
            // DelNullColors
            //
            this.DelNullColors.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.DelNullColors.FlatAppearance.BorderSize = 0;
            this.DelNullColors.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.DelNullColors.ForeColor = System.Drawing.Color.Snow;
            this.DelNullColors.Location = new System.Drawing.Point(6, 18);
            this.DelNullColors.Name = "DelNullColors";
            this.DelNullColors.Size = new System.Drawing.Size(92, 23);
            this.DelNullColors.TabIndex = 32;
            this.DelNullColors.Text = "Del Null Colors";
            this.DelNullColors.UseVisualStyleBackColor = false;
            this.DelNullColors.Click += new System.EventHandler(this.DelNullColors_Click);
            //
            // SchematicRotationGroupBox
            //
            this.SchematicRotationGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.SchematicRotationGroupBox.Controls.Add(this.SchematicModeGroupBox);
            this.SchematicRotationGroupBox.Controls.Add(this.SchematicAxisGroupBox);
            this.SchematicRotationGroupBox.Controls.Add(this.Rotation270);
            this.SchematicRotationGroupBox.Controls.Add(this.Rotation180);
            this.SchematicRotationGroupBox.Controls.Add(this.Rotation90);
            this.SchematicRotationGroupBox.Controls.Add(this.Rotation0);
            this.SchematicRotationGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SchematicRotationGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.SchematicRotationGroupBox.Location = new System.Drawing.Point(497, 712);
            this.SchematicRotationGroupBox.Name = "SchematicRotationGroupBox";
            this.SchematicRotationGroupBox.Size = new System.Drawing.Size(193, 159);
            this.SchematicRotationGroupBox.TabIndex = 24;
            this.SchematicRotationGroupBox.TabStop = false;
            this.SchematicRotationGroupBox.Text = "Schematic Rotation:";
            //
            // SchematicModeGroupBox
            //
            this.SchematicModeGroupBox.Controls.Add(this.Flat);
            this.SchematicModeGroupBox.Controls.Add(this.Standing);
            this.SchematicModeGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.SchematicModeGroupBox.Location = new System.Drawing.Point(5, 65);
            this.SchematicModeGroupBox.Name = "SchematicModeGroupBox";
            this.SchematicModeGroupBox.Size = new System.Drawing.Size(183, 40);
            this.SchematicModeGroupBox.TabIndex = 26;
            this.SchematicModeGroupBox.TabStop = false;
            this.SchematicModeGroupBox.Text = "Schematic Mode:";
            //
            // Flat
            //
            this.Flat.AutoSize = true;
            this.Flat.Checked = true;
            this.Flat.Location = new System.Drawing.Point(94, 16);
            this.Flat.Name = "Flat";
            this.Flat.Size = new System.Drawing.Size(39, 16);
            this.Flat.TabIndex = 14;
            this.Flat.TabStop = true;
            this.Flat.Text = "Flat";
            this.Flat.UseVisualStyleBackColor = true;
            //
            // Standing
            //
            this.Standing.AutoSize = true;
            this.Standing.Location = new System.Drawing.Point(3, 16);
            this.Standing.Name = "Standing";
            this.Standing.Size = new System.Drawing.Size(59, 16);
            this.Standing.TabIndex = 13;
            this.Standing.Text = "Standing";
            this.Standing.UseVisualStyleBackColor = true;
            //
            // SchematicAxisGroupBox
            //
            this.SchematicAxisGroupBox.Controls.Add(this.YAxis);
            this.SchematicAxisGroupBox.Controls.Add(this.XAxis);
            this.SchematicAxisGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.SchematicAxisGroupBox.Location = new System.Drawing.Point(5, 110);
            this.SchematicAxisGroupBox.Name = "SchematicAxisGroupBox";
            this.SchematicAxisGroupBox.Size = new System.Drawing.Size(183, 40);
            this.SchematicAxisGroupBox.TabIndex = 25;
            this.SchematicAxisGroupBox.TabStop = false;
            this.SchematicAxisGroupBox.Text = "World Axis:";
            //
            // YAxis
            //
            this.YAxis.AutoSize = true;
            this.YAxis.Location = new System.Drawing.Point(94, 16);
            this.YAxis.Name = "YAxis";
            this.YAxis.Size = new System.Drawing.Size(50, 16);
            this.YAxis.TabIndex = 16;
            this.YAxis.Text = "Y-Axis";
            this.YAxis.UseVisualStyleBackColor = true;
            //
            // XAxis
            //
            this.XAxis.AutoSize = true;
            this.XAxis.Checked = true;
            this.XAxis.Location = new System.Drawing.Point(3, 16);
            this.XAxis.Name = "XAxis";
            this.XAxis.Size = new System.Drawing.Size(51, 16);
            this.XAxis.TabIndex = 15;
            this.XAxis.TabStop = true;
            this.XAxis.Text = "X-Axis";
            this.XAxis.UseVisualStyleBackColor = true;
            //
            // Rotation270
            //
            this.Rotation270.AutoSize = true;
            this.Rotation270.Location = new System.Drawing.Point(99, 43);
            this.Rotation270.Name = "Rotation270";
            this.Rotation270.Size = new System.Drawing.Size(75, 16);
            this.Rotation270.TabIndex = 12;
            this.Rotation270.Text = "270 Degrees";
            this.Rotation270.UseVisualStyleBackColor = true;
            //
            // Rotation180
            //
            this.Rotation180.AutoSize = true;
            this.Rotation180.Location = new System.Drawing.Point(8, 43);
            this.Rotation180.Name = "Rotation180";
            this.Rotation180.Size = new System.Drawing.Size(75, 16);
            this.Rotation180.TabIndex = 11;
            this.Rotation180.Text = "180 Degrees";
            this.Rotation180.UseVisualStyleBackColor = true;
            //
            // Rotation90
            //
            this.Rotation90.AutoSize = true;
            this.Rotation90.Location = new System.Drawing.Point(99, 19);
            this.Rotation90.Name = "Rotation90";
            this.Rotation90.Size = new System.Drawing.Size(70, 16);
            this.Rotation90.TabIndex = 10;
            this.Rotation90.Text = "90 Degrees";
            this.Rotation90.UseVisualStyleBackColor = true;
            //
            // Rotation0
            //
            this.Rotation0.AutoSize = true;
            this.Rotation0.Checked = true;
            this.Rotation0.Location = new System.Drawing.Point(8, 19);
            this.Rotation0.Name = "Rotation0";
            this.Rotation0.Size = new System.Drawing.Size(72, 16);
            this.Rotation0.TabIndex = 9;
            this.Rotation0.TabStop = true;
            this.Rotation0.Text = "No Rotation";
            this.Rotation0.UseVisualStyleBackColor = true;
            //
            // BasicConfigurationGroupBox
            //
            this.BasicConfigurationGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.BasicConfigurationGroupBox.Controls.Add(this.CopyToClipboard);
            this.BasicConfigurationGroupBox.Controls.Add(this.OverwriteExistingFile);
            this.BasicConfigurationGroupBox.Controls.Add(this.NewRatioPercentLevelData);
            this.BasicConfigurationGroupBox.Controls.Add(this.CustomColorPicker);
            this.BasicConfigurationGroupBox.Controls.Add(this.NewRatioPercentLevel);
            this.BasicConfigurationGroupBox.Controls.Add(this.RefreshRatio);
            this.BasicConfigurationGroupBox.Controls.Add(this.NewRatioPercentLevelValue);
            this.BasicConfigurationGroupBox.Controls.Add(this.SpacingLabel);
            this.BasicConfigurationGroupBox.Controls.Add(this.OpenNewImage);
            this.BasicConfigurationGroupBox.Controls.Add(this.ConvertToPixelArt);
            this.BasicConfigurationGroupBox.Controls.Add(this.SpacingValue);
            this.BasicConfigurationGroupBox.Controls.Add(this.SaveImage);
            this.BasicConfigurationGroupBox.Controls.Add(this.SaveSchemToFile);
            this.BasicConfigurationGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.BasicConfigurationGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.BasicConfigurationGroupBox.Location = new System.Drawing.Point(10, 712);
            this.BasicConfigurationGroupBox.Name = "BasicConfigurationGroupBox";
            this.BasicConfigurationGroupBox.Size = new System.Drawing.Size(481, 159);
            this.BasicConfigurationGroupBox.TabIndex = 31;
            this.BasicConfigurationGroupBox.TabStop = false;
            this.BasicConfigurationGroupBox.Text = "Basic Configurations:";
            //
            // OverwriteExistingFile
            //
            this.OverwriteExistingFile.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.OverwriteExistingFile.FlatAppearance.BorderSize = 0;
            this.OverwriteExistingFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.OverwriteExistingFile.Location = new System.Drawing.Point(320, 74);
            this.OverwriteExistingFile.Name = "OverwriteExistingFile";
            this.OverwriteExistingFile.Size = new System.Drawing.Size(151, 23);
            this.OverwriteExistingFile.TabIndex = 25;
            this.OverwriteExistingFile.Text = "Overwrite Existing File";
            this.OverwriteExistingFile.UseVisualStyleBackColor = false;
            this.OverwriteExistingFile.Click += new System.EventHandler(this.OverwriteExistingFile_Click);
            //
            // NewRatioPercentLevelData
            //
            this.NewRatioPercentLevelData.AutoSize = true;
            this.NewRatioPercentLevelData.Location = new System.Drawing.Point(259, 21);
            this.NewRatioPercentLevelData.Name = "NewRatioPercentLevelData";
            this.NewRatioPercentLevelData.Size = new System.Drawing.Size(10, 12);
            this.NewRatioPercentLevelData.TabIndex = 24;
            this.NewRatioPercentLevelData.Text = "0";
            //
            // CustomColorPicker
            //
            this.CustomColorPicker.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.CustomColorPicker.FlatAppearance.BorderSize = 0;
            this.CustomColorPicker.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.CustomColorPicker.Location = new System.Drawing.Point(320, 16);
            this.CustomColorPicker.Name = "CustomColorPicker";
            this.CustomColorPicker.Size = new System.Drawing.Size(151, 23);
            this.CustomColorPicker.TabIndex = 6;
            this.CustomColorPicker.Text = "Custom Color Picker";
            this.CustomColorPicker.UseVisualStyleBackColor = false;
            this.CustomColorPicker.Click += new System.EventHandler(this.CustomColorPicker_Click);
            //
            // NewRatioPercentLevel
            //
            this.NewRatioPercentLevel.AutoSize = true;
            this.NewRatioPercentLevel.Location = new System.Drawing.Point(161, 21);
            this.NewRatioPercentLevel.Name = "NewRatioPercentLevel";
            this.NewRatioPercentLevel.Size = new System.Drawing.Size(87, 12);
            this.NewRatioPercentLevel.TabIndex = 18;
            this.NewRatioPercentLevel.Text = "New Ratio % Level:";
            //
            // RefreshRatio
            //
            this.RefreshRatio.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.RefreshRatio.FlatAppearance.BorderSize = 0;
            this.RefreshRatio.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.RefreshRatio.ForeColor = System.Drawing.Color.Snow;
            this.RefreshRatio.Location = new System.Drawing.Point(163, 70);
            this.RefreshRatio.Name = "RefreshRatio";
            this.RefreshRatio.Size = new System.Drawing.Size(151, 23);
            this.RefreshRatio.TabIndex = 5;
            this.RefreshRatio.Text = "Refresh Ratio";
            this.RefreshRatio.UseVisualStyleBackColor = false;
            this.RefreshRatio.Click += new System.EventHandler(this.RefreshRatio_Click);
            //
            // NewRatioPercentLevelValue
            //
            this.NewRatioPercentLevelValue.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.NewRatioPercentLevelValue.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.NewRatioPercentLevelValue.ForeColor = System.Drawing.Color.Snow;
            this.NewRatioPercentLevelValue.Location = new System.Drawing.Point(163, 40);
            this.NewRatioPercentLevelValue.Maximum = new decimal(new int[] {
            200,
            0,
            0,
            0});
            this.NewRatioPercentLevelValue.Minimum = new decimal(new int[] {
            200,
            0,
            0,
            -2147483648});
            this.NewRatioPercentLevelValue.Name = "NewRatioPercentLevelValue";
            this.NewRatioPercentLevelValue.Size = new System.Drawing.Size(151, 17);
            this.NewRatioPercentLevelValue.TabIndex = 3;
            //
            // SpacingLabel
            //
            this.SpacingLabel.AutoSize = true;
            this.SpacingLabel.Location = new System.Drawing.Point(3, 21);
            this.SpacingLabel.Name = "SpacingLabel";
            this.SpacingLabel.Size = new System.Drawing.Size(38, 12);
            this.SpacingLabel.TabIndex = 6;
            this.SpacingLabel.Text = "Spacing";
            //
            // OpenNewImage
            //
            this.OpenNewImage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.OpenNewImage.FlatAppearance.BorderSize = 0;
            this.OpenNewImage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.OpenNewImage.ForeColor = System.Drawing.Color.Snow;
            this.OpenNewImage.Location = new System.Drawing.Point(6, 103);
            this.OpenNewImage.Name = "OpenNewImage";
            this.OpenNewImage.Size = new System.Drawing.Size(151, 43);
            this.OpenNewImage.TabIndex = 7;
            this.OpenNewImage.Text = "Open New Image";
            this.OpenNewImage.UseVisualStyleBackColor = false;
            this.OpenNewImage.Click += new System.EventHandler(this.OpenNewImage_Click);
            //
            // ConvertToPixelArt
            //
            this.ConvertToPixelArt.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.ConvertToPixelArt.FlatAppearance.BorderSize = 0;
            this.ConvertToPixelArt.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ConvertToPixelArt.ForeColor = System.Drawing.Color.Snow;
            this.ConvertToPixelArt.Location = new System.Drawing.Point(164, 103);
            this.ConvertToPixelArt.Name = "ConvertToPixelArt";
            this.ConvertToPixelArt.Size = new System.Drawing.Size(151, 43);
            this.ConvertToPixelArt.TabIndex = 1;
            this.ConvertToPixelArt.Text = "Convert To Pixel Art";
            this.ConvertToPixelArt.UseVisualStyleBackColor = false;
            this.ConvertToPixelArt.Click += new System.EventHandler(this.ConvertToPixelArt_Click);
            //
            // SpacingValue
            //
            this.SpacingValue.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.SpacingValue.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SpacingValue.ForeColor = System.Drawing.Color.Snow;
            this.SpacingValue.Location = new System.Drawing.Point(6, 40);
            this.SpacingValue.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.SpacingValue.Name = "SpacingValue";
            this.SpacingValue.Size = new System.Drawing.Size(151, 17);
            this.SpacingValue.TabIndex = 2;
            this.SpacingValue.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            //
            // SaveImage
            //
            this.SaveImage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.SaveImage.FlatAppearance.BorderSize = 0;
            this.SaveImage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.SaveImage.ForeColor = System.Drawing.Color.Snow;
            this.SaveImage.Location = new System.Drawing.Point(6, 70);
            this.SaveImage.Name = "SaveImage";
            this.SaveImage.Size = new System.Drawing.Size(151, 23);
            this.SaveImage.TabIndex = 4;
            this.SaveImage.Text = "Save Image";
            this.SaveImage.UseVisualStyleBackColor = false;
            this.SaveImage.Click += new System.EventHandler(this.SaveImage_Click);
            //
            // SaveSchemToFile
            //
            this.SaveSchemToFile.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.SaveSchemToFile.FlatAppearance.BorderSize = 0;
            this.SaveSchemToFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.SaveSchemToFile.ForeColor = System.Drawing.Color.Snow;
            this.SaveSchemToFile.Location = new System.Drawing.Point(320, 45);
            this.SaveSchemToFile.Name = "SaveSchemToFile";
            this.SaveSchemToFile.Size = new System.Drawing.Size(151, 23);
            this.SaveSchemToFile.TabIndex = 8;
            this.SaveSchemToFile.Text = "Save Schematic To FIle";
            this.SaveSchemToFile.UseVisualStyleBackColor = false;
            this.SaveSchemToFile.Click += new System.EventHandler(this.SaveSchemToFile_Click);
            //
            // ScalingModeGroupBox
            //
            this.ScalingModeGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ScalingModeGroupBox.Controls.Add(this.SigmaEquals);
            this.ScalingModeGroupBox.Controls.Add(this.AEquals);
            this.ScalingModeGroupBox.Controls.Add(this.Gaussian);
            this.ScalingModeGroupBox.Controls.Add(this.Spline);
            this.ScalingModeGroupBox.Controls.Add(this.Hermite);
            this.ScalingModeGroupBox.Controls.Add(this.Lanczos);
            this.ScalingModeGroupBox.Controls.Add(this.Bicubic);
            this.ScalingModeGroupBox.Controls.Add(this.NearestNeighbor);
            this.ScalingModeGroupBox.Controls.Add(this.Bilinear);
            this.ScalingModeGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ScalingModeGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.ScalingModeGroupBox.Location = new System.Drawing.Point(871, 712);
            this.ScalingModeGroupBox.Name = "ScalingModeGroupBox";
            this.ScalingModeGroupBox.Size = new System.Drawing.Size(151, 159);
            this.ScalingModeGroupBox.TabIndex = 16;
            this.ScalingModeGroupBox.TabStop = false;
            this.ScalingModeGroupBox.Text = "Scaling Mode:";
            //
            // SigmaEquals
            //
            this.SigmaEquals.DecimalPlaces = 1;
            this.SigmaEquals.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.SigmaEquals.Location = new System.Drawing.Point(95, 129);
            this.SigmaEquals.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.SigmaEquals.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.SigmaEquals.Name = "SigmaEquals";
            this.SigmaEquals.Size = new System.Drawing.Size(50, 17);
            this.SigmaEquals.TabIndex = 30;
            this.SigmaEquals.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            //
            // AEquals
            //
            this.AEquals.Location = new System.Drawing.Point(95, 71);
            this.AEquals.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.AEquals.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.AEquals.Name = "AEquals";
            this.AEquals.Size = new System.Drawing.Size(50, 17);
            this.AEquals.TabIndex = 26;
            this.AEquals.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            //
            // Gaussian
            //
            this.Gaussian.AutoSize = true;
            this.Gaussian.Location = new System.Drawing.Point(6, 134);
            this.Gaussian.Name = "Gaussian";
            this.Gaussian.Size = new System.Drawing.Size(62, 16);
            this.Gaussian.TabIndex = 29;
            this.Gaussian.Text = "Gaussian";
            this.Gaussian.UseVisualStyleBackColor = true;
            //
            // Spline
            //
            this.Spline.AutoSize = true;
            this.Spline.Location = new System.Drawing.Point(6, 114);
            this.Spline.Name = "Spline";
            this.Spline.Size = new System.Drawing.Size(48, 16);
            this.Spline.TabIndex = 28;
            this.Spline.Text = "Spline";
            this.Spline.UseVisualStyleBackColor = true;
            //
            // Hermite
            //
            this.Hermite.AutoSize = true;
            this.Hermite.Location = new System.Drawing.Point(6, 94);
            this.Hermite.Name = "Hermite";
            this.Hermite.Size = new System.Drawing.Size(56, 16);
            this.Hermite.TabIndex = 27;
            this.Hermite.Text = "Hermite";
            this.Hermite.UseVisualStyleBackColor = true;
            //
            // Lanczos
            //
            this.Lanczos.AutoSize = true;
            this.Lanczos.Location = new System.Drawing.Point(6, 74);
            this.Lanczos.Name = "Lanczos";
            this.Lanczos.Size = new System.Drawing.Size(58, 16);
            this.Lanczos.TabIndex = 25;
            this.Lanczos.Text = "Lanczos";
            this.Lanczos.UseVisualStyleBackColor = true;
            //
            // Bicubic
            //
            this.Bicubic.AutoSize = true;
            this.Bicubic.Location = new System.Drawing.Point(6, 54);
            this.Bicubic.Name = "Bicubic";
            this.Bicubic.Size = new System.Drawing.Size(53, 16);
            this.Bicubic.TabIndex = 24;
            this.Bicubic.Text = "Bicubic";
            this.Bicubic.UseVisualStyleBackColor = true;
            //
            // NearestNeighbor
            //
            this.NearestNeighbor.AutoSize = true;
            this.NearestNeighbor.Location = new System.Drawing.Point(6, 34);
            this.NearestNeighbor.Name = "NearestNeighbor";
            this.NearestNeighbor.Size = new System.Drawing.Size(95, 16);
            this.NearestNeighbor.TabIndex = 23;
            this.NearestNeighbor.Text = "Nearest Neighbor";
            this.NearestNeighbor.UseVisualStyleBackColor = true;
            //
            // Bilinear
            //
            this.Bilinear.AutoSize = true;
            this.Bilinear.Checked = true;
            this.Bilinear.Location = new System.Drawing.Point(6, 14);
            this.Bilinear.Name = "Bilinear";
            this.Bilinear.Size = new System.Drawing.Size(53, 16);
            this.Bilinear.TabIndex = 22;
            this.Bilinear.TabStop = true;
            this.Bilinear.Text = "Bilinear";
            this.Bilinear.UseVisualStyleBackColor = true;
            //
            // ColorFilterSettingsGroupBox
            //
            this.ColorFilterSettingsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ColorFilterSettingsGroupBox.Controls.Add(this.UniqueColors);
            this.ColorFilterSettingsGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ColorFilterSettingsGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.ColorFilterSettingsGroupBox.Location = new System.Drawing.Point(1028, 712);
            this.ColorFilterSettingsGroupBox.Name = "ColorFilterSettingsGroupBox";
            this.ColorFilterSettingsGroupBox.Size = new System.Drawing.Size(203, 44);
            this.ColorFilterSettingsGroupBox.TabIndex = 32;
            this.ColorFilterSettingsGroupBox.TabStop = false;
            this.ColorFilterSettingsGroupBox.Text = "Color Filter Settings:";
            //
            // UniqueColors
            //
            this.UniqueColors.AutoSize = true;
            this.UniqueColors.Checked = true;
            this.UniqueColors.CheckState = System.Windows.Forms.CheckState.Checked;
            this.UniqueColors.Location = new System.Drawing.Point(6, 17);
            this.UniqueColors.Name = "UniqueColors";
            this.UniqueColors.Size = new System.Drawing.Size(82, 16);
            this.UniqueColors.TabIndex = 31;
            this.UniqueColors.Text = "Unique Colors";
            this.UniqueColors.UseVisualStyleBackColor = true;
            this.UniqueColors.CheckedChanged += new System.EventHandler(this.UniqueColors_CheckedChanged);
            //
            // ColorFilterDataGroupBox
            //
            this.ColorFilterDataGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ColorFilterDataGroupBox.Controls.Add(this.FilteredColorsData);
            this.ColorFilterDataGroupBox.Controls.Add(this.TotalColorsData);
            this.ColorFilterDataGroupBox.Controls.Add(this.FilteredColors);
            this.ColorFilterDataGroupBox.Controls.Add(this.TotalColors);
            this.ColorFilterDataGroupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ColorFilterDataGroupBox.ForeColor = System.Drawing.Color.Snow;
            this.ColorFilterDataGroupBox.Location = new System.Drawing.Point(1028, 762);
            this.ColorFilterDataGroupBox.Name = "ColorFilterDataGroupBox";
            this.ColorFilterDataGroupBox.Size = new System.Drawing.Size(203, 52);
            this.ColorFilterDataGroupBox.TabIndex = 33;
            this.ColorFilterDataGroupBox.TabStop = false;
            this.ColorFilterDataGroupBox.Text = "Color Filter Data";
            //
            // FilteredColorsData
            //
            this.FilteredColorsData.AutoSize = true;
            this.FilteredColorsData.Location = new System.Drawing.Point(83, 32);
            this.FilteredColorsData.Name = "FilteredColorsData";
            this.FilteredColorsData.Size = new System.Drawing.Size(10, 12);
            this.FilteredColorsData.TabIndex = 3;
            this.FilteredColorsData.Text = "0";
            //
            // TotalColorsData
            //
            this.TotalColorsData.AutoSize = true;
            this.TotalColorsData.Location = new System.Drawing.Point(83, 17);
            this.TotalColorsData.Name = "TotalColorsData";
            this.TotalColorsData.Size = new System.Drawing.Size(10, 12);
            this.TotalColorsData.TabIndex = 2;
            this.TotalColorsData.Text = "0";
            //
            // FilteredColors
            //
            this.FilteredColors.AutoSize = true;
            this.FilteredColors.Location = new System.Drawing.Point(6, 31);
            this.FilteredColors.Name = "FilteredColors";
            this.FilteredColors.Size = new System.Drawing.Size(70, 12);
            this.FilteredColors.TabIndex = 1;
            this.FilteredColors.Text = "Filtered Colors -";
            //
            // TotalColors
            //
            this.TotalColors.AutoSize = true;
            this.TotalColors.Location = new System.Drawing.Point(6, 16);
            this.TotalColors.Name = "TotalColors";
            this.TotalColors.Size = new System.Drawing.Size(68, 12);
            this.TotalColors.TabIndex = 0;
            this.TotalColors.Text = "Total Colors ----";
            //
            // CopyToClipboard
            //
            this.CopyToClipboard.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.CopyToClipboard.FlatAppearance.BorderSize = 0;
            this.CopyToClipboard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.CopyToClipboard.ForeColor = System.Drawing.Color.Snow;
            this.CopyToClipboard.Location = new System.Drawing.Point(320, 103);
            this.CopyToClipboard.Name = "CopyToClipboard";
            this.CopyToClipboard.Size = new System.Drawing.Size(151, 43);
            this.CopyToClipboard.TabIndex = 26;
            this.CopyToClipboard.Text = "Copy To Clipboard";
            this.CopyToClipboard.UseVisualStyleBackColor = false;
            this.CopyToClipboard.Click += new System.EventHandler(this.CopyToClipboard_Click);
            //
            // MainForm
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(51)))), ((int)(((byte)(51)))));
            this.ClientSize = new System.Drawing.Size(1774, 881);
            this.Controls.Add(this.ColorFilterDataGroupBox);
            this.Controls.Add(this.ColorFilterSettingsGroupBox);
            this.Controls.Add(this.ColorFilterToolsGroupBox);
            this.Controls.Add(this.ScalingModeGroupBox);
            this.Controls.Add(this.BasicConfigurationGroupBox);
            this.Controls.Add(this.SchematicRotationGroupBox);
            this.Controls.Add(this.PixelArtStatisticsGroupBox);
            this.Controls.Add(this.ColorFilterManagerGroupBox);
            this.Controls.Add(this.GridOptionsGroupBox);
            this.Controls.Add(this.SourceImage);
            this.Controls.Add(this.ConvertedImage);
            this.Font = new System.Drawing.Font("Arial", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "MainForm";
            this.Text = "Image To Pixel-Art To Schematic | Coded By: D.RUSS#2430 | WorldEdit Modding";
            ((System.ComponentModel.ISupportInitialize)(this.SourceImage)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ConvertedImage)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.GridY)).EndInit();
            this.GridOptionsGroupBox.ResumeLayout(false);
            this.GridOptionsGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.GridX)).EndInit();
            this.ColorFilterManagerGroupBox.ResumeLayout(false);
            this.ColorFilterManagerGroupBox.PerformLayout();
            this.PixelArtStatisticsGroupBox.ResumeLayout(false);
            this.PixelArtStatisticsGroupBox.PerformLayout();
            this.ColorFilterToolsGroupBox.ResumeLayout(false);
            this.SchematicRotationGroupBox.ResumeLayout(false);
            this.SchematicRotationGroupBox.PerformLayout();
            this.SchematicModeGroupBox.ResumeLayout(false);
            this.SchematicModeGroupBox.PerformLayout();
            this.SchematicAxisGroupBox.ResumeLayout(false);
            this.SchematicAxisGroupBox.PerformLayout();
            this.BasicConfigurationGroupBox.ResumeLayout(false);
            this.BasicConfigurationGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NewRatioPercentLevelValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpacingValue)).EndInit();
            this.ScalingModeGroupBox.ResumeLayout(false);
            this.ScalingModeGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SigmaEquals)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.AEquals)).EndInit();
            this.ColorFilterSettingsGroupBox.ResumeLayout(false);
            this.ColorFilterSettingsGroupBox.PerformLayout();
            this.ColorFilterDataGroupBox.ResumeLayout(false);
            this.ColorFilterDataGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button PickGridColor;
        private System.Windows.Forms.PictureBox SourceImage;
        private System.Windows.Forms.PictureBox ConvertedImage;
        private System.Windows.Forms.CheckBox ShowGrid;
        private System.Windows.Forms.NumericUpDown GridY;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.GroupBox GridOptionsGroupBox;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown GridX;
        private System.Windows.Forms.ProgressBar MainProgressBar;
        private System.Windows.Forms.GroupBox ColorFilterManagerGroupBox;
        private System.Windows.Forms.Button LoadColorFilter;
        private System.Windows.Forms.TextBox FilterName;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button SaveColorFilter;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.GroupBox PixelArtStatisticsGroupBox;
        private System.Windows.Forms.Label TotalWidth;
        private System.Windows.Forms.Label TotalHeight;
        private System.Windows.Forms.Label TotalBlocksData;
        private System.Windows.Forms.Label TotalWidthData;
        private System.Windows.Forms.Label TotalHeightData;
        private System.Windows.Forms.Label TotalBlocks;
        private System.Windows.Forms.CheckBox GatherStatistics;
        private System.Windows.Forms.GroupBox SchematicRotationGroupBox;
        private System.Windows.Forms.RadioButton Rotation270;
        private System.Windows.Forms.RadioButton Rotation180;
        private System.Windows.Forms.RadioButton Rotation90;
        private System.Windows.Forms.RadioButton Rotation0;
        private System.Windows.Forms.RadioButton YAxis;
        private System.Windows.Forms.RadioButton XAxis;
        private System.Windows.Forms.GroupBox SchematicAxisGroupBox;
        private System.Windows.Forms.Label CurrentImage;
        private System.Windows.Forms.TextBox CurrentImageData;
        private System.Windows.Forms.TextBox SaveDirectoryData;
        private System.Windows.Forms.Label SaveDirectory;
        private System.Windows.Forms.GroupBox SchematicModeGroupBox;
        private System.Windows.Forms.RadioButton Flat;
        private System.Windows.Forms.RadioButton Standing;
        private System.Windows.Forms.CheckBox Backdrop;
        private System.Windows.Forms.GroupBox ColorFilterToolsGroupBox;
        private System.Windows.Forms.Button DelNullColors;
        private System.Windows.Forms.GroupBox BasicConfigurationGroupBox;
        private System.Windows.Forms.GroupBox ScalingModeGroupBox;
        private System.Windows.Forms.RadioButton NearestNeighbor;
        private System.Windows.Forms.RadioButton Bilinear;
        private System.Windows.Forms.Label NewRatioPercentLevel;
        private System.Windows.Forms.Button RefreshRatio;
        private System.Windows.Forms.NumericUpDown NewRatioPercentLevelValue;
        private System.Windows.Forms.CheckBox GenerateSchematic;
        private System.Windows.Forms.Label SpacingLabel;
        private System.Windows.Forms.Button OpenNewImage;
        private System.Windows.Forms.Button ConvertToPixelArt;
        private System.Windows.Forms.NumericUpDown SpacingValue;
        private System.Windows.Forms.Button SaveImage;
        private System.Windows.Forms.Button SaveSchemToFile;
        private System.Windows.Forms.Button CustomColorPicker;
        private System.Windows.Forms.Button ResetColors;
        private System.Windows.Forms.GroupBox ColorFilterSettingsGroupBox;
        private System.Windows.Forms.CheckBox UniqueColors;
        private System.Windows.Forms.CheckBox ShowProgress;
        private System.Windows.Forms.RadioButton Bicubic;
        private System.Windows.Forms.NumericUpDown SigmaEquals;
        private System.Windows.Forms.NumericUpDown AEquals;
        private System.Windows.Forms.RadioButton Gaussian;
        private System.Windows.Forms.RadioButton Spline;
        private System.Windows.Forms.RadioButton Hermite;
        private System.Windows.Forms.RadioButton Lanczos;
        private System.Windows.Forms.GroupBox ColorFilterDataGroupBox;
        private System.Windows.Forms.Label FilteredColorsData;
        private System.Windows.Forms.Label TotalColorsData;
        private System.Windows.Forms.Label FilteredColors;
        private System.Windows.Forms.Label TotalColors;
        private System.Windows.Forms.Label NewRatioPercentLevelData;
        private System.Windows.Forms.Button OverwriteExistingFile;
        private System.Windows.Forms.Button DeleteColor;
        private System.Windows.Forms.Button CopyToClipboard;
    }
}


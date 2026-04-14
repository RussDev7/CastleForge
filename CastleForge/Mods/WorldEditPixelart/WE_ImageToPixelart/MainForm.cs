/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

/*
Sections of this class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp.
*/

using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Numerics;
using System.Xml.Linq;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System;

using static WorldEdit.WorldEditCore;

using Vector3 = Microsoft.Xna.Framework.Vector3; // For CMZ.

namespace WE_ImageToPixelart
{
    public partial class MainForm : Form
    {
        #region State / Flags

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);
        private static CancellationTokenSource _cancellationTokenSource;
        private bool _startedBuildColorFilter;

        public HashSet<Tuple<Vector3, int>> schematicData = new HashSet<Tuple<Vector3, int>>();
        public static Color GridColor { get; set; } = Color.Red;
        public static List<BlockData> BlockDataList { get; set; }
        public static List<BlockData> ClrsBlockData { get; set; }
        public static Color[] Clrs { get; set; } = new Color[1];
        public static Image OriginalImageSource { get; set; }
        public static FileInfo SavedSchematicLocation { get; set; }

        public static Color GetCustomColor = Color.Black;
        public static bool isSchematicOld = false;

        #endregion

        #region Initialization

        public MainForm()
        {
            InitializeComponent();

            // Define control tooltips.
            #region Tooltips

            // Create a new tooltip.
            ToolTip toolTip = new ToolTip()
            {
                AutoPopDelay = 5000,
                InitialDelay = 750
            };

            // Set tool texts.
            toolTip.SetToolTip(CopyToClipboard, "Copy the generated pixel art to the clipboard.");
            toolTip.SetToolTip(SaveImage, "Save the generated pixel art as an image.");
            toolTip.SetToolTip(RefreshRatio, "Set new image zoom ratio.");
            toolTip.SetToolTip(OverwriteExistingFile, "Overwrite the existing schematic.");
            toolTip.SetToolTip(OpenNewImage, "Open a new image for conversion.");
            toolTip.SetToolTip(ConvertToPixelArt, "Convert the current image to pixel art.");
            toolTip.SetToolTip(DelNullColors, "Remove colors from the filter not used in the render.");
            toolTip.SetToolTip(ResetColors, "Reset the color filter from the default file.");
            toolTip.SetToolTip(ShowGrid, "Place a grid over the rendered pixel art.");
            toolTip.SetToolTip(Backdrop, "Add a backdrop to the transparent pixels.");
            toolTip.SetToolTip(PickGridColor, "Change the grid color.");
            // toolTip.SetToolTip(NUDTextBox3, "Offset the grid Y-Axis.");
            // toolTip.SetToolTip(NUDTextBox4, "Offset the grid X-Axis.");
            toolTip.SetToolTip(AEquals, "Change the A=.");
            toolTip.SetToolTip(SigmaEquals, "Change the Sigma=.");
            toolTip.SetToolTip(GenerateSchematic, "Generate schematic file while rendering pixel art (slight speed impact).");
            toolTip.SetToolTip(GatherStatistics, "Record pixelart statistics.");
            toolTip.SetToolTip(MainProgressBar, "Show the pixel art progression (slight speed impact).");
            toolTip.SetToolTip(CustomColorPicker, "Pick a source color and add it to the filter.");
            toolTip.SetToolTip(LoadColorFilter, "Load an existing color filter from a file.");
            toolTip.SetToolTip(SaveColorFilter, "Save the existing color filter to a file.");
            toolTip.SetToolTip(DeleteColor, "Remove a specified render color from the filter.");

            #endregion

            // Define original image source.
            OriginalImageSource = SourceImage.Image;
        }

        // Starts first-run UI work after the form is actually shown.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_startedBuildColorFilter)
                return;

            _startedBuildColorFilter = true;

            // Load the color filter.
            var _ = Task.Run(() => BuildColorFilter())  // or BuildColorFilterAsync(ct)
                .ContinueWith(t =>
                {
                    // Observe/report error on UI thread
                    BeginInvoke(new Action(() =>
                        MessageBox.Show(this, t.Exception?.GetBaseException().Message ?? "Unknown error",
                                        "Color filter error")));
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
        #endregion

        #region Basic Configuration Controls

        #region Color Filter Checked Changed Logic

        private async void UniqueColors_CheckedChanged(object sender, EventArgs e)
        {
            await BuildColorFilter();
        }
        #endregion

        #region Open New Image

        // Open New Image
        private void OpenNewImage_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        // Load the selected image
                        using (Bitmap originalBitmap = new Bitmap(ofd.FileName))
                        {
                            CurrentImageData.Text = ofd.FileName;

                            // Update the original source to the new photo.
                            OriginalImageSource = new Bitmap(originalBitmap);

                            // Determine zoom factor
                            int zoomFactor = (int)NewRatioPercentLevelValue.Value;
                            if (zoomFactor == 0)
                            {
                                SourceImage.Image = new Bitmap(originalBitmap);
                            }
                            else
                            {
                                // Calculate new size based on zoom factor
                                Size newSize = (zoomFactor > 0)
                                    ? new Size(originalBitmap.Width * zoomFactor, originalBitmap.Height * zoomFactor)
                                    : new Size(originalBitmap.Width / -zoomFactor, originalBitmap.Height / -zoomFactor);

                                SourceImage.Image = new Bitmap(originalBitmap, newSize);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening image: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region Refresh Image Ratio

        // Adjust Ratio.
        private async void RefreshRatio_Click(object sender, EventArgs e)
        {
            try
            {
                Bitmap bitmapImage = null;
                int zoomFactor = 0;

                Invoke((Action)(() =>
                {
                    // Set the labels data.
                    NewRatioPercentLevelData.Text = "Calc...";

                    // Gather the values from the UI thread.
                    bitmapImage = new Bitmap(OriginalImageSource);
                    zoomFactor = (int)NewRatioPercentLevelValue.Value;

                    // Disable ratio, convert, and open buttons.
                    ConvertToPixelArt.Enabled = false;
                    OpenNewImage.Enabled = false;
                    RefreshRatio.Enabled = false;

                    NewRatioPercentLevelValue.Enabled = false;
                }));

                await Task.Run(() =>
                {
                    Bitmap resizedImage = null;

                    if (zoomFactor == 0)
                    {
                        Invoke((Action)(() =>
                        {
                            // Set the labels data.
                            NewRatioPercentLevelData.Text = ((int)NewRatioPercentLevelValue.Value).ToString();

                            // Set image to the original.
                            SourceImage.Image = OriginalImageSource;

                            // Enable ratio, convert, and open buttons.
                            ConvertToPixelArt.Enabled = true;
                            OpenNewImage.Enabled = true;
                            RefreshRatio.Enabled = true;

                            NewRatioPercentLevelValue.Enabled = true;
                        }));
                        return;
                    }
                    else if (zoomFactor > 0)
                    {
                        // Zoom In.
                        Size newSize = new Size(bitmapImage.Width * zoomFactor, bitmapImage.Height * zoomFactor);
                        resizedImage = new Bitmap(bitmapImage, newSize);
                    }
                    else if (zoomFactor < 0)
                    {
                        // Zoom Out.
                        zoomFactor *= -1;
                        Size newSize = new Size(bitmapImage.Width / zoomFactor, bitmapImage.Height / zoomFactor);
                        resizedImage = new Bitmap(bitmapImage, newSize);
                    }

                    Invoke((Action)(() =>
                    {
                        // Set the labels data.
                        NewRatioPercentLevelData.Text = ((int)NewRatioPercentLevelValue.Value).ToString();

                        // Set the new image.
                        SourceImage.Image = resizedImage;

                        // Enable ratio, convert, and open buttons.
                        ConvertToPixelArt.Enabled = true;
                        OpenNewImage.Enabled = true;
                        RefreshRatio.Enabled = true;

                        NewRatioPercentLevelValue.Enabled = true;
                    }));
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("ERROR: " + ex.Message);
            }
        }
        #endregion

        #region Convert Pixelart Button

        // Convert Button With no Output

        private async void ConvertToPixelArt_Click(object sender, EventArgs e)
        {
            // Create a new tooltip.
            ToolTip toolTip = new ToolTip()
            {
                AutoPopDelay = 5000,
                InitialDelay = 750
            };

            // Check the current button content.
            if (ConvertToPixelArt.Text.ToString() == "Convert To Pixel Art")
            {
                // Ensure the color filter is not zero.
                if (ClrsBlockData.Count == 0)
                {
                    // Display error.
                    MessageBox.Show("The color filter is zero. Adjust your settings.");
                    return;
                }

                // Start or restart the conversion.
                _cancellationTokenSource?.Cancel();                                               // Cancel any existing conversion tasks.
                _cancellationTokenSource = new CancellationTokenSource();                         // Create a new CancellationTokenSource for the new operation.
                toolTip.SetToolTip(ConvertToPixelArt, "Cancle the current rendering operation."); // Change button tooltip.
                ConvertToPixelArt.Text = "Cancel Conversion";                                     // Change button content to indicate the operation can be cancelled.

                try
                {
                    // Force remove the old schematic.
                    // schematicData.Clear();

                    // Call ConvertPixelArt asynchronously with the cancellation token.
                    await ConvertPixelArt(buildSchematic: GenerateSchematic.Checked, cancellationToken: _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // Handle cancellation.
                    MessageBox.Show("Rendering was cancelled.");
                }
                finally
                {
                    // Reset button content, tooltip, and state.
                    toolTip.SetToolTip(ConvertToPixelArt, "Convert the current image to pixel art."); // Change button tooltip.
                    ConvertToPixelArt.Text = "Convert To Pixel Art";
                }
            }
            else if (ConvertToPixelArt.Text.ToString() == "Cancel Conversion")
            {
                // Cancel the ongoing conversion.
                _cancellationTokenSource?.Cancel();
            }
        }
        #endregion

        #region Save Image

        private void SaveImage_Click(object sender, EventArgs e)
        {
            if (ConvertedImage.Image == null)
            {
                MessageBox.Show("No image to save.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "PNG Image|*.png";
                saveFileDialog.Title = "Save an Image File";
                saveFileDialog.FileName = "image.png";
                saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        ConvertedImage.Image.Save(saveFileDialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
                        MessageBox.Show("Image saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving image: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
        #endregion

        #region Save Schematic To File

        private void SaveSchemToFile_Click(object sender, EventArgs e)
        {
            try
            {
                if (schematicData.Count == 0)
                {
                    MessageBox.Show("The schematic data is empty! Generate a pixelart first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Check if the schematic data is old and ask user to continue.
                if (isSchematicOld)
                    if (MessageBox.Show("Your schematic data is not up to date with the current image.\n\nDo you want to continue?", "Confirmation", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                        return;

                // Launch an open file dialog to get the name.
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Title = "Select Save Location",
                    Filter = "Schem Files|*.schem",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                // Ensure directory exists.
                if (!Directory.Exists(saveFileDialog.InitialDirectory))
                    Directory.CreateDirectory(saveFileDialog.InitialDirectory);

                if (saveFileDialog.ShowDialog() == DialogResult.OK) // For WinForms
                {
                    try
                    {
                        // Define main file info.
                        FileInfo schemLocation = new FileInfo(saveFileDialog.FileName);

                        // Backup the save path.
                        SavedSchematicLocation = schemLocation;

                        // Set the exported schematic directory data.
                        SaveDirectoryData.Text = saveFileDialog.FileName;

                        // Save the regions data to a file.
                        SaveSchematic(schematicData, schemLocation, true);

                        // Display completion message.
                        MessageBox.Show($"Schematic '{schemLocation.Name}' has been saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        // Debug: Clipboard.SetText(ex.Message);
                        MessageBox.Show(ex.Message, "Error Saving Schematic");
                    }
                }
                else
                {
                    Console.WriteLine("Save operation canceled.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        // Schematic encryption algorithm.
        private static readonly byte WE_SAVE_VERSION = 0x1;
        private static readonly byte[] WE_SAVE_HEADER = Encoding.UTF8.GetBytes("WES");
        public static void SaveSchematic(HashSet<Tuple<Vector3, int>> clipboard, FileInfo schemPath, bool overwriteFile = false)
        {
            if (clipboard.Count() <= 0)
            {
                Console.WriteLine("Your clipboard is empty.");
                return;
            }

            if (schemPath.Directory != null && !schemPath.Directory.Exists)
                schemPath.Directory.Create();

            if (schemPath.Exists)
            {
                if (overwriteFile)
                    File.Delete(schemPath.FullName); // Delete the file and overwrite it.
                else
                {
                    Console.WriteLine("A save with that name already exists.");
                    return;
                }
            }

            long len = 0;
            using (FileStream stream = schemPath.Create())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(WE_SAVE_HEADER);
                writer.Write(WE_SAVE_VERSION);

                // Serialize the data manually using BinaryWriter.
                foreach (var item in clipboard)
                {
                    writer.Write(item.Item1.X);
                    writer.Write(item.Item1.Y);
                    writer.Write(item.Item1.Z);
                    writer.Write(item.Item2);
                }

                len = stream.Length;
                stream.Flush();
            }

            Console.WriteLine($"Created save called {schemPath.Name} successfully. (size is {len / 1024} KB)");
        }
        #endregion

        #region Overwrite Existing File

        private void OverwriteExistingFile_Click(object sender, EventArgs e)
        {
            try
            {
                if (schematicData.Count == 0)
                {
                    MessageBox.Show("The schematic data is empty! Generate a pixelart first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (SavedSchematicLocation == null)
                {
                    MessageBox.Show("You need to have previously saved a schematic first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    // Check if the schematic data is old and ask user to continue.
                    if (isSchematicOld)
                        if (MessageBox.Show("Your schematic data is not up to date with the current image.\n\nDo you want to continue?", "Confirmation", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                            return;

                    // Save the regions data to a file.
                    SaveSchematic(schematicData, SavedSchematicLocation, true);

                    // Display completion message.
                    MessageBox.Show($"Schematic '{SavedSchematicLocation.Name}' has been saved successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error Saving Schematic");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        #endregion

        #region Copy To Clipboard

        private void CopyToClipboard_Click(object sender, EventArgs e)
        {
            try
            {
                if (schematicData.Count == 0)
                {
                    MessageBox.Show("The schematic data is empty! Generate a pixelart first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Check if the schematic data is old and ask user to continue.
                if (isSchematicOld)
                    if (MessageBox.Show("Your schematic data is not up to date with the current image.\n\nDo you want to continue?", "Confirmation", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                        return;

                // Save the schematic to WorldEdits clipboard.
                try
                {
                    // Save copy data.
                    CopySchematic(schematicData);

                    // Display completion message.
                    MessageBox.Show("Schematic was copied over to worldedit successfully.");
                }
                catch (Exception ex)
                {
                    // Debug: Clipboard.SetText(ex.Message);
                    MessageBox.Show(ex.Message, "Error Saving Schematic");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        #endregion

        #region Drop File

        private async void SourceImage_DragDrop(object sender, DragEventArgs e)
        {
            var data = e.Data.GetData(DataFormats.FileDrop);
            if (data != null)
            {
                var filenames = data as string[];
                if (filenames.Length > 0)
                {
                    string filePath = filenames[0];

                    // Get File Extension
                    string fileExtension = Path.GetExtension(filePath).ToLower();

                    if (fileExtension == ".xml")
                    {
                        // Try Loading XML File
                        try
                        {
                            // Update UI
                            FilterName.Text = Path.GetFileName(filePath);

                            // Load the XML file
                            var blockData = new List<BlockData>();
                            using (var stream = File.OpenRead(filePath))
                            {
                                blockData = await ReadDataAsync(stream, null);
                            }

                            // Set the color filters to the new data.
                            BlockDataList = blockData;
                            ClrsBlockData = blockData;

                            // Update the color filter.
                            await BuildColorFilter(false);

                            // MessageBox.Show("Loaded the XML file successfully.");
                        }
                        catch (Exception)
                        {
                            MessageBox.Show("ERROR: Loading XML file failed!");
                            return;
                        }
                    }
                    else
                    {
                        // Try Loading Images with Scaling (Same as original behavior)
                        try
                        {
                            using (Bitmap originalBitmap = new Bitmap(filePath))
                            {
                                CurrentImageData.Text = filePath;

                                // Update the original source to the new photo.
                                OriginalImageSource = new Bitmap(originalBitmap);

                                // Determine zoom factor
                                int zoomFactor = (int)NewRatioPercentLevelValue.Value;
                                if (zoomFactor == 0)
                                {
                                    SourceImage.Image = new Bitmap(originalBitmap);
                                }
                                else
                                {
                                    // Calculate new size based on zoom factor
                                    Size newSize = (zoomFactor > 0)
                                        ? new Size(originalBitmap.Width * zoomFactor, originalBitmap.Height * zoomFactor)
                                        : new Size(originalBitmap.Width / -zoomFactor, originalBitmap.Height / -zoomFactor);

                                    SourceImage.Image = new Bitmap(originalBitmap, newSize);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            MessageBox.Show("ERROR: Loading image failed!");
                            return;
                        }
                    }
                }
            }
        }


        private void SourceImage_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }
        #endregion

        #endregion

        #region Grid Options

        // Change grid color
        private void PickGridColor_Click(object sender, EventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                GridColor = colorDialog.Color;

                // Set the button's foreground color.
                PickGridColor.BackColor = colorDialog.Color;
            }
        }
        #endregion

        #region Color Filter Tools

        #region Delete Null Colors

        private async void DelNullColors_Click(object sender, EventArgs e)
        {
            // Check if image was rendered.
            if (ConvertedImage.Image == null)
            {
                MessageBox.Show("The rendered image is empty! Generate a pixelart first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Disable all controls on the main form.
            foreach (Control control in this.Controls)
            {
                control.Enabled = false;
            }

            // Reset Progress (if using percentage-based progress).
            MainProgressBar.Minimum = 0;
            MainProgressBar.Maximum = 100;
            MainProgressBar.Value = 0;

            // Variables for later UI update
            List<BlockData> filteredClrsBlockData = null;
            int totalBlockDataCount = BlockDataList.Count;

            await Task.Run(() =>
            {
                // Do heavy work on the background thread.

                // Create a set to track unique colors from the image.
                HashSet<Color> uniqueColors = new HashSet<Color>();

                // Access the image once from the UI thread.
                Bitmap img = null;
                Invoke((Action)(() =>
                {
                    img = new Bitmap(ConvertedImage.Image);
                }));

                // Process the image on the background thread.
                int totalPixels = img.Width * img.Height;
                int processedPixels = 0;
                for (int i = 0; i < img.Width; i++)
                {
                    for (int j = 0; j < img.Height; j++)
                    {
                        uniqueColors.Add(img.GetPixel(i, j));
                        processedPixels++;

                        // Update progress occasionally.
                        if (processedPixels % (totalPixels / 100) == 0)
                        {
                            int progress = (int)((processedPixels / (float)totalPixels) * 100);
                            Invoke((Action)(() =>
                            {
                                MainProgressBar.Value = progress;
                            }));
                        }
                    }
                }

                // Filter ClrsBlockData based on the unique colors found.
                filteredClrsBlockData = new List<BlockData>();
                foreach (var entry in ClrsBlockData)
                {
                    Color color = ColorTranslator.FromHtml(entry.Color);

                    if (entry.WasPicked)
                        color = entry.PickedColor;

                    if (uniqueColors.Contains(color))
                    {
                        filteredClrsBlockData.Add(entry);
                    }
                }
            });

            // Update UI with the filtered data.
            TotalColorsData.Text = BlockDataList.Count.ToString();
            FilteredColorsData.Text = filteredClrsBlockData.Count.ToString();

            // Update your data.
            ClrsBlockData = filteredClrsBlockData;

            // Set progress to complete.
            MainProgressBar.Value = 100;

            // Re-enable all controls on the main form.
            foreach (Control control in this.Controls)
            {
                control.Enabled = true;
            }
        }
        #endregion

        #region Delete Color

        private void DeleteColor_Click(object sender, EventArgs e)
        {
            // Check if image was rendered.
            if (ConvertedImage.Image == null)
            {
                MessageBox.Show("The rendered image is empty! Generate a pixelart first!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DeleteColor.Enabled = false;
            DeleteColor.Text = "Click A Location";
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
            {
                Interval = 1
            };
            t.Tick += new EventHandler(DeleteColorpicker_Tick);
            t.Start();
        }

        async void DeleteColorpicker_Tick(object sender, EventArgs e)
        {
            try
            {
                // Get the bitmap of the image.
                Bitmap b = ((Bitmap)ConvertedImage.Image);

                // Get the current position of the cursor within the form (client area).
                Rectangle imgBounds = GetImageBounds(ConvertedImage);
                Point cp = ConvertedImage.PointToClient(Cursor.Position);

                if (!imgBounds.Contains(cp))
                    return;

                float scaleX = (float)ConvertedImage.Image.Width / imgBounds.Width;
                float scaleY = (float)ConvertedImage.Image.Height / imgBounds.Height;

                int x = (int)((cp.X - imgBounds.Left) * scaleX);
                int y = (int)((cp.Y - imgBounds.Top) * scaleY);

                // Ensure the coordinates are within the bounds of the image.
                if (x >= 0 && x < b.Width && y >= 0 && y < b.Height)
                {
                    // Get the pixel color at the calculated position.
                    Color c = b.GetPixel(x, y);
                    DeleteColor.BackColor = c;
                }
            }
            catch (Exception)
            {
                // Handle any errors, such as invalid coordinates or image not loaded.
            }

            // Check for left-click.
            if (GetKeyState((int)Keys.LButton) < 0)
            {
                try
                {
                    // Get the bitmap of the image.
                    Bitmap b = ((Bitmap)ConvertedImage.Image);

                    // Get the current position of the cursor within the form (client area).
                    Rectangle imgBounds = GetImageBounds(ConvertedImage);
                    Point cp = ConvertedImage.PointToClient(Cursor.Position);

                    if (!imgBounds.Contains(cp))
                        return;

                    float scaleX = (float)ConvertedImage.Image.Width / imgBounds.Width;
                    float scaleY = (float)ConvertedImage.Image.Height / imgBounds.Height;

                    int x = (int)((cp.X - imgBounds.Left) * scaleX);
                    int y = (int)((cp.Y - imgBounds.Top) * scaleY);

                    // Ensure the coordinates are within the bounds of the image.
                    if (x >= 0 && x < b.Width && y >= 0 && y < b.Height)
                    {
                        // Get the pixel color at the calculated position.
                        Color c = b.GetPixel(x, y);
                        GetCustomColor = c;

                        // Stop the System.Windows.Forms.Timer.
                        ((System.Windows.Forms.Timer)sender).Stop();

                        // Check if the color was transparent.
                        if ($"{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" == "00000000")
                            goto Cancel;

                        // Replace blocks with the selected color and update their block ID.
                        List<BlockData> updatedColorData = new List<BlockData>();

                        // Check if the clicked color was a picked color.
                        bool isPickedColor = ClrsBlockData.Any(entry => entry.WasPicked && entry.PickedColor == GetCustomColor);

                        foreach (var entry in ClrsBlockData)
                        {
                            Color color = ColorTranslator.FromHtml(entry.Color);

                            // If the color was picked, set the color to the matching block color for this ID.
                            if (entry.WasPicked)
                                color = entry.PickedColor;

                            // Remove all instances of the picked color if it was part of a selection.
                            if (isPickedColor)
                            {
                                if (color.ToArgb() == GetCustomColor.ToArgb() && entry.WasPicked)
                                {
                                    // Confirm to skip adding this entry.
                                    if (MessageBox.Show($"Do you want to remove the following block?\n\n" +
                                                        $"Block: '{entry.Name} ({entry.Id})'\n" +
                                                        $"Color: '{entry.Color}'.\n" +
                                                        $"Was Picked: '{entry.WasPicked}'",
                                                        "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes
                                    ) continue;
                                    {
                                        // Cancel searching after first match.
                                        // If using unique colors, there will only be one match.
                                        if (UniqueColors.Checked)
                                            goto Cancel;
                                    }
                                }
                            }
                            else
                            {
                                // Only remove the clicked color if it was not a picked color.
                                if (color.ToArgb() == GetCustomColor.ToArgb())
                                {
                                    // Confirm to skip adding this entry.
                                    if (MessageBox.Show($"Do you want to remove the following block?\n\n" +
                                                        $"Block: '{entry.Name} ({entry.Id})'\n" +
                                                        $"Color: '{entry.Color}'.\n" +
                                                        $"Was Picked: '{entry.WasPicked}'",
                                                        "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes
                                    ) continue;
                                    {
                                        // Cancel searching after first match.
                                        // If using unique colors, there will only be one match.
                                        if (UniqueColors.Checked)
                                            goto Cancel;
                                    }
                                }
                            }

                            // Keep the existing entry.
                            updatedColorData.Add(entry);
                        }

                        // Update colorData with the new list after replacing the block IDs.
                        ClrsBlockData = updatedColorData;

                        // Jump label to change the controls and return.
                        Cancel:

                        // Update controls.
                        DeleteColor.BackColor = SystemColors.Control;
                        DeleteColor.Text = "Delete Color";
                        DeleteColor.BackColor = Color.FromArgb(127, 127, 127);
                        DeleteColor.Enabled = true;

                        // Update the color list.
                        await BuildColorFilter(false);
                    }
                }
                catch (Exception)
                {
                    // Handle any errors, such as invalid coordinates or image not loaded.

                    DeleteColor.BackColor = SystemColors.Control;
                    DeleteColor.Text = "Delete Color";
                    DeleteColor.BackColor = Color.FromArgb(127, 127, 127);
                    DeleteColor.Enabled = true;
                    return;
                }
            }
            else if (GetKeyState((int)Keys.RButton) < 0)
            {
                // Stop the System.Windows.Forms.Timer.
                ((System.Windows.Forms.Timer)sender).Stop();

                // Cancel the pick.
                DeleteColor.BackColor = SystemColors.Control;
                DeleteColor.Text = "Delete Color";
                DeleteColor.BackColor = Color.FromArgb(127, 127, 127);
                DeleteColor.Enabled = true;
                return;
            }
        }
        #endregion

        #region Reset Colors

        private async void ResetColors_Click(object sender, EventArgs e)
        {
            await BuildColorFilter();

            // This gets annoying so its disabled.
            // MessageBox.Show("The color filter has been reset.");
        }
        #endregion

        #region Load Color Filter

        // Load Color Filter from XML
        private async void LoadColorFilter_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                Filter = "Color Filter Files (*.xml)|*.xml",
                Title = "Open Color Filter File"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog1.FileName;

                try
                {
                    // Update UI
                    FilterName.Text = Path.GetFileName(filePath);

                    // Load the XML file
                    var blockData = new List<BlockData>();
                    using (var stream = File.OpenRead(filePath))
                    {
                        blockData = await ReadDataAsync(stream, null);
                    }

                    // Set the color filters to the new data.
                    BlockDataList = blockData;
                    ClrsBlockData = blockData;

                    // Update the color filter.
                    await BuildColorFilter(false);

                    MessageBox.Show("Loaded the XML file successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"ERROR: Loading color filter failed!\n{ex.Message}");
                }
            }
        }
        #endregion

        #region Save Color Filter

        // Save Current Filter as XML.
        private void SaveColorFilter_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog
            {
                Filter = "Color Filter|*.xml",
                Title = "Save a Color Filter File"
            };

            if (saveFileDialog1.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(saveFileDialog1.FileName))
            {
                try
                {
                    // Create an XML structure
                    XDocument doc = new XDocument(
                        new XDeclaration("1.0", "utf-8", "yes"),
                        new XElement("Colors",
                            new XElement("Blocks",
                                ClrsBlockData.Select(block =>
                                    new XElement("Block",
                                        new XAttribute("Id", block.Id),
                                        new XAttribute("Name", block.Name),
                                        new XAttribute("Color", block.Color)
                                    )
                                )
                            )
                        )
                    );

                    // Save to file
                    doc.Save(saveFileDialog1.FileName);

                    MessageBox.Show("Color Filter Saved Successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}");
                }
            }
        }
        #endregion

        #endregion

        #region Main Pixelart Conversion

        private async Task ConvertPixelArt(bool buildSchematic = false, CancellationToken cancellationToken = default)
        {
            Bitmap btm = new Bitmap(1, 1);
            Bitmap bBt = new Bitmap(1, 1);
            Graphics g = null;

            // Disable all controls on main form excluding certain controls.
            SetEnabledState(this, false, new List<string> { "MainForm", "MainGrid", "BasicConfigurationGroupBox", "ConvertToPixelArt", "PixelArtStatisticsGroupBox", "MainProgressBar" });

            bool isProgressBarChecked = false;
            bool isGatherStatisticsChecked = false;
            bool isNearestNeighborChecked = false;
            bool isBicubicChecked = false;
            bool isLanczosChecked = false;
            bool isHermiteChecked = false;
            bool isSplineChecked = false;
            bool isGaussianChecked = false;
            bool isBackdropChecked = false;
            bool isShowGridChecked = false;
            bool isRotation90Checked = false;
            bool isRotation270Checked = false;
            int AEqualsValue = 3;
            double SigmaEqualsValue = 1.0;

            Invoke((Action)(() =>
            {
                isProgressBarChecked = (bool)ShowProgress.Checked;
                isGatherStatisticsChecked = (bool)GatherStatistics.Checked;
                isNearestNeighborChecked = (bool)NearestNeighbor.Checked;
                isBicubicChecked = (bool)Bicubic.Checked;
                isLanczosChecked = (bool)Lanczos.Checked;
                isHermiteChecked = (bool)Hermite.Checked;
                isSplineChecked = (bool)Spline.Checked;
                isGaussianChecked = (bool)Gaussian.Checked;
                isBackdropChecked = (bool)Backdrop.Checked;
                isShowGridChecked = (bool)ShowGrid.Checked;
                isRotation90Checked = (bool)Rotation90.Checked;
                isRotation270Checked = (bool)Rotation270.Checked;
                AEqualsValue = int.Parse(AEquals.Text);
                SigmaEqualsValue = double.Parse(SigmaEquals.Text);
            }));

            try
            {
                // Populate the colorlist with custom definitions.
                // await BuildColorFilter();

                int num = int.Parse(SpacingValue.Text);

                btm = new Bitmap(SourceImage.Image);
                bBt = new Bitmap(btm.Width, btm.Height);

                // Initialize variables for stats and conversion.
                int renderedCount = 0;

                // Reset the progress bar.
                // int _stepValue = 1;

                // Calculate the total number of non-transparent pixels.
                int totalNonTransparentPixels = CountNonTransparentPixels(btm);

                // Calculate the number of blocks (steps) based on the block size 'num'.
                int blocksPerRow = (btm.Width + num - 1) / num;      // Total blocks in a row.    // Width.
                int blocksPerColumn = (btm.Height + num - 1) / num;  // Total blocks in a column. // Height.
                int totalBlocks = blocksPerRow * blocksPerColumn;

                // Define new schematic data.
                if (buildSchematic)
                {
                    // Remove the old schematic.
                    schematicData.Clear();

                    // Reset the old schematic warning.
                    isSchematicOld = false;
                }
                else
                    if (schematicData.Count != 0) isSchematicOld = true; // Set a warning for old schematic data.

                Invoke((Action)(() =>
                {
                    TotalHeightData.Text = (isRotation90Checked || isRotation270Checked) ? blocksPerRow.ToString() : blocksPerColumn.ToString(); // Check if axis is rotated.
                    TotalWidthData.Text = (isRotation90Checked || isRotation270Checked) ? blocksPerColumn.ToString() : blocksPerRow.ToString();  // Check if axis is rotated.
                    TotalBlocksData.Text = "Calculating...";
                }));

                if (isProgressBarChecked)
                {
                    Invoke((Action)(() =>
                    {
                        MainProgressBar.Minimum = 0;
                        MainProgressBar.Value = 0;
                        MainProgressBar.Maximum = totalBlocks; // Set the maximum value based on the total number of blocks.
                    }));
                }
                else
                {
                    Invoke((Action)(() =>
                    {
                        MainProgressBar.Minimum = 0;
                        MainProgressBar.Value = 0;
                    }));
                }

                try
                {
                    await Task.Run(() =>
                    {
                        using (g = Graphics.FromImage(bBt))
                        {
                            List<Color> block = new List<Color>();
                            Color final = Color.Lime;

                            int progressCounter = 0;
                            int progressUpdateInterval = Math.Max(totalBlocks / 100, 1); // Update progress every 1%. // Ensure progressUpdateInterval is at least 1.

                            for (int x = 0; x < btm.Width; x += num)
                            {
                                for (int y = 0; y < btm.Height; y += num)
                                {
                                    // Check for cancellation.
                                    cancellationToken.ThrowIfCancellationRequested();

                                    // Get interpolation type.
                                    if (isNearestNeighborChecked)
                                    {
                                        final = NearestNeighborInterpolation(btm, x, y, num, isBackdropChecked);
                                    }
                                    else if (isBicubicChecked)
                                    {
                                        final = BicubicInterpolation(btm, x, y, isBackdropChecked);
                                    }
                                    else if (isLanczosChecked)
                                    {
                                        final = LanczosInterpolation(btm, x, y, num, AEqualsValue, isBackdropChecked); // Lanczos with a=3.
                                    }
                                    else if (isHermiteChecked)
                                    {
                                        final = HermiteInterpolation(btm, x, y, isBackdropChecked);
                                    }
                                    else if (isSplineChecked)
                                    {
                                        final = SplineInterpolation(btm, x, y, num, isBackdropChecked);
                                    }
                                    else if (isGaussianChecked)
                                    {
                                        final = GaussianInterpolation(btm, x, y, SigmaEqualsValue, isBackdropChecked); // Example with sigma=1.0.
                                    }
                                    else // Else use Bilinear (defualt).
                                    {
                                        final = BilinearInterpolation(btm, x, y, num, isBackdropChecked);
                                    }

                                    // Convert final color to HTML string with alpha channel as "FF".
                                    Color blockColor = Color.Transparent;
                                    BlockData matchedBlock = null;

                                    // Not using linq on this to increase speed.
                                    if (final.A != 0)
                                    {
                                        string targetColor = $"#FF{final.R:X2}{final.G:X2}{final.B:X2}";

                                        foreach (BlockData blockData in ClrsBlockData)
                                        {
                                            if (blockData.Color.Equals(targetColor, StringComparison.OrdinalIgnoreCase))
                                            {
                                                matchedBlock = blockData;
                                                break; // Exit loop early once a match is found
                                            }
                                        }

                                        blockColor = (matchedBlock != null && matchedBlock.WasPicked)
                                            ? matchedBlock.PickedColor
                                            : Color.FromArgb(255, final.R, final.G, final.B);
                                    }

                                    /*
                                    // Linq version for archiving.
                                    if (final.A != 0)
                                    {
                                        matchedBlock = ClrsBlockData
                                            .FirstOrDefault(blockData => blockData.Color.Equals($"#FF{final.R:X2}{final.G:X2}{final.B:X2}", StringComparison.OrdinalIgnoreCase));

                                        blockColor = (matchedBlock != null && matchedBlock.WasPicked) ? matchedBlock.PickedColor : Color.FromArgb(255, final.R, final.G, final.B);
                                    }
                                    */

                                    // Record schematic data. There should not be any transparent blocks.
                                    if (final.A != 0)
                                    {
                                        SolidBrush sb = new SolidBrush(blockColor);
                                        Rectangle rec = new Rectangle(x, y, num, num);
                                        g.FillRectangle(sb, rec);
                                    }

                                    // Build schematic from final & GeneratedSchematic.
                                    if (buildSchematic)
                                    {
                                        // Define position data.
                                        Vector3 positionData = new Vector3(x / num, y / num, 0);

                                        if (final.A == 0)
                                        {
                                            // Set schematic data as "air".
                                            if (isBackdropChecked)
                                                schematicData.Add(new Tuple<Vector3, int>(positionData, 0));
                                        }
                                        else
                                        {
                                            if (matchedBlock != null)
                                            {
                                                // Build schematic.
                                                schematicData.Add(new Tuple<Vector3, int>(positionData, matchedBlock.Id));

                                            } // There should never be a case where it does not find the block!
                                        }
                                    }

                                    // Gather statistics.
                                    if (isGatherStatisticsChecked && final.A != 0)
                                        renderedCount++;

                                    // Progress progress bar.
                                    if (isProgressBarChecked)
                                    {
                                        progressCounter++;
                                        if (progressCounter % progressUpdateInterval == 0)
                                        {
                                            Invoke((Action)(() =>
                                            {
                                                MainProgressBar.Value = Math.Min(MainProgressBar.Value + progressUpdateInterval, MainProgressBar.Maximum);
                                            }));
                                        }
                                    }
                                }
                            }

                            // After the loop, re-anchor the schematic so the lowest coordinates are 0.
                            int rotation = 0;
                            if (Rotation90.Checked) rotation = 90;
                            if (Rotation180.Checked) rotation = 180;
                            if (Rotation270.Checked) rotation = 270;

                            schematicData = RotateSchematic(schematicData, rotation, XAxis.Checked, false, Flat.Checked);

                            // Grid function.
                            if (isShowGridChecked)
                            {
                                Invoke((Action)(() =>
                                {
                                    HighlightCells(g, bBt.Width, num, int.Parse(GridX.Text), int.Parse(GridY.Text));
                                }));
                            }

                            // Apply rotation if needed.
                            Bitmap rotatedImage = new Bitmap(bBt);
                            Invoke((Action)(() =>
                            {
                                ApplyRotation(rotatedImage);
                            }));

                            Invoke((Action)(() =>
                            {
                                // Render Box
                                ConvertedImage.Image = rotatedImage;

                                // Populate Stats.
                                if (isGatherStatisticsChecked)
                                {
                                    TotalBlocksData.Text = renderedCount.ToString();
                                }

                                // Enable schematic controls.
                                if (buildSchematic)
                                {
                                    // CopyToClipboard.IsEnabled = true;
                                    // OverwriteExistingFile.IsEnabled = true;
                                    GenerateSchematic.Checked = true;
                                }
                            }));
                        }

                        // End section.
                    }, cancellationToken); // Pass the cancellation token to Task.Run.
                }
                catch (OperationCanceledException)
                {
                    // Opteration was cancled, show message.
                    MessageBox.Show("Rendering operation was cancled!");

                    // Update some controls.
                    TotalBlocksData.Text = "0";
                }
            }
            catch (Exception ex)
            {
                Invoke((Action)(() =>
                {
                    MainProgressBar.Value = 0;
                    MessageBox.Show("ERROR: The color filter formatting is invalid!" + ex.ToString());
                    System.Windows.Forms.Clipboard.SetText(ex.ToString());
                }));
            }

            // Update Progress
            if (isProgressBarChecked)
            {
                Invoke((Action)(() =>
                {
                    MainProgressBar.Value = MainProgressBar.Maximum;
                }));
            }

            // Enable all controls on main form.
            SetEnabledState(this, true);
        }
        #endregion

        #region Supporting Conversion Logic

        #region Scaling Interpolations

        private Color NearestNeighborInterpolation(Bitmap bmp, int x, int y, int num, bool includeTransparent)
        {
            int nx = Math.Min(x + num / 2, bmp.Width - 1);
            int ny = Math.Min(y + num / 2, bmp.Height - 1);
            Color nearestColor = bmp.GetPixel(nx, ny);

            if (includeTransparent || nearestColor.A != 0)
            {
                if (includeTransparent && nearestColor.A == 0)
                    nearestColor = Color.Lime; // Default color for transparency, or you can choose another default color.

                return Clr(new Color[] { nearestColor });
            }
            return nearestColor;
        }

        private Color BilinearInterpolation(Bitmap bmp, int x, int y, int num, bool includeTransparent)
        {
            List<Color> block = new List<Color>();

            for (int v = 0; v < num; v++)
            {
                for (int c = 0; c < num; c++)
                {
                    if (x + v < bmp.Width && y + c < bmp.Height)
                    {
                        Color color = bmp.GetPixel(x + v, y + c);
                        if (includeTransparent || color.A != 0)
                        {
                            if (includeTransparent && color.A == 0)
                                color = Color.Lime; // Default color for transparency, or you can choose another default color.
                            block.Add(color);
                        }
                    }
                }
            }

            if (block.Count == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            return Clr(block.ToArray());
        }

        private Color BicubicInterpolation(Bitmap bmp, int x, int y, bool includeTransparent)
        {
            double[] dx = new double[4];
            double[] dy = new double[4];

            for (int i = 0; i < 4; i++)
            {
                dx[i] = CubicPolynomial(i - 1);
                dy[i] = CubicPolynomial(i - 1);
            }

            double r = 0, g = 0, b = 0;
            double sum = 0;
            for (int i = -1; i <= 2; i++)
            {
                for (int j = -1; j <= 2; j++)
                {
                    int px = Math.Min(Math.Max(x + i, 0), bmp.Width - 1);
                    int py = Math.Min(Math.Max(y + j, 0), bmp.Height - 1);
                    Color pixel = bmp.GetPixel(px, py);

                    if (includeTransparent || pixel.A != 0)
                    {
                        if (includeTransparent && pixel.A == 0)
                            pixel = Color.Lime; // Default color for transparency, or you can choose another default color.

                        double coeff = dx[i + 1] * dy[j + 1];
                        r += pixel.R * coeff;
                        g += pixel.G * coeff;
                        b += pixel.B * coeff;
                        sum += coeff;
                    }
                }
            }

            if (sum == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            Color interpolatedColor = Color.FromArgb(Clamp(r / sum), Clamp(g / sum), Clamp(b / sum));
            return Clr(new Color[] { interpolatedColor });
        }

        private Color LanczosInterpolation(Bitmap bmp, int x, int y, int num, int a, bool includeTransparent)
        {
            double r = 0, g = 0, b = 0;
            double sum = 0;

            for (int i = -a + 1; i <= a; i++)
            {
                for (int j = -a + 1; j <= a; j++)
                {
                    int px = Math.Min(Math.Max(x + i, 0), bmp.Width - 1);
                    int py = Math.Min(Math.Max(y + j, 0), bmp.Height - 1);
                    Color pixel = bmp.GetPixel(px, py);

                    if (includeTransparent || pixel.A != 0)
                    {
                        if (includeTransparent && pixel.A == 0)
                            pixel = Color.Lime; // Default color for transparency, or you can choose another default color.

                        double lanczosWeight = LanczosKernel(i / (double)num, a) * LanczosKernel(j / (double)num, a);
                        r += pixel.R * lanczosWeight;
                        g += pixel.G * lanczosWeight;
                        b += pixel.B * lanczosWeight;
                        sum += lanczosWeight;
                    }
                }
            }

            if (sum == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            Color interpolatedColor = Color.FromArgb(Clamp(r / sum), Clamp(g / sum), Clamp(b / sum));
            return Clr(new Color[] { interpolatedColor });
        }

        private Color HermiteInterpolation(Bitmap bmp, int x, int y, bool includeTransparent)
        {
            double[] dx = new double[2];
            double[] dy = new double[2];

            dx[0] = HermitePolynomial(0);
            dx[1] = HermitePolynomial(1);

            dy[0] = HermitePolynomial(0);
            dy[1] = HermitePolynomial(1);

            double r = 0, g = 0, b = 0;
            double sum = 0;
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int px = Math.Min(Math.Max(x + i, 0), bmp.Width - 1);
                    int py = Math.Min(Math.Max(y + j, 0), bmp.Height - 1);
                    Color pixel = bmp.GetPixel(px, py);

                    if (includeTransparent || pixel.A != 0)
                    {
                        if (includeTransparent && pixel.A == 0)
                            pixel = Color.Lime; // Default color for transparency, or you can choose another default color.

                        double coeff = dx[i] * dy[j];
                        r += pixel.R * coeff;
                        g += pixel.G * coeff;
                        b += pixel.B * coeff;
                        sum += coeff;
                    }
                }
            }

            if (sum == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            Color interpolatedColor = Color.FromArgb(Clamp(r / sum), Clamp(g / sum), Clamp(b / sum));
            return Clr(new Color[] { interpolatedColor });
        }

        private Color SplineInterpolation(Bitmap bmp, int x, int y, int num, bool includeTransparent)
        {
            List<Color> block = new List<Color>();
            double r = 0, g = 0, b = 0;
            double sum = 0;

            for (int i = -1; i <= 2; i++)
            {
                for (int j = -1; j <= 2; j++)
                {
                    int px = Math.Min(Math.Max(x + i, 0), bmp.Width - 1);
                    int py = Math.Min(Math.Max(y + j, 0), bmp.Height - 1);
                    Color pixel = bmp.GetPixel(px, py);

                    if (includeTransparent || pixel.A != 0)
                    {
                        if (includeTransparent && pixel.A == 0)
                            pixel = Color.Lime; // Default color for transparency, or you can choose another default color.

                        double coeff = SplineKernel(i / (double)num) * SplineKernel(j / (double)num);
                        r += pixel.R * coeff;
                        g += pixel.G * coeff;
                        b += pixel.B * coeff;
                        sum += coeff;
                        block.Add(pixel);
                    }
                }
            }

            if (sum == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            Color interpolatedColor = Color.FromArgb(Clamp(r / sum), Clamp(g / sum), Clamp(b / sum));
            return Clr(new Color[] { interpolatedColor });
        }

        private Color GaussianInterpolation(Bitmap bmp, int x, int y, double sigma, bool includeTransparent)
        {
            double r = 0, g = 0, b = 0;
            double sum = 0;

            int radius = (int)Math.Ceiling(3 * sigma); // 3-sigma rule to determine kernel size

            for (int i = -radius; i <= radius; i++)
            {
                for (int j = -radius; j <= radius; j++)
                {
                    int px = Math.Min(Math.Max(x + i, 0), bmp.Width - 1);
                    int py = Math.Min(Math.Max(y + j, 0), bmp.Height - 1);
                    Color pixel = bmp.GetPixel(px, py);

                    if (includeTransparent || pixel.A != 0)
                    {
                        if (includeTransparent && pixel.A == 0)
                            pixel = Color.Lime; // Default color for transparency, or you can choose another default color.

                        double weight = GaussianKernel(i, j, sigma);
                        r += pixel.R * weight;
                        g += pixel.G * weight;
                        b += pixel.B * weight;
                        sum += weight;
                    }
                }
            }

            if (sum == 0)
            {
                // Return transparent if no valid pixels are found.
                return Color.Transparent;
            }

            Color interpolatedColor = Color.FromArgb(Clamp(r / sum), Clamp(g / sum), Clamp(b / sum));
            return Clr(new Color[] { interpolatedColor });
        }

        private double HermitePolynomial(double x)
        {
            if (x == 0) return 1.0;
            if (x < 0) x = -x;
            double x3 = x * x * x;
            double x2 = x * x;
            return (2 * x3) - (3 * x2) + 1;
        }

        private double LanczosKernel(double x, int a)
        {
            if (x == 0) return 1.0;
            if (x < -a || x > a) return 0.0;

            x *= Math.PI;
            return a * Math.Sin(x) * Math.Sin(x / a) / (x * x);
        }

        private double CubicPolynomial(double x)
        {
            x = Math.Abs(x);
            if (x <= 1)
                return 1.5 * x * x * x - 2.5 * x * x + 1;
            else if (x < 2)
                return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
            else
                return 0;
        }

        private double SplineKernel(double x)
        {
            x = Math.Abs(x);
            if (x <= 1)
            {
                return 1.0 - 2.0 * x * x + x * x * x;
            }
            else if (x < 2)
            {
                return 4.0 - 8.0 * x + 5.0 * x * x - x * x * x;
            }
            return 0.0;
        }

        private double GaussianKernel(int x, int y, double sigma)
        {
            double expPart = Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
            double normalPart = 1 / (2 * Math.PI * sigma * sigma);
            return normalPart * expPart;
        }

        private int Clamp(double value)
        {
            return (int)Math.Max(0, Math.Min(255, value));
        }
        #endregion

        #region Build Color Filter

        public async Task BuildColorFilter(bool loadDefaultList = true)
        {
            // Overwrite original with main filter file.
            if (loadDefaultList)
            {
                BlockDataList = new List<BlockData>();

                // Ensure the file exists on disk (extract from embedded once), then read it.
                var path = BlockColorsStorage.EnsureOnDisk();
                using (var stream = File.OpenRead(path))
                {
                    BlockDataList = await ReadDataAsync(stream, null).ConfigureAwait(false);
                }
            }

            await Task.Run(() =>
            {
                bool uniqueColors = true;

                Invoke((Action)(() =>
                {
                    uniqueColors = (bool)UniqueColors.Checked;
                }));

                var filteredList = BlockDataList.ToList();
                if (!loadDefaultList && ClrsBlockData.Count != 0) filteredList = ClrsBlockData;

                if (uniqueColors)
                {
                    filteredList = filteredList
                        .GroupBy(data => data.Color)
                        .Select(group => group.First())
                        .ToList();
                }

                // Populate filter data.
                Invoke((Action)(() =>
                {
                    TotalColorsData.Text = BlockDataList.Count.ToString();
                    FilteredColorsData.Text = filteredList.Count.ToString();
                }));

                // Set the filtered colors count.
                int colorCount = 0;
                Clrs = new Color[filteredList.Count];
                foreach (var item in filteredList)
                {
                    try
                    {
                        Clrs[colorCount] = ColorTranslator.FromHtml(item.Color);
                    }
                    catch
                    {
                        Clrs[colorCount] = Color.Transparent;
                    }
                    colorCount++;
                }

                // Create a backup of the new filtered colors for exporting schematics.
                ClrsBlockData = filteredList;
            });
        }

        static class BlockColorsStorage
        {
            // Destination: <Game>\!Mods\WorldEditPixelart\BlockColors.xml.
            private static readonly string ModDir =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(WorldEditPixelart.WorldEditPixelart).Namespace, "ColorPalette"); // Using the hosts namespace.
            private static readonly string TargetPath =
                Path.Combine(ModDir, "BlockColors.xml");

            // Extracts from embedded resource if missing. Returns the on-disk path.
            public static string EnsureOnDisk()
            {
                Directory.CreateDirectory(ModDir);

                if (!File.Exists(TargetPath))
                {
                    using (var src = OpenEmbedded())
                    {
                        if (src == null)
                            throw new FileNotFoundException(
                                "Embedded BlockColors.xml not found in the mod assembly."
                            );

                        using (var dst = File.Open(TargetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            src.CopyTo(dst);
                    }
                }

                return TargetPath;
            }

            // Optional: Call this if you want to force a reset to the embedded defaults.
            public static void OverwriteFromEmbedded()
            {
                Directory.CreateDirectory(ModDir);
                using (var src = OpenEmbedded() ?? throw new FileNotFoundException("Embedded BlockColors.xml not found."))
                using (var dst = File.Open(TargetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    src.CopyTo(dst);
            }

            // Finds and opens the embedded BlockColors.xml stream.
            private static Stream OpenEmbedded()
            {
                // Use the hosts namespace.
                var asm = typeof(WorldEditPixelart.WorldEditPixelart).Assembly;

                var resName = asm.GetManifestResourceNames()
                                 .FirstOrDefault(n => n.EndsWith("BlockColors.xml",
                                           System.StringComparison.OrdinalIgnoreCase));

                return resName != null ? asm.GetManifestResourceStream(resName) : null;
            }
        }
        #endregion

        #region Color Comparator

        public Color Clr(Color[] cs)
        {
            int r = 0;
            int g = 0;
            int b = 0;

            for (int i = 0; i < cs.Length; i++)
            {
                r += cs[i].R;
                g += cs[i].G;
                b += cs[i].B;
            }

            r /= cs.Length;
            g /= cs.Length;
            b /= cs.Length;

            int near = 1000;
            int ind = 0;

            for (int cl = 0; cl < Clrs.Length; cl++)
            {
                int valR = (Clrs[cl].R - r);
                int valG = (Clrs[cl].G - g);
                int valB = (Clrs[cl].B - b);

                if (valR < 0) valR = -valR;
                if (valG < 0) valG = -valG;
                if (valB < 0) valB = -valB;

                int total = valR + valG + valB;

                if (total < near)
                {
                    ind = cl;
                    near = total;
                }
            }

            Color c = Clrs[ind];

            return c;
        }
        #endregion

        #region Rotation & Grid Helpers

        HashSet<Tuple<Vector3, int>> RotateSchematic(
            HashSet<Tuple<Vector3, int>> schemData,
            int rotation,
            bool flipX,
            bool flipY,
            bool flat)
        {
            // Use the bottom-left of the schematic as pivot.
            float pivotX = schemData.Min(t => t.Item1.X);
            float pivotY = schemData.Min(t => t.Item1.Y);

            // First, translate all coordinates so that pivot becomes (0,0).
            var translatedData = schemData.Select(data =>
            {
                Vector3 pos = new Vector3(data.Item1.X - pivotX, data.Item1.Y - pivotY, data.Item1.Z);
                return new Tuple<Vector3, int>(pos, data.Item2);
            }).ToList();

            // Apply rotation and flip about (0,0).
            HashSet<Tuple<Vector3, int>> transformedData = new HashSet<Tuple<Vector3, int>>();
            foreach (var data in translatedData)
            {
                float x = data.Item1.X;
                float y = data.Item1.Y;
                float z = data.Item1.Z;
                Vector3 transformedPos;

                // Rotate around the origin (0,0).
                // The formulas assume a rotation in 90° increments.
                switch (rotation)
                {
                    case 90:
                        // 90° rotation: (x, y) -> (-y, -x).
                        transformedPos = new Vector3(-y, -x, z);
                        break;
                    case 180:
                        // 180° rotation: (x, y) -> (-x, y).
                        transformedPos = new Vector3(-x, y, z);
                        break;
                    case 270:
                        // 270° rotation: (x, y) -> (y, x).
                        transformedPos = new Vector3(y, x, z);
                        break;
                    default:
                        // 0° rotation: (x, y) -> (x, -y).
                        transformedPos = new Vector3(x, -y, z);
                        break;
                }

                // Apply flip operations.
                if (flipX)
                    transformedPos.X = -transformedPos.X;
                if (flipY)
                    transformedPos.Y = -transformedPos.Y;

                // If flat is checked, force Z to remain unchanged (or set to a specific flat level).
                if (flat)
                {
                    transformedPos = new Vector3(transformedPos.X, transformedPos.Z, transformedPos.Y);
                }

                transformedData.Add(new Tuple<Vector3, int>(transformedPos, data.Item2));
            }

            // Finally, re-anchor the rotated/flipped schematic so that its minimum is (0,0).
            float newMinX = transformedData.Min(t => t.Item1.X);
            float newMinY = transformedData.Min(t => t.Item1.Y);
            float newMinZ = transformedData.Min(t => t.Item1.Z);

            HashSet<Tuple<Vector3, int>> adjustedData = new HashSet<Tuple<Vector3, int>>();
            foreach (var data in transformedData)
            {
                Vector3 adjustedPos = new Vector3(data.Item1.X - newMinX, data.Item1.Y - newMinY, data.Item1.Z - newMinZ);
                adjustedData.Add(new Tuple<Vector3, int>(adjustedPos, data.Item2));
            }

            return adjustedData;
        }

        private void ApplyRotation(Bitmap temp)
        {
            // Rotate schematic.
            if ((bool)Rotation90.Checked)
                temp.RotateFlip(RotateFlipType.Rotate90FlipNone);
            else if ((bool)Rotation180.Checked)
                temp.RotateFlip(RotateFlipType.Rotate180FlipNone);
            else if ((bool)Rotation270.Checked)
                temp.RotateFlip(RotateFlipType.Rotate270FlipNone);

            // Flip the Y axis.
            if ((bool)YAxis.Checked)
                temp.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }

        // Grid.
        private void HighlightCells(Graphics g, int numOfCells, int cellSize, int offsetX, int offsetY)
        {
            Pen p = new Pen(PickGridColor.BackColor); // Maybe add controls to thickness?
            for (int y = 0; y < numOfCells; ++y)
            {
                g.DrawLine(p, 0, (y * cellSize) + offsetY, numOfCells * cellSize, (y * cellSize) + offsetY);
            }
            for (int x = 0; x < numOfCells; ++x)
            {
                g.DrawLine(p, (x * cellSize) + offsetX, 0, (x * cellSize) + offsetX, numOfCells * cellSize);
            }
        }

        private int CountNonTransparentPixels(Bitmap bitmap)
        {
            int nonTransparentPixelCount = 0;

            // Lock the bitmap's bits.
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                // Get the address of the first line.
                IntPtr ptr = bitmapData.Scan0;
                int bytes = Math.Abs(bitmapData.Stride) * bitmap.Height;
                byte[] rgbValues = new byte[bytes];

                // Copy the RGB values into the array.
                Marshal.Copy(ptr, rgbValues, 0, bytes);

                // Iterate through the pixel data.
                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    // The alpha channel is at index 3 in ARGB format.
                    if (rgbValues[i + 3] != 0)
                    {
                        nonTransparentPixelCount++;
                    }
                }
            }
            finally
            {
                // Unlock the bits.
                bitmap.UnlockBits(bitmapData);
            }

            return nonTransparentPixelCount;
        }
        #endregion

        #region Bitmap Converters

        public Bitmap BitmapFromSource(BitmapSource bitmapSource)
        {
            Bitmap bitmap;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new PngBitmapEncoder(); // Use PngBitmapEncoder for transparency support
                enc.Frames.Add(BitmapFrame.Create(bitmapSource));
                enc.Save(outStream);
                outStream.Seek(0, SeekOrigin.Begin);
                bitmap = new Bitmap(outStream);
            }
            return bitmap;
        }

        // Convert Bitmap to ImageSource.
        BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            BitmapImage bitmapimage = new BitmapImage();
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();
                bitmapimage.Freeze();
            }
            return bitmapimage;
        }
        #endregion

        #region Disable & Enable Controls

        public void SetEnabledState(Control element, bool isEnabled, IEnumerable<string> controlsToExclude = null)
        {
            if (element == null) return;

            // Check if this control should be excluded.
            if (controlsToExclude == null || !controlsToExclude.Contains(element.Name))
            {
                // Set the enabled state of the control.
                element.Enabled = isEnabled;
            }

            // Recursively process child elements using Controls collection.
            foreach (Control child in element.Controls)
            {
                SetEnabledState(child, isEnabled, controlsToExclude);
            }
        }
        #endregion

        #endregion

        #region Color Picker Form

        private void CustomColorPicker_Click(object sender, EventArgs e)
        {
            CustomColorPicker.Enabled = false;
            CustomColorPicker.Text = "Click A Location";
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
            {
                Interval = 1
            };
            t.Tick += new EventHandler(Colorpicker_Tick);
            t.Start();
        }

        void Colorpicker_Tick(object sender, EventArgs e)
        {
            try
            {
                // Get the bitmap of the image
                Bitmap b = ((Bitmap)SourceImage.Image);

                // Get the current position of the cursor within the form (client area)
                Rectangle imgBounds = GetImageBounds(SourceImage);
                Point cp = SourceImage.PointToClient(Cursor.Position);

                if (!imgBounds.Contains(cp))
                    return;

                float scaleX = (float)SourceImage.Image.Width / imgBounds.Width;
                float scaleY = (float)SourceImage.Image.Height / imgBounds.Height;

                int x = (int)((cp.X - imgBounds.Left) * scaleX);
                int y = (int)((cp.Y - imgBounds.Top) * scaleY);

                // Ensure the coordinates are within the bounds of the image
                if (x >= 0 && x < b.Width && y >= 0 && y < b.Height)
                {
                    // Get the pixel color at the calculated position
                    Color c = b.GetPixel(x, y);
                    CustomColorPicker.BackColor = c;
                }
            }
            catch (Exception)
            {
                // Handle any errors, such as invalid coordinates or image not loaded
            }

            // Check for left-click
            if (GetKeyState((int)Keys.LButton) < 0)
            {
                try
                {
                    // Get the bitmap of the image
                    Bitmap b = ((Bitmap)SourceImage.Image);

                    // Get the current position of the cursor within the form (client area)
                    Rectangle imgBounds = GetImageBounds(SourceImage);
                    Point cp = SourceImage.PointToClient(Cursor.Position);

                    if (!imgBounds.Contains(cp))
                        return;

                    float scaleX = (float)SourceImage.Image.Width / imgBounds.Width;
                    float scaleY = (float)SourceImage.Image.Height / imgBounds.Height;

                    int x = (int)((cp.X - imgBounds.Left) * scaleX);
                    int y = (int)((cp.Y - imgBounds.Top) * scaleY);

                    // Ensure the coordinates are within the bounds of the image
                    if (x >= 0 && x < b.Width && y >= 0 && y < b.Height)
                    {
                        // Get the pixel color at the calculated position
                        Color c = b.GetPixel(x, y);
                        // GetCustomColor = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}"; // Make the alpha max.
                        GetCustomColor = c;

                        // Update the button text
                        CustomColorPicker.Text = "Select A Block Type!";

                        // Stop the System.Windows.Forms.Timer
                        ((System.Windows.Forms.Timer)sender).Stop();
                        GetBlock();
                    }
                }
                catch (Exception)
                {
                    // Handle any errors, such as invalid coordinates or image not loaded
                    return;
                }
            }
            else if (GetKeyState((int)Keys.RButton) < 0)
            {
                // Stop the System.Windows.Forms.Timer.
                ((System.Windows.Forms.Timer)sender).Stop();

                // Cancel the pick.
                CustomColorPicker.BackColor = default;
                CustomColorPicker.Text = "Custom Color Picker";
                CustomColorPicker.BackColor = Color.FromArgb(127, 127, 127);
                CustomColorPicker.Enabled = true;
                return;
            }
        }

        // Open Block Picker Dialog
        async void GetBlock()
        {
            ColorPicker frm2 = new ColorPicker();
            DialogResult dr = frm2.ShowDialog(this);
            if (dr == DialogResult.Cancel)
            {
                frm2.Close();

                // Update Button
                CustomColorPicker.BackColor = default;
                CustomColorPicker.Text = "Custom Color Picker";
                CustomColorPicker.BackColor = Color.FromArgb(127, 127, 127);
                CustomColorPicker.Enabled = true;
            }
            else if (dr == DialogResult.OK)
            {
                if (frm2.GetText() == "" || frm2.GetText() == "Type a block id..")
                {
                    frm2.Close();
                    GetBlock();
                    return;
                }
                else
                {
                    string GetCustomColorBlockID = frm2.GetText();
                    frm2.Close();

                    if (int.TryParse(GetCustomColorBlockID, out int blockID))
                    {
                        // Try and get the first matching block color for this ID if possible.
                        string pickedName = "CustomBlock";
                        string pickedColor = "";
                        foreach (var b in ClrsBlockData)
                        {
                            if (b.Id == blockID)
                            {
                                pickedName = b.Name;
                                pickedColor = b.Color;
                                break;
                            }
                        }

                        var blockData = new BlockData
                        {
                            Id = blockID,
                            Name = pickedName,
                            Color = $"#FF{GetCustomColor.R:X2}{GetCustomColor.G:X2}{GetCustomColor.B:X2}",
                            WasPicked = true,
                            PickedColor = (string.IsNullOrEmpty(pickedColor)) ? GetCustomColor : ColorTranslator.FromHtml(pickedColor)
                        };

                        // Add the newly defined blockData to the main color filter.
                        ClrsBlockData.Add(blockData);

                        // Update Button
                        CustomColorPicker.BackColor = SystemColors.Control;
                        CustomColorPicker.Text = "Custom Color Picker";
                        CustomColorPicker.BackColor = Color.FromArgb(127, 127, 127);
                        CustomColorPicker.Enabled = true;

                        // Update the color list.
                        await BuildColorFilter(false);
                    }
                    else
                    {
                        MessageBox.Show("Invalid block ID. Please enter a valid number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        GetBlock(); // Retry input
                    }
                }
            }
        }
        #endregion

        #region Image Bounds Helper

        private Rectangle GetImageBounds(PictureBox pictureBox)
        {
            if (pictureBox.Image == null) return Rectangle.Empty;

            Size imgSize = pictureBox.Image.Size;
            Size containerSize = pictureBox.ClientSize;

            float imgAspect = (float)imgSize.Width / imgSize.Height;
            float containerAspect = (float)containerSize.Width / containerSize.Height;

            int drawWidth, drawHeight;
            if (imgAspect > containerAspect)  // Image is wider than container
            {
                drawWidth = containerSize.Width;
                drawHeight = (int)(containerSize.Width / imgAspect);
            }
            else  // Image is taller than container
            {
                drawWidth = (int)(containerSize.Height * imgAspect);
                drawHeight = containerSize.Height;
            }

            int drawX = (containerSize.Width - drawWidth) / 2;
            int drawY = (containerSize.Height - drawHeight) / 2;

            return new Rectangle(drawX, drawY, drawWidth, drawHeight);
        }
        #endregion

        #region BlockData Class Logic

        public class BlockData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Color { get; set; }
            public bool WasPicked { get; set; }
            public Color PickedColor { get; set; }
        }

        public static async Task<List<BlockData>> ReadDataAsync(string filePath, IProgress<int> progress)
        {
            var dataList = new List<BlockData>();

            var doc = await Task.Run(() => XDocument.Load(filePath));
            var elements = doc.Descendants("Block").ToList();

            int totalElements = elements.Count;
            for (int i = 0; i < totalElements; i++)
            {
                var element = elements[i];
                var data = new BlockData
                {
                    Id = int.Parse(element.Attribute("Id").Value),
                    Name = element.Attribute("Name").Value,
                    Color = element.Attribute("Color").Value
                };
                dataList.Add(data);

                // Report progress.
                progress?.Report((i + 1) * 100 / totalElements);
            }

            return dataList;
        }

        public static async Task<List<BlockData>> ReadDataAsync(Stream stream, IProgress<int> progress)
        {
            var dataList = new List<BlockData>();

            var doc = await Task.Run(() => XDocument.Load(stream));
            var elements = doc.Descendants("Block").ToList();

            int totalElements = elements.Count;
            for (int i = 0; i < totalElements; i++)
            {
                var element = elements[i];
                var data = new BlockData
                {
                    Id = int.Parse(element.Attribute("Id").Value),
                    Name = element.Attribute("Name").Value,
                    Color = element.Attribute("Color").Value
                };
                dataList.Add(data);

                // Report progress.
                progress?.Report((i + 1) * 100 / totalElements);
            }

            return dataList;
        }

        public static BlockData ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var data = new BlockData
            {
                Id = int.Parse(GetAttributeValue(line, "Id")),
                Name = GetAttributeValue(line, "Name"),
                Color = GetAttributeValue(line, "Color")
            };

            return data;
        }

        public static string GetAttributeValue(string line, string attributeName)
        {
            var attributeStartIndex = line.IndexOf(attributeName + "=\"") + attributeName.Length + 2;
            var attributeEndIndex = line.IndexOf("\"", attributeStartIndex);
            return line.Substring(attributeStartIndex, attributeEndIndex - attributeStartIndex);
        }
        #endregion
    }
}
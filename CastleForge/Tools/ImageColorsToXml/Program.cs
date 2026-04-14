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
This class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp
*/

using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System;

class Program
{
    static Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Drag and drop images or XML files onto this executable.");
            Console.WriteLine("If it's an image, the format should be 'blockname,id.*'. (.jpg .png .bpm)");
            Console.WriteLine("If it's an XML file, you will be asked for a luminosity percentage adjustment.");
            Console.ReadLine();

            return Task.CompletedTask;
        }

        var results = new ConcurrentBag<XElement>();

        Parallel.ForEach(args.Where(File.Exists), filePath =>
        {
            string fileExtension = Path.GetExtension(filePath).ToLower();

            if (fileExtension == ".xml")
            {
                // Process the XML file if it's dropped.
                ProcessXmlFileFromDragAndDrop(filePath);
            }
            else if (fileExtension == ".jpg" || fileExtension == ".png" || fileExtension == ".bmp")
            {
                // Process the image file if it's dropped.
                ProcessImageFile(filePath, results);
            }
        });

        if (results.Any())
        {
            XElement colorsXml = new XElement("Colors",
                new XElement("Blocks", results)
            );

            // Create XDocument with XML declaration.
            XDocument xDocument = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                colorsXml
            );

            string outputPath = "BlockColors.xml";

            // Save the XDocument with XML declaration using XmlWriterSettings.
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false // Ensures the declaration is included.
            };

            using (XmlWriter writer = XmlWriter.Create(outputPath, settings))
            {
                xDocument.Save(writer);
            }

            Console.WriteLine($"Generated {outputPath} successfully!");
        }

        return Task.CompletedTask;
    }

    static void ProcessImageFile(string imagePath, ConcurrentBag<XElement> results)
    {
        string fileName = Path.GetFileNameWithoutExtension(imagePath);
        var nameParts = fileName.Split(',');

        // Ensure the file name has both a name and an ID part.
        if (nameParts.Length == 2 && int.TryParse(nameParts[1], out int id))
        {
            string avgColorHex = GetAverageColor(imagePath);
            results.Add(new XElement("Block",
                new XAttribute("Id", id),             // Assign the second part of the name as Id.
                new XAttribute("Name", nameParts[0]), // Assign the first part as Name.
                new XAttribute("Color", avgColorHex)
            ));
        }
        else
        {
            Console.WriteLine($"Invalid filename format for {fileName}. Skipping...");
        }
    }

    static void ProcessXmlFile(int luminosityPercentage, string xmlFilePath)
    {
        if (!File.Exists(xmlFilePath))
        {
            Console.WriteLine("Invalid file path. Exiting...");
            return;
        }

        XDocument xDocument = XDocument.Load(xmlFilePath);

        // Loop through all the Block elements
        var blocks = xDocument.Descendants("Block");
        foreach (var block in blocks)
        {
            var colorHex = block.Attribute("Color")?.Value;

            if (!string.IsNullOrEmpty(colorHex) && colorHex.Length == 9) // Color should be in format #FFAABBCC
            {
                // Increase the luminosity by adjusting the RGB values
                string adjustedColor = AdjustLuminosity(colorHex, luminosityPercentage);
                block.SetAttributeValue("Color", adjustedColor);
            }
        }

        // Save the updated XML to a new file
        string outputFilePath = "AdjustedBlockColors.xml";
        xDocument.Save(outputFilePath);

        Console.WriteLine($"Adjusted colors and saved to {outputFilePath}");
    }

    static void ProcessXmlFileFromDragAndDrop(string xmlFilePath)
    {
        Console.WriteLine("XML file detected. Please enter the luminosity percentage increase (e.g., 10 for 10% increase): ");
        string input = Console.ReadLine();

        if (int.TryParse(input, out int luminosityPercentage))
        {
            ProcessXmlFile(luminosityPercentage, xmlFilePath);
        }
        else
        {
            Console.WriteLine("Invalid luminosity percentage.");
        }
    }

    static string GetAverageColor(string imagePath)
    {
        using (Bitmap bmp = new Bitmap(imagePath))
        {
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int bytesPerPixel = 4; // 32bpp ARGB means 4 bytes per pixel.
            int stride = bmpData.Stride;
            IntPtr scan0 = bmpData.Scan0;
            int width = bmp.Width, height = bmp.Height;
            long r = 0, g = 0, b = 0;
            int totalPixels = width * height;

            unsafe
            {
                byte* ptr = (byte*)scan0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = (y * stride) + (x * bytesPerPixel);
                        b += ptr[index];
                        g += ptr[index + 1];
                        r += ptr[index + 2];
                    }
                }
            }

            bmp.UnlockBits(bmpData);

            int avgR = (int)(r / totalPixels);
            int avgG = (int)(g / totalPixels);
            int avgB = (int)(b / totalPixels);

            return $"#FF{avgR:X2}{avgG:X2}{avgB:X2}"; // Always set alpha to FF.
        }
    }

    static string AdjustLuminosity(string hexColor, int luminosityPercentage)
    {
        // Parse the color
        int r = int.Parse(hexColor.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
        int g = int.Parse(hexColor.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
        int b = int.Parse(hexColor.Substring(7, 2), System.Globalization.NumberStyles.HexNumber);

        // Calculate luminosity adjustment
        r = AdjustColorChannel(r, luminosityPercentage);
        g = AdjustColorChannel(g, luminosityPercentage);
        b = AdjustColorChannel(b, luminosityPercentage);

        // Return the adjusted color as hex with FF alpha
        return $"#FF{r:X2}{g:X2}{b:X2}";
    }

    static int AdjustColorChannel(int colorValue, int luminosityPercentage)
    {
        int adjustedValue = colorValue + (colorValue * luminosityPercentage / 100);
        return Clamp(adjustedValue, 0, 255);
    }

    static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

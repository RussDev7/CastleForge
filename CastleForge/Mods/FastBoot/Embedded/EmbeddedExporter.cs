/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace FastBoot
{
    /// <summary>
    /// Extracts embedded resources that were compiled with default dot-separated
    /// manifest names (no LogicalName). This lets you package a whole folder
    /// (e.g. "ExtractMe\**") inside the DLL and later write it out to disk,
    /// preserving the folder structure.
    ///
    /// Resource name pattern assumed:
    ///   <RootNamespace>.<Folder>.<SubDir>.<File>.<Ext>
    /// Example:
    ///   MyMod.ExtractMe.Textures.HUD.png
    ///
    /// Usage:
    ///   var dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", myNs);
    ///   int n = EmbeddedExporterDots.ExtractFolder("ExtractMe", dest);
    ///
    /// Notes:
    /// - Works across all mods: Pass the target folder and (optionally) the source assembly.
    /// - Set overwrite=false to perform "extract-once" behavior.
    /// - Thread-safety: Avoid calling on the same destRoot concurrently.
    /// </summary>
    public static class EmbeddedExporter
    {
        #region Public API

        /// <summary>
        /// Extract all resources whose manifest name starts with
        /// "<RootNs>.<folderName>." to <paramref name="destRoot"/>,
        /// converting dots ('.') in the remainder to directories
        /// and keeping the REAL extension (text after the last '.').
        ///
        /// Examples:
        ///   MyMod.ExtractMe.Textures.HUD.png
        ///     => <destRoot>\Textures\HUD.png
        ///
        /// Returns the number of files written.
        /// </summary>
        /// <param name="folderName">Top-level folder name inside your project (e.g., "ExtractMe").</param>
        /// <param name="destRoot">Destination root directory (created if missing).</param>
        /// <param name="asm">
        ///   Source assembly. Defaults to the calling assembly if null.
        ///   Pass a specific assembly if you host this helper in a shared DLL.
        /// </param>
        /// <param name="overwrite">If false, skips files that already exist on disk.</param>
        public static int ExtractFolder(string folderName, string destRoot, Assembly asm = null, bool overwrite = false)
        {
            #region Parameter & Prefix Setup

            asm = asm ?? Assembly.GetExecutingAssembly();
            if (string.IsNullOrWhiteSpace(folderName))
                throw new ArgumentException("folderName cannot be null or empty.", nameof(folderName));
            if (string.IsNullOrWhiteSpace(destRoot))
                throw new ArgumentException("destRoot cannot be null or empty.", nameof(destRoot));

            Directory.CreateDirectory(destRoot);

            // Default root namespace (usually the project's Default Namespace).
            var rootNs = asm.GetName().Name ?? string.Empty;

            // Manifest names we consider: "<RootNs>.<Folder>.".
            // e.g., "MyMod.ExtractMe.".
            var prefix = rootNs + "." + folderName + ".";

            #endregion

            #region Scan Resources & Extract

            int count = 0;

            var names = asm
                .GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal));

            foreach (var name in names)
            {
                // Strip "<RootNs>.<Folder>." -> remainder "Textures.HUD.png".
                var rel = name.Substring(prefix.Length);

                // Split extension by the LAST dot so multi-dot filenames keep the right extension.
                int lastDot = rel.LastIndexOf('.');
                string ext  = lastDot >= 0 ? rel.Substring(lastDot) : string.Empty; // ".png".
                string stem = lastDot >= 0 ? rel.Substring(0, lastDot) : rel;       // "Textures.HUD".

                // Convert dots to directories: "Textures/HUD" + ".png".
                string relPath = stem.Replace('.', Path.DirectorySeparatorChar) + ext;
                string outPath = Path.Combine(destRoot, relPath);

                // Ensure directory exists.
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Skip if not overwriting and file exists.
                if (!overwrite && File.Exists(outPath))
                    continue;

                // Stream copy (no buffering into large arrays; works for any size).
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s == null)
                        throw new InvalidOperationException($"Missing resource stream: {name}.");

                    using (var f = File.Create(outPath))
                    {
                        s.CopyTo(f);
                    }
                }

                count++;
            }

            return count;

            #endregion
        }
        #endregion
    }
}
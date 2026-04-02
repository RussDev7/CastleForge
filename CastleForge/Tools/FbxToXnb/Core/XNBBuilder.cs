/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Content.Pipeline.Tasks;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Linq;
using System.IO;
using System;

namespace XNAConverter
{
    /// <summary>
    /// XNBBuilderEx (XNA Game Studio 4.0 Content Pipeline Wrapper)
    /// =========================================================================================
    ///
    /// Purpose
    /// -------
    /// Wraps the XNA Content Pipeline MSBuild task:
    ///   <see cref="Microsoft.Xna.Framework.Content.Pipeline.Tasks.BuildContent"/>
    ///
    /// so you can programmatically compile supported source assets (FBX, PNG, FX, SpriteFont, etc.)
    /// into XNB content files without requiring a full .contentproj project on disk.
    ///
    /// High-Level Flow
    /// ---------------
    /// 1) Configure build settings (platform/profile/compression and audio options).
    /// 2) Create SourceAssets TaskItems with Importer/Processor/Name metadata.
    /// 3) Provide PipelineAssemblies pointing at the XNA Game Studio v4.0 reference DLLs.
    /// 4) Execute the BuildContent task and return produced OutputContentFiles.
    ///
    /// Notes / Gotchas
    /// --------------
    /// - This class is a thin task-wrapper; it does not attempt to replicate the full MSBuild project system.
    /// - The pipeline reference assemblies MUST be present (either from XNA GSE install or app-local copy).
    /// - Any missing/invalid inputs typically surface in the build engine error list or logfile output.
    ///
    /// =========================================================================================
    /// </summary>
    public sealed class XNBBuilderEx : BuildContent
    {
        #region Configuration

        /// <summary>
        /// If true, MP3/WMA/WAV will be processed as SoundEffects; otherwise Song processors may be used
        /// (mirrors your original branching behavior).
        /// </summary>
        public bool BuildAudioAsSoundEffects { get; set; } = false;

        /// <summary>
        /// If true, MP3/WMA/WAV will be processed as Songs when not forced to SoundEffects.
        /// </summary>
        public bool BuildAudioAsSongs { get; set; } = false;

        /// <summary>
        /// Optional log file path used when logging is enabled.
        /// </summary>
        public string LogFilePath { get; set; } = "logfile.txt";

        /// <summary>
        /// Build engine that collects errors (same pattern as your working BuildEngine).
        /// </summary>
        public BuildEngine Engine { get; private set; } = new BuildEngine();

        /// <summary>
        /// FBX content processor to use when compiling .fbx files into .xnb.
        /// Summary:
        /// - Default is "ModelProcessor" (rigid/static models like items).
        /// - Set to a skinned processor name (ex: "SkinedModelProcessor") when building enemies/player/dragons.
        /// </summary>
        public string FbxProcessorName { get; set; } = "ModelProcessor";

        #endregion

        #region Construction

        /// <summary>
        /// Default ctor:
        ///   TargetPlatform = Windows
        ///   TargetProfile  = Reach
        ///   CompressContent = false
        /// </summary>
        public XNBBuilderEx()
            : this(targetPlatform: "Windows", targetProfile: "Reach", compressContent: false)
        {
        }

        /// <summary>
        /// Convenience ctor that only overrides compression.
        /// </summary>
        public XNBBuilderEx(bool compressContent)
            : this(targetPlatform: "Windows", targetProfile: "Reach", compressContent: compressContent)
        {
        }

        /// <summary>
        /// Full ctor for platform/profile/compression.
        /// </summary>
        public XNBBuilderEx(string targetPlatform, string targetProfile, bool compressContent)
        {
            TargetPlatform   = targetPlatform ?? "Windows";
            TargetProfile    = targetProfile  ?? "Reach";
            CompressContent  = compressContent;

            BuildAudioAsSongs        = false;
            BuildAudioAsSoundEffects = false;
        }
        #endregion

        #region Public API

        /// <summary>
        /// Returns the list of errors collected by the build engine.
        /// </summary>
        public List<string> GetErrors() => Engine?.GetErrors() ?? new List<string>();

        /// <summary>
        /// PackageContent (Main Build Entry Point)
        /// --------------------------------------
        /// Builds the provided source files into XNB content.
        ///
        /// Inputs:
        /// - fileNames:
        ///     Absolute or relative file paths to source assets (FBX, PNG, FX, etc.).
        /// - outputDirectory:
        ///     Destination folder for XNB output (absolute or relative).
        /// - shouldLog:
        ///     If true, the build engine writes to LogFilePath; if false, file logging is disabled.
        /// - rootDirectory:
        ///     Root directory used by the pipeline for resolving relative references (textures, includes, etc.).
        /// - intermediateDirectory:
        ///     Directory for intermediate build outputs (recommend a temp folder).
        /// - xnaReferenceDirectoryOrRoot:
        ///     • Option A: full path to "...\References\Windows\x86"
        ///     • Option B: full path to "...\XNA Game Studio\v4.0\" root
        ///     • null/empty => auto resolve (default install, then app-local)
        ///
        /// Returns:
        /// - outputXnbs:
        ///     String paths from OutputContentFiles (may be empty if build fails).
        /// - buildStatus:
        ///     True if Execute() succeeded.
        /// </summary>
        public string[] PackageContent(
            string[] fileNames,
            string   outputDirectory,
            bool     shouldLog,
            string   rootDirectory,
            string   intermediateDirectory,
            string   xnaReferenceDirectoryOrRoot,
            string[] extraPipelineAssembliesOrDirs,
            out bool buildStatus)
        {
            buildStatus = false;
            if (fileNames == null || fileNames.Length == 0)
                return Array.Empty<string>();

            // Normalize paths.
            outputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? "." : outputDirectory;
            rootDirectory   = string.IsNullOrWhiteSpace(rootDirectory)   ? "." : rootDirectory;

            // Intermediate is required for stable builds; default to a temp folder.
            if (string.IsNullOrWhiteSpace(intermediateDirectory))
                intermediateDirectory = Path.Combine(Path.GetTempPath(), "XNBBuilderEx_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(intermediateDirectory);

            // Setup logging engine.
            if (!shouldLog)
            {
                Engine.log = false;
            }
            else
            {
                Engine = new BuildEngine(LogFilePath);
            }

            // Setup task properties.
            OutputDirectory       = outputDirectory;
            RootDirectory         = rootDirectory;
            IntermediateDirectory = intermediateDirectory;

            // Prepare SourceAssets.
            SourceAssets = BuildSourceAssets(fileNames);

            // Pipeline assemblies (XNA GSE 4.0).
            PipelineAssemblies = ResolvePipelineAssemblies(xnaReferenceDirectoryOrRoot, extraPipelineAssembliesOrDirs);

            // Attach BuildEngine + execute.
            Engine.Begin();
            BuildEngine = Engine;

            try
            {
                buildStatus = Execute();

                if (!buildStatus || OutputContentFiles == null)
                    return Array.Empty<string>();

                return OutputContentFiles.Select(x => x?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                try { Engine.End(); } catch { }
            }
        }
        #endregion

        #region Internals: SourceAssets (Importer/Processor Selection)

        /// <summary>
        /// Builds the SourceAssets TaskItem list for BuildContent.
        ///
        /// Summary:
        /// - For each input file, assigns:
        ///     • Importer
        ///     • Processor
        ///     • Name (always the filename stem)
        ///
        /// Notes:
        /// - This mapping intentionally mirrors typical XNA Content Pipeline defaults.
        /// - Audio uses BuildAudioAsSoundEffects/BuildAudioAsSongs flags to choose processors.
        /// </summary>
        private ITaskItem[] BuildSourceAssets(string[] fileNames)
        {
            var items = new TaskItem[fileNames.Length];

            for (int i = 0; i < fileNames.Length; i++)
            {
                string path = fileNames[i] ?? "";
                string ext  = "." + (path.Split('.').LastOrDefault() ?? "").ToLowerInvariant();

                var meta = new Dictionary<string, object>();

                // Match the original importer/processor selection logic.
                if (".bmp.dds.dib.hdr.jpg.pfm.png.ppm.tga".Contains(ext))
                {
                    meta["Importer"]  = "TextureImporter";
                    meta["Processor"] = "TextureProcessor";
                }
                else if (".fbx".Contains(ext))
                {
                    meta["Importer"] = "FbxImporter";
                    meta["Processor"] = string.IsNullOrWhiteSpace(FbxProcessorName) ? "ModelProcessor" : FbxProcessorName;
                }
                else if (".fx".Contains(ext))
                {
                    meta["Importer"]  = "EffectImporter";
                    meta["Processor"] = "EffectProcessor";
                }
                else if (".spritefont".Contains(ext))
                {
                    meta["Importer"]  = "FontDescriptionImporter";
                    meta["Processor"] = "FontDescriptionProcessor";
                }
                else if (".x".Contains(ext))
                {
                    meta["Importer"]  = "XImporter";
                    meta["Processor"] = "ModelProcessor";
                }
                else if (".xml".Contains(ext))
                {
                    meta["Importer"]  = "XmlImporter";
                    meta["Processor"] = "PassThroughProcessor";
                }
                else if (".mp3".Contains(ext))
                {
                    meta["Importer"] = "Mp3Importer";
                    meta["Processor"] = BuildAudioAsSoundEffects ? "SoundEffectProcessor"
                                    : BuildAudioAsSongs         ? "SongProcessor"
                                    : "SoundEffectProcessor";
                }
                else if (".wma".Contains(ext))
                {
                    meta["Importer"] = "WmaImporter";
                    meta["Processor"] = BuildAudioAsSoundEffects ? "SoundEffectProcessor"
                                    : BuildAudioAsSongs         ? "SongProcessor"
                                    : "SoundEffectProcessor";
                }
                else if (".wav".Contains(ext))
                {
                    meta["Importer"] = "WavImporter";
                    meta["Processor"] = BuildAudioAsSoundEffects ? "SoundEffectProcessor"
                                    : BuildAudioAsSongs         ? "SongProcessor"
                                    : "SoundEffectProcessor";
                }
                else if (".wmv".Contains(ext))
                {
                    meta["Importer"]  = "WmvImporter";
                    meta["Processor"] = "VideoProcessor";
                }

                // Name is always file stem (keeps output stable).
                meta["Name"] = Path.GetFileNameWithoutExtension(path);

                items[i] = new TaskItem(path, meta);
            }

            return items;
        }
        #endregion

        #region Internals: Pipeline DLL Resolution (XNA GSE 4.0)

        /// <summary>
        /// ResolveXnaRefsDir
        /// -----------------
        /// Resolve the XNA GSE 4.0 "References\Windows\x86" folder.
        ///
        /// Priority:
        ///  1) Caller-provided path:
        ///     - If it ends with "\References\Windows\x86", use it as-is.
        ///     - Else treat it as XNAGSv4 root and append "\References\Windows\x86".
        ///  2) Default install location:
        ///     C:\Program Files (x86)\Microsoft XNA\XNA Game Studio\v4.0\References\Windows\x86
        ///  3) App-local:
        ///     <app>\References\Windows\x86
        ///     or <app>\ (directly)
        /// </summary>
        public static string ResolveXnaRefsDir(string xnaReferenceDirectoryOrRoot)
        {
            // 1) Caller-provided
            if (!string.IsNullOrWhiteSpace(xnaReferenceDirectoryOrRoot))
            {
                var p = xnaReferenceDirectoryOrRoot.Trim().Trim('"');

                // If they passed the exact refs directory, accept it.
                if (Directory.Exists(p) && p.EndsWith(Path.Combine("References", "Windows", "x86"), StringComparison.OrdinalIgnoreCase))
                    return p;

                // Otherwise treat as XNAGSv4 root and append.
                var combined = Path.Combine(p, "References", "Windows", "x86");
                if (Directory.Exists(combined))
                    return combined;
            }

            // 2) Default install.
            var def = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft XNA", "XNA Game Studio", "v4.0", "References", "Windows", "x86");
            if (Directory.Exists(def))
                return def;

            // 3) App-local.
            var app = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var localRefs = Path.Combine(app, "References", "Windows", "x86");
            if (Directory.Exists(localRefs))
                return localRefs;

            if (Directory.Exists(app))
                return app;

            return null;
        }

        /// <summary>
        /// ResolvePipelineAssemblies
        /// -------------------------
        /// Produces the PipelineAssemblies list required by the BuildContent task.
        ///
        /// Notes:
        /// - These are the standard XNA Game Studio 4.0 pipeline/reference DLLs.
        /// - If any are missing, we throw a detailed exception listing all missing paths.
        /// </summary>
        private ITaskItem[] ResolvePipelineAssemblies(string xnaReferenceDirectoryOrRoot, string[] extraPipelineAssembliesOrDirs)
        {
            string refsDir = ResolveXnaRefsDir(xnaReferenceDirectoryOrRoot);
            if (string.IsNullOrWhiteSpace(refsDir) || !Directory.Exists(refsDir))
                throw new DirectoryNotFoundException("XNA reference directory not found. Provide XNA GSE 4.0 refs or copy required DLLs next to the app.");

            // Stock XNA pipeline/reference DLLs (required)
            string[] required =
            {
                "Microsoft.Xna.Framework.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.AudioImporters.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.EffectImporter.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.FBXImporter.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.TextureImporter.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.VideoImporters.dll",
                "Microsoft.Xna.Framework.Content.Pipeline.XImporter.dll",
            };

            var missing = new List<string>();
            var list = new List<ITaskItem>(required.Length + 16);

            foreach (var dll in required)
            {
                var full = Path.Combine(refsDir, dll);
                if (!File.Exists(full)) missing.Add(full);
                else list.Add(new TaskItem(full));
            }

            if (missing.Count > 0)
                throw new FileNotFoundException("Missing XNA pipeline/reference DLL(s):\n" + string.Join("\n", missing));

            // ------------------------------------------------------------
            // EXTRA pipeline assemblies (your CMZ/DNA pipeline extensions)
            // ------------------------------------------------------------
            foreach (var p in ExpandExtraPipelinePaths(extraPipelineAssembliesOrDirs))
            {
                try
                {
                    // Avoid duplicates (path compare).
                    if (list.Any(x => string.Equals(x.ItemSpec, p, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (File.Exists(p))
                        list.Add(new TaskItem(p));
                }
                catch { }
            }

            return list.ToArray();
        }

        private static IEnumerable<string> ExpandExtraPipelinePaths(string[] extraPipelineAssembliesOrDirs)
        {
            if (extraPipelineAssembliesOrDirs == null) yield break;

            foreach (var raw in extraPipelineAssembliesOrDirs)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var p = raw.Trim().Trim('"');

                // If it's a directory, add likely pipeline dlls from it.
                if (Directory.Exists(p))
                {
                    // Add the obvious candidates first.
                    foreach (var f in Directory.EnumerateFiles(p, "*Pipeline*.dll", SearchOption.TopDirectoryOnly))
                        yield return f;

                    foreach (var f in Directory.EnumerateFiles(p, "*Content.Pipeline*.dll", SearchOption.TopDirectoryOnly))
                        yield return f;

                    // Optional: if you want broader search, uncomment:
                    // foreach (var f in Directory.EnumerateFiles(p, "*.dll", SearchOption.TopDirectoryOnly))
                    //     yield return f;

                    continue;
                }

                // If it's a file, add it directly.
                if (File.Exists(p))
                {
                    yield return p;
                    continue;
                }
            }
        }
        #endregion
    }
}
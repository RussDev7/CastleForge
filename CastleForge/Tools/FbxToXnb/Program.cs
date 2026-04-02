/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using XNAConverter;
using System.Linq;
using System.Text;
using System.IO;
using System;

/// <summary>
/// Program
/// =========================================================================================
/// FBX -> XNB (XNA Pipeline) Console Converter
/// =========================================================================================
///
/// What this tool does
/// -------------------
/// - Takes one or more .fbx files and runs them through the XNA pipeline (via XNBBuilderEx)
/// - Writes output into an isolated folder per FBX:
///     <srcDir>\<asset>\*.xnb
///
/// Why it stages to a temp folder
/// ------------------------------
/// - Content pipeline builds assume the working directory is a root.
/// - Sidecar textures (like <asset>.png) are copied into temp alongside the FBX.
/// - Prevents collisions like multiple models all producing "texture.xnb".
///
/// Custom processors (Skinned models)
/// ----------------------------------
/// - Skinned models require a pipeline extension assembly (DLL) that defines the processor.
/// - This tool supports passing those DLLs/dirs via flags or env var so XNBBuilderEx can find them.
///
/// New flags
/// ---------
///   --pipeline     "<path>"   (repeatable) Path to a pipeline DLL *or* folder containing it.
///   --pipelineDir  "<path>"   (repeatable) Same as --pipeline, but name makes intent clearer.
///   --processor    "<name>"   FBX processor name (ex: SkinedModelProcessor).
///
/// Examples
/// --------
///  (items / rigid)
///    FbxToXnbXna.exe "C:\...\0051_Pistol_model.fbx"
///
///  (skinned - you MUST provide the custom pipeline DLL that defines SkinedModelProcessor)
///    FbxToXnbXna.exe --processor SkinedModelProcessor --pipelineDir "C:\...\YourPipelineBin" "C:\...\ALIEN.fbx"
///
/// Environment fallback
/// --------------------
///   CMZ_PIPELINE = semicolon-separated list of dll/dir paths
///   Example:
///     set CMZ_PIPELINE=C:\...\DNA.Content.Pipeline.dll;C:\...\OtherPipelineDir
///
/// Notes / Intent
/// --------------
/// - Unknown CLI tokens are intentionally ignored to keep drag/drop usage tolerant.
/// - In interactive mode, options persist across lines (so you can set pipeline/processor once).
/// =========================================================================================
/// </summary>
internal static class Program
{
    #region Entry Point

    /// <summary>
    /// Main entrypoint.
    ///
    /// Flow:
    /// - Ensure XNA Game Studio pipeline bits are installed (with prompt).
    /// - Ensure XNAGSv4 env var exists (helps pipeline resolve references).
    /// - Parse options.
    ///   - If no FBX args were provided, enter interactive mode.
    ///   - Otherwise convert each provided FBX.
    ///
    /// Return codes:
    /// - 0 = all succeeded (or help shown)
    /// - 1 = any failure occurred
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            // Prompt-install if the pipeline refs aren't present.
            if (!XnaGseInstaller.EnsureInstalledWithPrompt())
                return 1;

            EnsureXnaGsEnvVar();

            // Parse CLI flags + FBX paths.
            var opt = ParseOptions(args);

            if (opt.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (opt.Fbxs.Count == 0)
            {
                Console.WriteLine("Drag .fbx file(s) into this window and press Enter.");
                Console.WriteLine("Type 'exit' to quit.");
                Console.WriteLine("Type 'help' for flags.");
                Console.WriteLine();

                // Persist options across interactive lines (pipeline dirs, processor, etc.)
                while (true)
                {
                    Console.Write("> ");
                    var line = Console.ReadLine();
                    if (line == null) break;

                    line = line.Trim();
                    if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.Equals("help", StringComparison.OrdinalIgnoreCase)) { PrintHelp(); continue; }

                    var tokens = SplitArgsLikeCmd(line);
                    var lineOpt = ParseOptions(tokens.ToArray(), mergeInto: opt);

                    // Convert any FBXs from the line.
                    foreach (var f in lineOpt.Fbxs)
                        ConvertOne(f, opt);

                    // If the line had no FBXs, just keep looping (maybe they were setting pipeline dirs).
                }

                return 0;
            }

            int failed = 0;
            foreach (var fbx in opt.Fbxs)
                failed += ConvertOne(fbx, opt) ? 0 : 1;

            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 1;
        }
    }
    #endregion

    #region Options

    /// <summary>
    /// Options parsed from CLI (and optionally merged into an existing Options for interactive mode).
    ///
    /// Key behaviors:
    /// - Fbxs: list of .fbx paths detected in args.
    /// - ExtraPipeline: repeatable list of DLL/dir paths used to resolve custom processors.
    /// - FbxProcessor: optional processor override (kept null/empty by default for compatibility).
    /// - ShowHelp: indicates help was requested.
    /// </summary>
    private sealed class Options
    {
        public readonly List<string> Fbxs = new List<string>();

        // Extra pipeline assembly paths or directories. Required for custom processors.
        public readonly List<string> ExtraPipeline = new List<string>();

        // Optional: override the FBX processor name.
        // If null/empty => builder default (keep items working).
        public string FbxProcessor;

        public bool ShowHelp;
    }

    /// <summary>
    /// Parse CLI args into Options.
    ///
    /// Notes:
    /// - When mergeInto is null, we also read CMZ_PIPELINE from environment (semicolon-separated).
    /// - Unknown tokens are intentionally ignored (drag/drop and casual typing resilience).
    /// </summary>
    private static Options ParseOptions(string[] args, Options mergeInto = null)
    {
        var opt = mergeInto ?? new Options();

        // Also accept env var list (semicolon-separated).
        if (mergeInto == null)
        {
            var env = Environment.GetEnvironmentVariable("CMZ_PIPELINE");
            if (!string.IsNullOrWhiteSpace(env))
            {
                foreach (var piece in env.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = piece.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(p))
                        AddUniquePath(opt.ExtraPipeline, p);
                }
            }
        }

        if (args == null || args.Length == 0)
            return opt;

        for (int i = 0; i < args.Length; i++)
        {
            var a = (args[i] ?? "").Trim();

            if (IsFlag(a, "--help", "-h", "/?"))
            {
                opt.ShowHelp = true;
                continue;
            }

            if (IsFlag(a, "--pipeline", "--pipelineDir", "-p", "-pd"))
            {
                if (i + 1 < args.Length)
                {
                    var p = TrimQuotes(args[++i]);
                    AddUniquePath(opt.ExtraPipeline, p);
                }
                continue;
            }

            if (IsFlag(a, "--processor", "--fbxProcessor", "-proc"))
            {
                if (i + 1 < args.Length)
                {
                    opt.FbxProcessor = TrimQuotes(args[++i]);
                }
                continue;
            }

            // FBX files.
            if (a.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                opt.Fbxs.Add(a);
                continue;
            }

            // Ignore unknown tokens (keeps drag/drop tolerant).
        }

        return opt;
    }

    /// <summary>
    /// Case-insensitive flag matcher.
    /// </summary>
    private static bool IsFlag(string token, params string[] flags)
        => flags.Any(f => token.Equals(f, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a path to list if it's non-empty and not already present (case-insensitive).
    /// </summary>
    private static void AddUniquePath(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim().Trim('"');

        // Avoid dupes.
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
                return;

        list.Add(path);
    }

    /// <summary>
    /// Prints help text for flags, env var, and common examples.
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Flags:");
        Console.WriteLine("  --pipeline <dllOrDir>     Add custom pipeline DLL or folder (repeatable)");
        Console.WriteLine("  --pipelineDir <dir>       Same as --pipeline (repeatable)");
        Console.WriteLine("  --processor <name>        FBX processor name (ex: SkinedModelProcessor)");
        Console.WriteLine("  --help                    Show help");
        Console.WriteLine();
        Console.WriteLine("Env:");
        Console.WriteLine("  CMZ_PIPELINE=path1;path2;...  (dlls or dirs added automatically)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  FbxToXnbXna.exe \"C:\\...\\0051_Pistol_model.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --processor SkinedModelProcessor --pipelineDir \"C:\\...\\PipelineBin\" \"C:\\...\\ALIEN.fbx\"");
        Console.WriteLine();
    }
    #endregion

    #region Core Conversion

    /// <summary>
    /// Convert a single FBX file into an isolated output folder:
    ///   <srcDir>\<asset>\*.xnb
    ///
    /// Build strategy:
    /// - Stage FBX (+ optional sidecar texture) into a unique temp folder.
    /// - Run the pipeline from that temp folder as the root.
    /// - Emit final content directly to <outDir>.
    /// - Clean up temp + intermediate folders on completion.
    /// </summary>
    private static bool ConvertOne(string fbxPath, Options opt)
    {
        fbxPath = Path.GetFullPath(TrimQuotes(fbxPath));
        if (!File.Exists(fbxPath))
        {
            Console.WriteLine($"  ! Not found: {fbxPath}");
            return false;
        }

        // Source + naming.
        string srcDir  = Path.GetDirectoryName(fbxPath) ?? ".";
        string asset   = Path.GetFileNameWithoutExtension(fbxPath);
        string sidecar = Path.Combine(srcDir, asset + ".png");

        // OUTPUT: Isolated folder per model (prevents texture.xnb collisions).
        string outDir = Path.Combine(srcDir, asset);
        Directory.CreateDirectory(outDir);

        // TEMP working dir.
        string work = Path.Combine(Path.GetTempPath(), "FbxToXnbXna_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        // Current directory must be restored even on failures.
        string oldCwd = Directory.GetCurrentDirectory();

        try
        {
            // --- Stage inputs into the TEMP work folder ---

            // Copy FBX to work folder.
            string workFbx = Path.Combine(work, Path.GetFileName(fbxPath));
            File.Copy(fbxPath, workFbx, overwrite: true);

            // Sidecar -> Same name + optional texture.png.
            if (File.Exists(sidecar))
            {
                var sidecarFileName = Path.GetFileName(sidecar);
                File.Copy(sidecar, Path.Combine(work, sidecarFileName), overwrite: true);

                if (!sidecarFileName.Equals("texture.png", StringComparison.OrdinalIgnoreCase))
                    File.Copy(sidecar, Path.Combine(work, "texture.png"), overwrite: true);

                Console.WriteLine($"  * Using sidecar: {sidecarFileName} -> (temp)");
            }

            Directory.SetCurrentDirectory(work);

            // --- Invoke pipeline build ---

            var builder = new XNBBuilderEx(targetPlatform: "Windows", targetProfile: "Reach", compressContent: true);

            // OPTIONAL: If you add a property in XNBBuilderEx like builder.FbxProcessorName, set it here.
            // If you DIDN'T add such a property, ignore this and keep processor selection inside XNBBuilderEx.
            TrySetBuilderFbxProcessor(builder, opt.FbxProcessor);

            string intermediateDir = Path.Combine(Path.GetTempPath(), "XNB_Inter_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(intermediateDir);

            string xnaRefs =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft XNA", "XNA Game Studio", "v4.0", "References", "Windows", "x86");

            // Helpful console line.
            if (!string.IsNullOrWhiteSpace(opt.FbxProcessor))
                Console.WriteLine($"  * FBX Processor: {opt.FbxProcessor}");
            if (opt.ExtraPipeline.Count > 0)
                Console.WriteLine($"  * Extra Pipeline: {string.Join("; ", opt.ExtraPipeline)}");

            // NOTE: This call assumes you updated XNBBuilderEx.PackageContent signature to include:
            //   string[] extraPipelineAssembliesOrDirs
            var outputs = builder.PackageContent(
                fileNames: new[] { workFbx },
                outputDirectory: outDir,
                shouldLog: true,
                rootDirectory: work,
                intermediateDirectory: intermediateDir,
                xnaReferenceDirectoryOrRoot: xnaRefs,
                extraPipelineAssembliesOrDirs: opt.ExtraPipeline.ToArray(),
                buildStatus: out bool ok
            );

            try { Directory.Delete(intermediateDir, true); } catch { }

            if (!ok)
            {
                Console.WriteLine("  ! Build failed. Check logfile.txt / builder errors.");
                var errs = builder.GetErrors();
                if (errs != null && errs.Count > 0)
                    Console.WriteLine("  ! " + errs[0]);

                // Extra hint when processor is missing.
                if (errs != null && errs.Any(e => e.IndexOf("Cannot find content processor", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Console.WriteLine("  ! Hint: You must pass the CMZ/DNA pipeline extension DLL/folder via --pipeline/--pipelineDir.");
                }

                return false;
            }

            Console.WriteLine($"  + Built to: {outDir}");
            if (outputs != null && outputs.Length > 0)
                Console.WriteLine($"  + XNBs: {string.Join(", ", outputs.Select(Path.GetFileName))}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! Failed: {ex.Message}");
            return false;
        }
        finally
        {
            // NOTE:
            // Always restore CWD and clean temp folders to avoid leaving behind locked dirs/files.
            try { Directory.SetCurrentDirectory(oldCwd); } catch { }
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Optional: set builder property if you added it.
    /// Safe no-op if the property does not exist.
    ///
    /// This keeps the Program decoupled from your XNBBuilderEx implementation:
    /// - If XNBBuilderEx exposes a writable string property named "FbxProcessorName", we set it.
    /// - Otherwise nothing happens and the builder can use its internal default processor logic.
    /// </summary>
    private static void TrySetBuilderFbxProcessor(object builder, string processorName)
    {
        if (builder == null) return;
        if (string.IsNullOrWhiteSpace(processorName)) return;

        try
        {
            var t = builder.GetType();
            var p = t.GetProperty("FbxProcessorName");
            if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                p.SetValue(builder, processorName, null);
        }
        catch { }
    }
    #endregion

    #region Environment Setup (XNAGSv4)

    /// <summary>
    /// Ensures the XNAGSv4 environment variable is set.
    ///
    /// Purpose:
    /// - Some pipeline setups (especially older XNA GSE tooling) expect XNAGSv4 to be defined.
    ///
    /// Strategy:
    /// - If already set: do nothing.
    /// - Else: try ProgramFiles(x86)\Microsoft XNA\XNA Game Studio\v4.0\
    /// - Else: try local app directory fallback if "References\Windows\x86" exists.
    /// </summary>
    private static void EnsureXnaGsEnvVar()
    {
        string cur = Environment.GetEnvironmentVariable("XNAGSv4");
        if (!string.IsNullOrWhiteSpace(cur))
            return;

        string guess =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft XNA", "XNA Game Studio", "v4.0") + Path.DirectorySeparatorChar;
        if (Directory.Exists(guess))
        {
            Environment.SetEnvironmentVariable("XNAGSv4", guess);
            return;
        }

        string local = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(Path.Combine(local, @"References\Windows\x86")))
        {
            Environment.SetEnvironmentVariable("XNAGSv4", local.TrimEnd('\\') + "\\");
        }
    }
    #endregion

    #region Drag-Drop / Line Parsing

    /// <summary>
    /// Splits a command-line-like input string into tokens.
    ///
    /// Notes:
    /// - Preserves quotes by toggling in/out of quote mode (simple but works well for typical usage).
    /// - Intended for interactive mode lines, not full CMD parsing edge cases.
    /// </summary>
    private static List<string> SplitArgsLikeCmd(string line)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; sb.Append(c); continue; }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }

        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    /// <summary>
    /// Removes wrapping quotes from a string:
    ///   "C:\Path With Spaces" -> C:\Path With Spaces
    /// </summary>
    private static string TrimQuotes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            return s.Substring(1, s.Length - 2);
        return s;
    }
    #endregion
}
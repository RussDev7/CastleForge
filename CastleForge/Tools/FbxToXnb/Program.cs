/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
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
///   --fbxComp      "10.0"     TexturePacks [Models] FbxComp value. Tool computes Scale=0.01/FbxComp.
///   --scale        "0.001"    Manual FBX ModelProcessor scale override.
///   --authoringLocation "0,0,0"
///   --authoringRotation "0,0,0"               3 values = Blender Euler degrees X,Y,Z.
///   --authoringRotation "1,0,0,0"             4 values = Blender Quaternion W,X,Y,Z.
///   --authoringRotationQuaternion "1,0,0,0"   Legacy explicit quaternion form.
///   --rigidMeshRestoreFile "<path>"           Optional TexturePacks .cmzrigid.ini sidecar override.
///   --noScale                 Do not auto-apply the default FBX scale.
///   --animName     "<name>"   AnimationClipProcessor output clip name.
///   --sourceClip   "<name>"   FBX take name to use when multiple takes exist.
///   --frameRate    "30"       AnimationClipProcessor sample rate.
///   --noReduce                Keep all sampled animation keys.
///
/// Examples
/// --------
///  (items / rigid exported from TexturePacks with FbxComp=10.0)
///    FbxToXnbXna.exe --fbxComp 10.0 "C:\...\0051_Pistol_model.fbx"
///
///  (skinned - you MUST provide the custom pipeline DLL that defines SkinedModelProcessor)
///    FbxToXnbXna.exe --processor SkinedModelProcessor --pipelineDir "C:\...\YourPipelineBin" "C:\...\ALIEN.fbx"
///
///  (standalone avatar/weapon animation clip)
///    FbxToXnbXna.exe --processor AnimationClipProcessor --pipelineDir "C:\...\SkinedModelProcessor" --animName Reload "C:\...\reload.fbx"
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
    /// <summary>
    /// Final root scale the game expects for FBX model content.
    /// </summary>
    private const float GameFbxModelScale = 0.01f;

    /// <summary>
    /// Default TexturePacks [Models] FbxComp value used for GLB/FBX round-trip exports.
    /// Effective converter Scale is GameFbxModelScale / FbxComp.
    /// </summary>
    private const float DefaultExtractorFbxComp = 10.0f;

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
    /// - ProcessorParameters: generic pipeline processor parameters such as Scale=0.001.
    /// - FbxComp: TexturePacks [Models] FbxComp value used to calculate Scale=0.01/FbxComp.
    /// - DisableFbxScale: prevents the automatic model scale injection.
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

        // Optional processor parameters, used by custom processors such as AnimationClipProcessor.
        public readonly Dictionary<string, string> ProcessorParameters =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // TexturePacks [Models] FbxComp value. When set, model Scale is calculated as 0.01 / FbxComp.
        public float? FbxComp;

        // When false, model processors receive a calculated Scale by default.
        public bool DisableFbxScale;

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

            if (IsFlag(a, "--fbxComp", "--extractorFbxComp", "--extractorScale", "--tpFbxComp"))
            {
                if (i + 1 < args.Length)
                {
                    string fbxCompText = TrimQuotes(args[++i]);
                    if (TryParsePositiveFloat(fbxCompText, out float fbxComp))
                    {
                        opt.FbxComp = fbxComp;
                        opt.DisableFbxScale = false;
                    }
                }
                continue;
            }

            if (IsFlag(a, "--scale", "--fbxScale", "--modelScale"))
            {
                if (i + 1 < args.Length)
                {
                    string scaleText = TrimQuotes(args[++i]);
                    if (TryParsePositiveFloat(scaleText, out float scale))
                    {
                        opt.ProcessorParameters["Scale"] = scale.ToString(CultureInfo.InvariantCulture);
                        opt.DisableFbxScale = false;
                    }
                }
                continue;
            }

            if (IsFlag(a, "--authoringLocation", "--authorLocation", "--exportLocation"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["AuthoringLocation"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--authoringRotation", "--authorRotation", "--exportRotation"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["AuthoringRotation"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--authoringRotationDegrees", "--authorRotationDegrees", "--exportRotationDegrees"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["AuthoringRotation"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--authoringRotationQuaternion", "--authorRotationQuaternion", "--exportRotationQuaternion"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["AuthoringRotationQuaternion"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--authoringLocationScale", "--authorLocationScale"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["AuthoringLocationScale"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--rigidMeshRestoreFile", "--rigidMeshRestore", "--cmzRigidRestore"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["RigidMeshRestoreFile"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--noScale", "--noFbxScale"))
            {
                opt.DisableFbxScale = true;
                opt.FbxComp = null;
                opt.ProcessorParameters.Remove("Scale");
                continue;
            }

            if (IsFlag(a, "--param", "--processorParam"))
            {
                if (i + 1 < args.Length)
                {
                    AddProcessorParam(opt, TrimQuotes(args[++i]));
                }
                continue;
            }

            if (IsFlag(a, "--animName", "--clipName"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["ClipName"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--sourceClip", "--take"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["SourceClipName"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--frameRate", "--fps"))
            {
                if (i + 1 < args.Length)
                {
                    opt.ProcessorParameters["FrameRate"] = TrimQuotes(args[++i]);
                }
                continue;
            }

            if (IsFlag(a, "--noReduce"))
            {
                opt.ProcessorParameters["ReduceKeys"] = "False";
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
    /// Adds a processor parameter from Name=Value syntax.
    /// </summary>
    private static void AddProcessorParam(Options opt, string text)
    {
        if (opt == null || string.IsNullOrWhiteSpace(text))
            return;

        int eq = text.IndexOf('=');
        if (eq <= 0)
            return;

        string name = text.Substring(0, eq).Trim();
        string value = text.Substring(eq + 1).Trim();

        if (string.IsNullOrWhiteSpace(name))
            return;

        opt.ProcessorParameters[name] = value;
    }

    /// <summary>
    /// Uses the bundled DNA.SkinnedPipeline processor automatically when it is available.
    /// This keeps drag/drop normal model builds socket-safe without requiring users to type
    /// --processor ScaledModelProcessor every time.
    /// </summary>
    private static void ApplyDefaultScaledModelProcessorIfAvailable(Options opt)
    {
        if (opt == null)
            return;

        if (!string.IsNullOrWhiteSpace(opt.FbxProcessor))
            return;

        string bundledDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SkinedModelProcessor", "DNA.SkinnedPipeline.dll");
        if (File.Exists(bundledDll))
        {
            AddUniquePath(opt.ExtraPipeline, bundledDll);
            opt.FbxProcessor = "ScaledModelProcessor";
            return;
        }

        // If the caller already supplied a pipeline folder/file that looks like the DNA pipeline,
        // still select ScaledModelProcessor by default.
        foreach (var raw in opt.ExtraPipeline)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string p = raw.Trim().Trim('"');

            if (File.Exists(p) && string.Equals(Path.GetFileName(p), "DNA.SkinnedPipeline.dll", StringComparison.OrdinalIgnoreCase))
            {
                opt.FbxProcessor = "ScaledModelProcessor";
                return;
            }

            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "DNA.SkinnedPipeline.dll")))
            {
                opt.FbxProcessor = "ScaledModelProcessor";
                return;
            }
        }
    }

    /// <summary>
    /// Builds the final processor parameter set for a conversion.
    /// Adds a calculated Scale for standard model processors unless the user already supplied Scale,
    /// disabled auto-scale, or selected an animation-only processor.
    /// </summary>
    private static Dictionary<string, string> BuildEffectiveProcessorParameters(Options opt)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (opt != null && opt.ProcessorParameters != null)
        {
            foreach (var pair in opt.ProcessorParameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                parameters[pair.Key.Trim()] = pair.Value ?? "";
            }
        }

        if (opt != null &&
            !opt.DisableFbxScale &&
            !parameters.ContainsKey("Scale") &&
            ShouldApplyDefaultFbxModelScale(opt.FbxProcessor))
        {
            float fbxComp = opt.FbxComp ?? DefaultExtractorFbxComp;
            float scale = CalculateModelScaleForFbxComp(fbxComp);
            parameters["Scale"] = scale.ToString(CultureInfo.InvariantCulture);
        }

        return parameters;
    }

    /// <summary>
    /// Adds the rigid mesh restore sidecar processor parameter when one is available for the FBX.
    /// </summary>
    /// <remarks>
    /// TexturePacks writes a <c>.cmzrigid.ini</c> sidecar when rigid mesh rotation normalization is used.
    /// FbxToXnb passes that sidecar into the content pipeline so the processor can undo the
    /// Blender-friendly authoring rotation cleanup and rebuild the model in game-space.
    /// 
    /// An explicitly supplied <c>RigidMeshRestoreFile</c> always wins. Otherwise, the converter attempts
    /// to auto-detect the sidecar beside the FBX using the normal TexturePacks round-trip naming flow.
    /// </remarks>
    private static void ApplyRigidMeshRestoreSidecarIfAvailable(string fbxPath, Dictionary<string, string> processorParameters)
    {
        if (string.IsNullOrWhiteSpace(fbxPath) || processorParameters == null)
            return;

        if (processorParameters.ContainsKey("RigidMeshRestoreFile"))
        {
            EchoRigidMeshRestoreSidecar(processorParameters["RigidMeshRestoreFile"], "explicit");
            return;
        }

        string sidecar = FindRigidMeshRestoreSidecar(fbxPath, out string reason);
        if (!string.IsNullOrWhiteSpace(sidecar))
        {
            processorParameters["RigidMeshRestoreFile"] = Path.GetFullPath(sidecar);
            EchoRigidMeshRestoreSidecar(sidecar, reason);
        }
    }

    /// <summary>
    /// Finds the best <c>.cmzrigid.ini</c> sidecar for a given FBX file.
    /// </summary>
    /// <remarks>
    /// The preferred match is an exact base-name match, such as <c>Raygun.fbx</c> with
    /// <c>Raygun.cmzrigid.ini</c>. If the user renamed the FBX after editing in Blender, this falls back
    /// to common TexturePacks clues such as embedded FBX references, nearby texture names, or a single
    /// sidecar in the folder.
    /// </remarks>
    private static string FindRigidMeshRestoreSidecar(string fbxPath, out string reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(fbxPath))
            return null;

        string dir = Path.GetDirectoryName(fbxPath);
        if (string.IsNullOrWhiteSpace(dir))
            dir = ".";

        if (!Directory.Exists(dir))
            return null;

        string asset = Path.GetFileNameWithoutExtension(fbxPath);
        string exact = Path.Combine(dir, asset + ".cmzrigid.ini");

        if (File.Exists(exact))
        {
            reason = "exact name";
            return exact;
        }

        string[] sidecars = Directory.GetFiles(dir, "*.cmzrigid.ini", SearchOption.TopDirectoryOnly);
        if (sidecars.Length == 0)
            return null;

        // Common workflow: export 0051_Pistol.glb + 0051_Pistol.cmzrigid.ini,
        // edit in Blender, then save/convert as Raygun.fbx. In that case the FBX
        // no longer shares the sidecar's base name, but it usually still contains
        // the texture/source name such as 0051_Pistol_model.png. Match that first.
        string[] fbxMatched = sidecars
            .Where(path => FbxAppearsToReferenceSidecarBase(fbxPath, GetRigidMeshSidecarBase(path)))
            .ToArray();

        if (fbxMatched.Length == 1)
        {
            reason = "FBX reference match";
            return fbxMatched[0];
        }

        // Secondary hint: if exactly one sidecar has a nearby texture with the same
        // exported model base name, use it. This catches folders where the FBX importer
        // strips/rewrites texture strings.
        string[] textureMatched = sidecars
            .Where(path => DirectoryContainsTextureForSidecarBase(dir, GetRigidMeshSidecarBase(path)))
            .ToArray();

        if (textureMatched.Length == 1)
        {
            reason = "texture name match";
            return textureMatched[0];
        }

        if (sidecars.Length == 1)
        {
            reason = "single sidecar fallback";
            return sidecars[0];
        }

        Console.WriteLine("  ! Multiple .cmzrigid.ini files found and none matched this FBX. Use --rigidMeshRestoreFile \"path\\to\\model.cmzrigid.ini\".");
        return null;
    }

    /// <summary>
    /// Gets the original exported model base name from a rigid mesh restore sidecar path.
    /// </summary>
    /// <remarks>
    /// Sidecars use the compound extension <c>.cmzrigid.ini</c>, so this trims that full suffix instead
    /// of only removing <c>.ini</c>. For example, <c>0051_Pistol.cmzrigid.ini</c> becomes
    /// <c>0051_Pistol</c>.
    /// </remarks>
    private static string GetRigidMeshSidecarBase(string path)
    {
        string name = Path.GetFileName(path) ?? "";

        const string suffix = ".cmzrigid.ini";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - suffix.Length);

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// Checks whether the FBX file appears to reference the source model name used by a sidecar.
    /// </summary>
    /// <remarks>
    /// This supports the common Blender workflow where the edited FBX is renamed, but still contains
    /// texture or source references such as <c>0051_Pistol</c> or <c>0051_Pistol_model</c>.
    /// </remarks>
    private static bool FbxAppearsToReferenceSidecarBase(string fbxPath, string sidecarBase)
    {
        if (string.IsNullOrWhiteSpace(fbxPath) || string.IsNullOrWhiteSpace(sidecarBase) || !File.Exists(fbxPath))
            return false;

        try
        {
            byte[] bytes = File.ReadAllBytes(fbxPath);
            string text = System.Text.Encoding.UTF8.GetString(bytes);
            return text.IndexOf(sidecarBase, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf(sidecarBase + "_model", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks for nearby texture files that indicate a sidecar belongs to the current FBX folder.
    /// </summary>
    /// <remarks>
    /// TexturePacks exports commonly use names like <c>0051_Pistol_model.png</c>. This fallback helps
    /// auto-detect the correct restore sidecar when the FBX no longer contains readable source strings.
    /// </remarks>
    private static bool DirectoryContainsTextureForSidecarBase(string dir, string sidecarBase)
    {
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(sidecarBase))
            return false;

        string[] patterns =
        {
        sidecarBase + ".png",
        sidecarBase + "_model.png",
        sidecarBase + "_model_0.png",
        sidecarBase + "*.png"
    };

        foreach (string pattern in patterns)
        {
            try
            {
                if (Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).Length > 0)
                    return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Prints the rigid mesh restore sidecar selected for this conversion.
    /// </summary>
    /// <remarks>
    /// The reason text explains whether the sidecar was explicitly supplied or auto-detected by exact
    /// name, FBX reference, texture name, or single-sidecar fallback. This makes converter logs easier
    /// to verify when debugging TexturePacks GLB → Blender → FBX → XNB round-trips.
    /// </remarks>
    private static void EchoRigidMeshRestoreSidecar(string sidecarPath, string reason)
    {
        if (string.IsNullOrWhiteSpace(sidecarPath))
            return;

        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : " (" + reason + ")";
        Console.WriteLine("  * Rigid Mesh Restore: " + sidecarPath + suffix);
    }

    /// <summary>
    /// Converts a TexturePacks FbxComp value into the XNA ModelProcessor Scale value needed for game-ready content.
    /// Example: FbxComp=10.0 -> Scale=0.001 because 0.01 / 10.0 = 0.001.
    /// </summary>
    private static float CalculateModelScaleForFbxComp(float fbxComp)
    {
        if (fbxComp <= 0f || float.IsNaN(fbxComp) || float.IsInfinity(fbxComp))
            return GameFbxModelScale / DefaultExtractorFbxComp;

        return GameFbxModelScale / fbxComp;
    }

    /// <summary>
    /// Only auto-scale processors that are expected to produce models.
    /// AnimationClipProcessor is intentionally excluded because it builds animation clips, not model geometry.
    /// </summary>
    private static bool ShouldApplyDefaultFbxModelScale(string processorName)
    {
        if (string.IsNullOrWhiteSpace(processorName))
            return true; // XNBBuilderEx default is ModelProcessor.

        processorName = processorName.Trim();

        if (processorName.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return processorName.IndexOf("ModelProcessor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Parses positive float values using invariant culture while accepting comma decimal separators.
    /// </summary>
    private static bool TryParsePositiveFloat(string text, out float value)
    {
        value = 0f;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().Replace(',', '.');

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
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
        Console.WriteLine("  --processor <name>        FBX processor name (ex: ScaledModelProcessor, SkinedModelProcessor)");
        Console.WriteLine("  --fbxComp <value>         TexturePacks [Models] FbxComp; computes Scale=0.01/FbxComp");
        Console.WriteLine("  --scale <value>           Manual FBX model scale override");
        Console.WriteLine("  --authoringLocation <x,y,z>");
        Console.WriteLine("                             Inverse-correct Blender RootNode UI Location from TexturePacks config");
        Console.WriteLine("  --authoringRotation <x,y,z | w,x,y,z>");
        Console.WriteLine("                             3 values = Blender Euler degrees X,Y,Z; 4 values = Quaternion W,X,Y,Z");
        Console.WriteLine("  --authoringRotationDegrees <x,y,z>");
        Console.WriteLine("                             Explicit Blender Euler degrees form; alias for --authoringRotation");
        Console.WriteLine("  --authoringRotationQuaternion <w,x,y,z>");
        Console.WriteLine("                             Explicit/legacy Blender RootNode UI Quaternion; order is W,X,Y,Z");
        Console.WriteLine("  --authoringLocationScale <value>");
        Console.WriteLine("                             Advanced: default 100 for Blender FBX importer units");
        Console.WriteLine("  --rigidMeshRestoreFile <path>");
        Console.WriteLine("                             Optional TexturePacks .cmzrigid.ini sidecar");
        Console.WriteLine("                             Auto-detect tries exact FBX name, FBX/texture reference match, then single-sidecar fallback");
        Console.WriteLine("  --noScale                 Do not auto-apply the calculated FBX model scale");
        Console.WriteLine("  --param Name=Value        Generic processor parameter");
        Console.WriteLine("                              Useful socket params:");
        Console.WriteLine("                              SocketDebugLog=True");
        Console.WriteLine("                              SocketBakeToModelRoot=False");
        Console.WriteLine("                              SocketPostProcessCorrection=True");
        Console.WriteLine("                              SocketBasisTransform=BlenderGlbRoundTripForward");
        Console.WriteLine("                              SocketBasisScale=0.01");
        Console.WriteLine("                              SocketTranslationScale=<manual override>");
        Console.WriteLine("                              SocketRotationCorrection=True");
        Console.WriteLine("                              SocketRotationCorrectionAxis=Y");
        Console.WriteLine("                              SocketRotationCorrectionDegrees=180");
        Console.WriteLine("  --animName <name>         AnimationClipProcessor output clip name");
        Console.WriteLine("  --sourceClip <name>       AnimationClipProcessor source FBX take");
        Console.WriteLine("  --frameRate <fps>         AnimationClipProcessor sample rate, usually 30");
        Console.WriteLine("  --noReduce                Keep all sampled animation keys");
        Console.WriteLine("  --help                    Show help");
        Console.WriteLine();
        Console.WriteLine("Env:");
        Console.WriteLine("  CMZ_PIPELINE=path1;path2;...  (dlls or dirs added automatically)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  FbxToXnbXna.exe \"C:\\...\\0051_Pistol_model.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --fbxComp 10.0 \"C:\\...\\TexturePacksRoundTrip.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --scale 0.001 \"C:\\...\\ManualScaleOverride.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --processor ScaledModelProcessor --pipelineDir \"C:\\...\\SkinedModelProcessor\" --fbxComp 10.0 \"C:\\...\\0051_Pistol_model.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --processor SkinedModelProcessor --pipelineDir \"C:\\...\\PipelineBin\" --fbxComp 10.0 \"C:\\...\\ALIEN.fbx\"");
        Console.WriteLine("  FbxToXnbXna.exe --processor AnimationClipProcessor --pipelineDir \"C:\\...\\SkinedModelProcessor\" --animName Reload \"C:\\...\\reload.fbx\"");
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

            // Stage candidate texture files into the TEMP work folder so the FBX importer
            // can resolve exact material/texture references such as:
            //   texture.png
            //   texture_0.png
            //   texture_1.png
            //   albedo.jpg
            //
            // This keeps the source folder untouched while allowing multi-texture FBXs
            // to compile correctly through the XNA pipeline.
            var stagedTextures = StageCandidateTextureFiles(srcDir, work, asset);

            if (stagedTextures.Count > 0)
            {
                Console.WriteLine($"  * Staged {stagedTextures.Count} texture file(s) into temp:");
                foreach (var tex in stagedTextures)
                    Console.WriteLine($"      - {tex}");
            }

            Directory.SetCurrentDirectory(work);

            // --- Invoke pipeline build ---

            var builder = new XNBBuilderEx(targetPlatform: "Windows", targetProfile: "Reach", compressContent: true)
            {
                LogFilePath = Path.Combine(outDir, "logfile.txt")
            };

            // If the bundled CMZ/DNA pipeline is available and the user did not choose a
            // processor, prefer ScaledModelProcessor so transform-only sockets such as
            // BarrelTip receive the same round-trip scale as visible mesh geometry.
            ApplyDefaultScaledModelProcessorIfAvailable(opt);

            // OPTIONAL: If you add a property in XNBBuilderEx like builder.FbxProcessorName, set it here.
            // If you DIDN'T add such a property, ignore this and keep processor selection inside XNBBuilderEx.
            var effectiveProcessorParameters = BuildEffectiveProcessorParameters(opt);
            ApplyRigidMeshRestoreSidecarIfAvailable(fbxPath, effectiveProcessorParameters);

            TrySetBuilderFbxProcessor(builder, opt.FbxProcessor);
            TrySetBuilderProcessorParameters(builder, effectiveProcessorParameters);

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
            if (!opt.DisableFbxScale && ShouldApplyDefaultFbxModelScale(opt.FbxProcessor) && !opt.ProcessorParameters.ContainsKey("Scale"))
            {
                float fbxComp = opt.FbxComp ?? DefaultExtractorFbxComp;
                Console.WriteLine($"  * FbxComp: {fbxComp.ToString(CultureInfo.InvariantCulture)} -> Scale={CalculateModelScaleForFbxComp(fbxComp).ToString(CultureInfo.InvariantCulture)}");
            }
            if (effectiveProcessorParameters.Count > 0)
                Console.WriteLine($"  * Processor Params: {string.Join(", ", effectiveProcessorParameters.Select(kv => kv.Key + "=" + kv.Value))}");

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
    /// Stages likely texture files for the FBX into the TEMP build folder while
    /// preserving their relative folder structure from the FBX source directory.
    ///
    /// Purpose:
    /// - Supports textures placed beside the FBX.
    /// - Supports textures inside subfolders such as "textures\", "materials\", etc.
    /// - Preserves the relative paths the FBX importer expects during pipeline build.
    /// - Still provides legacy "texture.png" alias behavior when "<asset>.png" exists.
    ///
    /// Notes:
    /// - Texture files are copied recursively from the FBX source folder.
    /// - Relative subfolders are recreated under the temp work folder.
    /// - This is more flexible than hardcoding a single "textures\" subfolder.
    /// </summary>
    private static List<string> StageCandidateTextureFiles(string srcDir, string workDir, string asset)
    {
        var copied = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] textureExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".tga",
            ".dds"
        };

        if (!Directory.Exists(srcDir))
            return copied;

        foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (string.IsNullOrWhiteSpace(ext))
                continue;

            bool isTexture = false;
            for (int i = 0; i < textureExtensions.Length; i++)
            {
                if (ext.Equals(textureExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    isTexture = true;
                    break;
                }
            }

            if (!isTexture)
                continue;

            string relativePath = GetRelativePathSafe(srcDir, file);

            // Skip anything suspicious that escapes the source root.
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.StartsWith("..\\", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("../", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dest = Path.Combine(workDir, relativePath);
            string destFolder = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destFolder))
                Directory.CreateDirectory(destFolder);

            File.Copy(file, dest, overwrite: true);

            if (seen.Add(relativePath))
                copied.Add(relativePath);
        }

        // Preserve old sidecar compatibility:
        // If "<asset>.png" exists beside the FBX, also expose "texture.png" in temp root.
        string sidecarPng = Path.Combine(srcDir, asset + ".png");
        if (File.Exists(sidecarPng))
        {
            string textureAlias = Path.Combine(workDir, "texture.png");
            if (!File.Exists(textureAlias))
                File.Copy(sidecarPng, textureAlias, overwrite: true);

            if (seen.Add("texture.png"))
                copied.Add("texture.png (alias of " + Path.GetFileName(sidecarPng) + ")");
        }

        return copied;
    }

    /// <summary>
    /// Returns a relative path from baseDir to fullPath using URI logic for
    /// .NET Framework compatibility.
    /// </summary>
    private static string GetRelativePathSafe(string baseDir, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(fullPath))
            return null;

        string baseDirFull = Path.GetFullPath(baseDir);
        string fullPathFull = Path.GetFullPath(fullPath);

        if (!baseDirFull.EndsWith("\\", StringComparison.Ordinal))
            baseDirFull += "\\";

        var baseUri = new Uri(baseDirFull, UriKind.Absolute);
        var fileUri = new Uri(fullPathFull, UriKind.Absolute);

        Uri relativeUri = baseUri.MakeRelativeUri(fileUri);
        string relative = Uri.UnescapeDataString(relativeUri.ToString());

        return relative.Replace('/', '\\');
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

    /// <summary>
    /// Copies parsed processor parameters into XNBBuilderEx when the property exists.
    /// </summary>
    private static void TrySetBuilderProcessorParameters(object builder, IDictionary<string, string> parameters)
    {
        if (builder == null || parameters == null || parameters.Count == 0)
            return;

        try
        {
            var t = builder.GetType();
            var p = t.GetProperty("ProcessorParameters");
            if (p == null)
                return;

            if (!(p.GetValue(builder, null) is IDictionary<string, string> target))
                return;

            foreach (var pair in parameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                target[pair.Key] = pair.Value ?? "";
            }
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

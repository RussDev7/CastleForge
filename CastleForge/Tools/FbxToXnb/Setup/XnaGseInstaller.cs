/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

/// <summary>
/// XNA Game Studio 4.0 (Content Pipeline refs) bootstrap helper.
///
/// Summary:
/// - Validates the presence of the XNA Content Pipeline reference DLLs used by XNBBuilder.
/// - If missing, prompts the user and installs from an embedded MSI via elevated msiexec.
/// - Supports both "system install" and optional app-local refs folder fallback.
///
/// Typical usage:
/// - Call early in the converter application's Main() before any XNB build work begins.
/// </summary>
internal static class XnaGseInstaller
{
    #region Constants / Requirements

    /// <summary>
    /// Match the pipeline/reference DLLs your XNBBuilder expects.
    ///
    /// Notes:
    /// - These are *build-time* pipeline references (Microsoft.Xna.Framework.Content.Pipeline.*),
    ///   not the in-game runtime redistributable.
    /// - If any are missing, the BuildContent task will fail or throw during assembly load.
    /// </summary>
    private static readonly string[] RequiredPipelineDlls =
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

    /// <summary>
    /// Default location for XNA GSE 4.0 Windows/x86 pipeline reference DLLs.
    ///
    /// Notes:
    /// - This matches the common install layout for XNA Game Studio v4.0.
    /// - Your builder can also support an app-local copy under "References\Windows\x86".
    /// </summary>
    private static readonly string DefaultRefsDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft XNA", "XNA Game Studio", "v4.0", "References", "Windows", "x86");

    #endregion

    #region Public API

    /// <summary>
    /// Ensures XNA GSE 4.0 pipeline reference DLLs exist; if missing, prompts and installs from embedded MSI.
    ///
    /// Summary:
    /// - Checks system install path first.
    /// - Falls back to app-local refs folder if present.
    /// - If missing everywhere, prompts user and runs msiexec elevated.
    ///
    /// Call site:
    /// - Call this early in Main() before attempting any pipeline build.
    /// </summary>
    public static bool EnsureInstalledWithPrompt()
    {
        if (HasRequiredDlls(DefaultRefsDir))
            return true;

        // Try app-local fallback (if you also support shipping refs next to the app)
        var appLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"References\Windows\x86");
        if (HasRequiredDlls(appLocal))
            return true;

        var msg =
            "XNA Game Studio 4.0 pipeline references were not found.\n\n" +
            "Required folder:\n" + DefaultRefsDir + "\n\n" +
            "Would you like to install them now?\n\n" +
            "Note: This may prompt for administrator permission.";

        var res = MessageBox.Show(msg, "Missing XNA Pipeline References",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (res != DialogResult.Yes)
            return false;

        try
        {
            // Extract embedded MSI to temp.
            var tempMsi = ExtractEmbeddedMsiToTemp(
                resourceNameEndsWith: "XNA Game Studio Shared.msi");

            // Install (elevated).
            int code = RunMsiInstallElevated(tempMsi);

            // Cleanup temp MSI (best-effort).
            try { File.Delete(tempMsi); } catch { }

            // Re-check after install.
            if (HasRequiredDlls(DefaultRefsDir) || HasRequiredDlls(appLocal))
            {
                MessageBox.Show(
                    "XNA pipeline references installed.",
                    "Install Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }

            MessageBox.Show(
                $"Installer finished, but required DLLs are still missing.\n\nmsiexec exit code: {code}.",
                "Install Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Install failed:\n\n{ex.Message}.",
                "Install Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
    #endregion

    #region Detection (Refs Present?)

    /// <summary>
    /// True if the provided directory exists and contains all required pipeline DLLs.
    ///
    /// Notes:
    /// - This is an intentionally strict gate: "all or nothing" to avoid partial installs
    ///   that fail later inside the pipeline task.
    /// </summary>
    private static bool HasRequiredDlls(string refsDir)
    {
        if (string.IsNullOrWhiteSpace(refsDir) || !Directory.Exists(refsDir))
            return false;

        return RequiredPipelineDlls.All(d => File.Exists(Path.Combine(refsDir, d)));
    }
    #endregion

    #region Embedded MSI Extraction

    /// <summary>
    /// Locates the embedded MSI resource (by suffix match), writes it to a temp file, and returns the path.
    ///
    /// Notes:
    /// - The suffix lookup avoids hard-coding your namespace/resource prefix.
    /// - Ensure the MSI is marked "Embedded Resource" in the project file properties.
    /// </summary>
    private static string ExtractEmbeddedMsiToTemp(string resourceNameEndsWith)
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();

        // Find resource by suffix so you don't have to guess the exact namespace prefix.
        var resName = names.FirstOrDefault(n =>
            n.EndsWith(resourceNameEndsWith, StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException(
                "Embedded MSI not found. Ensure Build Action = Embedded Resource.\n\n" +
                $"Expected resource ending with: {resourceNameEndsWith}.");
        var outPath = Path.Combine(Path.GetTempPath(),
            "XnaShared_" + Guid.NewGuid().ToString("N") + ".msi");

        using (var s = asm.GetManifestResourceStream(resName))
        {
            if (s == null)
                throw new InvalidOperationException($"Failed to open embedded MSI stream: {resName}.");

            using (var fs = File.Create(outPath))
                s.CopyTo(fs);
        }

        return outPath;
    }
    #endregion

    #region MSI Install (Elevated)

    /// <summary>
    /// Runs msiexec to install the extracted MSI and returns the msiexec exit code.
    ///
    /// Notes:
    /// - Uses Verb="runas" to trigger a UAC prompt (admin elevation).
    /// - /passive shows progress UI but avoids interactive prompts (still may require UAC).
    /// - If you ever want fully silent installs, /qn is the usual switch.
    /// </summary>
    private static int RunMsiInstallElevated(string msiPath)
    {
        // /i = install
        // /passive = shows progress UI but no prompts (still may require UAC)
        // You can swap to /qn for fully silent, or remove for interactive.
        var args = $"/i \"{msiPath}\" /passive";

        var psi = new ProcessStartInfo
        {
            FileName        = "msiexec.exe",
            Arguments       = args,
            UseShellExecute = true,
            Verb            = "runas", // Triggers UAC elevation prompt.
        };

        using (var p = Process.Start(psi))
        {
            p.WaitForExit();

            // Common exit codes:
            // 0 = success
            // 3010 = success, restart required
            // 1603 = fatal error
            return p.ExitCode;
        }
    }
    #endregion
}
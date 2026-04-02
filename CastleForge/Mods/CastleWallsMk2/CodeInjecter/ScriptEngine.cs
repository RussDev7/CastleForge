/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Reflection;
using Microsoft.CSharp;
using System.Linq;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Tiny runtime script engine that:
    ///  1) Wraps user code into a stable entrypoint (UserScript.Script.Run).
    ///  2) Compiles it in-memory against all currently loaded game/mod assemblies.
    ///  3) Invokes Run() and returns its stringified result (and/or compiler errors).
    ///
    /// Notes:
    ///  • Supports "full-source mode" when the snippet contains // @full, [HarmonyPatch], or defines classes/namespaces.
    ///    In that case, the source is compiled verbatim (no wrapper), which enables inline Harmony patch classes.
    ///  • References are de-duplicated by simple name to avoid CS1703 ("already been imported") when the same BCL
    ///    assembly is seen from multiple locations (GAC vs local redist).
    ///  • The most recent compiled type is cached if keepAssembly==true AND the source hash is unchanged.
    /// </summary>
    internal static class ScriptEngine
    {
        #region Cached State

        private static string _lastSourceHash = null;
        private static Type   _lastType       = null;

        #endregion

        #region Public API

        /// <summary>
        /// Compile and run a user snippet (or full source).
        /// </summary>
        /// <param name="userCode">Either a method body (snippet) or a full C# source file (see Wrap).</param>
        /// <param name="keepAssembly">If true, reuse the previously compiled assembly when the source hash matches.</param>
        /// <returns>(ok: success flag, result: user Run() return text, log: compiler/exception text)</returns>
        public static (bool ok, string result, string log) Run(string userCode, bool keepAssembly)
        {
            try
            {
                // Wrap user code as a method body OR detect full-source and pass through.
                string src = Wrap(userCode);

                // Cache hit: Reuse last compiled type if requested and source unchanged.
                string hash = keepAssembly ? FastHash(src) : null;
                Type   t    = null;

                if (keepAssembly && hash == _lastSourceHash && _lastType != null)
                {
                    t = _lastType;
                }
                else
                {
                    // Compile in-memory.
                    using (var provider = new CSharpCodeProvider())
                    {
                        var cp = new CompilerParameters
                        {
                            GenerateExecutable    = false,
                            GenerateInMemory      = true,
                            TreatWarningsAsErrors = false,
                            CompilerOptions       = "/optimize"
                        };

                        // --- Reference management ---
                        // Collect already-loaded, file-backed assemblies, then add them DISTINCTLY.
                        var loadedRefs = AppDomain.CurrentDomain.GetAssemblies()
                            .Where(a => { try { return !a.IsDynamic && !string.IsNullOrEmpty(a.Location); } catch { return false; } })
                            .Select(a => a.Location)
                            .Distinct(StringComparer.OrdinalIgnoreCase);

                        foreach (var path in loadedRefs)
                            AddRefDistinct(cp, path);

                        // Fill in any missing BCL references (deduped by simple name: System, System.Core, etc.).
                        FillReferences(cp);

                        // Ensure Harmony is referenced exactly once.
                        EnsureHarmonyReference(cp);

                        // --- Compile ---
                        var res = provider.CompileAssemblyFromSource(cp, src);
                        if (res.Errors.HasErrors)
                        {
                            var sw = new StringWriter();
                            foreach (CompilerError e in res.Errors)
                                if (!e.IsWarning) sw.WriteLine(e.ToString());
                            return (false, null, sw.ToString());
                        }

                        var asm = res.CompiledAssembly;
                        t = asm.GetType("UserScript.Script", throwOnError: true);

                        if (keepAssembly)
                        {
                            _lastSourceHash = hash;
                            _lastType       = t;
                        }
                    }
                }

                // Invoke Run().
                var run = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                object result = run.Invoke(null, null);
                return (true, result?.ToString(), "");
            }
            catch (Exception ex)
            {
                return (false, null, ex.ToString());
            }
        }
        #endregion

        #region Reference Management

        /// <summary>
        /// Add a file reference if it is not already present (path-compare, case-insensitive).
        /// Helps prevent duplicate path entries in CompilerParameters.
        /// </summary>
        private static void AddRefDistinct(CompilerParameters cp, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!cp.ReferencedAssemblies.Cast<string>()
                  .Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            {
                try { cp.ReferencedAssemblies.Add(path); } catch { /* Ignore. */ }
            }
        }

        /// <summary>
        /// De-duplicates loaded framework/engine references by simple name (e.g., "System"),
        /// keeping the highest version we see, then adds exactly one path per simple name.
        /// This avoids CS1703 ("already been imported") when the compiler resolves two copies
        /// of the same assembly identity from different locations.
        /// </summary>
        private static void FillReferences(CompilerParameters cp)
        {
            // Map: Simple name -> (version, path).
            var pick = new Dictionary<string, (Version ver, string path)>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (a.IsDynamic) continue;
                    var loc = a.Location;
                    if (string.IsNullOrEmpty(loc)) continue;

                    var an   = a.GetName();
                    var name = an.Name; // "System", "System.Core", "Microsoft.Xna.Framework", etc.
                    var ver  = an.Version ?? new Version(0, 0, 0, 0);

                    if (!pick.TryGetValue(name, out var cur) || ver > cur.ver)
                        pick[name] = (ver, loc);
                }
                catch
                {
                    // Some BCL proxies throw on Location; ignore them.
                }
            }

            // Belt & suspenders: If these weren't found via GetAssemblies, let CodeDom resolve from GAC.
            void Ensure(string simpleName)
            {
                if (!pick.ContainsKey(simpleName))
                    pick[simpleName] = (new Version(0, 0, 0, 0), simpleName + ".dll");
            }
            Ensure("System");
            Ensure("System.Core");
            Ensure("Microsoft.CSharp");
            // NOTE: mscorlib is injected automatically by the compiler; do NOT add it.

            // Add exactly one path per simple name (distinct path compare).
            foreach (var path in pick.Values.Select(v => v.path).Distinct(StringComparer.OrdinalIgnoreCase))
                AddRefDistinct(cp, path);
        }

        /// <summary>
        /// Ensure Harmony (0Harmony.dll) is available to the compiler exactly once.
        ///  1) Prefer the already-loaded Harmony assembly path.
        ///  2) Otherwise extract an embedded copy to a private refs folder and reference it.
        /// </summary>
        private static void EnsureHarmonyReference(CompilerParameters cp)
        {
            // Try to grab the loaded Harmony assembly by simple name ("0Harmony").
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase));

            // If it has a real Location, reference that file.
            if (loaded != null)
            {
                try
                {
                    var loc = loaded.Location; // Empty if loaded from bytes.
                    if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    {
                        ModLoader.LogSystem.Log("test");
                        AddRefDistinct(cp, loc);
                        return;
                    }
                }
                catch
                {
                    // Some assemblies throw on Location; ignore and fall back.
                }
            }

            // Fall back: Extract embedded 0Harmony.dll from this assembly's resources.
            var hostAsm = Assembly.GetExecutingAssembly();
            var resName = hostAsm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("0Harmony.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Embedded 0Harmony.dll not found in resources.");

            var refsDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(ScriptEngine).Namespace, "CodeInjector", "_Refs");
            Directory.CreateDirectory(refsDir);
            var outPath  = Path.Combine(refsDir, "0Harmony.dll");

            using (var s = hostAsm.GetManifestResourceStream(resName))
            {
                if (s == null) throw new InvalidOperationException("Failed to open embedded 0Harmony.dll resource stream.");

                bool write = true;
                if (File.Exists(outPath))
                {
                    try
                    {
                        using (var fs = File.OpenRead(outPath))
                            write = fs.Length != s.Length; // Cheap integrity check.
                    }
                    catch { write = true; }
                    s.Position = 0;
                }

                if (write)
                {
                    using (var fs = File.Create(outPath))
                        s.CopyTo(fs);
                }
            }

            AddRefDistinct(cp, outPath);
        }
        #endregion

        #region Snippet Wrapper (C#5-Safe)

        /// <summary>
        /// Wraps a snippet into a minimal program shell:
        ///  namespace UserScript { public static class Script { public static object Run() { /* body */ return ""; } } }
        ///
        /// Full-source mode:
        ///  If the text contains "// @full", "[HarmonyPatch]", "namespace ", or "class " it is compiled verbatim.
        ///  This allows defining patch classes and other types directly in the editor.
        /// </summary>
        private static string Wrap(string body)
        {
            // If user wants to supply their own namespace/classes (Harmony patches), compile verbatim.
            if (body.Contains("// @full") ||
                body.Contains("[HarmonyPatch") ||
                body.Contains("namespace ") ||
                body.Contains("class "))
                return body;

            var wrapper = new[]
            {
                @"using System;",
                @"using DNA.CastleMinerZ;",
                @"using DNA.CastleMinerZ.Inventory;",
                @"using Microsoft.Xna.Framework;",
                @"",
                @"namespace UserScript",
                @"{",
                @"    public static class Script",
                @"    {",
                @"        public static object Run()",
                @"        {",
                @"            " + (body ?? string.Empty),
                @"            return """";",
                @"        }",
                @"    }",
                @"}",
            };

            // NOTE: Using string.Join keeps this C#5-friendly (no string interpolation).
            return string.Join(Environment.NewLine, wrapper);
        }
        #endregion

        #region Utilities

        /// <summary>
        /// Fast, stable hash for source change detection (FNV-1a).
        /// </summary>
        private static string FastHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++)
                    h = (h ^ s[i]) * 16777619;
                return h.ToString("x8");
            }
        }
        #endregion
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Linq;
using DNA.Input;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace ModLoader
{
    /// <summary>
    /// Manages discovery, loading, and ticking of all mods derived from ModBase.
    /// </summary>
    public static class ModManager
    {
        // Internal list of loaded mod instances.
        private static readonly List<ModBase> _loaded = new List<ModBase>();

        /// <summary>
        /// Discover, order, and load all mods from <paramref name="pluginDir"/>.
        /// </summary>
        /// <remarks>
        /// Overview:
        ///   1) Discover: Scan all *.dll in the folder (and keep loader hints).
        ///   2) Order: Sort candidates by priority (desc), then by name; still respect RequiredDependencies.
        ///   3) Load: Topological pass - only load when all required mod names are already loaded.
        ///   4) Summarize: Print success/failure counts.
        ///
        /// Notes:
        ///   • Priority: Higher is earlier (e.g., First=800, VeryHigh=700, ..., Last=0).
        ///   • If a mod has no [Priority], it defaults to Priority.Normal (400).
        ///   • Dependencies are by ModBase.Name (case-insensitive).
        ///   • Deterministic: Ties break by Type.FullName so order is stable across runs.
        /// </remarks>
        /// <param name="pluginDir">Path to the folder containing mod DLLs (e.g. "!Mods").</param>
        public static void LoadMods(string pluginDir, IProgress<ModLoadProgressInfo> progress = null)
        {
            #region Load Progress Reporting

            // Running counters used by the optional progress UI.
            // These are intentionally declared once here so the local Report(...) helper
            // always sees the live values across the entire load process.
            int totalCount     = 0;
            int processedCount = 0;
            int loadedCount    = 0;
            int failedCount    = 0;

            /// <summary>
            /// Reports a snapshot of the current load state to the temporary progress window.
            /// Safe to call even when no UI is attached because the null-conditional keeps it no-op.
            /// </summary>
            /// <param name="phase">
            /// High-level phase text shown to the user
            /// (example: "Scanning DLLs...", "Loading mods...", "Resolving dependencies...").
            /// </param>
            /// <param name="currentItem">
            /// Optional detail text for the current DLL / mod / failure being processed.
            /// </param>
            /// <param name="isIndeterminate">
            /// True while total work is not yet known; false once we have a concrete mod count.
            /// </param>
            void Report(string phase = null, string currentItem = null, bool isIndeterminate = false)
            {
                progress?.Report(new ModLoadProgressInfo
                {
                    Phase           = phase,
                    CurrentItem     = currentItem,
                    Total           = totalCount,
                    Processed       = processedCount,
                    Loaded          = loadedCount,
                    Failed          = failedCount,
                    IsIndeterminate = isIndeterminate
                });
            }
            #endregion

            #region Phase 0: Validate Directory + Seed The DLL Search

            // Progress note:
            // At this point we have not discovered any mod types yet, so the UI should stay
            // indeterminate / marquee style and only show a broad "preparing" message.
            Report("Preparing mod loader...", "Initializing mod scan...", isIndeterminate: true);

            // Write a session separator at the top of the log to mark a new load run.
            WriteLogSessionSeparator("New Session");

            // Ensure the "!Mods" directory exists (create if missing).
            try
            {
                if (!Directory.Exists(pluginDir))
                    Directory.CreateDirectory(pluginDir);
            }
            catch (Exception ex)
            {
                // Cannot access or create the directory -> cleanly abort loading.
                Log($"[LoadMods] could not create or access '{pluginDir}': {ex}.");
                Report("Mod loading aborted.", "Could not access !Mods directory.", isIndeterminate: true);
                WriteLogSessionSeparator("Initiation Completed", usePrefixNewline: false);
                return;
            }

            // Discovery hint: Scan these DLLs first. This ONLY affects discovery order, not final load order.
            // Final load order is always Priority + Dependencies.
            string[] loadOrder = new[]
            {
                "ModLoaderExtensions.dll"
            };

            // Gather all DLL filenames in the mods directory (non-recursive).
            var allDlls = Directory
                .EnumerateFiles(pluginDir, "*.dll", SearchOption.TopDirectoryOnly)
                .ToList();

            // Sort: Those explicitly listed in loadOrder[] first (in that order); others after, alphabetically.
            // This makes discovery deterministic and keeps utility libs near the front.
            var orderedDlls = allDlls
                .OrderBy(path =>
                {
                    var name = Path.GetFileName(path);
                    int idx = Array.IndexOf(loadOrder, name);
                    return idx == -1 ? loadOrder.Length + 1 : idx;          // Put non-matches last.
                })
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase) // Alphabetical within each group.
                .ToList();

            // Progress note:
            // We know how many DLL files we will inspect, but not yet how many actual mod types exist.
            Report($"Scanning {orderedDlls.Count} mod DLL(s)...", "Discovering candidate mod types...", isIndeterminate: true);

            #endregion

            #region Phase 1: Discover Candidate Mod Types Across ALL Assemblies First

            // (asm, type, priority) keeps everything we need to instantiate later.
            var candidates = new List<(Assembly asm, Type type, int priority)>();

            foreach (var dll in orderedDlls)
            {
                var dllName = Path.GetFileName(dll);
                try
                {
                    // Load the assembly from file (AssemblyLoadContext.Default for .NET Framework).
                    var asm = Assembly.LoadFrom(dll);

                    // Reflectively find all types that inherit ModBase (and are not abstract).
                    // Exported types can partially fail (missing deps, etc.)-handle gracefully.
                    Type[] exported;
                    try
                    {
                        exported = asm.GetExportedTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        foreach (var lex in rtle.LoaderExceptions.Where(e => e != null))
                            Log($"[LoadMods] LoaderException ({dllName}): {lex.GetType().Name}: {lex.Message}");
                        exported = rtle.Types.Where(t => t != null).ToArray();
                    }

                    // Filter to concrete ModBase types only.
                    var pluginTypes = exported.Where(t => t != null && !t.IsAbstract && typeof(ModBase).IsAssignableFrom(t));

                    foreach (var t in pluginTypes)
                    {
                        // Read [Priority(..)] if present; fallback to Priority.Normal.
                        // We resolve by name to avoid a hard compile dependency if the attribute sits in another dll.
                        int priority     = Priority.Normal;
                        var priorityAttr = t.GetCustomAttributes(inherit: false)
                                            .FirstOrDefault(a => a.GetType().Name == "PriorityAttribute");
                        if (priorityAttr != null)
                        {
                            var valProp = priorityAttr.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                            if (valProp?.PropertyType == typeof(int))
                            {
                                try { priority = (int)valProp.GetValue(priorityAttr); } catch { /* keep default */ }
                            }
                        }

                        candidates.Add((asm, t, priority));
                        Log($"[LoadMods] + Discovered mod type: {t.FullName} (priority {priority}).");
                    }
                }
                catch (Exception ex)
                {
                    // This DLL couldn't be loaded (bad image, missing deps, etc.). Keep going.
                    Log($"[LoadMods] Failed to load '{dllName}': {ex.GetType().Name}: {ex.Message}.");
                }
            }

            if (candidates.Count == 0)
            {
                Log("[LoadMods] No plugins discovered.");
                Report("No mods discovered.", "No valid ModBase types were found.", isIndeterminate: true);
                WriteLogSessionSeparator("Initiation Completed.", usePrefixNewline: false);
                return;
            }

            // Now that discovery is complete, switch the progress UI to determinate mode.
            // From here on out, Total = total discovered mod types, and Processed advances as each
            // type is either loaded, skipped, or failed.
            totalCount = candidates.Count;
            Report($"Discovered {totalCount} mod type(s).", "Preparing dependency-aware load pass...", isIndeterminate: false);

            // Global, deterministic ordering:
            //  1) Higher priority loads earlier,
            //  2) Ties break by full type name (stable across runs).
            candidates = candidates
                .OrderByDescending(c => c.priority)
                .ThenBy(c => c.type.FullName, StringComparer.Ordinal)
                .ToList();

            #endregion

            #region Phase 2: Dependency-Aware Load Pass (Topological With Priority Tiebreakers)

            var pending = new List<(Assembly asm, Type type, int priority)>(candidates);

            // Helper: Checks [RequiredDependencies] against already-loaded mods (by ModBase.Name).
            bool DepsMet(Type t, out string[] missing)
            {
                var req = t.GetCustomAttribute<RequiredDependencies>(); // Attribute type.
                var needed = (req?.Mods ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

                missing = needed
                    .Where(dep => !_loaded.Any(p => p.Name.Equals(dep, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                return missing.Length == 0;
            }

            // Topological passes: each pass tries to load anything whose deps are now satisfied.
            // We stop when a pass makes no progress (cycle or missing dep).
            for (bool progressMade = true; progressMade && pending.Count > 0;)
            {
                progressMade = false;

                foreach (var c in pending.ToList())
                {
                    var t = c.type;

                    if (!DepsMet(t, out var missing))
                    {
                        // Dependencies not loaded yet; leave it for the next pass.
                        continue;
                    }

                    // Construct only AFTER deps are satisfied (prevents ctor-time missing-ref issues).
                    ModBase instance;
                    try
                    {
                        instance = (ModBase)Activator.CreateInstance(t);
                    }
                    catch (Exception ex)
                    {
                        Log($"[LoadMods] Skipping '{t.FullName}' - ctor failed: {ex.GetType().Name}: {ex.Message}.");
                        pending.Remove(c);
                        processedCount++;
                        failedCount++;

                        // Progress note:
                        // Constructor failure counts as "processed" because this mod type is now finished
                        // and will not be retried in later dependency passes.
                        Report("Loading mods...", $"Ctor failed: {t.FullName}", isIndeterminate: false);
                        continue;
                    }

                    // Duplicate guard: Same Name + Version already present -> skip.
                    if (_loaded.Any(p => p.Name == instance.Name && p.Version == instance.Version))
                    {
                        Log($"[LoadMods] Duplicate '{instance.Name}' v{instance.Version} detected; skipping.");
                        pending.Remove(c);
                        processedCount++;
                        failedCount++;

                        // Progress note:
                        // Duplicate mods are treated as skipped/failed for tally purposes so the user sees
                        // forward movement and the final processed count still reaches Total.
                        Report("Loading mods...", $"Duplicate skipped: {instance.Name} v{instance.Version}", isIndeterminate: false);
                        continue;
                    }

                    // Add then Start(), but keep this wrapped so a plugin failing Start() doesn't bring down the whole loader.
                    try
                    {
                        Log($"[LoadMods] Loading '{instance.Name}' v{instance.Version} (priority {c.priority})...");
                        Report("Loading mods...", $"Starting: {instance.Name} v{instance.Version}", isIndeterminate: false);

                        _loaded.Add(instance);
                        instance.Start();

                        loadedCount++;
                        processedCount++;

                        Log($"[LoadMods] Loaded '{instance.Name}' v{instance.Version}.");
                        Report("Loading mods...", $"Loaded: {instance.Name} v{instance.Version}", isIndeterminate: false);

                        pending.Remove(c);
                        progressMade = true; // We loaded at least one this pass.
                    }
                    catch (Exception ex)
                    {
                        Log($"[LoadMods] Start() failed for '{instance.Name}': {ex}.");
                        _loaded.Remove(instance);
                        pending.Remove(c);

                        failedCount++;
                        processedCount++;

                        // Progress note:
                        // Start() failure also finalizes this mod type, so it advances Processed and Failed.
                        Report("Loading mods...", $"Start() failed: {instance.Name}", isIndeterminate: false);
                    }
                }

                // No progress this pass -> report unresolved / cyclic / missing deps and exit the loop.
                if (!progressMade && pending.Count > 0)
                {
                    foreach (var (asm, type, priority) in pending)
                    {
                        var t = type;
                        _ = DepsMet(t, out var stillMissing);
                        var why = stillMissing.Length == 0 ? "(unknown or Start failure earlier)"
                                                           : string.Join(", ", stillMissing);
                        Log($"[LoadMods] Could not load '{t.FullName}' (priority {priority}) - unmet deps: {why}.");

                        processedCount++;
                        failedCount++;

                        // Progress note:
                        // These entries are being flushed from the pending list because the dependency graph
                        // can no longer make progress (cycle, missing dep, or another earlier failure).
                        Report("Resolving dependencies...", $"Skipped: {t.FullName}", isIndeterminate: false);
                    }

                    pending.Clear();
                }
            }

            #endregion

            #region Phase 3: Summary

            // Final tally for visibility in logs.
            Log($"[LoadMods] Summary: {loadedCount} plugin(s) loaded, {failedCount} failed or skipped.");

            // Final progress snapshot so the loading UI can show a completed state just before closing.
            Report($"Finished loading mods. ({loadedCount} loaded, {failedCount} failed)", "Initialization complete.", isIndeterminate: false);

            // Close out the session in the log after initiation has completed.
            WriteLogSessionSeparator("Initiation Completed", usePrefixNewline: false);

            #endregion
        }

        // Invokes Tick() on all loaded mods. Exceptions per-mod are caught and logged.
        public static void TickAll(InputManager inputManager, GameTime gameTime)
        {
            // Copy list to avoid modification during iteration.
            foreach (var mod in _loaded.ToList())
            {
                try
                {
                    mod.Tick(inputManager, gameTime);
                }
                catch (Exception ex)
                {
                    Log($"[TickAll] mod {mod.Name} tick error: {ex}.");
                }
            }
        }
    }
}

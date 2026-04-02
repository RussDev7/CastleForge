/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using System.Collections.Generic;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using System.IO;
using System;

namespace WorldGenPlus
{
    /// <summary>
    /// Loads custom biome DLLs from:
    ///   !Mods/WorldGenPlus/CustomBiomes/*.dll
    ///
    /// Discovers non-abstract Biome subclasses that have a ctor(WorldInfo).
    /// Exposes their FULL type names so they can be used in Rings + RandomRegions.
    ///
    /// NOTE: .NET Framework cannot unload assemblies. New DLLs can be detected, but
    /// updated/replaced DLLs generally require a restart to actually take effect.
    /// </summary>
    /// <remarks>
    /// Thread-safety:
    /// - Public entry points call <see cref="Refresh"/> or <see cref="EnsureScanned"/> and use <see cref="_sync"/> for consistency.
    /// - This registry is designed to be "safe to call repeatedly" from UI, hotkeys, or builder setup code.
    ///
    /// Assembly loading behavior:
    /// - Uses <see cref="Assembly.LoadFrom(string)"/> for simplicity and reliability.
    /// - LoadFrom typically locks the DLL on disk, so replacing/updating DLLs is usually a restart operation.
    /// </remarks>
    internal static class CustomBiomeRegistry
    {
        #region Synchronization / Lifecycle

        /// <summary>
        /// Global lock for all registry state (assemblies, caches, version).
        /// </summary>
        private static readonly object _sync = new object();

        /// <summary>
        /// True once we've attempted to scan at least once.
        /// NOTE: This is used as a fast-path gate; the authoritative work happens under <see cref="_sync"/>.
        /// </summary>
        private static bool _scanned;

        /// <summary>
        /// Monotonic counter incremented whenever newly discovered DLLs are loaded and caches are rebuilt.
        /// Helpful for consumers to detect changes without diffing arrays.
        /// </summary>
        private static int _version;

        #endregion

        #region Loaded DLL Tracking

        /// <summary>
        /// Tracks absolute DLL paths already loaded (case-insensitive).
        /// NOTE: This prevents re-loading the same DLL path multiple times.
        /// </summary>
        private static readonly HashSet<string> _loadedDllPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// All assemblies successfully loaded from CustomBiomes directory.
        /// </summary>
        private static readonly List<Assembly> _loadedAssemblies = new List<Assembly>();

        #endregion

        #region Caches (Type Resolution + Discoverable List)

        /// <summary>
        /// Cache map: FullName -> Type, case-insensitive.
        /// Used to resolve types quickly when parsing INI ring/region entries.
        /// </summary>
        private static readonly Dictionary<string, Type> _typeByFullName =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sorted list of discoverable custom biome full names.
        /// Used for UI dropdowns / autocomplete / random bags.
        /// </summary>
        private static readonly List<string> _customBiomeTypeNames = new List<string>();

        #endregion

        #region Properties (Public)

        /// <summary>
        /// Gets the current registry version.
        /// Increments when NEW DLLs are loaded and caches are rebuilt.
        /// </summary>
        public static int Version
        {
            get { lock (_sync) return _version; }
        }

        /// <summary>
        /// Root folder where custom biome DLLs are discovered.
        /// </summary>
        public static string CustomBiomeDirectory
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "!Mods",
                    "WorldGenPlus",
                    "CustomBiomes");
            }
        }
        #endregion

        #region Public API

        /// <summary>
        /// Scans for NEW dlls and loads them once.
        /// Safe to call repeatedly.
        /// </summary>
        /// <remarks>
        /// - Only new DLL paths are loaded (tracked via <see cref="_loadedDllPaths"/>).
        /// - When any new DLL is loaded, caches are rebuilt and <see cref="Version"/> increments.
        /// </remarks>
        public static void Refresh()
        {
            lock (_sync)
            {
                _scanned = true;

                try
                {
                    if (!Directory.Exists(CustomBiomeDirectory))
                        Directory.CreateDirectory(CustomBiomeDirectory);

                    var dlls = Directory.GetFiles(CustomBiomeDirectory, "*.dll", SearchOption.TopDirectoryOnly);

                    bool anyNewLoaded = false;

                    for (int i = 0; i < dlls.Length; i++)
                    {
                        string path = dlls[i];
                        if (_loadedDllPaths.Contains(path))
                            continue;

                        try
                        {
                            // Simple + reliable: LoadFrom.
                            // (Locks the file; replacing usually needs a restart.)
                            var asm = Assembly.LoadFrom(path);

                            _loadedDllPaths.Add(path);
                            _loadedAssemblies.Add(asm);

                            anyNewLoaded = true;

                            Console.WriteLine($"Loaded custom biome DLL: {Path.GetFileName(path)}.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed loading custom biome DLL: " +
                                Path.GetFileName(path) + " | " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }

                    if (anyNewLoaded)
                    {
                        RebuildCaches_NoLock();
                        _version++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CustomBiomeRegistry.Refresh failed: {ex}.");
                }
            }
        }

        /// <summary>
        /// Snapshot of discovered custom biome FULL type names.
        /// </summary>
        /// <remarks>
        /// - Calls <see cref="EnsureScanned"/> to guarantee at least one scan attempt.
        /// - Returned array is a snapshot; future Refresh calls can change internal list.
        /// </remarks>
        public static string[] GetCustomBiomeTypeNames()
        {
            EnsureScanned();
            lock (_sync)
                return _customBiomeTypeNames.ToArray();
        }

        /// <summary>
        /// Try resolve a type name from custom biome assemblies.
        /// </summary>
        /// <param name="fullTypeName">Full type name, e.g. "MyMod.Biomes.MyBiome".</param>
        /// <param name="t">Resolved type, if found.</param>
        /// <returns>True if a matching type is found; otherwise false.</returns>
        /// <remarks>
        /// Fast path: <see cref="_typeByFullName"/> cache.
        /// Slow path: Scans loaded assemblies via <see cref="Assembly.GetType(string,bool,bool)"/>.
        /// </remarks>
        public static bool TryResolveCustomBiomeType(string fullTypeName, out Type t)
        {
            t = null;
            if (string.IsNullOrWhiteSpace(fullTypeName))
                return false;

            EnsureScanned();

            lock (_sync)
            {
                if (_typeByFullName.TryGetValue(fullTypeName, out t) && t != null)
                    return true;

                // Slow path: Scan loaded assemblies for a matching type name.
                for (int i = 0; i < _loadedAssemblies.Count; i++)
                {
                    try
                    {
                        var cand = _loadedAssemblies[i].GetType(fullTypeName, throwOnError: false, ignoreCase: true);
                        if (cand != null)
                        {
                            _typeByFullName[fullTypeName] = cand;
                            t = cand;
                            return true;
                        }
                    }
                    catch { /* Ignore. */ }
                }

                return false;
            }
        }
        #endregion

        #region Private Helpers

        /// <summary>
        /// Ensures at least one scan attempt has been performed.
        /// </summary>
        /// <remarks>
        /// This uses <see cref="_scanned"/> as a quick check to avoid repeated work.
        /// Any real mutation and cache rebuild occurs under <see cref="_sync"/> inside <see cref="Refresh"/>.
        /// </remarks>
        private static void EnsureScanned()
        {
            if (_scanned) return;
            Refresh();
        }

        /// <summary>
        /// Rebuilds discoverable-biome caches from currently loaded assemblies.
        /// Caller must already hold <see cref="_sync"/>.
        /// </summary>
        /// <remarks>
        /// Discovery rules:
        /// - Type must be non-abstract.
        /// - Must derive from <see cref="Biome"/>.
        /// - Must have a ctor(WorldInfo) to match the game's biome construction path.
        /// </remarks>
        private static void RebuildCaches_NoLock()
        {
            _customBiomeTypeNames.Clear();

            // Keep existing map (could include older entries), but refresh discovered list.
            for (int a = 0; a < _loadedAssemblies.Count; a++)
            {
                Type[] types;
                try
                {
                    types = _loadedAssemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    types = (rtle.Types ?? new Type[0]).Where(x => x != null).ToArray();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    if (t.IsAbstract) continue;
                    if (!typeof(Biome).IsAssignableFrom(t)) continue;

                    // Must match the game biome ctor signature you use everywhere:
                    // new SomeBiome(WorldInfo worldInfo).
                    if (t.GetConstructor(new[] { typeof(WorldInfo) }) == null)
                        continue;

                    string fn = t.FullName;
                    if (string.IsNullOrWhiteSpace(fn)) continue;

                    _typeByFullName[fn] = t;

                    // Avoid duplicates by case-insensitive match.
                    bool exists = _customBiomeTypeNames.Any(s => string.Equals(s, fn, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        _customBiomeTypeNames.Add(fn);
                }
            }

            _customBiomeTypeNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        #endregion
    }
}
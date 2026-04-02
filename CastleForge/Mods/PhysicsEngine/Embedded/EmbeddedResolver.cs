/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace PhysicsEngine
{
    /// <summary>
    /// Unified resolver for embedded DLLs:
    ///   • Native DLLs are extracted to disk and preloaded with LoadLibrary so P/Invoke can bind.
    ///   • Managed DLLs are loaded directly from memory via AppDomain.AssemblyResolve.
    ///
    /// Notes:
    ///   - We classify "managed vs native" by reading PE headers (no filename conventions needed).
    ///   - Native DLLs must live on disk for the OS loader; managed DLLs can be Assembly.Load(bytes).
    ///   - This is safe to call once at startup; it caches the resource list and hooks AssemblyResolve.
    /// </summary>
    internal static class EmbeddedResolver
    {
        // ---------- Win32 loader interop for native DLL preloading ----------

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        // --------------------------------------------------------------------

        private static bool     _inited;        // Guard so Init() runs once.
        private static string[] _embeddedNames; // Cached resource names for quick lookups.

        /// <summary>
        /// Initialize the resolver:
        ///   1) Preload all embedded native DLLs (extract to disk + LoadLibrary).
        ///   2) Hook AssemblyResolve to load embedded managed DLLs from memory on demand.
        /// </summary>
        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            var asm = Assembly.GetExecutingAssembly();

            // Cache all resource names once; avoids listing per resolve call.
            _embeddedNames = asm.GetManifestResourceNames();

            // Step 1: Make sure any native payloads are available to the OS loader
            //         (P/Invoke from managed libraries often needs these ready).
            PreloadNativeDlls(asm, _embeddedNames);

            // Step 2: If the CLR can't find a managed dependency, try loading it
            //         straight from our embedded resource bytes.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedFromResources;
        }

        /// <summary>
        /// Locate all embedded *.dll resources, classify them by PE header,
        /// and for native DLLs:
        ///   • Extract them to a known folder under the game directory.
        ///   • (Optionally) add that folder to DLL search path.
        ///   • LoadLibrary() them immediately to pin the exact file.
        ///
        /// Managed DLL resources are skipped here; they're handled by AssemblyResolve.
        /// </summary>
        private static void PreloadNativeDlls(Assembly asm, string[] resources)
        {
            try
            {
                // Where native DLLs will be written. Keeping these together simplifies dependency resolution.
                string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(EmbeddedResolver).Namespace, "Natives");

                // Create/use this only if we actually find a native to extract.
                bool prepared = false;
                void EnsureNativeBase()
                {
                    if (prepared) return;
                    Directory.CreateDirectory(baseDir);
                    try { SetDllDirectory(baseDir); } catch { /* Ok on older OS. */ }
                    prepared = true;
                }

                // Let the Win32 loader also probe this directory for transitive native deps.
                try { SetDllDirectory(baseDir); } catch { /* Some older OS versions may not support this; safe to ignore. */ }

                // Consider only embedded resources that look like DLL files.
                var dllResources = resources.Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();

                foreach (var resName in dllResources)
                {
                    using (var s = asm.GetManifestResourceStream(resName))
                    {
                        if (s == null) continue; // Shouldn't happen, but be defensive.

                        // Read bytes once (so we can classify without writing to disk).
                        byte[] bytes;
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            bytes = ms.ToArray();
                        }

                        // If it contains a CLR header, it's managed-skip here; AssemblyResolve will handle it.
                        if (PeInspector.IsManagedPE(bytes))
                            continue;

                        // We have a native payload: Prepare the target directory/search path now.
                        EnsureNativeBase();

                        // Native payload: We must write to disk for LoadLibrary to map it.
                        string fileName = DeriveFileName(resName); // e.g., "Mod.libs.zlib1.dll" -> "zlib1.dll".
                        string outPath  = Path.Combine(baseDir, fileName);

                        // Avoid rewriting every launch; check length (cheap proxy for identity).
                        bool needWrite = !File.Exists(outPath) || new FileInfo(outPath).Length != bytes.Length;
                        if (needWrite)
                        {
                            try
                            {
                                // Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                                File.WriteAllBytes(outPath, bytes);
                            }
                            catch (Exception ioEx)
                            {
                                TryLog($"[Resolver] Failed to write native '{outPath}': {ioEx.Message}.");
                                continue; // Can't load if we couldn't write.
                            }
                        }

                        // Preload the exact native we just extracted. This pins the loader to our copy
                        // even if another version is on PATH or beside the game EXE.
                        IntPtr h = LoadLibrary(outPath);
                        if (h == IntPtr.Zero)
                        {
                            int err = Marshal.GetLastWin32Error();
                            TryLog($"[Resolver] LoadLibrary failed ({err}) for '{outPath}'");
                            // Typical errors:
                            //   193 = bad exe format (wrong bitness).
                            //   126 = module not found (missing dependent DLL).
                            //   127 = procedure not found (ABI mismatch).
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TryLog($"[Resolver] PreloadNativeDlls error: {ex}");
            }
        }

        /// <summary>
        /// AssemblyResolve handler:
        ///   • If the CLR asks for "Foo, Version=...", we try to find an embedded resource ending with "Foo.dll".
        ///   • If found, we load the bytes with Assembly.Load (managed-only).
        ///   • If already loaded, we return the existing Assembly instance to avoid duplicates.
        /// </summary>
        private static Assembly ResolveManagedFromResources(object sender, ResolveEventArgs args)
        {
            try
            {
                // If something already loaded the exact full-name, reuse it (prevents duplicate loads of the same assembly identity).
                var already = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
                if (already != null) return already;

                var asm = Assembly.GetExecutingAssembly();
                string simpleName = new AssemblyName(args.Name).Name + ".dll";

                // Match by filename suffix so your resource can live under any namespace.
                var resourceName = _embeddedNames?.FirstOrDefault(r =>
                    r.EndsWith(simpleName, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return null; // Not one of our embedded dependencies; let normal resolution continue.

                using (var s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s == null) return null;

                    // Copy bytes then classify; only load managed here.
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        var bytes = ms.ToArray();

                        if (!PeInspector.IsManagedPE(bytes))
                            return null; // If it's native, we bail (native must be preloaded on disk).

                        return Assembly.Load(bytes);
                    }
                }
            }
            catch (Exception ex)
            {
                TryLog($"[Resolver] Failed resolving {args.Name}: {ex}");
                return null; // Returning null tells the CLR to keep searching with its default rules.
            }
        }

        /// <summary>
        /// Convert a resource name to its final filename by taking the token before ".dll".
        /// Examples:
        ///   "My.Assets.libs.zlib1.dll"  -> "zlib1.dll".
        ///   "Pack.Native.libfoo.v2.dll" -> "libfoo.v2.dll".
        /// </summary>
        private static string DeriveFileName(string resourceName)
        {
            int dllIdx = resourceName.LastIndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            if (dllIdx < 0) return "unknown.dll";

            // Trim to before ".dll", then take everything after the last dot.
            string before = resourceName.Substring(0, dllIdx);
            int lastDot   = before.LastIndexOf('.');
            string baseNm = (lastDot >= 0) ? before.Substring(lastDot + 1) : before;
            return baseNm + ".dll";
        }

        /// <summary>
        /// Safe logger that prefers your in-game chat log; falls back to Debug if unavailable.
        /// </summary>
        private static void TryLog(string s)
        {
            try { ModLoader.LogSystem.Log(s); }
            catch { Debug.WriteLine(s); }
        }
    }

    /// <summary>
    /// Minimal PE inspector:
    /// Returns true iff the image contains a CLR (COM) data directory, which indicates a managed assembly.
    ///
    /// Why not rely on naming?
    ///   - Some native DLLs don't use a ".native.dll" suffix.
    ///   - Some managed helpers might be renamed oddly.
    ///   - Reading headers is cheap and removes the guesswork.
    /// </summary>
    internal static class PeInspector
    {
        public static bool IsManagedPE(byte[] pe)
        {
            try
            {
                if (pe == null || pe.Length < 0x100) return false;

                // DOS header magic: 'M','Z'.
                if (pe[0] != (byte)'M' || pe[1] != (byte)'Z') return false;

                // e_lfanew -> offset to NT headers ("PE\0\0").
                int peOff = BitConverter.ToInt32(pe, 0x3C);
                if (peOff <= 0 || peOff + 0x18 >= pe.Length) return false;

                // NT header signature: "PE\0\0".
                if (pe[peOff] != (byte)'P' || pe[peOff + 1] != (byte)'E' || pe[peOff + 2] != 0 || pe[peOff + 3] != 0)
                    return false;

                // Optional header magic distinguishes PE32 vs PE32+.
                int optOff = peOff + 4 + 20; // 4-byte sig + 20-byte COFF header.
                if (optOff + 2 > pe.Length) return false;

                ushort magic = BitConverter.ToUInt16(pe, optOff);
                bool isPE32  = (magic == 0x10B);
                bool isPE32p = (magic == 0x20B);
                if (!isPE32 && !isPE32p) return false;

                // Data directories start at +0x60 (PE32) or +0x70 (PE32+) from Optional header start.
                int dataDirOff = optOff + (isPE32 ? 0x60 : 0x70);

                // Directory index 14 = CLR/COM descriptor. Non-zero => managed assembly.
                int clrDirOff  = dataDirOff + 14 * 8; // Each directory is (RVA + Size) = 8 bytes.
                if (clrDirOff + 8 > pe.Length) return false;

                uint clrRva  = BitConverter.ToUInt32(pe, clrDirOff + 0);
                uint clrSize = BitConverter.ToUInt32(pe, clrDirOff + 4);

                return clrRva != 0 && clrSize != 0;
            }
            catch
            {
                // On any parsing error, err on the side of "native" so we don't attempt Assembly.Load on it.
                return false;
            }
        }
    }
}
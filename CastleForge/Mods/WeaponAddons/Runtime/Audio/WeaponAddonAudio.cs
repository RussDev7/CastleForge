/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Audio;
using System.Collections.Generic;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace WeaponAddons
{
    /// <summary>
    /// Selects how a WeaponAddons sound list is used.
    /// Summary: Single preserves vanilla-style one-cue behavior and is the default.
    /// </summary>
    internal enum WeaponAddonSoundMode
    {
        Single,
        Random,
        Cycle
    }

    /// <summary>
    /// WeaponAddonAudio
    /// ----------------
    /// File-backed and cue-list SFX router for WeaponAddons.
    ///
    /// Summary:
    /// - Supports .clag SFX values as either:
    ///   • Vanilla cue name,
    ///   • Pack-relative file path (.wav/.mp3) loaded into SoundEffect, OR
    ///   • A Random/Cycle list containing cue names and/or file paths.
    /// - For file-based or multi-choice sounds:
    ///   • Registers per-item SHOOT/RELOAD routes.
    ///   • Stores per-item Volume/Pitch tuning for file-backed choices.
    ///   • Uses token strings (WASFX_SHOOT:id / WASFX_RELOAD:id) that the SoundManager patches intercept.
    ///
    /// Notes:
    /// - Uses a path cache so multiple weapons can share the same sound file.
    /// - Tracks active SoundEffectInstances and disposes them when finished.
    /// </summary>
    internal static class WeaponAddonAudio
    {
        // =====================================================================================
        // TOKEN FORMAT (STORED INTO GUN CLASS STRINGS)
        // =====================================================================================
        //
        // Summary:
        // - These prefixes are written into gun _useSoundCue / _reloadSound when a file-backed
        //   sound or Random/Cycle route is needed.
        // - SoundManager.PlayInstance(...) patches detect these and play the resolved choice.
        //
        // Example:
        //   "WASFX_SHOOT:204"  -> select/play shoot sound route for itemId=204
        //   "WASFX_RELOAD:204" -> select/play reload sound route for itemId=204
        //
        // =====================================================================================

        // Tokens stored into gun _useSoundCue / _reloadSound.
        private const string ShootPrefix  = "WASFX_SHOOT:";
        private const string ReloadPrefix = "WASFX_RELOAD:";

        // =====================================================================================
        // CACHES (PATH -> SFX, ITEM -> ROUTE, ACTIVE INSTANCES)
        // =====================================================================================

        // Path cache so multiple items can share the same on-disk file.
        private static readonly Dictionary<string, SoundEffect> _pathCache =
            new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);

        // ItemId -> route containing one or more cue/file choices.
        private static readonly Dictionary<int, SoundRoute> _shootRoutes  = new Dictionary<int, SoundRoute>();
        private static readonly Dictionary<int, SoundRoute> _reloadRoutes = new Dictionary<int, SoundRoute>();

        // 3D/2D token-created instances need disposal after they finish.
        private static readonly List<SoundEffectInstance> _active3D = new List<SoundEffectInstance>();

        private static readonly Random _random = new Random();
        private static readonly object _randomLock = new object();

        /// <summary>
        /// Per-item tuning for file-based SFX.
        /// Volume: 0..1
        /// Pitch : -1..1  (XNA pitch range)
        /// </summary>
        private struct SfxParams
        {
            public float Volume; // 0..1.
            public float Pitch;  // -1..1.
        }

        /// <summary>
        /// One selectable sound choice. Exactly one of CueName/Sfx is expected.
        /// </summary>
        private sealed class SoundChoice
        {
            public string CueName;
            public SoundEffect Sfx;
        }

        /// <summary>
        /// Per-item sound route used by Single file-backed sounds and Random/Cycle lists.
        /// </summary>
        private sealed class SoundRoute
        {
            public WeaponAddonSoundMode Mode;
            public SfxParams Params;
            public readonly List<SoundChoice> Choices = new List<SoundChoice>();
            public int NextIndex;
        }

        // =====================================================================================
        // LIFECYCLE
        // =====================================================================================
        //
        // Summary:
        // - Reset(): called on pack reload / hot reload / shutdown.
        //   Disposes active instances + cached SoundEffects and clears routing tables.
        // - Update(): called periodically (e.g., from a SoundManager.Update postfix) to prune stopped instances.
        //
        // =====================================================================================

        public static void Reset()
        {
            try
            {
                for (int i = _active3D.Count - 1; i >= 0; i--)
                {
                    try { _active3D[i]?.Stop(); } catch { }
                    try { _active3D[i]?.Dispose(); } catch { }
                }
                _active3D.Clear();
            }
            catch { }

            try
            {
                foreach (var kv in _pathCache)
                    try { kv.Value?.Dispose(); } catch { }
                _pathCache.Clear();
            }
            catch { }

            _shootRoutes.Clear();
            _reloadRoutes.Clear();
        }

        /// <summary>
        /// Soft reset: Clear routing tables so we can rebuild mappings,
        /// but DO NOT dispose cached SoundEffects (prevents cutting live audio).
        /// </summary>
        public static void SoftResetRouting()
        {
            _shootRoutes.Clear();
            _reloadRoutes.Clear();

            // Intentionally keep:
            // - _pathCache (SoundEffects)
            // - _active3D (currently playing instances)
        }

        // =====================================================================================
        // INPUT CLASSIFICATION
        // =====================================================================================
        //
        // Summary:
        // - IsFileSpec(): heuristic to decide whether a .clag value looks like a file path or a cue name.
        //   (If false, caller should treat it as a vanilla cue string.)
        //
        // =====================================================================================

        public static bool IsFileSpec(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return s.IndexOf('\\') >= 0 || s.IndexOf('/') >= 0 ||
                   s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================================
        // REGISTRATION / FIELD VALUE PREPARATION
        // =====================================================================================
        //
        // Summary:
        // - TryPrepareShootSound/TryPrepareReloadSound:
        //   • Single mode returns a direct cue name when possible (vanilla behavior).
        //   • File-backed sounds and Random/Cycle lists are registered as token routes.
        //   • Lists may mix vanilla cue names and pack-relative .wav/.mp3 paths.
        // - TryRegisterShoot/TryRegisterReload are kept as compatibility wrappers.
        //
        // =====================================================================================

        /// <summary>
        /// Prepares the value that should be written into the gun's _useSoundCue field.
        /// Summary: Single is default/preserved behavior; Random/Cycle use the supplied list.
        /// </summary>
        public static string TryPrepareShootSound(
            int itemId,
            string packRoot,
            string primary,
            string[] values,
            WeaponAddonSoundMode mode,
            float vol,
            float pitch)
            => TryPrepareSound(itemId, packRoot, primary, values, mode, isShoot: true, vol, pitch);

        /// <summary>
        /// Prepares the value that should be written into the gun's _reloadSound field.
        /// Summary: Mirrors TryPrepareShootSound for reload sounds.
        /// </summary>
        public static string TryPrepareReloadSound(
            int itemId,
            string packRoot,
            string primary,
            string[] values,
            WeaponAddonSoundMode mode,
            float vol,
            float pitch)
            => TryPrepareSound(itemId, packRoot, primary, values, mode, isShoot: false, vol, pitch);

        // Returns token string (WASFX_SHOOT:id) if a file-backed route is registered; cue names pass through.
        public static string TryRegisterShoot(int itemId, string packRoot, string value, float vol, float pitch)
            => TryPrepareShootSound(itemId, packRoot, value, null, WeaponAddonSoundMode.Single, vol, pitch);

        public static string TryRegisterReload(int itemId, string packRoot, string value, float vol, float pitch)
            => TryPrepareReloadSound(itemId, packRoot, value, null, WeaponAddonSoundMode.Single, vol, pitch);

        private static float Clamp01(float v) => (v < 0f) ? 0f : (v > 1f ? 1f : v);
        private static float ClampPitch(float v) => (v < -1f) ? -1f : (v > 1f ? 1f : v);

        private static string TryPrepareSound(
            int itemId,
            string packRoot,
            string primary,
            string[] values,
            WeaponAddonSoundMode mode,
            bool isShoot,
            float vol,
            float pitch)
        {
            var list = BuildChoiceList(primary, values, mode);

            if (list.Count == 0)
                return null;

            // Single cue names stay direct so vanilla behavior remains the default.
            if (mode == WeaponAddonSoundMode.Single)
            {
                var value = list[0];

                if (!IsFileSpec(value))
                    return value;

                return TryRegisterRoute(itemId, packRoot, list, WeaponAddonSoundMode.Single, isShoot, vol, pitch);
            }

            return TryRegisterRoute(itemId, packRoot, list, mode, isShoot, vol, pitch);
        }

        /// <summary>
        /// Builds the effective choice list for the requested mode.
        /// Summary:
        /// - Single uses primary first, then the first list entry as fallback.
        /// - Random/Cycle use the list when present, otherwise primary as fallback.
        /// </summary>
        private static List<string> BuildChoiceList(string primary, string[] values, WeaponAddonSoundMode mode)
        {
            var result = new List<string>();

            if (mode == WeaponAddonSoundMode.Single)
            {
                AddClean(result, primary);

                if (result.Count == 0 && values != null)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        AddClean(result, values[i]);
                        if (result.Count > 0)
                            break;
                    }
                }

                return result;
            }

            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                    AddClean(result, values[i]);
            }

            if (result.Count == 0)
                AddClean(result, primary);

            return result;
        }

        private static void AddClean(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value))
                return;

            value = value.Trim().Trim('"', '\'');
            if (value.Length > 0)
                list.Add(value);
        }

        private static string TryRegisterRoute(
            int itemId,
            string packRoot,
            List<string> values,
            WeaponAddonSoundMode mode,
            bool isShoot,
            float vol,
            float pitch)
        {
            if (values == null || values.Count == 0)
                return null;

            var route = new SoundRoute
            {
                Mode = mode,
                Params = new SfxParams { Volume = Clamp01(vol), Pitch = ClampPitch(pitch) }
            };

            for (int i = 0; i < values.Count; i++)
                TryAddChoice(route, packRoot, values[i]);

            if (route.Choices.Count == 0)
                return null;

            var routes = isShoot ? _shootRoutes : _reloadRoutes;
            routes[itemId] = route;

            if (route.Choices.Count > 1 || mode != WeaponAddonSoundMode.Single)
                Log($"[WAddns] Registered {route.Choices.Count} {(isShoot ? "shoot" : "reload")} sound option(s) for item {itemId} ({mode}).");

            return (isShoot ? ShootPrefix : ReloadPrefix) + itemId.ToString();
        }

        private static void TryAddChoice(SoundRoute route, string packRoot, string value)
        {
            if (route == null || string.IsNullOrWhiteSpace(value))
                return;

            value = value.Trim();

            if (!IsFileSpec(value))
            {
                route.Choices.Add(new SoundChoice { CueName = value });
                return;
            }

            var full = ResolvePackPath(packRoot, value);
            if (full == null || !File.Exists(full))
            {
                Log($"[WAddns] Missing SFX file: {value}.");
                return;
            }

            if (!TryLoadByPath(full, out var sfx) || sfx == null)
                return;

            route.Choices.Add(new SoundChoice { Sfx = sfx });
        }

        // =====================================================================================
        // PATH RESOLUTION (PACK-RELATIVE, TRAVERSAL-SAFE)
        // =====================================================================================

        private static string ResolvePackPath(string packRoot, string rel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packRoot)) return null;
                if (string.IsNullOrWhiteSpace(rel)) return null;

                string root = Path.GetFullPath(packRoot.TrimEnd('\\', '/')) + Path.DirectorySeparatorChar;

                string cleaned = rel.Replace('/', '\\').TrimStart('\\');
                string combined = Path.GetFullPath(Path.Combine(root, cleaned));

                // Prevent traversal outside the pack folder.
                if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return null;

                return combined;
            }
            catch { return null; }
        }

        // =====================================================================================
        // LOADING (DISK -> SOUNDEFFECT)
        // =====================================================================================
        //
        // Summary:
        // - TryLoadByPath:
        //   • Uses path cache to avoid re-decoding.
        //   • WAV: decoded via WavPcmDecoder (handles extra chunks like bext/LIST/JUNK).
        //   • MP3: decoded via Mp3Decoder (NLayer -> PCM -> SoundEffect).
        //
        // =====================================================================================

        private static bool TryLoadByPath(string path, out SoundEffect sfx)
        {
            if (_pathCache.TryGetValue(path, out sfx) && sfx != null && !sfx.IsDisposed)
                return true;

            try
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    sfx = WavPcmDecoder.DecodeToSoundEffect(path);
                    if (sfx == null) return false;
                }
                else
                {
                    // Same MP3 decode strategy as TexturePackManager (NLayer -> PCM -> SoundEffect).
                    sfx = Mp3Decoder.DecodeToSoundEffect(path);
                }

                if (sfx == null) return false;

                _pathCache[path] = sfx;
                Log($"[WAddns] Loaded SFX: {Path.GetFileName(path)}.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[WAddns] Failed SFX {Path.GetFileName(path)}: {ex.Message}.");
                try { sfx?.Dispose(); } catch { }
                sfx = null;
                return false;
            }
        }

        // =====================================================================================
        // PLAYBACK (TOKEN -> CUE OR EFFECT INSTANCE)
        // =====================================================================================
        //
        // Summary:
        // - TryPlay2D / TryPlay3D:
        //   • Parse token, locate route for item id, select based on Single/Random/Cycle, and play.
        //   • Cue choices are passed back into SoundManager as normal cue names.
        //   • File choices create SoundEffectInstances tracked in _active3D and disposed later.
        //
        // =====================================================================================

        public static bool TryPlay2D(string name)
        {
            if (!TryResolveToken(name, out var isShoot, out var id)) return false;
            if (!TryGetRoute(isShoot, id, out var route)) return false;

            var choice = Choose(route);
            if (choice == null) return false;

            if (!string.IsNullOrWhiteSpace(choice.CueName))
            {
                try
                {
                    DNA.Audio.SoundManager.Instance?.PlayInstance(choice.CueName);
                    return true;
                }
                catch { return false; }
            }

            return TryPlaySfx2D(choice.Sfx, route.Params);
        }

        public static bool TryPlay3D(string name, AudioEmitter emitter)
        {
            if (!TryResolveToken(name, out var isShoot, out var id)) return false;
            if (!TryGetRoute(isShoot, id, out var route)) return false;

            var choice = Choose(route);
            if (choice == null) return false;

            if (!string.IsNullOrWhiteSpace(choice.CueName))
            {
                try
                {
                    DNA.Audio.SoundManager.Instance?.PlayInstance(choice.CueName, emitter);
                    return true;
                }
                catch { return false; }
            }

            return TryPlaySfx3D(choice.Sfx, route.Params, emitter);
        }

        private static bool TryGetRoute(bool isShoot, int id, out SoundRoute route)
        {
            var routes = isShoot ? _shootRoutes : _reloadRoutes;
            return routes.TryGetValue(id, out route) && route != null && route.Choices.Count > 0;
        }

        private static SoundChoice Choose(SoundRoute route)
        {
            if (route == null || route.Choices.Count == 0)
                return null;

            if (route.Mode == WeaponAddonSoundMode.Random)
            {
                lock (_randomLock)
                    return route.Choices[_random.Next(route.Choices.Count)];
            }

            if (route.Mode == WeaponAddonSoundMode.Cycle)
            {
                int index = route.NextIndex % route.Choices.Count;
                route.NextIndex++;
                if (route.NextIndex < 0)
                    route.NextIndex = 0;

                return route.Choices[index];
            }

            return route.Choices[0];
        }

        private static bool TryPlaySfx2D(SoundEffect sfx, SfxParams p)
        {
            if (sfx == null || sfx.IsDisposed)
                return false;

            try
            {
                var inst = sfx.CreateInstance();
                inst.IsLooped = false;
                inst.Volume   = Clamp01(p.Volume <= 0f ? 1f : p.Volume);
                inst.Pitch    = ClampPitch(p.Pitch);
                inst.Play();

                _active3D.Add(inst);
                return true;
            }
            catch { return false; }
        }

        private static bool TryPlaySfx3D(SoundEffect sfx, SfxParams p, AudioEmitter emitter)
        {
            if (sfx == null || sfx.IsDisposed)
                return false;

            try
            {
                var inst      = sfx.CreateInstance();
                inst.IsLooped = false;
                inst.Volume   = Clamp01(p.Volume <= 0f ? 1f : p.Volume);
                inst.Pitch    = ClampPitch(p.Pitch);

                inst.Apply3D(DNA.Audio.SoundManager.ActiveListener, emitter);
                inst.Play();

                _active3D.Add(inst);
                return true;
            }
            catch { return false; }
        }

        // =====================================================================================
        // HOUSEKEEPING (INSTANCE PRUNING)
        // =====================================================================================

        public static void Update()
        {
            for (int i = _active3D.Count - 1; i >= 0; i--)
            {
                var inst = _active3D[i];
                if (inst == null || inst.State == SoundState.Stopped)
                {
                    try { inst?.Dispose(); } catch { }
                    _active3D.RemoveAt(i);
                }
            }
        }

        // =====================================================================================
        // TOKEN PARSING (NAME -> [SHOOT/RELOAD], ITEM ID)
        // =====================================================================================

        private static bool TryResolveToken(string name, out bool isShoot, out int id)
        {
            isShoot = false;
            id = 0;

            if (string.IsNullOrWhiteSpace(name)) return false;

            if (name.StartsWith(ShootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isShoot = true;
                return int.TryParse(name.Substring(ShootPrefix.Length), out id);
            }
            if (name.StartsWith(ReloadPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isShoot = false;
                return int.TryParse(name.Substring(ReloadPrefix.Length), out id);
            }

            return false;
        }

        // =====================================================================================
        // MP3 DECODER (NLayer -> PCM -> SoundEffect)
        // =====================================================================================

        internal static class Mp3Decoder
        {
            public static SoundEffect DecodeToSoundEffect(string mp3Path)
            {
                try
                {
                    using (var mp3 = new NLayer.MpegFile(mp3Path))
                    {
                        int sampleRate  = Math.Max(8000, Math.Min(48000, mp3.SampleRate));
                        int channels    = (mp3.Channels >= 2) ? 2 : 1;
                        const int CHUNK = 32 * 1024;

                        var floatBuf = new float[CHUNK * channels];
                        using (var pcm = new MemoryStream(1 << 20))
                        using (var bw  = new BinaryWriter(pcm))
                        {
                            int read;
                            while ((read = mp3.ReadSamples(floatBuf, 0, floatBuf.Length)) > 0)
                            {
                                read -= read % channels;
                                for (int i = 0; i < read; i++)
                                {
                                    float f = floatBuf[i];
                                    if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                                    short s = (short)(f * 32767f);
                                    bw.Write(s);
                                }
                            }

                            var bytes       = pcm.ToArray();
                            var xnaChannels = (AudioChannels)channels;
                            return new SoundEffect(bytes, sampleRate, xnaChannels);
                        }
                    }
                }
                catch { return null; }
            }
        }

        // =====================================================================================
        // WAV DECODER (PCM 16-bit, CHUNK-ORDERING SAFE)
        // =====================================================================================

        internal static class WavPcmDecoder
        {
            /// <summary>
            /// Decode a PCM 16-bit WAV (any chunk ordering) into an XNA SoundEffect.
            /// Supports extra chunks like bext/LIST/JUNK before data.
            /// </summary>
            public static SoundEffect DecodeToSoundEffect(string wavPath)
            {
                try
                {
                    var bytes = File.ReadAllBytes(wavPath);
                    if (bytes.Length < 44) return null;

                    // RIFF header.
                    if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F')
                        return null;
                    if (bytes[8] != (byte)'W' || bytes[9] != (byte)'A' || bytes[10] != (byte)'V' || bytes[11] != (byte)'E')
                        return null;

                    int fmtOffset = -1, fmtSize = 0;
                    int dataOffset = -1, dataSize = 0;

                    int pos = 12;
                    while (pos + 8 <= bytes.Length)
                    {
                        int id0 = bytes[pos + 0], id1 = bytes[pos + 1], id2 = bytes[pos + 2], id3 = bytes[pos + 3];
                        int size = BitConverter.ToInt32(bytes, pos + 4);
                        pos += 8;

                        if (size < 0 || pos + size > bytes.Length) break;

                        // "fmt ".
                        if (id0 == 'f' && id1 == 'm' && id2 == 't' && id3 == ' ')
                        {
                            fmtOffset = pos;
                            fmtSize = size;
                        }
                        // "data".
                        else if (id0 == 'd' && id1 == 'a' && id2 == 't' && id3 == 'a')
                        {
                            dataOffset = pos;
                            dataSize = size;
                        }

                        pos += size;

                        // Chunks are padded to even size.
                        if ((size & 1) == 1) pos++;

                        if (fmtOffset >= 0 && dataOffset >= 0)
                            break;
                    }

                    if (fmtOffset < 0 || dataOffset < 0) return null;
                    if (fmtSize < 16)  return null;
                    if (dataSize <= 0) return null;

                    // Parse PCM fmt (first 16 bytes).
                    ushort audioFormat   = BitConverter.ToUInt16(bytes, fmtOffset + 0);
                    ushort channels      = BitConverter.ToUInt16(bytes, fmtOffset + 2);
                    int    sampleRate    = BitConverter.ToInt32(bytes, fmtOffset + 4);
                    ushort bitsPerSample = BitConverter.ToUInt16(bytes, fmtOffset + 14);

                    // PCM 16-bit only (you can extend if needed).
                    if (audioFormat != 1) return null;
                    if (bitsPerSample != 16) return null;
                    if (channels < 1) return null;

                    int ch = (channels >= 2) ? 2 : 1; // Clamp mono/stereo.
                    var xnaChannels = (AudioChannels)ch;

                    // Copy PCM data bytes.
                    var pcm = new byte[dataSize];
                    Buffer.BlockCopy(bytes, dataOffset, pcm, 0, dataSize);

                    // Create sound directly from PCM bytes (bypasses WAV parsing issues).
                    return new SoundEffect(pcm, sampleRate, xnaChannels);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
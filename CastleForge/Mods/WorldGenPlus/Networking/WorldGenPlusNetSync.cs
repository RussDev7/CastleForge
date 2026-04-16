/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.IO.Compression;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Text;
using HarmonyLib;
using System.IO;
using System;

namespace WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus multiplayer settings sync (vanilla-safe carrier injection).
    /// Summary:
    /// - Host: During WorldInfoMessage.SendData, temporarily appends a tagged, base64url(deflate(binary)) payload
    ///   onto a vanilla-serialized <see cref="WorldInfo"/> string field (currently <see cref="WorldInfo.ServerPassword"/>),
    ///   then restores the original value in a Harmony Finalizer so the injected suffix never persists in host memory.
    /// - Client: After vanilla WorldInfoMessage.RecieveData completes, detects the tag (if present), decodes the
    ///   payload into <see cref="WorldGenPlusSettings"/>, applies it as an in-memory network override via WGConfig,
    ///   and strips the injected suffix from the received <see cref="WorldInfo"/> so UI/save state remains clean.
    /// - Session end: On CastleMinerZGame.EndGame, clears any active network override so local INI-backed settings
    ///   resume outside the multiplayer session.
    /// </summary>
    /// <remarks>
    /// Notes (IMPORTANT):
    /// - This implementation intentionally avoids writing extra bytes to the network stream; it piggybacks on an existing
    ///   vanilla string that is already part of the message payload. This keeps CMZ message framing/checksums intact.
    /// - Join safety: All operations are best-effort with conservative size/alloc caps and exception swallowing so a bad or
    ///   missing blob never blocks connect/join.
    /// - Compatibility:
    ///   - Vanilla client -> modded host:  Safe (vanilla simply receives a longer string it already reads).
    ///   - Modded client  -> vanilla host: No tag present, so nothing is applied.
    /// - Integrity: The host-side carrier is ALWAYS restored via Finalizer, even if SendData throws.
    /// </remarks>
    internal static class WorldGenPlusNetSync
    {
        #region Wire / Payload Constants (Caps + Reserved Ids)

        // Safety caps (avoid insane allocations if someone sends garbage).
        private const int MaxCompressedBytes   = 512 * 1024;        // 512 KB (decode-time cap).
        private const int MaxDecompressedBytes = 2   * 1024 * 1024; // 2 MB   (decode-time cap).

        // Marker inside an existing serialized string field. Use something unlikely to appear naturally.
        private const string InjectTag = "\u200BWGP1:"; // Zero-width + tag.

        #region Reserved: Future-Proofing

        // Reserved: "WGP1" as uint (ASCII). Not currently written into the blob, but kept for future-proofing.
        // Little-endian in stream is fine as long as read/write match.
        private const uint Magic = 0x31504757;

        // Reserved: Wire version (separate from PayloadVersion). Kept for future-proofing.
        private const byte NetVersion = 1;

        // Reserved: intended target size for on-the-wire payload bytes (not enforced directly here).
        // (We cap by base64url string length instead, which is what actually rides inside the carrier string.)
        private const int MaxTrailerBytesOnWire = 768;

        #endregion

        #endregion

        #region Reflection Cache (Deterministic Accessors)

        // Cache the WorldInfoMessage.WorldInfo field once (no guessing).
        private static FieldInfo _worldInfoMsgWorldInfoField;

        /// <summary>
        /// Returns the <see cref="WorldInfo"/> carried by the concrete WorldInfoMessage instance.
        /// Summary: Deterministic; the game type contains a field literally named "WorldInfo".
        /// </summary>
        private static WorldInfo GetWorldInfoFromMessage(object msg)
        {
            if (msg == null) return null;

            if (_worldInfoMsgWorldInfoField == null)
            {
                var t = msg.GetType();
                _worldInfoMsgWorldInfoField = AccessTools.Field(t, "WorldInfo");
            }

            return _worldInfoMsgWorldInfoField != null
                ? (WorldInfo)_worldInfoMsgWorldInfoField.GetValue(msg)
                : null;
        }

        /// <summary>
        /// Returns the carrier string we inject into.
        /// Summary: Deterministic carrier; serialized by <see cref="WorldInfo.Save(BinaryWriter)"/> as:
        /// writer.Write(this.ServerPassword);
        /// </summary>
        private static string GetCarrier(WorldInfo wi) => wi?.ServerPassword;

        /// <summary>
        /// Sets the carrier string we inject into.
        /// Summary: Kept as a helper so the injection/restore sites are obvious.
        /// </summary>
        private static void SetCarrier(WorldInfo wi, string value)
        {
            if (wi != null) wi.ServerPassword = value ?? string.Empty;
        }
        #endregion

        #region Public Entrypoint (Manual Patch Install)

        /// <summary>
        /// Applies the net-sync patches using the provided Harmony instance.
        /// Summary:
        /// - Patches WorldInfoMessage.SendData(BinaryWriter) with Prefix + Finalizer (host inject + always-restore).
        /// - Patches WorldInfoMessage.RecieveData(BinaryReader) with Postfix (client parse + apply + strip).
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This type does NOT use [HarmonyPatch] attributes, so it must be invoked explicitly by your patch bootstrap.
        /// - Unpatch is automatic if you later call _harmony.UnpatchAll(yourHarmonyId) (same owner id).
        /// - Method spelling in this codebase is commonly "RecieveData".
        /// </remarks>
        public static int ApplyPatches(Harmony h)
        {
            if (h == null) return 0;

            int patched = 0;

            // WorldInfoMessage is in the game assembly (often internal); patch via name.
            var t = AccessTools.TypeByName("DNA.CastleMinerZ.Net.WorldInfoMessage");
            if (t == null) return 0;

            patched    += PatchIfExists(h, t, "SendData",    new[] { typeof(BinaryWriter) }, prefixName:  nameof(WorldInfoMessage_SendData_Prefix), finalizerName: nameof(WorldInfoMessage_SendData_Finalizer));
            patched    += PatchIfExists(h, t, "RecieveData", new[] { typeof(BinaryReader) }, postfixName: nameof(WorldInfoMessage_ReceiveData_Postfix));
            // patched += PatchIfExists(h, t, "ReceiveData", new[] { typeof(BinaryReader) }, postfixName: nameof(WorldInfoMessage_ReceiveData_Postfix));

            return patched;
        }

        /// <summary>
        /// Helper: Patches a method if it exists (best-effort).
        /// Summary: Avoids hard failures across different builds/versions.
        /// </summary>
        private static int PatchIfExists(
            Harmony h,
            Type    t,
            string  name,
            Type[]  args,
            string  prefixName    = null,
            string  postfixName   = null,
            string  finalizerName = null)
        {
            var m = AccessTools.Method(t, name, args);
            if (m == null) return 0;

            HarmonyMethod pre = null, post = null, fin = null;

            if (!string.IsNullOrEmpty(prefixName))
                pre  = new HarmonyMethod(typeof(WorldGenPlusNetSync), prefixName);

            if (!string.IsNullOrEmpty(postfixName))
                post = new HarmonyMethod(typeof(WorldGenPlusNetSync), postfixName);

            if (!string.IsNullOrEmpty(finalizerName))
                fin  = new HarmonyMethod(typeof(WorldGenPlusNetSync), finalizerName);

            h.Patch(m, prefix: pre, postfix: post, finalizer: fin);
            return 1;
        }
        #endregion

        #region Harmony Hooks (Inject / Restore / Receive / EndGame)

        /// <summary>
        /// Host-side: Before vanilla writes, temporarily append our settings blob into the carrier string.
        /// Summary:
        /// - Writes: [originalCarrier][InjectTag][base64url(deflate(binarySettings))].
        /// - Restoration is handled by <see cref="WorldInfoMessage_SendData_Finalizer"/>.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Must be conservative: If anything looks off, we do nothing (vanilla-safe).
        /// - The tag is appended to a string that vanilla already serializes (so no stream seeking required).
        /// </remarks>
        private static void WorldInfoMessage_SendData_Prefix(object __instance, ref string __state)
        {
            __state = null;

            try
            {
                var game    = CastleMinerZGame.Instance;
                bool isHost = game?.MyNetworkGamer?.IsHost ?? false;
                if (!isHost) return;

                // Respect your config toggle: Host must explicitly allow broadcast.
                WGConfig.Load();
                if (!WGConfig.Current.Multiplayer_BroadcastToClients) return;

                var wi = GetWorldInfoFromMessage(__instance);
                if (wi == null) return;

                var settings = WGConfig.Snapshot();
                if (settings == null) return;

                // Only broadcast meaningful data (optional).
                if (!settings.Enabled) return;

                // Build compressed payload.
                byte[] payload = BuildCompressedPayload(settings);
                if (payload == null || payload.Length == 0) return;

                string b64 = ToBase64Url(payload);

                // Hard cap to avoid packet bloat.
                // NOTE: This is "characters in the string", not bytes.
                if (b64.Length > 1400) return;

                string current = GetCarrier(wi) ?? string.Empty;

                // If something left a tag in there (shouldn't, but belt & suspenders), strip it first.
                int idx = current.IndexOf(InjectTag, StringComparison.Ordinal);
                if (idx >= 0)
                    current = current.Substring(0, idx);

                __state = GetCarrier(wi); // Restore exact original later (even if it contained a tag).
                SetCarrier(wi, current + InjectTag + b64);
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>
        /// Finalizer runs even if SendData throws, so we always restore the original carrier string.
        /// Summary: Ensures we never persist the injected suffix into in-memory world list / save state.
        /// </summary>
        private static Exception WorldInfoMessage_SendData_Finalizer(
            object     __instance,
            Exception  __exception,
            ref string __state)
        {
            if (__state != null)
            {
                try
                {
                    var wi = GetWorldInfoFromMessage(__instance);
                    if (wi != null)
                        SetCarrier(wi, __state);
                }
                catch
                {
                    // Never throw from finalizer.
                }
            }

            return __exception; // Preserve vanilla behavior.
        }

        /// <summary>
        /// Client-side: After vanilla reads, parse our appended blob (if present) from the carrier string,
        /// apply it as a network override, then restore the carrier back to the original (strip the injected suffix).
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Client-only:  Host never consumes its own injected blob.
        /// - Best-effort:  All exceptions are swallowed to avoid breaking join.
        /// - Sanitization: Always strips the suffix if a tag was present (finally block).
        /// </remarks>
        private static void WorldInfoMessage_ReceiveData_Postfix(object __instance)
        {
            WorldInfo wi       = null;
            string    original = null;
            bool      hadTag   = false;

            try
            {
                var game    = CastleMinerZGame.Instance;
                bool isHost = game?.MyNetworkGamer?.IsHost ?? false;
                if (isHost) return; // Client-only.

                // Load user config (do not create files here if you don't want to).
                WGConfig.Load();

                if (!WGConfig.Current.Multiplayer_SyncFromHost) return;

                wi = GetWorldInfoFromMessage(__instance);
                if (wi == null) return;

                string s = GetCarrier(wi);
                if (string.IsNullOrEmpty(s)) return;

                int idx = s.IndexOf(InjectTag, StringComparison.Ordinal);
                if (idx < 0) return;

                hadTag = true;
                original = s.Substring(0, idx);

                string b64 = s.Substring(idx + InjectTag.Length);
                if (string.IsNullOrEmpty(b64)) return;

                // Optional sanity cap (protects against weird/hostile data).
                if (b64.Length > 4096) return;

                byte[] comp = FromBase64Url(b64);
                if (comp == null || comp.Length == 0) return;

                var settings = ReadCompressedPayload(comp);
                if (settings == null) return;

                WGConfig.ApplyNetworkOverride(settings);
            }
            catch
            {
                // Swallow; never break join.
            }
            finally
            {
                // Always strip the injected suffix so the client's WorldInfo is clean.
                if (hadTag && wi != null && original != null)
                {
                    try { SetCarrier(wi, original); } catch { }
                }
            }
        }

        /// <summary>
        /// Clears any WorldGenPlus network override when a game session ends.
        /// Summary: Ensures a client doesn't keep the host's settings after disconnecting.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "EndGame")]
        internal static class CastleMinerZGame_EndGame_ClearWgpOverride
        {
            private static void Prefix()
            {
                try
                {
                    WGConfig.ClearNetworkOverride();
                }
                catch
                {
                    // Never block EndGame.
                }
            }
        }
        #endregion

        #region Base64Url Helpers

        /// <summary>
        /// Converts bytes to a URL-safe base64 string (no padding).
        /// Summary: Friendly for embedding inside serialized strings.
        /// </summary>
        private static string ToBase64Url(byte[] bytes)
        {
            string b64 = Convert.ToBase64String(bytes);
            b64 = b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return b64;
        }

        /// <summary>
        /// Parses a URL-safe base64 string (optional padding) into bytes.
        /// Summary: Returns null on invalid input.
        /// </summary>
        private static byte[] FromBase64Url(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            string b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            try { return Convert.FromBase64String(b64); }
            catch { return null; }
        }
        #endregion

        #region Payload (Compress + Serialize)

        /// <summary>
        /// Serializes + compresses settings.
        /// Summary: Deflate keeps join payloads smaller (field names compress well).
        /// </summary>
        private static byte[] BuildCompressedPayload(WorldGenPlusSettings settings)
        {
            byte[] raw = SerializeSettings(settings);
            if (raw == null || raw.Length == 0) return null;

            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                    ds.Write(raw, 0, raw.Length);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Decompresses + deserializes settings.
        /// Summary: Includes hard caps to prevent allocation bombs.
        /// </summary>
        private static WorldGenPlusSettings ReadCompressedPayload(byte[] comp)
        {
            if (comp == null || comp.Length == 0) return null;

            // Decode-time cap (compressed).
            if (comp.Length > MaxCompressedBytes) return null;

            try
            {
                using (var input  = new MemoryStream(comp))
                using (var ds     = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    CopyToWithLimit(ds, output, MaxDecompressedBytes);

                    var raw = output.ToArray();
                    if (raw == null || raw.Length == 0) return null;

                    return DeserializeSettings(raw);
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Raw Serialization (Binary DTO)

        // Payload version (inside the compressed blob).
        // NOTE: This is separate from NetVersion (wire trailer version).
        private const byte PayloadVersion = 2;

        // Defensive caps (keep wire-size predictable + avoid allocation bombs).
        private const int MaxStringBytes = 8 * 1024; // 8 KB per string is plenty for type names.
        private const int MaxRingCount = 256;        // Matches config ring clamp.
        private const int MaxBiomeListCount = 512;   // Matches config bag clamp.

        /// <summary>
        /// Copies from <paramref name="src"/> to <paramref name="dst"/> while enforcing a maximum total byte count.
        /// Summary: Prevents decompression bombs from allocating huge buffers.
        /// </summary>
        private static void CopyToWithLimit(Stream src, Stream dst, int maxBytes)
        {
            byte[] buffer = new byte[8192];
            int total = 0;

            while (true)
            {
                int read = src.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                total += read;
                if (total > maxBytes)
                    throw new InvalidDataException("Decompressed payload exceeds cap.");

                dst.Write(buffer, 0, read);
            }
        }

        /// <summary>
        /// Serializes settings into a compact binary blob (compressed later).
        /// Summary: Order MUST match <see cref="DeserializeSettings"/>.
        /// </summary>
        private static byte[] SerializeSettings(WorldGenPlusSettings s)
        {
            if (s == null) return null;

            try
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                {
                    // --- Header ---
                    bw.Write(PayloadVersion);

                    #region [WorldGenPlus]

                    bw.Write(s.Enabled);

                    #endregion

                    #region [Seed]

                    bw.Write(s.SeedOverrideEnabled);
                    bw.Write(s.SeedOverride);

                    #endregion

                    #region [Surface]

                    bw.Write((int)s.SurfaceMode);
                    WriteStringUtf8(bw, s.SingleSurfaceBiome);

                    #endregion

                    #region [Rings]

                    bw.Write(s.RingPeriod);
                    bw.Write(s.MirrorRepeat);
                    bw.Write(s.TransitionWidth);

                    bw.Write(s.RegionCellSize);
                    bw.Write(s.RegionBlendWidth);
                    bw.Write(s.RegionBlendPower);

                    bw.Write(s.WorldBlendRadius);
                    bw.Write(s.RegionSmoothEdges);

                    WriteRingList(bw, s.Rings, MaxRingCount);

                    #endregion

                    #region [RingsRandom]

                    bw.Write(s.RandomRingsVaryByPeriod);
                    WriteStringList(bw, s.RandomRingBiomeChoices, MaxBiomeListCount);

                    #endregion

                    #region [RandomRegions]

                    bw.Write(s.AutoIncludeCustomBiomesForRandomRegions);
                    WriteStringList(bw, s.RandomSurfaceBiomeChoices, MaxBiomeListCount);

                    #endregion

                    #region [Overlays]

                    bw.Write(s.EnableCrashSites);
                    bw.Write(s.EnableCaves);
                    bw.Write(s.EnableOre);
                    bw.Write(s.EnableBiomeOverlayGuards);
                    bw.Write(s.EnableHellCeiling);
                    bw.Write(s.EnableHell);
                    bw.Write(s.EnableBedrock);
                    bw.Write(s.EnableOrigin);
                    bw.Write(s.EnableWater);
                    bw.Write(s.EnableTrees);

                    #endregion

                    #region [Spawners]

                    bw.Write(s.EnableCaveSpawners);
                    bw.Write(s.EnableHellBossSpawners);

                    #endregion

                    #region [Bedrock]

                    bw.Write(s.Bedrock_CoordDiv);
                    bw.Write(s.Bedrock_MinLevel);
                    bw.Write(s.Bedrock_Variance);

                    #endregion

                    #region [CrashSite]

                    bw.Write(s.Crash_WorldScale);
                    bw.Write(s.Crash_NoiseThreshold);

                    bw.Write(s.Crash_GroundPlane);
                    bw.Write(s.Crash_StartY);
                    bw.Write(s.Crash_EndYExclusive);

                    bw.Write(s.Crash_CraterDepthMul);
                    bw.Write(s.Crash_EnableMound);
                    bw.Write(s.Crash_MoundThreshold);
                    bw.Write(s.Crash_MoundHeightMul);

                    bw.Write(s.Crash_CarvePadding);
                    bw.Write(s.Crash_ProtectBloodStone);

                    bw.Write(s.Crash_EnableSlime);
                    bw.Write(s.Crash_SlimePosOffset);
                    bw.Write(s.Crash_SlimeCoarseDiv);
                    bw.Write(s.Crash_SlimeAdjustCenter);
                    bw.Write(s.Crash_SlimeAdjustDiv);
                    bw.Write(s.Crash_SlimeThresholdBase);
                    bw.Write(s.Crash_SlimeBlendToIntBlendMul);
                    bw.Write(s.Crash_SlimeThresholdBlendMul);
                    bw.Write(s.Crash_SlimeTopPadding);

                    #endregion

                    #region [Ore]

                    bw.Write(s.Ore_BlendRadius);
                    bw.Write(s.Ore_BlendMul);
                    bw.Write(s.Ore_BlendAdd);

                    bw.Write(s.Ore_MaxY);
                    bw.Write(s.Ore_BlendToIntBlendMul);

                    bw.Write(s.Ore_NoiseAdjustCenter);
                    bw.Write(s.Ore_NoiseAdjustDiv);

                    bw.Write(s.Ore_CoalCoarseDiv);
                    bw.Write(s.Ore_CoalThresholdBase);
                    bw.Write(s.Ore_CopperThresholdOffset);

                    bw.Write(s.Ore_IronOffset);
                    bw.Write(s.Ore_IronCoarseDiv);
                    bw.Write(s.Ore_IronThresholdBase);
                    bw.Write(s.Ore_GoldThresholdOffset);
                    bw.Write(s.Ore_GoldMaxY);

                    bw.Write(s.Ore_DeepPassMaxY);
                    bw.Write(s.Ore_DiamondOffset);
                    bw.Write(s.Ore_DiamondCoarseDiv);
                    bw.Write(s.Ore_LavaThresholdBase);
                    bw.Write(s.Ore_DiamondThresholdOffset);
                    bw.Write(s.Ore_DiamondMaxY);

                    bw.Write(s.Ore_LootEnabled);
                    bw.Write(s.Ore_LootOnNonRockBlocks);
                    bw.Write(s.Ore_LootSandSnowMaxY);

                    bw.Write(s.Ore_LootOffset);
                    bw.Write(s.Ore_LootCoarseDiv);
                    bw.Write(s.Ore_LootFineDiv);

                    bw.Write(s.Ore_LootSurvivalMainThreshold);
                    bw.Write(s.Ore_LootSurvivalLuckyThreshold);
                    bw.Write(s.Ore_LootSurvivalRegularThreshold);
                    bw.Write(s.Ore_LootLuckyBandMinY);
                    bw.Write(s.Ore_LootLuckyBandMaxYStart);

                    bw.Write(s.Ore_LootScavengerTargetMod);
                    bw.Write(s.Ore_LootScavengerMainThreshold);
                    bw.Write(s.Ore_LootScavengerLuckyThreshold);
                    bw.Write(s.Ore_LootScavengerLuckyExtraPerMod);
                    bw.Write(s.Ore_LootScavengerRegularThreshold);

                    #endregion

                    #region [Trees]

                    bw.Write(s.Tree_TreeScale);
                    bw.Write(s.Tree_TreeThreshold);
                    bw.Write(s.Tree_BaseTrunkHeight);
                    bw.Write(s.Tree_HeightVarMul);
                    bw.Write(s.Tree_LeafRadius);
                    bw.Write(s.Tree_LeafNoiseScale);
                    bw.Write(s.Tree_LeafCutoff);
                    bw.Write(s.Tree_GroundScanStartY);
                    bw.Write(s.Tree_GroundScanMinY);
                    bw.Write(s.Tree_MinGroundHeight);

                    #endregion

                    bw.Flush();
                    return ms.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deserializes a compact binary settings blob.
        /// Summary: Order MUST match <see cref="SerializeSettings"/>.
        /// </summary>
        private static WorldGenPlusSettings DeserializeSettings(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return null;

            try
            {
                using (var ms = new MemoryStream(raw))
                using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
                {
                    byte ver = br.ReadByte();
                    if (ver != 1 && ver != 2)
                        return null;

                    var s = new WorldGenPlusSettings();

                    #region [WorldGenPlus]

                    s.Enabled = br.ReadBoolean();

                    #endregion

                    #region [Seed]

                    s.SeedOverrideEnabled = br.ReadBoolean();
                    s.SeedOverride        = br.ReadInt32();

                    #endregion

                    #region [Surface]

                    s.SurfaceMode = (WorldGenPlusBuilder.SurfaceGenMode)br.ReadInt32();

                    if (ver >= 2)
                        s.SingleSurfaceBiome = ReadStringUtf8(br, MaxStringBytes);
                    else
                        s.SingleSurfaceBiome = "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome";

                    #endregion

                    #region [Rings]

                    s.RingPeriod      = br.ReadInt32();
                    s.MirrorRepeat    = br.ReadBoolean();
                    s.TransitionWidth = br.ReadInt32();

                    s.RegionCellSize   = br.ReadInt32();
                    s.RegionBlendWidth = br.ReadInt32();
                    s.RegionBlendPower = br.ReadSingle();

                    s.WorldBlendRadius  = br.ReadInt32();
                    s.RegionSmoothEdges = br.ReadBoolean();

                    s.Rings             = ReadRingList(br, MaxRingCount);

                    #endregion

                    #region [RingsRandom]

                    s.RandomRingsVaryByPeriod = br.ReadBoolean();
                    s.RandomRingBiomeChoices  = ReadStringList(br, MaxBiomeListCount);

                    #endregion

                    #region [RandomRegions]

                    s.AutoIncludeCustomBiomesForRandomRegions = br.ReadBoolean();
                    s.RandomSurfaceBiomeChoices               = ReadStringList(br, MaxBiomeListCount);

                    #endregion

                    #region [Overlays]

                    s.EnableCrashSites         = br.ReadBoolean();
                    s.EnableCaves              = br.ReadBoolean();
                    s.EnableOre                = br.ReadBoolean();
                    s.EnableBiomeOverlayGuards = br.ReadBoolean();
                    s.EnableHellCeiling        = br.ReadBoolean();
                    s.EnableHell               = br.ReadBoolean();
                    s.EnableBedrock            = br.ReadBoolean();
                    s.EnableOrigin             = br.ReadBoolean();
                    s.EnableWater              = br.ReadBoolean();
                    s.EnableTrees              = br.ReadBoolean();

                    #endregion

                    #region [Spawners]

                    s.EnableCaveSpawners     = br.ReadBoolean();
                    s.EnableHellBossSpawners = br.ReadBoolean();

                    #endregion

                    #region [Bedrock]

                    s.Bedrock_CoordDiv = br.ReadInt32();
                    s.Bedrock_MinLevel = br.ReadInt32();
                    s.Bedrock_Variance = br.ReadInt32();

                    #endregion

                    #region [CrashSite]

                    s.Crash_WorldScale              = br.ReadSingle();
                    s.Crash_NoiseThreshold          = br.ReadSingle();

                    s.Crash_GroundPlane             = br.ReadInt32();
                    s.Crash_StartY                  = br.ReadInt32();
                    s.Crash_EndYExclusive           = br.ReadInt32();

                    s.Crash_CraterDepthMul          = br.ReadSingle();
                    s.Crash_EnableMound             = br.ReadBoolean();
                    s.Crash_MoundThreshold          = br.ReadSingle();
                    s.Crash_MoundHeightMul          = br.ReadSingle();

                    s.Crash_CarvePadding            = br.ReadInt32();
                    s.Crash_ProtectBloodStone       = br.ReadBoolean();

                    s.Crash_EnableSlime             = br.ReadBoolean();
                    s.Crash_SlimePosOffset          = br.ReadInt32();
                    s.Crash_SlimeCoarseDiv          = br.ReadInt32();
                    s.Crash_SlimeAdjustCenter       = br.ReadInt32();
                    s.Crash_SlimeAdjustDiv          = br.ReadInt32();
                    s.Crash_SlimeThresholdBase      = br.ReadInt32();
                    s.Crash_SlimeBlendToIntBlendMul = br.ReadSingle();
                    s.Crash_SlimeThresholdBlendMul  = br.ReadSingle();
                    s.Crash_SlimeTopPadding         = br.ReadInt32();

                    #endregion

                    #region [Ore]

                    s.Ore_BlendRadius                   = br.ReadInt32();
                    s.Ore_BlendMul                      = br.ReadSingle();
                    s.Ore_BlendAdd                      = br.ReadSingle();

                    s.Ore_MaxY                          = br.ReadInt32();
                    s.Ore_BlendToIntBlendMul            = br.ReadSingle();

                    s.Ore_NoiseAdjustCenter             = br.ReadInt32();
                    s.Ore_NoiseAdjustDiv                = br.ReadInt32();

                    s.Ore_CoalCoarseDiv                 = br.ReadInt32();
                    s.Ore_CoalThresholdBase             = br.ReadInt32();
                    s.Ore_CopperThresholdOffset         = br.ReadInt32();

                    s.Ore_IronOffset                    = br.ReadInt32();
                    s.Ore_IronCoarseDiv                 = br.ReadInt32();
                    s.Ore_IronThresholdBase             = br.ReadInt32();
                    s.Ore_GoldThresholdOffset           = br.ReadInt32();
                    s.Ore_GoldMaxY                      = br.ReadInt32();

                    s.Ore_DeepPassMaxY                  = br.ReadInt32();
                    s.Ore_DiamondOffset                 = br.ReadInt32();
                    s.Ore_DiamondCoarseDiv              = br.ReadInt32();
                    s.Ore_LavaThresholdBase             = br.ReadInt32();
                    s.Ore_DiamondThresholdOffset        = br.ReadInt32();
                    s.Ore_DiamondMaxY                   = br.ReadInt32();

                    s.Ore_LootEnabled                   = br.ReadBoolean();
                    s.Ore_LootOnNonRockBlocks           = br.ReadBoolean();
                    s.Ore_LootSandSnowMaxY              = br.ReadInt32();

                    s.Ore_LootOffset                    = br.ReadInt32();
                    s.Ore_LootCoarseDiv                 = br.ReadInt32();
                    s.Ore_LootFineDiv                   = br.ReadInt32();

                    s.Ore_LootSurvivalMainThreshold     = br.ReadInt32();
                    s.Ore_LootSurvivalLuckyThreshold    = br.ReadInt32();
                    s.Ore_LootSurvivalRegularThreshold  = br.ReadInt32();
                    s.Ore_LootLuckyBandMinY             = br.ReadInt32();
                    s.Ore_LootLuckyBandMaxYStart        = br.ReadInt32();

                    s.Ore_LootScavengerTargetMod        = br.ReadInt32();
                    s.Ore_LootScavengerMainThreshold    = br.ReadInt32();
                    s.Ore_LootScavengerLuckyThreshold   = br.ReadInt32();
                    s.Ore_LootScavengerLuckyExtraPerMod = br.ReadInt32();
                    s.Ore_LootScavengerRegularThreshold = br.ReadInt32();

                    #endregion

                    #region [Trees]

                    s.Tree_TreeScale        = br.ReadSingle();
                    s.Tree_TreeThreshold    = br.ReadSingle();
                    s.Tree_BaseTrunkHeight  = br.ReadInt32();
                    s.Tree_HeightVarMul     = br.ReadSingle();
                    s.Tree_LeafRadius       = br.ReadInt32();
                    s.Tree_LeafNoiseScale   = br.ReadSingle();
                    s.Tree_LeafCutoff       = br.ReadSingle();
                    s.Tree_GroundScanStartY = br.ReadInt32();
                    s.Tree_GroundScanMinY   = br.ReadInt32();
                    s.Tree_MinGroundHeight  = br.ReadInt32();

                    #endregion

                    return s;
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Small Binary Helpers (UTF8 Strings + Lists)

        /// <summary>
        /// Writes a string as: [int byteLen][byte*] (UTF8), with -1 meaning null.
        /// Summary: Avoids BinaryWriter's 7-bit string length and lets us enforce caps on read.
        /// </summary>
        private static void WriteStringUtf8(BinaryWriter bw, string s)
        {
            if (bw == null) return;

            if (s == null)
            {
                bw.Write(-1);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(s);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        /// <summary>
        /// Reads a string written by <see cref="WriteStringUtf8"/>.
        /// Summary: Enforces a maximum byte count to prevent large allocations.
        /// </summary>
        private static string ReadStringUtf8(BinaryReader br, int maxBytes)
        {
            int len = br.ReadInt32();
            if (len < 0) return null;

            if (len > maxBytes)
                throw new InvalidDataException("String exceeds cap.");

            byte[] bytes = br.ReadBytes(len);
            if (bytes == null || bytes.Length != len)
                throw new EndOfStreamException();

            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Writes a list of strings with a capped count.
        /// Summary: Duplicates are preserved (weights).
        /// </summary>
        private static void WriteStringList(BinaryWriter bw, List<string> list, int maxCount)
        {
            if (list == null || list.Count == 0)
            {
                bw.Write(0);
                return;
            }

            int count = list.Count;
            if (count > maxCount) count = maxCount;

            bw.Write(count);
            for (int i = 0; i < count; i++)
                WriteStringUtf8(bw, list[i] ?? "");
        }

        /// <summary>
        /// Reads a list of strings with a capped count.
        /// Summary: Duplicates are preserved (weights).
        /// </summary>
        private static List<string> ReadStringList(BinaryReader br, int maxCount)
        {
            int count = br.ReadInt32();
            if (count < 0 || count > maxCount)
                throw new InvalidDataException("String list count exceeds cap.");

            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string s = ReadStringUtf8(br, MaxStringBytes) ?? "";
                list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Writes the ring list as: [int count][(int endRadius, string biomeType)*].
        /// Summary: Capped to keep trailer size bounded.
        /// </summary>
        private static void WriteRingList(BinaryWriter bw, List<RingCore> rings, int maxCount)
        {
            if (rings == null || rings.Count == 0)
            {
                bw.Write(0);
                return;
            }

            int count = rings.Count;
            if (count > maxCount) count = maxCount;

            bw.Write(count);
            for (int i = 0; i < count; i++)
            {
                bw.Write(rings[i].EndRadius);
                WriteStringUtf8(bw, rings[i].BiomeType ?? "");
            }
        }

        /// <summary>
        /// Reads the ring list written by <see cref="WriteRingList"/>.
        /// Summary: Enforces ring count + string caps.
        /// </summary>
        private static List<RingCore> ReadRingList(BinaryReader br, int maxCount)
        {
            int count = br.ReadInt32();
            if (count < 0 || count > maxCount)
                throw new InvalidDataException("Ring count exceeds cap.");

            var list = new List<RingCore>(count);
            for (int i = 0; i < count; i++)
            {
                int end = br.ReadInt32();
                string biome = ReadStringUtf8(br, MaxStringBytes) ?? "";
                list.Add(new RingCore(end, biome));
            }
            return list;
        }
        #endregion
    }
}
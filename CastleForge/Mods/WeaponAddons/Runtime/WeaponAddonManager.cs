/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using HarmonyLib;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace WeaponAddons
{
    // =========================================================================================
    // Weapon Addons (Pack Loader + Runtime Overrides)
    // =========================================================================================
    //
    // Summary:
    // - Scans !Mods\WeaponAddons\ (or !Mods\WeaponAddons\Packs\) for pack folders.
    // - Each pack provides a .clag definition file + optional .xnb model(s).
    // - Packs map onto existing InventoryItemIDs "slots" so the engine/netcode stays compatible.
    // - Applies:
    //   • Display strings (name/description) via reflection best-effort.
    //   • Gun stats (damage, recoil, fire rate, clip, etc.) when the slot is a GunInventoryItemClass.
    //   • Model overrides (optional) loaded via a pack-rooted ContentManager, cached by slot.
    //   • Optional UI icon overrides:
    //       - PNG icon (direct file load)
    //       - Rendered model icon (RenderTarget2D snapshot)
    //       - Fallback to SLOT_ID icon (handled by UI patch)
    //
    // Notes:
    // - "Best-effort" philosophy: failures are logged, gameplay should keep running.
    // - Model assets are loaded from disk as XNB (no Content pipeline integration required).
    // =========================================================================================

    #region Data Model (Parsed .clag)

    /// <summary>
    /// Parsed result of a single pack's .clag file (plus resolved slot mapping).
    ///
    /// Summary:
    /// - Pack identity + paths (folder, root, clag path).
    /// - Option B (synthetic item) configuration.
    /// - Optional icon configuration:
    ///   • PNG icon path + enable toggle
    ///   • Render-from-model settings (size, pose, offsets, zoom)
    /// - Stats/configurable fields used to apply runtime overrides to the final ItemId.
    ///
    /// Notes:
    /// - Filled once in LoadAllDefs() and then treated as immutable at runtime.
    /// </summary>
    internal sealed class WeaponAddonDef
    {
        public string  PackKey;

        public string  PackFolderName;
        public string  PackRoot;
        public string  ClagPath;

        // Option B: Runtime-injected "new item id" support.
        public bool    AsNewItem;   // True => create synthetic item id.
        public int     DesiredItemId;

        // Optional: UI icon override configuration (PNG or model render).
        public string  IconPath;
        public bool    IconEnabled;
        public bool    IconRenderModel;
        public int     IconRenderSize;
        public float   IconRenderYawDeg;
        public float   IconRenderPitchDeg;
        public float   IconRenderRollDeg;
        public float   IconRenderOffsetX;
        public float   IconRenderOffsetY;
        public float   IconRenderOffsetZ;
        public float   IconRenderZoom;

        // Display fields.
        public string  Type;
        public string  Name;
        public string  Author;
        public string  Desc1;
        public string  Desc2;

        // Resolved IDs:
        // - SlotId: Base/vanilla class to clone (and also icon fallback).
        // - ItemId: Final ID to apply to (synthetic ID if AsNewItem is true).
        public InventoryItemIDs ItemId;
        public InventoryItemIDs SlotId;

        // Weapon configuration / behavior.
        public string  AmmoId;
        public string  ModelPath; // e.g. "models\\model2".
        public string  ShootSfx;
        public string  ReloadSfx;

        public float   ShootVol;
        public float   ShootPitch;
        public float   ReloadVol;
        public float   ReloadPitch;

        public bool    Automatic;
        public float   Damage;
        public float   SelfDamage;
        public float   InaccuracyDeg;
        public float   BulletsPerSecond;
        public float   RecoilDeg;

        public int     ClipSize;
        public int     RoundsPerReload;

        public float   SecondsToReload;
        public float   BulletLifetime;
        public float   BulletSpeed;

        public string  ProjectileType;
        public Vector4 ProjectileColor01;

        public Color   ModelColor1;
        public Color   ModelColor2;

        public bool    RecipeEnabled;
        public string  RecipeType;
        public int     RecipeOutputCount;
        public string  RecipeIngredients;
        public string  RecipeInsertAfter;
        public string  RecipeInsertMode;
    }
    #endregion

    /// <summary>
    /// Central runtime manager for WeaponAddons packs.
    ///
    /// Responsibilities:
    /// - Discover packs on disk.
    /// - Parse .clag into WeaponAddonDef entries.
    /// - Resolve each pack to a target slot (InventoryItemIDs).
    /// - Optionally create synthetic ItemIDs by cloning a base slot class (Option B).
    /// - Apply stats / names / ammo / sounds to the final ItemId.
    /// - Load and cache:
    ///   • Model overrides (XNB Model)
    ///   • Icon overrides (PNG or rendered-from-model)
    ///
    /// Notes:
    /// - Model loading uses a custom ContentManager rooted at the model folder to ensure
    ///   sibling dependency resolution (textures, etc.).
    /// - Slot mapping uses "last wins" semantics (the last pack mapped to a slot overrides prior).
    /// </summary>
    internal static class WeaponAddonManager
    {
        // =====================================================================================
        // PATHS / ROOTS
        // =====================================================================================

        #region Paths / Roots

        // Pack root: Matches desired mod folder location.
        public static string RootDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "WeaponAddons");

        #endregion

        /// <summary>
        /// =====================================================================================
        /// RUNTIME STATE
        /// =====================================================================================
        ///
        /// Summary:
        /// - _bySlot:    Last-wins slot mapping (SlotId -> def).
        /// - _modelById: Final item ID -> loaded model override.
        /// - _iconById:  Final item ID -> icon texture override (PNG or rendered RT).
        /// =====================================================================================
        /// </summary>

        #region Runtime State (Slot Maps)

        private static readonly Dictionary<InventoryItemIDs, WeaponAddonDef> _bySlot =
            new Dictionary<InventoryItemIDs, WeaponAddonDef>();

        // Slot -> loaded model (geometry override).
        private static readonly Dictionary<InventoryItemIDs, Model> _modelById =
            new Dictionary<InventoryItemIDs, Model>();

        // ItemId -> loaded icon (optional PNG / render target).
        private static readonly Dictionary<InventoryItemIDs, Texture2D> _iconById =
            new Dictionary<InventoryItemIDs, Texture2D>();

        #endregion

        // =====================================================================================
        // PACK CONTENT MANAGER (XNB-FROM-FOLDER)
        // =====================================================================================

        #region Pack ContentManager (XNB-From-Folder)

        /// <summary>
        /// ContentManager that loads XNB directly from an on-disk folder instead of the normal Content root.
        ///
        /// Summary:
        /// - OpenStream redirects asset loads to:  <root>\<assetName>.xnb
        /// - Root is chosen as the folder containing the primary model XNB so dependent assets resolve beside it.
        /// </summary>
        private sealed class PackContentManager : ContentManager
        {
            private readonly string _root;
            public PackContentManager(IServiceProvider services, string root) : base(services) => _root = root;

            protected override Stream OpenStream(string assetName)
            {
                var full = Path.Combine(_root, assetName + ".xnb");
                return File.OpenRead(full);
            }
        }

        // RootFolder -> ContentManager.
        // Summary: Cache per-root ContentManager instances so dependent loads share the same cache.
        private static readonly Dictionary<string, PackContentManager> _cms =
            new Dictionary<string, PackContentManager>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a cached PackContentManager for a given folder root.
        /// Summary: Each unique folder gets its own CM so models + dependencies resolve correctly.
        /// </summary>
        private static PackContentManager GetPackCM(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;

            if (!_cms.TryGetValue(root, out var cm) || cm == null)
            {
                cm = new PackContentManager(DNA.CastleMinerZ.CastleMinerZGame.Instance.Services, root);
                _cms[root] = cm;
            }

            return cm;
        }

        /// <summary>
        /// Soft reset: Drop our CM lookup so future loads use a fresh PackContentManager,
        /// but DO NOT Unload/Dispose existing managers (prevents breaking live models).
        /// </summary>
        private static void SoftResetPackCM()
        {
            _cms.Clear();
        }

        /// <summary>
        /// Clears all cached PackContentManagers.
        /// Summary: Used before reloads to ensure stale assets are evicted and file handles are released.
        /// </summary>
        private static void ResetPackCM()
        {
            foreach (var cm in _cms.Values)
            {
                try { cm?.Unload(); } catch { }
                try { (cm as IDisposable)?.Dispose(); } catch { }
            }
            _cms.Clear();
        }

        /// <summary>
        /// Clears all cached icon textures.
        /// Summary: Disposes GPU resources so hot-reload doesn't leak.
        /// </summary>
        private static void ResetIcons()
        {
            foreach (var kv in _iconById)
            {
                try { (kv.Value as IDisposable)?.Dispose(); } catch { }
            }
            _iconById.Clear();
        }

        /// <summary>
        /// Soft reset: Drop icon lookups, but DO NOT Dispose textures (prevents breaking UI refs).
        /// </summary>
        private static void SoftResetIcons()
        {
            _iconById.Clear();
        }
        #endregion

        // =====================================================================================
        // PUBLIC QUERIES (LOOKUPS USED BY PATCHES / SWAPPERS)
        // =====================================================================================

        #region Queries (Slot Lookups)

        /// <summary>
        /// Looks up the active addon definition for a given slot.
        /// Summary: Used by other systems (e.g., entity swapper) to decide if a slot is overridden.
        /// </summary>
        public static bool TryGetAddonForSlot(InventoryItemIDs id, out WeaponAddonDef def)
            => _bySlot.TryGetValue(id, out def) && def != null;

        /// <summary>
        /// Looks up the cached model override for a given slot.
        /// Summary: Returns the loaded XNB Model, if present.
        /// </summary>
        public static bool TryGetModelForSlot(InventoryItemIDs id, out Model m)
            => _modelById.TryGetValue(id, out m) && m != null;

        /// <summary>
        /// Looks up the cached icon override for a given item id.
        /// Summary: Returns a PNG-loaded Texture2D if provided by the pack.
        /// </summary>
        public static bool TryGetIconForItem(InventoryItemIDs id, out Texture2D tex)
            => _iconById.TryGetValue(id, out tex) && tex != null;

        #endregion

        /// <summary>
        /// =====================================================================================
        /// LOAD / APPLY PIPELINE
        /// =====================================================================================
        ///
        /// Summary:
        /// - LoadApply():
        ///   1) Load config
        ///   2) Clear caches (slot map, models, icons, content managers)
        ///   3) Scan and parse packs (LoadAllDefs)
        ///   4) For each pack:
        ///        - Resolve final ItemId (synthetic if requested)
        ///        - Load model override (TryLoadModel)
        ///        - Load icon override (TryLoadIcon) OR render icon (TryRenderIconFromModel)
        ///        - Apply stat/name/ammo/sfx onto ItemId (ApplyDefToItemId)
        ///   5) Persist auto-allocated IDs to config (if needed)
        /// =====================================================================================
        /// </summary>

        #region Load / Apply Pipeline

        /// <summary>
        /// Main entry point for WeaponAddons initialization.
        ///
        /// Summary:
        /// - Reads WeaponAddonConfig.
        /// - Clears previous runtime caches.
        /// - Discovers/loads pack definitions (.clag).
        /// - Applies stat overrides to matching item slots.
        /// - Loads/caches model overrides + optional icons.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                var cfg = WeaponAddonConfig.LoadOrCreate();
                if (!cfg.Enabled)
                {
                    Log("Disabled via config.");
                    return;
                }

                // Hard reset.
                /*
                _bySlot.Clear();
                _modelById.Clear();
                ResetPackCM();
                ResetIcons();
                WeaponAddonAudio.Reset();
                */

                // Soft reset.
                _bySlot.Clear();
                SoftResetPackCM();

                var defs = LoadAllDefs(cfg);

                bool changedIds = false;

                foreach (var def in defs)
                {
                    def.PackKey = def.PackFolderName;

                    // Slot override still works the same.
                    def.ItemId = def.SlotId;

                    if (def.AsNewItem)
                    {
                        // Prefer pinned ID from .clag, else persisted config, else allocate + persist.
                        int id = def.DesiredItemId;

                        if (id <= 0 && WeaponAddonConfig.NewItemIds.TryGetValue(def.PackKey, out var saved))
                            id = saved;

                        // Register clone (or return existing class if already registered).
                        var newId = WeaponAddonItemInjector.EnsureCloneRegistered(def.SlotId, id, def.PackKey, out var cls);

                        if ((int)newId > 0)
                        {
                            def.ItemId = newId;

                            if (def.DesiredItemId <= 0 && !WeaponAddonConfig.NewItemIds.ContainsKey(def.PackKey))
                            {
                                WeaponAddonConfig.NewItemIds[def.PackKey] = (int)newId;
                                changedIds = true;
                            }
                        }
                    }

                    WeaponAddonRecipeInjector.ApplyOrUpdate(def);

                    // Load model.
                    TryLoadModel(def);

                    // Load optional icon (PNG).
                    TryLoadIcon(def);

                    // If no PNG icon was provided/loaded, optionally render one from the model.
                    TryRenderIconFromModel(def);

                    // Apply stats/model to def.ItemId (NOT def.SlotId)
                    ApplyDefToItemId(def, def.ItemId);
                }

                WeaponAddonRecipeInjector.CleanupStale(defs);

                if (changedIds)
                    WeaponAddonConfig.Save();

                Log($"Loaded {defs.Count} weapon addon(s).");
            }
            catch (Exception ex)
            {
                Log($"LoadApply failed: {ex}.");
            }
        }

        /// <summary>
        /// Scans disk for pack folders and parses .clag files into WeaponAddonDef objects.
        ///
        /// Summary:
        /// - Supports:
        ///   A) !Mods\WeaponAddons\<PackName>\...
        ///   B) !Mods\WeaponAddons\Packs\<PackName>\...
        /// - Each pack must contain exactly one *.clag (first match wins).
        /// - Resolves SlotId using:
        ///   1) SLOT_ID from .clag
        ///   2) Config [Slots] mapping
        /// - Uses "last wins" per-slot if multiple packs map to the same slot.
        /// </summary>
        private static List<WeaponAddonDef> LoadAllDefs(WeaponAddonConfig cfg)
        {
            var list = new List<WeaponAddonDef>();

            if (!Directory.Exists(RootDir))
                return list;

            // Support either:
            //  A) RootDir\PackName\...
            //  B) RootDir\Packs\PackName\...
            var packsDir = Path.Combine(RootDir, "Packs");
            var scanRoot = Directory.Exists(packsDir) ? packsDir : RootDir;

            foreach (var dir in Directory.EnumerateDirectories(scanRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                // Skip common non-pack folders.
                if (folderName.Equals("Embedded", StringComparison.OrdinalIgnoreCase)) continue;
                if (folderName.Equals("!Logs", StringComparison.OrdinalIgnoreCase)) continue;

                var clag = FirstFileOrNull(dir, "*.clag");
                if (clag == null) continue;

                var doc = ClagParser.ParseFile(clag);

                var def = new WeaponAddonDef
                {
                    PackFolderName    = folderName,
                    PackRoot          = dir,
                    ClagPath          = clag,
                    Type              = doc.Type ?? "Firearm",

                    Name              = doc.Get("NAME", folderName),
                    Author            = doc.Get("AUTHOR", ""),
                    Desc1             = doc.Get("DESCRIPTION1", ""),
                    Desc2             = doc.Get("DESCRIPTION2", ""),

                    AmmoId            = doc.Get("AMMO_ID", doc.Get("AMMO_NAME", "")),
                    ModelPath         = doc.Get("MODEL", ""),
                    ShootSfx          = doc.Get("SHOOT_SFX", ""),
                    ReloadSfx         = doc.Get("RELOAD_SFX", ""),

                    IconPath          = doc.Get("ICON", doc.Get("ICON_PNG", "")),

                    Automatic         = ClagParser.ParseBool(doc.Get("AUTOMATIC", "false"), false),

                    Damage            = ClagParser.ParseFloat(doc.Get("DAMAGE", "0"), 0),
                    SelfDamage        = ClagParser.ParseFloat(doc.Get("SELF_DAMAGE", "0"), 0),

                    InaccuracyDeg     = ClagParser.ParseFloat(doc.Get("INACCURACY_DEG", doc.Get("INACCURACY", "0")), 0),
                    BulletsPerSecond  = ClagParser.ParseFloat(doc.Get("BULLETS_PER_SECOND", "0"), 0),
                    RecoilDeg         = ClagParser.ParseFloat(doc.Get("RECOIL_DEG", doc.Get("RECOIL", "0")), 0),

                    ClipSize          = ClagParser.ParseInt(doc.Get("CLIP_SIZE", "0"), 0),
                    RoundsPerReload   = ClagParser.ParseInt(doc.Get("ROUNDS_PER_RELOAD", "0"), 0),

                    SecondsToReload   = ClagParser.ParseFloat(doc.Get("SECONDS_TO_RELOAD", "0"), 0),
                    BulletLifetime    = ClagParser.ParseFloat(doc.Get("BULLET_LIFETIME", "0"), 0),
                    BulletSpeed       = ClagParser.ParseFloat(doc.Get("BULLET_SPEED", "0"), 0),

                    ProjectileType    = doc.Get("PROJECTILE_TYPE", ""),
                    ProjectileColor01 = ClagParser.ParseRgb01(doc.Get("PROJECTILE_COLOR", "")),

                    ModelColor1       = ClagParser.ParseRgb(doc.Get("MODEL_COLOR1", ""), Color.White),
                    ModelColor2       = ClagParser.ParseRgb(doc.Get("MODEL_COLOR2", ""), Color.Gray),

                    ShootVol          = MathHelper.Clamp(ClagParser.ParseFloat(doc.Get("SHOOT_VOL", "1.0"), 1f), 0f, 1f),
                    ShootPitch        = MathHelper.Clamp(ClagParser.ParseFloat(doc.Get("SHOOT_PITCH", "0.0"), 0f), -1f, 1f),

                    ReloadVol         = MathHelper.Clamp(ClagParser.ParseFloat(doc.Get("RELOAD_VOL", "1.0"), 1f), 0f, 1f),
                    ReloadPitch       = MathHelper.Clamp(ClagParser.ParseFloat(doc.Get("RELOAD_PITCH", "0.0"), 0f), -1f, 1f)
                };

                // Resolve slot:
                // 1) .clag $SLOT_ID.
                // 2) config [Slots] PackFolderName.
                string slotText = doc.Get("SLOT_ID", null);
                if (string.IsNullOrWhiteSpace(slotText) && cfg.Slots.TryGetValue(folderName, out var mapped))
                    slotText = mapped;

                def.AsNewItem       = doc.GetBool("AS_NEW_ITEM", false) || doc.GetBool("NEW_ITEM", false);
                def.DesiredItemId   = doc.GetInt("ITEM_ID", 0);

                // Icon toggle: Default ON if ICON path is provided.
                bool iconEnabled    = doc.GetBool("ICON_ENABLED", true);
                def.IconEnabled     = iconEnabled && !string.IsNullOrWhiteSpace(def.IconPath);

                // Optional: Render icon from MODEL .xnb (only used if PNG icon is not used).
                def.IconRenderModel = doc.GetBool("ICON_RENDER_MODEL", false) || doc.GetBool("ICON_FROM_MODEL", false);

                // Default 64; clamp to sane range.
                def.IconRenderSize = doc.GetInt("ICON_RENDER_SIZE", 64);
                if (def.IconRenderSize < 16) def.IconRenderSize = 16;
                if (def.IconRenderSize > 256) def.IconRenderSize = 256;

                // Icon render pose (degrees). Defaults match the current hard-coded yaw/pitch.
                def.IconRenderYawDeg   = ClagParser.ParseFloat(doc.Get("ICON_RENDER_YAW_DEG", "-68.75"), -68.75f);
                def.IconRenderPitchDeg = ClagParser.ParseFloat(doc.Get("ICON_RENDER_PITCH_DEG", "-20.00"), -20.00f);
                def.IconRenderRollDeg  = ClagParser.ParseFloat(doc.Get("ICON_RENDER_ROLL_DEG", "0"), 0f);
                def.IconRenderOffsetX  = ClagParser.ParseFloat(doc.Get("ICON_RENDER_OFFSET_X", "0"), 0f);
                def.IconRenderOffsetY  = ClagParser.ParseFloat(doc.Get("ICON_RENDER_OFFSET_Y", "0"), 0f);
                def.IconRenderOffsetZ  = ClagParser.ParseFloat(doc.Get("ICON_RENDER_OFFSET_Z", "0"), 0f);

                def.IconRenderZoom     = ClagParser.ParseFloat(doc.Get("ICON_RENDER_ZOOM", "1.0"), 1f);
                if (def.IconRenderZoom < 0.25f) def.IconRenderZoom = 0.25f;
                if (def.IconRenderZoom > 4.0f) def.IconRenderZoom = 4.0f;

                if (!TryResolveInventoryItemId(slotText, out var slot))
                {
                    Log($"Pack '{folderName}' has no valid SLOT_ID (clag or config). Skipping.");
                    continue;
                }

                def.SlotId = slot;

                // ----------------------------------------------------------
                // Optional: Crafting recipe (per-pack).
                // ----------------------------------------------------------
                def.RecipeEnabled     = doc.GetBool("RECIPE_ENABLED", false) || doc.GetBool("CRAFT_ENABLED", false);
                def.RecipeType        = doc.Get("RECIPE_TYPE", doc.Get("RECIPE_TAB", "")); // Ex: "Pistols".
                def.RecipeOutputCount = doc.GetInt("RECIPE_OUTPUT_COUNT", 1);
                def.RecipeIngredients = doc.Get("RECIPE_INGREDIENTS", doc.Get("RECIPE_ITEMS", ""));
                def.RecipeInsertAfter = doc.Get("RECIPE_INSERT_AFTER", doc.Get("RECIPE_AFTER", ""));
                def.RecipeInsertMode  = doc.Get("RECIPE_INSERT_MODE", "AFTER");

                // Keep last-wins if duplicates map to same slot.
                _bySlot[def.SlotId] = def;
                list.Add(def);
            }

            return list;

            /// <summary>
            /// Finds the first file matching the pattern in the given folder.
            /// Summary: Used to locate the pack's .clag without requiring a fixed filename.
            /// </summary>
            string FirstFileOrNull(string dir, string pattern)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                        return f;
                }
                catch { }
                return null;
            }
        }
        #endregion

        // =====================================================================================
        // APPLY OVERRIDES (STATS / DISPLAY / AMMO / SFX)
        // =====================================================================================

        #region Apply Runtime Overrides

        /// <summary>
        /// Applies parsed .clag fields onto the game's existing InventoryItemClass instance for a specific itemId.
        ///
        /// Summary:
        /// - Locates InventoryItem.AllItems[itemId].
        /// - Updates name/description via reflection (best-effort).
        /// - If the class is a GunInventoryItemClass:
        ///   • Applies damage, fire rate, reload time, recoil, inaccuracy, clip, etc.
        ///   • Applies tracer color (used for bullets and lasers depending on weapon type).
        ///   • Applies ammo type if resolvable.
        ///   • Applies reload/shoot sound values via best-effort reflection.
        /// - Applies model colors via best-effort reflection.
        ///
        /// Notes:
        /// - itemId should be the final runtime ID (synthetic or vanilla).
        /// - Some properties/fields may be private or read-only in the target build.
        /// - Any unsupported targets are silently skipped (best-effort).
        /// </summary>
        private static void ApplyDefToItemId(WeaponAddonDef def, InventoryItemIDs itemId)
        {
            try
            {
                var allItemsField = AccessTools.Field(typeof(InventoryItem), "AllItems");
                if (!(allItemsField?.GetValue(null) is Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> all) ||
                    !all.TryGetValue(itemId, out var cls) || cls == null)
                {
                    Log($"ItemId {itemId} not found in InventoryItem.AllItems.");
                    return;
                }

                var fullDesc = (def.Desc1 ?? "");
                if (!string.IsNullOrWhiteSpace(def.Desc2))
                    fullDesc = fullDesc + "\n" + def.Desc2;

                SetStringBestEffort(cls, "Name", def.Name);
                SetStringBestEffort(cls, "Description", fullDesc);

                if (cls is GunInventoryItemClass gun)
                {
                    if (def.Damage > 0) gun.EnemyDamage = def.Damage;
                    if (def.SelfDamage >= 0) gun.ItemSelfDamagePerUse = def.SelfDamage;

                    if (def.BulletsPerSecond > 0)
                    {
                        var secondsPerShot = 1.0 / Math.Max(0.001, def.BulletsPerSecond);
                        SetTimeSpanBestEffort(gun, "_coolDownTime", TimeSpan.FromSeconds(secondsPerShot));
                    }

                    if (def.SecondsToReload > 0)
                        gun.ReloadTime = TimeSpan.FromSeconds(def.SecondsToReload);

                    gun.Automatic = def.Automatic;

                    if (def.ClipSize > 0) gun.ClipCapacity = def.ClipSize;
                    if (def.RoundsPerReload > 0) gun.RoundsPerReload = def.RoundsPerReload;

                    if (def.BulletLifetime > 0) gun.FlightTime = def.BulletLifetime;
                    if (def.BulletSpeed > 0) gun.Velocity = def.BulletSpeed;

                    if (def.InaccuracyDeg > 0)
                    {
                        gun.MinInnaccuracy = Angle.FromDegrees(def.InaccuracyDeg);
                        gun.MaxInnaccuracy = Angle.FromDegrees(def.InaccuracyDeg);
                    }

                    if (def.RecoilDeg > 0)
                        gun.Recoil = Angle.FromDegrees(def.RecoilDeg);

                    gun.TracerColor = def.ProjectileColor01;

                    if (TryResolveInventoryItemId(def.AmmoId, out var ammoId))
                        gun.AmmoType = InventoryItem.GetClass(ammoId);

                    // Reload SFX: Cue name OR file path.
                    if (!string.IsNullOrWhiteSpace(def.ReloadSfx))
                    {
                        var token = WeaponAddonAudio.TryRegisterReload((int)def.ItemId, def.PackRoot, def.ReloadSfx, def.ReloadVol, def.ReloadPitch);
                        if (!string.IsNullOrEmpty(token))
                        {
                            SetStringBestEffort(gun, "_reloadSound", token);
                        }
                        else if (!WeaponAddonAudio.IsFileSpec(def.ReloadSfx))
                        {
                            // Cue name.
                            SetStringBestEffort(gun, "_reloadSound", def.ReloadSfx);
                        }
                        // else: file spec but failed load -> leave the cloned base reload sound intact
                    }

                    // Shoot SFX: Cue name OR file path.
                    if (!string.IsNullOrWhiteSpace(def.ShootSfx))
                    {
                        var token = WeaponAddonAudio.TryRegisterShoot((int)def.ItemId, def.PackRoot, def.ShootSfx, def.ShootVol, def.ShootPitch);
                        if (!string.IsNullOrEmpty(token))
                        {
                            SetStringBestEffort(gun, "_useSoundCue", token);
                        }
                        else if (!WeaponAddonAudio.IsFileSpec(def.ShootSfx))
                        {
                            // Cue name.
                            SetStringBestEffort(gun, "_useSoundCue", def.ShootSfx);
                        }
                        // else: file spec but failed load -> leave the cloned base shoot sound intact
                    }

                    SetColorBestEffort(cls, "ToolColor", def.ModelColor1);
                    SetColorBestEffort(cls, "ToolColor2", def.ModelColor2);
                }
            }
            catch (Exception ex)
            {
                Log($"ApplyDefToItemId failed for {def?.PackFolderName}: {ex.Message}.");
            }
        }
        #endregion

        // =====================================================================================
        // LOAD MODEL OVERRIDES (XNB)
        // =====================================================================================

        #region Load .xnb Model

        /// <summary>
        /// Loads and caches an XNB Model override for the pack's slot.
        ///
        /// Summary:
        /// - Resolves ModelPath relative to PackRoot.
        /// - Ensures .xnb extension.
        /// - Roots a PackContentManager at the model folder so dependencies load beside it.
        /// - Loads the model and stores it in _modelBySlot[SlotId] if successful.
        ///
        /// Notes:
        /// - Model load failures do not prevent the pack's stats from applying.
        /// - Model cache is keyed by def.ItemId (synthetic or vanilla) so CreateEntity swapper can
        ///   resolve the correct model for the runtime item ID.
        /// </summary>
        private static void TryLoadModel(WeaponAddonDef def)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(def.ModelPath))
                    return;

                var rel = def.ModelPath.Replace('/', '\\').TrimStart('\\');
                var fullNoExt = Path.Combine(def.PackRoot, rel);

                var xnbPath = fullNoExt.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)
                    ? fullNoExt
                    : fullNoExt + ".xnb";

                if (!File.Exists(xnbPath))
                {
                    Log($"Missing model XNB: {xnbPath}.");
                    return;
                }

                var root = Path.GetDirectoryName(xnbPath);
                var asset = Path.GetFileNameWithoutExtension(xnbPath);

                var cm = GetPackCM(root);
                var model = cm?.Load<Model>(asset);

                if (model != null)
                {
                    // IMPORTANT:
                    // Cache by the *actual* item ID (synthetic ITEM_ID or vanilla slot).
                    // EntitySwapper keys off __instance.ID, so this must match the runtime class ID.
                    _modelById[def.ItemId] = model;
                }
            }
            catch (Exception ex)
            {
                Log($"Model load failed for {def?.PackFolderName}: {ex.Message}.");
            }
        }
        #endregion

        // =====================================================================================
        // REFLECTION HELPERS
        // =====================================================================================

        #region Reflection Helpers

        /// <summary>
        /// Sets a string property/field on an object using Harmony AccessTools.
        ///
        /// Summary:
        /// - Attempts:
        ///   1) Writable property by name.
        ///   2) Field by name.
        ///   3) A handful of common private backing name variants.
        ///
        /// Notes:
        /// - Designed to be tolerant across builds where names/visibilities may differ.
        /// - Exceptions are intentionally swallowed to avoid impacting gameplay.
        /// </summary>

        private static void SetStringBestEffort(object obj, string nameOrField, string value)
        {
            if (obj == null) return;
            try
            {
                var t = obj.GetType();

                // Property first.
                var p = AccessTools.Property(t, nameOrField);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(obj, value, null);
                    return;
                }

                // Field fallback.
                var f = AccessTools.Field(t, nameOrField);
                if (f != null && f.FieldType == typeof(string))
                {
                    f.SetValue(obj, value);
                    return;
                }

                // Try common private backing names.
                foreach (var alt in new[] { "_" + nameOrField, "m_" + nameOrField, "_" + nameOrField.ToLowerInvariant(), "m_" + nameOrField.ToLowerInvariant() })
                {
                    f = AccessTools.Field(t, alt);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        f.SetValue(obj, value);
                        return;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets a TimeSpan field (best-effort).
        /// Summary: Used primarily to poke cooldown/fire-rate fields that may not have public setters.
        /// </summary>
        private static void SetTimeSpanBestEffort(object obj, string fieldName, TimeSpan value)
        {
            if (obj == null) return;
            try
            {
                var f = AccessTools.Field(obj.GetType(), fieldName);
                if (f != null && f.FieldType == typeof(TimeSpan))
                    f.SetValue(obj, value);
            }
            catch { }
        }

        /// <summary>
        /// Sets a Color property/field (best-effort).
        /// Summary: Used for model/tool tint fields that may be stored as properties or fields depending on build.
        /// </summary>
        private static void SetColorBestEffort(object obj, string propOrField, Color value)
        {
            if (obj == null) return;
            try
            {
                var t = obj.GetType();
                var p = AccessTools.Property(t, propOrField);
                if (p != null && p.CanWrite && p.PropertyType == typeof(Color))
                {
                    p.SetValue(obj, value, null);
                    return;
                }
                var f = AccessTools.Field(t, propOrField);
                if (f != null && f.FieldType == typeof(Color))
                    f.SetValue(obj, value);
            }
            catch { }
        }

        /// <summary>
        /// Resolves InventoryItemIDs from either a numeric value or an enum name.
        /// Summary: Used for SLOT_ID and AMMO_ID parsing.
        /// </summary>
        private static bool TryResolveInventoryItemId(string value, out InventoryItemIDs id)
        {
            id = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            // Numeric.
            if (int.TryParse(value, out var n))
            {
                id = (InventoryItemIDs)n;
                return true;
            }

            // Enum name (case-insensitive).
            if (Enum.TryParse(value, true, out InventoryItemIDs parsed))
            {
                id = parsed;
                return true;
            }

            return false;
        }

        // =====================================================================================
        // ICON LOADING (PNG)
        // =====================================================================================

        #region Load .png Icon

        /// <summary>
        /// Loads an optional PNG icon for this item.
        ///
        /// Summary:
        /// - Reads def.IconPath relative to PackRoot.
        /// - Uses Texture2D.FromStream (no content pipeline).
        /// - Caches by def.ItemId so synthetic IDs get the correct icon.
        ///
        /// Notes:
        /// - If the file is missing, we log and fall back (render icon or vanilla icon).
        /// - On hot-reload, old cached icons are disposed to avoid leaking GPU resources.
        /// </summary>
        private static void TryLoadIcon(WeaponAddonDef def)
        {
            try
            {
                if (def == null) return;
                if (!def.IconEnabled) return;
                if (string.IsNullOrWhiteSpace(def.IconPath)) return;

                var rel = def.IconPath.Replace('/', '\\').TrimStart('\\');
                var path = Path.Combine(def.PackRoot, rel);

                if (!File.Exists(path))
                {
                    Log($"Missing icon PNG: {path}.");
                    return;
                }

                var gd = DNA.CastleMinerZ.CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null)
                    return;

                // If we're reloading the same item id, dispose the old one first.
                if (_iconById.TryGetValue(def.ItemId, out var old) && old != null)
                {
                    try { (old as IDisposable)?.Dispose(); } catch { }
                    _iconById.Remove(def.ItemId);
                }

                // Soft reload icon update: never remove old unless replacement succeeds.
                using (var fs = File.OpenRead(path))
                {
                    var texNew = Texture2D.FromStream(gd, fs);
                    if (texNew == null)
                        return; // Keep existing.

                    // Soft reload: Do NOT dispose old (might still be referenced by UI this frame).
                    _iconById[def.ItemId] = texNew;
                }
            }
            catch (Exception ex)
            {
                Log($"Icon load failed for {def?.PackFolderName}: {ex.Message}.");
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// ICON RENDERING (MODEL -> RENDERTARGET)
        /// =====================================================================================
        ///
        /// Summary:
        /// - If PNG icon isn't present, optionally render a snapshot of the MODEL into a RenderTarget2D.
        /// - Rendering is best-effort and uses BasicEffect (texture extracted from the model's effects).
        /// - Includes a transparency sanity check so we don't hide the vanilla icon if nothing drew.
        /// =====================================================================================
        /// </summary>

        #region Render Icon

        /// <summary>
        /// Optional: Render an icon from the pack MODEL .xnb.
        /// Summary:
        /// - Only runs if def.IconRenderModel is true.
        /// - Only runs if no PNG icon is cached for this item id.
        /// - If render produces a fully-transparent texture, we discard it (so we fall back to SLOT_ID icon).
        /// </summary>
        private static void TryRenderIconFromModel(WeaponAddonDef def)
        {
            try
            {
                if (def == null) return;
                if (!def.IconRenderModel) return;

                // If a PNG icon already exists, prefer it.
                if (_iconById.TryGetValue(def.ItemId, out var existing) && existing != null)
                    return;

                if (!_modelById.TryGetValue(def.ItemId, out var model) || model == null)
                    return;

                var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                var gd = game?.GraphicsDevice;
                if (gd == null)
                    return;

                var rt = RenderModelIcon(
                    gd,
                    model,
                    def.IconRenderSize,
                    def.IconRenderYawDeg,
                    def.IconRenderPitchDeg,
                    def.IconRenderRollDeg,
                    def.IconRenderOffsetX,
                    def.IconRenderOffsetY,
                    def.IconRenderOffsetZ,
                    def.IconRenderZoom);

                if (rt == null)
                    return;

                // Sanity: If nothing was actually drawn, discard so we don't hide the vanilla icon.
                if (!HasAnyNonTransparentPixel(rt))
                {
                    try { rt.Dispose(); } catch { }
                    return;
                }

                _iconById[def.ItemId] = rt;
            }
            catch (Exception ex)
            {
                Log($"Icon render failed for {def?.PackFolderName}: {ex.Message}.");
            }
        }

        /// <summary>
        /// Renders a small "icon snapshot" of a Model into a RenderTarget2D.
        /// Summary:
        /// - Sets up a temporary render target + basic camera.
        /// - Applies yaw/pitch/roll + offsets + zoom.
        /// - Draws the model with BasicEffect (best-effort).
        /// - Restores device state and returns the RT (caller owns disposal).
        /// </summary>
        private static RenderTarget2D RenderModelIcon(
            GraphicsDevice gd,
            Model          model,
            int            sizePx,
            float          yawDeg,
            float          pitchDeg,
            float          rollDeg,
            float          offX,
            float          offY,
            float          offZ,
            float          zoom)
        {
            RenderTarget2D rt = null;
            var oldBlend   = gd.BlendState;
            var oldDepth   = gd.DepthStencilState;
            var oldRaster  = gd.RasterizerState;
            var oldSampler = gd.SamplerStates[0];

            try
            {
                RenderTargetBinding[] oldTargets = gd.GetRenderTargets();

                rt = new RenderTarget2D(
                    gd,
                    sizePx,
                    sizePx,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.Depth24,
                    0,
                    RenderTargetUsage.PreserveContents);

                gd.SetRenderTarget(rt);
                gd.Clear(Color.Transparent);

                gd.BlendState        = BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.Default;
                gd.RasterizerState   = RasterizerState.CullNone;
                gd.SamplerStates[0]  = SamplerState.LinearClamp;

                // Use bone-aware bounds so the model actually lands in-frame.
                var bones = new Matrix[model.Bones.Count];
                model.CopyAbsoluteBoneTransformsTo(bones);

                var bs  = GetModelBoundsBoneAware(model, bones);
                float r = Math.Max(0.001f, bs.Radius);

                float fov  = MathHelper.ToRadians(45f);
                float dist = (r / (float)Math.Sin(fov * 0.5f)) * 0.90f;
                dist /= Math.Max(0.01f, zoom); // >1 zooms in (closer)

                var view = Matrix.CreateLookAt(new Vector3(0f, 0f, dist), Vector3.Zero, Vector3.Up);
                var proj = Matrix.CreatePerspectiveFieldOfView(fov, 1f, 0.01f, dist * 10f);

                // Preview angle.
                var rot = Matrix.CreateFromYawPitchRoll(
                    MathHelper.ToRadians(yawDeg),
                    MathHelper.ToRadians(pitchDeg),
                    MathHelper.ToRadians(rollDeg));

                // Offsets are in "radius units" so they scale with model size.
                var offset = new Vector3(offX * r, offY * r, offZ * r);

                // Apply offset AFTER rotation so it's in screen/world axes (X=right, Y=up).
                var worldBase =
                    Matrix.CreateTranslation(-bs.Center) *
                    rot *
                    Matrix.CreateTranslation(offset);

                DrawModelWithBasicEffect(gd, model, bones, view, proj, worldBase);

                // Unbind before we return.
                gd.SetRenderTarget(null);

                // Restore old targets (if any).
                if (oldTargets != null && oldTargets.Length > 0)
                    gd.SetRenderTargets(oldTargets);

                return rt;
            }
            catch
            {
                try { rt?.Dispose(); } catch { }
                return null;
            }
            finally
            {
                try { gd.BlendState        = oldBlend;   } catch { }
                try { gd.DepthStencilState = oldDepth;   } catch { }
                try { gd.RasterizerState   = oldRaster;  } catch { }
                try { gd.SamplerStates[0]  = oldSampler; } catch { }
            }
        }

        /// <summary>
        /// Computes a merged bounding sphere for the model with bone transforms applied.
        /// Summary: Used to center/fit the icon camera so the model stays in-frame.
        /// </summary>
        private static BoundingSphere GetModelBoundsBoneAware(Model model, Matrix[] bones)
        {
            bool has = false;
            BoundingSphere merged = new BoundingSphere(Vector3.Zero, 0.01f);

            foreach (var mesh in model.Meshes)
            {
                var s = mesh.BoundingSphere;
                var t = bones[mesh.ParentBone.Index];
                s = TransformSphere(s, t);

                if (!has) { merged = s; has = true; }
                else merged = BoundingSphere.CreateMerged(merged, s);
            }

            return has ? merged : new BoundingSphere(Vector3.Zero, 0.01f);
        }

        /// <summary>
        /// Transforms a bounding sphere by a matrix (center + approximate uniform scale).
        /// Summary: Keeps icon fitting stable for scaled/offset mesh bones.
        /// </summary>
        private static BoundingSphere TransformSphere(BoundingSphere s, Matrix m)
        {
            // Transform center.
            var c = Vector3.Transform(s.Center, m);

            // Approximate uniform scale as max axis length.
            float sx = new Vector3(m.M11, m.M12, m.M13).Length();
            float sy = new Vector3(m.M21, m.M22, m.M23).Length();
            float sz = new Vector3(m.M31, m.M32, m.M33).Length();
            float scale = Math.Max(sx, Math.Max(sy, sz));
            if (scale <= 0f) scale = 1f;

            return new BoundingSphere(c, s.Radius * scale);
        }

        /// <summary>
        /// Draws a Model using BasicEffect by temporarily swapping mesh-part effects.
        /// Summary:
        /// - Extracts a texture (best-effort) from each original effect.
        /// - Replaces effects with BasicEffect for consistent icon rendering.
        /// - Restores original effects after draw.
        /// </summary>
        private static void DrawModelWithBasicEffect(GraphicsDevice gd, Model model, Matrix[] bones, Matrix view, Matrix proj, Matrix worldBase)
        {
            foreach (var mesh in model.Meshes)
            {
                var parts = mesh.MeshParts;
                var originals = new Effect[parts.Count];
                var temps = new BasicEffect[parts.Count];

                try
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var mp = parts[i];
                        originals[i] = mp.Effect;

                        var tex = TryExtractAnyTexture(originals[i]);

                        var be = new BasicEffect(gd)
                        {
                            TextureEnabled = (tex != null),
                            Texture = tex,
                            LightingEnabled = true,
                            PreferPerPixelLighting = true,
                            DiffuseColor = Vector3.One,
                            Alpha = 1f,
                        };
                        be.EnableDefaultLighting();
                        be.AmbientLightColor = new Vector3(0.35f);

                        temps[i] = be;
                        mp.Effect = be;
                    }

                    var world = bones[mesh.ParentBone.Index] * worldBase;

                    foreach (var fx in mesh.Effects)
                    {
                        if (fx is BasicEffect be)
                        {
                            be.World = world;
                            be.View = view;
                            be.Projection = proj;
                        }
                    }

                    mesh.Draw();
                }
                finally
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        try { parts[i].Effect = originals[i]; } catch { }
                        try { temps[i]?.Dispose(); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Best-effort attempt to pull a Texture2D out of an effect.
        /// Summary: Supports BasicEffect.Texture and common parameter names.
        /// </summary>
        private static Texture2D TryExtractAnyTexture(Effect fx)
        {
            if (fx == null) return null;

            try
            {
                if (fx is BasicEffect be && be.Texture != null)
                    return be.Texture;
            }
            catch { }

            try
            {
                var pars = fx.Parameters;
                if (pars == null) return null;

                foreach (var name in new[] { "Texture", "DiffuseTexture", "AlbedoTexture", "DiffuseMap", "BaseTexture" })
                {
                    try
                    {
                        var p = pars[name];
                        var t = p?.GetValueTexture2D();
                        if (t != null) return t;
                    }
                    catch { }
                }

                foreach (EffectParameter p in pars)
                {
                    try
                    {
                        var t = p?.GetValueTexture2D();
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Quick "did anything render?" check for a RenderTarget2D.
        /// Summary: Samples a few pixels and returns true if any alpha is non-trivial.
        /// </summary>
        private static bool HasAnyNonTransparentPixel(RenderTarget2D rt)
        {
            try
            {
                int w = rt.Width;
                int h = rt.Height;

                // Sample a few points (center + corners-ish).
                var pts = new[]
                {
                    new Point(w / 2, h / 2),
                    new Point(w / 4, h / 2),
                    new Point(3 * w / 4, h / 2),
                    new Point(w / 2, h / 4),
                    new Point(w / 2, 3 * h / 4),
                    new Point(w / 4, h / 4),
                    new Point(3 * w / 4, h / 4),
                    new Point(w / 4, 3 * h / 4),
                    new Point(3 * w / 4, 3 * h / 4),
                };

                var one = new Color[1];

                foreach (var p in pts)
                {
                    int x = Math.Max(0, Math.Min(p.X, w - 1));
                    int y = Math.Max(0, Math.Min(p.Y, h - 1));

                    rt.GetData(0, new Rectangle(x, y, 1, 1), one, 0, 1);
                    if (one[0].A > 5) // Non-trivial alpha.
                        return true;
                }
            }
            catch { }

            return false;
        }
        #endregion

        #endregion
    }
}
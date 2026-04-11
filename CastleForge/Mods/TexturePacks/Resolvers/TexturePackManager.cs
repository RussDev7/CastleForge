/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

/*
TexturePackManager
------------------
Applies user-provided content from !Mods\TexturePacks\<Pack>\ at runtime.

Core categories:

• Blocks (terrain atlases)
    - Near atlas: BlockTerrain._diffuseAlpha
    - Far atlas:  BlockTerrain._mipMapDiffuse   (used for distance / minification).
    - Filename patterns (case-insensitive, .png):
        · tile_###.png                          (zero-based tile index; fastest path).
        · BlockName_top.png
        · BlockName_side.png
        · BlockName_bottom.png
        · BlockName_all.png
      The BlockName_face mapping is read directly from the engine:
      BlockType.GetType(BlockTypeEnum).TileIndices[(int)BlockFace.*]

• Items (inventory icons)
    - Atlases: InventoryItem._2DImages (64px), InventoryItem._2DImagesLarge (128px).
    - Filename patterns (case-insensitive, .png):
        · ####_Name.png        (e.g., 0007_Iron_Pickaxe.png)
        · Name.png             (e.g., Iron_Pickaxe.png)
        · ####.png             (e.g., 0007.png)
        · Name_EnumName.png    (e.g., Iron_Pickaxe_EnumName.png)
        · EnumName.png         (e.g., Iron_Pickaxe_EnumName.png if the enum name is used directly)

• Item model skins (held items)
    - Optional per-item model texture applied to the entity's Texture2D / Effect texture slot.
    - Primary filename patterns (case-insensitive, .png):
        · ####_Name_model.png         (e.g., 0007_Iron_Pickaxe_model.png)
        · Name_model.png              (e.g., Iron_Pickaxe_model.png)
        · Name_EnumName_model.png     (e.g., Iron_Pickaxe_EnumName_model.png)
        · EnumName_model.png          (e.g., EnumName_model.png)
    - Fallback patterns (if no *_model hit, still treated as model skin):
        · ####.png
        · Name.png
        · EnumName.png
    - Lookups are ID + friendly/enum name-based, cached per item, per active pack.

• Door model skins / geometry
    - Optional door texture and/or model replacement applied to DoorEntity model families.
    - Pack layout:
        · Models\Doors\NormalDoor.png
        · Models\Doors\IronDoor.png
        · Models\Doors\DiamondDoor.png
        · Models\Doors\TechDoor.png
        · Models\Doors\NormalDoor.xnb
        · Models\Doors\IronDoor.xnb
        · Models\Doors\DiamondDoor.xnb
        · Models\Doors\TechDoor.xnb
        · Models\Doors\NormalDoor\NormalDoor.xnb
        · Models\Doors\NormalDoor\Model.xnb
          (same per-folder pattern for IronDoor / DiamondDoor / TechDoor)
    - Notes:
        · PNG = full-sheet door texture replacement.
        · XNB = door model geometry replacement.
        · Doors are model-driven, not terrain-atlas-backed blocks.
        · Keep XNB dependencies beside the model (prefer per-folder layout).
        · Higher-resolution PNGs (for example 512x512) are allowed if the sheet keeps
          the same UV layout proportions as vanilla.
    - Export support:
        · Door sheets can be exported to:
            !Mods\TexturePacks\_Export\Models\Doors\
        · Output filenames:
            · NormalDoor.png
            · IronDoor.png
            · DiamondDoor.png
            · TechDoor.png

• Item model geometry overrides (held items, XNB Model swap)
    - Optional per-item *geometry* replacement applied by swapping the entity's Model.
    - Pack layout (recommended to avoid dependency name collisions like "texture.xnb"):
        · Models\Items\<Stem>\ <Stem>.xnb
        · Models\Items\<Stem>\ texture.xnb            (and any other dependencies)
      Example:
        · Models\Items\0051_Pistol_model\0051_Pistol_model.xnb
        · Models\Items\0051_Pistol_model\texture.xnb
    - Notes:
        · XNA models commonly load textures by asset name (e.g., "texture") via the same ContentManager
          that loaded the Model. Keeping dependencies beside the model avoids cross-item cache collisions.
        · Model scale/origin must match vanilla expectations (see notes below).

• Character / creature model skins
    - Player:  Models\Player\*.png
        · Examples: Player.png, Player_model.png, SWATMale.png
    - Enemies: Models\Enemies\*.png
        · Examples: ZOMBIE.png, ZOMBIE_0_0.png (enemy textures keyed by EnemyType / texture enums)
    - Dragons: Models\Dragons\*.png
        · Examples: FIRE.png, FOREST.png, ICE.png, LIZARD.png, SKELETON.png (DragonType enum names).
    - Baselines captured from vanilla content; mod textures are tracked and retired on pack switch.

• Character / creature model geometry overrides (XNB Model swap)
    - Optional *global* geometry replacement for player, enemies, and dragon parts.
    - Uses XNB Models loaded from pack folders (with flat + per-folder support):
        Player (stems: SWATMale, Player_model, Player):
            · Models\Player\SWATMale.xnb
            · Models\Player\Player_model.xnb
            · Models\Player\Player.xnb
            · Models\Player\Player_model\Player_model.xnb
            · Models\Player\Player\Model.xnb
        Enemies (stem: EnemyType enum name, e.g., ZOMBIE):
            · Models\Enemies\ZOMBIE.xnb
            · Models\Enemies\ZOMBIE\ZOMBIE.xnb
            · Models\Enemies\ZOMBIE\Model.xnb
        Dragons (stems: DragonBody, DragonFeet):
            · Models\Dragons\DragonBody.xnb / DragonFeet.xnb
            · Models\Dragons\DragonBody\DragonBody.xnb
            · Models\Dragons\DragonFeet\Model.xnb
    - Notes:
        · Vanilla animation tags are grafted onto replacement models where needed, so clip lookups
          (PlayClip("...")) won't explode on missing animation dictionaries.
        · Like item geometry overrides, keep dependencies beside the model (prefer per-folder) to avoid
          collisions on common dependency names (texture_0.xnb, etc.).
        · IMPORTANT (pack switch safety): live entities often cache their Model at spawn/construct time.
          Before unloading/disposing pack ContentManagers, live players/enemies/dragons must be rebound
          to current safe models (vanilla or newly-applied pack models) to avoid disposed Effect crashes.

• Screens & UI sprites
    - Screens\MenuBack.*       -> Main menu backdrop.
    - Screens\Logo.*           -> Logo sprite.
    - Screens\DialogBack.*     -> Dialog background.
    - Screens\LoadScreen.*     -> Load-screen splash (via LoadScreen patches).
    - Inventory & HUD sprites: Inventory\*.png
        · Examples (keys / base names): DamageArrow, HudGrid, Selector, CrossHair, CrossHairTick,
          StaminaBarEmpty, HealthBarEmpty, HealthBarFull, BubbleBar, SniperScope, MissileLocking,
          MissileLock, BlockUIBack, SingleGrid, Tier2Back, CraftSelector, InventoryGrid.
    - Optional removal / replacement for the menu ad textures.

• Fonts (TTF/OTF -> SpriteFont)
    - Fonts\<FieldName>.ttf / .otf
        · FieldName typically matches SpriteFont fields on CastleMinerZGame:
          _consoleFont, _largeFont, _medFont, _medLargeFont, _myriadLarge, _myriadMed,
          _myriadSmall, _nameTagFont, _smallFont, _systemFont, DebugFont, GameModeMenu, MainMenu.
    - Runtime SpriteFont generation with lineSpacing/spacing matched to vanilla.
    - Rebinds DNA UI trees (menus, HUD, dialogs) to new fonts.
    - Picker description rich-text rendering can optionally use an italic SpriteFont
      for &o / §o runs; when unavailable, italic falls back to the normal font.

• Sound packs
    - Sounds\<CueName>.wav / Sounds\<CueName>.mp3
        · One-shot SFX (2D / 3D).
        · Ambience loops (e.g., Birds, Crickets, Drips, lostSouls).
        · Music "shadow" playback synced to engine cues (e.g., Theme, Song1-Song6, SpaceTheme).
    - CueName must match the game's cue / wavebank entry names:
        Click, Error, Award, Popup, Teleport, Reload, BulletHitHuman, thunderBig, craft, dropitem,
        pickupitem, punch, punchMiss, arrow, AssaultReload, Shotgun, ShotGunReload, CreatureUnearth,
        HorrorStinger, Fireball, Iceball, DoorClose, DoorOpen, locator, Fuse, LaserGun1-LaserGun4,
        Beep, SolidTone, RPGLaunch, Alien, GrenadeArm, RocketWhoosh, LightSaber, LightSaberSwing,
        GroundCrash, ZombieDig, ChainSawIdle, ChainSawSpinning, ChainSawCutting, Birds, FootStep,
        Theme, Pick, Place, Crickets, Drips, BulletHitDirt, GunShot1-GunShot4, BulletHitSpray,
        thunderLow, Sand, leaves, dirt, Skeleton, ZombieCry, ZombieGrowl, Hit, Fall, Douse,
        DragonScream, Explosion, WingFlap, DragonFall, Freeze, Felguard, SpaceTheme, etc.
    - ReplacementAudio and MusicShadow handle presence checks, caching, and fade mirroring.

• Pack picker icon
    - Optional pack-level icon shown in the Texture Pack picker UI.
    - Filename patterns (root of the pack folder, case-insensitive):
        · icon.(png|jpg|jpeg|bmp|tga)
        · pack.(png|jpg|jpeg|bmp|tga)
        · preview.(png|jpg|jpeg|bmp|tga)
        · logo.(png|jpg|jpeg|bmp|tga)

• Pack picker metadata / description
    - Optional pack-level metadata shown in the Texture Pack picker UI.
    - File:
        · about.json (root of the pack folder)
    - Format:
        · { "pack": { "description": "Line 1\nLine 2" } }
    - Notes:
        · Description is optional; invalid / missing metadata is ignored safely.
        · Supports explicit \n line breaks (max 2 lines shown in the picker row).
        · The picker keeps the pack title on the left and renders the description in a
          right-side column separated by a vertical divider.
        · The built-in default pack entry may also provide a fallback description even
          without an about.json file.
        · Inline formatting codes are supported in descriptions using either § or &:
            - Colors: 0-9, a-f
            - Styles: l (bold), n (underline), m (strikethrough), o (italic), r (reset)
        · Italic rendering uses an italic SpriteFont when available; otherwise it falls
          back to the normal font.

• Shaders (BlockEffect override)
    - Optional terrain shader override loaded from:
        · Shaders\blockEffect.fxb
    - Startup-time: BlockTerrain ctor load is routed through ShaderOverride.LoadBlockEffectOrVanilla(...)
      so the override can be applied as the terrain system initializes.
    - Pack-switch: ShaderOverride.OnPackSwitched() swaps BlockTerrain._effect to either:
        · override FXB (Effect(GraphicsDevice, fxbBytes))
        · or vanilla Content-managed "Shaders\BlockEffect" (baseline cached; never disposed)
    - Owned Effects (created from FXB bytes) are tracked and retired safely via the GPU retire queue.

• Skys (cubemap overrides)
    - Optional cubemap overrides loaded from:
        · Skys\ClearSky_px.png / _nx / _py / _ny / _pz / _nz
        · Skys\NightSky_px.png / ...
        · Skys\SunSet_px.png   / ...
        · Skys\DawnSky_px.png  / ...
    - Partial pack support:
        · If any face is missing for a cubemap, that sky stays vanilla (baseline fallback).
    - Vanilla baselines captured once (day/night/sunset/dawn + TextureSky blend effect);
      custom TextureCubes are tracked and retired on pack switch.

• ParticleEffects (XNB export + Texture2D PNG extraction)
    - Vanilla particle assets are stored as XNBs under:
        · Content\ParticleEffects\
        · Content\HiDefContent\ParticleEffects\
        · Content\ReachContent\ParticleEffects\
    - Export support:
        · Can copy ParticleEffects\*.xnb into an extracted folder for pack authors.
        · If a ParticleEffects XNB is a Texture2D, it can also be exported as .png alongside the dump.
    - Note:
        · Many ParticleEffects entries are NOT PNG textures; some are data-driven effect assets.
          Texture extraction only applies when the XNB actually loads as Texture2D.

• Content (Texture2D PNG export via flavor-aware probing)
    - Flavor-aware Content scanning can attempt to export Texture2D XNBs as PNG:
        · Content\*.xnb
        · Content\HiDefContent\*.xnb
        · Content\ReachContent\*.xnb
    - Uses candidate key probing (exact rel, stripped, forced flavor variants) so assets can load
      whether the engine references them with or without flavor prefixes.

--------------------------------------------------------------------------------
Model authoring & extraction workflow (GLB + bone-name safety)
--------------------------------------------------------------------------------
• Model extraction dumps now support .glb (single file) with node/bone hierarchy + meshes.
    - Goal: Preserve exact bone names (e.g., "BarrelTip") so Blender->FBX->XNA round-trips
      keep required nodes and avoid runtime crashes when the game looks up Model.Bones["..."].
    - Practical benefit vs OBJ:
        · OBJ is geometry-only. It cannot represent bones/empties/armatures.
        · GLB carries a real node tree, so Blender imports these as empties/nodes automatically.

• Item model geometry override authoring (recommended workflow)
    1) Extract the vanilla model to GLB using the exporter (for editing/reference).
    2) Import GLB into Blender (nodes/bones will import as empties).
    3) Edit meshes, preserving required node names (BarrelTip, etc.) and parent relationships.
    4) Export FBX and compile back to XNB using an XNA-compatible content pipeline.
    5) Place the model + dependencies in a per-item folder in the active pack:
        · Models\Items\<Stem>\<Stem>.xnb
        · Models\Items\<Stem>\texture_0.xnb (etc.)

• Common cause of "crash on weapon switch" after re-export
    - Missing required node/bone names in the rebuilt XNB (example: BarrelTip).
    - A visual marker named "o _Bone_BarrelTip" is NOT a bone; the name must match exactly.
    - If the model's bounding radius becomes 0 (empty mesh / bad export), some view scaling logic
      can behave badly (extreme scale or failures).

• GLB export coverage (extraction folders)
    - Models\Doors\Misc\*.glb         (door models)
    - Models\Items\Misc\*.glb         (inventory item models)
    - Models\Player\Misc\Player.glb   (player model, when available)
    - Models\Enemies\Misc\*.glb       (enemy models)
    - Models\Dragon\Misc\*.glb        (dragon body/feet models)
    - Models\Extras\*\*.glb           (reflection-discovered cached models)

Notes:
- Only assets with a corresponding file in the active pack are overridden; all others stay vanilla.
- Device reset is handled centrally via epoch flags and a GPU retire queue.
- All GPU reads/writes run under a "no render targets" guard to avoid DS-less Clear exceptions.
- Item model geometry overrides:
    · Prefer per-model subfolders so dependency names like "texture.xnb" don't collide across items.
    · If a custom model appears massive or offset in-hand, the most common cause is FBX scale/origin
      mismatch vs vanilla (apply transforms + align origin/pivot before export).
    · Some UI/view models scale held items using the model bounding radius; broken bounds (0 radius or
      empty meshes) can cause extreme scaling or crashes when switching items.
- Global character/creature model geometry overrides:
    · Live entities may retain references to pack-loaded Models/Effects; on pack switch, rebind live
      players/enemies/dragons before unloading/disposing pack ContentManagers to avoid disposed Effect
      crashes (e.g., BasicEffect ObjectDisposedException).
- Pack picker metadata (about.json) is optional and non-fatal; malformed files are ignored.
- Description styling is lightweight SpriteFont run rendering, not full rich-text/HTML.
- Door model skins:
    · Doors are authored as full-sheet textures under Models\Doors\, not as Blocks\*_top/_side/_bottom.
    · Keep the vanilla packed texture layout intact when repainting or upscaling door sheets.

-------------------------------------------------------------------------------
Authoring Notes (XNB Models)
-------------------------------------------------------------------------------
- CastleMiner Z uses XNA ContentManager; XNB Models should be built with an XNA-compatible pipeline.
- If your model references a texture file named "texture.png" at build time, the pipeline may emit
  "texture_0.xnb" as a dependency. Keep that dependency next to the model XNB (prefer per-model folders).

-------------------------------------------------------------------------------
Quick Start for Pack Authors
-------------------------------------------------------------------------------
Blocks\
  tile_2.png                                    // Direct index replacement (fastest).
  Grass_top.png                                 // Uses exact face->tile mapping from the engine.
  Grass_side.png
  Grass_bottom.png
  Grass_all.png                                 // Applies to all faces used by Grass.

Items (icons)\
  0007_Iron_Pickaxe.png                         // Icon: ####_Name.png.
  Iron_Pickaxe.png                              // Icon: Name.png.
  0007.png                                      // Icon: ####.png (fallback).
  Iron_Pickaxe_EnumName.png                     // Icon: Name_EnumName.png / EnumName.png.

Models (held items, doors & characters)\
  Items\0007_Iron_Pickaxe_model.png             // Held item model skin (####_Name_model).
  Items\0051_Pistol_model\0051_Pistol_model.xnb // Held item model GEOMETRY override (per-item folder).
  Items\0051_Pistol_model\texture_0.xnb         // Model dependency (example: diffuse texture referenced by the model).
  Doors\NormalDoor.png                          // Full-sheet wood door texture.
  Doors\IronDoor.png                            // Full-sheet iron door texture.
  Doors\DiamondDoor.png                         // Full-sheet diamond door texture.
  Doors\TechDoor.png                            // Full-sheet tech door texture.
  Doors\NormalDoor.xnb                          // Optional wood door GEOMETRY override (flat).
  Doors\NormalDoor\NormalDoor.xnb               // Optional wood door GEOMETRY override (per-folder).
  Doors\NormalDoor\Model.xnb                    // Optional wood door GEOMETRY override (convenience name).
  Player\SWATMale.png                           // Full-player texture.
  Player\Player.png                             // Full-player texture.
  Player\Player_model.xnb                       // Player GEOMETRY override (flat).
  Player\Player_model\Player_model.xnb          // Player GEOMETRY override (per-folder).
  Enemies\ZOMBIE.png                            // Enemy texture keyed by enum name.
  Enemies\ZOMBIE.xnb                            // Enemy GEOMETRY override (flat).
  Enemies\ZOMBIE\ZOMBIE.xnb                     // Enemy GEOMETRY override (per-folder).
  Dragons\ICE.png                               // Dragon texture keyed by enum name.
  Dragons\DragonBody\DragonBody.xnb             // Dragon body GEOMETRY override (per-folder).
  Dragons\DragonFeet\Model.xnb                  // Dragon feet GEOMETRY override (convenience name).

Screens\
  MenuBack.png                                  // Menu backdrop.
  Logo.png                                      // Logo sprite.
  DialogBack.png                                // Dialog screen background.
  LoadScreen.png                                // Load-screen splash (via LoadScreen patches).

Fonts\
  Game__largeFont.ttf                           // Example TTF matching a SpriteFont field name.

Sounds\
  Click.wav                                     // SFX / cue replacement.
  Theme.mp3                                     // Music replacement (shadowed over engine cue).

pack.png                                        // Optional pack picker image (root of the pack folder).
                                                // Also supports: icon, preview, logo.

about.json
  { "pack": { "description": "&6Classic Pack\n&7Faithful vanilla-inspired visuals" } }
                                                // Optional picker description (max 2 lines).
                                                // Supports § or & color/style codes.

Shaders\
  Shaders\blockEffect.fxb                       // Optional terrain shader override (FXB bytes).

Skys\
  Skys\ClearSky_px.png                          // Cubemap face (+X). Requires all 6 faces to override.
  Skys\ClearSky_nx.png
  Skys\ClearSky_py.png
  Skys\ClearSky_ny.png
  Skys\ClearSky_pz.png
  Skys\ClearSky_nz.png

ParticleEffects\
  ParticleEffects\*.xnb                         // Vanilla particle assets (exported as XNB dump).
  ParticleEffects\SomeTex.png                   // If an XNB loads as Texture2D, it can also be dumped as PNG.
*/

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using SixLabors.ImageSharp.Processing;
using System.Runtime.CompilerServices;
using SharpGLTF.Geometry.VertexTypes;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Drawing.Animation;
using System.Globalization;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using SharpGLTF.Materials;
using SharpGLTF.Geometry;
using System.Collections;
using System.Reflection;
using DNA.CastleMinerZ;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SixLabors.Fonts;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using System.Text;
using HarmonyLib;
using DNA.Audio;
using DNA.Input;
using System.IO;
using System;

using static TexturePacks.TexturePackManager.SafeTextureExtractor;
using static TexturePacks.TexturePackManager.PathShortener;
using static TexturePacks.GamePatches;
using static ModLoader.LogSystem;

namespace TexturePacks
{
    /// <summary>
    /// Central manager for CastleMiner Z "Texture Packs" and related runtime asset swaps.
    /// </summary>
    /// <remarks>
    /// Responsibilities:
    /// - Resolve the active pack under !Mods\TexturePacks\<PackName>.
    /// - Apply overrides for:
    ///   • Terrain block atlases (near + mip).
    ///   • Item icon atlases (64px + 128px).
    ///   • Optional item model skins (held entities).
    ///   • Optional item model geometry overrides (held entities; XNB Model swaps).
    ///   • Optional player / enemy / dragon model skins.
    ///   • Optional player / enemy / dragon model geometry overrides (XNB Model swaps).
    ///   • Menu / logo / dialog / loading "screen" textures.
    ///   • Inventory / HUD sprites and menu ad textures.
    ///   • Fonts (TTF/OTF -> SpriteFont) and DNA UI font rebinding.
    ///   • Sound packs (2D/3D SFX, ambience loops, music shadowing).
    ///   • Optional pack picker metadata via root about.json (2-line description text,
    ///     shown in the picker UI with optional inline color/style formatting codes).
    ///   • Terrain shader override (BlockEffect): optional per-pack blockEffect.fxb + vanilla restore + safe rebind.
    ///   • Sky cubemap overrides: optional per-pack 6-face PNG cubemaps (Clear/Night/SunSet/Dawn) with vanilla fallback.
    ///   • ParticleEffects export: copy ParticleEffects/*.xnb from flavor folders (HiDef/Reach/base),
    ///     plus optional Texture2D -> .png export when the XNB is a texture.
    /// - Handle device reset safely (GPU reset gates, retire queue) for all uploads / swaps.
    /// - Rebind live entities to safe models before unloading/disposing pack ContentManagers
    ///   (prevents disposed Effect/BasicEffect crashes during Draw after a pack switch).
    /// - Provide debug/export helpers (blocks, icons, sprites, models, fonts, sounds) for pack authors.
    ///
    /// Design goals:
    /// - Keep author-facing file names simple and predictable.
    /// - Derive face/atlas mappings from the game itself (no heuristics where avoidable).
    /// - Patch both near and far atlases (and all mips) to avoid distance "pop".
    /// - Never mutate engine state unless needed; capture baselines and short-circuit re-applies.
    /// </remarks>
    internal static class TexturePackManager
    {
        #region Core Runtime (Blocks, Item Icons, Device Reset)

        #region Paths / State

        // Global paths, active pack/cache state, and block face mapping.

        /// <summary>Root folder for all texture packs.</summary>
        /// <remarks>Resolved relative to the game executable. Example: .../!Mods/TexturePacks.</remarks>
        public static string PacksRoot =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "TexturePacks");

        // Prevent redundant re-apply onto the same atlas with the same pack.
        private static Texture2D _lastAtlas;
        private static string _lastPackName;

        // Per-item model skin cache: ID+friendly-name -> Texture2D created from the active pack.
        private static readonly Dictionary<string, Texture2D> _modelSkinCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Builds a stable cache key for model skins: "0007:Iron_Pickaxe".
        /// </summary>
        private static string CacheKey(InventoryItemIDs id, string friendlyName)
        {
            var id4 = ((int)id).ToString("0000");
            return $"{id4}:{NormalizeName(friendlyName)}";
        }

        /// <summary>
        /// Normalizes pack-facing names for use in filenames / cache keys.
        /// Keeps letters/digits/underscore and converts spaces to underscores.
        /// </summary>
        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Keep letters/digits/underscore; treat spaces as underscores.
            var chars = s.Replace(' ', '_')
                         .Where(c => char.IsLetterOrDigit(c) || c == '_')
                         .ToArray();
            return new string(chars);
        }

        // Legacy tuple-style face map (kept to avoid touching existing call sites).
        // BlockName -> (enum, top, sideRep, bottom, allRep); also keep raw indices by enum.
        private static Dictionary<BlockTypeEnum, int[]> _tileIndicesByEnum; // Raw TileIndices per enum.

        /// <summary>
        /// Author-facing face map entry. Mirrors engine data but grouped by friendly name.
        /// </summary>
        private sealed class BlockFaceMap
        {
            public BlockTypeEnum Enum;
            public int? Top;           // POSY (>=0) or null
            public int? Bottom;        // NEGY (>=0) or null
            public int[] Sides;        // distinct side indices (POSX, NEGZ, NEGX, POSZ), >=0
            public int[] All;          // union of Sides + Top + Bottom
        }

        // Newer, friendlier map by block name.
        // Example: "Grass" -> { Enum=Grass, Top=2, Sides=[1], Bottom=0, All=[2,1,0] }
        private static Dictionary<string, BlockFaceMap> _faceMapByName;

        #region Models: Doors (State / Runtime Caches)

        /// <summary>
        /// Runtime state for full-sheet door model skin replacements loaded from
        /// Packs\...\Models\Doors\*.png.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - CastleMiner Z doors render from DoorEntity models, not from terrain atlas tiles.
        /// - Each supported file replaces the full packed texture sheet for one door model family:
        ///     Models\Doors\NormalDoor.png
        ///     Models\Doors\IronDoor.png
        ///     Models\Doors\DiamondDoor.png
        ///     Models\Doors\TechDoor.png
        /// - These are full-sheet replacements, not per-face block replacements.
        /// </remarks>
        private static readonly Dictionary<string, DoorEntity.ModelNameEnum> _doorSheetNameMap =
            new Dictionary<string, DoorEntity.ModelNameEnum>(StringComparer.OrdinalIgnoreCase)
            {
                ["NormalDoor"] = DoorEntity.ModelNameEnum.Wood,
                ["IronDoor"] = DoorEntity.ModelNameEnum.Iron,
                ["DiamondDoor"] = DoorEntity.ModelNameEnum.Diamond,
                ["TechDoor"] = DoorEntity.ModelNameEnum.Tech,
            };

        /// <summary>
        /// Vanilla full-sheet door textures captured from the live game models.
        /// </summary>
        private static readonly Dictionary<DoorEntity.ModelNameEnum, Texture2D> _doorSkinVanilla =
            new Dictionary<DoorEntity.ModelNameEnum, Texture2D>();

        /// <summary>
        /// Active full-sheet door overrides loaded from the selected texture pack.
        /// </summary>
        private static readonly Dictionary<DoorEntity.ModelNameEnum, Texture2D> _doorSkinOverrides =
            new Dictionary<DoorEntity.ModelNameEnum, Texture2D>();

        #endregion

        #endregion

        #region Public API (Icons & Blocks)

        // High-level entry points used by GamePatches for blocks + item icon replacement,
        // plus device reset gates and icon upload helpers.

        /// <summary>
        /// Apply the active pack to terrain atlases (near + mip).
        /// </summary>
        /// <remarks>
        /// Call after <see cref="BlockTerrain"/> has constructed its textures.
        /// Best hook: postfix on BlockTerrain(ContentManager) constructor.
        /// Optional fallback: call during SecondaryLoad.
        /// Idempotent: skips re-apply for the same atlas+pack.
        /// </remarks>
        public static void ApplyActivePackIfAny()
        {
            EnsureBaselinesCaptured();
            var nearAtlas = FindTerrainAtlasTexture();     // _diffuseAlpha.
            if (nearAtlas == null)
            {
                Log("No terrain near atlas found (BlockTerrain.Instance?).");
                return;
            }

            var mipAtlas = FindTerrainMipDiffuseTexture(); // _mipMapDiffuse (may be null on some builds).

            var cfg = TPConfig.LoadOrCreate();
            var packDir = Path.Combine(PacksRoot, cfg.ActivePack ?? "");

            var blocksDir = Path.Combine(packDir, "Blocks");
            string modelsDir = Path.Combine(packDir, "Models");
            string modelsDoorsDir = Path.Combine(modelsDir, "Doors");

            bool hasBlocksDir = Directory.Exists(blocksDir);
            bool hasDoorModelsDir = Directory.Exists(modelsDoorsDir);

            if (!Directory.Exists(blocksDir))
            {
                // Log($"Blocks dir not found: {blocksDir}");
                return;
            }

            // Avoid duplicate re-apply to same atlas+pack (based on near atlas).
            if (ReferenceEquals(nearAtlas, _lastAtlas) &&
                string.Equals(_lastPackName, cfg.ActivePack, StringComparison.OrdinalIgnoreCase))
            {
                Log("Terrain already patched for this atlas+pack; skipping.");
                return;
            }

            _lastAtlas = nearAtlas;
            _lastPackName = cfg.ActivePack;

            // Build face map from engine data so BlockName_face works exactly.
            EnsureFaceMap();

            // Cache vanilla door model skins so full-sheet door exports and runtime overrides
            // can resolve against the actual live game model textures.
            CaptureDoorVanillaBaselinesIfNeeded();

            // Derive tile sizes (engine uses 8 tiles per row).
            int nearTile = Math.Max(1, nearAtlas.Width / 8);
            int mipTile;

            // Load replacements from the active pack.
            // - Terrain-backed blocks use Blocks\tile_### and Blocks\BlockName_face.
            // - Door models use Models\Doors\NormalDoor.png, IronDoor.png, etc.
            var reps = hasBlocksDir
                ? LoadBlockTileReplacements(blocksDir, nearAtlas.GraphicsDevice)
                : new List<TileReplacement>();
            var doorReps = hasDoorModelsDir
                ? LoadDoorSheetReplacements(modelsDoorsDir, nearAtlas.GraphicsDevice)
                : new List<DoorSheetReplacement>();

            Log($"Found {reps.Count} terrain block-face replacement PNG(s) and {doorReps.Count} door model sheet replacement PNG(s).");

            // Nothing to do if the pack provided neither terrain block replacements nor door model replacements.
            if (reps.Count == 0 && doorReps.Count == 0)
                return;

            // 1) Write the near atlas.
            BlitReplacementsIntoAtlas(nearAtlas, nearTile, reps, disposeImagesAfter: false);

            // 2) Write far/mip atlas (all mips).
            if (mipAtlas != null)
            {
                mipTile = Math.Max(1, mipAtlas.Width / 8);
                BlitReplacementsIntoAtlas(mipAtlas, mipTile, reps, disposeImagesAfter: false);
            }

            // 3) Apply full-sheet door model replacements from Models\Doors\.
            ApplyDoorSheetReplacements(doorReps);

            // Now it's safe to dispose source PNGs.
            foreach (var rep in reps) rep.Dispose();

            Log("Terrain atlases (near+far) patched.");
        }

        /// <summary>
        /// Apply item icon replacements (64px and 128px atlases).
        /// </summary>
        /// <remarks>
        /// Call after item atlases exist (e.g., during SecondaryLoad).
        /// PNG file names can be either "####_Name.png" or "Name.png".
        /// </remarks>
        public static void ApplyItemIconReplacementsIfAny()
        {
            // Ensure baseline & work buffers exist
            CaptureItemsBaselineIfNeeded();
            if (_items64Work == null && _items128Work == null)
            {
                Log("Item icon work buffers not ready.");
                return;
            }

            var cfg = TPConfig.LoadOrCreate();
            var packDir = Path.Combine(PacksRoot, cfg.ActivePack ?? "");
            var itemsDir = Path.Combine(packDir, "Items");

            // Resolve atlases to compute rects/sizes
            var smallField = AccessTools.Field(typeof(InventoryItem), "_2DImages");
            var largeField = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge");
            var smallAtlas = smallField?.GetValue(null) as Texture2D;
            var largeAtlas = largeField?.GetValue(null) as Texture2D;

            if (smallAtlas == null && largeAtlas == null) return;

            var items = GetAllItemClasses();
            if (items == null || items.Count == 0) return;

            var gd = (smallAtlas ?? largeAtlas).GraphicsDevice;

            // Always start from vanilla -> guarantees missing files revert.
            RestoreItemsToVanilla();

            int applied = 0;

            if (Directory.Exists(itemsDir))
            {
                var smallRects = BuildSmallItemRects(items, smallAtlas);
                int smallCols = smallAtlas != null ? Math.Max(1, smallAtlas.Width / 64) : 1;
                int largeCols = largeAtlas != null ? Math.Max(1, largeAtlas.Width / 128) : 1;

                foreach (var file in Directory.EnumerateFiles(itemsDir, "*.png", SearchOption.TopDirectoryOnly))
                {
                    var stem = Path.GetFileNameWithoutExtension(file);
                    var (isModel, baseStem) = StripModelSuffix(stem);
                    if (isModel) continue; // Models handled elsewhere.
                    if (!ItemNameResolver.TryResolveItemID(baseStem, items, out var id)) continue;

                    if (smallAtlas != null)
                    {
                        var r = smallRects.TryGetValue(id, out var rect)
                            ? rect
                            : new Rectangle(((int)id % smallCols) * 64, ((int)id / smallCols) * 64, 64, 64);

                        using (var tex = LoadPng(gd, file))
                        {
                            if (tex != null)
                            {
                                var src = ToSizedPixelData(tex, 64, 64);
                                BlitInto(_items64Work[0], smallAtlas.Width, r, src);
                                applied++;
                            }
                        }
                    }

                    if (largeAtlas != null)
                    {
                        var r128 = RectFrom64On128(smallRects, id, smallAtlas, largeAtlas, largeCols);
                        using (var tex = LoadPng(gd, file))
                        {
                            if (tex != null)
                            {
                                var src = ToSizedPixelData(tex, 128, 128);
                                BlitInto(_items128Work[0], largeAtlas.Width, r128, src);
                                applied++;
                            }
                        }
                    }
                }
            }

            // Upload new textures once and rebind the fields.
            SwapIconAtlasesFromCpu(gd);

            Log($"Applied {applied} item icon replacement(s).");
        }

        /// <summary>
        /// CPU-side blit into a level-0 buffer (row-major).
        /// </summary>
        static void BlitInto(Color[] dstLevel0, int atlasWidth, Rectangle dstRect, Color[] src)
        {
            int w = dstRect.Width;
            int h = dstRect.Height;
            for (int y = 0; y < h; y++)
            {
                int dstRow = (dstRect.Y + y) * atlasWidth + dstRect.X;
                int srcRow = y * w;
                Array.Copy(src, srcRow, dstLevel0, dstRow, w);
            }
        }

        #region Device Reset Handlers (Subscribe Once)

        private static GraphicsDevice _gdHooked;  // Last device we subscribed on.
        private static bool _resetHandlersHooked;

        /// <summary>
        /// Subscribe to GraphicsDevice reset events exactly once per device.
        /// Call from a safe point like DNAGame.SecondaryLoad (post device creation).
        /// </summary>
        public static void AttachDeviceResetHandlers(GraphicsDevice gd)
        {
            if (gd == null) return;

            // Re-hook if the device instance changed (minimize/restore can recreate it).
            if (_resetHandlersHooked && !ReferenceEquals(_gdHooked, gd))
            {
                try
                {
                    _gdHooked.DeviceResetting -= GdOnDeviceResetting;
                    _gdHooked.DeviceReset -= GdOnDeviceReset;
                }
                catch { /* ignore */ }
                _resetHandlersHooked = false;
                _gdHooked = null;
            }

            if (_resetHandlersHooked) return;

            gd.DeviceResetting += GdOnDeviceResetting;
            gd.DeviceReset += GdOnDeviceReset;
            _gdHooked = gd;
            _resetHandlersHooked = true;
        }

        /// <summary>Event: device is about to reset.</summary>
        private static void GdOnDeviceResetting(object sender, EventArgs e)
        {
            OnDeviceResettingSafe();
        }

        /// <summary>Event: device finished resetting.</summary>
        private static void GdOnDeviceReset(object sender, EventArgs e)
        {
            OnDeviceResetSafe();
        }
        #endregion

        #region Device Reset Gates (Epoch + Queue)

        /// <summary>
        /// True while the GraphicsDevice is in the Resetting window. We avoid uploads/swaps here and queue them
        /// for the next safe Update.
        /// </summary>
        public static volatile bool _gdResetting;

        /// <summary>
        /// Monotonic counter incremented on each DeviceReset. If an upload starts with epoch=N and detects
        /// epoch changed mid-flight, it bails and defers to the next Update.
        /// </summary>
        public static volatile int _gdEpoch;

        /// <summary>
        /// When true, a pending icon atlas upload/swap should be attempted on the next Update (outside Draw).
        /// </summary>
        public static volatile bool _itemSwapQueued; // Run on next Update when safe.

        #endregion

        #region Device Reset Gates (Setters)

        /// <summary>Mark the device as resetting; avoid touching GPU resources until reset finishes.</summary>
        public static void OnDeviceResettingSafe()
        {
            _gdResetting = true; // Assignment silences warning + drives gate.
        }

        /// <summary>
        /// Device reset finished: bump epoch, clear the resetting flag, and schedule a safe retry
        /// of any queued uploads/swaps.
        /// </summary>
        public static void OnDeviceResetSafe()
        {
            System.Threading.Interlocked.Increment(ref _gdEpoch); // Assignment silences warning + is atomic.
            _gdResetting = false;
            _itemSwapQueued = true; // Let Update runner perform uploads outside Draw.
        }
        #endregion

        #region Factory: CreateLikeForField

        /// <summary>
        /// Create a new texture compatible with the target field's declared type.
        /// Preserves mip count and format; uses <see cref="RenderTarget2D"/> only if the field demands it.
        /// </summary>
        /// <param name="gd">Graphics device.</param>
        /// <param name="oldTex">Existing texture instance to mirror (size/format/levels).</param>
        /// <param name="targetField">FieldInfo for the backing field we will assign into.</param>
        /// <remarks>
        /// • Atlases do not need a depth-stencil surface; we set DepthFormat.None when creating an RT.
        /// • The field-aware branch avoids "Texture2D -> RenderTarget2D" assignment exceptions on some builds.
        /// </remarks>
        static Texture2D CreateLikeForField(GraphicsDevice gd, Texture2D oldTex, FieldInfo targetField)
        {
            bool mip = (oldTex.LevelCount > 1);
            var fmt = oldTex.Format;
            bool wantRT = typeof(RenderTarget2D).IsAssignableFrom(targetField.FieldType);

            if (wantRT)
            {
                // DepthFormat.None is fine for atlases; 1x MSAA; preserve contents for safety.
                return new RenderTarget2D(
                    gd,
                    oldTex.Width,
                    oldTex.Height,
                    mipMap: mip,
                    preferredFormat: fmt,
                    preferredDepthFormat: DepthFormat.None,
                    preferredMultiSampleCount: 1,
                    usage: RenderTargetUsage.PreserveContents
                );
            }
            // Plain Texture2D is preferred when allowed by the field type.
            return new Texture2D(gd, oldTex.Width, oldTex.Height, mip, fmt);
        }
        #endregion

        #region Public API: SwapItemAtlasesFromCpu (Reset-Safe)

        /// <summary>
        /// Upload working CPU icon mips into the live small/large atlases in a reset-safe manner.
        /// Strategy:
        ///  • If device is resetting -> set a queue flag and return.
        ///  • Try in-place <see cref="Texture2D.SetData"/> into the existing instance (no type swap).
        ///  • If that fails AND the device epoch hasn't changed, create a compatible instance, upload, swap,
        ///    and enqueue the old instance for disposal after Draw.
        ///  • If anything looks unstable (epoch changed / still resetting), re-queue for the next Update.
        /// </summary>
        /// <param name="gd">Graphics device.</param>
        /// <remarks>
        /// All GPU writes run under WithNoRenderTargets(gd, ...) to avoid colliding with RTs missing DS.
        /// </remarks>
        public static void SwapIconAtlasesFromCpu(GraphicsDevice gd)
        {
            // Guard 1: If we are in the reset window, don't touch the device; try again next tick.
            if (_gdResetting) { _itemSwapQueued = true; return; }

            // Snapshot epoch so we can detect mid-flight device resets.
            int startEpoch = _gdEpoch;

            // Resolve fields once per call.
            var smallField = AccessTools.Field(typeof(InventoryItem), "_2DImages");
            var largeField = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge");

            // ---- Local worker: Prefer in-place upload; fall back to compatible replacement if safe ----
            void UploadInPlaceOrReplace(FieldInfo field, Color[][] work)
            {
                if (field == null || work == null) return;
                if (!(field.GetValue(null) is Texture2D dst) || dst.IsDisposed) return;

                // 1) Safest path - in-place upload (no runtime type/instance change).
                if (TryUploadInPlace(gd, dst, work)) return;

                // 2) Fallback - only proceed if the device epoch is stable.
                if (startEpoch != _gdEpoch || _gdResetting) { _itemSwapQueued = true; return; }

                // Create a field-compatible instance (Texture2D vs RenderTarget2D).
                var fresh = CreateLikeForField(gd, dst, field);

                if (TryUploadInPlace(gd, fresh, work))
                {
                    // Swap into the static field; old is retired after Draw to avoid in-flight use.
                    field.SetValue(null, fresh);
                    GpuRetireQueue.Enqueue(dst);
                }
                else
                {
                    // Couldn't upload now - device might still be settling; try again on the next Update.
                    try { fresh.Dispose(); } catch { }
                    _itemSwapQueued = true;
                }
            }

            // Apply to both atlases.
            UploadInPlaceOrReplace(smallField, _items64Work);
            UploadInPlaceOrReplace(largeField, _items128Work);
        }
        #endregion

        #region Helper: TryUploadInPlace (No-RTs, All Mips)

        /// <summary>
        /// Attempts to upload all available mip levels into the existing destination texture with
        /// absolutely no render targets bound. Returns false if the upload fails for any reason.
        /// </summary>
        /// <remarks>
        /// • Uses WithNoRenderTargets to avoid DS-less Clear exceptions during window restore.
        /// • Keeps logic minimal; caller decides whether to retry or replace.
        /// </remarks>
        static bool TryUploadInPlace(GraphicsDevice gd, Texture2D dst, Color[][] work)
        {
            try
            {
                WithNoRenderTargets(gd, () =>
                {
                    int levels = Math.Min(dst.LevelCount, work.Length);
                    for (int level = 0; level < levels; level++)
                        dst.SetData(level, null, work[level], 0, work[level].Length);
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Pack Replacement: Items (Icons)

        // Vanilla icon baselines + CPU work buffers used by ApplyItemIconReplacementsIfAny.

        // Baselines (vanilla) and working copies.
        static Color[][] _items64Baseline, _items128Baseline;
        static Color[][] _items64Work, _items128Work;
        static bool _itemsBaselineCaptured;

        public static void CaptureItemsBaselineIfNeeded()
        {
            if (_itemsBaselineCaptured) return;

            var smallField = AccessTools.Field(typeof(InventoryItem), "_2DImages");
            var largeField = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge");
            var small = smallField?.GetValue(null) as Texture2D;
            var large = largeField?.GetValue(null) as Texture2D;
            if (small == null && large == null) return;

            WithNoRenderTargets((small ?? large).GraphicsDevice, () =>
            {
                if (small != null) _items64Baseline = ReadAllMips(small);
                if (large != null) _items128Baseline = ReadAllMips(large);
            });

            _itemsBaselineCaptured = true;

            // Seed work buffers from vanilla right away.
            RestoreItemsToVanilla();
        }

        static Color[][] ReadAllMips(Texture2D tex)
        {
            var levels = tex.LevelCount;
            var arr = new Color[levels][];
            for (int level = 0; level < levels; level++)
            {
                int w = Math.Max(1, tex.Width >> level);
                int h = Math.Max(1, tex.Height >> level);
                var data = new Color[w * h];
                tex.GetData(level, null, data, 0, data.Length);
                arr[level] = data;
            }
            return arr;
        }

        static void RestoreItemsToVanilla()
        {
            if (_items64Baseline != null) _items64Work = CloneMips(_items64Baseline);
            if (_items128Baseline != null) _items128Work = CloneMips(_items128Baseline);
        }

        static Color[][] CloneMips(Color[][] src)
        {
            var dst = new Color[src.Length][];
            for (int i = 0; i < src.Length; i++)
            {
                var s = src[i];
                var d = new Color[s.Length];
                Array.Copy(s, d, s.Length);
                dst[i] = d;
            }
            return dst;
        }
        #endregion

        #region Skins & Models: Global (Player / Enemies / Dragons)

        #region Model Skins: Global (PNG)

        #region Model Skins: Items (Entity Registry)

        // ID+name-based per-entity model skins. The draw-time patch reads this registry.

        /// <summary>
        /// Looks up and registers a per-item model skin from
        /// Models\Items\*_model.png for a just-created entity instance.
        /// </summary>
        /// <param name="entity">
        /// The spawned, in-hand entity (item/tool/weapon) whose mesh should be skinned.
        /// </param>
        /// <param name="id">
        /// The item's <see cref="InventoryItemIDs"/> used as part of the cache key and
        /// filename pattern (e.g., 0007_ItemName_model.png).
        /// </param>
        /// <param name="friendlyName">
        /// Friendly display/name string for the item; combined with <paramref name="id"/>
        /// to resolve and cache a pack-local skin file.
        /// </param>
        /// <remarks>
        /// Invocation:
        ///   • Called from <see cref="ItemEntitySkinner.PostfixShim"/> (Harmony postfix on
        ///     InventoryItem.InventoryItemClass.CreateEntity(ItemUse,bool)).
        ///
        /// Lookup &amp; caching:
        ///   • Candidate filename stems include e.g.:
        ///       - 0007_ItemName_model.png
        ///       - ItemName_model.png
        ///   • Loaded textures are cached per ID+name in _modelSkinCache for
        ///     the currently active texture pack.
        ///   • On a pack switch, <see cref="EntitySkinRegistry.OnPackSwitched"/> retires
        ///     and clears these cached textures.
        ///
        /// Application:
        ///   • This method does not touch Effects directly; it only associates the skin
        ///     texture with the concrete <paramref name="entity"/> via
        ///     <see cref="EntitySkinRegistry.SetSkin"/>.
        ///   • A separate draw-time patch (see
        ///     <see cref="Patch_PerEntityModelSkin_AtSetEffectParams"/>) reads that
        ///     registry and binds the texture to each mesh part's Effect.
        /// </remarks>
        public static void TryApplyItemModelSkin(Entity entity, InventoryItemIDs id, string friendlyName)
        {
            try
            {
                var cfg = TPConfig.LoadOrCreate();
                var itemModelsDir = Path.Combine(PacksRoot, cfg.ActivePack ?? "", "Models", "Items");
                if (!Directory.Exists(itemModelsDir)) return;

                var key = CacheKey(id, friendlyName);

                // Resolve or (re)load the skin into the per-pack cache.
                if (!_modelSkinCache.TryGetValue(key, out var skinTex) || skinTex?.IsDisposed == true)
                {
                    var png = ItemModelNameResolver.EnumerateModelSkinCandidates(itemModelsDir, id, friendlyName).FirstOrDefault(File.Exists);
                    if (png == null) return;

                    skinTex = LoadPng(CastleMinerZGame.Instance.GraphicsDevice, png);
                    if (skinTex == null) return;

                    _modelSkinCache[key] = skinTex;
                    Log($"[Model] Cached model skin for {friendlyName} ({id}) from '{Path.GetFileName(png)}'.");
                }

                // Register this entity instance -> skin texture mapping.
                // The SetEffectParams patch will consult this registry at draw-time and
                // swap the Effect's texture only while this entity is being rendered.
                EntitySkinRegistry.SetSkin(entity, _modelSkinCache[key]);
            }
            catch (Exception ex)
            {
                Log($"[Model] Skin application failed for {friendlyName} ({id}): {ex.Message}.");
            }
        }

        /// <summary>
        /// Central registry for per-entity model skins (Texture2D) used by the
        /// draw-time patch. Keyed by <see cref="Entity"/> reference, not world
        /// position or item ID, so multiple instances of the same item type can
        /// be skinned independently.
        /// </summary>
        internal static class EntitySkinRegistry
        {
            /// <summary>
            /// Reference-equality comparer so entities are keyed strictly by object
            /// identity (no overridden Equals / GetHashCode surprises).
            /// </summary>
            private sealed class RefEq : IEqualityComparer<Entity>
            {
                public static readonly RefEq Instance = new RefEq();
                public bool Equals(Entity x, Entity y) => ReferenceEquals(x, y);
                public int GetHashCode(Entity obj) =>
                    RuntimeHelpers.GetHashCode(obj);
            }

            /// <summary>
            /// Per-entity mapping to the active skin Texture2D (if any). Entries are
            /// added when an item entity is created and receives a model skin, and are
            /// cleared on pack switch or when an entity is explicitly unskinned.
            /// </summary>
            private static readonly Dictionary<Entity, Texture2D> _byEntity =
                new Dictionary<Entity, Texture2D>(RefEq.Instance);

            /// <summary>
            /// Tracks all textures created / owned by this registry. Used to retire
            /// (queue for GPU-safe dispose) all model skin textures on pack switch or
            /// global cleanup.
            /// </summary>
            private static readonly HashSet<Texture2D> _ours = new HashSet<Texture2D>();

            /// <summary>
            /// Permanent tag store for mod-created textures (per Texture2D instance).
            /// This is used by other systems to answer "is this one of OUR textures?"
            /// without maintaining a central list. It auto-clears on GC and is NOT
            /// wiped on pack switch.
            /// </summary>
            private static readonly ConditionalWeakTable<Texture2D, object> _owned =
                new ConditionalWeakTable<Texture2D, object>();

            /// <summary>
            /// Entities we have already processed for skin registration. Prevents
            /// double-registration when multiple CreateEntity overrides are
            /// patched in the inheritance chain.
            /// </summary>
            private static readonly HashSet<Entity> _seenEntities = new HashSet<Entity>(RefEq.Instance);

            /// <summary>
            /// Try to resolve a live, non-disposed skin texture for the given entity.
            /// Returns true and a non-null, non-disposed texture if this entity
            /// was registered with <see cref="SetSkin"/> and the texture has not been
            /// retired or disposed.
            /// </summary>
            public static bool TryGetSkin(Entity e, out Texture2D tex)
                => _byEntity.TryGetValue(e, out tex) && tex != null && !tex.IsDisposed;

            /// <summary>
            /// Clear all per-entity skin mappings and retire any owned textures. This
            /// is typically invoked on pack switch:
            ///
            ///   • All textures tracked in <see cref="_ours"/> are queued via
            ///     <see cref="GpuRetireQueue"/> for safe GPU disposal.
            ///   • The entity -> skin map is emptied.
            ///   • The per-entity "seen" gate is reset so new entities can be skinned.
            ///
            /// NOTE:
            ///   The <see cref="_owned"/> ConditionalWeakTable is intentionally not
            ///   cleared; it represents a global "this Texture2D was mod-created"
            ///   marker and is safe to leave intact across pack switches.
            /// </summary>
            public static void ClearAll()
            {
                foreach (var tex in _ours.ToArray())
                    GpuRetireQueue.Enqueue(tex);
                _ours.Clear();
                _byEntity.Clear();
                _seenEntities.Clear();
                // NOTE: _owned is intentionally NOT cleared...
            }

            /// <summary>
            /// Legacy shim to keep older calls compiling; forwards directly to
            /// <see cref="Set(Entity,Texture2D)"/>.
            /// </summary>
            public static void SetSkin(Entity e, Texture2D tex) => Set(e, tex);

            /// <summary>
            /// Mark the given texture as "owned" by the mod system, so other code can
            /// cheaply test <see cref="IsOurTexture"/> later. No-op for null.
            /// </summary>
            private static void MarkOwned(Texture2D tex)
            {
                if (tex == null) return;
                // Never throws; returns existing marker if present.
                _owned.GetValue(tex, _ => new object());
            }

            /// <summary>
            /// Returns true if the given texture has previously been tagged
            /// via <see cref="MarkOwned"/>, indicating it originated from one of the
            /// mod's loaders rather than vanilla content.
            /// </summary>
            public static bool IsOurTexture(Texture2D tex)
            {
                if (tex == null) return false;
                return _owned.TryGetValue(tex, out _);
            }

            /// <summary>
            /// Register or update a skin for the specified entity:
            ///
            ///   • <paramref name="next"/> != null: entity is now associated
            ///     with this texture; the texture is tracked in <see cref="_ours"/>
            ///     and tagged as owned.
            ///   • <paramref name="next"/> == null: entity mapping is removed
            ///     (no skin for this entity).
            ///
            /// Texture lifetime:
            ///   • Textures are not disposed here; they are queued for disposal in
            ///     bulk by <see cref="ClearAll"/> and the pack-switch pipeline.
            /// </summary>
            public static void Set(Entity e, Texture2D next)
            {
                if (e == null)
                    return;

                if (next == null)
                {
                    _byEntity.Remove(e);
                    return;
                }

                _byEntity[e] = next;
                _ours.Add(next);
                MarkOwned(next);
            }

            /// <summary>
            /// Simple gate to ensure we only attempt to skin an entity once. Returns
            /// true the first time a given entity is seen; subsequent calls for
            /// the same entity return false.
            ///
            /// This is used by <see cref="ItemEntitySkinner.PostfixShim"/> to avoid
            /// double-registration when multiple CreateEntity overrides on a
            /// type hierarchy are all patched.
            /// </summary>
            public static bool MarkFirstTime(Entity e)
            {
                if (e == null)
                    return false;

                // CastleMinerZ creates entities on the main thread, so we can keep this simple.
                if (_seenEntities.Contains(e))
                    return false;

                _seenEntities.Add(e);
                return true;
            }

            /// <summary>
            /// Pack-switch entry point used by the texture-pack manager:
            ///
            ///   • Retires all cached model skin textures from _modelSkinCache.
            ///   • Clears per-entity skin mappings and "seen" gates via <see cref="ClearAll"/>.
            ///
            /// The per-effect default cache used by the SetEffectParams patch is now
            /// based on "first-seen" textures and does not require explicit reset here.
            /// </summary>
            public static void OnPackSwitched()
            {
                // Retire cached textures so Effects stop pointing at them once the frame ends.
                foreach (var tex in _modelSkinCache.Values)
                    GpuRetireQueue.Enqueue(tex);
                _modelSkinCache.Clear();

                EntitySkinRegistry.ClearAll();
                // GamePatches.Patch_PerEntityModelSkin_AtSetEffectParams.SoftResetForPackSwitch();
            }
        }
        #endregion

        #region Door Model Skins (Full-Sheet Overrides)

        /// <summary>
        /// Attempts to locate the first diffuse/albedo texture bound to a model's mesh parts.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - Door models do not expose their diffuse texture through a simple public property,
        ///   so we inspect the mesh part effect bindings.
        /// - This is used to capture the vanilla full-sheet textures once at runtime so exports
        ///   and pack reloads have a reliable source image.
        /// </remarks>
        private static Texture2D GetFirstDiffuseTextureFromModel(Model model)
        {
            if (model == null)
                return null;

            foreach (var mesh in model.Meshes)
            {
                foreach (var part in mesh.MeshParts)
                {
                    var fx = part.Effect;
                    if (fx == null)
                        continue;

                    if (fx is BasicEffect be && be.Texture != null)
                        return be.Texture;

                    var names = new[] { "Texture", "Texture0", "DiffuseTexture", "DiffuseMap", "Albedo", "AlbedoMap" };
                    foreach (var n in names)
                    {
                        var p = fx.Parameters[n];
                        if (p == null)
                            continue;

                        try
                        {
                            var tex = p.GetValueTexture2D();
                            if (tex != null)
                                return tex;
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Captures the vanilla full-sheet door textures from the live game models once per session.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - These source textures are used for exporting door sheets and as a known-good
        ///   fallback reference for active runtime overrides.
        /// - Because the game resolves door visuals through DoorEntity models, this is the
        ///   most reliable way to discover the true texture in use.
        /// </remarks>
        private static void CaptureDoorVanillaBaselinesIfNeeded()
        {
            var names = new[]
            {
                DoorEntity.ModelNameEnum.Wood,
                DoorEntity.ModelNameEnum.Iron,
                DoorEntity.ModelNameEnum.Diamond,
                DoorEntity.ModelNameEnum.Tech,
            };

            foreach (var modelName in names)
            {
                if (_doorSkinVanilla.TryGetValue(modelName, out Texture2D existing) && existing != null && !existing.IsDisposed)
                    continue;

                try
                {
                    var model = DoorEntity.GetDoorModel(modelName);
                    var tex = GetFirstDiffuseTextureFromModel(model);

                    if (tex != null && !tex.IsDisposed)
                        _doorSkinVanilla[modelName] = tex;
                }
                catch (Exception ex)
                {
                    Log($"[Doors] Failed to capture vanilla baseline for {modelName}: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Applies full-sheet door overrides loaded from Models\Doors\*.png.
        /// </summary>
        /// <param name="reps">Loaded full-sheet replacements for the active pack.</param>
        /// <remarks>
        /// NOTE:
        /// - Existing overrides are cleared before new ones are installed.
        /// - Replacement textures are not cloned; the loaded Texture2D is stored directly
        ///   as the active override for the corresponding door model family.
        /// - Source PNG textures are intentionally NOT disposed here because they become
        ///   the live active override textures.
        /// </remarks>
        private static void ApplyDoorSheetReplacements(List<DoorSheetReplacement> reps)
        {
            foreach (var tex in _doorSkinOverrides.Values)
            {
                try { tex?.Dispose(); } catch { }
            }
            _doorSkinOverrides.Clear();

            if (reps == null || reps.Count == 0)
                return;

            foreach (var rep in reps)
            {
                if (rep == null || rep.Image == null || rep.Image.IsDisposed)
                    continue;

                _doorSkinOverrides[rep.ModelName] = rep.Image;
                rep.Image = null; // Ownership transferred to _doorSkinOverrides.
            }

            foreach (var rep in reps)
                rep.Dispose();
        }

        /// <summary>
        /// Applies the active full-sheet override to a live door entity, if one exists.
        /// </summary>
        /// <param name="door">Live DoorEntity instance.</param>
        /// <param name="modelName">Door model family resolved by the game.</param>
        /// <remarks>
        /// NOTE:
        /// - DoorEntity renders through its private child model entity (_modelEnt).
        /// - We therefore register the override texture against that rendered child entity.
        /// </remarks>
        public static void TryApplyDoorModelSkin(DoorEntity door, DoorEntity.ModelNameEnum modelName)
        {
            try
            {
                if (door == null)
                    return;

                if (!_doorSkinOverrides.TryGetValue(modelName, out Texture2D skin) || skin == null || skin.IsDisposed)
                    return;

                var f = AccessTools.Field(typeof(DoorEntity), "_modelEnt");
                if (f == null)
                    return;

                if (!(f.GetValue(door) is Entity modelEnt))
                    return;

                EntitySkinRegistry.SetSkin(modelEnt, skin);
            }
            catch (Exception ex)
            {
                Log($"[DoorSkin] Failed to apply door skin: {ex.Message}.");
            }
        }
        #endregion

        #region Item Model Geometry: Overrides (XNB)

        /// <summary>
        /// Item model geometry overrides:
        /// - Loads pack-local *.xnb Models from Models\Items\
        /// - Applies them to ModelEntity instances (held item entities).
        /// - Captures and preserves the *vanilla model* per entity so pack switches can revert cleanly.
        /// </summary>
        internal static class ItemModelGeometryOverrides
        {
            #region Tracking (Entity -> Vanilla Baseline + Identity)

            /// <summary>
            /// Tracks a spawned item entity so we can:
            ///   1) Restore the vanilla model on pack switch
            ///   2) Re-apply the new pack's model (if present)
            /// </summary>
            private sealed class GeoEntry
            {
                public Model VanillaModel;
                public InventoryItemIDs Id;
                public string FriendlyName;
            }

            /// <summary>
            /// Reference-equality comparer (entities are identity objects).
            /// </summary>
            private sealed class RefEq<T> : IEqualityComparer<T> where T : class
            {
                public static readonly RefEq<T> Instance = new RefEq<T>();
                public bool Equals(T a, T b) => ReferenceEquals(a, b);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            /// <summary>
            /// All item entities we have seen (created via InventoryItemClass.CreateEntity).
            /// We keep a vanilla baseline model per entity so pack switches can revert.
            /// </summary>
            private static readonly Dictionary<ModelEntity, GeoEntry> _byEntity =
                new Dictionary<ModelEntity, GeoEntry>(RefEq<ModelEntity>.Instance);

            #endregion

            #region Cache (Key -> Loaded Model)

            /// <summary>
            /// Cache of resolved geometry overrides per item key (ID + friendly name).
            /// Summary: Prevents repeated disk scans + repeated XNB model loads.
            /// </summary>
            private static readonly Dictionary<string, Model> _modelGeoCache =
                new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);

            #endregion

            #region Reflection (ModelEntity.Model)

            /// <summary>
            /// Protected ModelEntity.Model property:
            ///   set => SetupModel(value) (rebuilds skeleton pose + bounds).
            /// </summary>
            private static readonly PropertyInfo PI_ModelEntity_Model =
                AccessTools.Property(typeof(ModelEntity), "Model");

            private static Model GetModel(ModelEntity e)
            {
                try { return (Model)PI_ModelEntity_Model.GetValue(e, null); }
                catch { return null; }
            }

            private static void SetModel(ModelEntity e, Model m)
            {
                try { PI_ModelEntity_Model.SetValue(e, m, null); }
                catch { /* best-effort */ }
            }
            #endregion

            #region Pack Switch Hook

            /// <summary>
            /// Pack-switch entry point called from TexturePackManager.DoReloadCore().
            ///
            /// Behavior:
            ///   1) Restore ALL tracked item entities to their vanilla baseline model
            ///   2) Clear caches & reset the pack ContentManager (drops old XNB assets)
            ///   3) Re-apply geometry overrides for the NEW active pack (if files exist)
            ///
            /// Result:
            ///   - Switching packs updates models live.
            ///   - If the new pack is missing a model override, that entity stays vanilla.
            /// </summary>
            public static void OnPackSwitched()
            {
                // 1) Restore vanilla first (so old pack models are no longer referenced by live entities).
                foreach (var kv in _byEntity.ToArray())
                {
                    var ent = kv.Key;
                    var entry = kv.Value;
                    if (ent == null || entry == null) continue;

                    if (entry.VanillaModel != null)
                        SetModel(ent, entry.VanillaModel);
                }

                // 2) Clear per-pack caches + reset loader.
                _modelGeoCache.Clear();
                ResetPackModelCM();

                // 3) Re-evaluate for the new active pack.
                foreach (var kv in _byEntity.ToArray())
                {
                    var ent = kv.Key;
                    var entry = kv.Value;
                    if (ent == null || entry == null) continue;

                    TryApplyItemModelGeometry(ent, entry.Id, entry.FriendlyName);
                }
            }
            #endregion

            #region Apply (CreateEntity Postfix Call)

            /// <summary>
            /// Attempts to apply a pack-provided XNB Model geometry override onto a spawned item entity.
            ///
            /// Notes:
            /// - Always captures the entity's current Model as the vanilla baseline the first time we see it.
            /// - If no override exists in the active pack, this method leaves the entity vanilla.
            /// </summary>
            public static void TryApplyItemModelGeometry(Entity entity, InventoryItemIDs id, string friendlyName)
            {
                if (!(entity is ModelEntity me))
                    return;

                // Track baseline + identity once per entity (enables live pack switching).
                if (!_byEntity.TryGetValue(me, out var entry) || entry == null)
                {
                    entry = new GeoEntry
                    {
                        VanillaModel = GetModel(me), // Capture baseline BEFORE we ever replace.
                        Id = id,
                        FriendlyName = friendlyName ?? ""
                    };
                    _byEntity[me] = entry;
                }

                var cfg = TPConfig.LoadOrCreate();

                // Pack folder layout:
                //   PacksRoot\<ActivePack>\Models\Items\*.xnb
                var dir = Path.Combine(TexturePackManager.PacksRoot, cfg.ActivePack ?? "", "Models", "Items");
                if (!Directory.Exists(dir))
                    return; // no folder -> vanilla

                var key = TexturePackManager.CacheKey(id, friendlyName);

                // Resolve + load once per key.
                if (!_modelGeoCache.TryGetValue(key, out var model) || model == null)
                {
                    var xnbPath = TexturePackManager.ItemModelGeoResolver
                        .EnumerateModelXnbCandidates(dir, id, friendlyName)
                        .FirstOrDefault(File.Exists);

                    if (xnbPath == null)
                        return; // No override -> vanilla.

                    var xnbRoot   = Path.GetDirectoryName(xnbPath);
                    var assetName = Path.GetFileNameWithoutExtension(xnbPath);

                    // IMPORTANT: Root at the XNB's folder so model + dependencies load from the same place.
                    var cm = GetPackCM(xnbRoot);
                    model  = cm.Load<Model>(assetName);
                    _modelGeoCache[key] = model;
                }

                // Apply (invokes SetupModel via the protected setter).
                SetModel(me, model);
            }
            #endregion
        }
        #endregion

        #region Door Model Geometry Overrides (XNB)

        /// <summary>
        /// Runtime manager for pack-provided door model geometry overrides loaded from
        /// Packs\...\Models\Doors\*.xnb.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - Doors are rendered by DoorEntity's private child model entity (_modelEnt).
        /// - We preserve that child entity and only swap its Model so the vanilla lighting,
        ///   transforms, and door-open/door-closed positioning behavior stay intact.
        /// - PNG door sheet overrides can still be layered on top of a custom XNB model.
        /// </remarks>
        internal static class DoorModelGeometryOverrides
        {
            #region Tracking

            private sealed class GeoEntry
            {
                public DoorEntity.ModelNameEnum ModelName;
                public ModelEntity ModelEntity;
                public Model VanillaModel;
            }

            private sealed class RefEq<T> : IEqualityComparer<T> where T : class
            {
                public static readonly RefEq<T> Instance = new RefEq<T>();
                public bool Equals(T a, T b) => ReferenceEquals(a, b);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            private static readonly Dictionary<DoorEntity, GeoEntry> _byDoor =
                new Dictionary<DoorEntity, GeoEntry>(RefEq<DoorEntity>.Instance);

            private static readonly Dictionary<DoorEntity.ModelNameEnum, Model> _modelGeoCache =
                new Dictionary<DoorEntity.ModelNameEnum, Model>();

            #endregion

            #region Reflection

            private static readonly FieldInfo FI_Door_ModelEnt =
                AccessTools.Field(typeof(DoorEntity), "_modelEnt");

            private static readonly PropertyInfo PI_ModelEntity_Model =
                AccessTools.Property(typeof(ModelEntity), "Model");

            private static ModelEntity GetDoorModelEntity(DoorEntity door)
            {
                try { return FI_Door_ModelEnt?.GetValue(door) as ModelEntity; }
                catch { return null; }
            }

            private static Model GetModel(ModelEntity e)
            {
                try { return (Model)PI_ModelEntity_Model.GetValue(e, null); }
                catch { return null; }
            }

            private static void SetModel(ModelEntity e, Model m)
            {
                try { PI_ModelEntity_Model.SetValue(e, m, null); }
                catch { }
            }
            #endregion

            #region Pack Switch Hook

            /// <summary>
            /// Restores tracked live door entities back to their vanilla baseline model,
            /// clears pack caches, and reapplies any active door geometry override.
            /// </summary>
            public static void OnPackSwitched()
            {
                // 1) Restore all tracked live doors to vanilla before unloading pack assets.
                foreach (var kv in _byDoor.ToArray())
                {
                    var door = kv.Key;
                    var entry = kv.Value;
                    if (door == null || entry == null || entry.ModelEntity == null)
                        continue;

                    if (entry.VanillaModel != null)
                        SetModel(entry.ModelEntity, entry.VanillaModel);
                }

                // 2) Clear loaded pack models + folder-rooted content managers.
                _modelGeoCache.Clear();
                ResetPackModelCM();

                // 3) Reapply against the new active pack.
                foreach (var kv in _byDoor.ToArray())
                {
                    var door = kv.Key;
                    var entry = kv.Value;
                    if (door == null || entry == null)
                        continue;

                    TryApplyDoorModelGeometry(door, entry.ModelName);
                }
            }
            #endregion

            #region Apply

            /// <summary>
            /// Attempts to apply a pack-provided XNB Model geometry override to a live door entity.
            /// </summary>
            /// <remarks>
            /// NOTE:
            /// - Supported filenames are:
            ///     Models\Doors\NormalDoor.xnb
            ///     Models\Doors\IronDoor.xnb
            ///     Models\Doors\DiamondDoor.xnb
            ///     Models\Doors\TechDoor.xnb
            /// - Per-folder variants are also supported:
            ///     Models\Doors\NormalDoor\NormalDoor.xnb
            ///     Models\Doors\NormalDoor\Model.xnb
            /// - Dependencies should live beside the selected XNB.
            /// </remarks>
            public static void TryApplyDoorModelGeometry(DoorEntity door, DoorEntity.ModelNameEnum modelName)
            {
                if (door == null || modelName == DoorEntity.ModelNameEnum.None)
                    return;

                var modelEnt = GetDoorModelEntity(door);
                if (modelEnt == null)
                    return;

                // Track the live door once so pack switches can restore/reapply safely.
                if (!_byDoor.TryGetValue(door, out var entry) || entry == null)
                {
                    entry = new GeoEntry
                    {
                        ModelName = modelName,
                        ModelEntity = modelEnt,
                        VanillaModel = GetModel(modelEnt)
                    };
                    _byDoor[door] = entry;
                }
                else
                {
                    entry.ModelName = modelName;
                    if (entry.ModelEntity == null)
                        entry.ModelEntity = modelEnt;
                    if (entry.VanillaModel == null)
                        entry.VanillaModel = GetModel(modelEnt);
                }

                var cfg = TPConfig.LoadOrCreate();
                var dir = Path.Combine(PacksRoot, cfg.ActivePack ?? "", "Models", "Doors");
                if (!Directory.Exists(dir))
                    return;

                if (!_modelGeoCache.TryGetValue(modelName, out var model) || model == null)
                {
                    var xnbPath = DoorModelGeoResolver
                        .EnumerateDoorCandidates(dir, modelName)
                        .FirstOrDefault(File.Exists);

                    if (xnbPath == null)
                        return;

                    var xnbRoot = Path.GetDirectoryName(xnbPath);
                    var assetName = Path.GetFileNameWithoutExtension(xnbPath);

                    var cm = GetPackCM(xnbRoot);
                    model = cm.Load<Model>(assetName);
                    _modelGeoCache[modelName] = model;
                }

                SetModel(modelEnt, model);
            }
            #endregion
        }
        #endregion

        #endregion

        #region Model Geometry: Global (XNB)

        internal static class ModelGeometryManager
        {
            #region Baselines (Vanilla)

            private sealed class RefEq<T> : IEqualityComparer<T> where T : class
            {
                public static readonly RefEq<T> Instance = new RefEq<T>();
                public bool Equals(T a, T b)  => ReferenceEquals(a, b);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            private static bool  _captured;
            private static Model _playerProxyVanilla;

            private static readonly Dictionary<EnemyType, Model> _enemyVanilla =
                new Dictionary<EnemyType, Model>(RefEq<EnemyType>.Instance);

            private static Model _dragonBodyVanilla;
            private static Model _dragonFeetVanilla;

            #endregion

            #region Reflection Handles

            private static readonly FieldInfo FI_PlayerProxyModel =
                AccessTools.Field(typeof(Player), "ProxyModel");

            private static readonly FieldInfo FI_EnemyTypes =
                AccessTools.Field(typeof(EnemyType), "Types");

            private static readonly FieldInfo FI_DragonBody =
                AccessTools.Field(typeof(DragonClientEntity), "DragonBody");

            private static readonly FieldInfo FI_DragonFeet =
                AccessTools.Field(typeof(DragonClientEntity), "DragonFeet");

            private static readonly PropertyInfo PI_ModelEntity_Model =
                AccessTools.Property(typeof(ModelEntity), "Model");

            private static readonly FieldInfo FI_EnemyManager_Enemies =
                AccessTools.Field(typeof(EnemyManager), "_enemies");

            static readonly FieldInfo FI_EnemyMgr_DragonClient =
                AccessTools.Field(typeof(EnemyManager), "_dragonClient");

            static readonly FieldInfo FI_EnemyMgr_DragonHost =
                AccessTools.Field(typeof(EnemyManager), "_dragon");

            #endregion

            #region Pack ContentManager (XNB-From-Folder)

            // ContentManager that loads XNB assets from an arbitrary folder.
            // Summary: Used for global model geometry overrides (Player/Enemies/Dragons).
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

            #region Pack Model Content Cache (Per-Folder ContentManagers)

            // Summary:
            // - One PackContentManager per model folder root.
            // - Avoids root-switch churn when multiple enemies/models live in different subfolders.
            // - Keeps dependencies isolated beside each model (no "texture_0" collisions).
            private static readonly Dictionary<string, PackContentManager> _cms =
                new Dictionary<string, PackContentManager>(StringComparer.OrdinalIgnoreCase);

            private static PackContentManager GetPackCM(string root)
            {
                if (string.IsNullOrWhiteSpace(root))
                    return null;

                if (!_cms.TryGetValue(root, out var cm) || cm == null)
                {
                    cm = new PackContentManager(CastleMinerZGame.Instance.Services, root);
                    _cms[root] = cm;
                }
                return cm;
            }

            private static void ResetPackCM()
            {
                foreach (var cm in _cms.Values)
                {
                    try { cm?.Unload(); } catch { }
                    try { (cm as IDisposable)?.Dispose(); } catch { }
                }
                _cms.Clear();
            }
            #endregion

            #endregion

            #region Cache (Path -> Loaded Model)

            /// <summary>
            /// Cache per file path (so flat + per-folder both work).
            /// </summary>
            private static readonly Dictionary<string, Model> _cache =
                new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Loads a Model from an .xnb file path using a ContentManager rooted at the file's folder.
            /// Results are cached by full path.
            /// </summary>
            private static Model LoadModelFromXnb(string xnbPath)
            {
                if (string.IsNullOrWhiteSpace(xnbPath) || !File.Exists(xnbPath))
                    return null;

                if (_cache.TryGetValue(xnbPath, out var cached) && cached != null)
                    return cached;

                var root = Path.GetDirectoryName(xnbPath);
                var asset = Path.GetFileNameWithoutExtension(xnbPath);

                var cm = GetPackCM(root);
                var m = cm.Load<Model>(asset);

                _cache[xnbPath] = m;
                return m;
            }
            #endregion

            #region Live Rebind Helpers (Detach From Soon-To-Be-Disposed ContentManager)

            /// <summary>
            /// Best-effort setter for ModelEntity.Model. Uses reflection; safe to call even if it fails.
            /// </summary>
            static void SetModelBestEffort(object maybeModelEntity, Model m)
            {
                if (maybeModelEntity == null || m == null) return;
                try { PI_ModelEntity_Model?.SetValue(maybeModelEntity, m, null); } catch { }
            }

            /// <summary>
            /// Rebinds live Player avatars to the current Player.ProxyModel.
            /// This prevents live players from holding onto a model whose ContentManager is about to be disposed.
            /// </summary>
            static void RebindLivePlayersToCurrentProxyModel()
            {
                try
                {
                    // Current proxy model (after RestoreVanilla or after ApplyPlayer)
                    if (!(FI_PlayerProxyModel?.GetValue(null) is Model proxy)) return;

                    var sess = CastleMinerZGame.Instance?.CurrentNetworkSession;
                    if (sess == null) return;

                    for (int i = 0; i < sess.AllGamers.Count; i++)
                    {
                        var g = sess.AllGamers[i];
                        if (!(g?.Tag is Player p)) continue;
                        if (p.Avatar == null) continue;

                        // SAFEST: Rebuild the skinned entity so bone arrays match the model.
                        // Player creates it this same way in the ctor.
                        try { p.Avatar.ProxyModelEntity = new PlayerModelEntity(proxy); } catch { }

                        // If you know skeletons always match, you can alternatively just SetModelBestEffort(p.Avatar.ProxyModelEntity, proxy);
                    }
                }
                catch { }
            }

            /// <summary>
            /// Rebinds any live enemies to their current EnemyType.Model.
            /// Prevents zombies from holding pack-loaded models that will be disposed on switch.
            /// </summary>
            private static void RebindLiveEnemiesToCurrentEnemyTypeModels()
            {
                try
                {
                    var mgr = EnemyManager.Instance;
                    if (mgr == null) return;

                    if (!(FI_EnemyManager_Enemies?.GetValue(mgr) is IList list)) return;

                    foreach (var obj in list)
                    {
                        if (obj is BaseZombie z && z.EType?.Model != null)
                        {
                            // Swap away from any pack-loaded model that might be about to get unloaded.
                            PI_ModelEntity_Model?.SetValue(z, z.EType.Model, null);
                        }
                    }
                }
                catch { }
            }

            /// <summary>
            /// Rebinds any live dragons (client + host) to the current static DragonBody/DragonFeet.
            /// Prevents active dragon parts from holding pack-loaded models that will be disposed on switch.
            /// </summary>
            static void RebindLiveDragonsToCurrentStatics()
            {
                try
                {
                    var mgr = EnemyManager.Instance;
                    if (mgr == null) return;

                    var body = FI_DragonBody?.GetValue(null) as Model; // private static in DragonClientEntity.
                    var feet = FI_DragonFeet?.GetValue(null) as Model; // public static.

                    // Client dragon: Two-part array.
                    var dc = FI_EnemyMgr_DragonClient?.GetValue(mgr) as DragonClientEntity;
                    if (dc?._dragonModel != null)
                    {
                        if (dc._dragonModel.Length > 0) SetModelBestEffort(dc._dragonModel[0], body);
                        if (dc._dragonModel.Length > 1) SetModelBestEffort(dc._dragonModel[1], feet);
                    }

                    // Host dragon: Single part.
                    var dh = FI_EnemyMgr_DragonHost?.GetValue(mgr) as DragonEntity;
                    if (dh?._dragonModel != null)
                    {
                        SetModelBestEffort(dh._dragonModel, feet);
                    }
                }
                catch { }
            }
            #endregion

            #region Pack Switch Lifecycle

            public static void OnPackSwitched()
            {
                CaptureBaselinesIfNeeded();
                RestoreVanilla();

                // IMPORTANT: Detach live enemies & dragons from pack-loaded models BEFORE Unload/Dispose.
                RebindLivePlayersToCurrentProxyModel();
                RebindLiveEnemiesToCurrentEnemyTypeModels();
                RebindLiveDragonsToCurrentStatics();

                _cache.Clear();
                ResetPackCM();
            }

            public static void ApplyActiveModelGeometryNow()
            {
                ApplyPlayer();
                ApplyEnemies();
                ApplyDragons();
            }
            #endregion

            #region Baseline Capture / Restore

            private static void CaptureBaselinesIfNeeded()
            {
                if (_captured) return;
                _captured = true;

                // Player proxy model baseline
                try
                {
                    if (_playerProxyVanilla == null && FI_PlayerProxyModel?.GetValue(null) is Model pm)
                        _playerProxyVanilla = pm;
                }
                catch { }

                // Enemy model baselines
                try
                {
                    if (FI_EnemyTypes?.GetValue(null) is EnemyType[] arr)
                    {
                        foreach (var et in arr)
                        {
                            if (et == null || _enemyVanilla.ContainsKey(et)) continue;
                            try
                            {
                                // EnemyType exposes Model on most builds.
                                var m = et.Model;
                                if (m != null) _enemyVanilla[et] = m;
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // Dragon body/feet baselines (shared static geometry)
                try { if (_dragonBodyVanilla == null) _dragonBodyVanilla = FI_DragonBody?.GetValue(null) as Model; } catch { }
                try { if (_dragonFeetVanilla == null) _dragonFeetVanilla = FI_DragonFeet?.GetValue(null) as Model; } catch { }
            }

            private static void RestoreVanilla()
            {
                // Player
                try { if (_playerProxyVanilla != null) FI_PlayerProxyModel?.SetValue(null, _playerProxyVanilla); } catch { }

                // Enemies (affects future spawns)
                try
                {
                    foreach (var kv in _enemyVanilla)
                    {
                        var et = kv.Key; var m = kv.Value;
                        if (et == null || m == null) continue;
                        try { et.Model = m; } catch { }
                    }
                }
                catch { }

                // Dragons (affects future dragon part entities)
                try { if (_dragonBodyVanilla != null) FI_DragonBody?.SetValue(null, _dragonBodyVanilla); } catch { }
                try { if (_dragonFeetVanilla != null) FI_DragonFeet?.SetValue(null, _dragonFeetVanilla); } catch { }
            }

            #endregion

            #region Apply (File Probes)

            private static void ApplyPlayer()
            {
                var cfg = TPConfig.LoadOrCreate();
                var dir = Path.Combine(TexturePackManager.PacksRoot, cfg.ActivePack ?? "", "Models", "Player");
                if (!Directory.Exists(dir)) return;

                var xnb = GlobalModelGeoResolver
                    .EnumeratePlayerCandidates(dir)
                    .FirstOrDefault(File.Exists);

                if (xnb == null) return;

                var model = LoadModelFromXnb(xnb);
                if (model == null) return;

                // Graft vanilla animation data so player clip lookups won't explode.
                if (_playerProxyVanilla != null)
                    TryGraftVanillaAnimationTag(_playerProxyVanilla, model, "Player");

                try { FI_PlayerProxyModel?.SetValue(null, model); } catch { }
            }

            private static void ApplyEnemies()
            {
                var cfg = TPConfig.LoadOrCreate();
                var dir = Path.Combine(TexturePackManager.PacksRoot, cfg.ActivePack ?? "", "Models", "Enemies");
                if (!Directory.Exists(dir)) return;

                if (!(FI_EnemyTypes?.GetValue(null) is EnemyType[] arr) || arr == null)
                    return;

                foreach (var et in arr)
                {
                    if (et == null) continue;

                    var name = et.EType.ToString();

                    var xnb = GlobalModelGeoResolver
                        .EnumerateEnemyCandidates(dir, name)
                        .FirstOrDefault(File.Exists);

                    if (xnb == null) continue;

                    var model = LoadModelFromXnb(xnb);
                    if (model == null) continue;

                    // Graft vanilla animation data so PlayClip(clipName) won't KeyNotFound.
                    if (_enemyVanilla.TryGetValue(et, out var vanillaModel) && vanillaModel != null)
                        TryGraftVanillaAnimationTag(vanillaModel, model, $"Enemy {name}");

                    try { et.Model = model; } catch { }
                }
            }

            private static void ApplyDragons()
            {
                var cfg = TPConfig.LoadOrCreate();
                var dir = Path.Combine(TexturePackManager.PacksRoot, cfg.ActivePack ?? "", "Models", "Dragons");
                if (!Directory.Exists(dir)) return;

                // DragonBody.
                {
                    var xnb = GlobalModelGeoResolver
                        .EnumerateDragonPartCandidates(dir, "DragonBody")
                        .FirstOrDefault(File.Exists);

                    if (xnb != null)
                    {
                        var m = LoadModelFromXnb(xnb);
                        if (m != null)
                        {
                            // Graft vanilla animation tag if the dragon body is skinned and matches.
                            if (_dragonBodyVanilla != null)
                                TryGraftVanillaAnimationTag(_dragonBodyVanilla, m, "DragonBody");

                            try { FI_DragonBody?.SetValue(null, m); } catch { }
                        }
                    }
                }

                // DragonFeet.
                {
                    var xnb = GlobalModelGeoResolver
                        .EnumerateDragonPartCandidates(dir, "DragonFeet")
                        .FirstOrDefault(File.Exists);

                    if (xnb != null)
                    {
                        var m = LoadModelFromXnb(xnb);
                        if (m != null)
                        {
                            // Graft vanilla animation tag if the dragon feet are skinned and match.
                            if (_dragonFeetVanilla != null)
                                TryGraftVanillaAnimationTag(_dragonFeetVanilla, m, "DragonFeet");

                            try { FI_DragonFeet?.SetValue(null, m); } catch { }
                        }
                    }
                }
            }
            #endregion

            #region Skinning Helpers

            /// <summary>
            /// Attempts to copy (graft) the vanilla SkinedAnimationData Tag onto a modded model,
            /// but only if the skeleton layout matches exactly (bone count + bone names by index).
            /// This preserves the original clip dictionary/skeleton/inv-bind data the game expects.
            /// </summary>
            private static void TryGraftVanillaAnimationTag(Model vanilla, Model modded, string label)
            {
                try
                {
                    if (vanilla == null || modded == null) return;

                    // We only graft if vanilla has the skinned tag we want.
                    if (!(vanilla.Tag is SkinedAnimationData))
                        return;

                    // Safety: Only graft if the bone layout matches (index + name).
                    if (vanilla.Bones.Count != modded.Bones.Count)
                    {
                        Log($"[Models] {label}: Bone count mismatch; vanilla={vanilla.Bones.Count} modded={modded.Bones.Count}; not grafting.");
                        // return;
                    }

                    for (int i = 0; i < vanilla.Bones.Count; i++)
                    {
                        var a = vanilla.Bones[i]?.Name ?? "";
                        var b = modded.Bones[i]?.Name ?? "";
                        if (!string.Equals(a, b, StringComparison.Ordinal))
                        {
                            Log($"[Models] {label}: Bone[{i}] name mismatch '{a}' != '{b}'; not grafting.");
                            // return;
                        }
                    }

                    // Graft: Keep vanilla clips/names/skeleton/invbind that the game expects.
                    modded.Tag = vanilla.Tag;

                    // Can be noisy.
                    // Log($"[Models] {label}: Grafted vanilla SkinedAnimationData onto pack model.");
                }
                catch (Exception ex)
                {
                    Log($"[Models] {label}: Graft failed: {ex.Message}.");
                }
            }
            #endregion
        }

        #region Door Model Geometry (XNB) - Path Candidate Resolver

        /// <summary>
        /// Candidate path resolver for door model geometry overrides.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - Supports both flat files and per-door subfolders so model dependencies can stay
        ///   beside the chosen XNB.
        /// </remarks>
        internal static class DoorModelGeoResolver
        {
            private static IEnumerable<string> Expand(string dir, string stem)
            {
                // Flat.
                yield return Path.Combine(dir, stem + ".xnb");

                // Per-folder (preferred).
                yield return Path.Combine(dir, stem, stem + ".xnb");

                // Convenience name.
                yield return Path.Combine(dir, stem, "Model.xnb");
            }

            private static string GetStem(DoorEntity.ModelNameEnum modelName)
            {
                switch (modelName)
                {
                    case DoorEntity.ModelNameEnum.Wood:
                        return "NormalDoor";

                    case DoorEntity.ModelNameEnum.Iron:
                        return "IronDoor";

                    case DoorEntity.ModelNameEnum.Diamond:
                        return "DiamondDoor";

                    case DoorEntity.ModelNameEnum.Tech:
                        return "TechDoor";

                    default:
                        return null;
                }
            }

            public static IEnumerable<string> EnumerateDoorCandidates(string dir, DoorEntity.ModelNameEnum modelName)
            {
                var stem = GetStem(modelName);
                if (string.IsNullOrWhiteSpace(stem))
                    yield break;

                foreach (var p in Expand(dir, stem))
                    yield return p;

                var lower = stem.ToLowerInvariant();
                if (lower != stem)
                {
                    foreach (var p in Expand(dir, lower))
                        yield return p;
                }
            }
        }
        #endregion

        #region Global Model Geometry (XNB) - Path Candidate Resolver

        /// <summary>
        /// Candidate path resolver for global model geometry overrides (Player / Enemies / Dragons).
        /// Summary:
        /// - Yields possible *.xnb locations for a given model "stem".
        /// - Supports both flat files (Models\<Category>\Stem.xnb) and per-model subfolders
        ///   (Models\<Category>\Stem\Stem.xnb or Models\<Category>\Stem\Model.xnb) so dependencies
        ///   can live beside the model without name collisions.
        /// </summary>
        internal static class GlobalModelGeoResolver
        {
            /// <summary>
            /// Expands a single stem into supported on-disk layouts (flat + per-folder).
            /// </summary>
            private static IEnumerable<string> Expand(string dir, string stem)
            {
                // Flat
                yield return Path.Combine(dir, stem + ".xnb");

                // Per-folder (preferred for dependency isolation)
                yield return Path.Combine(dir, stem, stem + ".xnb");

                // Optional convenience name
                yield return Path.Combine(dir, stem, "Model.xnb");
            }

            /// <summary>
            /// Player candidates (supports Player / Player_model / SWATMale).
            /// </summary>
            public static IEnumerable<string> EnumeratePlayerCandidates(string dir)
            {
                foreach (var p in Expand(dir, "SWATMale")) yield return p;
                foreach (var p in Expand(dir, "Player_model")) yield return p;
                foreach (var p in Expand(dir, "Player")) yield return p;
            }

            /// <summary>
            /// Enemy candidates keyed by enum name (supports exact + lower-case convenience).
            /// </summary>
            public static IEnumerable<string> EnumerateEnemyCandidates(string dir, string enumName)
            {
                if (string.IsNullOrWhiteSpace(enumName)) yield break;

                foreach (var p in Expand(dir, enumName)) yield return p;

                // optional convenience: lower-case folder/file
                var lower = enumName.ToLowerInvariant();
                if (lower != enumName)
                    foreach (var p in Expand(dir, lower)) yield return p;
            }

            /// <summary>
            /// Dragon part candidates (shared geometry like DragonBody / DragonFeet).
            /// </summary>
            public static IEnumerable<string> EnumerateDragonPartCandidates(string dir, string partStem)
            {
                foreach (var p in Expand(dir, partStem)) yield return p;

                var lower = partStem.ToLowerInvariant();
                if (lower != partStem)
                    foreach (var p in Expand(dir, lower)) yield return p;
            }
        }
        #endregion

        #endregion

        #endregion

        #region Pack Replacement: Blocks

        // Terrain atlas baselines and vanilla restore helpers.

        // --- Hot-swap baselines (vanilla pixels) ---
        private static readonly object _baselineLock = new object();
        private static Color[][] _nearBaseline; // For BlockTerrain._diffuseAlpha.
        private static Color[][] _mipBaseline;  // For BlockTerrain._mipMapDiffuse.

        private static Color[][] CaptureBaseline(Texture2D tex)
        {
            if (tex == null) return null;
            int levels = Math.Max(1, tex.LevelCount);
            var result = new Color[levels][];
            var gd = tex.GraphicsDevice;

            for (int level = 0; level < levels; level++)
            {
                int w = Math.Max(1, tex.Width >> level);
                int h = Math.Max(1, tex.Height >> level);
                var data = new Color[w * h];
                WithNoRenderTargets(gd, () => tex.GetData(level, null, data, 0, data.Length));
                result[level] = data;
            }
            return result;
        }

        private static void RestoreBaseline(Texture2D tex, Color[][] baseline)
        {
            if (tex == null || baseline == null) return;
            var gd = tex.GraphicsDevice;
            for (int level = 0; level < baseline.Length; level++)
            {
                int w = Math.Max(1, tex.Width >> level);
                int h = Math.Max(1, tex.Height >> level);
                var rect = new Rectangle(0, 0, w, h);
                var data = baseline[level];
                WithNoRenderTargets(gd, () => tex.SetData(level, rect, data, 0, data.Length));
            }
        }

        /// <summary>Capture vanilla pixels once (must happen before the first apply).</summary>
        private static void EnsureBaselinesCaptured()
        {
            lock (_baselineLock)
            {
                var near = FindTerrainAtlasTexture();
                if (near != null && _nearBaseline == null)
                    _nearBaseline = CaptureBaseline(near);

                var mip = FindTerrainMipDiffuseTexture();
                if (mip != null && _mipBaseline == null)
                    _mipBaseline = CaptureBaseline(mip);
            }
        }

        /// <summary>Restore all terrain atlases to vanilla.</summary>
        private static void RestoreTerrainToVanilla()
        {
            lock (_baselineLock)
            {
                RestoreBaseline(FindTerrainAtlasTexture(), _nearBaseline);
                RestoreBaseline(FindTerrainMipDiffuseTexture(), _mipBaseline);
            }
        }
        #endregion

        #endregion

        #region Blocks: Replacement Loading

        // Scans Blocks\ and turns per-file PNGs into target tile indices.

        /// <summary>
        /// Small DTO for a per-tile replacement: target tile index + source PNG.
        /// </summary>
        private sealed class TileReplacement : IDisposable
        {
            public int TileIndex;
            public Texture2D Image;

            /// <summary>
            /// Disposes the source Texture2D; caller decides when (can be deferred if patching multiple atlases).
            /// </summary>
            public void Dispose()
            {
                try { Image?.Dispose(); } catch { }
                Image = null;
            }
        }

        /// <summary>
        /// Scans Blocks\*.png and resolves each file to one or more target tile indices.
        /// </summary>
        /// <param name="blocksDir">Pack's Blocks directory.</param>
        /// <param name="gd">Graphics device for loading PNGs.</param>
        /// <returns>List of <see cref="TileReplacement"/> (one per tile index).</returns>
        /// <remarks>
        /// Supports "tile_###.png" (direct index) and "BlockName[_face].png"
        /// where face is one of: top, side, bottom, all (default: all).
        /// </remarks>
        private static List<TileReplacement> LoadBlockTileReplacements(string blocksDir, GraphicsDevice gd)
        {
            var list = new List<TileReplacement>();

            foreach (var path in Directory.EnumerateFiles(blocksDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(path);

                // 1) tile_###.png - direct, fastest path.
                if (TryParseTileIndexName(name, out int tileIdx))
                {
                    var tex = LoadPng(gd, path);
                    if (tex != null)
                    {
                        list.Add(new TileReplacement { TileIndex = tileIdx, Image = tex });
                        // Log($"+ tile_{tileIdx}.png.");
                    }
                    continue;
                }

                // 2) BlockName[_face].png - expand to one or more indices via exact engine mapping.
                if (TryParseBlockFaceName(name, out var block, out var face))
                {
                    var indices = ResolveTileIndicesFromFace(block, face).ToArray();
                    if (indices.Length == 0)
                    {
                        Log($"! Unresolved block face: {block}_{face}.");
                        continue;
                    }

                    var tex = LoadPng(gd, path);
                    if (tex == null) continue;

                    foreach (var idx in indices)
                        list.Add(new TileReplacement { TileIndex = idx, Image = tex });

                    // Log($"+ {block}_{face} -> [{string.Join(",", indices)}].");
                }
            }

            return list;
        }

        /// <summary>
        /// Attempts to parse a file stem like tile_123 into a zero-based index.
        /// </summary>
        private static bool TryParseTileIndexName(string bare, out int idx)
        {
            idx = -1;
            if (!bare.StartsWith("tile_", StringComparison.OrdinalIgnoreCase)) return false;
            var tail = bare.Substring(5);
            return int.TryParse(tail, out idx) && idx >= 0;
        }

        /// <summary>
        /// Parses "BlockName[_face]" into (block, face), defaulting face to "all".
        /// </summary>
        /// <remarks>Block is normalized to lowercase with underscores; face is lowercase.</remarks>
        private static bool TryParseBlockFaceName(string bare, out string block, out string face)
        {
            // BlockName[_face]  (face defaults to "all")
            block = null; face = "all";
            if (string.IsNullOrWhiteSpace(bare)) return false;

            var parts = bare.Split(new[] { '_' }, 2);
            block = parts[0].Trim();
            if (string.IsNullOrEmpty(block)) return false;

            if (parts.Length == 2)
                face = parts[1].Trim();

            block = block.ToLowerInvariant().Replace(' ', '_');
            face = face.ToLowerInvariant();

            return true;
        }

        /// <summary>
        /// Loads a PNG into a CPU-readable <see cref="Texture2D"/>.
        /// </summary>
        public static Texture2D LoadPng(GraphicsDevice gd, string path)
        {
            try { using (var s = File.OpenRead(path)) return Texture2D.FromStream(gd, s); }
            catch { return null; }
        }
        #endregion

        #region Models: Doors (Replacement Loading)

        /// <summary>
        /// Represents a full packed texture sheet replacement for one door model family.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - This replaces the entire diffuse texture bound to the door model.
        /// - We no longer try to treat doors like block-face tiles because the actual
        ///   door art is packed into a single model texture sheet.
        /// </remarks>
        private sealed class DoorSheetReplacement : IDisposable
        {
            public DoorEntity.ModelNameEnum ModelName;
            public Texture2D Image;

            public void Dispose()
            {
                try { Image?.Dispose(); } catch { }
                Image = null;
            }
        }

        /// <summary>
        /// Parses a door sheet filename stem from Models\Doors\*.png.
        /// </summary>
        /// <param name="bare">Filename without extension (for example "DiamondDoor").</param>
        /// <param name="modelName">Resolved door model family.</param>
        /// <returns>True if the filename is a supported canonical door model sheet name.</returns>
        private static bool TryParseDoorSheetName(string bare, out DoorEntity.ModelNameEnum modelName)
        {
            modelName = DoorEntity.ModelNameEnum.None;

            if (string.IsNullOrWhiteSpace(bare))
                return false;

            return _doorSheetNameMap.TryGetValue(bare.Trim(), out modelName);
        }

        /// <summary>
        /// Loads full-sheet door replacements from Packs\...\Models\Doors\*.png.
        /// </summary>
        /// <param name="doorsDir">Resolved Models\Doors directory for the active pack.</param>
        /// <param name="gd">Graphics device used to create Texture2D instances from PNG files.</param>
        /// <returns>List of full-sheet door texture replacements.</returns>
        /// <remarks>
        /// NOTE:
        /// - Supported canonical filenames are:
        ///     NormalDoor.png
        ///     IronDoor.png
        ///     DiamondDoor.png
        ///     TechDoor.png
        /// - Unknown filenames are ignored.
        /// </remarks>
        private static List<DoorSheetReplacement> LoadDoorSheetReplacements(string doorsDir, GraphicsDevice gd)
        {
            var list = new List<DoorSheetReplacement>();

            if (string.IsNullOrWhiteSpace(doorsDir) || !Directory.Exists(doorsDir) || gd == null)
                return list;

            foreach (var path in Directory.EnumerateFiles(doorsDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var bare = Path.GetFileNameWithoutExtension(path);

                if (!TryParseDoorSheetName(bare, out var modelName))
                    continue;

                var tex = LoadPng(gd, path);
                if (tex == null)
                    continue;

                list.Add(new DoorSheetReplacement
                {
                    ModelName = modelName,
                    Image = tex,
                });
            }

            return list;
        }
        #endregion

        #region Blocks: Atlas Patching

        // Pure CPU patching into near/far atlases (all mips), preserving original alpha.

        /// <summary>
        /// Overwrites destination atlas tiles with provided PNGs (scaled if needed), for all mip levels.
        /// </summary>
        /// <param name="atlas">Target atlas (near or far).</param>
        /// <param name="tileSize">Tile size at mip 0.</param>
        /// <param name="reps">List of tile replacements; each entry will be written to its TileIndex.</param>
        /// <param name="disposeImagesAfter">
        /// If true, disposes source images after writing (use false when reusing the same images for a second atlas).
        /// </param>
        /// <remarks>
        /// - Computes the destination rectangle per mip level.
        /// - Scales the source as needed (point/linear decisions are set elsewhere).
        /// - Merges the original atlas alpha back into the replacement data (coverage preservation).
        /// </remarks>
        /// <summary>
        /// Overwrites destination atlas tiles with provided PNGs (scaled if needed), for all mip levels.
        /// Pure CPU scaling; never binds a RenderTarget. All GPU reads/writes happen with NO RTs bound.
        /// </summary>
        private static void BlitReplacementsIntoAtlas(
            Texture2D atlas,
            int tileSize,
            List<TileReplacement> reps,
            bool disposeImagesAfter = false)
        {
            if (atlas == null || reps == null || reps.Count == 0) return;

            int cols = Math.Max(1, atlas.Width / Math.Max(1, tileSize));
            int mipCount = Math.Max(1, atlas.LevelCount);
            var gd = atlas.GraphicsDevice;

            foreach (var rep in reps)
            {
                if (rep?.Image == null || rep.Image.IsDisposed) continue;

                try
                {
                    int tx = Math.Max(0, rep.TileIndex % cols);
                    int ty = Math.Max(0, rep.TileIndex / cols);

                    for (int level = 0; level < mipCount; level++)
                    {
                        // Tile size and level bounds
                        int s = Math.Max(1, tileSize >> level);
                        int wL = Math.Max(1, atlas.Width >> level);
                        int hL = Math.Max(1, atlas.Height >> level);

                        var dest = new Rectangle(tx * s, ty * s, s, s);

                        // Clamp destination rect robustly.
                        int maxX = Math.Max(0, wL - dest.Width);
                        int maxY = Math.Max(0, hL - dest.Height);
                        if (dest.X < 0) dest.X = 0; else if (dest.X > maxX) dest.X = maxX;
                        if (dest.Y < 0) dest.Y = 0; else if (dest.Y > maxY) dest.Y = maxY;

                        // Source pixels (pure CPU; no RT binds).
                        Color[] data;
                        if (rep.Image.Width == s && rep.Image.Height == s)
                        {
                            data = new Color[s * s];
                            // Be conservative: read under "no RTs" guard.
                            WithNoRenderTargets(gd, () => rep.Image.GetData(data));
                        }
                        else
                        {
                            data = ToSizedPixelData(rep.Image, s, s); // Nearest-neighbor CPU scaler.
                        }

                        // Preserve original atlas alpha, then write - all under no-RT guard.
                        WithNoRenderTargets(gd, () =>
                        {
                            MergeOriginalAlpha(atlas, level, dest, data);
                            atlas.SetData(level, dest, data, 0, data.Length);
                        });
                    }
                }
                catch
                {
                    // One tile failed - keep going.
                }
            }

            if (disposeImagesAfter)
            {
                foreach (var rep in reps)
                {
                    try { rep?.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Replaces RGB but preserves the original alpha channel for a destination rectangle on a given mip level.
        /// </summary>
        /// <param name="atlas">Atlas to read original alpha from.</param>
        /// <param name="level">Mip level.</param>
        /// <param name="rect">Destination rect for the tile at this mip.</param>
        /// <param name="rgba">Pixel buffer to be written back; its A will be replaced with the atlas' A.</param>
        private static void MergeOriginalAlpha(Texture2D atlas, int level, Rectangle rect, Color[] rgba)
        {
            var gd = atlas.GraphicsDevice;
            var orig = new Color[rect.Width * rect.Height];

            WithNoRenderTargets(gd, () =>
            {
                atlas.GetData(level, rect, orig, 0, orig.Length);
            });

            for (int i = 0; i < rgba.Length; i++)
                rgba[i].A = orig[i].A;
        }

        /// <summary>Returns the near/primary terrain atlas (diffuse+alpha) currently in use.</summary>
        /// <remarks>Fast path reads _diffuseAlpha; falls back to heuristic scan if needed.</remarks>
        private static Texture2D FindTerrainAtlasTexture()
        {
            var bt = BlockTerrain.Instance;
            if (bt == null) return null;

            var field = typeof(BlockTerrain).GetField("_diffuseAlpha",
                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(bt) is Texture2D tex) return tex;

            // Fallback: Scan likely fields.
            foreach (var f in typeof(BlockTerrain).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(Texture2D).IsAssignableFrom(f.FieldType)) continue;
                var name = f.Name.ToLowerInvariant();
                if (name.Contains("diffuse") || name.Contains("alpha") || name.Contains("terrain") || name.Contains("atlas"))
                {
                    tex = f.GetValue(bt) as Texture2D;
                    if (tex != null) return tex;
                }
            }
            return null;
        }

        /// <summary>Returns the mip/distance diffuse atlas if present.</summary>
        /// <remarks>Looks for _mipMapDiffuse; falls back to a name scan if necessary.</remarks>
        private static Texture2D FindTerrainMipDiffuseTexture()
        {
            var bt = BlockTerrain.Instance;
            if (bt == null) return null;

            var f = typeof(BlockTerrain)
                    .GetField("_mipMapDiffuse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(bt) is Texture2D tex) return tex;

            foreach (var fld in typeof(BlockTerrain).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(Texture2D).IsAssignableFrom(fld.FieldType)) continue;
                var n = fld.Name.ToLowerInvariant();
                if (n.Contains("mipmap") && n.Contains("diffuse"))
                {
                    tex = fld.GetValue(bt) as Texture2D;
                    if (tex != null) return tex;
                }
            }
            return null;
        }
        #endregion

        #region Blocks: Exact Face Map (No Heuristics)

        // Exact face->tile mapping derived from BlockType.TileIndices (no guessing).

        /// <summary>
        /// Builds &amp; caches a face map from the engine's own <see cref="BlockType"/> data.
        /// </summary>
        /// <remarks>
        /// Mapping logic (no guesswork):
        /// - Top    = bt[BlockFace.POSY]
        /// - Bottom = bt[BlockFace.NEGY]
        /// - Sides  = {POSX, NEGZ, NEGX, POSZ} (distinct, >= 0)
        /// - All    = union(Sides, Top, Bottom)
        /// Also stores a clone of the raw TileIndices per enum for fast lookups later.
        /// </remarks>
        private static void EnsureFaceMap()
        {
            if (_faceMapByName != null && _tileIndicesByEnum != null) return;

            _tileIndicesByEnum = new Dictionary<BlockTypeEnum, int[]>();
            _faceMapByName = new Dictionary<string, BlockFaceMap>(StringComparer.OrdinalIgnoreCase);

            foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                if (e == BlockTypeEnum.NumberOfBlocks) continue; // Sentinel.

                var bt = BlockType.GetType(e);
                if (bt == null || bt.TileIndices == null || bt.TileIndices.Length < 6)
                    continue;

                // Keep a copy of engine-provided indices for later lookups.
                _tileIndicesByEnum[e] = (int[])bt.TileIndices.Clone();

                // Read faces directly via the indexer / array with BlockFace enum.
                int posY = bt.TileIndices[(int)BlockFace.POSY]; // Top.
                int negY = bt.TileIndices[(int)BlockFace.NEGY]; // Bottom.
                int posX = bt.TileIndices[(int)BlockFace.POSX];
                int negZ = bt.TileIndices[(int)BlockFace.NEGZ];
                int negX = bt.TileIndices[(int)BlockFace.NEGX];
                int posZ = bt.TileIndices[(int)BlockFace.POSZ];

                var sides = new HashSet<int>();
                if (posX >= 0) sides.Add(posX);
                if (negZ >= 0) sides.Add(negZ);
                if (negX >= 0) sides.Add(negX);
                if (posZ >= 0) sides.Add(posZ);

                var all = new HashSet<int>(sides);
                if (posY >= 0) all.Add(posY);
                if (negY >= 0) all.Add(negY);

                var map = new BlockFaceMap
                {
                    Enum = e,
                    Top = (posY >= 0) ? posY : (int?)null,
                    Bottom = (negY >= 0) ? negY : (int?)null,
                    Sides = sides.ToArray(),
                    All = all.ToArray()
                };

                _faceMapByName[e.ToString()] = map; // Key by canonical enum name (e.g., "Grass").
            }

            Log($"FaceMap built with {_faceMapByName.Count} block entries.");
        }

        /// <summary>
        /// Resolve a pack filename block name to its enum (forgiving: ignores '_' and spaces).
        /// </summary>
        /// <param name="blockNameLower">Lowercased pack filename stem for the block.</param>
        /// <param name="e">Resolved <see cref="BlockTypeEnum"/> (if true).</param>
        private static bool TryGetEnumForName(string blockNameLower, out BlockTypeEnum e)
        {
            // 1) Direct.
            if (_faceMapByName.TryGetValue(blockNameLower, out var map))
            {
                e = map.Enum; return true;
            }

            // 2) forgiving compare: Strip underscores/spaces.
            string norm = blockNameLower.Replace("_", "").Replace(" ", "");
            foreach (var kv in _faceMapByName)
            {
                var keyNorm = kv.Key.Replace("_", "").Replace(" ", "");
                if (string.Equals(keyNorm, norm, StringComparison.OrdinalIgnoreCase))
                {
                    e = kv.Value.Enum; return true;
                }
            }

            e = default;
            return false;
        }

        /// <summary>
        /// Yields one or more tile indices for BlockName_face.
        /// </summary>
        /// <param name="blockNameLower">Lowercased, underscore-normalized block name.</param>
        /// <param name="faceLower">One of: "top", "side", "bottom", "all" (default: all).</param>
        /// <remarks>
        /// Pulls from the cached raw TileIndices for the resolved enum to mirror renderer behavior.
        /// </remarks>
        private static IEnumerable<int> ResolveTileIndicesFromFace(string blockNameLower, string faceLower)
        {
            EnsureFaceMap();
            if (!TryGetEnumForName(blockNameLower, out var e)) yield break;

            var idx = _tileIndicesByEnum[e];
            switch (faceLower)
            {
                case "top":
                    {
                        int i = idx[(int)BlockFace.POSY];
                        if (i >= 0) yield return i;
                        break;
                    }
                case "bottom":
                    {
                        int i = idx[(int)BlockFace.NEGY];
                        if (i >= 0) yield return i;
                        break;
                    }
                case "side":
                    {
                        var sides = new[]
                        {
                            idx[(int)BlockFace.POSX],
                            idx[(int)BlockFace.NEGZ],
                            idx[(int)BlockFace.NEGX],
                            idx[(int)BlockFace.POSZ],
                        };
                        foreach (var i in sides.Where(i => i >= 0).Distinct())
                            yield return i;
                        break;
                    }
                case "all":
                default:
                    {
                        var all = new[]
                        {
                            idx[(int)BlockFace.POSX],
                            idx[(int)BlockFace.NEGZ],
                            idx[(int)BlockFace.NEGX],
                            idx[(int)BlockFace.POSZ],
                            idx[(int)BlockFace.POSY], // Top.
                            idx[(int)BlockFace.NEGY], // Bottom.
                        };
                        foreach (var i in all.Where(i => i >= 0).Distinct())
                            yield return i;
                        break;
                    }
            }
        }
        #endregion

        #region Items: Atlas Helpers

        // Inventory icon rectangle helpers, name resolvers, and shared scaling utilities.

        /// <summary>
        /// Returns the global dictionary of item classes from <see cref="InventoryItem"/>.
        /// </summary>
        private static Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> GetAllItemClasses()
        {
            var f = AccessTools.Field(typeof(InventoryItem), "AllItems");
            return f?.GetValue(null) as Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>;
        }

        /// <summary>
        /// Builds authoritative 64x64 rectangles for the small icon atlas.
        /// </summary>
        /// <remarks>
        /// Uses Sprite.SourceRectangle when available; otherwise falls back to grid-by-ID.
        /// </remarks>
        private static Dictionary<InventoryItemIDs, Rectangle> BuildSmallItemRects(
            Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> items, Texture2D smallAtlas)
        {
            var rects = new Dictionary<InventoryItemIDs, Rectangle>();
            int cols = (smallAtlas != null && smallAtlas.Width >= 64) ? Math.Max(1, smallAtlas.Width / 64) : 1;

            var iicType = typeof(InventoryItem).GetNestedType("InventoryItemClass", AccessTools.all);
            var spriteField = iicType != null
                ? AccessTools.GetDeclaredFields(iicType)
                             .FirstOrDefault(f => f.FieldType.Name.IndexOf("Sprite", StringComparison.OrdinalIgnoreCase) >= 0)
                : null;
            var spriteType = spriteField?.FieldType;
            var srcProp = spriteType != null ? AccessTools.Property(spriteType, "SourceRectangle") : null;
            var srcFld = spriteType != null ? AccessTools.Field(spriteType, "_sourceRectangle") : null;

            Rectangle GetRect(InventoryItem.InventoryItemClass cls)
            {
                int idx = (int)cls.ID;
                var fallback = new Rectangle((idx % cols) * 64, (idx / cols) * 64, 64, 64);

                try
                {
                    if (smallAtlas == null || spriteField == null) return fallback;

                    var sprite = spriteField.GetValue(cls);
                    if (sprite == null) return fallback;

                    if (srcProp != null)
                    {
                        var r = (Rectangle)srcProp.GetValue(sprite, null);
                        if (r.Width > 0 && r.Height > 0) return r;
                    }
                    if (srcFld != null)
                    {
                        var r = (Rectangle)srcFld.GetValue(sprite);
                        if (r.Width > 0 && r.Height > 0) return r;
                    }
                }
                catch
                {
                    // Ignored; use fallback.
                }
                return fallback;
            }

            foreach (var kv in items)
                rects[kv.Key] = GetRect(kv.Value);

            return rects;
        }

        /// <summary>
        /// Computes the 128px icon rectangle corresponding to the 64px one.
        /// </summary>
        private static Rectangle RectFrom64On128(
            Dictionary<InventoryItemIDs, Rectangle> smallRects, InventoryItemIDs id,
            Texture2D smallAtlas, Texture2D largeAtlas, int largeCols)
        {
            if (smallAtlas != null && smallRects.TryGetValue(id, out var s))
            {
                float sx = (float)largeAtlas.Width / Math.Max(1, smallAtlas.Width);
                float sy = (float)largeAtlas.Height / Math.Max(1, smallAtlas.Height);
                var r = new Rectangle(
                    (int)Math.Round(s.X * sx),
                    (int)Math.Round(s.Y * sy),
                    (int)Math.Round(s.Width * sx),
                    (int)Math.Round(s.Height * sy));
                r.Width = Math.Min(r.Width, 128);
                r.Height = Math.Min(r.Height, 128);
                return r;
            }
            int idx = (int)id;
            return new Rectangle((idx % largeCols) * 128, (idx / largeCols) * 128, 128, 128);
        }

        internal static class ItemNameResolver
        {
            // Build once: Map normalized enum keys -> ID.
            private static readonly Lazy<Dictionary<string, InventoryItemIDs>> _byKey =
                new Lazy<Dictionary<string, InventoryItemIDs>>(() =>
                {
                    var dict = new Dictionary<string, InventoryItemIDs>(StringComparer.OrdinalIgnoreCase);
                    foreach (InventoryItemIDs v in Enum.GetValues(typeof(InventoryItemIDs)))
                    {
                        var eName = v.ToString();         // EnumName.
                        var key1 = NormalizeKey(eName);   // "enUmNaMe" -> "enumname".
                        var key2 = key1.Replace("_", ""); // "iron_pickaxe" -> "ironpickaxe" (friendly-only fallback).
                        if (!dict.ContainsKey(key1)) dict[key1] = v;
                        if (!dict.ContainsKey(key2)) dict[key2] = v;
                    }
                    return dict;
                });

            // Normalizer compatible with your pack naming: lowercase, non-alnum -> underscore, collapse underscores.
            private static string NormalizeKey(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                s = s.Trim();
                // Strip extension if passed accidentally.
                var dot = s.LastIndexOf('.');
                if (dot >= 0) s = s.Substring(0, dot);

                // Strip a trailing "_model" if present (just in case).
                if (s.EndsWith("_model", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(0, s.Length - "_model".Length);

                // Canonicalize.
                s = Regex.Replace(s, @"[^A-Za-z0-9]+", "_"); // Non-alnum -> underscore.
                s = Regex.Replace(s, @"_+", "_");            // Collapse.
                return s.Trim('_').ToLowerInvariant();
            }

            private static bool TryParseId(string s, out InventoryItemIDs id)
            {
                id = default;
                // Accept 1..4 digit IDs (e.g., "25" or "0025").
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                    Enum.IsDefined(typeof(InventoryItemIDs), n))
                {
                    id = (InventoryItemIDs)n;
                    return true;
                }
                return false;
            }

            private static bool TryParseEnumKey(string key, out InventoryItemIDs id)
            {
                id = default;
                if (string.IsNullOrEmpty(key)) return false;

                var map = _byKey.Value;

                // Match exact normalized key (with underscores).
                if (map.TryGetValue(key, out id)) return true;

                // Match after removing underscores (friendly-only names like "iron_pickaxe").
                var noUnderscore = key.Replace("_", "");
                if (map.TryGetValue(noUnderscore, out id)) return true;

                return false;
            }

            /// <summary>
            /// Resolve stems like:
            ///  - "0025_Iron_Pickaxe"
            ///  - "Iron_Pickaxe"
            ///  - "0025"
            ///  - "Iron_Pickaxe_EnumName"
            ///  - "EnumName"
            /// to an InventoryItemIDs value.
            /// </summary>
            public static bool TryResolveItemID(string stem, out InventoryItemIDs id)
            {
                id = default;
                if (string.IsNullOrWhiteSpace(stem)) return false;

                // Normalize once.
                var norm = NormalizeKey(stem);

                if (string.IsNullOrEmpty(norm))
                    return false;

                // 1) Fast path: Begins with digits -> treat as ID prefix before an underscore.
                //    Examples: "0025", "0025_iron_pickaxe".
                var us = norm.IndexOf('_');
                var head = us >= 0 ? norm.Substring(0, us) : norm;
                if (TryParseId(head, out id))
                    return true;

                // 2) If we have a suffix after an underscore, try that as enum key.
                //    Example: "iron_pickaxe_enumname" (tail token is the enum name).
                if (us >= 0)
                {
                    var tail = norm.Substring(us + 1); // Everything after the first underscore.
                                                       // Also try the last token (after last underscore) to be forgiving:
                    var lastUs = norm.LastIndexOf('_');
                    var last = lastUs > 0 ? norm.Substring(lastUs + 1) : tail;

                    // Try "tail" first, then "last" (covers both Iron_Pickaxe_EnumName and Iron_Pickaxe)
                    if (TryParseEnumKey(tail, out id) || TryParseEnumKey(last, out id))
                        return true;
                }

                // 3) Try whole normalized stem as enum key (covers "EnumName" or "Iron_Pickaxe" matching the enum).
                if (TryParseEnumKey(norm, out id))
                    return true;

                return false;
            }

            // Overload to keep your current call sites happy; 'items' is not needed for this resolver.
            #pragma warning disable IDE0060 // Suppress "Remove unused parameter".
            public static bool TryResolveItemID(string stem, Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> /*unused*/
            items, out InventoryItemIDs id)
                => TryResolveItemID(stem, out id);
            #pragma warning restore IDE0060
        }

        internal static class ItemModelNameResolver
        {
            /// <summary>
            /// Yields candidate file names for a model skin based on the item ID.
            ///
            /// Resolve stems like:
            /// - "0025_Iron_Pickaxe_model.png"
            /// - "Iron_Pickaxe_model.png"
            /// - "Iron_Pickaxe.png"
            /// - "0025.png"
            /// - "Iron_Pickaxe_EnumName_model.png"
            /// - "EnumName_model.png"
            /// - "EnumName.png"
            /// </summary>
            public static IEnumerable<string> EnumerateModelSkinCandidates(string dir, InventoryItemIDs id, string friendlyName)
            {
                var id4 = ((int)id).ToString("0000");
                var name = NormalizeName(friendlyName);

                // Most specific (recommended for variants):
                if (!string.IsNullOrEmpty(name))
                {
                    yield return Path.Combine(dir, $"{id4}_{name}_model.png");
                    yield return Path.Combine(dir, $"{name}_model.png");
                    yield return Path.Combine(dir, $"{name}.png");
                    yield return Path.Combine(dir, $"{id4}.png");
                }

                // Fallback to strict ID-based names (old behavior):
                yield return Path.Combine(dir, $"{id4}_{id}_model.png");
                yield return Path.Combine(dir, $"{id}_model.png");
                yield return Path.Combine(dir, $"{id}.png");             // EnumName.png.
            }
        }

        internal static class ItemModelGeoResolver
        {
            /// <summary>
            /// Yields candidate file names for a model geometry override (XNB Model) based on the item ID.
            ///
            /// Resolve stems like:
            /// - "0025_Iron_Pickaxe_model.xnb"
            /// - "Iron_Pickaxe_model.xnb"
            /// - "Iron_Pickaxe.xnb"
            /// - "0025.xnb"
            /// - "Iron_Pickaxe_EnumName_model.xnb"
            /// - "EnumName_model.xnb"
            /// - "EnumName.xnb"
            /// </summary>
            public static IEnumerable<string> EnumerateModelXnbCandidates(string dir, InventoryItemIDs id, string friendlyName)
            {
                var id4  = ((int)id).ToString("0000");
                var name = NormalizeName(friendlyName);

                // Helper: Yield both flat + subfolder variants for a stem.
                IEnumerable<string> Expand(string dir2, string stem)
                {
                    // Flat.
                    yield return Path.Combine(dir2, stem + ".xnb");

                    // Per-folder (preferred for dependency isolation).
                    yield return Path.Combine(dir2, stem, stem + ".xnb");

                    // Optional convenience name inside the per-folder.
                    yield return Path.Combine(dir2, stem, "Model.xnb");
                }

                // Most specific (recommended for variants):
                if (!string.IsNullOrEmpty(name))
                {
                    foreach (var p in Expand(dir, $"{id4}_{name}_model")) yield return p;
                    foreach (var p in Expand(dir, $"{name}_model"))       yield return p;
                    foreach (var p in Expand(dir, $"{name}"))             yield return p;
                    foreach (var p in Expand(dir, $"{id4}"))              yield return p;
                }

                // Fallback to strict ID/enum-based names:
                foreach (var p in Expand(dir, $"{id4}_{id}_model")) yield return p;
                foreach (var p in Expand(dir, $"{id}_model"))       yield return p;
                foreach (var p in Expand(dir, $"{id}"))             yield return p; // EnumName.xnb.
            }
        }

        /// <summary>
        /// Splits a stem into "is model skin?" and the base stem (removing "_model" suffix if present).
        /// </summary>
        private static (bool isModel, string stem) StripModelSuffix(string stem)
        {
            if (stem.EndsWith("_model", StringComparison.OrdinalIgnoreCase))
                return (true, stem.Substring(0, stem.Length - "_model".Length));
            return (false, stem);
        }

        /// <summary>
        /// Produces a pixel buffer of exact size <paramref name="w"/>x<paramref name="h"/> from a Texture2D.
        /// </summary>
        /// <remarks>Uses a temporary RenderTarget for high-quality scaling if needed.</remarks>

        private static readonly object _gpuLock = new object();

        /// <summary>
        /// Executes <paramref name="work"/> with absolutely no render targets bound.
        /// Restores the previous RT set afterwards.
        /// </summary>
        private static void WithNoRenderTargets(GraphicsDevice gd, Action work)
        {
            lock (_gpuLock)
            {
                var saved = gd.GetRenderTargets();
                try
                {
                    gd.SetRenderTarget(null); // Absolutely no RT bound.
                    work();
                }
                finally
                {
                    if (saved != null && saved.Length > 0) gd.SetRenderTargets(saved);
                    else gd.SetRenderTarget(null);
                }
            }
        }

        // Simple CPU resizer (Point/Nearest). No RTs, no SpriteBatch, no Clear.
        // Nearest-neighbor CPU scaler, no GPU state changes at all.
        private static Color[] ToSizedPixelData(Texture2D src, int w, int h)
        {
            var gd = src.GraphicsDevice;

            // Read the source into CPU memory (safe even while drawing, but we still guard).
            var srcW = src.Width;
            var srcH = src.Height;
            var srcData = new Color[srcW * srcH];

            WithNoRenderTargets(gd, () => src.GetData(srcData)); // No RTs bound while reading.

            if (srcW == w && srcH == h)
                return srcData; // Already the size we need.

            return ScaleNearest(srcData, srcW, srcH, w, h);
        }

        // Super-fast integer math nearest-neighbor (no floats).
        private static Color[] ScaleNearest(Color[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new Color[dw * dh];

            // Fixed-point stepping (16.16).
            int stepX = (sw << 16) / Math.Max(1, dw);
            int stepY = (sh << 16) / Math.Max(1, dh);

            int syFP = 0;
            for (int y = 0; y < dh; y++)
            {
                int sy = (syFP >> 16);
                int rowSrc = sy * sw;

                int sxFP = 0;
                int rowDst = y * dw;

                for (int x = 0; x < dw; x++)
                {
                    int sx = (sxFP >> 16);
                    dst[rowDst + x] = src[rowSrc + sx];
                    sxFP += stepX;
                }
                syFP += stepY;
            }
            return dst;
        }
        #endregion

        #endregion

        #region Sound Packs (Runtime Replacement)

        /// <summary>
        /// Utility helpers for turning MP3 assets in a sound pack into XNA <see cref="SoundEffect"/> instances.
        /// </summary>
        internal static class Mp3Decoder
        {
            /// <summary>
            /// Decode entire MP3 (via NLayer) into 16-bit interleaved PCM suitable for <see cref="SoundEffect"/>.
            /// </summary>
            public static SoundEffect DecodeToSoundEffect(string mp3Path)
            {
                try
                {
                    using (var mp3 = new NLayer.MpegFile(mp3Path))
                    {
                        int sampleRate = Math.Max(8000, Math.Min(48000, mp3.SampleRate));
                        int channels = (mp3.Channels >= 2) ? 2 : 1;   // Clamp to mono/stereo.
                        const int CHUNK = 32 * 1024;

                        var floatBuf = new float[CHUNK * channels];
                        using (var pcm = new MemoryStream(1 << 20))
                        using (var bw = new BinaryWriter(pcm))
                        {

                            int read;
                            while ((read = mp3.ReadSamples(floatBuf, 0, floatBuf.Length)) > 0)
                            {
                                read -= read % channels;              // Keep samples aligned.
                                for (int i = 0; i < read; i++)
                                {
                                    float f = floatBuf[i];
                                    if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                                    short s = (short)(f * 32767f);    // Cast, no Round.
                                    bw.Write(s);
                                }
                            }

                            var bytes = pcm.ToArray();
                            var xnaChannels = (AudioChannels)channels; // Mono or Stereo.
                            return new SoundEffect(bytes, sampleRate, xnaChannels);
                        }
                    }
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// High-level sound-pack manager:
        /// locates replacement audio under Sounds\, caches <see cref="SoundEffect"/>s,
        /// and mirrors 2D, 3D, ambient, and music behavior without touching vanilla cues unless needed.
        /// </summary>
        internal static class ReplacementAudio
        {
            /// <summary>
            /// Cache keyed by full path so multiple cues can share the same on-disk file.
            /// </summary>
            public static readonly Dictionary<string, SoundEffect> _cache =
                new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Cache keyed by cue name (Click, Theme, etc.) for fast runtime lookup.
            /// </summary>
            public static readonly Dictionary<string, SoundEffect> _byName =
                new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Root: !Mods\TexturePacks\<Pack>\Sounds.
            /// </summary>
            internal static Func<string> ResolveRoot = () =>
                Path.Combine(TexturePackManager.PacksRoot,
                             TPConfig.LoadOrCreate().ActivePack ?? "", "Sounds");

            /// <summary>
            /// Lightweight list of "hot" cues that are often used right away in menus / gameplay.
            /// Used for eager preloading so the first click doesn't stutter.
            /// </summary>
            public static readonly string[] HotCues =
            {
                "Click","Error","Award","Popup","Teleport","Reload","BulletHitHuman","thunderBig","craft","dropitem",
                "pickupitem","punch","punchMiss","arrow","AssaultReload","Shotgun","ShotGunReload","CreatureUnearth",
                "HorrorStinger","Fireball","Iceball","DoorClose","DoorOpen","locator","Fuse","LaserGun1","LaserGun2",
                "LaserGun3","LaserGun4","LaserGun5","Beep","SolidTone","RPGLaunch","Alien","GrenadeArm","RocketWhoosh",
                "LightSaber","LightSaberSwing","GroundCrash","ZombieDig","ChainSawIdle","ChainSawSpinning",
                "ChainSawCutting","FootStep","Pick","Place","BulletHitDirt","GunShot1","GunShot2","GunShot3","GunShot4",
                "BulletHitSpray","thunderLow","Sand","leaves","dirt","Skeleton","ZombieCry","ZombieGrowl","Hit","Fall",
                "Douse","DragonScream","Explosion","WingFlap","DragonFall","Freeze","Felguard"
            };

            /// <summary>
            /// Cues that we treat as music/ambience and optionally shadow with <see cref="MusicShadow"/>.
            /// </summary>
            public static readonly string[] MusicCues =
            {
                "Theme","Birds","Crickets","Drips","song1","song2","lostSouls","song3","song4","song5","song6","SpaceTheme"
            };

            /// <summary>
            /// Eagerly preload a small set of high-traffic cues if replacement files exist.
            /// Does nothing for cues with no override file (keeps disk churn minimal).
            /// </summary>
            public static void PreloadSfx()
            {
                foreach (var cue in HotCues)
                {
                    try
                    {
                        // Only try to load if a file exists (avoid disk churn).
                        var root = ResolveRoot();
                        bool exists = File.Exists(Path.Combine(root, cue + ".wav")) ||
                                      File.Exists(Path.Combine(root, cue + ".mp3"));
                        if (!exists) continue;

                        if (!_byName.TryGetValue(cue, out var sfx) || sfx == null || sfx.IsDisposed)
                        {
                            if (TryLoad(cue, out sfx))
                                _byName[cue] = sfx;    // Cache by cue name.
                        }
                    }
                    catch { /* Ignore one-off failures. */ }
                }
            }

            /// <summary>
            /// Lookup helper that first checks the cue-name cache, then falls back to disk load.
            /// </summary>
            private static bool TryGet(string cueName, out SoundEffect sfx)
            {
                if (_byName.TryGetValue(cueName, out sfx) && sfx != null && !sfx.IsDisposed)
                    return true;

                if (TryLoad(cueName, out sfx))
                {
                    _byName[cueName] = sfx;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Play a 2D one-shot replacement if present; returns false if there is no override.
            /// Does not touch or stop the engine's own cue.
            /// </summary>
            public static bool TryPlay2D_IfAvailable(string cueName)
            {
                if (!TryGet(cueName, out var sfx)) return false;
                try { sfx.Play(); return true; } catch { return false; }
            }

            /// <summary>
            /// Play a 3D one-shot replacement if present; returns false if there is no override.
            /// Instances are tracked and pruned in <see cref="Update"/>.
            /// </summary>
            public static bool TryPlay3D_IfAvailable(string cueName, AudioEmitter emitter)
            {
                if (!TryGet(cueName, out var sfx)) return false;
                try
                {
                    var inst = sfx.CreateInstance();
                    inst.IsLooped = false;
                    inst.Apply3D(SoundManager.ActiveListener, emitter);
                    inst.Play();
                    return true;
                }
                catch { return false; }
            }

            /// <summary>
            /// Try to load SFX by cue name from Sounds\cueName.(wav|mp3).
            /// Uses path-based cache to avoid re-decoding files.
            /// </summary>
            private static bool TryLoad(string cueName, out SoundEffect sfx)
            {
                sfx = null;
                var root = ResolveRoot();
                var mp3 = Path.Combine(root, cueName + ".mp3");
                var wav = Path.Combine(root, cueName + ".wav");
                var path = File.Exists(mp3) ? mp3 : (File.Exists(wav) ? wav : null);
                if (path == null) return false;

                if (_cache.TryGetValue(path, out sfx) && sfx != null && !sfx.IsDisposed) return true;

                try
                {
                    if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var fs = File.OpenRead(path))
                        using (var ms = new MemoryStream())
                        {
                            fs.CopyTo(ms);
                            sfx = SoundEffect.FromStream(new MemoryStream(ms.ToArray()));
                        }
                    }
                    else
                    {
                        sfx = Mp3Decoder.DecodeToSoundEffect(path); // The NLayer-based decoder.
                        if (sfx == null) return false;
                    }
                    _cache[path] = sfx;
                    Log($"[SoundPacks] Loaded: {Path.GetFileName(path)}");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[SoundPacks] Failed {Path.GetFileName(path)}: {ex.Message}");
                    try { sfx?.Dispose(); } catch { }
                    sfx = null;
                    return false;
                }
            }

            // ---------- One-shots ----------

            /// <summary>
            /// For 3D one-shots we need an instance that we keep until it finishes; we prune in <see cref="Update"/>.
            /// </summary>
            public static readonly List<SoundEffectInstance> _active3D = new List<SoundEffectInstance>();

            // ---------- Ambience (4 named loops) ----------

            /// <summary>
            /// Ambient loop manager (Birds, Crickets, Drips, lostSouls); mirrors the engine's volume envelopes.
            /// </summary>
            internal static class Ambience
            {
                private static readonly Dictionary<string, SoundEffectInstance> _loops = new Dictionary<string, SoundEffectInstance>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Birds"]     = null,
                    ["Crickets"]  = null,
                    ["Drips"]     = null,
                    ["lostSouls"] = null,
                };

                private static SoundEffectInstance Ensure(string cue)
                {
                    if (_loops[cue] != null && _loops[cue].State != SoundState.Stopped) return _loops[cue];

                    // (Re)create
                    try { _loops[cue]?.Dispose(); } catch { _loops[cue] = null; }
                    if (!TryLoad(cue, out var sfx)) return null;

                    try
                    {
                        var inst = sfx.CreateInstance();
                        inst.IsLooped = true;
                        inst.Volume = 0f;
                        inst.Play();
                        _loops[cue] = inst;
                        return inst;
                    }
                    catch { return null; }
                }

                /// <summary>
                /// Apply the engine's intended ambient volumes (day/night/cave/hell) to our replacement loops.
                /// </summary>
                public static void Apply(float day, float night, float cave, float hell)
                {
                    // Make sure loops exist and then mirror the engine's volume intent.
                    SetVol("Birds", day);
                    SetVol("Crickets", night);
                    SetVol("Drips", cave);
                    SetVol("lostSouls", hell);
                }

                private static void SetVol(string cue, float v)
                {
                    var inst = Ensure(cue);
                    if (inst == null) return;
                    try { inst.Volume = MathHelper.Clamp(v, 0f, 1f); } catch { }
                }

                /// <summary>
                /// Stop and dispose all ambience instances (used on pack switch).
                /// </summary>
                public static void StopAll()
                {
                    foreach (var k in _loops.Keys.ToList())
                    {
                        try { _loops[k]?.Stop(); } catch { }
                        try { _loops[k]?.Dispose(); } catch { }
                        _loops[k] = null;
                    }
                }
            }

            // ---------- Housekeeping ----------

            /// <summary>
            /// Housekeeping: prune stopped 3D one-shots and dispose their instances.
            /// Call from a safe Update-like path.
            /// </summary>
            public static void Update()
            {
                // Prune stopped 3D one-shots.
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

            #region Cue Replacement (2D / 3D / Ambient)

            /// <summary>
            /// Handle full pack switch: stop/clear all replacement audio and reset presence flags.
            /// </summary>
            public static void OnPackSwitched()
            {
                // Stop and dispose all active audio we control.
                try
                {
                    // 3D one-shots.
                    for (int i = _active3D.Count - 1; i >= 0; i--)
                    {
                        try { _active3D[i]?.Stop(); } catch { }
                        try { _active3D[i]?.Dispose(); } catch { }
                    }
                    _active3D.Clear();

                    // Ambience loops (Birds/Crickets/Drips/lostSouls).
                    Ambience.StopAll();

                    // Our music single-loop helper.
                    MusicShadow.Stop();
                }
                catch { /* Best effort. */ }

                // Dispose cached sound effects so file handles are released.
                try
                {
                    foreach (var kvp in _byName.ToArray())
                    {
                        try { kvp.Value?.Dispose(); } catch { }
                    }
                    _byName.Clear();

                    foreach (var kvp in _cache.ToArray())
                    {
                        try { kvp.Value?.Dispose(); } catch { }
                    }
                    _cache.Clear();
                }
                catch { /* Ignore. */ }

                // Clear presence snapshot; will be re-scanned for the new pack when needed.
                try
                {
                    AmbiencePresence.HasBirds = AmbiencePresence.HasCrickets =
                    AmbiencePresence.HasDrips = AmbiencePresence.HasLostSouls = false;
                    // If you keep a bootstrapped flag, reset it:
                    var f = typeof(AmbiencePresence).GetField("_bootstrapped",
                             BindingFlags.NonPublic | BindingFlags.Static);
                    f?.SetValue(null, false);
                }
                catch { }
            }
            #endregion
        }

        /// <summary>
        /// Mirrors the game's music cues with custom songs from a texture pack.
        /// Keeps a parallel looped <see cref="SoundEffectInstance"/> and shadows fades/volume.
        /// </summary>
        internal static class MusicShadow
        {
            private static SoundEffect _sfx;
            private static SoundEffectInstance _inst;
            private static Cue _engineCueRef; // Which cue we're shadowing.
            private static string _cueName;
            private static bool _active;

            // Optional: Reflect private fade fields so we can mirror fades (safe if not found).
            private static readonly FieldInfo _fiFadeFlag =
                AccessTools.Field(typeof(CastleMinerZGame), "_fadeMusic");
            private static readonly FieldInfo _fiFadeTimer =
                AccessTools.Field(typeof(CastleMinerZGame), "_fadeTimer");

            public static bool IsActive => _active && _inst != null;

            /// <summary>
            /// Start shadowing a music cue with a replacement (if present). If already shadowing the same cue,
            /// this just updates the engine cue reference and lets <see cref="Tick"/> handle volume.
            /// </summary>
            public static void Start(string cueName, Cue engineCue)
            {
                // If we're already playing the same song and it's alive, just update target/vol on Tick.
                if (_active && string.Equals(_cueName, cueName, StringComparison.OrdinalIgnoreCase) &&
                    _inst != null && _inst.State == SoundState.Playing)
                {
                    _engineCueRef = engineCue;
                    return;
                }

                Stop(); // Stop any previous replacement.

                // Try to load a replacement (mp3 or wav). If none, stay inactive -> vanilla will play.
                if (!TryLoadReplacement(cueName, out _sfx))
                    return;

                _inst = _sfx.CreateInstance();
                _inst.IsLooped = true;
                _inst.Volume = 0f; // Will be set in Tick().
                _inst.Play();

                _cueName = cueName;
                _engineCueRef = engineCue;
                _active = true;

                Log($"[SoundPacks] Music replacement started: {cueName}");
            }

            /// <summary>
            /// Stop any active shadow music and release associated resources.
            /// </summary>
            public static void Stop()
            {
                try { _inst?.Stop(); } catch { }
                try { _inst?.Dispose(); } catch { }
                _inst = null;

                try { _sfx?.Dispose(); } catch { }
                _sfx = null;

                _engineCueRef = null;
                _cueName = null;
                _active = false;
            }

            /// <summary>
            /// Per-frame volume/fade sync for active shadow music.
            /// Call from the game's update loop after music cue changes have been applied.
            /// </summary>
            public static void Tick(CastleMinerZGame g)
            {
                if (!_active) return;

                // If the engine swapped or killed its cue, stop our replacement immediately.
                var eng = g.MusicCue;
                bool engineChanged = !object.ReferenceEquals(eng, _engineCueRef);
                bool engineDead = (eng == null) || (!eng.IsPlaying && !eng.IsPreparing);

                if (engineChanged || engineDead)
                {
                    Stop();
                    return;
                }

                // Mirror the engine's intended volume (mute + slider).
                float target = GetMusicMuted(g) ? 0f : MathHelper.Clamp(g.PlayerStats.musicVolume, 0f, 1f);

                // If the game is fading music, mirror a simple fade factor (best-effort).
                try
                {
                    if (_fiFadeFlag != null && _fiFadeTimer != null && (bool)(_fiFadeFlag.GetValue(g) ?? false))
                    {
                        var t = (float)(_fiFadeTimer.GetValue(g) ?? 0f);
                        // Vanilla fades for ~1.5s; clamp for safety.
                        float factor = 1f - MathHelper.Clamp(t / 1.5f, 0f, 1f);
                        target *= factor;
                    }
                }
                catch { /* Ignore reflection hiccups. */ }

                try { if (_inst != null) _inst.Volume = target; } catch { }
            }

            /// <summary>
            /// Try to load a replacement for the given music cue from the active pack's Sounds folder.
            /// </summary>
            private static bool TryLoadReplacement(string cueName, out SoundEffect sfx)
            {
                sfx = null;
                var root = TexturePackManager.PacksRoot;
                var pack = TPConfig.LoadOrCreate().ActivePack ?? "";
                var sounds = Path.Combine(root, pack, "Sounds");

                string mp3 = Path.Combine(sounds, cueName + ".mp3");
                string wav = Path.Combine(sounds, cueName + ".wav");

                try
                {
                    if (File.Exists(mp3))
                    {
                        sfx = Mp3Decoder.DecodeToSoundEffect(mp3);
                    }
                    else if (File.Exists(wav))
                    {
                        using (var fs = File.OpenRead(wav))
                            sfx = SoundEffect.FromStream(fs); // PCM/ADPCM WAV only.
                    }
                }
                catch { try { sfx?.Dispose(); } catch { } sfx = null; }

                return sfx != null;
            }

            // Small helpers so you don't repeat the cast everywhere.
            public static bool GetMusicMuted(CastleMinerZGame g)
            {
                var ps = g?.PlayerStats as CastleMinerZPlayerStats;
                return ps?.musicMute ?? false;
            }

            public static float GetMusicVolume(CastleMinerZGame g)
            {
                var ps = g?.PlayerStats as CastleMinerZPlayerStats;
                return MathHelper.Clamp(ps?.musicVolume ?? 1f, 0f, 1f);
            }
        }

        /// <summary>
        /// Tracks which ambience replacement files exist on disk (Birds, Crickets, Drips, lostSouls).
        /// Lazy-bootstrapped the first time a sound-pack query needs this info.
        /// </summary>
        internal static class AmbiencePresence
        {
            private static bool _bootstrapped;
            public static bool HasBirds, HasCrickets, HasDrips, HasLostSouls;

            /// <summary>
            /// Ensure that presence flags have been scanned for the current active pack.
            /// </summary>
            public static void EnsureScanned()
            {
                if (_bootstrapped) return;
                _bootstrapped = true;

                try
                {
                    var root = ReplacementAudio.ResolveRoot();
                    bool Exists(string name) =>
                        File.Exists(Path.Combine(root, name + ".mp3")) ||
                        File.Exists(Path.Combine(root, name + ".wav"));

                    HasBirds = Exists("Birds");
                    HasCrickets = Exists("Crickets");
                    HasDrips = Exists("Drips");
                    HasLostSouls = Exists("lostSouls");
                }
                catch { /* Leave false. */ }
            }

            /// <summary>
            /// True if any custom ambience file is present in the pack.
            /// </summary>
            public static bool Any => HasBirds || HasCrickets || HasDrips || HasLostSouls;
        }
        #endregion

        #region Fonts (TTF -> SpriteFont Packs)

        /// <summary>
        /// TTF/OTF -> SpriteFont builder based on SixLabors types.
        /// Produces XNA-compatible <see cref="SpriteFont"/>s that roughly match vanilla spacing/line-height.
        /// </summary>
        internal static class TtfSpriteFont
        {
            // Resource versions (critical):
            // SixLabors.ImageSharp.Drawing 1.0.0-beta13
            // SixLabors.ImageSharp         1.0.4
            // SixLabors.Fonts              1.0.0-beta15

            /// <summary>
            /// Try to locate a TTF/OTF matching the given <paramref name="stem"/>, and construct a SpriteFont
            /// whose line spacing and spacing closely match the provided vanilla metrics.
            /// </summary>
            public static bool TryLoadMatch(
                GraphicsDevice gd,
                string rootFontsDir,
                string stem,
                int targetLineSpacing,
                float? targetSpacing,
                out SpriteFont font)
            {
                font = null;

                // Locate .ttf/.otf.
                var path = new[] { ".ttf", ".otf" }
                    .Select(ext => Path.Combine(rootFontsDir, stem + ext))
                    .FirstOrDefault(File.Exists);
                if (path == null) return false;

                // Load face (SixLabors.Fonts v1.x).
                var fc = new FontCollection();
                FontFamily fam;
                using (var fs = File.OpenRead(path))
                    fam = fc.Install(fs);

                // Derive a point size that produces approximately the same line height (pixels) as the original SpriteFont.
                // Measure with a probe size, use linear scale:  linePx ≈ k * size  ->  size ≈ targetLine / k.
                const float PROBE_SIZE_PT = 64f;
                var probeFont = fam.CreateFont(PROBE_SIZE_PT, FontStyle.Regular);
                var probeMeasure = TextMeasurer.Measure("Hg", new RendererOptions(probeFont));
                float sizePxF = (probeMeasure.Height > 0f)
                    ? targetLineSpacing * (PROBE_SIZE_PT / probeMeasure.Height)
                    : Math.Max(6f, targetLineSpacing); // fallback

                int sizePx = Math.Max(6, (int)Math.Round(sizePxF));

                // Build the SpriteFont at this computed size, but force the caller's spacing/lineSpacing.
                return TryLoadWithOverrides(gd, rootFontsDir, stem, sizePx, targetSpacing ?? 0f, targetLineSpacing, out font);
            }

            /// <summary>
            /// Low-level construction helper: build a SpriteFont at a specific pixel size with explicit spacing/line-spacing overrides.
            /// </summary>
            public static bool TryLoadWithOverrides(
                GraphicsDevice gd,
                string rootFontsDir,
                string stem,
                int sizePx,
                float spacingOverride,
                int lineSpacingOverride,
                out SpriteFont font)
            {
                font = null;

                // Locate .ttf/.otf.
                var path = new[] { ".ttf", ".otf" }
                    .Select(ext => Path.Combine(rootFontsDir, stem + ext))
                    .FirstOrDefault(File.Exists);
                if (path == null) return false;

                // 1) Load the TTF (SixLabors.Fonts v1.x).
                var fc = new FontCollection();
                FontFamily fam;
                using (var fs = File.OpenRead(path))
                    fam = fc.Install(fs);

                var face = fam.CreateFont(sizePx, FontStyle.Regular);

                // 2) Charset (ASCII by default - extend if needed).
                var chars = DefaultCharset
                    .Distinct()
                    .Where(c => c >= ' ' && c != '\n' && c != '\r' && c != '\t')
                    .ToList();
                if (!chars.Contains('?')) chars.Add('?'); // Default missing char.

                // 3) Measure rough line height and conservative cell size.
                var ro = new RendererOptions(face);
                var measureH = TextMeasurer.Measure("Hg", ro);
                int roughLine = Math.Max(1, (int)Math.Ceiling(measureH.Height));

                const int PAD = 2;
                var wRect = TextMeasurer.Measure("W", ro);
                int cellW = Math.Max(8, (int)Math.Ceiling(wRect.Width) + PAD * 2);
                int cellH = Math.Max(8, roughLine + PAD * 2);

                // 4) Build power-of-two atlas.
                int cols = (int)Math.Ceiling(Math.Sqrt(chars.Count));
                int rows = (int)Math.Ceiling(chars.Count / (float)cols);
                int atlasW = NextPow2(cols * cellW);
                int atlasH = NextPow2(rows * cellH);

                using (var img = new SixLabors.ImageSharp.Image<Rgba32>(atlasW, atlasH, new Rgba32(0, 0, 0, 0)))
                {
                    var glyphRects = new List<Rectangle>(chars.Count);
                    var cropping = new List<Rectangle>(chars.Count);
                    var kernTrip = new List<Vector3>(chars.Count);

                    int i = 0;
                    foreach (var ch in chars)
                    {
                        int gx = (i % cols) * cellW;
                        int gy = (i / cols) * cellH;

                        // Measure single character.
                        var m = TextMeasurer.Measure(ch.ToString(), ro);
                        int gw = Math.Max(0, (int)Math.Ceiling(m.Width));
                        int gh = Math.Max(0, (int)Math.Ceiling(m.Height));

                        // Draw glyph into the cell.
                        if (gw > 0 && gh > 0)
                        {
                            img.Mutate(ctx =>
                            {
                                ctx.DrawText(
                                    ch.ToString(), // Text.
                                    face,          // SixLabors.Fonts.Font.
                                    SixLabors.ImageSharp.Color.White,
                                    new SixLabors.ImageSharp.PointF(gx + PAD, gy + PAD));
                            });
                        }

                        // SpriteFont data.
                        int left = 0;              // Simple bearing approximation.
                        int width = gw;
                        int advance = Math.Max(width, (int)Math.Ceiling(m.Width));
                        int right = Math.Max(0, advance - (left + width));

                        glyphRects.Add(new Rectangle(gx + PAD, gy + PAD, gw, gh));
                        cropping.Add(new Rectangle(left, /*top*/ 0, gw, gh));
                        kernTrip.Add(new Vector3(left, width, right));

                        i++;
                    }

                    // 5) Upload atlas to a Texture2D.
                    Texture2D tex;
                    using (var ms = new MemoryStream())
                    {
                        // Save as PNG to stream and let XNA read it; this avoids API differences like CopyPixelDataTo(Span<>).
                        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                        ms.Position = 0;
                        tex = Texture2D.FromStream(gd, ms);
                    }

                    // IMPORTANT: Do NOT call tex.SetData(...) again here (you'll wipe the pixels).
                    // The PNG is already in the texture.

                    // 6) Construct SpriteFont using the 8-arg ctor (XNA/MonoGame).
                    try
                    {
                        var spriteFontType = typeof(SpriteFont);
                        var ctor = spriteFontType
                            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .First(ci => ci.GetParameters().Length == 8);

                        char? defaultChar = '?';

                        font = (SpriteFont)ctor.Invoke(new object[]
                        {
                            tex,                // Texture2D.
                            glyphRects,         // List<Rectangle> glyphs.
                            cropping,           // List<Rectangle> cropping.
                            chars,              // List<char>.
                            lineSpacingOverride,// int lineSpacing (MATCH original).
                            spacingOverride,    // float spacing   (MATCH original).
                            kernTrip,           // List<Vector3> kerning.
                            defaultChar         // char? default character.
                        });

                        return true;
                    }
                    catch
                    {
                        try { tex.Dispose(); } catch { }
                        return false;
                    }
                }
            }

            // Helpers already in your class; included here for completeness.
            private static int NextPow2(int n)
            {
                n = Math.Max(8, n - 1);
                n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
                return n + 1;
            }

            private const string DefaultCharset =
                " !\"#$%&'()*+,-./0123456789:;<=>?@" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_'" +
                "abcdefghijklmnopqrstuvwxyz{|}~";
        }

        /// <summary>
        /// Font pack manager:
        /// discovers SpriteFont fields, captures baselines, and swaps them to TTF/OTF-driven replacements.
        /// </summary>
        internal static class FontPacks
        {
            // !Mods/TexturePacks/<Pack>/Fonts.
            private static string Root =>
                Path.Combine(TexturePackManager.PacksRoot,
                    TPConfig.LoadOrCreate().ActivePack ?? "", "Fonts");

            /// <summary>
            /// Apply replacements to all discovered SpriteFont fields.
            /// </summary>
            public static void ApplyAll(GraphicsDevice gd)
            {
                if (!Directory.Exists(Root)) return;

                var fontsRoot = Path.Combine(TexturePackManager.PacksRoot,
                             TPConfig.LoadOrCreate().ActivePack ?? "", "Fonts");

                int replaced = 0;
                foreach (var hit in EnumerateSpriteFontFields())
                {
                    var (owner, field) = hit;
                    var name = field.Name; // We use the field's name as the lookup stem.

                    var old = field.GetValue(owner) as SpriteFont;
                    int targetLine = old?.LineSpacing ?? 18;
                    float targetSpacing = old?.Spacing ?? 0f;

                    if (TtfSpriteFont.TryLoadMatch(gd, fontsRoot, field.Name, targetLine, targetSpacing, out var ttfFont))
                    {
                        // NOTE: Track atlas so we can retire it later
                        // In TryLoadWithOverrides and TryLoadAngelCodeFont you should already call:
                        // FontPacks.TrackCreated(font, texOrAtlas);
                        ForceSetFont(field, owner, ttfFont);
                        replaced++;
                        continue;
                    }

                }

                if (replaced == 0)
                {
                    // Log available SpriteFont field names to help pack authors drop correct files.
                    var names = EnumerateSpriteFontFields().Select(t => $"{t.field.DeclaringType.Name}.{t.field.Name}").Distinct().ToList();
                    if (names.Count > 0)
                        Log($"[Fonts] No replacements found. Available SpriteFont fields: {string.Join(", ", names)}.");
                }
                else
                {
                    Log($"[Fonts] Applied {replaced} font replacement(s).");
                }
            }

            /// <summary>
            /// Finds static and instance SpriteFont fields on common singletons (game, HUD, and static caches).
            /// </summary>
            private static IEnumerable<(object owner, FieldInfo field)> EnumerateSpriteFontFields()
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                // 1) CastleMinerZGame singleton.
                var game = CastleMinerZGame.Instance;
                if (game != null)
                {
                    foreach (var f in game.GetType().GetFields(flags))
                        if (typeof(SpriteFont).IsAssignableFrom(f.FieldType))
                            yield return (game, f);
                }

                // 2) Known UI/screen singletons if exposed (optional; extend as needed)
                // e.g., InGameHUD, FrontEndScreen instances hanging off the game:
                var hudField = game?.GetType().GetField("_hud", flags);
                var hud = hudField?.GetValue(game);
                if (hud != null)
                {
                    foreach (var f in hud.GetType().GetFields(flags))
                        if (typeof(SpriteFont).IsAssignableFrom(f.FieldType))
                            yield return (hud, f);
                }

                // 3) Also check static font caches in assemblies (rare but cheap).
                foreach (var t in typeof(CastleMinerZGame).Assembly.GetTypes())
                {
                    if (t == null) continue;
                    foreach (var f in t.GetFields(flags))
                        if (f.IsStatic && typeof(SpriteFont).IsAssignableFrom(f.FieldType))
                            yield return (null, f);
                }
            }

            #region Replacement

            private sealed class BaselineEntry
            {
                public WeakReference Owner; // Null for static fields.
                public FieldInfo Field;
                public SpriteFont Original; // Vanilla SpriteFont reference.
            }

            private static bool _baselineCaptured;
            private static readonly List<BaselineEntry> _baseline = new List<BaselineEntry>();

            /// <summary>
            /// Capture vanilla SpriteFont references once so that a pack switch can restore them.
            /// </summary>
            public static void CaptureBaselineIfNeeded()
            {
                if (_baselineCaptured) return;
                foreach (var hit in EnumerateSpriteFontFields())
                {
                    var owner = hit.owner;
                    var field = hit.field;
                    var orig = field.GetValue(owner) as SpriteFont;

                    var wr = (owner == null) ? null : new WeakReference(owner);
                    _baseline.Add(new BaselineEntry { Owner = wr, Field = field, Original = orig });
                }
                _baselineCaptured = true;
            }

            /// <summary>
            /// Restore all SpriteFont fields back to their baseline vanilla references.
            /// </summary>
            public static void RestoreVanillaFonts()
            {
                foreach (var b in _baseline)
                {
                    object owner = (b.Owner == null) ? null : (b.Owner.IsAlive ? b.Owner.Target : null);
                    try { ForceSetFont(b.Field, owner, b.Original); } catch { }
                }
            }

            /// <summary>
            /// Forcefully set a SpriteFont field, even if it's declared readonly (InitOnly).
            /// </summary>
            private static void ForceSetFont(FieldInfo fi, object owner, SpriteFont value)
            {
                if (fi == null) return;
                try
                {
                    // If readonly, strip the InitOnly bit on the backing RuntimeFieldInfo.
                    if (fi.IsInitOnly)
                    {
                        var attrFi = fi.GetType().GetField("m_fieldAttributes",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (attrFi != null)
                        {
                            var attrs = (FieldAttributes)attrFi.GetValue(fi);
                            attrFi.SetValue(fi, attrs & ~FieldAttributes.InitOnly);
                        }
                    }
                    fi.SetValue(owner, value);
                }
                catch { /* Best effort; some runtimes may block this. */ }
            }

            /// <summary>
            /// Rebind all major menu/HUD UI trees so they pick up newly installed fonts.
            /// </summary>
            public static void RebindUI(CastleMinerZGame gm)
            {
                try
                {
                    // Front-end and menus.
                    var feField = gm.GetType().GetField("_frontEndScreen", BindingFlags.NonPublic | BindingFlags.Instance);
                    var fe = feField?.GetValue(gm) as FrontEndScreen;
                    if (fe != null) MenuFontRebinder.RebindAllMenusOn(fe, gm);

                    // Whole UI trees (menus/HUD/options).
                    UIOptionsFontRebinder.RebindTree(fe, gm);
                    var hudField = gm.GetType().GetField("_hud", BindingFlags.NonPublic | BindingFlags.Instance);
                    var hud = hudField?.GetValue(gm);
                    UIOptionsFontRebinder.RebindTree(hud, gm);
                }
                catch { }
            }

            /// <summary>
            /// One-stop call on pack switch for all font-related state:
            /// restore vanilla, apply new pack fonts, and rebind UI trees.
            /// </summary>
            public static void OnPackSwitched(GraphicsDevice gd, CastleMinerZGame gm)
            {
                // 1) Capture baseline once (first run only).
                FontPacks.CaptureBaselineIfNeeded();

                // 2) Restore all SpriteFont fields to VANILLA references.
                FontPacks.RestoreVanillaFonts();     // Just reassigns original refs; no disposal.

                // 3) Apply replacements for the new pack.
                FontPacks.ApplyAll(gd);              // Builds & assigns new SpriteFonts (TTF/OTF/BMFont).

                // 4) Rebind menus/HUD so private caches pick up the new fonts.
                FontPacks.RebindUI(gm);
            }
            #endregion
        }

        /// <summary>
        /// Simple helper to rebind the FrontEndScreen's cached fonts to the game's current large font.
        /// </summary>
        public static class FrontEndFontRebinder
        {
            private static readonly FieldInfo FiLarge = AccessTools.Field(typeof(FrontEndScreen), "_largeFont");

            public static void Rebind(FrontEndScreen fe, CastleMinerZGame gm)
            {
                if (fe == null || gm == null || FiLarge == null || gm._largeFont == null) return;
                ForceSetReadonly(FiLarge, fe, gm._largeFont);
            }

            private static void ForceSetReadonly(FieldInfo fi, object owner, object value)
            {
                try
                {
                    if (fi.IsInitOnly)
                    {
                        var attrFi = fi.GetType().GetField("m_fieldAttributes",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (attrFi != null)
                        {
                            var attrs = (FieldAttributes)attrFi.GetValue(fi);
                            attrFi.SetValue(fi, attrs & ~FieldAttributes.InitOnly);
                        }
                    }
                    fi.SetValue(owner, value);
                }
                catch { /* Best-effort. */ }
            }
        }

        /// <summary>
        /// Front-end menu-specific font rebinder that walks all MenuScreen fields on FrontEndScreen.
        /// </summary>
        public static class MenuFontRebinder
        {
            private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            /// <summary>
            /// Rebind all menus hanging off a <see cref="FrontEndScreen"/> to use the game's current fonts.
            /// </summary>
            public static void RebindAllMenusOn(FrontEndScreen screen, CastleMinerZGame game)
            {
                // 1) Swap the cached big font the FE screen holds so anything that reads it gets the replacement.
                var feLargeFi = AccessTools.Field(typeof(FrontEndScreen), "_largeFont");
                feLargeFi?.SetValue(screen, game._largeFont);

                // 2) For every field that is (or derives from) MenuScreen, retarget its fonts.
                foreach (var fi in typeof(FrontEndScreen).GetFields(All))
                {
                    var ft = fi.FieldType;
                    if (ft == null) continue;

                    // Works even if the field type itself is internal.
                    if (!typeof(MenuScreen).IsAssignableFrom(ft)) continue;

                    var menuObj = fi.GetValue(screen);
                    if (menuObj is MenuScreen menu)
                    {
                        RebindMenu(menu, game);
                        continue;
                    }

                    // If the runtime type isn't directly visible, use reflection to do the same work.
                    if (menuObj != null)
                        RebindMenuViaReflection(menuObj, game);
                }
            }

            /// <summary>
            /// Rebind a strongly-typed <see cref="MenuScreen"/> to pack fonts (menu & description text).
            /// </summary>
            public static void RebindMenu(MenuScreen menu, CastleMinerZGame game)
            {
                // Base menu text.
                menu.Font = game._largeFont;

                // Items.
                if (menu.MenuItems != null)
                {
                    foreach (var mi in menu.MenuItems)
                        if (mi != null)
                            mi.Font = game._largeFont;
                }

                // Special case: GameModeMenu has a private TextRegionElement "_descriptionText"
                // that was created with _medLargeFont. If it exists, retarget its Font.
                var dteFi = menu.GetType().GetField("_descriptionText",
                             BindingFlags.NonPublic | BindingFlags.Instance);
                var dte = dteFi?.GetValue(menu);
                if (dte != null)
                {
                    var fontProp = dte.GetType().GetProperty("Font",
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    fontProp?.SetValue(dte, game._medLargeFont, null);
                }
            }

            /// <summary>
            /// Same as <see cref="RebindMenu"/> but for internal MenuScreen subclasses we can't reference directly.
            /// </summary>
            private static void RebindMenuViaReflection(object menuObj, CastleMinerZGame game)
            {
                var t = menuObj.GetType();
                // menu.Font.
                var fontProp = t.GetProperty("Font", BindingFlags.Public | BindingFlags.Instance);
                fontProp?.SetValue(menuObj, game._largeFont, null);

                // foreach (var mi in menu.MenuItems) mi.Font = ...
                var itemsProp = t.GetProperty("MenuItems", BindingFlags.Public | BindingFlags.Instance);
                if (itemsProp?.GetValue(menuObj) is System.Collections.IEnumerable items)
                {
                    foreach (var mi in items)
                    {
                        if (mi == null) continue;
                        var miFontProp = mi.GetType().GetProperty("Font", BindingFlags.Public | BindingFlags.Instance);
                        miFontProp?.SetValue(mi, game._largeFont, null);
                    }
                }

                // private _descriptionText.Font = _medLargeFont (if present).
                var dteFi = t.GetField("_descriptionText", BindingFlags.NonPublic | BindingFlags.Instance);
                var dte = dteFi?.GetValue(menuObj);
                if (dte != null)
                {
                    var dteFontProp = dte.GetType().GetProperty("Font", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    dteFontProp?.SetValue(dte, game._medLargeFont, null);
                }
            }
        }

        /// <summary>
        /// General-purpose DNA UI font rebinder. Walks UI trees and swaps any SpriteFont field/property
        /// to the nearest pack font based on line spacing.
        /// </summary>
        public static class UIOptionsFontRebinder
        {
            private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            // Only these are safe UI containers in DNA UI:
            private static readonly string[] ChildProps = { "Controls", "Children", "Tabs", "MenuItems", "Items" };

            // Bump this when you switch packs / re-apply fonts.
            public static int CurrentEpoch { get; private set; }
            public static void BumpEpoch() => CurrentEpoch++;

            // Track which UI roots we already rebound for the current epoch.
            private sealed class Stamp { public int Epoch; }
            private static readonly ConditionalWeakTable<object, Stamp> _stamps = new ConditionalWeakTable<object, Stamp>();

            /// <summary>
            /// Call this instead of <see cref="RebindTree"/> when you might be in a hot path (e.g., OnDraw).
            /// It avoids re-walking the tree if the epoch is unchanged.
            /// </summary>
            public static void RebindTreeIfDirty(object root, CastleMinerZGame gm)
            {
                if (root == null) return;
                var s = _stamps.GetOrCreateValue(root);
                if (s.Epoch == CurrentEpoch) return; // Fast no-op most frames.
                RebindTree(root, gm);                // Your existing safe traversal.
                s.Epoch = CurrentEpoch;
            }

            /// <summary>
            /// Full tree rebind for a given UI root (menus, HUD, etc.).
            /// </summary>
            public static void RebindTree(object root, CastleMinerZGame gm)
            {
                if (root == null) return;
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                RebindNode(root, gm, seen);
            }

            private static void RebindNode(object node, CastleMinerZGame gm, HashSet<object> seen)
            {
                if (node == null) return;
                var t  = node.GetType();
                var ns = t.Namespace ?? "";
                // Only touch DNA UI namespaces.
                if (!(ns.StartsWith("DNA.Drawing.UI") || ns.StartsWith("UI"))) return;
                if (!seen.Add(node)) return;

                // Fix private caches first for ControlsTab (so new controls/dialogs use pack fonts).
                if (node is ControlsTab)
                {
                    BumpFieldToNearest(node, "_controlsFont", gm);
                    BumpFieldToNearest(node, "_menuButtonFont", gm);
                }

                // Properties commonly used for fonts.
                BumpPropertyToNearest(node, "Font", gm);
                BumpPropertyToNearest(node, "TextFont", gm);

                // Replace any private SpriteFont fields (e.g., TextControl._font).
                foreach (var fi in t.GetFields(All))
                {
                    if (!typeof(SpriteFont).IsAssignableFrom(fi.FieldType)) continue;
                    try
                    {
                        var cur = fi.GetValue(node) as SpriteFont;
                        var repl = ChooseNearest(gm, cur?.LineSpacing ?? 18);
                        if (repl != null && !ReferenceEquals(cur, repl))
                            fi.SetValue(node, repl);
                    }
                    catch { }
                }

                // Recurse into SAFE child collections only (by property name).
                foreach (var propName in ChildProps)
                {
                    try
                    {
                        var pi = t.GetProperty(propName, All);
                        if (pi == null) continue;
                        if (!(pi.GetValue(node, null) is IEnumerable enumerable)) continue;

                        // Snapshot to avoid concurrent mutations.
                        var copy = new List<object>();
                        foreach (var item in enumerable)
                        {
                            if (item != null) copy.Add(item);
                        }
                        foreach (var child in copy)
                            RebindNode(child, gm, seen);
                    }
                    catch { }
                }
            }

            private static void BumpPropertyToNearest(object obj, string propName, CastleMinerZGame gm)
            {
                try
                {
                    var pi = obj.GetType().GetProperty(propName, All);
                    if (pi == null || !pi.CanWrite || !typeof(SpriteFont).IsAssignableFrom(pi.PropertyType)) return;
                    var cur = pi.GetValue(obj, null) as SpriteFont;
                    var repl = ChooseNearest(gm, cur?.LineSpacing ?? 18);
                    if (repl != null && !ReferenceEquals(cur, repl))
                        pi.SetValue(obj, repl, null);
                }
                catch { }
            }

            private static void BumpFieldToNearest(object obj, string fieldName, CastleMinerZGame gm)
            {
                try
                {
                    var fi = obj.GetType().GetField(fieldName, All);
                    if (fi == null || !typeof(SpriteFont).IsAssignableFrom(fi.FieldType)) return;
                    var cur = fi.GetValue(obj) as SpriteFont;
                    var repl = ChooseNearest(gm, cur?.LineSpacing ?? 18);
                    if (repl != null && !ReferenceEquals(cur, repl))
                        fi.SetValue(obj, repl);
                }
                catch { }
            }

            private static SpriteFont ChooseNearest(CastleMinerZGame g, int desiredLineSpacing)
            {
                var pool = new List<SpriteFont>(12);
                Add(pool, g?._smallFont); Add(pool, g?._medFont); Add(pool, g?._medLargeFont); Add(pool, g?._largeFont);
                Add(pool, g?._systemFont); Add(pool, g?._consoleFont);
                Add(pool, g?._myriadSmall); Add(pool, g?._myriadMed); Add(pool, g?._myriadLarge);
                if (pool.Count == 0) return null;

                SpriteFont best = pool[0]; int bestD = Math.Abs(best.LineSpacing - desiredLineSpacing);
                for (int i = 1; i < pool.Count; i++)
                {
                    var f = pool[i]; int d = Math.Abs(f.LineSpacing - desiredLineSpacing);
                    if (d < bestD) { best = f; bestD = d; }
                }
                return best;
            }

            private static void Add(List<SpriteFont> list, SpriteFont f) { if (f != null && !list.Contains(f)) list.Add(f); }

            private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
            {
                public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
                public new bool Equals(object x, object y) => ReferenceEquals(x, y);
                public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// Small helper utilities for pushing pack fonts into arbitrary UI controls by reflection.
        /// </summary>
        public static class UIFontUtil
        {
            private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            /// <summary>
            /// If a control exposes a public/protected Font property of type <see cref="SpriteFont"/>,
            /// assign the given font.
            /// </summary>
            public static void SetFontIfPresent(object control, SpriteFont font)
            {
                if (control == null || font == null) return;
                var pi = control.GetType().GetProperty("Font", All);
                if (pi != null && typeof(SpriteFont).IsAssignableFrom(pi.PropertyType))
                    pi.SetValue(control, font, null);
            }

            /// <summary>
            /// Convenience wrapper that reads a field (by name) and applies <see cref="SetFontIfPresent"/> to it.
            /// </summary>
            public static void SetFontByField(object owner, string fieldName, SpriteFont font)
            {
                if (owner == null || font == null) return;
                var fi = AccessTools.Field(owner.GetType(), fieldName);
                if (fi == null) return;
                var ctrl = fi.GetValue(owner);
                SetFontIfPresent(ctrl, font);
            }
        }
        #endregion

        #region Splash Image Helper (Loading, Menu, Inventory, HUD)

        /// <summary>
        /// Utility for loading single splash textures (MenuBack, Logo, DialogBack, Load, etc.)
        /// from the active pack, with simple "Stem + extension" matching.
        /// </summary>
        internal static class SplashTextures
        {
            // Root to the active pack
            private static string PackRoot =>
                Path.Combine(TexturePackManager.PacksRoot, TPConfig.LoadOrCreate().ActivePack ?? "");

            /// <summary>
            /// Try to load a texture from a subfolder under the active pack.
            /// Looks for exact <paramref name="stem"/> match only. Returns true on success.
            /// </summary>
            public static bool TryLoadExact(GraphicsDevice gd, string subfolder, string stem, out Texture2D tex)
            {
                tex = null;
                try
                {
                    var dir = Path.Combine(PackRoot, subfolder);
                    if (!Directory.Exists(dir)) return false;

                    // Preferred filename: Exactly "<stem>.<ext>".
                    string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
                    string path = exts
                        .Select(ext => Path.Combine(dir, stem + ext))
                        .FirstOrDefault(File.Exists);

                    if (path == null) return false;

                    using (var s = File.OpenRead(path))
                        tex = Texture2D.FromStream(gd, s);

                    // Log($"Loaded texture (exact): {subfolder}/{Path.GetFileName(path)}.");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"Failed to load exact {subfolder}/{stem}: {ex.Message}.");
                    try { tex?.Dispose(); } catch { }
                    tex = null;
                    return false;
                }
            }
        }

        #region Pack Replacement: Screens

        /// <summary>
        /// Replacement manager for menu/logo/dialog/load "screens" textures.
        /// Captures vanilla references once and swaps them with pack assets where available.
        /// </summary>
        internal static class SplashPacks
        {
            // Track only textures we created so we can retire them; never dispose vanilla.
            private static readonly HashSet<Texture2D> _ours = new HashSet<Texture2D>();

            // Baseline (vanilla) references - captured once.
            private static bool _baselineCaptured;
            private static Texture2D _baseMenuBack;
            private static Sprite _baseLogo;
            private static Texture2D _baseDialogBack;
            private static Texture2D _baseLoadImage;

            // Fields/properties we touch.
            private static readonly FieldInfo FI_Game_MenuBackdrop =
                AccessTools.Field(typeof(CastleMinerZGame), "MenuBackdrop");
            private static readonly FieldInfo FI_LoadScreen_Image =
                AccessTools.Field(typeof(LoadScreen), "_image");

            public static Texture2D BaseLoadImage => _baseLoadImage;

            // Pack root for "Screens" folder.
            private static string ScreensRoot =>
                Path.Combine(TexturePackManager.PacksRoot, TPConfig.LoadOrCreate().ActivePack ?? "", "Screens");

            /// <summary>
            /// Apply screen replacements for the active pack and mark relevant patches as already swapped.
            /// </summary>
            public static void OnPackSwitched(GraphicsDevice gd, CastleMinerZGame gm)
            {
                if (gd == null || gm == null) return;

                CaptureBaselineIfNeeded(gm);

                // --- MENU BACKDROP (Texture2D field on game) ---
                var curMenu = FI_Game_MenuBackdrop?.GetValue(gm) as Texture2D;
                var newMenu = LoadExact(gd, "MenuBack");            // Null if not provided by pack.
                SwapFieldTexture(FI_Game_MenuBackdrop, gm, curMenu,
                                 newMenu ?? _baseMenuBack);

                // --- LOGO (Sprite property on DNAGame) ---
                var curLogo = gm.Logo;
                var curLogoTex = curLogo?.Texture;
                var newLogoTex = LoadExact(gd, "Logo");             // Null if not provided.
                if (newLogoTex != null)
                {
                    gm.Logo = new Sprite(newLogoTex, new Rectangle(0, 0, newLogoTex.Width, newLogoTex.Height));
                    RetireIfOurs(curLogoTex);
                    MarkOurs(newLogoTex);
                }
                else
                {
                    // Restore vanilla sprite if not already.
                    if (!ReferenceEquals(curLogo, _baseLogo) && _baseLogo != null)
                    {
                        gm.Logo = _baseLogo;
                        RetireIfOurs(curLogoTex);
                    }
                }

                // --- DIALOG BACK (Texture2D property on DNAGame) ---
                var curDlg = gm.DialogScreenImage;
                var newDlg = LoadExact(gd, "DialogBack");
                if (newDlg != null)
                {
                    gm.DialogScreenImage = newDlg;
                    RetireIfOurs(curDlg);
                    MarkOurs(newDlg);
                }
                else
                {
                    if (!ReferenceEquals(curDlg, _baseDialogBack) && _baseDialogBack != null)
                    {
                        gm.DialogScreenImage = _baseDialogBack;
                        RetireIfOurs(curDlg);
                    }
                }

                // Let the LoadScreen patch try again next time a LoadScreen instance appears.
                // Prevent menu/logo/dialog patches from re-doing work (we just did it centrally).
                TrySetPatchFlag(typeof(GamePatches.Patch_LoadScreen_ImageFromPack), "loadScreenSplash_swapped", false);
                TrySetPatchFlag(typeof(GamePatches.Patch_MenuBackdrop_FromPack),    "menuBack_swapped",         true);
                TrySetPatchFlag(typeof(GamePatches.Patch_Logo_FromPack),            "logo_swapped",             true);
                TrySetPatchFlag(typeof(GamePatches.Patch_Logo_FromPack),            "dialogBack_swapped",       true);
            }

            private static void CaptureBaselineIfNeeded(CastleMinerZGame gm)
            {
                if (_baselineCaptured) return;

                _baseMenuBack = FI_Game_MenuBackdrop?.GetValue(gm) as Texture2D;
                _baseLogo = gm.Logo;                    // Sprite.
                _baseDialogBack = gm.DialogScreenImage; // Texture2D.
                _baselineCaptured = true;
            }

            // Load Screens/<stem>.(png|jpg|jpeg|bmp|tga); return null if nothing present.
            private static Texture2D LoadExact(GraphicsDevice gd, string stem)
            {
                try
                {
                    if (!Directory.Exists(ScreensRoot)) return null;
                    string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
                    string path = exts.Select(ext => Path.Combine(ScreensRoot, stem + ext))
                                      .FirstOrDefault(File.Exists);
                    if (path == null) return null;
                    using (var s = File.OpenRead(path))
                        return Texture2D.FromStream(gd, s);
                }
                catch { return null; }
            }

            // Swap a Texture2D field safely (retire old if it was ours).
            private static void SwapFieldTexture(FieldInfo fi, object owner, Texture2D current, Texture2D next)
            {
                if (fi == null) return;
                if (ReferenceEquals(current, next)) return;

                fi.SetValue(owner, next);
                RetireIfOurs(current);
                if (next != null && !BaselineCapturedTexture(next))
                    MarkOurs(next);
            }

            private static void RetireIfOurs(Texture2D tex)
            {
                if (tex == null) return;
                if (_ours.Remove(tex))
                    GpuRetireQueue.Enqueue(tex); // Dispose after Draw.
            }

            private static void MarkOurs(Texture2D tex)
            {
                if (tex != null) _ours.Add(tex);
            }

            // Treat known baselines as not ours.
            private static bool BaselineCapturedTexture(Texture2D tex) =>
                ReferenceEquals(tex, _baseMenuBack) || ReferenceEquals(tex, _baseDialogBack);

            private static void TrySetPatchFlag(Type patchType, string fieldName, bool value)
            {
                try { AccessTools.Field(patchType, fieldName)?.SetValue(null, value); } catch { }
            }

            /// <summary>
            /// Capture baseline screen textures from the running game (menu back, logo, dialog back).
            /// </summary>
            public static void CaptureBaselineFromGame(CastleMinerZGame gm)
            {
                if (_baselineCaptured || gm == null) return;
                _baseMenuBack = FI_Game_MenuBackdrop?.GetValue(gm) as Texture2D;
                _baseLogo = gm.Logo;
                _baseDialogBack = gm.DialogScreenImage;
                _baselineCaptured = true;
            }

            /// <summary>
            /// Capture the default load screen image for later restoration.
            /// </summary>
            public static void CaptureLoadScreenBaseline(LoadScreen ls)
            {
                if (_baseLoadImage != null || ls == null || FI_LoadScreen_Image == null) return;
                _baseLoadImage = FI_LoadScreen_Image.GetValue(ls) as Texture2D;
            }

            // Central swap helpers so we always retire old custom textures safely (after Draw).
            public static void ReplaceMenuBackdrop(CastleMinerZGame gm, Texture2D next)
            {
                var cur = FI_Game_MenuBackdrop?.GetValue(gm) as Texture2D;
                if (!ReferenceEquals(cur, next))
                {
                    FI_Game_MenuBackdrop?.SetValue(gm, next);
                    RetireIfOurs(cur);
                    MarkIfOurs(next, _baseMenuBack);
                }
            }

            public static void ReplaceLogo(CastleMinerZGame gm, Texture2D nextTex)
            {
                var curSprite = gm.Logo;
                var curTex = curSprite?.Texture;
                var newSprite = (nextTex != null) ? new Sprite(nextTex, new Rectangle(0, 0, nextTex.Width, nextTex.Height)) : null;

                if (newSprite != null)
                {
                    gm.Logo = newSprite;
                    RetireIfOurs(curTex);
                    MarkIfOurs(nextTex, _baseLogo?.Texture);
                }
                else if (!ReferenceEquals(curSprite, _baseLogo) && _baseLogo != null)
                {
                    gm.Logo = _baseLogo;
                    RetireIfOurs(curTex);
                }
            }

            public static void ReplaceDialogBack(CastleMinerZGame gm, Texture2D next)
            {
                var cur = gm.DialogScreenImage;
                if (!ReferenceEquals(cur, next))
                {
                    gm.DialogScreenImage = next ?? _baseDialogBack;
                    RetireIfOurs(cur);
                    if (next != null) MarkIfOurs(next, _baseDialogBack);
                }
            }

            public static void ReplaceLoadScreenImage(LoadScreen ls, Texture2D next)
            {
                if (ls == null || FI_LoadScreen_Image == null) return;
                var cur = FI_LoadScreen_Image.GetValue(ls) as Texture2D;
                if (!ReferenceEquals(cur, next))
                {
                    FI_LoadScreen_Image.SetValue(ls, next);
                    RetireIfOurs(cur);
                    MarkIfOurs(next, _baseLoadImage);
                }
            }

            private static void MarkIfOurs(Texture2D tex, Texture2D vanillaRef)
            {
                if (tex == null) return;
                if (!ReferenceEquals(tex, vanillaRef))
                    _ours.Add(tex);
            }
        }
        #endregion

        #region Pack Replacement: Inventory Sprites & HUD Sprites

        /// <summary>
        /// Replacement manager for inventory/HUD sprites (Sprite fields), with per-owner tracking and epoch gating.
        /// </summary>
        internal static class UISpritePacks
        {
            private sealed class Tracked
            {
                public WeakReference Owner;  // The screen/ HUD instance.
                public FieldInfo Field;      // Sprite field.
                public Sprite Vanilla;       // Original Sprite (engine-owned); do NOT dispose.
                public int LastEpochApplied; // To avoid re-assigning every frame.
            }

            // Epoch: Increment on pack switch; anything with stale epoch gets re-evaluated.
            private static int _epoch = 1;
            public static void BumpEpoch() => System.Threading.Interlocked.Increment(ref _epoch);

            // Track per (owner instance, field).
            private static readonly List<Tracked> _tracked = new List<Tracked>();

            // We own these textures; retire on pack switch.
            private static readonly HashSet<Texture2D> _ours = new HashSet<Texture2D>();

            private static Sprite MakeSprite(Texture2D tex) =>
                (tex == null) ? null : new Sprite(tex, new Rectangle(0, 0, tex.Width, tex.Height));

            private static Tracked GetOrCapture(object owner, FieldInfo field)
            {
                // Find existing.
                for (int i = 0; i < _tracked.Count; i++)
                {
                    var t = _tracked[i];
                    if (t.Field == field && t.Owner.IsAlive && ReferenceEquals(t.Owner.Target, owner))
                        return t;
                }

                // Capture vanilla once.
                var vanilla = field.GetValue(owner) as Sprite;
                var tr = new Tracked
                {
                    Owner = new WeakReference(owner),
                    Field = field,
                    Vanilla = vanilla,
                    LastEpochApplied = 0
                };
                _tracked.Add(tr);
                return tr;
            }

            /// <summary>
            /// Apply a pack sprite if present; otherwise restore vanilla.
            /// No-ops if the field has already been processed for the current epoch.
            /// </summary>
            public static void ApplyOrRestore(GraphicsDevice gd, object owner, FieldInfo field, string packFolder, string stem)
            {
                if (gd == null || owner == null || field == null) return;

                var tr = GetOrCapture(owner, field);
                if (tr.LastEpochApplied == _epoch) return; // Already processed for current pack.

                // Try pack override.
                if (SplashTextures.TryLoadExact(gd, packFolder, stem, out var modTex) && modTex != null)
                {
                    // Remember we own it; will dispose on pack switch.
                    _ours.Add(modTex);
                    field.SetValue(owner, MakeSprite(modTex));
                }
                else
                {
                    // No file in this pack -> restore vanilla reference.
                    field.SetValue(owner, tr.Vanilla);
                }

                tr.LastEpochApplied = _epoch;
            }

            /// <summary>
            /// Called on pack switch to retire all created textures and mark tracked fields dirty.
            /// </summary>
            public static void OnPackSwitched()
            {
                // Retire mod textures we created; vanilla sprites remain referenced.
                foreach (var tex in _ours)
                    try { GpuRetireQueue.Enqueue(tex); } catch { }
                _ours.Clear();

                // Force all tracked fields to re-evaluate on next draw.
                BumpEpoch();

                // Note: Vanilla references remain in _tracked so we can restore them even if
                // the next pack lacks assets; dead owners will get GC'd naturally.
            }
        }
        #endregion

        #endregion

        #region Player, Enemy, Dragon Sprites

        /// <summary>
        /// Skin manager for 3D models:
        /// handles player, enemy, and dragon texture replacement, baseline capture,
        /// and safe disposal of mod-created textures on pack switch.
        /// </summary>
        internal static class ModelSkinManager
        {
            #region Paths / Config

            // Override hooks if you ever need to
            public static Func<string> ResolvePacksRoot  = () => TexturePackManager.PacksRoot;
            public static Func<string> ResolveActivePack = () => TPConfig.LoadOrCreate().ActivePack ?? "";

            private static string ActivePackDir()
            {
                var root = ResolvePacksRoot?.Invoke() ?? ".";
                var pack = ResolveActivePack?.Invoke() ?? "";
                return Path.Combine(root, pack);
            }

            private static string ModelsRoot() => Path.Combine(ActivePackDir(), "Models");
            private static string PlayerDir()  => Path.Combine(ModelsRoot(), "Player");
            private static string EnemiesDir() => Path.Combine(ModelsRoot(), "Enemies");
            private static string DragonsDir() => Path.Combine(ModelsRoot(), "Dragons");

            #endregion

            #region Shared Texture Cache / Load

            private static readonly Dictionary<string, Texture2D> _texCache =
                new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Load a Texture2D from disk with per-path caching; returns false if not found or load failed.
            /// </summary>
            private static bool TryLoadTexture(string path, GraphicsDevice gd, out Texture2D tex)
            {
                tex = null;
                if (string.IsNullOrEmpty(path) || gd == null || !File.Exists(path)) return false;

                if (_texCache.TryGetValue(path, out Texture2D cached) && cached != null && !cached.IsDisposed)
                {
                    tex = cached;
                    return true;
                }

                try
                {
                    using (var fs = File.OpenRead(path))
                    {
                        tex = Texture2D.FromStream(gd, fs);
                        _texCache[path] = tex;
                        return true;
                    }
                }
                catch
                {
                    try { tex?.Dispose(); } catch { }
                    tex = null;
                    return false;
                }
            }

            private static string Normalize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                s = Path.GetFileNameWithoutExtension(s.Trim());
                s = s.Replace(' ', '_');
                var chars = s.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    char c = chars[i];
                    if (!char.IsLetterOrDigit(c) && c != '_') chars[i] = '_';
                }
                s = Regex.Replace(new string(chars), "_+", "_").Trim('_');
                return s.ToLowerInvariant();
            }

            private static IEnumerable<string> PlayerCandidates(string key)
            {
                // Matches the previous Player naming style: <Key>[_model].png.
                var dir = PlayerDir();
                var norm = Normalize(key);
                yield return Path.Combine(dir, key + "_model.png");
                yield return Path.Combine(dir, norm + "_model.png");
                yield return Path.Combine(dir, key + ".png");
                yield return Path.Combine(dir, norm + ".png");
            }

            private static string[] EnumCandidates(string dir, string enumName)
            {
                // Enemies/Dragons: Let users use exact EnumName, lower, and normalized lower.
                var stem1 = enumName ?? "";
                var stem2 = stem1.ToLowerInvariant();
                var stem3 = stem2.Replace("__", "_");
                return new[]
                {
                    Path.Combine(dir, stem1 + ".png"),
                    Path.Combine(dir, stem2 + ".png"),
                    Path.Combine(dir, stem3 + ".png"),
                };
            }

            /// <summary>
            /// Try the first successfully-loaded candidate texture for a skin.
            /// </summary>
            private static Texture2D TryLoadFirst(GraphicsDevice gd, IEnumerable<string> candidates)
            {
                foreach (var p in candidates)
                {
                    if (TryLoadTexture(p, gd, out Texture2D tex)) return tex;
                }
                return null;
            }

            private static void SetFieldOrProperty(object obj, string memberName, object value)
            {
                if (obj == null) return;
                var t = obj.GetType();

                var f = AccessTools.Field(t, memberName);
                if (f != null)
                {
                    try { f.SetValue(obj, value); return; } catch { }
                }

                var p = AccessTools.Property(t, memberName);
                if (p != null && p.CanWrite)
                {
                    try { p.SetValue(obj, value, null); return; } catch { }
                }
            }
            #endregion

            #region Baselines (Captured Once After Content Is Ready)

            // Mark/recognize our mod textures.
            private static void MarkOurs(Texture2D t) { if (t != null) _ours.Add(t); }
            private static bool IsOurs(Texture2D t) => t != null && _ours.Contains(t);

            // Enemy texture names table.

            // Dragon texture names table / index (mirror the enemy approach).
            private static readonly FieldInfo _fiDragonTextureNames = AccessTools.Field(typeof(DragonType), "_textureNames");
            private static readonly FieldInfo _fiDragonTextureIndex =
                AccessTools.Field(typeof(DragonType), "TextureIndex") ??
                AccessTools.Field(typeof(DragonType), "_textureIndex");

            private static Texture2D GetOriginalDragonTexture(DragonType dt)
            {
                try
                {
                    if (!(_fiDragonTextureNames?.GetValue(null) is string[] names)) return null;
                    if (_fiDragonTextureIndex == null) return null;
                    int idx = Convert.ToInt32(_fiDragonTextureIndex.GetValue(dt), CultureInfo.InvariantCulture);
                    if (idx < 0 || idx >= names.Length) return null;
                    return CastleMinerZGame.Instance.Content.Load<Texture2D>(names[idx]);
                }
                catch { return null; }
            }

            private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
            {
                public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
                public bool Equals(T x, T y) => ReferenceEquals(x, y);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            // Player: Remember original Effect per MeshPart (so we can restore).
            private sealed class PlayerBaseline
            {
                public List<(ModelMeshPart part, Effect original)> Parts = new List<(ModelMeshPart, Effect)>();
                public bool Captured;
            }
            private static readonly ConditionalWeakTable<Model, PlayerBaseline> _playerBaselines =
                new ConditionalWeakTable<Model, PlayerBaseline>();

            // Enemies: Per EnemyType original texture.
            private static readonly Dictionary<EnemyType, Texture2D> _enemyBaseline =
                new Dictionary<EnemyType, Texture2D>(ReferenceEqualityComparer<EnemyType>.Instance);

            // Dragons: Per DragonType original texture.
            private static readonly Dictionary<DragonType, Texture2D> _dragonBaseline =
                new Dictionary<DragonType, Texture2D>(ReferenceEqualityComparer<DragonType>.Instance);

            // Track our mod-created textures so we can retire them on switch.
            private static readonly HashSet<Texture2D> _ours = new HashSet<Texture2D>(ReferenceEqualityComparer<Texture2D>.Instance);

            #endregion

            #region Baseline Capture / Restore

            /// <summary>
            /// Capture baselines for player, enemies, and dragons if not already captured.
            /// Safe to call multiple times.
            /// </summary>
            public static void CaptureBaselinesIfNeeded()
            {
                try
                {
                    // Player (ProxyModel is the shared model used to instance players).
                    if (AccessTools.Field(typeof(Player), "ProxyModel")?.GetValue(null) is Model proxyModel) CapturePlayerBaseline(proxyModel);
                }
                catch { /* Ignore. */ }

                try { CaptureEnemyBaselines(); } catch { }
                try { CaptureDragonBaselines(); } catch { }
            }

            private static void CapturePlayerBaseline(Model model)
            {
                if (model == null) return;
                var entry = _playerBaselines.GetOrCreateValue(model);
                if (entry.Captured) return;

                try
                {
                    foreach (var mesh in model.Meshes)
                    {
                        foreach (var part in mesh.MeshParts)
                        {
                            if (part?.Effect == null) continue;
                            entry.Parts.Add((part, part.Effect));
                        }
                    }
                    entry.Captured = true;
                    Log("[Models] Player baseline captured.");
                }
                catch { /* Best-effort. */ }
            }

            private static void RestorePlayerBaseline(Model model)
            {
                if (model == null) return;
                if (!_playerBaselines.TryGetValue(model, out var entry) || !entry.Captured) return;

                foreach (var pair in entry.Parts)
                {
                    var part = pair.part;
                    var orig = pair.original;
                    if (part == null) continue;
                    try
                    {
                        // If we previously set a cloned effect, swap back.
                        if (!ReferenceEquals(part.Effect, orig))
                        {
                            // Optional: Try disposing the clone (it's ours), but safe to skip.
                            try { part.Effect?.Dispose(); } catch { }
                            part.Effect = orig;
                        }
                    }
                    catch { }
                }
                Log("[Models] Player baseline restored.");
            }

            // Capture once, prefer content asset, refuse "ours".
            private static void CaptureEnemyBaselines()
            {
                var typesField = AccessTools.Field(typeof(EnemyType), "Types");
                var arr = typesField != null ? (EnemyType[])typesField.GetValue(null) : null;
                if (arr == null) return;

                foreach (var et in arr)
                {
                    if (et == null || _enemyBaseline.ContainsKey(et)) continue;

                    var vanilla = GetOriginalEnemyTexture(et) ?? et.EnemyTexture;
                    if (IsOurs(vanilla) || vanilla == null) continue; // Do NOT capture mod/invalid.
                    _enemyBaseline[et] = vanilla;
                }
            }

            private static void RestoreEnemyBaselines()
            {
                foreach (var kv in _enemyBaseline)
                {
                    var et = kv.Key; var tex = kv.Value;
                    try { SetFieldOrProperty(et, "EnemyTexture", tex); } catch { }
                }
                Log("[Models] Enemy baselines restored.");
            }

            private static void CaptureDragonBaselines()
            {
                var dragonType = typeof(DragonType);
                DragonType[] arr = null;
                foreach (var f in dragonType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (typeof(DragonType[]).IsAssignableFrom(f.FieldType))
                    {
                        arr = f.GetValue(null) as DragonType[]; break;
                    }
                }
                if (arr == null) return;

                foreach (var dt in arr)
                {
                    if (dt == null || _dragonBaseline.ContainsKey(dt)) continue;

                    var vanilla = GetOriginalDragonTexture(dt) ??
                                  (AccessTools.Property(dt.GetType(), "Texture")?.GetValue(dt, null) as Texture2D) ??
                                  (AccessTools.Field(dt.GetType(), "Texture")?.GetValue(dt) as Texture2D);

                    if (IsOurs(vanilla) || vanilla == null) continue; // Refuse mod/invalid.
                    _dragonBaseline[dt] = vanilla;
                }
            }

            private static void RestoreDragonBaselines()
            {
                foreach (var kv in _dragonBaseline)
                {
                    var dt = kv.Key; var tex = kv.Value;
                    try { SetFieldOrProperty(dt, "Texture", tex); } catch { }
                }
                Log("[Models] Dragon baselines restored.");
            }
            #endregion

            #region Baseline - Public Entry Points

            /// <summary>
            /// Full reset on pack switch:
            /// restore all baselines and queue mod-created textures for GPU retirement.
            /// </summary>
            public static void OnPackSwitched()
            {
                // Restore.
                try
                {
                    var proxy = AccessTools.Field(typeof(Player), "ProxyModel")?.GetValue(null) as Model;
                    RestorePlayerBaseline(proxy);
                }
                catch { }

                RestoreEnemyBaselines();
                RestoreDragonBaselines();

                // Retire our textures + clear file cache.
                //
                // IMPORTANT:
                //   Dragon model textures are special. DragonPartEntity caches DragonType.Texture
                //   into its DragonTexture field at construction and continues to use that reference
                //   even after pack switches. If we retire those textures while a dragon is active,
                //   the next draw will attempt to use a disposed Texture2D and XNA will throw
                //   ObjectDisposedException.
                //
                //   To avoid this, we skip disposing any cached texture whose path lives under
                //   Models\Dragons\. Those textures will stay alive as long as a DragonPartEntity
                //   or DragonType still references them, and will be cleaned up naturally when
                //   those objects are collected.
                if (_texCache != null)
                {
                    foreach (var kv in _texCache)
                    {
                        var path = kv.Key;
                        var tex = kv.Value;
                        if (tex == null)
                            continue;

                        try
                        {
                            // Detect "Models\Dragons\" in the path (case-insensitive).
                            if (path.IndexOf(
                                    Path.DirectorySeparatorChar + "Dragons" + Path.DirectorySeparatorChar,
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Skip dragon textures: They may still be in use by live dragons.
                                continue;
                            }

                            GpuRetireQueue.Enqueue(tex);
                        }
                        catch
                        {
                            // Best-effort disposal; ignore per-texture failures.
                        }
                    }

                    // We still clear the cache; textures are now kept alive only by the engine
                    // (EnemyType, DragonType, Player models, entities, etc.).
                    _texCache.Clear();
                }

                // _ours tracks mod-created player/enemy/etc. textures. Dragon textures are no
                // longer added to _ours (see ApplyDragonsNow), so it's safe to retire them all.
                foreach (var t in _ours)
                {
                    try { GpuRetireQueue.Enqueue(t); } catch { }
                }
                _ours.Clear();
            }
            #endregion

            #region Player

            /// <summary>
            /// Apply a skin to a player Model (clones effects on parts to avoid bleed).
            /// File search: Models/Player/<key>[_model].png with friendly fallbacks.
            /// </summary>
            public static bool TryApplyPlayerSkin(Model model, string key = "Player")
            {
                if (model == null) return false;

                // Ensure baseline exists so we can always revert.
                CapturePlayerBaseline(model);

                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null) return false;

                var tex = TryLoadFirst(gd, PlayerCandidates(key));
                if (tex == null)
                {
                    // No replacement -> restore vanilla effects.
                    RestorePlayerBaseline(model);
                    return false;
                }

                MarkOurs(tex);
                ApplyTextureToWholeModel(model, tex);
                Log($"Player skin applied: {key}.");
                return true;
            }

            private static void ApplyTextureToWholeModel(Model model, Texture2D tex)
            {
                if (model == null || tex == null) return;

                foreach (var mesh in model.Meshes)
                {
                    foreach (var part in mesh.MeshParts)
                    {
                        if (part == null) continue;
                        var eff = part.Effect;
                        if (eff == null) continue;

                        try { part.Effect = eff = eff.Clone(); } catch { /* Best-effort. */ }

                        if (eff is BasicEffect be)
                        {
                            be.TextureEnabled = true;
                            be.Texture = tex;
                            continue;
                        }

                        try
                        {
                            var p = eff.Parameters;
                            EffectParameter param = null;
                            if (p != null)
                            {
                                param = p["Texture"] ?? p["Texture0"] ?? p["DiffuseTexture"] ??
                                        p["ColorMap"] ?? p["gDiffuseTexture"] ?? p["gColorMap"];
                            }
                            param?.SetValue(tex);
                        }
                        catch { }
                    }
                }
            }
            #endregion

            #region Enemies / Dragons (Enum Keyed)

            #region Texture Helpers

            /// <summary>
            /// Compose a mod RGB texture with original alpha from <paramref name="alphaSrc"/> to preserve cutouts.
            /// </summary>
            private static Texture2D ComposeWithOriginalAlpha(Texture2D modRgb, Texture2D alphaSrc)
            {
                if (modRgb == null || alphaSrc == null || modRgb.IsDisposed || alphaSrc.IsDisposed)
                    return null;

                int w = modRgb.Width, h = modRgb.Height;

                // Read mod RGB.
                if (!TryReadTextureToColors(modRgb, out var rgb)) return null;

                // Read original (possibly DXT) as Color[], then scale to mod size if needed.
                if (!TryReadTextureToColors(alphaSrc, out var aColors)) return null;
                if (alphaSrc.Width != w || alphaSrc.Height != h)
                    aColors = ScaleColorNearest(aColors, alphaSrc.Width, alphaSrc.Height, w, h);

                // Merge A.
                for (int i = 0; i < rgb.Length; i++)
                    rgb[i].A = aColors[i].A;

                var outTex = new Texture2D(modRgb.GraphicsDevice, w, h, false, SurfaceFormat.Color);
                outTex.SetData(rgb);
                return outTex;
            }

            private static SpriteBatch _rtBlitter; // Lazy.

            /// <summary>
            /// Try to read a Texture2D into a Color[] buffer. Uses a render-target blit for non-Color formats.
            /// </summary>
            public static bool TryReadTextureToColors(Texture2D tex, out Color[] pixels)
            {
                pixels = null;
                if (tex == null || tex.IsDisposed) return false;

                // Fast path: Already Color format, whole texture read.
                try
                {
                    if (tex.Format == SurfaceFormat.Color)
                    {
                        pixels = new Color[tex.Width * tex.Height];
                        tex.GetData(pixels);
                        return true;
                    }
                }
                catch
                {
                    // Fall through to RT path.
                }

                // Slow path: Blit to a Color render target, then GetData<Color>().
                try
                {
                    var gd = tex.GraphicsDevice;
                    var saved = gd.GetRenderTargets();
                    using (var rt = new RenderTarget2D(gd, tex.Width, tex.Height, false, SurfaceFormat.Color, DepthFormat.None))
                    {
                        if (_rtBlitter == null) _rtBlitter = new SpriteBatch(gd);

                        gd.SetRenderTarget(rt);

                        // No Clear() call (avoids your earlier depth/stencil error). We draw full-frame anyway.
                        _rtBlitter.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
                                         DepthStencilState.None, RasterizerState.CullNone);
                        _rtBlitter.Draw(tex, new Rectangle(0, 0, tex.Width, tex.Height), Color.White);
                        _rtBlitter.End();

                        // Back to default.
                        if (saved != null && saved.Length > 0) gd.SetRenderTargets(saved);
                        else gd.SetRenderTarget(null);

                        pixels = new Color[tex.Width * tex.Height];
                        rt.GetData(pixels); // Now it's always valid (SurfaceFormat.Color).
                        return true;
                    }
                }
                catch
                {
                    // Give up.
                    pixels = null;
                    return false;
                }
            }

            // Nearest-neighbor scaler for Color[] (kept from earlier).
            private static Color[] ScaleColorNearest(Color[] src, int sw, int sh, int dw, int dh)
            {
                var dst = new Color[dw * dh];
                int stepX = (sw << 16) / Math.Max(1, dw);
                int stepY = (sh << 16) / Math.Max(1, dh);
                int syFP = 0;
                for (int y = 0; y < dh; y++)
                {
                    int sy = syFP >> 16;
                    int rowSrc = sy * sw;
                    int sxFP = 0;
                    int rowDst = y * dw;
                    for (int x = 0; x < dw; x++)
                    {
                        int sx = sxFP >> 16;
                        dst[rowDst + x] = src[rowSrc + sx];
                        sxFP += stepX;
                    }
                    syFP += stepY;
                }
                return dst;
            }

            // Cache the content table.
            private static readonly FieldInfo _fiEnemyTextureNames =
                AccessTools.Field(typeof(EnemyType), "_textureNames");

            // Helper to get the original content Texture2D for a given EnemyType instance.
            private static Texture2D GetOriginalEnemyTexture(EnemyType et)
            {
                try
                {
                    if (et == null) return null;
                    if (!(_fiEnemyTextureNames?.GetValue(null) is string[] names)) return null;

                    // "TextureIndex" is the enum EnemyType.TextureNameEnum set in ctor.
                    var fiIdx = AccessTools.Field(typeof(EnemyType), "TextureIndex");
                    if (fiIdx == null) return null;

                    var idxObj = fiIdx.GetValue(et);
                    int idx = (int)Convert.ChangeType(idxObj, typeof(int), CultureInfo.InvariantCulture);
                    if (idx < 0 || idx >= names.Length) return null;

                    var asset = names[idx];
                    return CastleMinerZGame.Instance.Content.Load<Texture2D>(asset);
                }
                catch { return null; }
            }
            #endregion

            /// <summary>
            /// Call this after EnemyType.Init / DragonType.Init or on pack switch to apply all model skins.
            /// </summary>
            public static void ApplyActiveModelSkinsNow()
            {
                int players = ApplyPlayerNow();
                int enemies = ApplyEnemiesNow();
                int dragons = ApplyDragonsNow();
                Log($"[Models] Applied {players} player texture(s), {enemies} enemy texture(s), {dragons} dragon texture(s).");
            }

            /// <summary>
            /// Apply a Player.png (or *_model.png) skin to the entire player proxy model. Returns 1 if applied.
            /// </summary>
            private static int ApplyPlayerNow()
            {
                // Locate the model (same source you already used).
                if (!(AccessTools.Field(typeof(Player), "ProxyModel")?.GetValue(null) is Model proxyModel)) return 0;

                // Make sure baseline comes from true vanilla, BEFORE we touch anything.
                CapturePlayerBaseline(proxyModel);

                // Try pack PNGs.
                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null) return 0;

                var tex = TryLoadFirst(gd, PlayerCandidates("Player"));
                if (tex == null) return 0;

                MarkOurs(tex); // If you keep an _ours set.
                ApplyTextureToWholeModel(proxyModel, tex);
                return 1;
            }

            /// <summary>
            /// Enemy model skin application loop; composes mod RGB over original alpha when possible.
            /// </summary>
            private static int ApplyEnemiesNow()
            {
                int applied = 0;
                var dir = Path.Combine(TexturePackManager.PacksRoot, TPConfig.LoadOrCreate().ActivePack ?? "", "Models", "Enemies");
                var gd = CastleMinerZGame.Instance.GraphicsDevice;

                var typesField = AccessTools.Field(typeof(EnemyType), "Types");
                var arr = typesField != null ? (EnemyType[])typesField.GetValue(null) : null;
                if (arr == null) return 0;

                foreach (var et in arr)
                {
                    if (et == null) continue;

                    // Restore vanilla first (if captured).
                    if (_enemyBaseline.TryGetValue(et, out var baseTex) && baseTex != null)
                        SetFieldOrProperty(et, "EnemyTexture", baseTex);

                    if (!Directory.Exists(dir)) continue;

                    var png = TryLoadFirst(gd, EnumCandidates(dir, et.EType.ToString()));
                    if (png == null) continue;

                    // Compose with original alpha for DXT/alpha correctness.
                    var orig = baseTex ?? GetOriginalEnemyTexture(et) ?? et.EnemyTexture;
                    var composed = ComposeWithOriginalAlpha(png, orig) ?? png;

                    MarkOurs(composed);
                    SetFieldOrProperty(et, "EnemyTexture", composed);
                    applied++;
                }
                return applied;
            }

            /// <summary>
            /// Dragon model skin application loop; direct texture swap per DragonType.
            /// </summary>
            private static int ApplyDragonsNow()
            {
                int applied = 0;
                var dir = DragonsDir();
                var gd = CastleMinerZGame.Instance.GraphicsDevice;

                // Find DragonType[] again.
                DragonType[] arr = null;
                foreach (var f in typeof(DragonType).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (typeof(DragonType[]).IsAssignableFrom(f.FieldType)) { arr = f.GetValue(null) as DragonType[]; break; }
                if (arr == null) return 0;

                foreach (var dt in arr)
                {
                    if (dt == null) continue;

                    // Restore vanilla first.
                    if (_dragonBaseline.TryGetValue(dt, out var baseTex) && baseTex != null)
                        SetFieldOrProperty(dt, "Texture", baseTex);

                    if (!Directory.Exists(dir)) continue;

                    var tex = TryLoadFirst(gd, EnumCandidates(dir, dt.EType.ToString()));
                    if (tex == null) continue;

                    // IMPORTANT:
                    // Do NOT MarkOurs(tex) for dragon textures.
                    //
                    // DragonPartEntity caches DragonType.Texture into its DragonTexture field at
                    // construction time and keeps using that reference even after texture pack
                    // switches. If we track these in _ours and retire them on pack switch,
                    // active dragons will keep a pointer to a disposed Texture2D and XNA will
                    // throw ObjectDisposedException when rendering.
                    //
                    // We deliberately let dragon textures live for as long as any dragon entity
                    // references them. Once those entities are GC'd, the GraphicsResource
                    // finalizer will clean them up.
                    SetFieldOrProperty(dt, "Texture", tex);
                    applied++;
                }
                return applied;
            }
            #endregion
        }
        #endregion

        #region Terrain Shaders (BlockEffect)

        /// <summary>
        /// Provides a safe, pack-driven override for the terrain shader (BlockEffect).
        ///
        /// Supports:
        ///   - Startup-time override:
        ///       • via the BlockTerrain constructor transpiler which routes loads through
        ///         LoadBlockEffectOrVanilla(...).
        ///   - Runtime pack switching:
        ///       • OnPackSwitched() swaps BlockTerrain._effect to either:
        ///           - the override FXB (Effect(gd, bytes))
        ///           - or the vanilla content-managed Effect (Shaders\\BlockEffect)
        ///
        /// Safety:
        ///   - Tracks "owned" Effects (created from FXB bytes) so we only dispose what we create.
        ///   - Keeps a vanilla baseline reference (content-owned) and never retires/disposes it.
        ///   - Uses flavored folder resolution (HiDef/Reach) to avoid unnecessary FIRST_CHANCE probing.
        /// </summary>
        internal static class ShaderOverride
        {
            // Cache one override effect so we don't recreate it repeatedly.
            private static Effect   _vanillaBaseline; // Never dispose this; content-owned.
            private static Effect   _cached;
            private static string   _cachedPath;
            private static DateTime _cachedWriteUtc;

            // Track ONLY effects we created (safe to dispose).
            private static readonly HashSet<Effect> _owned = new HashSet<Effect>(RefEq<Effect>.Instance);

            // Prefer flavored folders first to avoid probing Content\<asset>.xnb and throwing FIRST_CHANCE.
            private static readonly string[] _flavorsPrefer = { "HiDefContent", "ReachContent", "" };

            /// <summary>
            /// Reference-equality comparer (because GPU resources are identity-based objects).
            /// </summary>
            private sealed class RefEq<T> : IEqualityComparer<T> where T : class
            {
                public static readonly RefEq<T> Instance = new RefEq<T>();
                public bool Equals(T a, T b) => ReferenceEquals(a, b);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            /// <summary>
            /// Pack file location for the override shader bytes.
            /// Expected path:
            ///   !Mods\TexturePacks\<ActivePack>\Shaders\blockEffect.fxb
            /// </summary>
            private static string OverrideFxbPath()
            {
                var root = TexturePackManager.PacksRoot;
                var pack = TPConfig.LoadOrCreate().ActivePack ?? "";
                return Path.Combine(root, pack, "Shaders", "blockEffect.fxb");
            }

            /// <summary>
            /// Called from TexturePackManager.DoReloadCore().
            /// Ensures BlockTerrain._effect is ALWAYS valid after a pack switch:
            ///   - Override FXB if present.
            ///   - Otherwise vanilla ContentManager effect.
            /// </summary>
            internal static void OnPackSwitched()
            {
                try
                {
                    var gm = CastleMinerZGame.Instance;
                    var gd = gm?.GraphicsDevice;
                    var cm = gm?.Content;

                    var bt = BlockTerrain.Instance;
                    if (bt == null || gd == null || cm == null)
                    {
                        Log("[Shaders] OnPackSwitched: BlockTerrain/GraphicsDevice/ContentManager not ready.");
                        return;
                    }

                    var fxbPath = OverrideFxbPath();
                    bool wantOverride = File.Exists(fxbPath);

                    // Read current terrain effect (old) so we can retire it AFTER we swap.
                    var oldFx = GetTerrainEffect(bt);

                    Effect nextFx = null;

                    if (wantOverride)
                    {
                        if (TryGetOverrideEffect(gd, out nextFx) && nextFx != null && !nextFx.IsDisposed)
                        {
                            Log($"[Shaders] Using OVERRIDE FXB '{fxbPath}'.");
                        }
                        else
                        {
                            Log($"[Shaders] OnPackSwitched: Override requested but failed; falling back to VANILLA.");
                            nextFx = LoadVanillaBlockEffect(cm);
                        }
                    }
                    else
                    {
                        Log($"[Shaders] OnPackSwitched: No FXB found for pack; restoring VANILLA (Shaders\\BlockEffect).");
                        nextFx = LoadVanillaBlockEffect(cm);

                        // We are leaving override mode; clear our cache bookkeeping.
                        _cached = null;
                        _cachedPath = null;
                        _cachedWriteUtc = default;
                    }

                    if (nextFx == null || nextFx.IsDisposed)
                    {
                        Log("[Shaders] OnPackSwitched: FAILED to obtain a valid next Effect (null/disposed).");
                        return;
                    }

                    // Swap to the live Effect first.
                    SetTerrainEffect(bt, nextFx);

                    // Rebind ctor-time params so the swapped effect is usable.
                    RebindTerrainEffectParameters(bt, nextFx);

                    // Now it's safe to retire the old effect, BUT ONLY if we created it.
                    SafeRetireIfOwned(oldFx, "old terrain effect");

                    // Also retire cached override if it's not the one we're currently using (owned only).
                    if (_cached != null && !ReferenceEquals(_cached, nextFx))
                        SafeRetireIfOwned(_cached, "cached override effect");

                    Log($"[Shaders] Swap complete. (override={(wantOverride ? "yes" : "no")})");
                }
                catch (Exception ex)
                {
                    Log($"[Shaders] OnPackSwitched failed. EX={ex}.");
                }
            }

            /// <summary>
            /// Retires an Effect only if it is "owned" (created by us from FXB bytes).
            /// This avoids disposing content-managed vanilla Effects, which can poison ContentManager caches.
            /// </summary>
            private static void SafeRetireIfOwned(Effect fx, string label)
            {
                if (fx == null) return;
                if (fx.IsDisposed) return;

                // Only dispose effects we created via new Effect(gd, bytes).
                if (!_owned.Remove(fx)) return;

                try
                {
                    TexturePackManager.GpuRetireQueue.Enqueue(fx); // After-draw disposal.
                    Log($"[Shaders] Retired {label} (owned).");
                }
                catch { }
            }

            /// <summary>
            /// Resolves the actual asset name used by disk layout, preferring:
            ///   Content\HiDefContent\<asset>.xnb
            ///   Content\ReachContent\<asset>.xnb
            ///   Content\<asset>.xnb
            ///
            /// This reduces FIRST_CHANCE "Content\<asset>.xnb not found" probing noise.
            /// </summary>
            private static string ResolveExistingFlavorAssetName(ContentManager cm, string asset)
            {
                if (cm == null || string.IsNullOrEmpty(asset)) return null;

                string rel = asset.Replace('\\', '/').TrimStart('/'); // "Shaders/BlockEffect".
                foreach (var flavor in _flavorsPrefer)
                {
                    string disk = (flavor.Length == 0)
                        ? Path.Combine(cm.RootDirectory, rel + ".xnb")
                        : Path.Combine(cm.RootDirectory, flavor, rel + ".xnb");

                    if (File.Exists(disk))
                    {
                        // Return asset name that points directly at the folder that exists on disk.
                        // ContentManager.Load will then open: Content\<flavor>\Shaders\BlockEffect.xnb.
                        return (flavor.Length == 0)
                            ? asset
                            : (flavor + "\\" + rel.Replace('/', '\\'));
                    }
                }
                return null;
            }

            /// <summary>
            /// Loads or returns cached vanilla (content-managed) BlockEffect.
            ///
            /// Strategy:
            ///   1) Reuse captured vanilla baseline if present.
            ///   2) If current terrain effect isn't owned by us, treat it as baseline (cheap).
            ///   3) Otherwise load via flavored path resolution to avoid probe spam.
            /// </summary>
            private static Effect LoadVanillaBlockEffect(ContentManager cm)
            {
                const string asset = "Shaders\\BlockEffect";

                try
                {
                    // 1) If we already captured vanilla, reuse it (no disk, no FIRST_CHANCE).
                    if (_vanillaBaseline != null && !_vanillaBaseline.IsDisposed)
                        return _vanillaBaseline;

                    // 2) If terrain is currently using a non-owned effect, treat that as vanilla baseline.
                    var bt = BlockTerrain.Instance;
                    var cur = (bt != null) ? GetTerrainEffect(bt) : null;
                    if (cur != null && !cur.IsDisposed && !_owned.Contains(cur))
                    {
                        _vanillaBaseline = cur;
                        return _vanillaBaseline;
                    }

                    // 3) Otherwise load vanilla by resolving the actual folder that contains the XNB.
                    string resolved = ResolveExistingFlavorAssetName(cm, asset) ?? asset;

                    // Optional: If resolved is null, just return current.
                    if (resolved == null) return cur;

                    var fx = cm.Load<Effect>(resolved);

                    // Cache baseline (do NOT add to _owned, do NOT retire/dispose later).
                    if (fx != null && !fx.IsDisposed)
                        _vanillaBaseline = fx;

                    return fx;
                }
                catch (Exception ex)
                {
                    Log($"[Shaders] LoadVanillaBlockEffect failed. EX={ex}.");
                    return null;
                }
            }

            /// <summary>
            /// Loads the override FXB bytes from disk and constructs an Effect.
            /// Caches by file timestamp, and tracks created Effects as "owned".
            /// </summary>
            internal static bool TryGetOverrideEffect(GraphicsDevice gd, out Effect fx)
            {
                fx = null;

                try
                {
                    var p = OverrideFxbPath();
                    if (!File.Exists(p)) return false;

                    DateTime wt;
                    try { wt = File.GetLastWriteTimeUtc(p); }
                    catch { wt = default; }

                    if (_cached != null &&
                        !_cached.IsDisposed &&
                        string.Equals(_cachedPath, p, StringComparison.OrdinalIgnoreCase) &&
                        _cachedWriteUtc == wt)
                    {
                        fx = _cached;
                        return true;
                    }

                    var bytes = File.ReadAllBytes(p);
                    if (bytes == null || bytes.Length == 0) return false;

                    var created = new Effect(gd, bytes);

                    // Mark as ours (safe to dispose later).
                    _owned.Add(created);

                    // Retire the previous cached override if owned.
                    if (_cached != null && !ReferenceEquals(_cached, created))
                        SafeRetireIfOwned(_cached, "previous cached override");

                    _cached = created;
                    _cachedPath = p;
                    _cachedWriteUtc = wt;

                    fx = created;
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[Shaders] TryGetOverrideEffect failed. EX={ex}.");
                    fx = null;
                    return false;
                }
            }

            // ------------------------------------------------------------------------------------
            // BlockTerrain internals:
            //   - _effect is private, so we reflect it.
            //   - RebindTerrainEffectParameters mirrors what the BlockTerrain ctor does for vanilla.
            // ------------------------------------------------------------------------------------

            private static Effect GetTerrainEffect(BlockTerrain bt)
            {
                try
                {
                    var fi = typeof(BlockTerrain).GetField("_effect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    return fi?.GetValue(bt) as Effect;
                }
                catch { return null; }
            }

            private static void SetTerrainEffect(BlockTerrain bt, Effect fx)
            {
                try
                {
                    var fi = typeof(BlockTerrain).GetField("_effect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    fi?.SetValue(bt, fx);
                }
                catch { }
            }

            /// <summary>
            /// Re-applies the ctor-time effect wiring:
            ///   - Vertex UV table.
            ///   - Face matrices.
            ///   - Diffuse/Normal/Metal textures, env map, mip textures.
            ///
            /// Notes:
            ///   - If the FXB doesn't define the same parameter names, it may render incorrectly even if it loads.
            /// </summary>
            private static void RebindTerrainEffectParameters(BlockTerrain bt, Effect fx)
            {
                // These are set in BlockTerrain ctor (textures + matrices).
                TrySetParam(fx, "VertexUVs",             GetField<Vector4[]>(bt, "_vertexUVs"));
                TrySetParam(fx, "FaceMatrices",          GetField<Matrix[]>(bt,  "_faceMatrices"));
                TrySetParam(fx, "DiffuseAlphaTexture",   GetField<Texture2D>(bt, "_diffuseAlpha"));
                TrySetParam(fx, "NormalSpecTexture",     GetField<Texture2D>(bt, "_normalSpec"));
                TrySetParam(fx, "MetalLightTexture",     GetField<Texture2D>(bt, "_metalLight"));
                TrySetParam(fx, "EnvMapTexture",         GetField<Texture3D>(bt, "_envMap"));
                TrySetParam(fx, "MipMapSpecularTexture", GetField<Texture2D>(bt, "_mipMapNormals"));
                TrySetParam(fx, "MipMapDiffuseTexture",  GetField<Texture2D>(bt, "_mipMapDiffuse"));
            }

            private static T GetField<T>(object obj, string name) where T : class
            {
                try
                {
                    var fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    return fi?.GetValue(obj) as T;
                }
                catch { return null; }
            }

            private static void TrySetParam(Effect fx, string name, object value)
            {
                if (fx == null || value == null) return;
                try
                {
                    var p = fx.Parameters[name];
                    if (p == null) return;

                    if (value is Vector4[] v4) p.SetValue(v4);
                    else if (value is Matrix[] m) p.SetValue(m);
                    else if (value is Texture2D t2) p.SetValue(t2);
                    else if (value is Texture3D t3) p.SetValue(t3);
                }
                catch { }
            }

            /// <summary>
            /// Called by the BlockTerrain constructor transpiler.
            ///
            /// Contract:
            ///   - If an override exists and is valid, return it.
            ///   - Otherwise return vanilla ContentManager.Load<Effect>(assetName).
            /// </summary>
            internal static Effect LoadBlockEffectOrVanilla(ContentManager cm, string assetName)
            {
                var an = (assetName ?? "").Replace('\\', '/');
                if (an.EndsWith("Shaders/BlockEffect", StringComparison.OrdinalIgnoreCase))
                {
                    var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                    if (TryGetOverrideEffect(gd, out var fx) && fx != null && !fx.IsDisposed)
                        return fx;
                }
                return cm.Load<Effect>(assetName);
            }
        }
        #endregion

        #region Pack XNB Loaders (Models/Effects/etc.)

        #region Item Model Geometry Overrides (XNB)

        // Cache of resolved geometry overrides per item (keyed by ID + friendlyName).
        // Summary: Prevents repeated disk scans + model loads for the same item model.
        private static readonly Dictionary<string, Model> _modelGeoCache = new Dictionary<string, Model>();

        #region Pack Content Manager (XNB-from-folder)

        /// <summary>
        /// ContentManager that loads XNB assets from an arbitrary pack folder instead of the game's Content root.
        /// Summary: Enables pack-local XNB loading (Models/Effects/etc.) by overriding OpenStream.
        /// </summary>
        private sealed class PackContentManager : ContentManager
        {
            private readonly string _root;

            /// <summary>
            /// Creates a pack-rooted ContentManager.
            /// Summary: Uses the game's IServiceProvider (graphics device/services) but reads from a custom folder root.
            /// </summary>
            public PackContentManager(IServiceProvider services, string root) : base(services)
                => _root = root;

            /// <summary>
            /// Opens a stream for an asset request (assetName is passed without extension).
            /// Summary: Loads "<assetName>.xnb" directly from the pack folder.
            /// </summary>
            protected override Stream OpenStream(string assetName)
            {
                // Load "<assetName>.xnb" from the pack folder
                var full = Path.Combine(_root, assetName + ".xnb");
                return File.OpenRead(full);
            }
        }
        #endregion

        #region Pack Model Content Cache (Per-Folder ContentManagers)

        /// <summary>
        /// Pack-scoped ContentManager cache keyed by folder root.
        /// Summary:
        /// - Each model override can live in its own subfolder (to keep dependencies beside the model).
        /// - We keep one PackContentManager per folder root so ContentManager.Load("asset") resolves:
        ///     <root>\asset.xnb
        /// - This avoids dependency name collisions (e.g., many models using "texture_0.xnb") and
        ///   avoids "root switching" bugs that happen with a single shared ContentManager.
        /// </summary>
        private static readonly Dictionary<string, PackContentManager> _cms =
            new Dictionary<string, PackContentManager>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets (or creates) a pack-rooted ContentManager for the specified folder.
        /// Summary:
        /// - Root should be the directory containing the chosen model XNB.
        /// - The returned ContentManager will also resolve that model's dependencies from the same folder.
        /// </summary>
        private static PackContentManager GetPackCM(string root)
        {
            if (!_cms.TryGetValue(root, out var cm) || cm == null)
            {
                cm = new PackContentManager(CastleMinerZGame.Instance.Services, root);
                _cms[root] = cm;
            }
            return cm;
        }

        /// <summary>
        /// Clears all pack-scoped model loaders.
        /// Summary:
        /// - Called on pack switch (after restoring entities to vanilla) so old pack models/dependencies
        ///   are no longer referenced by live entities.
        /// - Unload/Dispose is best-effort; failures are ignored to keep pack switching resilient.
        /// </summary>
        public static void ResetPackModelCM()
        {
            foreach (var cm in _cms.Values)
            {
                try { cm?.Unload(); } catch { }
                try { (cm as IDisposable)?.Dispose(); } catch { }
            }
            _cms.Clear();
        }
        #endregion

        #region Apply Item Model Geometry

        /// <summary>
        /// Attempts to apply a pack-provided XNB *Model geometry override* onto an item entity.
        /// Summary:
        /// - Resolves a matching "*_model.xnb" candidate from the active pack.
        /// - Loads the Model via a pack-rooted ContentManager (XNB stream load).
        /// - Caches the Model per-item key to avoid repeated loads.
        /// - Applies by setting ModelEntity's protected Model property (invokes SetupModel).
        /// </summary>
        public static void TryApplyItemModelGeometry(Entity entity, InventoryItemIDs id, string friendlyName)
        {
            var cfg = TPConfig.LoadOrCreate();

            // Pack folder layout:
            //   PacksRoot\<ActivePack>\Models\Items\*.xnb
            var dir = Path.Combine(PacksRoot, cfg.ActivePack ?? "", "Models", "Items");
            if (!Directory.Exists(dir) || entity == null)
                return;

            // Keyed similarly to skin caching to keep behavior predictable.
            var key = CacheKey(id, friendlyName);

            // Resolve + load once per key.
            if (!_modelGeoCache.TryGetValue(key, out var model) || model == null)
            {
                // Try best-match candidates first (friendlyName), then strict enum/ID fallbacks.
                var xnbPath = ItemModelGeoResolver.EnumerateModelXnbCandidates(dir, id, friendlyName)
                                                  .FirstOrDefault(File.Exists);
                if (xnbPath == null)
                    return;

                var xnbRoot   = Path.GetDirectoryName(xnbPath);
                var assetName = Path.GetFileNameWithoutExtension(xnbPath);

                // ContentManager.Load expects an asset name (no extension).
                var cm = GetPackCM(xnbRoot);
                model  = cm.Load<Model>(assetName);
            }

            // Apply onto ModelEntity (or subclasses).
            try
            {
                if (entity is ModelEntity)
                {
                    // protected Model Model { set => SetupModel(value); }
                    // Use reflection so we don't need to subclass/modify base game types.
                    var p = AccessTools.Property(typeof(ModelEntity), "Model");
                    p?.SetValue(entity, model, null);
                }
            }
            catch
            {
                // Optional: Log (kept silent to match other pack overrides).
            }
        }
        #endregion

        #endregion

        #endregion

        #region Skys (ClearSky, NightSky, SunSet, DawnSky, TextureSky)

        /// <summary>
        /// Optional sky cubemap overrides loaded from PNG faces under:
        ///   !Mods\TexturePacks\<ActivePack>\Skys\
        ///
        /// Behavior:
        ///   - Captures vanilla sky baselines once (day/night/sunset/dawn + blend effect).
        ///   - On pack switch, applies pack-provided cubemaps.
        ///   - If a cubemap is missing/incomplete (any face missing), it falls back to vanilla.
        ///   - Tracks "our" TextureCubes and disposes them safely when replaced.
        /// </summary>
        internal static class SkyPacks
        {
            private static string Root =>
                Path.Combine(TexturePackManager.PacksRoot, TPConfig.LoadOrCreate().ActivePack ?? "", "Skys");

            // Track only cubes we create (safe to dispose).
            private static readonly HashSet<TextureCube> _ours = new HashSet<TextureCube>(RefEq<TextureCube>.Instance);

            // Vanilla baselines (captured once).
            private static bool        _baselineCaptured;
            private static TextureCube _baseDay, _baseNight, _baseSunSet, _baseDawn;
            private static Effect      _baseBlendEffect;

            private sealed class RefEq<T> : IEqualityComparer<T> where T : class
            {
                public static readonly RefEq<T> Instance = new RefEq<T>();
                public bool Equals(T a, T b) => ReferenceEquals(a, b);
                public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
            }

            private static readonly FieldInfo FI_Day   = AccessTools.Field(typeof(CastleMinerSky), "_dayTexture");
            private static readonly FieldInfo FI_Night = AccessTools.Field(typeof(CastleMinerSky), "_nightTexture");
            private static readonly FieldInfo FI_Sun   = AccessTools.Field(typeof(CastleMinerSky), "_sunSetTexture");
            private static readonly FieldInfo FI_Dawn  = AccessTools.Field(typeof(CastleMinerSky), "_dawnTexture");
            private static readonly FieldInfo FI_Blend = AccessTools.Field(typeof(CastleMinerSky), "_blendEffect");

            /// <summary>
            /// Pack reload hook (called from DoReloadCore()).
            /// Applies sky overrides for the current pack with vanilla fallback.
            /// </summary>
            public static void OnPackSwitched(GraphicsDevice gd, CastleMinerZGame gm)
            {
                try
                {
                    if (gd == null || gm == null) return;

                    CaptureBaselineIfNeeded();

                    // Always start from baseline (no Content loads here).
                    ApplyOrRestoreCube(gd, "ClearSky", FI_Day, _baseDay);
                    ApplyOrRestoreCube(gd, "NightSky", FI_Night, _baseNight);
                    ApplyOrRestoreCube(gd, "SunSet", FI_Sun, _baseSunSet);
                    ApplyOrRestoreCube(gd, "DawnSky", FI_Dawn, _baseDawn);

                    // (Optional) if you later allow TextureSky override, you'd do the same here.
                    if (FI_Blend != null && _baseBlendEffect != null && FI_Blend.GetValue(null) == null)
                        FI_Blend.SetValue(null, _baseBlendEffect);

                    Log($"[Skys] Applied sky overrides (partial allowed) from '{Root}'.");
                }
                catch (Exception ex)
                {
                    Log($"[Skys] OnPackSwitched failed: {ex}.");
                }
            }

            /// <summary>
            /// Captures the vanilla sky textures once.
            /// Assumes they were loaded by the game during SecondaryLoad.
            /// </summary>
            private static void CaptureBaselineIfNeeded()
            {
                if (_baselineCaptured) return;
                _baselineCaptured = true;

                // These are already loaded at game startup via SecondaryLoad -> CastleMinerSky.LoadTextures().
                _baseDay         = FI_Day?.GetValue(null)   as TextureCube;
                _baseNight       = FI_Night?.GetValue(null) as TextureCube;
                _baseSunSet      = FI_Sun?.GetValue(null)   as TextureCube;
                _baseDawn        = FI_Dawn?.GetValue(null)  as TextureCube;
                _baseBlendEffect = FI_Blend?.GetValue(null) as Effect;

                Log("[Skys] Captured vanilla baselines: " +
                    $"day={(_baseDay != null)} night={(_baseNight != null)} sunset={(_baseSunSet != null)} dawn={(_baseDawn != null)} blend={(_baseBlendEffect != null)}.");
            }

            /// <summary>
            /// Apply pack cubemap if present; otherwise restore vanilla baseline.
            /// Partial pack behavior:
            ///   - If any face is missing, treat as "no override" and keep vanilla.
            /// </summary>
            private static void ApplyOrRestoreCube(GraphicsDevice gd, string name, FieldInfo field, TextureCube vanilla)
            {
                if (gd == null || field == null) return;

                // Try pack override (needs all 6 faces).
                TextureCube custom = TryLoadCubeFromPngs(gd, Path.Combine(Root, name));
                var cur = field.GetValue(null) as TextureCube;

                if (custom != null)
                {
                    field.SetValue(null, custom);
                    MarkOurs(custom);
                    RetireIfOurs(cur);
                    // Log($"[Skys] Override applied: {name}");
                }
                else
                {
                    // No override provided (or incomplete) => restore vanilla baseline.
                    if (vanilla != null && !ReferenceEquals(cur, vanilla))
                    {
                        field.SetValue(null, vanilla);
                        RetireIfOurs(cur);
                        // Log($"[Skys] Restored vanilla: {name}");
                    }
                }
            }

            /// <summary>
            /// Marks a TextureCube as "owned" by SkyPacks so it can be safely retired/disposed later
            /// when the pack changes (avoids touching vanilla/content-managed textures).
            /// </summary>
            private static void MarkOurs(TextureCube cube)
            {
                if (cube == null) return;
                _ours.Add(cube);
            }

            /// <summary>
            /// Dispose only TextureCubes created by this system (avoid touching vanilla/content-managed textures).
            /// </summary>
            private static void RetireIfOurs(TextureCube cube)
            {
                if (cube == null) return;
                if (!_ours.Remove(cube)) return;

                try
                {
                    // If your retire queue supports IDisposable, this is safe.
                    TexturePackManager.GpuRetireQueue.Enqueue(cube);
                }
                catch
                {
                    try { cube.Dispose(); } catch { }
                }
            }

            /// <summary>
            /// Attempts to build a TextureCube from 6 PNG faces (<stem>_px/_nx/_py/_ny/_pz/_nz).
            /// Summary: If any face is missing or invalid, returns null (treat as "no override").
            /// </summary>
            private static TextureCube TryLoadCubeFromPngs(GraphicsDevice gd, string stem)
            {
                // stem like ".../Skys/ClearSky"
                string px = stem + "_px.png", nx = stem + "_nx.png",
                       py = stem + "_py.png", ny = stem + "_ny.png",
                       pz = stem + "_pz.png", nz = stem + "_nz.png";

                // IMPORTANT: This is the "partial pack" behavior:
                // If ANY face is missing, treat it as "no override" and fall back to vanilla.
                if (!File.Exists(px) || !File.Exists(nx) || !File.Exists(py) ||
                    !File.Exists(ny) || !File.Exists(pz) || !File.Exists(nz))
                    return null;

                using (var s0 = File.OpenRead(px))
                using (var t0 = Texture2D.FromStream(gd, s0))
                {
                    int size = t0.Width;
                    if (t0.Width != t0.Height) return null;

                    var cube = new TextureCube(gd, size, false, SurfaceFormat.Color);

                    SetFaceFromPng(gd, cube, CubeMapFace.PositiveX, px, size);
                    SetFaceFromPng(gd, cube, CubeMapFace.NegativeX, nx, size);
                    SetFaceFromPng(gd, cube, CubeMapFace.PositiveY, py, size);
                    SetFaceFromPng(gd, cube, CubeMapFace.NegativeY, ny, size);
                    SetFaceFromPng(gd, cube, CubeMapFace.PositiveZ, pz, size);
                    SetFaceFromPng(gd, cube, CubeMapFace.NegativeZ, nz, size);

                    return cube;
                }
            }

            /// <summary>
            /// Loads one PNG face from disk and writes its pixels into the target cubemap face.
            /// </summary>
            private static void SetFaceFromPng(GraphicsDevice gd, TextureCube cube, CubeMapFace face, string path, int size)
            {
                using (var fs = File.OpenRead(path))
                using (var tex = Texture2D.FromStream(gd, fs))
                {
                    if (tex.Width != size || tex.Height != size)
                        throw new Exception($"Face size mismatch: {path}.");

                    var pix = new Color[size * size];
                    tex.GetData(pix);
                    cube.SetData(face, pix);
                }
            }
        }
        #endregion

        #region Particle Effects

        internal static class ParticleEffectPacks
        {
            /// <summary>
            /// Called from TexturePackManager.DoReloadCore().
            /// Clears cached ParticleEffects so the next loads come from the new pack,
            /// then refreshes the main static caches by re-calling common Init() owners.
            /// </summary>
            internal static void OnPackSwitched()
            {
                try
                {
                    var cm = CastleMinerZGame.Instance?.Content;
                    if (cm == null) return;

                    EvictParticleEffects(cm);

                    // Refresh common static holders (safe to call multiple times).
                    try { Explosive.Init();            } catch { }
                    try { BlasterShot.Init();          } catch { }
                    try { ExplosiveFlashEntity.Init(); } catch { }
                    try { TorchEntity.Init();          } catch { }
                    try { TorchCloud.Init();           } catch { }
                    try { RocketEntity.Init();         } catch { }
                    try { FireballEntity.Init();       } catch { }
                    try { DragonClientEntity.Init();   } catch { }

                    Log("[ParticleFX] Reloaded particle effect caches after pack switch.");
                }
                catch (Exception ex)
                {
                    Log("[ParticleFX] OnPackSwitched failed: " + ex.Message);
                }
            }

            /// <summary>
            /// Clears any cached ParticleEffects assets from the game's ContentManager.
            /// Summary: Finds loadedAssets entries whose keys point at "ParticleEffects\..." and removes them,
            /// forcing a clean reload the next time particle content is requested.
            /// </summary>
            private static void EvictParticleEffects(ContentManager cm)
            {
                try
                {
                    var fi = typeof(ContentManager).GetField("loadedAssets", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (!(fi?.GetValue(cm) is IDictionary dict)) return;

                    var keys = new List<string>();
                    foreach (var k in dict.Keys)
                    {
                        if (k is string s)
                        {
                            var n = s.Replace('/', '\\');
                            if (n.StartsWith("ParticleEffects\\", StringComparison.OrdinalIgnoreCase) ||
                                n.IndexOf("\\ParticleEffects\\", StringComparison.OrdinalIgnoreCase) >= 0)
                                keys.Add(s);
                        }
                    }

                    foreach (var k in keys)
                        dict.Remove(k);

                    if (keys.Count > 0)
                        Log($"[ParticleFX] Evicted {keys.Count} cached ParticleEffects from ContentManager.");
                }
                catch { }
            }
        }
        #endregion

        #region Debug / Export (Screens, Blocks, Sprites, Models, Fonts, Sounds, Shaders, Skys, ParticleEffects)

        // =========================================================================================
        // Debug / Export Helpers (Overview)
        // -----------------------------------------------------------------------------------------
        // Purpose:
        //   Hotkey-friendly helpers that dump live CastleMiner Z content to disk for texture-pack
        //   authors and modders:
        //
        //     • Screens         -> Menu / dialog / loading PNGs.
        //     • Blocks          -> Terrain block faces (top/side/bottom) from the near atlas.
        //     • Sprites         -> HUD / UI sprites.
        //     • Models          -> Misc field models + item models (OBJ+MTL) + skins.
        //     • Sounds          -> Wavebanks (.xwb) -> .wav/.wma (with optional ffmpeg xWMA -> WAV).
        //     • Fonts           -> SpriteFont atlases + JSON glyph metrics.
        //     • Shaders         -> Terrain BlockEffect FXB extraction (XNB -> FXB).
        //     • Skys            -> Cubemap export as 6 PNG faces per sky.
        //     • ParticleEffects -> Export ParticleEffects *.xnb from Content flavor folders,
        //                          plus optional Texture2D PNG export when an XNB is a texture.
        //
        // High-level usage:
        //   • Call TexturePackExtractor.ExportAll() from a debug hotkey after content load
        //     (e.g. from the main menu). It will:
        //       - Create a timestamped folder under: !Mods\TexturePacks\_Extracted\<timestamp>.
        //       - Invoke each exporter in a best-effort way (errors logged; rest still run).
        // =========================================================================================

        #region Hotkey Export (Master Entry Point)

        /// <summary>
        /// Master "dump everything" helper used by your debug/hotkey.
        /// Creates a timestamped root folder and calls each asset exporter.
        /// </summary>
        public static class TexturePackExtractor
        {
            /// <summary>
            /// One-shot export pipeline:
            ///   1) Build timestamped root under PacksRoot\_Extracted
            ///   2) Export Screens / Blocks / HUD sprites / Models / Sounds / Fonts
            ///   3) Item icons, item model skins, item model OBJs
            /// Each step is wrapped in <see cref="Try(Action)"/> so a single failure won't abort.
            /// </summary>
            public static void ExportAll()
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                string root = Path.Combine(TexturePackManager.PacksRoot, "_Extracted", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(root);

                Log($"[Export] ================ Starting Asset Dump ================");
                Log($"[Export] Exporting to \"{root}\".");

                // Screens (menu backdrop, logo, dialog, loading screen).
                Try(() => ExportScreens(gm, Path.Combine(root, "Screens")));

                // Blocks (terrain atlas faces).
                Try(() => ExportBlocks(Path.Combine(root, "Blocks")));

                // Door model sheets.
                Try(() => ExportDoorModelSheets(Path.Combine(root, "Models", "Doors")));

                // HUD (UI sprites).
                Try(() => ExportUISprites(gd, gm, Path.Combine(root, "HUD")));

                // Models (misc fields on the game and static caches).
                Try(() => ExportMiscModels(gm, Path.Combine(root, "Misc")));

                // Sounds (wavebanks).
                Try(() => ExportSounds(Path.Combine(root, "Sounds")));

                // Item icons (64px / 128px).
                ExportAllItemIcons(Path.Combine(root, "Items"), exportSmall: true, exportLarge: true, overwrite: true);

                // Item Models (skins).
                ExportAllItemModelSkins(Path.Combine(root, "Models", "Items"), overwrite: true);

                // Item Models (geometry as OBJ + MTL + PNGs).
                // ExportAllItemModelsObj(Path.Combine(root, "Models", "Items", "Misc"), overwrite: true);

                // Models (Player, Enemies, Dragons, Items).
                var proxyModelField = AccessTools.Field(typeof(Player), "ProxyModel");
                if (proxyModelField?.GetValue(null) is Model proxyModel)
                {
                    // Export player model as well.
                    ModelSkinExporter.ExportModelTextures(root, proxyModel);
                }
                else
                    ModelSkinExporter.ExportModelTextures(root, null); // Failed to get player model, skip it.

                // Model Extras by content names (enemy/dragon texture tables).
                var extra = ModelSkinExporter.ExportEnemyAndDragonByContentNames(
                    Path.Combine(root, "Models", "Extras", "Enemies"),
                    Path.Combine(root, "Models", "Extras", "Dragons"));
                if (extra > 0) Log($"[Export][IMS] + Wrote {extra} extra by-name models to \"{ShortenForLog(Path.Combine(root, "Models", "Extras"))}\" \\ (Enemies | Dragons).");

                // Fonts.
                FontExtractor.ExportAllFonts(gm, Path.Combine(root, "Fonts"));

                // Shaders.
                var contentRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
                var outDir      = Path.Combine(root, "Shaders");
                var wrote       = XnbEffectExtractor.DumpBlockEffectFxb(contentRoot, outDir);
                Log(wrote != null
                    ? $"[Export] Wrote blockEffect FXB: {ShortenForLog(wrote)}."
                    : $"[Export] Could not extract Shaders/blockEffect.fxb.");

                // Skies.
                Try(() => SkyExporter.ExportAllSkys(gd, Path.Combine(root, "Skys")));

                // ParticleEffects (XNBs & PNGs).
                Try(() =>
                {
                    var peDir = Path.Combine(root, "ParticleEffects");
                    var count = ContentExtractor.ExportAllParticleEffectXnbs(contentRoot, peDir);

                    Log(count > 0
                        ? $"[Export] Wrote {count} ParticleEffects XNB(s) to \"{ShortenForLog(peDir)}\"."
                        : $"[Export] No ParticleEffects XNBs found to export.");
                });

                // Content (ReachContent + HiDefContent).
                Try(() =>
                {
                    var peDir = Path.Combine(root, "Content");
                    var count = FullContentExtractor.ExportAllContent(contentRoot, peDir);

                    Log(count > 0
                        ? $"[Export] Wrote {count} Content XNB(s) to \"{ShortenForLog(peDir)}\"."
                        : $"[Export] No Content XNBs found to export.");
                });

                Log($"[Export] ================ Asset Dump Complete ================");
            }

            /// <summary>
            /// Small helper to run an export step in a guarded try/catch.
            /// Prevents one exporter from killing the entire ExportAll() run.
            /// </summary>
            static void Try(Action a)
            {
                try { a(); } catch (Exception ex) { Log($"[Export] {ex.Message}."); }
            }
        }
        #endregion

        #region Screens (Menu / Dialog / Loading)

        /// <summary>
        /// Screen capture helpers:
        ///   • Menu backdrop
        ///   • Menu logo (atlas sprite)
        ///   • Dialog background (atlas sprite)
        ///   • Loading screen (prefer live LastSeenLoadTexture; fallback via reflection)
        /// Saves them as PNGs with alpha preserved.
        /// </summary>
        public static class ScreenExporter
        {
            // OPTIONAL: let your LoadScreen ctor/postfix set this when it builds/replaces the image
            // e.g., in your Harmony postfix: ScreenExporter.LastSeenLoadTexture = thatTexture;
            public static Texture2D LastSeenLoadTexture;
            private static SpriteBatch _sb;

            /// <summary>
            /// Export core screens that texture-pack authors usually care about.
            /// Logs a short summary, including whether a loading screen was found.
            /// </summary>
            public static void ExportScreens(CastleMinerZGame gm, string dir)
            {
                int extracted = 0; bool loadingScreenSaved = false;
                Directory.CreateDirectory(dir);

                // === Menu Backdrop ===
                try { if (SaveTex(gm.MenuBackdrop, Path.Combine(dir, "MenuBack.png"))) extracted++; } catch { }

                // === Menu Logo (sprite on a big atlas) ===
                try { if (TrySaveSpriteLike(gm.Logo, Path.Combine(dir, "MenuLogo.png"))) extracted++; } catch { }

                // === Dialog Back (sprite on a big atlas) ===
                try { if (SaveTex(gm.DialogScreenImage, Path.Combine(dir, "DialogBack.png"))) extracted++; } catch { }

                // === Loading Screen ===
                // Must be read from the loading screen.
                try
                {
                    if (LastSeenLoadTexture != null)
                    {
                        SaveTex(LastSeenLoadTexture, Path.Combine(dir, "LoadScreen.png"));
                        loadingScreenSaved = true;
                        extracted++;
                    }
                    else
                    {
                        // Fallback: Try reflection off the currently shown screen.
                        var current = gm?.mainScreenGroup?.CurrentScreen;
                        var isLoadScreen = current != null && current.GetType() == typeof(LoadScreen);
                        if (isLoadScreen)
                        {
                            var fi = AccessTools.Field(typeof(LoadScreen), "_image");
                            if (fi?.GetValue(current) is Texture2D tex)
                            {
                                var path = Path.Combine(dir, "LoadScreen.png");
                                if (SaveTex(tex, path))
                                {
                                    loadingScreenSaved = true;
                                    extracted++;
                                }
                            }
                        }
                    }
                } catch { }

                if (extracted > 0)
                {
                    if (loadingScreenSaved) Log($"[Export] Wrote {extracted} screen PNG(s) to \"{ShortenForLog(dir)}\".");
                    else Log($"[Export] Wrote {extracted} screen PNG(s) - 'LoadingScreen' was not found (try removing fast-boot mods if present) - to \"{ShortenForLog(dir)}\".");
                }
            }

            // --- Sprite / Cropping helpers ---

            /// <summary>
            /// Save a "Sprite-like" object by cropping its SourceRectangle from its Texture.
            /// Falls back to saving the whole texture if no SourceRectangle is set.
            /// </summary>
            private static bool TrySaveSpriteLike(Sprite sprite, string outPath)
            {
                if (sprite == null) return false;

                var (tex, srcRect) = (sprite.Texture, sprite.SourceRectangle);
                if (tex == null) return false;

                if (srcRect.Width <= 0 || srcRect.Height <= 0)
                {
                    // No source rect -> whole texture.
                    return SaveTex(tex, outPath);
                }

                return SaveRegionViaRT(tex, srcRect, outPath);
            }

            /// <summary>
            /// Render a sub-rectangle of <paramref name="src"/> into an RT and save as PNG,
            /// preserving alpha. Uses a private SpriteBatch to avoid touching game state.
            /// </summary>
            private static bool SaveRegionViaRT(Texture2D src, Rectangle region, string outPath)
            {
                if (src == null || src.IsDisposed) return false;
                var gd = src.GraphicsDevice;
                if (gd == null) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                var saved = gd.GetRenderTargets();
                try
                {
                    using (var rt = new RenderTarget2D(
                        gd, region.Width, region.Height, false, SurfaceFormat.Color, DepthFormat.None,
                        0, RenderTargetUsage.PreserveContents))
                    {
                        gd.SetRenderTarget(rt);
                        gd.Viewport = new Viewport(0, 0, region.Width, region.Height);
                        gd.Clear(new Color(0, 0, 0, 0));

                        if (_sb == null || _sb.GraphicsDevice != gd) _sb = new SpriteBatch(gd);

                        _sb.Begin(SpriteSortMode.Deferred,
                                  BlendState.Opaque,
                                  SamplerState.PointClamp,
                                  DepthStencilState.None,
                                  RasterizerState.CullNone);
                        _sb.Draw(src,
                                 destinationRectangle: new Rectangle(0, 0, region.Width, region.Height),
                                 sourceRectangle: region,
                                 color: Color.White);
                        _sb.End();

                        gd.SetRenderTarget(null);

                        using (var fs = File.Create(outPath))
                            rt.SaveAsPng(fs, rt.Width, rt.Height);
                    }
                    return true;
                }
                finally
                {
                    if (saved != null && saved.Length > 0) gd.SetRenderTargets(saved);
                    else gd.SetRenderTarget(null);
                }
            }

            /// <summary>
            /// Basic Texture2D -> PNG helper.
            /// </summary>
            static bool SaveTex(Texture2D tex, string path)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var fs = File.Create(path))
                        tex.SaveAsPng(fs, tex.Width, tex.Height);
                    return true;
                }
                catch { return false; }
            }
        }

        // Thin wrapper to keep the TexturePackExtractor entry nice and small.
        // Call ScreenExporter.ExportScreens(...) behind a simple name.
        static void ExportScreens(CastleMinerZGame gm, string dir)
            => ScreenExporter.ExportScreens(gm, dir);

        #endregion

        #region Blocks (Terrain Atlas Faces)

        /// <summary>
        /// Terrain block face exporter:
        ///   • Resolves the near terrain atlas
        ///   • Builds a face map (Top / Sides / Bottom)
        ///   • Saves each referenced tile as:
        ///       BlockName_top.png
        ///       BlockName_side.png / _side2 / _side3...
        ///       BlockName_bottom.png
        /// </summary>
        public static class BlockExporter
        {
            // Entry point you can call from a hotkey
            /// <summary>
            /// Exports each terrain-backed block's face textures (Top / Side / Bottom) from the near terrain atlas
            /// into !Mods/TexturePacks/_Export/Blocks (or a custom folder).
            /// - Top:    "BlockName_top.png"    (if present)
            /// - Side:   "BlockName_side.png"   (first distinct side); if multiple, also _side2, _side3...
            /// - Bottom: "BlockName_bottom.png" (if present)
            /// Returns the number of PNG files written.
            /// </summary>
            public static int ExportAllFaces(string outDir = null, bool overwrite = true)
            {
                // Find near/primary atlas (diffuse+alpha). We export from the near one.
                if (!ResolveBlockAtlas(false, out AtlasInfo near) || near == null || near.Atlas == null || near.Atlas.IsDisposed)
                {
                    Log("[Export] Near terrain atlas not found.");
                    return 0;
                }

                // Build face map (Top/Bottom/Sides) from engine data once.
                EnsureFaceMap();
                if (_faceMapByName == null || _faceMapByName.Count == 0)
                {
                    Log("[Export] Face map empty; nothing to export.");
                    return 0;
                }

                // Output directory.
                if (string.IsNullOrWhiteSpace(outDir))
                    outDir = Path.Combine(PacksRoot, "_Export", "Blocks");

                try { Directory.CreateDirectory(outDir); }
                catch (Exception ex)
                {
                    Log($"[Export] Failed to create export directory: {ex.Message}.");
                    return 0;
                }

                int written = 0;

                foreach (var kv in _faceMapByName)
                {
                    var blockName = kv.Key; // Canonical enum name (e.g., "Grass").
                    var map = kv.Value;     // Top/Bottom/Sides/All.

                    // Top.
                    if (map.Top.HasValue)
                    {
                        string path = Path.Combine(outDir, SafeFileName(blockName + "_top") + ".png");
                        if (overwrite || !File.Exists(path))
                        {
                            if (SaveTilePNG(near.Atlas, TileRectAt(map.Top.Value, near), path))
                                written++;
                        }
                    }

                    // Side(s).
                    if (map.Sides != null && map.Sides.Length > 0)
                    {
                        // Export the first distinct side as "..._side.png".
                        string p0 = Path.Combine(outDir, SafeFileName(blockName + "_side") + ".png");
                        if (overwrite || !File.Exists(p0))
                        {
                            if (SaveTilePNG(near.Atlas, TileRectAt(map.Sides[0], near), p0))
                                written++;
                        }

                        // If there are additional distinct side indices, export them as _side2, _side3...
                        for (int i = 1; i < map.Sides.Length; i++)
                        {
                            string pn = Path.Combine(outDir, SafeFileName(blockName + "_side" + (i + 1)) + ".png");
                            if (overwrite || !File.Exists(pn))
                            {
                                if (SaveTilePNG(near.Atlas, TileRectAt(map.Sides[i], near), pn))
                                    written++;
                            }
                        }
                    }

                    // Bottom.
                    if (map.Bottom.HasValue)
                    {
                        string path = Path.Combine(outDir, SafeFileName(blockName + "_bottom") + ".png");
                        if (overwrite || !File.Exists(path))
                        {
                            if (SaveTilePNG(near.Atlas, TileRectAt(map.Bottom.Value, near), path))
                                written++;
                        }
                    }
                }

                Log($"[Export] Wrote {written} face PNG(s) to \"{ShortenForLog(outDir)}\".");
                return written;
            }

            #region Models: Doors (Full-Sheet Export)

            /// <summary>
            /// Exports the live vanilla full-sheet door model textures into
            /// !Mods/TexturePacks/_Export/Models/Doors (or a custom folder).
            /// </summary>
            /// <param name="outDir">Optional custom output directory.</param>
            /// <param name="overwrite">True to overwrite existing PNG files.</param>
            /// <returns>The number of PNG files written.</returns>
            /// <remarks>
            /// NOTE:
            /// - These are full-sheet door model textures, not block-face tiles.
            /// - Output filenames are:
            ///     NormalDoor.png
            ///     IronDoor.png
            ///     DiamondDoor.png
            ///     TechDoor.png
            /// </remarks>
            public static int ExportDoorSheets(string outDir = null, bool overwrite = true)
            {
                if (string.IsNullOrWhiteSpace(outDir))
                    outDir = Path.Combine(PacksRoot, "_Export", "Models", "Doors");

                try { Directory.CreateDirectory(outDir); }
                catch (Exception ex)
                {
                    Log($"[Export] Failed to create door export directory: {ex.Message}.");
                    return 0;
                }

                CaptureDoorVanillaBaselinesIfNeeded();

                int written = 0;

                foreach (var kv in _doorSkinVanilla)
                {
                    var modelName = kv.Key;
                    var tex = kv.Value;

                    if (tex == null || tex.IsDisposed)
                        continue;

                    string fileName;
                    switch (modelName)
                    {
                        case DoorEntity.ModelNameEnum.Wood:
                            fileName = "NormalDoor.png";
                            break;

                        case DoorEntity.ModelNameEnum.Iron:
                            fileName = "IronDoor.png";
                            break;

                        case DoorEntity.ModelNameEnum.Diamond:
                            fileName = "DiamondDoor.png";
                            break;

                        case DoorEntity.ModelNameEnum.Tech:
                            fileName = "TechDoor.png";
                            break;

                        default:
                            fileName = modelName.ToString() + ".png";
                            break;
                    }

                    var path = Path.Combine(outDir, fileName);

                    if (!overwrite && File.Exists(path))
                        continue;

                    try
                    {
                        if (SaveTextureAsPng(tex, path, true))
                            written++;
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Failed to export door sheet '{fileName}': {ex.Message}.");
                    }
                }

                Log($"[Export] Wrote {written} door sheet PNG(s) to \"{ShortenForLog(outDir)}\".");
                return written;
            }
            #endregion

            /// <summary>Build a safe filename stem (keeps letters, digits, underscore).</summary>
            private static string SafeFileName(string stem)
            {
                if (string.IsNullOrEmpty(stem)) return "untitled";
                var sb = new System.Text.StringBuilder(stem.Length);
                for (int i = 0; i < stem.Length; i++)
                {
                    char c = stem[i];
                    if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                    else if (c == ' ' || c == '-') sb.Append('_');
                    // else drop.
                }
                return sb.Length > 0 ? sb.ToString() : "untitled";
            }

            /// <summary>
            /// Save a single tile rectangle from <paramref name="atlas"/> as a PNG to <paramref name="path"/>.
            /// Tries MonoGame's Texture2D.SaveAsPng if available; otherwise falls back to ImageSharp.
            /// Preserves alpha.
            /// </summary>
            public static bool SaveTilePNG(Texture2D atlas, Rectangle src, string path)
            {
                try
                {
                    // Read pixels (no RTs bound).
                    var gd = atlas.GraphicsDevice;
                    var pixels = new Color[src.Width * src.Height];
                    WithNoRenderTargets(gd, () => atlas.GetData(0, src, pixels, 0, pixels.Length));

                    // Try MonoGame SaveAsPng via a temp texture (XNA doesn't have this; MonoGame does).
                    try
                    {
                        using (var tex = new Texture2D(gd, src.Width, src.Height))
                        {
                            tex.SetData(pixels);

                            var mi = typeof(Texture2D).GetMethod(
                                "SaveAsPng",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(Stream), typeof(int), typeof(int) },
                                null);

                            if (mi != null)
                            {
                                using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                                    mi.Invoke(tex, new object[] { fs, src.Width, src.Height });
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Fall through to ImageSharp.
                    }

                    // Fallback: ImageSharp.
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"[Export] SaveTilePNG failed: {ex.Message}.");
                    return false;
                }
            }

            // --- internals ---

            private sealed class AtlasInfo
            {
                public Texture2D Atlas; // Texture object.
                public int TileSize;    // Pixels at mip 0.
                public int Cols;        // Tiles per row at mip 0.
                public int Rows;        // Tiles per column at mip 0.
            }

            /// <summary>
            /// Resolve terrain atlas information.
            /// When <paramref name="preferMip"/> is true, attempts the mip/distance atlas first,
            /// otherwise the near/primary atlas. Returns false if no suitable atlas exists.
            /// </summary>
            private static bool ResolveBlockAtlas(bool preferMip, out AtlasInfo info)
            {
                info = null;

                Texture2D atlas = null;
                if (preferMip)
                    atlas = FindTerrainMipDiffuseTexture();
                if (atlas == null || atlas.IsDisposed)
                    atlas = FindTerrainAtlasTexture();

                if (atlas == null || atlas.IsDisposed)
                    return false;

                // CMZ atlases are 8 tiles across (engine convention).
                // Fall back to 8 if the texture isn't width-divisible (defensive).
                int tile = Math.Max(1, atlas.Width / 8);
                if (tile * 8 != atlas.Width)
                    tile = Math.Max(1, atlas.Width / 8); // Keep it forgiving but consistent.

                int cols = Math.Max(1, atlas.Width / tile);
                int rows = Math.Max(1, atlas.Height / tile);

                info = new AtlasInfo
                {
                    Atlas = atlas,
                    TileSize = tile,
                    Cols = cols,
                    Rows = rows
                };
                return true;
            }

            /// <summary>Convenience helper to compute a tile rect at mip 0.</summary>
            private static Rectangle TileRectAt(int tileIndex, AtlasInfo ai)
            {
                int x = (tileIndex % ai.Cols) * ai.TileSize;
                int y = (tileIndex / ai.Cols) * ai.TileSize;
                return new Rectangle(x, y, ai.TileSize, ai.TileSize);
            }

            private static string GetStringProp(object obj, string name)
            {
                if (obj == null) return null;
                var p = obj.GetType().GetProperty(name, BF.InstanceAll);
                if (p != null && p.PropertyType == typeof(string))
                    return p.GetValue(obj, null) as string;
                return null;
            }

            private static class BF
            {
                public const BindingFlags InstanceAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                public const BindingFlags StaticAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                public const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
            }

            private struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
        }

        // Thin wrapper so TexturePackExtractor.ExportAll() reads nicely.
        static void ExportBlocks(string dir)
            => BlockExporter.ExportAllFaces(dir);

        // Thin wrapper so TexturePackExtractor.ExportAll() reads nicely.
        static void ExportDoorModelSheets(string dir)
            => BlockExporter.ExportDoorSheets(dir);

        #endregion

        #region Sprites (HUD / UI Atlases)

        /// <summary>
        /// UI sprite exporter:
        ///   • Reflects CastleMinerZGame._uiSprites (various shapes: dictionaries, etc.)
        ///   • Resolves textures + source rectangles
        ///   • Writes each as key.png to the target folder.
        /// </summary>
        static class UISpriteExporter
        {
            public static void ExportUISprites(GraphicsDevice gd, CastleMinerZGame gm, string dir)
            {
                Directory.CreateDirectory(dir);

                var ui = AccessTools.Field(gm.GetType(), "_uiSprites")?.GetValue(gm);
                if (ui == null) { Log("[Export] _uiSprites not found."); return; }

                // Try common shapes: Dictionary<string, TSprite>, or something with .Sprites/.Keys, indexer.
                IEnumerable keys = TryKeys(ui) ?? TryDictionaryKeys(ui);
                if (keys == null) { Log("[Export] Could not enumerate UI sprites."); return; }

                int exportCount = 0;
                foreach (var keyObj in keys)
                {
                    string key = keyObj?.ToString() ?? "sprite";
                    var sprite = TryGetByKey(ui, key);
                    if (sprite == null) continue;

                    var tex = FindTex(sprite);
                    var src = FindSrcRect(sprite);
                    if (tex == null || src == null) continue;

                    bool save = SaveRegion(gd, tex, src.Value, Path.Combine(dir, San(key) + ".png"));
                    if (save) exportCount++;
                }
                Log($"[Export] Wrote {exportCount} sprite files to \"{ShortenForLog(dir)}\".");
            }

            // ---- Reflection helpers for key enumeration ---

            static IEnumerable TryKeys(object ui)
            {
                var pi = ui.GetType().GetProperty("Keys", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null) return pi.GetValue(ui, null) as IEnumerable;
                var fi = ui.GetType().GetField("Keys", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return fi?.GetValue(ui) as IEnumerable;
            }

            static IEnumerable TryDictionaryKeys(object ui)
            {
                foreach (var f in ui.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ft = f.FieldType;
                    if (!ft.IsGenericType) continue;
                    if (ft.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                    var dict = f.GetValue(ui) as IDictionary;
                    return dict?.Keys;
                }
                return null;
            }

            static object TryGetByKey(object ui, string key)
            {
                // Indexer this[string].
                var idx = ui.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              .FirstOrDefault(p =>
                                  p.GetIndexParameters().Length == 1 &&
                                  p.GetIndexParameters()[0].ParameterType == typeof(string));
                if (idx != null) return idx.GetValue(ui, new object[] { key });

                // Inner dictionary.
                foreach (var f in ui.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ft = f.FieldType;
                    if (!ft.IsGenericType) continue;
                    if (ft.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                    if (f.GetValue(ui) is IDictionary dict && dict.Contains(key)) return dict[key];
                }
                return null;
            }

            static Texture2D FindTex(object sprite)
            {
                var pi = sprite.GetType().GetProperty("Texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && typeof(Texture2D).IsAssignableFrom(pi.PropertyType)) return (Texture2D)pi.GetValue(sprite, null);
                var fi = sprite.GetType().GetField("_texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                      ?? sprite.GetType().GetField("Texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null && typeof(Texture2D).IsAssignableFrom(fi.FieldType)) return (Texture2D)fi.GetValue(sprite);
                return null;
            }

            static Rectangle? FindSrcRect(object sprite)
            {
                var pi = sprite.GetType().GetProperty("Source", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                      ?? sprite.GetType().GetProperty("SourceRectangle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.PropertyType == typeof(Rectangle)) return (Rectangle)pi.GetValue(sprite, null);
                var fi = sprite.GetType().GetField("_source", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                      ?? sprite.GetType().GetField("Source", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null && fi.FieldType == typeof(Rectangle)) return (Rectangle)fi.GetValue(sprite);
                return null;
            }

            /// <summary>
            /// Crop a region from <paramref name="src"/> and save it as PNG.
            /// </summary>
            static bool SaveRegion(GraphicsDevice gd, Texture2D src, Rectangle rect, string path)
            {
                try
                {
                    var pixels = new Color[rect.Width * rect.Height];
                    src.GetData(0, rect, pixels, 0, pixels.Length);
                    using (var tex = new Texture2D(gd, rect.Width, rect.Height))
                    {
                        tex.SetData(pixels);
                        using (var fs = File.Create(path))
                            tex.SaveAsPng(fs, rect.Width, rect.Height);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[Export] UI sprite fail: {Path.GetFileName(path)}: {ex.Message}.");
                    return false;
                }
            }

            static string San(string s)
            {
                foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
                return s;
            }
        }

        // Thin wrapper so TexturePackExtractor.ExportAll() reads clearly.
        static void ExportUISprites(GraphicsDevice gd, CastleMinerZGame gm, string dir)
            => UISpriteExporter.ExportUISprites(gd, gm, dir);

        #endregion

        #region Misc Models (Field / Static Models To GLB)

        // =====================================================================================
        // Overview
        // =====================================================================================
        // This section contains a grab-bag model export utility intended for "misc" Model
        // instances that aren't part of the normal content-table flows.
        //
        // It supports two export paths:
        //
        //   (1) OBJ/MTL exporter (TryExportObj)
        //       - Writes geometry to .obj + .mtl, optionally dumping per-material textures.
        //       - Useful for quick inspection, but OBJ cannot represent a real bone/node tree.
        //
        //   (2) GLB exporter (TryExportGlb)
        //       - Writes a single .glb containing: node/bone hierarchy + mesh primitives (+ textures).
        //       - Preserves exact bone names (e.g., BarrelTip) for Blender->FBX->XNA round-trips.
        //
        // Additionally, ExportMiscModelsGlb() provides an "Extras" export mode that routes
        // reflection-discovered models into organized subfolders (Dragons/Enemies/Misc).
        // =====================================================================================

        /// <summary>
        /// Misc model exporter:
        ///   • Scans CastleMinerZGame instance fields for Model.
        ///   • Scans all static Model fields across the assembly.
        ///   • Writes .obj/.mtl + per-material textures via TryExportObj().
        /// </summary>
        static class ModelExporter
        {
            #region Entry Points

            /// <summary>
            /// Exports reflection-discovered Model fields under the provided directory.
            /// This entry point currently exports using the OBJ/MTL path.
            /// </summary>
            public static void ExportMiscModels(CastleMinerZGame gm, string dir)
            {
                Directory.CreateDirectory(dir);

                int exportCount = 0;
                foreach (var (owner, fi) in FindModelFields(gm))
                {
                    if (!(fi.GetValue(owner) is Model model)) continue;

                    var safeName = $"{fi.DeclaringType.Name}.{fi.Name}";
                    var basePath = Path.Combine(dir, San(safeName));

                    bool save = TryExportObj(model, basePath);
                    if (save) exportCount++;
                }
                Log($"[Export] Wrote {exportCount} extra models to \"{ShortenForLog(dir)}\".");
            }

            /// <summary>
            /// Misc model exporter (GLB):
            ///   • Scans CastleMinerZGame instance fields for Model.
            ///   • Scans all static Model fields across the assembly.
            ///   • Writes ONE .glb per discovered Model using TryExportGlb().
            ///
            /// Output:
            ///   <extrasDir>\Dragons\*.glb
            ///   <extrasDir>\Enemies\*.glb
            ///   <extrasDir>\Misc\*.glb
            /// </summary>
            public static void ExportMiscModelsGlb(
                CastleMinerZGame gm,
                string extrasDir,
                bool overwrite,
                HashSet<Model> globalSeen)
            {
                Directory.CreateDirectory(extrasDir);

                int exportCount = 0;

                foreach (var (owner, fi) in FindModelFields(gm))
                {
                    Model model = null;
                    try { model = fi.GetValue(owner) as Model; } catch { }
                    if (model == null) continue;

                    // Choose a subfolder so we end up with:
                    // Models\Extras\Dragons, Models\Extras\Enemies, Models\Extras\Misc.
                    string sub = PickExtrasSubfolder(fi);

                    var outDir = Path.Combine(extrasDir, sub);
                    Directory.CreateDirectory(outDir);

                    var safeName = $"{fi.DeclaringType.Name}.{fi.Name}";
                    var outPath = Path.Combine(outDir, San(safeName) + ".glb");

                    // Global dedupe across everything (player/enemies/dragons/items/extras).
                    if (globalSeen != null && !globalSeen.Add(model))
                        continue;

                    if (!overwrite && File.Exists(outPath))
                        continue;

                    if (TryExportGlb(model, outPath, overwrite, embedTextures: true))
                        exportCount++;
                }

                Log($"[Export] Wrote {exportCount} extra GLB model(s) to \"{ShortenForLog(extrasDir)}\".");
            }
            #endregion

            #region Reflection Scanning

            /// <summary>
            /// Enumerate Model-typed fields on the game instance and on all static types
            /// in the CastleMinerZGame assembly.
            /// </summary>
            static IEnumerable<(object owner, FieldInfo fi)> FindModelFields(CastleMinerZGame gm)
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                // Instance fields.
                foreach (var f in gm.GetType().GetFields(flags))
                    if (typeof(Model).IsAssignableFrom(f.FieldType))
                        yield return (gm, f);

                // Static caches across assembly (common pattern).
                foreach (var t in typeof(CastleMinerZGame).Assembly.GetTypes())
                {
                    FieldInfo[] fs;
                    try { fs = t.GetFields(flags); } catch { continue; }
                    foreach (var f in fs)
                        if (f.IsStatic && typeof(Model).IsAssignableFrom(f.FieldType))
                            yield return (null, f);
                }
            }

            /// <summary>
            /// Heuristic routing for Extras subfolders based on declaring type + field name.
            /// Keeps output organized without needing any hand-maintained lists.
            /// </summary>
            private static string PickExtrasSubfolder(FieldInfo fi)
            {
                string s = (fi?.DeclaringType?.FullName ?? "") + "." + (fi?.Name ?? "");

                if (s.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Dragons";

                if (s.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("Zombie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("Skeleton", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Enemies";

                return "Misc";
            }
            #endregion

            #region OBJ Export (Legacy / Quick Inspection)

            /// <summary>
            /// Core OBJ/MTL exporter for a single Model.
            /// Writes:
            ///   <basePathNoExt>.obj
            ///   <basePathNoExt>.mtl
            /// Plus any texture PNGs next to them.
            /// </summary>
            internal static bool TryExportObj(Model model, string basePathNoExt)
            {
                try
                {
                    var objPath = basePathNoExt + ".obj";
                    var mtlPath = basePathNoExt + ".mtl";

                    using (var obj = new StreamWriter(objPath, false))
                    using (var mtl = new StreamWriter(mtlPath, false))
                    {

                        obj.WriteLine("# exported by TexturePackExtractor");
                        obj.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");

                        var abs = new Matrix[model.Bones.Count];
                        model.CopyAbsoluteBoneTransformsTo(abs);

                        int vbase = 1; // OBJ indices start at 1.
                        int tbase = 1;
                        int nbase = 1;
                        int matIndex = 0;

                        foreach (var mesh in model.Meshes)
                        {
                            var world = abs[mesh.ParentBone.Index];

                            foreach (var part in mesh.MeshParts)
                            {
                                // Try to get a texture from effect.
                                Texture2D tex = null;
                                if (part.Effect is BasicEffect be && be.TextureEnabled && be.Texture is Texture2D efTex)
                                    tex = efTex;

                                var matName = $"m{matIndex++}";
                                mtl.WriteLine($"newmtl {matName}");
                                if (tex != null)
                                {
                                    var texName = $"{Path.GetFileNameWithoutExtension(basePathNoExt)}_{matName}.png";
                                    mtl.WriteLine($"map_Kd {texName}");
                                    SaveTextureSafe(tex, Path.Combine(Path.GetDirectoryName(basePathNoExt), texName));
                                }
                                else
                                {
                                    mtl.WriteLine("Kd 0.8 0.8 0.8");
                                }

                                obj.WriteLine($"usemtl {matName}");
                                obj.WriteLine($"o {mesh.Name}_{matName}");

                                // Read vertex data (we only fully support PNT; other layouts get position only).
                                var vdecl = part.VertexBuffer.VertexDeclaration;
                                // LogVertexDecl($"[GLB] {mesh.Name} part", vdecl); // Debugging model details.
                                int stride = vdecl.VertexStride;
                                var vdata = new byte[stride * part.NumVertices];
                                part.VertexBuffer.GetData<byte>(part.VertexOffset * stride, vdata, 0, vdata.Length, 1);


                                // Indices.
                                var iCount = part.PrimitiveCount * 3;
                                var indices = new int[iCount];
                                bool sixteen = part.IndexBuffer.IndexElementSize == IndexElementSize.SixteenBits;
                                if (sixteen)
                                {
                                    var buf = new ushort[part.IndexBuffer.IndexCount];
                                    part.IndexBuffer.GetData(buf);
                                    for (int i = 0; i < iCount; i++)
                                        indices[i] = buf[part.StartIndex + i] - part.VertexOffset;
                                }
                                else
                                {
                                    var buf = new int[part.IndexBuffer.IndexCount];
                                    part.IndexBuffer.GetData(buf);
                                    for (int i = 0; i < iCount; i++)
                                        indices[i] = buf[part.StartIndex + i] - part.VertexOffset;
                                }

                                // Parse vertices.
                                var hasN = false; var hasT = false;
                                int posOffset = -1, normOffset = -1, texOffset = -1;

                                foreach (var e in vdecl.GetVertexElements())
                                {
                                    if (e.VertexElementUsage == VertexElementUsage.Position && e.VertexElementFormat == VertexElementFormat.Vector3) posOffset = e.Offset;
                                    if (e.VertexElementUsage == VertexElementUsage.Normal && e.VertexElementFormat == VertexElementFormat.Vector3) { normOffset = e.Offset; hasN = true; }
                                    if (e.VertexElementUsage == VertexElementUsage.TextureCoordinate && (e.VertexElementFormat == VertexElementFormat.Vector2 || e.VertexElementFormat == VertexElementFormat.HalfVector2))
                                    { texOffset = e.Offset; hasT = true; }
                                }

                                // Emit v/vt/vn.
                                for (int v = 0; v < part.NumVertices; v++)
                                {
                                    int o = v * stride;

                                    // Position.
                                    var px = BitConverter.ToSingle(vdata, o + posOffset + 0);
                                    var py = BitConverter.ToSingle(vdata, o + posOffset + 4);
                                    var pz = BitConverter.ToSingle(vdata, o + posOffset + 8);
                                    var p = Vector3.Transform(new Vector3(px, py, pz), world);
                                    obj.WriteLine($"v {p.X:R} {p.Y:R} {p.Z:R}");

                                    // Texcoord.
                                    if (hasT && texOffset >= 0)
                                    {
                                        float tu = BitConverter.ToSingle(vdata, o + texOffset + 0);
                                        float tv = BitConverter.ToSingle(vdata, o + texOffset + 4);
                                        obj.WriteLine($"vt {tu:R} {1f - tv:R}");
                                    }

                                    // Normal.
                                    if (hasN && normOffset >= 0)
                                    {
                                        var nx = BitConverter.ToSingle(vdata, o + normOffset + 0);
                                        var ny = BitConverter.ToSingle(vdata, o + normOffset + 4);
                                        var nz = BitConverter.ToSingle(vdata, o + normOffset + 8);
                                        var n = Vector3.Normalize(Vector3.TransformNormal(new Vector3(nx, ny, nz), world));
                                        obj.WriteLine($"vn {n.X:R} {n.Y:R} {n.Z:R}");
                                    }
                                }

                                // Faces.
                                for (int i = 0; i < iCount; i += 3)
                                {
                                    int a = indices[i + 0] + vbase;
                                    int b = indices[i + 1] + vbase;
                                    int c = indices[i + 2] + vbase;

                                    if (hasN && hasT)
                                        obj.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
                                    else if (hasT)
                                        obj.WriteLine($"f {a}/{a} {b}/{b} {c}/{c}");
                                    else if (hasN)
                                        obj.WriteLine($"f {a}//{a} {b}//{b} {c}//{c}");
                                    else
                                        obj.WriteLine($"f {a} {b} {c}");
                                }

                                vbase += part.NumVertices;
                                tbase = vbase; // Keep aligned indices for vt/vn style.
                                nbase = vbase;
                            }
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Export] Model export fail: {ex.Message}.");
                    return false;
                }
            }
            #endregion

            #region GLB Export (Meshes + Bones In ONE File)

            // =====================================================================================
            // Overview
            // =====================================================================================
            // This region exports XNA Models to single-file GLB for inspection and Blender round-trips.
            //
            // There are TWO export paths:
            //
            //  (1) TryExportGlb_CharacterStructure (skinned-like / character models)
            //      - Detects BlendIndices + BlendWeight in any mesh-part (heuristic).
            //      - Forces a "vanilla-ish" hierarchy and stable naming:
            //          RootNode
            //            Warrior_mesh
            //              M_Warrior_mesh
            //              Root_Bone
            //                Bip01...
            //      - Reuses canonical NodeBuilders when bones already use names like "RootNode" and "Warrior_mesh"
            //        to avoid Blender-style duplicates (RootNode.001 / Warrior_mesh.001).
            //      - Special-case: forces Root_Bone to live under Warrior_mesh for animation compatibility.
            //
            //  (2) TryExportGlb_RigidLegacy (rigid/static models: items, guns, props)
            //      - Exports a single RootNode (no _EXPORT_ROOT/_ROOT wrapper nodes).
            //      - Reuses the scene RootNode if the model contains a bone named "RootNode"
            //        (prevents RootNode.001).
            //      - Attaches meshes under the mesh's ParentBone to preserve placement.
            //
            // Notes / Gotchas
            // --------------
            // - GLB preserves a node tree; OBJ does not.
            // - Bone/node NAME stability matters for later lookups (e.g., BarrelTip).
            // - XNA/FBX pipeline often treats *all* transform nodes as bones; extra nodes can shift indices.
            // - For the character path, avoid manually creating a mesh node then calling AddRigidMesh on it
            //   (SharpGLTF can create an extra child node; see the comment near AddRigidMesh).
            // =====================================================================================

            /// <summary>
            /// Core GLB exporter entry point.
            /// Summary:
            /// - Validates inputs and ensures output directory exists.
            /// - Uses a "skinned-like" heuristic to pick the appropriate exporter.
            /// </summary>
            internal static bool TryExportGlb(Model xnaModel, string glbPath, bool overwrite = true, bool embedTextures = true)
            {
                try
                {
                    // -------------------------------------------------------------
                    // Guardrails / IO
                    // -------------------------------------------------------------
                    if (xnaModel == null || string.IsNullOrWhiteSpace(glbPath))
                        return false;

                    if (!overwrite && File.Exists(glbPath))
                        return false;

                    try
                    {
                        var dir = Path.GetDirectoryName(glbPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);
                    }
                    catch { }

                    // -------------------------------------------------------------
                    // Global export scaling (FBX round-trip compensation)
                    // -------------------------------------------------------------
                    float FBX_COMP = TPConfig.LoadOrCreate().ModelFbxComp;

                    // -------------------------------------------------------------
                    // Export mode selection
                    // -------------------------------------------------------------
                    // If this looks like a character (BlendIndices+BlendWeight exist), export with the special structure.
                    bool skinnedLike = IsSkinnedLike(xnaModel);

                    if (skinnedLike)
                        return TryExportGlb_CharacterStructure(xnaModel, glbPath, FBX_COMP, embedTextures);

                    // Rigid/static (items/guns/etc.)
                    return TryExportGlb_RigidLegacy(xnaModel, glbPath, overwrite, embedTextures);
                }
                catch (Exception ex)
                {
                    Log($"[Export] GLB export fail: {ex}.");
                    return false;
                }
            }

            #region Character Export (Skinned-like: Enemies/Players/Dragons)

            /// <summary>
            /// Character-friendly GLB export that forces:
            ///   RootNode
            ///     Warrior_mesh
            ///       M_Warrior_mesh
            ///       Root_Bone
            ///         Bip01...
            ///
            /// Summary:
            /// - Creates canonical nodes for RootNode + "main mesh name" (Warrior_mesh) and reuses them when possible.
            /// - Builds the bone tree using XNA Model.Bones (names + transforms).
            /// - Special-case: if Root_Bone would attach under RootNode, force it under Warrior_mesh.
            /// - Exports geometry under Warrior_mesh (as M_<MeshName>).
            ///
            /// Notes:
            /// - Uses MeshBuilder name as the node name produced by AddRigidMesh to keep hierarchy clean.
            /// - Avoids manually CreateNode()+AddRigidMesh on the same node, which can create extra child nodes.
            /// </summary>
            private static bool TryExportGlb_CharacterStructure(Model xnaModel, string glbPath, float fbxComp, bool embedTextures)
            {
                try
                {
                    var scene = new SceneBuilder();

                    // -------------------------------------------------------------
                    // Choose "main" mesh container name (ALIEN: Warrior_mesh)
                    // -------------------------------------------------------------
                    var firstMesh = xnaModel.Meshes.FirstOrDefault(m => m != null);
                    string mainMeshName = (firstMesh != null && !string.IsNullOrWhiteSpace(firstMesh.Name))
                        ? firstMesh.Name
                        : "Warrior_mesh";

                    // -------------------------------------------------------------
                    // Canonical nodes (avoid duplicates like RootNode.001 / Warrior_mesh.001)
                    // -------------------------------------------------------------
                    var rootNode = new NodeBuilder("RootNode");
                    var warriorNode = new NodeBuilder(mainMeshName);

                    // Apply scale compensation at the scene root.
                    if (Math.Abs(fbxComp - 1f) > 0.000001f)
                        rootNode.UseScale().Value = new System.Numerics.Vector3(fbxComp, fbxComp, fbxComp);

                    // RootNode -> Warrior_mesh.
                    rootNode.AddNode(warriorNode);
                    scene.AddNode(rootNode);

                    // -------------------------------------------------------------
                    // Build bone hierarchy (boneIndex -> NodeBuilder)
                    // -------------------------------------------------------------
                    var boneNodes = new Dictionary<int, NodeBuilder>();

                    // Create nodes for every bone, reusing canonical nodes on name match.
                    foreach (var b in xnaModel.Bones)
                    {
                        if (b == null) continue;

                        string name = string.IsNullOrWhiteSpace(b.Name) ? $"Bone_{b.Index}" : b.Name;

                        if (name.Equals("RootNode", StringComparison.Ordinal))
                            boneNodes[b.Index] = rootNode;
                        else if (name.Equals(mainMeshName, StringComparison.Ordinal))
                            boneNodes[b.Index] = warriorNode;
                        else
                            boneNodes[b.Index] = new NodeBuilder(name);
                    }

                    // Parent + set TRS.
                    foreach (var b in xnaModel.Bones)
                    {
                        if (b == null) continue;

                        var node = boneNodes[b.Index];

                        // Local TRS from XNA bone.Transform.
                        b.Transform.Decompose(out Vector3 s, out Quaternion r, out Vector3 t);
                        node.UseTranslation().Value = new System.Numerics.Vector3(t.X, t.Y, t.Z);
                        node.UseRotation().Value    = new System.Numerics.Quaternion(r.X, r.Y, r.Z, r.W);
                        node.UseScale().Value       = new System.Numerics.Vector3(s.X, s.Y, s.Z);

                        // Parent assignment.
                        if (b.Parent != null && boneNodes.TryGetValue(b.Parent.Index, out var parentNode))
                        {
                            // Avoid self-parenting.
                            if (ReferenceEquals(parentNode, node))
                                continue;

                            // Force Root_Bone under Warrior_mesh (so: RootNode -> Warrior_mesh -> Root_Bone -> ...).
                            bool isRootBone = string.Equals(b.Name, "Root_Bone", StringComparison.Ordinal);

                            if (isRootBone && ReferenceEquals(parentNode, rootNode))
                                warriorNode.AddNode(node);
                            else
                                parentNode.AddNode(node);
                        }
                        else
                        {
                            // Root bones:
                            // - RootNode stays the scene root
                            // - Warrior_mesh stays under RootNode
                            // - Everything else goes under Warrior_mesh
                            if (ReferenceEquals(node, rootNode))
                            {
                                // Already in scene.
                            }
                            else if (ReferenceEquals(node, warriorNode))
                            {
                                // Already under RootNode.
                            }
                            else
                            {
                                warriorNode.AddNode(node);
                            }
                        }
                    }

                    // -------------------------------------------------------------
                    // Export geometry under Warrior_mesh as M_<meshname>[_n]
                    // -------------------------------------------------------------
                    // Intended hierarchy:
                    // RootNode
                    //   Warrior_mesh
                    //     M_Warrior_mesh
                    //     Root_Bone
                    // -------------------------------------------------------------
                    var texMatCache = new Dictionary<Texture2D, MaterialBuilder>(new ReferenceEqualityComparerTexture2D());

                    int meshCounter = 0;

                    foreach (var mesh in xnaModel.Meshes)
                    {
                        if (mesh == null) continue;

                        // Node name exactly under Warrior_mesh.
                        // Multiple meshes get a suffix so names remain unique.
                        string nodeMeshName = (meshCounter == 0)
                            ? $"M_{mesh.Name}"
                            : $"M_{mesh.Name}_{meshCounter}";

                        // IMPORTANT:
                        // Use the MeshBuilder name as the produced node name.
                        var gltfMesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(nodeMeshName);

                        int partCounter = 0;

                        foreach (var part in mesh.MeshParts)
                        {
                            if (part == null) continue;

                            // Material per part (best-effort texture extraction from BasicEffect).
                            Texture2D tex = null;
                            if (part.Effect is BasicEffect be && be.TextureEnabled && be.Texture is Texture2D efTex)
                                tex = efTex;

                            var mat  = BuildMaterial(tex, $"{nodeMeshName}_P{partCounter:000}", texMatCache, embedTextures);
                            var prim = gltfMesh.UsePrimitive(mat);

                            // Vertex buffer.
                            var vdecl  = part.VertexBuffer.VertexDeclaration;
                            int stride = vdecl.VertexStride;

                            var vdata = new byte[stride * part.NumVertices];
                            part.VertexBuffer.GetData<byte>(part.VertexOffset * stride, vdata, 0, vdata.Length, 1);

                            // Indices (triangles).
                            int iCount  = part.PrimitiveCount * 3;
                            var indices = ReadIndices(part, iCount);

                            // Vertex element offsets.
                            GetOffsets(vdecl,
                                out int posOffset, out var posFmt,
                                out int normOffset, out var normFmt,
                                out int texOffset, out var texFmt,
                                out int colOffset, out var colFmt);

                            if (posOffset < 0) { partCounter++; continue; }

                            // Emit triangles.
                            for (int i = 0; i < iCount; i += 3)
                            {
                                int ia = indices[i + 0];
                                int ib = indices[i + 1];
                                int ic = indices[i + 2];

                                if ((uint)ia >= (uint)part.NumVertices ||
                                    (uint)ib >= (uint)part.NumVertices ||
                                    (uint)ic >= (uint)part.NumVertices)
                                    continue;

                                var va = ReadVertex(vdata, stride, ia, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool na);
                                var vb = ReadVertex(vdata, stride, ib, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool nb);
                                var vc = ReadVertex(vdata, stride, ic, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool nc);

                                FixupNormalIfMissing(ref va, ref vb, ref vc, na && nb && nc);
                                prim.AddTriangle(va, vb, vc);
                            }

                            partCounter++;
                        }

                        // CRITICAL:
                        // Don't CreateNode() yourself (it can cause an extra ".00X" child).
                        // Let AddRigidMesh create the single child node named after the MeshBuilder.
                        scene.AddRigidMesh(gltfMesh, warriorNode);

                        meshCounter++;
                    }

                    scene.ToGltf2().SaveGLB(glbPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[Export] GLB character export fail: {ex}.");
                    return false;
                }
            }
            #endregion

            #region Export Mode Heuristic

            /// <summary>
            /// Heuristic: treat as skinned-like if any mesh part contains both BlendIndices and BlendWeight.
            /// Summary: this identifies "character" style models that benefit from stable node ordering.
            /// </summary>
            private static bool IsSkinnedLike(Model m)
            {
                try
                {
                    foreach (var mesh in m.Meshes)
                        foreach (var part in mesh.MeshParts)
                        {
                            var decl = part?.VertexBuffer?.VertexDeclaration;
                            if (decl == null) continue;

                            bool hasBI = false, hasBW = false;
                            foreach (var e in decl.GetVertexElements())
                            {
                                if (e.VertexElementUsage == VertexElementUsage.BlendIndices) hasBI = true;
                                if (e.VertexElementUsage == VertexElementUsage.BlendWeight) hasBW = true;
                            }
                            if (hasBI && hasBW) return true;
                        }
                }
                catch { }
                return false;
            }
            #endregion

            #region Rigid Export (Static Items / Guns / Props)

            /// <summary>
            /// Rigid/static GLB export (items/guns/etc).
            /// Summary:
            /// - Uses a single RootNode (no _EXPORT_ROOT/_ROOT wrappers).
            /// - Reuses RootNode if the model has a bone named "RootNode" (prevents RootNode.001).
            /// - Parents each mesh under its ParentBone node.
            /// </summary>
            private static bool TryExportGlb_RigidLegacy(Model xnaModel, string glbPath, bool overwrite, bool embedTextures)
            {
                try
                {
                    if (xnaModel == null || string.IsNullOrWhiteSpace(glbPath))
                        return false;

                    if (!overwrite && File.Exists(glbPath))
                        return false;

                    try
                    {
                        var dir = Path.GetDirectoryName(glbPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);
                    }
                    catch { }

                    float FBX_COMP = TPConfig.LoadOrCreate().ModelFbxComp;

                    var scene = new SceneBuilder();

                    // Single root node (no _EXPORT_ROOT / _ROOT)
                    var root = new NodeBuilder("RootNode");
                    scene.AddNode(root);

                    // If the model does NOT already have a RootNode bone, apply scale comp directly here.
                    // (If it DOES have a RootNode bone, we'll apply comp when setting that bone's scale, below.)
                    bool hasRootNodeBone = false;
                    foreach (var b in xnaModel.Bones)
                    {
                        if (b != null && string.Equals(b.Name, "RootNode", StringComparison.Ordinal))
                        {
                            hasRootNodeBone = true;
                            break;
                        }
                    }

                    if (!hasRootNodeBone && Math.Abs(FBX_COMP - 1f) > 0.000001f)
                        root.UseScale().Value = new System.Numerics.Vector3(FBX_COMP, FBX_COMP, FBX_COMP);

                    // boneIndex -> node.
                    var boneNodes = new Dictionary<int, NodeBuilder>();

                    // Create nodes for every bone, but REUSE the scene root when a bone is named "RootNode"
                    foreach (var b in xnaModel.Bones)
                    {
                        if (b == null) continue;

                        string name = string.IsNullOrWhiteSpace(b.Name) ? $"Bone_{b.Index}" : b.Name;

                        if (string.Equals(name, "RootNode", StringComparison.Ordinal))
                            boneNodes[b.Index] = root; // <-- Prevents RootNode.001.
                        else
                            boneNodes[b.Index] = new NodeBuilder(name);
                    }

                    // Parent bones + set local TRS.
                    foreach (var b in xnaModel.Bones)
                    {
                        if (b == null) continue;

                        var node = boneNodes[b.Index];

                        b.Transform.Decompose(out Vector3 s, out Quaternion r, out Vector3 t);

                        node.UseTranslation().Value = new System.Numerics.Vector3(t.X, t.Y, t.Z);
                        node.UseRotation().Value    = new System.Numerics.Quaternion(r.X, r.Y, r.Z, r.W);

                        // Preserve scale comp if this bone is the RootNode and we mapped it to the scene root.
                        var scale = new System.Numerics.Vector3(s.X, s.Y, s.Z);
                        if (ReferenceEquals(node, root) && Math.Abs(FBX_COMP - 1f) > 0.000001f)
                            scale = new System.Numerics.Vector3(scale.X * FBX_COMP, scale.Y * FBX_COMP, scale.Z * FBX_COMP);

                        node.UseScale().Value = scale;

                        if (b.Parent != null && boneNodes.TryGetValue(b.Parent.Index, out var parentNode))
                        {
                            // Avoid self-parenting in case something weird maps parent->same node.
                            if (!ReferenceEquals(parentNode, node))
                                parentNode.AddNode(node);
                        }
                        else
                        {
                            // Root bones attach under RootNode, but don't add RootNode under itself.
                            if (!ReferenceEquals(node, root))
                                root.AddNode(node);
                        }
                    }

                    var texMatCache = new Dictionary<Texture2D, MaterialBuilder>(new ReferenceEqualityComparerTexture2D());

                    int meshCounter = 0;

                    foreach (var mesh in xnaModel.Meshes)
                    {
                        if (mesh == null) continue;

                        NodeBuilder parentBoneNode = root;
                        if (mesh.ParentBone != null && boneNodes.TryGetValue(mesh.ParentBone.Index, out var bn))
                            parentBoneNode = bn;

                        int partCounter = 0;

                        foreach (var part in mesh.MeshParts)
                        {
                            if (part == null) continue;

                            string gltfMeshName = $"_MESH_{mesh.Name}_{meshCounter:000}_{partCounter:000}";
                            var gltfMesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(gltfMeshName);

                            Texture2D tex = null;
                            if (part.Effect is BasicEffect be && be.TextureEnabled && be.Texture is Texture2D efTex)
                                tex = efTex;

                            var mat  = BuildMaterial(tex, gltfMeshName, texMatCache, embedTextures);
                            var prim = gltfMesh.UsePrimitive(mat);

                            var vdecl  = part.VertexBuffer.VertexDeclaration;
                            int stride = vdecl.VertexStride;

                            var vdata = new byte[stride * part.NumVertices];
                            part.VertexBuffer.GetData<byte>(part.VertexOffset * stride, vdata, 0, vdata.Length, 1);

                            int iCount  = part.PrimitiveCount * 3;
                            var indices = ReadIndices(part, iCount);

                            GetOffsets(vdecl,
                                out int posOffset, out var posFmt,
                                out int normOffset, out var normFmt,
                                out int texOffset, out var texFmt,
                                out int colOffset, out var colFmt);

                            if (posOffset < 0)
                            {
                                Log($"[GLB] SKIP part: No Position element. mesh={mesh.Name}.");
                                continue;
                            }

                            for (int i = 0; i < iCount; i += 3)
                            {
                                int ia = indices[i + 0];
                                int ib = indices[i + 1];
                                int ic = indices[i + 2];

                                if ((uint)ia >= (uint)part.NumVertices ||
                                    (uint)ib >= (uint)part.NumVertices ||
                                    (uint)ic >= (uint)part.NumVertices)
                                    continue;

                                var va = ReadVertex(vdata, stride, ia, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool na);
                                var vb = ReadVertex(vdata, stride, ib, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool nb);
                                var vc = ReadVertex(vdata, stride, ic, posOffset, posFmt, normOffset, normFmt, texOffset, texFmt, colOffset, colFmt, out bool nc);

                                FixupNormalIfMissing(ref va, ref vb, ref vc, na && nb && nc);
                                prim.AddTriangle(va, vb, vc);
                            }

                            var meshNode = parentBoneNode.CreateNode(gltfMeshName);
                            scene.AddRigidMesh(gltfMesh, meshNode);

                            partCounter++;
                            meshCounter++;
                        }
                    }

                    scene.ToGltf2().SaveGLB(glbPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[Export] GLB export fail: {ex}.");
                    return false;
                }
            }
            #endregion

            #endregion

            #region Materials (Optional Embedded Textures)

            /// <summary>
            /// Build (or reuse) a GLTF material for a mesh-part.
            /// Summary: Creates a basic metallic-roughness material and optionally embeds a Texture2D as BaseColor.
            /// </summary>
            private static MaterialBuilder BuildMaterial(Texture2D tex, string name, Dictionary<Texture2D, MaterialBuilder> cache, bool embedTextures)
            {
                // Base: Metallic-roughness, white base color.
                var mat = new MaterialBuilder(name)
                    .WithMetallicRoughnessShader()
                    .WithChannelParam(
                        KnownChannel.BaseColor,
                        KnownProperty.RGBA,
                        new System.Numerics.Vector4(1f, 1f, 1f, 1f));

                // Summary: No texture (or embedding disabled) => plain material.
                if (tex == null || !embedTextures)
                    return mat;

                // Summary: Cache hit => reuse existing MaterialBuilder for this Texture2D reference.
                if (cache.TryGetValue(tex, out var cached))
                    return cached;

                try
                {
                    // Summary: Convert Texture2D -> PNG bytes and embed into the GLB.
                    var png = TextureToPngBytes(tex);

                    if (TryCreateMemoryImageFromPng(png, out var memImg))
                    {
                        mat.WithChannelImage(KnownChannel.BaseColor, memImg);
                    }
                    else
                    {
                        // Summary: If MemoryImage construction fails, still export (material stays untextured).
                    }
                }
                catch
                {
                    // Summary: Texture embedding is best-effort; failures fall back to a plain base-color material.
                }

                cache[tex] = mat;
                return mat;
            }

            /// <summary>
            /// Convert a Texture2D into a PNG byte[] (for embedding into GLB).
            /// Summary: Uses Texture2D.SaveAsPng into a MemoryStream; returns null on failure.
            /// </summary>
            private static byte[] TextureToPngBytes(Texture2D tex)
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        tex.SaveAsPng(ms, tex.Width, tex.Height);
                        return ms.ToArray();
                    }
                }
                catch { return null; }
            }

            /// <summary>
            /// Create a SharpGLTF MemoryImage from PNG bytes.
            /// Summary: Uses reflection to support multiple SharpGLTF versions (static factories, ctors, or stream APIs).
            /// </summary>
            private static bool TryCreateMemoryImageFromPng(byte[] pngBytes, out MemoryImage img)
            {
                img = default;
                if (pngBytes == null || pngBytes.Length == 0) return false;

                var t = typeof(MemoryImage);

                // Summary: Try common static factory names/signatures across versions.
                object boxed =
                    TryInvokeStatic(t, "CreateFrom", pngBytes) ??
                    TryInvokeStatic(t, "Create", pngBytes) ??
                    TryInvokeStatic(t, "FromBytes", pngBytes) ??
                    TryInvokeStatic(t, "From", pngBytes) ??
                    TryInvokeStatic(t, "CreateFromBytes", pngBytes) ??
                    TryInvokeStatic(t, "CreateFrom", pngBytes, "image/png") ??
                    TryInvokeStatic(t, "Create", pngBytes, "image/png") ??
                    TryInvokeStatic(t, "FromBytes", pngBytes, "image/png") ??
                    TryInvokeStatic(t, "From", pngBytes, "image/png");

                if (boxed is MemoryImage mi1) { img = mi1; return true; }

                // Summary: Try constructors that take byte[] / ReadOnlyMemory<byte> / ArraySegment<byte>.
                boxed =
                    TryInvokeCtor(t, pngBytes) ??
                    TryInvokeCtor(t, new ArraySegment<byte>(pngBytes)) ??
                    TryInvokeCtor(t, new ReadOnlyMemory<byte>(pngBytes)) ??
                    TryInvokeCtor(t, new ReadOnlyMemory<byte>(pngBytes), "image/png");

                if (boxed is MemoryImage mi2) { img = mi2; return true; }

                // Summary: Try stream-based APIs (static or ctor).
                using (var ms = new MemoryStream(pngBytes, writable: false))
                {
                    boxed =
                        TryInvokeStatic(t, "CreateFromStream", ms) ??
                        TryInvokeStatic(t, "FromStream", ms) ??
                        TryInvokeStatic(t, "CreateFrom", ms) ??
                        TryInvokeCtor(t, ms) ??
                        TryInvokeCtor(t, ms, "image/png");
                }

                if (boxed is MemoryImage mi3) { img = mi3; return true; }

                return false;
            }

            /// <summary>
            /// Reflection helper for static method invocation.
            /// Summary: Finds a matching public static method by name + argument types and invokes it.
            /// </summary>
            private static object TryInvokeStatic(Type t, string name, params object[] args)
            {
                try
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                   .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                                   .ToArray();

                    foreach (var m in methods)
                    {
                        var ps = m.GetParameters();
                        if (ps.Length != args.Length) continue;

                        bool ok = true;
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (args[i] == null) { ok = false; break; }
                            if (!ps[i].ParameterType.IsInstanceOfType(args[i]) &&
                                !(ps[i].ParameterType.IsAssignableFrom(args[i].GetType())))
                            { ok = false; break; }
                        }

                        if (!ok) continue;
                        return m.Invoke(null, args);
                    }
                }
                catch { }
                return null;
            }

            /// <summary>
            /// Reflection helper for constructor invocation.
            /// Summary: Finds a matching public ctor by argument types and invokes it.
            /// </summary>
            private static object TryInvokeCtor(Type t, params object[] args)
            {
                try
                {
                    var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var c in ctors)
                    {
                        var ps = c.GetParameters();
                        if (ps.Length != args.Length) continue;

                        bool ok = true;
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (args[i] == null) { ok = false; break; }
                            if (!ps[i].ParameterType.IsInstanceOfType(args[i]) &&
                                !(ps[i].ParameterType.IsAssignableFrom(args[i].GetType())))
                            { ok = false; break; }
                        }

                        if (!ok) continue;
                        return c.Invoke(args);
                    }
                }
                catch { }
                return null;
            }
            #endregion

            #region Vertex/Index Reading Helpers

            /// <summary>
            /// Read triangle indices for a mesh-part.
            /// Summary: Pulls indices starting at StartIndex (clamped) and returns them without applying VertexOffset.
            /// </summary>
            private static int[] ReadIndices(ModelMeshPart part, int iCount)
            {
                int remaining = part.IndexBuffer.IndexCount - part.StartIndex;
                iCount = Math.Min(iCount, remaining);

                var indices = new int[iCount];

                bool sixteen = part.IndexBuffer.IndexElementSize == IndexElementSize.SixteenBits;
                if (sixteen)
                {
                    var buf = new ushort[part.IndexBuffer.IndexCount];
                    part.IndexBuffer.GetData(buf);

                    for (int i = 0; i < iCount; i++)
                        indices[i] = buf[part.StartIndex + i]; // <-- NO - part.VertexOffset.
                }
                else
                {
                    var buf = new int[part.IndexBuffer.IndexCount];
                    part.IndexBuffer.GetData(buf);

                    for (int i = 0; i < iCount; i++)
                        indices[i] = buf[part.StartIndex + i]; // <-- NO - part.VertexOffset.
                }

                return indices;
            }

            /// <summary>
            /// Locate element offsets + formats within a VertexDeclaration.
            /// Summary: Finds the first Position/Normal/UV/Color elements and returns offsets + formats (or -1 if missing).
            /// </summary>
            private static void GetOffsets(
                VertexDeclaration vdecl,
                out int posOffset, out VertexElementFormat posFmt,
                out int normOffset, out VertexElementFormat normFmt,
                out int texOffset, out VertexElementFormat texFmt,
                out int colOffset, out VertexElementFormat colFmt)
            {
                posOffset = -1; posFmt = default;
                normOffset = -1; normFmt = default;
                texOffset = -1; texFmt = default;
                colOffset = -1; colFmt = default;

                foreach (var e in vdecl.GetVertexElements())
                {
                    if (e.VertexElementUsage == VertexElementUsage.Position && posOffset < 0)
                    { posOffset = e.Offset; posFmt = e.VertexElementFormat; }

                    if (e.VertexElementUsage == VertexElementUsage.Normal && normOffset < 0)
                    { normOffset = e.Offset; normFmt = e.VertexElementFormat; }

                    if (e.VertexElementUsage == VertexElementUsage.TextureCoordinate && texOffset < 0)
                    { texOffset = e.Offset; texFmt = e.VertexElementFormat; }

                    if (e.VertexElementUsage == VertexElementUsage.Color && colOffset < 0)
                    { colOffset = e.Offset; colFmt = e.VertexElementFormat; }
                }
            }

            /// <summary>
            /// Decode a single vertex from the raw vertex buffer into SharpGLTF vertex structures.
            /// Summary: Reads Position (required), Normal/UV/Color (optional), normalizes as needed, and flips V for UVs.
            /// </summary>
            private static VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> ReadVertex(
                byte[] vdata, int stride, int vIndex,
                int posOffset, VertexElementFormat posFmt,
                int normOffset, VertexElementFormat normFmt,
                int texOffset, VertexElementFormat texFmt,
                int colOffset, VertexElementFormat colFmt,
                out bool hadNormal)
            {
                int o = vIndex * stride;

                // Position (required).
                var pos = ReadVec3(vdata, o + posOffset, posFmt);

                // Normal (optional).
                hadNormal = (normOffset >= 0);
                System.Numerics.Vector3 nrm = new System.Numerics.Vector3(0, 0, 1);

                if (hadNormal)
                {
                    try
                    {
                        nrm = ReadVec3(vdata, o + normOffset, normFmt);
                        float len = nrm.Length();
                        if (len > 1e-8f) nrm /= len;
                        else { nrm = new System.Numerics.Vector3(0, 0, 1); hadNormal = false; }
                    }
                    catch
                    {
                        nrm = new System.Numerics.Vector3(0, 0, 1);
                        hadNormal = false;
                    }
                }

                // UV (optional).
                System.Numerics.Vector2 uv = System.Numerics.Vector2.Zero;
                if (texOffset >= 0)
                {
                    try
                    {
                        uv = ReadVec2(vdata, o + texOffset, texFmt);
                        // uv = new System.Numerics.Vector2(uv.X, 1f - uv.Y); // Flip V.
                    }
                    catch
                    {
                        uv = System.Numerics.Vector2.Zero;
                    }
                }

                // Color (optional) - default to white.
                System.Numerics.Vector4 col = new System.Numerics.Vector4(1f, 1f, 1f, 1f);
                if (colOffset >= 0 && colFmt == VertexElementFormat.Color)
                {
                    // XNA Color is typically packed BGRA in memory; interpret as BGRA.
                    byte b = vdata[o + colOffset + 0];
                    byte g = vdata[o + colOffset + 1];
                    byte r = vdata[o + colOffset + 2];
                    byte a = vdata[o + colOffset + 3];
                    col = new System.Numerics.Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
                }

                var geo = new VertexPositionNormal(pos, nrm);
                var mat = new VertexColor1Texture1(col, uv);

                // IMPORTANT: Use new VertexEmpty() (matches SharpGLTF examples style).
                return new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(geo, mat, new VertexEmpty());
            }

            /// <summary>
            /// Read a Vector3 from a vertex buffer element.
            /// Summary: Supports Vector3/Vector4/HalfVector4 (Vector4 uses XYZ and ignores W).
            /// </summary>
            private static System.Numerics.Vector3 ReadVec3(byte[] data, int offset, VertexElementFormat fmt)
            {
                switch (fmt)
                {
                    case VertexElementFormat.Vector3:
                        return new System.Numerics.Vector3(
                            BitConverter.ToSingle(data, offset + 0),
                            BitConverter.ToSingle(data, offset + 4),
                            BitConverter.ToSingle(data, offset + 8));

                    case VertexElementFormat.Vector4:
                        return new System.Numerics.Vector3(
                            BitConverter.ToSingle(data, offset + 0),
                            BitConverter.ToSingle(data, offset + 4),
                            BitConverter.ToSingle(data, offset + 8));

                    case VertexElementFormat.HalfVector4:
                        return new System.Numerics.Vector3(
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 0)),
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 2)),
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 4)));

                    default:
                        // If your exporter previously threw NotSupported here, this is what was happening.
                        // Prefer a clear message:
                        throw new NotSupportedException($"ReadVec3 unsupported format: {fmt}.");
                }
            }

            /// <summary>
            /// Read a Vector2 from a vertex buffer element.
            /// Summary: Supports Vector2/Vector4/HalfVector2/HalfVector4 (Vector4 uses XY and ignores ZW).
            /// </summary>
            private static System.Numerics.Vector2 ReadVec2(byte[] data, int offset, VertexElementFormat fmt)
            {
                switch (fmt)
                {
                    case VertexElementFormat.Vector2:
                        return new System.Numerics.Vector2(
                            BitConverter.ToSingle(data, offset + 0),
                            BitConverter.ToSingle(data, offset + 4));

                    case VertexElementFormat.Vector4:
                        return new System.Numerics.Vector2(
                            BitConverter.ToSingle(data, offset + 0),
                            BitConverter.ToSingle(data, offset + 4));

                    case VertexElementFormat.HalfVector2:
                        return new System.Numerics.Vector2(
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 0)),
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 2)));

                    case VertexElementFormat.HalfVector4:
                        return new System.Numerics.Vector2(
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 0)),
                            HalfToSingle(BitConverter.ToUInt16(data, offset + 2)));

                    default:
                        // UV is optional; if you'd rather not fail exports, return 0,0 instead of throwing.
                        return System.Numerics.Vector2.Zero;
                }
            }

            /// <summary>
            /// Generate a flat normal when the source buffer has no valid normals.
            /// Summary: Computes triangle normal and writes it back into the three VertexPositionNormal instances.
            /// </summary>
            private static void FixupNormalIfMissing(
                ref VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> a,
                ref VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> b,
                ref VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> c,
                bool normalsPresent)
            {
                if (normalsPresent) return;

                var p0 = a.Geometry.Position;
                var p1 = b.Geometry.Position;
                var p2 = c.Geometry.Position;

                var n = System.Numerics.Vector3.Cross(p1 - p0, p2 - p0);
                float len = n.Length();
                if (len < 1e-8f) n = new System.Numerics.Vector3(0, 0, 1);
                else n /= len;

                a.Geometry = new VertexPositionNormal(p0, n);
                b.Geometry = new VertexPositionNormal(p1, n);
                c.Geometry = new VertexPositionNormal(p2, n);
            }

            /// <summary>
            /// Convert an IEEE-754 binary16 (half-float) to float.
            /// Summary: Supports zero/subnormal/inf/nan paths.
            /// </summary>
            private static float HalfToSingle(ushort h)
            {
                uint sign = (uint)(h >> 15) & 0x1;
                uint exp = (uint)(h >> 10) & 0x1F;
                uint mant = (uint)(h & 0x3FF);

                if (exp == 0)
                {
                    if (mant == 0) return sign == 1 ? -0.0f : 0.0f;
                    // Subnormal.
                    float v = mant / 1024.0f;
                    v *= (float)Math.Pow(2, -14);
                    return sign == 1 ? -v : v;
                }

                if (exp == 31)
                {
                    if (mant == 0) return sign == 1 ? float.NegativeInfinity : float.PositiveInfinity;
                    return float.NaN;
                }

                float value = 1.0f + (mant / 1024.0f);
                value *= (float)Math.Pow(2, (int)exp - 15);
                return sign == 1 ? -value : value;
            }

            /// <summary>
            /// Reference comparer for Texture2D keys.
            /// Summary: Uses reference identity so the material cache only hits for the same Texture2D instance.
            /// </summary>
            private sealed class ReferenceEqualityComparerTexture2D : IEqualityComparer<Texture2D>
            {
                public bool Equals(Texture2D x, Texture2D y) => ReferenceEquals(x, y);
                public int GetHashCode(Texture2D obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
            #endregion

            #region IO Helpers

            /// <summary>
            /// Safe Texture2D -> PNG save for model textures (errors only logged).
            /// </summary>
            static void SaveTextureSafe(Texture2D tex, string path)
            {
                try
                {
                    using (var fs = File.Create(path))
                        tex.SaveAsPng(fs, tex.Width, tex.Height);
                }
                catch (Exception ex)
                {
                    Log($"[Export] Model texture save fail: {ex.Message}.");
                }
            }

            /// <summary>
            /// Sanitizes a string for use as a file stem.
            /// </summary>
            static string San(string s)
            {
                foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
                return s;
            }
            #endregion
        }

        // Thin wrapper so TexturePackExtractor.ExportAll() can call a short name.
        static void ExportMiscModels(CastleMinerZGame gm, string dir)
            => ModelExporter.ExportMiscModels(gm, dir);

        #endregion

        #region Sounds

        /// <summary>
        /// ==================================================================================================
        ///    WavebankExport
        ///
        ///    Purpose
        ///    -------
        ///    Dump XACT/XWB banks to individual audio files. This extractor is for XWB v46
        ///    (common on PC XNA titles) audio files, and its logic includes:
        ///      • PCM -> direct WAV
        ///      • MS ADPCM -> decode to 16-bit PCM (embedded converter)
        ///      • xWMA -> wrap as RIFF XWMA and (optionally) convert via external ffmpeg if available
        ///      • XMA/unknown -> raw dump for later tools
        ///
        ///    Key Behavior / Gotchas
        ///    ----------------------
        ///    • XWB v46 has 5 segments; wave data is at seg[4]. Bank/entry metadata at seg[0]/seg[1].
        ///    • MiniWaveFormat bit packing MUST be decoded like TConvert (notice **channels = (fmt>>2)&7**;
        ///      do NOT add +1 like some other parsers). This was the root cause of "sped up / short" WAVs.
        ///    • ADPCM: the "align" field in MiniWaveFormat for v46 is the 8-bit block align code TConvert
        ///      expects; pass it directly to the MS-ADPCM decoder (don't expand it to a "true block" size).
        ///    • xWMA: we build a minimal RIFF XWMA (fmt+dpds+data) so ffmpeg can decode. If ffmpeg is not
        ///      present, we keep the .wma (xwma) stub so you can batch convert later.
        ///    • Streaming or Compact banks are skipped here by design.
        ///
        ///    Attributions / Credits
        ///    ----------------------
        ///    Portions of the design/approach and format understanding were informed by projects below.
        ///    We changed and simplified a lot for our use case, but credit where due:
        ///
        ///    TConvert / TExtract lineage
        ///    • "Xna sound extraction" modified from TExtract (MIT):
        ///        https://github.com/antag99/TExtract
        ///      TConvert:
        ///        https://github.com/trigger-segfault/TConvert
        ///    • "WAV to XNB conversion" modified from WAVToXNBSoundConverter:
        ///        https://github.com/JavidPack/WAVToXNBSoundConverter
        ///    • TExtract repository notes (abridged and relevant here):
        ///        - Incorporates parts of FFmpeg (GNU LGPL v2.1 or later).
        ///        - Includes MonoGame-derived files: LzxDecoder.cs (LGPLv2.1/MS-PL), WaveBank.cs (MIT).
        ///        - MSADPCMToPCM.cs is public domain (see below).
        ///        - Example ffmpeg build flags used by TExtract for xWMA: enable xwma demuxer + wmav2 decoder.
        ///
        ///    MSADPCM decoder origin
        ///    • Our embedded ADPCM converter follows the structure/coefficients of
        ///      "MSADPCMToPCM" by Ethan "flibitijibibo" Lee (public domain):
        ///        http://wiki.multimedia.cx/index.php?title=Microsoft_ADPCM
        ///
        ///    External Tools
        ///    --------------
        ///    • ffmpeg is optional. If bundled (or found on PATH), we auto-convert xWMA->WAV; otherwise we
        ///      leave .wma files next to the exports for manual or later conversion.
        ///
        ///    Notes for Maintainers
        ///    ---------------------
        ///    • Keep the MiniWaveFormat decode exactly as-is; even a "+1 channel" slip will halve/double
        ///      durations and pitch.
        ///    • If you later decide to support compact banks or XMA, do so in separate code paths to avoid
        ///      destabilizing the working v46/PCM/ADPCM/xWMA handling here.
        ///
        /// ==================================================================================================
        /// </summary>

        internal static class WavebankExport
        {
            #region Constants / Helpers

            /// <summary>
            /// Strip to printable ASCII and collapse whitespace; useful before filename sanitization.
            /// </summary>
            private static string CleanAscii(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var sb = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c >= 32 && c <= 126) sb.Append(c);
                }
                return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            }

            /// <summary>
            /// Safe filename based on <see cref="CleanAscii"/> and OS invalid character filtering.
            /// </summary>
            private static string Sanitize(string s)
            {
                s = CleanAscii(s);
                if (string.IsNullOrEmpty(s)) return "_";
                var bad = Path.GetInvalidFileNameChars();
                var sb  = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    bool inv = false;
                    for (int j = 0; j < bad.Length; j++) if (c == bad[j]) { inv = true; break; }
                    sb.Append(inv ? '_' : c);
                }
                var cleaned = sb.ToString().Trim('.', ' ');
                return string.IsNullOrEmpty(cleaned) ? "_" : cleaned;
            }

            // MiniWaveFormat tags (match XACT v46 usage).
            private const int TAG_PCM   = 0;
            private const int TAG_XMA   = 1;
            private const int TAG_ADPCM = 2;
            private const int TAG_WMA   = 3;

            private const uint FLAG_STREAMING = 0x00010000;
            private const uint FLAG_COMPACT   = 0x00020000;

            #endregion

            #region Name Maps / Renaming

            // Non-streaming wavebank ("Sounds").
            private static readonly string[] SoundsNamesDefault = new[]
            {
                "Click","FootStep_1","FootStep_2","FootStep_3","FootStep_4","FootStep_5","Pick_1","Pick_2","Place_1",
                "Place_2","Place_3","Place_4","Place_5","Place_6","Place_7","Error","Popup","Award","Teleport",
                "BulletHitDirt_1","BulletHitDirt_2","BulletHitHuman","BulletHitDirt_3","GunShot1_1","GunShot1_2",
                "GunShot1_3","GunShot2_1","GunShot2_2","GunShot2_3","GunShot3_1","GunShot3_2","GunShot3_3","GunShot4_1",
                "GunShot4_2","GunShot4_3","BulletHitSpray_1","BulletHitSpray_2","Reload","thunderLow_1","thunderBig",
                "thunderLow_2","thunderLow_3","craft","dropitem","pickupitem","punch","punchMiss","arrow","AssaultReload",
                "Shotgun","ShotGunReload","Sand_1","Sand_2","leaves_1","leaves_2","dirt_1","dirt_2","CreatureUnearth",
                "Skeleton_1","Skeleton_2","Skeleton_3","Skeleton_4","Skeleton_5","Skeleton_6","Skeleton_7","ZombieCry_1",
                "ZombieCry_2","ZombieGrowl_1","ZombieGrowl_2","ZombieGrowl_3","ZombieGrowl_4","ZombieGrowl_5",
                "ZombieGrowl_6","Hit_1","Hit_2","Hit_3","Hit_4","Hit_5","Hit_6","Fall_1","Fall_2","Fall_3","ZombieCry_3",
                "Douse_1","Douse_2","HorrorStinger","DragonScream_1","DragonScream_2","DragonScream_3","DragonScream_4",
                "Explosion_1","Explosion_2","Explosion_3","Explosion_4","WingFlap_1","WingFlap_2","WingFlap_3",
                "WingFlap_4","WingFlap_5","WingFlap_6","DragonFall_1","DragonFall_2","DragonFall_3","DragonFall_4",
                "DragonFall_5","DragonFall_6","Fireball","Iceball","Freeze_1","Freeze_2","Freeze_3","DoorClose",
                "DoorOpen","locator","Felguard_1","Felguard_2","Felguard_3","Felguard_4","Fuse","LaserGun1_1",
                "LaserGun1_2","LaserGun1_3","LaserGun1_4","Beep","SolidTone","RPGLaunch","Alien_1","Alien_2","Alien_3",
                "Alien_4","Alien_5","GrenadeArm","RocketWhoosh","LightSaberSwing_1","LightSaberSwing_2",
                "LightSaberSwing_3","LightSaber","GroundCrash_1","GroundCrash_2","GroundCrash_3","GroundCrash_4",
                "ZombieDig_1","ZombieDig_2","ZombieDig_3","ZombieDig_4","ChainSawCutting","ChainSawIdle","ChainSawSpinning"
            };

            // Streaming bank ("SoundsStreaming").
            private static readonly string[] SoundsStreamingNamesDefault = new[]
            {
                "Theme","Birds","Crickets","Drips","song1","song2","lostSouls","song3","song4","song5","song6","SpaceTheme"
            };

            // Mutable copies actually used during export (can be overridden by files)
            private static string[] SoundsNames          = SoundsNamesDefault;
            private static string[] SoundsStreamingNames = SoundsStreamingNamesDefault;

            #endregion

            #region Name Map Files (Create Always, Load If Non-Empty)

            /// <summary>
            /// Ensure the two name-map files exist in <paramref name="dir"/> (seed with defaults if missing/empty),
            /// then optionally load them to override the in-memory name lists.
            /// </summary>
            /// <param name="dir">Output folder where SoundsNames.txt and SoundsStreamingNames.txt live.</param>
            private static void EnsureAndOptionallyLoadNameMaps(string dir)
            {
                Directory.CreateDirectory(dir);

                string soundsTxt = Path.Combine(dir, "SoundsNames.txt");
                string streamTxt = Path.Combine(dir, "SoundsStreamingNames.txt");

                // Always make the files if missing or empty, seeded with defaults.
                if (!File.Exists(soundsTxt) || new FileInfo(soundsTxt).Length == 0)
                {
                    File.WriteAllLines(soundsTxt, SoundsNamesDefault);
                    Log($"[Export] + Wrote default name map: \"{ShortenForLog(soundsTxt)}\".");
                }
                if (!File.Exists(streamTxt) || new FileInfo(streamTxt).Length == 0)
                {
                    File.WriteAllLines(streamTxt, SoundsStreamingNamesDefault);
                    Log($"[Export] + Wrote default name map: \"{ShortenForLog(streamTxt)}\".");
                }

                // Optionally load if non-empty (case-sensitive, order preserved).
                var loadedSounds = LoadNameList(soundsTxt);
                if (loadedSounds.Count > 0) SoundsNames = loadedSounds.ToArray();

                var loadedStream = LoadNameList(streamTxt);
                if (loadedStream.Count > 0) SoundsStreamingNames = loadedStream.ToArray();
            }

            /// <summary>
            /// Load non-empty, non-comment lines from a text file, preserving order and case.
            /// </summary>
            /// <param name="path">Path to the list file.</param>
            /// <returns>List of names exactly as written.</returns>
            private static List<string> LoadNameList(string path)
            {
                var list = new List<string>();
                foreach (var raw in File.ReadAllLines(path))
                {
                    var s = raw.Trim();
                    if (s.Length == 0) continue;
                    if (s.StartsWith("#") || s.StartsWith("//")) continue; // Allow comments.
                    list.Add(s);                                           // Keep exact case and order.
                }
                return list;
            }

            /// <summary>
            /// Map an entry to a human-friendly stem using bank-specific name arrays; fallback to the provided default if out of range.
            /// </summary>
            /// <param name="bankName">XWB bank name (e.g., "Sounds" or "SoundsStreaming").</param>
            /// <param name="index">Zero-based entry index within the bank.</param>
            /// <param name="fallback">Default stem to use if no user-provided name exists.</param>
            /// <returns>Sanitized stem for the file name.</returns>
            private static string ResolveHumanStem(string bankName, int index, string fallback)
            {
                // Bank names come from the XWB header; CastleMiner Z typically uses "Sounds" / "SoundsStreaming".
                if (bankName.Equals("Sounds", StringComparison.OrdinalIgnoreCase))
                {
                    if ((uint)index < (uint)SoundsNames.Length) return Sanitize(SoundsNames[index]);
                }
                else if (bankName.Equals("SoundsStreaming", StringComparison.OrdinalIgnoreCase))
                {
                    if ((uint)index < (uint)SoundsStreamingNames.Length) return Sanitize(SoundsStreamingNames[index]);
                }
                return fallback; // Fallback to bankName_index.
            }

            /// <summary>
            /// Per-bank simple de-duplication: if a stem repeats, append _NN to keep names unique.
            /// </summary>
            private sealed class NameDeduper
            {
                private static readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                public static string GetUnique(string stem)
                {
                    if (!_counts.TryGetValue(stem, out var n))
                    {
                        _counts[stem] = 1;
                        return stem;
                    }
                    _counts[stem] = n + 1;
                    return $"{stem}_{n:00}";
                }
            }
            #endregion

            #region Dump Cue Names (Optional Helper)

            /// <summary>
            /// Build the known cue & music name set. We union project-exposed names with a static list.
            /// </summary>
            private static IEnumerable<string> BuildCueNames()
            {
                var cueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                cueNames.UnionWith(SoundsNamesDefault ?? Array.Empty<string>());

                // Return sorted & ASCII-cleaned.
                var list = new List<string>(cueNames.Count);
                foreach (var n in cueNames)
                {
                    var cleaned = CleanAscii(n);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        list.Add(cleaned);
                }
                // list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
            private static IEnumerable<string> BuildMusicCueNames()
            {
                var cueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                cueNames.UnionWith(SoundsStreamingNamesDefault ?? Array.Empty<string>());

                // Return sorted & ASCII-cleaned.
                var list = new List<string>(cueNames.Count);
                foreach (var n in cueNames)
                {
                    var cleaned = CleanAscii(n);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        list.Add(cleaned);
                }
                // list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }

            /// <summary>
            /// Write cue & music names to a plain text file (handy for pack authors).
            /// </summary>
            public static string DumpCueNamesToFile(string dir, string fileName = "Cue_Names.txt")
            {
                Directory.CreateDirectory(dir);

                var lines = new List<string>
                {
                    $"# Cue names (one per line).",
                    $"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}.",
                    $""
                };

                int count = 0;
                foreach (var name in BuildCueNames()) { lines.Add(name); count++; }

                lines.Add($"");
                lines.Add($"# Music specific cue names (one per line).");
                lines.Add($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
                lines.Add($"");

                foreach (var name in BuildMusicCueNames()) { lines.Add(name); count++; }

                var path = Path.Combine(dir, fileName);
                File.WriteAllLines(path, lines);
                Log($"[Export] Wrote {count} cue names to \"{ShortenForLog(path)}\".");
                return path;
            }
            #endregion

            #region Public Entry (Wavebank Export)

            /// <summary>
            /// Export every .xwb under <paramref name="contentDir"/> into subfolders of <paramref name="outDir"/>.
            /// Skips streaming/compact banks. Writes WAV, or WMA stub if xWMA and ffmpeg is not found.
            /// </summary>
            public static int ExportAllWavebanks(string contentDir, string outDir)
            {
                var extractCuePath = Path.Combine(TexturePackManager.PacksRoot, "_Extracted", "_SoundCues");
                Directory.CreateDirectory(outDir);
                Directory.CreateDirectory(extractCuePath);
                EnsureAndOptionallyLoadNameMaps(extractCuePath); // Create the name map files and load overrides if provided.
                int total = 0;

                foreach (var xwb in Directory.EnumerateFiles(contentDir, "*.xwb", SearchOption.AllDirectories))
                {
                    var sub = Path.Combine(outDir, Sanitize(Path.GetFileNameWithoutExtension(xwb)));
                    Directory.CreateDirectory(sub);
                    total += ExportOneWavebankV46(xwb, sub);
                }

                Log($"[Export] Wrote {total} Wavebank files to \"{ShortenForLog(outDir)}\" \\ (Sounds | SoundsStreaming).");
                return total;
            }
            #endregion

            #region Core: XWB v46 Extractor

            /// <summary>
            /// Extract a single XWB v46 wavebank. PCM -> WAV; ADPCM -> decode to 16-bit PCM WAV;
            /// xWMA -> XWMA stub (then optional ffmpeg to WAV); XMA -> raw dump.
            /// </summary>
            private static int ExportOneWavebankV46(string path, string outDir)
            {
                using (var br = new BinaryReader(File.OpenRead(path)))
                {
                    // "WBND"
                    if (br.ReadUInt32() != 0x444E4255 - 1) // Hack to avoid hex: 0x444E4257.
                    {
                        br.BaseStream.Position = 0;
                        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WBND")
                            throw new InvalidDataException("Not an XWB (WBND)");
                    }

                    int version = br.ReadInt32(); // Tool version (Terraria/CMZ commonly 46).
                    br.ReadInt32();               // HeaderVersion (unused by us).

                    if (version != 46)
                        Log($"[Export] Warning: version {version} (expected 46), continuing.");

                    // 5 segments (v46).
                    var segOff = new int[5];
                    var segLen = new int[5];
                    for (int i = 0; i < 5; i++) { segOff[i] = br.ReadInt32(); segLen[i] = br.ReadInt32(); }

                    // BankData @ seg 0.
                    br.BaseStream.Position = segOff[0];
                    uint flags             = br.ReadUInt32();
                    int entryCount         = br.ReadInt32();
                    var nameBytes          = br.ReadBytes(64);
                    string bankName        = Sanitize(CleanAscii(Encoding.ASCII.GetString(nameBytes).TrimEnd('\0', ' ')));
                    int entryMetaSize      = br.ReadInt32();
                    br.ReadInt32(); // EntryNameElementSize.
                    br.ReadInt32(); // Alignment (not used here).
                    br.ReadInt32(); // CompactMiniFmt (ignored for non-compact).
                    br.ReadInt64(); // Build time.

                    bool streaming = (flags & FLAG_STREAMING) != 0;
                    bool compact   = (flags & FLAG_COMPACT) != 0;
                    if (streaming || compact)
                    {
                        Log($"[Export] Skipping '{bankName}' (streaming={streaming}, compact={compact}).");
                        return 0;
                    }

                    // Entry metadata table @ seg 1.
                    int metaPos  = segOff[1];

                    // Wave data (play region) @ seg 4.
                    int dataBase = segOff[4];

                    int wrote = 0;
                    for (int i = 0; i < entryCount; i++)
                    {
                        br.BaseStream.Position = metaPos;

                        // Entry metadata element is variable-sized; read only what exists:
                        if (entryMetaSize >= 4) br.ReadInt32();
                        int fmt = (entryMetaSize >= 8) ? br.ReadInt32() : 0;
                        int dataOffset = (entryMetaSize >= 12) ? br.ReadInt32() : 0;
                        int dataLength = (entryMetaSize >= 16) ? br.ReadInt32() : 0;
                        /* int loopOfs */
                        if (entryMetaSize >= 20) br.ReadInt32();
                        /* int loopLen */
                        if (entryMetaSize >= 24) br.ReadInt32();

                        metaPos += entryMetaSize;
                        int absDataOfs = dataBase + dataOffset;

                        // MiniWaveFormat decode (IMPORTANT: channels = (fmt>>2)&7; NO "+1").
                        int codec  = (fmt) & 0x3;
                        int chans  = (fmt >> 2) & 0x7;     // Keep TConvert semantics.
                        if (chans  <= 0) chans = 1;        // Guard for safety.
                        int rate   = (fmt >> 5) & 0x3FFFF; // 18-bit sample rate.
                        int align  = (fmt >> 23) & 0xFF;   // 8-bit align code.
                        bool pcm16 = (fmt & unchecked((int)0x80000000)) != 0;

                        // Read the raw bytes for this entry.
                        br.BaseStream.Position = absDataOfs;
                        var audio = br.ReadBytes(dataLength);

                        // File base name.
                        string fallbackStem = $"{bankName}_{i:D3}";
                        string baseName     = ResolveHumanStem(bankName, i, fallbackStem); // Human filename stem.
                        baseName            = NameDeduper.GetUnique(baseName);             // Prevents collisions.

                        switch (codec)
                        {
                            case TAG_PCM:
                                {
                                    // Bits-per-sample from top bit.
                                    int bits = pcm16 ? 16 : 8;
                                    WritePcmWav(Path.Combine(outDir, baseName + ".wav"), audio, rate, chans, bits);
                                    wrote++;
                                    break;
                                }

                            case TAG_ADPCM:
                                {
                                    // Decode to PCM using the embedded ADPCM converter.
                                    // NOTE: Pass the 8-bit "align" value DIRECTLY; do not expand to a "true block" size.
                                    short ch = (short)chans;
                                    short baln = (short)align;
                                    byte[] pcm = ADPCMConverter.ConvertToPCM(audio, ch, baln);

                                    // Write as 16-bit PCM mono/stereo
                                    WritePcmWav(Path.Combine(outDir, baseName + ".wav"), pcm, rate, chans, 16);
                                    wrote++;
                                    break;
                                }

                            case TAG_WMA: // xWMA.
                                {
                                    // Build an xWMA RIFF stub; try ffmpeg -> WAV, else keep .wma for later conversion.
                                    string wmaPath = Path.Combine(outDir, baseName + ".wma");
                                    string wavPath = Path.Combine(outDir, baseName + ".wav");
                                    WriteXwmaStub(wmaPath, audio, rate, chans, align);

                                    if (TryFfmpeg(wmaPath, wavPath))
                                    {
                                        File.Delete(wmaPath);
                                        wrote++;
                                    }
                                    else
                                    {
                                        Log($"[Export] - FFmpeg not found. Kept {Path.GetFileName(wmaPath)}; convert it to WAV later.");
                                    }
                                    break;
                                }

                            case TAG_XMA:
                            default:
                                {
                                    // Unknown/unsupported (rare in PC releases). Save raw for later tooling.
                                    File.WriteAllBytes(Path.Combine(outDir, baseName + ".bin"), audio);
                                    break;
                                }
                        }
                    }

                    return wrote;
                }
            }
            #endregion

            #region WAV Writers (PCM) + xWMA Wrapper

            /// <summary>
            /// Write a minimal PCM WAV (little-endian) with fmt/data chunks.
            /// </summary>
            private static void WritePcmWav(string path, byte[] pcmLE, int sampleRate, int channels, int bits)
            {
                int byteRate   = sampleRate * channels * (bits / 8);
                int blockAlign = channels * (bits / 8);

                using (var fs = File.Create(path))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(36 + pcmLE.Length);
                    bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(16);
                    bw.Write((short)1); // PCM.
                    bw.Write((short)channels);
                    bw.Write(sampleRate);
                    bw.Write(byteRate);
                    bw.Write((short)blockAlign);
                    bw.Write((short)bits);
                    bw.Write(Encoding.ASCII.GetBytes("data"));
                    bw.Write(pcmLE.Length);
                    bw.Write(pcmLE);
                }
            }

            /// <summary>
            /// Construct a minimal RIFF XWMA (fmt+dpds+data) so ffmpeg can decode xWMA payloads.
            /// </summary>
            private static void WriteXwmaStub(string path, byte[] data, int rate, int chans, int alignCode)
            {
                // Tables (these map the 8-bit align code to real values).
                int[] wmaAvgBytesPerSec = { 12000, 24000, 4000, 6000, 8000, 20000 };
                int[] wmaBlockAlign     = { 929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304, 1536, 1485, 1008, 2731, 4096, 6827, 5462 };

                int avgBps = (alignCode >= wmaAvgBytesPerSec.Length) ? wmaAvgBytesPerSec[alignCode >> 5] : wmaAvgBytesPerSec[alignCode];
                int blkAln = (alignCode >= wmaBlockAlign.Length)     ? wmaBlockAlign[alignCode & 0xF]    : wmaBlockAlign[alignCode];

                using (var bw = new BinaryWriter(File.Create(path)))
                {
                    // RIFF XWMA.
                    bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(0); // Patched by ffmpeg anyway.
                    bw.Write(Encoding.ASCII.GetBytes("XWMA"));

                    // fmt.
                    bw.Write(Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(18);
                    bw.Write((short)0x0161); // WMAUDIO2.
                    bw.Write((short)chans);
                    bw.Write(rate);
                    bw.Write(avgBps);
                    bw.Write((short)blkAln);
                    bw.Write((short)16);
                    bw.Write((short)0);      // cbSize.

                    // dpds (decode packet durations).
                    bw.Write(Encoding.ASCII.GetBytes("dpds"));
                    int packetLen = blkAln;
                    int packetNum = data.Length / packetLen;
                    bw.Write(packetNum * 4);

                    // Size math.
                    int fullSize = (data.Length * avgBps % 4096 != 0)
                        ? ((1 + (data.Length * avgBps / 4096)) * 4096)
                        : data.Length;
                    int allBlocks = fullSize / 4096;
                    int avgBlocksPerPacket = allBlocks / packetNum;
                    int spareBlocks = allBlocks - (avgBlocksPerPacket * packetNum);

                    int accu = 0;
                    for (int i = 0; i < packetNum; i++)
                    {
                        accu += avgBlocksPerPacket * 4096;
                        if (spareBlocks != 0) { accu += 4096; spareBlocks--; }
                        bw.Write(accu);
                    }

                    // Data.
                    bw.Write(Encoding.ASCII.GetBytes("data"));
                    bw.Write(data.Length);
                    bw.Write(data);
                }
            }
            #endregion

            #region Ffmpeg Discovery & Shell-Out (Optional)

            #region FFmpeg README Helper

            /// <summary>
            /// Ensure a simple README is present in the _ffmpeg folder explaining purpose, optionality, and download links.
            /// Safe no-op on errors.
            /// </summary>
            private static void EnsureFfmpegReadme(string dir)
            {
                try
                {
                    string readme = Path.Combine(dir, "_READ ME.txt");
                    if (!File.Exists(readme) || new FileInfo(readme).Length == 0)
                    {
                        var lines = new[]
                        {
                            "FFmpeg (optional) for audio conversion.",
                            "",
                            "What this folder is:",
                            "- Place a copy of ffmpeg.exe (Windows) here to enable automatic xWMA -> WAV",
                            "  conversion when exporting XACT/XWB wavebanks.",
                            "- If this folder is empty, exports still work: PCM/ADPCM become .wav and xWMA entries are kept as",
                            "  .wma stubs for manual conversion later.",
                            "",
                            "Where to get FFmpeg:",
                            "- Official site:          https://ffmpeg.org/download.html",
                            "- Popular Windows builds: https://www.gyan.dev/ffmpeg/builds/ (grab a 'release full' static zip)",
                            "- Alternative builds:     https://github.com/BtbN/FFmpeg-Builds/releases",
                            "",
                            "How to use:",
                            "- Easiest: Copy ffmpeg.exe directly into this folder (no subfolders).",
                            "- Or place ffmpeg.exe next to the game/app .exe.",
                            "- Or set the FFMPEG_DIR environment variable to a folder that contains ffmpeg.exe.",
                            "- Or add FFmpeg to your system PATH.",
                            "",
                            "Detection order used by this mod:",
                            "  1) !Mods\\TexturePacks\\_ffmpeg\\ffmpeg.exe (this folder)",
                            "  2) Application (game) folder",
                            "  3) FFMPEG_DIR environment variable",
                            "  4) PATH",
                            "",
                            "Why it's not bundled:",
                            "- Licensing/size considerations; you choose whether to install it.",
                            "",
                            "This folder is safe to delete; it will be recreated automatically if missing."
                        };
                        File.WriteAllLines(readme, lines);
                    }
                }
                catch { /* Ignore doc write failures. */ }
            }
            #endregion

            /// <summary>
            /// Find ffmpeg.exe (bundled, app dir, FFMPEG_DIR, PATH). Returns a full path or null.
            /// </summary>
            private static string ResolveFfmpegPath()
            {
                string exeName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg";

                // 1) Bundled copy: !Mods\TexturePacks\_ffmpeg\ffmpeg.exe.
                string bundledDir = Path.Combine(TexturePackManager.PacksRoot, "_ffmpeg");
                Directory.CreateDirectory(bundledDir); // Always make the bundled directory as a visual location for users.
                EnsureFfmpegReadme(bundledDir);        // Always ensure README exists (idempotent).
                string bundled = Path.Combine(bundledDir, exeName);
                if (File.Exists(bundled)) return bundled;

                // 2) App (game) folder.
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
                if (File.Exists(local)) return local;

                // 3) Env: FFMPEG_DIR.
                var envDir = Environment.GetEnvironmentVariable("FFMPEG_DIR");
                if (!string.IsNullOrWhiteSpace(envDir))
                {
                    string envPath = Path.Combine(envDir.Trim('"'), exeName);
                    if (File.Exists(envPath)) return envPath;
                }

                // 4) PATH scan.
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathVar.Split(Path.PathSeparator))
                {
                    try
                    {
                        var d = (dir ?? "").Trim().Trim('"');
                        if (d.Length == 0) continue;
                        var candidate = Path.Combine(d, exeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { /* Ignore malformed PATH entries. */ }
                }

                return null; // Not found.
            }

            /// <summary>
            /// Shell out to ffmpeg if available; returns false if not found/failed. Timeout included.
            /// </summary>
            private static bool TryFfmpeg(string input, string output, int timeoutMs = 45000)
            {
                string ff = ResolveFfmpegPath();
                if (string.IsNullOrEmpty(ff))
                    return false; // Lets caller log "FFmpeg not found...".

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName               = ff, // Full path prevents CreateProcess from probing PATH again.
                        Arguments              = $"-y -loglevel error -i \"{Path.GetFullPath(input)}\" -acodec pcm_s16le -map_metadata -1 \"{Path.GetFullPath(output)}\"",
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardError  = true,
                        RedirectStandardOutput = true,
                        WorkingDirectory       = Path.GetDirectoryName(ff) ?? TexturePackManager.PacksRoot
                    };

                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) return false;

                        // Drain pipes defensively (suppressed with -loglevel error, but safe).
                        _ = p.StandardOutput.ReadToEndAsync();
                        _ = p.StandardError.ReadToEndAsync();

                        if (!p.WaitForExit(timeoutMs))
                        {
                            try { p.Kill(); } catch { }
                            return false;
                        }

                        return p.ExitCode == 0 && File.Exists(output);
                    }
                }
                catch
                {
                    // Swallow and report "not available/failed" to caller.
                    return false;
                }
            }
            #endregion

            #region Embedded MS-ADPCM Decoder (Public Domain Basis)

            /// <summary>
            /// Minimal MS-ADPCM -> 16-bit PCM converter (mono/stereo) adapted from public-domain sources.
            /// </summary>
            private static class ADPCMConverter
            {
                // NOTE from MSADPCMToPCM authorship: These tables/coefficients are the "magic numbers"
                // for the predictor model. See MultimediaWiki "Microsoft ADPCM" for details.
                private static readonly int[] adaptionTable = { 230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230 };
                private static readonly int[] adaptCoeff_1  = { 256, 512, 0, 192, 240, 460, 392 };
                private static readonly int[] adaptCoeff_2  = { 0, -256, 0, 64, 0, -208, -232 };

                private static void GetNibbleBlock(int b, int[] n) { n[0] = (b >> 4) & 0xF; n[1] = b & 0xF; }

                private static short CalcSample(int nibble, int predictor, short[] s1, short[] s2, short[] delta)
                {
                    // Signed nibble.
                    byte nb = (byte)nibble; if ((nb & 8) == 8) nb -= 16;

                    // Predict new sample.
                    int acc = (s1[0] * adaptCoeff_1[predictor] + s2[0] * adaptCoeff_2[predictor]) / 256;
                    acc += (short)nb * delta[0];

                    // Clamp to 16-bit.
                    short outS = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, acc));

                    // Shuffle state, update delta.
                    s2[0] = s1[0]; s1[0] = outS;
                    delta[0] = (short)(adaptionTable[nibble] * delta[0] / 256);
                    if (delta[0] < 16) delta[0] = 16;
                    return outS;
                }

                /// <summary>
                /// Decode headerless MS-ADPCM blocks to raw signed 16-bit PCM (little-endian).
                /// Pass the 8-bit blockAlign code from MiniWaveFormat for v46 banks.
                /// </summary>
                public static byte[] ConvertToPCM(byte[] source, short numChannels, short blockAlign)
                {
                    using (var br = new BinaryReader(new MemoryStream(source)))
                    {
                        var ms = new MemoryStream();
                        using (var bw = new BinaryWriter(ms))
                        {
                            int[] nibb = new int[2];
                            long end = source.Length - blockAlign;

                            if (numChannels == 1)
                            {
                                while (br.BaseStream.Position <= end)
                                {
                                    int predictor = br.ReadByte();
                                    short[] delta = { br.ReadInt16() };
                                    short[] s1 = { br.ReadInt16() };
                                    short[] s2 = { br.ReadInt16() };

                                    // Initial samples.
                                    bw.Write(s1[0]); bw.Write(s2[0]);
                                    for (int i = 0; i < (blockAlign + 15); i++)
                                    {
                                        GetNibbleBlock(br.ReadByte(), nibb);
                                        for (int j = 0; j < 2; j++)
                                            bw.Write(CalcSample(nibb[j], predictor, s1, s2, delta));
                                    }
                                }
                            }
                            else if (numChannels == 2)
                            {
                                while (br.BaseStream.Position <= end)
                                {
                                    int predL = br.ReadByte(), predR = br.ReadByte();
                                    short[] deltaL = { br.ReadInt16() }, deltaR = { br.ReadInt16() };
                                    short[] s1L = { br.ReadInt16() }, s1R = { br.ReadInt16() };
                                    short[] s2L = { br.ReadInt16() }, s2R = { br.ReadInt16() };

                                    // Initial samples L2,R2,L1,R1.
                                    bw.Write(s2L[0]); bw.Write(s2R[0]); bw.Write(s1L[0]); bw.Write(s1R[0]);
                                    for (int i = 0; i < (blockAlign + 15) * 2; i++)
                                    {
                                        GetNibbleBlock(br.ReadByte(), nibb);
                                        bw.Write(CalcSample(nibb[0], predL, s1L, s2L, deltaL));
                                        bw.Write(CalcSample(nibb[1], predR, s1R, s2R, deltaR));
                                    }
                                }
                            }
                            else throw new InvalidOperationException("ADPCM: Only mono/stereo supported");
                        }
                        return ms.ToArray();
                    }
                }
            }
            #endregion
        }

        #region Entry Point Helper (Wavebank)

        /// <summary>
        /// Example entry that dumps cue names and exports both banks from the game's Content folder.
        /// </summary>
        static void ExportSounds(string dir)
        {
            WavebankExport.DumpCueNamesToFile(dir);
            WavebankExport.ExportAllWavebanks(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content"), dir);
        }
        #endregion

        #endregion

        #region Items (Icons) / ItemModels

        /// <summary>
        /// Export item icons from the 64px (small) and/or 128px (large) atlases.
        /// Writes to:
        ///   !Mods/TexturePacks/_Export/Items/Items64/0007_Iron_Pickaxe.png
        ///   !Mods/TexturePacks/_Export/Items/Items128/0007_Iron_Pickaxe.png
        /// Returns number of PNGs written.
        /// </summary>
        public static int ExportAllItemIcons(string outDir, bool exportSmall = true, bool exportLarge = true, bool overwrite = true)
        {
            var items = GetAllItemClasses();
            if (items == null || items.Count == 0)
            {
                Log("[Export] No InventoryItem classes available.");
                return 0;
            }

            // Resolve atlases
            var smallAtlasField = AccessTools.Field(typeof(InventoryItem), "_2DImages");
            var largeAtlasField = AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge");
            var smallAtlas = smallAtlasField?.GetValue(null) as Texture2D;
            var largeAtlas = largeAtlasField?.GetValue(null) as Texture2D;

            if ((!exportSmall || smallAtlas == null) && (!exportLarge || largeAtlas == null))
            {
                Log("[Export] No item icon atlas to export from.");
                return 0;
            }

            // Build authoritative 64px rects once
            var smallRects = smallAtlas != null ? BuildSmallItemRects(items, smallAtlas)
                                                : new Dictionary<InventoryItemIDs, Rectangle>();
            int largeCols = largeAtlas != null ? Math.Max(1, largeAtlas.Width / 128) : 1;

            // Output directory
            if (string.IsNullOrWhiteSpace(outDir))
                outDir = Path.Combine(PacksRoot, "_Export", "Items");

            // Output folders
            string out64 = Path.Combine(outDir, "Items64");
            string out128 = Path.Combine(outDir, "Items128");
            try
            {
                if (exportSmall) Directory.CreateDirectory(out64);
                if (exportLarge) Directory.CreateDirectory(out128);
            }
            catch (Exception ex)
            {
                Log($"[Export] Failed to create Item Icons export directories: {ex.Message}.");
                return 0;
            }

            int written = 0;

            foreach (var kv in items)
            {
                var id = kv.Key;
                var cls = kv.Value;

                // Friendly-ish name if available, else enum name
                string nice = GetFriendlyItemName(cls);
                if (string.IsNullOrWhiteSpace(nice)) nice = id.ToString();
                string stem = ((int)id).ToString("0000") + "_" + NormalizeName(nice);

                // Small 64x64
                if (exportSmall && smallAtlas != null)
                {
                    if (!smallRects.TryGetValue(id, out Rectangle r64) || r64.Width <= 0 || r64.Height <= 0)
                    {
                        // grid fallback by ID
                        int cols = Math.Max(1, smallAtlas.Width / 64);
                        int idx = (int)id;
                        r64 = new Rectangle((idx % cols) * 64, (idx / cols) * 64, 64, 64);
                    }

                    string path64 = Path.Combine(out64, SafeFileName(stem) + ".png");
                    if (overwrite || !File.Exists(path64))
                    {
                        if (BlockExporter.SaveTilePNG(smallAtlas, r64, path64))
                            written++;
                    }
                }

                // Large 128x128 (mapped from the 64 rect)
                if (exportLarge && largeAtlas != null)
                {
                    var r128 = RectFrom64On128(smallRects, id, smallAtlas, largeAtlas, largeCols);
                    string path128 = Path.Combine(out128, SafeFileName(stem) + ".png");
                    if (overwrite || !File.Exists(path128))
                    {
                        if (BlockExporter.SaveTilePNG(largeAtlas, r128, path128))
                            written++;
                    }
                }
            }

            Log($"[Export] Wrote {written} item icon PNG(s) to \"{ShortenForLog(outDir)}\" \\ (Items64 | Items128).");
            return written;
        }

        /// <summary>
        /// Export item model diffuse textures (the skins you later override via Items\*_model.png).
        /// It searches each InventoryItemClass for a loaded Model and grabs Texture2D from BasicEffect
        /// or common custom-effect parameter names. Writes to:
        ///   !Mods/TexturePacks/_Export/ItemModels/0007_Iron_Pickaxe_model.png
        /// If multiple distinct textures are found for an item, additional files get _model2/_model3 suffixes.
        /// Returns number of PNGs written.
        /// </summary>
        public static int ExportAllItemModelSkins(string outDir, bool overwrite = true)
        {
            var items = GetAllItemClasses();
            if (items == null || items.Count == 0)
            {
                Log("[Export] No InventoryItem classes available for model export.");
                return 0;
            }

            // Output directory
            if (string.IsNullOrWhiteSpace(outDir))
                outDir = Path.Combine(PacksRoot, "_Export", "Items");

            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex)
            {
                Log($"[Export] Failed to create ItemModels export directory: {ex.Message}.");
                return 0;
            }

            int written = 0;

            foreach (var kv in items)
            {
                var id = kv.Key;
                var cls = kv.Value;

                // Collect one or more diffuse textures from the model(s) this item uses
                var textures = CollectItemDiffuseTextures(cls);
                if (textures == null || textures.Count == 0)
                    continue;

                // Filename stem
                string nice = GetFriendlyItemName(cls);
                if (string.IsNullOrWhiteSpace(nice)) nice = id.ToString();
                string stemBase = ((int)id).ToString("0000") + "_" + NormalizeName(nice) + "_model";

                // Some items share textures; avoid re-saving identical object references for the same item
                var seen = new HashSet<Texture2D>(ReferenceEqualityComparerTex.Instance);
                int n = 0;
                for (int i = 0; i < textures.Count; i++)
                {
                    var tex = textures[i];
                    if (tex == null || tex.IsDisposed) continue;
                    if (!seen.Add(tex)) continue;

                    string stem = (n == 0) ? stemBase : (stemBase + (n + 1).ToString());
                    string path = Path.Combine(outDir, SafeFileName(stem) + ".png");

                    if (overwrite || !File.Exists(path))
                    {
                        if (SaveTextureAsPng(tex, path, true))
                            written++;
                    }
                    n++;
                }
            }

            Log($"[Export] Wrote {written} item model skin PNG(s) to \"{ShortenForLog(outDir)}\".");
            return written;
        }

        /// <summary>
        /// Export item models as OBJ/MTL using ModelExporter.TryExportObj.
        /// Each InventoryItem class is scanned for Model fields; each distinct Model
        /// is written as:
        ///   <outDir>\0007_Iron_Pickaxe.obj
        ///   <outDir>\0007_Iron_Pickaxe.mtl
        /// plus per-material PNGs next to them.
        /// Returns number of OBJ files written.
        /// </summary>
        public static int ExportAllItemModelsGlb(string outDir, bool overwrite = true)
        {
            var items = GetAllItemClasses();
            if (items == null || items.Count == 0)
            {
                Log("[Export] No InventoryItem classes available for model export.");
                return 0;
            }

            // Output directory
            if (string.IsNullOrWhiteSpace(outDir))
                outDir = Path.Combine(PacksRoot, "_Export", "ItemModels");

            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex)
            {
                Log($"[Export] Failed to create ItemModels MODEL export directory: {ex.Message}.");
                return 0;
            }

            int wroteModel = 0, wroteBones = 0;

            // Optional: global dedupe so we don't export the exact same Model ref 100 times.
            var globalSeen = new HashSet<Model>(new ReferenceEqualityComparerModel());

            foreach (var kv in items)
            {
                var id = kv.Key;
                var cls = kv.Value;

                var models = CollectItemModels(cls);
                if (models == null || models.Count == 0)
                    continue;

                // Base name from item ID + friendly name (same logic as skins)
                string nice = GetFriendlyItemName(cls);
                if (string.IsNullOrWhiteSpace(nice)) nice = id.ToString();
                string stemBase = ((int)id).ToString("0000") + "_" + NormalizeName(nice);

                for (int i = 0; i < models.Count; i++)
                {
                    var model = models[i];
                    if (model == null) continue;

                    // Global dedupe (optional): comment this block out if you WANT duplicates per item
                    if (!globalSeen.Add(model))
                        continue;

                    string stem = (models.Count == 1) ? stemBase : $"{stemBase}_model{i + 1}";
                    string basePathNoExt = Path.Combine(outDir, SafeFileName(stem));

                    if (!overwrite && File.Exists(basePathNoExt + ".glb"))
                        continue;

                    if (ModelExporter.TryExportGlb(model, basePathNoExt + ".glb", overwrite, embedTextures: true))
                        wroteModel++;
                }
            }

            Log($"[Export] Wrote {wroteModel} item model GLB(s) to \"{ShortenForLog(outDir)}\".");
            return wroteModel + wroteBones;
        }

        private static string GetFriendlyItemName(InventoryItem.InventoryItemClass cls)
        {
            if (cls == null) return "";
            var t = cls.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Try common names/properties first.
            var tryProps = new[] { "DisplayName", "FriendlyName", "Name", "ItemName", "Label" };
            for (int i = 0; i < tryProps.Length; i++)
            {
                var p = t.GetProperty(tryProps[i], BF);
                if (p != null && p.PropertyType == typeof(string))
                {
                    try { var s = p.GetValue(cls, null) as string; if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
                }
            }

            var tryFields = new[] { "_displayName", "_friendlyName", "_name" };
            for (int i = 0; i < tryFields.Length; i++)
            {
                var f = t.GetField(tryFields[i], BF);
                if (f != null && f.FieldType == typeof(string))
                {
                    try { var s = f.GetValue(cls) as string; if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
                }
            }

            // Fallback: whatever ToString() returns.
            try { var s = cls.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
            return "";
        }

        /// <summary>
        /// Pull diffuse Texture2D used by the item's Model(s).
        /// Looks for Model fields on the InventoryItemClass and crawls mesh parts' Effects.
        /// </summary>
        private static List<Texture2D> CollectItemDiffuseTextures(InventoryItem.InventoryItemClass cls)
        {
            var list = new List<Texture2D>();
            if (cls == null) return list;

            var t = cls.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 1) Find Model fields on the class
            var modelType = typeof(Model);
            var modelFields = t.GetFields(BF).Where(f => modelType.IsAssignableFrom(f.FieldType)).ToArray();

            for (int i = 0; i < modelFields.Length; i++)
            {
                var f = modelFields[i];
                Model model = null;
                try { model = f.GetValue(cls) as Model; } catch { }
                if (model == null) continue;

                // 2) Crawl mesh parts and scrape textures from Effects.
                foreach (var mesh in model.Meshes)
                    foreach (var part in mesh.MeshParts)
                    {
                        var fx = part.Effect;
                        if (fx == null) continue;

                        // BasicEffect path.
                        if (fx is BasicEffect be && be.Texture != null)
                        {
                            AddUnique(list, be.Texture);
                            continue;
                        }

                        // Common custom parameter names.
                        var paramNames = new[] { "Texture", "Texture0", "DiffuseTexture", "DiffuseMap", "Albedo", "AlbedoMap" };
                        for (int p = 0; p < paramNames.Length; p++)
                        {
                            var param = fx.Parameters[paramNames[p]];
                            if (param == null) continue;

                            try
                            {
                                var obj = param.GetValueTexture2D(); // MonoGame helper; if not present, fall back reflection below.
                                if (obj != null) { AddUnique(list, obj); continue; }
                            }
                            catch { /* MG helper not present; fall back */ }

                            try
                            {
                                // Generic GetValue<object>() then cast.
                                var boxed = param.GetValueTexture2D(); // Some forks expose Texture not Texture2D.
                                if (boxed is Texture2D asTex2D) { AddUnique(list, asTex2D); }
                            }
                            catch
                            {
                                // very old XNA: EffectParameter.GetValueTexture* APIs don't exist;
                                // there isn't a supported way to pull it - skip in that case.
                            }
                        }
                    }
            }

            return list;
        }

        private static void AddUnique(List<Texture2D> list, Texture2D tex)
        {
            if (tex == null) return;
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], tex)) return;
            list.Add(tex);
        }

        /// <summary>Build a safe filename stem (letters, digits, underscores).</summary>
        private static string SafeFileName(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return "untitled";
            var sb = new System.Text.StringBuilder(stem.Length);
            for (int i = 0; i < stem.Length; i++)
            {
                char c = stem[i];
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "untitled";
        }

        /// <summary>
        /// Pull Model instances used by the item's InventoryItemClass.
        /// Looks for instance Model fields on the class.
        /// </summary>
        private static List<Model> CollectItemModels(InventoryItem.InventoryItemClass cls)
        {
            var list = new List<Model>();
            if (cls == null) return list;

            var t = cls.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var modelType = typeof(Model);

            foreach (var f in t.GetFields(BF))
            {
                if (!modelType.IsAssignableFrom(f.FieldType))
                    continue;

                try
                {
                    if (!(f.GetValue(cls) is Model m)) continue;
                    // Avoid duplicates per item
                    bool dup = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (ReferenceEquals(list[i], m)) { dup = true; break; }
                    }
                    if (!dup)
                        list.Add(m);
                }
                catch { /* ignore per-field failures */ }
            }

            return list;
        }

        /// <summary>
        /// Reference equality comparer for Model so we can dedupe by instance.
        /// </summary>
        private sealed class ReferenceEqualityComparerModel : IEqualityComparer<Model>
        {
            public bool Equals(Model x, Model y) => ReferenceEquals(x, y);
            public int GetHashCode(Model obj) =>
                RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class ReferenceEqualityComparerTex : IEqualityComparer<Texture2D>
        {
            public static readonly ReferenceEqualityComparerTex Instance = new ReferenceEqualityComparerTex();
            public bool Equals(Texture2D x, Texture2D y) => ReferenceEquals(x, y);
            public int GetHashCode(Texture2D obj) => RuntimeHelpers.GetHashCode(obj);
        }
        #endregion

        #region Models (Player, Enemies, Dragons, Extras)

        internal static partial class ModelSkinExporter
        {
            private static IEnumerable<Texture2D> CollectDistinctTexturesFromModel(Model model)
            {
                var set = new HashSet<Texture2D>(ReferenceEqualityComparer<Texture2D>.Default);
                if (model == null) return set;

                foreach (var mesh in model.Meshes)
                {
                    foreach (var part in mesh.MeshParts)
                    {
                        if (part == null || part.Effect == null) continue;

                        var eff = part.Effect;

                        // BasicEffect
                        if (eff is BasicEffect be && be.Texture != null && !be.Texture.IsDisposed)
                            set.Add(be.Texture);

                        // Common custom param names
                        try
                        {
                            var p = eff.Parameters;
                            if (p != null)
                            {
                                var texParam =
                                    p["Texture"] ?? p["Texture0"] ?? p["DiffuseTexture"] ??
                                    p["ColorMap"] ?? p["gDiffuseTexture"] ?? p["gColorMap"];

                                if (texParam != null)
                                {
                                    var t = texParam.GetValueTexture2D();
                                    if (t != null && !t.IsDisposed) set.Add(t);
                                }
                            }
                        }
                        catch { /* ignore per-part failures */ }
                    }
                }
                return set;
            }

            /// <summary>
            /// Export current model textures (Doors / Player / Enemies / Dragon) and model GLBs under:
            ///   !Mods/TexturePacks/_Extracted/<timestamp>/Models/...
            ///
            /// Folder layout produced:
            ///   Models\Doors\Misc\NormalDoor.glb / IronDoor.glb / DiamondDoor.glb / TechDoor.glb
            ///   Models\Player\Misc\Player.glb
            ///   Models\Enemies\Misc\<EnemyType>.glb
            ///   Models\Dragon\Misc\DragonBody.glb + DragonFeet.glb
            ///   Models\Items\Misc\<Item>.glb
            ///   Models\Extras\{Dragons|Enemies|Misc}\*.glb
            ///
            /// Pass the player's Model if you want their current skin exported as well.
            /// Returns the absolute extraction folder.
            /// </summary>
            public static string ExportModelTextures(string root, Model playerModel = null, bool overwrite = true)
            {
                var extract = Path.Combine(root, "Models");
                var doorDir = Path.Combine(extract, "Doors");
                var playerDir = Path.Combine(extract, "Player");
                var enemyDir = Path.Combine(extract, "Enemies");
                var dragonDir = Path.Combine(extract, "Dragon");
                var itemsDir = Path.Combine(extract, "Items");
                var extrasDir = Path.Combine(extract, "Extras");

                int doorGlbCount = 0, pCount = 0, eCount = 0, dCount = 0;

                // One dedupe set for *all* model instances exported under Models\
                var globalSeen = new HashSet<Model>(new ReferenceEqualityComparerModel());

                try
                {
                    Directory.CreateDirectory(extract);

                    // =====================================================================================
                    // Doors (shared door model GLBs in Models\Doors\Misc)
                    // =====================================================================================
                    try
                    {
                        Directory.CreateDirectory(doorDir);

                        var miscDir = Path.Combine(doorDir, "Misc");
                        Directory.CreateDirectory(miscDir);

                        var doorNames = new[]
                        {
                            DoorEntity.ModelNameEnum.Wood,
                            DoorEntity.ModelNameEnum.Iron,
                            DoorEntity.ModelNameEnum.Diamond,
                            DoorEntity.ModelNameEnum.Tech,
                        };

                        for (int i = 0; i < doorNames.Length; i++)
                        {
                            var modelName = doorNames[i];
                            var stem = GetDoorStem(modelName);
                            if (string.IsNullOrWhiteSpace(stem))
                                continue;

                            Model model = null;
                            try { model = DoorEntity.GetDoorModel(modelName); }
                            catch { }

                            if (model == null)
                                continue;

                            if (TryExportGlbOnce(
                                model,
                                Path.Combine(miscDir, stem + ".glb"),
                                overwrite,
                                globalSeen))
                            {
                                doorGlbCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Doors export error: {ex.Message}.");
                    }

                    // =====================================================================================
                    // Player (optional, if you provide a live Model instance)
                    // =====================================================================================
                    if (playerModel != null)
                    {
                        Directory.CreateDirectory(playerDir);

                        // --- Export GLB once under Models\Player\Misc ---
                        var miscDir = Path.Combine(playerDir, "Misc");
                        Directory.CreateDirectory(miscDir);

                        TryExportGlbOnce(
                            playerModel,
                            Path.Combine(miscDir, "Player.glb"),
                            overwrite,
                            globalSeen);

                        // --- Export player textures (keep your existing behavior) ---
                        var textures = CollectDistinctTexturesFromModel(playerModel).ToList();
                        for (int i = 0; i < textures.Count; i++)
                        {
                            var tex = textures[i];
                            if (tex == null || tex.IsDisposed) continue;

                            // Prefer a sane filename
                            var stem = !string.IsNullOrEmpty(tex.Name)
                                       ? Path.GetFileNameWithoutExtension(tex.Name)
                                       : (textures.Count == 1 ? "Player" : $"Player_{i}");

                            var outPath = Path.Combine(playerDir, stem + ".png");
                            if (SaveTextureAsPng(tex, outPath)) pCount++;
                        }
                    }

                    // =====================================================================================
                    // Enemies (EnumName.png + EnumName.glb in Models\Enemies\Misc)
                    // =====================================================================================
                    try
                    {
                        Directory.CreateDirectory(enemyDir);

                        var miscDir = Path.Combine(enemyDir, "Misc");
                        Directory.CreateDirectory(miscDir);

                        var typesField = AccessTools.Field(typeof(EnemyType), "Types");
                        var arr = typesField != null ? (EnemyType[])typesField.GetValue(null) : null;

                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Length; i++)
                            {
                                var et = arr[i];
                                if (et == null) continue;

                                // --- Export enemy model GLB once ---
                                var model = et.Model;
                                if (model != null)
                                {
                                    TryExportGlbOnce(
                                        model,
                                        Path.Combine(miscDir, et.EType.ToString() + ".glb"),
                                        overwrite,
                                        globalSeen);
                                }

                                // --- Export enemy texture (keep your existing behavior) ---
                                var tex = GetFieldTexture2D(et, "EnemyTexture");
                                if (tex == null || tex.IsDisposed) continue;

                                var name = et.EType.ToString();
                                var outPath = Path.Combine(enemyDir, name + ".png");
                                if (SaveTextureAsPng(tex, outPath, flattenAlpha: true)) eCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Enemies export error: {ex.Message}.");
                    }

                    // =====================================================================================
                    // Dragons (EnumName.png + shared DragonBody/DragonFeet GLBs in Models\Dragon\Misc)
                    // =====================================================================================
                    try
                    {
                        Directory.CreateDirectory(dragonDir);

                        var miscDir = Path.Combine(dragonDir, "Misc");
                        Directory.CreateDirectory(miscDir);

                        // Export shared dragon body/feet geometry as GLB once.
                        var bodyField = AccessTools.Field(typeof(DragonClientEntity), "DragonBody");
                        var feetField = AccessTools.Field(typeof(DragonClientEntity), "DragonFeet");

                        // NOTE: DragonClientEntity.Init() must have run already or these will be null.
                        if (bodyField?.GetValue(null) is Model dragonBodyModel)
                        {
                            TryExportGlbOnce(
                                dragonBodyModel,
                                Path.Combine(miscDir, "DragonBody.glb"),
                                overwrite,
                                globalSeen);
                        }

                        if (feetField?.GetValue(null) is Model dragonFeetModel)
                        {
                            TryExportGlbOnce(
                                dragonFeetModel,
                                Path.Combine(miscDir, "DragonFeet.glb"),
                                overwrite,
                                globalSeen);
                        }

                        // Export dragon textures (keep your existing behavior)
                        // Try to find a static DragonType[] field
                        var dragonType = typeof(DragonType);
                        var fields = dragonType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                        DragonType[] darr = null;
                        for (int i = 0; i < fields.Length; i++)
                        {
                            var f = fields[i];
                            if (typeof(DragonType[]).IsAssignableFrom(f.FieldType))
                            {
                                darr = f.GetValue(null) as DragonType[];
                                if (darr != null) break;
                            }
                        }

                        if (darr != null)
                        {
                            for (int i = 0; i < darr.Length; i++)
                            {
                                var dt = darr[i];
                                if (dt == null) continue;

                                var tex = GetFieldTexture2D(dt, "Texture");
                                if (tex == null || tex.IsDisposed) continue;

                                var name = dt.EType.ToString();
                                var outPath = Path.Combine(dragonDir, name + ".png");
                                if (SaveTextureAsPng(tex, outPath)) dCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Dragons export error: {ex.Message}.");
                    }

                    // =====================================================================================
                    // Items (write GLBs under Models\Items\Misc)
                    // =====================================================================================
                    try
                    {
                        var itemMisc = Path.Combine(itemsDir, "Misc");
                        Directory.CreateDirectory(itemMisc);

                        // Your function name is still ExportAllItemModelsObj, but it now writes GLB internally.
                        ExportAllItemModelsGlb(itemMisc, overwrite);
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Items export error: {ex.Message}.");
                    }

                    // =====================================================================================
                    // Extras (reflection scan of Model fields across the assembly)
                    // Writes under Models\Extras\{Dragons|Enemies|Misc}
                    // =====================================================================================
                    try
                    {
                        Directory.CreateDirectory(extrasDir);
                        ModelExporter.ExportMiscModelsGlb(CastleMinerZGame.Instance, extrasDir, overwrite, globalSeen);
                    }
                    catch (Exception ex)
                    {
                        Log($"[Export] Extras export error: {ex.Message}.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Export] Unhandled export error: {ex.Message}.");
                }

                Log($"[Export] Wrote Doors:{doorGlbCount} P:{pCount} E:{eCount} D:{dCount} -> \"{ShortenForLog(extract)}\".");
                return extract;
            }

            /// <summary>
            /// Export a model to GLB once (global dedupe + overwrite policy).
            /// </summary>
            private static bool TryExportGlbOnce(Model model, string outGlbPath, bool overwrite, HashSet<Model> globalSeen)
            {
                if (model == null) return false;

                // Global dedupe across Player/Enemies/Dragon/Items/Extras
                if (globalSeen != null && !globalSeen.Add(model))
                    return false;

                if (!overwrite && File.Exists(outGlbPath))
                    return false;

                try
                {
                    var dir = Path.GetDirectoryName(outGlbPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);
                }
                catch { }

                return ModelExporter.TryExportGlb(model, outGlbPath, overwrite, embedTextures: true);
            }

            // EnemyType._textureNames[(int) tname]
            // DragonType._textureNames[(int) tname]
            public static int ExportEnemyAndDragonByContentNames(string enemyDir, string dragonDir)
            {
                int count = 0;
                try
                {
                    // Enemy textures by table.
                    var etNamesField = AccessTools.Field(typeof(EnemyType), "_textureNames");
                    if (etNamesField?.GetValue(null) is string[] etNames)
                    {
                        Directory.CreateDirectory(enemyDir);
                        for (int i = 0; i < etNames.Length; i++)
                        {
                            var name = etNames[i];
                            if (string.IsNullOrEmpty(name)) continue;

                            var tex = TryLoadTexture2D(name);
                            if (tex == null) continue;

                            var outPath = Path.Combine(enemyDir, ((EnemyType.TextureNameEnum)i).ToString() + ".png");
                            try { if (SaveTextureAsPng(tex, outPath, flattenAlpha: true)) count++; } catch { }
                        }
                    }

                    // Dragon textures by table.
                    var dtNamesField = AccessTools.Field(typeof(DragonType), "_textureNames");
                    if (dtNamesField?.GetValue(null) is string[] dtNames)
                    {
                        Directory.CreateDirectory(dragonDir);
                        for (int i = 0; i < dtNames.Length; i++)
                        {
                            var name = dtNames[i];
                            if (string.IsNullOrEmpty(name)) continue;

                            var tex = TryLoadTexture2D(name);
                            if (tex == null) continue;

                            var outPath = Path.Combine(dragonDir, ((DragonType.TextureNameEnum)i).ToString() + ".png");
                            try { if (SaveTextureAsPng(tex, outPath)) count++; } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Export] Content-name export error: {ex.Message}.");
                }
                return count;
            }

            /// <summary>
            /// Gets a Texture2D from a field or property by name.
            /// </summary>
            private static Texture2D GetFieldTexture2D(object obj, string fieldOrProp)
            {
                if (obj == null) return null;
                var t = obj.GetType();

                var f = AccessTools.Field(t, fieldOrProp);
                if (f != null)
                {
                    try { return f.GetValue(obj) as Texture2D; } catch { }
                }

                var p = AccessTools.Property(t, fieldOrProp);
                if (p != null)
                {
                    try { return p.GetValue(obj, null) as Texture2D; } catch { }
                }
                return null;
            }

            /// <summary>
            /// Gets the pack/export stem for a door model type.
            /// </summary>
            private static string GetDoorStem(DoorEntity.ModelNameEnum modelName)
            {
                switch (modelName)
                {
                    case DoorEntity.ModelNameEnum.Wood:
                        return "NormalDoor";

                    case DoorEntity.ModelNameEnum.Iron:
                        return "IronDoor";

                    case DoorEntity.ModelNameEnum.Diamond:
                        return "DiamondDoor";

                    case DoorEntity.ModelNameEnum.Tech:
                        return "TechDoor";

                    default:
                        return null;
                }
            }

            // Reference equality comparer for HashSet< Texture2D >
            private sealed class ReferenceEqualityComparer<TX> : IEqualityComparer<TX> where TX : class
            {
                public static readonly ReferenceEqualityComparer<TX> Default = new ReferenceEqualityComparer<TX>();
                bool IEqualityComparer<TX>.Equals(TX x, TX y) => object.ReferenceEquals(x, y);
                int IEqualityComparer<TX>.GetHashCode(TX obj) => RuntimeHelpers.GetHashCode(obj);
            }
        }
        #endregion

        #region Fonts (Export Helpers)

        internal static class FontExtractor
        {
            /// <summary>
            /// Export every SpriteFont field on CastleMinerZGame (ConsoleFont, MedFont, etc.).
            /// Call this once after SecondaryLoad (e.g., from a hotkey in the main menu).
            /// Returns number of exported fonts.
            /// </summary>
            public static int ExportAllFonts(CastleMinerZGame gm, string outDir)
            {
                int count = 0;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in gm.GetType().GetFields(flags))
                {
                    if (f.FieldType != typeof(SpriteFont)) continue;
                    var font = (SpriteFont)f.GetValue(gm);
                    if (font == null) continue;

                    string name = f.Name.TrimStart('_'); // e.g., "_consoleFont" -> "consoleFont".
                    if (ExportFont(font, Path.Combine(outDir, name)))
                        count++;
                }

                Log($"[Export] Wrote {count} SpriteFont(s) to \"{ShortenForLog(outDir)}\".");
                return count;
            }

            /// <summary>
            /// Export a single SpriteFont to <basePath>.png and <basePath>.json.
            /// </summary>
            public static bool ExportFont(SpriteFont font, string basePath)
            {
                if (font == null) return false;
                string png = basePath + ".png";
                string json = basePath + ".json";
                Directory.CreateDirectory(Path.GetDirectoryName(basePath));

                // 1) Grab atlas texture
                var tex = GetTexture(font);
                if (tex == null)
                {
                    Log($"[Export] {Path.GetFileName(basePath)}: no Texture on SpriteFont.");
                    return false;
                }

                // Prefer your GPU round-trip saver to avoid premultiplied alpha quirks.
                // If you don't want that, you can fall back to tex.SaveAsPng(fs, ...).
                if (!TrySaveTextureAsPng(tex, png))
                    using (var fs = File.Create(png))
                        tex.SaveAsPng(fs, tex.Width, tex.Height);

                // 2) Gather metrics (robust reflection across XNA/MonoGame).
                var glyphs = GetList<Rectangle>(font, "Glyphs") ?? GetList<Rectangle>(font, "glyphs");
                var cropping = GetList<Rectangle>(font, "Cropping") ?? GetList<Rectangle>(font, "cropping");
                var kerning = GetList<Vector3>(font, "Kerning") ?? GetList<Vector3>(font, "kerning");
                var charMap = GetList<char>(font, "CharacterMap") ?? GetList<char>(font, "characterMap");
                int lineSpacing = GetValue<int>(font, "LineSpacing", "lineSpacing");
                float spacing = GetValue<float>(font, "Spacing", "spacing");
                char? defChar = GetValue<char?>(font, "DefaultCharacter", "defaultCharacter");

                // 3) Write JSON (AngelCode-ish but minimal and lossless).
                using (var sw = new StreamWriter(File.Create(json), new UTF8Encoding(false)))
                {
                    var sb = new StringBuilder(1 << 16);
                    sb.Append("{\n");
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  \"atlas\": \"{0}\",\n", Path.GetFileName(png));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  \"size\": {{\"w\":{0},\"h\":{1}}},\n", tex.Width, tex.Height);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lineSpacing\": {0},\n", lineSpacing);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  \"spacing\": {0},\n", spacing);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  \"defaultChar\": {0},\n",
                        defChar.HasValue ? ("\"" + Escape(defChar.Value) + "\"") : "null");

                    sb.Append("  \"glyphs\": [\n");
                    int count = Math.Min(
                        Math.Min(glyphs?.Count ?? 0, cropping?.Count ?? 0),
                        Math.Min(kerning?.Count ?? int.MaxValue, charMap?.Count ?? 0));

                    for (int i = 0; i < count; i++)
                    {
                        var ch = charMap[i];
                        var g = glyphs[i];
                        var c = cropping[i];
                        var k = (kerning != null && i < kerning.Count) ? kerning[i] : Vector3.Zero;

                        sb.Append("    {");
                        sb.AppendFormat(CultureInfo.InvariantCulture, "\"char\":\"{0}\",\"code\":{1},", Escape(ch), (int)ch);
                        sb.AppendFormat(CultureInfo.InvariantCulture, "\"glyph\":{{\"x\":{0},\"y\":{1},\"w\":{2},\"h\":{3}}},", g.X, g.Y, g.Width, g.Height);
                        sb.AppendFormat(CultureInfo.InvariantCulture, "\"crop\":{{\"x\":{0},\"y\":{1},\"w\":{2},\"h\":{3}}},", c.X, c.Y, c.Width, c.Height);
                        sb.AppendFormat(CultureInfo.InvariantCulture, "\"kern\":{{\"left\":{0},\"width\":{1},\"right\":{2}}}", k.X, k.Y, k.Z);
                        sb.Append("}");
                        if (i != count - 1) sb.Append(",");
                        sb.Append("\n");
                    }
                    sb.Append("  ]\n}\n");
                    sw.Write(sb.ToString());
                }

                // Log($"[Export] Wrote: {ShortenForLog(png)} + {ShortenForLog(json)}.");
                return true;
            }

            // --- helpers ---

            private static string Escape(char c)
            {
                switch (c)
                {
                    case '"': return "\\\"";
                    case '\\': return "\\\\";
                    case '\n': return "\\n";
                    case '\r': return "\\r";
                    case '\t': return "\\t";
                    case '\b': return "\\b";
                    case '\f': return "\\f";
                    default:
                        if (char.IsControl(c))
                            return "\\u" + ((int)c).ToString("x4"); // Lower-hex, 4 digits.
                        return new string(c, 1);
                }
            }

            private static Texture2D GetTexture(SpriteFont sf)
            {
                // Prefer public property (MonoGame), then private fields (XNA).
                var prop = sf.GetType().GetProperty("Texture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && typeof(Texture2D).IsAssignableFrom(prop.PropertyType))
                    return (Texture2D)prop.GetValue(sf, null);

                var fi = sf.GetType().GetField("texture", BindingFlags.Instance | BindingFlags.NonPublic)
                      ?? sf.GetType().GetField("textureValue", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fi != null && typeof(Texture2D).IsAssignableFrom(fi.FieldType))
                    return (Texture2D)fi.GetValue(sf);

                return null;
            }

            private static IList<T> GetList<T>(SpriteFont sf, string name)
            {
                var p = sf.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
                    return (IList<T>)p.GetValue(sf, null);

                var f = sf.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null && typeof(IEnumerable).IsAssignableFrom(f.FieldType))
                    return (IList<T>)f.GetValue(sf);

                return null;
            }

            private static T GetValue<T>(SpriteFont sf, params string[] names)
            {
                foreach (var n in names)
                {
                    var p = sf.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && typeof(T).IsAssignableFrom(p.PropertyType))
                        return (T)p.GetValue(sf, null);

                    var f = sf.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (f != null && typeof(T).IsAssignableFrom(f.FieldType))
                        return (T)f.GetValue(sf);
                }
                return default;
            }

            /// <summary>
            /// Uses the existing GPU round-trip saver if available; else fall back to direct SaveAsPng.
            /// </summary>
            private static bool TrySaveTextureAsPng(Texture2D tex, string path)
            {
                try
                {
                    SaveTextureAsPng(tex, path);
                }
                catch { /* Ignore. */ }

                // Fallback - direct.
                using (var fs = File.Create(path))
                    tex.SaveAsPng(fs, tex.Width, tex.Height);
                return true;
            }
        }
        #endregion

        #region Shaders (BlockEffect)

        /// <summary>
        /// Extract the raw compiled FX bytecode (.fxb) from an Effect XNB.
        ///
        /// Implementation:
        ///   - Fast path: Manual XNB parse when uncompressed.
        ///   - Fallback:  Use ContentManager.Load<Effect>() (handles compressed XNB/LZX),
        ///                then reflect the effect bytecode out of the loaded Effect instance.
        ///
        /// Notes:
        ///   - DumpBlockEffectFxb uses the HiDefContent path, matching the common CMZ layout.
        ///   - Reflection fallback is intended for "in-game" extraction where ContentManager is available.
        /// </summary>
        internal static class XnbEffectExtractor
        {
            /// <summary>
            /// Reads an Effect XNB and returns the compiled effect bytes (.fxb).
            /// Supports BOTH:
            ///   - Uncompressed XNB:     Fast path (manual parse).
            ///   - Compressed XNB (LZX): Fallback via ContentManager.Load<Effect>() + reflection.
            /// </summary>
            public static bool TryExtractFxb(string xnbPath, out byte[] fxbBytes)
            {
                fxbBytes = null;
                if (string.IsNullOrEmpty(xnbPath) || !File.Exists(xnbPath))
                    return false;

                // -----------------------------
                // Fast path: Manual parse if not compressed.
                // -----------------------------
                try
                {
                    using (var fs = File.OpenRead(xnbPath))
                    using (var br = new BinaryReader(fs))
                    {
                        // ---- Header ----
                        if (br.ReadByte() != (byte)'X' || br.ReadByte() != (byte)'N' || br.ReadByte() != (byte)'B')
                            return false;

                        char platform = (char)br.ReadByte(); // 'w','m','x'.
                        byte ver      = br.ReadByte();       // Usually 5.
                        byte flags    = br.ReadByte();       // bit7 = compressed LZX.
                        int size      = br.ReadInt32();      // Total size (unused here).

                        bool compressed = (flags & 0x80) != 0;
                        if (!compressed)
                        {
                            // NOTE: Must read shared resource count before root object.
                            return TryReadCompiledEffectPayload(br, fs.Length, out fxbBytes);
                        }
                    }
                }
                catch
                {
                    // Ignore and try fallback.
                }

                // -----------------------------
                // Fallback: Let XNA load/decompress, then reflect the bytecode out.
                // -----------------------------
                return TryExtractViaContentManager(xnbPath, out fxbBytes);
            }

            /// <summary>Dump blockEffect.xnb -> blockEffect.fxb into outDir. Returns path or null.</summary>
            public static string DumpBlockEffectFxb(string contentRoot, string outDir)
            {
                string xnb = Path.Combine(contentRoot, "HiDefContent", "Shaders", "blockEffect.xnb");
                if (!File.Exists(xnb)) return null;

                if (!TryExtractFxb(xnb, out var fxb) || fxb == null || fxb.Length == 0)
                    return null;

                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "blockEffect.fxb");
                File.WriteAllBytes(outPath, fxb);
                return outPath;
            }

            // =============================
            // Manual XNB parse (uncompressed).
            // =============================
            private static bool TryReadCompiledEffectPayload(BinaryReader br, long fileLen, out byte[] fxb)
            {
                fxb = null;

                // ---- Type readers ----
                int readerCount = Read7(br);
                var readers = new List<string>(readerCount);
                for (int i = 0; i < readerCount; i++)
                {
                    string readerType = ReadString(br);
                    _ = br.ReadInt32(); // Ignore.
                    readers.Add(readerType ?? "");
                }

                // Shared resources count MUST be present (usually 0 for simple XNBs).
                _ = Read7(br); // Ignore, but must advance the stream.

                // Root object: "type reader index" (7-bit, 1-based), then its payload.
                int rootReaderIdx1Based = Read7(br);
                if (rootReaderIdx1Based <= 0 || rootReaderIdx1Based > readers.Count)
                    return false;

                string rootReader = readers[rootReaderIdx1Based - 1];

                if (rootReader.IndexOf("CompiledEffectReader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rootReader.IndexOf("EffectReader", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int byteCount = br.ReadInt32();
                    if (byteCount < 0 || byteCount > fileLen) return false;

                    fxb = br.ReadBytes(byteCount);
                    return (fxb != null && fxb.Length == byteCount);
                }

                return false;
            }

            // =============================
            // Engine fallback (handles LZX).
            // =============================
            private static bool TryExtractViaContentManager(string xnbPath, out byte[] fxbBytes)
            {
                fxbBytes = null;

                try
                {
                    var gm = CastleMinerZGame.Instance;
                    var cm = gm?.Content; // XNA ContentManager already supports compressed XNBs
                    if (cm == null) return false;

                    // Convert on-disk path -> content asset name (relative to Content root, without .xnb).
                    string contentRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
                    string assetName = TryMakeContentAssetName(contentRoot, xnbPath);
                    if (string.IsNullOrEmpty(assetName)) return false;

                    // Load (will decompress if needed).
                    Effect fx = null;
                    try { fx = cm.Load<Effect>(assetName); }
                    catch { return false; }

                    if (fx == null) return false;

                    // Reflect raw compiled bytecode out of the Effect instance.
                    fxbBytes = TryGetEffectBytecode(fx);
                    return (fxbBytes != null && fxbBytes.Length > 0);
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Converts an on-disk XNB path (under ContentRoot) into a ContentManager asset name:
            ///   - Makes it relative to ContentRoot.
            ///   - Strips ".xnb".
            ///   - Normalizes to forward slashes.
            /// Returns null if the path is not under the given ContentRoot.
            /// </summary>
            private static string TryMakeContentAssetName(string contentRoot, string xnbPath)
            {
                if (string.IsNullOrEmpty(contentRoot) || string.IsNullOrEmpty(xnbPath)) return null;

                string root = contentRoot.TrimEnd('\\', '/');
                if (!xnbPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return null;

                string rel = xnbPath.Substring(root.Length).TrimStart('\\', '/');

                if (rel.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(0, rel.Length - 4);

                // ContentManager accepts forward slashes fine.
                return rel.Replace('\\', '/');
            }

            /// <summary>
            /// Attempts to extract the compiled effect bytecode (FXB) from an Effect instance via reflection.
            /// Tries common field names first, then falls back to scanning for the largest byte[] field, and returns a safe copy.
            /// </summary>
            private static byte[] TryGetEffectBytecode(Effect fx)
            {
                if (fx == null) return null;

                byte[] best = null;

                // 1) Named field fast-paths (common across XNA/MonoGame variants).
                foreach (var name in new[]
                {
                    "effectCode", "_effectCode", "m_effectCode",
                    "byteCode", "bytecode", "_byteCode", "_bytecode"
                })
                {
                    var b = TryReadByteArrayField(fx, name);
                    if (b != null && b.Length > 0 && (best == null || b.Length > best.Length))
                        best = b;
                }

                // 2) Generic scan: Pick the largest byte[] field on Effect/type hierarchy.
                for (Type t = fx.GetType(); t != null; t = t.BaseType)
                {
                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    foreach (var f in fields)
                    {
                        if (f.FieldType != typeof(byte[])) continue;
                        try
                        {
                            if (f.GetValue(fx) is byte[] b && b.Length > 0 && (best == null || b.Length > best.Length))
                                best = b;
                        }
                        catch { }
                    }
                }

                if (best == null || best.Length == 0)
                    return null;

                // Return a copy to avoid exposing internal buffers.
                var copy = new byte[best.Length];
                Buffer.BlockCopy(best, 0, copy, 0, best.Length);
                return copy;
            }

            /// <summary>
            /// Reflection helper:
            ///   Walks the type hierarchy (obj -> base types) looking for a field named <paramref name="fieldName"/>
            ///   that is a <see cref="byte[]"/>. Returns its value if found; otherwise null.
            /// </summary>
            private static byte[] TryReadByteArrayField(object obj, string fieldName)
            {
                try
                {
                    for (Type t = obj.GetType(); t != null; t = t.BaseType)
                    {
                        var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (f != null && f.FieldType == typeof(byte[]))
                            return f.GetValue(obj) as byte[];
                    }
                }
                catch { }
                return null;
            }

            // ------- XNB primitives --------
            private static int Read7(BinaryReader br)
            {
                int count = 0, shift = 0;
                byte b;
                do { b = br.ReadByte(); count |= (b & 0x7F) << shift; shift += 7; } while ((b & 0x80) != 0);
                return count;
            }

            private static string ReadString(BinaryReader br)
            {
                int byteLen = Read7(br);
                if (byteLen < 0) return null;
                var bytes = br.ReadBytes(byteLen);
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
        }
        #endregion

        #region Skys (Cubemap Exporter)

        /// <summary>
        /// Exports the currently loaded sky cubemaps as PNG faces.
        ///
        /// Outputs:
        ///   ClearSky_px/nx/py/ny/pz/nz.png
        ///   NightSky_px/nx/py/ny/pz/nz.png
        ///   SunSet_px/nx/py/ny/pz/nz.png
        ///   DawnSky_px/nx/py/ny/pz/nz.png
        ///
        /// Notes:
        ///   - Export uses TextureCube.GetData(face, Color[]) and assumes readable formats.
        ///   - If cube.Format is Dxt*, GetData(Color[]) can fail; errors are logged per-face.
        ///   - LoadTextures() call is guarded; exporter can also rely on startup-loaded textures.
        /// </summary>
        internal static class SkyExporter
        {
            public static void ExportAllSkys(GraphicsDevice gd, string dir)
            {
                if (gd == null) return;

                Directory.CreateDirectory(dir);

                // NOTE:
                // Calling LoadTextures() can throw FIRST_CHANCE noise if the engine probes variants.
                // If you want zero spam, remove this and rely on startup-load (SecondaryLoad already calls it).
                try
                {
                    CastleMinerSky.LoadTextures();
                    // Log("[Export][Skys] CastleMinerSky.LoadTextures() OK.");
                }
                catch (Exception ex)
                {
                    Log($"[Export][Skys] CastleMinerSky.LoadTextures() FAILED: {ex.Message}.");
                }

                var tSky = typeof(CastleMinerSky);
                var day = AccessTools.Field(tSky, "_dayTexture")?.GetValue(null) as TextureCube;
                var night = AccessTools.Field(tSky, "_nightTexture")?.GetValue(null) as TextureCube;
                var dusk = AccessTools.Field(tSky, "_sunSetTexture")?.GetValue(null) as TextureCube;
                var dawn = AccessTools.Field(tSky, "_dawnTexture")?.GetValue(null) as TextureCube;

                int wroteFaces = 0;
                wroteFaces += ExportCube(gd, day, Path.Combine(dir, "ClearSky"));
                wroteFaces += ExportCube(gd, night, Path.Combine(dir, "NightSky"));
                wroteFaces += ExportCube(gd, dusk, Path.Combine(dir, "SunSet"));
                wroteFaces += ExportCube(gd, dawn, Path.Combine(dir, "DawnSky"));

                Log($"[Export] Wrote {wroteFaces} sky face PNG(s) to \"{ShortenForLog(dir)}\".");
            }

            /// <summary>
            /// Exports a cubemap as 6 PNGs:
            ///   <stem>_px.png, _nx.png, _py.png, _ny.png, _pz.png, _nz.png
            /// Returns number of faces written (0..6).
            /// </summary>
            private static int ExportCube(GraphicsDevice gd, TextureCube cube, string stemPath)
            {
                if (cube == null)
                {
                    Log($"[Export][Skys] - {Path.GetFileName(stemPath)}: null (not loaded).");
                    return 0;
                }

                // Log($"[Export] - {Path.GetFileName(stemPath)}: size={cube.Size}, format={cube.Format}.");

                int wrote = 0;

                wrote += SaveFace(gd, cube, CubeMapFace.PositiveX, stemPath + "_px.png");
                wrote += SaveFace(gd, cube, CubeMapFace.NegativeX, stemPath + "_nx.png");
                wrote += SaveFace(gd, cube, CubeMapFace.PositiveY, stemPath + "_py.png");
                wrote += SaveFace(gd, cube, CubeMapFace.NegativeY, stemPath + "_ny.png");
                wrote += SaveFace(gd, cube, CubeMapFace.PositiveZ, stemPath + "_pz.png");
                wrote += SaveFace(gd, cube, CubeMapFace.NegativeZ, stemPath + "_nz.png");

                if (wrote == 6)
                {
                    // Log($"[Export]   + Wrote 6 faces for {Path.GetFileName(stemPath)}.");
                }
                else
                    Log($"[Export]   ! Wrote {wrote}/6 faces for {Path.GetFileName(stemPath)} (see errors above).");

                return wrote;
            }

            /// <summary>
            /// Writes one cubemap face as a PNG. Returns 1 if written, 0 on failure.
            /// </summary>
            private static int SaveFace(GraphicsDevice gd, TextureCube cube, CubeMapFace face, string path)
            {
                try
                {
                    int size = cube.Size;

                    // IMPORTANT:
                    // This assumes the cubemap can be read into Color[] (common if SurfaceFormat.Color).
                    // If you see failures here and cube.Format is Dxt1/Dxt5, we'll need a DXT decode fallback.
                    var pix = new Color[size * size];
                    cube.GetData(face, pix);

                    Directory.CreateDirectory(Path.GetDirectoryName(path));

                    using (var tex = new Texture2D(gd, size, size, false, SurfaceFormat.Color))
                    {
                        tex.SetData(pix);
                        using (var fs = File.Create(path))
                            tex.SaveAsPng(fs, size, size);
                    }

                    return 1;
                }
                catch (Exception ex)
                {
                    Log($"[Export][Skys]   - FAILED {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}.");
                    return 0;
                }
            }
        }
        #endregion

        #region Particle Effects

        internal static class ContentExtractor
        {
            /// <summary>
            /// Copies vanilla ParticleEffects *.xnb files out of Content (and flavor folders) into outDir.
            /// Also attempts to export a .png next to the copied .xnb if that asset is actually a Texture2D.
            /// Returns total number of files written (XNB + PNG).
            /// </summary>
            public static int ExportAllParticleEffectXnbs(string contentRoot, string outDir)
            {
                int wrote = 0;

                if (string.IsNullOrEmpty(contentRoot) || !Directory.Exists(contentRoot))
                    return 0;

                Directory.CreateDirectory(outDir);

                var cm = CastleMinerZGame.Instance?.Content;
                if (cm == null)
                    return 0;

                foreach (var sub in new[] { "", "HiDefContent", "ReachContent" })
                {
                    // Source folder on disk
                    string srcDir = (sub.Length == 0)
                        ? Path.Combine(contentRoot)
                        : Path.Combine(contentRoot, sub);

                    if (!Directory.Exists(srcDir))
                        continue;

                    foreach (var xnbPath in Directory.GetFiles(srcDir, "*.xnb", SearchOption.TopDirectoryOnly))
                    {
                        // Preserve folder structure under outDir:
                        //   outDir\ParticleEffects\*.xnb
                        //   outDir\HiDefContent\ParticleEffects\*.xnb
                        //   outDir\ReachContent\ParticleEffects\*.xnb
                        string relUnderContent = GetRelativePathSafe(contentRoot, xnbPath);
                        if (string.IsNullOrEmpty(relUnderContent))
                            continue;

                        string dstXnb = Path.Combine(outDir, relUnderContent);
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dstXnb));
                            File.Copy(xnbPath, dstXnb, overwrite: true);
                            wrote++;
                        }
                        catch
                        {
                            // If copy fails, still try next file.
                            continue;
                        }

                        // Try export as PNG if this XNB is a Texture2D.
                        // Content.Load wants "asset name" (relative to Content root, WITHOUT .xnb).
                        string assetName = Path.ChangeExtension(relUnderContent, null); // removes .xnb
                                                                                        // ContentManager is fine with backslashes, keep it consistent:
                        assetName = assetName.Replace('/', '\\');

                        Texture2D tex = null;
                        try
                        {
                            tex = cm.Load<Texture2D>(assetName);
                        }
                        catch
                        {
                            tex = null; // not a Texture2D (common for particle effect data)
                        }

                        if (tex != null && !tex.IsDisposed)
                        {
                            // Write next to the copied .xnb, but as .png:
                            //   Foo.xnb -> Foo.png
                            string dstPng = Path.ChangeExtension(dstXnb, ".png");
                            try
                            {
                                if (SafeTextureExtractor.SaveTextureAsPng(tex, dstPng))
                                    wrote++;
                            }
                            catch
                            {
                                // ignore PNG failures
                            }
                        }
                    }
                }

                return wrote;
            }

            /// <summary>
            /// Returns path relative to root (best-effort), using OS separators.
            /// </summary>
            private static string GetRelativePathSafe(string root, string fullPath)
            {
                try
                {
                    root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    // .NET Framework may not have Path.GetRelativePath; do it manually.
                    if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return null;

                    string rel = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return rel;
                }
                catch { return null; }
            }
        }
        #endregion

        #region Content (ReachContent + HiDefContent)

        internal static class FullContentExtractor
        {
            /// <summary>
            /// Export pipeline:
            ///   1) Validate contentRoot exists and ContentManager is available.
            ///   2) Iterate base + flavor roots ("", "HiDefContent", "ReachContent").
            ///   3) For each top-level *.xnb:
            ///        - Build relUnderContent (path relative to contentRoot)
            ///        - Attempt to load as Texture2D via several candidate keys
            ///        - If loaded, export to outDir preserving folder layout (as .png).
            ///
            /// Returns:
            ///   - Count of PNG files successfully written.
            /// </summary>
            public static int ExportAllContent(string contentRoot, string outDir)
            {
                int wrote = 0;

                if (string.IsNullOrEmpty(contentRoot) || !Directory.Exists(contentRoot))
                    return wrote;

                Directory.CreateDirectory(outDir);

                var cm = CastleMinerZGame.Instance?.Content;
                if (cm == null)
                    return wrote;

                foreach (var sub in new[] { "", "HiDefContent", "ReachContent" })
                {
                    // Source folder on disk:
                    //   Content\               (sub="")
                    //   Content\HiDefContent\  (sub="HiDefContent")
                    //   Content\ReachContent\  (sub="ReachContent")
                    string srcDir = (sub.Length == 0)
                        ? Path.Combine(contentRoot)
                        : Path.Combine(contentRoot, sub);

                    if (!Directory.Exists(srcDir))
                        continue;

                    foreach (var xnbPath in Directory.GetFiles(srcDir, "*.xnb", SearchOption.TopDirectoryOnly))
                    {
                        // Preserve folder structure under outDir using relUnderContent:
                        //   outDir\*.png
                        //   outDir\HiDefContent\*.png
                        //   outDir\ReachContent\*.png
                        string relUnderContent = GetRelativePathSafe(contentRoot, xnbPath);
                        if (string.IsNullOrEmpty(relUnderContent))
                            continue;

                        // Destination stem (mirrors original, but PNG extension)
                        string dstXnb = Path.Combine(outDir, relUnderContent);

                        // Attempt to treat XNB as a Texture2D and export it.
                        if (TryLoadTexture2D_ByCandidates(cm, relUnderContent, out Texture2D tex, out string usedKey))
                        {
                            string dstPng = Path.ChangeExtension(dstXnb, ".png");
                            try
                            {
                                if (SafeTextureExtractor.SaveTextureAsPng(tex, dstPng))
                                {
                                    wrote++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"[Export][Content] ! PNG EX for {Path.GetFileName(dstPng)}: {ex.Message}.");
                            }
                        }

                        // NOTE:
                        // If TryLoadTexture2D_ByCandidates fails, we assume this XNB is non-texture content
                        // (Effect, Model, particle config, etc.) and skip it quietly.
                    }
                }

                return wrote;
            }

            /// <summary>
            /// Returns path relative to root (best-effort), using OS separators.
            /// </summary>
            /// <remarks>
            /// Why this exists:
            ///   - .NET Framework builds often lack Path.GetRelativePath.
            ///   - The exporter needs a stable rel path to mirror Content layout under outDir.
            /// </remarks>
            private static string GetRelativePathSafe(string root, string fullPath)
            {
                try
                {
                    root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Manual relative path:
                    // If the file isn't actually under the Content root, we can't safely mirror it.
                    if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return null;

                    string rel = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return rel;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Attempts to load a Texture2D from ContentManager using multiple possible asset-name keys.
        /// </summary>
        /// <remarks>
        /// Input expectations:
        ///   relUnderContent is a relative disk path under Content, e.g.:
        ///     "HiDefContent\\ParticleEffects\\Foo.xnb"
        ///     "ReachContent\\Textures\\Skys\\ClearSky.xnb"
        ///     "Textures\\Inventory\\HudGrid.xnb"
        ///
        /// Why multiple candidates:
        ///   - Some callsites load by "Textures\\X", others by "HiDefContent\\Textures\\X".
        ///   - This helper tries both "as-is" and "stripped" flavor variants.
        /// </remarks>
        private static bool TryLoadTexture2D_ByCandidates(
            ContentManager cm,
            string         relUnderContent, // e.g. "HiDefContent\\ParticleEffects\\Foo.xnb".
            out Texture2D  tex,
            out string     usedAssetName)
        {
            tex = null;
            usedAssetName = null;
            if (cm == null || string.IsNullOrEmpty(relUnderContent)) return false;

            // Build a few likely keys the game might use.
            // relNoExt: "HiDefContent\\ParticleEffects\\Foo".
            string relNoExt = Path.ChangeExtension(relUnderContent, null)
                .Replace('/', '\\')
                .TrimStart('\\');

            // Also try stripping flavor prefix entirely:
            // "ParticleEffects\\Foo"
            string stripped = relNoExt;
            if (stripped.StartsWith("HiDefContent\\", StringComparison.OrdinalIgnoreCase))
                stripped = stripped.Substring("HiDefContent\\".Length);
            else if (stripped.StartsWith("ReachContent\\", StringComparison.OrdinalIgnoreCase))
                stripped = stripped.Substring("ReachContent\\".Length);

            // Candidate order: Exact rel -> stripped -> then force flavor variants of stripped.
            string[] candidates =
            {
                relNoExt,
                stripped,
                "HiDefContent\\" + stripped,
                "ReachContent\\" + stripped
            };

            foreach (var key in candidates)
            {
                try
                {
                    var t = cm.Load<Texture2D>(key);
                    if (t != null && !t.IsDisposed)
                    {
                        tex = t;
                        usedAssetName = key;
                        return true;
                    }
                }
                catch
                {
                    // Ignore and try next candidate.
                }
            }

            return false;
        }
        #endregion


        #region Helper: XNB Pre-Flight Existence

        static readonly string[] _flavors = { "", "ReachContent", "HiDefContent" };

        static bool ContentXnbExists(string assetName, ContentManager cm)
        {
            // Convert "Enemies/Reaper/rb_ao" to a filesystem path we can check.
            string rel = assetName.Replace('\\', '/').TrimStart('/');
            foreach (var flavor in _flavors)
            {
                string path = (flavor.Length == 0)
                    ? Path.Combine(cm.RootDirectory, rel + ".xnb")
                    : Path.Combine(cm.RootDirectory, flavor, rel + ".xnb");
                if (File.Exists(path)) return true;
            }
            return false;
        }

        static Texture2D TryLoadTexture2D(string assetName)
        {
            var cm = CastleMinerZGame.Instance.Content;
            if (!ContentXnbExists(assetName, cm))
                return null;       // Avoids first-chance exceptions entirely.
            try { return cm.Load<Texture2D>(assetName); }
            catch { return null; } // Corrupt/wrong type? still guarded.
        }
        #endregion

        #region UNUSED: Grab Already Loaded Textures

        /*
        // EnemyType.Types[] generally exists after EnemyType.Init().
        if (AccessTools.Field(typeof(EnemyType), "Types")?.GetValue(null) is EnemyType[] enemies)
        {
            Directory.CreateDirectory(enemyDir);
            foreach (var et in enemies)
            {
                if (et?.EnemyTexture == null) continue;
                string outPath = Path.Combine(enemyDir, et.TextureIndex.ToString() + ".png");
                try { if (SaveTextureAsPng(et.EnemyTexture, outPath)) count++; } catch { }
            }
        }

        // DragonType: Find the static array the same way.
        var dragonFields = typeof(DragonType).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var dragons = dragonFields.Select(f => f.GetValue(null)).OfType<DragonType[]>().FirstOrDefault();
        if (dragons != null)
        {
            Directory.CreateDirectory(dragonDir);
            foreach (var dt in dragons)
            {
                if (dt?.Texture == null) continue;
                string outPath = Path.Combine(dragonDir, dt.EType.ToString() + ".png");
                try { if (SaveTextureAsPng(dt.Texture, outPath)) count++; } catch { }
            }
        }
        */
        #endregion

        #endregion

        #region Helpers (SafeTextureExtractor / PathShortener)

        // ----------------------------------------------------------------------------------
        // Helpers:
        //  • SafeTextureExtractor
        //      GPU round-trip Texture2D -> PNG saver that works for any SurfaceFormat and
        //      avoids depth-buffer Clear issues (no depth/stencil bound).
        //  • PathShortener
        //      Trims long absolute paths (e.g., to "\_Extracted\...") for concise log output.
        // ----------------------------------------------------------------------------------

        #region SafeTextureExtractor

        /// <summary>
        /// GPU-safe helper that exports any Texture2D to PNG via a Color RenderTarget2D.
        /// Handles premultiplied alpha correctly and can optionally "flatten" alpha for
        /// screenshot-style opaque exports.
        /// </summary>
        internal static class SafeTextureExtractor
        {
            private static SpriteBatch _sb;

            /// <summary>
            /// Draws any Texture2D (any <see cref="SurfaceFormat"/>) into a Color RenderTarget2D and writes PNG.
            /// No depth/stencil clear (avoids the Clear depth error).
            /// </summary>
            /// <remarks>
            /// Replaces the older SaveTextureAsPng. It preserves original alpha/masks by default.
            /// If you ever need an "opaque screenshot-style" export, pass <paramref name="flattenAlpha"/> = true.
            /// </remarks>
            public static bool SaveTextureAsPng(Texture2D src, string outPath, bool flattenAlpha = false)
            {
                try
                {
                    if (src == null || src.IsDisposed) return false;
                    var gd = src.GraphicsDevice;
                    if (gd == null) return false;

                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                    // Draw the source onto an uncompressed Color render target 1:1.
                    using (var rt = new RenderTarget2D(
                        gd, src.Width, src.Height,
                        false,
                        SurfaceFormat.Color,
                        DepthFormat.None,
                        0,
                        RenderTargetUsage.PreserveContents))
                    {
                        // Save/restore current RTs.
                        var saved = gd.GetRenderTargets();
                        try
                        {
                            gd.SetRenderTarget(rt);
                            // IMPORTANT: Clear only the color target (no depth/stencil).
                            gd.Clear(new Color(0, 0, 0, 0));

                            if (_sb == null || _sb.GraphicsDevice != gd) _sb = new SpriteBatch(gd);

                            // Pure copy: No blending math, no filtering.
                            _sb.Begin(SpriteSortMode.Deferred,
                                      BlendState.Opaque,
                                      SamplerState.PointClamp,
                                      DepthStencilState.None,
                                      RasterizerState.CullNone);
                            _sb.Draw(src, new Rectangle(0, 0, src.Width, src.Height), Color.White);
                            _sb.End();
                        }
                        finally
                        {
                            gd.SetRenderTargets(saved);
                        }

                        if (!flattenAlpha)
                        {
                            // Exact export: Keep color+alpha as-is from the RT.
                            using (var fs = File.Create(outPath))
                                rt.SaveAsPng(fs, rt.Width, rt.Height);
                            return true;
                        }
                        else
                        {
                            // Optional: "screenshot-style" opaque export (rarely needed for game assets).
                            var pix = new Color[rt.Width * rt.Height];
                            rt.GetData(pix);

                            // Do NOT un-premultiply; just force A=255 for viewing tools that ignore alpha.
                            for (int i = 0; i < pix.Length; i++)
                                pix[i].A = 255;

                            using (var opaque = new Texture2D(gd, rt.Width, rt.Height, false, SurfaceFormat.Color))
                            {
                                opaque.SetData(pix);
                                using (var fs = File.Create(outPath))
                                    opaque.SaveAsPng(fs, opaque.Width, opaque.Height);
                            }
                            return true;
                        }
                    }
                }
                catch { return false; }
            }
        }
        #endregion

        #region PathShortener

        /// <summary>
        /// Small helper for shortening fully-qualified paths in log messages.
        /// Prefers trimming to the \_Extracted\... tail, falling back to the full path.
        /// </summary>
        internal static class PathShortener
        {
            /// <summary>
            /// Shorten a path for logging (e.g., C:\...\_Extracted\2025...\Fonts\foo.png ->
            /// \_Extracted\2025...\Fonts\foo.png).
            /// </summary>
            public static string ShortenForLog(string fullPath)
            {
                if (string.IsNullOrEmpty(fullPath))
                    return string.Empty;

                // Normalize slashes.
                var p = fullPath.Replace('/', '\\');

                // Prefer showing from "\_Extracted\...".
                int idx = p.IndexOf(@"\_Extracted\", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return p.Substring(idx); // e.g. "\_Extracted\20251029_165134\Fonts\myriadSmall.png".

                // Fallback: Full path.
                return p;
            }
        }
        #endregion

        #endregion

        #region Pack Replacement

        // ----------------------------------------------------------------------------------
        // Texture pack hot-swap pipeline
        // ----------------------------------------------------------------------------------
        // • RequestReload()
        //     Called by UI / config changes / hotkeys to request a pack swap.
        //     Simply flips a flag; actual reload happens on the main thread.
        //
        // • Tick()
        //     Called once per frame from the game's Update. If a reload was requested,
        //     it invokes DoReloadCore() under a lock to perform the swap safely.
        //
        // • DoReloadCore()
        //     Master pipeline that restores vanilla baselines, then applies the active pack:
        //     sounds, splash screens, fonts, models, icons, terrain, etc.
        //
        // • GpuRetireQueue
        //     Defer-disposal queue for GPU resources that must only be disposed
        //     after Draw has finished.
        // ----------------------------------------------------------------------------------

        #region All Tick

        // --- Hot-swap dispatch ---

        /// <summary>
        /// Flag flipped by UI / config / hotkeys to request a texture-pack reload on the next Tick.
        /// </summary>
        private static volatile bool _reloadRequested;

        /// <summary>
        /// Synchronization lock to ensure only one reload runs at a time on the main thread.
        /// </summary>
        private static readonly object _swapLock = new object();

        /// <summary>
        /// Public entry point used by other systems (UI, hotkeys, config watchers)
        /// to request a deferred pack reload.
        /// </summary>
        public static void RequestReload() => _reloadRequested = true;

        /// <summary>
        /// Main-thread pump; call once per frame from the game's Update.
        /// When a reload has been requested, performs the swap via <see cref="DoReloadCore"/>.
        /// </summary>
        public static void Tick()
        {
            if (!_reloadRequested) return;
            lock (_swapLock)
            {
                _reloadRequested = false;
                DoReloadCore(); // Performs the actual safe swap.
            }
        }
        #endregion

        #region All Core

        /// <summary>
        /// Core reload pipeline. Restores vanilla baselines for textures/models/fonts/audio,
        /// then applies the currently selected texture pack and preloads key assets.
        /// Intended to run on the main thread.
        /// </summary>
        public static void DoReloadCore()
        {
            var gm = CastleMinerZGame.Instance;
            var gd = gm.GraphicsDevice;

            gd.SetRenderTarget(null);
            UnbindAllTextures(gd);

            // Sounds.
            ReplacementAudio.OnPackSwitched();

            // Splash screens (menu backdrop, logo, dialog) - central control.
            SplashPacks.OnPackSwitched(gd, gm);

            // Fonts with deferred disposal:
            FontPacks.OnPackSwitched(gd, gm);
            UIOptionsFontRebinder.BumpEpoch(); // Mark all UI trees dirty for next draw/update.

            // MODELS (global): Restore baselines, clear caches, then apply replacements.
            ModelGeometryManager.OnPackSwitched();
            ModelSkinManager.OnPackSwitched(); // Restore -> clear ours.
            ModelGeometryManager.ApplyActiveModelGeometryNow();
            ModelSkinManager.ApplyActiveModelSkinsNow();

            // ITEM / DOOR MODELS: Clear old per-entity skins + models + reset default caches.
            EntitySkinRegistry.OnPackSwitched();
            ItemModelGeometryOverrides.OnPackSwitched();
            DoorModelGeometryOverrides.OnPackSwitched();

            // Inventory/HUD sprites.
            UISpritePacks.OnPackSwitched();

            // Shaders.
            ShaderOverride.OnPackSwitched();

            // Skies.
            SkyPacks.OnPackSwitched(gd, gm);

            // Particle effects.
            ParticleEffectPacks.OnPackSwitched();

            // Icons: Restore vanilla -> then apply new pack -> then swap.
            CaptureItemsBaselineIfNeeded();   // If not already captured.
            RestoreItemsToVanilla();          // Start from true vanilla pixels.
            ApplyItemIconReplacementsIfAny(); // Will write into _work and SwapIconAtlasesFromCpu().

            _lastAtlas = null; _lastPackName = null;
            RestoreTerrainToVanilla();
            ApplyActivePackIfAny();

            ReplacementAudio.PreloadSfx();
        }

        /// <summary>
        /// Clears all pixel and vertex texture slots on the <see cref="GraphicsDevice"/>.
        /// This avoids "resource is actively set on the device" exceptions when reloading atlases.
        /// </summary>
        static void UnbindAllTextures(GraphicsDevice gd)
        {
            const int PixelSlots = 16, VertexSlots = 4;
            for (int i = 0; i < PixelSlots; i++) { try { gd.Textures[i] = null; } catch { } }
            try { for (int i = 0; i < VertexSlots; i++) gd.VertexTextures[i] = null; } catch { }
        }
        #endregion

        #region Defer Draw: FlushRetireQueue

        /// <summary>
        /// Defer GPU resource disposal until AFTER the frame finishes drawing.
        /// Enqueue disposables from anywhere, then call <see cref="FlushAfterDraw"/>
        /// once per frame from a Game.Draw postfix.
        /// </summary>
        internal static class GpuRetireQueue
        {
            private static readonly object _lock = new object();
            private static readonly List<IDisposable> _afterDraw = new List<IDisposable>();

            /// <summary>
            /// Queue a GPU-backed resource (Texture2D, RenderTarget2D, etc.) for disposal
            /// after the current frame's draw call completes.
            /// </summary>
            public static void Enqueue(IDisposable res)
            {
                if (res == null) return;
                lock (_lock) _afterDraw.Add(res);
            }

            /// <summary>
            /// Flush and dispose all queued resources. Call once per frame from Game.Draw.
            /// </summary>
            public static void FlushAfterDraw()
            {
                IDisposable[] batch;
                lock (_lock)
                {
                    if (_afterDraw.Count == 0) return;
                    batch = _afterDraw.ToArray();
                    _afterDraw.Clear();
                }
                for (int i = 0; i < batch.Length; i++)
                {
                    try { batch[i]?.Dispose(); } catch { /* Ignore. */ }
                }
            }
        }
        #endregion

        #endregion

        #region Texture Pack Button / UI

        /// <summary>
        /// Non-modal overlay that lists subfolders in PacksRoot (excluding folders starting with "_").
        /// First row is a synthetic "(default)" option that clears ActivePack (vanilla).
        /// Select a row and Apply (or press Enter / double-click) to set ActivePack (deferred reload).
        /// ESC / Close closes the overlay. Mouse wheel scroll supported.
        /// </summary>
        internal sealed class TexturePackPickerScreen : Screen
        {
            #region Fields & Layout Config

            private readonly CastleMinerZGame _game;
            private SpriteFont _titleFont, _itemFont, _smallFont, _smallFontItalic;
            private Texture2D  _white;

            // Viewport safety: Cache last viewport to recompute layout on resize.
            private Rectangle _lastVp;

            // Color copy (CMZColors.MenuGreen is not public).
            private static readonly Color MenuGreen = new Color(78, 177, 61);

            // ---- DEFAULT/SENTINEL ----
            private const string DEFAULT_KEY                  = "__DEFAULT__"; // Internal key, never equals a folder name.
            private const string DEFAULT_LABEL                = "(default)";   // What the user sees.
            private const string DEFAULT_ICON_RESOURCE_SUFFIX = "pack.png";    // Embedded resource to use as icon.

            // Data: pack folder names (+ DEFAULT row).
            private readonly List<string> _packs = new List<string>();         // Contains DEFAULT_KEY + folder names.
            private int _selectedIndex = -1;

            // Scroll state (pixel offset).
            private int _scroll;                                               // Px.
            private int RowH, RowPad;

            private Rectangle _scrollbarTrackRect;                             // Track rectangle for the vertical scrollbar.
            private Rectangle _scrollbarThumbRect;                             // Thumb rectangle that represents the visible portion of the list.
            private bool      _scrollbarVisible;                               // True if the scrollbar should be drawn (content taller than view).
            private bool      _draggingScrollbar;                              // True while the user is dragging the scrollbar thumb.
            private int       _dragStartMouseY;                                // Mouse Y position when a thumb drag starts.
            private int       _dragStartScroll;                                // Scroll offset at the start of a thumb drag.

            // Layout rectangles.
            private Rectangle _panelRect;                                      // Main window.
            private Rectangle _listRect;                                       // Scrolling list.
            private Rectangle _applyRect, _refreshRect, _closeRect;
            private int PanelPad, TitleGap, ButtonsGap;                        // Inner panel padding, space under title, space above buttons.

            // --- Pack icon support ---
            private readonly Dictionary<string, Texture2D> _icons =
                new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            private int IconPad, IconGap;
            private int IconSize => RowH - IconPad * 2;
            private static readonly string[] IconNames = { "icon", "pack", "preview", "logo" };
            private static readonly string[] IconExts  = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

            // --- Optional pack about/description support ---
            private readonly Dictionary<string, PackAboutInfo> _about =
                new Dictionary<string, PackAboutInfo>(StringComparer.OrdinalIgnoreCase);

            private sealed class PackAboutInfo
            {
                public string Line1;
                public string Line2;

                public bool HasText =>
                    !string.IsNullOrWhiteSpace(Line1) ||
                    !string.IsNullOrWhiteSpace(Line2);
            }
            #endregion

            #region Optional Pack About Metadata + Description Formatting

            /// <summary>
            /// Returns the optional root-level about.json path for a pack, if present.
            /// </summary>
            private static string FindAboutPathForPack(string packDir)
            {
                // Keep it simple: one supported file name.
                var p = Path.Combine(packDir, "about.json");
                return File.Exists(p) ? p : null;
            }

            /// <summary>
            /// Loads and parses a pack description from the optional about.json file.
            /// </summary>
            private static PackAboutInfo LoadAboutForPack(string packDir)
            {
                try
                {
                    var path = FindAboutPathForPack(packDir);
                    if (string.IsNullOrEmpty(path))
                        return null;

                    var json = File.ReadAllText(path);

                    // Minimal JSON extraction:
                    // pulls the first "description": "..." it finds.
                    var m = Regex.Match(
                        json,
                        "\"description\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"\\\\])*)\"",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    if (!m.Success)
                        return null;

                    string desc = m.Groups["v"].Value;

                    // Basic JSON unescape for common sequences.
                    desc = desc.Replace("\\r", "\r")
                               .Replace("\\n", "\n")
                               .Replace("\\t", "\t")
                               .Replace("\\\"", "\"")
                               .Replace("\\\\", "\\");

                    // Normalize line endings but KEEP line breaks.
                    desc = desc.Replace("\r\n", "\n").Replace('\r', '\n');

                    // Support \n in about.json as the line separator (max 2 lines).
                    string[] parts = desc.Split(new[] { '\n' }, 2, StringSplitOptions.None);

                    string line1 = parts.Length > 0
                        ? Regex.Replace(parts[0], "[ \t]+", " ").Trim()
                        : null;

                    string line2 = parts.Length > 1
                        ? Regex.Replace(parts[1], "[ \t]+", " ").Trim()
                        : null;

                    if (string.IsNullOrWhiteSpace(line1) && string.IsNullOrWhiteSpace(line2))
                        return null;

                    line1 = TrimTo(line1, 60);
                    line2 = TrimTo(line2, 60);

                    return new PackAboutInfo
                    {
                        Line1 = line1,
                        Line2 = line2
                    };
                }
                catch
                {
                    // Optional metadata should never break the picker.
                    return null;
                }
            }

            /// <summary>
            /// Trims a string to a max character count and adds "..." when needed.
            /// </summary>
            private static string TrimTo(string s, int maxChars)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return null;

                s = s.Trim();
                if (s.Length <= maxChars)
                    return s;

                if (maxChars <= 3)
                    return s.Substring(0, maxChars);

                return s.Substring(0, maxChars - 3).TrimEnd() + "...";
            }
            #endregion

            #region SFX & Text Helpers

            // --- SFX helpers ---
            private static void PlayClick()
            {
                try { SoundManager.Instance?.PlayInstance("Click"); } catch { }
            }

            private static void PlaySelect()
            {
                // Use a distinct "Select" cue here; else reuse Click.
                try { SoundManager.Instance?.PlayInstance("Award"); } catch { }
            }

            /// <summary>
            /// Snap to whole pixels to avoid blurry/shifted text with linear filtering.
            /// </summary>
            private static Vector2 PixelSnap(Vector2 p)
                => new Vector2((float)Math.Round(p.X), (float)Math.Round(p.Y));

            /// <summary>
            /// Helper that draws centered text inside <paramref name="bounds"/>, with optional drop shadow.
            /// </summary>
            private static void DrawStringCentered(SpriteBatch sb, SpriteFont f, string text,
                                                   Rectangle bounds, Color color,
                                                   float scale = 1f, bool shadow = true)
            {
                if (f == null || string.IsNullOrEmpty(text)) return;

                Vector2 size = f.MeasureString(text) * scale;
                Vector2 pos = new Vector2(bounds.Center.X - size.X * 0.5f,
                                           bounds.Center.Y - size.Y * 0.5f);
                pos = PixelSnap(pos);

                if (shadow) sb.DrawString(f, text, pos + new Vector2(1, 1), Color.Black, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(f, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            #endregion

            #region Styled Text Helpers

            /// <summary>
            /// Small parsed text segment used for inline-styled description drawing.
            /// Each run stores the text plus the style state active when it was parsed.
            /// </summary>
            private sealed class StyledRun
            {
                public string Text;
                public Color  Color;
                public bool   Bold;
                public bool   Underline;
                public bool   Strikethrough;
                public bool   Italic;
            }

            /// <summary>
            /// Parses a description string containing Minecraft-style formatting codes
            /// (for example &a, &l, &n, &m, &o, &r or § variants) into styled text runs.
            /// </summary>
            private static List<StyledRun> ParseStyledRuns(string input, Color defaultColor)
            {
                var runs = new List<StyledRun>();
                if (string.IsNullOrEmpty(input))
                    return runs;

                var sb = new StringBuilder();

                Color curColor = defaultColor;
                bool curBold = false;
                bool curUnderline = false;
                bool curStrikethrough = false;
                bool curItalic = false;

                void Flush()
                {
                    if (sb.Length == 0)
                        return;

                    runs.Add(new StyledRun
                    {
                        Text          = sb.ToString(),
                        Color         = curColor,
                        Bold          = curBold,
                        Underline     = curUnderline,
                        Strikethrough = curStrikethrough,
                        Italic        = curItalic
                    });

                    sb.Length = 0;
                }

                for (int i = 0; i < input.Length; i++)
                {
                    char ch = input[i];

                    if ((ch == '§' || ch == '&') && i + 1 < input.Length)
                    {
                        char code = char.ToLowerInvariant(input[i + 1]);
                        Flush();

                        switch (code)
                        {
                            case '0': curColor = Color.Black;              break;
                            case '1': curColor = new Color(0, 0, 170);     break;
                            case '2': curColor = new Color(0, 170, 0);     break;
                            case '3': curColor = new Color(0, 170, 170);   break;
                            case '4': curColor = new Color(170, 0, 0);     break;
                            case '5': curColor = new Color(170, 0, 170);   break;
                            case '6': curColor = new Color(255, 170, 0);   break;
                            case '7': curColor = new Color(170, 170, 170); break;
                            case '8': curColor = new Color(85, 85, 85);    break;
                            case '9': curColor = new Color(85, 85, 255);   break;
                            case 'a': curColor = new Color(85, 255, 85);   break;
                            case 'b': curColor = new Color(85, 255, 255);  break;
                            case 'c': curColor = new Color(255, 85, 85);   break;
                            case 'd': curColor = new Color(255, 85, 255);  break;
                            case 'e': curColor = new Color(255, 255, 85);  break;
                            case 'f': curColor = Color.White; break;

                            case 'l': curBold      = true; break;
                            case 'n': curUnderline = true; break;
                            case 'm': curStrikethrough = true; break;
                            case 'o': curItalic = true; break;

                            case 'r':
                                curColor     = defaultColor;
                                curBold      = false;
                                curUnderline = false;
                                curStrikethrough = false;
                                break;

                            default:
                                // Unknown code: keep it ignored.
                                break;
                        }

                        i++; // Skip code char too.
                        continue;
                    }

                    sb.Append(ch);
                }

                Flush();
                return runs;
            }

            /// <summary>
            /// Draws one line of styled text by parsing formatting codes into runs,
            /// then rendering each run in sequence with optional shadow and decorations.
            /// </summary>
            private void DrawStyledLine(
                SpriteBatch sb,
                SpriteFont  font,
                SpriteFont  italicFont,
                string      text,
                Vector2     pos,
                Color       defaultColor)
            {
                if (font == null || string.IsNullOrEmpty(text))
                    return;

                var runs = ParseStyledRuns(text, defaultColor);
                float x = pos.X;

                foreach (var run in runs)
                {
                    if (string.IsNullOrEmpty(run.Text))
                        continue;

                    SpriteFont runFont = (run.Italic && italicFont != null) ? italicFont : font;
                    var p = PixelSnap(new Vector2(x, pos.Y));
                    Vector2 size = runFont.MeasureString(run.Text);

                    // Shadow.
                    sb.DrawString(runFont, run.Text, p + new Vector2(1, 1), Color.Black * 0.75f);

                    // Main draw.
                    sb.DrawString(runFont, run.Text, p, run.Color);

                    // Fake bold.
                    if (run.Bold)
                    {
                        sb.DrawString(runFont, run.Text, p + new Vector2(1, 0), run.Color);
                    }

                    // Underline.
                    if (run.Underline && _white != null)
                    {
                        int uy = (int)(p.Y + runFont.LineSpacing - 3);
                        sb.Draw(_white, new Rectangle((int)x, uy, Math.Max(1, (int)size.X), 1), run.Color);
                    }

                    // Strikethrough.
                    if (run.Strikethrough && _white != null)
                    {
                        int sy = (int)(p.Y + (runFont.LineSpacing * 0.5f));
                        sb.Draw(_white, new Rectangle((int)x, sy, Math.Max(1, (int)size.X), 1), run.Color);
                    }

                    x += size.X;
                }
            }
            #endregion

            #region Lifecycle & Layout

            public TexturePackPickerScreen(CastleMinerZGame game)
                : base(acceptInput: true, drawBehind: true)
            {
                _game = game ?? CastleMinerZGame.Instance;
            }

            /// <summary>
            /// Rebuild layout when the viewport changes (window resize, fullscreen toggle, etc.).
            /// </summary>
            private void EnsureLayoutForCurrentViewport(GraphicsDevice gd)
            {
                if (gd == null) return;
                var vp = gd.Viewport.Bounds;
                if (vp != _lastVp)
                {
                    _lastVp = vp;
                    LayoutToScreen();
                }
            }

            /// <summary>
            /// Pull UI layout from TPConfig (row height, paddings, gaps, icon padding).
            /// </summary>
            private void LoadUiFromConfig()
            {
                var cfg = TPConfig.LoadOrCreate();
                RowH = cfg.UI_RowH;
                RowPad = cfg.UI_RowPad;
                PanelPad = cfg.UI_PanelPad;
                TitleGap = cfg.UI_TitleGap;
                ButtonsGap = cfg.UI_ButtonsGap;
                IconPad = cfg.UI_IconPad;
                IconGap = cfg.UI_IconGap;
            }

            public override void OnPushed()
            {
                base.OnPushed();

                var gm = _game ?? CastleMinerZGame.Instance;
                _titleFont = gm?._largeFont ?? gm?._myriadMed ?? gm?._consoleFont;
                _itemFont = gm?._medFont ?? gm?._consoleFont ?? gm?._myriadMed;
                _smallFont = gm?._myriadSmall ?? gm?._smallFont ?? _itemFont;
                _smallFontItalic = gm?._smallFont; // There is no italic font in the base game.

                var gd = gm?.GraphicsDevice;
                if (gd != null)
                {
                    _white = new Texture2D(gd, 1, 1);
                    _white.SetData(new[] { Color.White });
                }

                LoadUiFromConfig();
                RebuildList();
                LoadIcons(gd);
                LoadAboutFiles();
                LayoutToScreen();
            }

            public override void OnPoped()
            {
                try { _white?.Dispose(); } catch { }
                _white = null;
                try { foreach (var t in _icons.Values) t?.Dispose(); } catch { }
                _icons.Clear();
                _about.Clear();
                base.OnPoped();
            }

            /// <summary>
            /// Build the pack list from subfolders under PacksRoot. Inserts the synthetic DEFAULT first.
            /// Uses TPConfig.ActivePack to determine the initial selection.
            /// </summary>
            private void RebuildList()
            {
                _packs.Clear();
                try
                {
                    // Always put DEFAULT first.
                    _packs.Add(DEFAULT_KEY);

                    var root = TexturePackManager.PacksRoot;
                    if (Directory.Exists(root))
                    {
                        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                        {
                            var name = Path.GetFileName(dir);
                            if (string.IsNullOrEmpty(name)) continue;
                            if (name.StartsWith("_", StringComparison.OrdinalIgnoreCase)) continue; // Exclude.
                            _packs.Add(name);
                        }
                    }

                    // Keep DEFAULT first; sort the rest.
                    if (_packs.Count > 1)
                    {
                        var rest = _packs.Skip(1).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                        _packs.Clear();
                        _packs.Add(DEFAULT_KEY);
                        _packs.AddRange(rest);
                    }

                    var cfg = TPConfig.LoadOrCreate();
                    var cur = cfg.ActivePack ?? "";

                    // Select DEFAULT if there is no active pack; else select the matching folder.
                    _selectedIndex = string.IsNullOrEmpty(cur)
                        ? 0
                        : _packs.FindIndex(p => !string.Equals(p, DEFAULT_KEY) &&
                                                string.Equals(p, cur, StringComparison.OrdinalIgnoreCase));
                    if (_selectedIndex < 0) _selectedIndex = 0;
                }
                catch (Exception ex)
                {
                    Log($"[Picker] RebuildList failed: {ex.Message}.");
                    // Fallback: Show DEFAULT only.
                    if (_packs.Count == 0) _packs.Add(DEFAULT_KEY);
                    _selectedIndex = 0;
                }
            }

            /// <summary>
            /// Recalculate scrollbar track/thumb rectangles based on the current
            /// list rect, content height, and scroll offset.
            /// </summary>
            private void UpdateScrollbarRects()
            {
                _scrollbarVisible = false;

                if (_packs == null || _packs.Count == 0)
                    return;
                if (_listRect.Height <= 0)
                    return;

                int contentH = _packs.Count * (RowH + RowPad);
                if (contentH <= _listRect.Height)
                    return; // Everything fits, no scrollbar.

                _scrollbarVisible = true;

                // Thickness of the scrollbar (same idea as the achievements screen).
                int trackWidth = (int)(18f * Screen.Adjuster.ScaleFactor.Y);

                _scrollbarTrackRect = new Rectangle(
                    _listRect.Right - trackWidth,
                    _listRect.Top,
                    trackWidth,
                    _listRect.Height);

                // Clamp scroll to valid range.
                int maxScroll = Math.Max(0, contentH - _listRect.Height);
                if (_scroll < 0) _scroll = 0;
                if (_scroll > maxScroll) _scroll = maxScroll;

                // Thumb height ~ visible fraction, but never too tiny.
                int minThumb    = (int)(20f * Screen.Adjuster.ScaleFactor.Y);
                int thumbHeight = Math.Max(
                    minThumb,
                    (int)((float)_listRect.Height * _listRect.Height / contentH));

                float t = (maxScroll > 0) ? (float)_scroll / maxScroll : 0f;
                int thumbY = _scrollbarTrackRect.Top +
                             (int)(t * (_scrollbarTrackRect.Height - thumbHeight));

                _scrollbarThumbRect = new Rectangle(
                    _scrollbarTrackRect.X + 2,
                    thumbY,
                    _scrollbarTrackRect.Width - 4,
                    thumbHeight);
            }

            /// <summary>
            /// Loads optional per-pack "about" descriptions into the local cache,
            /// including a built-in description for the default CastleMinerZ pack.
            /// </summary>
            private void LoadAboutFiles()
            {
                _about.Clear();

                // Built-in description for the default / vanilla option.
                _about[DEFAULT_KEY] = new PackAboutInfo
                {
                    Line1 = "The default look of CastleMinerZ",
                    Line2 = null
                };

                var root = TexturePackManager.PacksRoot;
                foreach (var pack in _packs)
                {
                    if (pack == DEFAULT_KEY)
                        continue;

                    try
                    {
                        var dir = Path.Combine(root, pack);
                        var info = LoadAboutForPack(dir);
                        if (info != null && info.HasText)
                            _about[pack] = info;
                    }
                    catch
                    {
                        // Ignore bad/missing metadata.
                    }
                }
            }

            /// <summary>
            /// Compute panel, list, and button rectangles based on the current Screen.Adjuster.ScreenRect.
            /// </summary>
            private void LayoutToScreen()
            {
                var r = Screen.Adjuster.ScreenRect;

                int W = Math.Max((int)(r.Width * 0.60f), 720);
                int H = Math.Max((int)(r.Height * 0.70f), 480);
                _panelRect = new Rectangle(r.Center.X - W / 2, r.Center.Y - H / 2, W, H);

                // Title height.
                var titleText = "Texture Packs";
                int titleH = _titleFont != null
                    ? (int)Math.Ceiling(Math.Max(_titleFont.MeasureString(titleText).Y, _titleFont.LineSpacing))
                    : 48;

                int btnH     = Math.Max(38, _itemFont != null ? _itemFont.LineSpacing + 10 : 38);
                int btnW     = 140;
                int btnGap   = 16;

                int titleTop = _panelRect.Top + PanelPad;
                int listTop  = titleTop + titleH + TitleGap;

                int btnY     = _panelRect.Bottom - PanelPad - btnH;

                int listLeft = _panelRect.Left + PanelPad;
                int listW    = _panelRect.Width - PanelPad * 2;
                int listH    = Math.Max(64, btnY - ButtonsGap - listTop);
                _listRect    = new Rectangle(listLeft, listTop, listW, listH);

                int btnX0    = _panelRect.Right - (btnW * 3 + btnGap * 2) - PanelPad;
                _applyRect   = new Rectangle(btnX0, btnY, btnW, btnH);
                _refreshRect = new Rectangle(_applyRect.Right + btnGap, btnY, btnW, btnH);
                _closeRect   = new Rectangle(_refreshRect.Right + btnGap, btnY, btnW, btnH);

                // Initialize scrollbar layout for current panel/list rect.
                UpdateScrollbarRects();
            }

            #endregion

            #region Rendering

            protected override void OnDraw(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                EnsureLayoutForCurrentViewport(device);

                // Backdrop.
                spriteBatch.Begin();
                spriteBatch.Draw(_white, Screen.Adjuster.ScreenRect, new Color(0, 0, 0, 180));
                spriteBatch.End();

                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                DrawPanel(spriteBatch);
                DrawTitle(spriteBatch);
                DrawList(spriteBatch);
                DrawButtons(spriteBatch);
                spriteBatch.End();

                base.OnDraw(device, spriteBatch, gameTime);
            }

            private void DrawPanel(SpriteBatch sb)
            {
                var bg = new Color(18, 18, 18, 230);
                var border = new Color(60, 60, 60, 255);
                sb.Draw(_white, _panelRect, bg);
                sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Top, _panelRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Bottom - 1, _panelRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Top, 1, _panelRect.Height), border);
                sb.Draw(_white, new Rectangle(_panelRect.Right - 1, _panelRect.Top, 1, _panelRect.Height), border);
            }

            private void DrawTitle(SpriteBatch sb)
            {
                DrawStringCentered(sb, _titleFont, "Texture Packs",
                                   new Rectangle(_panelRect.Left, _panelRect.Top + PanelPad, _panelRect.Width, 56),
                                   MenuGreen, 1f, shadow: true);
            }

            private void DrawButtons(SpriteBatch sb)
            {
                DrawButton(sb, _applyRect, "APPLY");
                DrawButton(sb, _refreshRect, "REFRESH");
                DrawButton(sb, _closeRect, "CLOSE");

                var cfg = TPConfig.LoadOrCreate();
                string activeDisplay = string.IsNullOrEmpty(cfg.ActivePack) ? DEFAULT_LABEL : cfg.ActivePack;

                var hint = $"Active: {activeDisplay}";
                Vector2 size = _smallFont.MeasureString(hint);
                Vector2 pos = PixelSnap(new Vector2(_panelRect.Left + 24, _closeRect.Center.Y - size.Y * 0.5f));
                sb.DrawString(_smallFont, hint, pos + new Vector2(1, 1), Color.Black);
                sb.DrawString(_smallFont, hint, pos, Color.White);
            }

            private void DrawButton(SpriteBatch sb, Rectangle r, string text)
            {
                var fill = new Color(30, 30, 30, 255);
                var outline = new Color(80, 80, 80, 255);

                sb.Draw(_white, r, fill);
                sb.Draw(_white, new Rectangle(r.Left, r.Top, r.Width, 1), outline);
                sb.Draw(_white, new Rectangle(r.Left, r.Bottom - 1, r.Width, 1), outline);
                sb.Draw(_white, new Rectangle(r.Left, r.Top, 1, r.Height), outline);
                sb.Draw(_white, new Rectangle(r.Right - 1, r.Top, 1, r.Height), outline);

                DrawStringCentered(sb, _itemFont, text, r, Color.White, 1f, shadow: true);
            }

            private void DrawList(SpriteBatch sb)
            {
                var gd = sb.GraphicsDevice;

                // Save/restore GD state.
                var oldVP  = gd.Viewport;
                var oldSR  = gd.ScissorRectangle;
                var oldRS  = gd.RasterizerState;
                var oldBS  = gd.BlendState;
                var oldSS0 = gd.SamplerStates[0];
                var oldDS  = gd.DepthStencilState;

                sb.End();
                try
                {
                    var raster = new RasterizerState { ScissorTestEnable = true };
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, raster);

                    gd.Viewport = oldVP;
                    gd.ScissorRectangle = ClampToViewport(_listRect, gd);

                    // Recompute scrollbar geom for this frame.
                    UpdateScrollbarRects();

                    sb.Draw(_white, _listRect, new Color(22, 22, 22, 255));

                    int y         = _listRect.Top - _scroll;
                    var cfg       = TPConfig.LoadOrCreate();
                    string active = cfg.ActivePack ?? "";

                    // If scrollbar is visible, shrink content width to not draw under it.
                    int contentWidth = _listRect.Width;
                    if (_scrollbarVisible) contentWidth -= _scrollbarTrackRect.Width;

                    for (int i = 0; i < _packs.Count; i++)
                    {
                        var name = _packs[i];
                        var row = new Rectangle(_listRect.Left, y, contentWidth, RowH);

                        bool visible = row.Bottom >= _listRect.Top && row.Top <= _listRect.Bottom;
                        if (visible)
                        {
                            var fill = (i % 2 == 0) ? new Color(30, 30, 30, 255) : new Color(28, 28, 28, 255);
                            sb.Draw(_white, row, fill);

                            bool isActive = string.IsNullOrEmpty(active) ? (name == DEFAULT_KEY)
                                                                         : string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
                            if (isActive)
                                sb.Draw(_white, new Rectangle(row.Left, row.Top, 4, row.Height), MenuGreen);

                            // Icon slot.
                            var iconRect = new Rectangle(
                                row.Left + 12,
                                row.Top + (row.Height - IconSize) / 2,
                                IconSize,
                                IconSize);

                            _icons.TryGetValue(name, out Texture2D iconTex);

                            if (iconTex != null && !iconTex.IsDisposed)
                                sb.Draw(iconTex, iconRect, Color.White);
                            else
                                sb.Draw(_white, iconRect, new Color(45, 45, 45, 255));

                            var outline = new Color(20, 20, 20, 160);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Top, iconRect.Width, 1), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Bottom - 1, iconRect.Width, 1), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Top, 1, iconRect.Height), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Right - 1, iconRect.Top, 1, iconRect.Height), outline);

                            int textLeft = iconRect.Right + IconGap;
                            var textRect = new Rectangle(textLeft, row.Top, row.Right - textLeft, row.Height);
                            var label    = (name == DEFAULT_KEY) ? DEFAULT_LABEL : name;
                            var col      = (i == _selectedIndex) ? MenuGreen : Color.White;

                            // Split point: 1/3 from the left side of the text area.
                            int sepX = textRect.Left + (textRect.Width / 3);

                            // -------------------------
                            // Left side: title
                            // -------------------------
                            Vector2 sz = _itemFont.MeasureString(label);
                            var pos = PixelSnap(new Vector2(textRect.Left, textRect.Center.Y - sz.Y * 0.5f));
                            sb.DrawString(_itemFont, label, pos + new Vector2(1, 1), Color.Black * 0.85f);
                            sb.DrawString(_itemFont, label, pos, col);

                            // -------------------------
                            // Separator
                            // -------------------------
                            if (_white != null)
                            {
                                // Small inset so it doesn't touch the row borders.
                                int lineTop = row.Top + 6;
                                int lineH = Math.Max(0, row.Height - 12);
                                sb.Draw(_white, new Rectangle(sepX, lineTop, 1, lineH), Color.White * 0.20f);
                            }

                            // -------------------------
                            // Right side: description
                            // Left-aligned to separator
                            // -------------------------
                            if (_smallFont != null &&
                                _about.TryGetValue(name, out PackAboutInfo about) &&
                                about != null &&
                                about.HasText)
                            {
                                string line1 = about.Line1;
                                string line2 = about.Line2;

                                const int descPadLeft = 10;
                                const int descPadRight = 8;

                                int descLeft  = sepX + descPadLeft;
                                int descRight = textRect.Right - descPadRight;
                                int descWidth = Math.Max(0, descRight - descLeft);

                                if (descWidth > 24)
                                {
                                    bool has1 = !string.IsNullOrWhiteSpace(line1);
                                    bool has2 = !string.IsNullOrWhiteSpace(line2);

                                    float totalH = (has1 && has2)
                                        ? (_smallFont.LineSpacing * 2) - 2f
                                        : _smallFont.LineSpacing;

                                    float y2 = textRect.Center.Y - (totalH * 0.5f);

                                    if (has1)
                                    {
                                        var p1 = PixelSnap(new Vector2(descLeft, y2));
                                        // DrawStyledLine(sb, _smallFont, line1, p1 + new Vector2(1, 1), Color.Black * 0.75f);
                                        DrawStyledLine(sb, _smallFont, _smallFontItalic, line1, p1, new Color(210, 210, 210, 255));
                                        y2 += _smallFont.LineSpacing - 2f;
                                    }

                                    if (has2)
                                    {
                                        var p2 = PixelSnap(new Vector2(descLeft, y2));
                                        // DrawStyledLine(sb, _smallFont, line2, p2 + new Vector2(1, 1), Color.Black * 0.75f);
                                        DrawStyledLine(sb, _smallFont, _smallFontItalic, line2, p2, new Color(185, 185, 185, 255));
                                    }
                                }
                            }
                        }

                        y += RowH + RowPad;
                    }

                    // Scrollbar on top.
                    if (_scrollbarVisible)
                    {
                        var trackColor  = new Color(10, 10, 10, 220);
                        var borderColor = new Color(0, 0, 0, 220);
                        var thumbColor  = new Color(170, 170, 170, 255);

                        // Track.
                        sb.Draw(_white, _scrollbarTrackRect, trackColor);
                        sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Top, _scrollbarTrackRect.Width, 1), borderColor);
                        sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Bottom - 1, _scrollbarTrackRect.Width, 1), borderColor);
                        sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Top, 1, _scrollbarTrackRect.Height), borderColor);
                        sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Right - 1, _scrollbarTrackRect.Top, 1, _scrollbarTrackRect.Height), borderColor);

                        // Thumb.
                        sb.Draw(_white, _scrollbarThumbRect, thumbColor);
                    }
                }
                finally
                {
                    sb.End();
                    try
                    {
                        gd.Viewport          = oldVP;
                        gd.ScissorRectangle  = oldSR;
                        gd.RasterizerState   = oldRS  ?? RasterizerState.CullCounterClockwise;
                        gd.BlendState        = oldBS  ?? BlendState.AlphaBlend;
                        gd.SamplerStates[0]  = oldSS0 ?? SamplerState.LinearClamp;
                        gd.DepthStencilState = oldDS  ?? DepthStencilState.None;
                    }
                    catch { }

                    sb.Begin();
                }
            }

            private static Rectangle ClampToViewport(Rectangle r, GraphicsDevice gd)
            {
                var vp = gd.Viewport.Bounds;
                int x  = Math.Max(vp.Left, Math.Min(r.Left, vp.Right));
                int y  = Math.Max(vp.Top, Math.Min(r.Top, vp.Bottom));
                int w  = Math.Max(0, Math.Min(r.Right, vp.Right) - x);
                int h  = Math.Max(0, Math.Min(r.Bottom, vp.Bottom) - y);
                return new Rectangle(x, y, w, h);
            }
            #endregion

            #region Input & Selection

            protected override bool OnPlayerInput(InputManager input, GameController controller, KeyboardInput chatpad, GameTime gameTime)
            {
                if (input.Keyboard.WasKeyPressed(Keys.Escape))
                {
                    PlayClick();
                    this.PopMe();
                    return false;
                }

                int contentH  = _packs.Count * (RowH + RowPad);
                int maxScroll = Math.Max(0, contentH - _listRect.Height);

                // Mouse wheel scroll.
                int dWheel = input.Mouse.DeltaWheel;
                if (dWheel != 0)
                {
                    _scroll -= Math.Sign(dWheel) * (RowH + RowPad);
                    if (_scroll < 0) _scroll = 0;
                    if (_scroll > maxScroll) _scroll = maxScroll;
                }

                // Update scrollbar rectangles based on new scroll.
                UpdateScrollbarRects();

                var mouse = input.Mouse;
                var mpos  = mouse.Position;

                // Scrollbar interactions (thumb drag + track click).
                if (_scrollbarVisible)
                {
                    // Begin drag if user clicks on the thumb.
                    if (mouse.LeftButtonPressed && _scrollbarThumbRect.Contains(mpos))
                    {
                        _draggingScrollbar = true;
                        _dragStartMouseY = mpos.Y;
                        _dragStartScroll = _scroll;
                    }
                    // Click on track (but not thumb) -> jump to that approximate position.
                    else if (mouse.LeftButtonPressed &&
                             _scrollbarTrackRect.Contains(mpos) &&
                             !_scrollbarThumbRect.Contains(mpos))
                    {
                        if (maxScroll > 0)
                        {
                            float tClick = (float)(mpos.Y - _scrollbarTrackRect.Top) /
                                           Math.Max(1, _scrollbarTrackRect.Height);
                            _scroll = (int)(tClick * maxScroll);
                            if (_scroll < 0) _scroll = 0;
                            if (_scroll > maxScroll) _scroll = maxScroll;

                            UpdateScrollbarRects();
                        }
                    }

                    // Dragging while holding the mouse button.
                    if (_draggingScrollbar && mouse.LeftButtonDown)
                    {
                        if (maxScroll > 0)
                        {
                            float trackSpan = Math.Max(1, _scrollbarTrackRect.Height - _scrollbarThumbRect.Height);
                            int dy = mpos.Y - _dragStartMouseY;
                            float deltaT = dy / trackSpan;

                            _scroll = _dragStartScroll + (int)(deltaT * maxScroll);
                            if (_scroll < 0) _scroll = 0;
                            if (_scroll > maxScroll) _scroll = maxScroll;

                            UpdateScrollbarRects();
                        }
                    }
                    else if (!mouse.LeftButtonDown)
                    {
                        _draggingScrollbar = false;
                    }
                }

                // List selection (ignore clicks on scrollbar area).
                if (mouse.LeftButtonPressed &&
                    _listRect.Contains(mpos) &&
                    (!_scrollbarVisible || !_scrollbarTrackRect.Contains(mpos)))
                {
                    int relY = mpos.Y - _listRect.Top + _scroll;
                    int index = relY / (RowH + RowPad);
                    if (index >= 0 && index < _packs.Count)
                    {
                        if (_selectedIndex == index)
                        {
                            PlaySelect();    // Double-click -> apply.
                            ApplySelected();
                        }
                        else
                        {
                            _selectedIndex = index;
                            PlayClick();     // Moved selection with mouse.
                        }
                    }
                }

                // Buttons.
                if (input.Mouse.LeftButtonPressed)
                {
                    if      (_applyRect.Contains(mpos))   { PlaySelect(); ApplySelected(); }
                    else if (_refreshRect.Contains(mpos)) { PlayClick(); RebuildList(); LoadIcons(_game?.GraphicsDevice ?? CastleMinerZGame.Instance?.GraphicsDevice); LoadAboutFiles(); }
                    else if (_closeRect.Contains(mpos))   { PlayClick(); this.PopMe(); }
                }

                // Keyboard navigation.
                if (input.Keyboard.WasKeyPressed(Keys.Up) && _packs.Count > 0)
                {
                    int old = _selectedIndex;
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    if (_selectedIndex != old) PlayClick();
                    EnsureRowVisible(_selectedIndex);
                }
                if (input.Keyboard.WasKeyPressed(Keys.Down) && _packs.Count > 0)
                {
                    int old = _selectedIndex;
                    _selectedIndex = Math.Min(_packs.Count - 1, Math.Max(0, _selectedIndex) + 1);
                    if (_selectedIndex != old) PlayClick();
                    EnsureRowVisible(_selectedIndex);
                }
                if (input.Keyboard.WasKeyPressed(Keys.Enter))
                {
                    PlaySelect();
                    ApplySelected();
                }

                return base.OnPlayerInput(input, controller, chatpad, gameTime);
            }

            /// <summary>
            /// Adjust scroll so the given row index is fully visible in the scroll window.
            /// </summary>
            private void EnsureRowVisible(int index)
            {
                if (index < 0 || index >= _packs.Count) return;

                int itemTop = index * (RowH + RowPad);
                int itemBot = itemTop + RowH;

                if (itemTop < _scroll) _scroll = itemTop;
                else if (itemBot > _scroll + _listRect.Height) _scroll = itemBot - _listRect.Height;

                if (_scroll < 0) _scroll = 0;

                int contentH = _packs.Count * (RowH + RowPad);
                int maxScroll = Math.Max(0, contentH - _listRect.Height);
                if (_scroll > maxScroll) _scroll = maxScroll;
            }

            /// <summary>
            /// Commit the current selection: update TPConfig.ActivePack and request a deferred reload.
            /// Pops the picker screen when finished.
            /// </summary>
            private void ApplySelected()
            {
                try
                {
                    if (_selectedIndex < 0 || _selectedIndex >= _packs.Count)
                        return;

                    var chosen = _packs[_selectedIndex];
                    var cfg = TPConfig.LoadOrCreate();

                    if (chosen == DEFAULT_KEY)
                    {
                        // Clear active pack -> vanilla.
                        cfg.ActivePack = string.Empty;
                        try { cfg.Save(); } catch { }
                        Log("[Picker] Restoring vanilla textures (no pack)...");
                        GamePatches.TexturePackPickerPatches.DeferredApply.Request(""); // Deferred reload next Update.
                    }
                    else
                    {
                        cfg.ActivePack = chosen;
                        try { cfg.Save(); } catch { }
                        Log($"[Picker] Applying '{chosen}' (deferred reload)...");
                        GamePatches.TexturePackPickerPatches.DeferredApply.Request(chosen);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Picker] Apply failed: {ex.Message}.");
                }
                finally
                {
                    this.PopMe();
                }
            }
            #endregion

            #region Icon Discovery & Loading

            /// <summary>
            /// Search for a suitable icon file in a given pack directory using <see cref="IconNames"/> and <see cref="IconExts"/>.
            /// </summary>
            private static string FindIconPathForPack(string packDir)
            {
                foreach (var stem in IconNames)
                    foreach (var ext in IconExts)
                    {
                        var p = Path.Combine(packDir, stem + ext);
                        if (File.Exists(p)) return p;
                    }
                return null;
            }

            /// <summary>
            /// Load pack icons (including the embedded DEFAULT icon) into <see cref="_icons"/>.
            /// Safe to call multiple times (previous icons are disposed/discarded).
            /// </summary>
            private void LoadIcons(GraphicsDevice gd)
            {
                foreach (var t in _icons.Values) { try { t.Dispose(); } catch { } }
                _icons.Clear();
                if (gd == null) return;

                // 1) Embedded icon for DEFAULT.
                try
                {
                    var asm = typeof(TexturePackPickerScreen).Assembly;
                    var rn = asm.GetManifestResourceNames()
                                .FirstOrDefault(n => n.EndsWith(DEFAULT_ICON_RESOURCE_SUFFIX, StringComparison.OrdinalIgnoreCase));
                    if (rn != null)
                    {
                        using (var s = asm.GetManifestResourceStream(rn))
                        {
                            if (s != null)
                                _icons[DEFAULT_KEY] = Texture2D.FromStream(gd, s);
                        }
                    }
                }
                catch { /* No embedded icon -> we'll draw the gray placeholder. */ }

                // 2) Icons for real pack folders
                var root = TexturePackManager.PacksRoot;
                foreach (var pack in _packs)
                {
                    if (pack == DEFAULT_KEY) continue;

                    var dir = Path.Combine(root, pack);
                    var iconPath = FindIconPathForPack(dir);
                    if (iconPath == null) continue;

                    try
                    {
                        using (var s = File.OpenRead(iconPath))
                            _icons[pack] = Texture2D.FromStream(gd, s);
                    }
                    catch { /* Ignore bad icon; keep row text-only. */ }
                }
            }
            #endregion
        }
        #endregion
    }
}
/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using HarmonyLib;
using DNA.Audio;
using System;

using static TooManyItems.ConfigGlobals;
using static TooManyItems.TMIOverlay;
using static ModLoader.LogSystem;
using static TooManyItems.TMILog;

namespace TooManyItems
{
    /// <summary>
    /// Centralized actions invoked by the TMI overlay (toolbar/buttons).
    /// Keeps game-side logic in one place while the UI stays dumb/thin.
    /// </summary>
    internal static class TMIOverlayActions
    {
        #region State & Wiring

        // The currently active CraftingScreen (WeakReference to avoid lifetime issues).
        private static WeakReference<CraftingScreen> _currentCraftWR;

        /// <summary> Called when a CraftingScreen becomes active or closes. </summary>
        public static void SetActiveCraftingScreen(CraftingScreen cs)
        {
            if (cs == null)
            {
                _currentCraftWR = null;
                _craftingIsOpen = false;
            }
            else
            {
                _currentCraftWR = new WeakReference<CraftingScreen>(cs);
                _craftingIsOpen = true;
            }
        }

        // Whether that crafting screen is actually on the ScreenManager stack.
        private static bool _craftingIsOpen;

        // Overlay state flags (driven by UI).
        public static bool _deleteOn;

        public enum   GameMode   { Endurance, Survival, DragonEndurance, Creative, Exploration, Scavenger }
        public static GameMode   _curMode = GameMode.Endurance;

        public enum   Difficulty { Peaceful, Easy, Normal, Hardcore }
        public static Difficulty _curDifficulty = Difficulty.Peaceful;

        public enum   Time       { Sunrise, Noon, Sunset, Midnight }
        public static Time       _curTime = Time.Sunrise;

        #endregion

        #region Held Item State & Helpers

        /// <summary> Reflection handle to CraftingScreen._holdingItem (the item on the cursor). </summary>
        public static readonly FieldInfo _fiHolding =
            AccessTools.Field(typeof(CraftingScreen), "_holdingItem");

        /// <summary> Returns the currently held (cursor) item, or null. </summary>
        public static InventoryItem GetHolding(CraftingScreen s) =>
            (s != null && _fiHolding != null) ? (InventoryItem)_fiHolding.GetValue(s) : null;

        /// <summary> Drops the held item (sets it to null). </summary>
        public static void ClearHolding(CraftingScreen s)
        {
            if (s != null && _fiHolding != null)
                _fiHolding.SetValue(s, null);
        }

        /// <summary> Quick check if the player is holding something right now. </summary>
        public static bool IsHoldingCursorItem()
        {
            var cs = TMIOverlayActions.GetOpenCraftingScreen();
            if (cs == null) return false;
            return GetHolding(cs) != null;
        }
        #endregion

        #region Quick Actions (Toolbar Buttons)

        /// <summary>
        /// Trash button: Shift = delete-all; otherwise toggles "delete mode".
        /// </summary>
        public static void ToggleDelete()
        {
            if (TMIOverlay.ShiftHeld())
            {
                // Shift-click means "delete everything now" (does not change _deleteOn).
                DeleteAllItemsFromInventory();
            }
            else
            {
                _deleteOn = !_deleteOn;
            }
        }

        /// <summary> Programmatic setter (used by menu items / hotkeys). </summary>
        public static void SetDelete(bool state) => _deleteOn = state;

        /// <summary>
        /// Cycle difficulty (Peaceful -> Easy -> Normal -> Hard) and apply to the game.
        /// </summary>
        public static void ToggleDifficulty()
        {
            if (!IsInGame()) return;

            var vals = (Difficulty[])Enum.GetValues(typeof(Difficulty));
            int i    = Array.IndexOf(vals, _curDifficulty);
            _curDifficulty = vals[(i + 1) % vals.Length];

            switch (_curDifficulty)
            {
                case Difficulty.Peaceful:
                    CastleMinerZGame.Instance.Difficulty = GameDifficultyTypes.NOENEMIES;
                    break;
                case Difficulty.Easy:
                    CastleMinerZGame.Instance.Difficulty = GameDifficultyTypes.EASY;
                    break;
                case Difficulty.Normal:
                    CastleMinerZGame.Instance.Difficulty = GameDifficultyTypes.HARD; // CMZ's "Normal" maps to "Hard".
                    break;
                case Difficulty.Hardcore:
                    CastleMinerZGame.Instance.Difficulty = GameDifficultyTypes.HARDCORE;
                    break;
            }

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog($"Difficulty: {_curDifficulty}.");
        }

        /// <summary> Sync overlay difficulty to a value observed from the game. </summary>
        public static void SetSelectedDifficulty(GameDifficultyTypes gameDifficultyTypes)
        {
            switch (gameDifficultyTypes)
            {
                case GameDifficultyTypes.NOENEMIES: _curDifficulty = Difficulty.Peaceful; break;
                case GameDifficultyTypes.EASY:      _curDifficulty = Difficulty.Easy;     break;
                case GameDifficultyTypes.HARD:      _curDifficulty = Difficulty.Normal;   break; // CMZ mapping.
                case GameDifficultyTypes.HARDCORE:  _curDifficulty = Difficulty.Hardcore; break;
            }
        }

        /// <summary>
        /// Set game mode (Endurance, Survival, DragonEndurance, Creative, Scavenger).
        /// </summary>
        public static void SetMode(GameMode gameMode)
        {
            if (!IsInGame()) return;

            _curMode = gameMode;
            try
            {
                switch (gameMode)
                {
                    case GameMode.Endurance:       CastleMinerZGame.Instance.GameMode = GameModeTypes.Endurance;       break;
                    case GameMode.Survival:        CastleMinerZGame.Instance.GameMode = GameModeTypes.Survival;        break;
                    case GameMode.DragonEndurance: CastleMinerZGame.Instance.GameMode = GameModeTypes.DragonEndurance; break;
                    case GameMode.Creative:        CastleMinerZGame.Instance.GameMode = GameModeTypes.Creative;        break;
                    case GameMode.Exploration:     CastleMinerZGame.Instance.GameMode = GameModeTypes.Exploration;     break;
                    case GameMode.Scavenger:       CastleMinerZGame.Instance.GameMode = GameModeTypes.Scavenger;       break;
                }
            }
            catch { /* Ignore set failures (e.g., in menus). */ }

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog($"Game mode: {gameMode}.");
        }

        /// <summary> Sync overlay mode to a value observed from the game. </summary>
        public static void SetSelectedMode(GameModeTypes gameMode)
        {
            try
            {
                switch (gameMode)
                {
                    case GameModeTypes.Endurance:       _curMode = GameMode.Endurance;       break;
                    case GameModeTypes.Survival:        _curMode = GameMode.Survival;        break;
                    case GameModeTypes.DragonEndurance: _curMode = GameMode.DragonEndurance; break;
                    case GameModeTypes.Creative:        _curMode = GameMode.Creative;        break;
                    case GameModeTypes.Exploration:     _curMode = GameMode.Exploration;     break;
                    case GameModeTypes.Scavenger:       _curMode = GameMode.Scavenger;       break;
                }
            }
            catch { }
        }

        /// <summary>
        /// Set time-of-day (preserves current "day" and updates the fractional time).
        /// Sends a network message if the local player is not host.
        /// </summary>
        public static void SetTime(Time newTime)
        {
            if (!IsInGame()) return;

            try
            {
                var day    = (int)CastleMinerZGame.Instance.GameScreen.Day;         // Integer day.
                var tScale = CastleMinerZGame.Instance.GameScreen.TimeOfDay * 100f;

                switch (newTime)
                {
                    case Time.Sunrise:  tScale = 30f;  break;
                    case Time.Noon:     tScale = 50f;  break;
                    case Time.Sunset:   tScale = 75f;  break;
                    case Time.Midnight: tScale = MidnightIsNewday ? 100f : 95f;  break;
                }

                float newTimeFormat = day + (float)(tScale / 100.0);

                if (!CastleMinerZGame.Instance.CurrentNetworkSession.IsHost)
                    TimeOfDayMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, newTimeFormat);

                CastleMinerZGame.Instance.GameScreen.Day = newTimeFormat; // Always sync local.
            }
            catch { }

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog($"Time set: {newTime}.");
        }

        /// <summary> Restore player HP, stamina, & oxygen to full (client-side and immediate). </summary>
        public static void MaxHealthAndStamina()
        {
            if (!IsInGame()) return;

            void Refill(float value = 1f)
            {
                CastleMinerZGame.Instance.GameScreen.HUD.PlayerHealth  = value;
                CastleMinerZGame.Instance.GameScreen.HUD.PlayerStamina = value;
                CastleMinerZGame.Instance.GameScreen.HUD.PlayerOxygen  = value;
            }

            Refill(1f);
            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog("Health & stamina restored.");
        }
        #endregion

        #region Core Access (Shortcuts & Basic Checks)

        private static CastleMinerZGame     Game  => CastleMinerZGame.Instance;
        private static Player               Me    => Game?.LocalPlayer;
        private static PlayerInventory      Inv   => Me?.PlayerInventory;
        private static InventoryTrayManager Trays => Inv?.TrayManager;

        /// <summary> Minimal sanity check that we're in a running game-world. </summary>
        internal static bool IsInGame()
        {
            var g = CastleMinerZGame.Instance;
            return g != null && g.GameScreen != null && g.CurrentNetworkSession != null;
        }

        private static InventoryItem GetTrayItem(int tray, int slot)
            => (Trays != null && tray >= 0 && tray < 2 && slot >= 0 && slot < 8) ? Trays.Trays[tray, slot] : null;

        private static void SetTrayItem(int tray, int slot, InventoryItem item)
        {
            if (Trays == null || tray < 0 || tray >= 2 || slot < 0 || slot >= 8) return;
            Trays.Trays[tray, slot] = item;
        }

        private static InventoryItem GetBackpackItem(int index)
            => (Inv?.Inventory != null && index >= 0 && index < Inv.Inventory.Length) ? Inv.Inventory[index] : null;

        private static void SetBackpackItem(int index, InventoryItem item)
        {
            if (Inv?.Inventory == null || index < 0 || index >= Inv.Inventory.Length) return;
            Inv.Inventory[index] = item;
        }
        #endregion

        #region Inventory Helpers (Stacking, Placement, Sync)

        private const int UNSAFE_STACK_CAP = 100000; // OPTIONAL: Add a "no cap" mode.

        private static int EffectiveStackCap(InventoryItem it)
        {
            int baseCap = Math.Max(1, it?.MaxStackCount ?? 1);
            return baseCap; // Keep vanilla-safe; replace with Math.Max(baseCap, UNSAFE_STACK_CAP) for no-cap mode.
        }

        private static InventoryItem CreateClampedItem(InventoryItemIDs id, int desiredStack, float desiredHealth = 1f)
        {
            var it = InventoryItem.CreateItem(id, Math.Max(1, desiredStack));
            if (it == null) return null;

            int stackCap  = EffectiveStackCap(it);
            it.StackCount = Math.Min(Math.Max(0, desiredStack), stackCap);

            float h = float.IsNaN(desiredHealth) ? 1f : desiredHealth;
            it.ItemHealthLevel = Math.Max(0f, Math.Min(1f, h));

            if (it is GunInventoryItem gi)
            {
                int clipMax = Math.Max(0, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);
                gi.RoundsInClip = Math.Min(gi.RoundsInClip, clipMax);
            }
            return it;
        }

        private static bool SameKind(InventoryItem a, InventoryItem b)
            => a != null && b != null && a.ItemClass?.ID.Equals(b.ItemClass?.ID) == true;

        /// <summary> Try to stack 'src' into existing stacks across trays and backpack. </summary>
        private static void TryStackEverywhere(InventoryItem src)
        {
            if (src == null || src.StackCount <= 0) return;

            // Trays first (hotbars).
            for (int t = 0; t < 2 && src.StackCount > 0; t++)
            {
                for (int s = 0; s < 8 && src.StackCount > 0; s++)
                {
                    var dst = GetTrayItem(t, s);
                    if (!SameKind(dst, src)) continue;

                    int cap  = EffectiveStackCap(dst);
                    int room = Math.Max(0, cap - dst.StackCount);
                    int move = Math.Min(room, src.StackCount);
                    if (move > 0) { dst.StackCount += move; src.StackCount -= move; }
                }
            }

            // Backpack.
            if (Inv?.Inventory != null && src.StackCount > 0)
            {
                for (int i = 0; i < Inv.Inventory.Length && src.StackCount > 0; i++)
                {
                    var dst = GetBackpackItem(i);
                    if (!SameKind(dst, src)) continue;

                    int cap  = EffectiveStackCap(dst);
                    int room = Math.Max(0, cap - dst.StackCount);
                    int move = Math.Min(room, src.StackCount);
                    if (move > 0) { dst.StackCount += move; src.StackCount -= move; }
                }
            }
        }

        /// <summary> Place leftover 'src' into first empty slot (hotbars, then backpack). </summary>
        private static void PlaceIntoFirstEmpty(InventoryItem src)
        {
            if (src == null || src.StackCount <= 0) return;

            // 1) Hotbar 1 (tray 0).
            for (int s = 0; s < 8; s++)
                if (GetTrayItem(0, s) == null) { SetTrayItem(0, s, src); return; }

            // 2) Hotbar 2 (tray 1).
            for (int s = 0; s < 8; s++)
                if (GetTrayItem(1, s) == null) { SetTrayItem(1, s, src); return; }

            // 3) Backpack.
            if (Inv?.Inventory != null)
            {
                for (int i = 0; i < Inv.Inventory.Length; i++)
                    if (GetBackpackItem(i) == null) { SetBackpackItem(i, src); return; }
            }

            SendLog("No empty slot for item.");
        }

        /// <summary>
        /// Ask the game to tidy/sync inventory (safe in SP; in MP it flows through normal save message).
        /// </summary>
        private static void UploadInventoryToServer()
        {
            if (!IsInGame()) return;
            try
            {
                var hudInv = Game?.GameScreen?.HUD?.PlayerInventory;
                hudInv?.RemoveEmptyItems();
                Game?.SaveData();
            }
            catch { /* Keep client-side only if SaveData isn't available here. */ }
        }

        public static CraftingScreen GetOpenCraftingScreen()
        {
            if (!_craftingIsOpen)
                return null;

            if (_currentCraftWR != null &&
                _currentCraftWR.TryGetTarget(out var cs) &&
                cs != null)
            {
                return cs;
            }

            // If the weak reference died, treat it as closed too.
            _craftingIsOpen = false;
            _currentCraftWR = null;
            return null;
        }
        #endregion

        #region Deletion & Give (Overlay-Facing API)

        /// <summary> Delete the inventory slot under the mouse (within CraftingScreen). </summary>
        public static bool TryDeleteUnderMouse()
        {
            if (!IsInGame()) return false;

            var hudInv = CastleMinerZGame.Instance?.GameScreen?.HUD?.PlayerInventory;
            var craft  = GetOpenCraftingScreen();
            if (hudInv == null || craft == null) return false;

            var mp  = Mouse.GetState();
            int hit = craft.HitTest(new Point(mp.X, mp.Y)); // Uses CraftingScreen's rectangles.
            if (hit < 0) return false;

            if (hit < 32)      // Backpack 8x4 grid.
                hudInv.Inventory[hit] = null;
            else if (hit < 40) // Tray 0.
                hudInv.TrayManager.SetTrayItem(0, hit - 32, null);
            else if (hit < 48) // Tray 1.
                hudInv.TrayManager.SetTrayItem(1, hit - 40, null);

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog($"Deleted item at slot {hit}.");
            return true;
        }

        /// <summary>
        /// Delete everything in both trays and the backpack (does not flip _deleteOn).
        /// </summary>
        public static void DeleteAllItemsFromInventory()
        {
            if (!IsInGame()) return;

            try
            {
                // Clear trays.
                for (int t = 0; t < 2; t++)
                    for (int s = 0; s < 8; s++)
                        SetTrayItem(t, s, null);

                // Clear bag.
                if (Inv?.Inventory != null)
                    for (int i = 0; i < Inv.Inventory.Length; i++)
                        SetBackpackItem(i, null);

                if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
                UploadInventoryToServer(); // Keep online sessions synced.
                SendLog("Deleted all items from trays and backpack.");
            }
            catch (Exception ex)
            {
                SendLog($"Delete-all failed: {ex.GetType().Name}: {ex.Message}.");
            }
        }

        /// <summary>
        /// Give an item to the player. Left-click (fullStack=true) gives a full stack; right-click gives 1.
        /// </summary>
        public static void GiveItem(InventoryItemIDs id, bool fullStack)
        {
            if (!IsInGame() || Inv == null) return;

            var probe = InventoryItem.CreateItem(id, 1);
            if (probe == null) { SendFeedback($"Unknown item: {id}"); return; }

            int targetStack = fullStack ? EffectiveStackCap(probe) : 1;
            var toAdd = CreateClampedItem(id, targetStack);

            // Stack into existing piles.
            TryStackEverywhere(toAdd);

            // Drop leftovers into first empty slot.
            if (toAdd.StackCount > 0) PlaceIntoFirstEmpty(toAdd);

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("craft");
            SendLog($"Gave {targetStack} x {id}.");
            UploadInventoryToServer();
        }
        #endregion

        #region Drop Handlers (Delete, Favorite)

        /// <summary>
        /// Delete handler for the right-side Items column.
        /// When the mouse is inside <paramref name="zone"/> and the player is holding an item,
        /// a left click will permanently delete the cursor item (no pickup created, no give-back).
        /// </summary>
        public static void HandleDeleteDrop(Rectangle zone)
        {
            // Require HUD + CraftingScreen. If the crafting screen isn't open, do nothing.
            var hudInv = CastleMinerZGame.Instance?.GameScreen?.HUD?.PlayerInventory;
            var craft  = GetOpenCraftingScreen();
            if (hudInv == null || craft == null) return;

            // Only act when the cursor is inside the Items panel.
            if (!zone.Contains(Input.Mouse)) return;

            // If the player is holding something, offer to delete it on click.
            var heldItem = GetHolding(craft);
            if (heldItem != null)
            {
                // Left click -> nuke the cursor item.
                if (Input.LeftClicked)
                {
                    // Remove the cursor item (does not drop/spawn a pickup).
                    ClearHolding(craft);

                    // Audio/telemetry feedback.
                    if (USE_SOUNDS) SoundManager.Instance.PlayInstance("dropitem");
                    SendLog($"Deleted (cursor): {heldItem.Name} x{Math.Max(1, heldItem.StackCount)}.");

                    // Avoid flashing a tooltip for a now-gone item.
                    ClearHover();
                }

                // We consumed this path (don't let the grid grant items on the same click).
                return;
            }

            // No held item -> Nothing to do here (let normal grid behavior proceed).
        }

        /// <summary>
        /// Favorite handler for the Favorites panel.
        /// When the user left-clicks while holding an item over the Favorites panel,
        /// add that item's ID to the favorites list (no item give/consume).
        /// </summary>
        public static void HandleFavoriteDrop(InventoryItem heldItem)
        {
            if (heldItem == null) return;

            // Left click inside the favorites drop zone -> favorite the held item.
            if (Input.LeftClicked)
            {
                var heldItemID = heldItem.ItemClass.ID;

                // Add to favorites (ignores duplicates); does not spawn or remove the item.
                if (AddFavorite(heldItemID))
                {
                    if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Award");
                    SendLog($"Favorited '{GetItemName(heldItemID)}'.");

                    // Avoid immediate tooltip flicker after state change.
                    ClearHover();
                }
            }
        }
        #endregion

        #region Snapshots (Save/Load/Clear)

        public class ItemData
        {
            public InventoryItemIDs Id;
            public int   Stack;
            public float Health;
            public int?  Clip;
        }

        public class InvSnapshot
        {
            public ItemData[,] Trays = new ItemData[2, 8];
            public ItemData[]  Bag;
        }

        private static readonly Dictionary<int, InvSnapshot> _slots = new Dictionary<int, InvSnapshot>();

        // Expose to serializer in a safe way.
        internal static IReadOnlyDictionary<int, InvSnapshot> ExportAllSlots()
            => new Dictionary<int, InvSnapshot>(_slots);

        internal static void ImportAllSlots(Dictionary<int, InvSnapshot> src)
        {
            _slots.Clear();
            if (src != null) foreach (var kv in src) _slots[kv.Key] = kv.Value;
        }

        private static ItemData Capture(InventoryItem it)
        {
            if (it == null) return null;
            var d = new ItemData
            {
                Id     = it.ItemClass.ID,
                Stack  = it.StackCount,
                Health = it.ItemHealthLevel
            };
            if (it is GunInventoryItem gi) d.Clip = gi.RoundsInClip;
            return d;
        }

        private static InventoryItem Rebuild(ItemData d)
        {
            if (d == null) return null;

            var it = CreateClampedItem(d.Id, d.Stack, d.Health) as InventoryItem;
            if (it is GunInventoryItem gi && d.Clip.HasValue)
            {
                int clipMax = Math.Max(0, gi.GunClass?.ClipCapacity ?? gi.RoundsInClip);
                gi.RoundsInClip = Math.Max(0, Math.Min(clipMax, d.Clip.Value));
            }
            return it;
        }

        public static void SaveSlot(int i)
        {
            if (!IsInGame() || Inv == null) return;

            var snap = new InvSnapshot
            {
                Bag = Inv.Inventory != null ? new ItemData[Inv.Inventory.Length] : Array.Empty<ItemData>()
            };

            // Trays.
            for (int t = 0; t < 2; t++)
                for (int s = 0; s < 8; s++)
                    snap.Trays[t, s] = Capture(GetTrayItem(t, s));

            // Bag.
            if (Inv.Inventory != null)
                for (int b = 0; b < Inv.Inventory.Length; b++)
                    snap.Bag[b] = Capture(GetBackpackItem(b));

            _slots[i] = snap;
            _slotNames[i] = "snapshot"; // (comes from TMIState; UI-friendly label).

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            SendLog($"Saved slot {i + 1}.");
            TMIState.SaveSlots(); // Persist snapshot set.
        }

        public static void LoadSlot(int i)
        {
            if (!IsInGame()) return;

            if (!_slots.TryGetValue(i, out var snap) || Inv == null)
            {
                SendLog($"Slot {i + 1} empty.");
                return;
            }

            // Trays.
            for (int t = 0; t < 2; t++)
                for (int s = 0; s < 8; s++)
                    SetTrayItem(t, s, Rebuild(snap.Trays[t, s]));

            // Bag.
            if (Inv.Inventory != null)
            {
                int n = Math.Min(Inv.Inventory.Length, snap.Bag?.Length ?? 0);
                for (int b = 0; b < n; b++)
                    SetBackpackItem(b, Rebuild(snap.Bag[b]));
                for (int b = n; b < Inv.Inventory.Length; b++)
                    SetBackpackItem(b, null);
            }

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            UploadInventoryToServer();
            SendLog($"Loaded slot {i + 1}.");
        }

        public static void ClearSlot(int i)
        {
            if (!IsInGame()) return;

            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Click");
            _slots.Remove(i);
            _slotNames[i] = null;
            SendLog($"Cleared slot {i + 1}.");
            TMIState.SaveSlots();
        }
        #endregion

        #region Hard Block Tuning (Dynamic Hardness-5 Group)

        // Blocks we want to treat as "hard blocks" even if their vanilla hardness != 5.
        private static readonly BlockTypeEnum[] _specialHardBlocks =
        {
            BlockTypeEnum.DeepLava,
            BlockTypeEnum.BombBlock,
            BlockTypeEnum.TurretBlock,

            // Add more here if you like:
            // BlockTypeEnum.SomeSpawner,
            // BlockTypeEnum.SomeOtherSpecial
        };

        /// <summary>
        /// Default data for blocks that were hardness 5 at startup so we can restore them.
        /// </summary>
        public struct HardBlockDefaults
        {
            public int  Hardness;
            public bool CanBeDug;
            public bool CanBeTouched;
        }

        /// <summary> True once we've scanned the BlockType table. </summary>
        private static bool _hardBlockTableInitialized;

        /// <summary>
        /// All blocks that had Hardness >= 5 at initialization, with their defaults.
        /// Key: BlockTypeEnum
        /// </summary>
        public static readonly Dictionary<BlockTypeEnum, HardBlockDefaults> _hardBlockDefaults =
            new Dictionary<BlockTypeEnum, HardBlockDefaults>();

        /// <summary>
        /// Ensure the hard-block table is built (only once).
        /// We snapshot any block whose default Hardness >= 5.
        /// If you want strictly "== 5", change the comparison accordingly.
        /// </summary>
        private static void EnsureHardBlockTable()
        {
            if (_hardBlockTableInitialized)
                return;

            _hardBlockTableInitialized = true;

            // 1) Dynamic group: All blocks whose vanilla hardness == 5.
            foreach (BlockTypeEnum b in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                var bt = BlockType.GetType(b);
                if (bt == null)
                    continue;

                // Any "very hard" block.
                if (bt.Hardness >= 5) // Change to == 5 if you want exact level 5 only.
                {
                    _hardBlockDefaults[b] = new HardBlockDefaults
                    {
                        Hardness     = bt.Hardness,
                        CanBeDug     = bt.CanBeDug,
                        CanBeTouched = bt.CanBeTouched
                    };
                }
            }

            // 2) Special-case group: BombBlock, TurretBlock, etc.,
            //    even if their hardness is not 5.
            foreach (var b in _specialHardBlocks)
            {
                var bt = BlockType.GetType(b);
                if (bt == null)
                    continue;

                // If not already in the table, snapshot its defaults now.
                if (!_hardBlockDefaults.ContainsKey(b))
                {
                    _hardBlockDefaults[b] = new HardBlockDefaults
                    {
                        Hardness     = bt.Hardness,
                        CanBeDug     = bt.CanBeDug,
                        CanBeTouched = bt.CanBeTouched
                    };
                }
            }

            Log($"Hard-block table initialized with {_hardBlockDefaults.Count} entries (Hardness>=5).");
        }

        /// <summary>
        /// Exposed helper for patches: Does this BlockType belong to the
        /// "very hard" group (Hardness>=5 at vanilla startup)?
        /// </summary>
        public static bool IsVeryHardBlock(BlockTypeEnum blockType)
        {
            EnsureHardBlockTable();
            return _hardBlockDefaults.ContainsKey(blockType);
        }

        /// <summary>
        /// Applies the hard-block settings to all hardness-5 blocks.
        /// Called once at content load and whenever the UI settings change.
        /// </summary>
        public static void ApplyHardBlockSettings()
        {
            EnsureHardBlockTable();

            bool enabled  = TMIState.GetHardBlocksEnabled(defaultIfMissing: false);

            foreach (var kv in _hardBlockDefaults)
            {
                BlockTypeEnum blockEnum = kv.Key;
                HardBlockDefaults def   = kv.Value;

                var bt = BlockType.GetType(blockEnum);
                if (bt == null)
                    continue;

                if (!enabled)
                {
                    // Restore vanilla behavior.
                    bt.Hardness     = def.Hardness;
                    bt.CanBeDug     = def.CanBeDug;
                    bt.CanBeTouched = def.CanBeTouched;
                }
                else
                {
                    // Make very hard blocks mineable and touchable.
                    bt.CanBeDug     = true;
                    bt.CanBeTouched = bt.CanBeTouched || def.CanBeTouched;
                }
            }
        }
        #endregion
    }
}
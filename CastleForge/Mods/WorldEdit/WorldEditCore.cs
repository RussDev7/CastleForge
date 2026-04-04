/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

/*
This class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp
*/

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Forms;
using System.Threading;
using System.Numerics;
using System.Linq;
using System.Text;
using System.IO;
using System;

using static WorldEdit.WorldEditCore.WorldUtils;
using static ModLoader.LogSystem;

using Vector3 = Microsoft.Xna.Framework.Vector3; // For CMZ.

namespace WorldEdit
{
    public class WorldEditCore
    {
        /// <summary>
        ///
        /// Welcome to the World Edit C# Class!
        ///
        /// This project is almost ready to go right out of the box!
        /// The following require your attention:
        ///
        /// Class Definitions:
        ///
        /// 'WorldHeights'
        /// 'BlockIDValues'
        /// 'ChunkSize'
        /// 'AirID'
        /// 'LogID'
        /// 'LeavesID'
        /// 'WandItemID'
        ///
        /// Networking Definitions:
        ///
        /// 'IsNetworkSessionActive'
        ///
        /// World Utilities:
        ///
        /// 'GetUsersCursorLocation'
        /// 'GetUsersLocation'
        /// 'GetUsersHeldItem'
        /// 'GetBlockFromLocation'
        /// 'PlaceBlock'
        /// 'DropItem'
        /// 'TeleportUser'
        ///
        /// </summary>

        // You need to implement 'WorldHeights', 'BlockIDValues', 'ChunkSize', 'WandItemID', 'NavWandItemID', 'LeavesID', 'LogID', 'SurfaceLavaID', 'DeepLavaID',
        // and 'AirID' support manually!
        #region Class Definitions

        /// <summary>
        ///
        /// This is a placeholder variable for the WorldEditCUI addon.
        /// If you do not plan on using this addon, this can be removed.
        ///
        /// </summary>
        public static bool _enableCLU = false;

        /// <summary>
        ///
        /// You need to implement these values for your project!
        ///
        /// </summary>
        public static (int MinY,   int MaxY)    WorldHeights  = (-64, 64);
        public static (int MinID,  int MaxID)   BlockIDValues = (0, 94);
        public static (int WidthX, int LengthZ) ChunkSize     = (16, 16);
        public static int WandItemID    = (int)DNA.CastleMinerZ.Inventory.InventoryItemIDs.CopperAxe; // ID: 35.
        public static int NavWandItemID = (int)DNA.CastleMinerZ.Inventory.InventoryItemIDs.Compass;   // ID: 39.
        public static int LeavesID      = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Leaves;         // ID: 18.
        public static int LogID         = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Log;            // ID: 17.
        public static int SurfaceLavaID = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.SurfaceLava;    // ID: 12.
        public static int DeepLavaID    = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.DeepLava;       // ID: 13.
        public static int AirID         = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Empty;          // ID: 0.

        #region Definitions

        // Define the main point1 and point2 location vectors. This is used to track the users initial set positions.
        public static Vector3 _pointToLocation1, _pointToLocation2;

        // The offset from the copied region's minimum corner to the player's position at copy time.
        // When pasting, this ensures the same relative spot in the clipboard lines up with the player.
        public static Vector3 CopyAnchorOffset;

        // Undo capture toggle.
        // - true  = Record undo/redo snapshots (normal editing behavior).
        // - false = Skip recording (useful for huge deletes/terrain ops; edits still happen).
        // Runtime-only (not persisted). Resets to default on relaunch.
        public static bool _undoRecordingEnabled = true;

        // Define the main hashsets and their stacks. // Hashsets increase speed and removes unnecessary duplicates.
        // The undo and redo stacks use a third integer that's used as a guid to prevent removing duplicate regions.
        public static Stack<HashSet<Tuple<Vector3, int, int>>> UndoStack           = new Stack<HashSet<Tuple<Vector3, int, int>>>();
        public static Stack<HashSet<Tuple<Vector3, int, int>>> RedoStack           = new Stack<HashSet<Tuple<Vector3, int, int>>>();
        public static Stack<HashSet<Tuple<DNA.IntVector3, byte[]>>> UndoCrateStack = new Stack<HashSet<Tuple<DNA.IntVector3, byte[]>>>();
        public static Stack<HashSet<Tuple<DNA.IntVector3, byte[]>>> RedoCrateStack = new Stack<HashSet<Tuple<DNA.IntVector3, byte[]>>>();
        public static HashSet<Tuple<Vector3, int>> copiedRegion      = new HashSet<Tuple<Vector3, int>>();
        public static HashSet<Tuple<Vector3, int>> copiedStackRegion = new HashSet<Tuple<Vector3, int>>();
        public static HashSet<Tuple<Vector3, int>> copiedChunk       = new HashSet<Tuple<Vector3, int>>();
        public static List<ClipboardCrate> copiedCrates              = new List<ClipboardCrate>();

        /// <summary>
        ///
        /// Maximum normaliMob Levenshtein distance (0.0-1.0) allowed when doing a fuzzy enum match.
        ///
        /// A lower value means only very close matches will succeed; a higher value allows
        /// more "fuzziness." Default is 0.4 (i.e. 40% different).
        ///
        /// You can tweak this in code to make your mapping more or less permissive.
        ///
        /// </summary>
        private static readonly double _maxDistanceThreshold = 0.4;

        public class Region
        {
            // Save vectors including absolute positions.
            public Vector3 AbsolutePosition1 { get; set; }
            public Vector3 AbsolutePosition2 { get; set; }
            public Vector3 Position1 { get; set; }
            public Vector3 Position2 { get; set; }

            // Property to get the center of the region.
            public Vector3 Center => (Position1 + Position2) / 2;

            // Constructor to initialize the region with two vectors.
            public Region(Vector3 corner1, Vector3 corner2)
            {
                // Set the absolute positions.
                AbsolutePosition1 = corner1;
                AbsolutePosition2 = corner2;

                // Set the values based on the min and max.
                Position1 = Vector3.Min(corner1, corner2);
                Position2 = Vector3.Max(corner1, corner2);
            }
        }
        #endregion

        #endregion

        // You need to implement 'IsNetworkSessionActive' support manually!
        #region Networking Definitions

        /// <summary>
        ///
        /// You need to implement these values for your project!
        ///
        /// </summary>

        // Implement a feature to check whether the current game session is active or not.
        public static bool IsNetworkSessionActive() => (DNA.CastleMinerZ.CastleMinerZGame.Instance.CurrentNetworkSession != null);
        ///

        #endregion

        // You need to implement 'IsGameWindowActive', 'IsValidCursorLocation', 'IsCraftingMenuOpen', 'IsChatOpen', 'GetUsersCursorLocation', 'GetUsersLocation',
        // 'GetUsersHeldItem', 'GetBlockFromLocation', 'PlaceBlock', 'DropItem', 'TeleportUser', 'UserHasItem', and 'GiveUserItem' support manually!
        #region Class: World Utilities

        public class WorldUtils
        {
            /// <summary>
            ///
            /// You need to implement these functions for your project!
            ///
            /// </summary>

            // Implement a feature to check whether the current game window is active or not.
            public static bool IsGameWindowActive() => DNA.CastleMinerZ.CastleMinerZGame.Instance?.IsActive ?? false;

            // Implement a feature to validate the cursor location.
            public static bool IsValidCursorLocation() => DNA.CastleMinerZ.UI.InGameHUD.Instance?.ConstructionProbe.AbleToBuild ?? false;

            // Implement a feature to check if the in-game menu/screen is open.
            public static bool IsInGameMenuOpen() => DNA.CastleMinerZ.CastleMinerZGame.Instance?.GameScreen?._uiGroup?.CurrentScreen is DNA.CastleMinerZ.UI.InGameMenu;

            // Implement a feature to check if the crafting menu/screen is open.
            public static bool IsCraftingMenuOpen() => DNA.CastleMinerZ.CastleMinerZGame.Instance?.GameScreen?._uiGroup?.CurrentScreen is DNA.CastleMinerZ.UI.CraftingScreen;

            // Implement a feature to check if the chat console is open.
            public static bool IsChatOpen() => DNA.CastleMinerZ.CastleMinerZGame.Instance?.GameScreen?._uiGroup?.CurrentScreen is DNA.CastleMinerZ.UI.PlainChatInputScreen; // .IsChatting can throw.

            // Implement a feature to gather the games cursor location. This is the in reach block location the user can interact with.
            public static Vector3 GetUsersCursorLocation() => DNA.CastleMinerZ.UI.InGameHUD.Instance?.ConstructionProbe._worldIndex ?? DNA.IntVector3.Zero;

            // Implement a feature to get the users location.
            public static Vector3 GetUsersLocation() => new Vector3((int)Math.Floor(DNA.CastleMinerZ.CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.X ?? 0f),
                                                                    (int)Math.Floor(DNA.CastleMinerZ.CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.Y ?? 0f),
                                                                    (int)Math.Floor(DNA.CastleMinerZ.CastleMinerZGame.Instance?.LocalPlayer?.LocalPosition.Z ?? 0f));

            // Implement a feature to get the currently held item from the user.
            /// <summary>
            /// Returns the currently held/active inventory item ID for the local player.
            /// Summary: Returns -1 when the player/inventory/active item is not available (loading, menus, empty slot, etc.).
            /// </summary>
            public static int GetUsersHeldItem()
            {
                try
                {
                    var id = DNA.CastleMinerZ.CastleMinerZGame.Instance?
                        .LocalPlayer?
                        .PlayerInventory?
                        .ActiveInventoryItem?
                        .ItemClass?
                        .ID;

                    return id.HasValue ? (int)id.Value : -1;
                }
                catch
                {
                    // Summary: Never allow held-item queries to crash input features.
                    return -1;
                }
            }

            // Implement a feature to gather a block ID from a specified vector3 (XYZ) location.
            public static int GetBlockFromLocation(Vector3 location) => (int)DNA.CastleMinerZ.UI.InGameHUD.GetBlock(new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z));
            public static DNA.CastleMinerZ.Terrain.BlockTypeEnum GetBlockTypeFromLocation(Vector3 location) => DNA.CastleMinerZ.UI.InGameHUD.GetBlock(new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z));

            // Implement a feature for placing a block ID at a specified vector3 (XYZ) location.
            public static void PlaceBlock(Vector3 location, int block) => DNA.CastleMinerZ.Net.AlterBlockMessage.Send(
                (DNA.Net.GamerServices.LocalNetworkGamer)DNA.CastleMinerZ.CastleMinerZGame.Instance.LocalPlayer.Gamer,
                new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z),
                (DNA.CastleMinerZ.Terrain.BlockTypeEnum)block
            );

            // Implement a feature for dropping an item ID at a specified vector3 (XYZ) location.
            public static void DropItem(Vector3 location, int item) => DNA.CastleMinerZ.PickupManager.Instance.CreatePickup(
                DNA.CastleMinerZ.Inventory.InventoryItem.CreateItem((DNA.CastleMinerZ.Inventory.InventoryItemIDs)item, 1),
                location,
                true,
                false
            );
            public static void DropParentItem(Vector3 location, DNA.CastleMinerZ.Terrain.BlockTypeEnum item)
            {
                var parentBlockType  = DNA.CastleMinerZ.Terrain.BlockType.GetType(item).ParentBlockType;
                var parentBlockClass = DNA.CastleMinerZ.Inventory.BlockInventoryItemClass.BlockClasses[parentBlockType];

                var type             = typeof(DNA.CastleMinerZ.Inventory.PickInventoryItem);
                var mi               = type.GetMethod("GetOutputFromBlock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var dummy            = (DNA.CastleMinerZ.Inventory.PickInventoryItem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);

                var outputId         = (DNA.CastleMinerZ.Inventory.InventoryItemIDs)mi.Invoke(dummy, new object[] { parentBlockType });
                var outputClass      = DNA.CastleMinerZ.Inventory.InventoryItem.GetClass(outputId);

                // Handle loot boxes.
                if (GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.LootBlock ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.LuckyLootBlock)
                {
                    DNA.CastleMinerZ.Inventory.Explosive.FindBlocksToRemove((DNA.IntVector3)location, DNA.CastleMinerZ.Inventory.ExplosiveTypes.C4, false);
                    return;
                }

                // Handle torches.
                if (GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.Torch     ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchNEGX ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchNEGY ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchNEGZ ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchPOSX ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchPOSY ||
                    GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.TorchPOSZ)
                {
                    DNA.CastleMinerZ.PickupManager.Instance.CreatePickup(
                        DNA.CastleMinerZ.Inventory.InventoryItem.CreateItem(DNA.CastleMinerZ.Inventory.InventoryItemIDs.Torch, 1),
                        location,
                        true,
                        false
                    );
                    return;
                }

                // Handle grass.
                if (GetBlock(location) == DNA.CastleMinerZ.Terrain.BlockTypeEnum.Grass)
                {
                    var dirtClass = DNA.CastleMinerZ.Inventory.BlockInventoryItemClass.BlockClasses[DNA.CastleMinerZ.Terrain.BlockTypeEnum.Dirt];
                    DNA.CastleMinerZ.PickupManager.Instance.CreatePickup(
                        dirtClass.CreateItem(1),
                        location,
                        true,
                        false
                    );
                    return;
                }

                // Handle crates.
                if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(location)))
                {
                    DNA.CastleMinerZ.Net.DestroyCrateMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer, (DNA.IntVector3)location);
                    if (DNA.CastleMinerZ.CastleMinerZGame.Instance.CurrentWorld.Crates.TryGetValue((DNA.IntVector3)location, out DNA.CastleMinerZ.Inventory.Crate crate))
                    {
                        crate.EjectContents();
                        DNA.CastleMinerZ.CastleMinerZGame.Instance?.CurrentWorld.Crates.Remove((DNA.IntVector3)location);
                        // Don't return, allow the next function to drop the crate.
                    }
                }

                // Handle everything else.
                DNA.CastleMinerZ.PickupManager.Instance.CreatePickup(
                    outputId == 0 ? parentBlockClass.CreateItem(1) : outputClass.CreateItem(1),
                    location,
                    true,
                    false
                );

                // Get block helper.
                DNA.CastleMinerZ.Terrain.BlockTypeEnum GetBlock(Vector3 blockLocation) => DNA.CastleMinerZ.UI.InGameHUD.GetBlock((DNA.IntVector3)blockLocation);
            }

            // Implement a feature for teleporting the user to a specified vector3 (XYZ) location.
            public static void TeleportUser(Vector3 location, bool spawnOnTop)
            {
                if (spawnOnTop)
                    DNA.CastleMinerZ.CastleMinerZGame.Instance.GameScreen.TeleportToLocation(
                        new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z),
                        spawnOnTop
                    );
                else
                    DNA.CastleMinerZ.CastleMinerZGame.Instance.LocalPlayer.LocalPosition = location;
            }

            // Implement a feature for checking if the user already has a specified item.
            public static bool UserHasItem(DNA.CastleMinerZ.Inventory.InventoryItemIDs itemType)
            {
                var inv = DNA.CastleMinerZ.CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory;
                var trays = inv.TrayManager?.Trays;
                var bag = inv.Inventory;

                // Check trays.
                if (trays != null)
                {
                    int trayMax = trays.GetUpperBound(0);
                    int slotMax = trays.GetUpperBound(1);

                    for (int t = 0; t <= trayMax; t++)
                    {
                        for (int s = 0; s <= slotMax; s++)
                        {
                            var it = trays[t, s];
                            if (it?.ItemClass != null && it.ItemClass.ID == itemType)
                                return true;
                        }
                    }
                }

                // Check bag.
                if (bag != null)
                {
                    for (int i = 0; i < bag.Length; i++)
                    {
                        var it = bag[i];
                        if (it?.ItemClass != null && it.ItemClass.ID == itemType)
                            return true;
                    }
                }

                // No item found.
                return false;
            }

            // Implement a feature for giving the user a specified item & amount.
            public static void GiveUserItem(DNA.CastleMinerZ.Inventory.InventoryItemIDs itemType, int amount = 1)
            {
                DNA.CastleMinerZ.CastleMinerZGame.Instance.LocalPlayer.PlayerInventory.AddInventoryItem(
                    DNA.CastleMinerZ.Inventory.InventoryItem.CreateItem(itemType, amount)
                );
            }
            ///

            #region Direction Helpers

            public enum Direction
            {
                posX,
                negX,
                posZ,
                negZ,
                Up,
                Down
            }

            public static Direction GetFacingDirection(Vector3 regionLocation, Vector3 cursorLocation)
            {
                // Calculate absolute differences for X, Y, and Z.
                float deltaX = Math.Abs(regionLocation.X - cursorLocation.X);
                float deltaY = Math.Abs(regionLocation.Y - cursorLocation.Y);
                float deltaZ = Math.Abs(regionLocation.Z - cursorLocation.Z);

                // Check if cursorLocation is directly above or below regionLocation.
                if (deltaX < float.Epsilon && deltaZ < float.Epsilon)
                {
                    return cursorLocation.Y > regionLocation.Y ? Direction.Up : Direction.Down;
                }

                // Get the direction based on the largest absolute difference.
                if (deltaY > deltaX && deltaY > deltaZ)
                {
                    return cursorLocation.Y > regionLocation.Y ? Direction.Up : Direction.Down;
                }
                else if (deltaX > deltaZ)
                {
                    return regionLocation.X > cursorLocation.X ? Direction.negX : Direction.posX;
                }
                else
                {
                    return regionLocation.Z > cursorLocation.Z ? Direction.negZ : Direction.posZ;
                }
            }

            // Returns a unit offset based on the provided direction.
            public static Vector3 GetDirectionalUnitOffset(Direction direction)
            {
                if (direction == Direction.Up)
                    return new Vector3(0, 1, 0);
                else if (direction == Direction.Down)
                    return new Vector3(0, -1, 0);
                else if (direction == Direction.posX)
                    return new Vector3(1, 0, 0);
                else if (direction == Direction.negX)
                    return new Vector3(-1, 0, 0);
                else if (direction == Direction.posZ)
                    return new Vector3(0, 0, 1);
                else if (direction == Direction.negZ)
                    return new Vector3(0, 0, -1);
                else
                    return Vector3.Zero;
            }

            // Returns a normaliMob unit vector corresponding to the given direction.
            public static Vector3 GetNormaliMobDirectionVector(Direction dir)
            {
                Vector3 vector = Vector3.Zero;
                switch (dir)
                {
                    case Direction.Up:
                        vector = new Vector3(0, 1, 0);
                        break;
                    case Direction.Down:
                        vector = new Vector3(0, -1, 0);
                        break;
                    case Direction.posX:
                        vector = new Vector3(1, 0, 0);
                        break;
                    case Direction.negX:
                        vector = new Vector3(-1, 0, 0);
                        break;
                    case Direction.posZ:
                        vector = new Vector3(0, 0, 1);
                        break;
                    case Direction.negZ:
                        vector = new Vector3(0, 0, -1);
                        break;
                }
                float len = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
                if (len > 0)
                {
                    vector.X /= len;
                    vector.Y /= len;
                    vector.Z /= len;
                }
                return vector;
            }

            // Returns true if the position is inside the spherical radius from the origin.
            public static bool IsWithinSphericalRadius(Vector3 origin, Vector3 pos, int radius)
            {
                if (radius < 0)
                    radius = -radius;

                int dx = (int)(pos.X - origin.X);
                int dy = (int)(pos.Y - origin.Y);
                int dz = (int)(pos.Z - origin.Z);

                return (dx * dx) + (dy * dy) + (dz * dz) <= (radius * radius);
            }

            // Gets the signed distance from the origin along the given direction.
            public static int GetDirectionalDistance(Vector3 origin, Vector3 pos, Direction direction)
            {
                switch (direction)
                {
                    case Direction.Up:
                        return (int)(pos.Y - origin.Y);

                    case Direction.Down:
                        return (int)(origin.Y - pos.Y);

                    case Direction.posX:
                        return (int)(pos.X - origin.X);

                    case Direction.negX:
                        return (int)(origin.X - pos.X);

                    case Direction.posZ:
                        return (int)(pos.Z - origin.Z);

                    case Direction.negZ:
                        return (int)(origin.Z - pos.Z);

                    default:
                        return 0;
                }
            }

            // Returns true if the position is within the allowed depth for the direction.
            private static bool IsWithinDirectionalDepth(Vector3 origin, Vector3 pos, Direction direction, int depth)
            {
                if (depth == int.MaxValue)
                    return GetDirectionalDistance(origin, pos, direction) >= 0;

                int distance = GetDirectionalDistance(origin, pos, direction);
                return distance >= 0 && distance < depth;
            }

            // Gets the four directions perpendicular to the given fill direction.
            public static Vector3[] GetPerpendicularDirections(Direction direction)
            {
                switch (direction)
                {
                    case Direction.Up:
                    case Direction.Down:
                        return new Vector3[]
                        {
                        new Vector3(1, 0, 0),
                        new Vector3(-1, 0, 0),
                        new Vector3(0, 0, 1),
                        new Vector3(0, 0, -1)
                        };

                    case Direction.posX:
                    case Direction.negX:
                        return new Vector3[]
                        {
                        new Vector3(0, 1, 0),
                        new Vector3(0, -1, 0),
                        new Vector3(0, 0, 1),
                        new Vector3(0, 0, -1)
                        };

                    case Direction.posZ:
                    case Direction.negZ:
                        return new Vector3[]
                        {
                        new Vector3(1, 0, 0),
                        new Vector3(-1, 0, 0),
                        new Vector3(0, 1, 0),
                        new Vector3(0, -1, 0)
                        };

                    default:
                        return Array.Empty<Vector3>();
                }
            }
            #endregion

            #region Direction Parsing Helpers

            /// <summary>
            /// Converts a single direction token into a <see cref="Direction"/>.
            /// Supports enum names (posX/negX/posZ/negZ/Up/Down) plus shorthands (x+, x-, z+, z-, u, d, etc.).
            /// </summary>
            private static bool TryParseDirectionToken(string token, out Direction dir)
            {
                dir = default;

                if (string.IsNullOrWhiteSpace(token))
                    return false;

                token = token.Trim();

                // Shorthand aliases (case-insensitive).
                // Keep these conservative (no "north/east" assumptions).
                switch (token.ToLowerInvariant())
                {
                    case "x+":
                    case "+x":
                    case "px":
                    case "posx":
                    case "pos_x":
                    case "xplus":
                        dir = Direction.posX;
                        return true;

                    case "x-":
                    case "-x":
                    case "nx":
                    case "negx":
                    case "neg_x":
                    case "xminus":
                        dir = Direction.negX;
                        return true;

                    case "z+":
                    case "+z":
                    case "pz":
                    case "posz":
                    case "pos_z":
                    case "zplus":
                        dir = Direction.posZ;
                        return true;

                    case "z-":
                    case "-z":
                    case "nz":
                    case "negz":
                    case "neg_z":
                    case "zminus":
                        dir = Direction.negZ;
                        return true;

                    case "u":
                    case "up":
                    case "+y":
                    case "y+":
                    case "py":
                    case "posy":
                        dir = Direction.Up;
                        return true;

                    case "d":
                    case "down":
                    case "-y":
                    case "y-":
                    case "ny":
                    case "negy":
                        dir = Direction.Down;
                        return true;
                }

                // Fall back to enum parsing for anything else.
                return Enum.TryParse(token, true, out dir);
            }

            /// <summary>
            /// Parses a single direction token using the same aliases accepted by region commands.
            /// Supports: posX/negX/posZ/negZ/up/down and shorthands like x+, x-, z+, z-, u, d.
            /// </summary>
            public static bool TryParseDirection(string token, out Direction dir)
            {
                return TryParseDirectionToken(token, out dir);
            }

            /// <summary>
            /// Parses comma-separated direction lists (e.g. "posx,posz", "x+,z-", "up,down").
            /// If <paramref name="allowAll"/> and input is "all", returns null meaning "use all directions".
            /// </summary>
            public static bool TryParseDirectionList(string text, out HashSet<Direction> result, bool allowAll)
            {
                result = null;

                if (string.IsNullOrWhiteSpace(text))
                    return true;   // Treat empty as "not provided".

                if (allowAll && text.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    result = null; // Null == all directions.
                    return true;
                }

                HashSet<Direction> set = new HashSet<Direction>();
                string[] parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string p in parts)
                {
                    string token = p.Trim();
                    if (token.Length == 0)
                        continue;

                    if (!TryParseDirectionToken(token, out Direction d))
                        return false;

                    set.Add(d);
                }

                result = set.Count > 0 ? set : null;
                return true;
            }
            #endregion

            #region Rotation Parsing Helpers

            /// <summary>
            /// Accepts: 0/90/180/270/360 (360 == 0). Also normalizes negatives (ex: -90 => 270).
            /// </summary>
            public static bool TryParseRotationDegrees(string s, out int degrees)
            {
                degrees = 0;
                if (string.IsNullOrWhiteSpace(s)) return false;

                if (!int.TryParse(s.Trim(), out int raw)) return false;
                if (raw % 90 != 0) return false;

                int norm = ((raw % 360) + 360) % 360; // 0..359
                if (norm == 0 || norm == 90 || norm == 180 || norm == 270)
                {
                    degrees = norm;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Accepts: rot90, rotate180, r270, rot=90, rotate:270, etc.
            /// </summary>
            public static bool TryParseRotationDegreesFromToken(string token, out int degrees)
            {
                degrees = 0;
                if (string.IsNullOrWhiteSpace(token)) return false;

                string t = token.Trim().ToLowerInvariant();
                t = t.TrimStart('-', '/'); // allow "-rot90" or "/rot90" etc.

                // rot=90 / rotate=90 / rot:90 / rotate:90
                int sep = t.IndexOfAny(new[] { '=', ':' });
                if (sep >= 0)
                {
                    string left = t.Substring(0, sep);
                    string right = (sep + 1 < t.Length) ? t.Substring(sep + 1) : string.Empty;

                    if ((left == "rot" || left == "rotate" || left == "r") && TryParseRotationDegrees(right, out degrees))
                        return true;

                    return false;
                }

                // rot90 / rotate180 / r270
                if (t.StartsWith("rotate"))
                    return TryParseRotationDegrees(t.Substring("rotate".Length), out degrees);

                if (t.StartsWith("rot"))
                    return TryParseRotationDegrees(t.Substring("rot".Length), out degrees);

                if (t.StartsWith("r"))
                    return TryParseRotationDegrees(t.Substring("r".Length), out degrees);

                return false;
            }
            #endregion

            #region Utility Helpers

            public static int GetRandomBlockFromPattern(string pattern)
            {
                // Turn the pattern into IDs.
                // Examples that work: "12", "12,34", "log", "log,glass,12".
                int[] ids = EnumMapper.GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(pattern, BlockIDValues);

                // If nothing valid was found, fail fast so caller can show an error.
                if (ids == null || ids.Length == 0)
                    throw new FormatException($"Invalid block pattern: {pattern}.");

                // Reuse the int[] version to actually pick the random ID.
                return GetRandomBlockFromPattern(ids);
            }

            #region BlockPatternRng

            /// <summary>
            /// In-Game Benchmarks (.NET 4.7.2):
            /// --------------------------------------------------
            /// Original (new RNGCSP per call, modulo bias)
            /// Time:    909.52 ms   Throughput: 2,198.95 ops/ms    Uniformity≈ 0.0044   Checksum: 62989811
            /// --------------------------------------------------
            /// GetInt32-style (CSPRNG, zero bias)
            /// Time:    796.34 ms   Throughput: 2,511.48 ops/ms    Uniformity≈ 0.0056   Checksum: 63062655
            /// --------------------------------------------------
            /// Buffered CSPRNG (zero bias)
            /// Time:     45.75 ms   Throughput: 43,711.36 ops/ms   Uniformity≈ 0.0041   Checksum: 62981794
            /// --------------------------------------------------
            /// Fast PRNG (PCG-ish, seeded from CSPRNG)
            /// Time:     50.46 ms   Throughput: 39,635.67 ops/ms   Uniformity≈ 0.0030   Checksum: 62995908
            /// --------------------------------------------------
            /// </summary>
            internal static class BlockPatternRng
            {
                // One CSPRNG for the process; no per-call allocs.
                private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

                // Per-thread 4KB buffer (~1024 draws per refill).
                [ThreadStatic] private static byte[] _buf;
                [ThreadStatic] private static int _ofs;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static uint NextU32()
                {
                    var b = _buf;

                    // If this is the first time on this thread OR we exhausted the buffer,
                    // (re)fill it with fresh random bytes.
                    if (b == null || _ofs >= b.Length)
                    {
                        b = _buf ?? (_buf = new byte[4096]);
                        _rng.GetBytes(b);
                        _ofs = 0;
                    }

                    // Faster than BitConverter on hot path.
                    uint v = (uint)(b[_ofs]
                                 | (b[_ofs + 1] << 8)
                                 | (b[_ofs + 2] << 16)
                                 | (b[_ofs + 3] << 24));
                    _ofs += 4;
                    return v;
                }

                // Uniform [0, n) with zero modulo bias
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int NextIndex(int n)
                {
                    if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
                    uint un = (uint)n;
                    uint limit = (uint.MaxValue / un) * un; // largest multiple < 2^32
                    uint r;
                    do { r = NextU32(); } while (r >= limit);
                    return (int)(r % un);
                }
            }
            #endregion

            public static int GetRandomBlockFromPattern(int[] pattern)
            {
                if (pattern == null || pattern.Length == 0) throw new ArgumentException("pattern");
                if (pattern.Length == 1) return pattern[0];
                return pattern[BlockPatternRng.NextIndex(pattern.Length)];
            }

            // Returns a random block ID in [MinID, MaxID) that is NOT in exclusionList.
            public static int GetRandomBlock(HashSet<int> exclusionList)
            {
                if (exclusionList == null) exclusionList = new HashSet<int>();

                int min = BlockIDValues.MinID;
                int max = BlockIDValues.MaxID;    // INCLUSIVE upper bound.
                long span = (long)max - min + 1L; // Avoid int overflow if bounds are large.
                if (span <= 1) return min;
                if (exclusionList.Count >= span) return min;

                // Try random picks first.
                int tries = 0;
                int maxTries = (int)Math.Min(span * 4L, 10000L);
                while (tries++ < maxTries)
                {
                    int candidate = min + BlockPatternRng.NextIndex((int)span);
                    if (!exclusionList.Contains(candidate))
                        return candidate;
                }

                // Fallback scan.
                for (int v = min; v <= max; v++)
                    if (!exclusionList.Contains(v))
                        return v;

                return min;
            }

            // Returns a random integer in [min, max).
            public static int GenerateRandomNumber(int min, int max)
            {
                if (min > max) return min;
                long span = (long)max - min + 1L;
                if (span <= 1) return min;
                return min + BlockPatternRng.NextIndex((int)span);
            }

            public static HashSet<Vector3> ExtractVector3HashSet(HashSet<Tuple<Vector3, int>> tupleHashSet)
            {
                // Rebuild the hashset removing Item2 (block id) and only select Item1 (Vector3).
                // Avoid using LINQ's overhead to improve the speed.

                HashSet<Vector3> vector3HashSet = new HashSet<Vector3>();

                foreach (var tuple in tupleHashSet)
                {
                    vector3HashSet.Add(tuple.Item1);
                }

                return vector3HashSet;
            }

            public static bool GetBoundingBoxFromRegion(out Vector3 min, out Vector3 max)
            {
                min = default;
                max = default;

                if (copiedRegion == null || copiedRegion.Count == 0)
                    return false;

                min = new Vector3(float.MaxValue);
                max = new Vector3(float.MinValue);

                foreach (var entry in copiedRegion)
                {
                    Vector3 p = entry.Item1;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                return true;
            }

            // Check if the direction is valid.
            public static bool IsValidDirection(string direction)
            {
                return direction.ToLower() == "posx" || direction.ToLower() == "negx" || direction.ToLower() == "posz" || direction.ToLower() == "negz" || direction.ToLower() == "up" || direction.ToLower() == "down";
            }

            // Check if the rotation angle is valid.
            public static bool IsValidRotation(int rotation)
            {
                return rotation == 0 || rotation == -0 || rotation == 90 || rotation == -90 || rotation == 180 || rotation == -180 || rotation == 240 || rotation == -240 || rotation == 360 || rotation == -360;
            }

            // Check if the shape is valid.
            public static bool IsValidBrushShape(string shape)
            {
                return shape.ToLower() == "floor" || shape.ToLower() == "cube" || shape.ToLower() == "prism" || shape.ToLower() == "sphere" || shape.ToLower() == "ring" || shape.ToLower() == "pyramid" || shape.ToLower() == "cone" || shape.ToLower() == "cylinder" || shape.ToLower() == "diamond" || shape.ToLower() == "tree" || shape.ToLower() == "snow"|| shape.ToLower() == "schem" || shape.ToLower() == "floodfill";
            }

            // Check if a location is within the worlds height limit.
            public static bool IsWithinWorldHeight(Vector3 location)
            {
                return location.Y <= WorldHeights.MaxY && location.Y >= WorldHeights.MinY;
            }
            #endregion

            #region Math Helpers

            // Computes the dot product of two vectors.
            public static float DotProduct(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            // Returns the Euclidean length (magnitude) of a vector.
            public static float Vector3Length(Vector3 v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

            // Method to check if a position is within the region defined by start and end points.
            public static bool IsWithinRegion(Vector3 position, Vector3 start, Vector3 end)
            {
                return position.X >= Math.Min(start.X, end.X) && position.X <= Math.Max(start.X, end.X) &&
                       position.Y >= Math.Min(start.Y, end.Y) && position.Y <= Math.Max(start.Y, end.Y) &&
                       position.Z >= Math.Min(start.Z, end.Z) && position.Z <= Math.Max(start.Z, end.Z);
            }

            public static bool IsWithinWorldBounds(Vector3 pos, int additionalHeight = 0, int additionalDepth = 0)
            {
                return pos.Y <= (WorldHeights.MaxY + additionalHeight) && pos.Y >= (WorldHeights.MinY - additionalDepth);
            }

            public static bool IsWithinWorldBounds(int yPos, int additionalHeight = 0, int additionalDepth = 0)
            {
                return yPos <= (WorldHeights.MaxY + additionalHeight) && yPos >= (WorldHeights.MinY - additionalDepth);
            }

            public static Vector3 GetRegionCenter()
            {
                float xSum = 0, ySum = 0, zSum = 0;
                int count = copiedRegion.Count;

                foreach (var item in copiedRegion)
                {
                    xSum += item.Item1.X;
                    ySum += item.Item1.Y;
                    zSum += item.Item1.Z;
                }

                // Calculate the average of all vectors to find the center.
                return new Vector3(xSum / count, ySum / count, zSum / count);
            }

            public static int CalculateBlockCount(Vector3 pos1, Vector3 pos2)
            {
                int xCount = Math.Abs((int)pos2.X - (int)pos1.X) + 1;
                int yCount = Math.Abs((int)pos2.Y - (int)pos1.Y) + 1;
                int zCount = Math.Abs((int)pos2.Z - (int)pos1.Z) + 1;

                return xCount * yCount * zCount;
            }

            public bool AreInSameQuadrant(Vector3 v1, Vector3 v2)
            {
                return (Math.Sign(v1.X) == Math.Sign(v2.X)) &&
                       (Math.Sign(v1.Y) == Math.Sign(v2.Y)) &&
                       (Math.Sign(v1.Z) == Math.Sign(v2.Z));
            }

            public static double LengthSq(double x, double z)
            {
                return x * x + z * z;
            }

            public static double LengthSq(double x, double y, double z)
            {
                return x * x + y * y + z * z;
            }

            public static int Clamp(int value, int min, int max)
            {
                return (value < min) ? min : (value > max) ? max : value;
            }

            // Clamps yMin / yMax so the region stays within WorldHeights.
            public static void ClampToWorldHeight(ref float yMin, ref float yMax)
            {
                yMin = Math.Max(yMin, WorldHeights.MinY);
                yMax = Math.Min(yMax, WorldHeights.MaxY);
            }

            // Parses "x,z" or "x,y,z" into a Vector3 (ints). Returns false if malformed.
            public static bool TryParseXYZ(string s, out Vector3 v)
            {
                int x, z;
                v = default;
                var parts = s.Split(',');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[0], out x) &&
                        int.TryParse(parts[1], out z))
                    {
                        v = new Vector3(x, 0, z); // Y ignored.
                        return true;
                    }
                }
                else if (parts.Length == 3)
                {
                    if (int.TryParse(parts[0], out x) &&
                        int.TryParse(parts[1], out int y) &&
                        int.TryParse(parts[2], out z))
                    {
                        v = new Vector3(x, y, z);
                        return true;
                    }
                }
                return false;
            }

            // Mathematical floor division that works for negatives.
            public static int FloorDiv(int value, int divisor)
            {
                return (value >= 0) ? value / divisor
                                    : -(((-value) + divisor - 1) / divisor);
            }

            public static Vector3 RoundVector(Vector3 v)
            {
                return new Vector3((int)Math.Round(v.X), (int)Math.Round(v.Y), (int)Math.Round(v.Z));
            }
            #endregion

            #region XnaPerlinNoise Helper

            public class XnaPerlinNoise
            {
                public float ComputeNoise(Vector3 position)
                {
                    // Extract the individual components of the position vector.
                    float posX = position.X;
                    float posY = position.Y;
                    float posZ = position.Z;

                    // Calculate integer floor values for each coordinate.
                    int cellX = (posX > 0f) ? (int)posX : (int)posX - 1;
                    int cellY = (posY > 0f) ? (int)posY : (int)posY - 1;
                    int cellZ = (posZ > 0f) ? (int)posZ : (int)posZ - 1;

                    // Wrap the cell coordinates within 255 (used for permutation table lookup).
                    int permX = cellX & 255;
                    int permY = cellY & 255;
                    int permZ = cellZ & 255;

                    // Compute local positions within the grid cell.
                    float localX = posX - (float)cellX;
                    float localY = posY - (float)cellY;
                    float localZ = posZ - (float)cellZ;

                    // Compute fade curves for smooth transitions.
                    float fadeX = localX * localX * localX * (localX * (localX * 6f - 15f) + 10f);
                    float fadeY = localY * localY * localY * (localY * (localY * 6f - 15f) + 10f);
                    float fadeZ = localZ * localZ * localZ * (localZ * (localZ * 6f - 15f) + 10f);

                    // Hash coordinates to get permutation indices.
                    int hashXY = _permute[permX] + permY;
                    int hashXYZ = _permute[hashXY] + permZ;
                    int hashXY1Z = _permute[hashXY + 1] + permZ;
                    int hashX1Y = _permute[permX + 1] + permY;
                    int hashX1YZ = _permute[hashX1Y] + permZ;
                    int hashX1Y1Z = _permute[hashX1Y + 1] + permZ;

                    // Calculate gradient dot products for each corner of the cell.
                    float gradX1 = localX - 1f;
                    float gradY1 = localY - 1f;
                    float gradZ1 = localZ - 1f;

                    int[] gradientVector = s_gradientVectors[_permute[hashXYZ] & 15];
                    float dot000 = localX * gradientVector[0] + localY * gradientVector[1] + localZ * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashX1YZ] & 15];
                    float dot100 = gradX1 * gradientVector[0] + localY * gradientVector[1] + localZ * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashXY1Z] & 15];
                    float dot010 = localX * gradientVector[0] + gradY1 * gradientVector[1] + localZ * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashX1Y1Z] & 15];
                    float dot110 = gradX1 * gradientVector[0] + gradY1 * gradientVector[1] + localZ * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashXYZ + 1] & 15];
                    float dot001 = localX * gradientVector[0] + localY * gradientVector[1] + gradZ1 * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashX1YZ + 1] & 15];
                    float dot101 = gradX1 * gradientVector[0] + localY * gradientVector[1] + gradZ1 * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashXY1Z + 1] & 15];
                    float dot011 = localX * gradientVector[0] + gradY1 * gradientVector[1] + gradZ1 * gradientVector[2];

                    gradientVector = s_gradientVectors[_permute[hashX1Y1Z + 1] & 15];
                    float dot111 = gradX1 * gradientVector[0] + gradY1 * gradientVector[1] + gradZ1 * gradientVector[2];

                    // Interpolate between gradient values.
                    float interpX0 = dot000 + fadeX * (dot100 - dot000);
                    float interpX1 = dot010 + fadeX * (dot110 - dot010);
                    float interpX2 = dot001 + fadeX * (dot101 - dot001);
                    float interpX3 = dot011 + fadeX * (dot111 - dot011);

                    // Interpolate the corner values.
                    float interpY0 = interpX0 + fadeY * (interpX1 - interpX0);
                    float interpY1 = interpX2 + fadeY * (interpX3 - interpX2);

                    // Final interpolation.
                    return interpY0 + fadeZ * (interpY1 - interpY0);
                }

                // Initialize permutation table.
                private void Initalize(Random r)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        _permute[256 + i] = (_permute[i] = r.Next(256));
                    }
                    for (int j = 0; j < 512; j++)
                    {
                        _permute[512 + j] = _permute[j];
                    }
                }

                // Constructors.
                public XnaPerlinNoise()
                {
                    this.Initalize(new Random());
                }

                public XnaPerlinNoise(Random r)
                {
                    this.Initalize(r);
                }

                // Gradient vectors for the noise function.
                private static readonly int[][] s_gradientVectors = new int[][]
                {
                new int[] { 0, -1, -1, -1 },
                new int[] { -1, 0, -1, -1 },
                new int[] { 1, 0, -1, -1 },
                new int[] { 0, 1, -1, -1 },
                new int[] { -1, -1, 0, -1 },
                new int[] { 1, -1, 0, -1 },
                new int[] { -1, 1, 0, -1 },
                new int[] { 1, 1, 0, -1 },
                new int[] { 0, -1, 1, -1 },
                new int[] { -1, 0, 1, -1 },
                new int[] { 1, 0, 1, -1 },
                new int[] { 0, 1, 1, -1 },
                new int[] { 1, 1, 0, 1 },
                new int[] { -1, 1, 0, 1 },
                new int[] { 0, -1, -1, 1 },
                new int[] { 0, -1, 1, 1 },
                new int[] { -1, -1, -1, 0 },
                new int[] { 1, -1, -1, 0 },
                new int[] { -1, 1, -1, 0 },
                new int[] { 1, 1, -1, 0 },
                new int[] { -1, -1, 1, 0 },
                new int[] { 1, -1, 1, 0 },
                new int[] { -1, 1, 1, 0 },
                new int[] { 1, 1, 1, 0 },
                new int[] { -1, 0, -1, 1 },
                new int[] { 1, 0, -1, 1 },
                new int[] { 0, 1, -1, 1 },
                new int[] { -1, -1, 0, 1 },
                new int[] { 1, -1, 0, 1 },
                new int[] { -1, 0, 1, 1 },
                new int[] { 1, 0, 1, 1 },
                new int[] { 0, 1, 1, 1 }
                };

                // Permutation table for the noise function.
                private static readonly int[] _permute = new int[1024];
            }
            #endregion

            #region Fill / Drain Helpers

            // Returns true if the position is a valid air block for fill traversal.
            public static bool CanVisitFillPosition(Vector3 origin, Vector3 pos, int radius, int depth, Direction direction)
            {
                if (!IsWithinWorldHeight(pos))
                    return false;

                if (!IsWithinSphericalRadius(origin, pos, radius))
                    return false;

                if (!IsWithinDirectionalDepth(origin, pos, direction, depth))
                    return false;

                return GetBlockFromLocation(pos) == AirID;
            }

            // Returns true if the block ID is a liquid block.
            public static bool IsLiquidBlock(int blockID)
            {
                return blockID == SurfaceLavaID ||
                       blockID == DeepLavaID;
            }
            #endregion

            #region Command Parsing Helpers

            /// <summary>
            /// Parses "on/off/toggle/true/false/1/0". If args empty/unrecognized, returns !current.
            /// </summary>
            public static bool ResolveToggle(string[] args, bool current)
            {
                if (args == null || args.Length == 0) return !current;

                var s = (args[0] ?? "").Trim().ToLowerInvariant();
                if (s == "toggle") return !current;
                if (s == "on" || s == "true" || s == "1") return true;
                if (s == "off" || s == "false" || s == "0") return false;

                // Unknown arg => toggle.
                return !current;
            }

            /// <summary>
            /// Returns true when a config token should be treated as disabled.
            /// Summary: Used by /navwand to interpret "none/off/disabled" (or empty) as an off state.
            /// </summary>
            public static bool IsDisabledToken(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return true;

                string v = value.Trim();
                return v.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                       v.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                       v.Equals("disabled", StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Returns true when an argument looks like a toggle token ("on/off/toggle/true/false/1/0").
            /// Summary: Allows /navwand to distinguish between state toggles and item names.
            /// </summary>
            public static bool IsToggleToken(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return true;

                string v = value.Trim().ToLowerInvariant();
                return v == "toggle" ||
                       v == "on" || v == "true" || v == "1" ||
                       v == "off" || v == "false" || v == "0";
            }

            /// <summary>
            /// Attempts to normalize a user token into a valid <see cref="InventoryItemIDs"/> enum name.
            /// Summary: Supports numeric IDs ("39"), enum names ("Compass"), or fully-qualified names ("InventoryItemIDs.Compass").
            /// </summary>
            /// <param name="value">User supplied token.</param>
            /// <param name="normalizedName">Normalized enum name (e.g. "Compass").</param>
            public static bool TryNormalizeInventoryItemToken(string value, out string normalizedName)
            {
                normalizedName = null;

                if (string.IsNullOrWhiteSpace(value))
                    return false;

                string cleaned = value.Trim();
                cleaned = StripPrefixIgnoreCase(cleaned, "DNA.CastleMinerZ.Inventory.InventoryItemIDs.");
                cleaned = StripPrefixIgnoreCase(cleaned, "InventoryItemIDs.");

                // Numeric support.
                if (int.TryParse(cleaned, out int numeric))
                {
                    var id = (DNA.CastleMinerZ.Inventory.InventoryItemIDs)numeric;
                    if (Enum.IsDefined(typeof(DNA.CastleMinerZ.Inventory.InventoryItemIDs), id))
                    {
                        normalizedName = id.ToString();
                        return true;
                    }

                    return false;
                }

                // Enum name support (case-insensitive).
                if (Enum.TryParse(cleaned, ignoreCase: true, out DNA.CastleMinerZ.Inventory.InventoryItemIDs parsed) &&
                    Enum.IsDefined(typeof(DNA.CastleMinerZ.Inventory.InventoryItemIDs), parsed))
                {
                    normalizedName = parsed.ToString();
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Removes the given prefix from <paramref name="value"/> if present (case-insensitive).
            /// Summary: Lets commands accept fully-qualified enum names without requiring .NET's newer string.Replace overloads.
            /// </summary>
            private static string StripPrefixIgnoreCase(string value, string prefix)
            {
                if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefix))
                    return value;

                return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? value.Substring(prefix.Length)
                    : value;
            }
            #endregion

            #region Containers (Crate) Core Helpers

            /// <summary>
            /// "Crate contents" are stored outside the terrain grid (WorldInfo.Crates), so any world-edit style
            /// copy/paste/move/stack MUST also handle these sidecar entries or you'll get:
            ///   - Ghost inventories (crate removed but data remains).
            ///   - Empty crates (crate pasted but data wasn't restored).
            ///
            /// Notes:
            ///   - We never *create* crates during capture; we only persist ones that already exist in WorldInfo.Crates.
            ///   - Payload format is slot-based: [bool hasItem][InventoryItem bytes...] repeated for 32 slots.
            ///   - Network sync:
            ///       * DestroyCrateMessage = Tell clients "crate at X,Y,Z is gone".
            ///       * ItemCrateMessage    = Push a slot update to clients.
            ///   - "RelativePos" in ClipboardCrate is always relative to the MIN corner of the capture bounds.
            /// </summary>

            // CMZ crates have 32 slots.
            private const int WE_CRATE_SLOT_COUNT = 32;

            #region Types & Comparers (Clipboard + Undo/Redo Snapshots)

            // Clipboard sidecar for container contents (currently crates).
            // - Positions are in the same origin-based coordinate space as copiedRegion (min corner normalized to 0,0,0).
            // - Payload is a compact binary blob that mirrors ItemCrateMessage semantics (32 slots):
            //     [bool hasItem][item bytes...]
            public sealed class ClipboardCrate
            {
                public Vector3 RelativePos;
                public byte[] Payload;

                public ClipboardCrate(Vector3 relativePos, byte[] payload)
                {
                    RelativePos = relativePos;
                    Payload = payload;
                }
            }

            /// <summary>
            /// Equality comparer for crate snapshots that keys ONLY on position.
            /// This lets a HashSet store (pos, payload) pairs while treating duplicates by world position
            /// (payload changes don't create "new" entries for the same crate location).
            /// </summary>
            private sealed class CrateSnapPosComparer : IEqualityComparer<Tuple<DNA.IntVector3, byte[]>>
            {
                public bool Equals(Tuple<DNA.IntVector3, byte[]> x, Tuple<DNA.IntVector3, byte[]> y)
                {
                    if (ReferenceEquals(x, y)) return true;
                    if (x == null || y == null) return false;
                    return x.Item1.Equals(y.Item1); // Position only.
                }

                public int GetHashCode(Tuple<DNA.IntVector3, byte[]> obj)
                {
                    return (obj == null) ? 0 : obj.Item1.GetHashCode();
                }
            }

            /// <summary>
            /// Shared comparer instance for crate snapshot HashSets (position-only equality).
            /// </summary>
            public static readonly IEqualityComparer<Tuple<DNA.IntVector3, byte[]>> WE_CrateSnapComparer = new CrateSnapPosComparer();

            #endregion

            #region Payload Serialization

            /// <summary>
            /// Converts a crate's inventory (slots only) into a compact binary blob.
            /// This stores no position info - only slot occupancy + item data.
            /// </summary>
            public static byte[] SerializeCratePayload(DNA.CastleMinerZ.Inventory.Crate crate)
            {
                if (crate == null || crate.Inventory == null)
                    return Array.Empty<byte>();

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    for (int i = 0; i < WE_CRATE_SLOT_COUNT; i++)
                    {
                        var it = (i >= 0 && i < crate.Inventory.Length) ? crate.Inventory[i] : null;
                        bw.Write(it != null);
                        it?.Write(bw);
                    }

                    bw.Flush();
                    return ms.ToArray();
                }
            }
            #endregion

            #region Capture (World -> Clipboard Sidecar)

            /// <summary>
            /// Scans WorldInfo.Crates and copies any *existing* crate inventories within the bounds into copiedCrates.
            /// Guardrails:
            ///   - Bounds check (fast reject).
            ///   - Requires the current block still be a container (prevents saving stale entries).
            /// </summary>
            public static void CaptureCratesInBounds(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
            {
                copiedCrates.Clear();

                try
                {
                    var world = DNA.CastleMinerZ.CastleMinerZGame.Instance?.CurrentWorld;
                    if (world == null || world.Crates == null)
                        return;

                    foreach (var kvp in world.Crates)
                    {
                        var p = kvp.Key; // DNA.IntVector3.
                        if (p.X < minX || p.X > maxX) continue;
                        if (p.Y < minY || p.Y > maxY) continue;
                        if (p.Z < minZ || p.Z > maxZ) continue;

                        // Only persist crates that still sit on a container block (guards against stale crate entries).
                        var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)GetBlockFromLocation(new Vector3(p.X, p.Y, p.Z));
                        if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                            continue;

                        Vector3 rel = new Vector3(p.X - minX, p.Y - minY, p.Z - minZ);
                        copiedCrates.Add(new ClipboardCrate(rel, SerializeCratePayload(kvp.Value)));
                    }
                }
                catch
                {
                    // Summary: Never fail a copy because crate capture failed.
                }
            }
            #endregion

            #region Purge / Destroy (World Crate Entries)

            /// <summary>
            /// Removes crate entries inside the bounds from WorldInfo.Crates.
            /// If online, sends DestroyCrateMessage to keep clients in sync.
            ///
            /// Notes:
            ///   - Does NOT touch copiedCrates. Pair this with CopyRegion/CopyChunk if you want to preserve inventories.
            ///   - Optional container check prevents removing already-stale entries unless you want to force purge them.
            /// </summary>
            public static void DestroyCratesInBounds(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
            {
                try
                {
                    var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                    var world = game?.CurrentWorld;
                    if (world == null || world.Crates == null || world.Crates.Count == 0)
                        return;

                    // Prefer network messages when online so other clients stay in sync.
                    bool canSend = IsNetworkSessionActive()
                        && game.LocalPlayer != null
                        && game.LocalPlayer.Gamer is DNA.Net.GamerServices.LocalNetworkGamer;

                    var lg = canSend ? (DNA.Net.GamerServices.LocalNetworkGamer)game.LocalPlayer.Gamer : null;

                    // Collect keys first (cannot modify dictionary during enumeration).
                    var toRemove = new List<DNA.IntVector3>();

                    foreach (var kvp in world.Crates)
                    {
                        var p = kvp.Key;

                        if (p.X < minX || p.X > maxX) continue;
                        if (p.Y < minY || p.Y > maxY) continue;
                        if (p.Z < minZ || p.Z > maxZ) continue;

                        // Optional safety: Only remove if it currently sits on a container block.
                        // (Delete this check to always purge stale entries.)
                        var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)GetBlockFromLocation(new Vector3(p.X, p.Y, p.Z));
                        if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                            continue;

                        toRemove.Add(p);
                    }

                    if (toRemove.Count == 0)
                        return;

                    foreach (var p in toRemove)
                    {
                        if (canSend)
                            DNA.CastleMinerZ.Net.DestroyCrateMessage.Send(lg, p);

                        world.Crates.Remove(p);
                    }
                }
                catch
                {
                    // Summary: Never fail cut operations because crate cleanup failed.
                }
            }

            /// <summary>
            /// Single-location "surgical" purge:
            /// If a crate entry exists at blockLocation, send DestroyCrateMessage and remove it from WorldInfo.Crates.
            /// Common use: /set (non-container) should delete crate sidecar to avoid ghost inventories.
            /// </summary>
            public static void TryDestroyCrateAt(Vector3 blockLocation)
            {
                var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                var world = game?.CurrentWorld;
                if (world == null || world.Crates == null)
                    return;

                // Only do work if a crate entry actually exists here.
                if (!world.Crates.ContainsKey((DNA.IntVector3)blockLocation))
                    return;

                // Purge crate contents.
                DNA.CastleMinerZ.Net.DestroyCrateMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer, (DNA.IntVector3)blockLocation);
                world.Crates.Remove((DNA.IntVector3)blockLocation);
            }
            #endregion

            #region Apply (Clipboard Sidecar -> World)

            /// <summary>
            /// Rehydrates crate inventories from copiedCrates after the terrain blocks have been pasted.
            ///
            /// How it decides where to apply:
            ///   - Builds a lookup of container offsets from sourceClipboard (relative positions).
            ///   - For each copiedCrates entry that matches a container offset, creates/gets the world crate and pushes slots.
            ///
            /// Notes:
            ///   - overwriteExisting=false preserves existing crate contents (skips non-empty crates).
            ///   - Uses ItemCrateMessage.Send for each slot to keep clients in sync.
            /// </summary>
            public static void PasteClipboardCrates(Vector3 pasteBasePosition, HashSet<Tuple<Vector3, int>> sourceClipboard, bool overwriteExisting = true)
            {
                if (copiedCrates.Count <= 0)
                    return;

                var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                var world = game?.CurrentWorld;
                if (world == null)
                    return;

                // Build a quick lookup of which relative positions in the source clipboard are container blocks.
                var containerRelPositions = new HashSet<Vector3>();
                if (sourceClipboard != null)
                {
                    foreach (var item in sourceClipboard)
                    {
                        var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)item.Item2;
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                            containerRelPositions.Add(item.Item1);
                    }
                }

                // If the clipboard had no container blocks, there's nothing to apply.
                if (containerRelPositions.Count == 0)
                    return;

                foreach (var cc in copiedCrates)
                {
                    Vector3 worldPosV = pasteBasePosition + cc.RelativePos;
                    var worldPos = new DNA.IntVector3((int)worldPosV.X, (int)worldPosV.Y, (int)worldPosV.Z);

                    // Only apply to container blocks that are part of the pasted clipboard.
                    if (!containerRelPositions.Contains(cc.RelativePos))
                        continue;

                    // Create/get the crate instance in the WorldInfo.
                    var crate = world.GetCrate(worldPos, true);
                    if (crate == null || crate.Inventory == null)
                        continue;

                    if (!overwriteExisting)
                    {
                        bool any = false;
                        for (int i = 0; i < crate.Inventory.Length; i++)
                        {
                            if (crate.Inventory[i] != null) { any = true; break; }
                        }
                        if (any)
                            continue; // Keep existing contents.
                    }

                    // Apply payload.
                    byte[] payload = cc.Payload ?? Array.Empty<byte>();
                    try
                    {
                        using (var ms = new MemoryStream(payload))
                        using (var br = new BinaryReader(ms))
                        {
                            int slots = Math.Min(WE_CRATE_SLOT_COUNT, crate.Inventory.Length);
                            for (int i = 0; i < slots; i++)
                            {
                                DNA.CastleMinerZ.Inventory.InventoryItem item = null;

                                if (ms.Position < ms.Length && br.ReadBoolean())
                                {
                                    item = DNA.CastleMinerZ.Inventory.InventoryItem.Create(br);
                                    if (item != null && !item.IsValid())
                                        item = null;
                                }

                                DNA.CastleMinerZ.Net.ItemCrateMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer, item, crate, i);
                            }
                        }
                    }
                    catch
                    {
                        // Summary: Skip bad crate payloads instead of breaking a paste.
                    }
                }
            }
            #endregion

            #region Stack / Move Helpers

            /// <summary>
            /// Stack companion: duplicates crate payloads alongside StackRegion() so stacked containers keep their contents.
            ///
            /// Implementation notes:
            ///   - Temporarily swaps copiedCrates so /stack doesn't clobber the user's clipboard sidecar.
            ///   - Captures crates from the source, then applies them for each stacked offset via PasteClipboardCrates().
            /// </summary>
            public static void StackCrateContents(Region region, Direction facingDirection, int stackCount, bool overwriteExisting = true)
            {
                if (stackCount <= 0)
                    return;

                // Normalize bounds.
                int minX = (int)Math.Min(region.Position1.X, region.Position2.X);
                int minY = (int)Math.Min(region.Position1.Y, region.Position2.Y);
                int minZ = (int)Math.Min(region.Position1.Z, region.Position2.Z);
                int maxX = (int)Math.Max(region.Position1.X, region.Position2.X);
                int maxY = (int)Math.Max(region.Position1.Y, region.Position2.Y);
                int maxZ = (int)Math.Max(region.Position1.Z, region.Position2.Z);

                // Save/restore clipboard crate sidecar (so /stack doesn't clobber /copy clipboard crates).
                var savedClipboardCrates = copiedCrates;
                copiedCrates = new List<ClipboardCrate>();

                try
                {
                    // Capture crates in the source region.
                    CaptureCratesInBounds(minX, minY, minZ, maxX, maxY, maxZ);
                    if (copiedCrates.Count <= 0)
                        return;

                    // Build a "source clipboard" with ONLY container blocks, in relative coordinates (matching copiedCrates.RelativePos).
                    var containerClipboard = new HashSet<Tuple<Vector3, int>>();
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            for (int z = minZ; z <= maxZ; z++)
                            {
                                int block = GetBlockFromLocation(new Vector3(x, y, z));
                                var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)block;
                                if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                                    continue;

                                Vector3 rel = new Vector3(x - minX, y - minY, z - minZ);
                                containerClipboard.Add(new Tuple<Vector3, int>(rel, block));
                            }
                        }
                    }

                    if (containerClipboard.Count <= 0)
                        return;

                    Vector3 stackOffset = new Vector3(1, 1, 1); // Must match StackRegion().
                    Vector3 baseMin = new Vector3(minX, minY, minZ);

                    for (int i = 1; i <= stackCount; i++)
                    {
                        Vector3 pasteBase = baseMin + GetStackedRegionOffset(region, facingDirection, stackOffset, i);

                        // Height guard (prevents creating crate entries outside world bounds).
                        if (pasteBase.Y > WorldHeights.MaxY || pasteBase.Y < WorldHeights.MinY)
                            continue;

                        PasteClipboardCrates(pasteBase, containerClipboard, overwriteExisting);
                    }
                }
                finally
                {
                    // Restore clipboard sidecar.
                    copiedCrates = savedClipboardCrates;
                }
            }

            /// <summary>
            /// Move companion: Migrates crate inventories alongside MoveRegion() so moved containers keep their contents.
            ///
            /// Implementation notes:
            ///   - Captures payloads first (never mutate dictionary while enumerating).
            ///   - Removes source crate entries (DestroyCrateMessage + world.Crates.Remove).
            ///   - Recreates destination crates and replays slots using ItemCrateMessage.
            /// </summary>
            public static void MoveCrateContents(Region region, Vector3 moveOffset)
            {
                try
                {
                    var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                    var world = game?.CurrentWorld;
                    if (world == null || world.Crates == null)
                        return;

                    int minX = (int)region.Position1.X;
                    int minY = (int)region.Position1.Y;
                    int minZ = (int)region.Position1.Z;
                    int maxX = (int)region.Position2.X;
                    int maxY = (int)region.Position2.Y;
                    int maxZ = (int)region.Position2.Z;

                    // Capture source crates first (never mutate the dictionary while enumerating).
                    var moves = new List<Tuple<DNA.IntVector3, DNA.IntVector3, byte[]>>();

                    foreach (var kvp in world.Crates)
                    {
                        var src = kvp.Key; // DNA.IntVector3

                        if (src.X < minX || src.X > maxX) continue;
                        if (src.Y < minY || src.Y > maxY) continue;
                        if (src.Z < minZ || src.Z > maxZ) continue;

                        // Only move crates that still sit on a container block (guards against stale crate entries).
                        var btSrc = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)GetBlockFromLocation(new Vector3(src.X, src.Y, src.Z));
                        if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(btSrc))
                            continue;

                        Vector3 dstV = new Vector3(src.X, src.Y, src.Z) + moveOffset;

                        // Match MoveRegion's height guard.
                        if (dstV.Y > WorldHeights.MaxY || dstV.Y < WorldHeights.MinY)
                            continue;

                        var dst = new DNA.IntVector3((int)dstV.X, (int)dstV.Y, (int)dstV.Z);
                        moves.Add(new Tuple<DNA.IntVector3, DNA.IntVector3, byte[]>(src, dst, SerializeCratePayload(kvp.Value)));
                    }

                    if (moves.Count <= 0)
                        return;

                    // Remove old crate entries.
                    foreach (var mv in moves)
                    {
                        var src = mv.Item1;

                        DNA.CastleMinerZ.Net.DestroyCrateMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer, src);

                        world.Crates.Remove(src);
                    }

                    // Apply moved payloads at destinations.
                    foreach (var mv in moves)
                    {
                        var dst = mv.Item2;
                        byte[] payload = mv.Item3 ?? Array.Empty<byte>();

                        var crate = world.GetCrate(dst, true);
                        if (crate == null || crate.Inventory == null)
                            continue;

                        try
                        {
                            using (var ms = new MemoryStream(payload))
                            using (var br = new BinaryReader(ms))
                            {
                                int slots = Math.Min(WE_CRATE_SLOT_COUNT, crate.Inventory.Length);
                                for (int i = 0; i < slots; i++)
                                {
                                    DNA.CastleMinerZ.Inventory.InventoryItem item = null;

                                    // If payload is short/corrupt, treat missing as empty.
                                    bool has = (ms.Position < ms.Length) && br.ReadBoolean();
                                    if (has)
                                    {
                                        item = DNA.CastleMinerZ.Inventory.InventoryItem.Create(br);
                                        if (item != null && !item.IsValid())
                                            item = null;
                                    }

                                    DNA.CastleMinerZ.Net.ItemCrateMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer, item, crate, i);
                                }
                            }
                        }
                        catch
                        {
                            // Summary: Skip bad crate payloads instead of breaking /move.
                        }
                    }
                }
                catch
                {
                    // Summary: Never fail /move because crate migration failed.
                }
            }
            #endregion

            #region Undo/Redo (Crate Sidecar Snapshots)

            /// <summary>
            /// Captures crate payloads for all container blocks in the given actions set.
            /// Key = world position, Value = serialized slot payload.
            /// </summary>
            /// <summary>
            /// Captures crate payloads for all container blocks in the given actions set.
            /// Set item:
            ///   Item1 = world position
            ///   Item2 = serialized slot payload
            ///
            /// Style note:
            ///   - HashSet is keyed by position via WE_CrateSnapComparer, so each position appears at most once.
            /// </summary>
            public static HashSet<Tuple<DNA.IntVector3, byte[]>> CaptureCratesForActions(HashSet<Tuple<Vector3, int, int>> actions)
            {
                var snap = new HashSet<Tuple<DNA.IntVector3, byte[]>>(WE_CrateSnapComparer);

                try
                {
                    var world = DNA.CastleMinerZ.CastleMinerZGame.Instance?.CurrentWorld;
                    if (world == null || world.Crates == null || world.Crates.Count == 0)
                        return snap;

                    foreach (var a in actions)
                    {
                        var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)a.Item2;
                        if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                            continue;

                        var p = (DNA.IntVector3)a.Item1;

                        if (world.Crates.TryGetValue(p, out var crate) && crate != null)
                            snap.Add(Tuple.Create(p, SerializeCratePayload(crate)));
                    }
                }
                catch
                {
                    // Never fail undo snapshot capture due to crate issues.
                }

                return snap;
            }

            /// <summary>
            /// Applies a serialized crate payload to an absolute world position.
            /// Uses ItemCrateMessage per slot (matches your paste/move behavior).
            /// </summary>
            public static void ApplyCratePayloadAt(DNA.IntVector3 worldPos, byte[] payload)
            {
                try
                {
                    var world = DNA.CastleMinerZ.CastleMinerZGame.Instance?.CurrentWorld;
                    if (world == null)
                        return;

                    var crate = world.GetCrate(worldPos, true);
                    if (crate == null || crate.Inventory == null)
                        return;

                    payload = payload ?? Array.Empty<byte>();

                    using (var ms = new MemoryStream(payload))
                    using (var br = new BinaryReader(ms))
                    {
                        int slots = Math.Min(WE_CRATE_SLOT_COUNT, crate.Inventory.Length);
                        for (int i = 0; i < slots; i++)
                        {
                            DNA.CastleMinerZ.Inventory.InventoryItem item = null;

                            bool has = (ms.Position < ms.Length) && br.ReadBoolean();
                            if (has)
                            {
                                item = DNA.CastleMinerZ.Inventory.InventoryItem.Create(br);
                                if (item != null && !item.IsValid())
                                    item = null;
                            }

                            DNA.CastleMinerZ.Net.ItemCrateMessage.Send(
                                DNA.CastleMinerZ.CastleMinerZGame.Instance.MyNetworkGamer,
                                item, crate, i);
                        }
                    }
                }
                catch
                {
                    // Never fail undo/redo application due to crate payload issues.
                }
            }

            /// <summary>
            /// Reconciles crate sidecar to match the snapshot:
            /// - If snapshot block is non-container => destroy crate entry (prevents ghosts).
            /// - If snapshot block is container:
            ///     - If snapshot has payload => apply it.
            ///     - Else => destroy crate entry (forces empty/no-stale).
            /// </summary>
            public static void ApplyCratesFromSnapshot(HashSet<Tuple<Vector3, int>> actions, HashSet<Tuple<DNA.IntVector3, byte[]>> crateSnap)
            {
                if (actions == null || actions.Count == 0)
                    return;

                Dictionary<DNA.IntVector3, byte[]> lookup = null;
                if (crateSnap != null && crateSnap.Count > 0)
                {
                    lookup = new Dictionary<DNA.IntVector3, byte[]>(crateSnap.Count);
                    foreach (var t in crateSnap)
                        lookup[t.Item1] = t.Item2;
                }

                foreach (var a in actions)
                {
                    Vector3 pos = a.Item1;
                    int block = a.Item2;

                    var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)block;

                    if (!DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                    {
                        TryDestroyCrateAt(pos);
                        continue;
                    }

                    var ip = (DNA.IntVector3)pos;

                    if (lookup != null && lookup.TryGetValue(ip, out var payload))
                        ApplyCratePayloadAt(ip, payload);
                    else
                        TryDestroyCrateAt(pos); // Container block but no snapshot => ensure empty/no stale.
                }
            }
            #endregion

            #endregion
        }
        #endregion

        /// <summary>
        ///
        /// Custom comparison class for mapping and comparing enum values by name or numeric value.
        ///
        /// </summary>
        #region Class: Enum Mapper

        public static class EnumMapper
        {
            #region Enum Compare Functions

            // Try to map comma sepererated input strings onto their "closest" members of enum T, returning their numeric values as an integer array.
            public static int[] GetClosestEnumValues<T>(
                string input,
                (int MinID, int MaxID)? rangeIDs = null)
                where T : struct, Enum
            {
                var enumNames = Enum.GetNames(typeof(T));
                var enumValues = Enum.GetValues(typeof(T)).Cast<int>().ToArray();

                int[] enumIDs = input
                    .Split(',')
                    .Select(name => name.Trim())
                    .Where(name => name.Length > 0)
                    .Select(name => ProcessEnumInput<T>(name, enumNames, enumValues, rangeIDs))
                    .ToArray();

                // Check if the array is empty or out of range.
                bool isUnderMin = (!rangeIDs.Equals(default)) && enumIDs.Min() < rangeIDs.Value.MinID;
                bool isOverMax = (!rangeIDs.Equals(default)) && enumIDs.Max() > rangeIDs.Value.MaxID;
                if (enumIDs.Length == 0 || isUnderMin || isOverMax)
                {
                    SendFeedback($"ERROR: Block ID(s) out of range. (min: {BlockIDValues.MinID}, max: {BlockIDValues.MaxID})");
                    return new int[0];
                }

                return enumIDs;
            }

            // Try to map a value of one enum onto a "closest" value of another enum.
            public static bool TryMapEnum<TSource, TDest>(
                TSource sourceValue,
                out TDest destValue,
                string suffix = "Block")
                where TSource : struct, Enum
                where TDest : struct, Enum
            {
                // Reject any unmapped or out-of-range source IDs.
                int raw = Convert.ToInt32(sourceValue);
                if (!Enum.IsDefined(typeof(TSource), raw))
                {
                    destValue = default;
                    return false;
                }

                string name = sourceValue.ToString();

                // If exact parse with suffex, return true.
                if (Enum.TryParse<TDest>(name + suffix, ignoreCase: true, out destValue))
                    return true;

                // If exact parse, return true.
                if (Enum.TryParse<TDest>(name, ignoreCase: true, out destValue))
                    return true;

                // If starts-with, return true.
                var allNames = Enum.GetNames(typeof(TDest));
                var starts = allNames
                    .Where(n => n.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (starts.Length == 1)
                {
                    destValue = (TDest)Enum.Parse(typeof(TDest), starts[0], ignoreCase: true);
                    return true;
                }

                // If fuzzy‐matched by normaliMob Levenshtein distance, return true.

                /*
                destValue = Enum.GetNames(typeof(TDest))
                    .Select(n => new
                    {
                        Name = n,
                        Score = GetLevenshteinDistance(n, name)
                              / (double)Math.Max(n.Length, name.Length)
                    })
                    .OrderBy(x => x.Score)
                    .Select(x => (TDest)Enum.Parse(typeof(TDest), x.Name))
                    .First();
                */

                var scored = Enum.GetNames(typeof(TDest))
                                .Select(n => new {
                                    Name = n,
                                    Score = GetLevenshteinDistance(n, name)
                                          / (double)Math.Max(n.Length, name.Length)
                                })
                                .OrderBy(x => x.Score)
                                .ToArray();

                // If the best match is too far, fail.
                if (scored[0].Score > _maxDistanceThreshold)
                {
                    destValue = default;
                    return false;
                }

                // Otherwise, accept the best match:
                destValue = (TDest)Enum.Parse(typeof(TDest), scored[0].Name, true);
                return true;
            }

            // Try to map an input string onto the "closest" member of enum T.
            public static bool GetClosestEnumValues<T>(string input, out T result) where T : struct, Enum
            {
                // If exact parse, return true.
                if (Enum.TryParse(input, ignoreCase: true, out result))
                    return true;

                // Build a list of names & values.
                var names = Enum.GetNames(typeof(T));
                var values = Enum.GetValues(typeof(T)).Cast<int>().ToArray();

                // Find closest name by Levenshtein.
                string bestName = names
                    .OrderBy(n => GetLevenshteinDistance(n, input))
                    .First();

                // Parse that back into T.
                result = (T)Enum.Parse(typeof(T), bestName, ignoreCase: true);
                return true;
            }
            #endregion

            #region Algorithm Helpers

            private static int ProcessEnumInput<T>(string input, string[] enumNames, int[] enumValues, (int MinID, int MaxID)? rangeIDs) where T : struct, Enum
            {
                // Try to read the input as a number.
                if (int.TryParse(input, out int intValue))
                {
                    // Numeric: Accept only if defined (or mark invalid).
                    return enumValues.Contains(intValue)
                        ? intValue
                        : (rangeIDs.HasValue ? rangeIDs.Value.MinID - 1 : AirID);
                }

                // If the input is a string, find the closest match.
                return FindClosestEnumValue<T>(input, enumNames, rangeIDs);
            }

            private static int FindClosestEnumValue<T>(string input, string[] enumNames, (int MinID, int MaxID)? rangeIDs) where T : struct, Enum
            {
                var closestMatch = enumNames.OrderBy(name => GetLevenshteinDistance(name, input)).First();

                if (Enum.TryParse(closestMatch, true, out T result))
                {
                    return Convert.ToInt32(result);
                }

                // Return the minimum ID (or invalid value) if not found.
                return (!rangeIDs.Equals(default)) ? rangeIDs.Value.MinID - 1 : AirID;
            }

            // Levenshtein Distance Algorithm.
            private static int GetLevenshteinDistance(string s1, string s2)
            {
                int len1 = s1.Length;
                int len2 = s2.Length;
                var dp = new int[len1 + 1, len2 + 1];

                for (int i = 0; i <= len1; i++)
                    for (int j = 0; j <= len2; j++)
                        if (i == 0) dp[i, j] = j;
                        else if (j == 0) dp[i, j] = i;
                        else dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (s1[i - 1] == s2[j - 1] ? 0 : 1));

                return dp[len1, len2];
            }
            #endregion
        }
        #endregion

        /// <summary>
        ///
        /// Custom evaluator class for parsing math expressions.
        ///
        /// </summary>
        #region Class: Math Expression Parsing Utilities

        public enum TokenType { Number, Operator, Function, Variable, LeftParen, RightParen, Comma }

        public class Token
        {
            public TokenType Type;
            public string Value;
            public double Number;
            public Token(TokenType type, string value) { Type = type; Value = value; }
            public Token(double number) { Type = TokenType.Number; Number = number; Value = number.ToString(CultureInfo.InvariantCulture); }
        }

        public class ExpressionEvaluator
        {
            private readonly string expr;

            // Operator precedence: lower number = lower precedence.
            private readonly Dictionary<string, int> opPrecedence = new Dictionary<string, int> {
                {"==", 1}, {"!=", 1},
                {"<", 2}, {"<=", 2}, {">", 2}, {">=", 2},
                {"+", 3}, {"-", 3},
                {"*", 4}, {"/", 4}, {"%", 4},
                {"^", 5}
            };

            // Only exponentiation is right-associative.
            private readonly Dictionary<string, bool> opRightAssoc = new Dictionary<string, bool> {
                {"^", true}
            };

            // One-argument functions.
            private readonly Dictionary<string, Func<double, double>> functions1 = new Dictionary<string, Func<double, double>> {
                {"sqrt", Math.Sqrt},
                {"sin", Math.Sin},
                {"cos", Math.Cos},
                {"tan", Math.Tan},
                {"atan", Math.Atan},
                {"neg", a => -a}, // Our unary minus function.
                {"abs", Math.Abs}
            };

            // Two-argument functions.
            private readonly Dictionary<string, Func<double, double, double>> functions2 = new Dictionary<string, Func<double, double, double>> {
                {"atan2", Math.Atan2},
                {"min", Math.Min},
                {"max", Math.Max}
            };

            public ExpressionEvaluator(string expression)
            {
                expr = expression;
            }

            // Tokenize the expression into a list of tokens.
            // This version detects unary '+' and '-' by looking at the previous token.
            private List<Token> Tokenize()
            {
                List<Token> tokens = new List<Token>();
                Token lastToken = null;
                int i = 0;
                while (i < expr.Length)
                {
                    char c = expr[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    // Number (including decimals)
                    if (char.IsDigit(c) || c == '.')
                    {
                        int start = i;
                        while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.'))
                            i++;
                        double num = double.Parse(expr.Substring(start, i - start), CultureInfo.InvariantCulture);
                        var t = new Token(num);
                        tokens.Add(t);
                        lastToken = t;
                        continue;
                    }
                    // Identifier: variable or function name.
                    if (char.IsLetter(c))
                    {
                        int start = i;
                        while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                            i++;
                        string name = expr.Substring(start, i - start);
                        // If immediately followed by '(' then it's a function.
                        if (i < expr.Length && expr[i] == '(')
                        {
                            var t = new Token(TokenType.Function, name);
                            tokens.Add(t);
                            lastToken = t;
                        }
                        else
                        {
                            var t = new Token(TokenType.Variable, name);
                            tokens.Add(t);
                            lastToken = t;
                        }
                        continue;
                    }
                    // Parentheses and comma.
                    if (c == '(')
                    {
                        tokens.Add(new Token(TokenType.LeftParen, "("));
                        i++;
                        lastToken = new Token(TokenType.LeftParen, "(");
                        continue;
                    }
                    if (c == ')')
                    {
                        tokens.Add(new Token(TokenType.RightParen, ")"));
                        i++;
                        lastToken = new Token(TokenType.RightParen, ")");
                        continue;
                    }
                    if (c == ',')
                    {
                        tokens.Add(new Token(TokenType.Comma, ","));
                        i++;
                        lastToken = new Token(TokenType.Comma, ",");
                        continue;
                    }
                    // Operators (handle multi-character ones first).
                    string op = null;
                    // Check for two-character operator.
                    if (i + 1 < expr.Length)
                    {
                        string two = expr.Substring(i, 2);
                        if (two == "<=" || two == ">=" || two == "==" || two == "!=")
                        {
                            op = two;
                            i += 2;
                        }
                    }
                    if (op == null)
                    {
                        op = c.ToString();
                        i++;
                    }
                    // Check if this operator is unary.
                    // It's unary if it's '+' or '-' and either it's at the start, or the previous token is:
                    // an operator, left parenthesis, or comma.
                    if ((op == "+" || op == "-") && (lastToken == null ||
                          lastToken.Type == TokenType.Operator ||
                          lastToken.Type == TokenType.LeftParen ||
                          lastToken.Type == TokenType.Comma))
                    {
                        // For unary '+', we simply ignore it.
                        if (op == "+")
                        {
                            // Do nothing.
                            continue;
                        }
                        else
                        {
                            // For unary '-', replace it with the "neg" function.
                            var t = new Token(TokenType.Function, "neg");
                            tokens.Add(t);
                            lastToken = t;
                            continue;
                        }
                    }
                    else
                    {
                        var t = new Token(TokenType.Operator, op);
                        tokens.Add(t);
                        lastToken = t;
                    }
                }
                return tokens;
            }

            // Convert tokens from infix to Reverse Polish Notation using the shunting-yard algorithm.
            private List<Token> ToRPN(List<Token> tokens)
            {
                List<Token> output = new List<Token>();
                Stack<Token> stack = new Stack<Token>();
                foreach (var token in tokens)
                {
                    switch (token.Type)
                    {
                        case TokenType.Number:
                        case TokenType.Variable:
                            output.Add(token);
                            break;
                        case TokenType.Function:
                            stack.Push(token);
                            break;
                        case TokenType.Operator:
                            while (stack.Count > 0 && stack.Peek().Type == TokenType.Operator)
                            {
                                string op1 = token.Value;
                                string op2 = stack.Peek().Value;
                                int p1 = opPrecedence.ContainsKey(op1) ? opPrecedence[op1] : 0;
                                int p2 = opPrecedence.ContainsKey(op2) ? opPrecedence[op2] : 0;
                                bool rightAssoc = opRightAssoc.ContainsKey(op1) && opRightAssoc[op1];
                                if ((rightAssoc && p1 < p2) || (!rightAssoc && p1 <= p2))
                                    output.Add(stack.Pop());
                                else
                                    break;
                            }
                            stack.Push(token);
                            break;
                        case TokenType.LeftParen:
                            stack.Push(token);
                            break;
                        case TokenType.RightParen:
                            while (stack.Count > 0 && stack.Peek().Type != TokenType.LeftParen)
                                output.Add(stack.Pop());
                            if (stack.Count == 0) throw new Exception("Mismatched parentheses");
                            stack.Pop(); // Pop the left parenthesis.
                            if (stack.Count > 0 && stack.Peek().Type == TokenType.Function)
                                output.Add(stack.Pop());
                            break;
                        case TokenType.Comma:
                            while (stack.Count > 0 && stack.Peek().Type != TokenType.LeftParen)
                                output.Add(stack.Pop());
                            if (stack.Count == 0) throw new Exception("Mismatched parentheses or misplaced comma");
                            break;
                    }
                }
                while (stack.Count > 0)
                {
                    if (stack.Peek().Type == TokenType.LeftParen || stack.Peek().Type == TokenType.RightParen)
                        throw new Exception("Mismatched parentheses");
                    output.Add(stack.Pop());
                }
                return output;
            }

            // Evaluate the RPN expression.
            public double Evaluate(double x, double y, double z)
            {
                var tokens = Tokenize();
                var rpn = ToRPN(tokens);
                Stack<double> evalStack = new Stack<double>();
                foreach (var token in rpn)
                {
                    if (token.Type == TokenType.Number)
                    {
                        evalStack.Push(token.Number);
                    }
                    else if (token.Type == TokenType.Variable)
                    {
                        double val = 0;
                        switch (token.Value)
                        {
                            case "x": val = x; break;
                            case "y": val = y; break;
                            case "z": val = z; break;
                            case "pi": val = Math.PI; break;
                            default: throw new Exception("Unknown variable: " + token.Value);
                        }
                        evalStack.Push(val);
                    }
                    else if (token.Type == TokenType.Operator)
                    {
                        if (evalStack.Count < 2) throw new Exception("Insufficient operands for operator " + token.Value);
                        double b = evalStack.Pop();
                        double a = evalStack.Pop();
                        double res = 0;
                        switch (token.Value)
                        {
                            case "+": res = a + b; break;
                            case "-": res = a - b; break;
                            case "*": res = a * b; break;
                            case "/": res = a / b; break;
                            case "%": res = a % b; break;
                            case "^": res = Math.Pow(a, b); break;
                            case "<": res = a < b ? 1 : 0; break;
                            case "<=": res = a <= b ? 1 : 0; break;
                            case ">": res = a > b ? 1 : 0; break;
                            case ">=": res = a >= b ? 1 : 0; break;
                            case "==": res = Math.Abs(a - b) < 1e-9 ? 1 : 0; break;
                            case "!=": res = Math.Abs(a - b) > 1e-9 ? 1 : 0; break;
                            default: throw new Exception("Unknown operator: " + token.Value);
                        }
                        evalStack.Push(res);
                    }
                    else if (token.Type == TokenType.Function)
                    {
                        if (functions1.ContainsKey(token.Value))
                        {
                            if (evalStack.Count < 1) throw new Exception("Insufficient arguments for function " + token.Value);
                            double arg = evalStack.Pop();
                            double res = functions1[token.Value](arg);
                            evalStack.Push(res);
                        }
                        else if (functions2.ContainsKey(token.Value))
                        {
                            if (evalStack.Count < 2) throw new Exception("Insufficient arguments for function " + token.Value);
                            double arg2 = evalStack.Pop();
                            double arg1 = evalStack.Pop();
                            double res = functions2[token.Value](arg1, arg2);
                            evalStack.Push(res);
                        }
                        else
                        {
                            throw new Exception("Unknown function: " + token.Value);
                        }
                    }
                }
                if (evalStack.Count != 1)
                    throw new Exception("Invalid expression evaluation");
                return evalStack.Pop();
            }

            // Public Parse method (alias for Evaluate).
            public double Parse(double x, double y, double z)
            {
                return Evaluate(x, y, z);
            }
        }
        #endregion

        /// <summary>
        ///
        /// 'Undo'
        /// 'Redo'
        /// 'Clear'
        ///
        /// </summary>
        #region General Methods

        #region Undo

        // Push nothng to the undo stack to "skip" this data.
        public static Task SaveUndo(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!_undoRecordingEnabled)
                    return;

                HashSet<Tuple<Vector3, int, int>> actionsBuilder = new HashSet<Tuple<Vector3, int, int>>();

                // Add builder to new redo.
                UndoStack.Push(actionsBuilder);
                UndoCrateStack.Push(CaptureCratesForActions(actionsBuilder));
            }, ct);
        }

        // Get the existing block data and push it to the undo stack.
        public static Task SaveUndo(HashSet<Vector3> region, int[] saveBlock = null, int[] ignoreBlock = null, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!_undoRecordingEnabled)
                        return;

                    HashSet<Tuple<Vector3, int, int>> actionsBuilder = new HashSet<Tuple<Vector3, int, int>>();
                    bool wasGUIDCreated = false; // Prevents making a new guid on non-duplicate hashsets.
                    bool useForAll = false;      // Skip further stack checking to increase performance.
                    int randomGUID = 0;          // Define a starting integer.

                    // Treat empty the same as null.
                    if (saveBlock   != null && saveBlock.Length   == 0) return;
                    if (ignoreBlock != null && ignoreBlock.Length == 0) return;

                    // Iterate through all vectors within the region.
                    foreach (Vector3 i in region)
                    {
                        // Get the block ID from regions location within the world.
                        int worldBlock = GetBlockFromLocation(i);

                        // If a save block was specified, only save the locations of that block id.
                        if (saveBlock != null && !saveBlock.Contains(worldBlock)) continue;

                        // If ignore block was specified, skip locations matching the block id.
                        if (ignoreBlock != null && ignoreBlock.Contains(worldBlock)) continue;

                        // Define new data.
                        var vectorData = new Tuple<Vector3, int, int>(i, worldBlock, 0);

                        // Check if the hashset already contains this data.
                        if (useForAll || (UndoStack.Count > 0 && UndoStack.Peek().Contains(vectorData)))
                        {
                            // Generate a new guid only once.
                            if (!wasGUIDCreated)
                            {
                                wasGUIDCreated = true; // Prevents making a new guid on non-duplicate hashsets.
                                randomGUID = Guid.NewGuid().GetHashCode();
                            }

                            useForAll = true;          // Skip further stack checking to increase performance.
                            vectorData = new Tuple<Vector3, int, int>(i, worldBlock, randomGUID);
                        }

                        actionsBuilder.Add(vectorData);
                    }

                    // Add builder to new redo.
                    UndoStack.Push(actionsBuilder);
                    UndoCrateStack.Push(CaptureCratesForActions(actionsBuilder));
                }
                catch (NullReferenceException ex)
                {
                    SendFeedback($"[SaveUndo(actions)] NRE: {ex.Message}.");
                }
            }, ct);
        }

        // Push the new block data to the undo stack.
        public static Task SaveUndo(HashSet<Tuple<Vector3, int>> currentActions, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!_undoRecordingEnabled)
                    return;

                HashSet<Tuple<Vector3, int, int>> actionsBuilder = new HashSet<Tuple<Vector3, int, int>>();
                bool wasGUIDCreated = false; // Prevents making a new guid on non-duplicate hashsets.
                bool useForAll = false;      // Skip further stack checking to increase performance.
                int randomGUID = 0;          // Define a starting integer.

                // Iterate through all vectors within the region.
                foreach (var action in currentActions)
                {
                    Vector3 position = action.Item1;
                    int blockID = action.Item2;

                    // Define new data.
                    var vectorData = new Tuple<Vector3, int, int>(position, blockID, 0);

                    // Check if the undo stack already contains this data.
                    if (useForAll || (UndoStack.Count > 0 && UndoStack.Peek().Contains(vectorData)))
                    {
                        // Generate a new guid only once.
                        if (!wasGUIDCreated)
                        {
                            wasGUIDCreated = true; // Prevents making a new guid on non-duplicate hashsets.
                            randomGUID = Guid.NewGuid().GetHashCode();
                        }

                        useForAll = true;          // Skip further stack checking to increase performance.
                        vectorData = new Tuple<Vector3, int, int>(position, blockID, randomGUID);
                    }

                    actionsBuilder.Add(vectorData);
                }

                // Add builder to new redo.
                UndoStack.Push(actionsBuilder);
                UndoCrateStack.Push(CaptureCratesForActions(actionsBuilder));
            }, ct);
        }
        #endregion

        #region Redo

        public static Task<Tuple<HashSet<Tuple<Vector3, int>>, HashSet<Tuple<DNA.IntVector3, byte[]>>>> LoadUndo(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Original behavior (style note):
                // - Undo requires a "pair" of snapshots: BEFORE + AFTER.
                // - We pop AFTER (move to redo), then pop BEFORE (apply it).
                //
                // New behavior:
                // - We do the same pairing logic for crate snapshots (UndoCrateStack),
                //   so container contents follow the same timeline as blocks.

                // Need at least 2 snapshots (before + after).
                if (UndoStack.Count <= 1 || UndoCrateStack.Count <= 1)
                    return Tuple.Create(new HashSet<Tuple<Vector3, int>>(), new HashSet<Tuple<DNA.IntVector3, byte[]>>(WE_CrateSnapComparer));

                var actionsBuilder = new HashSet<Tuple<Vector3, int, int>>();
                var redoActionsBuilder = new HashSet<Tuple<Vector3, int, int>>();

                // Pop "after" (goes to redo), then "before" (what we apply).
                // Style note: UnionWith() is the HashSet equivalent of AddRange().
                redoActionsBuilder.UnionWith(UndoStack.Pop());
                var redoCrates = UndoCrateStack.Pop();

                actionsBuilder.UnionWith(UndoStack.Pop());
                var undoCrates = UndoCrateStack.Pop();

                // Push to redo stacks in the same order as blocks.
                // Note: This preserves the "pair" structure so redo can re-apply the AFTER snapshot.
                RedoStack.Push(redoActionsBuilder);
                RedoStack.Push(actionsBuilder);

                RedoCrateStack.Push(redoCrates);
                RedoCrateStack.Push(undoCrates);

                // Convert the internal triple tuple to the public "apply list" tuple.
                // Note: We intentionally return the BEFORE edits here (actionsBuilder) because undo applies BEFORE.
                var flat = new HashSet<Tuple<Vector3, int>>();
                foreach (var a in actionsBuilder)
                    flat.Add(new Tuple<Vector3, int>(a.Item1, a.Item2));

                // Return:
                // - flat:       Blocks to apply for undo.
                // - undoCrates: Crate snapshot to apply AFTER blocks (restore contents / remove ghost entries).
                return Tuple.Create(flat, undoCrates);
            }, ct);
        }

        public static Task<Tuple<HashSet<Tuple<Vector3, int>>, HashSet<Tuple<DNA.IntVector3, byte[]>>>> LoadRedo(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Original behavior (style note):
                // - Redo also requires 2 snapshots: BEFORE + AFTER.
                // - We pop BEFORE (move back to undo), then pop AFTER (apply it).
                //
                // New behavior:
                // - Same pairing logic for crate snapshots so contents redo correctly.

                if (RedoStack.Count <= 1 || RedoCrateStack.Count <= 1)
                    return Tuple.Create(new HashSet<Tuple<Vector3, int>>(), new HashSet<Tuple<DNA.IntVector3, byte[]>>(WE_CrateSnapComparer));

                var actionsBuilder = new HashSet<Tuple<Vector3, int, int>>();
                var redoActionsBuilder = new HashSet<Tuple<Vector3, int, int>>();

                // Pop "before" then "after"; apply "after".
                actionsBuilder.UnionWith(RedoStack.Pop());
                var beforeCrates = RedoCrateStack.Pop();

                redoActionsBuilder.UnionWith(RedoStack.Pop());
                var afterCrates = RedoCrateStack.Pop();

                // Push back onto undo stacks.
                // Note: This reconstructs the same "pair" layout Undo expects later.
                UndoStack.Push(actionsBuilder);
                UndoStack.Push(redoActionsBuilder);

                UndoCrateStack.Push(beforeCrates);
                UndoCrateStack.Push(afterCrates);

                // Convert the internal triple tuple to the public "apply list" tuple.
                // Note: We return the AFTER edits here (redoActionsBuilder) because redo re-applies AFTER.
                var flat = new HashSet<Tuple<Vector3, int>>();
                foreach (var a in redoActionsBuilder)
                    flat.Add(new Tuple<Vector3, int>(a.Item1, a.Item2));

                // Return:
                // - flat:        Blocks to apply for redo.
                // - afterCrates: Crate snapshot to apply AFTER blocks.
                return Tuple.Create(flat, afterCrates);
            }, ct);
        }
        #endregion

        #region Clear

        public static void ClearRedo()
        {
            RedoStack.Clear();
            RedoCrateStack.Clear();
        }
        public static void ClearUndo()
        {
            UndoStack.Clear();
            UndoCrateStack.Clear();
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'Ascend'
        /// 'Descend'
        /// 'Ceil'
        /// 'Thru'
        /// 'ClearHistory'
        ///
        /// </summary>
        #region Navigation Methods

        #region Ascend

        public static Task<Vector3> GetAscendingVector(Vector3 pos, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                for (int y = (int)pos.Y; y < WorldHeights.MaxY; y++)                         // Start at current Y, go up.
                {
                    if (GetBlockFromLocation(new Vector3(pos.X, y, pos.Z)) != AirID)          // Stop at non air block.
                    {
                        for (int y2 = y + 1; y2 < WorldHeights.MaxY; y2++)                   // Continue up to find a valid block to stand on.
                        {
                            if (GetBlockFromLocation(new Vector3(pos.X, y2, pos.Z)) == AirID) // Stop at air.
                            {
                                return new Vector3(pos.X, y2, pos.Z);                         // Return open space block.
                            }
                        }
                    }
                }
                return pos;                                                                   // If no valid position found, return original position.
            }, ct);
        }
        #endregion

        #region Descend

        public static Task<Vector3> GetDescendingVector(Vector3 pos, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                for (int y = (int)pos.Y; y > WorldHeights.MinY; y--)                                 // Start at current Y, go down.
                {
                    if (GetBlockFromLocation(new Vector3(pos.X, y, pos.Z)) != AirID)                  // Stop at none air block.
                    {
                        for (int y2 = y - 1; y2 > WorldHeights.MinY; y2--)                           // Continue down to find air.
                        {
                            if (GetBlockFromLocation(new Vector3(pos.X, y2, pos.Z)) == AirID)         // Stop at air.
                            {
                                for (int y3 = y2 - 1; y3 > WorldHeights.MinY; y3--)                  // Continue down to find a valid block to stand on.
                                {
                                    if (GetBlockFromLocation(new Vector3(pos.X, y3, pos.Z)) != AirID) // Stop at non air block.
                                    {
                                        return new Vector3(pos.X, y3 + 1, pos.Z);                     // Return open space above block.
                                    }
                                }
                            }
                        }
                    }
                }
                return pos;                                                                           // If no valid position found, return original position.
            }, ct);
        }
        #endregion

        #region Ceil

        public static Task<Vector3> GetCeilingVector(Vector3 pos, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                for (int y = WorldHeights.MaxY; y > WorldHeights.MinY - 1; y--)    // Start at max Y, go down.
                {
                    if (GetBlockFromLocation(new Vector3(pos.X, y, pos.Z)) != AirID) // Stop at non air block.
                    {
                        return new Vector3(pos.X, y + 1f, pos.Z);                    // Return open space above block.
                    }
                }
                return pos;                                                          // If no valid position found, return original position.
            }, ct);
        }
        #endregion

        #region Thru

        public static Task<Vector3> GetThruVector(Vector3 pos, Direction facingDirection, int maxSteps = 512, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Step vector to move in the correct direction.
                Vector3 step = Vector3.Zero;

                if (facingDirection == Direction.Up) step = new Vector3(0, 1, 0);
                else if (facingDirection == Direction.Down) step = new Vector3(0, -1, 0);
                else if (facingDirection == Direction.posX) step = new Vector3(1, 0, 0);
                else if (facingDirection == Direction.negX) step = new Vector3(-1, 0, 0);
                else if (facingDirection == Direction.posZ) step = new Vector3(0, 0, 1);
                else if (facingDirection == Direction.negZ) step = new Vector3(0, 0, -1);

                // Defensive: Unknown direction (or future enum value) -> do nothing.
                if (step == Vector3.Zero)
                    return pos;

                // Start one step "behind" so the first increment lands back on pos.
                Vector3 currentPos = pos - step;

                // Start at the current position and move in the step direction.
                int steps = 0;
                while (steps++ < maxSteps && IsWithinWorldBounds(currentPos)) // Ensure we stay within world bounds.
                {
                    currentPos += step;                                       // Move in the facing direction.

                    if (GetBlockFromLocation(currentPos) != AirID)            // Continue moving when we find a none air block.
                    {                                                         // This would be the wall to pass through.

                        while (IsWithinWorldBounds(currentPos))               // Ensure we stay within world bounds.
                        {
                            currentPos += step;                               // Move in the facing direction.

                            if (GetBlockFromLocation(currentPos) == AirID)    // Air found. This is the other side of the wall.
                            {
                                currentPos += step;                           // Move once more in the facing direction.
                                return currentPos;                            // Return the first open space.
                            }
                        }
                    }
                }

                return pos;                                                   // If no valid position found, return original position.
            }, ct);
        }
        #endregion

        #region ClearHistory

        public static void ClearHistory()
        {
            UndoStack.Clear();
            RedoStack.Clear();
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'Trim'
        /// 'Count'
        /// 'Distr'
        ///
        /// </summary>
        #region Selection Methods

        #region Trim

        public static bool TrimRegion(Region region, HashSet<int> maskSet, out Vector3 outMin, out Vector3 outMax)
        {
            // Prepare current region (min / max no matter which corner was pos1).
            Vector3 selMin = Vector3.Min(region.Position1, region.Position2);
            Vector3 selMax = Vector3.Max(region.Position1, region.Position2);

            bool found = false;
            outMin = new Vector3(float.MaxValue);
            outMax = new Vector3(float.MinValue);

            for (int x = (int)selMin.X; x <= selMax.X; x++)
            {
                for (int y = (int)selMin.Y; y <= selMax.Y; y++)
                {
                    for (int z = (int)selMin.Z; z <= selMax.Z; z++)
                    {
                        Vector3 p = new Vector3(x, y, z);

                        int id = GetBlockFromLocation(p);
                        if (!maskSet.Contains(id)) continue;

                        if (!found)
                        {
                            outMin = outMax = p;
                            found  = true;
                        }
                        else
                        {
                            outMin = Vector3.Min(outMin, p);
                            outMax = Vector3.Max(outMax, p);
                        }
                    }
                }
            }
            return found;
        }
        #endregion

        #region Count

        public static Task<HashSet<Tuple<Vector3, int>>> CountRegion(Region region, HashSet<int> maskSet, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> regionBlocks = new HashSet<Tuple<Vector3, int>>();

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    // Ensure Y is within the world's height constraints.
                    if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                        continue;

                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            Vector3 newPos = new Vector3(x, y, z);
                            int blockType = GetBlockFromLocation(newPos);

                            // If ignore block was specified, skip locations matching the block id.
                            if (ignoreBlock != -1 && blockType == ignoreBlock) continue;

                            if (maskSet.Contains(blockType))
                                regionBlocks.Add(new Tuple<Vector3, int>(newPos, blockType));
                        }
                    }
                }

                return regionBlocks;
            }, ct);
        }
        #endregion

        #region Distr

        // Helper function.
        public static IEnumerable<int> EnumerateIdsInRegion(Vector3 min, Vector3 max)
        {
            for (int x = (int)min.X; x <= max.X; x++)
                for (int y = (int)min.Y; y <= max.Y; y++)
                    for (int z = (int)min.Z; z <= max.Z; z++)
                        yield return GetBlockFromLocation(new Vector3(x, y, z));
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'Fill Region'
        /// 'Line'
        /// 'Overlay'
        /// 'Naturalize'
        /// 'Walls'
        /// 'Smooth'
        /// 'Move'
        /// 'Stack'
        /// 'Stretch'
        /// 'Spell Words'
        /// 'Hollow'
        /// 'ShapeFill'
        /// 'FloodFill'
        /// 'Wrap'
        /// 'Matrix'
        /// 'Forest'
        /// 'Tree'
        ///
        /// </summary>
        #region Region Methods

        #region Fill

        public static Task<HashSet<Vector3>> FillRegion(Region region, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> regionBlocks = new HashSet<Vector3>();

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    // Ensure Y is within the world's height constraints.
                    if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                        continue;

                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            // If not hollow, add all blocks.
                            if (!hollow)
                            {
                                // If ignore block was specified, skip locations matching the block id.
                                Vector3 newPos = new Vector3(x, y, z);
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    regionBlocks.Add(newPos);
                            }
                            else
                            {
                                // Add only the boundary blocks.
                                bool isBoundary =
                                    x == (int)region.Position1.X || x == (int)region.Position2.X || // X boundaries.
                                    y == (int)region.Position1.Y || y == (int)region.Position2.Y || // Y boundaries.
                                    z == (int)region.Position1.Z || z == (int)region.Position2.Z;   // Z boundaries.

                                if (isBoundary)
                                {
                                    // If ignore block was specified, skip locations matching the block id.
                                    Vector3 newPos = new Vector3(x, y, z);
                                    if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                        regionBlocks.Add(newPos);
                                }
                            }
                        }
                    }
                }

                return regionBlocks;
            }, ct);
        }
        #endregion

        #region Line

        public static Task<HashSet<Vector3>> MakeLine(Region region, int thickness, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Important: Use absolute positions.
                Vector3 start = region.AbsolutePosition1;
                Vector3 end = region.AbsolutePosition2;

                HashSet<Vector3> lineBlocks = new HashSet<Vector3>();

                // Get direction vector.
                Vector3 direction = end - start;
                float maxComponent = Math.Max(Math.Abs(direction.X), Math.Max(Math.Abs(direction.Y), Math.Abs(direction.Z)));
                direction /= maxComponent; // Normalize while keeping integer precision.

                // Iterate through points along the line.
                for (int i = 0; i <= maxComponent; i++)
                {
                    Vector3 point = start + direction * i;
                    Vector3 roundedPoint = new Vector3((int)point.X, (int)point.Y, (int)point.Z);

                    // Ensure Y is within the world's height constraints.
                    if (roundedPoint.Y > WorldHeights.MaxY || roundedPoint.Y < WorldHeights.MinY)
                        continue;

                    lineBlocks.Add(new Vector3(roundedPoint.X, roundedPoint.Y, roundedPoint.Z));
                }

                // Account for the thickness by expanding the line into a 'ballooned' shape.
                if (thickness > 0)
                    lineBlocks = GetBallooned(lineBlocks, thickness);

                return lineBlocks;
            }, ct);
        }

        #region Balloon Math Helper

        private static HashSet<Vector3> GetBallooned(HashSet<Vector3> lineBlocks, int thickness)
        {
            HashSet<Vector3> expandedBlocks = new HashSet<Vector3>();

            foreach (var block in lineBlocks)
            {
                // For each block, add surrounding blocks to create the thickness effect
                for (int xOffset = -thickness; xOffset <= thickness; xOffset++)
                {
                    for (int yOffset = -thickness; yOffset <= thickness; yOffset++)
                    {
                        // Ensure Y is within the world's height constraints.
                        int worldY = (int)block.Y + yOffset; // Calculate actual world Y position.
                        if (worldY > WorldHeights.MaxY || worldY < WorldHeights.MinY)
                            continue;

                        for (int zOffset = -thickness; zOffset <= thickness; zOffset++)
                        {
                            // Only add blocks that are within the radius of the original block
                            if (Math.Sqrt(xOffset * xOffset + yOffset * yOffset + zOffset * zOffset) <= thickness)
                            {
                                expandedBlocks.Add(block + new Vector3(xOffset, yOffset, zOffset));
                            }
                        }
                    }
                }
            }

            return expandedBlocks;
        }
        #endregion

        #endregion

        #region Overlay

        public static Task<HashSet<Vector3>> OverlayObject(Region region, List<int> replaceBlockPattern, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> regionBlocks = new HashSet<Vector3>();

                int minY = Math.Min((int)region.Position1.Y, (int)region.Position2.Y);
                int maxY = Math.Max((int)region.Position1.Y, (int)region.Position2.Y) + 1;

                int minX = Math.Min((int)region.Position1.X, (int)region.Position2.X);
                int maxX = Math.Max((int)region.Position1.X, (int)region.Position2.X) + 1;

                int minZ = Math.Min((int)region.Position1.Z, (int)region.Position2.Z);
                int maxZ = Math.Max((int)region.Position1.Z, (int)region.Position2.Z) + 1;

                // Iterate over the range of blocks to wrap.
                for (int y = minY; y < maxY; y++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        for (int z = minZ; z < maxZ; z++)
                        {
                            Vector3 currentPosition = new Vector3(x, y, z);

                            // Check if the current block is different from the target block type.
                            bool overlayBlocks = false;
                            if (GetBlockFromLocation(currentPosition) != AirID)
                            {
                                overlayBlocks = !replaceBlockPattern.Contains(GetBlockFromLocation(currentPosition));
                            }

                            if (overlayBlocks)
                            {
                                // Try wrapping the block in 7 possible adjacent positions.
                                for (int direction = 0; direction < 2; direction++)
                                {
                                    switch (direction)
                                    {
                                        case 0:
                                            currentPosition = new Vector3(x, y, z);
                                            break;
                                        case 1:
                                            currentPosition = new Vector3(x, y + 1f, z);
                                            break;
                                    }

                                    // If the block is empty, wrap it with the new block type.
                                    if (GetBlockFromLocation(currentPosition) == AirID)
                                    {
                                        regionBlocks.Add(currentPosition);
                                    }
                                }
                            }
                        }
                    }
                }

                return regionBlocks;
            }, ct);
        }
        #endregion

        #region Naturalize

        /// <summary>
        /// Re-layers natural terrain blocks in the region into Grass (top), Dirt (next 3), and Rock (below).
        /// </summary>
        public static Task<HashSet<Tuple<Vector3, int>>> NaturalizeTerrain(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> actions = new HashSet<Tuple<Vector3, int>>();

                int minY = Math.Min((int)region.Position1.Y, (int)region.Position2.Y);
                int maxY = Math.Max((int)region.Position1.Y, (int)region.Position2.Y);

                int minX = Math.Min((int)region.Position1.X, (int)region.Position2.X);
                int maxX = Math.Max((int)region.Position1.X, (int)region.Position2.X);

                int minZ = Math.Min((int)region.Position1.Z, (int)region.Position2.Z);
                int maxZ = Math.Max((int)region.Position1.Z, (int)region.Position2.Z);

                int idDirt = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Dirt;
                int idGrass = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Grass;
                int idRock = (int)DNA.CastleMinerZ.Terrain.BlockTypeEnum.Rock;

                bool IsNatural(int id) => (id == idRock || id == idDirt || id == idGrass);

                // Process each X/Z column.
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        // Find the top-most non-air block in the selection.
                        int surfaceY = int.MinValue;
                        int surfaceBlock = AirID;

                        for (int y = maxY; y >= minY; y--)
                        {
                            int b = GetBlockFromLocation(new Vector3(x, y, z));
                            if (b != AirID)
                            {
                                surfaceY = y;
                                surfaceBlock = b;
                                break;
                            }
                        }

                        if (surfaceY == int.MinValue)
                            continue; // Entire column is air.

                        // If the surface isn't a natural block, skip placing grass.
                        bool surfaceIsNatural = IsNatural(surfaceBlock);

                        int naturalDepth = 0;

                        // Walk downward; only re-layer natural blocks (rock/dirt/grass).
                        for (int y = surfaceY; y >= minY; y--)
                        {
                            Vector3 pos = new Vector3(x, y, z);
                            int current = GetBlockFromLocation(pos);

                            if (!IsNatural(current))
                                continue;

                            int target;
                            if (naturalDepth == 0)
                                target = surfaceIsNatural ? idGrass : idDirt; // Grass skipped if surface is non-natural.
                            else if (naturalDepth <= 3)
                                target = idDirt;                              // Next 3 natural layers.
                            else
                                target = idRock;                              // Rest.

                            if (current != target)
                                actions.Add(new Tuple<Vector3, int>(pos, target));

                            naturalDepth++;
                        }
                    }
                }

                return actions;
            }, ct);
        }
        #endregion

        #region Walls

        public static Task<HashSet<Vector3>> MakeWalls(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> wallBlocks = new HashSet<Vector3>();

                // Get min/max values to form a bounding box.
                int minX = (int)Math.Min(region.Position1.X, region.Position2.X);
                int maxX = (int)Math.Max(region.Position1.X, region.Position2.X);
                int minY = (int)Math.Min(region.Position1.Y, region.Position2.Y);
                int maxY = (int)Math.Max(region.Position1.Y, region.Position2.Y);
                int minZ = (int)Math.Min(region.Position1.Z, region.Position2.Z);
                int maxZ = (int)Math.Max(region.Position1.Z, region.Position2.Z);

                // Iterate over the bounding box.
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        // Ensure Y is within the world's height constraints.
                        if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                            continue;

                        for (int z = minZ; z <= maxZ; z++)
                        {
                            // Exclude full top and bottom layers but keep their edges.
                            bool isSideWall = (x == minX || x == maxX || z == minZ || z == maxZ);
                            bool isTopBottomEdge = (y == minY || y == maxY) && isSideWall;

                            if (isSideWall || isTopBottomEdge)
                            {
                                wallBlocks.Add(new Vector3(x, y, z));
                            }
                        }
                    }
                }

                return wallBlocks;
            }, ct);
        }
        #endregion

        #region Smooth

        /// <summary>
        /// Smooths "terrain" in a region by:
        ///  1) Building a 2D heightmap (top-most terrain block per X/Z column),
        ///  2) Blurring that heightmap with a separable Gaussian filter,
        ///  3) Raising/lowering each column to match the smoothed height.
        /// </summary>
        public static Task<HashSet<Tuple<Vector3, int>>> SmoothTerrain(
            Region region,
            int iterations,
            int radius = 2,                    // Default blur radius.
            Func<int, bool> heightMask = null, // Which blocks count as "terrain" for surface detection.
            Func<int, bool> editMask = null,   // Which existing blocks are allowed to be modified.
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Default masks.
                if (heightMask == null) heightMask = (id => id != AirID); // Treat non-air as terrain.
                if (editMask == null) editMask = (id => true);            // Allow edits everywhere unless restricted.

                if (iterations < 1) iterations = 1;
                if (radius < 1) radius = 1;

                // Region corners (Region already stores min/max, but we still guard).
                int minX = (int)Math.Min(region.Position1.X, region.Position2.X);
                int maxX = (int)Math.Max(region.Position1.X, region.Position2.X);
                int minY = (int)Math.Min(region.Position1.Y, region.Position2.Y);
                int maxY = (int)Math.Max(region.Position1.Y, region.Position2.Y);
                int minZ = (int)Math.Min(region.Position1.Z, region.Position2.Z);
                int maxZ = (int)Math.Max(region.Position1.Z, region.Position2.Z);

                int width = maxX - minX + 1;
                int depth = maxZ - minZ + 1;

                // Defensive: Empty/invalid selection -> no-op.
                if (width <= 0 || depth <= 0)
                    return new HashSet<Tuple<Vector3, int>>();

                // Defensive: Prevent int overflow / impossible allocations on huge selections.
                long countLong = (long)width * (long)depth;
                if (countLong > int.MaxValue)
                    throw new InvalidOperationException($"SmoothTerrain: Region too large (width={width}, depth={depth}).");

                int count = (int)countLong;

                // Per-column data.
                int[] baseHeights = new int[count]; // Original surface Y (or minY-1 if none).
                int[] surfaceId = new int[count];   // Surface block id.
                int[] fillId = new int[count];      // Fill block id under surface.

                // 1) Build heightmap (scan downward and break).
                for (int dz = 0; dz < depth; dz++)
                {
                    ct.ThrowIfCancellationRequested();

                    int z = minZ + dz;
                    for (int dx = 0; dx < width; dx++)
                    {
                        int x = minX + dx;
                        int idx = dz * width + dx;

                        int foundY = minY - 1;
                        int topId = AirID;
                        int under = AirID;

                        for (int y = maxY; y >= minY; y--)
                        {
                            int id = GetBlockFromLocation(new Vector3(x, y, z));
                            if (id != AirID && heightMask(id))
                            {
                                foundY = y;
                                topId = id;

                                // Choose fill: Block directly under surface if it's also "terrain", else use surface.
                                if (y - 1 >= minY)
                                {
                                    int underId = GetBlockFromLocation(new Vector3(x, y - 1, z));
                                    under = (underId != AirID && heightMask(underId)) ? underId : topId;
                                }
                                else
                                {
                                    under = topId;
                                }

                                break;
                            }
                        }

                        baseHeights[idx] = foundY;
                        surfaceId[idx] = topId;
                        fillId[idx] = under;
                    }
                }

                // Float heightmap for filtering.
                float[] h = new float[count];
                for (int i = 0; i < count; i++) h[i] = baseHeights[i];

                // 2) Smooth heightmap (Gaussian blur, separable).
                float[] kernel = BuildGaussianKernel1D(radius, radius / 2f);
                float[] tmp = new float[count];

                for (int it = 0; it < iterations; it++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Horizontal pass.
                    for (int dz = 0; dz < depth; dz++)
                    {
                        int row = dz * width;
                        for (int dx = 0; dx < width; dx++)
                        {
                            float sum = 0f;
                            for (int k = -radius; k <= radius; k++)
                            {
                                int sx = Clamp(dx + k, 0, width - 1);
                                sum += h[row + sx] * kernel[k + radius];
                            }
                            tmp[row + dx] = sum;
                        }
                    }

                    // Vertical pass.
                    for (int dz = 0; dz < depth; dz++)
                    {
                        int row = dz * width;
                        for (int dx = 0; dx < width; dx++)
                        {
                            float sum = 0f;
                            for (int k = -radius; k <= radius; k++)
                            {
                                int sz = Clamp(dz + k, 0, depth - 1);
                                sum += tmp[sz * width + dx] * kernel[k + radius];
                            }
                            h[row + dx] = sum;
                        }
                    }
                }

                // 3) Apply smoothed heights by editing full columns.
                var changes = new HashSet<Tuple<Vector3, int>>();

                for (int dz = 0; dz < depth; dz++)
                {
                    ct.ThrowIfCancellationRequested();

                    int z = minZ + dz;
                    for (int dx = 0; dx < width; dx++)
                    {
                        int x = minX + dx;
                        int idx = dz * width + dx;

                        int oldY = baseHeights[idx];
                        int newY = (int)Math.Round(h[idx]);

                        // Clamp to region Y bounds (allow "no terrain" at minY-1).
                        if (newY < minY - 1) newY = minY - 1;
                        if (newY > maxY) newY = maxY;

                        int surf = surfaceId[idx];
                        int fill = fillId[idx];

                        // If column had no detected surface but smoothing wants to raise it,
                        // borrow a nearby surface material (fills small holes nicer).
                        if (surf == AirID && newY >= minY)
                        {
                            surf = BorrowNeighborSurface(surfaceId, width, depth, dx, dz);
                            if (surf == AirID) continue;
                            if (fill == AirID) fill = surf;
                        }

                        if (newY == oldY) continue;

                        // Raise: Fill oldY+1 .. newY.
                        if (newY > oldY)
                        {
                            int start = Math.Max(minY, oldY + 1);
                            for (int y = start; y <= newY; y++)
                            {
                                int targetId = (y == newY) ? surf : fill;
                                Vector3 pos = new Vector3(x, y, z);

                                int existing = GetBlockFromLocation(pos);
                                if (!editMask(existing)) continue;
                                if (existing == targetId) continue;

                                changes.Add(Tuple.Create(pos, targetId));
                            }
                        }
                        // Lower: Carve oldY .. newY+1.
                        else
                        {
                            int start = Math.Min(maxY, oldY);
                            int stop = Math.Max(minY, newY + 1);

                            for (int y = start; y >= stop; y--)
                            {
                                Vector3 pos = new Vector3(x, y, z);

                                int existing = GetBlockFromLocation(pos);
                                if (!editMask(existing)) continue;
                                if (existing == AirID) continue;

                                changes.Add(Tuple.Create(pos, AirID)); // Carve to air.
                            }

                            // Ensure new surface exists (optional, usually looks better).
                            if (newY >= minY)
                            {
                                Vector3 pos = new Vector3(x, newY, z);
                                int existing = GetBlockFromLocation(pos);

                                if (editMask(existing) && existing != surf)
                                    changes.Add(Tuple.Create(pos, surf));
                            }
                        }
                    }
                }

                return changes;
            }, ct);
        }

        #region Smooth Helpers

        /// <summary>
        /// Builds a normalized 1D Gaussian kernel (size = 2*radius+1) suitable for separable blur passes.
        /// </summary>
        /// <remarks>
        private static float[] BuildGaussianKernel1D(int radius, float sigma)
        {
            int size = radius * 2 + 1;
            var k = new float[size];

            if (sigma <= 0f) sigma = 1f;
            float twoSigma2 = 2f * sigma * sigma;

            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
            {
                float w = (float)Math.Exp(-(i * i) / twoSigma2);
                k[i + radius] = w;
                sum += w;
            }

            // Normalize so weights sum to 1.
            for (int i = 0; i < size; i++) k[i] /= sum;
            return k;
        }

        /// <summary>
        /// Attempts to "borrow" a nearby surface block id when a column has no detected surface,
        /// but the smoothing result wants to raise that column above minY.
        /// </summary>
        private static int BorrowNeighborSurface(int[] surface, int width, int depth, int x, int z)
        {
            // Simple "closest ring" search (cheap + good enough for filling holes).
            for (int r = 1; r <= 3; r++)
            {
                int minX = Math.Max(0, x - r);
                int maxX = Math.Min(width - 1, x + r);
                int minZ = Math.Max(0, z - r);
                int maxZ = Math.Min(depth - 1, z + r);

                for (int zz = minZ; zz <= maxZ; zz++)
                    for (int xx = minX; xx <= maxX; xx++)
                    {
                        int id = surface[zz * width + xx];
                        if (id != AirID) return id;
                    }
            }

            return AirID;
        }
        #endregion

        #endregion

        #region Move

        public static Task<HashSet<Tuple<Vector3, int>>> MoveRegion(Region region, Vector3 moveOffset, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sourceSnapshot = new Dictionary<(int X, int Y, int Z), int>();
                var movedSourceKeys = new HashSet<(int X, int Y, int Z)>();
                var finalWrites = new Dictionary<(int X, int Y, int Z), int>();

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            var srcKey = (x, y, z);
                            Vector3 srcPos = new Vector3(x, y, z);
                            int block = GetBlockFromLocation(srcPos);

                            // Snapshot the source first.
                            sourceSnapshot[srcKey] = block;

                            Vector3 dstPosV = srcPos + moveOffset;
                            int dstY = (int)dstPosV.Y;

                            // Preserve your current height guard behavior:
                            // if the destination is out of bounds, don't move or clear the source.
                            if (dstY > WorldHeights.MaxY || dstY < WorldHeights.MinY)
                                continue;

                            var dstKey = ((int)dstPosV.X, (int)dstPosV.Y, (int)dstPosV.Z);

                            movedSourceKeys.Add(srcKey);

                            // Includes air so holes inside the selection move correctly too.
                            finalWrites[dstKey] = block;
                        }
                    }
                }

                // Clear old source locations, but do NOT let source-clears erase blocks
                // that are being moved into overlapping positions.
                foreach (var srcKey in movedSourceKeys)
                {
                    int sourceBlock = sourceSnapshot[srcKey];

                    // No need to clear source air.
                    if (sourceBlock == AirID)
                        continue;

                    // If a non-air moved block is landing here, that destination write wins.
                    if (finalWrites.TryGetValue(srcKey, out int incomingBlock) && incomingBlock != AirID)
                        continue;

                    finalWrites[srcKey] = AirID;
                }

                var result = new HashSet<Tuple<Vector3, int>>();

                foreach (var kvp in finalWrites)
                {
                    result.Add(new Tuple<Vector3, int>(
                        new Vector3(kvp.Key.X, kvp.Key.Y, kvp.Key.Z),
                        kvp.Value));
                }

                return result;
            }, ct);
        }

        /// <summary>
        /// Represents the result of a move-region gather operation.
        /// Holds the generated write set plus the number of solid source blocks that were actually moved.
        /// </summary>
        public sealed class MoveRegionResult
        {
            /// <summary>
            /// The generated write set for the move operation.
            /// </summary>
            public HashSet<Tuple<Vector3, int>> RegionBlocks { get; set; }

            /// <summary>
            /// The number of non-air source blocks that were moved to a valid destination.
            /// </summary>
            public int MovedSolidBlockCount { get; set; }
        }
        #endregion

        #region Stack

        public static Task<HashSet<Tuple<Vector3, int>>> StackRegion(Region region, Direction facingDirection, int stackCount, bool useAir = true, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> stackBlocks = new HashSet<Tuple<Vector3, int>>();

                // Offset for each stack (adjust as needed).
                Vector3 stackOffset = new Vector3(1, 1, 1);

                // Loop for each stack.
                for (int i = 1; i <= stackCount; i++)
                {
                    // Get each XYZ.
                    for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                    {
                        for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                        {
                            for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                            {
                                // Get block from location.
                                int block = GetBlockFromLocation(new Vector3(x, y, z));

                                // Check if useAir is disabled and if so, skip gathering air blocks.
                                if (!useAir && block == AirID) continue;

                                // Calculate the offset per direction and update original position with calculated position.
                                Vector3 regionOffset = new Vector3(x, y, z) + GetStackedRegionOffset(region, facingDirection, stackOffset, i);

                                // Ensure Y is within the world's height constraints.
                                if (regionOffset.Y > WorldHeights.MaxY || regionOffset.Y < WorldHeights.MinY)
                                    continue;

                                // Save new location to stack region hashset.
                                stackBlocks.Add(new Tuple<Vector3, int>(regionOffset, block));

                                // SendFeedback("Stacked Region " + i + " - Original: " + new Vector3(x, y, z) + ", Offset: " + regionOffset);
                            }
                        }
                    }
                }

                return stackBlocks;
            }, ct);
        }

        static Vector3 GetStackedRegionOffset(Region region, Direction facingDirection, Vector3 stackOffset, int stackIndex)
        {
            // Calculate the size of the original region.
            Vector3 regionSize = region.Position2 - region.Position1;

            // Initialize the region offset.
            Vector3 regionOffset = Vector3.Zero;

            // Determine the offset based on the stacking direction.
            if (facingDirection == Direction.Up)
            {
                regionOffset = new Vector3(0, (regionSize.Y + stackOffset.Y) * stackIndex, 0);
            }
            else if (facingDirection == Direction.Down)
            {
                regionOffset = new Vector3(0, -(regionSize.Y + stackOffset.Y) * stackIndex, 0);
            }
            else if (facingDirection == Direction.posX)
            {
                regionOffset = new Vector3((regionSize.X + stackOffset.X) * stackIndex, 0, 0);
            }
            else if (facingDirection == Direction.negX)
            {
                regionOffset = new Vector3(-(regionSize.X + stackOffset.X) * stackIndex, 0, 0);
            }
            else if (facingDirection == Direction.posZ)
            {
                regionOffset = new Vector3(0, 0, (regionSize.Z + stackOffset.Z) * stackIndex);
            }
            else if (facingDirection == Direction.negZ)
            {
                regionOffset = new Vector3(0, 0, -(regionSize.Z + stackOffset.Z) * stackIndex);
            }

            // Return the calculated region offset.
            return regionOffset;
        }
        #endregion

        #region Stretch

        public static Task<HashSet<Tuple<Vector3, int>>> StretchRegion(Region region, Direction stretchDirection, double stretchFactor, bool useAir = true, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> stretchedBlocks = new HashSet<Tuple<Vector3, int>>();

                if (stretchDirection == Direction.posX || stretchDirection == Direction.negX)
                {
                    double centerX = (region.Position1.X + region.Position2.X) / 2.0;
                    // Group blocks by (Y,Z) - these coordinates remain constant when stretching along X.
                    Dictionary<(int, int), List<(int originalX, int blockType)>> rows = new Dictionary<(int, int), List<(int, int)>>();
                    for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; y++)
                    {
                        for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; x++)
                        {
                            for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; z++)
                            {
                                Vector3 pos = new Vector3(x, y, z);
                                int block = GetBlockFromLocation(pos);
                                var key = (y, z);
                                if (!rows.ContainsKey(key))
                                    rows[key] = new List<(int, int)>();
                                rows[key].Add((x, block));
                            }
                        }
                    }

                    // Process each row (constant Y and Z).
                    foreach (var kvp in rows)
                    {
                        var key = kvp.Key;         // key = (y, z).
                        var rowBlocks = kvp.Value; // Each tuple: (originalX, blockType).
                        var transformed = rowBlocks
                            .Select(b => new
                            {
                                newCoord = (int)Math.Round(centerX + ((double)b.originalX - centerX) * stretchFactor),
                                b.blockType,
                                original = b.originalX
                            })
                            .ToList();

                        // Sort in order according to the stretch direction.
                        if (stretchDirection == Direction.negX)
                            transformed.Sort((a, b) => b.newCoord.CompareTo(a.newCoord));
                        else
                            transformed.Sort((a, b) => a.newCoord.CompareTo(b.newCoord));

                        // Add each transformed block and fill gaps between adjacent blocks.
                        for (int i = 0; i < transformed.Count; i++)
                        {
                            var current = transformed[i];
                            Vector3 newLocation = new Vector3(current.newCoord, key.Item1, key.Item2);

                            if (useAir || GetBlockFromLocation(newLocation) != current.blockType || current.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    newLocation,
                                    current.blockType));

                            if (i < transformed.Count - 1)
                            {
                                var next = transformed[i + 1];
                                int gap = Math.Abs(next.newCoord - current.newCoord);
                                if (gap > 1)
                                {
                                    int start = Math.Min(current.newCoord, next.newCoord);
                                    int end = Math.Max(current.newCoord, next.newCoord);
                                    for (int fillX = start + 1; fillX < end; fillX++)
                                    {
                                        Vector3 fillLocation = new Vector3(fillX, key.Item1, key.Item2);

                                        if (useAir || GetBlockFromLocation(fillLocation) != current.blockType || current.blockType != AirID)
                                            stretchedBlocks.Add(new Tuple<Vector3, int>(
                                                fillLocation,
                                                current.blockType));
                                    }
                                }
                            }
                        }

                        // Extend the last segment-only if there's more than one block in this row.
                        if (transformed.Count > 1)
                        {
                            var last = transformed.Last();
                            int gap = Math.Abs(last.newCoord - transformed[transformed.Count - 2].newCoord);
                            gap = gap > 0 ? gap - 1 : 0;  // Adjust to avoid overextension.
                            int extension = (stretchDirection == Direction.posX) ? gap : -gap;
                            int extensionEnd = last.newCoord + extension;
                            int fillStart = Math.Min(last.newCoord, extensionEnd);
                            int fillEnd = Math.Max(last.newCoord, extensionEnd);
                            for (int fillX = fillStart + 1; fillX < fillEnd; fillX++)
                            {
                                Vector3 fillLocation = new Vector3(fillX, key.Item1, key.Item2);

                                if (useAir || GetBlockFromLocation(fillLocation) != last.blockType || last.blockType != AirID)
                                    stretchedBlocks.Add(new Tuple<Vector3, int>(
                                        fillLocation,
                                        last.blockType));
                            }
                            Vector3 extensionLocation = new Vector3(extensionEnd, key.Item1, key.Item2);

                            if (useAir || GetBlockFromLocation(extensionLocation) != last.blockType || last.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    extensionLocation,
                                    last.blockType));
                        }
                    }
                }
                else if (stretchDirection == Direction.Up || stretchDirection == Direction.Down)
                {
                    double centerY = (region.Position1.Y + region.Position2.Y) / 2.0;
                    // Group blocks by (X,Z) - these remain constant when stretching along Y.
                    Dictionary<(int, int), List<(int originalY, int blockType)>> rows = new Dictionary<(int, int), List<(int, int)>>();
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; x++)
                    {
                        for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; y++)
                        {
                            for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; z++)
                            {
                                Vector3 pos = new Vector3(x, y, z);
                                int block = GetBlockFromLocation(pos);
                                var key = (x, z);
                                if (!rows.ContainsKey(key))
                                    rows[key] = new List<(int, int)>();
                                rows[key].Add((y, block));
                            }
                        }
                    }

                    // Process each group (constant X and Z).
                    foreach (var kvp in rows)
                    {
                        var key = kvp.Key;         // key = (x, z).
                        var rowBlocks = kvp.Value; // Each tuple: (originalY, blockType).
                        var transformed = rowBlocks
                            .Select(b => new
                            {
                                newCoord = (int)Math.Round(centerY + ((double)b.originalY - centerY) * stretchFactor),
                                b.blockType,
                                original = b.originalY
                            })
                            .ToList();

                        if (stretchDirection == Direction.Down)
                            transformed.Sort((a, b) => b.newCoord.CompareTo(a.newCoord));
                        else
                            transformed.Sort((a, b) => a.newCoord.CompareTo(b.newCoord));

                        for (int i = 0; i < transformed.Count; i++)
                        {
                            var current = transformed[i];

                            // Clamp Y to world bounds to ensure the new Y value is within world bounds.
                            Vector3 newLocation = new Vector3(key.Item1, current.newCoord, key.Item2);
                            int clampedY = (int)Math.Min(Math.Max(newLocation.Y, WorldHeights.MinY), WorldHeights.MaxY);
                            Vector3 finalLocation = new Vector3(newLocation.X, clampedY, newLocation.Z);

                            if (useAir || GetBlockFromLocation(newLocation) != current.blockType || current.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    finalLocation,
                                    current.blockType));

                            if (i < transformed.Count - 1)
                            {
                                var next = transformed[i + 1];
                                int gap = Math.Abs(next.newCoord - current.newCoord);
                                if (gap > 1)
                                {
                                    int start = Math.Min(current.newCoord, next.newCoord);
                                    int end = Math.Max(current.newCoord, next.newCoord);
                                    for (int fillY = start + 1; fillY < end; fillY++)
                                    {
                                        // Clamp the fill Y position to ensure the new Y value is within world bounds.
                                        int clampedFillY = (int)Math.Min(Math.Max(fillY, WorldHeights.MinY), WorldHeights.MaxY);
                                        Vector3 fillLocation = new Vector3(key.Item1, clampedFillY, key.Item2);

                                        if (useAir || GetBlockFromLocation(fillLocation) != current.blockType || current.blockType != AirID)
                                            stretchedBlocks.Add(new Tuple<Vector3, int>(
                                                fillLocation,
                                                current.blockType));
                                    }
                                }
                            }
                        }

                        // For Y, only extend if there is more than one block in this group.
                        if (transformed.Count > 1)
                        {
                            var last = transformed.Last();
                            int gap = Math.Abs(last.newCoord - transformed[transformed.Count - 2].newCoord);
                            gap = gap > 0 ? gap - 1 : 0;
                            int extension = (stretchDirection == Direction.Up) ? gap : -gap;
                            int extensionEnd = last.newCoord + extension;
                            int fillStart = Math.Min(last.newCoord, extensionEnd);
                            int fillEnd = Math.Max(last.newCoord, extensionEnd);
                            for (int fillY = fillStart + 1; fillY < fillEnd; fillY++)
                            {
                                int clampedFillY = (int)Math.Min(Math.Max(fillY, WorldHeights.MinY), WorldHeights.MaxY);
                                Vector3 fillLocation = new Vector3(key.Item1, clampedFillY, key.Item2);

                                if (useAir || GetBlockFromLocation(fillLocation) != last.blockType || last.blockType != AirID)
                                    stretchedBlocks.Add(new Tuple<Vector3, int>(
                                        fillLocation,
                                        last.blockType));
                            }
                            // Clamp the extension end before adding to ensure the new Y value is within world bounds.
                            int clampedExtensionY = (int)Math.Min(Math.Max(extensionEnd, WorldHeights.MinY), WorldHeights.MaxY);
                            Vector3 extensionLocation = new Vector3(key.Item1, clampedExtensionY, key.Item2);

                            if (useAir || GetBlockFromLocation(new Vector3(key.Item1, extensionEnd, key.Item2)) != last.blockType || last.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    extensionLocation,
                                    last.blockType));
                        }
                    }
                }
                else if (stretchDirection == Direction.posZ || stretchDirection == Direction.negZ)
                {
                    double centerZ = (region.Position1.Z + region.Position2.Z) / 2.0;
                    // Group blocks by (X,Y) - these remain constant when stretching along Z.
                    Dictionary<(int, int), List<(int originalZ, int blockType)>> rows = new Dictionary<(int, int), List<(int, int)>>();
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; x++)
                    {
                        for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; y++)
                        {
                            for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; z++)
                            {
                                Vector3 pos = new Vector3(x, y, z);
                                int block = GetBlockFromLocation(pos);
                                var key = (x, y);
                                if (!rows.ContainsKey(key))
                                    rows[key] = new List<(int, int)>();
                                rows[key].Add((z, block));
                            }
                        }
                    }

                    // Process each group (constant X and Y).
                    foreach (var kvp in rows)
                    {
                        var key = kvp.Key;         // key = (x, y).
                        var rowBlocks = kvp.Value; // Each tuple: (originalZ, blockType).
                        var transformed = rowBlocks
                            .Select(b => new
                            {
                                newCoord = (int)Math.Round(centerZ + ((double)b.originalZ - centerZ) * stretchFactor),
                                b.blockType,
                                original = b.originalZ
                            })
                            .ToList();

                        if (stretchDirection == Direction.negZ)
                            transformed.Sort((a, b) => b.newCoord.CompareTo(a.newCoord));
                        else
                            transformed.Sort((a, b) => a.newCoord.CompareTo(b.newCoord));

                        for (int i = 0; i < transformed.Count; i++)
                        {
                            var current = transformed[i];
                            Vector3 newLocation = new Vector3(key.Item1, key.Item2, current.newCoord);

                            if (useAir || GetBlockFromLocation(newLocation) != current.blockType || current.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    newLocation,
                                    current.blockType));

                            if (i < transformed.Count - 1)
                            {
                                var next = transformed[i + 1];
                                int gap = Math.Abs(next.newCoord - current.newCoord);
                                if (gap > 1)
                                {
                                    int start = Math.Min(current.newCoord, next.newCoord);
                                    int end = Math.Max(current.newCoord, next.newCoord);
                                    for (int fillZ = start + 1; fillZ < end; fillZ++)
                                    {
                                        Vector3 fillLocation = new Vector3(key.Item1, key.Item2, fillZ);

                                        if (useAir || GetBlockFromLocation(fillLocation) != current.blockType || current.blockType != AirID)
                                            stretchedBlocks.Add(new Tuple<Vector3, int>(
                                                fillLocation,
                                                current.blockType));
                                    }
                                }
                            }
                        }

                        // For Z, extend the last segment only if there is more than one block.
                        if (transformed.Count > 1)
                        {
                            var last = transformed.Last();
                            int gap = Math.Abs(last.newCoord - transformed[transformed.Count - 2].newCoord);
                            gap = gap > 0 ? gap - 1 : 0;
                            int extension = (stretchDirection == Direction.posZ) ? gap : -gap;
                            int extensionEnd = last.newCoord + extension;
                            int fillStart = Math.Min(last.newCoord, extensionEnd);
                            int fillEnd = Math.Max(last.newCoord, extensionEnd);
                            for (int fillZ = fillStart + 1; fillZ < fillEnd; fillZ++)
                            {
                                Vector3 fillLocation = new Vector3(key.Item1, key.Item2, fillZ);

                                if (useAir || GetBlockFromLocation(fillLocation) != last.blockType || last.blockType != AirID)
                                    stretchedBlocks.Add(new Tuple<Vector3, int>(
                                        fillLocation,
                                        last.blockType));
                            }
                            Vector3 extensionLocation = new Vector3(key.Item1, key.Item2, extensionEnd);

                            if (useAir || GetBlockFromLocation(new Vector3(key.Item1, key.Item2, extensionEnd)) != last.blockType || last.blockType != AirID)
                                stretchedBlocks.Add(new Tuple<Vector3, int>(
                                    extensionLocation,
                                    last.blockType));
                        }
                    }
                }

                return stretchedBlocks;
            }, ct);
        }
        #endregion

        #region Spell Words

        public static Task<HashSet<Vector3>> MakeWords(Vector3 pos, string wordString, bool flipSide = false, int rotateDegrees = 0, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> wordBlocks = new HashSet<Vector3>();

                // Check if the input string is "/paste".
                if (wordString == "/paste")
                    wordString = Clipboard.GetText();

                #region Individual Characters

                // Letter patterns using '#' as block positions.
                Dictionary<char, string[]> letterPatterns = new Dictionary<char, string[]>
                {
                    #region Letters
                    { 'A', new string[]
                    {
                        "  #  ",
                        " # # ",
                        "#   #",
                        "#####",
                        "#   #"
                    }},
                    { 'B', new string[]
                    {
                        "#### ",
                        "#   #",
                        "#### ",
                        "#   #",
                        "#### "
                    }},
                    { 'C', new string[]
                    {
                        " ####",
                        "#    ",
                        "#    ",
                        "#    ",
                        " ####"
                    }},
                    { 'D', new string[]
                    {
                        "###  ",
                        "#  # ",
                        "#   #",
                        "#  # ",
                        "###  "
                    }},
                    { 'E', new string[]
                    {
                        "#####",
                        "#    ",
                        "#####",
                        "#    ",
                        "#####"
                    }},
                    { 'F', new string[]
                    {
                        "#####",
                        "#    ",
                        "#####",
                        "#    ",
                        "#    "
                    }},
                    { 'G', new string[]
                    {
                        " ####",
                        "#    ",
                        "#  ##",
                        "#   #",
                        " ####"
                    }},
                    { 'H', new string[]
                    {
                        "#   #",
                        "#   #",
                        "#####",
                        "#   #",
                        "#   #"
                    }},
                    { 'I', new string[]
                    {
                        " ### ",
                        "  #  ",
                        "  #  ",
                        "  #  ",
                        " ### "
                    }},
                    { 'J', new string[]
                    {
                        "  ###",
                        "   # ",
                        "   # ",
                        "#  # ",
                        " ##  "
                    }},
                    { 'K', new string[]
                    {
                        "#   #",
                        "#  # ",
                        "###  ",
                        "#  # ",
                        "#   #"
                    }},
                    { 'L', new string[]
                    {
                        "#    ",
                        "#    ",
                        "#    ",
                        "#    ",
                        "#####"
                    }},
                    { 'M', new string[]
                    {
                        "#   #",
                        "## ##",
                        "# # #",
                        "#   #",
                        "#   #"
                    }},
                    { 'N', new string[]
                    {
                        "#   #",
                        "##  #",
                        "# # #",
                        "#  ##",
                        "#   #"
                    }},
                    { 'O', new string[]
                    {
                        " ### ",
                        "#   #",
                        "#   #",
                        "#   #",
                        " ### "
                    }},
                    { 'P', new string[]
                    {
                        "#### ",
                        "#   #",
                        "#### ",
                        "#    ",
                        "#    "
                    }},
                    { 'Q', new string[]
                    {
                        " ### ",
                        "#   #",
                        "#   #",
                        "#  ##",
                        " ####"
                    }},
                    { 'R', new string[]
                    {
                        "#### ",
                        "#   #",
                        "#### ",
                        "#  # ",
                        "#   #"
                    }},
                    { 'S', new string[]
                    {
                        " ####",
                        "#    ",
                        " ### ",
                        "    #",
                        "#### "
                    }},
                    { 'T', new string[]
                    {
                        "#####",
                        "  #  ",
                        "  #  ",
                        "  #  ",
                        "  #  "
                    }},
                    { 'U', new string[]
                    {
                        "#   #",
                        "#   #",
                        "#   #",
                        "#   #",
                        " ### "
                    }},
                    { 'V', new string[]
                    {
                        "#   #",
                        "#   #",
                        "#   #",
                        " # # ",
                        "  #  "
                    }},
                    { 'W', new string[]
                    {
                        "#   #",
                        "#   #",
                        "# # #",
                        "# # #",
                        " # # "
                    }},
                    { 'X', new string[]
                    {
                        "#   #",
                        " # # ",
                        "  #  ",
                        " # # ",
                        "#   #"
                    }},
                    { 'Y', new string[]
                    {
                        "#   #",
                        " # # ",
                        "  #  ",
                        "  #  ",
                        "  #  "
                    }},
                    { 'Z', new string[]
                    {
                        "#####",
                        "   # ",
                        "  #  ",
                        " #   ",
                        "#####"
                    }},
                    #endregion

                    #region Numbers
                    { '0', new string[]
                    {
                        "#####",
                        "#  ##",
                        "# # #",
                        "##  #",
                        "#####"
                    }},
                    { '1', new string[]
                    {
                        "  #  ",
                        " ##  ",
                        "  #  ",
                        "  #  ",
                        "#####"
                    }},
                    { '2', new string[]
                    {
                        "#####",
                        "    #",
                        "#####",
                        "#    ",
                        "#####"
                    }},
                    { '3', new string[]
                    {
                        "#### ",
                        "    #",
                        "#### ",
                        "    #",
                        "#### "
                    }},
                    { '4', new string[]
                    {
                        "#   #",
                        "#   #",
                        "#### ",
                        "    #",
                        "    #"
                    }},
                    { '5', new string[]
                    {
                        "#### ",
                        "#    ",
                        "#### ",
                        "    #",
                        "#### "
                    }},
                    { '6', new string[]
                    {
                        "#### ",
                        "#    ",
                        "#### ",
                        "#   #",
                        "#### "
                    }},
                    { '7', new string[]
                    {
                        "#####",
                        "   # ",
                        "  #  ",
                        " #   ",
                        "#    "
                    }},
                    { '8', new string[]
                    {
                        " ### ",
                        "#   #",
                        " ### ",
                        "#   #",
                        " ### "
                    }},
                    { '9', new string[]
                    {
                        "#### ",
                        "#   #",
                        "#### ",
                        "    #",
                        "#### "
                    }},
                    #endregion

                    #region Special Characters
                    { ' ', new string[]
                    {
                        "     ",
                        "     ",
                        "     ",
                        "     ",
                        "     "
                    }},
                    { '!', new string[]
                    {
                        "  #  ",
                        "  #  ",
                        "  #  ",
                        "     ",
                        "  #  "
                    }},
                    { '"', new string[]
                    {
                        " # # ",
                        " # # ",
                        "     ",
                        "     ",
                        "     "
                    }},
                    { '#', new string[]
                    {
                        " # # ",
                        "#####",
                        " # # ",
                        "#####",
                        " # # "
                    }},
                    { '$', new string[]
                    {
                        " ####",
                        "# #  ",
                        " ### ",
                        "  # #",
                        "#####"
                    }},
                    { '%', new string[]
                    {
                        " #   #",
                        "   #  ",
                        "  #   ",
                        " #    ",
                        "#   # "
                    }},
                    { '&', new string[]
                    {
                        " ### ",
                        "#   #",
                        " ### ",
                        "#  # ",
                        " ####"
                    }},
                    { '\'', new string[]
                    {
                        "  #  ",
                        "     ",
                        "     ",
                        "     ",
                        "     "
                    }},
                    { '(', new string[]
                    {
                        "  ## ",
                        " #   ",
                        "#    ",
                        " #   ",
                        "  ## "
                    }},
                    { ')', new string[]
                    {
                        " ##  ",
                        "   # ",
                        "    #",
                        "   # ",
                        " ##  "
                    }},
                    { '*', new string[]
                    {
                        "  #  ",
                        " # # ",
                        "#####",
                        " # # ",
                        "  #  "
                    }},
                    { '+', new string[]
                    {
                        "     ",
                        "  #  ",
                        "#####",
                        "  #  ",
                        "     "
                    }},
                    { ',', new string[]
                    {
                        "     ",
                        "     ",
                        "     ",
                        "  #  ",
                        " #   "
                    }},
                    { '-', new string[]
                    {
                        "     ",
                        "     ",
                        "#####",
                        "     ",
                        "     "
                    }},
                    { '.', new string[]
                    {
                        "     ",
                        "     ",
                        "     ",
                        "     ",
                        "  #  "
                    }},
                    { '/', new string[]
                    {
                        "    #",
                        "   # ",
                        "  #  ",
                        " #   ",
                        "#    "
                    }},
                    { ':', new string[]
                    {
                        "     ",
                        "  #  ",
                        "     ",
                        "  #  ",
                        "     "
                    }},
                    { ';', new string[]
                    {
                        "     ",
                        "  #  ",
                        "     ",
                        "  #  ",
                        " #   "
                    }},
                    { '<', new string[]
                    {
                        "   # ",
                        "  #  ",
                        " #   ",
                        "  #  ",
                        "   # "
                    }},
                    { '=', new string[]
                    {
                        "     ",
                        "#####",
                        "     ",
                        "#####",
                        "     "
                    }},
                    { '>', new string[]
                    {
                        " #   ",
                        "  #  ",
                        "   # ",
                        "  #  ",
                        " #   "
                    }},
                    { '?', new string[]
                    {
                        "#### ",
                        "    #",
                        "  #  ",
                        "     ",
                        "  #  "
                    }},
                    { '[', new string[]
                    {
                        "#####",
                        "#    ",
                        "#    ",
                        "#    ",
                        "#####"
                    }},
                    { '\\', new string[]
                    {
                        "#    ",
                        " #   ",
                        "  #  ",
                        "   # ",
                        "    #"
                    }},
                    { ']', new string[]
                    {
                        "#####",
                        "    #",
                        "    #",
                        "    #",
                        "#####"
                    }},
                    { '^', new string[]
                    {
                        "  #  ",
                        " # # ",
                        "     ",
                        "     ",
                        "     "
                    }},
                    { '_', new string[]
                    {
                        "     ",
                        "     ",
                        "     ",
                        "     ",
                        "#####"
                    }},
                    { '`', new string[]
                    {
                        " #   ",
                        "     ",
                        "     ",
                        "     ",
                        "     "
                    }},
                    { '{', new string[]
                    {
                        "  ###",
                        " #    ",
                        "#     ",
                        " #    ",
                        "  ###"
                    }},
                    { '|', new string[]
                    {
                        "  #  ",
                        "  #  ",
                        "  #  ",
                        "  #  ",
                        "  #  "
                    }},
                    { '}', new string[]
                    {
                        "###  ",
                        "   # ",
                        "     #",
                        "   # ",
                        "###  "
                    }},
                    { '~', new string[]
                    {
                        "     ",
                        " #  #",
                        "#  # ",
                        "     ",
                        "     "
                    }}
                    #endregion

                    /*
                    { '@', new string[]
                    {
                        " #### ",
                        "#    #",
                        "#  # #",
                        "#   # ",
                        " #### "
                    }},
                    */

                    // The '@' is sacrificed as a linebreak. Its hardly used and hard to represent.
                    // Add other characters here.
                };
                #endregion

                int letterSpacing = 1; // Space between letters.
                int xOffset = 0;       // Offset for each letter.
                int yOffset = 0;       // Y offset to move lines down.

                // Normalize rotation to { 0, 90, 180, 270 } (360 == 0, negatives allowed).
                int rot = ((rotateDegrees % 360) + 360) % 360;
                if (rot != 0 && rot != 90 && rot != 180 && rot != 270)
                    rot = 0;

                // Split wordString at "@" to handle line breaks.
                string[] lines = wordString.Split('@', '\n');

                foreach (string line in lines)
                {
                    string upper = (line ?? string.Empty).ToUpperInvariant();

                    // "Flip" = place the entire line on the OTHER side of /pos while keeping letter order.
                    // We do this by shifting the whole line so its MAX-X ends at 0 (instead of its MIN-X).
                    int maxLocalX = -1;
                    int tmpOffset = 0;

                    // Pass 1: Compute width-ish so we can anchor the right edge when flipping.
                    foreach (char c in upper)
                    {
                        if (!letterPatterns.TryGetValue(c, out string[] pat))
                            continue;

                        // Determine actual pattern width (some rows may be shorter/longer).
                        int patW = 0;
                        for (int i = 0; i < pat.Length; i++)
                        {
                            string row = pat[i] ?? string.Empty;
                            if (row.Length > patW) patW = row.Length;
                        }

                        // Track the rightmost possible X for this character at its current offset.
                        if (patW > 0)
                            maxLocalX = Math.Max(maxLocalX, tmpOffset + (patW - 1));

                        // Move X position for next letter.
                        tmpOffset += 5 + letterSpacing;
                    }

                    // If flipping, move the whole line so its right-most block touches X=0.
                    int flipShiftX = (flipSide && maxLocalX >= 0) ? -maxLocalX : 0;

                    // Pass 2: Build the blocks for this line.
                    xOffset = 0;
                    foreach (char letter in upper)
                    {
                        if (!letterPatterns.TryGetValue(letter, out string[] pattern))
                            continue;

                        // Iterate over rows (Y-axis) and columns (X-axis) of the 2D pattern.
                        for (int y = 0; y < pattern.Length; y++) // Rows (Y-axis).
                        {
                            string row = pattern[y] ?? string.Empty;
                            for (int x = 0; x < row.Length; x++) // Cols (X-axis).
                            {
                                // If it's not a block pixel, skip.
                                if (row[x] != '#') continue;

                                // Local X = Letter's base offset + pixel X + optional flip shift.
                                int localX = xOffset + x + flipShiftX;

                                // Top row sits at pos.Y; text grows downward under /pos.
                                int yWorld = yOffset - y;

                                // Rotate the whole line around the Y axis in 90-degree steps.
                                // localX becomes either world X or world Z (or their negatives).
                                Vector3 off;
                                switch (rot)
                                {
                                    default:
                                    case 0: off = new Vector3(localX, yWorld, 0); break;
                                    case 90: off = new Vector3(0, yWorld, localX); break;
                                    case 180: off = new Vector3(-localX, yWorld, 0); break;
                                    case 270: off = new Vector3(0, yWorld, -localX); break;
                                }

                                wordBlocks.Add(Vector3.Add(pos, off));
                            }
                        }

                        xOffset += 5 + letterSpacing; // Move X position for next letter.
                    }

                    yOffset -= 6; // Move Y position down for next line.
                    xOffset = 0;  // Reset X offset for new line.
                }

                return wordBlocks;
            }, ct);
        }
        #endregion

        #region Hollow

        public static Task<HashSet<Vector3>> HollowObject(Region region, int thickness, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> regionBlocks = new HashSet<Vector3>();

                int minY = Math.Min((int)region.Position1.Y, (int)region.Position2.Y);
                int maxY = Math.Max((int)region.Position1.Y, (int)region.Position2.Y) + 1;

                int minX = Math.Min((int)region.Position1.X, (int)region.Position2.X);
                int maxX = Math.Max((int)region.Position1.X, (int)region.Position2.X) + 1;

                int minZ = Math.Min((int)region.Position1.Z, (int)region.Position2.Z);
                int maxZ = Math.Max((int)region.Position1.Z, (int)region.Position2.Z) + 1;

                // Iterate over the range of blocks to wrap.
                for (int y = minY; y < maxY; y++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        for (int z = minZ; z < maxZ; z++)
                        {
                            Vector3 currentPosition = new Vector3(x, y, z);

                            // Check if the current block is within the thickness range.
                            bool isOuterLayer = false;

                            // Check all six directions up to the specified thickness.
                            for (int dx = -thickness; dx <= thickness; dx++)
                            {
                                for (int dy = -thickness; dy <= thickness; dy++)
                                {
                                    for (int dz = -thickness; dz <= thickness; dz++)
                                    {
                                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > thickness)
                                            continue; // Only check within the given thickness

                                        Vector3 neighbor = new Vector3(x + dx, y + dy, z + dz);

                                        if (GetBlockFromLocation(neighbor) == AirID ||
                                            !IsWithinRegion(neighbor, region.Position1, region.Position2))
                                        {
                                            isOuterLayer = true;
                                            break;
                                        }
                                    }
                                    if (isOuterLayer) break;
                                }
                                if (isOuterLayer) break;
                            }

                            // If it's not part of the outer layer, hollow it out.
                            if (!isOuterLayer)
                            {
                                regionBlocks.Add(currentPosition);
                            }
                        }
                    }
                }

                return regionBlocks;
            }, ct);
        }
        #endregion

        #region Shape Fill

        public static Task<HashSet<Vector3>> ShapeFill(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // This set will contain all the positions we end up filling.
                HashSet<Vector3> filledPositions = new HashSet<Vector3>();

                // Get the min and max bounds of the region (we add 1 to max so that our loops are inclusive of the full region).
                int minX = Math.Min((int)region.Position1.X, (int)region.Position2.X);
                int maxX = Math.Max((int)region.Position1.X, (int)region.Position2.X) + 1;
                int minY = Math.Min((int)region.Position1.Y, (int)region.Position2.Y);
                int maxY = Math.Max((int)region.Position1.Y, (int)region.Position2.Y) + 1;
                int minZ = Math.Min((int)region.Position1.Z, (int)region.Position2.Z);
                int maxZ = Math.Max((int)region.Position1.Z, (int)region.Position2.Z) + 1;

                // Flood-fill the exterior: any air block reachable from the boundaries is "outside" the closed object.
                HashSet<Vector3> exteriorAir = new HashSet<Vector3>();
                Queue<Vector3> queue = new Queue<Vector3>();

                // Helper to add a position to the queue if it's air and not already added.
                void TryEnqueue(Vector3 pos)
                {
                    if (!exteriorAir.Contains(pos) && GetBlockFromLocation(pos) == AirID)
                    {
                        exteriorAir.Add(pos);
                        queue.Enqueue(pos);
                    }
                }

                // Add all boundary positions (all positions on the faces of the region) to the queue.
                for (int x = minX; x < maxX; x++)
                {
                    for (int y = minY; y < maxY; y++)
                    {
                        TryEnqueue(new Vector3(x, y, minZ));
                        TryEnqueue(new Vector3(x, y, maxZ - 1));
                    }
                }
                for (int x = minX; x < maxX; x++)
                {
                    for (int z = minZ; z < maxZ; z++)
                    {
                        TryEnqueue(new Vector3(x, minY, z));
                        TryEnqueue(new Vector3(x, maxY - 1, z));
                    }
                }
                for (int y = minY; y < maxY; y++)
                {
                    for (int z = minZ; z < maxZ; z++)
                    {
                        TryEnqueue(new Vector3(minX, y, z));
                        TryEnqueue(new Vector3(maxX - 1, y, z));
                    }
                }

                // Define the 6 cardinal directions for flood fill.
                Vector3[] directions = new Vector3[]
                {
                    new Vector3(-1, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, -1, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(0, 0, -1),
                    new Vector3(0, 0, 1)
                };

                // Flood-fill all exterior air blocks.
                while (queue.Count > 0)
                {
                    Vector3 current = queue.Dequeue();
                    foreach (var dir in directions)
                    {
                        Vector3 neighbor = current + dir;
                        // Ensure neighbor is within the region bounds.
                        if (neighbor.X < minX || neighbor.X >= maxX ||
                            neighbor.Y < minY || neighbor.Y >= maxY ||
                            neighbor.Z < minZ || neighbor.Z >= maxZ)
                        {
                            continue;
                        }

                        TryEnqueue(neighbor);
                    }
                }

                // Now, any air block within the region that is NOT in exteriorAir is "inside" the closed object.
                for (int x = minX; x < maxX; x++)
                {
                    for (int y = minY; y < maxY; y++)
                    {
                        for (int z = minZ; z < maxZ; z++)
                        {
                            Vector3 pos = new Vector3(x, y, z);
                            if (GetBlockFromLocation(pos) == AirID && !exteriorAir.Contains(pos))
                            {
                                // Add the interior blocks for filling to the hashset.
                                filledPositions.Add(pos);
                            }
                        }
                    }
                }

                return filledPositions;
            }, ct);
        }
        #endregion

        #region Flood Fill

        public static Task<HashSet<Vector3>> FloodFill(Vector3 start, int maxBlocks = 10000, CancellationToken ct = default) // Use 10k blocks as the default max.
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> filledPositions = new HashSet<Vector3>();
                Queue<Vector3> queue = new Queue<Vector3>();

                int blockID = GetBlockFromLocation(start);
                if (blockID == AirID) return filledPositions; // Avoid filling air.

                queue.Enqueue(start);
                filledPositions.Add(start);

                // Define 6 cardinal directions for flood fill.
                Vector3[] directions = new Vector3[]
                {
                    new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
                    new Vector3(0, -1, 0), new Vector3(0, 1, 0),
                    new Vector3(0, 0, -1), new Vector3(0, 0, 1)
                };

                while (queue.Count > 0 && filledPositions.Count < maxBlocks)
                {
                    Vector3 current = queue.Dequeue();

                    foreach (var dir in directions)
                    {
                        Vector3 neighbor = current + dir;

                        // Enforce Y constraints (only flood-fill within Y: max height to min height).
                        if (neighbor.Y > WorldHeights.MaxY || neighbor.Y < WorldHeights.MinY) continue;

                        // Ensure block matches the block ID and hasn't been visited
                        if (GetBlockFromLocation(neighbor) == blockID && !filledPositions.Contains(neighbor))
                        {
                            filledPositions.Add(neighbor);
                            queue.Enqueue(neighbor);

                            if (filledPositions.Count >= maxBlocks) break;
                        }
                    }
                }

                return filledPositions;
            }, ct);
        }
        #endregion

        #region Wrap

        public static Task<HashSet<Vector3>> WrapObject(Region region, List<int> replaceBlockPattern, HashSet<Direction> wrapDirections = null, HashSet<Direction> excludeDirections = null, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> regionBlocks = new HashSet<Vector3>();

                int minY = Math.Min((int)region.Position1.Y, (int)region.Position2.Y);
                int maxY = Math.Max((int)region.Position1.Y, (int)region.Position2.Y) + 1;
                int minX = Math.Min((int)region.Position1.X, (int)region.Position2.X);
                int maxX = Math.Max((int)region.Position1.X, (int)region.Position2.X) + 1;
                int minZ = Math.Min((int)region.Position1.Z, (int)region.Position2.Z);
                int maxZ = Math.Max((int)region.Position1.Z, (int)region.Position2.Z) + 1;

                // Define offsets for each direction.
                var availableDirections = new Dictionary<Direction, Vector3>
                {
                    { Direction.posX, new Vector3(1, 0, 0) },
                    { Direction.negX, new Vector3(-1, 0, 0) },
                    { Direction.posZ, new Vector3(0, 0, 1) },
                    { Direction.negZ, new Vector3(0, 0, -1) },
                    { Direction.Up, new Vector3(0, 1, 0) },
                    { Direction.Down, new Vector3(0, -1, 0) }
                };

                // Determine which directions to use:
                // - If wrapDirections is null => all directions
                // - Else => only the specified ones
                List<KeyValuePair<Direction, Vector3>> directionsToApply =
                    (wrapDirections == null || wrapDirections.Count == 0)
                        ? availableDirections.ToList()
                        : availableDirections.Where(d => wrapDirections.Contains(d.Key)).ToList();

                // Apply excludes (if any).
                if (excludeDirections != null && excludeDirections.Count > 0)
                    directionsToApply = directionsToApply.Where(d => !excludeDirections.Contains(d.Key)).ToList();

                // Iterate over each block in the region.
                for (int y = minY; y < maxY; y++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        for (int z = minZ; z < maxZ; z++)
                        {
                            Vector3 currentBlockPos = new Vector3(x, y, z);
                            // First, check if the block is not air and is not one of the replacement block types.
                            int currentBlockType = GetBlockFromLocation(currentBlockPos);
                            bool shouldWrap = (currentBlockType != AirID) && (!replaceBlockPattern.Contains(currentBlockType));
                            if (shouldWrap)
                            {
                                // For each allowed direction, check the neighbor.
                                foreach (var dir in directionsToApply)
                                {
                                    Vector3 neighborPos = currentBlockPos + dir.Value;
                                    if (GetBlockFromLocation(neighborPos) == AirID)
                                    {
                                        regionBlocks.Add(neighborPos);
                                    }
                                }
                            }
                        }
                    }
                }
                return regionBlocks;
            }, ct);
        }
        #endregion

        #region Matrix

        public static Task<HashSet<Tuple<Vector3, int>>> MakeMatrix(Vector3 pos, int radius, int spacing, bool enableSnow, int[] optionalBlockPattern, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> maxtrixBlocks = new HashSet<Tuple<Vector3, int>>();

                // Calculate the spacing based on the clipboards size.
                int width = Convert.ToInt32(copiedRegion.Max(t => t.Item1.X) - copiedRegion.Min(t => t.Item1.X) + 1);
                int length = Convert.ToInt32(copiedRegion.Max(t => t.Item1.Z) - copiedRegion.Min(t => t.Item1.Z) + 1);
                int maxFace = Math.Max(width, length);
                spacing += maxFace;

                // Calculate the grid size based on the radius and spacing.
                int startX = Convert.ToInt32(pos.X) - (radius * 3) - spacing + 1;
                int endX = (radius * 3) + Convert.ToInt32(pos.X) + 1;
                int startZ = Convert.ToInt32(pos.Z) - (radius * 3) - spacing + 1;
                int endZ = (radius * 3) + Convert.ToInt32(pos.Z) + 1;

                // Find how far 'down' the copy goes, relative to its anchor.
                int minOffsetY = copiedRegion.Min(b => (int)b.Item1.Y);

                for (int x = startX; x < endX; x += spacing)
                {
                    for (int z = startZ; z < endZ; z += spacing)
                    {
                        // Adjust Y position based on terrain height if snow is enabled.
                        int baseY;
                        if (enableSnow)
                        {
                            int groundY = GetTerrainHeight(x, z); // Get the ground height for this point.
                            baseY = groundY - minOffsetY;         // Offset the copy up so its lowest block sits on ground.
                        }
                        else
                            baseY = Convert.ToInt32(pos.Y);

                        foreach (var blockData in copiedRegion)
                        {
                            // Parse block data.
                            int offsetX = (int)blockData.Item1.X;
                            int offsetY = (int)blockData.Item1.Y;
                            int offsetZ = (int)blockData.Item1.Z;
                            int blockId = blockData.Item2;

                            int finalY = baseY + offsetY;

                            // Calculate the final position.
                            Vector3 blockPosition = new Vector3(x + offsetX, finalY, z + offsetZ);

                            // Get the block id.
                            if (optionalBlockPattern.Length > 0)
                            {
                                // Overwrite block id value with a specified value.
                                blockId = GetRandomBlockFromPattern(optionalBlockPattern);
                            }

                            // Spawn the block at the calculated position.
                            maxtrixBlocks.Add(new Tuple<Vector3, int>(blockPosition, blockId));
                        }
                    }
                }

                return maxtrixBlocks;
            }, ct);
        }
        private static int GetTerrainHeight(int x, int z)
        {
            // Loop through each Y-level from top to bottom, checking for non-empty blocks (e.g., terrain).
            for (int y = WorldHeights.MaxY; y > (WorldHeights.MinY - 1); y--)
            {
                // Create a vector for the world coordinates at each y-level.
                Vector3 currentPosition = new Vector3(x, y, z);

                // Check if the block is not empty.
                if (GetBlockFromLocation(currentPosition) != AirID)
                {
                    // If it's not empty, set the base height of the tree.
                    return (y + 1);
                }
            }
            return WorldHeights.MinY; // Return the lowest world level.
        }
        #endregion

        #region Forest

        public static XnaPerlinNoise _noiseFunction;
        public static Task<HashSet<Tuple<Vector3, int>>> MakeForest(Region pos, int density, int max_height, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> forestBlocks = new HashSet<Tuple<Vector3, int>>();

                for (int i = 0; i < density; i++)
                {
                    // Generate random positions within the given region.
                    float RandomX = (float)GenerateRandomNumber((int)pos.Position1.X, (int)pos.Position2.X);
                    float RandomZ = (float)GenerateRandomNumber((int)pos.Position1.Z, (int)pos.Position2.Z);

                    // Merge the new tree blocks into the forestBlocks set.
                    forestBlocks.UnionWith(MakeTree((int)RandomX, (int)RandomZ, max_height));
                }

                return forestBlocks;
            }, ct);
        }

        public static Task<HashSet<Tuple<Vector3, int>>> MakeForest(Vector3 center, int radius, int density, int max_height, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> forestBlocks = new HashSet<Tuple<Vector3, int>>();

                if (radius < 0)
                    radius = Math.Abs(radius);

                if (radius == 0 || density <= 0)
                    return forestBlocks;

                int placedTrees = 0;
                int attempts = 0;

                // Prevent an endless loop if random picks keep colliding or landing on bad spots.
                int maxAttempts = Math.Max(density * 25, 100);

                while (placedTrees < density && attempts++ < maxAttempts)
                {
                    int randomX = GenerateRandomNumber((int)center.X - radius, (int)center.X + radius);
                    int randomZ = GenerateRandomNumber((int)center.Z - radius, (int)center.Z + radius);

                    int dx = randomX - (int)center.X;
                    int dz = randomZ - (int)center.Z;

                    // Keep the placement inside the circle, like /snow.
                    if ((dx * dx) + (dz * dz) > (radius * radius))
                        continue;

                    int beforeCount = forestBlocks.Count;

                    // Merge the new tree blocks into the forest set.
                    forestBlocks.UnionWith(MakeTree(randomX, randomZ, max_height));

                    // Only count it if something new was actually added.
                    if (forestBlocks.Count > beforeCount)
                        placedTrees++;
                }

                return forestBlocks;
            }, ct);
        }
        #endregion

        #region Tree

        public static HashSet<Tuple<Vector3, int>> MakeTree(int worldX, int worldZ, int maxHeight)
        {
            _noiseFunction = new XnaPerlinNoise(new Random(BlockPatternRng.NextIndex(int.MaxValue)));
            HashSet<Tuple<Vector3, int>> treeBlocks = new HashSet<Tuple<Vector3, int>>();

            try
            {
                // Start by assuming a height below the surface.
                float treeBaseHeight = WorldHeights.MinY;

                // Loop through each Y-level from 62 to -62, checking for non-empty blocks (e.g., terrain).
                for (int y = WorldHeights.MaxY; y > (WorldHeights.MinY - 1); y--)
                {
                    // Create a vector for the world coordinates at each y-level.
                    Vector3 currentPosition = new Vector3(worldX, y, worldZ);

                    // Check if the block is not empty.
                    if (GetBlockFromLocation(currentPosition) != AirID)
                    {
                        // If it's not empty, set the base height of the tree.
                        treeBaseHeight = y + 1f;
                        break;
                    }
                }

                // Set the final position where the tree should start.
                Vector3 treeTopPosition = new Vector3(worldX, (int)treeBaseHeight - 1, worldZ);

                // If the top block is not leaves, start building the tree.
                if (GetBlockFromLocation(treeTopPosition) != LeavesID)
                {
                    int trunkHeight = 0;
                    // Randomly select the height of the tree between 5 (stock) and maxHeight.
                    int treeHeight = (maxHeight <= 0)
                        ? GenerateRandomNumber(5, 8) // Stock-like fallback band.
                        : GenerateRandomNumber(5, Math.Max(5, maxHeight));

                    // Build the trunk of the tree by placing log blocks.
                    for (int i = 0; i < treeHeight; i++)
                    {
                        if ((int)treeBaseHeight + trunkHeight <= WorldHeights.MaxY)
                        {
                            Vector3 trunkPosition = new Vector3(worldX, (int)treeBaseHeight + trunkHeight, worldZ);

                            // Add block to the builder.
                            treeBlocks.Add(new Tuple<Vector3, int>(trunkPosition, LogID));
                        }
                        trunkHeight++;
                    }

                    // Generate foliage around the tree.
                    for (int xOffset = -3; xOffset <= 3; xOffset++)
                    {
                        for (int yOffset = -3; yOffset <= 3; yOffset++)
                        {
                            for (int zOffset = -3; zOffset <= 3; zOffset++)
                            {
                                // Create a position vector for foliage placement.
                                Vector3 foliagePosition = new Vector3(worldX + xOffset, (int)treeBaseHeight + yOffset + trunkHeight, worldZ + zOffset);

                                // Ensure the foliage is within the map height limit.
                                if ((float)foliagePosition.Y <= WorldHeights.MaxY)
                                {
                                    // Check if the block is empty or unassigned
                                    int blockType = GetBlockFromLocation(foliagePosition);
                                    if (blockType == 0 || blockType == 94) // Empty or NumberOfBlocks (null).
                                    {
                                        // Generate noise and distance-based checks to place foliage (leaves).
                                        float noiseValue = _noiseFunction.ComputeNoise(foliagePosition * 0.5f);
                                        float distanceValue = 1f - (float)Math.Sqrt((xOffset * xOffset + yOffset * yOffset + zOffset * zOffset)) / 3f;

                                        // If the noise + distance exceeds a threshold, place leaves.
                                        if (noiseValue + distanceValue > 0.25f)
                                        {
                                            // Add block to the builder.
                                            treeBlocks.Add(new Tuple<Vector3, int>(foliagePosition, LeavesID));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return treeBlocks;
            }
            catch (Exception)
            {
                return treeBlocks;
            }
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'Floor'
        /// 'Cube'
        /// 'Prism'
        /// 'Sphere'
        /// 'Pyramid'
        /// 'Cone'
        /// 'Cylinder'
        /// 'Diamond'
        /// 'Ring'
        /// 'Generate'
        ///
        /// </summary>
        #region Generation Methods

        #region Floor

        public static Task<HashSet<Vector3>> MakeFloor(Vector3 pos, int radius, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> floorBlocks = new HashSet<Vector3>();

                int halfradius = radius / 2; // Calculate half the radius for centering.

                for (int x = -halfradius; x < radius - halfradius; ++x)
                {
                    for (int z = -halfradius; z < radius - halfradius; ++z)
                    {
                        if (!hollow || x == -halfradius || x == radius - halfradius - 1 ||
                                       z == -halfradius || z == radius - halfradius - 1)
                        {
                            // If ignore block was specified, skip locations matching the block id.
                            Vector3 newPos = Vector3.Add(pos, new Vector3(x, 0, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                floorBlocks.Add(newPos);
                        }
                    }
                }

                return floorBlocks;
            }, ct);
        }
        #endregion

        #region Cube

        public static Task<HashSet<Vector3>> MakeCube(Vector3 pos, int radii, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> cubeBlocks = new HashSet<Vector3>();

                int halfradii = radii / 2; // Calculate half the radii for centering.

                for (int x = -halfradii; x < radii - halfradii; ++x)
                {
                    for (int y = 0; y < radii; ++y)
                    {
                        // Ensure Y is within the world's height constraints.
                        int worldY = (int)pos.Y + y; // Calculate actual world Y position.
                        if (worldY > WorldHeights.MaxY || worldY < WorldHeights.MinY)
                            continue;

                        for (int z = -halfradii; z < radii - halfradii; ++z)
                        {
                            if (!hollow || x == -halfradii || x == radii - halfradii - 1 ||
                                           y == 0 || y == radii - 1 ||
                                           z == -halfradii || z == radii - halfradii - 1)
                            {
                                // If ignore block was specified, skip locations matching the block id.
                                Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    cubeBlocks.Add(newPos);
                            }
                        }
                    }
                }

                return cubeBlocks;
            }, ct);
        }
        #endregion

        #region Prism

        public static Task<HashSet<Vector3>> MakeTriangularPrism(Vector3 pos, int length, int width, int height, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> blocks = new HashSet<Vector3>();

                // Decide whether to rotate the triangle:
                // When rotated, the triangle's cross-section is drawn on the Z-Y plane (base along Z) and extruded along X.
                bool rotate = length < width;

                // If height isn't specified, compute the equilateral triangle height.
                // In the non-rotated case, the base is 'width'; in the rotated case, the base is 'length'.
                if (height == -1)
                {
                    int baseLength = rotate ? length : width;
                    height = baseLength / 2;
                }

                // In both cases, Y is anchored at pos.Y (bottom) and goes upward to pos.Y + height.
                // The extrusion axis (Z in non-rotated, X in rotated) is centered on pos.
                if (!rotate)
                {
                    // Non-rotated: triangle drawn on X-Y, extruded along Z.
                    int halfWidth = width / 2;    // Half the triangle's base (X direction).
                    int halfExtrude = length / 2; // Half the extrusion along Z.

                    // Y: from pos.Y (base) up to pos.Y + height (apex).
                    for (int y = (int)pos.Y; y <= (int)pos.Y + height; y++)
                    {
                        // Determine progress up the triangle: 0 at base, 1 at apex.
                        double relative = (double)(y - pos.Y) / height;
                        // Allowed half-width shrinks linearly to 0 at the apex.
                        int currentHalfWidth = (int)Math.Round(halfWidth * (1 - relative));

                        // X: centered on pos.X.
                        for (int x = (int)pos.X - currentHalfWidth; x <= (int)pos.X + currentHalfWidth; x++)
                        {
                            // Z: extrusion axis, centered on pos.Z.
                            for (int z = (int)pos.Z - halfExtrude; z <= (int)pos.Z + halfExtrude; z++)
                            {
                                if (hollow)
                                {
                                    // Only include boundary blocks.
                                    bool isEdge = (y == pos.Y || y == pos.Y + height ||
                                                   x == pos.X - currentHalfWidth || x == pos.X + currentHalfWidth ||
                                                   z == pos.Z - halfExtrude || z == pos.Z + halfExtrude);
                                    if (!isEdge)
                                        continue;
                                }
                                if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                                    continue;
                                Vector3 newPos = new Vector3(x, y, z);
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    blocks.Add(newPos);
                            }
                        }
                    }
                }
                else
                {
                    // Rotated: triangle drawn on Z-Y, extruded along X.
                    int halfBase = length / 2;   // Half the triangle's base (Z direction).
                    int halfExtrude = width / 2; // Half the extrusion along X.

                    for (int y = (int)pos.Y; y <= (int)pos.Y + height; y++)
                    {
                        double relative = (double)(y - pos.Y) / height;
                        int currentHalfBase = (int)Math.Round(halfBase * (1 - relative));

                        // Z: triangle is drawn on the Z axis, centered on pos.Z.
                        for (int z = (int)pos.Z - currentHalfBase; z <= (int)pos.Z + currentHalfBase; z++)
                        {
                            // X: extrusion axis, centered on pos.X.
                            for (int x = (int)pos.X - halfExtrude; x <= (int)pos.X + halfExtrude; x++)
                            {
                                if (hollow)
                                {
                                    bool isEdge = (y == pos.Y || y == pos.Y + height ||
                                                   z == pos.Z - currentHalfBase || z == pos.Z + currentHalfBase ||
                                                   x == pos.X - halfExtrude || x == pos.X + halfExtrude);
                                    if (!isEdge)
                                        continue;
                                }
                                if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                                    continue;
                                Vector3 newPos = new Vector3(x, y, z);
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    blocks.Add(newPos);
                            }
                        }
                    }
                }

                return blocks;
            }, ct);
        }
        #endregion

        #region Sphere

        public static Task<HashSet<Vector3>> MakeSphere(Vector3 pos, double radiusX, double radiusY, double radiusZ, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> sphereBlocks = new HashSet<Vector3>();

                radiusX += 0.5;
                radiusY += 0.5;
                radiusZ += 0.5;

                double invRadiusX = 1 / radiusX;
                double invRadiusY = 1 / radiusY;
                double invRadiusZ = 1 / radiusZ;

                int ceilRadiusX = (int)Math.Ceiling(radiusX);
                int ceilRadiusY = (int)Math.Ceiling(radiusY);
                int ceilRadiusZ = (int)Math.Ceiling(radiusZ);

                // Shift pos.Y up so the sphere is fully above the original Y position.
                pos.Y += (float)radiusY;

                double nextXn = 0;
                for (int x = 0; x <= ceilRadiusX; ++x)
                {
                    double xn = nextXn;
                    nextXn = (x + 1) * invRadiusX;
                    double nextYn = 0;
                    for (int y = 0; y <= ceilRadiusY; ++y)
                    {
                        double yn = nextYn;
                        nextYn = (y + 1) * invRadiusY;
                        double nextZn = 0;

                        for (int z = 0; z <= ceilRadiusZ; ++z)
                        {
                            double zn = nextZn;
                            nextZn = (z + 1) * invRadiusZ;
                            double distanceSq = LengthSq(xn, yn, zn);
                            if (distanceSq > 1)
                            {
                                if (z == 0)
                                {
                                    if (y == 0)
                                    {
                                        break;
                                    }

                                    break;
                                }

                                break;
                            }

                            if (hollow)
                            {
                                if (LengthSq(nextXn, yn, zn) <= 1 && LengthSq(xn, nextYn, zn) <= 1 && LengthSq(xn, yn, nextZn) <= 1)
                                {
                                    continue;
                                }
                            }

                            // Ensure Y is within the world's height constraints.
                            // If ignore block was specified, skip locations matching the block id.
                            Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(x, -y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(x, y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, -y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(x, -y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, -y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                if (IsWithinWorldBounds(newPos, 1, 1)) sphereBlocks.Add(newPos);
                        }
                    }
                }

                return sphereBlocks;
            }, ct);
        }
        #endregion

        #region Pyramid

        public static Task<HashSet<Vector3>> MakePyramid(Vector3 pos, int size, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> pyramidBlocks = new HashSet<Vector3>();

                int height = size;
                for (int y = 0; y <= height; ++y)
                {
                    // Ensure Y is within the world's height constraints.
                    if (!IsWithinWorldBounds((int)(pos.Y + y))) continue;

                    size--;
                    for (int x = 0; x <= size; ++x)
                    {
                        for (int z = 0; z <= size; ++z)
                        {
                            if ((!hollow && z <= size && x <= size) || z == size || x == size)
                            {
                                // If ignore block was specified, skip locations matching the block id.
                                Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    pyramidBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(-x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    pyramidBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(x, y, -z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    pyramidBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(-x, y, -z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    pyramidBlocks.Add(newPos);
                            }
                        }
                    }
                }

                return pyramidBlocks;
            }, ct);
        }
        #endregion

        #region Cone

        public static Task<HashSet<Vector3>> MakeCone(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, double thickness, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> coneBlocks = new HashSet<Vector3>();

                int ceilRadiusX = (int)Math.Ceiling(radiusX);
                int ceilRadiusZ = (int)Math.Ceiling(radiusZ);

                double radiusXPow = Math.Pow(radiusX, 2);
                double radiusZPow = Math.Pow(radiusZ, 2);
                double heightPow = Math.Pow(height, 2);

                for (int y = 0; y < height; ++y)
                {
                    // Ensure Y is within the world's height constraints.
                    if (!IsWithinWorldBounds((int)(pos.Y + y))) continue;

                    double ySquaredMinusHeightOverHeightSquared = Math.Pow(y - height, 2) / heightPow;
                    for (int x = 0; x <= ceilRadiusX; ++x)
                    {
                        double xSquaredOverRadiusX = Math.Pow(x, 2) / radiusXPow;
                        for (int z = 0; z <= ceilRadiusZ; ++z)
                        {
                            double zSquaredOverRadiusZ = Math.Pow(z, 2) / radiusZPow;
                            double distanceFromOriginMinusHeightSquared = xSquaredOverRadiusX + zSquaredOverRadiusZ - ySquaredMinusHeightOverHeightSquared;
                            if (distanceFromOriginMinusHeightSquared > 1)
                            {
                                if (z == 0)
                                {
                                    break;
                                }

                                break;
                            }

                            if (hollow)
                            {
                                double xNext = Math.Pow(x + thickness, 2) / radiusXPow + zSquaredOverRadiusZ - ySquaredMinusHeightOverHeightSquared;
                                double yNext = xSquaredOverRadiusX + zSquaredOverRadiusZ - Math.Pow(y + thickness - height, 2) / heightPow;
                                double zNext = xSquaredOverRadiusX + Math.Pow(z + thickness, 2) / radiusZPow - ySquaredMinusHeightOverHeightSquared;
                                if (xNext <= 0 && zNext <= 0 && (yNext <= 0 && y + thickness != height))
                                {
                                    continue;
                                }
                            }

                            if (distanceFromOriginMinusHeightSquared <= 0)
                            {
                                // If ignore block was specified, skip locations matching the block id.
                                Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    coneBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(-x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    coneBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(x, y, -z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    coneBlocks.Add(newPos);

                                newPos = Vector3.Add(pos, new Vector3(-x, y, -z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    coneBlocks.Add(newPos);
                            }
                        }
                    }
                }

                return coneBlocks;
            }, ct);
        }
        #endregion

        #region Cylinder

        public static Task<HashSet<Vector3>> MakeCylinder(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> cylinderBlocks = new HashSet<Vector3>();

                radiusX += 0.5;
                radiusZ += 0.5;

                if (height == 0)
                {
                    return cylinderBlocks;
                }
                else if (height < 0)
                {
                    height = -height;
                    pos = Vector3.Subtract(pos, new Vector3(0, height, 0));
                }

                // Assuming world, getMinY, and getMaxY are accessible from your context
                // if (pos.Y < world.Y)
                // {
                //     pos.Y = world.Y;
                // }
                // else if (pos.Y + height - 1 > world.Y)
                // {
                //     height = (int)(world.Y - pos.Y + 1);
                // }

                double invRadiusX = 1 / radiusX;
                double invRadiusZ = 1 / radiusZ;

                int ceilRadiusX = (int)Math.Ceiling(radiusX);
                int ceilRadiusZ = (int)Math.Ceiling(radiusZ);

                double nextXn = 0;
                for (int x = 0; x <= ceilRadiusX; ++x)
                {
                    double xn = nextXn;
                    nextXn = (x + 1) * invRadiusX;
                    double nextZn = 0;
                    for (int z = 0; z <= ceilRadiusZ; ++z)
                    {
                        double zn = nextZn;
                        nextZn = (z + 1) * invRadiusZ;
                        double distanceSq = LengthSq(xn, zn);
                        if (distanceSq > 1)
                        {
                            if (z == 0)
                            {
                                break;
                            }

                            break;
                        }

                        if (hollow)
                        {
                            if (LengthSq(nextXn, zn) <= 1 && LengthSq(xn, nextZn) <= 1)
                            {
                                continue;
                            }
                        }

                        for (int y = 0; y < height; ++y)
                        {
                            // Ensure Y is within the world's height constraints.
                            if (!IsWithinWorldBounds((int)(pos.Y + y))) continue;

                            // If ignore block was specified, skip locations matching the block id.
                            Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                cylinderBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, y, z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                cylinderBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(x, y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                cylinderBlocks.Add(newPos);

                            newPos = Vector3.Add(pos, new Vector3(-x, y, -z));
                            if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                cylinderBlocks.Add(newPos);
                        }
                    }
                }

                return cylinderBlocks;
            }, ct);
        }
        #endregion

        #region Diamond

        public static Task<HashSet<Vector3>> MakeDiamond(Vector3 pos, int size, bool hollow, bool squared, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> diamondBlocks = new HashSet<Vector3>();
                int halfSize = size / 2;

                // Loop through Y levels to form the full diamond shape.
                // SPAWN_CENETER: for (int y = -halfSize; y <= halfSize; y++)
                for (int y = 0; y <= size; y++)
                {
                    // Ensure Y is within the world's height constraints.
                    if (!IsWithinWorldBounds((int)(pos.Y + y))) continue;

                    int layerSize = halfSize - Math.Abs(y - halfSize); // Determine how wide or round each layer is. // Use  '- halfSize' for above pos.Y.
                    int squaredRadius = layerSize * layerSize;
                    bool isTopOrBottom = (y == 0 || y == size);
                    // SPAWN_CENETER: bool isTopOrBottom = (y == -halfSize || y == halfSize);

                    for (int x = -layerSize; x <= layerSize; x++)
                    {
                        for (int z = -layerSize; z <= layerSize; z++)
                        {
                            bool insideShape = squared || (x * x + z * z <= squaredRadius);
                            bool isBorder = squared ? (Math.Abs(x) == layerSize || Math.Abs(z) == layerSize) : (x * x + z * z >= (layerSize - 1) * (layerSize - 1));

                            if (insideShape && (!hollow || isBorder || isTopOrBottom))
                            {
                                // If ignore block was specified, skip locations matching the block id.
                                Vector3 newPos = Vector3.Add(pos, new Vector3(x, y, z));
                                if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                                    diamondBlocks.Add(newPos);
                            }
                        }
                    }
                }

                return diamondBlocks;
            }, ct);
        }
        #endregion

        #region Ring

        public static Task<HashSet<Vector3>> MakeRing(Vector3 pos, double radius, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> ringBlocks = new HashSet<Vector3>();

                radius += 0.5; // Adjust radius for correct block placement.

                double invRadius = 1 / radius;
                int ceilRadius = (int)Math.Ceiling(radius);

                double nextXn = 0;
                for (int x = 0; x <= ceilRadius; ++x)
                {
                    double xn = nextXn;
                    nextXn = (x + 1) * invRadius;
                    double nextZn = 0;

                    for (int z = 0; z <= ceilRadius; ++z)
                    {
                        double zn = nextZn;
                        nextZn = (z + 1) * invRadius;

                        double distanceSq = LengthSq(xn, 0, zn); // Y is always 0.
                        if (distanceSq > 1)
                        {
                            if (z == 0) break;
                            break;
                        }

                        if (hollow)
                        {
                            if (LengthSq(nextXn, 0, zn) <= 1 && LengthSq(xn, 0, nextZn) <= 1)
                            {
                                continue;
                            }
                        }

                        // If ignore block was specified, skip locations matching the block id.
                        // Place blocks only on the Y = pos.Y plane.
                        Vector3 newPos = Vector3.Add(pos, new Vector3(x, 0, z));
                        if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                            ringBlocks.Add(newPos);

                        newPos = Vector3.Add(pos, new Vector3(-x, 0, z));
                        if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                            ringBlocks.Add(newPos);

                        newPos = Vector3.Add(pos, new Vector3(x, 0, -z));
                        if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                            ringBlocks.Add(newPos);

                        newPos = Vector3.Add(pos, new Vector3(-x, 0, -z));
                        if (ignoreBlock == -1 || GetBlockFromLocation(newPos) != ignoreBlock)
                            ringBlocks.Add(newPos);
                    }
                }

                return ringBlocks;
            }, ct);
        }
        #endregion

        #region Generate

        public static Task<HashSet<Tuple<Vector3, int>>> MakeShape(Region region, string expression, bool hollow, int ignoreBlock = -1, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // This dictionary holds positions and data values for blocks that satisfy the condition.
                Dictionary<Vector3, int> fullShape = new Dictionary<Vector3, int>();

                // Split the expression if a semicolon is present.
                // The left part (optionally starting with "data=") is the data expression,
                // and the right part is the condition expression.
                string dataExpression = null;
                string conditionExpression = expression;

                if (expression.Contains(";"))
                {
                    string[] parts = expression.Split(new char[] { ';' }, 2);
                    dataExpression = parts[0].Trim();
                    conditionExpression = parts[1].Trim();

                    if (dataExpression.StartsWith("data=", StringComparison.OrdinalIgnoreCase))
                    {
                        dataExpression = dataExpression.Substring("data=".Length).Trim();
                    }
                }

                // Create evaluators for the condition and (optionally) the data expression.
                ExpressionEvaluator condEvaluator = new ExpressionEvaluator(conditionExpression);
                ExpressionEvaluator dataEvaluator = dataExpression != null ? new ExpressionEvaluator(dataExpression) : null;

                // Use the region's bounding box.
                int minX = (int)region.Position1.X;
                int maxX = (int)region.Position2.X;
                int minY = (int)region.Position1.Y;
                int maxY = (int)region.Position2.Y;
                int minZ = (int)region.Position1.Z;
                int maxZ = (int)region.Position2.Z;

                // Compute the region center for relative coordinate evaluation.
                Vector3 center = region.Center;

                // First pass: evaluate all points in the region.
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        // Ensure y is within world bounds.
                        if (y > WorldHeights.MaxY || y < WorldHeights.MinY)
                            continue;

                        for (int z = minZ; z <= maxZ; z++)
                        {
                            // Convert absolute coordinates to relative ones (centered around 0,0,0).
                            int relX = x - (int)center.X;
                            int relY = y - (int)center.Y;
                            int relZ = z - (int)center.Z;

                            double result = condEvaluator.Parse(relX, relY, relZ);
                            bool placeBlock = result > 0;

                            if (placeBlock)
                            {
                                int dataValue = -1;
                                if (dataEvaluator != null)
                                {
                                    dataValue = (int)dataEvaluator.Parse(relX, relY, relZ);
                                }

                                Vector3 pos = new Vector3(x, y, z);
                                if (ignoreBlock == -1 || GetBlockFromLocation(pos) != ignoreBlock)
                                {
                                    fullShape[pos] = dataValue;
                                }
                            }
                        }
                    }
                }

                // If not hollow, return the full shape.
                if (!hollow)
                {
                    // Convert dictionary to hash set of tuples.
                    return new HashSet<Tuple<Vector3, int>>(fullShape.Select(kvp => new Tuple<Vector3, int>(kvp.Key, kvp.Value)));
                }

                // For hollow shapes, keep only blocks that have at least one neighbor missing.
                // Define neighbor offsets (6-adjacency; you can extend to 26-adjacency if needed).
                Vector3[] offsets = new Vector3[]
                {
                    new Vector3(1,0,0),
                    new Vector3(-1,0,0),
                    new Vector3(0,1,0),
                    new Vector3(0,-1,0),
                    new Vector3(0,0,1),
                    new Vector3(0,0,-1)
                };

                HashSet<Tuple<Vector3, int>> hollowShape = new HashSet<Tuple<Vector3, int>>();

                foreach (var kvp in fullShape)
                {
                    Vector3 pos = kvp.Key;
                    bool isBoundary = false;
                    foreach (var offset in offsets)
                    {
                        Vector3 neighbor = pos + offset;
                        if (!fullShape.ContainsKey(neighbor))
                        {
                            isBoundary = true;
                            break;
                        }
                    }
                    if (isBoundary)
                    {
                        hollowShape.Add(new Tuple<Vector3, int>(pos, kvp.Value));
                    }
                }

                return hollowShape;
            }, ct);
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'Schematics'
        /// 'Copy'
        /// 'Cut'
        /// 'Paste'
        /// 'Rotate'
        /// 'Flip'
        /// 'ClearClipboard'
        ///
        /// </summary>
        #region Schematic and Clipboard Methods

        #region Schematics

        private static readonly byte WE_SAVE_VERSION = 0x3;
        private static readonly byte[] WE_SAVE_HEADER = Encoding.UTF8.GetBytes("WES");
        public static Task SaveSchematic(HashSet<Tuple<Vector3, int>> clipboard, FileInfo schemPath, bool overwriteFile = false, bool saveAir = false, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (clipboard.Count() <= 0)
                {
                    SendFeedback("Your clipboard is empty.");
                    return;
                }

                if (schemPath.Directory != null && !schemPath.Directory.Exists)
                    schemPath.Directory.Create();

                if (schemPath.Exists)
                {
                    if (overwriteFile)
                        File.Delete(schemPath.FullName); // Delete the file and overwrite it.
                    else
                    {
                        SendFeedback("A save with that name already exists.");
                        return;
                    }
                }

                long len = 0;
                using (FileStream stream = schemPath.Create())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(WE_SAVE_HEADER);
                    writer.Write(WE_SAVE_VERSION);

                    // v2+: Store the copy anchor so paste aligns the same way /copy does.
                    writer.Write(CopyAnchorOffset.X);
                    writer.Write(CopyAnchorOffset.Y);
                    writer.Write(CopyAnchorOffset.Z);

                    // Serialize the data manually using BinaryWriter.
                    // v3+: explicit block count so we can append additional sections later (crates, etc).
                    var blocksToWrite = new List<Tuple<Vector3, int>>();
                    foreach (var item in clipboard)
                    {
                        // Check to save air. If not, skip saving air blocks.
                        if (!saveAir && item.Item2 == AirID) continue;

                        blocksToWrite.Add(item);
                    }

                    writer.Write(blocksToWrite.Count);

                    // Serialize blocks.
                    foreach (var item in blocksToWrite)
                    {
                        writer.Write(item.Item1.X);
                        writer.Write(item.Item1.Y);
                        writer.Write(item.Item1.Z);
                        writer.Write(item.Item2);
                    }

                    // Serialize crate payloads (sidecar).
                    var containerPositions = new HashSet<Vector3>();
                    foreach (var item in blocksToWrite)
                    {
                        var bt = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)item.Item2;
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(bt))
                            containerPositions.Add(item.Item1);
                    }

                    var cratesToWrite = new List<ClipboardCrate>();
                    foreach (var cc in copiedCrates)
                    {
                        if (containerPositions.Contains(cc.RelativePos))
                            cratesToWrite.Add(cc);
                    }

                    writer.Write(cratesToWrite.Count);

                    foreach (var cc in cratesToWrite)
                    {
                        writer.Write(cc.RelativePos.X);
                        writer.Write(cc.RelativePos.Y);
                        writer.Write(cc.RelativePos.Z);

                        byte[] payload = cc.Payload ?? Array.Empty<byte>();
                        writer.Write(payload.Length);
                        if (payload.Length > 0)
                            writer.Write(payload);
                    }

                    len = stream.Length;
                    stream.Flush();
                }

                SendFeedback($"Created save called {schemPath.Name} successfully. (size is {len / 1024} KB)");
            }, ct);
        }

        public static Task LoadSchematic(FileInfo schemPath, bool loadAir = false, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!schemPath.Exists)
                {
                    SendFeedback("Save does not exist.");
                    return;
                }

                using (FileStream stream = schemPath.OpenRead())
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    byte[] header = reader.ReadBytes(3);
                    if (!UnsafeCompare(header, WE_SAVE_HEADER))
                    {
                        SendFeedback("Not a valid save file.");
                        return;
                    }

                    int version = reader.ReadByte();
                    if (version > WE_SAVE_VERSION)
                    {
                        SendFeedback($"Invalid save version. (file is version 0x{version:X}, you have 0x{WE_SAVE_VERSION:X})");
                        SendFeedback($"Your save WILL still work; however, it must be done with a different version.");
                        return;
                    }

                    CopyAnchorOffset = Vector3.Zero; // Clear existing copy offset.
                    copiedRegion.Clear();            // Clear existing region.

                    // v2+: Read anchor (3 floats) before reading block records.
                    if (version >= 0x2)
                    {
                        float ox = reader.ReadSingle();
                        float oy = reader.ReadSingle();
                        float oz = reader.ReadSingle();
                        CopyAnchorOffset = new Vector3(ox, oy, oz);
                    }
                    else
                    {
                        // v1 files have no anchor; they will paste from the min corner.
                        CopyAnchorOffset = Vector3.Zero;
                    }

                    // Deserialize data.
                    copiedCrates.Clear();

                    if (version >= 0x3)
                    {
                        int blockCount = reader.ReadInt32();
                        for (int i = 0; i < blockCount; i++)
                        {
                            var x = reader.ReadSingle();
                            var y = reader.ReadSingle();
                            var z = reader.ReadSingle();
                            var value = reader.ReadInt32();

                            if (!loadAir && value == AirID) continue;

                            copiedRegion.Add(new Tuple<Vector3, int>(new Vector3(x, y, z), value));
                        }

                        int crateCount = reader.ReadInt32();
                        for (int i = 0; i < crateCount; i++)
                        {
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            int len = reader.ReadInt32();

                            // Sanity: crate payloads should be small (typically a few hundred bytes).
                            if (len < 0 || len > 1024 * 1024)
                                break;

                            byte[] payload = (len > 0) ? reader.ReadBytes(len) : Array.Empty<byte>();
                            copiedCrates.Add(new ClipboardCrate(new Vector3(x, y, z), payload));
                        }
                    }
                    else
                    {
                        while (reader.BaseStream.Position < reader.BaseStream.Length)
                        {
                            var x = reader.ReadSingle();
                            var y = reader.ReadSingle();
                            var z = reader.ReadSingle();
                            var value = reader.ReadInt32();

                            // Check to load air. If not, skip loading air blocks.
                            if (!loadAir && value == AirID) continue;

                            copiedRegion.Add(new Tuple<Vector3, int>(new Vector3(x, y, z), value));
                        }
                    }
                }
                SendFeedback($"Loaded save {schemPath.Name} from file. ({copiedRegion.Count} blocks)");
            }, ct);
        }

        #region Schematic Helper

        // Distributed under the MIT/X11 software license.
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        static unsafe bool UnsafeCompare(byte[] a1, byte[] a2)
        {
            if (a1 == a2) return true;
            if (a1 == null || a2 == null || a1.Length != a2.Length)
                return false;
            fixed (byte* p1 = a1, p2 = a2)
            {
                byte* x1 = p1, x2 = p2;
                int l = a1.Length;
                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                    if (*((long*)x1) != *((long*)x2)) return false;
                if ((l & 4) != 0) { if (*((int*)x1) != *((int*)x2)) return false; x1 += 4; x2 += 4; }
                if ((l & 2) != 0) { if (*((short*)x1) != *((short*)x2)) return false; x1 += 2; x2 += 2; }
                if ((l & 1) != 0) if (*((byte*)x1) != *((byte*)x2)) return false;
                return true;
            }
        }
        #endregion

        #endregion

        #region Copy

        public static Task CopyRegion(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Record the users position within the region. This is used to offset the paste.
                CopyAnchorOffset = GetUsersLocation() - region.Position1;

                // Clear existing region.
                copiedRegion.Clear();
                copiedCrates.Clear();

                // Get the smallest X value.
                int[] xData = { (int)region.Position1.X, (int)region.Position2.X };
                int[] yData = { (int)region.Position1.Y, (int)region.Position2.Y };
                int[] zData = { (int)region.Position1.Z, (int)region.Position2.Z };

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            // Get block from position.
                            Vector3 redactedLocation = new Vector3(x - xData.Min(), y - yData.Min(), z - zData.Min());

                            // Save copied block from location.
                            copiedRegion.Add(new Tuple<Vector3, int>(redactedLocation, GetBlockFromLocation(new Vector3(x, y, z))));

                            // SendFeedback(" {0} : {1}", location, GetBlockFromLocation(location));
                        }
                    }
                }

                // Sidecar: Copy crate inventories that exist inside this region.
                CaptureCratesInBounds(xData.Min(), yData.Min(), zData.Min(), xData.Max(), yData.Max(), zData.Max());
            }, ct);
        }

        public static Task CopyChunk(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Clear existing region.
                copiedChunk.Clear();
                copiedCrates.Clear();

                // Get the smallest X value.
                int[] xData = { (int)region.Position1.X, (int)region.Position2.X };
                int[] yData = { (int)region.Position1.Y, (int)region.Position2.Y };
                int[] zData = { (int)region.Position1.Z, (int)region.Position2.Z };

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            // Get block from position.
                            Vector3 redactedLocation = new Vector3(x - xData.Min(), y - yData.Min(), z - zData.Min());

                            // Save copied block from location.
                            copiedChunk.Add(new Tuple<Vector3, int>(redactedLocation, GetBlockFromLocation(new Vector3(x, y, z))));
                        }
                    }
                }

                // Sidecar: Copy crate inventories that exist inside this region.
                CaptureCratesInBounds(xData.Min(), yData.Min(), zData.Min(), xData.Max(), yData.Max(), zData.Max());
            }, ct);
        }

        public static Task CopySchematic(HashSet<Tuple<Vector3, int>> schematic, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Treat this as a "fresh" copy from an external source:
                // - Anchor is unknown, so reset it.
                // - Clipboard is cleared.
                CopyAnchorOffset = Vector3.Zero;
                copiedRegion.Clear();
                copiedCrates.Clear();

                // Find the minimum corner so the clipboard is origin-based (0,0,0), same as CopyRegion.
                int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
                foreach (var t in schematic)
                {
                    var p = t.Item1;
                    int x = (int)p.X, y = (int)p.Y, z = (int)p.Z;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (z < minZ) minZ = z;
                }

                // Normalize and copy into the game clipboard.
                foreach (var t in schematic)
                {
                    var p = t.Item1;
                    int block = t.Item2;

                    // Normalize to the min corner (exactly how CopyRegion does it).
                    var redacted = new Vector3(
                        (int)p.X - minX,
                        (int)p.Y - minY,
                        (int)p.Z - minZ
                    );

                    // Save copied block from location.
                    copiedRegion.Add(Tuple.Create(redacted, block));
                }
            }, ct);
        }
        #endregion

        #region Cut

        public static Task CutRegion(Region region, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Clear existing region.
                copiedRegion.Clear();

                // Get the smallest X value.
                int[] xData = { (int)region.Position1.X, (int)region.Position2.X };
                int[] yData = { (int)region.Position1.Y, (int)region.Position2.Y };
                int[] zData = { (int)region.Position1.Z, (int)region.Position2.Z };

                for (int y = (int)region.Position1.Y; y <= (int)region.Position2.Y; ++y)
                {
                    for (int x = (int)region.Position1.X; x <= (int)region.Position2.X; ++x)
                    {
                        for (int z = (int)region.Position1.Z; z <= (int)region.Position2.Z; ++z)
                        {
                            // Get block from position.
                            Vector3 redactedLocation = new Vector3(x - xData.Min(), y - yData.Min(), z - zData.Min());

                            // Save copied block from location.
                            copiedRegion.Add(new Tuple<Vector3, int>(redactedLocation, GetBlockFromLocation(new Vector3(x, y, z))));

                            // Remove all existing valid blocks.
                            if (GetBlockFromLocation(redactedLocation) != AirID)
                                AsyncBlockPlacer.Enqueue(redactedLocation, AirID);
                        }
                    }
                }
            }, ct);
        }
        #endregion

        #region Paste

        public static Task<HashSet<Tuple<Vector3, int>>> PasteRegion(Vector3 location, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Tuple<Vector3, int>> pasteBlocks = new HashSet<Tuple<Vector3, int>>();

                foreach (var tuple in copiedRegion)
                {
                    // Get block location from saved data and add it to the current position.
                    Vector3 blockLocation = Vector3.Add(location, tuple.Item1);

                    // Get block ID from saved data.
                    int block = tuple.Item2;

                    // Save copied block from location.
                    pasteBlocks.Add(new Tuple<Vector3, int>(blockLocation, block));
                }

                return pasteBlocks;
            }, ct);
        }
        #endregion

        #region Rotate

        public static Task RotateClipboard(int rotateX, int rotateY, int rotateZ, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Rotate around the clipboard anchor (where the player stood during /copy),
                // not around the geometric center.
                Vector3 anchor = CopyAnchorOffset;

                var rotatedRegion = new HashSet<Tuple<Vector3, int>>();
                var rotatedCrates = new List<ClipboardCrate>();

                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;

                foreach (var item in copiedRegion)
                {
                    Vector3 rotatedVector = RotateVectorGrid(item.Item1 - anchor, rotateX, rotateY, rotateZ) + anchor;

                    minX = Math.Min(minX, rotatedVector.X);
                    minY = Math.Min(minY, rotatedVector.Y);
                    minZ = Math.Min(minZ, rotatedVector.Z);

                    rotatedRegion.Add(new Tuple<Vector3, int>(rotatedVector, item.Item2));
                }

                foreach (var cc in copiedCrates)
                {
                    Vector3 rotatedVector = RotateVectorGrid(cc.RelativePos - anchor, rotateX, rotateY, rotateZ) + anchor;

                    minX = Math.Min(minX, rotatedVector.X);
                    minY = Math.Min(minY, rotatedVector.Y);
                    minZ = Math.Min(minZ, rotatedVector.Z);

                    rotatedCrates.Add(new ClipboardCrate(rotatedVector, cc.Payload));
                }

                Vector3 offset = new Vector3(-minX, -minY, -minZ);

                var alignedRegion = new HashSet<Tuple<Vector3, int>>();
                foreach (var item in rotatedRegion)
                    alignedRegion.Add(new Tuple<Vector3, int>(item.Item1 + offset, item.Item2));

                var alignedCrates = new List<ClipboardCrate>(rotatedCrates.Count);
                foreach (var cc in rotatedCrates)
                    alignedCrates.Add(new ClipboardCrate(cc.RelativePos + offset, cc.Payload));

                copiedRegion = alignedRegion;
                copiedCrates = alignedCrates;

                // The anchor also moves when we normalize the rotated clipboard back to min=(0,0,0).
                CopyAnchorOffset = anchor + offset;
            }, ct);
        }

        #region Rotational Math Helpers

        private static Vector3 RotateVectorGrid(Vector3 vector, int rotateX, int rotateY, int rotateZ)
        {
            vector = RotateVector(vector, rotateX, rotateY, rotateZ);

            return new Vector3(
                (float)Math.Round(vector.X),
                (float)Math.Round(vector.Y),
                (float)Math.Round(vector.Z));
        }

        public static Vector3 RotateVector(Vector3 vector, int rotateX, int rotateY, int rotateZ)
        {
            // Rotate the vector around the X axis.
            vector = RotateAroundX(vector, rotateX);

            // Rotate the vector around the Y axis.
            vector = RotateAroundY(vector, rotateY);

            // Rotate the vector around the Z axis.
            vector = RotateAroundZ(vector, rotateZ);

            return vector;
        }

        // Rotate around the X axis.
        public static Vector3 RotateAroundX(Vector3 vector, int angle)
        {
            if (angle == 0) return vector;

            float radians = (float)(Math.PI * angle / 180);
            float y = vector.Y;
            float z = vector.Z;
            vector.Y = (float)(y * Math.Cos(radians) - z * Math.Sin(radians));
            vector.Z = (float)(y * Math.Sin(radians) + z * Math.Cos(radians));

            return vector;
        }

        // Rotate around the Y axis.
        public static Vector3 RotateAroundY(Vector3 vector, int angle)
        {
            if (angle == 0) return vector;

            float radians = (float)(Math.PI * angle / 180);
            float x = vector.X;
            float z = vector.Z;
            vector.X = (float)(x * Math.Cos(radians) + z * Math.Sin(radians));
            vector.Z = (float)(-x * Math.Sin(radians) + z * Math.Cos(radians));

            return vector;
        }

        // Rotate around the Z axis.
        public static Vector3 RotateAroundZ(Vector3 vector, int angle)
        {
            if (angle == 0) return vector;

            float radians = (float)(Math.PI * angle / 180);
            float x = vector.X;
            float y = vector.Y;
            vector.X = (float)(x * Math.Cos(radians) - y * Math.Sin(radians));
            vector.Y = (float)(x * Math.Sin(radians) + y * Math.Cos(radians));

            return vector;
        }
        #endregion

        #endregion

        #region Flip

        public static Task FlipClipboard(Direction direction, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                // Find the center of the copied region.
                Vector3 center = GetRegionCenter();

                var flippedRegion = new HashSet<Tuple<Vector3, int>>();
                var flippedCrates = new List<ClipboardCrate>();

                // Variables to track the min and max bounds of the flipped region.
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

                foreach (var item in copiedRegion)
                {
                    // Flip the vector.
                    Vector3 flippedVector = FlipVector(item.Item1, center, direction);

                    // Update the bounding box for the flipped region.
                    minX = Math.Min(minX, flippedVector.X);
                    minY = Math.Min(minY, flippedVector.Y);
                    minZ = Math.Min(minZ, flippedVector.Z);
                    maxX = Math.Max(maxX, flippedVector.X);
                    maxY = Math.Max(maxY, flippedVector.Y);
                    maxZ = Math.Max(maxZ, flippedVector.Z);

                    flippedRegion.Add(new Tuple<Vector3, int>(flippedVector, item.Item2));
                }

                // Flip crates (sidecar) using the same transform.
                foreach (var cc in copiedCrates)
                {
                    Vector3 flippedVector = FlipVector(cc.RelativePos, center, direction);

                    // Update the bounding box for the flipped data.
                    minX = Math.Min(minX, flippedVector.X);
                    minY = Math.Min(minY, flippedVector.Y);
                    minZ = Math.Min(minZ, flippedVector.Z);
                    maxX = Math.Max(maxX, flippedVector.X);
                    maxY = Math.Max(maxY, flippedVector.Y);
                    maxZ = Math.Max(maxZ, flippedVector.Z);

                    flippedCrates.Add(new ClipboardCrate(flippedVector, cc.Payload));
                }

                // Find the corner of the flipped region (min values).
                Vector3 flippedCorner = new Vector3(minX, minY, minZ);

                // Calculate the offset to align the corner of the flipped region with the target paste location (0, 0, 0).
                Vector3 offset = -flippedCorner;

                // Apply the offset to all flipped vectors.
                var alignedRegion = new HashSet<Tuple<Vector3, int>>();
                foreach (var item in flippedRegion)
                {
                    Vector3 alignedVector = item.Item1 + offset;
                    alignedRegion.Add(new Tuple<Vector3, int>(alignedVector, item.Item2));
                }

                // Align crates with the same offset.
                var alignedCrates = new List<ClipboardCrate>(flippedCrates.Count);
                foreach (var cc in flippedCrates)
                {
                    Vector3 alignedVector = cc.RelativePos + offset;
                    alignedCrates.Add(new ClipboardCrate(alignedVector, cc.Payload));
                }

                // Replace the original region with the aligned flipped region.
                copiedRegion = alignedRegion;
                copiedCrates = alignedCrates;
            }, ct);
        }

        #region Flip Math Helpers

        public static Vector3 FlipVector(Vector3 vector, Vector3 center, Direction direction)
        {
            switch (direction)
            {
                case Direction.posX:
                case Direction.negX:
                    return new Vector3(2 * center.X - vector.X, vector.Y, vector.Z);

                case Direction.posZ:
                case Direction.negZ:
                    return new Vector3(vector.X, vector.Y, 2 * center.Z - vector.Z);

                case Direction.Up:
                case Direction.Down:
                    return new Vector3(vector.X, 2 * center.Y - vector.Y, vector.Z);

                default:
                    return vector;
            }
        }
        #endregion

        #endregion

        #region ClearClipboard

        public static void ClearClipboard()
        {
            copiedRegion.Clear();
            copiedStackRegion.Clear();
            copiedChunk.Clear();
            copiedCrates.Clear();
        }
        #endregion

        #endregion

        /// <summary>
        ///
        /// 'FillHole'
        /// 'Drain'
        /// 'Snow'
        ///
        /// </summary>
        #region Utility Methods

        #region Fill Hole

        public static Task<HashSet<Vector3>> FillHole(Vector3 start, int radius, int depth = 1, Direction direction = Direction.Down, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> filledPositions = new HashSet<Vector3>();
                HashSet<Vector3> visited = new HashSet<Vector3>();
                Queue<Vector3> queue = new Queue<Vector3>();

                if (!CanVisitFillPosition(start, start, radius, depth, direction))
                    return filledPositions;

                Vector3 forward = GetDirectionalUnitOffset(direction);
                Vector3[] lateralDirections = GetPerpendicularDirections(direction);

                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    Vector3 current = queue.Dequeue();
                    filledPositions.Add(current);

                    Vector3 forwardNeighbor = current + forward;
                    if (!visited.Contains(forwardNeighbor) && CanVisitFillPosition(start, forwardNeighbor, radius, depth, direction))
                    {
                        visited.Add(forwardNeighbor);
                        queue.Enqueue(forwardNeighbor);
                    }

                    if (GetDirectionalDistance(start, current, direction) == 0)
                    {
                        foreach (Vector3 lateral in lateralDirections)
                        {
                            Vector3 neighbor = current + lateral;
                            if (visited.Contains(neighbor))
                                continue;

                            if (CanVisitFillPosition(start, neighbor, radius, depth, direction))
                            {
                                visited.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }

                return filledPositions;
            }, ct);
        }

        public static Task<HashSet<Vector3>> FillHoleRecursive(Vector3 start, int radius, int depth = int.MaxValue, Direction direction = Direction.Down, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> filledPositions = new HashSet<Vector3>();
                Queue<Vector3> queue = new Queue<Vector3>();

                if (!CanVisitFillPosition(start, start, radius, depth, direction))
                    return filledPositions;

                Vector3[] directions = new Vector3[]
                {
                    new Vector3(-1, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, -1, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(0, 0, -1),
                    new Vector3(0, 0, 1)
                };

                queue.Enqueue(start);
                filledPositions.Add(start);

                while (queue.Count > 0)
                {
                    Vector3 current = queue.Dequeue();

                    foreach (Vector3 dir in directions)
                    {
                        Vector3 neighbor = current + dir;

                        if (filledPositions.Contains(neighbor))
                            continue;

                        if (CanVisitFillPosition(start, neighbor, radius, depth, direction))
                        {
                            filledPositions.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                return filledPositions;
            }, ct);
        }
        #endregion

        #region Drain

        public static Task<HashSet<Vector3>> Drain(Vector3 origin, int radius, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> drainedPositions = new HashSet<Vector3>();
                Queue<Vector3> queue = new Queue<Vector3>();

                Vector3[] directions = new Vector3[]
                {
                    new Vector3(-1, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, -1, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(0, 0, -1),
                    new Vector3(0, 0, 1)
                };

                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            Vector3 seed = new Vector3(origin.X + x, origin.Y + y, origin.Z + z);

                            if (!IsWithinWorldHeight(seed))
                                continue;

                            if (!IsWithinSphericalRadius(origin, seed, radius))
                                continue;

                            if (!IsLiquidBlock(GetBlockFromLocation(seed)))
                                continue;

                            if (drainedPositions.Add(seed))
                                queue.Enqueue(seed);
                        }
                    }
                }

                while (queue.Count > 0)
                {
                    Vector3 current = queue.Dequeue();

                    foreach (Vector3 dir in directions)
                    {
                        Vector3 neighbor = current + dir;

                        if (drainedPositions.Contains(neighbor))
                            continue;

                        if (!IsWithinWorldHeight(neighbor))
                            continue;

                        if (!IsWithinSphericalRadius(origin, neighbor, radius))
                            continue;

                        if (!IsLiquidBlock(GetBlockFromLocation(neighbor)))
                            continue;

                        drainedPositions.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                return drainedPositions;
            }, ct);
        }
        #endregion

        #region Snow

        /// <summary>
        /// Finds the first solid block in each X/Z column within a circular radius around a center point and returns the snow placement positions.
        /// </summary>
        public static Task<HashSet<Vector3>> MakeSnow(Vector3 center, int radius, bool replaceSurface = false, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> snowBlocks = new HashSet<Vector3>();

                for (int x = (int)center.X - radius; x <= (int)center.X + radius; ++x)
                {
                    for (int z = (int)center.Z - radius; z <= (int)center.Z + radius; ++z)
                    {
                        // Ensure the point is within the circular radius.
                        if ((x - (int)center.X) * (x - (int)center.X) + (z - (int)center.Z) * (z - (int)center.Z) <= radius * radius)
                        {
                            // Loop through each Y-level from 62 to -62, checking for non-empty blocks (e.g., terrain).
                            for (int y = WorldHeights.MaxY; y > WorldHeights.MinY - 1; y--)
                            {
                                // Check if the block is not empty.
                                if (GetBlockFromLocation(new Vector3(x, y, z)) != AirID)
                                {
                                    // If replace mode is enabled, don't offset upwards.
                                    if (replaceSurface)
                                        snowBlocks.Add(new Vector3(x, y, z));
                                    else
                                        snowBlocks.Add(new Vector3(x, y + 1f, z));
                                    break;
                                }
                            }
                        }
                    }
                }

                return snowBlocks;
            }, ct);
        }

        /// <summary>
        /// Finds the first solid block in each X/Z column within a region and returns the snow placement positions.
        /// Optional behavior allows Y scanning to begin at the world's max Y instead of the region's max Y.
        /// </summary>
        public static Task<HashSet<Vector3>> MakeSnow(Region region, bool replaceSurface = false, bool useWorldY = false, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                HashSet<Vector3> snowBlocks = new HashSet<Vector3>();

                // Use the exact region bounds for X, Y, and Z.
                int minX = (int)Math.Min(region.Position1.X, region.Position2.X);
                int maxX = (int)Math.Max(region.Position1.X, region.Position2.X);
                int minZ = (int)Math.Min(region.Position1.Z, region.Position2.Z);
                int maxZ = (int)Math.Max(region.Position1.Z, region.Position2.Z);
                int minY = (int)Math.Min(region.Position1.Y, region.Position2.Y);
                int maxY = (int)Math.Max(region.Position1.Y, region.Position2.Y);

                // Start at either the world's max Y or the region's max Y.
                int startY = useWorldY ? WorldHeights.MaxY : maxY;

                for (int x = minX; x <= maxX; ++x)
                {
                    for (int z = minZ; z <= maxZ; ++z)
                    {
                        // Scan downward until the first non-air block is found.
                        for (int y = startY; y >= WorldHeights.MinY - 1; y--)
                        {
                            if (GetBlockFromLocation(new Vector3(x, y, z)) != AirID)
                            {
                                if (replaceSurface)
                                    snowBlocks.Add(new Vector3(x, y, z));
                                else
                                    snowBlocks.Add(new Vector3(x, y + 1f, z));
                                break;
                            }
                        }
                    }
                }

                return snowBlocks;
            }, ct);
        }
        #endregion

        #endregion
    }
}
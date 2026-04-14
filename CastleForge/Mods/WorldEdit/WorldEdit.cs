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
Sections of this class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Numerics;
using ModLoaderExt;
using System.Linq;
using System.Text;
using ModLoader;
using System.IO;
using System;

using static WorldEdit.WorldEditCore.EnumMapper;
using static WorldEdit.WorldEditCore.WorldUtils;
using static WorldEdit.WorldEditCore;
using static ModLoader.LogSystem;

using ButtonState = Microsoft.Xna.Framework.Input.ButtonState; // XNA (CMZ) Type Aliases.
using MouseState  = Microsoft.Xna.Framework.Input.MouseState;  //
using Mouse       = Microsoft.Xna.Framework.Input.Mouse;       //
using Vector3     = Microsoft.Xna.Framework.Vector3;           //

namespace WorldEdit
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class WorldEdit : ModBase
    {
        /// <summary>
        /// Entrypoint for the WorldEdit mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public WorldEdit() : base("WorldEdit", new Version("0.1.0"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        /// <summary>
        /// Called once when the mod is first loaded by the ModLoader.
        /// Good place to:
        /// 1) Verify the game is running.
        /// 2) Install any Harmony patches or interceptors.
        /// 3) Register your command handlers.
        /// </summary>
        public override void Start()
        {
            // Acquire game and world references.
            var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(WorldEdit).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            WEConfig.LoadApply();

            // Start the nav-wand driver (compass by default). This is always-on and does not require /wand.
            StartNavWandTimer();

            // Register this plugin's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            // Also register this plugin's command list to the global help registry.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded. Use /help for a list of commands.");
        }

        /// <summary>
        /// Called when the game exits or mod is unloaded.
        /// Used to safely dispose patches and resources.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                try { GamePatches.DisableAll(); } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}."); } // Unpatch Harmony.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { Log($"Error shutting down mod: {ex}."); }
        }

        /// <summary>
        /// Called once per game tick.
        /// Not used by this mod (but required by ModBase).
        /// </summary>
        public override void Tick(DNA.Input.InputManager inputManager, GameTime gameTime) { }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// This example code was taken from 'WorldEdit-CSharp' by RussDev7.
        /// Main Project: https://github.com/RussDev7/WorldEdit-CSharp
        /// </summary>
        #region WorldEdit Mod

        #region Wand State

        // Instance flags (drive behavior).
        private bool _wandEnabled;
        private bool _toolEnabled;
        private bool _brushEnabled;

        // Static snapshot.
        internal static volatile bool WandEnabled;
        internal static volatile bool ToolEnabled;
        internal static volatile bool BrushEnabled;
        internal static volatile int  ActiveWandItemID;
        internal static volatile int  ActiveToolItemID;
        internal static volatile int  ActiveNavWandItemID = NavWandItemID;
        internal static volatile int  ActiveBrushItemID;

        private string _toolCommand = "";         // Active tool sub-command (set by /tool); empty = none selected.
        private int    _toolItem    = WandItemID; // Item ID required to activate the tool; defaults to WandItem as a safe placeholder.
        private int    _brushItem   = WandItemID; // Item ID required to activate the brush; defaults to WandItem as a safe placeholder.

        /// <summary>
        /// Returns true if any wand mode is currently enabled.
        /// Summary: Gates durability bypass to active WorldEdit tools only.
        /// </summary>
        public static bool IsAnyWandModeActive()
        {
            if (WandEnabled) return true;
            if (ToolEnabled) return true;
            if (BrushEnabled) return true;

            // NavWand "enabled" if configured.
            if (WorldEditCore.NavWandItemID >= 0)
                return true;

            return false;
        }
        #endregion

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // Showcasing Commands.
            ("cc",                                                                           "Clears the chat. This was made for showcasing."),
            ("brightness [amount]",                                                          "Change the brightness. Use '1' for default. This was made for showcasing."),
            ("fullbright",                                                                   "Force all tiles to use the max light level."),
            ("teleport [x] [y] [z] (spawnOnTop)",                                            "Teleport the player to a new position."),
            ("time [time]",                                                                  "Change the worlds time. Use 0-100 for time of day."),
            ("toggleui",                                                                     "Toggles the HUD and UI visibility."),
            ("seed",                                                                         "Display the seed of the current world."),

            // General Commands.
            ("help (page)",                                                                  "Display all available commands."),
            ("undo (times)",                                                                 "Undoes the last action (from history)."),
            ("redo (times)",                                                                 "Redoes the last action (from history)."),
            ("clearhistory",                                                                 "Clear your history."),
            ("undorecord (toggle|on|off|check)",                                             "Toggle recording of undo/redo snapshot data (persists to config)."),

            // Navigation Commands.
            ("unstuck",                                                                      "Escape from being stuck inside a block."),
            ("ascend (levels)",                                                              "Go up a floor."),
            ("descend (levels)",                                                             "Go down a floor."),
            ("ceil",                                                                         "Go to the ceiling."),
            ("thru",                                                                         "Pass through walls."),
            ("jumpto",                                                                       "Teleport to the cursors location."),
            ("up [amount|max]",                                                              "Go upwards some distance."),
            ("down [amount|max]",                                                            "Go downwards some distance."),

            // Selection Commands.
            ("pos [pos1|pos2..] (or /pos1|/pos2)",                                           "Set positions."),
            ("hpos [hpos1|hpos2..]",                                                         "Set position to targeted block."),
            ("chunk (chunkRadius|coordinates)",                                              "Set the selection to your current chunk."),
            ("wand (on|off)",                                                                "Get the wand item."),
            ("contract [amount] (direction)",                                                "Contract the selection area."),
            ("shift [amount] (direction)",                                                   "Shift the selection area."),
            ("trim [mask block(,array)]",                                                    "Minimize the selection to encompass matching blocks."),
            ("size (clipboard)",                                                             "Get information about the selection."),
            ("count [find block(,array)]",                                                   "Counts the number of blocks matching a mask."),
            ("distr (clipboard) (page)",                                                     "Get the distribution of blocks in the selection."),
            ("expand [amount(vert)] (direction)",                                            "Expand the selection area."),

            // Region Commands.
            ("set [block(,array)] (hollow)",                                                 "Sets all the blocks in the region."),
            ("break (mask block(,array))",                                                   "Breaks all blocks in the region (drops items)."),
            ("line [block(,array)] (thickness)",                                             "Draws line segments between two positions."),
            ("replace [source block,(all)] [to block,(all)]",                                "Replace all blocks in the selection with another."),
            ("allexcept [source block(,array)] (to block(,array))",                          "Replace all blocks except a desired block pattern."),
            ("overlay [replace block(,array)]",                                              "Set a block on top of blocks in the region."),
            ("naturalize",                                                                   "Place 3 layers of dirt on top then rock below."),
            ("walls [block(,array)]",                                                        "Build the four sides of the selection."),
            ("smooth (iterations)",                                                          "Smooth the elevation in the selection."),
            ("move [amount] (direction)",                                                    "Move the contents of the selection."),
            ("regen (seed)",                                                                 "Regenerates the contents of the selection."),
            ("stack (amount) (direction) (useAir)",                                          "Repeat the contents of the selection."),
            ("stretch (amount) (direction) (useAir)",                                        "Stretch the contents of the selection."),
            ("spell [(\")words(\")(@linebreak)/(/paste)] [block(,array)] (flip) (rotate)",   "Draws a text made of blocks relative to position 1."),
            ("hollow (block(,array)) (thickness)",                                           "Hollows out the object contained in this selection."),
            ("shapefill [block(,array)]",                                                    "Fills only the inner-most blocks of an object contained in this selection."),
            ("wrap [replace block(,array)] (wrap direction(s)(all)) (exclude direction(s))", "Fills only the outer-most air blocks of an object contained in this selection."),
            ("matrix [radius] [spacing] (snow) (default(,array))",                           "Places your clipboard spaced out in intervals."),
            ("forest [area_size] [density] (max_height) (snow_radius)",                      "Make a forest within the region, or in a circle around pos1."),
            ("tree (max_height)",                                                            "Make a tree at position 1."),
            ("break",                                                                        "Mines and drops all blocks within the region."),

            // Generation Commands.
            ("floor [block(,array)] [radius] (hollow)",                                      "Makes a filled floor."),
            ("cube [block(,array)] [radii] (hollow)",                                        "Makes a filled cube."),
            ("prism [block(,array)] [length] [width] (height) (hollow)",                     "Makes a filled prism."),
            ("sphere [block(,array)] [radii] (hollow) (height)",                             "Makes a filled sphere."),
            ("pyramid [block(,array)] [size] (hollow)",                                      "Makes a filled pyramid."),
            ("cone [block(,array)] [radii] [height] (hollow)",                               "Makes a filled cone."),
            ("cylinder [block(,array)] [radii] [height] (hollow)",                           "Makes a filled cylinder."),
            ("diamond [r block(,array)] [radii] (hollow) (squared)",                         "Makes a filled diamond."),
            ("ring [block(,array)] [radius] (hollow)",                                       "Makes a filled ring."),
            ("ringarray [block(,array)] [amount] [space]",                                   "Makes a hollowed ring at evenly spaced intervals."),
            ("generate [block(,array)] [expression(clipboard)] (hollow)",                    "Generates a shape according to a formula."),

            // Schematic and Clipboard Commands.
            ("schematic [save] (saveAir)",                                                   "Save your clipboard into a schematic file."),
            ("schematic [load] (loadAir)",                                                   "Load a schematic into your clipboard."),
            ("copy",                                                                         "Copy the selection to the clipboard."),
            ("cut",                                                                          "Cut the selection to the clipboard."),
            ("paste (useAir) (pos1)",                                                        "Paste the clipboard's contents."),
            ("rotate (rotateY) (rotateX) (rotateZ)",                                         "Rotate the contents of the clipboard."),
            ("flip (direction)",                                                             "Flip the contents of the clipboard across the origin."),
            ("clearclipboard",                                                               "Clear your clipboard."),
            ("copychunk (chunkRadius)",                                                      "Copies the current chunk or a 16x16 range of chunks into the chunk clipboard."),
            ("cutchunk (chunkRadius)",                                                       "Cuts the current chunk or a 16x16 range of chunks into the chunk clipboard."),
            ("pastechunk (useAir) (force)",                                                  "Pastes the copied chunk(s) clipboard around the player's current chunk."),
            ("delchunk (chunkRadius) (noundo)",                                              "Deletes the contents of your current chunk(s). Undoable by default."),

            // Tool Commands.
            ("tool [on/off] [/command], "              +
                 "tool command [/command]",                                                  "Binds a tool to the item in your hand."),
            ("navwand (on|off|item|id|none)",                                                "Navigation wand tool; left click = /jumpto, right click = /thru."),

            // Brush Commands.
            ("brush [on/off] (block(,array)) (size), " +
                 "brush block [block(,array)], "       +
                 "brush shape [shape], "               +
                 "brush size [size], "                 +
                 "brush height [height], "             +
                 "brush hollow [true/false], "         +
                 "brush replace [true/false], "        +
                 "brush rapid [true/false]",                                                 "Brushing commands."),

            // Utility Commands.
            ("fill [block(,array)] [radius] (depth) (direction)",                            "Fills connected air columns from the targeted block."),
            ("fillr [block(,array)] [radius] (depth) (direction)",                           "Recursively fills connected air from the targeted block."),
            ("drain [radius]",                                                               "Drains connected liquid near the targeted block."),
            ("removenear [radii] (pos1)",                                                    "Remove all blocks within a cylindrical radii."),
            ("replacenear [radii] [source block,(all)] [to block,(all)] (pos1)",             "Replace all blocks within a cylindrical radii with another."),
            ("snow [block(,array)] [radius] (pos1|region (worldY)) (replaceSurface)",        "Places a pattern of blocks on ground level or within the selected region."),
            ("randomplace [block(,array)] [radius] (amount) (replaceSurface)",               "Places a block pattern at random positions within a square radius."),
        };
        #endregion

        #region Chat Command Functions

        // Showcasing Commands.

        #region SHOWCASING COMMANDS ONLY - Remove this category from your project.

        #region /cc

        [Command("/cc")]
        private void ExecuteCC()
        {
            try
            {
                // Send blank messages to chat to clear chat publicly.
                for (int i = 0; i < 10; i++)
                    DNA.CastleMinerZ.Net.BroadcastTextMessage.Send(DNA.CastleMinerZ.CastleMinerZGame.Instance?.MyNetworkGamer, " "); // SendFeedback("");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /brightness

        [Command("/brightness")]
        private void ExecuteBrightness(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /brightness [amount]");
                return;
            }

            try
            {
                float amount = float.TryParse(args[0], out float a) ? a : 1;

                // Check if the brightness is greater then 0. If not, disable.
                if (amount > 1)
                {
                    DNA.CastleMinerZ.UI.InGameHUD.Instance.PlayerHealth = amount;

                    // Display message.
                    SendFeedback($"Brightness was set to: '{amount}'.");
                }
                else
                {
                    // Reset the brightness to the default max values.
                    DNA.CastleMinerZ.UI.InGameHUD.Instance.PlayerHealth = 1;

                    // Display message.
                    SendFeedback($"Brightness returned to default.");
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /fullbright

        [Command("/fullbright")]
        private void ExecuteFullBright()
        {
            try
            {
                // Toggle the _useFullBrightTiles bool.
                bool next = !GamePatches.FullBrightRuntime.UseFullBrightTiles;
                GamePatches.FullBrightRuntime.SetEnabled(next);

                // Display message.
                SendFeedback($"Fullbright has been {(next ? "enabled" : "disabled")}.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /teleport

        [Command("/teleport")]
        [Command("/tp")]
        private void ExecuteTeleport(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("ERROR: Command usage /teleport [x] [y] [z] (spawnOnTop)");
                return;
            }

            try
            {
                float yPos  = 0f;
                float zPos  = 0f;
                bool  spawnOnTop = true; // Default.
                int   index = 0;

                // Parse X (required, must be numeric).
                if (index >= args.Length || !float.TryParse(args[index], out float xPos))
                {
                    SendFeedback("ERROR: X must be a number. Usage: /teleport [x] (y) (z) (spawnOnTop:true|false).");
                    return;
                }
                index++;

                // Parse Y (optional numeric).
                if (index < args.Length && float.TryParse(args[index], out float yTmp))
                {
                    yPos = yTmp;
                    index++;
                }

                // Parse Z (optional numeric).
                if (index < args.Length && float.TryParse(args[index], out float zTmp))
                {
                    zPos = zTmp;
                    index++;
                }

                // Optional spawnOnTop bool.
                if (index < args.Length)
                {
                    string flag = args[index];
                    if (!string.IsNullOrWhiteSpace(flag))
                    {
                        if (!bool.TryParse(flag, out spawnOnTop))
                        {
                            SendFeedback("ERROR: spawnOnTop flag; Usage: /teleport [x] (y) (z) (spawnOnTop:true|false).");
                            return;
                        }
                    }
                    index++;
                }

                // If there are extra args beyond this point, treat as misuse.
                if (index < args.Length)
                {
                    SendFeedback("ERROR: Too many arguments. Usage: /teleport [x] (y) (z) (spawnOnTop:true|false).");
                    return;
                }

                // Define new position.
                Vector3 newPosition = new Vector3(xPos, yPos, zPos);

                // Teleport the player to the new position.
                TeleportUser(newPosition, spawnOnTop);

                // Display message with rounded coords.
                var rounded = new Vector3(
                    (int)Math.Round(newPosition.X),
                    (int)Math.Round(newPosition.Y),
                    (int)Math.Round(newPosition.Z));

                SendFeedback($"Teleported to: '{rounded}' (spawnOnTop={spawnOnTop.ToString().ToLowerInvariant()}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /time

        [Command("/time")]
        private void ExecuteTime(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /time [time]");
                return;
            }

            try
            {
                float time = float.TryParse(args[0], out float t) ? t : 0;

                // Define new time from a 0.0-1.0 range.
                // Ensure to handle value under and over 0-100.
                float newTime = (time < 0) ? 0 : (time > 100) ? 1 : (float)(time / 100.0);

                // Set the time to the new value.
                // Use the none host version of this function.
                DNA.CastleMinerZ.CastleMinerZGame.Instance.GameScreen.Day = newTime;

                // Display message.
                SendFeedback($"Time was set to: '{newTime}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /toggleui

        [Command("/toggleui")]
        private void ExecuteToggleUI()
        {
            try
            {
                // Toggle the _hideUI and avatar visibility bools.
                // DNA.CastleMinerZ.UI.InGameHUD._hideUI = !DNA.CastleMinerZ.UI.InGameHUD._hideUI;
                GamePatches.HUDToggle._hideUI = !GamePatches.HUDToggle._hideUI;
                DNA.CastleMinerZ.CastleMinerZGame.Instance.LocalPlayer.Avatar.Visible = !DNA.CastleMinerZ.CastleMinerZGame.Instance.LocalPlayer.Avatar.Visible;
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /seed

        [Command("/seed")]
        private void ExecuteSeed()
        {
            try
            {
                // If in-game, display the current world seed.
                if (IsInGame())
                    SendFeedback($"World seed: '{DNA.CastleMinerZ.CastleMinerZGame.Instance?.CurrentWorld?.Seed}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }

            bool IsInGame()
            {
                var g = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                return g != null && g?.GameScreen != null && g?.CurrentNetworkSession != null;
            }
        }
        #endregion

        #endregion

        // General Commands.

        #region /cui

        [Command("//cui")]
        [Command("/cui")]
        private void ExecuteCUI(string[] args)
        {
            /*
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /cui [on/off]");
                return;
            }
            */

            try
            {
                bool? enable = ResolveToggle(args, _wandEnabled);
                switch (enable)
                {
                    case true:
                        // if (args.Length != 1) { SendFeedback("ERROR: Missing parameter. Usage: /cui [on/off]"); return; }

                        _enableCLU = true;
                        SendFeedback("Selections are now shown.");
                        break;

                    case false:
                        // if (args.Length != 1) { SendFeedback("ERROR: Missing parameter. Usage: /cui [on/off]"); return; }

                        _enableCLU = false;
                        SendFeedback("Selections are now hidden.");
                        break;

                    default:
                        SendFeedback("ERROR: Command usage /cui [on/off]");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /help

        /*
        [Command("//help")]
        [Command("/help")]
        private void ExecuteHelp(string[] args)
        {
            int maxLinesPerPage = GetHelpPageSize();
            int totalPages = (int)Math.Ceiling((double)commands.Length / maxLinesPerPage);
            int page = 1;

            // If an argument is provided, try to parse it as a page number.
            if (args.Length > 1 || (args.Length == 1 && !int.TryParse(args[0], out page) || page < 1 || page > totalPages))
            {
                SendFeedback("ERROR: Command usage /help (optional: page number)");
                return;
            }

            int startIndex = (page - 1) * maxLinesPerPage;
            int endIndex = Math.Min(startIndex + maxLinesPerPage, commands.Length);

            SendFeedback($"== Help - Page {page}/{totalPages} ==");
            for (int i = startIndex; i < endIndex; i++)
            {
                SendFeedback($"{commands[i].command} - {commands[i].description}");
            }

            if (page < totalPages)
            {
                SendFeedback($"== Use \"/help {page + 1}\" for the next page. ==");
            }
        }

        #region Pagination Helpers

        /// <summary>
        /// Calculates how many help rows can fit on screen based on the current resolution.
        /// Reserves space for the page header and navigation hint.
        /// </summary>
        private static int GetHelpPageSize()
        {
            int screenHeight = DNA.Drawing.UI.Screen.Adjuster.ScreenRect.Height;

            // Rough visible line capacity:
            // 720p  -> 7.
            // 1080p -> 10.
            int totalVisibleRows = (int)Math.Floor(screenHeight / 100f);

            // Reserve lines for:
            // 1) page header.
            // 2) next-page hint.
            int reservedRows = 2;

            // Actual command rows allowed on the page.
            int pageSize = totalVisibleRows - reservedRows;

            // Safety clamp so extremes do not get silly.
            return Math.Max(4, Math.Min(12, pageSize));
        }
        #endregion
        */
        #endregion

        #region /undo

        [Command("//undo")]
        [Command("/undo")]
        private async Task ExecuteUndo(string[] args)
        {
            try
            {
                int times = args.Length > 0 && int.TryParse(args[0], out int t) ? t : 1;

                // Check if any undo actions exist.
                if (UndoStack.Count == 0)
                {
                    SendFeedback("No undo actions available.");
                    return;
                }

                // Perform undo actions multiple times based on the 'times' parameter.
                int actionsCount = 0;
                for (int i = 0; i < times; i++)
                {
                    if (UndoStack.Count == 0)
                    {
                        // SendFeedback($"Only {i} actions were available.");
                        break;
                    }
                    else
                        actionsCount++;

                    // Run the load undo function from the undo/redo manager.
                    var frame = await LoadUndo();
                    var actions = frame.Item1;
                    var crates = frame.Item2;

                    foreach (var action in actions)
                    {
                        // Get location of block and block ID.
                        Vector3 blockLocation = action.Item1;
                        int block = action.Item2;

                        // Place block if it doesn't already exist. (improves the performance)
                        // If multiple undo's where made, the count is less then 1, make an exception.
                        // This is done encase the start and finish saves where the same nullifying them out.
                        if (GetBlockFromLocation(blockLocation) != block || (times > 1 && UndoStack.Count <= 1))
                            AsyncBlockPlacer.Enqueue(blockLocation, block);
                    }

                    // After enqueuing blocks, reconcile crate sidecar to match this snapshot.
                    ApplyCratesFromSnapshot(actions, crates);
                }

                SendFeedback($"Undid '{actionsCount}' action(s) successfully!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /redo

        [Command("//redo")]
        [Command("/redo")]
        private async Task ExecuteRedo(string[] args)
        {
            try
            {
                int times = args.Length > 0 && int.TryParse(args[0], out int t) ? t : 1;

                // Check if any redo actions exist.
                if (RedoStack.Count == 0)
                {
                    SendFeedback("No redo actions available.");
                    return;
                }

                // Perform redo actions multiple times based on the 'times' parameter.
                int actionsCount = 0;
                for (int i = 0; i < times; i++)
                {
                    if (RedoStack.Count == 0)
                    {
                        // SendFeedback($"Only {i} actions were available.");
                        break;
                    }
                    else
                        actionsCount++;

                    // Run the load redo function from the undo/redo manager.
                    var frame = await LoadRedo();
                    var actions = frame.Item1;
                    var crates = frame.Item2;

                    foreach (var action in actions)
                    {
                        // Get location of block and block ID.
                        Vector3 blockLocation = action.Item1;
                        int block = action.Item2;

                        // Place block if it doesn't already exist. (improves the performance)
                        // If multiple redo's where made, the count is less then 1, make an exception.
                        // This is done encase the start and finish saves where the same nullifying them out.
                        if (GetBlockFromLocation(blockLocation) != block || (times > 1 && RedoStack.Count <= 1))
                            AsyncBlockPlacer.Enqueue(blockLocation, block);
                    }

                    // After enqueuing blocks, reconcile crate sidecar to match this snapshot.
                    ApplyCratesFromSnapshot(actions, crates);
                }

                SendFeedback($"Redid '{actionsCount}' action(s) successfully!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /clearhistory

        [Command("//clearhistory")]
        [Command("/clearhistory")]
        [Command("//clearh")]
        [Command("/clearh")]
        private void ExecuteClearHistory()
        {
            try
            {
                // Clear existing clearhistory.
                ClearHistory();

                SendFeedback($"History has been cleared!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /undorecord

        [Command("//recordundo")]
        [Command("/recordundo")]
        [Command("//undorecord")]
        [Command("/undorecord")]
        [Command("//undorec")]
        [Command("/undorec")]
        private void ExecuteUndoRecord(string[] args)
        {
            // Summary: Toggles whether world edits record undo/redo snapshot data.
            // This is useful when doing huge terrain operations where capturing history is expensive.
            //
            // Usage:
            // - /undorecord               -> Toggle on/off.
            // - /undorecord on|off|toggle -> Explicit state.
            // - /undorecord check         -> Print current state.
            //
            // Persistence:
            // - Writes to WorldEdit.Config.ini ([Undo] UndoRecordingEnabled) and reapplies config immediately.
            try
            {
                var cfg = WEConfig.LoadOrCreate();
                bool current = cfg.UndoRecordingEnabled;

                if (args != null && args.Length >= 1)
                {
                    string token = (args[0] ?? "").Trim().ToLowerInvariant();
                    if (token == "check" || token == "status" || token == "state")
                    {
                        SendFeedback($"Undo recording is currently {(current ? "ON" : "OFF")}.");
                        return;
                    }
                }

                bool enable = ResolveToggle(args, current);

                WEConfig.UpdateUndoRecordingConfig(enable);
                WEConfig.LoadApply();

                SendFeedback($"Undo recording {(enable ? "ENABLED" : "DISABLED")} (saved to config).");

                if (!enable)
                    SendFeedback("Note: Edits will still apply, but changes made while OFF will not be undoable.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Navigation Commands.

        #region /unstuck

        [Command("//unstuck")]
        [Command("/unstuck")]
        [Command("//!")]
        [Command("/!")]
        private async Task ExecuteUnstuck()
        {
            try
            {
                Vector3 usersLocation = GetUsersLocation(); // Get the user's current location.

                // 1) Try to ascend first.
                Vector3 nextLocation = await GetAscendingVector(usersLocation);

                if (nextLocation != usersLocation)
                {
                    // Use safe placement: Chunks may still be loading, and we're explicitly "unstucking".
                    TeleportUser(nextLocation, true);
                    SendFeedback("Teleported up '1' level!");
                    return;
                }

                // 2) Still stuck, try going through.
                Vector3 cursorLocation = GetUsersCursorLocation();
                Direction facingDirection = GetFacingDirection(usersLocation, cursorLocation);

                nextLocation = await GetThruVector(usersLocation, facingDirection);

                if (nextLocation != usersLocation)
                {
                    TeleportUser(nextLocation, true);
                    SendFeedback($"Teleported thru '{Math.Round(Vector3.Distance(usersLocation, nextLocation))}' blocks!");
                    return;
                }

                // 3) Still stuck, try descending.
                nextLocation = await GetDescendingVector(usersLocation);

                if (nextLocation != usersLocation)
                {
                    TeleportUser(nextLocation, true);
                    SendFeedback("Teleported down '1' level!");
                    return;
                }

                // Still stuck. How did this happen?
                // Last resort: Let the game pick something safe near current.
                TeleportUser(usersLocation, true);
                SendFeedback("Unable to find a suitable location. Attempted safe placement.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /ascend

        [Command("//ascend")]
        [Command("/ascend")]
        [Command("//asc")]
        [Command("/asc")]
        private async Task ExecuteAscend(string[] args)
        {
            try
            {
                int levels = args.Length > 0 && int.TryParse(args[0], out int l) ? l : 1;

                // Get the new location offset.
                Vector3 usersLocation = GetUsersLocation(); // Get the user's current location.
                Vector3 newLocation = usersLocation;

                // Ascend the specified number of levels.
                int levelCount = 0;
                for (int i = 0; i < levels; i++)
                {
                    Vector3 nextLocation = await GetAscendingVector(newLocation);

                    // Stop if no valid location is found.
                    if (nextLocation == newLocation)
                    {
                        SendFeedback($"Stopped at level {levelCount}: No furthest valid location found.");
                        break;
                    }
                    else
                        levelCount++;

                    newLocation = nextLocation;
                }

                // Teleport only if the location changed.
                if (newLocation != usersLocation)
                {
                    // Teleport user.
                    TeleportUser(newLocation, false);

                    SendFeedback($"Teleported up {levelCount} level(s)!");
                }
                else
                    SendFeedback("No valid location was found.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /decend

        [Command("//descend")]
        [Command("/descend")]
        [Command("//desc")]
        [Command("/desc")]
        private async Task ExecuteDescend(string[] args)
        {
            try
            {
                int levels = args.Length > 0 && int.TryParse(args[0], out int l) ? l : 1;

                // Get the new location offset.
                Vector3 usersLocation = GetUsersLocation(); // Get the user's current location.
                Vector3 newLocation = usersLocation;

                // Descend the specified number of levels.
                int levelCount = 0;
                for (int i = 0; i < levels; i++)
                {
                    Vector3 nextLocation = await GetDescendingVector(newLocation);

                    // Stop if no valid location is found.
                    if (nextLocation == newLocation)
                    {
                        SendFeedback($"Stopped at level {levelCount}: No furthest valid location found.");
                        break;
                    }
                    else
                        levelCount++;

                    newLocation = nextLocation;
                }

                // Teleport only if the location changed.
                if (newLocation != usersLocation)
                {
                    // Teleport user.
                    TeleportUser(newLocation, false);

                    SendFeedback($"Teleported down {levelCount} level(s)!");
                }
                else
                    SendFeedback("No valid location was found.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /ceil

        [Command("//ceil")]
        [Command("/ceil")]
        private async Task ExecuteCeil()
        {
            try
            {
                // Get the new location offset.
                Vector3 usersLocation = GetUsersLocation(); // Get the user's current location.
                Vector3 newLocation = await GetCeilingVector(usersLocation);

                // Check if a valid location was found.
                if (newLocation != usersLocation)
                {
                    // Teleport user.
                    TeleportUser(newLocation, false);

                    SendFeedback($"Teleported up '{Math.Round(Vector3.Distance(usersLocation, newLocation))}' blocks!");
                }
                else
                    SendFeedback("No valid location was found.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /thru

        [Command("//thru")]
        [Command("/thru")]
        private async Task ExecuteThru()
        {
            try
            {
                // Get the new location offset.
                Vector3 usersLocation = GetUsersLocation();                                    // Get the user's current location.
                Vector3 cursorLocation = GetUsersCursorLocation();                             // Get the user's cursor location.

                // If the cursor isn't actually on a solid block, /thru has no wall to pass through.
                if (!IsValidCursorLocation() || GetBlockFromLocation(cursorLocation) == AirID)
                {
                    SendFeedback("No wall targeted (aim at a block).");
                    return;
                }

                Direction facingDirection = GetFacingDirection(usersLocation, cursorLocation); // Determine the direction the user is facing.
                Vector3 newLocation = await GetThruVector(usersLocation, facingDirection, maxSteps: 512);

                // Check if a valid location was found.
                if (newLocation != usersLocation)
                {
                    // Snap down to solid ground below the "thru" exit point.
                    Vector3 grounded = SnapToGround(newLocation, maxDrop: 96);

                    // Teleport user.
                    TeleportUser(grounded, ShouldSpawnOnTop(grounded));

                    // Feel free to comment this out. Can get annoying.
                    // SendFeedback($"Teleported thru '{Math.Round(Vector3.Distance(usersLocation, newLocation))}' blocks!");
                }
                else
                    SendFeedback("No valid location was found.");

                /// <summary>
                /// Returns a snapped destination that is standing on solid ground (if found).
                /// Summary: Drops downward from the candidate location until it finds a walkable spot.
                /// </summary>
                Vector3 SnapToGround(Vector3 candidate, int maxDrop = 64)
                {
                    Vector3 p = candidate;

                    // Drop down until we either find ground or hit the limit.
                    for (int i = 0; i < maxDrop; i++)
                    {
                        // Feet + head must be clear where we want to stand.
                        bool feetClear = GetBlockFromLocation(p) == AirID;
                        bool headClear = GetBlockFromLocation(new Vector3(p.X, p.Y + 1, p.Z)) == AirID;

                        // The block below must be solid.
                        bool hasGround = GetBlockFromLocation(new Vector3(p.X, p.Y - 1, p.Z)) != AirID;

                        if (feetClear && headClear && hasGround)
                            return p;

                        // Otherwise keep dropping.
                        p = new Vector3(p.X, p.Y - 1, p.Z);
                    }

                    // If we can't find ground within maxDrop, return the original candidate.
                    return candidate;
                }

                /// <summary>
                /// Returns true if teleport should use the game's "spawn on top" safety.
                /// Summary: If the destination is obstructed, let the game place us safely.
                /// </summary>
                bool ShouldSpawnOnTop(Vector3 destination)
                {
                    // Need 2 blocks of air at the destination (feet + head).
                    for (int y = 1; y < 3; y++)
                    {
                        Vector3 check = new Vector3(destination.X, destination.Y + y, destination.Z);
                        if (GetBlockFromLocation(check) != AirID)
                            return true; // Blocked -> spawnOnTop.
                    }

                    return false; // Clear -> direct position OK.
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /jumpto

        private static Vector3 lastJumpLocation = new Vector3(0, 0, 0); // Store the last jump location.

        [Command("//jumpto")]
        [Command("/jumpto")]
        [Command("//j")]
        [Command("/j")]
        private void ExecuteJumpTo()
        {
            try
            {
                // Get the new location offset.
                Vector3 usersLocation = GetUsersLocation();        // Get the user's current location.
                Vector3 cursorLocation = GetUsersCursorLocation(); // Get the user's cursor location.

                // If the cursor isn't actually on a solid block, /thru has no wall to pass through.
                if (!IsValidCursorLocation() || GetBlockFromLocation(cursorLocation) == AirID)
                {
                    SendFeedback("No wall targeted (aim at a block).");
                    return;
                }

                // Check if a valid location was found. Ensure we don't teleport to the same location twice.
                if (lastJumpLocation != cursorLocation && cursorLocation != usersLocation)
                {
                    // Teleport user.
                    // Adjust the position one up so we don't clip into the cursor block.
                    TeleportUser(new Vector3(cursorLocation.X, cursorLocation.Y + 1, cursorLocation.Z), ShouldSpawnOnTop(cursorLocation));

                    // Store this jump location.
                    lastJumpLocation = cursorLocation;

                    // Feel free to comment this out. Can get annoying.
                    // SendFeedback($"Teleported '{Math.Round(Vector3.Distance(usersLocation, cursorLocation))}' blocks away!");
                }
                // else
                    // SendFeedback("No valid location was found.");

                /// <summary>
                /// Returns true if teleport should use the game's "spawn on top" safety.
                /// Summary: If the destination is obstructed, let the game place us safely.
                /// </summary>
                bool ShouldSpawnOnTop(Vector3 destination)
                {
                    // Need 2 blocks of air (feet + head).
                    for (int y = 1; y < 3; y++)
                    {
                        Vector3 check = new Vector3(destination.X, destination.Y + y, destination.Z);
                        if (GetBlockFromLocation(check) != AirID)
                            return true; // Blocked -> spawnOnTop.
                    }

                    return false; // Clear -> direct position OK.
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /up

        [Command("//up")]
        [Command("/up")]
        private static async void ExecuteUp(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /up [amount|max]");
                return;
            }

            try
            {
                string argPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "15";

                int amount;
                if (argPattern.Equals("max", StringComparison.OrdinalIgnoreCase))
                    amount = WorldHeights.MaxY - ((int)GetUsersLocation().Y + 2); // Cap to one below max.
                else
                    amount = int.TryParse(argPattern, out int a) ? a : 15;

                // Get the new location offset. Convert the offsets into integers.
                Vector3 placePos = GetUsersLocation();
                placePos.Y += amount;

                // Ensure the position is within the bounds of the world.
                if (placePos.Y <= WorldHeights.MaxY)
                {
                    // Place block using cell coords only.
                    AsyncBlockPlacer.Enqueue(placePos, 48); // GlassMystery.
                    await Task.Delay(100);                  // Add short wait.

                    // Teleport using centered coords
                    Vector3 tpPos = new Vector3(placePos.X + 0.5f, placePos.Y + 1.01f, placePos.Z + 0.5f);
                    TeleportUser(tpPos, false);

                    SendFeedback($"Teleported up '{amount}' blocks!");
                }
                else
                    SendFeedback($"Location 'Y:{Math.Round(placePos.Y)}' is out of bounds. Max: '{WorldHeights.MaxY}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /down

        [Command("//down")]
        [Command("/down")]
        private static async void ExecuteDown(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /down [amount|max]");
                return;
            }

            try
            {
                string argPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "15";

                int amount;
                if (argPattern.Equals("max", StringComparison.OrdinalIgnoreCase))
                    amount = (int)GetUsersLocation().Y - (WorldHeights.MinY + 1); // Cap to one above max.
                else
                    amount = int.TryParse(argPattern, out int a) ? a : 15;

                // Get the new location offset. Convert the offsets into integers.
                Vector3 placePos = GetUsersLocation();
                placePos.Y -= amount;

                // Ensure the position is within the bounds of the world.
                if (placePos.Y >= WorldHeights.MinY)
                {
                    AsyncBlockPlacer.Enqueue(placePos, 48); // GlassMystery.
                    await Task.Delay(100);                  // Add short wait.

                    // Teleport using centered coords.
                    Vector3 tpPos = new Vector3(placePos.X + 0.5f, placePos.Y + 1.01f, placePos.Z + 0.5f);
                    TeleportUser(tpPos, false);

                    SendFeedback($"Teleported down '{amount}' blocks!");
                }
                else
                    SendFeedback($"Location 'Y:{Math.Round(placePos.Y)}' is out of bounds. Max: '{WorldHeights.MinY}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Selection Commands.

        #region /pos

        [Command("//pos")]
        [Command("/pos")]
        private void ExecutePos(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /pos [1/2] (or use /pos1, /pos2)");
                return;
            }

            if (!int.TryParse(args[0], out int point))
                point = 1;

            ExecutePosCore(point);
        }

        [Command("//pos1")]
        [Command("/pos1")]
        private void ExecutePos1()
        {
            ExecutePosCore(1);
        }

        [Command("//pos2")]
        [Command("/pos2")]
        private void ExecutePos2()
        {
            ExecutePosCore(2);
        }

        private void ExecutePosCore(int point)
        {
            try
            {
                if (point == 1)
                    _pointToLocation1 = GetUsersLocation();
                else if (point == 2)
                    _pointToLocation2 = GetUsersLocation();
                else
                {
                    SendFeedback($"Position {point} is not valid!");
                    return;
                }

                var v = (point == 1) ? _pointToLocation1 : _pointToLocation2;
                SendFeedback($"Position {point} ({Math.Round(v.X)}, {Math.Round(v.Y)}, {Math.Round(v.Z)}) has been set!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /hpos

        [Command("//hpos")]
        [Command("/hpos")]
        private void ExecuteHpos(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /hpos [hpos1/hpos2..]");
                return;
            }

            try
            {
                int point = int.TryParse(args[0], out int p) ? p : 1;

                // Check what position to set.
                if (point == 1)
                    _pointToLocation1 = GetUsersCursorLocation();
                else if (point == 2)
                    _pointToLocation2 = GetUsersCursorLocation();

                // Ensure point is within range.
                if (point == 1 || point == 2)
                    SendFeedback($"Targeted position {point} ({(point == 1 ? $"{Math.Round(_pointToLocation1.X)}, {Math.Round(_pointToLocation1.Y)}, {Math.Round(_pointToLocation1.Z)}" : $"{Math.Round(_pointToLocation2.X)}, {Math.Round(_pointToLocation2.Y)}, {Math.Round(_pointToLocation2.Z)}")}) has been set!");
                else
                    SendFeedback($"Targeted position {point} is not valid!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /chunk

        [Command("//chunk")]
        [Command("/chunk")]
        private void ExecuteChunk(string[] args)
        {
            // Decide which point to center on.
            Vector3 chunkLoc = Vector3.Zero;
            bool useArg = false;

            // Optional radius; default = 1 (just the current chunk).
            // Semantics (matches /copychunk):
            //   Radius = 1 -> Just the current chunk (1x1).
            //   Radius = 2 -> A 3x3 area (chunk + 8 around it).
            //   Radius = 3 -> A 5x5 area, etc.
            int radius = 1;

            // Supported forms:
            //   /chunk
            //   /chunk 2
            //   /chunk 100,0,200
            //   /chunk 2 100,0,200
            //   /chunk 100,0,200 2
            if (args.Length == 1)
            {
                // If it's an int, treat it as chunkRadius (same as /copychunk).
                if (!string.IsNullOrWhiteSpace(args[0]) && int.TryParse(args[0], out int r))
                    radius = r;
                else
                    useArg = TryParseXYZ(args[0], out chunkLoc);
            }
            else if (args.Length == 2)
            {
                // Allow either order: (radius coords) or (coords radius).
                bool aIsRadius = int.TryParse(args[0], out int rA);
                bool bIsRadius = int.TryParse(args[1], out int rB);

                bool aIsCoords = TryParseXYZ(args[0], out Vector3 cA);
                bool bIsCoords = TryParseXYZ(args[1], out Vector3 cB);

                if (aIsRadius && bIsCoords)
                {
                    radius = rA;
                    chunkLoc = cB;
                    useArg = true;
                }
                else if (aIsCoords && bIsRadius)
                {
                    radius = rB;
                    chunkLoc = cA;
                    useArg = true;
                }
                else
                {
                    SendFeedback("ERROR: Command usage /chunk (chunkRadius|coordinates)");
                    return;
                }
            }
            else if (args.Length > 2)
            {
                SendFeedback("ERROR: Command usage /chunk (chunkRadius|coordinates)");
                return;
            }

            if (radius < 1)
                radius = 1;

            // If no chunk location was specified or valid, fall back to the users location.
            if (!useArg)
                chunkLoc = GetUsersLocation();

            // Compute chunk indices using mathematical floor.
            int sizeX = ChunkSize.WidthX;
            int sizeZ = ChunkSize.LengthZ;

            int centerChunkX = FloorDiv((int)Math.Floor(chunkLoc.X), sizeX);
            int centerChunkZ = FloorDiv((int)Math.Floor(chunkLoc.Z), sizeZ);

            // Radius = 1 -> ring = 0 -> 1x1 chunk area.
            // Radius = 2 -> ring = 1 -> 3x3 chunk area, etc.
            int ring = radius - 1;

            int minChunkX = centerChunkX - ring;
            int maxChunkX = centerChunkX + ring;
            int minChunkZ = centerChunkZ - ring;
            int maxChunkZ = centerChunkZ + ring;

            // Convert chunk indices -> World coordinates.
            int minX = minChunkX * sizeX;
            int maxX = (maxChunkX + 1) * sizeX - 1;
            int minZ = minChunkZ * sizeZ;
            int maxZ = (maxChunkZ + 1) * sizeZ - 1;

            // Update selection - Full vertical column.
            _pointToLocation1 = new Vector3(minX, WorldHeights.MinY, minZ);
            _pointToLocation2 = new Vector3(maxX, WorldHeights.MaxY, maxZ);

            // Feedback.
            if (radius <= 1)
            {
                SendFeedback(
                    $"Chunk selected at X:{centerChunkX} Z:{centerChunkZ} " +
                    $"({minX},{WorldHeights.MinY},{minZ}) -> ({maxX},{WorldHeights.MaxY},{maxZ}).");
            }
            else
            {
                SendFeedback(
                    $"Chunk selection X:[{minChunkX}..{maxChunkX}] Z:[{minChunkZ}..{maxChunkZ}] " +
                    $"(center X:{centerChunkX} Z:{centerChunkZ} radius={radius}) " +
                    $"({minX},{WorldHeights.MinY},{minZ}) -> ({maxX},{WorldHeights.MaxY},{maxZ}).");
            }
        }
        #endregion

        #region /wand

        [Command("//wand")]
        [Command("/wand")]
        private void ExecuteWand(string[] args) // Don't give 'static' for wand command.
        {
            /*
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /wand [on/off]");
                return;
            }
            */

            try
            {
                bool? enable = ResolveToggle(args, _wandEnabled);
                switch (enable)
                {
                    case true:
                        // if (args.Length != 1) { SendFeedback("ERROR: Missing parameter. Usage: /wand [on/off]"); return; }

                        // Optionally grant the wand item when enabling.
                        if (WEConfig.Active?.GiveWandItemOnEnable ?? true)
                        {
                            if (WandItemID >= 0)
                            {
                                if (!UserHasItem((DNA.CastleMinerZ.Inventory.InventoryItemIDs)WandItemID))
                                    GiveUserItem((DNA.CastleMinerZ.Inventory.InventoryItemIDs)WandItemID);
                            }
                            else
                            {
                                SendFeedback("WandItem is disabled (set to 'none/off'). No item was given.");
                            }
                        }

                        Timer wandTimer = new Timer() { Interval = 1 };
                        wandTimer.Tick += WorldWand_Tick;
                        wandTimer.Start();

                        _wandEnabled = true; WandEnabled = true;
                        ActiveWandItemID = WandItemID; // Set the snapshot wand item.
                        SendFeedback("Wand Activated!");
                        break;

                    case false:
                        // if (args.Length != 1) { SendFeedback("ERROR: Missing parameter. Usage: /wand [on/off]"); return; }

                        _wandEnabled = false; WandEnabled = false;
                        ActiveWandItemID = -1; // Clear the snapshot wand item.
                        SendFeedback("Wand Deactivated!");
                        break;

                    default:
                        SendFeedback("ERROR: Command usage /wand [on/off]");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /contract

        [Command("//contract")]
        [Command("/contract")]
        private void ExecuteContract(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /contract [amount] (direction)");
                return;
            }

            try
            {
                if (!int.TryParse(args[0], out int amount) || amount < 0)
                {
                    SendFeedback("ERROR: [amount] must be a positive integer.");
                    return;
                }

                // Parse optional direction (null ? all faces).
                Direction? direction = null;
                if (args.Length >= 2 &&
                    !string.IsNullOrWhiteSpace(args[1]) &&
                    !args[1].Equals("all", StringComparison.OrdinalIgnoreCase) && // allow  "all".
                    Enum.TryParse(args[1], true, out Direction parsedDir))
                {
                    direction = parsedDir;
                }

                // Work with *min* and *max* so we don't care which corner is pos1/pos2.
                Vector3 min = Vector3.Min(_pointToLocation1, _pointToLocation2);
                Vector3 max = Vector3.Max(_pointToLocation1, _pointToLocation2);

                void shrink(ref float face, int by) => face -= by;
                void grow  (ref float face, int by) => face += by;

                if (direction is null) // contract from every face.
                {
                    grow  (ref min.X, amount); shrink(ref max.X, amount);
                    grow  (ref min.Y, amount); shrink(ref max.Y, amount);
                    grow  (ref min.Z, amount); shrink(ref max.Z, amount);
                }
                else                   // contract one face only.
                {
                    switch (direction.Value)
                    {
                        case Direction.posX: shrink(ref max.X, amount); break;
                        case Direction.negX: grow  (ref min.X, amount); break;
                        case Direction.Up:   shrink(ref max.Y, amount); break;
                        case Direction.Down: grow  (ref min.Y, amount); break;
                        case Direction.posZ: shrink(ref max.Z, amount); break;
                        case Direction.negZ: grow  (ref min.Z, amount); break;
                    }
                }

                // Validate that we still have a non-negative region.
                if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                {
                    SendFeedback("ERROR: Contract amount too large  selection would invert/vanish.");
                    return;
                }

                // Ensure the expansion does not exceed world height limits.
                // This should not be an issue for contracting.
                ClampToWorldHeight(ref min.Y, ref max.Y);

                // Persist the new selection back to the global points.
                _pointToLocation1 = min;
                _pointToLocation2 = max;

                // Feedback.
                string dirText = direction?.ToString() ?? "all faces";
                SendFeedback(
                    $"Contracted selection by {amount} block(s) on {dirText}.");
                    // $"New region: ({Math.Round(min.X)}, {Math.Round(min.Y)}, {Math.Round(min.Z)}) -> " +
                    // $"({Math.Round(max.X)}, {Math.Round(max.Y)}, {Math.Round(max.Z)}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /shift

        [Command("//shift")]
        [Command("/shift")]
        private void ExecuteShift(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /shift [amount] (direction)");
                return;
            }

            try
            {
                if (!int.TryParse(args[0], out int amount) || amount < 0)
                {
                    SendFeedback("ERROR: [amount] must be a positive integer.");
                    return;
                }

                // Parse direction. Use facing direction as the default.
                Vector3 cursorLocation = GetUsersCursorLocation();
                Direction dir = GetFacingDirection(_pointToLocation1, cursorLocation);
                if (args.Length >= 2 &&
                    !string.IsNullOrWhiteSpace(args[1]) &&
                    Enum.TryParse(args[1], true, out Direction parsedDir))
                {
                    dir = parsedDir;
                }

                // Build the offset vector.
                Vector3 offset;
                switch (dir)
                {
                    case Direction.posX:
                        offset = new Vector3(+amount, 0, 0);
                        break;
                    case Direction.negX:
                        offset = new Vector3(-amount, 0, 0);
                        break;
                    case Direction.posZ:
                        offset = new Vector3(0, 0, +amount);
                        break;
                    case Direction.negZ:
                        offset = new Vector3(0, 0, -amount);
                        break;
                    case Direction.Up:
                        offset = new Vector3(0, +amount, 0);
                        break;
                    case Direction.Down:
                        offset = new Vector3(0, -amount, 0);
                        break;
                    default:
                        offset = Vector3.Zero;
                        break;
                }

                // C# 8.0+.
                /*
                Vector3 offset = dir switch
                {
                    Direction.posX => new Vector3(+amount, 0, 0),
                    Direction.negX => new Vector3(-amount, 0, 0),
                    Direction.posZ => new Vector3(0, 0, +amount),
                    Direction.negZ => new Vector3(0, 0, -amount),
                    Direction.Up   => new Vector3(0, +amount, 0),
                    Direction.Down => new Vector3(0, -amount, 0),
                    _              => Vector3.Zero
                };
                */

                // Compute new positions.
                Vector3 newP1 = _pointToLocation1 + offset;
                Vector3 newP2 = _pointToLocation2 + offset;

                // Keep X & Z unlimited; clamp Y only.
                newP1.Y = Clamp((int)newP1.Y, WorldHeights.MinY, WorldHeights.MaxY);
                newP2.Y = Clamp((int)newP2.Y, WorldHeights.MinY, WorldHeights.MaxY);

                // If clamp collapsed the region completely (i.e. it would lie outside).
                if (newP1.Y == newP2.Y && _pointToLocation1.Y != _pointToLocation2.Y)
                {
                    SendFeedback("ERROR: Shift would move the selection outside world height limits.");
                    return;
                }

                // Persist & report.
                _pointToLocation1 = newP1;
                _pointToLocation2 = newP2;

                SendFeedback(
                    $"Shifted selection {amount} block(s) toward {dir}.");
                    // $"New region: ({Math.Round(newP1.X)}, {Math.Round(newP1.Y)}, {Math.Round(newP1.Z)}) -> " +
                    // $"({Math.Round(newP2.X)}, {Math.Round(newP2.Y)}, {Math.Round(newP2.Z)}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /trim

        [Command("//trim")]
        [Command("/trim")]
        private void ExecuteTrim(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /trim [mask block(,array)]");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] maskArray = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (maskArray.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data and mask data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);
                HashSet<int> maskSet = new HashSet<int>(maskArray);

                // TrimRegion(Region region, HashSet<int> maskSet, out Vector3 outMin, out Vector3 outMax).
                // Find bounding box of matching blocks.
                if (!TrimRegion(definedRegion, maskSet, out Vector3 newMin, out Vector3 newMax))
                {
                    SendFeedback("No matching blocks found inside the current selection.");
                    return;
                }

                // Clamp Y to world height limits just in case.
                newMin.Y = Math.Max(newMin.Y, WorldHeights.MinY);
                newMax.Y = Math.Min(newMax.Y, WorldHeights.MaxY);

                // Persist & feedback.
                _pointToLocation1 = newMin;
                _pointToLocation2 = newMax;

                SendFeedback(
                    $"Trimmed selection to matching blocks.");
                    // $"New region: ({Math.Round(newMin.X)}, {Math.Round(newMin.Y)}, {Math.Round(newMin.Z)}) -> " +
                    // $"({Math.Round(newMax.X)}, {Math.Round(newMax.Y)}, {Math.Round(newMax.Z)}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /size

        [Command("//size")]
        [Command("/size")]
        private void ExecuteSize(string[] args)
        {
            bool fromClipboard = args.Length >= 1 && args[0].Equals("clipboard", StringComparison.OrdinalIgnoreCase);

            // If clipboard was requested and empty, display an error and return.
            if (fromClipboard && (copiedRegion == null || copiedRegion.Count == 0))
            {
                SendFeedback("ERROR: The clipboard is empty. Try using /copy first.");
                return;
            }

            // Decide which bounding box to use.
            if (!(fromClipboard && GetBoundingBoxFromRegion(out Vector3 min, out Vector3 max)))
            {
                // Make sure both points have been set.
                if (_pointToLocation1 == default || _pointToLocation2 == default)
                {
                    SendFeedback("ERROR: You must set /pos1 and /pos2 first.");
                    return;
                }

                // Put the two corners into canonical order.
                min = Vector3.Min(_pointToLocation1, _pointToLocation2);
                max = Vector3.Max(_pointToLocation1, _pointToLocation2);
            }

            // Calculate dimensions (inclusive, so +1).
            int sizeX = (int)(max.X - min.X) + 1;
            int sizeY = (int)(max.Y - min.Y) + 1;
            int sizeZ = (int)(max.Z - min.Z) + 1;

            long volume = (long)sizeX * sizeY * sizeZ;

            // Generate a neat summary. // C# 11+ can use raw string '"""'.
            string report = new StringBuilder()
                .AppendLine($"----------------------------------------------------")
                .AppendLine(
                    $"Selection corners : ({Math.Round(min.X)}, {Math.Round(min.Y)}, {Math.Round(min.Z)}) -> " +
                    $"({Math.Round(max.X)}, {Math.Round(max.Y)}, {Math.Round(max.Z)})"
                )
                .AppendLine($"Width  (X)        : {sizeX} block{(sizeX == 1 ? "" : "s")}")
                .AppendLine($"Height (Y)        : {sizeY} block{(sizeY == 1 ? "" : "s")}")
                .AppendLine($"Length (Z)        : {sizeZ} block{(sizeZ == 1 ? "" : "s")}")
                .AppendLine($"Total volume      : {volume:N0} block{(volume == 1 ? "" : "s")}")
                .Append    ($"----------------------------------------------------")
                .ToString();

            // Display report.
            SendFeedback(report);
        }
        #endregion

        #region /count

        [Command("//count")]
        [Command("/count")]
        private async Task ExecuteCount(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /count [find block(,array)]");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] maskArray = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (maskArray.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data and mask data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);
                HashSet<int> maskSet = new HashSet<int>(maskArray);

                // Decide if air blocks (denoted by AirID) should be ignored.
                // For example, if the input does NOT include AirID, then we ignore air in the region.
                int ignoreBlock = (!maskSet.Contains(AirID)) ? AirID : -1;

                // CountRegion(Region region, HashSet<int> maskSet, int ignoreBlock = -1).
                // Pass the HashSet of block IDs (converted from the array) to CountRegion, along with the ignore block.
                var regionBlocks = await CountRegion(definedRegion, maskSet, ignoreBlock);

                // Group the blocks by block type (Item2 in the tuple) and count them.
                var blockCounts = regionBlocks
                    .GroupBy(t => t.Item2)
                    .Select(g => new { BlockType = g.Key, Count = g.Count() });

                // Output the results, displaying each unique block's count on a new line.
                if (regionBlocks.Count > 0)
                {
                    SendFeedback("Blocks found matching the criteria:");
                    foreach (var block in blockCounts)
                    {
                        SendFeedback($"Block ID {block.BlockType}: {block.Count}.");
                    }
                }
                else
                    SendFeedback($"{regionBlocks.Count} blocks found matching the criteria.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /distr

        [Command("//distr")]
        [Command("/distr")]
        private void ExecuteDistr(string[] args)
        {
            bool fromClipboard = false;
            int  page          = 1;

            if (args.Length == 1)
            {
                if (args[0].Equals("clipboard", StringComparison.OrdinalIgnoreCase))
                    fromClipboard = true;
                else if (!int.TryParse(args[0], out page) || page < 1)
                {
                    SendFeedback("ERROR: Page must be a positive integer.");
                    return;
                }
            }
            else if (args.Length >= 2)
            {
                if (!args[0].Equals("clipboard", StringComparison.OrdinalIgnoreCase))
                {
                    SendFeedback("ERROR: Command usage /distr (clipboard) (page)");
                    return;
                }
                fromClipboard = true;

                if (!int.TryParse(args[1], out page) || page < 1)
                {
                    SendFeedback("ERROR: Page must be a positive integer.");
                    return;
                }
            }

            // Get IDs: Clipboard or live selection.
            IEnumerable<int> idStream;

            if (fromClipboard)
            {
                if (copiedRegion == null || copiedRegion.Count == 0)
                {
                    SendFeedback("ERROR: The clipboard is empty. Try using /copy first.");
                    return;
                }
                idStream = copiedRegion.Select(t => t.Item2);
            }
            else
            {
                if (_pointToLocation1 == default || _pointToLocation2 == default)
                {
                    SendFeedback("ERROR: You must set /pos1 and /pos2 first.");
                    return;
                }

                Vector3 min = Vector3.Min(_pointToLocation1, _pointToLocation2);
                Vector3 max = Vector3.Max(_pointToLocation1, _pointToLocation2);
                idStream    = EnumerateIdsInRegion(min, max);
            }

            // Count occurrences.
            var counts = new Dictionary<int, long>();
            long total = 0;

            foreach (int id in idStream)
            {
                counts[id] = counts.TryGetValue(id, out long c) ? c + 1 : 1;
                total++;
            }

            if (total == 0)
            {
                SendFeedback("Nothing to count: the chosen region is empty.");
                return;
            }

            // Paging.
            const int rowsPerPage = 4;
            int totalPages = (counts.Count + rowsPerPage - 1) / rowsPerPage;

            if (page > totalPages)
            {
                SendFeedback($"ERROR: Page {page} is out of range (max {totalPages}).");
                return;
            }

            var ordered = counts.OrderByDescending(p => p.Value)
                                .Skip((page - 1) * rowsPerPage)
                                .Take(rowsPerPage);

            // Build report.
            var sb = new StringBuilder();
            sb.AppendLine("----------------------------------------------------");
            sb.AppendLine($"Block distribution ({total:N0} blocks total) | Page {page}/{totalPages}.");
            sb.AppendLine("ID / Name                   Count      Share");
            sb.AppendLine("----------------------------------------------------");

            foreach (var pair in ordered)
            {
                int   id    = pair.Key;
                long  count = pair.Value;
                double pct  = (double)count / total;

                string name = Enum.IsDefined(typeof(DNA.CastleMinerZ.Terrain.BlockTypeEnum), id)
                            ? Enum.GetName(typeof(DNA.CastleMinerZ.Terrain.BlockTypeEnum), id)
                            : "Unknown";

                sb.AppendLine($"{id,3} {name,-18} {count,10:N0}    {pct,7:P2}.");
            }

            sb.Append("----------------------------------------------------");
            SendFeedback(sb.ToString());
        }
        #endregion

        #region /expand

        [Command("//expand")]
        [Command("/expand")]
        private void ExecuteExpand(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /expand [amount(vert)] (direction)");
                return;
            }

            try
            {
                // Check for the vertical keyword. If so, only vertically expand the selection to world limits.
                if (args[0].Equals("vert", StringComparison.OrdinalIgnoreCase))
                {
                    Vector3 minVert = Vector3.Min(_pointToLocation1, _pointToLocation2);
                    Vector3 maxVert = Vector3.Max(_pointToLocation1, _pointToLocation2);

                    minVert.Y = WorldHeights.MinY;
                    maxVert.Y = WorldHeights.MaxY;

                    _pointToLocation1 = minVert;
                    _pointToLocation2 = maxVert;

                    SendFeedback(
                        $"Expanded selection vertically to world limits ({WorldHeights.MinY} ? {WorldHeights.MaxY}).");
                    return;
                }

                if (!int.TryParse(args[0], out int amount) || amount < 0)
                {
                    SendFeedback("ERROR: [amount] must be a positive integer.");
                    return;
                }

                // Parse optional direction (null ? all faces).
                Direction? direction = null;
                if (args.Length >= 2 &&
                    !string.IsNullOrWhiteSpace(args[1]) &&
                    !args[1].Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(args[1], true, out Direction parsedDir))
                {
                    direction = parsedDir;
                }

                // Work with *min* and *max* so we don't care which corner was set first.
                Vector3 min = Vector3.Min(_pointToLocation1, _pointToLocation2);
                Vector3 max = Vector3.Max(_pointToLocation1, _pointToLocation2);

                void growMin (ref float face, int by) => face -= by;
                void growMax (ref float face, int by) => face += by;

                if (direction is null) // expand every face.
                {
                    growMin(ref min.X, amount); growMax(ref max.X, amount);
                    growMin(ref min.Y, amount); growMax(ref max.Y, amount);
                    growMin(ref min.Z, amount); growMax(ref max.Z, amount);
                }
                else                   // expand one face only.
                {
                    switch (direction.Value)
                    {
                        case Direction.posX: growMax(ref max.X, amount); break;
                        case Direction.negX: growMin(ref min.X, amount); break;
                        case Direction.Up:   growMax(ref max.Y, amount); break;
                        case Direction.Down: growMin(ref min.Y, amount); break;
                        case Direction.posZ: growMax(ref max.Z, amount); break;
                        case Direction.negZ: growMin(ref min.Z, amount); break;
                    }
                }

                // Ensure the expansion does not exceed world height limits.
                ClampToWorldHeight(ref min.Y, ref max.Y);

                // Persist the new selection back to the global points.
                _pointToLocation1 = min;
                _pointToLocation2 = max;

                // Feedback.
                string dirText = direction?.ToString() ?? "all faces";
                SendFeedback(
                    $"Expanded selection by {amount} block(s) on {dirText}.");
                    // $"New region: ({Math.Round(min.X)}, {Math.Round(min.Y)}, {Math.Round(min.Z)}) -> " +
                    // $"({Math.Round(max.X)}, {Math.Round(max.Y)}, {Math.Round(max.Z)}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Region Commands.

        #region /set

        [Command("//set")]
        [Command("/set")]
        private static async Task ExecuteSet(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /set [block(,array)] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                bool hollow = args.Length > 1 && args[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Check and make sure the region contains less than a million blocks.
                // Make 'No' the highlighted option. Helps mitigate issues when using '/tool'.
                if (CalculateBlockCount(definedRegion.Position1, definedRegion.Position2) > 1000000 &&
                    MessageBox.Show("This region contains over a million blocks.\n\nDo you want to continue anyways?",
                                    "WE: Woah! That's a ton of blocks!",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button2) == DialogResult.No)
                {
                    SendFeedback("Operation canceled.");
                    return;
                }

                // FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                // Check if the to-block pattern is only air, and if so, have the region skip saving it.
                var region = (blockPatternNumbers.Length == 1 && blockPatternNumbers[0] == AirID) ? await FillRegion(definedRegion, hollow, AirID) : await FillRegion(definedRegion, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /line

        [Command("//line")]
        [Command("/line")]
        private async Task ExecuteLine(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /line [block(,array)] (thickness)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int thickness = args.Length > 1 && int.TryParse(args[1], out int t) ? t : 0;

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // MakeLine(Region region, int thickness).
                var region = await MakeLine(definedRegion, thickness);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /replace

        [Command("//replace")]
        [Command("/replace")]
        [Command("//rep")]
        [Command("/rep")]
        [Command("//re")]
        [Command("/re")]
        private static async Task ExecuteReplace(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /replace [source block,(all)] [to block,(all)]");
                return;
            }

            try
            {
                string searchPattern = int.TryParse(args[0], out int r) ? r.ToString() : !string.IsNullOrEmpty(args[0]) ? args[0] : "-1"; // Use an invalid id so it fails.
                string replacePattern = !string.IsNullOrEmpty(args[1]) ? args[1] : "1";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] searchPatternNumbers = (searchPattern == "all") ? Array.Empty<int>() : GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(searchPattern, BlockIDValues);
                if (searchPattern != "all" && searchPatternNumbers.Length == 0) return;   // Make sure the input is within the min/max.
                int[] replacePatternNumbers = (replacePattern == "all") ? Array.Empty<int>() : GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePattern != "all" && replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Use fill region to define a rectangular area to search in.
                // FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                var region = await FillRegion(definedRegion, false);

                // Save the existing region and clear the upcoming redo.
                if (searchPattern == "all")
                    await SaveUndo(region);
                else
                    await SaveUndo(region, saveBlock: searchPatternNumbers);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get the current block type.
                    int currentBlock = GetBlockFromLocation(blockLocation);

                    // Check if the current block is a block to replace.
                    if ((searchPattern == "all" && currentBlock != AirID) || searchPatternNumbers.Contains(currentBlock)) // Make sure not to replace 'air' when using 'all' mode.
                    {
                        // Get random block from input.
                        HashSet<int> excludedBlocks = new HashSet<int> { AirID, 26 }; // IDs to exclude. Block ID 26 'Torch' crashes.
                        int replaceBlock = (replacePattern == "all") ? GetRandomBlock(excludedBlocks) : GetRandomBlockFromPattern(replacePatternNumbers);

                        // Place block if it doesn't already exist. (improves the performance).
                        if (GetBlockFromLocation(blockLocation) != replaceBlock)
                        {
                            // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                            if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                                !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)replaceBlock))
                                TryDestroyCrateAt(blockLocation);

                            AsyncBlockPlacer.Enqueue(blockLocation, replaceBlock);

                            // Add block to redo.
                            redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, replaceBlock));
                        }
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /allexcept

        [Command("//allexcept")]
        [Command("/allexcept")]
        [Command("//allex")]
        [Command("/allex")]
        private static async Task ExecuteAllExcept(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /allexcept [source block(,array)] (to block(,array))");
                return;
            }

            try
            {
                string exceptPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                string replacePattern = args.Length > 1 && !string.IsNullOrEmpty(args[1]) ? args[1] : "0";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] exceptPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(exceptPattern);
                if (exceptPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                int[] replacePatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Use fill region to define a rectangular area to search in.
                // FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                var region = await FillRegion(definedRegion, false);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get the current block type.
                    int currentBlock = GetBlockFromLocation(blockLocation);

                    // Convert string to a list of integers.
                    var excludedBlocks = exceptPattern.Split(',').Select(int.Parse).ToList();

                    // Check if the current block is not excluded, and its not air, place new block.
                    if ((!excludedBlocks.Contains(currentBlock)) && currentBlock != AirID)
                    {
                        // Get random block from input.
                        int replaceBlock = GetRandomBlockFromPattern(replacePatternNumbers);

                        // Place block if it doesn't already exist. (improves the performance).
                        if (GetBlockFromLocation(blockLocation) != replaceBlock)
                        {
                            // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                            if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                                !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)replaceBlock))
                                TryDestroyCrateAt(blockLocation);

                            AsyncBlockPlacer.Enqueue(blockLocation, replaceBlock);

                            // Add block to redo.
                            redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, replaceBlock));
                        }
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /overlay

        [Command("//overlay")]
        [Command("/overlay")]
        private async Task ExecuteOverlay(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /overlay [replace block(,array)]");
                return;
            }

            try
            {
                string replacePattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "0";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] replacePatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // OverlayObject(Region region, List<int> replaceBlockPattern).
                var replaceBlockPattern = replacePattern.Split(',').Select(int.Parse).ToList();
                var region = await OverlayObject(definedRegion, replaceBlockPattern);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(replacePatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /naturalize

        [Command("//naturalize")]
        [Command("/naturalize")]
        [Command("//natur")]
        [Command("/natur")]
        private async Task ExecuteNaturalize()
        {
            try
            {
                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Build deterministic actions (pos -> new block).
                // NaturalizeTerrain(Region region).
                var naturalizedTerrain = await NaturalizeTerrain(definedRegion);

                if (naturalizedTerrain.Count == 0)
                {
                    SendFeedback("Nothing to change.");
                    return;
                }

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(ExtractVector3HashSet(naturalizedTerrain));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var a in naturalizedTerrain)
                {
                    Vector3 pos = a.Item1;
                    int block = a.Item2;

                    // Place block if it doesn't already exist. (improves the performance).
                    if (GetBlockFromLocation(pos) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(pos)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(pos);

                        AsyncBlockPlacer.Enqueue(pos, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(pos, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"Replaced {redoBuilder.Count} block(s).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /walls

        [Command("//walls")]
        [Command("/walls")]
        private async Task ExecuteWalls(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /walls [block(,array)]");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // MakeWalls(Region region).
                var region = await MakeWalls(definedRegion);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance).
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /smooth

        [Command("//smooth")]
        [Command("/smooth")]
        private async Task ExecuteSmooth(string[] args)
        {
            try
            {
                int iterations = args.Length > 0 && int.TryParse(args[0], out int i) ? i : 1;

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // SmoothTerrain(Region region, int iterations).
                var smoothedTerrain = await SmoothTerrain(definedRegion, iterations);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(smoothedTerrain));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var t in smoothedTerrain)
                {
                    // Get location of block and block ID.
                    Vector3 blockLocation = t.Item1;
                    int block = t.Item2;

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /move

        [Command("//move")]
        [Command("/move")]
        private async Task ExecuteMove(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /move [amount] (direction)");
                return;
            }

            try
            {
                int moveAmount = int.TryParse(args[0], out int m) ? m : 1;

                // Check if the move amount is greater then zero.
                if (moveAmount < 1)
                {
                    SendFeedback($"ERROR: 'Amount' must be greater then '0'!");
                    return;
                }

                // Default settings.
                Direction moveDirection = Direction.posX;

                // If only amount is provided, derive direction from the user's cursor.
                if (args.Length == 1)
                {
                    Vector3 cursorLocation = GetUsersCursorLocation();
                    moveDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                }
                // Otherwise, try to parse the given direction.
                else if (args.Length >= 2)
                {
                    if (!Enum.TryParse<Direction>(args[1], true, out moveDirection))
                    {
                        // Fallback if parsing fails.
                        Vector3 cursorLocation = GetUsersCursorLocation();
                        moveDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                    }
                }

                // Define the region to move using the two global points.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Calculate the move offset based on the direction and amount.
                Vector3 moveOffset = GetDirectionalUnitOffset(moveDirection) * moveAmount;

                // MoveRegion(Region region, Vector3 moveOffset).
                var originalBlocks = await MoveRegion(definedRegion, moveOffset);

                await SaveUndo(ExtractVector3HashSet(originalBlocks));
                ClearRedo();

                // Move crate inventories alongside the blocks (container blocks keep their contents).
                MoveCrateContents(definedRegion, moveOffset);

                // Tracks any location that has a non-air "write" in this move operation.
                HashSet<Vector3> nonAirTargets = new HashSet<Vector3>();

                // Iterate over each block and perform the move.
                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var i in originalBlocks)
                {
                    Vector3 blockLocation = i.Item1; // Get location of block.
                    int block = i.Item2;             // Get block from input.

                    // Record that this position is supposed to end up as a real block.
                    if (block != AirID)
                        nonAirTargets.Add(blockLocation);

                    // If this op is an Air write, but we already know this position has a non-air target,
                    // skip it so Air can't override the real block in overlap moves.
                    if (block == AirID && nonAirTargets.Contains(blockLocation))
                        continue;

                    // Get block from location.
                    int blockAtLocation = GetBlockFromLocation(blockLocation);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (blockAtLocation != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                await SaveUndo(redoBuilder);
                SendFeedback($"{originalBlocks.Count / 2} blocks have been moved!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /stack

        [Command("//stack")]
        [Command("/stack")]
        private async Task ExecuteStack(string[] args)
        {
            try
            {
                // Default settings.
                bool useAir = true; // Enabled by default.
                int stackCount = args.Length > 0 && int.TryParse(args[0], out int s) ? s : 1;
                Direction stackDirection = Direction.posX;

                // If only the amount is provided, use the cursor location to determine the direction.
                if (args.Length == 1)
                {
                    Vector3 cursorLocation = GetUsersCursorLocation();
                    stackDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                }
                else if (args.Length == 2)
                {
                    // Try to determine if the second argument is a boolean.
                    if (bool.TryParse(args[1], out bool parsedBool))
                    {
                        // When the boolean parses successfully, it means the user omitted a direction.
                        useAir = parsedBool;
                        Vector3 cursorLocation = GetUsersCursorLocation();
                        stackDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                    }
                    else
                    {
                        // Otherwise, assume the second argument is a direction.
                        if (!Enum.TryParse<Direction>(args[1], true, out stackDirection))
                        {
                            // If parsing fails, fall back to the cursor location.
                            Vector3 cursorLocation = GetUsersCursorLocation();
                            stackDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                        }
                    }
                }
                else if (args.Length >= 3)
                {
                    // Assume the user provided both a direction and a useAir flag.
                    if (!Enum.TryParse<Direction>(args[1], true, out stackDirection))
                    {
                        // If the direction string doesn't match, fallback to the cursor direction.
                        Vector3 cursorLocation = GetUsersCursorLocation();
                        stackDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                    }
                    // Parse the boolean from the third argument.
                    if (bool.TryParse(args[2], out bool parsedAir))
                    {
                        useAir = parsedAir;
                    }
                }

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // StackRegion(Region region, Direction facingDirection, int stackCount, bool useAir = true).
                var stackedBlocks = await StackRegion(definedRegion, stackDirection, stackCount, useAir);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(stackedBlocks));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var i in stackedBlocks)
                {
                    // Get location of block.
                    Vector3 blockLocation = i.Item1;

                    // Get block from location.
                    int blockAtLocation = GetBlockFromLocation(blockLocation);

                    // Get block from input.
                    int block = i.Item2;

                    // Place block if it doesn't already exist. (improves the performance)
                    if (blockAtLocation != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Stack crate inventories too (container blocks keep their contents).
                // NOTE: This preserves the user's clipboard (/copy) sidecar.
                StackCrateContents(definedRegion, stackDirection, stackCount, overwriteExisting: true);

                // Save the builder to new redo.
                await SaveUndo(redoBuilder);

                SendFeedback($"{stackedBlocks.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /regen

        /// <summary>
        /// /regen (seed)
        ///
        /// Regenerates terrain from the vanilla world generator, but ONLY within the
        /// current selection (/pos1 and /pos2).
        ///
        /// Optional:
        ///   (seed) -> Uses a temporary/scratch world builder seeded with this value
        ///             WITHOUT modifying the real world seed or the live terrain builder.
        /// </summary>
        [Command("//regen")]
        [Command("/regen")]
        private async Task ExecuteRegen(string[] args)
        {
            // Capture originals so we can sanity-check / re-stabilize state.
            var terrain = DNA.CastleMinerZ.Terrain.BlockTerrain.Instance;
            if (terrain == null || terrain.WorldInfo == null || terrain._worldBuilder == null)
            {
                SendFeedback("ERROR: /regen - terrain or world builder not ready.");
                return;
            }

            int originalWorldSeed = terrain.WorldInfo.Seed;

            // Parse optional seed override.
            bool hasSeedOverride = false;
            int regenSeed = originalWorldSeed;

            if (args.Length > 0)
            {
                if (!int.TryParse(args[0], out regenSeed))
                {
                    SendFeedback("ERROR: /regen (seed) - seed must be an integer.");
                    return;
                }
                hasSeedOverride = true;
            }

            // Require selection (the /chunk can set this, so a chunk-radius mode is unnecessary).
            if (_pointToLocation1 == default || _pointToLocation2 == default)
            {
                SendFeedback("ERROR: /regen - You must set /pos1 and /pos2 (or use /chunk) first.");
                return;
            }

            try
            {
                #region Async Pacing (Budget Stopwatch + Helpers)

                /// <summary>
                /// Summary: Starts a stopwatch used to enforce a per-frame time budget,
                /// so long-running loops can yield to the next frame instead of freezing the game.
                /// </summary>
                var budget = System.Diagnostics.Stopwatch.StartNew();

                /// <summary>
                /// Summary: Cooperative "time-slice" helper - yields to the next frame once we exceed a per-frame time budget.
                /// ~6ms per frame keeps things responsive; tweak 4-10ms depending on feel.
                /// </summary>
                async Task YieldIfOverBudget(System.Diagnostics.Stopwatch sw, int budgetMs = 6)
                {
                    if (sw.ElapsedMilliseconds >= budgetMs)
                    {
                        sw.Restart();
                        await AsyncFrameYield.NextFrame();
                    }
                }
                #endregion

                #region Selection Bounds + Chunk Bounds

                int sizeX = ChunkSize.WidthX;  // 16.
                int sizeZ = ChunkSize.LengthZ; // 16.

                int baseY = WorldHeights.MinY;
                int maxYWorld = WorldHeights.MaxY;

                // Normalize selection bounds (IMPORTANT if pos1 > pos2).
                int selMinX = (int)Math.Min(_pointToLocation1.X, _pointToLocation2.X);
                int selMaxX = (int)Math.Max(_pointToLocation1.X, _pointToLocation2.X);

                int selMinY = (int)Math.Min(_pointToLocation1.Y, _pointToLocation2.Y);
                int selMaxY = (int)Math.Max(_pointToLocation1.Y, _pointToLocation2.Y);

                int selMinZ = (int)Math.Min(_pointToLocation1.Z, _pointToLocation2.Z);
                int selMaxZ = (int)Math.Max(_pointToLocation1.Z, _pointToLocation2.Z);

                // Clamp Y to world bounds (optional safety).
                if (selMinY < baseY) selMinY = baseY;
                if (selMaxY > maxYWorld) selMaxY = maxYWorld;

                int minChunkX = FloorDiv(selMinX, sizeX);
                int maxChunkX = FloorDiv(selMaxX, sizeX);
                int minChunkZ = FloorDiv(selMinZ, sizeZ);
                int maxChunkZ = FloorDiv(selMaxZ, sizeZ);

                #endregion

                #region Build A SCRATCH Builder (Never Touch Live WorldInfo / Live _worldBuilder)

                // Clone WorldInfo so any generator side-effects (spawners/crates/etc) never hit the real world info.
                DNA.CastleMinerZ.WorldInfo scratchInfo = new DNA.CastleMinerZ.WorldInfo(terrain.WorldInfo);

                // If you want a different seed, set it on the CLONE only (Seed is read-only, private field is "_seed").
                if (hasSeedOverride)
                {
                    var seedField = typeof(DNA.CastleMinerZ.WorldInfo).GetField("_seed",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    if (seedField == null)
                    {
                        SendFeedback("ERROR: /regen - Could not reflect WorldInfo._seed.");
                        return;
                    }

                    seedField.SetValue(scratchInfo, regenSeed);
                }

                // New builder instance backed by scratchInfo (does not affect terrain._worldBuilder).
                var scratchBuilder = scratchInfo.GetBuilder();

                #endregion

                #region Pre-Pass: Capture Before + TargetPositions (Selection Only)

                var beforeMap = new Dictionary<Vector3, int>();
                var targetPositions = new HashSet<Vector3>();

                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
                    {
                        int chunkMinX = chunkX * sizeX;
                        int chunkMaxX = chunkMinX + sizeX - 1;

                        int chunkMinZ = chunkZ * sizeZ;
                        int chunkMaxZ = chunkMinZ + sizeZ - 1;

                        for (int y = baseY; y <= maxYWorld; y++)
                        {
                            // Periodically yield while scanning vertical layers to keep the game responsive during large selections.
                            if ((y & 7) == 0) // Every 8 layers.
                                await YieldIfOverBudget(budget);

                            for (int x = chunkMinX; x <= chunkMaxX; x++)
                            {
                                for (int z = chunkMinZ; z <= chunkMaxZ; z++)
                                {
                                    Vector3 pos = new Vector3(x, y, z);

                                    // Skip blocks outside the current 24x24 terrain window.
                                    DNA.IntVector3 wp = DNA.IntVector3.FromVector3(pos);
                                    int idx = terrain.MakeIndexFromWorldIndexVector(wp);
                                    if (idx < 0)
                                        continue;

                                    int data = terrain.GetBlockAt(idx);
                                    int oldId = (int)DNA.CastleMinerZ.Terrain.Block.GetTypeIndex(data);
                                    beforeMap[pos] = oldId;

                                    // Target ONLY inside the selection.
                                    if (x >= selMinX && x <= selMaxX &&
                                        y >= selMinY && y <= selMaxY &&
                                        z >= selMinZ && z <= selMaxZ)
                                    {
                                        targetPositions.Add(pos);
                                    }
                                }
                            }
                        }

                        // Yield after each chunk (or each few chunks) to avoid freezing.
                        await YieldIfOverBudget(budget);
                    }
                }

                if (targetPositions.Count == 0)
                {
                    SendFeedback("Regen: No valid blocks found inside the requested selection.");
                    return;
                }

                await SaveUndo(targetPositions);
                ClearRedo();

                #endregion

                #region Per-Chunk: Scratch Build + After Snapshot + Restore Before

                var afterMap = new Dictionary<Vector3, int>(beforeMap.Count);
                int chunkCount = 0;

                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
                    {
                        int chunkMinX = chunkX * sizeX;
                        int chunkMinZ = chunkZ * sizeZ;

                        var chunkCorner = new DNA.IntVector3(chunkMinX, baseY, chunkMinZ);

                        // Convert chunkCorner (world) -> index-space region [16x128x16].
                        Vector3 chunkCornerVec3 = DNA.IntVector3.ToVector3(chunkCorner);
                        DNA.IntVector3 regionMin = terrain.MakeIndexVectorFromPosition(chunkCornerVec3);
                        regionMin.Y = 0;

                        DNA.IntVector3 regionMax = new DNA.IntVector3(
                            regionMin.X + sizeX - 1,
                            127,
                            regionMin.Z + sizeZ - 1);

                        if (!terrain.IsIndexValid(regionMin) || !terrain.IsIndexValid(regionMax))
                        {
                            // Chunk is outside the loaded 24x24 window; skip it entirely.
                            continue;
                        }

                        chunkCount++;

                        // 1) Fill this chunk region with NumberOfBlocks as a sentinel.
                        terrain.FillRegion(regionMin, regionMax, DNA.CastleMinerZ.Terrain.BlockTypeEnum.NumberOfBlocks);

                        // 2) Populate chunk using the SCRATCH builder (seed override lives here only).
                        scratchBuilder.BuildWorldChunk(terrain, chunkCorner);

                        // 3) Replace leftover sentinel with Empty.
                        terrain.ReplaceRegion(
                            regionMin,
                            regionMax,
                            DNA.CastleMinerZ.Terrain.BlockTypeEnum.NumberOfBlocks,
                            DNA.CastleMinerZ.Terrain.BlockTypeEnum.Empty);

                        // 4) Snapshot generated types + restore original types (so scratch writes leave no trace).
                        for (int y = baseY; y <= maxYWorld; y++)
                        {
                            // Yield more frequently during per-chunk snapshot/restore to reduce frame hitches while iterating dense block volumes.
                            if ((y & 3) == 0) // Every 4 layers.
                                await YieldIfOverBudget(budget);

                            for (int x = chunkMinX; x <= chunkMinX + sizeX - 1; x++)
                            {
                                for (int z = chunkMinZ; z <= chunkMinZ + sizeZ - 1; z++)
                                {
                                    Vector3 pos = new Vector3(x, y, z);

                                    if (!beforeMap.ContainsKey(pos))
                                        continue;

                                    DNA.IntVector3 wp = DNA.IntVector3.FromVector3(pos);
                                    int idx = terrain.MakeIndexFromWorldIndexVector(wp);
                                    if (idx < 0)
                                        continue;

                                    int data = terrain.GetBlockAt(idx);
                                    int newId = (int)DNA.CastleMinerZ.Terrain.Block.GetTypeIndex(data);
                                    afterMap[pos] = newId;

                                    int oldId = beforeMap[pos];
                                    int oldDat = DNA.CastleMinerZ.Terrain.Block.SetType(0, (DNA.CastleMinerZ.Terrain.BlockTypeEnum)oldId);
                                    terrain.SetBlockAt(idx, oldDat);
                                }
                            }
                        }

                        //Yield here if we've exceeded the per-frame time budget, letting the next frame run to avoid stalling.
                        await YieldIfOverBudget(budget);
                    }
                }
                #endregion

                #region Apply Differences Inside Selection + Redo

                var redoBuilder = new HashSet<Tuple<Vector3, int>>();
                int changedCount = 0;

                foreach (Vector3 pos in targetPositions)
                {
                    int oldId = beforeMap[pos];

                    if (!afterMap.TryGetValue(pos, out int newId))
                        continue;

                    if (newId == oldId)
                        continue;

                    AsyncBlockPlacer.Enqueue(pos, newId);

                    redoBuilder.Add(new Tuple<Vector3, int>(pos, newId));
                    changedCount++;
                }

                await SaveUndo(redoBuilder);

                SendFeedback(
                    $"Rebuilt {chunkCount} chunk(s) using seed {regenSeed} " +
                    $"over {targetPositions.Count} selected block(s), {changedCount} changed.");

                #endregion
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
            finally
            {
                // Safety: If the live builder had somehow previously been mutated,
                // re-create it from the real WorldInfo so future chunk-gen uses the real seed again.
                // (This does NOT change the world seed; it just refreshes the generator instance.)
                try
                {
                    if (terrain != null && terrain.WorldInfo != null)
                        terrain._worldBuilder = terrain.WorldInfo.GetBuilder();
                }
                catch { }

                // Optional sanity warning if something *did* change your real world seed.
                try
                {
                    if (terrain != null && terrain.WorldInfo != null && terrain.WorldInfo.Seed != originalWorldSeed)
                        SendFeedback($"WARN: World seed changed unexpectedly ({originalWorldSeed} -> {terrain.WorldInfo.Seed}).");
                }
                catch { }
            }
        }
        #endregion

        #region /stretch

        [Command("//stretch")]
        [Command("/stretch")]
        [Command("//str")]
        [Command("/str")]
        private async Task ExecuteStretch(string[] args)
        {
            try
            {
                // Default settings.
                bool useAir = true; // Enabled by default.
                double stretchFactor = args.Length > 0 && double.TryParse(args[0], out double f) ? f : 1.0;
                Direction stretchDirection = Direction.posX;

                // If only the amount is provided, use the cursor location to determine the direction.
                if (args.Length == 1)
                {
                    Vector3 cursorLocation = GetUsersCursorLocation();
                    stretchDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                }
                else if (args.Length == 2)
                {
                    // Try to determine if the second argument is a boolean.
                    if (bool.TryParse(args[1], out bool parsedBool))
                    {
                        // When the boolean parses successfully, it means the user omitted a direction.
                        useAir = parsedBool;
                        Vector3 cursorLocation = GetUsersCursorLocation();
                        stretchDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                    }
                    else
                    {
                        // Otherwise, assume the second argument is a direction.
                        if (!Enum.TryParse<Direction>(args[1], true, out stretchDirection))
                        {
                            // If parsing fails, fall back to the cursor location.
                            Vector3 cursorLocation = GetUsersCursorLocation();
                            stretchDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                        }
                    }
                }
                else if (args.Length >= 3)
                {
                    // Assume the user provided both a direction and a useAir flag.
                    if (!Enum.TryParse<Direction>(args[1], true, out stretchDirection))
                    {
                        // If the direction string doesn't match, fallback to the cursor direction.
                        Vector3 cursorLocation = GetUsersCursorLocation();
                        stretchDirection = GetFacingDirection(_pointToLocation1, cursorLocation);
                    }
                    // Parse the boolean from the third argument.
                    if (bool.TryParse(args[2], out bool parsedAir))
                    {
                        useAir = parsedAir;
                    }
                }

                // Define the selection region.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Ensure the stretchDirection is valid.
                HashSet<Tuple<Vector3, int>> stretchedBlocks;
                if (stretchDirection == Direction.posX || stretchDirection == Direction.negX
                    || stretchDirection == Direction.posZ || stretchDirection == Direction.negZ
                    || stretchDirection == Direction.Up || stretchDirection == Direction.Down)
                {
                    // StretchRegion(Region region, Direction stretchDirection, double stretchFactor, bool useAir = true).
                    stretchedBlocks = await StretchRegion(definedRegion, stretchDirection, stretchFactor, useAir);
                }
                else
                {
                    // An invalid direction was thrown. This should never happen unless its 4D. (ex: posW, negW).
                    SendFeedback($"ERROR: Invalid direction.");
                    return;
                }

                // Save current state for undo and clear any existing redo history.
                await SaveUndo(ExtractVector3HashSet(stretchedBlocks));
                ClearRedo();

                // Apply the changed blocks.
                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var tuple in stretchedBlocks)
                {
                    Vector3 newLocation = tuple.Item1;
                    int blockType = tuple.Item2;

                    if (GetBlockFromLocation(newLocation) != blockType)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(newLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)blockType))
                            TryDestroyCrateAt(newLocation);

                        AsyncBlockPlacer.Enqueue(newLocation, blockType);
                        redoBuilder.Add(new Tuple<Vector3, int>(newLocation, blockType));
                    }
                }

                await SaveUndo(redoBuilder);
                SendFeedback($"{stretchedBlocks.Count} blocks have been modified!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /spell

        [Command("//spell")]
        [Command("/spell")]
        private async Task ExecuteSpell(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /spell [(\")words(\")(@linebreak)/(/paste)] [block(,array)] (flip) (rot|rotate) (90|180|270|360)");
                return;
            }

            try
            {
                int index = 0;
                string words;

                // Parse the words argument (supports "quoted phrases").
                if (!string.IsNullOrEmpty(args[0]) && args[0].StartsWith("\""))
                {
                    var sb = new StringBuilder();

                    // Strip leading quote from the first token.
                    string first = args[0].Substring(1);

                    // Case 1: The phrase is a single word.
                    if (first.EndsWith("\""))
                    {
                        // Remove trailing quote too.
                        first = first.Substring(0, first.Length - 1);
                        sb.Append(first);
                        index = 1; // Next arg after the quoted phrase.
                    }
                    else
                    {
                        // Case 2: Multi-word phrase:
                        sb.Append(first);

                        for (index = 1; index < args.Length; index++)
                        {
                            string part = args[index];
                            bool endsWithQuote = part.EndsWith("\"");

                            // Add space before every subsequent token.
                            sb.Append(' ');

                            if (endsWithQuote)
                            {
                                // Remove the trailing quote.
                                string trimmed = part.Substring(0, part.Length - 1);
                                sb.Append(trimmed);
                                index++; // Move to arg after the closing quote.
                                break;
                            }
                            else
                            {
                                sb.Append(part);
                            }
                        }
                    }

                    words = sb.ToString();
                }
                else
                {
                    // No quotes -> first token is the word string.
                    words = args[0];
                    index = 1;
                }

                string blockPattern = !string.IsNullOrEmpty(args[index]) ? args[index] : "1";
                index++;

                // Optional switches:
                //  - flip:       Place the whole line on the OTHER side of /pos (without mirroring letters).
                //  - rot/rotate: Rotate text around Y by 0/90/180/270/360 degrees (360 == 0).
                bool flipSide = false;
                int rotateDeg = 0;

                // Legacy support:
                //   /spell "hi" dirt true true  => flipSide=true, rotateDeg=90.
                bool legacyFlipSeen = false;
                bool legacyRotSeen = false;

                while (index < args.Length)
                {
                    string raw = args[index] ?? string.Empty;
                    string t = raw.Trim();

                    if (t.Length == 0)
                    {
                        index++;
                        continue;
                    }

                    // Keyword: flip.
                    if (t.Equals("flip", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("f", StringComparison.OrdinalIgnoreCase))
                    {
                        flipSide = true;
                        index++;
                        continue;
                    }

                    // Keyword: rotate / rot / r (optionally followed by degrees).
                    if (t.Equals("rotate", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("rot", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("r", StringComparison.OrdinalIgnoreCase))
                    {
                        int deg = 90; // Default if no degrees provided.
                        if (index + 1 < args.Length && TryParseRotationDegrees(args[index + 1], out int parsedDeg))
                        {
                            deg = parsedDeg;
                            index += 2;
                        }
                        else
                        {
                            index++;
                        }

                        rotateDeg = deg;
                        continue;
                    }

                    // Combined forms: rot90, rotate180, rot=270, rotate=90.
                    if (TryParseRotationDegreesFromToken(t, out int tokenDeg))
                    {
                        rotateDeg = tokenDeg;
                        index++;
                        continue;
                    }

                    // Bare degrees: 90/180/270/360.
                    if (TryParseRotationDegrees(t, out int bareDeg))
                    {
                        rotateDeg = bareDeg;
                        index++;
                        continue;
                    }

                    // Legacy: /spell ... true true.
                    if (bool.TryParse(t, out bool b))
                    {
                        if (!legacyFlipSeen)
                        {
                            flipSide = b;
                            legacyFlipSeen = true;
                            index++;
                            continue;
                        }
                        if (!legacyRotSeen)
                        {
                            rotateDeg = b ? 90 : 0;
                            legacyRotSeen = true;
                            index++;
                            continue;
                        }
                    }

                    // Unknown token -> ignore (future-proof).
                    index++;
                }

                // Since "MakeWords" is now a background thread, add support to "Clipboard.GetText()".
                if (string.Equals(words, "/paste", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string clip = Clipboard.GetText();

                        if (string.IsNullOrWhiteSpace(clip))
                        {
                            SendFeedback("ERROR: Clipboard is empty or does not contain text.");
                            return;
                        }

                        words = clip;
                    }
                    catch (Exception ex)
                    {
                        SendFeedback($"ERROR: Failed to read clipboard text: {ex.Message}.");
                        return;
                    }
                }

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeWords(Vector3 pos, string wordString, bool flipSide = false, int rotateDegrees = 0).
                var region = await MakeWords(_pointToLocation1, words, flipSide, rotateDeg);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /hollow

        [Command("//hollow")]
        [Command("/hollow")]
        private async Task ExecuteHollow(string[] args)
        {
            try
            {
                string replacePattern = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : "0";
                int thickness = args.Length > 1 && int.TryParse(args[1], out int t) ? t : 1;

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] replacePatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // HollowObject(Region region, int thickness).
                var replaceBlockPattern = replacePattern.Split(',').Select(int.Parse).ToList();
                var region = await HollowObject(definedRegion, thickness);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(replacePatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /shapefill

        [Command("//shapefill")]
        [Command("/shapefill")]
        private async Task ExecuteShapeFill(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /shapefill [block(,array)]");
                return;
            }

            try
            {
                string replacePattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] replacePatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // ShapeFill(Region region).
                var replaceBlockPattern = replacePattern.Split(',').Select(int.Parse).ToList();
                var region = await ShapeFill(definedRegion);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(replacePatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /wrap

        [Command("//wrap")]
        [Command("/wrap")]
        private async Task ExecuteWrap(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /wrap [replace block(,array)] (wrap direction(s)(all)) (exclude direction(s))");
                return;
            }

            try
            {
                string replacePattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "0";

                // Default settings.
                // wrapDirections:    Null => All directions.
                // excludeDirections: Null/empty => None.
                HashSet<Direction> wrapDirections = null;
                HashSet<Direction> excludeDirections = null;

                if (args.Length >= 2 && !string.IsNullOrEmpty(args[1]))
                {
                    if (!TryParseDirectionList(args[1], out wrapDirections, allowAll: true))
                    {
                        SendFeedback("ERROR: Invalid wrap direction(s). Use e.g. posX,posZ or x+,z- or 'all'.");
                        return;
                    }
                }

                if (args.Length >= 3 && !string.IsNullOrEmpty(args[2]))
                {
                    if (args[2].Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        SendFeedback("ERROR: 'all' is not valid for exclude directions. Use e.g. down,up or d,u.");
                        return;
                    }

                    if (!TryParseDirectionList(args[2], out excludeDirections, allowAll: false))
                    {
                        SendFeedback("ERROR: Invalid exclude direction(s). Use e.g. down,up or d,u or -y,+y.");
                        return;
                    }
                }

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] replacePatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // WrapObject(Region region, List<int> replaceBlockPattern, HashSet<Direction> wrapDirections = null, HashSet<Direction> excludeDirections = null).
                // NOTE: Use the already-resolved block IDs so enum names like "dirt" work.
                var replaceBlockPattern = replacePatternNumbers.Distinct().ToList();
                var region = await WrapObject(definedRegion, replaceBlockPattern, wrapDirections, excludeDirections);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(replacePatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /matrix

        [Command("//matrix")]
        [Command("/matrix")]
        private async Task ExecuteMatrix(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /matrix [radius] [spacing] (snow) (default(,array))");
                return;
            }

            // Ensure the copied clipboard is full.
            if (copiedRegion.Count() == 0)
            {
                SendFeedback("ERROR: You need to first copy a region.");
                return;
            }

            try
            {
                int radius = int.TryParse(args[0], out int r) ? r : 50;
                int spacing = int.TryParse(args[1], out int s) ? s : 8;
                bool snow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);
                string optionalBlockPattern = args.Length > 3 && !string.IsNullOrEmpty(args[3]) ? args[3] : "";

                // Populate optional arguments.
                for (int i = 2; i < args.Length; i++)
                {
                    var a = args[i].Trim();

                    // Check if it's parseable as a bool.
                    if (bool.TryParse(a, out bool parsedSnow))
                    {
                        snow = parsedSnow;
                    }
                    else
                    {
                        // Otherwise, assume it's the block pattern.
                        optionalBlockPattern = a;
                    }
                }

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] optionalBlockPatternNumbers = (!string.IsNullOrEmpty(optionalBlockPattern)) ? GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(optionalBlockPattern) : new int[0];
                // if (optionalBlockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeMatrix(Vector3 pos, int radius, int spacing, bool enableSnow, int[] optionalBlockPattern).
                var region = await MakeMatrix(_pointToLocation1, radius, spacing, snow, optionalBlockPatternNumbers);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(region));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var (blockLocation, block) in region)
                {
                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /forest

        [Command("//forest")]
        [Command("/forest")]
        private async Task ExecuteForest(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /forest [density] (max height) (snow_radius)");
                return;
            }

            try
            {
                // int treeAreaSquared = int.TryParse(args[0], out int a) ? a : 10;
                int treeDensity = int.TryParse(args[0], out int d) ? d : 20;
                int treeMaxHeight = args.Length > 1 && int.TryParse(args[1], out int h) ? h : -1;
                int snowRadius = args.Length > 2 && int.TryParse(args[2], out int s) ? Math.Abs(s) : -1;

                // Define location data.
                HashSet<Tuple<Vector3, int>> region;
                if (snowRadius > 0)
                {
                    Vector3 basePosition = _pointToLocation1;

                    // MakeForest(Vector3 center, int radius, int density, int max_height).
                    region = await MakeForest(basePosition, snowRadius, treeDensity, treeMaxHeight);
                }
                else
                {
                    // Default behavior: Use selected region.
                    Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                    // MakeForest(Region pos, int density, int max_height).
                    region = await MakeForest(definedRegion, treeDensity, treeMaxHeight);
                }

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(region));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var (blockLocation, block) in region)
                {
                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                if (snowRadius > 0)
                    SendFeedback($"Forest built in a circle of radius '{snowRadius}' with density '{treeDensity}' and max tree heights of '{treeMaxHeight}'!");
                else
                    SendFeedback($"Forest built with a density of '{treeDensity}' and with max tree heights of '{treeMaxHeight}'!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /tree

        [Command("//tree")]
        [Command("/tree")]
        private async Task ExecuteTree(string[] args)
        {
            try
            {
                int treeMaxHeight = args.Length > 0 && int.TryParse(args[0], out int h) ? h : -1;

                // MakeTree(int worldX, int worldZ, int maxHeight).
                var region = MakeTree((int)_pointToLocation1.X, (int)_pointToLocation1.Z, treeMaxHeight);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(region));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var (blockLocation, block) in region)
                {
                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"Tree built with a max possible height of '{treeMaxHeight}'!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /break

        [Command("//break")]
        [Command("/break")]
        private static async Task ExecuteBreak(string[] args)
        {
            try
            {
                string blockPattern = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : "";

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] maskArray = (!string.IsNullOrEmpty(blockPattern)) ? GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues) : new int[0];
                // if (maskArray.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);
                HashSet<int> maskSet = new HashSet<int>(maskArray);

                // Check and make sure the region contains less than a million blocks.
                // Make 'No' the highlighted option. Helps mitigate issues when using '/tool'.
                if (CalculateBlockCount(definedRegion.Position1, definedRegion.Position2) > 1000000 &&
                    MessageBox.Show("This region contains over a million blocks.\n\nDo you want to continue anyways?",
                                    "WE: Woah! That's a ton of blocks!",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button2) == DialogResult.No)
                {
                    SendFeedback("Operation canceled.");
                    return;
                }

                // FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                var region = await FillRegion(definedRegion, false, AirID);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Check if the mask is enabled. If not, remove block, if so, use mask.
                    int block = GetBlockFromLocation(blockLocation);
                    if (string.IsNullOrEmpty(blockPattern) || maskSet.Contains(block))
                    {
                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);

                        // Try to map the block to its item id. If valid, drop the block as an item.
                        var blockType = (DNA.CastleMinerZ.Terrain.BlockTypeEnum)block;
                        DropParentItem(blockLocation, blockType);

                        // Legacy drop item (drops raw id, does not map to parent, e.g. CoalOre -> Coal):
                        /*
                        if (TryMapEnum<DNA.CastleMinerZ.Terrain.BlockTypeEnum, DNA.CastleMinerZ.Inventory.InventoryItemIDs>(blockType, out var item))
                            DropItem(blockLocation, (int)item);
                        */

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Generation Commands.

        #region /floor

        [Command("//floor")]
        [Command("/floor")]
        private async Task ExecuteFloor(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /floor [block(,array)] [radius] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radius = int.TryParse(args[1], out int r) ? r : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeFloor(Vector3 pos, int size, bool hollow, int ignoreBlock = -1).
                var region = await MakeFloor(_pointToLocation1, radius, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /cube

        [Command("//cube")]
        [Command("/cube")]
        private async Task ExecuteCube(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /cube [block(,array)] [radii] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radii = int.TryParse(args[1], out int r) ? r : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeCube(Vector3 pos, int radii, bool hollow, int ignoreBlock = -1).
                var region = await MakeCube(_pointToLocation1, radii, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /prism

        [Command("//prism")]
        [Command("/prism")]
        private async Task ExecutePrism(string[] args)
        {
            if (args.Length < 3)
            {
                SendFeedback("ERROR: Command usage /prism [block(,array)] [length] [width] (height) (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int length = int.TryParse(args[1], out int l) ? l : 10;
                int width = int.TryParse(args[2], out int w) ? w : 5;
                int height = args.Length > 3 && int.TryParse(args[3], out int h) ? h : -1; // If not specified, make the triangle equilateral.
                bool hollow = args.Length > 4 && args[4].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeTriangularPrism(Vector3 pos, int length, int width, int height, bool hollow, int ignoreBlock = -1).
                var region = await MakeTriangularPrism(_pointToLocation1, length, width, height, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /sphere

        [Command("//sphere")]
        [Command("/sphere")]
        private async Task ExecuteSphere(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /sphere [block(,array)] [radii] (hollow) (height)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radii = int.TryParse(args[1], out int r) ? r : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);
                int height = args.Length > 3 && int.TryParse(args[3], out int h) ? h : radii;

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeSphere(Vector3 pos, double radiusX, double radiusY, double radiusZ, bool hollow, int ignoreBlock = -1).
                var region = await MakeSphere(_pointToLocation1, radii, height, radii, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /pyramid

        [Command("//pyramid")]
        [Command("/pyramid")]
        private async Task ExecutePyramid(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /pyramid [block(,array)] [size] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int size = int.TryParse(args[1], out int s) ? s : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakePyramid(Vector3 pos, int size, bool hollow, int ignoreBlock = -1).
                var region = await MakePyramid(_pointToLocation1, size, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /cone

        [Command("//cone")]
        [Command("/cone")]
        private async Task ExecuteCone(string[] args)
        {
            if (args.Length < 3)
            {
                SendFeedback("ERROR: Command usage /cone [block(,array)] [radii] [height] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radii = int.TryParse(args[1], out int r) ? r : 5;
                int height = int.TryParse(args[2], out int h) ? h : 10;
                bool hollow = args.Length > 3 && args[3].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeCone(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, double thickness, int ignoreBlock = -1).
                var region = await MakeCone(_pointToLocation1, radii, radii, height, hollow, 1);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /cylinder

        [Command("//cylinder")]
        [Command("/cylinder")]
        [Command("//cyl")]
        [Command("/cyl")]
        private async Task ExecuteCylinder(string[] args)
        {
            if (args.Length < 3)
            {
                SendFeedback("ERROR: Command usage /cylinder [block(,array)] [radii] [height] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radii = int.TryParse(args[1], out int r) ? r : 5;
                int height = int.TryParse(args[2], out int h) ? h : 10;
                bool hollow = args.Length > 3 && args[3].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeCylinder(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, int ignoreBlock = -1).
                var region = await MakeCylinder(_pointToLocation1, radii, radii, height, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /diamond

        [Command("//diamond")]
        [Command("/diamond")]
        private async Task ExecuteDiamond(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /diamond [r block(,array)] [radii] (hollow) (squared)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radii = int.TryParse(args[1], out int r) ? r : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);
                bool squared = args.Length > 3 && args[3].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeDiamond(Vector3 pos, int size, bool hollow, int ignoreBlock = -1).
                var region = await MakeDiamond(_pointToLocation1, radii, hollow, squared);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /ring

        [Command("//ring")]
        [Command("/ring")]
        private async Task ExecuteRing(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /ring [block(,array)] [radius] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radius = int.TryParse(args[1], out int r) ? r : 5;
                bool hollow = args.Length > 2 && args[2].Equals("true", StringComparison.OrdinalIgnoreCase);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeRing(Vector3 pos, double radius, bool hollow, int ignoreBlock = -1).
                var region = await MakeRing(_pointToLocation1, radius, hollow);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /ringarray

        [Command("//ringarray")]
        [Command("/ringarray")]
        [Command("//ringa")]
        [Command("/ringa")]
        private async Task ExecuteRingArray(string[] args)
        {
            if (args.Length < 3)
            {
                SendFeedback("ERROR: Command usage /ringarray [block(,array)] [amount] [space]");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int amount = int.TryParse(args[1], out int a) ? a : 5;
                int space = int.TryParse(args[2], out int s) ? s : 1;

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // MakeRing(Vector3 pos, double radius, bool hollow, int ignoreBlock = -1).
                HashSet<Vector3> region = new HashSet<Vector3>();
                for (int i = 0; i < amount; i++)
                    region.UnionWith(await MakeRing(_pointToLocation1, i * space, true));

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /generate

        [Command("//generate")]
        [Command("/generate")]
        [Command("//gen")]
        [Command("/gen")]
        [Command("//g")]
        [Command("/g")]
        private async Task ExecuteGenerate(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /generate [block(,array)] [expression(clipboard)] (hollow)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                // Determine if the last argument is the hollow flag.
                bool hollowSpecified = args.Length > 1 &&
                    (args[args.Length - 1].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     args[args.Length - 1].Equals("false", StringComparison.OrdinalIgnoreCase));

                bool hollow = false;
                string expression;

                if (hollowSpecified)
                {
                    // Last token is the hollow flag.
                    hollow = bool.Parse(args[args.Length - 1]);
                    // Join all tokens from index 1 to the second-to-last token.
                    expression = string.Join(" ", args, 1, args.Length - 2);
                }
                else
                {
                    // Join all tokens from index 1 onward as the expression.
                    expression = string.Join(" ", args, 1, args.Length - 1);
                }

                // If the user has specified "clipboard" as the expression, fetch the clipboard text.
                if (string.Equals(expression.Trim(), "clipboard", StringComparison.OrdinalIgnoreCase))
                {
                    expression = Clipboard.GetText();
                }

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // MakeShape(Vector3 pos, string expression, bool hollow, int ignoreBlock = -1).
                var region = await MakeShape(definedRegion, expression, hollow);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(region));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var (blockLocation, block) in region)
                {
                    // Check if output is -1, use random block from input.
                    int blockToUse = block;
                    if (block == -1)
                        blockToUse = GetRandomBlockFromPattern(blockPatternNumbers);
                    else
                        if (blockToUse < BlockIDValues.MinID || blockToUse > BlockIDValues.MaxID) // Ensure the returning value is valid.
                            blockToUse = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != blockToUse)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, blockToUse);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, blockToUse));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);
                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Schematic and Clipboard Commands.

        #region /schem

        [Command("//schematic")]
        [Command("/schematic")]
        [Command("//schem")]
        [Command("/schem")]
        private void ExecuteSchematic(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /schematic [save/load] (useAir)");
                return;
            }

            try
            {
                bool useAir = true; // Enabled by default.
                string commandSuffex = !string.IsNullOrEmpty(args[0]) ? args[0] : "";
                if (args.Length > 1 && args[1].Equals("false", StringComparison.OrdinalIgnoreCase)) useAir = false;

                // Check for suffix options.
                if (commandSuffex == "save")
                {
                    // Ensure the copied clipboard is full.
                    if (copiedRegion.Count() == 0)
                    {
                        SendFeedback("ERROR: You need to first copy a region.");
                        return;
                    }

                    // Launch an open file dialog to get the name.
                    SaveFileDialog saveFileDialog = new SaveFileDialog
                    {
                        Title = "Select Save Location",
                        Filter = "Schem Files|*.schem",
                        InitialDirectory = Environment.CurrentDirectory
                    };

                    if (saveFileDialog.ShowDialog() == DialogResult.OK) // For WinForms
                    {
                        // Define main file info.
                        FileInfo schemLocation = new FileInfo(saveFileDialog.FileName);

                        // Save the regions data to a file.
                        SaveSchematic(copiedRegion, schemLocation, saveAir: useAir);
                        // SendFeedback($"Schematic '{schemLocation.Name}' has been saved successfully.");
                    }
                    else
                    {
                        SendFeedback("Save operation canceled.");
                    }
                }
                else if (commandSuffex == "load")
                {
                    // Launch an open file dialog to get the name.
                    OpenFileDialog openFileDialog = new OpenFileDialog
                    {
                        Title = "Select Schematic File",
                        Filter = "Schem Files|*.schem",
                        InitialDirectory = Environment.CurrentDirectory
                    };

                    if (openFileDialog.ShowDialog() == DialogResult.OK) // For WinForms
                    {
                        // Define main file info.
                        FileInfo schemLocation = new FileInfo(openFileDialog.FileName);

                        // Save the schematics region data to the clipboard.
                        LoadSchematic(schemLocation, useAir);
                        // SendFeedback($"Schematic '{schemLocation.Name}' has loaded to the clipboard successfully.");
                    }
                    else
                    {
                        SendFeedback("Load operation canceled.");
                    }
                }
                else
                {
                    // No valid argument.
                    SendFeedback($"ERROR: Argument was not valid.");
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
                Clipboard.SetText(ex.Message);
            }
        }
        #endregion

        #region /copy

        [Command("//copy")]
        [Command("/copy")]
        private async Task ExecuteCopy()
        {
            try
            {
                // Define location data.
                Region region = new Region(_pointToLocation1, _pointToLocation2);

                // Save copy data.
                await CopyRegion(region);

                SendFeedback($"Region was copied.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /cut

        [Command("//cut")]
        [Command("/cut")]
        private async Task ExecuteCut()
        {
            try
            {
                // Define location data.
                Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                // Check and make sure the region contains less than a million blocks.
                // Make 'No' the highlighted option. Helps mitigate issues when using '/tool'.
                if (CalculateBlockCount(definedRegion.Position1, definedRegion.Position2) > 1000000 &&
                    MessageBox.Show("This region contains over a million blocks.\n\nDo you want to continue anyways?",
                                    "WE: Woah! That's a ton of blocks!",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button2) == DialogResult.No)
                {
                    SendFeedback("Operation canceled.");
                    return;
                }

                // Save copy data first.
                await CopyRegion(definedRegion);

                // Build a list of all non-air blocks in the region.
                var regionBlocks = await FillRegion(definedRegion, false, AirID);

                // Save the BEFORE snapshot for undo.
                await SaveUndo(regionBlocks);
                ClearRedo();

                // Remove crate entries from the world + notify clients.
                // Do this AFTER SaveUndo so crate contents can be restored on undo.
                int minX = (int)Math.Min(definedRegion.Position1.X, definedRegion.Position2.X);
                int minY = (int)Math.Min(definedRegion.Position1.Y, definedRegion.Position2.Y);
                int minZ = (int)Math.Min(definedRegion.Position1.Z, definedRegion.Position2.Z);
                int maxX = (int)Math.Max(definedRegion.Position1.X, definedRegion.Position2.X);
                int maxY = (int)Math.Max(definedRegion.Position1.Y, definedRegion.Position2.Y);
                int maxZ = (int)Math.Max(definedRegion.Position1.Z, definedRegion.Position2.Z);
                DestroyCratesInBounds(minX, minY, minZ, maxX, maxY, maxZ);

                // Delete the contents of this region and build the AFTER snapshot.
                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();

                foreach (Vector3 blockLocation in regionBlocks)
                {
                    if (GetBlockFromLocation(blockLocation) != AirID)
                    {
                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                // Save the AFTER snapshot so redo can re-apply the cut.
                await SaveUndo(redoBuilder);

                SendFeedback("Region was cut.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /paste

        [Command("//paste")]
        [Command("/paste")]
        private async Task ExecutePaste(string[] args)
        {
            // Ensure the copied clipboard is full.
            if (copiedRegion.Count() == 0)
            {
                SendFeedback("ERROR: You need to first copy a region.");
                return;
            }

            try
            {
                bool useAir = args.Any(a => a.Equals("false", StringComparison.OrdinalIgnoreCase)) == false; // Enabled by default.
                bool pasteAtPoint1 = args.Any(a => a.Equals("pos1", StringComparison.OrdinalIgnoreCase));

                Vector3 basePosition;
                if (pasteAtPoint1)
                    basePosition = _pointToLocation1;
                else
                {
                    // Shift the whole copied box so that: (old player-anchor) -> (new player-anchor).
                    Vector3 playerLoc = GetUsersLocation();
                    basePosition = playerLoc - CopyAnchorOffset;
                }

                // PasteRegion(Region region).
                var region = await PasteRegion(basePosition);

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(ExtractVector3HashSet(region));
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (var (blockLocation, block) in region)
                {
                    // Check if useAir is disabled and if so, skip placing air blocks.
                    if (!useAir && block == AirID) continue;

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Apply crate inventories (sidecar) after enqueueing blocks.
                PasteClipboardCrates(basePosition, copiedRegion, overwriteExisting: true);

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /rotate

        [Command("//rotate")]
        [Command("/rotate")]
        private async Task ExecuteRotate(string[] args)
        {
            // Ensure the copied clipboard is full.
            if (copiedRegion.Count() == 0)
            {
                SendFeedback("ERROR: You need to first copy a region.");
                return;
            }

            try
            {
                int rotateY = args.Length > 0 && int.TryParse(args[0], out int rY) ? rY : 90;
                int rotateX = args.Length > 1 && int.TryParse(args[1], out int rX) ? rX : 0;
                int rotateZ = args.Length > 2 && int.TryParse(args[2], out int rZ) ? rZ : 0;

                // Ensure all rotations are valid.
                if (!(IsValidRotation(rotateY) && IsValidRotation(rotateX) && IsValidRotation(rotateZ)))
                {
                    SendFeedback($"ERROR: One or more rotations are invalid. Use: (90, 180, 270, 360)");
                    return;
                }

                // Apply the clipboard rotations.
                await RotateClipboard(-rotateX, -rotateY, -rotateZ);

                SendFeedback($"Clipboard has been rotated by Y: '{rotateY}', X: '{rotateX}', Z: '{rotateZ}' degrees!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /flip

        [Command("//flip")]
        [Command("/flip")]
        private async Task ExecuteFlip(string[] args)
        {
            // Ensure the copied clipboard is full.
            if (copiedRegion.Count() == 0)
            {
                SendFeedback("ERROR: You need to first copy a region.");
                return;
            }

            try
            {
                string flipDirectionInput = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : "";

                // Check if the user wants to manually specify the flip direction. If not use facing direction.
                if (string.IsNullOrEmpty(flipDirectionInput))
                {
                    // Get the facing direction from the users location and the cursor location.
                    Direction facingDirection = GetFacingDirection(GetUsersLocation(), GetUsersCursorLocation());

                    // Perform the flip operation
                    await FlipClipboard(facingDirection);

                    SendFeedback($"Clipboard has been flipped along '{facingDirection}'!");
                    return;
                }
                else if (Enum.TryParse(args[0], true, out Direction flipDirection))
                {
                    // Perform the flip operation
                    await FlipClipboard(flipDirection);

                    SendFeedback($"Clipboard has been flipped along '{flipDirection}'!");
                    return;
                }

                SendFeedback($"ERROR: '{args[0]}' is not a valid direction. Use: (posX, negX, posZ, negZ, Up, Down)");
                return;
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /clearclipboard

        [Command("//clearclipboard")]
        [Command("/clearclipboard")]
        [Command("//clearc")]
        [Command("/clearc")]
        private void ExecuteClearClipboard()
        {
            try
            {
                // Clear existing clearclipboard.
                ClearClipboard();

                SendFeedback($"Clipboard has been cleared!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /copychunk

        [Command("//copychunk")]
        [Command("/copychunk")]
        [Command("//copyc")]
        [Command("/copyc")]
        private async Task ExecuteCopyChunk(string[] args)
        {
            try
            {
                // Optional radius; default 1 = only the current chunk.
                int radius = 1;
                if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                {
                    if (!int.TryParse(args[0], out radius))
                    {
                        SendFeedback("ERROR: Radius must be an integer. Usage: /copychunk (chunkRadius)");
                        return;
                    }
                }

                if (radius < 1)
                    radius = 1;

                // Center on the player's current world position.
                Vector3 center = GetUsersLocation();

                // Chunk dimensions (from WorldEditCore.ChunkSize).
                int sizeX = ChunkSize.WidthX;
                int sizeZ = ChunkSize.LengthZ;

                // Which chunk is the player standing in?
                int centerChunkX = FloorDiv((int)Math.Floor(center.X), sizeX);
                int centerChunkZ = FloorDiv((int)Math.Floor(center.Z), sizeZ);

                // radius = 1 -> ring 0 (1x1), radius = 2 -> ring 1 (3x3), etc.
                int ring = radius - 1;

                int minChunkX = centerChunkX - ring;
                int maxChunkX = centerChunkX + ring;
                int minChunkZ = centerChunkZ - ring;
                int maxChunkZ = centerChunkZ + ring;

                // Convert chunk indices -> world coordinates.
                int minX = minChunkX * sizeX;
                int maxX = (maxChunkX + 1) * sizeX - 1;
                int minZ = minChunkZ * sizeZ;
                int maxZ = (maxChunkZ + 1) * sizeZ - 1;

                Vector3 pos1 = new Vector3(minX, WorldHeights.MinY, minZ);
                Vector3 pos2 = new Vector3(maxX, WorldHeights.MaxY, maxZ);

                Region region = new Region(pos1, pos2);

                // Fill copiedChunk.
                await CopyChunk(region);

                int chunksWide = (maxChunkX - minChunkX + 1);
                int chunksDeep = (maxChunkZ - minChunkZ + 1);
                int chunkCount = chunksWide * chunksDeep;

                SendFeedback(
                    $"Copied {chunkCount} chunk(s) [{minChunkX}..{maxChunkX}]x[{minChunkZ}..{maxChunkZ}] " +
                    $"(center chunk X:{centerChunkX}, Z:{centerChunkZ}, radius={radius}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /cutchunk

        [Command("//cutchunk")]
        [Command("/cutchunk")]
        [Command("//cutc")]
        [Command("/cutc")]
        private async Task ExecuteCutChunk(string[] args)
        {
            try
            {
                // Optional radius; default = 1 (just the current chunk).
                int radius = 1;
                if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                {
                    if (!int.TryParse(args[0], out radius))
                    {
                        SendFeedback("ERROR: Radius must be an integer. Usage: /cutchunk (chunkRadius)");
                        return;
                    }
                }

                if (radius < 1)
                    radius = 1;

                // Center on the player's current world position.
                Vector3 center = GetUsersLocation();

                // Chunk dimensions (from WorldEditCore.ChunkSize).
                int sizeX = ChunkSize.WidthX;
                int sizeZ = ChunkSize.LengthZ;

                // Which chunk is the player standing in?
                int centerChunkX = FloorDiv((int)Math.Floor(center.X), sizeX);
                int centerChunkZ = FloorDiv((int)Math.Floor(center.Z), sizeZ);

                // radius = 1 -> ring = 0 -> 1x1 chunk area
                // radius = 2 -> ring = 1 -> 3x3 chunk area, etc.
                int ring = radius - 1;

                int minChunkX = centerChunkX - ring;
                int maxChunkX = centerChunkX + ring;
                int minChunkZ = centerChunkZ - ring;
                int maxChunkZ = centerChunkZ + ring;

                // Convert chunk indices -> world coordinates.
                int minX = minChunkX * sizeX;
                int maxX = (maxChunkX + 1) * sizeX - 1;
                int minZ = minChunkZ * sizeZ;
                int maxZ = (maxChunkZ + 1) * sizeZ - 1;

                Vector3 pos1 = new Vector3(minX, WorldHeights.MinY, minZ);
                Vector3 pos2 = new Vector3(maxX, WorldHeights.MaxY, maxZ);
                Region definedRegion = new Region(pos1, pos2);

                // Safety: warn if this is huge, same as /cut.
                if (CalculateBlockCount(definedRegion.Position1, definedRegion.Position2) > 1000000 &&
                    MessageBox.Show("This region contains over a million blocks.\n\nDo you want to continue anyways?",
                                    "WE: Woah! That's a ton of blocks!",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button2) == DialogResult.No)
                {
                    SendFeedback("Operation canceled.");
                    return;
                }

                // 1) Copy the chunk area into the chunk clipboard.
                await CopyChunk(definedRegion);

                // Remove crate entries from the world + notify clients.
                int chunkMinX = (int)Math.Min(definedRegion.Position1.X, definedRegion.Position2.X);
                int chunkMinY = (int)Math.Min(definedRegion.Position1.Y, definedRegion.Position2.Y);
                int chunkMinZ = (int)Math.Min(definedRegion.Position1.Z, definedRegion.Position2.Z);
                int chunkMaxX = (int)Math.Max(definedRegion.Position1.X, definedRegion.Position2.X);
                int chunkMaxY = (int)Math.Max(definedRegion.Position1.Y, definedRegion.Position2.Y);
                int chunkMaxZ = (int)Math.Max(definedRegion.Position1.Z, definedRegion.Position2.Z);
                DestroyCratesInBounds(chunkMinX, chunkMinY, chunkMinZ, chunkMaxX, chunkMaxY, chunkMaxZ);

                // 2) Build a list of all non-air blocks in that region (full height).
                //    FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                var regionBlocks = await FillRegion(definedRegion, hollow: false, ignoreBlock: AirID);

                // --- Undo support (default): save what we're about to delete, then clear redo. ---
                await SaveUndo(regionBlocks);
                ClearRedo();

                // 3) Delete the contents of this chunk region + build redo snapshot.
                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();

                // 3) Delete the contents of this chunk region.
                foreach (Vector3 blockLocation in regionBlocks)
                {
                    // Remove blocks that are not already air (performance).
                    if (GetBlockFromLocation(blockLocation) != AirID)
                    {
                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);

                        // Redo should re-apply the deletion.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                // Save the "after" snapshot so /undo and /redo both work.
                await SaveUndo(redoBuilder);

                int chunksWide = (maxChunkX - minChunkX + 1);
                int chunksDeep = (maxChunkZ - minChunkZ + 1);
                int chunkCount = chunksWide * chunksDeep;

                SendFeedback(
                    $"Cut and copied {chunkCount} chunk(s) " +
                    $"[{minChunkX}..{maxChunkX}]x[{minChunkZ}..{maxChunkZ}] " +
                    $"(center chunk X:{centerChunkX}, Z:{centerChunkZ}, radius={radius}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /pastechunk

        [Command("//pastechunk")]
        [Command("/pastechunk")]
        [Command("//pastec")]
        [Command("/pastec")]
        private async Task ExecutePasteChunk(string[] args)
        {
            // Ensure the chunk clipboard is full.
            if (copiedChunk.Count == 0)
            {
                SendFeedback("ERROR: Chunk clipboard is empty. Use /copychunk first.");
                return;
            }

            try
            {
                // Optional: "false" anywhere => skip air blocks.
                bool useAir = args.Any(a => a.Equals("false", StringComparison.OrdinalIgnoreCase)) == false;

                // Optional: "force" or "override" anywhere => bypass alignment check.
                bool forceMisaligned = args.Any(a =>
                    a.Equals("force", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("override", StringComparison.OrdinalIgnoreCase));

                // --- Derive clipboard dimensions from copiedChunk. ---

                int maxOffsetX = int.MinValue;
                int maxOffsetY = int.MinValue;
                int maxOffsetZ = int.MinValue;

                foreach (var entry in copiedChunk)
                {
                    Vector3 v = entry.Item1;
                    if (v.X > maxOffsetX) maxOffsetX = (int)v.X;
                    if (v.Y > maxOffsetY) maxOffsetY = (int)v.Y;
                    if (v.Z > maxOffsetZ) maxOffsetZ = (int)v.Z;
                }

                int widthX = maxOffsetX + 1;
                int heightY = maxOffsetY + 1;
                int lengthZ = maxOffsetZ + 1;

                int sizeX = ChunkSize.WidthX;
                int sizeZ = ChunkSize.LengthZ;

                // Sanity check: Must align with chunk grid, unless forced.
                if ((widthX % sizeX != 0 || lengthZ % sizeZ != 0) && !forceMisaligned)
                {
                    SendFeedback("ERROR: Copied chunk data is not aligned to chunk size. " +
                                      "Did you use /copychunk to create it? " +
                                      "Use '/pastechunk force' to override.");
                    return;
                }
                else if (widthX % sizeX != 0 || lengthZ % sizeZ != 0)
                {
                    SendFeedback("WARN: Copied data is not aligned to chunk size; forcing paste anyway.");
                }

                // Make sure we never end up with 0 chunks if misaligned.
                int chunksX = Math.Max(1, widthX / sizeX);
                int chunksZ = Math.Max(1, lengthZ / sizeZ);

                // --- Figure out where to paste around the current chunk. ---

                Vector3 player = GetUsersLocation();

                int playerChunkX = FloorDiv((int)Math.Floor(player.X), sizeX);
                int playerChunkZ = FloorDiv((int)Math.Floor(player.Z), sizeZ);

                // For odd chunk counts, this centers the area on the player's chunk.
                int ringX = (chunksX - 1) / 2;
                int ringZ = (chunksZ - 1) / 2;

                int minChunkX = playerChunkX - ringX;
                int minChunkZ = playerChunkZ - ringZ;

                int minX = minChunkX * sizeX;
                int minZ = minChunkZ * sizeZ;

                // For Y, always anchor to world base (same as when copying).
                int baseY = WorldHeights.MinY;

                Vector3 baseLocation = new Vector3(minX, baseY, minZ);

                // --- Build destination set for undo (before we change anything). ---

                HashSet<Vector3> dstPositions = new HashSet<Vector3>();
                foreach (var entry in copiedChunk)
                {
                    dstPositions.Add(baseLocation + entry.Item1);
                }

                await SaveUndo(dstPositions);
                ClearRedo();

                // --- Place blocks and build redo snapshot. ---

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();

                foreach (var (offset, block) in copiedChunk)
                {
                    Vector3 destPos = baseLocation + offset;

                    if (!useAir && block == AirID)
                        continue;

                    // Place block only if it actually changes something.
                    if (GetBlockFromLocation(destPos) != block)
                    {
                        AsyncBlockPlacer.Enqueue(destPos, block);
                        redoBuilder.Add(new Tuple<Vector3, int>(destPos, block));
                    }
                }

                // Apply crate inventories (sidecar) after enqueueing blocks.
                PasteClipboardCrates(baseLocation, copiedChunk, overwriteExisting: true);

                await SaveUndo(redoBuilder);

                int chunkCount = chunksX * chunksZ;
                SendFeedback($"Pasted {redoBuilder.Count} block(s) into {chunkCount} chunk(s) around you.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /delchunk

        [Command("//delchunk")]
        [Command("/delchunk")]
        [Command("//delc")]
        [Command("/delc")]
        private async Task ExecuteDelChunk(string[] args)
        {
            try
            {
                // Defaults.
                int radius = 1;
                bool recordHistory = true;

                // Parse args (order-independent):
                // - First int => radius.
                // - Optional flags => true/noundo/nohistory/skipundo.
                if (args != null && args.Length > 0)
                {
                    foreach (string a in args)
                    {
                        if (string.IsNullOrWhiteSpace(a))
                            continue;

                        if (a.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("noundo", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("nohistory", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("skipundo", StringComparison.OrdinalIgnoreCase))
                        {
                            recordHistory = false;
                            continue;
                        }

                        if (int.TryParse(a, out int parsed))
                        {
                            radius = parsed;
                            continue;
                        }

                        SendFeedback($"ERROR: Unknown argument '{a}'. Usage: /delchunk (chunkRadius) (noundo)");
                        return;
                    }
                }

                if (radius < 1)
                    radius = 1;

                // Center on the player's current world position.
                Vector3 center = GetUsersLocation();

                // Chunk dimensions (from WorldEditCore.ChunkSize).
                int sizeX = ChunkSize.WidthX;
                int sizeZ = ChunkSize.LengthZ;

                // Which chunk is the player standing in?
                int centerChunkX = FloorDiv((int)Math.Floor(center.X), sizeX);
                int centerChunkZ = FloorDiv((int)Math.Floor(center.Z), sizeZ);

                // radius = 1 -> ring = 0 -> 1x1 chunk area
                // radius = 2 -> ring = 1 -> 3x3 chunk area, etc.
                int ring = radius - 1;

                int minChunkX = centerChunkX - ring;
                int maxChunkX = centerChunkX + ring;
                int minChunkZ = centerChunkZ - ring;
                int maxChunkZ = centerChunkZ + ring;

                // Convert chunk indices -> world coordinates.
                int minX = minChunkX * sizeX;
                int maxX = (maxChunkX + 1) * sizeX - 1;
                int minZ = minChunkZ * sizeZ;
                int maxZ = (maxChunkZ + 1) * sizeZ - 1;

                Vector3 pos1 = new Vector3(minX, WorldHeights.MinY, minZ);
                Vector3 pos2 = new Vector3(maxX, WorldHeights.MaxY, maxZ);
                Region definedRegion = new Region(pos1, pos2);

                // Safety: warn if this is huge, same as /cut.
                if (CalculateBlockCount(definedRegion.Position1, definedRegion.Position2) > 1000000 &&
                    MessageBox.Show("This region contains over a million blocks.\n\nDo you want to continue anyways?",
                                    "WE: Woah! That's a ton of blocks!",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button2) == DialogResult.No)
                {
                    SendFeedback("Operation canceled.");
                    return;
                }

                // Build a list of all non-air blocks in that region (full height).
                // FillRegion(Region region, bool hollow, int ignoreBlock = -1).
                var regionBlocks = await FillRegion(definedRegion, hollow: false, ignoreBlock: AirID);

                if (regionBlocks.Count == 0)
                {
                    SendFeedback("Nothing to delete (all air).");
                    return;
                }

                // Undo snapshot (BEFORE).
                if (recordHistory)
                {
                    await SaveUndo(regionBlocks);

                    // Any new edit invalidates redo history.
                    ClearRedo();
                }

                // Redo snapshot (AFTER).
                HashSet<Tuple<Vector3, int>> redoBuilder = recordHistory
                    ? new HashSet<Tuple<Vector3, int>>()
                    : null;

                foreach (Vector3 blockLocation in regionBlocks)
                {
                    // Remove blocks that are not already air (performance/safety).
                    if (GetBlockFromLocation(blockLocation) != AirID)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);

                        if (recordHistory)
                            redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                if (recordHistory)
                    await SaveUndo(redoBuilder);

                int chunksWide = (maxChunkX - minChunkX + 1);
                int chunksDeep = (maxChunkZ - minChunkZ + 1);
                int chunkCount = chunksWide * chunksDeep;

                int deleted = recordHistory ? redoBuilder.Count : regionBlocks.Count;

                SendFeedback(
                    $"Deleted {deleted} block(s) in {chunkCount} chunk(s) " +
                    $"[{minChunkX}..{maxChunkX}]x[{minChunkZ}..{maxChunkZ}] " +
                    $"(center chunk X:{centerChunkX}, Z:{centerChunkZ}, radius={radius})" +
                    (recordHistory ? "." : " (history skipped)."));
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Tool Commands.

        #region /tool

        [Command("//tool")]
        [Command("/tool")]
        private void ExecuteTool(string[] args) // Don't give 'static' for tool command.
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /tool [on/off] [/command],\n" +
                                  "    /tool command [/command]"
                );
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "on":
                        if (args.Length < 2) { SendFeedback("ERROR: Missing parameter. Usage: /tool [on/off] [/command]"); return; }

                        _toolCommand = args.Length >= 2 ? string.Join(" ", args.Skip(1)) : string.Empty;
                        _toolItem = ActiveToolItemID = GetUsersHeldItem(); // Set the snapshot tool item.

                        Timer toolTimer = new Timer() { Interval = 1 };
                        toolTimer.Tick += WorldTool_Tick;
                        toolTimer.Start();

                        _toolEnabled = true; ToolEnabled = true;
                        SendFeedback($"Tool Activated! Command: {_toolCommand}.");
                        break;

                    case "off":
                        _toolEnabled = false; ToolEnabled = false;
                        ActiveToolItemID = -1; // Clear the snapshot tool item.
                        SendFeedback("Tool Deactivated!");
                        break;

                    case "command":
                        if (args.Length < 2) { SendFeedback("ERROR: Missing parameter. Usage: /tool [on/off] [/command]"); return; }

                        _toolCommand = args.Length >= 2 ? string.Join(" ", args.Skip(1)) : string.Empty;
                        SendFeedback($"New Tool Command: {_toolCommand}.");
                        break;

                    default:
                        SendFeedback("ERROR: Command usage /tool [on/off] [/command]");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /navwand

        [Command("//navwand")]
        [Command("/navwand")]
        private void ExecuteNavWand(string[] args)
        {
            // Summary: Toggles the nav-wand on/off or sets the nav-wand item, and persists the choice to WorldEdit.Config.ini.
            // Usage:
            // - /navwand                -> Toggle on/off.
            // - /navwand on|off|toggle  -> Explicit toggle state.
            // - /navwand <item|id|none> -> Set item (or disable with "none").
            // Behavior:
            // - When disabling: NavWandItem -> none, and NavWandItemPrevious remembers the last non-none item.
            // - When enabling:  Restores NavWandItemPrevious, else falls back to the default (Compass).
            // - When setting:   NavWandItem + NavWandItemPrevious are both set to the given item.
            try
            {
                var cfg = WEConfig.LoadOrCreate();

                // If the first argument is not a toggle token, treat it as an item assignment.
                // Summary: Allows "/navwand Compass" and "/navwand none" without conflicting with on/off.
                if (args != null && args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0]) && !IsToggleToken(args[0]))
                {
                    string token = (args[0] ?? "").Trim();

                    // Explicit disable via "/navwand none".
                    if (IsDisabledToken(token))
                    {
                        string prev = !IsDisabledToken(cfg.NavWandItem) ? cfg.NavWandItem : cfg.NavWandItemPrevious;
                        if (IsDisabledToken(prev))
                            prev = "";

                        WEConfig.UpdateNavWandConfig("none", prev);
                        WEConfig.LoadApply();

                        // Clear the snapshot navWand item.
                        ActiveNavWandItemID = -1;

                        SendFeedback($"NavWand Disabled! (NavWandItem = none){(!string.IsNullOrWhiteSpace(prev) ? $" | Previous item saved: {prev}." : ".")}");
                        return;
                    }

                    // Set the item (enum name or numeric id).
                    if (!TryNormalizeInventoryItemToken(token, out string normalized))
                    {
                        SendFeedback($"ERROR: Unknown item '{token}'. Usage: /navwand (on|off|item|id|none).");
                        return;
                    }

                    WEConfig.UpdateNavWandConfig(normalized, normalized);
                    WEConfig.LoadApply();

                    // Update the snapshot navWand item from config.
                    ActiveNavWandItemID = NavWandItemID;

                    SendFeedback($"NavWand Item set to: {normalized} (enabled).");
                    return;
                }

                // Toggle on/off.
                bool isEnabled = !IsDisabledToken(cfg.NavWandItem);
                bool enable = ResolveToggle(args, isEnabled);

                if (!enable)
                {
                    // Disabling: Remember current item (if any), then set NavWandItem=none.
                    string prev = !IsDisabledToken(cfg.NavWandItem) ? cfg.NavWandItem : cfg.NavWandItemPrevious;
                    if (IsDisabledToken(prev))
                        prev = "";

                    WEConfig.UpdateNavWandConfig("none", prev);
                    WEConfig.LoadApply();

                    // Clear the snapshot navWand item.
                    ActiveNavWandItemID = -1;

                    SendFeedback($"NavWand Disabled! (NavWandItem = none){(!string.IsNullOrWhiteSpace(prev) ? $" | Previous item saved: {prev}." : ".")}");
                    return;
                }

                // Enabling: restore previous, else default.
                string restore = (!string.IsNullOrWhiteSpace(cfg.NavWandItemPrevious) && !IsDisabledToken(cfg.NavWandItemPrevious))
                    ? cfg.NavWandItemPrevious
                    : "Compass";

                WEConfig.UpdateNavWandConfig(restore, restore);
                WEConfig.LoadApply();

                // Reset the nav-wand click latch after changing the config.
                _navWandRunTimes = 0;

                // Update the snapshot navWand item from config.
                ActiveNavWandItemID = NavWandItemID;

                SendFeedback($"NavWand Enabled! (NavWandItem = {restore}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Brush Commands.

        #region /brush

        [Command("//brush")]
        [Command("/brush")]
        [Command("//br")]
        [Command("/br")]
        private void ExecuteBrush(string[] args) // Don't give 'static' for brush command.
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /brush [on/off] (block(,array)) (size),\n" +
                                  "    /brush block [block(,array)] | /brush shape [shape],\n" +
                                  "    /brush size [size]           | /brush height [height],\n" +
                                  "    /brush hollow [true/false]   | /brush replace [true/false],\n" +
                                  "    /brush rapid [true/false]"
                );
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "on":
                        {
                            if (args.Length < 1) { SendFeedback("ERROR: Missing parameter. Usage: /brush [on/off] (block(,array)) (size)"); return; }

                            string defaultBlock = !string.IsNullOrEmpty(_brushBlockPattern) ? _brushBlockPattern : "1";
                            string blockPattern = args.Length > 1 && !string.IsNullOrEmpty(args[1]) ? args[1] : defaultBlock;

                            // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                            int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                            if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                            _brushItem         = ActiveBrushItemID = GetUsersHeldItem();                               // Or use 'WandItemID'. // Set the snapshot brush item.
                            _brushBlockPattern = blockPattern;
                            _brushSize         = args.Length > 2 && int.TryParse(args[2], out int s) ? s : _brushSize; // If value is not set, keep set value.

                            // Turn off brushing commands.
                            _brushReplaceMode  = false;
                            _brushRapidMode    = false;
                            _brushRunTimes     = 0;

                            // Make sure we don't stack multiple timers.
                            if (_brushTimer != null)
                            {
                                _brushTimer.Stop();
                                _brushTimer.Tick -= WorldBrush_Tick;
                                _brushTimer.Dispose();
                                _brushTimer = null;
                            }

                            // Create and start a single shared timer.
                            _brushTimer = new Timer { Interval = 1 };
                            _brushTimer.Tick += WorldBrush_Tick;
                            _brushTimer.Start();

                            _brushEnabled = true; BrushEnabled = true;
                            break;
                        }

                    case "off":
                        SendFeedback("Brush Deactivated!");
                        _brushEnabled     = false; BrushEnabled = false;
                        _brushRunTimes    = 0;
                        ActiveBrushItemID = -1; // Clear the snapshot brush item.

                        if (_brushTimer != null)
                        {
                            _brushTimer.Stop();
                            _brushTimer.Tick -= WorldBrush_Tick;
                            _brushTimer.Dispose();
                            _brushTimer = null;
                        }
                        return;

                    case "block":
                        {
                            if (args.Length != 2)                                              { SendFeedback("ERROR: Missing block type. Usage: /brush block [block(,array)]");  return; }

                            string defaultBlock = !string.IsNullOrEmpty(_brushBlockPattern) ? _brushBlockPattern : "1";
                            string blockPattern = args.Length > 1 && !string.IsNullOrEmpty(args[1]) ? args[1] : defaultBlock;

                            // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                            int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                            if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                            _brushBlockPattern = blockPattern;
                            break;
                        }

                    case "shape":
                        if (args.Length != 2 || !IsValidBrushShape(args[1].ToLower()))         { SendFeedback("ERROR: Missing shape type. Usage: /brush shape [shape]");          return; }

                        _brushShape = args[1].ToLower();
                        break;

                    case "size":
                        if (args.Length != 2 || !int.TryParse(args[1], out int size))          { SendFeedback("ERROR: Invalid brush size. Usage: /brush size [size]");            return; }

                        _brushSize = size;
                        break;

                    case "height":
                        if (args.Length != 2 || !int.TryParse(args[1], out int height))        { SendFeedback("ERROR: Invalid brush size. Usage: /brush height [height]");        return; }

                        _brushHeight = height;
                        break;

                    case "hollow":
                        if (args.Length != 2 || !bool.TryParse(args[1], out bool hollow))      { SendFeedback("ERROR: Invalid replace mode. Usage: /brush hollow [true/false]");  return; }

                        _brushHollow = hollow;
                        break;

                    case "replace":
                        if (args.Length != 2 || !bool.TryParse(args[1], out bool replaceMode)) { SendFeedback("ERROR: Invalid replace mode. Usage: /brush replace [true/false]"); return; }

                        _brushReplaceMode = replaceMode;
                        break;

                    case "rapid":
                        if (args.Length != 2 || !bool.TryParse(args[1], out bool rapidMode))   { SendFeedback("ERROR: Invalid rapid mode. Usage: /brush rapid [true/false]");     return; }

                        _brushRapidMode = rapidMode;
                        break;

                    default:
                        SendFeedback("ERROR: Command usage /brush [on/off] (block(,array)) (size),\n" +
                                  "    /brush block [block(,array)],\n" +
                                  "    /brush shape [shape],\n" +
                                  "    /brush size [size],\n" +
                                  "    /brush height [height],\n" +
                                  "    /brush hollow [true/false],\n" +
                                  "    /brush replace [true/false],\n" +
                                  "    /brush rapid [true/false]"
                        );
                        return;
                }

                // Display enabled message.
                SendFeedback($"Brush Activated!\n\n" +
                                  $"Block Type: {_brushBlockPattern}\n" +
                                  $"Block Shape: {_brushShape}\n" +
                                  $"Brush Size: {_brushSize}\n" +
                                  $"Brush Height: {_brushHeight}\n" +
                                  $"Hollow Mode: {_brushHollow}\n" +
                                  $"Replace Mode: {_brushReplaceMode}\n" +
                                  $"Rapid Mode: {_brushRapidMode}.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Utility Commands.

        #region /fill

        [Command("//fill")]
        [Command("/fill")]
        private async Task ExecuteFill(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /fill [block(,array)] [radius] (depth) (direction)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radius = int.TryParse(args[1], out int r) ? Math.Abs(r) : 1;
                int depth = args.Length > 2 && int.TryParse(args[2], out int d) ? Math.Max(1, Math.Abs(d)) : 1;

                Direction fillDirection = GetFacingDirection(GetUsersLocation(), GetUsersCursorLocation());
                if (args.Length > 3)
                {
                    if (!TryParseDirection(args[3], out fillDirection))
                    {
                        SendFeedback("ERROR: Invalid direction. Use posX, negX, posZ, negZ, Up, or Down.");
                        return;
                    }
                }

                if (!IsValidCursorLocation())
                {
                    SendFeedback("ERROR: Target an empty block first.");
                    return;
                }

                Vector3 start = GetUsersLocation();
                if (GetBlockFromLocation(start) != AirID)
                {
                    SendFeedback("ERROR: /fill must start from an empty block.");
                    return;
                }

                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return;

                HashSet<Vector3> region = await FillHole(start, radius, depth, fillDirection);

                if (region.Count == 0)
                {
                    SendFeedback("No matching air blocks were found to fill.");
                    return;
                }

                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /fillr

        [Command("//fillr")]
        [Command("/fillr")]
        private async Task ExecuteFillRecursive(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /fillr [block(,array)] [radius] (depth) (direction)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radius = int.TryParse(args[1], out int r) ? Math.Abs(r) : 1;
                int depth = args.Length > 2 && int.TryParse(args[2], out int d) ? Math.Max(1, Math.Abs(d)) : int.MaxValue;

                Direction fillDirection = GetFacingDirection(GetUsersLocation(), GetUsersCursorLocation());
                if (args.Length > 3)
                {
                    if (!TryParseDirection(args[3], out fillDirection))
                    {
                        SendFeedback("ERROR: Invalid direction. Use posX, negX, posZ, negZ, Up, or Down.");
                        return;
                    }
                }

                if (!IsValidCursorLocation())
                {
                    SendFeedback("ERROR: Target an empty block first.");
                    return;
                }

                Vector3 start = GetUsersLocation();
                if (GetBlockFromLocation(start) != AirID)
                {
                    SendFeedback("ERROR: /fillr must start from an empty block.");
                    return;
                }

                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return;

                HashSet<Vector3> region = await FillHoleRecursive(start, radius, depth, fillDirection);

                if (region.Count == 0)
                {
                    SendFeedback("No matching air blocks were found to fill.");
                    return;
                }

                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /drain

        [Command("//drain")]
        [Command("/drain")]
        private async Task ExecuteDrain(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("ERROR: Command usage /drain [radius]");
                return;
            }

            try
            {
                int radius = int.TryParse(args[0], out int r) ? Math.Abs(r) : 0;
                Vector3 origin = GetUsersLocation();

                HashSet<Vector3> region = await Drain(origin, radius);

                if (region.Count == 0)
                {
                    SendFeedback("No nearby lava blocks were found to drain.");
                    return;
                }

                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    if (GetBlockFromLocation(blockLocation) != AirID)
                    {
                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} lava blocks have been drained!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /removenear

        [Command("//removenear")]
        [Command("/removenear")]
        [Command("//nuke")]
        [Command("/nuke")]
        private async Task ExecuteRemoveNear(string[] args)
        {
            if (args.Length == 0)
            {
                SendFeedback("ERROR: Command usage /removenear [radii] (pos1)");
                return;
            }

            try
            {
                int radii = int.TryParse(args[0], out int r) ? r : 1;
                bool pasteAtPoint1 = args.Any(a => a.Equals("pos1", StringComparison.OrdinalIgnoreCase));

                // Define the base location.
                Vector3 basePosition = GetUsersLocation();
                if (pasteAtPoint1)
                    basePosition = _pointToLocation1;

                // Get the shortest distance from the world boundaries.
                int furthestDistance = (int)Math.Max(Math.Abs(basePosition.Y - WorldHeights.MaxY), Math.Abs(basePosition.Y - WorldHeights.MinY));
                int shortestDistance = (int)Math.Min(Math.Abs(basePosition.Y - WorldHeights.MaxY), Math.Abs(basePosition.Y - WorldHeights.MinY));

                // Get the max height based on the input and the cap.
                // Make the max height use x2 so it removes radii below & above.
                int searchHeight = (radii * 2 <= furthestDistance) ? radii * 2: furthestDistance;

                // Get the center point.
                Vector3 centerOffset = new Vector3(basePosition.X, basePosition.Y - (searchHeight / 2), basePosition.Z);

                // MakeCylinder(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, int ignoreBlock = -1).
                var region = await MakeCylinder(centerOffset, radii, radii, searchHeight, false, AirID);

                // Save the existing region and clear the upcoming redo.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Place block if it doesn't already exist. (improves the performance).
                    if (GetBlockFromLocation(blockLocation) != AirID)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, AirID);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, AirID));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /replacenear

        [Command("//replacenear")]
        [Command("/replacenear")]
        [Command("//renear")]
        [Command("/renear")]
        private async Task ExecuteReplaceNear(string[] args)
        {
            if (args.Length < 3)
            {
                SendFeedback("ERROR: Command usage /replacenear [radii] [source block,(all)] [to block,(all)] (pos1)");
                return;
            }

            try
            {
                int radii = int.TryParse(args[0], out int r) ? r : 1;
                string searchPattern = !string.IsNullOrEmpty(args[1]) ? args[1] : "2";
                string replacePattern = !string.IsNullOrEmpty(args[2]) ? args[2] : "1";
                bool pasteAtPoint1 = args.Any(a => a.Equals("pos1", StringComparison.OrdinalIgnoreCase));

                // Define the base location.
                Vector3 basePosition = GetUsersLocation();
                if (pasteAtPoint1)
                    basePosition = _pointToLocation1;

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] searchPatternNumbers = (searchPattern == "all") ? Array.Empty<int>() : GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(searchPattern, BlockIDValues);
                if (searchPattern != "all" && searchPatternNumbers.Length == 0) return;   // Make sure the input is within the min/max.
                int[] replacePatternNumbers = (replacePattern == "all") ? Array.Empty<int>() : GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(replacePattern, BlockIDValues);
                if (replacePattern != "all" && replacePatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                // Get the shortest distance from the world boundaries.
                int furthestDistance = (int)Math.Max(Math.Abs(basePosition.Y - WorldHeights.MaxY), Math.Abs(basePosition.Y - WorldHeights.MinY));
                int shortestDistance = (int)Math.Min(Math.Abs(basePosition.Y - WorldHeights.MaxY), Math.Abs(basePosition.Y - WorldHeights.MinY));

                // Get the max height based on the input and the cap.
                // Make the max height use x2 so it removes radii below & above.
                int searchHeight = (radii * 2 <= furthestDistance) ? radii * 2: furthestDistance;

                // Get the center point.
                Vector3 centerOffset = new Vector3(basePosition.X, basePosition.Y - (searchHeight / 2), basePosition.Z);

                // MakeCylinder(Vector3 pos, double radiusX, double radiusZ, int height, bool hollow, int ignoreBlock = -1).
                // Check if the from-block pattern contains air, and if so, have the region save it.
                var region = (searchPattern == "all" || searchPatternNumbers.Contains(AirID)) ? await MakeCylinder(centerOffset, radii, radii, searchHeight, false) : await MakeCylinder(centerOffset, radii, radii, searchHeight, false, AirID);

                // Save the existing region and clear the upcoming redo.
                if (searchPattern == "all")
                    await SaveUndo(region);
                else
                    await SaveUndo(region, saveBlock: searchPatternNumbers);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get the current block type.
                    int currentBlock = GetBlockFromLocation(blockLocation);

                    // Check if the current block is a block to replace.
                    if ((searchPattern == "all" && currentBlock != AirID) || searchPatternNumbers.Contains(currentBlock)) // Make sure not to replace 'air' when using 'all' mode.
                    {
                        // Get random block from input.
                        HashSet<int> excludedBlocks = new HashSet<int> { AirID, 26 }; // IDs to exclude. Block ID 26 'Torch' crashes.
                        int replaceBlock = (replacePattern == "all") ? GetRandomBlock(excludedBlocks) : GetRandomBlockFromPattern(replacePatternNumbers);

                        // Place block if it doesn't already exist. (improves the performance).
                        if (GetBlockFromLocation(blockLocation) != replaceBlock)
                        {
                            // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                            if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                                !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)replaceBlock))
                                TryDestroyCrateAt(blockLocation);

                            AsyncBlockPlacer.Enqueue(blockLocation, replaceBlock);

                            // Add block to redo.
                            redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, replaceBlock));
                        }
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{redoBuilder.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /snow

        [Command("//snow")]
        [Command("/snow")]
        private async Task ExecuteSnow(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /snow [block(,array)] [radius] (pos1|region (worldY)) (replaceSurface)");
                return;
            }

            try
            {
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";

                bool useRegion = args.Any(a => a.Equals("region", StringComparison.OrdinalIgnoreCase));
                bool pasteAtPoint1 = !useRegion && args.Any(a => a.Equals("pos1", StringComparison.OrdinalIgnoreCase));

                // Optional: When using region mode, start Y scanning from the world's max Y instead of the region's max Y.
                bool useWorldY = args.Any(a =>
                    a.Equals("worldy", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("fullheight", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("maxy", StringComparison.OrdinalIgnoreCase));

                // Optional: "replace"/"surface" anywhere => place snow ON the first solid block (no +1).
                bool replaceSurface = args.Any(a =>
                    a.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("replace", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("surface", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("replacesurface", StringComparison.OrdinalIgnoreCase));

                // Radius meaning:
                // - Normal mode: Circle radius around player (or pos1).
                // - Region mode: Radius is ignored (selection bounds are used exactly).
                if (!int.TryParse(args[1], out int radius))
                    radius = 10;

                if (radius < 0)
                    radius = Math.Abs(radius);

                // Compare the input string to the games Enums and convert to their numerical values excluding numerical inputs.
                int[] blockPatternNumbers = GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);
                if (blockPatternNumbers.Length == 0) return; // Make sure the input is within the min/max.

                HashSet<Vector3> region;

                if (useRegion)
                {
                    // Make sure both points have been set.
                    if (_pointToLocation1 == default || _pointToLocation2 == default)
                    {
                        SendFeedback("ERROR: You must set /pos1 and /pos2 first.");
                        return;
                    }

                    Region definedRegion = new Region(_pointToLocation1, _pointToLocation2);

                    // MakeSnow(Region region, bool replaceSurface, bool useWorldY).
                    region = await MakeSnow(definedRegion, replaceSurface, useWorldY);
                }
                else
                {
                    // Define the base location.
                    Vector3 basePosition = GetUsersLocation();
                    if (pasteAtPoint1)
                        basePosition = _pointToLocation1;

                    // MakeSnow(Vector3 center, int radius, bool replaceSurface).
                    region = await MakeSnow(basePosition, radius, replaceSurface);
                }

                // Save the existing region and clear the upcoming redo.
                // Extract and save only the vector locations for the initial save.
                await SaveUndo(region);
                ClearRedo();

                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);

                        // Add block to redo.
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));
                    }
                }

                // Save the actions to undo stack.
                await SaveUndo(redoBuilder);

                SendFeedback($"{region.Count} blocks have been replaced!");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /randomplace

        /// <summary>
        /// /randomplace [block(,array)] [radius] (amount) (surface)
        /// Places the given block pattern at random positions in a square region
        /// centered on the player. If surface = true, blocks are spawned on the
        /// terrain surface; otherwise they may appear at any Y within world bounds.
        /// After each placement, the position is printed to the console and the
        /// action is recorded to the undo/redo stacks.
        /// </summary>
        [Command("//randomplace")]
        [Command("/randomplace")]
        private async Task ExecuteRandomPlace(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("ERROR: Command usage /randomplace [block(,array)] [radius] (amount) (surface)");
                return;
            }

            try
            {
                // Required args.
                string blockPattern = !string.IsNullOrEmpty(args[0]) ? args[0] : "1";
                int radius          = int.TryParse(args[1], out int r) ? Math.Max(1, r) : 5;

                // Optional args.
                int  amount = (args.Length > 2 && int.TryParse(args[2], out int a) && a > 0) ? a : 1;
                bool surfaceOnly = true; // Default: true (surface mode).

                if (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]))
                {
                    // Accept "false" or "nosurface" to turn it off.
                    if (args[3].Equals("false", StringComparison.OrdinalIgnoreCase) ||
                        args[3].Equals("nosurface", StringComparison.OrdinalIgnoreCase))
                    {
                        surfaceOnly = false;
                    }
                    // (Optionally accept "true"/"surface" explicitly, but they're redundant.)
                }

                // Resolve the pattern to block IDs once and reuse.
                int[] blockPatternNumbers =
                    GetClosestEnumValues<DNA.CastleMinerZ.Terrain.BlockTypeEnum>(blockPattern, BlockIDValues);

                if (blockPatternNumbers == null || blockPatternNumbers.Length == 0)
                {
                    SendFeedback($"ERROR: Invalid block pattern '{blockPattern}'.");
                    return;
                }

                // Center the square on the player (swap to GetUsersCursorLocation() if you prefer cursor).
                Vector3 center = GetUsersLocation();

                // Build the random positions first so SaveUndo captures the old state.
                HashSet<Vector3> region = new HashSet<Vector3>();

                // Safety so we don't spin forever if the radius is too small / columns are invalid.
                int attempts    = 0;
                int maxAttempts = amount * 50; // Up to 50 attempts per desired placement.

                while (region.Count < amount && attempts < maxAttempts)
                {
                    attempts++;

                    int offsetX = GenerateRandomNumber(-radius, radius);
                    int offsetZ = GenerateRandomNumber(-radius, radius);

                    int worldX = (int)center.X + offsetX;
                    int worldZ = (int)center.Z + offsetZ;

                    int worldY;

                    if (surfaceOnly)
                    {
                        // Find the first solid block from top to bottom at this (x, z),
                        // then place the new block just above it.
                        float baseHeight = WorldHeights.MinY;

                        for (int y = WorldHeights.MaxY; y > (WorldHeights.MinY - 1); y--)
                        {
                            Vector3 probePos = new Vector3(worldX, y, worldZ);

                            if (GetBlockFromLocation(probePos) != AirID)
                            {
                                baseHeight = y + 1f;
                                break;
                            }
                        }

                        if (baseHeight <= WorldHeights.MinY)
                            continue; // No surface found in this column, pick another spot.

                        worldY = (int)baseHeight;
                    }
                    else
                    {
                        // Pick any Y within the world range.
                        worldY = GenerateRandomNumber(WorldHeights.MinY, WorldHeights.MaxY);
                    }

                    Vector3 placePos = new Vector3(worldX, worldY, worldZ);

                    // Ensure Y is within world bounds (X/Z unconstrained, same as other commands).
                    if (!IsWithinWorldBounds(placePos, additionalHeight: 1, additionalDepth: 0))
                        continue;

                    // Avoid duplicate positions.
                    region.Add(placePos);
                }

                if (region.Count == 0)
                {
                    SendFeedback("ERROR: No valid locations found for /randomplace in the given radius.");
                    return;
                }

                // Capture old blocks for undo.
                await SaveUndo(region);
                ClearRedo();

                // Apply the new blocks and record redo data.
                HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();

                foreach (Vector3 blockLocation in region)
                {
                    // Get random block from input.
                    int block = GetRandomBlockFromPattern(blockPatternNumbers);

                    // Place block if it doesn't already exist. (improves the performance)
                    if (GetBlockFromLocation(blockLocation) != block)
                    {
                        // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                        if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                            !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                            TryDestroyCrateAt(blockLocation);

                        AsyncBlockPlacer.Enqueue(blockLocation, block);
                        redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, block));

                        SendFeedback($"RandomPlace: Placed block '{block}' at ({blockLocation.X}, {blockLocation.Y}, {blockLocation.Z}).");
                    }
                }

                await SaveUndo(redoBuilder);

                SendFeedback($"RandomPlace: Placed {redoBuilder.Count} block(s) within radius {radius} (surfaceOnly={surfaceOnly}).");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #region Timers

        #region World Wand

        private int _wandRunTimes;
        private void WorldWand_Tick(object sender, EventArgs e)
        {
            if (!IsNetworkSessionActive() || !_wandEnabled)
            {
                ((Timer)sender).Stop();
                _wandEnabled = false; WandEnabled = false;
                return;
            }

            // If the game does not have focus, do nothing.
            if (!IsGameWindowActive())
                return;

            // If the in-game menu is open, do nothing.
            if (IsInGameMenuOpen())
                return;

            // If the crafting menu is open, do nothing.
            if (IsCraftingMenuOpen())
                return;

            // If the chat console is open, do nothing.
            if (IsChatOpen())
                return;

            MouseState mouseState = Mouse.GetState();
            bool leftClick = mouseState.LeftButton == ButtonState.Pressed;
            bool rightClick = mouseState.RightButton == ButtonState.Pressed;

            if (GetUsersHeldItem() == WandItemID)
            {
                if (leftClick && _wandRunTimes == 0)
                {
                    _wandRunTimes++;
                    _pointToLocation1 = GetUsersCursorLocation();
                    SendFeedback("Position 1 " + _pointToLocation1 + " has been set!");
                }
                else if (!leftClick && !rightClick)
                {
                    _wandRunTimes = 0;
                }

                if (rightClick && _wandRunTimes == 0)
                {
                    _wandRunTimes++;
                    _pointToLocation2 = GetUsersCursorLocation();
                    SendFeedback("Position 2 " + _pointToLocation2 + " has been set!");
                }
            }
        }
        #endregion

        #region World Tool

        private int _toolRunTimes;
        private void WorldTool_Tick(object sender, EventArgs e)
        {
            if (!IsNetworkSessionActive() || !_toolEnabled)
            {
                ((Timer)sender).Stop();
                _toolEnabled = false; ToolEnabled = false;
                return;
            }

            // If the game does not have focus, do nothing.
            if (!IsGameWindowActive())
                return;

            // If the in-game menu is open, do nothing.
            if (IsInGameMenuOpen())
                return;

            // If the crafting menu is open, do nothing.
            if (IsCraftingMenuOpen())
                return;

            // If the chat console is open, do nothing.
            if (IsChatOpen())
                return;

            MouseState mouseState = Mouse.GetState();
            bool leftClick = mouseState.LeftButton == ButtonState.Pressed;

            if (GetUsersHeldItem() == _toolItem)
            {
                if (leftClick && _toolRunTimes == 0)
                {
                    _toolRunTimes++;

                    string command = _toolCommand; // Define the command string.

                    // Make sure it looks like a normal chat command.
                    if (!string.IsNullOrWhiteSpace(command) && !command.StartsWith("/"))
                        command = "/" + command;

                    // Reuse the dispatcher: same code path as typing in chat.
                    if (!_dispatcher.TryInvoke(command))
                    {
                        SendFeedback($"The command '{_toolCommand}' not found.");
                    }

                    #region Obsolete: Legacy Reflection-Based Tool Runner (Replaced By Dispatcher)

                    /*
                     * Legacy note:
                     * The code below used manual reflection to find and invoke ExecuteXxx methods
                     * from /tool commands. It has been replaced by _dispatcher.TryInvoke(command)
                     * so tools now use the same command pipeline as chat (aliases, instance methods,
                     * and future changes) and are easier to maintain.
                     */

                    /*
                    // Split the command into method name and parameters.
                    string[] commandParts = command.Split(' ');

                    // Construct the method name (e.g., "ExecuteTest").
                    string methodName = "Execute" + commandParts[0].TrimStart('/').ToLower();

                    // Get the method expecting a string[] parameter using reflection, ignoring case.
                    // INFO: For running the tool's method from a static void use typeof(Program).
                    MethodInfo method = GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);

                    if (method != null)
                    {
                        ParameterInfo[] methodParams = method.GetParameters();

                        if (methodParams.Length == 0)
                        {
                            // If the method has no parameters, invoke it directly.
                            method.Invoke(null, null);
                        }
                        else if (methodParams.Length == 1 && methodParams[0].ParameterType == typeof(string[]))
                        {
                            // Extract parameters from the command string (skip the command part, so start at index 1).
                            // .NET 8.0+ string[] parameters = commandParts.Length > 1 ? commandParts[1..] : new string[] { };
                            string[] parameters = new string[commandParts.Length - 1];
                            if (commandParts.Length > 1)
                            {
                                Array.Copy(commandParts, 1, parameters, 0, parameters.Length);
                            }

                            // Obsolete: This code is used to pass parameters to types.
                            // WorldEditCSharp passes parameters in a string argument.
                                // // Convert parameters to the appropriate types.
                                // ParameterInfo[] methodParams = method.GetParameters();
                                // object[] args = new object[methodParams.Length];
                                //
                                // for (int i = 0; i < methodParams.Length; i++)
                                // {
                                //     if (i < parameters.Length)
                                //     {
                                //         // Convert the string argument to the appropriate parameter type.
                                //         args[i] = Convert.ChangeType(parameters[i], methodParams[i].ParameterType);
                                //     }
                                // }
                                //
                                // // Invoke the method with the converted arguments.
                                // method.Invoke(null, args);

                            // Invoke the method with the parameters as a single string[] argument.
                            method.Invoke(null, new object[] { parameters });
                        }
                        else
                        {
                            SendFeedback($"The command '{command}' does not match any valid method signature.");
                        }
                    }
                    else
                    {
                        SendFeedback($"The command '{command}' not found.");
                    }
                    */
                    #endregion
                }
                else if (!leftClick)
                {
                    _toolRunTimes = 0;
                }
            }
        }
        #endregion

        #region World NavWand

        private Timer _navWandTimer;
        private bool  _navWandBusy;
        private int   _navWandRunTimes;

        /// <summary>
        /// Starts the always-on nav-wand timer.
        /// Summary: While holding <see cref="NavWandItemID"/> (default: Compass), left click = /jumpto and right click = /thru.
        /// </summary>
        private void StartNavWandTimer()
        {
            // Already running.
            if (_navWandTimer != null)
                return;

            _navWandTimer = new Timer() { Interval = 16 };
            _navWandTimer.Tick += WorldNavWand_Tick;
            _navWandTimer.Start();
        }

        /// <summary>
        /// Stops and clears the nav-wand timer.
        /// Summary: Fully shuts down nav-wand ticking and resets its click/busy state.
        /// </summary>
        private void StopNavWandTimer()
        {
            if (_navWandTimer == null)
                return;

            _navWandTimer.Stop();
            _navWandTimer.Tick -= WorldNavWand_Tick;
            _navWandTimer.Dispose();
            _navWandTimer = null;

            _navWandBusy = false;
            _navWandRunTimes = 0;
        }

        /// <summary>
        /// Handles nav-wand input.
        /// Summary: Provides quick navigation actions without typing commands.
        /// </summary>
        private async void WorldNavWand_Tick(object sender, EventArgs e)
        {
            // If we're not in a session, do nothing (timer stays alive across world changes).
            if (!IsNetworkSessionActive())
                return;

            // If the game does not have focus, do nothing.
            if (!IsGameWindowActive())
                return;

            // If the in-game menu is open, do nothing.
            if (IsInGameMenuOpen())
                return;

            // If the crafting menu is open, do nothing.
            if (IsCraftingMenuOpen())
                return;

            // If the chat console is open, do nothing.
            if (IsChatOpen())
                return;

            MouseState mouseState = Mouse.GetState();
            bool leftClick = mouseState.LeftButton == ButtonState.Pressed;
            bool rightClick = mouseState.RightButton == ButtonState.Pressed;

            // Always re-arm once the user releases input (even if we're busy).
            if (!leftClick && !rightClick)
                _navWandRunTimes = 0;

            // If an action is running, don't start another one.
            if (_navWandBusy)
                return;

            // Disabled via config?
            if (NavWandItemID < 0)
                return;

            int held = GetUsersHeldItem();
            if (held < 0 || held != NavWandItemID)
                return;

            // Left click -> /jumpto.
            if (leftClick && _navWandRunTimes == 0)
            {
                _navWandBusy = true;
                _navWandRunTimes++;

                try { ExecuteJumpTo(); }
                finally { _navWandBusy = false; }
                return;
            }

            // Right click -> /thru.
            if (rightClick && _navWandRunTimes == 0)
            {
                _navWandBusy = true;
                _navWandRunTimes++;

                try { await ExecuteThru(); }
                finally { _navWandBusy = false; }
                return;
            }
        }
        #endregion

        #region World Brush

        private Timer _brushTimer;        // The single timer that drives WorldBrush_Tick.
        private int _brushRunTimes;       // Set default values.
        private string _brushBlockPattern = "1";
        private string _brushShape        = "sphere";
        private int _brushSize            = 4;
        private int _brushHeight          = 8;
        private bool _brushHollow         = false;
        private bool _brushReplaceMode    = false;
        private bool _brushRapidMode      = false;
        private async void WorldBrush_Tick(object sender, EventArgs e)
        {
            if (!IsNetworkSessionActive() || !_brushEnabled)
            {
                if (sender is Timer t)
                {
                    t.Stop();
                    t.Tick -= WorldBrush_Tick;
                    t.Dispose();

                    if (ReferenceEquals(_brushTimer, t))
                        _brushTimer = null;
                }

                _brushEnabled  = false; BrushEnabled = false;
                _brushRunTimes = 0;
                return;
            }

            // If the game does not have focus, do nothing.
            if (!IsGameWindowActive())
                return;

            // If the in-game menu is open, do nothing.
            if (IsInGameMenuOpen())
                return;

            // If the crafting menu is open, do nothing.
            if (IsCraftingMenuOpen())
                return;

            // If the chat console is open, do nothing.
            if (IsChatOpen())
                return;

            MouseState mouseState = Mouse.GetState();
            bool leftClick = mouseState.LeftButton == ButtonState.Pressed;

            if (GetUsersHeldItem() == _brushItem) // Or use 'WandItemID'.
            {
                if (leftClick && (_brushRunTimes == 0 || _brushRapidMode))
                {
                    _brushRunTimes++;

                    // Define 1x1 region placeholder.
                    var cursorLocation = GetUsersCursorLocation();

                    // Define region placeholders.
                    var region = new HashSet<Tuple<Vector3, int>>();
                    var tempRegion = new HashSet<Vector3>(); // Temporary storage for Vector3-based regions.

                    // Check if the size is zero.
                    if (_brushSize == 0)
                    {
                        // Define 1x1 region.
                        tempRegion = new HashSet<Vector3>() { cursorLocation };
                    }
                    else
                    {
                        // Define the shape based on the users input.

                        // Get the center point.
                        Vector3 centerOffset = new Vector3(cursorLocation.X, cursorLocation.Y - (_brushSize / 2), cursorLocation.Z);
                        Vector3 buildLocation = (_brushReplaceMode) ? centerOffset  : centerOffset;

                        switch (_brushShape)
                        {
                            // Check if the from-block pattern contains air, and if so, have the region save it.
                            case "floor":
                                tempRegion = (_brushReplaceMode) ? await MakeFloor(cursorLocation, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakeFloor(cursorLocation, _brushSize, _brushHollow);
                                break;

                            case "cube":
                                tempRegion = (_brushReplaceMode) ? await MakeCube(buildLocation, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakeCube(buildLocation, _brushSize, _brushHollow);
                                break;

                            case "prism":
                                tempRegion = (_brushReplaceMode) ? await MakeTriangularPrism(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow, ignoreBlock: AirID) : await MakeTriangularPrism(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow);
                                break;

                            case "sphere":
                                tempRegion = (_brushReplaceMode) ? await MakeSphere(buildLocation, _brushSize, _brushSize, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakeSphere(buildLocation, _brushSize, _brushSize, _brushSize, _brushHollow);
                                break;

                            case "ring":
                                tempRegion = (_brushReplaceMode) ? await MakeRing(cursorLocation, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakeRing(cursorLocation, _brushSize, _brushHollow);
                                break;

                            case "pyramid":
                                tempRegion = (_brushReplaceMode) ? await MakePyramid(buildLocation, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakePyramid(buildLocation, _brushSize, _brushHollow);
                                break;

                            case "cone":
                                tempRegion = (_brushReplaceMode) ? await MakeCone(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow, 1, ignoreBlock: AirID) : await MakeCone(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow, 1);
                                break;

                            case "cylinder":
                                tempRegion = (_brushReplaceMode) ? await MakeCylinder(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow, ignoreBlock: AirID) : await MakeCylinder(buildLocation, _brushSize, _brushSize, _brushHeight, _brushHollow);
                                break;

                            case "diamond":
                                tempRegion = (_brushReplaceMode) ? await MakeDiamond(buildLocation, _brushSize, _brushHollow, false, ignoreBlock: AirID) : await MakeDiamond(buildLocation, _brushSize, _brushHollow, false);
                                break;

                            case "snow":
                                tempRegion = await MakeSnow(cursorLocation, _brushSize, _brushReplaceMode);
                                break;

                            case "floodfill":
                                tempRegion = await FloodFill(cursorLocation, _brushSize);
                                break;

                            case "tree":
                                region = MakeTree((int)cursorLocation.X, (int)cursorLocation.Z, _brushSize);
                                break;

                            case "schem":
                                if (copiedRegion.Count() == 0)
                                    SendFeedback("BRUSH: No schem data found. You need to first copy a region or import file.");
                                else
                                    region = await PasteRegion(buildLocation);
                                break;

                            default:
                                tempRegion = (_brushReplaceMode) ? await MakeSphere(buildLocation, _brushSize, _brushSize, _brushSize, _brushHollow, ignoreBlock: AirID) : await MakeSphere(buildLocation, _brushSize, _brushSize, _brushSize, _brushHollow);
                                break;
                        }
                    }

                    // Convert tempRegion to region with Tuple<Vector3, int>.
                    foreach (var vec in tempRegion)
                    {
                        region.Add(new Tuple<Vector3, int>(vec, -1));
                    }

                    // Get the current block type from the cursor position.
                    int cursorBlock = (_brushReplaceMode) ? GetBlockFromLocation(GetUsersCursorLocation()) : -1; // Use -1 if off to save all.

                    // Save the existing region and clear the upcoming redo.
                    // If replacemode is enabled, do not save the entire region, only the effected blocks.
                    if (_brushReplaceMode)
                        await SaveUndo(ExtractVector3HashSet(region), saveBlock: new int[] { cursorBlock });
                    else
                        await SaveUndo(ExtractVector3HashSet(region));
                    ClearRedo();

                    HashSet<Tuple<Vector3, int>> redoBuilder = new HashSet<Tuple<Vector3, int>>();
                    foreach (var (blockLocation, block) in region)
                    {
                        // Get random block from input.
                        int blockToUse = block;
                        if (block == -1)
                            blockToUse = GetRandomBlockFromPattern(_brushBlockPattern);

                        // Check for replace mode. If so, check if the current block is a block to replace, otherwise continue.
                        if (!_brushReplaceMode || (_brushReplaceMode && GetBlockFromLocation(blockLocation) == cursorBlock) || _brushShape == "snow")
                        {
                            // Place block if it doesn't already exist. (improves the performance)
                            if (GetBlockFromLocation(blockLocation) != blockToUse)
                            {
                                // If this location contains a crate, and we're not placing a crate, purge the crate contents.
                                if (DNA.CastleMinerZ.Terrain.BlockType.IsContainer(GetBlockTypeFromLocation(blockLocation)) &&
                                    !DNA.CastleMinerZ.Terrain.BlockType.IsContainer((DNA.CastleMinerZ.Terrain.BlockTypeEnum)block))
                                    TryDestroyCrateAt(blockLocation);

                                AsyncBlockPlacer.Enqueue(blockLocation, blockToUse);

                                // Add block to redo.
                                redoBuilder.Add(new Tuple<Vector3, int>(blockLocation, blockToUse));
                            }
                        }
                    }

                    // Save the actions to undo stack.
                    await SaveUndo(redoBuilder);
                }
                else if (!leftClick)
                {
                    _brushRunTimes = 0;
                }
            }
        }
        #endregion

        #endregion

        #region Benchmarking

        // ============================================================================
        // RNG Benchmark Chat Command
        // ----------------------------------------------------------------------------
        // Purpose:
        //   Dispatches an in-game benchmark of several RNG pickers and streams results
        //   to the player/chat via SendFeedback.
        //
        // Usage:
        //   /rngbench [iterations] [patternLen] [modes]
        //
        //   iterations : (optional) total samples per picker (default: 2,000,000)
        //   patternLen : (optional) histogram bucket count   (default: 64)
        //   modes      : (optional) which pickers to run:
        //                "all" | "original" | "getint32" | "buffered" | "fast"
        //                You can pass a comma list, e.g. "buffered,fast"
        //
        // Notes:
        //   - This only formats/forwards results; measurement logic lives in
        //     WorldEdit.RngBenchmark.Run(...).
        //   - Errors are caught and surfaced to chat as a single line.
        // ============================================================================

        #region /rngbench

        /// <summary>
        /// Runs the RNG benchmark harness and prints results to chat.
        /// </summary>
        /// <remarks>
        /// Examples:
        ///   /rngbench
        ///   /rngbench 1000000
        ///   /rngbench 2000000 64 buffered,fast
        ///   /rngbench 500000 128 getint32
        /// </remarks>
        [Command("//rngbench")]
        [Command("/rngbench")]
        private static void ExecuteRngBench(string[] args)
        {
            try
            {
                // Usage: /rngbench [iterations] [patternLen] [modes]
                //   modes: all | original | getint32 | buffered | fast | any comma-list (e.g., buffered,fast)
                int iterations = (args.Length > 0 && int.TryParse(args[0], out var it) && it > 0) ? it : 2000000;
                int patternLen = (args.Length > 1 && int.TryParse(args[1], out var pl) && pl > 0) ? pl : 64;
                string modes = (args.Length > 2) ? args[2] : "all";

                // Stream lines to chat/log.
                RngBenchmark.Run(iterations, patternLen, modes, s => SendFeedback(s));
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #endregion
    }
}
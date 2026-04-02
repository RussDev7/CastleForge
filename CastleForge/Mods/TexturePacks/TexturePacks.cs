/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static TexturePacks.TexturePackManager;
using static ModLoader.LogSystem;

namespace TexturePacks
{
    [Priority(Priority.Low)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class TexturePacks : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public TexturePacks() : base("TexturePacks", new Version("0.0.1"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
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
            var game = CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(TexturePacks).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Load or create config.
            var cfg = TPConfig.LoadOrCreate();

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Register this plugin's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            // Also register this plugin's command list to the global help registry.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded.");
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
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            TexturePackManager.Tick();
        }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General commands.
            ("tpset",       "Set a new active texturepack."),
            ("tpreset",     "Unload the active pack & reset to vanilla."),
            ("tpreload",    "Reload the active texturepack."),
            ("tpexportall", "Dumps all game content to \"...\\!Mods\\TexturePacks\\_Extracted\\<timestamp>\\\"."),

            // Debugging commands.
            // ("tpshader", "Show shader override status (FXB + cache + patch)."),
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /tpset

        [Command("/tpset")]
        private static void ExecuteTpSet(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("ERROR: Command usage /tpset [PackName]");
                return;
            }

            try
            {
                // Set the active pack.
                var cfg        = TPConfig.LoadOrCreate();
                cfg.ActivePack = args[0];
                cfg.Save();

                // Reload the TP manager with the new pack.
                TexturePackManager.RequestReload();

                // Send feedback.
                SendFeedback($"Pack '{cfg.ActivePack}' active - Texturepack Manager reloaded.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /tpreset

        [Command("/tpreset")]
        private static void ExecuteTpReset()
        {
            try
            {
                // Set the active pack to empty.
                var cfg = TPConfig.LoadOrCreate();
                cfg.ActivePack = "";
                cfg.Save();

                // Reload the TP manager with the empty pack.
                TexturePackManager.RequestReload();

                // Send feedback.
                SendFeedback($"Restored vanilla textures - Texturepack Manager reloaded.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /tpreload

        [Command("/tpreload")]
        private static void ExecuteTpReload()
        {
            try
            {
                // Reload the TP manager with the new pack.
                TexturePackManager.RequestReload();

                // Send feedback.
                SendFeedback($"Texturepack Manager reloaded.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /tpexportall

        /// <summary>
        /// /tpexportall
        /// Dumps a full vanilla extraction set to: !Mods\TexturePacks\_Extracted\<timestamp>\
        /// Run after content is loaded (main menu is perfect).
        /// </summary>
        [Command("/tpexportall")]
        private static void ExecuteTpExportAll()
        {
            try
            {
                TexturePackExtractor.ExportAll();
                SendFeedback("ExportAll complete. Check !Mods\\TexturePacks\\_Extracted\\<timestamp>\\");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        // Debugging Commands.

        #region /tpshader (debugging)

        /*
        /// <summary>
        /// /tpshader [clear] [reload]
        /// - Shows shader override status for Shaders/blockEffect:
        ///   • Active pack.
        ///   • FXB path + file info.
        ///   • Can we construct Effect(GraphicsDevice, fxb)?
        ///   • Is blockEffect cached inside ContentManager?
        ///   • Is the ContentManager.Load<Effect>(string) swap patch installed?
        /// - Optional:
        ///   • "clear"  => removes cached *Shaders/blockEffect* entries from ContentManager.
        ///   • "reload" => requests a texture pack reload (same as /tpreload).
        /// </summary>
        [Command("/tpshader")]
        private static void ExecuteTpShader(string[] args)
        {
            try
            {
                bool doClear  = HasArg(args, "clear") || HasArg(args, "purge");
                bool doReload = HasArg(args, "reload") || HasArg(args, "reapply");

                var cfg  = TPConfig.LoadOrCreate();
                var pack = cfg?.ActivePack ?? "";

                var root = TexturePackManager.PacksRoot; // ...\!Mods\TexturePacks
                var path = Path.Combine(root, pack, "Shaders", "blockEffect.fxb");

                SendFeedback($"ActivePack = '{pack}'.");
                SendFeedback($"Override FXB = {path}.");

                byte[] fxb = null;

                if (!File.Exists(path))
                {
                    SendFeedback("FXB: NOT FOUND (place it at <Pack>\\Shaders\\blockEffect.fxb).");
                }
                else
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        SendFeedback($"FXB: Present ({fi.Length} bytes, writeUtc={fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}).");
                    }
                    catch { SendFeedback("FXB: Present (file info unavailable)."); }

                    try
                    {
                        fxb = File.ReadAllBytes(path);
                        SendFeedback($"FXB: read OK ({fxb.Length} bytes).");
                    }
                    catch (Exception ex)
                    {
                        SendFeedback($"FXB: READ FAILED: {ex.GetType().Name}: {ex.Message}.");
                    }
                }

                // Validate: can XNA construct the Effect from these bytes?
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null)
                {
                    SendFeedback("GraphicsDevice: null (not in a renderable context yet).");
                }
                else if (fxb != null && fxb.Length > 0)
                {
                    try
                    {
                        var test = new Microsoft.Xna.Framework.Graphics.Effect(gd, fxb);

                        // Don't dispose immediately; retire after draw (your project already uses this pattern).
                        try { TexturePackManager.GpuRetireQueue.Enqueue(test); } catch { try { test.Dispose(); } catch { } }

                        SendFeedback("FXB: Effect(GraphicsDevice, bytes) constructed OK.");
                    }
                    catch (Exception ex)
                    {
                        SendFeedback($"FXB: Effect() FAILED: {ex.GetType().Name}: {ex.Message}.");
                    }
                }

                // Inspect ContentManager's loaded asset cache for any *Shaders/blockEffect* keys.
                var cm = gm?.Content;
                if (cm == null)
                {
                    SendFeedback("ContentManager: null.");
                }
                else
                {
                    if (TryGetLoadedAssetsDict(cm, out var dict, out var dictField))
                    {
                        var keys = FindKeysEndingWith(dict, "Shaders/blockEffect");
                        if (keys.Count == 0)
                        {
                            SendFeedback($"Content cache: blockEffect not loaded yet (dict='{dictField}').");
                        }
                        else
                        {
                            SendFeedback($"Content cache hits ({keys.Count}) in '{dictField}':");
                            foreach (var k in keys)
                            {
                                object v = null;
                                try { v = dict[k]; } catch { }

                                if (v is Microsoft.Xna.Framework.Graphics.Effect eff)
                                {
                                    int bcLen = TryGetEffectBytecodeLen(eff);
                                    SendFeedback($"  - {k} => Effect (bytecodeLen={bcLen}).");
                                    if (fxb != null && fxb.Length > 0 && bcLen > 0)
                                    {
                                        SendFeedback($"    - Matches FXB size? {(bcLen == fxb.Length ? "YES" : "NO")}.");
                                    }
                                }
                                else
                                {
                                    SendFeedback($"  - {k} => {(v == null ? "(null)" : v.GetType().FullName)}.");
                                }
                            }

                            if (doClear)
                            {
                                int removed = 0;
                                foreach (var k in keys)
                                {
                                    try
                                    {
                                        if (dict[k] is Microsoft.Xna.Framework.Graphics.Effect oldEff)
                                            try { TexturePackManager.GpuRetireQueue.Enqueue(oldEff); } catch { }
                                    }
                                    catch { }

                                    try { dict.Remove(k); removed++; } catch { }
                                }

                                SendFeedback($"Cleared {removed} cached blockEffect entr{(removed == 1 ? "y" : "ies")}.");
                            }
                        }
                    }
                    else
                    {
                        SendFeedback("Could not locate ContentManager loaded-assets table via reflection.");
                    }
                }

                // Check whether your swap patch is installed on ContentManager.Load<Effect>(string).
                try
                {
                    var loadGeneric = HarmonyLib.AccessTools.Method(typeof(Microsoft.Xna.Framework.Content.ContentManager),
                                                                   "Load", new Type[] { typeof(string) });
                    if (loadGeneric != null)
                    {
                        var loadEffect = loadGeneric.MakeGenericMethod(typeof(Microsoft.Xna.Framework.Graphics.Effect));
                        var info = HarmonyLib.Harmony.GetPatchInfo(loadEffect);

                        if (info == null)
                        {
                            SendFeedback("Swap patch: NOT PRESENT on ContentManager.Load<Effect>(string).");
                        }
                        else
                        {
                            bool hasOurPostfix = false;
                            try
                            {
                                foreach (var p in info.Postfixes)
                                {
                                    var dt = p?.PatchMethod?.DeclaringType;
                                    var n = dt != null ? dt.FullName : "";
                                    if (n != null && n.IndexOf("Patch_Content_Load_Effect_Swap", StringComparison.OrdinalIgnoreCase) >= 0)
                                        hasOurPostfix = true;
                                }
                            }
                            catch { }

                            SendFeedback($"Swap patch: {(hasOurPostfix ? "PRESENT" : "patched (unknown postfix owner)")}.");
                        }
                    }
                }
                catch
                {
                    SendFeedback("Swap patch: unable to query Harmony patch info.");
                }

                if (doReload)
                {
                    TexturePackManager.RequestReload();
                    SendFeedback("Requested texture pack reload.");
                }

                SendFeedback("Usage: /tpshader [clear] [reload]");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.GetType().Name}: {ex.Message}.");
            }
        }

        /// <summary>
        /// Returns true if args contains the given token (case-insensitive).
        /// Summary: Used by debug commands like "/tpshader clear reload".
        /// </summary>
        private static bool HasArg(string[] args, string token)
        {
            if (args == null || args.Length == 0) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], token, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Attempts to reflect ContentManager's internal "loaded assets" dictionary.
        /// Summary: Lets us inspect/evict cached content (e.g., shaders) without patching global load logic.
        ///
        /// Notes:
        /// - Field names differ between XNA/MonoGame variants, so we try common names first.
        /// - Falls back to the first IDictionary field if names don't match.
        /// </summary>
        private static bool TryGetLoadedAssetsDict(Microsoft.Xna.Framework.Content.ContentManager cm,
                                                  out System.Collections.IDictionary dict,
                                                  out string fieldName)
        {
            dict = null;
            fieldName = null;
            if (cm == null) return false;

            try
            {
                // Common field names in XNA/MonoGame variants.
                foreach (var name in new[] { "loadedAssets", "_loadedAssets", "LoadedAssets" })
                {
                    var fi = typeof(Microsoft.Xna.Framework.Content.ContentManager)
                             .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (fi == null) continue;

                    var obj = fi.GetValue(cm);
                    if (obj is System.Collections.IDictionary id)
                    {
                        dict = id;
                        fieldName = name;
                        return true;
                    }
                }

                // Fallback: First IDictionary field.
                foreach (var fi in typeof(Microsoft.Xna.Framework.Content.ContentManager)
                         .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!typeof(System.Collections.IDictionary).IsAssignableFrom(fi.FieldType)) continue;
                    var obj = fi.GetValue(cm);
                    if (obj is System.Collections.IDictionary id)
                    {
                        dict = id;
                        fieldName = fi.Name;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Finds all ContentManager cache keys that end with a suffix (case-insensitive).
        /// Summary: Used to locate assets regardless of whether they're "HiDefContent/..." or just "Shaders/...".
        /// </summary>
        private static System.Collections.Generic.List<string> FindKeysEndingWith(System.Collections.IDictionary dict, string suffix)
        {
            var hits = new System.Collections.Generic.List<string>();
            if (dict == null || suffix == null) return hits;

            foreach (var k in dict.Keys)
            {
                if (!(k is string s) || string.IsNullOrEmpty(s)) continue;
                var norm = s.Replace('\\', '/');
                if (norm.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    hits.Add(s);
            }
            return hits;
        }

        /// <summary>
        /// Tries to estimate an Effect's compiled bytecode size by scanning for the largest byte[] field.
        /// Summary: Debug-only signal to confirm "this is a real compiled shader blob" (FXB) is present.
        ///
        /// Notes:
        /// - Different XNA/MonoGame builds store bytecode in different private fields.
        /// - This doesn't validate correctness; it's just a quick length check for logging/comparison.
        /// </summary>
        private static int TryGetEffectBytecodeLen(Microsoft.Xna.Framework.Graphics.Effect fx)
        {
            if (fx == null) return -1;

            try
            {
                int best = -1;
                for (Type t = fx.GetType(); t != null; t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (f.FieldType != typeof(byte[])) continue;
                        try
                        {
                            if (f.GetValue(fx) is byte[] b && b.Length > best) best = b.Length;
                        }
                        catch { }
                    }
                }
                return best;
            }
            catch
            {
                return -1;
            }
        }
        */
        #endregion

        #endregion

        #endregion
    }
}
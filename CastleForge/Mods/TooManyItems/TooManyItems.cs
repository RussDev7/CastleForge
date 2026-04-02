/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using System.Linq;
using HarmonyLib;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;
using static TooManyItems.TMILog;

namespace TooManyItems
{
    [Priority(ModLoader.Priority.VeryLow)] // We want to load this LAST to capture modded content.
    [RequiredDependencies("ModLoaderExtensions")]
    public class TooManyItems : ModBase
    {
        /// <summary>
        /// Entrypoint for the TooManyItems mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public TooManyItems() : base("TooManyItems", new Version("1.0.0"))
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
            var ns    = typeof(TooManyItems).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Load config once at startup so all non-UI systems / patches see live values immediately.
            var cfg = TMIConfig.LoadOrCreate();
            TMIConfig.ApplyToStatics(cfg);
            ConfigGlobals._configApplied = true;

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
        /// Not used by this mod (but required by ModBase).
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime) { }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General commands.
            ("give [username] [item name|id] (amount) (health%)", "Give yourself or another player an item."),
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /give

        [Command("/give")]
        private static void CmdGive(string[] args)
        {
            // /give [username] [item name|id] (amount) (health%)
            // Examples:
            //   /give me dirt         -> 1 Dirt @ 100%.
            //   /give me dirt 5       -> 5 Dirt @ 100%.
            //   /give me dirt 5 50%   -> 5 Dirt @ 50%.
            //   /give me dirt 5 0.25  -> 5 Dirt @ 25%.
            //   /give me dirt 100%    -> 1 Dirt @ 100% (health only when it's the only numeric).

            if (args.Length < 2)
            {
                SendLog("Usage: /give [username] [item name|id] (amount) (health%)");
                return;
            }

            try
            {
                // 1) Parse trailing [amount] [health%] from the end.
                //    Rules:
                //      - A token like "75%" or "hp=75" or "health=75" is health.
                //      - Otherwise, a single trailing integer is the amount.
                //      - If both are present, amount comes first, then health.
                int last = args.Length - 1;

                int healthPct = 100;
                int amount    = 1;

                bool TryParseHealthToken(string s, out int pct)
                {
                    pct = 0;
                    if (string.IsNullOrWhiteSpace(s)) return false;
                    s = s.Trim();

                    // "75%"
                    if (s.EndsWith("%", StringComparison.Ordinal))
                    {
                        if (int.TryParse(s.Substring(0, s.Length - 1), out pct)) return true;
                        return false;
                    }

                    // "hp=75" or "health=75"
                    if (s.StartsWith("hp=", StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith("health=", StringComparison.OrdinalIgnoreCase))
                    {
                        var eq = s.IndexOf('=');
                        if (eq >= 0 && int.TryParse(s.Substring(eq + 1), out pct)) return true;
                        return false;
                    }

                    return false;
                }

                // Two trailing integers? Treat LAST as health% (0..100), PREVIOUS as amount.
                if (last >= 3 &&
                    int.TryParse(args[last], out var hpNum) && hpNum >= 0 && hpNum <= 100 &&
                    int.TryParse(args[last - 1], out var amtNum) && amtNum > 0)
                {
                    healthPct = TMIMathHelpers.Clamp(hpNum, 0, 100);
                    amount = amtNum;
                    last -= 2;                   // consumed two tokens
                }
                else
                {
                    // explicit health token at the very end: "75%" or "hp=75" or "health=75"
                    if (last >= 2 && TryParseHealthToken(args[last], out var hpTok))
                    {
                        healthPct = TMIMathHelpers.Clamp(hpTok, 0, 100);
                        last--;
                    }

                    // single trailing integer = amount
                    if (last >= 2 && int.TryParse(args[last], out var amtTok) && amtTok > 0)
                    {
                        amount = amtTok;
                        last--;
                    }
                }

                // 2) Username and item text (tokens [1..last], inclusive)
                if (last < 1)
                {
                    SendLog("ERROR: Missing item name or id.");
                    return;
                }

                // 2) Username and item text
                string userToken = args[0];
                // Use array overload: startIndex=1, count=(last - 1 + 1) = last
                string itemToken = string.Join(" ", args, 1, last);

                // 3) Resolve player
                NetworkGamer target = ResolveGamer(userToken, out string matchedTag);
                if (target == null)
                {
                    SendLog($"ERROR: No player matching \"{userToken}\".");
                    return;
                }

                // 4) Resolve item
                if (!TryResolveItemId(itemToken, out InventoryItemIDs itemId, out string matchedItem, out string why))
                {
                    SendLog($"ERROR: Item \"{itemToken}\" not found{why}.");
                    return;
                }

                // 5) Give
                GiveTo(target, itemId, amount, healthPct);

                SendLog($"Gave {amount} x {matchedItem} (id {(int)itemId}, {TMIMathHelpers.Clamp(healthPct, 0, 100)}% health) to {matchedTag}.");
            }
            catch (Exception ex)
            {
                if (GetLoggingMode() != LoggingType.None)
                    Log($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #region Typed Accessor Patches

        // Send private messages to DNA.CastleMinerZ.Net calls.
        public abstract class MessageBridge : DNA.Net.Message
        {
            public static T Get<T>() where T : DNA.Net.Message => GetSendInstance<T>();
        }

        // 1-arg broadcast.
        public static readonly MethodInfo DoSend = AccessTools.Method(typeof(DNA.Net.Message), "DoSend",
            new[] { typeof(LocalNetworkGamer) });

        // 2-arg direct-to-recipient.
        public static readonly MethodInfo DoSendDirect = AccessTools.Method(typeof(DNA.Net.Message), "DoSend",
            new[] { typeof(LocalNetworkGamer), typeof(NetworkGamer) });

        #endregion

        #region Helpers

        // Find gamer by exact, substring, then fuzzy(Levenshtein)
        static NetworkGamer ResolveGamer(string token, out string matchedTag)
        {
            matchedTag = null;
            var sess = CastleMinerZGame.Instance?.CurrentNetworkSession;
            if (sess == null) return null;

            // exact
            var exact = sess.AllGamers.FirstOrDefault(g => string.Equals(g.Gamertag, token, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { matchedTag = exact.Gamertag; return exact; }

            // contains
            var contains = sess.AllGamers.FirstOrDefault(g => g.Gamertag.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            if (contains != null) { matchedTag = contains.Gamertag; return contains; }

            // fuzzy
            string normTok = Norm(token);
            var best = sess.AllGamers
                .Select(g => new { G = g, S = Norm(g.Gamertag) })
                .Select(x => new { x.G, Score = NormLev(x.S, normTok) })
                .OrderBy(x => x.Score)
                .FirstOrDefault();

            if (best == null) return null;
            // require reasonably close
            if (best.Score > 0.6) return null; // 0 identical..1 far
            matchedTag = best.G.Gamertag;
            return best.G;
        }

        // Resolve InventoryItemIDs from number, item class name, or display name (fuzzy)
        static bool TryResolveItemId(string token, out InventoryItemIDs id, out string matchedName, out string why)
        {
            id = default;
            matchedName = null;
            why = "";

            // numeric?
            if (int.TryParse(token, out int num))
            {
                id = (InventoryItemIDs)num;
                var dict = GetAllItems();
                if (dict != null && dict.ContainsKey(id))
                {
                    matchedName = dict[id]?.Name ?? id.ToString();
                    return true;
                }
                why = " (unknown numeric id)";
                return false;
            }

            var items = GetAllItems();
            if (items == null || items.Count == 0)
            {
                why = " (item table unavailable)";
                return false;
            }

            string normTok = Norm(token);

            // Build candidates on BOTH enum names and display names
            var best = items.Select(kv => new
            {
                Id = kv.Key,
                Disp = kv.Value?.Name ?? kv.Key.ToString(),
                Score = Math.Min(
                                NormLev(Norm(kv.Value?.Name ?? ""), normTok),
                                NormLev(Norm(kv.Key.ToString()), normTok))
            })
                        .OrderBy(x => x.Score)
                        .First();

            // accept only "close enough"
            if (best.Score <= 0.45)
            {
                id = best.Id;
                matchedName = best.Disp;
                return true;
            }

            why = " (no close match)";
            return false;
        }

        static Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> GetAllItems()
        {
            var f = AccessTools.Field(typeof(InventoryItem), "AllItems");
            return f?.GetValue(null) as Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>;
        }

        static void GiveTo(NetworkGamer target, InventoryItemIDs itemId, int amount, int healthPct)
        {
            amount = Math.Max(1, amount);
            float health = TMIMathHelpers.Clamp(healthPct, 0, 100) / 100f;

            var from = (LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer;
            var player = target?.Tag as Player;
            Vector3 pos = (player?.LocalPosition ?? Vector3.Zero) + new Vector3(0, 1f, 0);

            int left = amount;
            while (left > 0)
            {
                int give = Math.Min(999, left); // split big amounts
                left -= give;

                var item = InventoryItem.CreateItem(itemId, give);
                item.ItemHealthLevel = health;

                // If you have a local/offline case, you can add directly:
                // CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory?.AddInventoryItem(item, false);

                // Network path (your existing message bridge)
                var msg = MessageBridge.Get<ConsumePickupMessage>();
                msg.PickupPosition = pos;
                msg.Item = item;
                msg.PickerUpper = target.Id;
                msg.SpawnerID = 0;
                msg.PickupID = 0;
                msg.DisplayOnPickup = false;

                DoSendDirect.Invoke(msg, new object[] { from, target });
            }
        }
        #endregion

        #region Tiny Fuzzy Utils

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // lower + remove spaces/underscores/dashes
            return new string(s.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray());
        }

        // normalized Levenshtein distance (0 perfect .. 1 worst)
        static double NormLev(string a, string b)
        {
            int d = Lev(a, b);
            int m = Math.Max(1, Math.Max(a.Length, b.Length));
            return (double)d / m;
        }

        static int Lev(string s, string t)
        {
            int n = s.Length, m = t.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            return dp[n, m];
        }
        #endregion

        #endregion
    }

    #region Extensions: GamerCollection<T>

    /// <summary>
    /// Extension helpers for <see cref="GamerCollection{T}"/>.
    /// These avoid extra allocations and let you stay LINQ-light where desired.
    /// </summary>
    internal static class GamerCollectionExtensions
    {
        /// <summary>
        /// Returns the first gamer that matches <paramref name="predicate"/>,
        /// or null/default if none match.
        /// </summary>
        /// <typeparam name="T">A type derived from <see cref="Gamer"/>.</typeparam>
        /// <param name="src">Source gamer collection.</param>
        /// <param name="predicate">Match predicate.</param>
        /// <returns>The first matching gamer, or default.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="src"/> or <paramref name="predicate"/> is null.</exception>
        public static T FirstOrDefault<T>(this GamerCollection<T> src, Func<T, bool> predicate)
            where T : Gamer
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            foreach (var x in src)
                if (predicate(x))
                    return x;

            return default;
        }

        /// <summary>
        /// Projects each gamer into a new form via <paramref name="selector"/>.
        /// (Lightweight alternative to Enumerable.Select for <see cref="GamerCollection{T}"/>.)
        /// </summary>
        /// <typeparam name="T">A type derived from <see cref="Gamer"/>.</typeparam>
        /// <typeparam name="TResult">Projection result type.</typeparam>
        /// <param name="src">Source gamer collection.</param>
        /// <param name="selector">Projection function.</param>
        /// <returns>Lazy sequence of projected values.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="src"/> or <paramref name="selector"/> is null.</exception>
        public static IEnumerable<TResult> Select<T, TResult>(this GamerCollection<T> src, Func<T, TResult> selector)
            where T : Gamer
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            foreach (var x in src)
                yield return selector(x);
        }
    }
    #endregion
}
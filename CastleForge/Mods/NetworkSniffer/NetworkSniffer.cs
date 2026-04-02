/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using System.Linq;
using System.Text;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace NetworkSniffer
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class NetworkSniffer : ModBase
    {
        /// <summary>
        /// Main mod entry for the NetworkSniffer. Wires command routing, hooks patches,
        /// and registers help text with the shared HelpRegistry.
        /// </summary>
        #region Mod wrapper + Commands

        // Routes "/commands" to methods marked with [Command(...)] in this type.
        private readonly CommandDispatcher _dispatcher;

        public NetworkSniffer() : base("NetworkSniffer", new Version("0.2.0"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        /// <summary>
        /// Called once by the ModLoader. We install our Harmony patches and hook
        /// the chat interceptor so our commands work (e.g. /sniff ...).
        /// </summary>
        public override void Start()
        {
            var game = CastleMinerZGame.Instance;
            if (game == null) { Log("Game instance is null."); return; }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(NetworkSniffer).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply all Harmony patches that make up the sniffer.
            GamePatches.ApplyAllPatches();

            // Hook our command dispatcher into the global chat pipeline.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));

            // Expose our commands in the global /help registry.
            HelpRegistry.Register(this.Name, _helpCommands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded. Use /sniff status.");
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

        // This mod doesn't need per-tick behavior.
        public override void Tick(InputManager inputManager, GameTime gameTime) { }

        // ----- Help entries for global /help -----
        // These show up under "* NetworkSniffer *" in your shared help formatter.
        private static readonly (string command, string description)[] _helpCommands = new (string, string)[]
        {
            ("/sniff on|off",                 "Enable/disable the sniffer"),
            ("/sniff status",                 "Show current sniffer settings"),
            ("/sniff only A,B",               "Log only these message types (reset include list)"),
            ("/sniff onlyadd A,B",            "Add message types to include list"),
            ("/sniff onlyclear",              "Clear include list (back to all)"),
            ("/sniff exclude A,B",            "Exclude these message types (reset)"),
            ("/sniff excludeadd A,B",         "Add to exclude list"),
            ("/sniff exclear",                "Clear exclude list"),
            ("/sniff sample N",               "Sample 1/N messages (N>=1)"),
            ("/sniff maxbytes N",             "Cap pretty payload length in chars (N>=0)"),
            ("/sniff raw on|off|cap N",       "Toggle raw byte hex dump / set cap (bytes)"),
            ("/sniff file <path>",            "Change output file (relative or absolute)"),
            ("/sniff types [page]",           "List known message types (paged)"),
            ("/sniff ignoreempties on|off",   "Skip logs whose payload is null or {...}"),
            ("/sniff prune on|off",           "Toggle pruning of trivial child members in payloads (e.g., drops {...} leaves)."),
            ("/sniff rx on|off",              "Toggle logging of inbound (Receive) messages"),
            ("/sniff tx on|off",              "Toggle logging of outbound (Send) messages"),
            ("/sniff dir both|rx|tx|none",    "Quick direction preset for RX/TX logging"),
        };

        // ----- Chat Command Entrypoint -----
        // Supports a suite of subcommands to control the sniffer without recompiling.
        [Command("/networksniffer")]
        [Command("/netsniffer")]
        [Command("/sniffer")]
        [Command("/sniff")]
        [Command("/log")]
        private static bool ExecuteNetworkSniffer(string[] args)
        {
            try
            {
                // Usage banner (shown for /sniff and unknown subcommands).
                string Usage() =>
                    "sniff on|off|status|only|onlyadd|onlyclear|exclude|excludeadd|exclear|sample|maxbytes|raw|file|types|ignoreempties|prune|rx|tx|dir\n" +
                    "  on/off                -> enable or disable sniffer\n" +
                    "  status                -> show current settings\n" +
                    "  only   A,B            -> log only these types (resets include list)\n" +
                    "  onlyadd A,B           -> add to include list (keeps existing)\n" +
                    "  onlyclear             -> clear include list (back to all types)\n" +
                    "  exclude A,B           -> set exclude list (resets list)\n" +
                    "  excludeadd A,B        -> add to exclude list (keeps existing)\n" +
                    "  exclear               -> clear exclude list\n" +
                    "  sample N              -> sample 1/N messages globally (>=1)\n" +
                    "  maxbytes N            -> cap pretty payload length (>=0)\n" +
                    "  raw on|off|cap N      -> toggle/cap raw byte dump (cap default 256)\n" +
                    "  file <path>           -> set output file (relative or absolute)\n" +
                    "  types [page]          -> list known message types\n" +
                    "  ignoreempties on|off  -> skip logs whose payload is null or {...}\n" +
                    "  prune on|off          -> drop trivial child members inside payloads\n" +
                    "  rx on|off             -> toggle logging of inbound (Receive) messages\n" +
                    "  tx on|off             -> toggle logging of outbound (Send) messages\n" +
                    "  dir both|rx|tx|none   -> quick preset for RX/TX logging";

                if (args == null || args.Length == 0)
                {
                    SendFeedback(Usage());
                    return true;
                }

                string sub = args[0].ToLowerInvariant();

                // Master toggle.
                if (sub == "on")  { SnifferSettings.Enabled = true;  SendFeedback("Sniffer ON.");  return true; }
                if (sub == "off") { SnifferSettings.Enabled = false; SendFeedback("Sniffer OFF."); return true; }

                // Dump current settings in chat.
                if (sub == "status") { PrintStatus(); return true; }

                // Replace the include list (whitelist).
                if (sub == "only")
                {
                    SnifferSettings.Include.Clear();
                    AddCsvTo(args, 1, SnifferSettings.Include);
                    SendFeedback("Sniffer include set.");
                    return true;
                }
                // Add to existing include list.
                if (sub == "onlyadd")
                {
                    AddCsvTo(args, 1, SnifferSettings.Include);
                    SendFeedback("Sniffer include updated.");
                    return true;
                }
                // Clear include list (revert to allow-all minus exclusions).
                if (sub == "onlyclear")
                {
                    SnifferSettings.Include.Clear();
                    SendFeedback("Sniffer include cleared.");
                    return true;
                }

                // Replace the exclude list (blacklist).
                if (sub == "exclude")
                {
                    SnifferSettings.Exclude.Clear();
                    AddCsvTo(args, 1, SnifferSettings.Exclude);
                    SendFeedback("Sniffer exclude set.");
                    return true;
                }
                // Add to existing exclude list.
                if (sub == "excludeadd")
                {
                    AddCsvTo(args, 1, SnifferSettings.Exclude);
                    SendFeedback("Sniffer exclude updated.");
                    return true;
                }
                // Clear exclude list.
                if (sub == "exclear")
                {
                    SnifferSettings.Exclude.Clear();
                    SendFeedback("Sniffer exclude cleared.");
                    return true;
                }

                // Global sampling: record 1 in every N messages for each type.
                if (sub == "sample")
                {
                    if (args.Length > 1 && int.TryParse(args[1], out var n) && n >= 1)
                        SnifferSettings.GlobalSampleN = n;
                    SendFeedback($"Sniffer sampling 1/{SnifferSettings.GlobalSampleN}");
                    return true;
                }

                // Pretty payload maximum characters (just UI/log safety).
                if (sub == "maxbytes")
                {
                    if (args.Length > 1 && int.TryParse(args[1], out var b) && b >= 0)
                        SnifferSettings.MaxBytes = b;
                    SendFeedback($"Sniffer payload cap {SnifferSettings.MaxBytes} chars.");
                    return true;
                }

                // Raw byte dumping control.
                if (sub == "raw")
                {
                    if (args.Length <= 1)
                    {
                        SendFeedback($"raw {(SnifferSettings.RawEnabled ? "on" : "off")}, cap {SnifferSettings.RawCap} bytes.");
                        return true;
                    }
                    var a = args[1].ToLowerInvariant();
                    if (a == "on")  { SnifferSettings.RawEnabled = true;  SendFeedback("Raw ON.");  return true; }
                    if (a == "off") { SnifferSettings.RawEnabled = false; SendFeedback("Raw OFF."); return true; }
                    if (a == "cap")
                    {
                        if (args.Length > 2 && int.TryParse(args[2], out var cap) && cap >= 0)
                        {
                            SnifferSettings.RawCap = cap;
                            SendFeedback($"Raw cap set to {SnifferSettings.RawCap} bytes.");
                        }
                        else SendFeedback("Usage: /sniff raw cap <bytes>.");
                        return true;
                    }
                    SendFeedback("Usage: /sniff raw on|off|cap <bytes>.");
                    return true;
                }

                // Change output log file. Relative paths go under !Mods/NetworkSniffer.
                if (sub == "file")
                {
                    if (args.Length <= 1) { SendFeedback("Usage: /sniff file <path>."); return true; }
                    var p = args[1].Trim();
                    string full = Path.IsPathRooted(p)
                        ? p
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "NetworkSniffer", p);
                    if (NetSnifferLog.SetOutputFile(full))
                        SendFeedback($"Sniffer file -> {full}.");
                    else
                        SendFeedback("Failed to set output file.");
                    return true;
                }

                // Pagination over discovered message types (for filtering/debugging).
                if (sub == "types")
                {
                    int page = 1, pageSize = 20;
                    if (args.Length > 1 && int.TryParse(args[1], out var p) && p >= 1) page = p;

                    var types = (SnifferSettings.KnownTypes ?? new List<string>())
                                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                    if (types.Count == 0) { SendFeedback("No known message types."); return true; }

                    int total = (int)Math.Ceiling(types.Count / (double)pageSize);
                    if (total < 1) total = 1;
                    if (page > total) page = total;

                    int start = (page - 1) * pageSize;
                    int end = Math.Min(start + pageSize, types.Count);

                    SendFeedback($"== Types {page}/{total} ({types.Count}) ==");
                    for (int i = start; i < end; i++) SendFeedback(types[i]);
                    if (page < total) SendFeedback($"> /sniff types {page + 1}.");
                    return true;
                }

                // Skip logs whose pretty payload is trivial (null, {...}, [], ...).
                if (sub == "ignoreempties")
                {
                    if (args.Length <= 1)
                    {
                        SendFeedback($"ignoreempties is {(SnifferSettings.IgnoreEmpties ? "on" : "off")}.");
                        return true;
                    }
                    var a = args[1].ToLowerInvariant();
                    if (a == "on")  { SnifferSettings.IgnoreEmpties = true;  SendFeedback("IgnoreEmpties ON.");  return true; }
                    if (a == "off") { SnifferSettings.IgnoreEmpties = false; SendFeedback("IgnoreEmpties OFF."); return true; }
                    SendFeedback("Usage: /sniff ignoreempties on|off.");
                    return true;
                }

                // Drop trivial child members while building the pretty payload string.
                if (sub == "prune" || sub == "pruneempties")
                {
                    if (args.Length == 1)
                    {
                        SendFeedback($"Prune empty members is {(SnifferSettings.PruneEmptyMembers ? "ON" : "OFF")}.");
                        return true;
                    }

                    var a = args[1].ToLowerInvariant();
                    if (a == "on")  { SnifferSettings.PruneEmptyMembers = true;  SendFeedback("Prune empty members: ON.");  return true; }
                    if (a == "off") { SnifferSettings.PruneEmptyMembers = false; SendFeedback("Prune empty members: OFF."); return true; }

                    SendFeedback("Usage: /sniff prune on|off.");
                    return true;
                }

                // Logic for - /sniff rx on|off  - turns Receive logging on/off.
                if (sub == "rx")
                {
                    if (args.Length <= 1) { SendFeedback($"RX logging is {(SnifferSettings.LogRX ? "ON" : "OFF")}."); return true; }
                    var a = args[1].ToLowerInvariant();
                    if (a == "on") { SnifferSettings.LogRX = true; SendFeedback("RX logging ON."); return true; }
                    if (a == "off") { SnifferSettings.LogRX = false; SendFeedback("RX logging OFF."); return true; }
                    SendFeedback("Usage: /sniff rx on|off.");
                    return true;
                }

                // Logic for - /sniff tx on|off  - turns Send logging on/off.
                if (sub == "tx")
                {
                    if (args.Length <= 1) { SendFeedback($"TX logging is {(SnifferSettings.LogTX ? "ON" : "OFF")}."); return true; }
                    var a = args[1].ToLowerInvariant();
                    if (a == "on") { SnifferSettings.LogTX = true; SendFeedback("TX logging ON."); return true; }
                    if (a == "off") { SnifferSettings.LogTX = false; SendFeedback("TX logging OFF."); return true; }
                    SendFeedback("Usage: /sniff tx on|off.");
                    return true;
                }

                // Logic for - /sniff dir both|rx|tx|none  - quick preset.
                if (sub == "dir" || sub == "direction")
                {
                    if (args.Length <= 1)
                    {
                        SendFeedback($"Direction: RX={(SnifferSettings.LogRX ? "on" : "off")}, TX={(SnifferSettings.LogTX ? "on" : "off")}.");
                        return true;
                    }

                    switch (args[1].ToLowerInvariant())
                    {
                        case "both": SnifferSettings.LogRX = SnifferSettings.LogTX = true; SendFeedback("Direction: BOTH."); return true;
                        case "rx": SnifferSettings.LogRX = true; SnifferSettings.LogTX = false; SendFeedback("Direction: RX only."); return true;
                        case "tx": SnifferSettings.LogRX = false; SnifferSettings.LogTX = true; SendFeedback("Direction: TX only."); return true;
                        case "none": SnifferSettings.LogRX = SnifferSettings.LogTX = false; SendFeedback("Direction: NONE."); return true;
                        default: SendFeedback("Usage: /sniff dir both|rx|tx|none."); return true;
                    }
                }

                // Unknown subcommand -> usage.
                SendFeedback(Usage());
                return true;
            }
            catch (Exception ex)
            {
                // Never bubble failures from chat commands back into the game.
                SendFeedback($"Command error: {ex.Message}.");
                return true;
            }

            // ---- helpers ----

            // Parse comma-separated values from args[index] into a set.
            void AddCsvTo(string[] a, int index, ISet<string> set)
            {
                if (a.Length <= index) return;
                foreach (var part in a[index].Split(','))
                {
                    var s = part?.Trim();
                    if (!string.IsNullOrEmpty(s)) set.Add(s);
                }
            }

            // Emit a quick multi-line status to chat.
            void PrintStatus()
            {
                string Inc() => SnifferSettings.Include.Count == 0 ? "(all)" : string.Join(", ", SnifferSettings.Include.OrderBy(x => x));
                string Exc() => SnifferSettings.Exclude.Count == 0 ? "(none)" : string.Join(", ", SnifferSettings.Exclude.OrderBy(x => x));

                SendFeedback($"Enabled: {SnifferSettings.Enabled}.");
                SendFeedback($"Include: {Inc()}.");
                SendFeedback($"Exclude: {Exc()}.");
                SendFeedback($"Sample: 1/{SnifferSettings.GlobalSampleN}");
                SendFeedback($"Payload cap: {SnifferSettings.MaxBytes} chars.");
                SendFeedback($"Raw: {(SnifferSettings.RawEnabled ? "on" : "off")} (cap {SnifferSettings.RawCap} bytes).");
                SendFeedback($"Ignore empties: {(SnifferSettings.IgnoreEmpties ? "on" : "off")}.");
                SendFeedback($"Prune empty members: {SnifferSettings.PruneEmptyMembers}.");
            }
        }
        #endregion
    }

    #region Settings / State

    /// <summary>
    /// CentraliMob runtime toggles and state for the sniffer. Most fields can be
    /// updated via chat commands at any time.
    /// </summary>
    internal static class SnifferSettings
    {
        // Master on/off switch.
        public static volatile bool Enabled = false;

        // Whitelist/blacklist. If Include has entries, only those type names log.
        public static readonly HashSet<string> Include = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Default high-rate messages we skip to reduce log volume & lag.
            "PlayerUpdateMessage",
            "RequestChunkMessage",
        };

        // Global 1/N sampling (per type). Set to 1 for "log everything".
        public static int GlobalSampleN = 1;

        // Pretty payload character cap (for human-readable dumps).
        public static int MaxBytes = 1024;

        // Raw hex dump controls (binary slice of the message body).
        public static bool RawEnabled = false;
        public static int  RawCap     = 256;

        // If true, drop lines where the pretty payload is trivial (null, {...}, [], ...).
        public static bool IgnoreEmpties = true;

        // If true, prune trivial child members during pretty printing (keeps logs dense).
        public static bool PruneEmptyMembers = true;

        // Discovered type names (for /sniff types).
        public static List<string> KnownTypes = new List<string>();

        public static bool LogRX = true; // Log Receive-side messages.
        public static bool LogTX = true; // Log Send-side messages.

        // Per-type sample counters (thread-safe).
        private static readonly ConcurrentDictionary<string, int> _sampleCounters =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Fast-path gate (include/exclude + sampling). The caller can optionally provide
        /// a known payload string to apply IgnoreEmpties without recomputing Describe twice.
        /// </summary>
        public static bool ShouldLog(string typeName, string prettyIfKnownNull = null)
        {
            if (!Enabled) return false;

            // Whitelist & blacklist.
            if (Include.Count > 0 && !Include.Contains(typeName)) return false;
            if (Exclude.Contains(typeName)) return false;

            // Optional early empty filter.
            if (IgnoreEmpties && prettyIfKnownNull != null && (prettyIfKnownNull == "null" || prettyIfKnownNull == "{...}"))
                return false;

            // 1/N sampling (per type).
            int n = Math.Max(1, GlobalSampleN);
            int next = _sampleCounters.AddOrUpdate(typeName, 1, (_, cur) => (cur >= n) ? 1 : cur + 1);
            return next == n;
        }
    }
    #endregion

    #region Logging

    /// <summary>
    /// File logger with aligned columns and bracket-tag prefixing, styled to match
    /// your existing ModLoader logging format.
    /// </summary>
    internal static class NetSnifferLog
    {
        private static readonly object _lock        = new object();
        private static readonly string _dir         = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "NetworkSniffer");
        private static string          _logPath     = Path.Combine(_dir, "NetworkCalls.txt");
        private static int             _alignColumn = 90; // Target column for message bodies.

        // Types to ignore while resolving the "real" caller for the [namespace] tag.
        private static readonly HashSet<string> _skipTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(NetSnifferLog).FullName,
            "ModLoaderExt.ChatSystem",
            "ModLoader.ModManager"
        };

        /// <summary>
        /// Initialize the log file on startup, writing a session header. We avoid
        /// leading blank lines by peeking at the last byte (if any).
        /// </summary>
        public static void Init(string logPath = null)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
                _logPath = logPath;

            Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? ".");
            bool needLeadingNewline = false;

            if (File.Exists(_logPath))
            {
                var fi = new FileInfo(_logPath);
                if (fi.Length > 0)
                {
                    using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length > 0)
                        {
                            fs.Seek(-1, SeekOrigin.End);
                            needLeadingNewline = (fs.ReadByte() != '\n'); // Handles CRLF gracefully.
                        }
                    }
                }
            }

            var header = $"[{DateTime.Now:O}][{typeof(NetSnifferLog).Namespace}] ===================================== New Session =====================================";
            lock (_lock)
            {
                File.AppendAllText(_logPath, (needLeadingNewline ? string.Empty : Environment.NewLine) + header + Environment.NewLine);
            }
        }

        /// <summary>Add a helper type to skip for caller tag resolution.</summary>
        public static void AddSkipType(Type t)
        {
            if (t?.FullName != null) _skipTypes.Add(t.FullName);
        }

        /// <summary>
        /// Core logging helper. Applies your bracket tag style and keeps message columns aligned.
        /// </summary>
        public static void Log(string msg)
        {
            try
            {
                // Find first non-helper frame to tag the line with its namespace.
                var st = new StackTrace(1, false); // Skip this Log frame.
                Type caller = null;
                foreach (var frame in st.GetFrames() ?? Array.Empty<StackFrame>())
                {
                    var type = frame.GetMethod()?.DeclaringType;
                    if (type == null) continue;
                    if (!_skipTypes.Contains(type.FullName))
                    {
                        caller = type;
                        break;
                    }
                }

                string callerNamespace = caller?.Namespace ?? typeof(NetSnifferLog).Namespace;

                // Pull off any leading [Tag] blocks from msg so we can place them
                // right after the [timestamp][namespace] prefix.
                SplitLeadingTags(msg, out string msgTags, out string msgRest);

                // Compose prefix (no trailing space yet).
                string prefix = $"[{DateTime.Now:O}][{callerNamespace}]{msgTags}";

                lock (_lock)
                {
                    // Keep first character of message bodies in the same column.
                    if (prefix.Length + 1 > _alignColumn)
                        _alignColumn = prefix.Length + 1;

                    int pad = Math.Max(1, _alignColumn - prefix.Length);
                    string line = prefix + new string(' ', pad) + msgRest + Environment.NewLine;
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // Never throw from logging.
            }
        }

        /// <summary>
        /// Extract contiguous leading "[...]" tag tokens from s, returning tags+rest.
        /// Matches your shared ModLoader style.
        /// </summary>
        public static void SplitLeadingTags(string s, out string tags, out string rest)
        {
            tags = string.Empty;
            rest = s ?? string.Empty;

            if (string.IsNullOrEmpty(rest) || rest[0] != '[')
                return;

            int i = 0;
            while (i < rest.Length && rest[i] == '[')
            {
                int close = rest.IndexOf(']', i + 1);
                if (close < 0) break;                     // Malformed -> stop.
                tags += rest.Substring(i, close - i + 1); // Append "[...]".
                i = close + 1;

                // Allow single space between adjacent tags.
                if (i < rest.Length && rest[i] == ' ')
                    i++;

                if (i >= rest.Length || rest[i] != '[')
                    break;
            }

            rest = (i < rest.Length) ? rest.Substring(i) : string.Empty;
            if (rest.StartsWith(" ")) rest = rest.Substring(1);
        }

        /// <summary>
        /// Switch the output file at runtime; creates parent directories as needed
        /// and writes a small "Switched Output" stamp.
        /// </summary>
        public static bool SetOutputFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string full = Path.GetFullPath(path);
                string dir  = Path.GetDirectoryName(full) ?? ".";
                Directory.CreateDirectory(dir);

                lock (_lock)
                {
                    _logPath = full;

                    bool hasContent = File.Exists(_logPath) && new FileInfo(_logPath).Length > 0;
                    var stamp = $"[{DateTime.Now:O}][{typeof(NetSnifferLog).Namespace}] ===================================== Switched Output =====================================";
                    File.AppendAllText(_logPath, (hasContent ? string.Empty : Environment.NewLine) + stamp + Environment.NewLine);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Pretty line writer for TX/RX messages. Enforces the pretty payload cap.
        /// </summary>
        public static void Write(string tag, string messageType, string payload = null)
        {
            try
            {
                string suffix = string.Empty;
                if (!string.IsNullOrEmpty(payload))
                {
                    if (SnifferSettings.MaxBytes >= 0 && payload.Length > SnifferSettings.MaxBytes)
                        payload = payload.Substring(0, SnifferSettings.MaxBytes) + "...";
                    suffix = " " + payload;
                }
                Log($"[{tag}][{messageType}]{suffix}");
            }
            catch { }
        }

        /// <summary>
        /// Raw hex dump writer (bounded by RawCap). Use with care-this can be noisy.
        /// </summary>
        public static void WriteRaw(string tag, string messageType, byte[] data, int len, bool truncated)
        {
            try
            {
                if (data == null || len <= 0) return;
                var sb = new StringBuilder(len * 3);
                for (int i = 0; i < len; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(data[i].ToString("X2"));
                }
                if (truncated) sb.Append(" ...");
                Log($"[{tag}][{messageType}] {sb}");
            }
            catch { }
        }
    }
    #endregion

    #region Pretty Description

    /// <summary>
    /// Converts a message object into a readable one-liner. Walks simple fields,
    /// bounded arrays, and common types (Vector2/3, Guid, enums, byte[]).
    /// Honors IgnoreEmpties and PruneEmptyMembers settings to control verbosity.
    /// </summary>
    internal static class MsgDescribe
    {
        // Optional per-type custom formatters (if some messages need special printing).
        private static readonly Dictionary<Type, Func<object, string>> _formatters =
            new Dictionary<Type, Func<object, string>>();

        // Cache of fields/properties per type so we don't reflect every time.
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> _memberCache =
            new ConcurrentDictionary<Type, MemberInfo[]>();

        public static void RegisterFormatter<T>(Func<T, string> f) where T : class
        {
            _formatters[typeof(T)] = o => f((T)o);
        }

        /// <summary>Top-level describe with formatter fallback.</summary>
        public static string Describe(object msg)
        {
            if (msg == null) return "null";

            var t = msg.GetType();
            if (_formatters.TryGetValue(t, out var fmt))
            {
                try { return fmt(msg); } catch { /* fall through */ }
            }
            return DumpObject(msg);
        }

        /// <summary>
        /// Recursive dumper with depth and item caps. Produces compact "{ A: 1, B: {...}, C: [...] }"
        /// style strings. Detects many primitive/known types explicitly.
        /// </summary>
        private static string DumpObject(object obj, int depth = 0, int maxDepth = 2, int maxItems = 8)
        {
            if (obj == null) return "null";
            var t = obj.GetType();

            // IMPORTANT: CMZUpdate stubs a bunch of NetworkGamer getters with NotImplementedException.
            // First-chance logging will spam even if we catch, so we must not invoke getters at all.
            if (obj is DNA.Net.GamerServices.NetworkGamer)
                return DescribeNetworkGamer(obj);

            // Primitives & common leaf cases.
            if (t.IsPrimitive || obj is string || obj is decimal) return PrimitiveToString(obj);
            if (obj is Guid) return obj.ToString();
            if (obj is Enum) return obj.ToString();
            if (obj is Vector2 v2) return $"{{X:{v2.X},Y:{v2.Y}}}";
            if (obj is Vector3 v3) return $"{{X:{v3.X},Y:{v3.Y},Z:{v3.Z}}}";
            if (obj is byte[] bytes) return HexPreview(bytes, 64);

            // IEnumerable (arrays, lists, etc.).
            if (obj is IEnumerable en && !(obj is string))
            {
                if (depth >= maxDepth) return "[...]";
                var sb = new StringBuilder();
                sb.Append("[");
                int i = 0;
                foreach (var it in en)
                {
                    if (i > 0) sb.Append(", ");
                    if (i >= maxItems) { sb.Append("..."); break; }
                    sb.Append(DumpObject(it, depth + 1, maxDepth, maxItems));
                    i++;
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Composite object: list members (fields + readable props).
            if (depth >= maxDepth) return "{...}";
            var members = _memberCache.GetOrAdd(t, DiscoverMembers);
            var parts = new List<string>(members.Length);

            foreach (var m in members)
            {
                object val = null;
                try
                {
                    if (m is PropertyInfo pi && pi.CanRead && pi.GetIndexParameters().Length == 0)
                        val = pi.GetValue(obj, null);
                    else if (m is FieldInfo fi)
                        val = fi.GetValue(obj);

                    var child = DumpObject(val, depth + 1, maxDepth, maxItems);

                    // Optionally drop trivial child nodes (less noise).
                    if (SnifferSettings.IgnoreEmpties && SnifferSettings.PruneEmptyMembers && IsTrivial(child))
                        continue;

                    parts.Add($"{m.Name}: {child}");
                }
                catch { /* skip broken getter/field */ }
            }

            // If everything was trivial, show a minimal marker.
            if (parts.Count == 0) return "{...}";
            return "{ " + string.Join(", ", parts) + " }";
        }

        /// <summary>Member discovery (props + fields), cached per type.</summary>
        private static MemberInfo[] DiscoverMembers(Type t)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var props = t.GetProperties(flags)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                         .Cast<MemberInfo>();

            var fields = t.GetFields(flags).Cast<MemberInfo>();

            // Remove compiler backing fields (noise).
            var all = props.Concat(fields)
                           .Where(m => !m.Name.Contains("k__BackingField"))
                           .ToArray();
            return all;
        }

        private static string PrimitiveToString(object v)
        {
            if (v is string s)
            {
                if (s.Length > 160) s = s.Substring(0, 157) + "...";
                return "\"" + s.Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
            }
            if (v is bool b) return b ? "true" : "false";
            if (v is char c) return "'" + c + "'";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string HexPreview(byte[] data, int max)
        {
            int n = Math.Min(data.Length, max);
            var sb = new StringBuilder(n * 3);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (data.Length > n) sb.Append(" ...");
            return sb.ToString();
        }

        /// <summary>
        /// Recognize trivial strings that add little value to logs.
        /// Used by IgnoreEmpties and PruneEmptyMembers behaviors.
        /// </summary>
        internal static bool IsTrivial(string s)
        {
            if (s == null) return true;
            var t = s.Trim();
            if (t.Length == 0) return true;

            // Common empties.
            if (string.Equals(t, "null", StringComparison.OrdinalIgnoreCase)) return true;
            if (t == "{...}" || t == "{}" || t == "[]" || t == "[...]") return true;

            // Braced/bracketed with only whitespace or ellipsis inside.
            if ((t.StartsWith("{") && t.EndsWith("}")) ||
                (t.StartsWith("[") && t.EndsWith("]")))
            {
                var inner = t.Substring(1, t.Length - 2).Trim();
                if (inner.Length == 0) return true;
                if (inner == "..." || inner == "...") return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a safe, readable label for a NetworkGamer without triggering stubbed/throwing getters.
        /// Tries fields first, then Gamertag, then Id->session lookup; falls back to "<NetworkGamer>".
        /// </summary>
        private static string DescribeNetworkGamer(object gamer)
        {
            if (gamer == null) return "<NetworkGamer>";

            // 1) Best-effort: string fields ONLY (walk base classes too).
            string name = TryFindStringFieldInTypeHierarchy(
                gamer,
                "Gamertag", "GamerTag", "_gamertag", "m_gamertag",
                "Name", "_name", "m_name"
            );

            if (!string.IsNullOrEmpty(name))
                return $"<{name}>";

            // 2) Known-good: Direct property access (single getter call, NOT reflection-enumeration).
            try
            {
                if (gamer is DNA.Net.GamerServices.NetworkGamer ng)
                {
                    var tag = ng.Gamertag;
                    if (!string.IsNullOrEmpty(tag))
                        return $"<{tag}>";

                    // 3) Fallback: use Id -> session lookup (proven method).
                    var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
                    if (session != null)
                    {
                        var id = ng.Id;
                        foreach (DNA.Net.GamerServices.NetworkGamer g in session.AllGamers)
                        {
                            if (g.Id == id)
                            {
                                var tag2 = g.Gamertag;
                                if (!string.IsNullOrEmpty(tag2))
                                    return $"<{tag2}>";
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Swallow; we still return a safe marker below.
            }

            return "<NetworkGamer>";
        }

        /// <summary>
        /// Reads a string field by name (or best guess) while walking base classes,
        /// so private fields declared on parent types can still be found.
        /// </summary>
        private static string TryFindStringFieldInTypeHierarchy(object obj, params string[] fieldNames)
        {
            if (obj == null) return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Walk derived -> base so we can see private fields declared on base types.
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                // Prefer known field names first.
                foreach (var n in fieldNames)
                {
                    var f = t.GetField(n, flags | BindingFlags.DeclaredOnly);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        var v = f.GetValue(obj) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }

                // Fallback: Scan plausible string fields.
                foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string)) continue;

                    var fn = f.Name.ToLowerInvariant();
                    if (fn.Contains("gamer") || fn.Contains("tag") || fn.Contains("name"))
                    {
                        var v = f.GetValue(obj) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }

            return null;
        }
    }
    #endregion
}
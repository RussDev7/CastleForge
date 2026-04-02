using System;
using System.Collections.Generic;
using System.IO;

namespace DedicatedServerLauncher
{
    internal static class DedicatedServerArgs
    {
        private static bool _loaded;
        private static bool _enabled;
        private static int _port = 61903;
        private static string _name = "Dedicated Server";
        private static string _password = "";

        public static bool Enabled
        {
            get
            {
                EnsureLoaded();
                return _enabled;
            }
        }

        public static int Port
        {
            get
            {
                EnsureLoaded();
                return _port;
            }
        }

        public static string Name
        {
            get
            {
                EnsureLoaded();
                return _name;
            }
        }

        public static string Password
        {
            get
            {
                EnsureLoaded();
                return _password;
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            try
            {
                string markerPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "!Mods",
                    "DedicatedServerLauncher",
                    "PendingDedicatedLaunch.txt");

                if (!File.Exists(markerPath))
                    return;

                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string raw in File.ReadAllLines(markerPath))
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    int idx = raw.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    string key = raw.Substring(0, idx).Trim();
                    string val = raw.Substring(idx + 1).Trim();
                    map[key] = val;
                }

                DateTime stampUtc;
                if (!map.ContainsKey("TimestampUtc") ||
                    !DateTime.TryParse(map["TimestampUtc"], null, System.Globalization.DateTimeStyles.RoundtripKind, out stampUtc))
                {
                    return;
                }

                // Only let a very recent launch claim dedicated mode.
                if ((DateTime.UtcNow - stampUtc).TotalSeconds > 20)
                    return;

                int port;
                if (map.ContainsKey("Port") && int.TryParse(map["Port"], out port) && port > 0 && port < 65536)
                    _port = port;

                if (map.ContainsKey("Name") && !string.IsNullOrWhiteSpace(map["Name"]))
                    _name = map["Name"];

                if (map.ContainsKey("Password"))
                    _password = map["Password"] ?? "";

                _enabled = true;

                // Consume marker so future normal launches do not become dedicated.
                try { File.Delete(markerPath); } catch { }
            }
            catch
            {
            }
        }
    }
}
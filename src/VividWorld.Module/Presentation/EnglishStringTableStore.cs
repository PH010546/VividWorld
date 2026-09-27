using System;
using System.Collections.Generic;
using System.IO;
using VividWorld.Core.Presentation;

namespace VividWorld.Presentation
{
    /// <summary>
    /// Loads and caches the English module string table from disk.
    /// Used for AI push dialogue and English recall queries without relying on active game language.
    /// </summary>
    internal static class EnglishStringTableStore
    {
        private static EnglishStringTable? _instance;
        private static readonly object _lock = new object();

        public static EnglishStringTable Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = Load();
                        }
                    }
                }
                return _instance;
            }
        }

        public static void SetInstanceForTesting(EnglishStringTable table)
        {
            lock (_lock)
            {
                _instance = table;
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        public static string Lookup(string? id, string? fallback)
        {
            return Instance.Lookup(id, fallback);
        }

        public static string GetWithFallback(string? id, string fallback)
        {
            return Instance.GetWithFallback(id, fallback);
        }

        private static EnglishStringTable Load()
        {
            string? path = ResolvePath();
            if (path != null && File.Exists(path))
            {
                try
                {
                    var table = EnglishStringTable.LoadFromFile(path);
                    ModLog.Info($"EnglishStringTableStore: loaded {table.Count} strings from {path}.");
                    return table;
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"EnglishStringTableStore: failed to load strings from {path}: {ex.Message}");
                }
            }
            else
            {
                ModLog.Warn("EnglishStringTableStore: could not locate std_module_strings_xml.xml.");
            }

            return new EnglishStringTable(new Dictionary<string, string>());
        }

        internal static string? ResolvePath()
        {
            try
            {
                // 1. 自己這份 dll 所在的模組（bin\Win64_Shipping_Client 往上兩層）。
                //    放第一順位：同時裝了正式版與開發版時，要讀的是正在跑的這一份
                string loc = typeof(EnglishStringTableStore).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string dir = Path.GetDirectoryName(loc)!;
                    string p0 = Path.Combine(dir, "..", "..", "ModuleData", "Languages", "std_module_strings_xml.xml");
                    if (File.Exists(p0)) return Path.GetFullPath(p0);
                }

                // 2. TaleWorlds BasePath Modules/VividWorld
                string p1 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld", "ModuleData", "Languages", "std_module_strings_xml.xml");
                if (File.Exists(p1)) return p1;

                // 3. TaleWorlds BasePath Modules/VividWorld.Dev
                string p2 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld.Dev", "ModuleData", "Languages", "std_module_strings_xml.xml");
                if (File.Exists(p2)) return p2;
            }
            catch
            {
                // Native paths may fail in offline or test contexts
            }

            try
            {
                // 4. Current working directory or parent directories
                string cwd = Directory.GetCurrentDirectory();
                string p4 = Path.Combine(cwd, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
                if (File.Exists(p4)) return p4;

                var parent = Directory.GetParent(cwd);
                while (parent != null)
                {
                    string candidate = Path.Combine(parent.FullName, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
                    if (File.Exists(candidate)) return candidate;
                    parent = parent.Parent;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}

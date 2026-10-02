using System;
using System.IO;
using VividWorld.Core.Feelings;

namespace VividWorld.Campaign
{
    /// <summary>載入出貨的感想句子目錄（<c>vividworld_feelings.json</c>）。找不到或壞掉時是空目錄：所有分享都不附感想，並在日誌說明。</summary>
    internal static class FeelingCatalogStore
    {
        public const string FileName = "vividworld_feelings.json";

        public static FeelingCatalog Catalog { get; private set; } = FeelingCatalog.Empty;
        public static string ActiveFilePath { get; private set; } = string.Empty;

        public static FeelingCatalog Load()
        {
            string? path = EventCatalogStore.ResolveCatalogFilePath(FileName);
            if (path == null)
            {
                ActiveFilePath = string.Empty;
                Catalog = FeelingCatalog.Empty;
                ModLog.Warn($"Feeling catalog: '{FileName}' not found, so NPCs will add no feeling after a rumor.");
                return Catalog;
            }

            ActiveFilePath = path;
            try
            {
                Catalog = FeelingCatalog.Parse(File.ReadAllText(path));
                foreach (var issue in Catalog.Issues)
                {
                    ModLog.Warn($"Feeling catalog: {issue}");
                }
                ModLog.Info($"Feeling catalog: loaded {Catalog.CellCount} category/mood cell(s) from {path}, {Catalog.Issues.Count} issue(s).");
            }
            catch (Exception ex)
            {
                ModLog.Error($"Feeling catalog: exception while reading {path}", ex);
                Catalog = FeelingCatalog.Empty;
            }
            return Catalog;
        }
    }
}

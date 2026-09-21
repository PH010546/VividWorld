using System;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;

namespace VividWorld.Campaign
{
    internal static class EventCatalogStore
    {
        public static EventCatalog Catalog { get; private set; } =
            new(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);

        public static EventCatalog SampleCatalog { get; private set; } =
            new(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);

        public static EventCatalog SituationCatalog { get; internal set; } =
            new(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);

        public static EventTemplate? TemplateByType(string type) =>
            Catalog.ByType(type) ?? SituationCatalog.ByType(type) ?? SampleCatalog.ByType(type);

        public static string ActiveFilePath { get; private set; } = string.Empty;
        public static string SampleFilePath { get; private set; } = string.Empty;

        public static EventCatalog Load(VividWorldConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            string? primaryFileName = config.Events.CatalogFile;
            string? foundPath = null;

            if (!string.IsNullOrWhiteSpace(primaryFileName))
            {
                foundPath = ResolveCatalogFilePath(primaryFileName);
            }

            if (foundPath == null && !string.Equals(primaryFileName, "vividworld_sample_events.json", StringComparison.OrdinalIgnoreCase))
            {
                foundPath = ResolveCatalogFilePath("vividworld_sample_events.json");
            }

            // Also try to load the sample catalog for fake event producer
            string? samplePath = ResolveCatalogFilePath("vividworld_sample_events.json");
            if (samplePath != null)
            {
                SampleFilePath = samplePath;
                try
                {
                    string sampleJson = File.ReadAllText(samplePath);
                    SampleCatalog = EventCatalogLoader.Load(sampleJson, config.Persistence, config.Events.StrictCatalog);
                    ModLog.Info($"Event catalog: loaded {SampleCatalog.Templates.Count} sample template(s) from {samplePath}.");
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Event catalog: exception while reading sample catalog at {samplePath}", ex);
                    SampleCatalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
                }
            }
            else
            {
                SampleFilePath = string.Empty;
                SampleCatalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
            }

            if (foundPath == null)
            {
                ActiveFilePath = string.Empty;
                Catalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
                ModLog.Warn($"Event catalog: no catalog file found for '{primaryFileName}' or fallback 'vividworld_sample_events.json'.");
                return Catalog;
            }

            ActiveFilePath = foundPath;
            try
            {
                string json = File.ReadAllText(foundPath);
                Catalog = EventCatalogLoader.Load(json, config.Persistence, config.Events.StrictCatalog);

                foreach (var issue in Catalog.Issues)
                {
                    if (issue.IsError)
                    {
                        ModLog.Warn($"Event catalog: template[{issue.TemplateIndex}] '{issue.TemplateType}' {issue.Field} - {issue.Code}: {issue.Detail}. Template skipped.");
                    }
                    else
                    {
                        ModLog.Info($"Event catalog: template[{issue.TemplateIndex}] '{issue.TemplateType}' {issue.Field} - {issue.Code}: {issue.Detail}. Kept.");
                    }
                }

                int errorCount = Catalog.Issues.Count(i => i.IsError);
                int warnCount = Catalog.Issues.Count(i => !i.IsError);
                ModLog.Info($"Event catalog: loaded {Catalog.Templates.Count} template(s) from {foundPath}, skipped {Catalog.SkippedCount}, {errorCount} error(s) {warnCount} warning(s).");

                return Catalog;
            }
            catch (Exception ex)
            {
                ModLog.Error($"Event catalog: exception while reading {foundPath}", ex);
                Catalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
                return Catalog;
            }
        }

        public static EventCatalog Reload(VividWorldConfig config)
        {
            return Load(config);
        }

        internal static string? ResolveCatalogFilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            try
            {
                string p1 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld", "ModuleData", fileName);
                if (File.Exists(p1)) return p1;

                string p2 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld.Dev", "ModuleData", fileName);
                if (File.Exists(p2)) return p2;

                string loc = typeof(EventCatalogStore).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string dir = Path.GetDirectoryName(loc)!;
                    string p3 = Path.Combine(dir, "..", "ModuleData", fileName);
                    if (File.Exists(p3)) return Path.GetFullPath(p3);
                }
            }
            catch
            {
                // File access fallback
            }

            return null;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Situations;

namespace VividWorld.Campaign
{
    internal static class SituationCatalogStore
    {
        public static SituationCatalog Catalog { get; private set; } =
            new(Array.Empty<SituationTemplate>(), Array.Empty<SituationIssue>(), 0);

        public static EventCatalog EventCatalog { get; private set; } =
            new(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);

        public static string ActiveFilePath { get; private set; } = string.Empty;
        public static string ActiveEventFilePath { get; private set; } = string.Empty;

        public static void Load(VividWorldConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // 1. Load situations catalog
            string? sitFileName = config.Situations.CatalogFile;
            string? sitPath = !string.IsNullOrWhiteSpace(sitFileName)
                ? EventCatalogStore.ResolveCatalogFilePath(sitFileName)
                : null;

            if (sitPath == null)
            {
                ActiveFilePath = string.Empty;
                Catalog = new SituationCatalog(Array.Empty<SituationTemplate>(), Array.Empty<SituationIssue>(), 0);
                ModLog.Warn($"Situation catalog: no catalog file found for '{sitFileName}'.");
            }
            else
            {
                ActiveFilePath = sitPath;
                try
                {
                    string json = File.ReadAllText(sitPath);
                    Catalog = SituationCatalogLoader.Load(json);

                    foreach (var issue in Catalog.Issues)
                    {
                        if (issue.IsError)
                        {
                            ModLog.Warn($"Situation catalog: situation[{issue.SituationIndex}] '{issue.SituationId}' {issue.Field} - {issue.Code}: {issue.Detail}. Situation skipped.");
                        }
                        else
                        {
                            ModLog.Info($"Situation catalog: situation[{issue.SituationIndex}] '{issue.SituationId}' {issue.Field} - {issue.Code}: {issue.Detail}. Kept.");
                        }
                    }

                    int errorCount = Catalog.Issues.Count(i => i.IsError);
                    int warnCount = Catalog.Issues.Count(i => !i.IsError);
                    ModLog.Info($"Situation catalog: loaded {Catalog.Situations.Count} situation(s) from {sitPath}, skipped {Catalog.SkippedCount}, {errorCount} error(s) {warnCount} warning(s).");
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Situation catalog: exception while reading {sitPath}", ex);
                    Catalog = new SituationCatalog(Array.Empty<SituationTemplate>(), Array.Empty<SituationIssue>(), 0);
                }
            }

            // 2. Load situation events catalog
            string? eventFileName = config.Situations.EventCatalogFile;
            string? eventPath = !string.IsNullOrWhiteSpace(eventFileName)
                ? EventCatalogStore.ResolveCatalogFilePath(eventFileName)
                : null;

            if (eventPath == null)
            {
                ActiveEventFilePath = string.Empty;
                EventCatalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
                EventCatalogStore.SituationCatalog = EventCatalog;
                ModLog.Warn($"Situation event catalog: no catalog file found for '{eventFileName}'.");
            }
            else
            {
                ActiveEventFilePath = eventPath;
                try
                {
                    string json = File.ReadAllText(eventPath);
                    EventCatalog = EventCatalogLoader.Load(json, config.Persistence, config.Events.StrictCatalog);
                    EventCatalogStore.SituationCatalog = EventCatalog;

                    foreach (var issue in EventCatalog.Issues)
                    {
                        if (issue.IsError)
                        {
                            ModLog.Warn($"Situation event catalog: template[{issue.TemplateIndex}] '{issue.TemplateType}' {issue.Field} - {issue.Code}: {issue.Detail}. Template skipped.");
                        }
                        else
                        {
                            ModLog.Info($"Situation event catalog: template[{issue.TemplateIndex}] '{issue.TemplateType}' {issue.Field} - {issue.Code}: {issue.Detail}. Kept.");
                        }
                    }

                    int errorCount = EventCatalog.Issues.Count(i => i.IsError);
                    int warnCount = EventCatalog.Issues.Count(i => !i.IsError);
                    ModLog.Info($"Situation event catalog: loaded {EventCatalog.Templates.Count} template(s) from {eventPath}, skipped {EventCatalog.SkippedCount}, {errorCount} error(s) {warnCount} warning(s).");
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Situation event catalog: exception while reading {eventPath}", ex);
                    EventCatalog = new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
                    EventCatalogStore.SituationCatalog = EventCatalog;
                }
            }

            // 3. Cross-check
            var crossIssues = SituationCatalogCrossCheck.Check(Catalog, EventCatalog, EventCatalogStore.Catalog);
            foreach (var issue in crossIssues)
            {
                if (issue.IsError)
                {
                    ModLog.Warn($"Situation cross-check: situation[{issue.SituationIndex}] '{issue.SituationId}' {issue.Field} - {issue.Code}: {issue.Detail}.");
                }
                else
                {
                    ModLog.Info($"Situation cross-check: situation[{issue.SituationIndex}] '{issue.SituationId}' {issue.Field} - {issue.Code}: {issue.Detail}.");
                }
            }

            ModLog.Info($"Situation cross-check: {crossIssues.Count} issue(s).");
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Dialogue
{
    public enum ModeNoticeAction
    {
        None = 0,
        Popup,
        Message
    }

    public class ModeNoticeRecord
    {
        [JsonProperty("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonProperty("detected")]
        public List<string> Detected { get; set; } = new List<string>();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public static class ModeNotice
    {
        public static ModeNoticeAction Evaluate(string? configuredMode,
                                                RumorMode actualMode,
                                                IReadOnlyList<string>? detectedModules,
                                                ModeNoticeRecord? lastRecord)
        {
            var conf = (configuredMode ?? "auto").Trim().ToLowerInvariant();
            if (conf == "casual" || conf == "realistic")
            {
                return ModeNoticeAction.None;
            }

            if (lastRecord == null)
            {
                return ModeNoticeAction.Popup;
            }

            string currentModeStr = actualMode.ToString().ToLowerInvariant();
            string lastModeStr = (lastRecord.Mode ?? string.Empty).Trim().ToLowerInvariant();

            if (!string.Equals(currentModeStr, lastModeStr, StringComparison.OrdinalIgnoreCase))
            {
                return ModeNoticeAction.Popup;
            }

            var currentDetected = detectedModules != null
                ? new List<string>(detectedModules)
                : new List<string>();
            currentDetected.Sort(StringComparer.OrdinalIgnoreCase);

            var lastDetected = lastRecord.Detected != null
                ? new List<string>(lastRecord.Detected)
                : new List<string>();
            lastDetected.Sort(StringComparer.OrdinalIgnoreCase);

            if (currentDetected.Count != lastDetected.Count)
            {
                return ModeNoticeAction.Popup;
            }

            for (int i = 0; i < currentDetected.Count; i++)
            {
                if (!string.Equals(currentDetected[i], lastDetected[i], StringComparison.OrdinalIgnoreCase))
                {
                    return ModeNoticeAction.Popup;
                }
            }

            return ModeNoticeAction.Message;
        }

        public static ModeNoticeRecord? Load(string filePath, IFileWriter? writer = null)
        {
            try
            {
                writer ??= new SystemFileWriter();
                var json = writer.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return VividJson.Read<ModeNoticeRecord>(json!);
            }
            catch
            {
                return null;
            }
        }

        public static bool Save(string filePath, ModeNoticeRecord record, IFileWriter? writer = null)
        {
            try
            {
                if (record == null || string.IsNullOrEmpty(filePath)) return false;
                writer ??= new SystemFileWriter();
                var sortedDetected = new List<string>(record.Detected ?? new List<string>());
                sortedDetected.Sort(StringComparer.OrdinalIgnoreCase);
                var toSave = new ModeNoticeRecord
                {
                    Mode = (record.Mode ?? string.Empty).Trim().ToLowerInvariant(),
                    Detected = sortedDetected,
                    Extra = record.Extra ?? new Dictionary<string, JToken>()
                };

                var json = VividJson.Write(toSave);
                return AtomicFile.Write(writer, filePath, json);
            }
            catch
            {
                return false;
            }
        }
    }
}

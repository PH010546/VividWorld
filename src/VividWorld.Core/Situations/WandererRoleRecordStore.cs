using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public enum WandererRoleLoadStatus
    {
        Success,
        FileNotFound,
        Corrupt
    }

    public sealed class WandererRoleLoadResult
    {
        public WandererRoleLoadStatus Status { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? SetAsidePath { get; set; }
        public WandererRoleData? Data { get; set; }
    }

    /// <summary>
    /// 流浪者職位交接紀錄儲存庫（檔案 wanderer_roles.json）。
    /// </summary>
    public sealed class WandererRoleRecordStore
    {
        private readonly string _filePath;
        private readonly IFileWriter _writer;
        private readonly ILogSink? _logSink;
        private WandererRoleData _data = new();

        public WandererRoleRecordStore(string filePath, IFileWriter writer, ILogSink? logSink = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _logSink = logSink;
        }

        public string FilePath => _filePath;
        public WandererRoleData Data => _data;
        public WandererRoleRecords Records => new(_data);

        public WandererRoleLoadResult Load()
        {
            if (!_writer.Exists(_filePath))
            {
                _data = new WandererRoleData();
                return new WandererRoleLoadResult
                {
                    Status = WandererRoleLoadStatus.FileNotFound,
                    Data = _data
                };
            }

            string? json = _writer.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return HandleCorrupt("File content was empty");
            }

            try
            {
                var obj = JObject.Parse(json!);
                var versionProp = obj.Property("version", StringComparison.OrdinalIgnoreCase);
                if (versionProp == null || versionProp.Value.Type != JTokenType.Integer || versionProp.Value.Value<int>() != 1)
                {
                    return HandleCorrupt("Unknown or missing version (expected version 1)");
                }

                var data = VividJson.Read<WandererRoleData>(json!);
                if (data == null)
                {
                    return HandleCorrupt("Failed to deserialize WandererRoleData");
                }

                data.Yesterday ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                data.Swaps ??= new List<WandererRoleSwapRecord>();

                _data = data;
                return new WandererRoleLoadResult
                {
                    Status = WandererRoleLoadStatus.Success,
                    Data = _data
                };
            }
            catch (Exception ex)
            {
                return HandleCorrupt(ex.Message);
            }
        }

        public bool Save()
        {
            string json = VividJson.Write(_data);
            return AtomicFile.Write(_writer, _filePath, json);
        }

        private WandererRoleLoadResult HandleCorrupt(string message)
        {
            _data = new WandererRoleData();

            string aside = _filePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            bool moved = false;
            try { moved = _writer.Move(_filePath, aside); } catch { moved = false; }
            _logSink?.Warn($"WandererRoleRecordStore: file unreadable ({message}) - moved to {aside} and started empty.");

            return new WandererRoleLoadResult
            {
                Status = WandererRoleLoadStatus.Corrupt,
                ExceptionMessage = message,
                SetAsidePath = moved ? aside : null,
                Data = _data
            };
        }
    }
}

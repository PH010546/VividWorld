#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using VividWorld.Core.Util;

namespace VividWorld.Core.Secrets
{
    public enum PlayerConfideQueueLoadStatus
    {
        Success,
        FileNotFound,
        Corrupt
    }

    public sealed class PlayerConfideQueueLoadResult
    {
        public PlayerConfideQueueLoadStatus Status { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? SetAsidePath { get; set; }
        public PlayerConfideQueueData? Data { get; set; }
        public int Count => Data?.Items?.Count ?? 0;
    }

    public sealed class PlayerConfideQueueStore
    {
        private readonly string _filePath;
        private readonly IFileWriter _writer;
        private readonly ILogSink? _logSink;
        private PlayerConfideQueueData _data = new();

        public PlayerConfideQueueStore(string filePath, IFileWriter writer, ILogSink? logSink = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _logSink = logSink;
        }

        public string FilePath => _filePath;
        public PlayerConfideQueueData Data => _data;
        public int Count => _data.Items.Count;

        public void Enqueue(PlayerConfideQueueItem item)
        {
            if (item == null) return;
            _data.Items.Add(item);
            Save();
        }

        public PlayerConfideQueueItem? Peek()
        {
            return _data.Items.Count > 0 ? _data.Items[0] : null;
        }

        public PlayerConfideQueueItem? Dequeue()
        {
            if (_data.Items.Count == 0) return null;
            var item = _data.Items[0];
            _data.Items.RemoveAt(0);
            Save();
            return item;
        }

        public PlayerConfideQueueLoadResult Load()
        {
            if (!_writer.Exists(_filePath))
            {
                _data = new PlayerConfideQueueData();
                return new PlayerConfideQueueLoadResult
                {
                    Status = PlayerConfideQueueLoadStatus.FileNotFound,
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

                var parsed = VividJson.Read<PlayerConfideQueueData>(json!);
                if (parsed == null)
                {
                    return HandleCorrupt("Failed to deserialize PlayerConfideQueueData");
                }

                parsed.Items ??= new();
                _data = parsed;
                return new PlayerConfideQueueLoadResult
                {
                    Status = PlayerConfideQueueLoadStatus.Success,
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
            try
            {
                string json = VividJson.Write(_data);
                return AtomicFile.Write(_writer, _filePath, json);
            }
            catch (Exception ex)
            {
                _logSink?.Error($"Failed to save player confide queue: {ex.Message}", ex);
                return false;
            }
        }

        private PlayerConfideQueueLoadResult HandleCorrupt(string message)
        {
            string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string backupPath = $"{_filePath}.corrupt_{ts}";
            _logSink?.Warn($"Player confide queue file corrupt ({message}), setting aside to: {backupPath}");

            try
            {
                if (_writer.Exists(_filePath))
                {
                    string content = _writer.ReadAllText(_filePath) ?? string.Empty;
                    _writer.WriteAllText(backupPath, content);
                }
            }
            catch (Exception ex)
            {
                _logSink?.Error($"Failed to backup corrupt player confide queue file: {ex.Message}", ex);
            }

            _data = new PlayerConfideQueueData();
            return new PlayerConfideQueueLoadResult
            {
                Status = PlayerConfideQueueLoadStatus.Corrupt,
                ExceptionMessage = message,
                SetAsidePath = backupPath,
                Data = _data
            };
        }
    }
}

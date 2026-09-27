using System;
using System.Collections.Generic;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// Tracks what has been pushed to AI dialogue mods per target, hero, and event.
    /// Format: TargetName -> NpcId -> EventId -> AiPushRecord(version, pushedDay).
    /// </summary>
    public sealed class AiPushedStore
    {
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>> _data;
        private readonly object _lock = new object();
        private bool _isDirty;

        public bool IsDirty
        {
            get
            {
                lock (_lock)
                {
                    return _isDirty;
                }
            }
        }

        public AiPushedStore()
        {
            _data = new Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>>(StringComparer.OrdinalIgnoreCase);
        }

        public AiPushedStore(Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>>? data)
        {
            _data = data != null
                ? new Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>>(data, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasPushedVersion(string targetId, string npcId, string eventId, string version)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(eventId))
                return false;

            lock (_lock)
            {
                if (_data.TryGetValue(targetId, out var targetMap) &&
                    targetMap.TryGetValue(npcId, out var heroMap) &&
                    heroMap.TryGetValue(eventId, out var record))
                {
                    return string.Equals(record.Version, version, StringComparison.Ordinal);
                }
                return false;
            }
        }

        public void Record(string targetId, string npcId, string eventId, string version, double pushedDay)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(eventId))
                return;

            lock (_lock)
            {
                if (!_data.TryGetValue(targetId, out var targetMap))
                {
                    targetMap = new Dictionary<string, Dictionary<string, AiPushRecord>>(StringComparer.OrdinalIgnoreCase);
                    _data[targetId] = targetMap;
                }
                if (!targetMap.TryGetValue(npcId, out var heroMap))
                {
                    heroMap = new Dictionary<string, AiPushRecord>(StringComparer.OrdinalIgnoreCase);
                    targetMap[npcId] = heroMap;
                }
                heroMap[eventId] = new AiPushRecord
                {
                    Version = version ?? string.Empty,
                    PushedDay = pushedDay
                };
                _isDirty = true;
            }
        }

        public IReadOnlyDictionary<string, AiPushRecord> GetPushedForHero(string targetId, string npcId)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(npcId))
                return new Dictionary<string, AiPushRecord>();

            lock (_lock)
            {
                if (_data.TryGetValue(targetId, out var targetMap) &&
                    targetMap.TryGetValue(npcId, out var heroMap))
                {
                    return new Dictionary<string, AiPushRecord>(heroMap, StringComparer.OrdinalIgnoreCase);
                }
                return new Dictionary<string, AiPushRecord>();
            }
        }

        public string Serialize()
        {
            lock (_lock)
            {
                return VividJson.Write(_data);
            }
        }

        public static AiPushedStore Load(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AiPushedStore();
            }

            try
            {
                var dict = VividJson.Read<Dictionary<string, Dictionary<string, Dictionary<string, AiPushRecord>>>>(json!);
                return new AiPushedStore(dict);
            }
            catch
            {
                return new AiPushedStore();
            }
        }

        public static AiPushedStore LoadFromFile(string filePath, IFileWriter? writer = null)
        {
            writer ??= new SystemFileWriter();
            if (!writer.Exists(filePath))
            {
                return new AiPushedStore();
            }

            try
            {
                string? json = writer.ReadAllText(filePath);
                return Load(json);
            }
            catch
            {
                return new AiPushedStore();
            }
        }

        public bool SaveToFile(string filePath, IFileWriter? writer = null)
        {
            writer ??= new SystemFileWriter();
            string json = Serialize();
            bool ok = AtomicFile.Write(writer, filePath, json);
            if (ok)
            {
                lock (_lock)
                {
                    _isDirty = false;
                }
            }
            return ok;
        }
    }
}

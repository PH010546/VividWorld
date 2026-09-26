#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 事件分片儲存庫。
    /// 依天數區間分片儲存事件，維護持久化索引 _index.json 與記憶體快取。
    /// </summary>
    public sealed class EventShardStore
    {
        private readonly string _eventsFolder;
        private readonly int _shardDays;
        private readonly IFileWriter _writer;
        private readonly string _indexPath;

        private readonly Dictionary<string, List<WorldEvent>> _shardCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirtyShards = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastTouchedFlush = new(StringComparer.OrdinalIgnoreCase);
        private int _flushCount;
        private RumorIndex? _currentIndex;
        private bool _indexDirty;

        public string LastLoadNote { get; private set; } = string.Empty;
        public int IdleFlushesBeforeRelease { get; }
        public int CachedShardCount => _shardCache.Count;
        public int CachedEventCount => _shardCache.Values.Sum(list => list?.Count ?? 0);
        public int ReleasedTotal { get; private set; }
        public IReadOnlyList<string> LastReleased { get; private set; } = Array.Empty<string>();

        public EventShardStore(string eventsFolder, int shardDays, IFileWriter writer, int idleFlushesBeforeRelease = 0)
        {
            _eventsFolder = eventsFolder ?? throw new ArgumentNullException(nameof(eventsFolder));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _shardDays = shardDays <= 0 ? 1 : shardDays;
            IdleFlushesBeforeRelease = idleFlushesBeforeRelease < 0 ? 0 : idleFlushesBeforeRelease;
            _indexPath = Path.Combine(_eventsFolder, "_index.json");
        }

        private void TouchShard(string shardKey)
        {
            if (!string.IsNullOrEmpty(shardKey))
            {
                _lastTouchedFlush[shardKey] = _flushCount;
            }
        }

        public RumorIndex LoadIndex()
        {
            try
            {
                var json = _writer.ReadAllText(_indexPath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = VividJson.Read<RumorIndex>(json!);
                    if (parsed != null)
                    {
                        if (parsed.FormatVersion < RumorIndex.CurrentFormatVersion)
                        {
                            LastLoadNote = $"rebuilt from shards: index format {parsed.FormatVersion} < {RumorIndex.CurrentFormatVersion}";
                            return RebuildIndexFromShards();
                        }

                        parsed.RebuildLookup();
                        _currentIndex = parsed;
                        _indexDirty = false;
                        LastLoadNote = $"read _index.json (format {parsed.FormatVersion})";
                        return parsed;
                    }
                }

                // 檔案不存在、讀不到、或 VividJson.Read 回傳 null → 從分片重建
                LastLoadNote = "rebuilt from shards: _index.json missing or unreadable";
                return RebuildIndexFromShards();
            }
            catch
            {
                LastLoadNote = "rebuilt from shards: _index.json missing or unreadable";
                return RebuildIndexFromShards();
            }
        }

        private RumorIndex RebuildIndexFromShards()
        {
            var newIndex = new RumorIndex { FormatVersion = RumorIndex.CurrentFormatVersion };
            try
            {
                var files = _writer.ListFiles(_eventsFolder, "d*.json");
                foreach (var file in files)
                {
                    if (string.IsNullOrEmpty(file)) continue;
                    if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    var shardContent = _writer.ReadAllText(file);
                    if (string.IsNullOrEmpty(shardContent)) continue;

                    var events = ReadShardEvents(shardContent!);
                    if (events == null) continue;

                    var shardKey = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(shardKey))
                    {
                        _shardCache[shardKey] = events;
                        TouchShard(shardKey);
                    }

                    foreach (var evt in events)
                    {
                        if (evt == null || string.IsNullOrEmpty(evt.EventId)) continue;
                        var entry = RumorIndexEntry.From(evt, _shardDays);
                        newIndex.Upsert(entry);
                    }
                }

                newIndex.RebuildLookup();
                _currentIndex = newIndex;

                // 重建完成後沖寫一次 _index.json
                var indexJson = VividJson.Write(newIndex);
                if (AtomicFile.Write(_writer, _indexPath, indexJson))
                {
                    _indexDirty = false;
                }
            }
            catch
            {
                // 絕不拋出例外
            }

            return newIndex;
        }

        private static List<WorldEvent>? ReadShardEvents(string content)
        {
            var events = VividJson.Read<List<WorldEvent>>(content);
            if (events != null) return events;

            // 分片讀取失敗時（如某則事件帶舊物件形狀的 relationImpacts），逐筆嘗試反序列化讓其餘事件存活
            try
            {
                var jArray = JArray.Parse(content);
                var result = new List<WorldEvent>();
                var serializer = JsonSerializer.Create(VividJson.Settings);
                foreach (var item in jArray)
                {
                    try
                    {
                        var evt = item.ToObject<WorldEvent>(serializer);
                        if (evt != null && !string.IsNullOrEmpty(evt.EventId))
                        {
                            result.Add(evt);
                        }
                    }
                    catch
                    {
                        // 單一損毀事件跳過，其餘事件存活
                    }
                }
                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        public WorldEvent? Load(string eventId, RumorIndex? index)
        {
            try
            {
                if (string.IsNullOrEmpty(eventId)) return null;

                var targetIndex = index ?? _currentIndex ?? LoadIndex();
                var entry = targetIndex.Find(eventId);
                if (entry == null && _currentIndex != null && !ReferenceEquals(_currentIndex, targetIndex))
                {
                    entry = _currentIndex.Find(eventId);
                }

                var shardKey = entry?.ShardKey;
                if (string.IsNullOrEmpty(shardKey))
                {
                    // 索引找不到時，檢查是否在髒快取中
                    foreach (var kvp in _shardCache)
                    {
                        var found = kvp.Value.Find(e => e != null && e.EventId == eventId);
                        if (found != null)
                        {
                            TouchShard(kvp.Key);
                            return found;
                        }
                    }
                    return null;
                }

                if (_shardCache.TryGetValue(shardKey!, out var cachedEvents))
                {
                    TouchShard(shardKey!);
                    return cachedEvents.Find(e => e != null && e.EventId == eventId);
                }

                var shardPath = Path.Combine(_eventsFolder, $"{shardKey}.json");
                var content = _writer.ReadAllText(shardPath);
                if (string.IsNullOrEmpty(content)) return null;

                var events = ReadShardEvents(content!);
                if (events == null) return null;

                _shardCache[shardKey!] = events;
                TouchShard(shardKey!);
                return events.Find(e => e != null && e.EventId == eventId);
            }
            catch
            {
                return null;
            }
        }

        public void Upsert(WorldEvent evt)
        {
            try
            {
                if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;

                var shardKey = ShardKey.For(evt.Day, _shardDays);
                TouchShard(shardKey);
                if (!_shardCache.TryGetValue(shardKey, out var events))
                {
                    var shardPath = Path.Combine(_eventsFolder, $"{shardKey}.json");
                    var content = _writer.ReadAllText(shardPath);
                    if (!string.IsNullOrEmpty(content))
                    {
                        events = ReadShardEvents(content!) ?? new List<WorldEvent>();
                    }
                    else
                    {
                        events = new List<WorldEvent>();
                    }
                    _shardCache[shardKey] = events;
                }

                var existingIdx = events.FindIndex(e => e != null && e.EventId == evt.EventId);
                if (existingIdx >= 0)
                {
                    events[existingIdx] = evt;
                }
                else
                {
                    events.Add(evt);
                }

                _dirtyShards.Add(shardKey);

                if (_currentIndex == null)
                {
                    LoadIndex();
                }

                if (_currentIndex != null)
                {
                    var entry = RumorIndexEntry.From(evt, _shardDays);
                    _currentIndex.Upsert(entry);
                    _indexDirty = true;
                }
            }
            catch
            {
                // 絕不拋出例外
            }
        }

        public void Flush()
        {
            try
            {
                var succeededShards = new List<string>();
                foreach (var shardKey in _dirtyShards)
                {
                    if (!_shardCache.TryGetValue(shardKey, out var events))
                    {
                        succeededShards.Add(shardKey);
                        continue;
                    }

                    var shardPath = Path.Combine(_eventsFolder, $"{shardKey}.json");
                    var json = VividJson.Write(events);
                    if (AtomicFile.Write(_writer, shardPath, json))
                    {
                        succeededShards.Add(shardKey);
                    }
                }

                foreach (var shardKey in succeededShards)
                {
                    _dirtyShards.Remove(shardKey);
                }

                // 索引只在「所有髒分片都寫成功」之後才寫。
                //
                // 若某片失敗卻仍寫索引，索引會跑到分片前面：它宣稱事件 X 在分片 B，
                // 但 B 的磁碟內容是舊的、沒有 X。此時 Load(X) 回傳 null，而 KnownByIndex
                // 仍會把 X 列給某個英雄 —— 對話層會提供一則載不出來的傳聞，靜默失敗。
                //
                // 索引落後是安全的（事件暫時不被索引到，記憶體裡還在，下次沖寫補上）；
                // 索引超前是懸空指標。兩害相權，寧可落後。
                if (_indexDirty && _dirtyShards.Count == 0 && _currentIndex != null)
                {
                    var indexJson = VividJson.Write(_currentIndex);
                    if (AtomicFile.Write(_writer, _indexPath, indexJson))
                    {
                        _indexDirty = false;
                    }
                }

                _flushCount++;

                if (IdleFlushesBeforeRelease > 0)
                {
                    var released = new List<string>();
                    var cachedKeys = new List<string>(_shardCache.Keys);
                    foreach (var shardKey in cachedKeys)
                    {
                        if (_dirtyShards.Contains(shardKey))
                        {
                            continue;
                        }

                        int lastTouched = _lastTouchedFlush.TryGetValue(shardKey, out int t) ? t : 0;
                        if (_flushCount - lastTouched >= IdleFlushesBeforeRelease)
                        {
                            _shardCache.Remove(shardKey);
                            _lastTouchedFlush.Remove(shardKey);
                            released.Add(shardKey);
                        }
                    }

                    released.Sort(StringComparer.OrdinalIgnoreCase);
                    LastReleased = released;
                    ReleasedTotal += released.Count;
                }
                else
                {
                    LastReleased = Array.Empty<string>();
                }
            }
            catch
            {
                // 絕不拋出例外
            }
        }

        public (int RemovedCount, int TouchedShards) Remove(IReadOnlyCollection<string> eventIds)
        {
            if (eventIds == null || eventIds.Count == 0) return (0, 0);

            var targetIndex = _currentIndex ?? LoadIndex();
            var touchedShards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int removedCount = 0;

            foreach (var eventId in eventIds)
            {
                if (string.IsNullOrEmpty(eventId)) continue;

                var entry = targetIndex.Find(eventId);
                string? shardKey = entry?.ShardKey;

                if (string.IsNullOrEmpty(shardKey))
                {
                    foreach (var kvp in _shardCache)
                    {
                        if (kvp.Value.Any(e => e != null && e.EventId == eventId))
                        {
                            shardKey = kvp.Key;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(shardKey))
                {
                    if (!_shardCache.TryGetValue(shardKey!, out var cachedEvents))
                    {
                        var shardPath = Path.Combine(_eventsFolder, $"{shardKey}.json");
                        var content = _writer.ReadAllText(shardPath);
                        cachedEvents = (!string.IsNullOrEmpty(content) ? ReadShardEvents(content!) : null) ?? new List<WorldEvent>();
                        _shardCache[shardKey!] = cachedEvents;
                    }

                    TouchShard(shardKey!);

                    int count = cachedEvents.RemoveAll(e => e != null && e.EventId == eventId);
                    if (count > 0)
                    {
                        _dirtyShards.Add(shardKey!);
                        touchedShards.Add(shardKey!);
                        removedCount += count;
                    }
                }

                if (targetIndex.Remove(eventId))
                {
                    _indexDirty = true;
                }
            }

            return (removedCount, touchedShards.Count);
        }
    }
}

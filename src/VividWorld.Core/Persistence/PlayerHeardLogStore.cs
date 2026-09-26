#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    public enum PlayerHeardLoadStatus
    {
        ReadFromFile,
        FileNotFound,
        FileUnreadable
    }

    public sealed class PlayerHeardLoadResult
    {
        public PlayerHeardLoadStatus Status { get; set; }
        public int Count { get; set; }
        public string How => Status switch
        {
            PlayerHeardLoadStatus.ReadFromFile => "read from player_heard.json",
            PlayerHeardLoadStatus.FileNotFound => "no file yet",
            PlayerHeardLoadStatus.FileUnreadable => "file unreadable, started empty",
            _ => "unknown"
        };
        public string? ExceptionMessage { get; set; }
        public string? SetAsidePath { get; set; }   // 讀不出來的檔案被改名到哪裡；null = 沒改名成功（下一次沖寫會蓋掉它）
    }

    public sealed class PlayerHeardBackfillResult
    {
        public int Considered { get; set; }
        public int Added { get; set; }
        public int SkippedSecret { get; set; }
        public int NoPlayerEntry { get; set; }
        public int NotLoadable { get; set; }
    }

    /// <summary>
    /// 玩家聽過的消息儲存庫（卡 MF3a §1.3）。
    /// </summary>
    public sealed class PlayerHeardLogStore
    {
        private readonly string _filePath;
        private readonly IFileWriter _writer;
        private PlayerHeardLog _log = new();
        private readonly Dictionary<string, PlayerHeardEntry> _lookup = new(StringComparer.Ordinal);

        public PlayerHeardLogStore(string filePath, IFileWriter writer)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public int Count => _log.Entries.Count;
        public bool IsDirty { get; private set; }

        public bool Contains(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return false;
            return _lookup.ContainsKey(eventId);
        }

        public PlayerHeardEntry? Find(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            return _lookup.TryGetValue(eventId, out var entry) ? entry : null;
        }

        public IEnumerable<PlayerHeardEntry> VisibleEntries(double currentDay)
        {
            foreach (var entry in _log.Entries)
            {
                if (entry != null && EventVisibility.IsVisibleOn(entry.Day, currentDay))
                {
                    yield return entry;
                }
            }
        }

        public int HiddenFutureCount(double currentDay)
        {
            int count = 0;
            foreach (var entry in _log.Entries)
            {
                if (entry != null && !EventVisibility.IsVisibleOn(entry.Day, currentDay))
                {
                    count++;
                }
            }
            return count;
        }

        public PlayerHeardLoadResult Load()
        {
            if (!_writer.Exists(_filePath))
            {
                _log = new PlayerHeardLog();
                _lookup.Clear();
                IsDirty = false;
                return new PlayerHeardLoadResult
                {
                    Status = PlayerHeardLoadStatus.FileNotFound,
                    Count = 0
                };
            }

            try
            {
                string? json = _writer.ReadAllText(_filePath);
                if (string.IsNullOrEmpty(json))
                {
                    return Unreadable("File content was empty");
                }

                var log = VividJson.Read<PlayerHeardLog>(json!);
                if (log == null)
                {
                    return Unreadable("Failed to deserialize PlayerHeardLog");
                }

                _log = log;
                if (_log.Entries == null) _log.Entries = new List<PlayerHeardEntry>();
                RebuildLookup();
                IsDirty = false;
                return new PlayerHeardLoadResult
                {
                    Status = PlayerHeardLoadStatus.ReadFromFile,
                    Count = _log.Entries.Count
                };
            }
            catch (Exception ex)
            {
                return Unreadable(ex.Message);
            }
        }

        /// <summary>讀不出來的檔案先改名放到旁邊，不讓下一次沖寫蓋掉它：
        /// 事件被清掉之後（MF3b），這份紀錄就是那些消息僅存的一份，從事件補齊補不回來。</summary>
        private PlayerHeardLoadResult Unreadable(string message)
        {
            _log = new PlayerHeardLog();
            _lookup.Clear();
            IsDirty = false;

            string aside = _filePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            bool moved = false;
            try { moved = _writer.Move(_filePath, aside); } catch { moved = false; }

            return new PlayerHeardLoadResult
            {
                Status = PlayerHeardLoadStatus.FileUnreadable,
                Count = 0,
                ExceptionMessage = message,
                SetAsidePath = moved ? aside : null
            };
        }

        private void RebuildLookup()
        {
            _lookup.Clear();
            if (_log.Entries != null)
            {
                foreach (var entry in _log.Entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.EventId))
                    {
                        _lookup[entry.EventId] = entry;
                    }
                }
            }
        }

        public bool Record(WorldEvent evt, KnownByEntry playerEntry, IReadOnlyList<Fact> knownFacts, double day)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem) return false;
            if (playerEntry == null) return false;

            if (!_lookup.TryGetValue(evt.EventId, out var existing))
            {
                // 新增
                var entry = new PlayerHeardEntry
                {
                    EventId = evt.EventId,
                    Type = evt.Type ?? string.Empty,
                    Day = evt.Day,
                    LinkedEventId = evt.LinkedEventId,
                    Participants = evt.Participants != null
                        ? new Dictionary<string, string>(evt.Participants, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal),
                    DramaWeight = evt.DramaWeight,
                    PlayerHop = playerEntry.Hop,
                    SourceHeroId = playerEntry.SourceHeroId,
                    LearnedDay = playerEntry.LearnedDay,
                    UpdatedDay = day,
                    Facts = (knownFacts ?? Array.Empty<Fact>()).Select(f => f.Clone()).ToList()
                };

                _log.Entries.Add(entry);
                _lookup[entry.EventId] = entry;
                IsDirty = true;
                return true;
            }

            // 已經有 ⇒ 碎片取聯集
            var existingFactIds = new HashSet<string>(existing.Facts.Select(f => f.Id), StringComparer.Ordinal);
            var newFacts = (knownFacts ?? Array.Empty<Fact>())
                .Where(f => !existingFactIds.Contains(f.Id))
                .Select(f => f.Clone())
                .ToList();

            if (newFacts.Count == 0)
            {
                // 沒變動時回 false 且不標髒
                return false;
            }

            var allFactsById = existing.Facts.Concat(newFacts).ToDictionary(f => f.Id, StringComparer.Ordinal);
            var merged = new List<Fact>();

            if (evt.Facts != null)
            {
                foreach (var f in evt.Facts)
                {
                    if (allFactsById.TryGetValue(f.Id, out var fact))
                    {
                        merged.Add(fact);
                        allFactsById.Remove(f.Id);
                    }
                }
            }

            // evt.Facts 裡沒有的既有碎片排在最後、保持原順序
            foreach (var f in existing.Facts)
            {
                if (allFactsById.ContainsKey(f.Id))
                {
                    merged.Add(f);
                    allFactsById.Remove(f.Id);
                }
            }

            // 防禦性：若 newFacts 裡有不在 evt.Facts 的也加進去
            foreach (var remaining in allFactsById.Values)
            {
                merged.Add(remaining);
            }

            existing.Facts = merged;
            existing.PlayerHop = Math.Min(existing.PlayerHop, playerEntry.Hop);
            existing.SourceHeroId = playerEntry.SourceHeroId;
            existing.UpdatedDay = day;
            IsDirty = true;
            return true;
        }

        public PlayerHeardBackfillResult Backfill(
            RumorIndex index,
            string playerHeroId,
            Func<string, WorldEvent?> load,
            Func<WorldEvent, KnownByEntry, IReadOnlyList<Fact>> factsFor,
            double day)
        {
            var result = new PlayerHeardBackfillResult();
            if (index?.Entries == null || string.IsNullOrEmpty(playerHeroId)) return result;

            foreach (var entry in index.Entries)
            {
                if (entry.KnownByHeroIds == null || !entry.KnownByHeroIds.Contains(playerHeroId, StringComparer.Ordinal))
                {
                    continue;
                }

                result.Considered++;

                if (Contains(entry.EventId))
                {
                    // 已經在紀錄裡的不重算（不讀它的分片）
                    continue;
                }

                var evt = load(entry.EventId);
                if (evt == null)
                {
                    result.NotLoadable++;
                    continue;
                }

                var playerEntry = evt.EntryFor(playerHeroId);
                if (playerEntry == null)
                {
                    result.NoPlayerEntry++;
                    continue;
                }

                if (!evt.IsVisibleToRumorSystem)
                {
                    result.SkippedSecret++;
                    continue;
                }

                var facts = factsFor(evt, playerEntry);
                bool recorded = Record(evt, playerEntry, facts, day);
                if (recorded)
                {
                    result.Added++;
                }
            }

            return result;
        }

        public bool Flush()
        {
            if (!IsDirty) return true;
            try
            {
                string json = VividJson.Write(_log);
                bool ok = AtomicFile.Write(_writer, _filePath, json);
                if (ok)
                {
                    IsDirty = false;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}

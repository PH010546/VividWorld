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

    public enum PlayerHeardSourceChange
    {
        None,
        Added,
        Updated
    }

    /// <summary>一次寫入玩家紀錄的結果：整筆有沒有變、來源清單那邊是新增還是更新了誰。</summary>
    public sealed class PlayerHeardRecordResult
    {
        public bool Changed { get; set; }
        public bool EntryAdded { get; set; }
        public PlayerHeardSourceChange SourceChange { get; set; }
        public PlayerHeardSource? Source { get; set; }
        public int SourceCount { get; set; }
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
        private readonly HashSet<string> _endingsByLinkedId = new(StringComparer.Ordinal);

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

        /// <summary>玩家是否已經聽過指回該事件的結局（例如被俘的被放、逃脫、獲救）。</summary>
        public bool HasHeardEndingFor(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return false;
            return _endingsByLinkedId.Contains(eventId);
        }

        /// <summary>該講述者是否親口告訴過玩家指定事件。</summary>
        public bool DidTellerTellPlayer(string eventId, string tellerHeroId)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(tellerHeroId)) return false;
            if (!_lookup.TryGetValue(eventId, out var entry)) return false;
            if (string.Equals(entry.SourceHeroId, tellerHeroId, StringComparison.Ordinal)) return true;
            if (entry.Sources != null)
            {
                foreach (var s in entry.Sources)
                {
                    if (string.Equals(s.HeroId, tellerHeroId, StringComparison.Ordinal)) return true;
                }
            }
            return false;
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
                MaterializeLegacySources();
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

        /// <summary>舊檔的紀錄沒有來源清單：用既有欄位合成一份放進去。
        /// 只改記憶體、不標髒——下次這筆有變動時才會連同清單一起寫出去，舊檔不會因為只是讀過就被改寫。</summary>
        private void MaterializeLegacySources()
        {
            foreach (var entry in _log.Entries)
            {
                if (entry == null) continue;
                EnsureSources(entry);
            }
        }

        private static void EnsureSources(PlayerHeardEntry entry)
        {
            entry.Sources ??= new List<PlayerHeardSource>();
            if (entry.Sources.Count == 0)
            {
                entry.Sources.Add(PlayerHeardSource.FromLegacy(entry));
            }
        }

        private void RebuildLookup()
        {
            _lookup.Clear();
            _endingsByLinkedId.Clear();
            if (_log.Entries != null)
            {
                foreach (var entry in _log.Entries)
                {
                    if (entry != null)
                    {
                        if (!string.IsNullOrEmpty(entry.EventId))
                        {
                            _lookup[entry.EventId] = entry;
                        }
                        if (!string.IsNullOrEmpty(entry.LinkedEventId))
                        {
                            _endingsByLinkedId.Add(entry.LinkedEventId!);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 補齊與舊呼叫端用：沒有講述細節，來源當成玩家那筆紀錄上的來源、碎片當成傳進來的全部、沒有感想。
        /// </summary>
        public bool Record(WorldEvent evt, KnownByEntry playerEntry, IReadOnlyList<Fact> knownFacts, double day)
        {
            if (playerEntry == null) return false;
            var telling = new PlayerHeardSource
            {
                HeroId = string.IsNullOrEmpty(playerEntry.SourceHeroId) ? null : playerEntry.SourceHeroId,
                Day = playerEntry.LearnedDay,
                Hop = playerEntry.Hop,
                FactIds = (knownFacts ?? Array.Empty<Fact>()).Select(f => f.Id).ToList()
            };
            return RecordTelling(evt, playerEntry, knownFacts ?? Array.Empty<Fact>(), day, telling).Changed;
        }

        /// <summary>
        /// 某個人剛講給玩家聽。新的人 ⇒ 來源清單加一份；同一個人再講 ⇒ 更新他那一份
        /// （碎片取聯集、手數取較小、有新感想就換成新的）。
        /// 最上層欄位照舊更新：碎片取所有來源的聯集、手數取最小。
        /// </summary>
        /// <param name="knownFacts">玩家目前知道的全部碎片（所有來源的聯集）。</param>
        /// <param name="telling">這一次講述：誰、第幾手、講了哪些碎片、感想；寫進去時會複製一份。</param>
        public PlayerHeardRecordResult RecordTelling(WorldEvent evt, KnownByEntry playerEntry, IReadOnlyList<Fact> knownFacts, double day, PlayerHeardSource telling)
        {
            var result = new PlayerHeardRecordResult();
            if (evt == null || !evt.IsVisibleToRumorSystem) return result;
            if (playerEntry == null || telling == null) return result;

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
                    DramaScale = evt.DramaScale,
                    PlayerHop = playerEntry.Hop,
                    SourceHeroId = playerEntry.SourceHeroId,
                    LearnedDay = playerEntry.LearnedDay,
                    UpdatedDay = day,
                    Facts = (knownFacts ?? Array.Empty<Fact>()).Select(f => f.Clone()).ToList(),
                    Sources = new List<PlayerHeardSource> { telling.Clone() }
                };

                _log.Entries.Add(entry);
                _lookup[entry.EventId] = entry;
                if (!string.IsNullOrEmpty(entry.LinkedEventId))
                {
                    _endingsByLinkedId.Add(entry.LinkedEventId!);
                }
                IsDirty = true;

                result.Changed = true;
                result.EntryAdded = true;
                result.SourceChange = PlayerHeardSourceChange.Added;
                result.Source = entry.Sources[0];
                result.SourceCount = 1;
                return result;
            }

            EnsureSources(existing);

            // 已經有 ⇒ 碎片取聯集
            var existingFactIds = new HashSet<string>(existing.Facts.Select(f => f.Id), StringComparer.Ordinal);
            var newFacts = (knownFacts ?? Array.Empty<Fact>())
                .Where(f => !existingFactIds.Contains(f.Id))
                .Select(f => f.Clone())
                .ToList();

            bool factsChanged = newFacts.Count > 0;
            if (factsChanged)
            {
                existing.Facts = MergeFacts(evt, existing.Facts, newFacts);
            }

            // 來源清單：同一個人更新他那一份，新的人加一份
            var match = existing.Sources.FirstOrDefault(s => PlayerHeardSource.SameTeller(s, telling));
            if (match == null)
            {
                match = telling.Clone();
                existing.Sources.Add(match);
                result.SourceChange = PlayerHeardSourceChange.Added;
            }
            else if (MergeInto(match, telling))
            {
                result.SourceChange = PlayerHeardSourceChange.Updated;
            }

            result.Source = match;
            result.SourceCount = existing.Sources.Count;

            if (!factsChanged && result.SourceChange == PlayerHeardSourceChange.None)
            {
                // 沒變動時回 false 且不標髒
                return result;
            }

            existing.PlayerHop = Math.Min(existing.PlayerHop, playerEntry.Hop);
            existing.SourceHeroId = playerEntry.SourceHeroId;
            existing.UpdatedDay = day;
            IsDirty = true;
            result.Changed = true;
            return result;
        }

        /// <summary>把新的一次講述併進同一個人既有的那一份。回傳有沒有真的改到東西。</summary>
        private static bool MergeInto(PlayerHeardSource target, PlayerHeardSource telling)
        {
            bool changed = false;

            target.FactIds ??= new List<string>();
            var have = new HashSet<string>(target.FactIds, StringComparer.Ordinal);
            foreach (var id in telling.FactIds ?? new List<string>())
            {
                if (have.Add(id))
                {
                    target.FactIds.Add(id);
                    changed = true;
                }
            }

            if (telling.Hop < target.Hop)
            {
                target.Hop = telling.Hop;
                changed = true;
            }

            // 這次有附感想就以這次為準（先講大概、之後講完整，完整那次才有感想）；這次沒有就保留原本的
            if (telling.HasFeeling &&
                (!string.Equals(target.FeelingLineKey, telling.FeelingLineKey, StringComparison.Ordinal) ||
                 !string.Equals(target.FeelingAddressKey, telling.FeelingAddressKey, StringComparison.Ordinal) ||
                 !string.Equals(target.FeelingFocusHeroId, telling.FeelingFocusHeroId, StringComparison.Ordinal)))
            {
                target.FeelingLineKey = telling.FeelingLineKey;
                target.FeelingAddressKey = telling.FeelingAddressKey;
                target.FeelingFocusHeroId = telling.FeelingFocusHeroId;
                changed = true;
            }

            // 同一個人再講：連同原句欄位一起換成新的（留最後一次講的原句）
            if (telling.HasSpokenLine)
            {
                target.PrefixTextId = telling.PrefixTextId;
                target.PrefixVars = telling.PrefixVars != null ? new Dictionary<string, string>(telling.PrefixVars) : new();
                target.SpeakerHeroId = telling.SpeakerHeroId;
                target.SpeakerRole = telling.SpeakerRole;
                target.SourceHeroId = telling.SourceHeroId;
                target.SourceRole = telling.SourceRole;
                target.Roles = telling.Roles != null ? new Dictionary<string, string>(telling.Roles, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
                target.SentenceKeyCandidates = telling.SentenceKeyCandidates != null ? new List<string>(telling.SentenceKeyCandidates) : new();
                target.SelfFeelingKeyCandidates = telling.SelfFeelingKeyCandidates != null ? new List<string>(telling.SelfFeelingKeyCandidates) : new();
                target.IsGist = telling.IsGist;
                target.HeldBack = telling.HeldBack;
                target.ClosingKey = telling.ClosingKey;
                changed = true;
            }

            if (changed && telling.Day > target.Day)
            {
                target.Day = telling.Day;
            }

            return changed;
        }

        private static List<Fact> MergeFacts(WorldEvent evt, List<Fact> existingFacts, List<Fact> newFacts)
        {
            var allFactsById = existingFacts.Concat(newFacts).ToDictionary(f => f.Id, StringComparer.Ordinal);
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
            foreach (var f in existingFacts)
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

            return merged;
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

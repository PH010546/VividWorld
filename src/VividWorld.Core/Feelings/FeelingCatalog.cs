using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Feelings
{
    /// <summary>感想句子的一句：字串鍵，與選用的個性標記（<c>Mercy+</c>、<c>Mercy-</c>、<c>Honor+</c> …）。</summary>
    public sealed class FeelingLine
    {
        public string Key { get; }

        /// <summary>個性名（Honor／Mercy／Valor／Calculating／Generosity）；沒標記為 null。</summary>
        public string? Trait { get; }

        /// <summary>個性標記的方向：正＝說話的人這項特質高於 0 時偏好；負＝低於 0 時偏好。</summary>
        public int TraitSign { get; }

        public FeelingLine(string key, string? traitTag)
        {
            Key = key;
            if (!string.IsNullOrEmpty(traitTag))
            {
                string tag = traitTag!.Trim();
                TraitSign = tag.EndsWith("-", StringComparison.Ordinal) ? -1 : 1;
                Trait = tag.TrimEnd('+', '-');
            }
        }

        public string TagText => Trait == null ? string.Empty : Trait + (TraitSign < 0 ? "-" : "+");

        /// <summary>1＝說話的人的特質符合標記；−1＝特質正好相反；0＝沒標記或特質剛好是 0。</summary>
        public int Score(TraitProfile? speaker)
        {
            if (Trait == null || speaker == null) return 0;
            int v = ValueOf(speaker, Trait);
            if (v == 0) return 0;
            return Math.Sign(v) == TraitSign ? 1 : -1;
        }

        public static int ValueOf(TraitProfile speaker, string trait)
        {
            switch (trait.ToLowerInvariant())
            {
                case "honor": return speaker.Honor;
                case "mercy": return speaker.Mercy;
                case "valor": return speaker.Valor;
                case "calculating": return speaker.Calculating;
                case "generosity": return speaker.Generosity;
                default: return 0;
            }
        }

        public static bool IsKnownTrait(string trait)
        {
            switch (trait.ToLowerInvariant())
            {
                case "honor":
                case "mercy":
                case "valor":
                case "calculating":
                case "generosity":
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>感想句子目錄：感想類別 × 心情 → 每格兩句。出貨檔是 <c>vividworld_feelings.json</c>。</summary>
    public sealed class FeelingCatalog
    {
        private readonly Dictionary<string, IReadOnlyList<FeelingLine>> _cells;

        public IReadOnlyList<string> Issues { get; }

        public static readonly FeelingCatalog Empty = new FeelingCatalog(new Dictionary<string, IReadOnlyList<FeelingLine>>(), new List<string>());

        private FeelingCatalog(Dictionary<string, IReadOnlyList<FeelingLine>> cells, IReadOnlyList<string> issues)
        {
            _cells = cells;
            Issues = issues;
        }

        public int CellCount => _cells.Count;

        public IEnumerable<FeelingLine> AllLines => _cells.Values.SelectMany(l => l);

        public IReadOnlyList<FeelingLine> Lines(string category, FeelingMood mood)
        {
            return _cells.TryGetValue(CellKey(category, mood), out var lines) ? lines : Array.Empty<FeelingLine>();
        }

        private static string CellKey(string category, FeelingMood mood) => category + "/" + FeelingGrid.MoodId(mood);

        public static FeelingCatalog Parse(string json)
        {
            var issues = new List<string>();
            var cells = new Dictionary<string, IReadOnlyList<FeelingLine>>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json))
            {
                issues.Add("feeling catalog is empty");
                return new FeelingCatalog(cells, issues);
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                issues.Add("feeling catalog is not valid JSON: " + ex.Message);
                return new FeelingCatalog(cells, issues);
            }

            var cats = root["categories"] as JObject;
            if (cats == null)
            {
                issues.Add("feeling catalog has no 'categories' object");
                return new FeelingCatalog(cells, issues);
            }

            foreach (var catProp in cats.Properties())
            {
                if (!FeelingCategories.IsKnown(catProp.Name))
                {
                    issues.Add($"unknown feeling category '{catProp.Name}'");
                    continue;
                }
                var moods = catProp.Value as JObject;
                if (moods == null) { issues.Add($"category '{catProp.Name}' is not an object"); continue; }

                foreach (FeelingMood mood in Enum.GetValues(typeof(FeelingMood)))
                {
                    var arr = moods[FeelingGrid.MoodId(mood)] as JArray;
                    if (arr == null) continue;
                    var lines = new List<FeelingLine>();
                    foreach (var item in arr)
                    {
                        string? key = item?["key"]?.Value<string>();
                        string? trait = item?["trait"]?.Value<string>();
                        if (string.IsNullOrEmpty(key))
                        {
                            issues.Add($"{catProp.Name}/{FeelingGrid.MoodId(mood)}: a line has no key");
                            continue;
                        }
                        var line = new FeelingLine(key!, trait);
                        if (line.Trait != null && !FeelingLine.IsKnownTrait(line.Trait))
                        {
                            issues.Add($"{key}: unknown trait '{trait}'");
                            line = new FeelingLine(key!, null);
                        }
                        lines.Add(line);
                    }
                    cells[CellKey(catProp.Name, mood)] = lines;
                }
            }

            return new FeelingCatalog(cells, issues);
        }
    }
}

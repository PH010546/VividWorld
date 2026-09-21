using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class LocalizationTableTests
    {
        /// <summary>
        /// **每一個**語言資料夾都要有和英文完全相同的鍵集，不是只有 CNt。
        /// 缺鍵不會壞遊戲，只會讓那一句悄悄退回英文後備（`FallbackTextRenderer.LocalizedTemplate`），
        /// 而且分隔符、句尾、someone／somewhere、重述前綴是**一個鍵一個鍵各自退**的
        /// ⇒ 只翻一半會變成「中文句子, 用英文逗號.」。這條測試就是不讓那種半成品進到出貨路徑。
        /// </summary>
        [Fact]
        public void Localization_EveryLanguageTable_ExposesTheSameKeySetAsEnglish()
        {
            string repoRoot = FindRepoRoot();
            string enPath = EnglishTablePath(repoRoot);
            Assert.True(File.Exists(enPath), $"English strings file not found at: {enPath}");

            var enKeys = LoadStringIds(enPath);
            Assert.NotEmpty(enKeys);

            var others = TranslatedTablePaths(repoRoot);
            Assert.True(others.Count > 0, "Languages/ 底下一個翻譯資料夾都沒有（CNt 不見了？）");

            var problems = new List<string>();
            foreach (string path in others)
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(path)) ?? path;
                var keys = LoadStringIds(path);

                var missing = enKeys.Except(keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var extra = keys.Except(enKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

                if (missing.Count > 0) problems.Add($"{lang} 缺少：{string.Join(", ", missing)}");
                if (extra.Count > 0) problems.Add($"{lang} 多出英文表沒有的鍵：{string.Join(", ", extra)}");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>事件目錄裡每一條碎片的 <c>textId</c> 都要在兩張字串表裡找得到。
        /// 少一條不會壞遊戲，只會讓那句話在畫面上變成原始 id，而且沒有任何東西會出聲。</summary>
        [Fact]
        public void Localization_EveryCatalogFactTextId_ExistsInEveryTable()
        {
            string repoRoot = FindRepoRoot();

            var textIds = LoadCatalogTexts(repoRoot).Keys.ToList();
            Assert.NotEmpty(textIds);

            var problems = new List<string>();
            foreach (string path in new[] { EnglishTablePath(repoRoot) }.Concat(TranslatedTablePaths(repoRoot)))
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(path)) ?? "English";
                var keys = LoadStringIds(path);
                var missing = textIds.Where(id => !keys.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (missing.Count > 0) problems.Add($"{lang} 缺少：{string.Join(", ", missing)}");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>出貨事件目錄裡的每一個事件型別，都要在每一張字串表裡有一條標題
        /// （`VividWorld_EventType_&lt;type&gt;`，慣例見 <c>ChronicleProvider</c>）。
        /// 少一條不會壞遊戲——紀事視窗那一格會直接印出原始型別字串（例如 `seat_dispute_yielded`），
        /// 玩家看到的是開發者代號，而且沒有任何東西會出聲。</summary>
        [Fact]
        public void Localization_EveryShippingEventType_HasAChronicleHeadline()
        {
            string repoRoot = FindRepoRoot();

            var types = LoadCatalogEventTypes(repoRoot);
            Assert.NotEmpty(types);

            var problems = new List<string>();
            foreach (string path in new[] { EnglishTablePath(repoRoot) }.Concat(TranslatedTablePaths(repoRoot)))
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(path)) ?? "English";
                var keys = LoadStringIds(path);
                var missing = types.Select(t => "VividWorld_EventType_" + t)
                                   .Where(id => !keys.Contains(id))
                                   .OrderBy(id => id, StringComparer.Ordinal)
                                   .ToList();
                if (missing.Count > 0) problems.Add($"{lang} 缺少：{string.Join(", ", missing)}");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>兩份出貨事件目錄裡的 <c>type</c>。測試用的 `vividworld_sample_events.json`
        /// 不算——那些型別玩家永遠看不到（`debug.fakeProducerEnabled` 預設 false）。</summary>
        private static List<string> LoadCatalogEventTypes(string repoRoot)
        {
            var result = new List<string>();

            foreach (string fileName in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                string path = Path.Combine(repoRoot, "module", "ModuleData", fileName);
                Assert.True(File.Exists(path), $"Catalog not found: {path}");

                var templates = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var template in templates)
                {
                    string? type = (string?)template["type"];
                    if (!string.IsNullOrWhiteSpace(type) && !result.Contains(type!)) result.Add(type!);
                }
            }

            return result;
        }

        /// <summary>英文表在 `Languages/` 底下，翻譯各自一個子資料夾（`Languages/CNt/` …）。</summary>
        internal static string EnglishTablePath(string repoRoot)
        {
            return Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
        }

        /// <summary>`Languages/` 底下每一個有 `std_module_strings_xml.xml` 的子資料夾，數量不寫死。</summary>
        internal static List<string> TranslatedTablePaths(string repoRoot)
        {
            string languagesDir = Path.Combine(repoRoot, "module", "ModuleData", "Languages");
            Assert.True(Directory.Exists(languagesDir), $"Languages folder not found: {languagesDir}");

            return Directory.GetDirectories(languagesDir)
                .Select(dir => Path.Combine(dir, "std_module_strings_xml.xml"))
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>JSON 裡的 <c>text</c> 是字串表查不到時的退路，兩邊必須逐字相同——
        /// 不然玩家看到的句子會隨「字串表載入成功與否」而變。</summary>
        [Fact]
        public void Localization_EnglishTable_MatchesCatalogFallbackTextVerbatim()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            var enTexts = LoadStringTable(enPath);

            var mismatches = new List<string>();
            foreach (var kvp in LoadCatalogTexts(repoRoot))
            {
                if (!enTexts.TryGetValue(kvp.Key, out var tableText)) continue;   // 缺漏由上一條測試負責
                if (!string.Equals(tableText, kvp.Value, StringComparison.Ordinal))
                {
                    mismatches.Add($"{kvp.Key}: json=\"{kvp.Value}\" table=\"{tableText}\"");
                }
            }

            Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
        }

        /// <summary>兩份事件目錄裡的 textId → JSON 的英文退路文字。</summary>
        private static Dictionary<string, string> LoadCatalogTexts(string repoRoot)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string fileName in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                string path = Path.Combine(repoRoot, "module", "ModuleData", fileName);
                Assert.True(File.Exists(path), $"Catalog not found: {path}");

                var templates = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var template in templates)
                {
                    foreach (var fact in template["facts"] ?? new Newtonsoft.Json.Linq.JArray())
                    {
                        string? textId = (string?)fact["textId"];
                        string? text = (string?)fact["text"];
                        if (string.IsNullOrWhiteSpace(textId)) continue;
                        result[textId!] = text ?? string.Empty;
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, string> LoadStringTable(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var elem in doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
            {
                var id = elem.Attribute("id")?.Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[id!] = elem.Attribute("text")?.Value ?? string.Empty;
                }
            }
            return result;
        }

        private static HashSet<string> LoadStringIds(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var stringsElement = doc.Root?.Element("strings");
            if (stringsElement == null)
            {
                Assert.Fail($"Missing <strings> root child element in {filePath}");
            }

            var ids = new HashSet<string>();
            foreach (var elem in stringsElement!.Elements("string"))
            {
                var idAttr = elem.Attribute("id");
                if (idAttr == null || string.IsNullOrWhiteSpace(idAttr.Value))
                {
                    Assert.Fail($"Found <string> without valid id in {filePath}");
                }

                bool added = ids.Add(idAttr!.Value);
                Assert.True(added, $"Duplicate string id '{idAttr.Value}' found in {filePath}");
            }

            return ids;
        }

        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            Assert.Fail($"Could not locate repo root (containing VividWorld.sln) starting from {AppContext.BaseDirectory}");
            return string.Empty;
        }
    }
}

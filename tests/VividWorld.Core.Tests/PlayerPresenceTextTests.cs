using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PlayerPresenceTextTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException("repo root not found");
        }

        [Fact]
        public void ApplyGenderSelectTokens_WithRoleDictionary_SelectsExpectedGenderSegments()
        {
            // 測試 1: ApplyGenderSelectTokens 在沒有傳聞（r 為 null）、只給角色字典時：
            // 男選第一段、女選第二段、查不到性別有第三段選第三段；角色名大小寫不同也認得
            var isFemale = new Func<string, bool?>(id => id switch
            {
                "hero_m" => false,
                "hero_f" => true,
                "hero_unk" => null,
                _ => null
            });

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FAVORED"] = "hero:hero_m",
                ["STUDENT"] = "hero:hero_f",
                ["AGGRIEVED"] = "hero:hero_unk",
                ["claimant"] = "hero:hero_f" // 小寫鍵測試
            };

            // 男選第一段
            string resM = RumorTextAssembler.ApplyGenderSelectTokens(
                "敬了{FAVORED:他|她}一杯",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("敬了他一杯", resM);

            // 女選第二段
            string resF = RumorTextAssembler.ApplyGenderSelectTokens(
                "講給{STUDENT:他|她}聽",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("講給她聽", resF);

            // 查不到性別有第三段選第三段
            string resUnk3 = RumorTextAssembler.ApplyGenderSelectTokens(
                "raised a cup to {AGGRIEVED:him|her|them} as well",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("raised a cup to them as well", resUnk3);

            // 查不到性別沒有第三段選第一段
            string resUnk2 = RumorTextAssembler.ApplyGenderSelectTokens(
                "向{AGGRIEVED:他|她}賠了不是",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("向他賠了不是", resUnk2);

            // 角色名大小寫不同也認得：樣板小寫但字典大寫，或樣板大寫字典小寫
            string resCase1 = RumorTextAssembler.ApplyGenderSelectTokens(
                "敬了{favored:他|她}一杯",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("敬了他一杯", resCase1);

            string resCase2 = RumorTextAssembler.ApplyGenderSelectTokens(
                "是{CLAIMANT:他|她}的功勞",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("是她的功勞", resCase2);

            string resCase3 = RumorTextAssembler.ApplyGenderSelectTokens(
                "first on {claimant:his|her|their} feet",
                null, null, null, isFemale, vars, null, "test_key");
            Assert.Equal("first on her feet", resCase3);
        }

        [Fact]
        public void PlayerPresenceTextSource_AllFallbackDictionaries_MatchEnglishStringTable()
        {
            // 測試 2: 讀 PlayerPresenceText.cs 原始碼（照 MadeUpTalkTests 讀 Module 原始檔的做法），
            // 三個後備字典的每一項都等於英文字串表同一個鍵的字
            string repoRoot = FindRepoRoot();
            string csPath = Path.Combine(repoRoot, "src", "VividWorld.Module", "Campaign", "PlayerPresenceText.cs");
            Assert.True(File.Exists(csPath), $"File not found: {csPath}");
            string csText = File.ReadAllText(csPath);

            string enXmlPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            Assert.True(File.Exists(enXmlPath), $"File not found: {enXmlPath}");
            var doc = XDocument.Load(enXmlPath);
            var enStrings = (doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                .ToDictionary(
                    e => e.Attribute("id")?.Value ?? "",
                    e => e.Attribute("text")?.Value ?? "",
                    StringComparer.Ordinal);

            // 從原始碼解析字典內容：["key"] = "value"
            var dictEntryRegex = new Regex(@"\[""([^""]+)""\]\s*=\s*""([^""]*)""", RegexOptions.Compiled);

            // 抽取 SceneFallbacks
            var sceneDict = ExtractDictionary(csText, "SceneFallbacks", dictEntryRegex);
            Assert.NotEmpty(sceneDict);
            foreach (var kv in sceneDict)
            {
                string fullKey = "VividWorld_Presence_Scene_" + kv.Key;
                Assert.True(enStrings.ContainsKey(fullKey), $"English string table missing scene key: {fullKey}");
                string xmlVal = enStrings[fullKey].Replace("&apos;", "'");
                Assert.Equal(xmlVal, kv.Value);
            }

            // 抽取 OptionFallbacks
            var optionDict = ExtractDictionary(csText, "OptionFallbacks", dictEntryRegex);
            Assert.NotEmpty(optionDict);
            foreach (var kv in optionDict)
            {
                string fullKey = "VividWorld_Presence_Option_" + kv.Key;
                Assert.True(enStrings.ContainsKey(fullKey), $"English string table missing option key: {fullKey}");
                string xmlVal = enStrings[fullKey].Replace("&apos;", "'");
                Assert.Equal(xmlVal, kv.Value);
            }

            // 抽取 OutcomeFallbacks
            var outcomeDict = ExtractDictionary(csText, "OutcomeFallbacks", dictEntryRegex);
            Assert.NotEmpty(outcomeDict);
            foreach (var kv in outcomeDict)
            {
                string fullKey = "VividWorld_Presence_Outcome_" + kv.Key;
                Assert.True(enStrings.ContainsKey(fullKey), $"English string table missing outcome key: {fullKey}");
                string xmlVal = enStrings[fullKey].Replace("&apos;", "'");
                Assert.Equal(xmlVal, kv.Value);
            }
        }

        private static Dictionary<string, string> ExtractDictionary(string code, string dictName, Regex entryRegex)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            int idx = code.IndexOf(dictName, StringComparison.Ordinal);
            if (idx < 0) return dict;

            int braceStart = code.IndexOf('{', idx);
            int braceEnd = code.IndexOf("};", braceStart, StringComparison.Ordinal);
            if (braceStart < 0 || braceEnd < 0) return dict;

            string block = code.Substring(braceStart, braceEnd - braceStart + 2);
            var matches = entryRegex.Matches(block);
            foreach (Match m in matches)
            {
                dict[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return dict;
        }

        [Fact]
        public void StringTables_PresenceStrings_GenderSelectTokens_RolesMustAppearAsPlaceholdersAndHaveAtLeastTwoSegments()
        {
            // 測試 3: 兩份字串表裡 VividWorld_Presence_ 開頭的字串：
            // 每個選字記號的角色名，都要以 {角色名} 的形式出現在同一份字串表裡某一句 VividWorld_Presence_ 字串（防打錯字）；
            // 記號至少兩段
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var tokenRegex = new Regex(@"\{([^{}:]+):([^{}]*)\}", RegexOptions.Compiled);
            var rolePlaceholderRegex = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

            foreach (string path in new[] { enPath, cntPath })
            {
                Assert.True(File.Exists(path), $"String file not found: {path}");
                var doc = XDocument.Load(path);
                var presenceStrings = (doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                    .Where(e => (e.Attribute("id")?.Value ?? "").StartsWith("VividWorld_Presence_", StringComparison.Ordinal))
                    .Select(e => new { Id = e.Attribute("id")?.Value ?? "", Text = e.Attribute("text")?.Value ?? "" })
                    .ToList();

                Assert.NotEmpty(presenceStrings);

                // 收集這份字串表所有出現在 VividWorld_Presence_ 中的單純角色佔位符 {ROLE}
                var knownRolesInPresence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in presenceStrings)
                {
                    foreach (Match m in rolePlaceholderRegex.Matches(item.Text))
                    {
                        knownRolesInPresence.Add(m.Groups[1].Value);
                    }
                }

                int tokensFound = 0;
                foreach (var item in presenceStrings)
                {
                    var tokenMatches = tokenRegex.Matches(item.Text);
                    foreach (Match m in tokenMatches)
                    {
                        tokensFound++;
                        string roleName = m.Groups[1].Value.Trim();
                        string rawSegments = m.Groups[2].Value;
                        string[] segments = rawSegments.Split('|');

                        // 記號至少兩段
                        Assert.True(segments.Length >= 2,
                            $"Presence string '{item.Id}' token '{m.Value}' must have at least 2 segments separated by '|'");

                        // 每個選字記號的角色名，都要以 {角色名} 的形式出現在同一份字串表裡某一句 VividWorld_Presence_ 字串
                        Assert.True(knownRolesInPresence.Contains(roleName),
                            $"Role '{roleName}' in token '{m.Value}' of string '{item.Id}' must appear as a '{{{roleName}}}' placeholder in some VividWorld_Presence_ string in {Path.GetFileName(path)}");
                    }
                }

                // 繁中不依性別分「她」，一律寫「他」，所以只有英文表一定要有選字記號
                if (path == enPath)
                {
                    Assert.True(tokensFound > 0, $"Expected gender select tokens in {Path.GetFileName(path)}, but found none");
                }
            }
        }
    }
}

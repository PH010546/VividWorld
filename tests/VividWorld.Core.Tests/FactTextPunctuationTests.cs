using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 碎片文字一律不帶句尾標點（規格 §9.3／§9.5.4）：句子的分隔符與句號是
    /// <see cref="VividWorld.Core.Presentation.RumorTextAssembler"/> 依當前語言補上的。
    /// 碎片自己帶了句點，玩家看到的就會是「…主導者鎖定了玩家.，雙方當眾起了爭執.。」。
    /// 三個來源一起掃：兩張字串表、兩份事件目錄 JSON、以及 dev 測試事件寫死在程式裡的字面字串。
    /// </summary>
    public class FactTextPunctuationTests
    {
        private const string TerminalPunctuation = ".。．!！?？,，;；、";

        [Fact]
        public void FactStrings_InEveryStringTable_DoNotEndWithSentencePunctuation()
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();

            // 語言數量不寫死：`Languages/` 底下之後多一個資料夾，這條就自動掃得到。
            var tables = new[] { LocalizationTableTests.EnglishTablePath(repoRoot) }
                .Concat(LocalizationTableTests.TranslatedTablePaths(repoRoot));

            foreach (string path in tables)
            {
                string relative = path.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                Assert.True(File.Exists(path), $"String table not found: {path}");

                var doc = XDocument.Load(path);
                foreach (var elem in doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                {
                    string id = elem.Attribute("id")?.Value ?? string.Empty;
                    string text = elem.Attribute("text")?.Value ?? string.Empty;

                    // 只管碎片文字。分隔符與句號本身就是標點，前綴語（VividWorld_RetellPrefix 之類）也不算。
                    if (!id.StartsWith("VividWorld_Fact_", StringComparison.Ordinal)) continue;

                    if (EndsWithTerminalPunctuation(text))
                    {
                        offenders.Add($"{relative} :: {id} = \"{text}\"");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "碎片文字不該自帶句尾標點：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void CatalogFactTexts_DoNotEndWithSentencePunctuation()
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();

            foreach (string fileName in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                string path = Path.Combine(repoRoot, "module", "ModuleData", fileName);
                Assert.True(File.Exists(path), $"Catalog not found: {path}");

                var templates = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var template in templates)
                {
                    foreach (var fact in template["facts"] ?? new Newtonsoft.Json.Linq.JArray())
                    {
                        string text = (string?)fact["text"] ?? string.Empty;
                        if (EndsWithTerminalPunctuation(text))
                        {
                            offenders.Add($"{fileName} :: {(string?)fact["textId"]} = \"{text}\"");
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "碎片文字不該自帶句尾標點：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// dev 測試事件的碎片寫死在 <c>VividWorld.Module</c> 裡，那個專案要遊戲組件才編得起來、
        /// 沒有離線測試 ⇒ 直接讀它的原始碼把 <c>Text = "..."</c> 撈出來檢查。
        /// 2026-09-20 實機就是這裡帶了句點，才印出 "main_hero.，...parties.。"。
        /// </summary>
        [Fact]
        public void DevEventFactory_FactTexts_DoNotEndWithSentencePunctuation()
        {
            string repoRoot = FindRepoRoot();
            string path = Path.Combine(repoRoot, "src", "VividWorld.Module", "Debug", "DevEventFactory.cs");
            Assert.True(File.Exists(path), $"DevEventFactory source not found: {path}");

            string source = File.ReadAllText(path);
            var matches = Regex.Matches(source, @"Text\s*=\s*\$?""(?<text>[^""]*)""");
            Assert.True(matches.Count >= 6, $"只在 {path} 找到 {matches.Count} 句碎片文字，預期至少 6 句（檔案改過形狀？）");

            var offenders = matches.Cast<Match>()
                .Select(m => m.Groups["text"].Value)
                .Where(EndsWithTerminalPunctuation)
                .ToList();

            Assert.True(offenders.Count == 0,
                "dev 測試事件的碎片文字不該自帶句尾標點：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        private static bool EndsWithTerminalPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return TerminalPunctuation.IndexOf(text[text.Length - 1]) >= 0;
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

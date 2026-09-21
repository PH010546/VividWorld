using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class RumorEncyclopediaLinksTests
    {
        private static string MockResolveVar(string val, bool useLinks)
        {
            if (string.IsNullOrEmpty(val)) return string.Empty;
            int colon = val.IndexOf(':');
            if (colon < 0) return val;

            string prefix = val.Substring(0, colon);
            string payload = val.Substring(colon + 1);

            switch (prefix.ToLowerInvariant())
            {
                case "hero":
                    if (payload == "missing_hero")
                    {
                        // 查不到英雄物件：退回純名字／someone，不產生半截錨點
                        return "someone";
                    }
                    if (payload == "lord_5_11")
                    {
                        return useLinks
                            ? "<a style=\"Link.Hero\" href=\"event:lord_5_11\"><b>Corein</b></a>"
                            : "Corein";
                    }
                    return useLinks
                        ? $"<a style=\"Link.Hero\" href=\"event:{payload}\"><b>{payload}</b></a>"
                        : payload;

                case "settlement":
                    if (payload == "missing_town")
                    {
                        // 查不到聚落物件：退回純名字／somewhere
                        return "somewhere";
                    }
                    if (payload == "town_B1")
                    {
                        return useLinks
                            ? "<a style=\"Link.Settlement\" href=\"event:town_B1\"><b>Pravend</b></a>"
                            : "Pravend";
                    }
                    return useLinks
                        ? $"<a style=\"Link.Settlement\" href=\"event:{payload}\"><b>{payload}</b></a>"
                        : payload;

                default:
                    return payload;
            }
        }

        /// <summary>
        /// 6. hero: 與 settlement: 在開啟時產出帶錨點的字串，且錨點內含正確的 StringId（M6c 卡 §4 第 6 項）
        /// </summary>
        [Fact]
        public void RumorText_WithEncyclopediaLinksEnabled_ProducesAnchorWithCorrectStringId()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_Duel_Who",
                        Fallback = "{DUELIST} engaged in a bloody duel with {RIVAL}",
                        Vars = new Dictionary<string, string>
                        {
                            ["DUELIST"] = "hero:lord_5_11",
                            ["RIVAL"] = "hero:lord_5_12"
                        }
                    },
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_Duel_Where",
                        Fallback = "near {SETTLEMENT}",
                        Vars = new Dictionary<string, string>
                        {
                            ["SETTLEMENT"] = "settlement:town_B1"
                        }
                    }
                }
            };

            var result = RumorTextAssembler.Assemble(rumor, cfg, MockResolveVar);

            // DisplayText 應包含正確的英雄與聚落超連結，且錨點內含正確的 StringId
            Assert.Contains("<a style=\"Link.Hero\" href=\"event:lord_5_11\"><b>Corein</b></a>", result.DisplayText);
            Assert.Contains("<a style=\"Link.Hero\" href=\"event:lord_5_12\"><b>lord_5_12</b></a>", result.DisplayText);
            Assert.Contains("<a style=\"Link.Settlement\" href=\"event:town_B1\"><b>Pravend</b></a>", result.DisplayText);
            Assert.Contains("href=\"event:lord_5_11\"", result.DisplayText);
            Assert.Contains("href=\"event:town_B1\"", result.DisplayText);
        }

        /// <summary>
        /// 7. 同一則傳聞的純文字版本不含任何 '<'／'>'，且與關閉設定鍵時的輸出一致（M6c 卡 §4 第 7 項）
        /// </summary>
        [Fact]
        public void RumorText_PlainTextVersion_ContainsNoAngleBrackets_AndMatchesDisabledState()
        {
            var cfgEnabled = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var cfgDisabled = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_Duel_Who",
                        Fallback = "{DUELIST} fought near {SETTLEMENT}",
                        Vars = new Dictionary<string, string>
                        {
                            ["DUELIST"] = "hero:lord_5_11",
                            ["SETTLEMENT"] = "settlement:town_B1"
                        }
                    }
                }
            };

            var resultEnabled = RumorTextAssembler.Assemble(rumor, cfgEnabled, MockResolveVar);
            var resultDisabled = RumorTextAssembler.Assemble(rumor, cfgDisabled, MockResolveVar);

            // 1. 純文字版本不含任何 '<' 或 '>'
            Assert.DoesNotContain("<", resultEnabled.PlainText);
            Assert.DoesNotContain(">", resultEnabled.PlainText);

            // 2. 純文字版本與關閉設定鍵時的 DisplayText 完全一致
            Assert.Equal(resultDisabled.DisplayText, resultEnabled.PlainText);
            Assert.Equal("Corein fought near Pravend.", resultEnabled.PlainText);
        }

        /// <summary>
        /// 8. 查不到英雄物件時退回純名字，且輸出不含半截錨點（M6c 卡 §4 第 8 項）
        /// </summary>
        [Fact]
        public void RumorText_WhenHeroOrSettlementNotFound_ReturnsPlainName_WithNoBrokenAnchor()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_Duel_Who",
                        Fallback = "{DUELIST} was seen near {SETTLEMENT}",
                        Vars = new Dictionary<string, string>
                        {
                            ["DUELIST"] = "hero:missing_hero",
                            ["SETTLEMENT"] = "settlement:missing_town"
                        }
                    }
                }
            };

            var result = RumorTextAssembler.Assemble(rumor, cfg, MockResolveVar);

            // 查不到物件時應退回純名字（someone / somewhere），絕不產生半截錨點
            Assert.Contains("someone was seen near somewhere.", result.DisplayText);
            Assert.DoesNotContain("<a", result.DisplayText);
            Assert.DoesNotContain("href=", result.DisplayText);
            Assert.DoesNotContain("<", result.DisplayText);
            Assert.DoesNotContain(">", result.DisplayText);
        }

        /// <summary>
        /// 設定合併驗證：既有 config.json 在缺少 presentation.encyclopediaLinksEnabled 時自動補上且不覆蓋既有值。
        /// </summary>
        [Fact]
        public void Merge_AddsPresentationEncyclopediaLinksEnabled_WithoutOverwritingExisting()
        {
            var existingJson = @"{
                ""configVersion"": 1,
                ""presentation"": {
                    ""chronicleMaxEntries"": 80,
                    ""chronicleHotkey"": ""K""
                }
            }";

            var canonical = new VividWorldConfig();
            var canonicalJObj = JObject.Parse(VividJson.Write(canonical));
            var existingJObj = JObject.Parse(existingJson);

            var mergeResult = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            // 既有值維持不變
            Assert.Equal(80, (int)mergeResult.Merged["presentation"]!["chronicleMaxEntries"]!);
            Assert.Equal("K", (string)mergeResult.Merged["presentation"]!["chronicleHotkey"]!);

            // 缺少的新鍵被補上
            Assert.True((bool)mergeResult.Merged["presentation"]!["encyclopediaLinksEnabled"]!);
            Assert.Contains("presentation.encyclopediaLinksEnabled", mergeResult.AddedPaths);
        }

        /// <summary>
        /// 代換不掉的佔位符必須留下一行 WARN，而且**只留一行**。
        /// M6c 改成「顯示／純文字」雙重渲染之後，這條告警一度整段消失
        /// （實機驗收第 11 項「全份 log 搜 placeholder(s) 應該一行都沒有」會因此變成恆真的空檢查），
        /// 而天真的接法又會讓同一個失敗印兩次。帳本 L-23、規格 §9.5.2。
        /// </summary>
        [Fact]
        public void UnresolvedPlaceholder_EmitsExactlyOneWarning_WithVarsAndRawText()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,   // 開著＝兩趟渲染會走不同路徑
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_SharedMeal_Who",
                        Fallback = "{HOST} shared a simple meal with {GUEST}",
                        Vars = new Dictionary<string, string>
                        {
                            ["HOST"] = "hero:lord_5_11"
                            // GUEST 故意缺席：這正是 L-23 那則存過檔的傳聞的形狀
                        }
                    }
                }
            };

            var warnings = new List<string>();
            var result = RumorTextAssembler.Assemble(
                rumor, cfg, MockResolveVar, null, null, warnings.Add);

            // 只有一行，不因雙重渲染變成兩行
            Assert.Single(warnings);

            string w = warnings[0];
            Assert.Contains("placeholder(s)", w);
            Assert.Contains("GUEST", w);
            Assert.Contains("VividWorld_Fact_SharedMeal_Who", w);
            Assert.Contains("vars present: HOST", w);
            Assert.Contains("{HOST} shared a simple meal with {GUEST}", w);

            // 半句話絕不送出去：缺的那個要被渲染成 someone（規格 §9.5.2）
            Assert.Contains("someone", result.PlainText);
            Assert.DoesNotContain("{GUEST}", result.PlainText);
            Assert.DoesNotContain("{GUEST}", result.DisplayText);
        }

        /// <summary>沒有代換失敗時，一行 WARN 都不該有。</summary>
        [Fact]
        public void AllPlaceholdersResolved_EmitsNoWarning()
        {
            var cfg = new PresentationConfig { EncyclopediaLinksEnabled = true, FactSeparator = ", ", SentenceEnd = "." };
            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = "VividWorld_Fact_Duel_Where",
                        Fallback = "near {SETTLEMENT}",
                        Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:town_B1" }
                    }
                }
            };

            var warnings = new List<string>();
            RumorTextAssembler.Assemble(rumor, cfg, MockResolveVar, null, null, warnings.Add);

            Assert.Empty(warnings);
        }
    }
}

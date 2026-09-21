using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class McmExposedKeysTests
    {
        [Fact]
        public void McmExposedKeys_CountAndUniqueness()
        {
            Assert.Equal(24, McmExposedKeys.All.Count);
            var paths = McmExposedKeys.All.Select(k => k.Path).ToList();
            Assert.Equal(24, paths.Distinct().Count());
        }

        [Fact]
        public void VividWorldCore_HasNoForbiddenAssemblyReferences()
        {
            var refs = typeof(McmExposedKeys).Assembly.GetReferencedAssemblies();
            var refNames = refs.Select(r => r.Name ?? "").ToList();

            foreach (var r in refNames)
            {
                Assert.False(r.StartsWith("TaleWorlds", StringComparison.OrdinalIgnoreCase), $"Forbidden TaleWorlds ref: {r}");
                Assert.False(r.StartsWith("MCM", StringComparison.OrdinalIgnoreCase), $"Forbidden MCM ref: {r}");
                Assert.False(r.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase), $"Forbidden Harmony ref: {r}");
            }
        }

        [Fact]
        public void ModuleReleaseBin_DoesNotContainForbiddenDlls()
        {
            string repoRoot = FindRepoRoot();
            string releaseDir = Path.Combine(repoRoot, "src", "VividWorld.Module", "bin", "Release");
            if (Directory.Exists(releaseDir))
            {
                var files = Directory.GetFiles(releaseDir, "*.dll").Select(p => Path.GetFileName(p) ?? "").ToList();
                Assert.DoesNotContain(files, f => string.Equals(f, "MCMv5.dll", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(files, f => f.StartsWith("TaleWorlds.", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void McmExposedKeys_Read_ReturnsValueMatchingKind()
        {
            var cfg = new VividWorldConfig();
            foreach (var key in McmExposedKeys.All)
            {
                var val = McmExposedKeys.Read(cfg, key.Path);
                Assert.NotNull(val);
                switch (key.Kind)
                {
                    case McmKeyKind.Bool:
                        Assert.IsType<bool>(val);
                        break;
                    case McmKeyKind.Int:
                        Assert.IsType<int>(val);
                        break;
                    case McmKeyKind.Double:
                        Assert.IsType<double>(val);
                        break;
                    case McmKeyKind.Text:
                    case McmKeyKind.Dropdown:
                        Assert.IsType<string>(val);
                        break;
                    default:
                        Assert.Fail($"Unknown McmKeyKind: {key.Kind}");
                        break;
                }
            }
        }

        public static IEnumerable<object[]> AllKeyPaths()
        {
            return McmExposedKeys.All.Select(k => new object[] { k.Path });
        }

        [Theory]
        [MemberData(nameof(AllKeyPaths))]
        public void McmExposedKeys_RoundTrip_WriteThenRead(string path)
        {
            var key = McmExposedKeys.All.First(k => k.Path == path);
            var cfg = new VividWorldConfig();

            object writeVal;
            switch (key.Kind)
            {
                case McmKeyKind.Bool:
                    bool currentBool = (bool)McmExposedKeys.Read(cfg, path)!;
                    writeVal = !currentBool;
                    break;
                case McmKeyKind.Int:
                    writeVal = (int)key.Min;
                    break;
                case McmKeyKind.Double:
                    writeVal = key.Min;
                    break;
                case McmKeyKind.Text:
                    writeVal = "CustomTestKey";
                    break;
                case McmKeyKind.Dropdown:
                    Assert.NotNull(key.Choices);
                    Assert.True(key.Choices!.Count > 1);
                    writeVal = key.Choices[1];
                    break;
                default:
                    throw new InvalidOperationException("Unknown kind: " + key.Kind);
            }

            bool ok = McmExposedKeys.Write(cfg, path, writeVal);
            Assert.True(ok, $"Write failed for {path} with value {writeVal}");

            var readBack = McmExposedKeys.Read(cfg, path);
            if (key.Kind == McmKeyKind.Double)
            {
                Assert.Equal((double)writeVal, (double)readBack!, 4);
            }
            else
            {
                Assert.Equal(writeVal, readBack);
            }
        }

        [Fact]
        public void McmExposedKeys_NumericBounds_MatchNormalizeClamping()
        {
            var numericKeys = McmExposedKeys.All.Where(k => k.Kind == McmKeyKind.Int || k.Kind == McmKeyKind.Double);
            foreach (var key in numericKeys)
            {
                var cfg = new VividWorldConfig();

                // Test Min - 1
                object belowMin = key.Kind == McmKeyKind.Int ? (object)((int)key.Min - 1) : (object)(key.Min - 1.0);
                bool okBelow = McmExposedKeys.Write(cfg, key.Path, belowMin);
                Assert.False(okBelow, $"{key.Path} write below Min should return false");
                var readBelow = Convert.ToDouble(McmExposedKeys.Read(cfg, key.Path));
                Assert.Equal(key.Min, readBelow, 4);

                // Normalizing should not change the clamped value
                var notices = new List<ClampNotice>();
                cfg.Normalize(notices);
                var normalizedBelow = Convert.ToDouble(McmExposedKeys.Read(cfg, key.Path));
                Assert.Equal(key.Min, normalizedBelow, 4);

                // Test Max + 1
                object aboveMax = key.Kind == McmKeyKind.Int ? (object)((int)key.Max + 1) : (object)(key.Max + 1.0);
                bool okAbove = McmExposedKeys.Write(cfg, key.Path, aboveMax);
                Assert.False(okAbove, $"{key.Path} write above Max should return false");
                var readAbove = Convert.ToDouble(McmExposedKeys.Read(cfg, key.Path));
                Assert.Equal(key.Max, readAbove, 4);

                cfg.Normalize(notices);
                var normalizedAbove = Convert.ToDouble(McmExposedKeys.Read(cfg, key.Path));
                Assert.Equal(key.Max, normalizedAbove, 4);
            }
        }

        [Fact]
        public void McmExposedKeys_Dropdown_InvalidValue_ReturnsFalseAndPreservesValue()
        {
            var dropdownKeys = McmExposedKeys.All.Where(k => k.Kind == McmKeyKind.Dropdown);
            foreach (var key in dropdownKeys)
            {
                var cfg = new VividWorldConfig();
                var before = McmExposedKeys.Read(cfg, key.Path);

                bool ok = McmExposedKeys.Write(cfg, key.Path, "invalid_choice_xyz_123");
                Assert.False(ok);

                var after = McmExposedKeys.Read(cfg, key.Path);
                Assert.Equal(before, after);
            }
        }

        [Fact]
        public void McmExposedKeys_Write_OnlyModifiesTargetKeyInSerializedJson()
        {
            foreach (var key in McmExposedKeys.All)
            {
                var cfgBaseline = new VividWorldConfig();
                var cfgTarget = new VividWorldConfig();

                object testVal;
                switch (key.Kind)
                {
                    case McmKeyKind.Bool:
                        testVal = !(bool)McmExposedKeys.Read(cfgBaseline, key.Path)!;
                        break;
                    case McmKeyKind.Int:
                        int curInt = (int)McmExposedKeys.Read(cfgBaseline, key.Path)!;
                        testVal = curInt == (int)key.Min ? (int)key.Max : (int)key.Min;
                        break;
                    case McmKeyKind.Double:
                        double curDbl = (double)McmExposedKeys.Read(cfgBaseline, key.Path)!;
                        testVal = Math.Abs(curDbl - key.Min) < 0.001 ? key.Max : key.Min;
                        break;
                    case McmKeyKind.Text:
                        string curTxt = (string)McmExposedKeys.Read(cfgBaseline, key.Path)!;
                        testVal = curTxt + "_diff";
                        break;
                    case McmKeyKind.Dropdown:
                        string curChoice = (string)McmExposedKeys.Read(cfgBaseline, key.Path)!;
                        testVal = key.Choices!.First(c => !string.Equals(c, curChoice, StringComparison.Ordinal));
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                McmExposedKeys.Write(cfgTarget, key.Path, testVal);

                var jBase = JObject.Parse(VividJson.Write(cfgBaseline));
                var jTarget = JObject.Parse(VividJson.Write(cfgTarget));

                var diffPaths = FindDifferingLeafPaths(jBase, jTarget, "");
                Assert.Single(diffPaths);
                Assert.Equal(key.Path, diffPaths[0]);
            }
        }

        [Fact]
        public void VividWorldMcmSettings_StringTable_Coverage()
        {
            string repoRoot = FindRepoRoot();
            string settingsPath = Path.Combine(repoRoot, "src", "VividWorld.Module", "Mcm", "VividWorldMcmSettings.cs");
            Assert.True(File.Exists(settingsPath), $"VividWorldMcmSettings.cs not found at {settingsPath}");

            string code = File.ReadAllText(settingsPath);
            var matches = Regex.Matches(code, @"\{=([A-Za-z0-9_]+)\}");
            Assert.True(matches.Count >= 50, $"Expected at least 50 localized keys in VividWorldMcmSettings.cs, found {matches.Count}");

            var keysInCode = matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

            string enPath = LocalizationTableTests.EnglishTablePath(repoRoot);
            var enIds = LoadStringIds(enPath);

            var tables = new[] { enPath }.Concat(LocalizationTableTests.TranslatedTablePaths(repoRoot));

            foreach (var tablePath in tables)
            {
                string langName = Path.GetFileName(Path.GetDirectoryName(tablePath)) ?? "English";
                var tableIds = LoadStringIds(tablePath);

                var missing = keysInCode.Except(tableIds).ToList();
                Assert.True(missing.Count == 0,
                    $"Language table {langName} is missing keys from VividWorldMcmSettings: {string.Join(", ", missing)}");
            }
        }

        [Fact]
        public void VividWorldMcmSettings_FallbackText_MatchesEnglishTableVerbatim()
        {
            string repoRoot = FindRepoRoot();
            string settingsPath = Path.Combine(repoRoot, "src", "VividWorld.Module", "Mcm", "VividWorldMcmSettings.cs");
            Assert.True(File.Exists(settingsPath), $"VividWorldMcmSettings.cs not found at {settingsPath}");

            string code = File.ReadAllText(settingsPath);
            // Match {=Key}FallbackText up to quote or closing tag
            var matches = Regex.Matches(code, @"\{=([A-Za-z0-9_]+)\}([^""]+)""");
            Assert.True(matches.Count >= 50, $"Expected at least 50 localized key/fallback pairs, found {matches.Count}");

            string enPath = LocalizationTableTests.EnglishTablePath(repoRoot);
            var enDoc = XDocument.Load(enPath);
            var enDict = (enDoc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                .ToDictionary(
                    elem => elem.Attribute("id")?.Value ?? "",
                    elem => elem.Attribute("text")?.Value ?? "",
                    StringComparer.Ordinal);

            var mismatches = new List<string>();
            foreach (Match m in matches)
            {
                string key = m.Groups[1].Value;
                string fallback = m.Groups[2].Value;

                if (!enDict.TryGetValue(key, out var enText))
                {
                    mismatches.Add($"Key '{key}' not found in English table");
                }
                else if (!string.Equals(fallback, enText, StringComparison.Ordinal))
                {
                    mismatches.Add($"Key '{key}': code fallback \"{fallback}\" != English table \"{enText}\"");
                }
            }

            Assert.True(mismatches.Count == 0,
                "Fallback text mismatches found:\n" + string.Join("\n", mismatches));
        }

        private static List<string> FindDifferingLeafPaths(JToken a, JToken b, string currentPath)
        {
            var diffs = new List<string>();
            if (a.Type != b.Type)
            {
                diffs.Add(currentPath);
                return diffs;
            }

            if (a is JObject objA && b is JObject objB)
            {
                var allKeys = objA.Properties().Select(p => p.Name).Union(objB.Properties().Select(p => p.Name)).Distinct();
                foreach (var k in allKeys)
                {
                    var childA = objA[k];
                    var childB = objB[k];
                    string childPath = string.IsNullOrEmpty(currentPath) ? k : $"{currentPath}.{k}";
                    if (childA == null || childB == null)
                    {
                        diffs.Add(childPath);
                    }
                    else
                    {
                        diffs.AddRange(FindDifferingLeafPaths(childA, childB, childPath));
                    }
                }
            }
            else if (a is JArray arrA && b is JArray arrB)
            {
                if (arrA.Count != arrB.Count)
                {
                    diffs.Add(currentPath);
                }
                else
                {
                    for (int i = 0; i < arrA.Count; i++)
                    {
                        diffs.AddRange(FindDifferingLeafPaths(arrA[i], arrB[i], $"{currentPath}[{i}]"));
                    }
                }
            }
            else if (!JToken.DeepEquals(a, b))
            {
                diffs.Add(currentPath);
            }

            return diffs;
        }

        private static List<string> LoadStringIds(string path)
        {
            var doc = XDocument.Load(path);
            return (doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                .Select(e => e.Attribute("id")?.Value ?? "")
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
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

        /// <summary>**曆法縮放過的鍵不准出現在設定畫面上。**
        /// 那些鍵在戰役載入時會被 <c>CalendarScaling.Apply</c> 乘上比例，記憶體裡的值
        /// 不等於 config.json 裡的值；任何把設定寫回檔案的路徑一碰它，縮放後的值就進了
        /// 檔案，下次啟動再縮一次，一輪一輪縮下去。這條測試是那個洞的封條。</summary>
        [Fact]
        public void McmExposedKeys_DoesNotExposeAnyCalendarScaledKey()
        {
            var scaled = new HashSet<string>(CalendarScaling.ScaledPaths, StringComparer.OrdinalIgnoreCase);
            var offenders = McmExposedKeys.All
                .Select(k => k.Path)
                .Where(scaled.Contains)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "這些鍵會被曆法縮放，不能接到設定畫面上（寫回檔案會一輪一輪縮下去）：" +
                string.Join(", ", offenders));
        }
    }
}

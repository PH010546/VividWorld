#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Tests.Fakes
{
    public enum FileOp
    {
        WriteAllText,
        Replace,
        Move,
        Exists,
        ReadAllText,
        ListFiles
    }

    internal sealed class FailingFileWriter : IFileWriter
    {
        public sealed class FailureRule
        {
            public string PathSubstring { get; set; } = string.Empty;
            public FileOp Op { get; set; }
            public int OnCallNumber { get; set; } = 1;
            public int CurrentCallCount { get; set; }
        }

        private readonly List<FailureRule> _rules = new();

        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void FailOn(string pathSubstring, FileOp op, int onCallNumber = 1)
        {
            _rules.Add(new FailureRule
            {
                PathSubstring = pathSubstring ?? string.Empty,
                Op = op,
                OnCallNumber = onCallNumber
            });
        }

        public void ClearFailures()
        {
            _rules.Clear();
        }

        private bool ShouldFail(string? path, FileOp op)
        {
            foreach (var rule in _rules)
            {
                if (rule.Op == op)
                {
                    if (string.IsNullOrEmpty(rule.PathSubstring) ||
                        (path != null && path.IndexOf(rule.PathSubstring, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        rule.CurrentCallCount++;
                        if (rule.CurrentCallCount == rule.OnCallNumber)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }

        public bool WriteAllText(string path, string content)
        {
            if (ShouldFail(path, FileOp.WriteAllText)) return false;
            Files[Normalize(path)] = content ?? string.Empty;
            return true;
        }

        public bool Replace(string sourceTmp, string destination)
        {
            if (ShouldFail(sourceTmp, FileOp.Replace) || ShouldFail(destination, FileOp.Replace)) return false;

            var normSrc = Normalize(sourceTmp);
            var normDst = Normalize(destination);
            if (!Files.ContainsKey(normSrc)) return false;

            Files[normDst] = Files[normSrc];
            Files.Remove(normSrc);
            return true;
        }

        public bool Move(string source, string destination)
        {
            if (ShouldFail(source, FileOp.Move) || ShouldFail(destination, FileOp.Move)) return false;

            var normSrc = Normalize(source);
            var normDst = Normalize(destination);
            if (!Files.ContainsKey(normSrc)) return false;

            Files[normDst] = Files[normSrc];
            Files.Remove(normSrc);
            return true;
        }

        public bool Exists(string path)
        {
            if (ShouldFail(path, FileOp.Exists)) return false;
            return Files.ContainsKey(Normalize(path));
        }

        public string? ReadAllText(string path)
        {
            if (ShouldFail(path, FileOp.ReadAllText)) return null;
            return Files.TryGetValue(Normalize(path), out var text) ? text : null;
        }

        public IReadOnlyList<string> ListFiles(string folder, string searchPattern)
        {
            if (ShouldFail(folder, FileOp.ListFiles)) return Array.Empty<string>();

            var normFolder = Normalize(folder).TrimEnd('/');
            var regexPattern = "^" + Regex.Escape(searchPattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

            var results = new List<string>();
            foreach (var kvp in Files)
            {
                var filePath = kvp.Key;
                var dir = Path.GetDirectoryName(filePath)?.Replace('\\', '/').TrimEnd('/') ?? "";
                if (string.Equals(dir, normFolder, StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (regex.IsMatch(fileName))
                    {
                        results.Add(filePath);
                    }
                }
            }
            return results;
        }
    }
}

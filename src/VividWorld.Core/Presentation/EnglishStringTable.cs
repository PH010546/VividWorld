using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// Reads and holds the English module string table directly from std_module_strings_xml.xml.
    /// Provides fresh wording for events regardless of active game language.
    /// </summary>
    public sealed class EnglishStringTable
    {
        private readonly Dictionary<string, string> _strings;

        public int Count => _strings.Count;

        public IEnumerable<string> Keys => _strings.Keys;

        public EnglishStringTable(Dictionary<string, string>? strings = null)
        {
            _strings = strings != null
                ? new Dictionary<string, string>(strings, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public string? Get(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _strings.TryGetValue(id!, out var val) ? val : null;
        }

        public string Lookup(string? id, string? fallback)
        {
            if (!string.IsNullOrEmpty(id) && _strings.TryGetValue(id!, out var val))
            {
                return val;
            }
            return fallback ?? string.Empty;
        }

        public string GetWithFallback(string? id, string fallback) => Lookup(id, fallback);

        public static EnglishStringTable LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new EnglishStringTable();
            }

            try
            {
                string xml = File.ReadAllText(path);
                return Parse(xml);
            }
            catch
            {
                return new EnglishStringTable();
            }
        }

        public static EnglishStringTable Parse(string xml)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(xml)) return new EnglishStringTable(dict);

            try
            {
                using var stringReader = new StringReader(xml);
                using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                });

                while (xmlReader.Read())
                {
                    if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "string")
                    {
                        string? id = xmlReader.GetAttribute("id");
                        string? text = xmlReader.GetAttribute("text");
                        if (!string.IsNullOrEmpty(id) && text != null)
                        {
                            dict[id!] = text;
                        }
                    }
                }
            }
            catch
            {
                // Return whatever was successfully parsed up to the error
            }

            return new EnglishStringTable(dict);
        }
    }
}

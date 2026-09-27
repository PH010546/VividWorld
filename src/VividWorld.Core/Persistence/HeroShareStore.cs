#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Dialogue;

namespace VividWorld.Core.Persistence
{
    public sealed class HeroShareStore
    {
        private readonly string _filePath;
        private readonly IFileWriter _writer;

        public HeroShareStore(string filePath) : this(filePath, new SystemFileWriter())
        {
        }

        public HeroShareStore(string filePath, IFileWriter writer)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public string FilePath => _filePath;

        public string? LastLoadError { get; private set; }

        public bool FileExisted { get; private set; }

        public HeroShareLedger Load()
        {
            LastLoadError = null;
            FileExisted = _writer.Exists(_filePath);
            if (!FileExisted)
            {
                return new HeroShareLedger();
            }

            try
            {
                string? json = _writer.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new HeroShareLedger();
                }

                var dict = VividJson.Read<Dictionary<string, HeroShareEntry>>(json!);
                if (dict == null)
                {
                    LastLoadError = "file has content but is not a valid shares dictionary";
                    return new HeroShareLedger();
                }

                var ledger = new HeroShareLedger();
                ledger.LoadFrom(dict);
                return ledger;
            }
            catch (Exception ex)
            {
                LastLoadError = ex.GetType().Name + ": " + ex.Message;
                return new HeroShareLedger();
            }
        }

        public bool Save(HeroShareLedger ledger)
        {
            if (ledger == null) return false;
            try
            {
                string json = VividJson.Write(ledger.ToDictionary());
                return AtomicFile.Write(_writer, _filePath, json);
            }
            catch
            {
                return false;
            }
        }
    }
}

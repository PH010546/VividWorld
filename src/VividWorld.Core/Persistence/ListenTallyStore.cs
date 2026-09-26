#nullable enable
using System;
using VividWorld.Core.Dialogue;

namespace VividWorld.Core.Persistence
{
    public sealed class ListenTallyStore
    {
        private readonly string _filePath;
        private readonly IFileWriter _writer;

        public ListenTallyStore(string filePath) : this(filePath, new SystemFileWriter())
        {
        }

        public ListenTallyStore(string filePath, IFileWriter writer)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public string FilePath => _filePath;

        /// <summary>上一次 <see cref="Load"/> 讀到了檔案卻解析失敗時的原因；成功或檔案不存在為 null。
        /// 失敗時 Load 仍回傳空的加總（量測不該擋遊戲），但下一次存檔會蓋掉那個壞檔——呼叫端要把這個印出來。</summary>
        public string? LastLoadError { get; private set; }

        public ListenTally Load()
        {
            LastLoadError = null;
            if (!_writer.Exists(_filePath))
            {
                return new ListenTally();
            }

            try
            {
                string? json = _writer.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new ListenTally();
                }
                // VividJson.Read 遇到壞檔不拋例外、只回 null，所以「有內容卻讀出 null」就是壞檔。
                var loaded = VividJson.Read<ListenTally>(json!);
                if (loaded == null)
                {
                    LastLoadError = "file has content but is not a valid listen tally";
                    return new ListenTally();
                }
                return loaded;
            }
            catch (Exception ex)
            {
                LastLoadError = ex.GetType().Name + ": " + ex.Message;
                return new ListenTally();
            }
        }

        public bool Save(ListenTally tally)
        {
            if (tally == null) return false;
            try
            {
                string json = VividJson.Write(tally);
                return AtomicFile.Write(_writer, _filePath, json);
            }
            catch
            {
                return false;
            }
        }
    }
}

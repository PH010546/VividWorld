#nullable enable
using System.Collections.Generic;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 存在的唯一理由是讓測試能注入失敗。
    /// 每個方法都必須整段 try/catch 並回傳 false，絕不向上拋例外。
    /// </summary>
    public interface IFileWriter
    {
        bool WriteAllText(string path, string content);
        bool Replace(string sourceTmp, string destination);
        bool Move(string source, string destination);
        bool Exists(string path);
        string? ReadAllText(string path);                                      // 失敗或不存在回傳 null，絕不拋出
        IReadOnlyList<string> ListFiles(string folder, string searchPattern);   // 失敗回傳空清單
    }
}

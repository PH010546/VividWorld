#nullable enable
using System;

namespace VividWorld.Core.Persistence
{
    public static class AtomicFile
    {
        /// <summary>
        /// 序列化好的內容原子寫入。回傳是否成功。絕不拋出。
        /// 流程（規格 §8.2）：
        /// 1. 寫 path + ".tmp" —— 失敗即回傳 false，且不得動原檔
        /// 2. 目標存在 → Replace(tmp, path)；不存在 → Move(tmp, path)
        /// 3. 任一步失敗 → 回傳 false
        /// </summary>
        public static bool Write(IFileWriter writer, string path, string content)
        {
            try
            {
                if (writer == null || string.IsNullOrEmpty(path)) return false;

                var tmp = path + ".tmp";
                if (!writer.WriteAllText(tmp, content ?? string.Empty)) return false;

                if (writer.Exists(path))
                {
                    return writer.Replace(tmp, path);
                }
                else
                {
                    return writer.Move(tmp, path);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

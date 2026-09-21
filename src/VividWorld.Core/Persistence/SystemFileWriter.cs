#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 正式環境使用的檔案寫入器。
    /// 每個方法整段 try/catch，絕不向上拋例外。
    /// </summary>
    public sealed class SystemFileWriter : IFileWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public bool WriteAllText(string path, string content)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, content ?? string.Empty, Utf8NoBom);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Replace(string sourceTmp, string destination)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceTmp) || string.IsNullOrEmpty(destination)) return false;
                File.Replace(sourceTmp, destination, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Move(string source, string destination)
        {
            try
            {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination)) return false;
                var dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Move(source, destination);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Exists(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public string? ReadAllText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return null;
            }
        }

        public IReadOnlyList<string> ListFiles(string folder, string searchPattern)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return Array.Empty<string>();
                }
                return Directory.GetFiles(folder, searchPattern);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}

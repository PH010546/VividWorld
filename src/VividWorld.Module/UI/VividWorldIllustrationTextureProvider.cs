#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using TaleWorlds.GauntletUI;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.TwoDimension;
using VividWorld.Core.Config;
using EngineTextureHandle = TaleWorlds.Engine.Texture;
using UiTexture = TaleWorlds.TwoDimension.Texture;

namespace VividWorld.UI
{
    /// <summary>
    /// 從模組資料夾 <c>GUI\Illustrations\&lt;代號&gt;.png</c> 讀圖給 <see cref="VividWorldIllustrationWidget"/>。
    /// 每個代號只讀一輪：成功就快取，失敗（檔案不在、讀不出來）就不放圖，視窗照開。
    /// 沒有透明通道的 PNG 交給引擎會被畫成紅藍對調，有透明通道的照檔案顏色畫，所以先轉成有透明通道的 32 位元 PNG 再交 <c>CreateFromMemory</c>，不對調任何通道；給路徑讓引擎自己讀（<c>LoadTextureFromPath</c>）不會轉檔，沒有透明通道的圖會偏藍，所以只當備援（出處：帳本 L-83）。
    /// </summary>
    public class VividWorldIllustrationTextureProvider : TextureProvider
    {
        private string _illustrationId = string.Empty;
        private string? _attemptedId;
        private EngineTextureHandle? _engineTexture;
        private UiTexture? _texture;

        /// <summary>由元件的 <c>SetTextureProviderProperty</c> 以名稱設進來。</summary>
        public string IllustrationId
        {
            get => _illustrationId;
            set
            {
                string v = value ?? string.Empty;
                if (v == _illustrationId) return;
                _illustrationId = v;
                ReleaseTexture();   // 換代號就丟掉舊圖，下一幀重讀
                _attemptedId = null;
            }
        }

        protected override UiTexture OnGetTextureForRender(TwoDimensionContext twoDimensionContext, string name)
        {
            if (_attemptedId != _illustrationId)
            {
                _attemptedId = _illustrationId;
                try
                {
                    Load(_illustrationId);
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Illustration '{_illustrationId}': loading failed", ex);
                }
            }
            return _texture!;   // 沒有圖時回 null，元件就不畫
        }

        public override void Clear(bool clearNextFrame)
        {
            ReleaseTexture();
            _attemptedId = null;
            base.Clear(clearNextFrame);
        }

        private void ReleaseTexture()
        {
            var engine = _engineTexture;
            _engineTexture = null;
            _texture = null;
            if (engine == null) return;
            try
            {
                engine.Release();
            }
            catch (Exception ex)
            {
                ModLog.Error("Illustration: releasing the texture failed", ex);
            }
        }

        private void Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            string? folder = ResolveFolder();
            if (folder == null)
            {
                ModLog.Info($"Illustration '{id}': missing (no GUI/Illustrations folder under Modules/VividWorld or Modules/VividWorld.Dev).");
                return;
            }

            string file = Path.Combine(folder, id + ".png");
            if (!File.Exists(file))
            {
                ModLog.Info($"Illustration '{id}': missing ({file}).");
                return;
            }

            // 優先嘗試：沒有透明通道的 PNG 交給引擎會被畫成紅藍對調，有透明通道的照檔案顏色畫，所以先轉成有透明通道的 32 位元 PNG 再交 CreateFromMemory，不對調任何通道（出處：帳本 L-83）
            EngineTextureHandle? memoryTexture = null;
            try
            {
                byte[] rawFileBytes = File.ReadAllBytes(file);
                using (var rawStream = new MemoryStream(rawFileBytes))
                using (var originalBitmap = new Bitmap(rawStream))
                using (var argbBitmap = new Bitmap(originalBitmap.Width, originalBitmap.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(argbBitmap))
                    {
                        g.DrawImage(originalBitmap, 0, 0, originalBitmap.Width, originalBitmap.Height);
                    }

                    using (var pngStream = new MemoryStream())
                    {
                        argbBitmap.Save(pngStream, ImageFormat.Png);
                        byte[] png32Bytes = pngStream.ToArray();
                        memoryTexture = EngineTextureHandle.CreateFromMemory(png32Bytes);
                    }
                }

                if (memoryTexture == null)
                {
                    ModLog.Info($"Illustration '{id}': converted to 32-bit PNG, CreateFromMemory returned null.");
                }
                else
                {
                    var wrapped = new UiTexture(new EngineTexture(memoryTexture));
                    _engineTexture = memoryTexture;
                    _texture = wrapped;
                    ModLog.Info($"Illustration '{id}': converted to 32-bit PNG, CreateFromMemory succeeded ({DescribeSize(wrapped)}).");
                    return;
                }
            }
            catch (Exception ex)
            {
                ModLog.Info($"Illustration '{id}': converting to 32-bit PNG or CreateFromMemory threw {ex.GetType().Name}: {ex.Message}");
                if (memoryTexture != null && !ReferenceEquals(memoryTexture, _engineTexture))
                {
                    try { memoryTexture.Release(); } catch { /* 沒載成功的貼圖放不掉也無妨 */ }
                }
            }

            // 備援嘗試：給路徑讓引擎自己讀（LoadTextureFromPath）會把紅藍對調，所以只當備援；引擎要的檔名含不含副檔名沒查到，兩種都試；資料夾用絕對路徑（出處：帳本 L-81）
            string[] fileNames = { id + ".png", id };
            foreach (string fileName in fileNames)
            {
                string attempt = "LoadTextureFromPath('" + fileName + "', '" + folder + "')";
                EngineTextureHandle? loaded = null;
                try
                {
                    loaded = EngineTextureHandle.LoadTextureFromPath(fileName, folder);
                    if (loaded == null)
                    {
                        ModLog.Info($"Illustration '{id}': {attempt} returned null.");
                        continue;
                    }

                    var wrapped = new UiTexture(new EngineTexture(loaded));
                    _engineTexture = loaded;
                    _texture = wrapped;
                    ModLog.Info($"Illustration '{id}': {attempt} succeeded ({DescribeSize(wrapped)}) - colours not corrected.");
                    return;
                }
                catch (Exception ex)
                {
                    ModLog.Info($"Illustration '{id}': {attempt} threw {ex.GetType().Name}: {ex.Message}");
                    if (loaded != null && !ReferenceEquals(loaded, _engineTexture))
                    {
                        try { loaded.Release(); } catch { /* 沒載成功的貼圖放不掉也無妨 */ }
                    }
                }
            }

            ModLog.Warn($"Illustration '{id}': every way of reading {file} failed - the window opens without a picture.");
        }

        private static string DescribeSize(UiTexture texture)
        {
            try
            {
                return $"{texture.Width}x{texture.Height}, valid={texture.IsValid}";
            }
            catch (Exception ex)
            {
                return "size unreadable: " + ex.Message;
            }
        }

        /// <summary>模組資料夾：先試 <c>Modules\VividWorld</c>，再試 <c>Modules\VividWorld.Dev</c>。</summary>
        private static string? ResolveFolder()
        {
            try
            {
                string p1 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld", "GUI", "Illustrations");
                if (Directory.Exists(p1)) return p1;

                string p2 = Path.Combine(TaleWorlds.Library.BasePath.Name, "Modules", "VividWorld.Dev", "GUI", "Illustrations");
                if (Directory.Exists(p2)) return p2;
            }
            catch (Exception ex)
            {
                ModLog.Error("Illustration: resolving the folder failed", ex);
            }
            return null;
        }
    }
}

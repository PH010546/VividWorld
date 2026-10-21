#nullable enable

using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace VividWorld.UI
{
    /// <summary>
    /// 版面檔裡用來顯示模組自己一張插圖的元件。<see cref="IllustrationId"/> 是情境代號，
    /// 圖檔由 <see cref="VividWorldIllustrationTextureProvider"/> 在執行時從模組資料夾讀進來。
    /// </summary>
    public class VividWorldIllustrationWidget : TextureWidget
    {
        private string _illustrationId = string.Empty;

        public VividWorldIllustrationWidget(UIContext context) : base(context)
        {
            // 要在建構時就指定：版面的綁定一旦先把代號送進來，提供者必須已經存在才收得到
            TextureProviderName = nameof(VividWorldIllustrationTextureProvider);
        }

        public string IllustrationId
        {
            get => _illustrationId;
            set
            {
                if (value != _illustrationId)
                {
                    _illustrationId = value ?? string.Empty;
                    SetTextureProviderProperty(nameof(IllustrationId), _illustrationId);
                }
            }
        }
    }
}

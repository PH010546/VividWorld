#nullable enable

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 玩家入局視窗右下角顯示哪一項的說明：滑鼠指著的優先，沒有指著就顯示已選的那項，都沒有就是空字串。
    /// 順序跟原版隨機事件視窗一致，讓兩種視窗用起來不一樣的地方只有資料來源。
    /// </summary>
    public static class PresenceHintPicker
    {
        public static string Pick(string? hoveredHint, string? selectedHint)
        {
            if (hoveredHint != null) return hoveredHint;
            if (selectedHint != null) return selectedHint;
            return string.Empty;
        }
    }
}

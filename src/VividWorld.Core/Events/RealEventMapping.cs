namespace VividWorld.Core.Events
{
    /// <summary>原生 `KillCharacterAction.KillCharacterActionDetail` 的離線鏡像（帳本 D-52）。
    /// **成員與數值必須與原生逐一相同，不得自己加別名**——這份是給 Core 測試用的，
    /// 多出來的成員會讓讀的人以為原生也有那個值。</summary>
    public enum KillCharacterActionDetail
    {
        None = 0,
        Murdered = 1,
        DiedInLabor = 2,
        DiedOfOldAge = 3,
        DiedInBattle = 4,
        WoundedInBattle = 5,
        Executed = 6,
        ExecutionAfterMapEvent = 7,
        Lost = 8
    }

    public static class RealEventMapping
    {
        public static string? TemplateForKill(KillCharacterActionDetail detail)
            => TemplateForKill((int)detail);

        public static string? TemplateForKill(int detail)
        {
            switch (detail)
            {
                case 1: // Murdered
                    return "hero_murdered";
                case 6: // Executed
                case 7: // ExecutionAfterMapEvent
                    return "hero_executed";
                case 4: // DiedInBattle
                    return "hero_died_in_battle";
                case 2: // DiedInLabor
                case 3: // DiedOfOldAge
                    return "hero_died_naturally";
                case 0: // None
                case 5: // WoundedInBattle
                case 8: // Lost
                default:
                    return null;
            }
        }

        public static string? TemplateForRelease(EndCaptivityDetail detail)
            => TemplateForRelease((int)detail);

        public static string? TemplateForRelease(int detail)
        {
            switch (detail)
            {
                case 3: // ReleasedAfterEscape
                    return "hero_escaped_captivity";
                case 0: // Ransom
                case 1: // ReleasedAfterPeace
                case 2: // ReleasedAfterBattle
                case 4: // ReleasedByChoice
                case 6: // ReleasedByCompensation
                    return "hero_released";
                case 5: // Death (defensive; native doesn't emit for non-player)
                default:
                    return null;
            }
        }
    }

    /// <summary>原生 `EndCaptivityDetail` 的離線鏡像（帳本 D-59）。
    /// 成員與數值必須與原生逐一相同，不得自己加別名。</summary>
    public enum EndCaptivityDetail
    {
        Ransom = 0,
        ReleasedAfterPeace = 1,
        ReleasedAfterBattle = 2,
        ReleasedAfterEscape = 3,
        ReleasedByChoice = 4,
        Death = 5,
        ReleasedByCompensation = 6
    }
}

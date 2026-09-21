using System;

namespace VividWorld.Core.Channels
{
    public static class ChannelClass
    {
        /// <summary>見面通道：兩人實際在同一個地方。關係影響小——你不會因為喜歡一個人就更常碰到他。</summary>
        public static bool IsInPerson(ChannelKind kind) =>
            kind switch
            {
                ChannelKind.SameParty => true,
                ChannelKind.SameArmy => true,
                ChannelKind.SameSettlement => true,
                ChannelKind.SameClan => false,
                ChannelKind.KinAbroad => false,
                ChannelKind.Kingdom => false,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Unknown ChannelKind: {kind}")
            };
    }
}

using System;
using System.Collections.Generic;
using VividWorld.Core.Channels;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Rumors
{
    public sealed class PropagationOutcome
    {
        public bool DidAnything { get; }
        public string? TellerHeroId { get; }
        public IReadOnlyList<KnownByEntry> NewKnowers { get; }
        public IReadOnlyDictionary<string, ChannelKind> ChannelKinds { get; }
        public IReadOnlyList<RehearRecord> Reheard { get; }

        /// <summary>講述者不信這則消息時傳話機率乘上的倍數；1 = 沒有影響。給講述者日誌用。</summary>
        public double DisbelieverMultiplier { get; set; } = 1.0;

        /// <summary>這一次開口，每位聽的人的狀況（通道、好感、機率、結果）。
        /// 只在 debug.logTellerTurns 開著時才收集，關著時是空清單。純診斷，不影響傳話。</summary>
        public IReadOnlyList<ContactObservation> Contacts { get; set; } = Array.Empty<ContactObservation>();

        public static PropagationOutcome Nothing { get; } = new PropagationOutcome(false, null, Array.Empty<KnownByEntry>());

        public PropagationOutcome(
            bool didAnything,
            string? tellerHeroId,
            IReadOnlyList<KnownByEntry> newKnowers,
            IReadOnlyDictionary<string, ChannelKind>? channelKinds = null)
            : this(didAnything, tellerHeroId, newKnowers, channelKinds, null)
        {
        }

        public PropagationOutcome(
            bool didAnything,
            string? tellerHeroId,
            IReadOnlyList<KnownByEntry> newKnowers,
            IReadOnlyDictionary<string, ChannelKind>? channelKinds,
            IReadOnlyList<RehearRecord>? reheard)
        {
            DidAnything = didAnything;
            TellerHeroId = tellerHeroId;
            NewKnowers = newKnowers ?? Array.Empty<KnownByEntry>();
            ChannelKinds = channelKinds ?? new Dictionary<string, ChannelKind>();
            Reheard = reheard ?? Array.Empty<RehearRecord>();
        }
    }
}

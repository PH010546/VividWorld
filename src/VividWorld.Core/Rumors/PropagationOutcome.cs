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

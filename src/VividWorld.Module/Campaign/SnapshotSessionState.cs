#nullable enable

using VividWorld.Core.Persistence;

namespace VividWorld.Campaign
{
    internal sealed class SnapshotSessionState
    {
        internal SnapshotResult? LastTake { get; set; }
        internal SnapshotResult? LastRestore { get; set; }
    }
}

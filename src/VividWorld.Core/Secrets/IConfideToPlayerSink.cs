using VividWorld.Core.Events;

namespace VividWorld.Core.Secrets
{
    public interface IConfideToPlayerSink
    {
        void OnConfideToPlayer(WorldEvent evt, string tellerHeroId, double day);
    }

    public sealed class NullConfideToPlayerSink : IConfideToPlayerSink
    {
        public static readonly NullConfideToPlayerSink Instance = new();
        public void OnConfideToPlayer(WorldEvent evt, string tellerHeroId, double day) { }
    }
}

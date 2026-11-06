using System;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Events;
using VividWorld.Core.Secrets;

namespace VividWorld.Campaign
{
    internal sealed class LogConfideToPlayerSink : IConfideToPlayerSink
    {
        public void OnConfideToPlayer(WorldEvent evt, string tellerHeroId, double day)
        {
            if (evt == null) return;
            string tellerName = Hero.Find(tellerHeroId)?.Name?.ToString() ?? tellerHeroId;
            ModLog.Info($"secret confided to player: {evt.EventId} ({evt.Type}) by {tellerName} ({tellerHeroId}) on day {day:0.00}");
        }
    }
}

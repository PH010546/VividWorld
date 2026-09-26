using System;
using System.IO;

namespace VividWorld
{
    /// <summary>所有磁碟路徑的唯一來源。規格 §8.1，形狀取自 ImmersiveAI 的 ModConfig.cs:1210。</summary>
    internal static class VividWorldPaths
    {
        internal static string ConfigDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord", "Configs", "VividWorld");

        internal static string LogFile => Path.Combine(ConfigDirectory, "log.txt");

        internal static string ConfigFile         => Path.Combine(ConfigDirectory, "config.json");
        internal static string ModeNoticeFile     => Path.Combine(ConfigDirectory, "mode_notice.json");
        internal static string ReadmeFile         => Path.Combine(ConfigDirectory, "_README.txt");
        internal static string CampaignsDirectory => Path.Combine(ConfigDirectory, "campaigns");

        internal static string CampaignDirectory(string campaignId) =>
            Path.Combine(CampaignsDirectory, "campaign_" + campaignId);

        internal static string EventsDirectory(string campaignId) =>
            Path.Combine(CampaignDirectory(campaignId), "events");

        internal static string CampaignMarkerFile(string campaignId) =>
            Path.Combine(CampaignDirectory(campaignId), "_campaign.txt");

        internal static string VolunteersFile(string campaignId) =>
            Path.Combine(CampaignDirectory(campaignId), "volunteers.json");

        internal static string PlayerHeardFile(string campaignId) =>
            Path.Combine(CampaignDirectory(campaignId), "player_heard.json");

        internal static string ListenTallyFile(string campaignId) =>
            Path.Combine(CampaignDirectory(campaignId), "listen_tally.json");
    }
}

using System;
using System.Text;

namespace VividWorld.Campaign
{
    internal static class CampaignIdentity
    {
        internal static string MintCampaignId(string playerFirstName)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var fn = Sanitize(playerFirstName);
            return (fn.Length > 0 && fn != "_") ? id + "_" + fn : id;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            bool lastWasUnderscore = false;
            foreach (char c in name)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastWasUnderscore = false;
                }
                else
                {
                    if (!lastWasUnderscore)
                    {
                        sb.Append('_');
                        lastWasUnderscore = true;
                    }
                }
            }
            return sb.ToString().Trim('_');
        }
    }
}

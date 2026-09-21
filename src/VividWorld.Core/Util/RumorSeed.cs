using System;
using System.Globalization;
using System.Text;

namespace VividWorld.Core.Util
{
    public static class RumorSeed
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static long Of(long campaignSeed, params object[] parts)
        {
            var sb = new StringBuilder();
            sb.Append(campaignSeed.ToString(CultureInfo.InvariantCulture));
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    sb.Append('|');
                    var part = parts[i];
                    if (part != null)
                    {
                        sb.Append(Convert.ToString(part, CultureInfo.InvariantCulture));
                    }
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            ulong hash = FnvOffsetBasis;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return (long)hash;
            }
        }

        public static int DayBucket(double day) => (int)Math.Floor(day);
    }
}

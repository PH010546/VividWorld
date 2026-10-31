using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Situations
{
    /// <summary>讀情境目錄時，每個流浪者模板一行：觸發、是否不往外傳、裁決做法數與非裁決做法數、不插手做法數。
    /// 診斷輸出用英文，不在地化。</summary>
    public static class WandererCatalogLogFormatter
    {
        public static IReadOnlyList<string> FormatTemplates(SituationCatalog catalog)
        {
            var lines = new List<string>();
            if (catalog == null) return lines;

            foreach (var template in catalog.Situations)
            {
                if (!SituationCatalogLoader.IsWandererTrigger(template.Trigger)) continue;

                int judgeBranches = template.Branches.Count(b => b.Judge);
                int plainBranches = template.Branches.Count(b => !b.Judge);
                int standAsideBranches = template.Branches.Count(b => b.StandAside);
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "Wanderer situation '{0}': trigger={1} noSpread={2} judgeBranches={3} plainBranches={4} standAsideBranches={5}",
                    template.Id, template.Trigger, template.NoSpread, judgeBranches, plainBranches, standAsideBranches));
            }

            return lines;
        }
    }
}

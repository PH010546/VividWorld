using System.Collections.Generic;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;

namespace VividWorld.Debug
{
    /// <summary>
    /// dev 測試事件的碎片文字。**句尾不帶標點**——分隔符與句號由
    /// <see cref="VividWorld.Core.Presentation.RumorTextAssembler"/> 依當前語言補上
    /// （規格 §9.3／§9.5.4）。自己帶了句點就會變成 "....，....。"。
    /// 由 FactTextPunctuationTests 守住。
    /// </summary>
    internal static class DevEventFactory
    {
        public static EventSubmission CreatePublicTestEvent(string playerHeroId, string conversationHeroId, double day)
        {
            return new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Public,
                Day = day,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = conversationHeroId,
                    ["target"] = playerHeroId
                },
                KnowingRoles = new HashSet<string> { "mastermind" },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "f_who",
                        Category = FactCategory.Who,
                        Fragility = 1,
                        Text = $"Mastermind {conversationHeroId} targeted {playerHeroId}"
                    },
                    new Fact
                    {
                        Id = "f_what",
                        Category = FactCategory.What,
                        Fragility = 3,
                        Text = "A public dispute erupted between the parties"
                    },
                    new Fact
                    {
                        Id = "f_where",
                        Category = FactCategory.Where,
                        Fragility = 5,
                        Text = "The incident occurred in the local territory"
                    }
                }
            };
        }

        public static EventSubmission CreateSecretTestEvent(string playerHeroId, string conversationHeroId, double day)
        {
            return new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Secret,
                Day = day,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = conversationHeroId,
                    ["target"] = playerHeroId
                },
                KnowingRoles = new HashSet<string> { "mastermind" },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "f_who",
                        Category = FactCategory.Who,
                        Fragility = 1,
                        Text = $"Mastermind {conversationHeroId} schemed secretly against {playerHeroId}"
                    },
                    new Fact
                    {
                        Id = "f_what",
                        Category = FactCategory.What,
                        Fragility = 3,
                        Text = "A clandestine conspiracy was set in motion"
                    },
                    new Fact
                    {
                        Id = "f_where",
                        Category = FactCategory.Where,
                        Fragility = 5,
                        Text = "The conspiracy was hatched in the shadows of the realm"
                    }
                }
            };
        }
    }
}

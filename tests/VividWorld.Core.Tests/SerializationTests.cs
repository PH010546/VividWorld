using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SerializationTests
    {
        private static string LoadDesignDocSampleJson()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DesignDocSample.json");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Deserialize_DesignDocSample_PopulatesEveryField()
        {
            string json = LoadDesignDocSampleJson();
            var evt = VividJson.Read<WorldEvent>(json);

            Assert.NotNull(evt);
            Assert.Equal("evt_0412_a7f3", evt!.EventId);
            Assert.Equal(EventOrigin.Secret, evt.Origin);
            Assert.Equal("evt_0409_c1e2", evt.LinkedEventId);
            Assert.Equal(3, evt.Participants.Count);
            Assert.Equal("target", evt.RoleOf("Boris_hero_id"));
            Assert.Equal("mastermind", evt.RoleOf("Ivan_hero_id"));
            Assert.Equal("agent", evt.RoleOf("Aldric_hero_id"));
            Assert.Null(evt.RoleOf("non_existent_hero"));

            Assert.Equal(8, evt.Facts.Count);
            Assert.Equal("who_mastermind", evt.Facts[0].Id);
            Assert.Equal(FactCategory.Who, evt.Facts[0].Category);
            Assert.Equal(string.Empty, evt.Facts[0].TextId);
            Assert.Null(evt.Facts[0].Vars);

            Assert.Equal("context", evt.Facts[4].Id);
            Assert.Equal(FactCategory.Context, evt.Facts[4].Category);
            Assert.Equal("evt_0409_c1e2", evt.Facts[4].RefersTo);

            Assert.Equal(2, evt.KnownBy.Count);
            Assert.Equal("Ivan_hero_id", evt.KnownBy[0].HeroId);
            Assert.Equal(0, evt.KnownBy[0].Hop);
            Assert.Null(evt.KnownBy[0].SourceHeroId);
            Assert.Equal(0.0, evt.KnownBy[0].LearnedDay);
            Assert.Null(evt.KnownBy[0].KnownFactIds);
            Assert.Null(evt.KnownBy[0].RelationImpacts);

            Assert.Equal("Aldric_hero_id", evt.KnownBy[1].HeroId);
            Assert.Equal(0, evt.KnownBy[1].Hop);

            Assert.True(evt.IsKnownBy("Ivan_hero_id"));
            Assert.False(evt.IsKnownBy("Boris_hero_id"));
            Assert.Equal(0, evt.MinHop());
            Assert.Equal(0, evt.MaxHop());

            Assert.NotNull(evt.State);
            Assert.False(evt.State.Leaked);
            Assert.Equal(-1.0, evt.State.LeakedDay);

            Assert.Equal(3, evt.DramaWeight);
            Assert.Empty(evt.Extra);
        }

        [Fact]
        public void Roundtrip_IsStable()
        {
            string json = LoadDesignDocSampleJson();
            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);

            string json1 = VividJson.Write(evt!);
            var evt2 = VividJson.Read<WorldEvent>(json1);
            Assert.NotNull(evt2);

            string json2 = VividJson.Write(evt2!);
            Assert.Equal(json1, json2);
        }

        [Fact]
        public void Roundtrip_ProducesUppercaseCategories()
        {
            string json = LoadDesignDocSampleJson();
            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);

            string serialized = VividJson.Write(evt!);
            Assert.Contains("\"category\": \"WHO\"", serialized);
            Assert.Contains("\"category\": \"CONTEXT\"", serialized);
            Assert.Contains("\"category\": \"OUTCOME\"", serialized);
        }

        [Fact]
        public void Deserialize_MissingStateBlock_DefaultsToUnleaked()
        {
            string json = LoadDesignDocSampleJson();
            Assert.DoesNotContain("\"state\"", json, StringComparison.OrdinalIgnoreCase);

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);
            Assert.NotNull(evt!.State);
            Assert.False(evt.State.Leaked);
            Assert.Equal(-1.0, evt.State.LeakedDay);
            Assert.Null(evt.State.LeakerHeroId);
            Assert.Equal(-1.0, evt.State.LastPropagatedDay);
            Assert.Equal(-1.0, evt.State.LastNewKnowerDay);
            Assert.False(evt.State.Dormant);
        }

        [Fact]
        public void Deserialize_MissingOrigin_DefaultsToSecret()
        {
            string json = @"{
                ""eventId"": ""evt_0001_0001"",
                ""type"": ""test_event"",
                ""day"": 1.0
            }";

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);
            Assert.Equal(EventOrigin.Secret, evt!.Origin);
            Assert.False(evt.IsVisibleToRumorSystem);
        }

        [Fact]
        public void Roundtrip_PreservesUnknownFutureFields()
        {
            string json = @"{
                ""eventId"": ""evt_0001_0001"",
                ""type"": ""test_event"",
                ""day"": 1.0,
                ""origin"": ""public"",
                ""futureTopLevel"": ""future_val_123"",
                ""facts"": [
                    {
                        ""id"": ""f1"",
                        ""category"": ""WHO"",
                        ""text"": ""Someone"",
                        ""futureFactProp"": 999
                    }
                ]
            }";

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);
            Assert.True(evt!.Extra.ContainsKey("futureTopLevel"));
            Assert.Equal("future_val_123", evt.Extra["futureTopLevel"].ToString());
            Assert.True(evt.Facts[0].Extra.ContainsKey("futureFactProp"));
            Assert.Equal(999, (int)evt.Facts[0].Extra["futureFactProp"]);

            string serialized = VividJson.Write(evt);
            Assert.Contains("\"futureTopLevel\": \"future_val_123\"", serialized);
            Assert.Contains("\"futureFactProp\": 999", serialized);
        }

        /// <summary>
        /// 規格 §5.1 的規則是「任何會被寫進分片的型別」，不是特定兩個類別。
        /// 少掉任何一個，舊版建置讀入再沖寫（Flush() 是整片重寫）就會永久刪掉它不認識的欄位。
        /// 這對 KnownByEntry 尤其致命：RelationImpacts 消失會讓 §6.7.2 的冪等性失效
        /// （下次結算施加完整 delta 而非差額），且 §6.7.3 的誤會撤銷永遠無值可補回。
        /// </summary>
        [Fact]
        public void Roundtrip_PreservesUnknownFieldsOnEveryPersistedType()
        {
            string json = @"{
                ""eventId"": ""evt_0001_0001"",
                ""type"": ""test_event"",
                ""day"": 1.0,
                ""origin"": ""public"",
                ""futureOnEvent"": ""e1"",
                ""facts"": [
                    {
                        ""id"": ""f1"",
                        ""category"": ""WHO"",
                        ""text"": ""Someone"",
                        ""futureOnFact"": ""f1v""
                    }
                ],
                ""state"": {
                    ""leaked"": true,
                    ""leakedDay"": 5.0,
                    ""futureOnState"": ""s1""
                },
                ""knownBy"": [
                    {
                        ""heroId"": ""hero_1"",
                        ""hop"": 2,
                        ""futureOnKnownBy"": ""k1"",
                        ""relationImpacts"": [
                            {
                                ""aboutHeroId"": ""hero_2"",
                                ""delta"": -12,
                                ""appliedDay"": 412.0,
                                ""futureOnImpact"": ""i1""
                            }
                        ]
                    }
                ]
            }";

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);

            // 五個持久化型別都必須把未知欄位收進 Extra
            Assert.Equal("e1", evt!.Extra["futureOnEvent"].ToString());
            Assert.Equal("f1v", evt.Facts[0].Extra["futureOnFact"].ToString());
            Assert.Equal("s1", evt.State.Extra["futureOnState"].ToString());
            Assert.Equal("k1", evt.KnownBy[0].Extra["futureOnKnownBy"].ToString());

            var impact = evt.KnownBy[0].RelationImpacts![0];
            Assert.Equal(-12, impact.Delta);
            Assert.Equal("i1", impact.Extra["futureOnImpact"].ToString());

            // 沖寫（Flush 的等價操作）之後五個欄位都必須還在
            string serialized = VividJson.Write(evt);
            Assert.Contains("\"futureOnEvent\": \"e1\"", serialized);
            Assert.Contains("\"futureOnFact\": \"f1v\"", serialized);
            Assert.Contains("\"futureOnState\": \"s1\"", serialized);
            Assert.Contains("\"futureOnKnownBy\": \"k1\"", serialized);
            Assert.Contains("\"futureOnImpact\": \"i1\"", serialized);

            // 且不得產生 "extra" 這個鍵——JsonExtensionData 的內容必須攤平到父物件
            Assert.DoesNotContain("\"extra\"", serialized, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Serialize_DoesNotEmitIsVisibleToRumorSystem()
        {
            var evtPublic = new WorldEvent
            {
                EventId = "evt_0001_0001",
                Type = "test",
                Day = 1.0,
                Origin = EventOrigin.Public
            };

            var evtSecret = new WorldEvent
            {
                EventId = "evt_0001_0002",
                Type = "test",
                Day = 1.0,
                Origin = EventOrigin.Secret
            };

            string jsonPublic = VividJson.Write(evtPublic);
            string jsonSecret = VividJson.Write(evtSecret);

            Assert.DoesNotContain("isVisibleToRumorSystem", jsonPublic, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("isVisibleToRumorSystem", jsonSecret, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Deserialize_MissingV3Fields_DefaultToNullOrEmpty()
        {
            string json = @"{
                ""eventId"": ""evt_0001_0001"",
                ""type"": ""test"",
                ""day"": 1.0,
                ""facts"": [
                    {
                        ""id"": ""f1"",
                        ""category"": ""WHAT"",
                        ""text"": ""Something happened""
                    }
                ],
                ""knownBy"": [
                    {
                        ""heroId"": ""hero_1"",
                        ""hop"": 1
                    }
                ]
            }";

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);
            Assert.Equal(string.Empty, evt!.Facts[0].TextId);
            Assert.Null(evt.Facts[0].Vars);
            Assert.Null(evt.KnownBy[0].KnownFactIds);
            Assert.Null(evt.KnownBy[0].RelationImpacts);
        }

        /// <summary>
        /// 帳本 L-23。舊設定用 CamelCasePropertyNamesContractResolver，它連**字典的鍵**一起
        /// 改成 camelCase："HOST" 寫進磁碟變 "host"，讀回來就配不上碎片文字裡的 {HOST}。
        /// </summary>
        [Fact]
        public void Roundtrip_PreservesFactVarKeyCasing()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_26023_7f27",
                Type = "shared_meal",
                Day = 26023.4,
                Origin = EventOrigin.Public,
                DramaWeight = 1,
            };
            evt.Participants["host"] = "CharacterObject_2827";
            evt.Participants["guest"] = "lord_5_11";
            evt.Facts.Add(new Fact
            {
                Id = "who",
                Category = FactCategory.Who,
                TextId = "VividWorld_Fact_SharedMeal_Who",
                Text = "{HOST} shared a simple meal with {GUEST}",
                Fragility = 1,
                Vars = new Dictionary<string, string>
                {
                    ["HOST"] = "hero:CharacterObject_2827",
                    ["GUEST"] = "hero:lord_5_11",
                },
            });

            string json = VividJson.Write(evt);
            Assert.Contains("\"HOST\": \"hero:CharacterObject_2827\"", json);
            Assert.DoesNotContain("\"host\": \"hero:", json);

            var back = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(back);
            Assert.NotNull(back!.Facts[0].Vars);
            Assert.Equal("hero:CharacterObject_2827", back.Facts[0].Vars!["HOST"]);
            Assert.Equal("hero:lord_5_11", back.Facts[0].Vars!["GUEST"]);
        }

        /// <summary>屬性名還是要 camelCase——磁碟上已經有的分片靠這個讀得進來。</summary>
        [Fact]
        public void Roundtrip_StillCamelCasesPropertyNames()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "duel",
                Day = 1.0,
                DramaWeight = 3,
            };
            evt.Participants["mastermind"] = "hero_a";

            string json = VividJson.Write(evt);
            Assert.Contains("\"eventId\"", json);
            Assert.Contains("\"dramaWeight\"", json);
            Assert.DoesNotContain("\"EventId\"", json);
        }

        [Fact]
        public void Fact_Clone_DeepCopiesVarsAndExtra()
        {
            var fact = new Fact
            {
                Id = "f1",
                Category = FactCategory.Who,
                TextId = "VividWorld_Fact_Who",
                Text = "Hero {NAME}",
                Vars = new Dictionary<string, string>
                {
                    ["NAME"] = "hero:Ivan_hero_id"
                },
                Fragility = 2,
                RefersTo = "evt_0001_0000",
                Role = "mastermind",
                IsFabricated = false,
                Extra = new Dictionary<string, JToken>
                {
                    ["meta"] = new JValue("val1")
                }
            };

            var clone = fact.Clone();

            Assert.NotSame(fact, clone);
            Assert.Equal(fact.Id, clone.Id);
            Assert.Equal(fact.Category, clone.Category);
            Assert.Equal(fact.TextId, clone.TextId);
            Assert.Equal(fact.Text, clone.Text);
            Assert.Equal(fact.Fragility, clone.Fragility);
            Assert.Equal(fact.RefersTo, clone.RefersTo);
            Assert.Equal(fact.Role, clone.Role);
            Assert.Equal(fact.IsFabricated, clone.IsFabricated);

            Assert.NotNull(clone.Vars);
            Assert.NotSame(fact.Vars, clone.Vars);
            Assert.Equal("hero:Ivan_hero_id", clone.Vars!["NAME"]);

            clone.Vars["NAME"] = "hero:Boris_hero_id";
            Assert.Equal("hero:Ivan_hero_id", fact.Vars!["NAME"]);

            Assert.NotNull(clone.Extra);
            Assert.NotSame(fact.Extra, clone.Extra);
            Assert.Equal("val1", clone.Extra["meta"].ToString());

            clone.Extra["meta"] = new JValue("changed");
            Assert.Equal("val1", fact.Extra["meta"].ToString());
        }

        [Fact]
        public void RelationImpact_Source_RoundtripsCorrectly()
        {
            var impactRumor = new RelationImpact
            {
                AboutHeroId = "hero_target",
                Requested = -2.1,
                Delta = -2,
                Source = GrudgeSource.Rumor,
                AppliedDay = 10.5
            };

            string jsonRumor = VividJson.Write(impactRumor);
            Assert.Contains("\"source\": \"rumor\"", jsonRumor);

            var readRumor = VividJson.Read<RelationImpact>(jsonRumor);
            Assert.NotNull(readRumor);
            Assert.Equal(GrudgeSource.Rumor, readRumor!.Source);
            Assert.Equal(-2.1, readRumor.Requested);
            Assert.Equal(-2, readRumor.Delta);

            var impactSituation = new RelationImpact
            {
                AboutHeroId = "hero_target",
                Requested = -5.0,
                Delta = -5,
                Source = GrudgeSource.Situation,
                AppliedDay = 10.5
            };

            string jsonSituation = VividJson.Write(impactSituation);
            Assert.Contains("\"source\": \"situation\"", jsonSituation);

            var readSituation = VividJson.Read<RelationImpact>(jsonSituation);
            Assert.NotNull(readSituation);
            Assert.Equal(GrudgeSource.Situation, readSituation!.Source);
        }

        [Fact]
        public void RelationImpact_MissingSourceInJson_DefaultsToSituation()
        {
            // 旧 JSON 檔案沒有 source 欄位，反序列化時必須預設為 Situation (0 值)
            string oldImpactJson = @"{
                ""aboutHeroId"": ""lord_victim"",
                ""delta"": -6,
                ""appliedDay"": 45.0
            }";

            var read = VividJson.Read<RelationImpact>(oldImpactJson);
            Assert.NotNull(read);
            Assert.Equal(GrudgeSource.Situation, read!.Source);
            Assert.Equal("lord_victim", read.AboutHeroId);
            Assert.Equal(-6, read.Delta);
        }
    }
}

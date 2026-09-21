using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace VividWorld.Core.Persistence
{
    public static class VividJson
    {
        public static readonly JsonSerializerSettings Settings = new()
        {
            // 屬性名照樣 camelCase，但**字典的鍵原樣保留**（帳本 L-23）。
            // CamelCasePropertyNamesContractResolver 連字典鍵一起改：Fact.Vars 的 "HOST"
            // 被寫成 "host"，讀回來就配不上碎片文字裡的 {HOST}，代換不掉、遊戲的文字引擎
            // 再把留著的 {HOST} 吃成空字串 ⇒ 玩家看到「shared a simple meal with.」。
            ContractResolver      = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys  = false,
                    OverrideSpecifiedNames = true,
                },
            },
            // 檔案裡的清單**取代**預設值，不是接在後面。Json.NET 的預設是 Auto：
            // 有 getter 的集合屬性會沿用既有實例然後 Add ⇒ `commonerCompatModules` 的
            // C# 預設 ["NaN","Lowborn"] 加上檔案裡的同兩筆，變成註冊行印出來的
            // 「detected NaN, Lowborn, NaN, Lowborn」。
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting            = Formatting.Indented,
            NullValueHandling     = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters            = { new StringEnumConverter() },
        };

        public static string Write(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T? Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch
            {
                return null;
            }
        }
    }
}

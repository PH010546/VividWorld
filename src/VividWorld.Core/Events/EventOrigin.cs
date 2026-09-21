using System.Runtime.Serialization;

namespace VividWorld.Core.Events
{
    /// <summary>
    /// Secret 必須是 0 值：損毀或缺漏的 origin 會落到預設值，而預設值必須是「不揭露」。
    /// 整個 §6.4 的保密保證建立在這個欄位上，它必須 fail-closed。
    /// 嚴禁為了列舉順序好看而把 Public 排到前面。
    /// </summary>
    public enum EventOrigin
    {
        [EnumMember(Value = "secret")] Secret = 0,
        [EnumMember(Value = "public")] Public = 1,
    }
}

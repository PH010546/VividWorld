using System.Runtime.Serialization;

namespace VividWorld.Core.Events
{
    public enum FactCategory
    {
        [EnumMember(Value = "WHO")]     Who,
        [EnumMember(Value = "WHERE")]   Where,
        [EnumMember(Value = "WHEN")]    When,
        [EnumMember(Value = "WHY")]     Why,
        [EnumMember(Value = "WHAT")]    What,
        [EnumMember(Value = "CONTEXT")] Context,
        [EnumMember(Value = "OUTCOME")] Outcome,
    }
}

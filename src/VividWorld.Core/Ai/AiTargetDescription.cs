namespace VividWorld.Core.Ai
{
    public sealed class AiTargetDescription
    {
        public string TargetId { get; set; } = string.Empty;
        public string AssemblyName { get; set; } = string.Empty;
        public string ConversationTypeName { get; set; } = string.Empty;
        public string ConversationEventName { get; set; } = string.Empty;
        public string MemoryTypeName { get; set; } = string.Empty;
        public string AddMemoryMethodName { get; set; } = string.Empty;
        public string GetMemoriesMethodName { get; set; } = string.Empty;
        public string MemoryEntryTypeName { get; set; } = string.Empty;
        public string ApiVersionMemberName { get; set; } = string.Empty;
        public int AcceptedApiVersion { get; set; } = 1;
        public bool IsPersistent { get; set; } = true;

        public static AiTargetDescription CalradiaRemembers() => new AiTargetDescription
        {
            TargetId = "CalradiaRemembers",
            AssemblyName = "CalradiaRemembers",
            ConversationTypeName = "CalradiaRemembers.CrConversation",
            ConversationEventName = "Started",
            MemoryTypeName = "CalradiaRemembers.CrNpcMemory",
            AddMemoryMethodName = "AddMemory",
            GetMemoriesMethodName = "GetMemories",
            MemoryEntryTypeName = "CalradiaRemembers.CrMemoryEntry",
            ApiVersionMemberName = "ApiVersion",
            AcceptedApiVersion = 1,
            IsPersistent = true
        };

        public static AiTargetDescription ImmersiveAI() => new AiTargetDescription
        {
            TargetId = "ImmersiveAI",
            AssemblyName = "ImmersiveAI",
            ConversationTypeName = "ImmersiveAI.Interop.IaConversation",
            ConversationEventName = "Started",
            MemoryTypeName = "ImmersiveAI.Interop.IaNpcMemory",
            AddMemoryMethodName = "AddMemory",
            GetMemoriesMethodName = "GetMemories",
            MemoryEntryTypeName = "ImmersiveAI.Interop.IaMemoryEntry",
            ApiVersionMemberName = "ApiVersion",
            AcceptedApiVersion = 1,
            IsPersistent = false
        };
    }
}

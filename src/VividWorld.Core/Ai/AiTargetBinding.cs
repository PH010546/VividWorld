using System;
using System.Reflection;

namespace VividWorld.Core.Ai
{
    public sealed class AiTargetBindingResult
    {
        public bool IsEnabled { get; }
        public string Reason { get; }
        public int DetectedVersion { get; }

        public AiTargetBindingResult(bool isEnabled, string reason, int detectedVersion)
        {
            IsEnabled = isEnabled;
            Reason = reason;
            DetectedVersion = detectedVersion;
        }
    }

    public static class AiTargetValidator
    {
        public static AiTargetBindingResult Validate(AiTargetDescription desc, Assembly? assembly)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));

            if (assembly == null)
            {
                return new AiTargetBindingResult(false, $"Assembly '{desc.AssemblyName}' not found", 0);
            }

            // 1. Types
            Type? convType = assembly.GetType(desc.ConversationTypeName);
            if (convType == null)
            {
                return new AiTargetBindingResult(false, $"Type '{desc.ConversationTypeName}' not found", 0);
            }

            Type? memType = assembly.GetType(desc.MemoryTypeName);
            if (memType == null)
            {
                return new AiTargetBindingResult(false, $"Type '{desc.MemoryTypeName}' not found", 0);
            }

            Type? entryType = assembly.GetType(desc.MemoryEntryTypeName);
            if (entryType == null)
            {
                return new AiTargetBindingResult(false, $"Type '{desc.MemoryEntryTypeName}' not found", 0);
            }

            // 2. ApiVersion
            int detectedVersion = 0;
            if (!string.IsNullOrEmpty(desc.ApiVersionMemberName))
            {
                FieldInfo? verField = memType.GetField(desc.ApiVersionMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (verField != null)
                {
                    object? val = verField.GetValue(null);
                    if (val is int intVal) detectedVersion = intVal;
                }
                else
                {
                    PropertyInfo? verProp = memType.GetProperty(desc.ApiVersionMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (verProp != null)
                    {
                        object? val = verProp.GetValue(null);
                        if (val is int intVal) detectedVersion = intVal;
                    }
                    else
                    {
                        return new AiTargetBindingResult(false, $"Version member '{desc.ApiVersionMemberName}' not found on '{memType.FullName}'", 0);
                    }
                }

                if (detectedVersion != desc.AcceptedApiVersion)
                {
                    return new AiTargetBindingResult(false, $"Version mismatch: detected {detectedVersion}, accepted {desc.AcceptedApiVersion}", detectedVersion);
                }
            }

            // 3. Conversation Event
            EventInfo? ev = convType.GetEvent(desc.ConversationEventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo? addMethod = ev?.GetAddMethod(true) ?? convType.GetMethod("add_" + desc.ConversationEventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (addMethod == null)
            {
                return new AiTargetBindingResult(false, $"Event '{desc.ConversationEventName}' not found on '{convType.FullName}'", detectedVersion);
            }

            // 4. AddMemory Method
            MethodInfo? addMemMethod = memType.GetMethod(desc.AddMemoryMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (addMemMethod == null)
            {
                return new AiTargetBindingResult(false, $"Method '{desc.AddMemoryMethodName}' not found on '{memType.FullName}'", detectedVersion);
            }

            return new AiTargetBindingResult(true, "Binding successful", detectedVersion);
        }
    }
}

namespace VividWorld.Core.Config
{
    public sealed class ClampNotice
    {
        public string Key { get; set; } = string.Empty;
        public double Requested { get; set; }
        public double Applied { get; set; }
        public string AllowedRange { get; set; } = string.Empty;
    }
}

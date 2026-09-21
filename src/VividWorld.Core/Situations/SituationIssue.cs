namespace VividWorld.Core.Situations
{
    public sealed class SituationIssue
    {
        public int SituationIndex;
        public string SituationId = string.Empty;
        public string Field = string.Empty;
        public SituationIssueCode Code;
        public bool IsError;
        public string Detail = string.Empty;

        public override string ToString()
        {
            string status = IsError ? "Error" : "Warning";
            return $"[{status}] situation[{SituationIndex}] '{SituationId}' {Field} - {Code}: {Detail}";
        }
    }
}

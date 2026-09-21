namespace VividWorld.Core.Catalog
{
    public sealed class CatalogIssue
    {
        public int TemplateIndex;
        public string TemplateType = string.Empty;
        public string Field = string.Empty;
        public CatalogIssueCode Code;
        public bool IsError;
        public string Detail = string.Empty;

        public override string ToString()
        {
            string status = IsError ? "Error" : "Warning";
            return $"[{status}] template[{TemplateIndex}] '{TemplateType}' {Field} - {Code}: {Detail}";
        }
    }
}

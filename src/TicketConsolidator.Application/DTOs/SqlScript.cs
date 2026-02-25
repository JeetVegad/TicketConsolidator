namespace TicketConsolidator.Application.DTOs
{
    public class SqlScript
    {
        public string TicketNumber { get; set; }
        public string Content { get; set; }
        public ScriptType Type { get; set; }
        public string SourceFileName { get; set; }
        public string Summary { get; set; }
        public string ProcedureName { get; set; } // For Deduplication
        public DateTime SourceDate { get; set; }  // For Deduplication
    }

    public enum ScriptType
    {
        StoredProcedure,
        Data,
        Table,
        Trigger
    }
}

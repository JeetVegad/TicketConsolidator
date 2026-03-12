using System;

namespace TicketConsolidator.Application.DTOs
{
    public class ActivityEntry
    {
        public string Type { get; set; }          // "Consolidation" or "CodeReview"
        public DateTime Date { get; set; }
        public string TicketKey { get; set; }
        public string Summary { get; set; }       // "3 tickets, 12 scripts" or "2 VS, 1 DB"
        public int ItemCount { get; set; }

        // Computed display properties
        public string DateText => Date.ToString("MMM dd, HH:mm");
        public string TypeIcon => Type == "Consolidation" ? "DatabaseSync" : "EmailCheck";
    }
}

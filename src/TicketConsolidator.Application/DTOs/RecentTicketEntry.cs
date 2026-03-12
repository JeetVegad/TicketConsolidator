using System;

namespace TicketConsolidator.Application.DTOs
{
    public class RecentTicketEntry
    {
        public string TicketKey { get; set; }
        public string Summary { get; set; }
        public DateTime LastSearched { get; set; }

        public string DisplayText => $"{TicketKey} — {Summary}";
        public string DateText => LastSearched.ToString("MMM dd, HH:mm");
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace TicketConsolidator.Application.DTOs
{
    public class EmailMessage
    {
        public string Subject { get; set; }
        public string Sender { get; set; }
        public DateTime Date { get; set; }
        public List<string> AttachmentPaths { get; set; } = new List<string>();
        public List<string> MatchedTickets { get; set; } = new List<string>();
        public Dictionary<string, string> TicketSummaries { get; set; } = new Dictionary<string, string>();
    }
}

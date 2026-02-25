namespace TicketConsolidator.Application.Configurations
{
    public class JiraConfiguration
    {
        // Jira Server
        public string JiraBaseUrl { get; set; } = "https://jira.lnw.com";

        // Perforce
        public string PerforceServer { get; set; } = "";       // e.g., "ssl:perforce.company.com:1666"
        public string PerforceUser { get; set; } = "";
        public string PerforceWorkspace { get; set; } = "";
        public string DatabaseDepotPattern { get; set; } = "Database"; // Keyword for DB commits

        // Template
        public string CodeReviewTemplate { get; set; } = "";   // HTML template body

        // Tickets folder — parent folder containing subfolders named by ticket keys, each with .sql scripts
        public string TicketsFolder { get; set; } = "";
    }
}

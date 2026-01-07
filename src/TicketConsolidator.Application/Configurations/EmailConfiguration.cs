namespace TicketConsolidator.Application.Configurations
{
    public class EmailConfiguration
    {
        public string TargetFolder { get; set; } = "Inbox";
        public string EmailTemplateBody { get; set; }
    }
}

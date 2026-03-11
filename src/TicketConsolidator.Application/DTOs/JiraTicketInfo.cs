using System;
using System.Collections.Generic;

namespace TicketConsolidator.Application.DTOs
{
    public class JiraTicketInfo
    {
        public string Key { get; set; }              // "PROJ-1234"
        public string Summary { get; set; }          // Title
        public string Description { get; set; }
        public string Status { get; set; }           // "In Review"
        public string Assignee { get; set; }
        public string Url { get; set; }              // Full Jira URL
        public List<PerforceChangelist> Changelists { get; set; } = new List<PerforceChangelist>();

        // Auto-separated by depot path analysis
        public List<PerforceChangelist> VSCommits { get; set; } = new List<PerforceChangelist>();
        public List<PerforceChangelist> DBCommits { get; set; } = new List<PerforceChangelist>();

        // Swarm / remote links found on the Jira ticket
        public List<SwarmLink> SwarmLinks { get; set; } = new List<SwarmLink>();

        // Code review / self code review tickets linked via 'satisfies'
        public List<LinkedJiraTicket> CodeReviewTickets { get; set; } = new List<LinkedJiraTicket>();
    }

    public class LinkedJiraTicket
    {
        public string Key { get; set; }
        public string Summary { get; set; }
        public string Url { get; set; }
        public string Type { get; set; } // e.g. "Self Code Review" or "Code Review"
    }

    public class PerforceChangelist
    {
        public int ChangeNumber { get; set; }
        public string Description { get; set; }
        public string User { get; set; }
        public DateTime Date { get; set; }
        public List<string> AffectedFiles { get; set; } = new List<string>();
        public bool IsDatabase { get; set; }         // True if any file path contains "Database"
    }

    /// <summary>
    /// Represents a Swarm review/changelist link found in a Jira ticket's issue links or remote links.
    /// </summary>
    public class SwarmLink
    {
        public string Title { get; set; }           // Display name, e.g., "Review 12345" or "Change 67890"
        public string Url { get; set; }             // Full Swarm URL
        public string ChangeNumber { get; set; }    // Extracted changelist or review number
        public string Relationship { get; set; }    // "links to", "is related to", etc.
        public bool IsDatabase { get; set; }        // True if auto-detected as a DB commit
        public string Comment { get; set; }         // Commit description/comment 

        public string AssignedTicketKey { get; set; } // The Jira ticket this commit belongs to


        public string DisplayText 
        {
            get 
            {
                if (!string.IsNullOrWhiteSpace(Comment))
                    return $"Commit {ChangeNumber} -- {Comment}";
                return $"Commit {ChangeNumber}";
            }
        }
    }
}

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface IJiraService
    {
        /// <summary>Whether the service currently holds valid session cookies.</summary>
        bool IsAuthenticated { get; }

        /// <summary>Set cookies obtained from WebView2 browser session.</summary>
        void SetCookies(IEnumerable<Cookie> cookies);

        /// <summary>Fetch ticket details from Jira.</summary>
        Task<JiraTicketInfo> GetTicketAsync(string ticketKey);

        /// <summary>Test connection to Jira server.</summary>
        Task<bool> TestConnectionAsync();
    }
}

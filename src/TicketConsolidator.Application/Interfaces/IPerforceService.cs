using System.Collections.Generic;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface IPerforceService
    {
        /// <summary>
        /// Searches Perforce changelists whose description contains the given ticket number.
        /// </summary>
        /// <param name="ticketNumber">Jira ticket key, e.g., "PROJ-1234".</param>
        /// <returns>List of matching changelists with VS/DB classification.</returns>
        Task<List<PerforceChangelist>> GetChangelistsByTicketAsync(string ticketNumber);

        /// <summary>
        /// Tests connectivity to the Perforce server.
        /// </summary>
        Task<bool> TestConnectionAsync();
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface IConsolidationService
    {
        /// <summary>
        /// Consolidates a list of SQL scripts into a single file content string.
        /// </summary>
        /// <param name="scripts">List of scripts to merge.</param>
        /// <returns>Merged content string.</returns>
        string ConsolidateScripts(List<SqlScript> scripts);

        /// <summary>
        /// Saves consolidated content to a file.
        /// </summary>
        Task SaveConsolidatedFileAsync(string content, string outputPath);
    }
}

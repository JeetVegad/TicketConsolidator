using System.Collections.Generic;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface ISqlParserService
    {
        /// <summary>
        /// Parses the raw content of a file and extracts script blocks for a specific ticket.
        /// </summary>
        /// <param name="fileContent">The raw string content of the SQL file.</param>
        /// <param name="fileName">The name of the file (for metadata and fallback detection).</param>
        /// <param name="ticketNumber">The specific ticket number to look for.</param>
        /// <param name="sourceDate">The timestamp when the source file was received/created.</param>
        /// <returns>A list of extracted SQL Script blocks.</returns>
        List<SqlScript> ParseScript(string fileContent, string fileName, string ticketNumber, System.DateTime sourceDate);
    }
}

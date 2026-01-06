using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface IScriptValidatorService
    {
        /// <summary>
        /// Validates a single SQL script block.
        /// </summary>
        /// <param name="script">The script to validate.</param>
        /// <returns>ValidationResult containing status and messages.</returns>
        ValidationResult Validate(SqlScript script);
    }
}

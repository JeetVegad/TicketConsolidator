using System;
using System.Text.RegularExpressions;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class ScriptValidatorService : IScriptValidatorService
    {
        public ValidationResult Validate(SqlScript script)
        {
            var result = new ValidationResult { IsValid = true };

            if (script == null)
            {
                result.IsValid = false;
                result.Errors.Add("Script object is null.");
                return result;
            }

            // 1. Check Empty Content
            if (string.IsNullOrWhiteSpace(script.Content))
            {
                result.IsValid = false;
                result.Errors.Add($"Ticket {script.TicketNumber}: Script content is empty.");
            }

            // 2. Check for GO statement
            // T-SQL scripts often require GO to separate batches. 
            // Warning if missing, assuming it might be needed for SPs.
            if (!Regex.IsMatch(script.Content, @"\bGO\b", RegexOptions.IgnoreCase))
            {
                result.Warnings.Add($"Ticket {script.TicketNumber}: Missing 'GO' statement. Ensure batch separation if required.");
                // Not strict error, as single statements don't strictly need it, but consolidation usually benefits from it.
            }

            // 3. Basic Syntax Checks (Very rudimentary)
            // Check for unclosed comments, missing brackets? 
            // Too complex for Regex, sticking to requirement "basic SQL syntax issues".
            // Let's check for suspended transactions? Not easily without parser.
            
            // Check for use? 
            if (Regex.IsMatch(script.Content, @"\bUSE\s+\[?master\]?", RegexOptions.IgnoreCase))
            {
                 result.Warnings.Add($"Ticket {script.TicketNumber}: specific 'USE database' statement found. This might override target deployment DB.");
            }

            return result;
        }
    }
}

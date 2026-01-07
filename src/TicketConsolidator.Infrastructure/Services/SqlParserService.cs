using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class SqlParserService : ISqlParserService
    {
        public List<SqlScript> ParseScript(string fileContent, string fileName, string ticketNumber)
        {
            var scripts = new List<SqlScript>();
            if (string.IsNullOrWhiteSpace(fileContent)) return scripts;

            // Strategy 1: Regex for Explicit Blocks (Relaxed)
            // Matches: <Ticket>START ... <Ticket>END
            // Also supports: [Ticket] START, Ticket START, Ticket-START
            string safeTicket = Regex.Escape(ticketNumber);
            // Allow: Optional < or [, Ticket, Optional > or ], Optional spacer (-, _), START
            // PLUS: Optional "PRINT '... #" prefix and "...';" suffix to handle previously consolidated files cleanly
            string prefix = @"(?:PRINT\s+'[-]*\s*[#]?)?"; 
            string suffix = @"(?:[-]*';)?";

            string startPattern = $@"{prefix}(?:<|\[)?{safeTicket}(?:>|\])?[\s-_]*START{suffix}";
            string endPattern = $@"{prefix}(?:<|\[)?{safeTicket}(?:>|\])?[\s-_]*END{suffix}";
            
            string pattern = $@"{startPattern}(.*?)(?:{endPattern}|$)";
            
            var matches = Regex.Matches(fileContent, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string content = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        scripts.Add(new SqlScript
                        {
                            TicketNumber = ticketNumber,
                            Content = content,
                            SourceFileName = fileName,
                            Type = DetectScriptType(content, fileName)
                        });
                    }
                }
            }
            else
            {
                // Strategy 2: Check for "Consolidated" format (Recursive Consolidation Support)
                // Format: /* Ticket: {TicketNumber} | Source: {SourceFileName} */
                
                // Regex to match the Ticket Header
                var headerRegex = new Regex(@"/\*\s*Ticket:\s*(.*?)(?:\s*\|\s*Source:\s*(.*?))?\s*\*/", RegexOptions.IgnoreCase);
                var headerMatches = headerRegex.Matches(fileContent);

                if (headerMatches.Count > 0)
                {
                    for (int i = 0; i < headerMatches.Count; i++)
                    {
                        var currentMatch = headerMatches[i];
                        string extractedTicket = currentMatch.Groups[1].Value.Trim();
                        string extractedSource = currentMatch.Groups[2].Success ? currentMatch.Groups[2].Value.Trim() : fileName;

                        // Content starts after this match + the closing separator line "/* ===... */"
                        // But finding the exact end of the separator is tricky if we rely on rigid format.
                        // Let's assume content starts after the header match index + length, 
                        // and we trim the separator manually if present.
                        
                        int startIdx = currentMatch.Index + currentMatch.Length;
                        int endIdx = (i == headerMatches.Count - 1) 
                            ? fileContent.Length 
                            : headerMatches[i + 1].Index; // Stop at next header

                        string rawBlock = fileContent.Substring(startIdx, endIdx - startIdx);
                        
                        // Cleanup: Output usually has "/* ===... */" immediately after "Ticket: ... */"
                        // And usually has "/* ===... */" immediately BEFORE the next header (which is captured in rawBlock at the end?)
                        // No, the previous header was "/* Ticket: ... */".
                        // Our ConsolidationService generator:
                        // line 1: ===
                        // line 2: Ticket: ...  <-- We matched this
                        // line 3: ===
                        
                        // So 'rawBlock' starts immediately after "Ticket: ... */". 
                        // It definitely contains the closing "/* ==== */" of the current header block.
                        
                        // Let's use a robust cleanup: removing the Separator lines from start/end.
                        // Separator Regex: /\* =+ \*/
                        string cleanContent = Regex.Replace(rawBlock, @"/\*\s*=+\s*\*/", "").Trim();
                        // Also remove "GO" if it was added solely for consolidation separation? 
                        // ConsolidationService adds "GO" if loose.
                        // But users might have "GO" in their script.
                        // Better to keep "GO" to ensure validity.

                        if (!string.IsNullOrWhiteSpace(cleanContent))
                        {
                            scripts.Add(new SqlScript
                            {
                                TicketNumber = extractedTicket,
                                Content = cleanContent,
                                SourceFileName = extractedSource,
                                Type = DetectScriptType(cleanContent, extractedSource)
                            });
                        }
                    }
                }
            }

            return scripts;
        }

        private ScriptType DetectScriptType(string content, string fileName)
        {
            // 1. Check filename hints first (highest reliability if provided)
            // Use Regex to handle various delimiters (_, -, ., space) and case insensitivity
            if (Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(SP|StoredProcedure)(?:[_\-\.\s]|$)", RegexOptions.IgnoreCase) ||
                fileName.EndsWith(".sp.sql", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptType.StoredProcedure;
            }

            if (Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Data|Insert)(?:[_\-\.\s]|$)", RegexOptions.IgnoreCase) ||
                fileName.EndsWith(".data.sql", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptType.Data;
            }

            if (Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Trigger|Trg)(?:[_\-\.\s]|$)", RegexOptions.IgnoreCase) ||
                fileName.EndsWith(".trigger.sql", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".trg.sql", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptType.Trigger;
            }

            // 2. Content Analysis
            string upperContent = content.ToUpperInvariant();
            if (Regex.IsMatch(upperContent, @"\bCREATE\s+PROCEDURE\b") || 
                Regex.IsMatch(upperContent, @"\bALTER\s+PROCEDURE\b") ||
                Regex.IsMatch(upperContent, @"\bCREATE\s+Or\s+ALTER\s+PROCEDURE\b"))
            {
                return ScriptType.StoredProcedure;
            }

            if (Regex.IsMatch(upperContent, @"\bCREATE\s+TRIGGER\b") || 
                Regex.IsMatch(upperContent, @"\bALTER\s+TRIGGER\b") ||
                Regex.IsMatch(upperContent, @"\bCREATE\s+Or\s+ALTER\s+TRIGGER\b"))
            {
                return ScriptType.Trigger;
            }

            if (Regex.IsMatch(upperContent, @"\bINSERT\s+INTO\b") || 
                Regex.IsMatch(upperContent, @"\bUPDATE\b") || 
                Regex.IsMatch(upperContent, @"\bDELETE\s+FROM\b") ||
                Regex.IsMatch(upperContent, @"\bMERGE\b"))
            {
                return ScriptType.Data;
            }

            // Default fallback
            return ScriptType.Table;
        }
    }
}

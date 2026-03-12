using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class SqlParserService : ISqlParserService
    {
        public List<SqlScript> ParseScript(string fileContent, string fileName, string ticketNumber, DateTime sourceDate)
        {
            var scripts = new List<SqlScript>();
            if (string.IsNullOrWhiteSpace(fileContent)) return scripts;

            // Strategy 1: Regex for Explicit Blocks (Relaxed)
            string safeTicket = Regex.Escape(ticketNumber);
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
                        var type = DetectScriptType(content, fileName);
                        scripts.Add(new SqlScript
                        {
                            TicketNumber = ticketNumber,
                            Content = content,
                            SourceFileName = fileName,
                            Type = type,
                            SourceDate = sourceDate,
                            ProcedureName = ExtractProcedureName(content, type)
                        });
                    }
                }
            }
            else
            {
                // Strategy 2: Check for "Consolidated" format
                var headerRegex = new Regex(@"/\*\s*Ticket:\s*(.*?)(?:\s*\|\s*Source:\s*(.*?))?\s*\*/", RegexOptions.IgnoreCase);
                var headerMatches = headerRegex.Matches(fileContent);

                if (headerMatches.Count > 0)
                {
                    for (int i = 0; i < headerMatches.Count; i++)
                    {
                        var currentMatch = headerMatches[i];
                        string extractedTicket = currentMatch.Groups[1].Value.Trim();
                        string extractedSource = currentMatch.Groups[2].Success ? currentMatch.Groups[2].Value.Trim() : fileName;
                        
                        int startIdx = currentMatch.Index + currentMatch.Length;
                        int endIdx = (i == headerMatches.Count - 1) 
                            ? fileContent.Length 
                            : headerMatches[i + 1].Index;

                        string rawBlock = fileContent.Substring(startIdx, endIdx - startIdx);
                        string cleanContent = Regex.Replace(rawBlock, @"/\*\s*=+\s*\*/", "").Trim();

                        if (!string.IsNullOrWhiteSpace(cleanContent))
                        {
                            var type = DetectScriptType(cleanContent, extractedSource);
                            scripts.Add(new SqlScript
                            {
                                TicketNumber = extractedTicket,
                                Content = cleanContent,
                                SourceFileName = extractedSource,
                                Type = type,
                                SourceDate = sourceDate, // Inherit from file/email date
                                ProcedureName = ExtractProcedureName(cleanContent, type)
                            });
                        }
                    }
                }
            }

            return scripts;
        }

        private string ExtractProcedureName(string content, ScriptType type)
        {
            if (type != ScriptType.StoredProcedure) return null;

            try
            {
                // Matches: CREATE/ALTER PROCEDURE [Schema].[Name] OR [Name]
                // 1. CREATE OR ALTER | CREATE | ALTER
                // 2. PROCEDURE | PROC
                // 3. Name capture
                var regex = new Regex(@"\b(?:CREATE|ALTER)\s+(?:OR\s+ALTER\s+)?(?:PROCEDURE|PROC)\s+(?:\[?[\w@#$]+\]?\.\[?)?(\[?[\w@#$]+\]?)", RegexOptions.IgnoreCase);
                var match = regex.Match(content);
                if (match.Success)
                {
                    // Return cleaner name (remove brackets if needed, specialized logic later if strict)
                    string rawName = match.Groups[1].Value;
                    return rawName.Replace("[", "").Replace("]", "").Trim();
                }
            }
            catch { }
            return null;
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

using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class ConsolidationService : IConsolidationService
    {
        public string ConsolidateScripts(List<SqlScript> scripts)
        {
            var sb = new StringBuilder();
            
            // 1. SPECIFIC HEADER
            sb.AppendLine("SET XACT_ABORT ON;");
            sb.AppendLine("GO");
            sb.AppendLine();
            sb.AppendLine("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;");
            sb.AppendLine("GO");
            sb.AppendLine();
            sb.AppendLine("BEGIN TRANSACTION T1;");
            sb.AppendLine("GO");
            sb.AppendLine();
            sb.AppendLine("-----------------------------------------------");
            sb.AppendLine("SET ANSI_NULLS ON;");
            sb.AppendLine("GO");
            sb.AppendLine();
            sb.AppendLine("SET QUOTED_IDENTIFIER ON;");
            sb.AppendLine("GO");
            sb.AppendLine();



            foreach (var script in scripts)
            {
                // CLEAN CONTENT: Remove existing Header/Footer AND existing Print Wrappers
                string cleanedContent = StripHeaderFooter(script.Content);

                // Check if cleaned content is effectively empty
                if (string.IsNullOrWhiteSpace(cleanedContent))
                {
                    continue; // Skip empty scripts
                }

                // 2. TICKET WRAPPER (START) - INJECTED FRESH
                sb.AppendLine("GO");
                sb.AppendLine($"PRINT '-------------------- #{script.TicketNumber} START -------------------------';");
                sb.AppendLine("GO");
                sb.AppendLine();

                sb.AppendLine(cleanedContent);
                sb.AppendLine();
                
                // Ensure GO is present between content
                if (!cleanedContent.TrimEnd().EndsWith("GO", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("GO");
                }

                // 2. TICKET WRAPPER (END) - INJECTED FRESH
                sb.AppendLine("GO");
                sb.AppendLine($"PRINT '-------------------- #{script.TicketNumber} END -------------------------';");
                sb.AppendLine();
            }

            // 3. SPECIFIC FOOTER
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("--FOOTER, DO NOT MODIFY");
            sb.AppendLine("IF @@ERROR <> 0");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    PRINT 'The database update failed';");
            sb.AppendLine("    PRINT 'ROLLING BACK CHANGES';");
            sb.AppendLine();
            sb.AppendLine("    ROLLBACK TRANSACTION T1;");
            sb.AppendLine("END;");
            sb.AppendLine("ELSE");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    PRINT 'The database update succeeded';");
            sb.AppendLine();
            sb.AppendLine("    COMMIT TRANSACTION T1;");
            sb.AppendLine();
            sb.AppendLine("    PRINT 'EXECUTE VERSIONING SP';");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @Version VARCHAR(15);");
            sb.AppendLine("    DECLARE @Database VARCHAR(15);");
            sb.AppendLine();
            sb.AppendLine("    SET @Version = 'xxxxx';");
            sb.AppendLine("    SET @Database = 'HALOCOREDB';");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("--EXEC dbo.usp_VersionUpdate @Version, @Database;");
            sb.AppendLine("END;");
            sb.AppendLine("GO");

            return sb.ToString();
        }

        private string StripHeaderFooter(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;

            // 1. Remove Header: "SET XACT_ABORT ON ... SET QUOTED_IDENTIFIER ON; GO"
            string headerPattern = @"SET\s+XACT_ABORT\s+ON;.*?SET\s+QUOTED_IDENTIFIER\s+ON;\s*GO";
            content = Regex.Replace(content, headerPattern, "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 2. Remove Footer: "--FOOTER, DO NOT MODIFY ... END; GO"
            string footerPattern = @"-{20,}[\r\n]+--FOOTER, DO NOT MODIFY.*?GO\s*$"; 
            content = Regex.Replace(content, footerPattern, "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 3. Remove Existing PRINT START/END Wrappers (Safe Cleanup)
            // Matches: PRINT '...#...START...'; AND PRINT '...#...END...';
            // Handles potential malformed/truncated lines if they match the general pattern
            string printStartPattern = @"(?:^|[\r\n])\s*PRINT\s+'[-#\w\s]+START.*?';?(\s*GO)?";
            content = Regex.Replace(content, printStartPattern, "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            string printEndPattern = @"(?:^|[\r\n])\s*PRINT\s+'[-#\w\s]+END.*?';?(\s*GO)?";
            content = Regex.Replace(content, printEndPattern, "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return content.Trim();
        }

        public async Task SaveConsolidatedFileAsync(string content, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(outputPath, content);
        }
    }
}

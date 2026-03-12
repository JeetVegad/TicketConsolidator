using Microsoft.AspNetCore.Components.Forms; // For IBrowserFile
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;
using TicketConsolidator.Infrastructure.Services; // For SettingsService

namespace TicketConsolidator.Web.Services
{
    public class TicketService
    {
        private readonly IEmailService _emailService;
        private readonly ISqlParserService _parserService;
        private readonly IScriptValidatorService _validatorService;
        private readonly IConsolidationService _consolidationService;
        private readonly ILoggerService _logger;
        private readonly SettingsService _settingsService;
        private readonly ToastService _toastService;

        // State Validation & UI Updates
        public event Action OnChange;
        public event Action<string> OnConsolidationSuccess;
        private void NotifyStateChanged() => OnChange?.Invoke();

        // State Properties
        public bool IsBusy { get; private set; }
        public string StatusMessage { get; private set; } = "Idle";
        public int ProgressValue { get; private set; }
        
        // Data Collections
        public List<SqlScript> TableScripts { get; private set; } = new List<SqlScript>();
        public List<SqlScript> SpScripts { get; private set; } = new List<SqlScript>();
        public List<SqlScript> DataScripts { get; private set; } = new List<SqlScript>();
        public List<SqlScript> TriggerScripts { get; private set; } = new List<SqlScript>();

        // Metadata
        public string LastConsolidatedPath { get; private set; }
        public string LastRunId { get; private set; }
        public string TicketsCount { get; private set; } = "0/0";

        private CancellationTokenSource _cancellationTokenSource;

        public TicketService(
            IEmailService emailService,
            ISqlParserService parserService,
            IScriptValidatorService validatorService,
            IConsolidationService consolidationService,
            ILoggerService logger,
            SettingsService settingsService,
            ToastService toastService)
        {
            _emailService = emailService;
            _parserService = parserService;
            _validatorService = validatorService;
            _consolidationService = consolidationService;
            _logger = logger;
            _settingsService = settingsService;
            _toastService = toastService;
        }

        public void Clear()
        {
            TableScripts.Clear();
            SpScripts.Clear();
            DataScripts.Clear();
            TriggerScripts.Clear();
            StatusMessage = "Idle";
            ProgressValue = 0;
            TicketsCount = "0/0";
            LastConsolidatedPath = null;
            LastRunId = null;
            NotifyStateChanged();
        }

        public async Task StartScanAsync(string ticketInput)
        {
            if (string.IsNullOrWhiteSpace(ticketInput)) return;
            if (IsBusy) return;

            IsBusy = true;
            NotifyStateChanged();

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                StatusMessage = "Initializing Scan...";
                ProgressValue = 5;
                NotifyStateChanged();

                var value = EasterEgg(ticketInput);
                if (value != null)
                {
                    ProgressValue = 0;
                    _toastService.ShowSuccess(value);
                    NotifyStateChanged();
                    return;
                }

                string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logger.StartSession($"Scan Run [ID: {runId}]");

                 var tickets = ticketInput.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(t => t.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                // 1. Scan Emails
                StatusMessage = "Scanning Emails...";
                _logger.LogInfo($"Starting email scan for {tickets.Count} tickets...");
                ProgressValue = 15;
                NotifyStateChanged();

                string folderName = _settingsService.CurrentTargetFolder ?? "Inbox";
                var emails = await _emailService.GetEmailsByTicketNumbersAsync(tickets, folderName, token);

                ProgressValue = 40;
                StatusMessage = $"Found {emails.Count} emails. Parsing...";
                _logger.LogInfo($"Found {emails.Count} matching emails. Parsing attachments...");
                NotifyStateChanged();

                // Missing Tickets Check
                var foundTickets = new HashSet<string>(emails.SelectMany(e => e.MatchedTickets), StringComparer.OrdinalIgnoreCase);
                var missingTickets = tickets.Except(foundTickets).ToList();
                if (missingTickets.Any())
                {
                    _logger.LogWarning($"Missing Tickets: {string.Join(", ", missingTickets)}");
                }
                else
                {
                    _logger.LogSuccess("All tickets found in emails.");
                }

                // 2. Parse Scripts
                // We use a simplified logic here compared to WPF: we just add valid scripts.
                // Duplicate checking logic can be added later if needed.
                
                var newScripts = new List<SqlScript>();

                await Task.Run(() => 
                {
                    foreach (var email in emails)
                    {
                        if (token.IsCancellationRequested) break;
                        
                        foreach (var path in email.AttachmentPaths)
                        {
                            if (System.IO.File.Exists(path))
                            {
                                string content = System.IO.File.ReadAllText(path);
                                string fileName = System.IO.Path.GetFileName(path);
                                
                                // Determine Real Ticket (Regex Upgrade)
                                string ticketInputMatch = tickets.FirstOrDefault(t => fileName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (ticketInputMatch == null) continue;

                                string realTicket = ticketInputMatch;
                                var ticketRegexMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"([A-Za-z]+-\d+|\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (ticketRegexMatch.Success) 
                                {
                                    string found = ticketRegexMatch.Value;
                                    if (found.IndexOf(ticketInputMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        realTicket = found;
                                    }
                                }
                                
                                // LOGGING: Granular File Processing
                                _logger.LogInfo($"Processing {fileName} for Ticket {realTicket}...");

                                // Parse
                                var parsed = _parserService.ParseScript(content, fileName, realTicket, email.Date);
                                
                                // Fallback
                                if (parsed.Count == 0)
                                {
                                    _logger.LogWarning($"No script blocks found in {fileName}. Treating as whole file.");
                                    parsed.Add(new SqlScript 
                                    { 
                                        TicketNumber = realTicket, 
                                        Content = content, 
                                        SourceFileName = fileName,
                                        Type = DetectScriptType(fileName)
                                    });
                                }
                                else
                                {
                                    _logger.LogInfo($"Parsed {parsed.Count} blocks from {fileName}.");
                                }

                                // Apply Summary
                                string summary = email.TicketSummaries != null && email.TicketSummaries.TryGetValue(realTicket, out var s) ? s : "No Summary Found";
                                foreach(var sc in parsed) sc.Summary = summary;

                                newScripts.AddRange(parsed);
                            }
                        }
                    }
                });

                ProgressValue = 80;
                StatusMessage = "Updating List...";
                NotifyStateChanged();

                // Add to Collections
                foreach(var script in newScripts)
                {
                    switch(script.Type)
                    {
                        case ScriptType.Table: TableScripts.Add(script); break;
                        case ScriptType.StoredProcedure: SpScripts.Add(script); break;
                        case ScriptType.Data: DataScripts.Add(script); break;
                        case ScriptType.Trigger: TriggerScripts.Add(script); break;
                    }
                }

                ProgressValue = 100;
                TicketsCount = $"{foundTickets.Count}/{tickets.Count}";
                StatusMessage = $"Scan Complete. Loaded {newScripts.Count} scripts.";
                _logger.LogSuccess($"Scan complete. Loaded {newScripts.Count} scripts total.");
                
                if (newScripts.Count > 0) 
                    _toastService.ShowSuccess($"Scan Complete! Found {newScripts.Count} scripts.");
                else 
                    _toastService.ShowWarning("Scan completed but no scripts were found.");

                NotifyStateChanged();

            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogError($"Scan Error: {ex.Message}");
                _toastService.ShowError($"Scan Failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        public async Task ConsolidateAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Consolidating...";
            NotifyStateChanged();

            try
            {
                var allScripts = new List<SqlScript>();
                allScripts.AddRange(SpScripts);
                allScripts.AddRange(DataScripts);
                allScripts.AddRange(TriggerScripts);
                allScripts.AddRange(TableScripts);

                string baseDir = _settingsService.ConsolidatedScriptsPath;
                string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputDir = System.IO.Path.Combine(baseDir, $"Run_{runId}");

                if (!System.IO.Directory.Exists(outputDir)) System.IO.Directory.CreateDirectory(outputDir);

                LastConsolidatedPath = outputDir;
                LastRunId = runId;

                // Group by Context & Logic from ViewModel
                var contextGroups = allScripts.GroupBy(s => GetScriptContext(s));

                foreach (var group in contextGroups)
                {
                    string context = group.Key;
                    var dataScripts = group.Where(s => s.Type == ScriptType.Data || s.Type == ScriptType.Table).ToList();
                    // SP Deduplication Logic for this context group
                    // 1. Group by Ticket
                    // 2. Group by ProcedureName
                    // 3. Pick Latest
                    
                    var spScriptsRaw = group.Where(s => s.Type == ScriptType.StoredProcedure).ToList();
                    var spScripts = new List<SqlScript>();

                    if (spScriptsRaw.Any())
                    {
                        var loopTickets = spScriptsRaw.GroupBy(s => s.TicketNumber);
                        foreach(var ticketGroup in loopTickets)
                        {
                            var procGroups = ticketGroup.GroupBy(s => s.ProcedureName);
                            foreach(var procGroup in procGroups)
                            {
                                if (string.IsNullOrEmpty(procGroup.Key))
                                {
                                    // No proc name extracted? Keep all to be safe (or latest? Safe is keep all)
                                    spScripts.AddRange(procGroup);
                                }
                                else
                                {
                                    // Deduplicate: Keep Latest
                                    var winner = procGroup.OrderByDescending(s => s.SourceDate).First();
                                    spScripts.Add(winner);

                                    if (procGroup.Count() > 1)
                                    {
                                        var dropped = procGroup.Count() - 1;
                                        _logger.LogWarning($"Deduped SP [{procGroup.Key}] for Ticket {ticketGroup.Key}. Kept {winner.SourceDate}, dropped {dropped} older versions.");
                                    }
                                }
                            }
                        }
                    }

                    if (dataScripts.Any()) 
                        await ProcessConsolidationGroup(dataScripts, "DATA", outputDir, context, "01");
                    
                    if (spScripts.Any())
                        await ProcessConsolidationGroup(spScripts, "SP", outputDir, context, "02");
                    
                    var triggerScripts = group.Where(s => s.Type == ScriptType.Trigger).ToList();
                    if (triggerScripts.Any())
                        await ProcessConsolidationGroup(triggerScripts, "TRIGGER", outputDir, context, "03");
                }

                StatusMessage = "Consolidation Complete!";
                NotifyStateChanged();
                OnConsolidationSuccess?.Invoke(outputDir);
                _toastService.ShowSuccess("Consolidation Successful! Files saved.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Consolidation Failed: {ex.Message}";
                NotifyStateChanged();
                _toastService.ShowError($"Consolidation Failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        private async Task ProcessConsolidationGroup(List<SqlScript> scripts, string typeSuffix, string outputDir, string context, string prefix)
        {
            string fileName = $"{prefix}_{context}_{typeSuffix}.sql";
            string contextDir = System.IO.Path.Combine(outputDir, context);
            if (!System.IO.Directory.Exists(contextDir)) System.IO.Directory.CreateDirectory(contextDir);

            string fullPath = System.IO.Path.Combine(contextDir, fileName);
            string content = _consolidationService.ConsolidateScripts(scripts);
            
            await _consolidationService.SaveConsolidatedFileAsync(content, fullPath);
        }

        public async Task CreateReleaseEmailAsync(string buildNum, string solPath, string userName)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Creating Release Email...";
            NotifyStateChanged();

            try
            {
                 var allScripts = TableScripts.Concat(SpScripts).Concat(TriggerScripts).Concat(DataScripts).ToList();
                 var uniqueTickets = allScripts.Select(s => s.TicketNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                 // Summary Map
                 var summaryMap = new Dictionary<string, string>();
                 foreach(var t in uniqueTickets)
                 {
                     var s = allScripts.FirstOrDefault(x => x.TicketNumber == t && !string.IsNullOrEmpty(x.Summary) && !x.Summary.StartsWith("Manual"));
                     if (s == null) s = allScripts.FirstOrDefault(x => x.TicketNumber == t);
                     summaryMap[t] = s?.Summary ?? "No Summary Found";
                 }

                 // Get Files
                 string[] files = System.IO.Directory.GetFiles(LastConsolidatedPath, "*.*", System.IO.SearchOption.AllDirectories);
                 var fileList = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                 var attachmentPaths = files.ToList();

                 // LOAD TEMPLATE
                 string template = _settingsService.EmailTemplate;
                 if (string.IsNullOrWhiteSpace(template)) template = "<html><body>No Template Configured.</body></html>";

                 // PREPARE DATA
                 var sbFiles = new System.Text.StringBuilder();
                 foreach(var f in fileList) sbFiles.AppendLine($"<li style='font-family:Calibri,sans-serif;font-size:11pt'>{f}</li>");

                 var sbDetails = new System.Text.StringBuilder();
                 foreach(var t in uniqueTickets)
                 {
                     sbDetails.AppendLine("<tr>");
                     sbDetails.AppendLine($"<td style='padding:4px'><b>{t.ToUpperInvariant()}</b></td>");
                     sbDetails.AppendLine($"<td style='padding:4px'>{summaryMap[t]}</td>");
                     sbDetails.AppendLine("</tr>");
                 }

                 // REPLACE PLACEHOLDERS
                 string finalBody = template
                     .Replace("{BuildNumber}", buildNum)
                     .Replace("{SolutionPath}", solPath)
                     .Replace("{FileList}", sbFiles.ToString())
                     .Replace("{ReleaseDetails}", sbDetails.ToString())
                     .Replace("{UserName}", userName);

                 await _emailService.CreateDraftEmailAsync(
                     $"Product Release Notification [Build {buildNum}]", 
                     finalBody, 
                     attachmentPaths);

                 StatusMessage = "Email Draft Created.";
                 _logger.LogSuccess("Release Email Draft created.");
                 _toastService.ShowSuccess("Email Draft Created Successfully in Outlook!");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Email Failed: {ex.Message}";
                _logger.LogError($"Email Failed: {ex.Message}");
                _toastService.ShowError($"Email Failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        public async Task UploadFilesAsync(List<string> filePaths)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Processing uploaded files...";
            NotifyStateChanged();

            try 
            {
                var newScripts = new List<SqlScript>();
                
                await Task.Run(() => 
                {
                    foreach (var path in filePaths)
                    {
                        if (!System.IO.File.Exists(path)) continue;
                        
                        string fileName = System.IO.Path.GetFileName(path);
                        string content = System.IO.File.ReadAllText(path);

                        // 1. Detect Type
                        var scriptType = DetectScriptType(fileName);

                        // 2. Parse (Consolidated check or regular)
                        var ticketNumber = "Manual"; 
                        if (fileName.StartsWith("Consolidated", StringComparison.OrdinalIgnoreCase))
                            ticketNumber = "ConsolidatedScript";
                        else
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"([A-Za-z]+-\d+|\d{3,})");
                            if (match.Success) ticketNumber = match.Value;
                        }

                        // Parse
                        var parsed = _parserService.ParseScript(content, fileName, ticketNumber, DateTime.Now);

                        if (parsed.Count == 0)
                        {
                            parsed.Add(new SqlScript
                            {
                                TicketNumber = ticketNumber,
                                Content = content,
                                SourceFileName = fileName,
                                Type = scriptType,
                                Summary = "Manual Upload"
                            });
                        }

                        newScripts.AddRange(parsed);
                    }
                });

                // Add to Collections
                foreach(var script in newScripts)
                {
                    switch(script.Type)
                    {
                         case ScriptType.Table: TableScripts.Add(script); break;
                         case ScriptType.StoredProcedure: SpScripts.Add(script); break;
                         case ScriptType.Data: DataScripts.Add(script); break;
                         case ScriptType.Trigger: TriggerScripts.Add(script); break;
                    }
                }
                
                StatusMessage = "Upload Complete.";
                _logger.LogSuccess($"Processed {filePaths.Count} files.");
            }
            catch(Exception ex)
            {
                StatusMessage = $"Upload Error: {ex.Message}";
                _logger.LogError($"Upload Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        // Helper Methods
        private ScriptType DetectScriptType(string fileName)
        {
             if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(SP|StoredProcedure)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                 fileName.EndsWith(".sp.sql", StringComparison.OrdinalIgnoreCase))
             {
                 return ScriptType.StoredProcedure;
             }
             if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Data|Insert)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                 fileName.EndsWith(".data.sql", StringComparison.OrdinalIgnoreCase))
             {
                 return ScriptType.Data;
             }
             if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Trigger|Trg)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                 fileName.EndsWith(".trigger.sql", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith(".trg.sql", StringComparison.OrdinalIgnoreCase))
             {
                 return ScriptType.Trigger;
             }
             return ScriptType.Table;
        }

        private string GetScriptContext(SqlScript script)
        {
             if (string.IsNullOrWhiteSpace(script.SourceFileName)) return "HALOCOREDB";

             string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(script.SourceFileName);
             string cleanName = fileNameNoExt;
             if (!string.IsNullOrEmpty(script.TicketNumber))
             {
                 cleanName = cleanName.Replace(script.TicketNumber, "", StringComparison.OrdinalIgnoreCase);
             }

             var parts = cleanName.Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
             var ignoredKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
             {
                 "SP", "Data", "Table", "Tables", "Tb", "Tbl", "StoredProcedure", "Insert", "Update", "Script", "Stored", "Procedure", "Engage", "Ticket",
                 "CR", "PR", "INC", "TASK", "US", "Bug", "Feat", "Feature", "Fix", "Hotfix", "Trigger", "Trg", "Triggers"
             };

             var contextParts = parts.Where(p => !ignoredKeywords.Contains(p) && !p.All(char.IsDigit)).ToList();
             if (contextParts.Count == 0) return "HALOCOREDB";

             return string.Join("_", contextParts).ToUpperInvariant();
        }

        public static string EasterEgg(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            if (GetDeterministicHashCode(input) == -612463164)
            {
               return ShowEasterEgg();
            }
            return null;
        }

        private static string ShowEasterEgg()
        {
            try
            {
                byte[] msgBytes = new byte[] {
                    0x64, 0x65, 0x76, 0x65, 0x6C, 0x6F, 0x70, 0x65, 0x64, 0x20, 0x62, 0x79, 0x20, 0x4B, 0x4D
                };
                string msg = Encoding.UTF8.GetString(msgBytes);
                return msg;
            }
            catch
            {
                return null; 
            }
        }

        private static int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;
using TicketConsolidator.Infrastructure.Services;

namespace TicketConsolidator.UI.Views.Dialogs
{
    public class InternalReleaseDialogViewModel : INotifyPropertyChanged
    {
        public JiraTicketInfo Ticket { get; }
        private readonly IEmailService _emailService;
        private readonly SettingsService _settingsService;
        private readonly ILoggerService _logger;

        public InternalReleaseDialogViewModel(
            JiraTicketInfo ticket, 
            string defaultArtifact,
            IEmailService emailService, 
            SettingsService settingsService,
            ILoggerService logger)
        {
            Ticket = ticket;
            _emailService = emailService;
            _settingsService = settingsService;
            _logger = logger;

            ImpactedArtifact = "";
            
            // Try to set coder to the assignee automatically
            Coder = !string.IsNullOrWhiteSpace(ticket.Assignee) ? ticket.Assignee : "Krish Maniar";

            GenerateDraftCommand = new RelayCommand(async o => await GenerateDraft(o), o => true);
        }

        private string _reviewer = "";
        public string Reviewer
        {
            get => _reviewer;
            set { _reviewer = value; OnPropertyChanged(); }
        }

        private string _taskDescription = "";
        public string TaskDescription
        {
            get => _taskDescription;
            set { _taskDescription = value; OnPropertyChanged(); }
        }

        private string _resolution = "";
        public string Resolution
        {
            get => _resolution;
            set { _resolution = value; OnPropertyChanged(); }
        }

        private string _impactedArtifact = "";
        public string ImpactedArtifact
        {
            get => _impactedArtifact;
            set { _impactedArtifact = value; OnPropertyChanged(); }
        }

        private string _coder = "";
        public string Coder
        {
            get => _coder;
            set { _coder = value; OnPropertyChanged(); }
        }
        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand GenerateDraftCommand { get; }

        private async Task GenerateDraft(object dialogHost)
        {
            try
            {
                string subject = $"Product Release Notification: {Ticket.Key}"; // No title requested by user
                
                // Get local attachments if the Tickets folder is configured
                var emailAttachments = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(_settingsService.TicketsFolder) && System.IO.Directory.Exists(_settingsService.TicketsFolder))
                {
                    // Find actual directory in case it has suffix like "ticket-123 task name"
                    var directories = System.IO.Directory.GetDirectories(_settingsService.TicketsFolder, $"*{Ticket.Key}*");
                    string folderPath = directories.FirstOrDefault();
                    
                    if (!string.IsNullOrWhiteSpace(folderPath) && System.IO.Directory.Exists(folderPath))
                    {
                        var files = System.IO.Directory.GetFiles(folderPath);
                        emailAttachments.AddRange(files.Where(f => 
                            f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || 
                            f.Contains("UT", StringComparison.OrdinalIgnoreCase)));
                    }
                }

                bool hasUTDocument = Ticket.HasUTDocument || emailAttachments.Any(a => a.Contains("UT", StringComparison.OrdinalIgnoreCase));
                string utAttachedStr = hasUTDocument ? "Yes" : "No";

                bool hasSelfReview = Ticket.CodeReviewTickets.Any(t => t.Type.Contains("Self", StringComparison.OrdinalIgnoreCase));
                bool hasPeerReview = Ticket.CodeReviewTickets.Any(t => t.Type.Contains("Code Review", StringComparison.OrdinalIgnoreCase) && !t.Type.Contains("Self", StringComparison.OrdinalIgnoreCase));
                string selfCodeReviewStatus = hasSelfReview ? "Yes" : "NA";
                string codeReviewDefectStatus = hasPeerReview ? "Yes" : "NA";

                // Inspect SQL files
                bool dbCommitDone = false;
                bool dataScriptApplicable = false;
                var attachmentTypes = new System.Collections.Generic.List<string>();

                var allSqlFiles = emailAttachments.Where(a => a.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList();
                var jiraSqlFiles = Ticket.Attachments.Where(a => a.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList();
                allSqlFiles.AddRange(jiraSqlFiles);

                foreach (var sqlFile in allSqlFiles.Distinct())
                {
                    string filename = System.IO.Path.GetFileNameWithoutExtension(sqlFile);
                    int lastUnderscore = filename.LastIndexOf('_');
                    
                    if (lastUnderscore >= 0 && lastUnderscore < filename.Length - 1)
                    {
                        string typeSuffix = filename.Substring(lastUnderscore + 1);
                        
                        // Capitalize first letter of suffix if possible
                        if (typeSuffix.Length > 0)
                            typeSuffix = char.ToUpper(typeSuffix[0]) + typeSuffix.Substring(1).ToLower();

                        if (typeSuffix.Equals("Data", StringComparison.OrdinalIgnoreCase))
                        {
                            dataScriptApplicable = true;
                            if (!attachmentTypes.Contains("Data script", StringComparer.OrdinalIgnoreCase))
                                attachmentTypes.Add("Data script");
                        }
                        else
                        {
                            dbCommitDone = true;
                            string displayType = typeSuffix + " script";
                            if (typeSuffix.Equals("Sp", StringComparison.OrdinalIgnoreCase)) displayType = "SP script";

                            if (!attachmentTypes.Contains(displayType, StringComparer.OrdinalIgnoreCase))
                                attachmentTypes.Add(displayType);
                        }
                    }
                    else
                    {
                        dbCommitDone = true;
                        if (!attachmentTypes.Contains("Data script", StringComparer.OrdinalIgnoreCase))
                            attachmentTypes.Add("Data script");
                    }
                }

                string dbCommitDoneStr = dbCommitDone || Ticket.DBCommits.Count > 0 ? "Yes" : "NA";
                string dataScriptApplicableStr = dataScriptApplicable ? "Yes" : "NA";
                string dbConfigStr = (dbCommitDone || dataScriptApplicable || Ticket.DBCommits.Count > 0) ? "Yes" : "NA";

                // Update ImpactedArtifact
                string inferredArtifacts = string.Join(", ", attachmentTypes);
                string finalImpactedArtifact = string.IsNullOrWhiteSpace(ImpactedArtifact) 
                    ? inferredArtifacts 
                    : (string.IsNullOrWhiteSpace(inferredArtifacts) ? ImpactedArtifact : $"{inferredArtifacts}, {ImpactedArtifact}");

                // Generate Attachments HTML
                string attachmentsHtml = "<ul>\n";
                if (hasUTDocument) attachmentsHtml += $"  <li>UT Document – attached</li>\n";
                foreach (var atype in attachmentTypes) attachmentsHtml += $"  <li>{atype} – attached ({Ticket.Key})</li>\n";
                if (!hasUTDocument && attachmentTypes.Count == 0) attachmentsHtml += "  <li>None</li>\n";
                attachmentsHtml += "</ul>";

                string templateHtml = _settingsService.InternalReleaseTemplate;
                if (string.IsNullOrWhiteSpace(templateHtml))
                    templateHtml = SettingsService.DefaultInternalReleaseTemplate;

                // Replace placeholders
                string htmlBody = templateHtml
                    .Replace("{TicketKey}", Ticket.Key)
                    .Replace("{TicketSummary}", Ticket.Summary ?? "")
                    .Replace("{TicketUrl}", Ticket.Url ?? "")
                    .Replace("{TaskDescription}", TaskDescription ?? "")
                    .Replace("{Resolution}", Resolution ?? "")
                    .Replace("{ImpactedArtifact}", finalImpactedArtifact)
                    .Replace("{Coder}", Coder ?? "")
                    .Replace("{Reviewer}", Reviewer ?? "")
                    .Replace("{IsUTAttached}", utAttachedStr)
                    .Replace("{DbConfiguration}", dbConfigStr)
                    .Replace("{UserName}", Environment.UserName)
                    .Replace("{SelfCodeReviewStatus}", selfCodeReviewStatus)
                    .Replace("{CodeReviewDefectStatus}", codeReviewDefectStatus)
                    .Replace("{DbCommitDoneStatus}", dbCommitDoneStr)
                    .Replace("{DataScriptApplicableStatus}", dataScriptApplicableStr)
                    .Replace("{AttachmentsListHtml}", attachmentsHtml);

                await _emailService.CreateDraftEmailAsync(
                    subject: subject,
                    htmlBody: htmlBody,
                    attachmentPaths: emailAttachments.Count > 0 ? emailAttachments : null
                );

                _logger.LogSuccess($"Drafted Internal Release email for {Ticket.Key}");
                // Close dialog
                DialogHost.Close("RootDialog");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to generate draft: {ex.Message}";
                _logger.LogError(ErrorMessage);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;
using TicketConsolidator.Infrastructure.Services;
using TicketConsolidator.UI.Views;
using TicketConsolidator.UI.Views.Dialogs;

namespace TicketConsolidator.UI
{
    public class CodeReviewViewModel : INotifyPropertyChanged
    {
        private readonly IJiraService _jiraService;
        private readonly IEmailService _emailService;
        private readonly ILoggerService _logger;
        private readonly JiraConfiguration _config;
        private readonly SettingsService _settingsService;


        public CodeReviewViewModel(
            IJiraService jiraService,
            IPerforceService p4Service,
            IEmailService emailService,
            ILoggerService logger,
            JiraConfiguration config,
            SettingsService settingsService)
        {
            _jiraService = jiraService;
            _emailService = emailService;
            _logger = logger;
            _config = config;
            _settingsService = settingsService;


            LoginCommand = new RelayCommand(o => ShowBrowser(), o => !IsLoading);
            SearchCommand = new RelayCommand(async o => await ExecuteSearch(), o => !IsLoading && _jiraService.IsAuthenticated);
            DraftEmailCommand = new RelayCommand(async o => await ExecuteDraftEmail(), o => Ticket != null && !IsLoading);
            AssignVSCommand = new RelayCommand(o => AssignLink(o, "VS"), o => true);
            AssignDBCommand = new RelayCommand(o => AssignLink(o, "DB"), o => true);
            RemoveVSCommand = new RelayCommand(o => RemoveAssignment(o, "VS"), o => true);
            RemoveDBCommand = new RelayCommand(o => RemoveAssignment(o, "DB"), o => true);

        }

        #region Properties

        private string _ticketKey = "";
        public string TicketKey
        {
            get => _ticketKey;
            set { _ticketKey = value; OnPropertyChanged(); }
        }



        public string JiraLoginUrl => _config.JiraBaseUrl.TrimEnd('/') + "/login.jsp";

        public bool IsAuthenticated => _jiraService.IsAuthenticated;

        private bool _isBrowserVisible;
        public bool IsBrowserVisible
        {
            get => _isBrowserVisible;
            set { _isBrowserVisible = value; OnPropertyChanged(); }
        }

        private string _authStatus = "Not logged in — click LOGIN to authenticate via Jira";
        public string AuthStatus
        {
            get => _authStatus;
            set { _authStatus = value; OnPropertyChanged(); }
        }

        private JiraTicketInfo _ticket;
        public JiraTicketInfo Ticket
        {
            get => _ticket;
            set { _ticket = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTicket)); }
        }

        public bool HasTicket => Ticket != null;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        // All links found from Jira (unassigned ones)
        private ObservableCollection<SwarmLink> _foundLinks = new();
        public ObservableCollection<SwarmLink> FoundLinks
        {
            get => _foundLinks;
            set { _foundLinks = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFoundLinks)); }
        }
        public bool HasFoundLinks => FoundLinks?.Count > 0;

        // User-assigned VS commits
        private ObservableCollection<SwarmLink> _vsCommits = new();
        public ObservableCollection<SwarmLink> VSCommits
        {
            get => _vsCommits;
            set { _vsCommits = value; OnPropertyChanged(); }
        }

        // User-assigned DB commits
        private ObservableCollection<SwarmLink> _dbCommits = new();
        public ObservableCollection<SwarmLink> DBCommits
        {
            get => _dbCommits;
            set { _dbCommits = value; OnPropertyChanged(); }
        }

        // Selected items for email
        private SwarmLink _selectedVSCommit;
        public SwarmLink SelectedVSCommit
        {
            get => _selectedVSCommit;
            set { _selectedVSCommit = value; OnPropertyChanged(); }
        }

        private SwarmLink _selectedDBCommit;
        public SwarmLink SelectedDBCommit
        {
            get => _selectedDBCommit;
            set { _selectedDBCommit = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand LoginCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand DraftEmailCommand { get; }
        public ICommand AssignVSCommand { get; }
        public ICommand AssignDBCommand { get; }
        public ICommand RemoveVSCommand { get; }
        public ICommand RemoveDBCommand { get; }


        #endregion

        #region Browser Login

        private void ShowBrowser()
        {
            _logger.LogInfo("Opening Jira login window.");
            AuthStatus = "Opening Jira login window...";

            var loginUrl = JiraLoginUrl;
            var cookieBaseUrl = _config.JiraBaseUrl;

            var loginWindow = new JiraLoginWindow(loginUrl, cookieBaseUrl);

            // Set owner to the application main window
            if (System.Windows.Application.Current.MainWindow != null)
                loginWindow.Owner = System.Windows.Application.Current.MainWindow;

            var result = loginWindow.ShowDialog();

            if (result == true && loginWindow.ExtractedCookies != null)
            {
                _jiraService.SetCookies(loginWindow.ExtractedCookies);

                IsBrowserVisible = false;
                AuthStatus = "Connected to Jira ✓";
                OnPropertyChanged(nameof(IsAuthenticated));
                CommandManager.InvalidateRequerySuggested();

                _logger.LogSuccess("Jira session extracted from login window.");
            }
            else
            {
                AuthStatus = "Login cancelled — click LOGIN to try again";
                _logger.LogInfo("Jira login cancelled by user.");
            }
        }

        public void HideBrowser()
        {
            IsBrowserVisible = false;
        }

        #endregion

        #region Assign Links

        private void AssignLink(object param, string type)
        {
            if (param is SwarmLink link)
            {
                FoundLinks.Remove(link);
                OnPropertyChanged(nameof(HasFoundLinks));

                if (type == "VS")
                {
                    VSCommits.Add(link);
                    _logger.LogInfo($"Assigned '{link.Title}' as VS Commit.");
                }
                else
                {
                    DBCommits.Add(link);
                    _logger.LogInfo($"Assigned '{link.Title}' as DB Commit.");
                }

                if (SelectedVSCommit == null && VSCommits.Count > 0) SelectedVSCommit = VSCommits[0];
                if (SelectedDBCommit == null && DBCommits.Count > 0) SelectedDBCommit = DBCommits[0];
            }
        }

        private void RemoveAssignment(object param, string type)
        {
            if (param is SwarmLink link)
            {
                if (type == "VS")
                    VSCommits.Remove(link);
                else
                    DBCommits.Remove(link);

                FoundLinks.Add(link);
                OnPropertyChanged(nameof(HasFoundLinks));
                _logger.LogInfo($"Unassigned '{link.Title}' from {type} Commits.");
            }
        }

        #endregion

        #region Search / Fetch Data

        private async Task ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(TicketKey)) return;

            IsLoading = true;
            HasError = false;
            StatusMessage = "Fetching ticket from Jira...";
            Ticket = null;
            FoundLinks.Clear();
            VSCommits.Clear();
            DBCommits.Clear();
            OnPropertyChanged(nameof(HasFoundLinks));

            try
            {
                try
                {
                    Ticket = await _jiraService.GetTicketAsync(TicketKey.Trim().ToUpperInvariant());
                }
                catch (UnauthorizedAccessException)
                {
                    HasError = true;
                    AuthStatus = "Session expired — click LOGIN again";
                    StatusMessage = "Session expired. Please log in again.";
                    OnPropertyChanged(nameof(IsAuthenticated));
                    CommandManager.InvalidateRequerySuggested();
                    IsLoading = false;
                    return;
                }

                // Populate found links from Jira for manual assignment
                if (Ticket.SwarmLinks?.Count > 0)
                {
                    FoundLinks = new ObservableCollection<SwarmLink>(Ticket.SwarmLinks);
                    OnPropertyChanged(nameof(HasFoundLinks));
                    StatusMessage = $"Found {Ticket.Key} with {Ticket.SwarmLinks.Count} linked commit(s). Assign them below.";
                }
                else
                {
                    StatusMessage = $"Found {Ticket.Key} — no linked commits found.";
                }

                HasError = false;
                _logger.LogSuccess($"Code Review: fetched {Ticket.Key} with {Ticket.SwarmLinks?.Count ?? 0} links.");


            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = ex.Message;
                _logger.LogError($"Code Review search failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Classifies a Swarm link as "DB" or "VS" based on the commit description
        /// found in the link Title. If the description contains DB-related keywords
        /// (e.g., "DB", "database", "sql", "stored proc"), it's a DB commit.
        /// Everything else defaults to VS.
        /// </summary>
        private string ClassifyCommitLink(SwarmLink link)
        {
            // The Title from Jira issue links contains the commit description
            // e.g., "CHANGE-12345 - [DB] Updated stored procedure sp_GetUsers"
            var text = $"{link.Title} {link.Url}".ToLowerInvariant();

            // DB indicators — common terms found in database commit descriptions
            string[] dbKeywords = { "db", "database", "sql", "stored proc",
                                    "script", "migration", "schema", "trigger",
                                    "table", "alter ", "create proc", "sp_",
                                    "function", "view ", "index " };

            bool isDb = dbKeywords.Any(k => text.Contains(k));

            return isDb ? "DB" : "VS";
        }

        #endregion

        #region Draft Email

        private async Task ExecuteDraftEmail()
        {
            if (Ticket == null) return;

            IsLoading = true;
            StatusMessage = "Creating email draft...";

            try
            {
                string template = !string.IsNullOrEmpty(_config.CodeReviewTemplate)
                    ? _config.CodeReviewTemplate
                    : DefaultCodeReviewTemplate;

                // Scan tickets folder for SQL scripts and _UT documents
                List<string> allAttachments = new();
                bool hasDataScript = false;
                string ticketsFolder = _settingsService.TicketsFolder;

                _logger.LogInfo($"Tickets folder setting: '{ticketsFolder}'");

                if (string.IsNullOrWhiteSpace(ticketsFolder))
                {
                    // Show warning dialog — folder not configured
                    _logger.LogWarning("Tickets folder is not configured.");
                    await DialogHost.Show(new InfoDialog(
                        "Tickets folder is not configured.\nGo to Settings \u2192 Tickets Folder to set the path.\n\nEmail will be drafted without attachments.",
                        "Folder Not Configured", isWarning: true), "RootDialog");
                }
                else if (!Directory.Exists(ticketsFolder))
                {
                    _logger.LogWarning($"Tickets folder does not exist: {ticketsFolder}");
                }
                else
                {
                    var allDirs = Directory.GetDirectories(ticketsFolder);
                    _logger.LogInfo($"Found {allDirs.Length} subfolder(s) in tickets folder.");

                    // Try multiple matching strategies
                    string ticketKey = Ticket.Key;
                    string ticketDir = null;

                    // 1. Exact match
                    ticketDir = allDirs.FirstOrDefault(d =>
                        Path.GetFileName(d).Equals(ticketKey, StringComparison.OrdinalIgnoreCase));

                    // 2. Folder starts with ticket key (e.g., "PROJ-1234_some_description")
                    if (ticketDir == null)
                    {
                        ticketDir = allDirs.FirstOrDefault(d =>
                            Path.GetFileName(d).StartsWith(ticketKey, StringComparison.OrdinalIgnoreCase));
                    }

                    // 3. Folder contains ticket key anywhere
                    if (ticketDir == null)
                    {
                        ticketDir = allDirs.FirstOrDefault(d =>
                            Path.GetFileName(d).IndexOf(ticketKey, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    if (ticketDir != null)
                    {
                        _logger.LogInfo($"Matched folder: {ticketDir}");

                        // Collect SQL files
                        var sqlFiles = Directory.GetFiles(ticketDir, "*.sql", SearchOption.AllDirectories);
                        allAttachments.AddRange(sqlFiles);

                        foreach (var f in sqlFiles)
                            _logger.LogInfo($"  → SQL file: {Path.GetFileName(f)}");

                        // Check if any file ends with _data.sql (case insensitive)
                        hasDataScript = sqlFiles.Any(f =>
                            Path.GetFileName(f).EndsWith("_data.sql", StringComparison.OrdinalIgnoreCase));

                        // Collect _UT Word documents (.doc, .docx)
                        var allFiles = Directory.GetFiles(ticketDir, "*.*", SearchOption.AllDirectories);
                        var utDocs = allFiles.Where(f =>
                        {
                            var name = Path.GetFileName(f).ToLowerInvariant();
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            return name.Contains("_ut") && (ext == ".doc" || ext == ".docx");
                        }).ToArray();

                        allAttachments.AddRange(utDocs);

                        foreach (var f in utDocs)
                            _logger.LogInfo($"  → UT doc: {Path.GetFileName(f)}");

                        _logger.LogSuccess($"Found {sqlFiles.Length} SQL files and {utDocs.Length} UT doc(s) in {Path.GetFileName(ticketDir)}" +
                            (hasDataScript ? " (data script detected)" : ""));
                    }
                    else
                    {
                        // Log what folders exist so user can see the mismatch
                        var folderNames = allDirs.Select(d => Path.GetFileName(d)).Take(10);
                        _logger.LogWarning($"No folder matching '{ticketKey}' found. Existing folders: {string.Join(", ", folderNames)}");
                    }
                }

                string dbScriptAnswer = hasDataScript ? "Yes" : "NA";

                // Build commit rows for ALL VS and DB commits
                string vsCommitRows = "";
                if (VSCommits.Count > 0)
                {
                    vsCommitRows = string.Join(", ", VSCommits.Select(c =>
                        $"<a href='{c.Url}'>{c.ChangeNumber}</a>"));
                }
                else
                {
                    vsCommitRows = "NA";
                }

                string dbCommitRows = "";
                if (DBCommits.Count > 0)
                {
                    dbCommitRows = string.Join(", ", DBCommits.Select(c =>
                        $"<a href='{c.Url}'>{c.ChangeNumber}</a>"));
                }
                else
                {
                    dbCommitRows = "NA";
                }

                string body = template
                    .Replace("{TicketKey}", Ticket.Key ?? "")
                    .Replace("{TicketTitle}", Ticket.Summary ?? "")
                    .Replace("{TicketUrl}", Ticket.Url ?? "")
                    .Replace("{TicketDescription}", Ticket.Description ?? "")
                    .Replace("{TicketStatus}", Ticket.Status ?? "")
                    .Replace("{Assignee}", Ticket.Assignee ?? "")
                    .Replace("{VSCommitNumber}", vsCommitRows)
                    .Replace("{VSCommitUrl}", SelectedVSCommit?.Url ?? "#")
                    .Replace("{DBCommitNumber}", dbCommitRows)
                    .Replace("{DBCommitUrl}", SelectedDBCommit?.Url ?? "#")
                    .Replace("{HasDataScript}", dbScriptAnswer)
                    .Replace("{UserName}", Environment.UserName)
                    .Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd"));

                string subject = $"Code Review: #{Ticket.Key}";
                await _emailService.CreateDraftEmailAsync(subject, body, allAttachments.Count > 0 ? allAttachments : null);

                string attachMsg = allAttachments.Count > 0
                    ? $" with {allAttachments.Count} attachment(s)"
                    : "";
                StatusMessage = $"Email draft created{attachMsg}!";
                _logger.LogSuccess($"Code Review email drafted for {Ticket.Key}{attachMsg}");


                await DialogHost.Show(new InfoDialog(
                    $"Code Review email draft has been created in Outlook{attachMsg}.",
                    "Email Created"), "RootDialog");
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Email failed: {ex.Message}";
                _logger.LogError($"Code Review email failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private const string DefaultCodeReviewTemplate = @"<html>
<body style='font-family:Calibri,sans-serif; font-size:11pt'>

  <p>Hi Team,</p>

  <p>I've included the ticket's changeset details below for your review.</p>

  <!-- Changeset Details Table -->
  <table border='1' cellpadding='6' cellspacing='0'
         style='border-collapse:collapse; font-family:Calibri; font-size:11pt'>
    <tr style='background:#4472C4; color:white'>
      <th>Ticket</th>
      <th>Change Set</th>
    </tr>
    <tr>
      <td rowspan='2'>
        Ticket No:- <a href='{TicketUrl}'>{TicketKey} - {TicketTitle}</a>
      </td>
      <td>VS Commit: {VSCommitNumber}</td>
    </tr>
    <tr>
      <td>DB Commit: {DBCommitNumber}</td>
    </tr>
    <tr>
      <td rowspan='2'>Self-Code review Ticket No: NA</td>
      <td>VS Commit: NA</td>
    </tr>
    <tr>
      <td>DB Commit: NA</td>
    </tr>
  </table>

  <br/>

  <!-- DB Review Checklist -->
  <p><b>DB Review Checklist:-</b></p>

  <table border='1' cellpadding='6' cellspacing='0'
         style='border-collapse:collapse; font-family:Calibri; font-size:11pt'>
    <tr style='background:#4472C4; color:white'>
      <th>Sr. No.</th>
      <th>Name</th>
      <th>Is the point considered during development</th>
    </tr>
    <tr>
      <td>1</td>
      <td>I have used meaningful variable and method names.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>2</td>
      <td>I have enhanced the names of existing variables/methods based on a better understanding of the requirements.</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>3</td>
      <td>I am not adding any unnecessary extra files, code/commented code.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>4</td>
      <td>I am not committing any confidential information.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>5</td>
      <td>I have followed the ""single responsibility principle"".</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>6</td>
      <td>I have identified Unit Test Scenarios for the feature and at least documented them informally.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>7</td>
      <td>I have added meaningful logs for the new code. I have also added exception handling in the code.</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>8</td>
      <td>I have added the DB script of the config/data used. (Attach the DB script for data)</td>
      <td>{HasDataScript}</td>
    </tr>
    <tr>
      <td>9</td>
      <td>Is the requirement/issue fully understood?</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>10</td>
      <td>Is Self-code review done using Co-pilot?</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>11</td>
      <td>Has the co-pilot code review defect been raised?</td>
      <td>NA</td>
    </tr>
  </table>

  <br/>

  <p>Best Regards,<br/>{UserName}</p>

</body>
</html>";

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

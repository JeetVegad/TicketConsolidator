using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
    public class InternalReleaseViewModel : INotifyPropertyChanged
    {
        private readonly IJiraService _jiraService;
        private readonly IEmailService _emailService;
        private readonly ILoggerService _logger;
        private readonly JiraConfiguration _config;
        private readonly SettingsService _settingsService;

        public InternalReleaseViewModel(
            IJiraService jiraService,
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

            var savedCookies = _settingsService.LoadJiraSession();
            if (savedCookies != null && savedCookies.Any())
            {
                // We do not need to call SetCookies here if it was already called, 
                // but since these are Singletons, it might be called twice on startup.
                // However SetCookies is safe to call twice.
                if (!_jiraService.IsAuthenticated)
                {
                    _jiraService.SetCookies(savedCookies);
                }
                AuthStatus = "Connected to Jira ✓";
            }

            _jiraService.AuthenticationStatusChanged += OnAuthenticationStatusChanged;

            SearchCommand = new RelayCommand(async o => await ExecuteSearch(), o => !IsLoading && _jiraService.IsAuthenticated);
            OpenDraftDialogCommand = new RelayCommand(async o => await OpenDraftDialog(), o => Ticket != null && !IsLoading);
            LoginCommand = new RelayCommand(o => ShowBrowser(), o => !IsLoading && !_jiraService.IsAuthenticated);
        }

        private void OnAuthenticationStatusChanged()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AuthStatus = _jiraService.IsAuthenticated ? "Connected to Jira ✓" : "Not logged in — click LOGIN to authenticate via Jira";
                OnPropertyChanged(nameof(IsAuthenticated));
                CommandManager.InvalidateRequerySuggested();
            });
        }

        public string JiraLoginUrl => _config.JiraBaseUrl.TrimEnd('/') + "/login.jsp";

        private async void ShowBrowser()
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
                var netCookies = loginWindow.ExtractedCookies.Select(sc => new System.Net.Cookie
                {
                    Name = sc.Name,
                    Value = sc.Value,
                    Path = sc.Path,
                    Domain = sc.Domain,
                    Secure = sc.IsSecure,
                    HttpOnly = sc.IsHttpOnly
                }).ToList();

                _jiraService.SetCookies(netCookies);
                await _settingsService.SaveJiraSessionAsync(loginWindow.ExtractedCookies);

                _logger.LogSuccess("Jira session extracted from login window and saved reliably for today.");
            }
            else
            {
                AuthStatus = "Login cancelled — click LOGIN to try again";
                _logger.LogInfo("Jira login cancelled by user.");
            }
        }

        private string _ticketKey = "";
        public string TicketKey
        {
            get => _ticketKey;
            set { _ticketKey = value; OnPropertyChanged(); }
        }

        public bool IsAuthenticated => _jiraService.IsAuthenticated;

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
            set { _ticket = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        public ICommand SearchCommand { get; }
        public ICommand OpenDraftDialogCommand { get; }
        public ICommand LoginCommand { get; }

        private async Task ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(TicketKey))
            {
                ShowError("Please enter a ticket key.");
                return;
            }

            string runId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logger.StartSession($"Internal Release Search [ID: {runId}] - Ticket: {TicketKey.Trim().ToUpperInvariant()}");

            try
            {
                IsLoading = true;
                HasError = false;
                ErrorMessage = "";
                Ticket = null;

                var ticketInfo = await _jiraService.GetTicketAsync(TicketKey.Trim());
                
                // Automatically categorize commits to help with 'Impacted Artifact'
                if (ticketInfo.SwarmLinks != null)
                {
                    foreach (var link in ticketInfo.SwarmLinks)
                    {
                        var isDb = link.Relationship?.Contains("db") == true ||
                                   link.Title?.Contains("db", StringComparison.OrdinalIgnoreCase) == true ||
                                   link.Comment?.Contains("db", StringComparison.OrdinalIgnoreCase) == true;

                        if (isDb) ticketInfo.DBCommits.Add(new PerforceChangelist { ChangeNumber = int.TryParse(link.ChangeNumber, out var cn) ? cn : 0 });
                        else ticketInfo.VSCommits.Add(new PerforceChangelist { ChangeNumber = int.TryParse(link.ChangeNumber, out var cn2) ? cn2 : 0 });
                    }
                }

                Ticket = ticketInfo;
            }
            catch (UnauthorizedAccessException)
            {
                HasError = true;
                _jiraService.ClearCookies();
                await _settingsService.ClearJiraSessionAsync();
                ErrorMessage = "Session expired. Please log in again.";
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                IsLoading = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task OpenDraftDialog()
        {
            string runId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logger.StartSession($"Internal Release Draft [ID: {runId}] - Ticket: {Ticket?.Key}");

            // Determine default Impacted Artifact based on commits
            string defaultArtifact = "No Artifacts Detected";
            if (Ticket.DBCommits.Count > 0 && Ticket.VSCommits.Count == 0)
                defaultArtifact = "Data Script";
            else if (Ticket.VSCommits.Count > 0 && Ticket.DBCommits.Count == 0)
                defaultArtifact = "Visual Studio Code";
            else if (Ticket.DBCommits.Count > 0 && Ticket.VSCommits.Count > 0)
                defaultArtifact = "Code and Data Script";

            // Open the dialog
            var dialogViewModel = new InternalReleaseDialogViewModel(Ticket, defaultArtifact, _emailService, _settingsService, _logger);
            var dialog = new InternalReleaseDialog
            {
                DataContext = dialogViewModel
            };

            await DialogHost.Show(dialog, "RootDialog");
        }

        private void ShowError(string message)
        {
            HasError = true;
            ErrorMessage = message;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

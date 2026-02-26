using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using TicketConsolidator.Infrastructure.Services;

namespace TicketConsolidator.UI
{
    public class TemplateEditorViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _debounceTimer;

        public TemplateEditorViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;

            // Debounce timer for live preview (300ms)
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                UpdatePreview();
            };

            // Initialize with Release template
            _selectedTemplateType = "Release Email";
            Document = new TextDocument(GetTemplateOrFormattedDefault(
                _settingsService.EmailTemplate, SettingsService.DefaultEmailTemplate));
            Document.TextChanged += OnDocumentTextChanged;

            // Commands
            SaveCommand = new RelayCommand(ExecuteSave);
            ResetCommand = new RelayCommand(ExecuteReset);
            InsertPlaceholderCommand = new RelayCommand(ExecuteInsertPlaceholder);

            // Initial preview
            UpdatePreview();
        }

        // ── Document ────────────────────────────────────────────────
        public TextDocument Document { get; private set; }

        /// <summary>Caret offset set by the View code-behind so placeholder insertion works.</summary>
        public int CaretOffset { get; set; }

        // ── Template Type Selector ──────────────────────────────────
        private string _selectedTemplateType;
        public string SelectedTemplateType
        {
            get => _selectedTemplateType;
            set
            {
                if (_selectedTemplateType == value) return;
                _selectedTemplateType = value;
                OnPropertyChanged();
                LoadTemplate();
            }
        }

        public string[] TemplateTypes { get; } = { "Release Email", "Code Review Email" };

        private bool IsCodeReview => SelectedTemplateType == "Code Review Email";

        // ── Placeholder chips (shown in the UI) ─────────────────────
        private string[] _availablePlaceholders = ReleasePlaceholders;

        public string[] AvailablePlaceholders
        {
            get => _availablePlaceholders;
            private set { _availablePlaceholders = value; OnPropertyChanged(); }
        }

        private static readonly string[] ReleasePlaceholders =
            { "{BuildNumber}", "{SolutionPath}", "{FileList}", "{ReleaseDetails}", "{UserName}" };

        private static readonly string[] CodeReviewPlaceholders =
            { "{TicketKey}", "{TicketTitle}", "{TicketUrl}", "{VSCommitNumber}", "{DBCommitNumber}", "{HasDataScript}", "{UserName}", "{Date}" };

        // ── Preview HTML (bound to WebView2 via code-behind) ────────
        private string _previewHtml = "";
        public string PreviewHtml
        {
            get => _previewHtml;
            private set { _previewHtml = value; OnPropertyChanged(); }
        }

        // ── Status bar ──────────────────────────────────────────────
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // ── Commands ────────────────────────────────────────────────
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand InsertPlaceholderCommand { get; }

        // ── Event for preview navigation (consumed by code-behind) ──
        public event Action<string> PreviewRequested;

        // ── Private helpers ─────────────────────────────────────────

        private void LoadTemplate()
        {
            Document.TextChanged -= OnDocumentTextChanged;

            if (IsCodeReview)
            {
                Document.Text = GetTemplateOrFormattedDefault(
                    _settingsService.CodeReviewTemplate,
                    SettingsService.DefaultCodeReviewTemplate);
                AvailablePlaceholders = CodeReviewPlaceholders;
            }
            else
            {
                Document.Text = GetTemplateOrFormattedDefault(
                    _settingsService.EmailTemplate,
                    SettingsService.DefaultEmailTemplate);
                AvailablePlaceholders = ReleasePlaceholders;
            }

            Document.TextChanged += OnDocumentTextChanged;
            UpdatePreview();
        }

        /// <summary>
        /// Returns the formatted default if the saved template is empty or is an
        /// unformatted version of the same default. Otherwise returns the saved template as-is.
        /// </summary>
        private static string GetTemplateOrFormattedDefault(string saved, string formattedDefault)
        {
            if (string.IsNullOrWhiteSpace(saved))
                return formattedDefault;

            // Strip all whitespace and compare — if they match, the saved one is just
            // the old unformatted version of the same default, so upgrade it.
            var savedNorm = System.Text.RegularExpressions.Regex.Replace(saved, @"\s+", "");
            var defaultNorm = System.Text.RegularExpressions.Regex.Replace(formattedDefault, @"\s+", "");

            if (string.Equals(savedNorm, defaultNorm, StringComparison.OrdinalIgnoreCase))
                return formattedDefault;

            // User has customized the template — keep their version
            return saved;
        }

        private void OnDocumentTextChanged(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void UpdatePreview()
        {
            string html = Document.Text;

            if (string.IsNullOrWhiteSpace(html))
            {
                PreviewHtml = "<html><body style='font-family:Calibri;padding:20px;color:#888'><p>No template content. Start typing or click RESET to load the default.</p></body></html>";
                PreviewRequested?.Invoke(PreviewHtml);
                return;
            }

            // Replace placeholders with sample data for preview
            if (IsCodeReview)
            {
                html = html
                    .Replace("{TicketKey}", "ENGAGE-12345")
                    .Replace("{TicketTitle}", "Sample ticket — Fix login timeout issue")
                    .Replace("{TicketUrl}", "https://jira.example.com/browse/ENGAGE-12345")
                    .Replace("{TicketDescription}", "Users experience session timeout during peak hours.")
                    .Replace("{TicketStatus}", "In Development")
                    .Replace("{Assignee}", Environment.UserName)
                    .Replace("{VSCommitNumber}", "<a href='#'>CS-9901</a>, <a href='#'>CS-9902</a>")
                    .Replace("{VSCommitUrl}", "#")
                    .Replace("{DBCommitNumber}", "<a href='#'>CS-8801</a>")
                    .Replace("{DBCommitUrl}", "#")
                    .Replace("{HasDataScript}", "Yes")
                    .Replace("{UserName}", Environment.UserName)
                    .Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd"));
            }
            else
            {
                html = html
                    .Replace("{BuildNumber}", "1.0.0-PREVIEW")
                    .Replace("{SolutionPath}", @"C:\Release\Build_20260225")
                    .Replace("{FileList}", "<li style='font-family:Calibri'>01_Tables.sql</li><li style='font-family:Calibri'>02_StoredProcedures.sql</li><li style='font-family:Calibri'>03_Data.sql</li>")
                    .Replace("{ReleaseDetails}", "<tr><td>ENGAGE-123</td><td>Fix login timeout issue</td></tr><tr><td>ENGAGE-456</td><td>Add user preferences page</td></tr>")
                    .Replace("{UserName}", Environment.UserName);
            }

            PreviewHtml = WrapInWhiteBackground(html);
            PreviewRequested?.Invoke(PreviewHtml);
        }

        /// <summary>
        /// Wraps HTML in a white-background body so the preview is always readable,
        /// regardless of the app's dark mode setting. Emails render on white in real clients.
        /// </summary>
        private static string WrapInWhiteBackground(string html)
        {
            // If the HTML already has a full <html> tag, inject a style override
            if (html.Contains("<body", StringComparison.OrdinalIgnoreCase))
            {
                // Wrap the entire thing in an iframe-like container via a meta wrapper
                return $@"<html><head><style>
                    html, body {{ background: #ffffff !important; color: #222 !important; margin: 8px; }}
                </style></head><body>{html}</body></html>";
            }

            return $@"<html><head><style>
                html, body {{ background: #ffffff !important; color: #222 !important; margin: 8px; }}
            </style></head><body>{html}</body></html>";
        }

        private async void ExecuteSave(object obj)
        {
            try
            {
                if (IsCodeReview)
                {
                    _settingsService.CodeReviewTemplate = Document.Text;
                }

                await _settingsService.UpdateSettingsAsync(
                    _settingsService.CurrentTargetFolder,
                    _settingsService.ScriptsPath,
                    _settingsService.ConsolidatedScriptsPath,
                    IsCodeReview ? null : Document.Text);

                StatusMessage = $"✓ {SelectedTemplateType} saved at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Save failed: {ex.Message}";
            }
        }

        private void ExecuteReset(object obj)
        {
            Document.TextChanged -= OnDocumentTextChanged;

            if (IsCodeReview)
            {
                Document.Text = SettingsService.DefaultCodeReviewTemplate;
                AvailablePlaceholders = CodeReviewPlaceholders;
            }
            else
            {
                Document.Text = SettingsService.DefaultEmailTemplate;
                AvailablePlaceholders = ReleasePlaceholders;
            }

            Document.TextChanged += OnDocumentTextChanged;
            UpdatePreview();
            StatusMessage = "Template reset to default";
        }

        private void ExecuteInsertPlaceholder(object obj)
        {
            if (obj is string placeholder && !string.IsNullOrEmpty(placeholder))
            {
                int offset = Math.Min(CaretOffset, Document.TextLength);
                Document.Insert(offset, placeholder);
                CaretOffset = offset + placeholder.Length;
            }
        }

        // ── INotifyPropertyChanged ──────────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

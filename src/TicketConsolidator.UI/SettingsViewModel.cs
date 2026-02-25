using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TicketConsolidator.Infrastructure.Services;
using MaterialDesignThemes.Wpf;
// using materialDesign = MaterialDesignThemes.Wpf; // Removed alias to use standard namespace inclusions

namespace TicketConsolidator.UI
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;

        public SettingsViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
            TargetFolder = _settingsService.CurrentTargetFolder;
            TicketsFolder = _settingsService.TicketsFolder ?? "";
            
            // Initialize Document
            EmailTemplateDocument = new ICSharpCode.AvalonEdit.Document.TextDocument(_settingsService.EmailTemplate ?? "");
            EmailTemplate = _settingsService.EmailTemplate ?? ""; // FIX: Initialize property immediately
            EmailTemplateDocument.TextChanged += (s, e) => 
            {
                 EmailTemplate = EmailTemplateDocument.Text;
            };

            UpdateFolderCommand = new RelayCommand(ExecuteUpdateSettings);
            PreviewTemplateCommand = new RelayCommand(ExecutePreviewTemplate);
        }

        public ICSharpCode.AvalonEdit.Document.TextDocument EmailTemplateDocument { get; }

        private string _targetFolder;
        public string TargetFolder
        {
            get => _targetFolder;
            set
            {
                if (_targetFolder != value)
                {
                    _targetFolder = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _emailTemplate;
        public string EmailTemplate
        {
            get => _emailTemplate;
            set
            {
                if (_emailTemplate != value)
                {
                    _emailTemplate = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _ticketsFolder;
        public string TicketsFolder
        {
            get => _ticketsFolder;
            set
            {
                if (_ticketsFolder != value)
                {
                    _ticketsFolder = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand UpdateFolderCommand { get; }
        public ICommand PreviewTemplateCommand { get; }

        public bool IsDarkMode
        {
            get => _settingsService.IsDarkMode;
            set
            {
                if (_settingsService.IsDarkMode != value)
                {
                    ModifyTheme(value);
                    _settingsService.UpdateDarkModeAsync(value); // Fire and forget async save
                    OnPropertyChanged();
                }
            }
        }

        private void ModifyTheme(bool isDark)
        {
            // RESOURCE DICTIONARY SWAP - Fallback method due to build issues with PaletteHelper extension methods
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            var target = System.Linq.Enumerable.FirstOrDefault(dicts, d => d.Source != null && (d.Source.ToString().Contains("MaterialDesignTheme.Light.xaml") || d.Source.ToString().Contains("MaterialDesignTheme.Dark.xaml")));
            
            if (target != null)
            {
                dicts.Remove(target);
                var newSource = isDark 
                    ? "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml" 
                    : "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml";
                dicts.Add(new System.Windows.ResourceDictionary { Source = new System.Uri(newSource) });
            }
        }

        private async void ExecuteUpdateSettings(object obj)
        {
            _settingsService.TicketsFolder = TicketsFolder;

            await _settingsService.UpdateSettingsAsync(
                TargetFolder, 
                _settingsService.ScriptsPath, 
                _settingsService.ConsolidatedScriptsPath, 
                EmailTemplate);
                
            // Use consistent UI Popup
            await DialogHost.Show(new Views.Dialogs.InfoDialog(
                "Configuration saved successfully.", 
                "Settings Saved"), "RootDialog");
        }

        private void ExecutePreviewTemplate(object obj)
        {
            try 
            {
                if(string.IsNullOrWhiteSpace(EmailTemplate)) return;

                // Replace placeholders with dummy data for preview
                string previewHtml = EmailTemplate
                    .Replace("{BuildNumber}", "1.0.0-PREVIEW")
                    .Replace("{SolutionPath}", @"C:\Preview\Release\Folder")
                    .Replace("{FileList}", "<li>Script_1.sql</li><li>Script_2.sql</li>")
                    .Replace("{ReleaseDetails}", "<tr><td>TICKET-123</td><td>Preview Ticket Summary</td></tr>")
                    .Replace("{UserName}", System.Environment.UserName);

                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmailPreview.html");
                System.IO.File.WriteAllText(tempPath, previewHtml);

                // Open in default browser
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch(System.Exception ex)
            {
                // Can't show dialog easily from void without async/await propagation or Dispatcher, 
                // but RelayCommand is fire-and-forget.
                // Just swallow or log needed. For now simple check.
                System.Windows.MessageBox.Show($"Error previewing template: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

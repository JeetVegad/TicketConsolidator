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

            UpdateFolderCommand = new RelayCommand(ExecuteUpdateFolder);
        }

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

        public ICommand UpdateFolderCommand { get; }

        private async void ExecuteUpdateFolder(object obj)
        {
            if (!string.IsNullOrWhiteSpace(TargetFolder))
            {
                await _settingsService.UpdateTargetFolderAsync(TargetFolder);
                
                // Use consistent UI Popup
                await DialogHost.Show(new Views.Dialogs.InfoDialog(
                    $"Folder name saved successfully. Next scan will use '{TargetFolder}'.", 
                    "Settings Saved"), "RootDialog");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

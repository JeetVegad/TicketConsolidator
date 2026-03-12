using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TicketConsolidator.UI
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private object _currentView;

        public object CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;
                CurrentViewName = _currentView?.GetType().Name; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentViewName));
                
                // Triggers to update the grid visibility
                OnPropertyChanged(nameof(IsDashboardVisible));
                OnPropertyChanged(nameof(IsCodeReviewVisible));
                OnPropertyChanged(nameof(IsInternalReleaseVisible));
                OnPropertyChanged(nameof(IsTemplateEditorVisible));
                OnPropertyChanged(nameof(IsSettingsVisible));
                OnPropertyChanged(nameof(IsLogsVisible));
                OnPropertyChanged(nameof(IsHelpVisible));
            }
        }

        public string CurrentViewName { get; private set; }

        // Cached View Instances (resolved from DI Singleton container)
        public object DashboardView => ResolveView(typeof(Views.DashboardView));
        public object CodeReviewView => ResolveView(typeof(Views.CodeReviewView));
        public object InternalReleaseView => ResolveView(typeof(Views.InternalReleaseView));
        public object TemplateEditorView => ResolveView(typeof(Views.TemplateEditorView));
        public object SettingsView => ResolveView(typeof(Views.SettingsView));
        public object LogsView => ResolveView(typeof(Views.LogsView));
        public object HelpView => ResolveView(typeof(Views.HelpView));

        // Visibility Flags
        public bool IsDashboardVisible => CurrentView == DashboardView;
        public bool IsCodeReviewVisible => CurrentView == CodeReviewView;
        public bool IsInternalReleaseVisible => CurrentView == InternalReleaseView;
        public bool IsTemplateEditorVisible => CurrentView == TemplateEditorView;
        public bool IsSettingsVisible => CurrentView == SettingsView;
        public bool IsLogsVisible => CurrentView == LogsView;
        public bool IsHelpVisible => CurrentView == HelpView;


        public ICommand NavigateSettingsCommand { get; }
        public ICommand NavigateLogsCommand { get; }
        public ICommand NavigateHelpCommand { get; }
        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateCodeReviewCommand { get; }
        public ICommand NavigateInternalReleaseCommand { get; }
        public ICommand NavigateTemplateEditorCommand { get; }

        private readonly IServiceProvider _serviceProvider;

        public MainWindowViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;


            NavigateSettingsCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.SettingsView)));
            NavigateLogsCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.LogsView)));
            NavigateHelpCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.HelpView)));
            NavigateDashboardCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.DashboardView)));
            NavigateCodeReviewCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.CodeReviewView)));
            NavigateInternalReleaseCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.InternalReleaseView)));
            NavigateTemplateEditorCommand = new RelayCommand(o => CurrentView = ResolveView(typeof(Views.TemplateEditorView)));
            
            // Set initial view
            CurrentView = ResolveView(typeof(Views.DashboardView));
        }

        private object ResolveView(Type type)
        {
             return ((App)System.Windows.Application.Current).ServiceProvider.GetService(type);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

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
            }
        }

        public string CurrentViewName { get; private set; }


        public ICommand NavigateSettingsCommand { get; }
        public ICommand NavigateLogsCommand { get; }
        public ICommand NavigateHelpCommand { get; }
        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateCodeReviewCommand { get; }
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

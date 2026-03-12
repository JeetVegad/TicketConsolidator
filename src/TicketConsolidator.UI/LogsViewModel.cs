using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.UI
{
    public class LogsViewModel : INotifyPropertyChanged
    {
        private readonly ILoggerService _loggerService;
        private ICollectionView _sessionsView;

        public ObservableCollection<string> AvailableFilters { get; } = new ObservableCollection<string>
        {
            "All",
            "Code Review",
            "Internal Release",
            "Consolidated Script",
            "Scan Run",
            "General Application Log"
        };

        private string _selectedFilter = "All";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter != value)
                {
                    _selectedFilter = value;
                    OnPropertyChanged();
                    _sessionsView?.Refresh();
                }
            }
        }

        public ICollectionView SessionsView
        {
            get => _sessionsView;
            set { _sessionsView = value; OnPropertyChanged(); }
        }

        public LogsViewModel(ILoggerService loggerService)
        {
            _loggerService = loggerService;
            
            _sessionsView = CollectionViewSource.GetDefaultView(_loggerService.Sessions);
            if (_sessionsView != null)
            {
                _sessionsView.Filter = FilterSessions;
            }
        }

        private bool FilterSessions(object item)
        {
            if (item is LogSession session)
            {
                if (SelectedFilter == "All") return true;
                
                return session.SessionName != null && session.SessionName.Contains(SelectedFilter, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

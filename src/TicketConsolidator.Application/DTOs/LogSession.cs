using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TicketConsolidator.Application.DTOs
{
    public class LogSession : INotifyPropertyChanged
    {
        public string SessionName { get; set; }
        public DateTime StartTime { get; set; }
        public ObservableCollection<LogEntry> Logs { get; set; } = new ObservableCollection<LogEntry>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public LogSession(string name, bool isExpanded = false)
        {
            SessionName = name;
            StartTime = DateTime.Now;
            IsExpanded = isExpanded;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System.Reflection;
using System.Windows.Input;
using System.Diagnostics;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.UI
{
    public class HelpViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly ILoggerService _logger;

        public string AppVersion { get; private set; }
        public string BuildDate { get; private set; }

        public HelpViewModel(ILoggerService logger)
        {
            _logger = logger;
            AppVersion = $"v{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}";
            BuildDate = "December 2025"; 
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}

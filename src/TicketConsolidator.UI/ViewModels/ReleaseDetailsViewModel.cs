using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TicketConsolidator.UI.ViewModels
{
    public class ReleaseDetailsViewModel : INotifyPropertyChanged
    {
        private string _buildNumber;
        private string _solutionPath;
        private string _username;

        public string BuildNumber
        {
            get => _buildNumber;
            set { _buildNumber = value; OnPropertyChanged(); }
        }

        public string SolutionPath
        {
            get => _solutionPath;
            set { _solutionPath = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

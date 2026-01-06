using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.UI
{
    public class ScriptItemViewModel : INotifyPropertyChanged
    {
        private SqlScript _script;
        
        public string TicketNumber => _script.TicketNumber;
        public string SourceFile => _script.SourceFileName;
        public string Type => _script.Type.ToString();
        public string Snippet => _script.Content.Length > 50 ? _script.Content.Substring(0, 50) + "..." : _script.Content;
        public SqlScript Script => _script;

        public ScriptItemViewModel(SqlScript script)
        {
            _script = script;
        }

        private bool _isFirst;
        public bool IsFirst
        {
            get => _isFirst;
            set { _isFirst = value; OnPropertyChanged(); }
        }

        private bool _isLast;
        public bool IsLast
        {
            get => _isLast;
            set { _isLast = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

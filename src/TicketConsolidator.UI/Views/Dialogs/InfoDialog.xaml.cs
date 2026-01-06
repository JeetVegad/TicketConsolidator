using System.Windows.Controls;
using System.Windows.Media;

namespace TicketConsolidator.UI.Views.Dialogs
{
    public partial class InfoDialog : UserControl
    {
        public string Title { get; }
        public string Message { get; }
        public bool IsWarning { get; }

        public string IconKind => IsWarning ? "AlertCircle" : "CheckCircle";
        public Brush ColorBrush => IsWarning ? new SolidColorBrush(Color.FromRgb(211, 47, 47)) : new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Red or Green

        public InfoDialog(string message, string title = "Info", bool isWarning = false)
        {
            InitializeComponent();
            Message = message;
            Title = title;
            IsWarning = isWarning;
            DataContext = this;
        }
    }
}

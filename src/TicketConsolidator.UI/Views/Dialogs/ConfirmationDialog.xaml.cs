using System.Windows.Controls;

namespace TicketConsolidator.UI.Views.Dialogs
{
    public partial class ConfirmationDialog : UserControl
    {
        public string Title { get; }
        public string Message { get; }

        public ConfirmationDialog(string message, string title = "Confirmation")
        {
            InitializeComponent();
            Title = title;
            Message = message;
            DataContext = this;
        }
    }
}

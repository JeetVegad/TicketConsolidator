using System.Windows.Controls;

namespace TicketConsolidator.UI.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView(LogsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

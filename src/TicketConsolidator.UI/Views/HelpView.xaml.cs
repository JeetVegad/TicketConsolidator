using System.Windows.Controls;

namespace TicketConsolidator.UI.Views
{
    public partial class HelpView : UserControl
    {
        public HelpView(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

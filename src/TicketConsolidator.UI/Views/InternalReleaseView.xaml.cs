using System.Windows.Controls;

namespace TicketConsolidator.UI.Views
{
    public partial class InternalReleaseView : UserControl
    {
        private readonly InternalReleaseViewModel _viewModel;

        public InternalReleaseView(InternalReleaseViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }
    }
}

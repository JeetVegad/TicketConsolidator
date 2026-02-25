using System.Windows.Controls;
using System.Windows.Input;

namespace TicketConsolidator.UI.Views
{
    public partial class CodeReviewView : UserControl
    {
        private readonly CodeReviewViewModel _viewModel;

        public CodeReviewView(CodeReviewViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
            {
                _viewModel.SearchCommand.Execute(null);
            }
        }
    }
}

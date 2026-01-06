using System.Windows.Controls;
using TicketConsolidator.UI; // For RelayCommand

namespace TicketConsolidator.UI.Views.Dialogs
{
    public partial class SuccessDialog : UserControl
    {
        public string Message { get; }
        public string Path { get; }
        public bool HasPath => !string.IsNullOrEmpty(Path);

        public System.Windows.Input.ICommand CopyPathCommand { get; }

        // Parameterless constructor for XAML
        public SuccessDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public SuccessDialog(string message, string path = null)
        {
            InitializeComponent();
            Message = message;
            Path = path;
            
            CopyPathCommand = new RelayCommand(_ => 
            {
                if(!string.IsNullOrEmpty(Path))
                {
                    System.Windows.Clipboard.SetText(Path);
                }
            });

            DataContext = this;
        }
    }
}

using System.Windows;

namespace TicketConsolidator.UI.Views.Dialogs
{
    public partial class ReleaseDetailsDialog : Window
    {
        public string BuildNumber { get; private set; }
        public string SolutionPath { get; private set; }
        public string Username { get; private set; }

        public ReleaseDetailsDialog()
        {
            InitializeComponent();
            UsernameBox.Text = System.Environment.UserName;
            BuildNumberBox.Focus();
        }

        private void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            BuildNumber = BuildNumberBox.Text;
            SolutionPath = SolutionPathBox.Text;
            Username = UsernameBox.Text;

            if (string.IsNullOrWhiteSpace(BuildNumber)) 
            {
                MessageBox.Show("Please enter a Build Number.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

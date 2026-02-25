using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace TicketConsolidator.UI.Views.Dialogs
{
    public partial class LoginDialog : UserControl
    {
        public LoginDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => UsernameBox.Focus();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text?.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return;

            // Return credentials as a tuple — kept in memory only
            DialogHost.CloseDialogCommand.Execute(
                new LoginResult(username, password), this);
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                LoginButton_Click(sender, e);
        }
    }

    /// <summary>Login result — only lives in memory, never persisted.</summary>
    public class LoginResult
    {
        public string Username { get; }
        public string Password { get; }

        public LoginResult(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }
}

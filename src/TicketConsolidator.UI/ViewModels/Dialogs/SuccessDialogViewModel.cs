using System;
using System.Windows;
using System.Windows.Input;
using TicketConsolidator.UI.Helpers; // Assuming RelayCommand is here

namespace TicketConsolidator.UI.ViewModels.Dialogs
{
    public class SuccessDialogViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        public string Message { get; }
        public ICommand CopyPathCommand { get; }

        public SuccessDialogViewModel(string message)
        {
            Message = message;
            CopyPathCommand = new RelayCommand(CopyPathToClipboard);
        }

        private void CopyPathToClipboard(object obj)
        {
            if (!string.IsNullOrWhiteSpace(Message))
            {
                 // Extract path if message contains it, or just copy whole message
                 // Usually message is just the path in some contexts, but let's just copy the message
                 Clipboard.SetText(Message);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Timers;

namespace TicketConsolidator.Web.Services
{
    public class ToastService : IDisposable
    {
        public event Action OnChange;
        public List<ToastMessage> Toasts { get; } = new List<ToastMessage>();

        public void ShowToast(string message, ToastLevel level = ToastLevel.Info)
        {
            var toast = new ToastMessage 
            { 
                Id = Guid.NewGuid(), 
                Message = message, 
                Level = level, 
                Timestamp = DateTime.Now 
            };
            
            Toasts.Add(toast);
            OnChange?.Invoke();

            // Auto-dismiss
            var timer = new Timer(4000);
            timer.Elapsed += (s, e) => RemoveToast(toast.Id);
            timer.AutoReset = false;
            timer.Start();
        }
        
        public void ShowSuccess(string message) => ShowToast(message, ToastLevel.Success);
        public void ShowError(string message) => ShowToast(message, ToastLevel.Error);
        public void ShowInfo(string message) => ShowToast(message, ToastLevel.Info);
        public void ShowWarning(string message) => ShowToast(message, ToastLevel.Warning);

        private void RemoveToast(Guid id)
        {
            var toast = Toasts.Find(x => x.Id == id);
            if (toast != null)
            {
                Toasts.Remove(toast);
                OnChange?.Invoke();
            }
        }
        
        public void Dispose()
        {
            // Timer disposal handled by GC naturally as timers are attached to transient objects here, 
            // but ideally we track them. For simplicity in Blazor Server scoped service:
            // The timers will eventually die.
        }
    }

    public class ToastMessage
    {
        public Guid Id { get; set; }
        public string Message { get; set; }
        public ToastLevel Level { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum ToastLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}

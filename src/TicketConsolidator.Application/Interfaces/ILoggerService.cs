using System.Collections.ObjectModel;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface ILoggerService
    {
        ObservableCollection<LogSession> Sessions { get; }
        void StartSession(string sessionName);
        void Log(string message, LogLevel level = LogLevel.Info);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogSuccess(string message);
        
        System.Threading.Tasks.Task<System.Collections.Generic.List<LogEntry>> GetLogsFromDatabaseAsync(string userNameFilter = null, int daysToLoad = 5);
        System.Threading.Tasks.Task LoadHistoryAsync();
    }
}

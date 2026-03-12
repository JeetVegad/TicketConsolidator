using System.Collections.ObjectModel;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface ILoggerService
    {
        ObservableCollection<LogSession> Sessions { get; }
        void StartSession(string sessionName);
        void Log(string message, LogLevel level = LogLevel.Info, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "");
        void LogInfo(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "");
        void LogWarning(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "");
        void LogError(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "");
        void LogSuccess(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "");
    }
}

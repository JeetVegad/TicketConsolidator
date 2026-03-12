using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace TicketConsolidator.Infrastructure.Services
{
    public class LoggerService : ILoggerService
    {
        public ObservableCollection<LogSession> Sessions { get; } = new ObservableCollection<LogSession>();
        private LogSession _currentSession;
        private readonly string _logDirectory;
        private readonly object _lock = new object();

        public LoggerService(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
             // Determine Log Path
             string basePath = configuration["Storage:BasePath"];
             if (string.IsNullOrWhiteSpace(basePath))
             {
                 basePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "TicketConsolidatorData");
             }
             _logDirectory = System.IO.Path.Combine(basePath, "Logs");
             
             if (!System.IO.Directory.Exists(_logDirectory))
                 System.IO.Directory.CreateDirectory(_logDirectory);
                 
             LoadHistoricalLogs();
        }

        private void DispatchToUI(System.Action action)
        {
            if (System.Windows.Application.Current != null)
            {
                if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                    action();
                else
                    System.Windows.Application.Current.Dispatcher.Invoke(action);
            }
            else
            {
                lock(_lock) { action(); }
            }
        }

        private void LoadHistoricalLogs()
        {
            for (int i = 1; i >= 0; i--) // Yesterday then Today
            {
                var date = System.DateTime.Now.AddDays(-i);
                string fileName = $"Log_{date:yyyy-MM-dd}.txt";
                string fullPath = System.IO.Path.Combine(_logDirectory, fileName);
                
                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(fullPath);
                        LogSession currentHistSession = null;
                        
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("--- SESSION STARTED:"))
                            {
                                string name = line.Replace("--- SESSION STARTED:", "").Replace("---", "").Trim();
                                string displayDate = i == 1 ? "Yesterday" : "Today";
                                
                                // Do not append (Today) if it's already there
                                if (i == 1) name = $"{name} (Yesterday)";
                                
                                currentHistSession = new LogSession(name, isExpanded: false);
                                currentHistSession.StartTime = date.Date;
                                
                                DispatchToUI(() => Sessions.Insert(0, currentHistSession));
                            }
                            else if (currentHistSession != null)
                            {
                                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(.*?)\] \[(.*?)\] (.*)$");
                                if (m.Success)
                                {
                                    if (System.Enum.TryParse<LogLevel>(m.Groups[2].Value, out var level))
                                    {
                                        var logEntry = new LogEntry(m.Groups[3].Value, level);
                                        if (System.DateTime.TryParse(m.Groups[1].Value, out var ts))
                                        {
                                            logEntry.Timestamp = date.Date + ts.TimeOfDay;
                                            
                                            // The first log parsed in the session will set the hour/minute of the session
                                            if (currentHistSession.Logs.Count == 0 || logEntry.Timestamp < currentHistSession.StartTime)
                                                currentHistSession.StartTime = logEntry.Timestamp;
                                        }
                                        
                                        DispatchToUI(() => currentHistSession.Logs.Insert(0, logEntry));
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public void StartSession(string sessionName)
        {
            // Collapse all previous sessions
            foreach (var s in Sessions)
            {
                s.IsExpanded = false;
            }

            DispatchToUI(() =>
            {
                _currentSession = new LogSession(sessionName, isExpanded: true);
                Sessions.Insert(0, _currentSession); // Newest on top
            });
            
            var msg = $"--- SESSION STARTED: {sessionName} ---";
            LogToFile(msg);
        }

        public void Log(string message, LogLevel level = LogLevel.Info, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "")
        {
            if (_currentSession == null)
            {
                StartSession("General Application Log");
            }
            
            string context = System.IO.Path.GetFileNameWithoutExtension(callerPath ?? "");
            if (context.EndsWith("ViewModel")) context = context.Substring(0, context.Length - 9);
            if (context.EndsWith("Service")) context = context.Substring(0, context.Length - 7);
            
            if (!string.IsNullOrEmpty(context) && context != "Logger")
            {
                // Add spaces between PascalCase words
                context = System.Text.RegularExpressions.Regex.Replace(context, "([a-z])([A-Z])", "$1 $2").Trim();
                message = $"[{context}] {message}";
            }

            DispatchToUI(() =>
            {
                if (_currentSession != null)
                {
                    _currentSession.Logs.Insert(0, new LogEntry(message, level));
                }
            });
            
            string timestamped = $"[{System.DateTime.Now:HH:mm:ss}] [{level}] {message}";
            LogToFile(timestamped);
        }

        private void LogToFile(string line)
        {
            try
            {
                string fileName = $"Log_{System.DateTime.Now:yyyy-MM-dd}.txt";
                string fullPath = System.IO.Path.Combine(_logDirectory, fileName);
                
                lock (_lock)
                {
                    System.IO.File.AppendAllText(fullPath, line + System.Environment.NewLine);
                }
            }
            catch
            {
                // Fail silently
            }
        }


        public void LogInfo(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") => Log(message, LogLevel.Info, callerPath);
        public void LogWarning(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") => Log(message, LogLevel.Warning, callerPath);
        public void LogError(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") => Log(message, LogLevel.Error, callerPath);
        public void LogSuccess(string message, [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") => Log(message, LogLevel.Success, callerPath);
    }
}

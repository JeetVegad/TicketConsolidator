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
        private readonly string _connectionString; 
        private readonly bool _enableDatabase; // Feature Flag
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

             // DB Configuration
             _connectionString = configuration.GetConnectionString("LogDatabase");
             
             // Check flag (default to false if missing)
             var enableDbVal = configuration["Logging:EnableDatabase"];
             if (bool.TryParse(enableDbVal, out bool enabled))
             {
                 _enableDatabase = enabled;
             }
             else
             {
                 _enableDatabase = false; 
             }
        }

        public void StartSession(string sessionName)
        {
            // Collapse all previous sessions
            foreach (var s in Sessions)
            {
                s.IsExpanded = false;
            }

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _currentSession = new LogSession(sessionName, isExpanded: true);
                    Sessions.Insert(0, _currentSession); // Newest on top
                });
            }
            else
            {
                 // Non-WPF context (e.g. Blazor)
                 // Just update the collection directly if we are on the right thread, 
                 // or ignore if this collection is only for WPF binding.
                 // For now, let's update it directly assuming Blazor services are single-threaded or robust enough.
                _currentSession = new LogSession(sessionName, isExpanded: true);
                lock(_lock) { Sessions.Insert(0, _currentSession); }
            }
            
            var msg = $"--- SESSION STARTED: {sessionName} ---";
            LogToFile(msg);
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (_currentSession == null)
            {
                StartSession("General Application Log");
            }

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _currentSession.Logs.Insert(0, new LogEntry(message, level));
                });
            }
            else
            {
                // Non-WPF context
                if (_currentSession != null)
                {
                    lock(_lock) { _currentSession.Logs.Insert(0, new LogEntry(message, level)); }
                }
            }
            
            string timestamped = $"[{System.DateTime.Now:HH:mm:ss}] [{level}] {message}";
            LogToFile(timestamped);

            // Log to Database (Async, Fire-and-Forget) -> Only if Enabled
            if (_enableDatabase && !string.IsNullOrWhiteSpace(_connectionString))
            {
                System.Threading.Tasks.Task.Run(() => LogToDatabase(message, level));
            }
        }

        private void LogToDatabase(string message, LogLevel level)
        {
            try
            {
                // Simple parsing for StackTrace if it exists in message
                string stackTrace = null;
                string cleanMessage = message;
                
                if (message.Contains("Stack Trace:"))
                {
                    var parts = message.Split(new[] { "Stack Trace:" }, System.StringSplitOptions.None);
                    if (parts.Length > 1) 
                    {
                        cleanMessage = parts[0].Trim();
                        stackTrace = parts[1].Trim();
                    }
                }

                using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
                {
                     conn.Open();
                     string sql = @"INSERT INTO AppLogs (MachineName, UserName, LogLevel, Message, StackTrace) 
                                    VALUES (@Machine, @User, @Level, @Msg, @Trace)";
                     
                     using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
                     {
                         // ENHANCED IDENTITY: Append IP and Domain
                         string machineInfo = $"{System.Environment.MachineName} ({GetLocalIpAddress()})";
                         string userInfo = $"{System.Environment.UserDomainName}\\{System.Environment.UserName}";

                         cmd.Parameters.AddWithValue("@Machine", machineInfo);
                         cmd.Parameters.AddWithValue("@User", userInfo);
                         cmd.Parameters.AddWithValue("@Level", level.ToString());
                         cmd.Parameters.AddWithValue("@Msg", cleanMessage ?? "");
                         cmd.Parameters.AddWithValue("@Trace", stackTrace ?? (object)System.DBNull.Value);
                         cmd.ExecuteNonQuery();
                     }
                }
            }
            catch (System.Exception ex)
            {
                // Fallback to file if DB fails
                LogToFile($"[DB-FAIL] {ex.Message}"); 
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                foreach (var netInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        netInterface.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        var ipProps = netInterface.GetIPProperties();
                        foreach (var ip in ipProps.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch { /* Ignore network errors */ }
            return "Unknown IP";
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

        // Feature: Get Logs from Database
        public async System.Threading.Tasks.Task<System.Collections.Generic.List<LogEntry>> GetLogsFromDatabaseAsync(string userNameFilter = null, int daysToLoad = 5)
        {
             var results = new System.Collections.Generic.List<LogEntry>();
             if (!_enableDatabase || string.IsNullOrWhiteSpace(_connectionString)) return results; 

             try 
             {
                 using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
                 {
                     await conn.OpenAsync();
                     // Filter by User AND Time (Last N Days)
                     string sql = @"SELECT LogDate, LogLevel, Message 
                                    FROM AppLogs 
                                    WHERE LogDate >= DATEADD(day, @Days, GETDATE())";
                                    
                     if (!string.IsNullOrWhiteSpace(userNameFilter))
                     {
                         sql += " AND UserName = @User";
                     }
                     
                     sql += " ORDER BY LogDate DESC";

                     using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
                     {
                         cmd.Parameters.AddWithValue("@Days", -daysToLoad); // Negative for past
                         
                         if (!string.IsNullOrWhiteSpace(userNameFilter))
                            cmd.Parameters.AddWithValue("@User", userNameFilter);

                         using (var reader = await cmd.ExecuteReaderAsync())
                         {
                             while (await reader.ReadAsync())
                             {
                                 string lvlStr = reader["LogLevel"].ToString();
                                 LogLevel lvl = LogLevel.Info;
                                 if (System.Enum.TryParse(lvlStr, out LogLevel parsed)) lvl = parsed;
                                 
                                 // Format message to include date clearly
                                 System.DateTime date = (System.DateTime)reader["LogDate"];
                                 string msg = reader["Message"].ToString() + $" ({date:g})";
                                 
                                 results.Add(new LogEntry(msg, lvl));
                             }
                         }
                     }
                 }
             }
             catch (System.Exception ex)
             {
                 // Keep this silent or log to file only to prevent recursion loops
                 LogToFile($"[DB-READ-FAIL] {ex.Message}");
             }
             return results;
        }

        public async System.Threading.Tasks.Task LoadHistoryAsync()
        {
            if (!_enableDatabase || string.IsNullOrWhiteSpace(_connectionString)) return;

            var historyLogs = await GetLogsFromDatabaseAsync(System.Environment.UserName, 5);
            if (historyLogs.Count > 0)
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var historySession = new LogSession("History (Last 5 Days)", isExpanded: false);
                        foreach (var log in historyLogs)
                        {
                            historySession.Logs.Add(log);
                        }
                        Sessions.Add(historySession); 
                    });
                }
            }
        }

        public void LogInfo(string message) => Log(message, LogLevel.Info);
        public void LogWarning(string message) => Log(message, LogLevel.Warning);
        public void LogError(string message) => Log(message, LogLevel.Error);
        public void LogSuccess(string message) => Log(message, LogLevel.Success);
    }
}

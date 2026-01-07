using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace TicketConsolidator.Infrastructure.Services
{
    public class SettingsService
    {
        private readonly IConfiguration _configuration;
        private readonly IEncryptionService _encryptionService;
        private readonly ILoggerService _logger;
        private readonly string _settingsFilePath;

        // Current Runtime Folder (Default "Inbox", or load from config/last saved)
        public string CurrentTargetFolder { get; private set; }

        public SettingsService(IEncryptionService encryptionService, ILoggerService logger, IConfiguration configuration)
        {
            _encryptionService = encryptionService;
            _logger = logger;
            _configuration = configuration;
            
            // USER SPECIFIC SETTINGS (Avoid locking appsettings.json in Program Files)
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "TicketConsolidator");
            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
            
            _settingsFilePath = Path.Combine(appFolder, "userSettings.json");
            
            // 1. Storage Base Path Logic
            // Priority: User Settings > Config > Default MyDocuments
            string baseStorage = LoadUserSetting("StorageBasePath");
            if (string.IsNullOrWhiteSpace(baseStorage)) baseStorage = _configuration["Storage:BasePath"];
            
            if (string.IsNullOrWhiteSpace(baseStorage) || !Directory.Exists(baseStorage))
            {
                // Strict Default as per user request (similar to old app) or robust new path
                baseStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TicketConsolidatorData");
            }
            if (!Directory.Exists(baseStorage)) Directory.CreateDirectory(baseStorage);

            // 2. Folder Specifics
            // Default Structure: Base/Scripts, Base/Consolidated
            ScriptsPath = LoadUserSetting("ScriptsPath");
            if (string.IsNullOrWhiteSpace(ScriptsPath)) ScriptsPath = Path.Combine(baseStorage, "Scripts");
            
            ConsolidatedScriptsPath = LoadUserSetting("ConsolidatedPath");
            if (string.IsNullOrWhiteSpace(ConsolidatedScriptsPath)) ConsolidatedScriptsPath = Path.Combine(baseStorage, "Consolidated");

            // Ensure they exist
            if (!Directory.Exists(ScriptsPath)) Directory.CreateDirectory(ScriptsPath);
            if (!Directory.Exists(ConsolidatedScriptsPath)) Directory.CreateDirectory(ConsolidatedScriptsPath);

            // 3. Email Settings
            CurrentTargetFolder = LoadUserSetting("OutlookFolder");
            if (string.IsNullOrWhiteSpace(CurrentTargetFolder)) CurrentTargetFolder = _configuration["EmailSettings:TargetFolder"] ?? "Inbox";

            EmailTemplate = LoadUserSetting("EmailTemplate");
            if (string.IsNullOrWhiteSpace(EmailTemplate)) EmailTemplate = DefaultEmailTemplate;
        }

        private string LoadUserSetting(string key)
        {
            if (!File.Exists(_settingsFilePath)) return null;
            try 
            {
                var json = File.ReadAllText(_settingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var val)) return val.GetString();
            }
            catch { }
            return null;
        }

        public string ScriptsPath { get; private set; }
        public string ConsolidatedScriptsPath { get; private set; }
        public string EmailTemplate { get; private set; }

        private const string DefaultEmailTemplate = @"<html><body>
<p style='font-family:Calibri,sans-serif;font-size:11pt'>Hi All,</p>
<p style='font-family:Calibri,sans-serif;font-size:11pt'><b>Product Release Notification [Build {BuildNumber}]:</b></p>
<p style='font-family:Calibri,sans-serif;font-size:11pt'>Please find the consolidated release deliverables as below:</p>
<p style='font-family:Calibri,sans-serif;font-size:11pt'><b>Release Folder:</b> <a href='{SolutionPath}'>{SolutionPath}</a></p>
<p style='font-family:Calibri,sans-serif;font-size:11pt'><b>DB Scripts:</b></p>
<ul>
{FileList}
</ul>
<br/>
<p style='font-family:Calibri,sans-serif;font-size:11pt'><b>Release Includes:</b></p>
<table border='1' style='border-collapse:collapse;font-family:Calibri,sans-serif;font-size:10pt;width:100%'>
<tr style='background-color:#f2f2f2'><th>JIRA IDs</th><th>Summary</th></tr>
{ReleaseDetails}
</table>
<br/>
<p style='font-family:Calibri,sans-serif;font-size:11pt'>Regards,<br/>{UserName}</p>
</body></html>";

        public async Task UpdateSettingsAsync(string outlookFolder, string scriptsPath, string consolidatedPath, string emailTemplate = null)
        {
             CurrentTargetFolder = outlookFolder;
             ScriptsPath = scriptsPath;
             ConsolidatedScriptsPath = consolidatedPath;
             if (!string.IsNullOrEmpty(emailTemplate)) EmailTemplate = emailTemplate;
             
             // Ensure directories exist
             if (!string.IsNullOrWhiteSpace(ScriptsPath) && !Directory.Exists(ScriptsPath)) Directory.CreateDirectory(ScriptsPath);
             if (!string.IsNullOrWhiteSpace(ConsolidatedScriptsPath) && !Directory.Exists(ConsolidatedScriptsPath)) Directory.CreateDirectory(ConsolidatedScriptsPath);

             var data = new System.Collections.Generic.Dictionary<string, string>
             {
                 ["OutlookFolder"] = outlookFolder,
                 ["ScriptsPath"] = scriptsPath,
                 ["ConsolidatedPath"] = consolidatedPath,
                 ["StorageBasePath"] = Path.GetDirectoryName(ScriptsPath), // Infer base
                 ["EmailTemplate"] = EmailTemplate
             };

             var options = new JsonSerializerOptions { WriteIndented = true };
             await File.WriteAllTextAsync(_settingsFilePath, JsonSerializer.Serialize(data, options));
             
             _logger.LogInfo("Settings updated by user.");
        }



        public async Task UpdateTargetFolderAsync(string newFolder)
        {
            if (string.IsNullOrWhiteSpace(newFolder) || newFolder == CurrentTargetFolder) return;

            string oldFolder = CurrentTargetFolder;
            CurrentTargetFolder = newFolder;
            _logger.LogInfo($"Scan folder changed from '{oldFolder}' to '{CurrentTargetFolder}'.");

            // Persist to Disk
            await SaveSettingsToDiskAsync();
        }

        private async Task SaveSettingsToDiskAsync()
        {
             try 
             {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var root = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);

                if (root == null) root = new System.Collections.Generic.Dictionary<string, object>();

                // Get existing or create new EmailSettings
                EmailConfiguration emailConfig = null;
                if (root.ContainsKey("EmailSettings"))
                {
                    // Handle if it's a JsonElement or string
                    string innerJson = root["EmailSettings"].ToString();
                    try {
                        emailConfig = JsonSerializer.Deserialize<EmailConfiguration>(innerJson);
                    } catch {
                        // Fallback implies it might be an object that serializer handled differently
                        // Simplest: Just create new object with current values
                         emailConfig = new EmailConfiguration();
                    }
                }
                else
                {
                    emailConfig = new EmailConfiguration();
                }

                // Update Folder
                if(emailConfig == null) emailConfig = new EmailConfiguration();
                emailConfig.TargetFolder = CurrentTargetFolder;

                // Update Root
                // Note: System.Text.Json dictionary<string, object> deserialization results in JsonElement for values.
                // It's safer to just read, modify specific strong typed section if possible, or use JObject (Newtonsoft).
                // Given standard deps, let's keep it simple: Read as Dict -> Update -> Write.
                root["EmailSettings"] = emailConfig;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_settingsFilePath, JsonSerializer.Serialize(root, options));
             }
             catch(Exception ex)
             {
                 _logger.LogError($"Failed to save settings: {ex.Message}");
             }
        }

        public EmailConfiguration LoadEmailConfiguration()
        {
             // NOTE: This usually comes from IConfiguration. 
             // But if we want to support "Decrypt" for UI display (showing *******), we just bind it.
             // If we need to *Decrypt* for usage:
             return null; // Not strictly needed if we inject IConfiguration and IEncryptionService separately.
        }
    }
}

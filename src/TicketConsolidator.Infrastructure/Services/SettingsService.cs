using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.DTOs;
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
        public string AppDataFolder { get; private set; }

        public SettingsService(IEncryptionService encryptionService, ILoggerService logger, IConfiguration configuration)
        {
            _encryptionService = encryptionService;
            _logger = logger;
            _configuration = configuration;
            
            // USER SPECIFIC SETTINGS (Avoid locking appsettings.json in Program Files)
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "TicketConsolidator");
            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
            AppDataFolder = appFolder;
            
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

            CurrentTargetFolder = LoadUserSetting("OutlookFolder");
            if (string.IsNullOrWhiteSpace(CurrentTargetFolder)) CurrentTargetFolder = _configuration["EmailSettings:TargetFolder"] ?? "Inbox";

            EmailTemplate = LoadUserSetting("EmailTemplate");
            if (string.IsNullOrWhiteSpace(EmailTemplate)) EmailTemplate = DefaultEmailTemplate;

            CodeReviewTemplate = LoadUserSetting("CodeReviewTemplate");
            if (string.IsNullOrWhiteSpace(CodeReviewTemplate)) CodeReviewTemplate = DefaultCodeReviewTemplate;

            InternalReleaseTemplate = LoadUserSetting("InternalReleaseTemplate");
            if (string.IsNullOrWhiteSpace(InternalReleaseTemplate)) InternalReleaseTemplate = DefaultInternalReleaseTemplate;

            TicketsFolder = LoadUserSetting("TicketsFolder") ?? "";

            IsDarkMode = bool.TryParse(LoadUserSetting("IsDarkMode"), out bool dm) ? dm : false;
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
        public string CodeReviewTemplate { get; set; }
        public string InternalReleaseTemplate { get; set; }
        public string TicketsFolder { get; set; }

        public const string DefaultEmailTemplate = @"<html>
<body>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>Hi All,</p>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    <b>Product Release Notification [Build {BuildNumber}]:</b>
  </p>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    Please find the consolidated release deliverables as below:
  </p>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    <b>Release Folder:</b>
    <a href='{SolutionPath}'>{SolutionPath}</a>
  </p>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    <b>DB Scripts:</b>
  </p>

  <ul>
    {FileList}
  </ul>

  <br/>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    <b>Release Includes:</b>
  </p>

  <table border='1' style='border-collapse:collapse; font-family:Calibri,sans-serif; font-size:10pt; width:100%'>
    <tr style='background-color:#f2f2f2'>
      <th>JIRA IDs</th>
      <th>Summary</th>
    </tr>
    {ReleaseDetails}
  </table>

  <br/>

  <p style='font-family:Calibri,sans-serif; font-size:11pt'>
    Regards,<br/>{UserName}
  </p>

</body>
</html>";

        public bool IsDarkMode { get; private set; }

        public const string DefaultCodeReviewTemplate = @"<html>
<body style='font-family:Calibri,sans-serif; font-size:11pt'>

  <p>Hi Team,</p>

  <p>I've included the ticket's changeset details below for your review.</p>

  <!-- Changeset Details Table -->
  <table border='1' cellpadding='6' cellspacing='0'
         style='border-collapse:collapse; font-family:Calibri; font-size:11pt'>
    <tr style='background:#4472C4; color:white'>
      <th>Ticket</th>
      <th>Change Set</th>
    </tr>
    <tr>
      <td rowspan='2'>
        Ticket No:- <a href='{TicketUrl}'>{TicketKey} - {TicketTitle}</a>
      </td>
      <td>VS Commit: {VSCommitNumber}</td>
    </tr>
    <tr>
      <td>DB Commit: {DBCommitNumber}</td>
    </tr>
{CodeReviewRows}
  </table>

  <br/>

  <!-- DB Review Checklist -->
  <p><b>DB Review Checklist:-</b></p>

  <table border='1' cellpadding='6' cellspacing='0'
         style='border-collapse:collapse; font-family:Calibri; font-size:11pt'>
    <tr style='background:#4472C4; color:white'>
      <th>Sr. No.</th>
      <th>Name</th>
      <th>Is the point considered during development</th>
    </tr>
    <tr>
      <td>1</td>
      <td>I have used meaningful variable and method names.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>2</td>
      <td>I have enhanced the names of existing variables/methods based on a better understanding of the requirements.</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>3</td>
      <td>I am not adding any unnecessary extra files, code/commented code.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>4</td>
      <td>I am not committing any confidential information.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>5</td>
      <td>I have followed the ""single responsibility principle"".</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>6</td>
      <td>I have identified Unit Test Scenarios for the feature and at least documented them informally.</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>7</td>
      <td>I have added meaningful logs for the new code. I have also added exception handling in the code.</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>8</td>
      <td>I have added the DB script of the config/data used. (Attach the DB script for data)</td>
      <td>{HasDataScript}</td>
    </tr>
    <tr>
      <td>9</td>
      <td>Is the requirement/issue fully understood?</td>
      <td>Yes</td>
    </tr>
    <tr>
      <td>10</td>
      <td>Is Self-code review done using Co-pilot?</td>
      <td>NA</td>
    </tr>
    <tr>
      <td>11</td>
      <td>Has the co-pilot code review defect been raised?</td>
      <td>NA</td>
    </tr>
  </table>

  <br/>

  <p>Best Regards,<br/>{UserName}</p>

</body>
</html>";

        public const string DefaultInternalReleaseTemplate = @"<html>
<body style='font-family:Calibri,sans-serif; font-size:11pt'>

  <p>Hi Team,</p>

  <p><b>Product Release Notification:</b></p>
  <ul>
    <li><a href='{TicketUrl}'>[{TicketKey}] - {TicketSummary}</a></li>
  </ul>

  <p><b>Release Notes:</b></p>

  <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; font-family:Calibri; font-size:11pt; width:100%'>
    <tr>
      <td style='width:40%'>Task description</td>
      <td>{TaskDescription}</td>
    </tr>
    <tr>
      <td>Resolution</td>
      <td><b>{Resolution}</b></td>
    </tr>
    <tr>
      <td>Impacted Artifact</td>
      <td>{ImpactedArtifact}</td>
    </tr>
    <tr>
      <td>Coder</td>
      <td>{Coder}</td>
    </tr>
    <tr>
      <td>Reviewer</td>
      <td>{Reviewer}</td>
    </tr>
    <tr>
      <td>Is UT document attached with the ticket?</td>
      <td>{IsUTAttached}</td>
    </tr>
    <tr>
      <td>DB Configuration, If Any ?</td>
      <td>{DbConfiguration}</td>
    </tr>
  </table>

  <br/>

  <p><b>Attachments:</b></p>
{AttachmentsListHtml}

  <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; font-family:Calibri; font-size:11pt; width:400px'>
    <tr>
      <td style='width:70%'><b>1. Self-Code Review using co-pilot</b></td>
      <td>{SelfCodeReviewStatus}</td>
    </tr>
    <tr>
      <td><b>2. Code Review Defect Raised</b></td>
      <td>{CodeReviewDefectStatus}</td>
    </tr>
    <tr>
      <td><b>3. Peer Code Review Done</b></td>
      <td>Yes</td>
    </tr>
    <tr>
      <td><b>4. DB Commit Done</b></td>
      <td>{DbCommitDoneStatus}</td>
    </tr>
    <tr>
      <td><b>5. Data Script Applicable?</b></td>
      <td>{DataScriptApplicableStatus}</td>
    </tr>
    <tr>
      <td><b>6. Permission Script Applicable?</b></td>
      <td>NA</td>
    </tr>
    <tr>
      <td><b>7. UT Document attached to JIRA</b></td>
      <td>{IsUTAttached}</td>
    </tr>
  </table>

  <br/>

  <p>Thanks,</p>

  <p><b>Best Regards,</b><br/>
  {UserName}</p>

</body>
</html>";

        public async Task UpdateSettingsAsync(string outlookFolder, string scriptsPath, string consolidatedPath, string emailTemplate = null, bool? isDarkMode = null)
        {
             CurrentTargetFolder = outlookFolder;
             ScriptsPath = scriptsPath;
             ConsolidatedScriptsPath = consolidatedPath;
             if (!string.IsNullOrEmpty(emailTemplate)) EmailTemplate = emailTemplate;
             if (isDarkMode.HasValue) IsDarkMode = isDarkMode.Value;
             
             // Ensure directories exist
             if (!string.IsNullOrWhiteSpace(ScriptsPath) && !Directory.Exists(ScriptsPath)) Directory.CreateDirectory(ScriptsPath);
             if (!string.IsNullOrWhiteSpace(ConsolidatedScriptsPath) && !Directory.Exists(ConsolidatedScriptsPath)) Directory.CreateDirectory(ConsolidatedScriptsPath);

             try 
             {
                 string json = "{}";
                 if (File.Exists(_settingsFilePath)) json = await File.ReadAllTextAsync(_settingsFilePath);
                 var root = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json) ?? new System.Collections.Generic.Dictionary<string, object>();

                 root["OutlookFolder"] = outlookFolder;
                 root["ScriptsPath"] = scriptsPath;
                 root["ConsolidatedPath"] = consolidatedPath;
                 root["StorageBasePath"] = Path.GetDirectoryName(ScriptsPath); // Infer base
                 root["EmailTemplate"] = EmailTemplate;
                 root["CodeReviewTemplate"] = CodeReviewTemplate ?? "";
                 root["TicketsFolder"] = TicketsFolder ?? "";
                 root["IsDarkMode"] = IsDarkMode.ToString();

                 var options = new JsonSerializerOptions { WriteIndented = true };
                 await File.WriteAllTextAsync(_settingsFilePath, JsonSerializer.Serialize(root, options));
             }
             catch(Exception ex)
             {
                 _logger.LogError($"Failed to update settings: {ex.Message}");
             }
             
             _logger.LogInfo("Settings updated by user.");
        }

        public async Task UpdateDarkModeAsync(bool isDarkMode)
        {
            IsDarkMode = isDarkMode;
            // Re-save all current settings
            await UpdateSettingsAsync(CurrentTargetFolder, ScriptsPath, ConsolidatedScriptsPath, EmailTemplate, isDarkMode);
        }

        public async Task UpdateTargetFolderAsync(string newFolder)
        {
            if (string.IsNullOrWhiteSpace(newFolder) || newFolder == CurrentTargetFolder) return;

            string oldFolder = CurrentTargetFolder;
            CurrentTargetFolder = newFolder;
            _logger.LogInfo($"Scan folder changed from '{oldFolder}' to '{CurrentTargetFolder}'.");
            
            // Should reuse general save method to ensure consistency but keeping legacy separate for now, 
            // although optimally should just call UpdateSettingsAsync
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

        public async Task SaveJiraSessionAsync(IEnumerable<SimpleCookie> cookies)
        {
            var cookieList = cookies.ToList();
            var sessionData = new JiraSessionData
            {
                LoggedInDate = DateTime.Now.Date,
                Cookies = cookieList
            };

            // Update cache immediately
            _cachedJiraSession = cookieList.Select(sc => new Cookie
            {
                Name = sc.Name,
                Value = sc.Value,
                Path = sc.Path,
                Domain = sc.Domain,
                Secure = sc.IsSecure,
                HttpOnly = sc.IsHttpOnly
            }).ToList();
            _cachedSessionDate = DateTime.Now.Date;

            var json = JsonSerializer.Serialize(sessionData);
            var encrypted = _encryptionService.Encrypt(json);

            try 
            {
                string fileJson = "{}";
                if (File.Exists(_settingsFilePath)) fileJson = await File.ReadAllTextAsync(_settingsFilePath);
                var root = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(fileJson) ?? new System.Collections.Generic.Dictionary<string, object>();
                
                root["JiraSessionData"] = encrypted;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_settingsFilePath, JsonSerializer.Serialize(root, options));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save Jira session: {ex.Message}");
            }
        }

        private IEnumerable<Cookie> _cachedJiraSession;
        private DateTime? _cachedSessionDate;

        public IEnumerable<Cookie> LoadJiraSession()
        {
            try
            {
                if (_cachedJiraSession != null && _cachedSessionDate == DateTime.Now.Date)
                {
                    return _cachedJiraSession;
                }

                string encrypted = LoadUserSetting("JiraSessionData");
                if (string.IsNullOrEmpty(encrypted)) return null;

                string decrypted = _encryptionService.Decrypt(encrypted);
                if (string.IsNullOrEmpty(decrypted)) return null;

                var sessionData = JsonSerializer.Deserialize<JiraSessionData>(decrypted);
                if (sessionData == null) return null;

                // Enforce daily login session as requested earlier
                if (sessionData.LoggedInDate != DateTime.Now.Date)
                {
                    _logger.LogInfo("Jira session from a previous day expired.");
                    return null;
                }

                if (sessionData.Cookies == null) return null;

                var cookies = new List<Cookie>();
                foreach (var sc in sessionData.Cookies)
                {
                    cookies.Add(new Cookie
                    {
                        Name = sc.Name,
                        Value = sc.Value,
                        Path = sc.Path,
                        Domain = sc.Domain,
                        Secure = sc.IsSecure,
                        HttpOnly = sc.IsHttpOnly
                    });
                }

                _cachedJiraSession = cookies;
                _cachedSessionDate = sessionData.LoggedInDate;

                return cookies;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load Jira session: {ex.Message}");
                return null;
            }
        }

        public async Task ClearJiraSessionAsync()
        {
            try 
            {
                string fileJson = "{}";
                if (File.Exists(_settingsFilePath)) fileJson = await File.ReadAllTextAsync(_settingsFilePath);
                var root = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(fileJson) ?? new System.Collections.Generic.Dictionary<string, object>();
                
                if (root.ContainsKey("JiraSessionData"))
                {
                    root.Remove("JiraSessionData");
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await File.WriteAllTextAsync(_settingsFilePath, JsonSerializer.Serialize(root, options));
                    _logger.LogInfo("Jira session cleared from settings.");
                }
                _cachedJiraSession = null;
                _cachedSessionDate = null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to clear Jira session: {ex.Message}");
            }
        }
    }

    public class SimpleCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
    }

    public class JiraSessionData
    {
        public DateTime LoggedInDate { get; set; }
        public System.Collections.Generic.List<SimpleCookie> Cookies { get; set; }
    }
}

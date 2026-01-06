using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Reflection;

namespace TicketConsolidator.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailConfiguration _emailConfig;
        
        // Use dynamic to bypass NetOffice wrapper issues entirely
        private dynamic _outlookApp;
        private dynamic _outlookNamespace;

        // Outlook Constants (replacing NetOffice Enums)
        private const int olFolderInbox = 6;
        
        public EmailService(EmailConfiguration emailConfig)
        {
            _emailConfig = emailConfig;
        }

        public async Task ConnectAsync()
        {
            // 0. New Outlook Check (olk.exe)
            if (System.Diagnostics.Process.GetProcessesByName("olk").Length > 0)
            {
                throw new Exception("Detected 'New Outlook' (olk.exe). This application requires 'Classic Outlook' (OUTLOOK.EXE). Please switch versions.");
            }

            // 1. Establish Connection (Smart Strategy: Retry Attach -> Keep if good -> Else Restart Visible -> Force UI)
            _outlookApp = null;
            Exception lastError = null;

            // ATTEMPT 1: Attach to running instance (With Retry for Busy/Transient states)
            for (int attemptMatch = 0; attemptMatch < 3; attemptMatch++) 
            {
                if (System.Diagnostics.Process.GetProcessesByName("OUTLOOK").Length > 0)
                {
                    try 
                    {
                        _outlookApp = NativeMethods.GetActiveObject("Outlook.Application");
                        
                        // Zombie/Busy Check
                        try 
                        { 
                             // Just check a property. If Outlook is busy/stuck, this might throw or hang.
                            string v = _outlookApp.Version; 
                        } 
                        catch 
                        { 
                            _outlookApp = null; // discard
                        }
                    }
                    catch { /* Attach failed */ }
                }
                
                if (_outlookApp != null) break; // Found it!
                await Task.Delay(500); // Wait a bit before retrying attach
            }

            // ATTEMPT 2: Restart if Attach Failed (Permission Mismatch or Real Zombie)
            if (_outlookApp == null)
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("OUTLOOK");
                if (procs.Length > 0)
                {
                    // If we reach here, Outlook IS running, but we couldn't attach after 3 tries.
                    // This confirms a Hard Permission Mismatch (Admin vs User) or Hard Stuck.
                    // We MUST Close it to proceed.
                    foreach (var p in procs) { try { p.Kill(); } catch { } }
                    await Task.Delay(2000); // Wait for release
                }

                // Start VISIBLE Outlook Application
                try 
                {
                     // Use ShellExecute to behave like a User Double-Click (Best for visibility)
                     var psi = new System.Diagnostics.ProcessStartInfo("OUTLOOK.EXE") { UseShellExecute = true };
                     System.Diagnostics.Process.Start(psi);
                }
                catch
                {
                     // Fallback: Try CreateInstance (will be hidden initially, but we fix that below)
                     Type outlookType = Type.GetTypeFromProgID("Outlook.Application");
                     if (outlookType != null) _outlookApp = Activator.CreateInstance(outlookType);
                }

                // Wait for Registration
                // Loop to grab the object from ROT (Active Object)
                for(int i=0; i<15; i++)
                {
                    try 
                    {
                        _outlookApp = NativeMethods.GetActiveObject("Outlook.Application");
                        if(_outlookApp != null) break;
                    }
                    catch { }
                    
                    // If we created it via Activator (and couldn't attach yet), we might already have it in _outlookApp?
                    // No, simpler to just re-fetch to be consistent.
                    if (_outlookApp == null && i > 5)
                    {
                         // If Process.Start failed to register, try CreateInstance as backup
                         try 
                         {
                             Type outlookType = Type.GetTypeFromProgID("Outlook.Application");
                             if (outlookType != null) _outlookApp = Activator.CreateInstance(outlookType);
                         } catch {}
                    }
                    
                    if (_outlookApp != null) break;
                    await Task.Delay(1000);
                }
            }

            // ATTEMPT 3: Last Ditch Creation
            if (_outlookApp == null)
            {
                try 
                {
                     Type outlookType = Type.GetTypeFromProgID("Outlook.Application");
                     if (outlookType != null) _outlookApp = Activator.CreateInstance(outlookType);
                }
                catch (Exception ex) { lastError = ex; }
            }

            // CRITICAL STEP: Force Visibility
            // If we started Outlook (via Process or CreateInstance), it might be hidden.
            // We explicitily command it to show itself.
            if (_outlookApp != null)
            {
                try
                {
                    _outlookNamespace = _outlookApp.GetNamespace("MAPI");
                    dynamic inbox = _outlookNamespace.GetDefaultFolder(olFolderInbox);
                    
                    // Check if any UI is visible
                    int explorerCount = 0;
                    try { explorerCount = _outlookApp.Explorers.Count; } catch {}
                    
                    if (explorerCount == 0)
                    {
                        // HIDDEN! Force Display.
                        inbox.Display();
                    }
                }
                catch { /* Post-connection UI tweak failed, but connection might still be good */ }
            }

            if (_outlookApp == null)
            {
                bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                
                string msg = "Could not connect to Outlook.\n\nTroubleshooting:\n";
                if (isAdmin)
                {
                    msg += "Warning: You are running as Administrator. If Outlook is running as a normal user, Windows blocks the connection.\n";
                    msg += "Solution: Close Outlook completely, or restart this app as a Normal User.\n\n";
                }
                msg += "1. Close Outlook Manually and Try Again.\n";
                msg += "2. Repair Office Installation (Control Panel)\n";
                throw new Exception(msg, lastError);
            }

            // 2. Initialize MAPI Session
            bool sessionReady = false;
            int attempts = 0;

            while (!sessionReady && attempts < 5)
            {
                try
                {
                    // Access MAPI namespace
                    _outlookNamespace = _outlookApp.GetNamespace("MAPI");
                    
                    if (_outlookNamespace != null)
                    {
                        // Connection Verification: Try to access Inbox
                        // If this throws, we aren't fully connected/logged in
                        dynamic inbox = _outlookNamespace.GetDefaultFolder(olFolderInbox);
                        int count = inbox.Items.Count; // This triggers the actual RPC call
                        
                        sessionReady = true; // Connection Valid!
                    }
                }
                catch
                {
                    // Failed to access Inbox? Try logging on.
                    try 
                    {
                        // Default Profile, Default Password, ShowDialog=False, NewSession=False
                        _outlookNamespace.Logon(Missing.Value, Missing.Value, false, false);
                    }
                    catch { /* Login might be suppressed or already active */ }

                    attempts++;
                    await Task.Delay(1000);
                }
            }

            if (!sessionReady)
            {
                DisconnectAsync();
                throw new Exception("Outlook Connection Failed: Could not access MAPI Session (Inbox). verify you are logged into Outlook.");
            }
        }

        private static class NativeMethods
        {
            [DllImport("oleaut32.dll", PreserveSig = false)]
            public static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

            [DllImport("ole32.dll")]
            public static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid pclsid);

            public static object GetActiveObject(string progId)
            {
                Guid clsid;
                CLSIDFromProgID(progId, out clsid);
                object obj;
                GetActiveObject(ref clsid, IntPtr.Zero, out obj);
                return obj;
            }
        }

        public Task DisconnectAsync()
        {
            try
            {
                if (_outlookNamespace != null)
                {
                    // Release MAPI
                    Marshal.ReleaseComObject(_outlookNamespace);
                    _outlookNamespace = null;
                }
                if (_outlookApp != null)
                {
                    // Quit is optional - usually better to leave Outlook running if user started it
                    // _outlookApp.Quit(); 
                    Marshal.ReleaseComObject(_outlookApp);
                    _outlookApp = null;
                }
                
                // Force GC to clean up COM handles
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch { }
            
            return Task.CompletedTask;
        }

        public async Task<List<EmailMessage>> GetEmailsByTicketNumbersAsync(List<string> ticketNumbers, string folderName = "Inbox", System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new List<EmailMessage>();

            // Liveliness Check (v2.5 Fix for "User Closed Outlook" scenario)
            await EnsureConnectionAlive();

            // Ensure Connected (will run if EnsureConnectionAlive found a zombie and killed it, or if first run)
            if (_outlookApp == null) await ConnectAsync();

            try
            {
                // Move heavy COM work to background thread
                await Task.Run(() => 
                {
                    if (_outlookNamespace == null) throw new Exception("MAPI session lost.");

                    dynamic inbox = _outlookNamespace.GetDefaultFolder(olFolderInbox);
                    dynamic targetFolder = inbox;

                    // Navigate to subfolder if requested
                    if (!string.Equals(folderName, "Inbox", StringComparison.OrdinalIgnoreCase))
                    {
                        try 
                        {
                            // 1. Try finding in Inbox first
                            dynamic found = FindFolderRecursive(inbox, folderName);
                            // 2. If not found, try Account Root (Sibling of Inbox) - v2.2 Fix
                            if (found == null)
                            {
                                try 
                                {
                                    dynamic root = inbox.Parent;
                                    found = FindFolderRecursive(root, folderName);
                                    if(root != null) Marshal.ReleaseComObject(root);
                                }
                                catch { }
                            }

                            if (found != null) targetFolder = found;
                        }
                        catch { /* Fallback to inbox if search fails */ }
                    }

                    dynamic items = targetFolder.Items;
                    
                    // SAFE SORT: Try to sort, but handle failure
                    bool isSorted = false;
                    try
                    {
                        items.Sort("[ReceivedTime]", false); // Ascending
                        isSorted = true;
                    }
                    catch 
                    {
                        // Sort failed (common in some Outlook configs). 
                        // We will proceed with raw order, but we CANNOT rely on "stop if old" optimization.
                        isSorted = false;
                    }

                    // SAFE COUNT: Convert to int explicity to avoid "Specified Cast" errors from dynamic
                    int count = 0;
                    try { count = Convert.ToInt32(items.Count); } catch { return; }

                    if (count == 0) return;

                    var pendingTickets = new HashSet<string>(ticketNumbers, StringComparer.OrdinalIgnoreCase);
                    var limitDate = DateTime.Now.AddDays(-365); // Extended to 1 Year for v2.2

                    // Reverse Iteration (Newest First)
                    int scanned = 0;
                    int maxScan = 5000; // Increased Limit

                    for (int i = count; i >= 1; i--)
                    {
                        if (scanned++ > maxScan) break;
                        if (pendingTickets.Count == 0) break;
                        if (cancellationToken.IsCancellationRequested) break;

                        dynamic item = null;
                        try { item = items[i]; } catch { continue; }

                        try 
                        {
                            // Basic Property Check
                            // Use try-catch for individual property access to be 100% safe
                            DateTime receivedTime;
                            try { receivedTime = item.ReceivedTime; } catch { receivedTime = DateTime.MinValue; }

                            // OPTIMIZATION: Only safe to break if we are sure it is sorted
                            // If Date > 60 days, we used to Break. Now checking 365.
                            if (isSorted && receivedTime < limitDate && receivedTime > DateTime.MinValue) 
                            {
                                break; 
                            }

                            string subject = "";
                            try { subject = item.Subject ?? ""; } catch { }
                            
                            // Check Match
                            List<string> matches = new List<string>();
                            foreach (var ticket in pendingTickets)
                            {
                                string ticketRegex = Regex.Escape(ticket).Replace("-", @"\s*-\s*");
                                if (Regex.IsMatch(subject, $@"\b{ticketRegex}\b", RegexOptions.IgnoreCase))
                                {
                                    matches.Add(ticket);
                                }
                            }

                            if (matches.Count > 0)
                            {
                                // Found Match! Process Attachments
                                var attPaths = new List<string>();
                                dynamic attachments = null;
                                int attCount = 0;
                                
                                try 
                                { 
                                    attachments = item.Attachments; 
                                    attCount = Convert.ToInt32(attachments.Count);
                                } 
                                catch { }

                                if (attCount > 0)
                                {
                                    string tempDir = Path.Combine(Path.GetTempPath(), "TicketConsolidator_" + Guid.NewGuid().ToString());
                                    Directory.CreateDirectory(tempDir);

                                    // Iterate generic collection
                                    for (int a = 1; a <= attCount; a++)
                                    {
                                        dynamic att = null;
                                        try
                                        {
                                            att = attachments[a];
                                            string rawName = att.FileName;
                                            string safeName = SanitizeFileName(rawName);
                                            string ext = Path.GetExtension(safeName)?.ToLower();

                                            if (ext == ".sql" || ext == ".txt")
                                            {
                                                string fullPath = Path.Combine(tempDir, safeName);
                                                
                                                // Handle Duplicates (e.g. two log.txt files in same email)
                                                int duplicateCounter = 1;
                                                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
                                                while (File.Exists(fullPath))
                                                {
                                                    // Prevents overwriting existing file
                                                    string newName = $"{fileNameWithoutExt}_{duplicateCounter}{ext}";
                                                    fullPath = Path.Combine(tempDir, newName);
                                                    duplicateCounter++;
                                                }

                                                att.SaveAsFile(fullPath);
                                                attPaths.Add(fullPath);
                                            }
                                        }
                                        catch { /* Skip single bad attachment */ }
                                        finally
                                        {
                                            if (att != null) Marshal.ReleaseComObject(att);
                                        }
                                    }
                                }

                                if (attachments != null) Marshal.ReleaseComObject(attachments);

                                if (attPaths.Count > 0)
                                {
                                    result.Add(new EmailMessage
                                    {
                                        Subject = subject,
                                        Sender = GetSenderInfo(item),
                                        Date = receivedTime,
                                        AttachmentPaths = attPaths,
                                        MatchedTickets = matches,
                                        TicketSummaries = ExtractTicketSummaries(GetBodySafe(item))
                                    });

                                    foreach(var m in matches) pendingTickets.Remove(m);
                                }
                            }
                        }
                        catch 
                        { 
                            // Skip item errors
                        }
                        finally
                        {
                             if (item != null) Marshal.ReleaseComObject(item);
                        }
                    }
                    
                    if (items != null) Marshal.ReleaseComObject(items);
                    if (targetFolder != null && targetFolder != inbox) Marshal.ReleaseComObject(targetFolder);
                    if (inbox != null) Marshal.ReleaseComObject(inbox);

                }, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new Exception($"Scan Failed: {ex.Message}", ex);
            }

            return result;
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "unnamed_attachment";
            
            // Removing Illegal Chars
            string invalid = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalid)
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            // Truncate length
            if (fileName.Length > 100)
            {
                string ext = Path.GetExtension(fileName);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                if (baseName.Length > 50) baseName = baseName.Substring(0, 50);
                fileName = baseName + ext;
            }
            return fileName;
        }

        // Helper: Dynamic Folder Search
        private dynamic FindFolderRecursive(dynamic parentFolder, string name)
        {
            dynamic folders = null;
            try { folders = parentFolder.Folders; } catch { return null; }
            
            dynamic result = null;

            try 
            {
                int count = 0;
                try { count = Convert.ToInt32(folders.Count); } catch { return null; }

                // Direct Child Check
                for (int i = 1; i <= count; i++)
                {
                    dynamic sub = null;
                    try 
                    {
                        sub = folders[i];
                        string subName = sub.Name; // Can fail cast
                        if (string.Equals(subName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            result = sub;
                            break;
                        }
                    }
                    catch { /* Skip unreadable folder */ }
                    finally 
                    {
                        if (result != sub && sub != null) Marshal.ReleaseComObject(sub);
                    }
                }

                if (result != null) return result;

                // Deep Search
                 for (int i = 1; i <= count; i++)
                {
                    dynamic sub = null;
                    try 
                    {
                        sub = folders[i];
                        result = FindFolderRecursive(sub, name);
                        if (result != null) return result;
                    }
                    catch { }
                    finally
                    {
                         if (result == null && sub != null) Marshal.ReleaseComObject(sub);
                    }
                }
            }
            finally
            {
                if (folders != null) Marshal.ReleaseComObject(folders);
            }
            return null;
        }

        private string GetSenderInfo(dynamic item)
        {
             try 
             {
                 return item.SenderName ?? item.SenderEmailAddress ?? "Unknown";
             }
             catch
             {
                 return "Unknown";
             }
        }

        private string GetBodySafe(dynamic item)
        {
            try { return item.Body; } catch { return string.Empty; }
        }

        private Dictionary<string, string> ExtractTicketSummaries(string body)
        {
            var summaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(body)) return summaries;

            try
            {
                // Normalization
                var lines = body.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

                // Strategy: Find "Release Includes" OR "Product Release Notification"
                // Then assume subsequent lines might be "TICKET DESCRIPTION"
                int startIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Contains("Release Includes", StringComparison.OrdinalIgnoreCase) ||
                        lines[i].Contains("Product Release Notification", StringComparison.OrdinalIgnoreCase))
                    {
                        startIndex = i;
                        break;
                    }
                }

                if (startIndex == -1) return summaries; // Block not found

                // Regex for "TicketID  Description"
                // Matches "ENGAGE-1234  Some Text", "ENGAGE- 1234 : Some Text", etc.
                // Enhanced to support bullet points, spaces in ID, and flexible separators
                var ticketRegex = new Regex(@"^[\s•\-\*]*([A-Za-z]+\s*-\s*\d+|\d+)(?:\s*[\-:\|]\s*|\s+)(.+)$", RegexOptions.IgnoreCase);

                for (int i = startIndex + 1; i < lines.Count; i++)
                {
                    var line = lines[i];
                    
                    // Stop if we hit typical footer markers
                    if (line.StartsWith("Regards", StringComparison.OrdinalIgnoreCase) || 
                        line.StartsWith("Thanks", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    var match = ticketRegex.Match(line);
                    if (match.Success)
                    {
                        // Normalize: "ENGAGE - 123" -> "ENGAGE-123"
                        string ticketId = match.Groups[1].Value
                                            .Replace(" ", "")
                                            .Replace("\t", "")
                                            .Trim()
                                            .ToUpperInvariant(); 
                        
                        
                        string summary = match.Groups[2].Value.Trim();
                        // Filter out if summary is just a date or simple junk
                        if (summary.Length > 2 && !summaries.ContainsKey(ticketId))
                        {
                            summaries[ticketId] = summary;
                        }
                    }
                }
            }
            catch { /* Parsing robustness */ }
            return summaries;
        }

        public async Task CreateDraftEmailAsync(string subject, string htmlBody, List<string> attachmentPaths)
        {
            // Liveliness Check
            await EnsureConnectionAlive();
            if (_outlookApp == null) await ConnectAsync();

            await Task.Run(() => 
            {
                try
                {
                    dynamic mailItem = _outlookApp.CreateItem(0); // 0 = OlItemType.olMailItem
                    mailItem.Subject = subject;
                    mailItem.HTMLBody = htmlBody;

                    if (attachmentPaths != null)
                    {
                        foreach (var path in attachmentPaths)
                        {
                            if (File.Exists(path))
                            {
                                mailItem.Attachments.Add(path);
                            }
                        }
                    }

                    mailItem.Display(); // Show Draft
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create email draft: {ex.Message}", ex);
                }
            });
        }

        private async Task EnsureConnectionAlive()
        {
            if (_outlookApp == null) return; // Will be handled by ConnectAsync

            try 
            {
                // Probe for Liveliness
                // If Outlook was closed by user, this will throw a COMException 
                string v = _outlookApp.Version; 
            }
            catch 
            {
                // Outlook is dead/closed.
                // Force cleanup so ConnectAsync() can restart it using our robust logic.
                await DisconnectAsync();
                _outlookApp = null;
            }
        }
    }
}

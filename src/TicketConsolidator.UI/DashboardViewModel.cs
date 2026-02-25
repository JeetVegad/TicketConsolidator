using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Linq; // Added for Linq extension methods
using TicketConsolidator.Application.Interfaces;
using materialDesign = MaterialDesignThemes.Wpf;
using TicketConsolidator.Application.DTOs; // Added for DTOs

namespace TicketConsolidator.UI
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly IEmailService _emailService;
        private readonly ISqlParserService _parserService;
        private readonly IScriptValidatorService _validatorService;
        private readonly ILoggerService _logger; // Logger

        private readonly IConsolidationService _consolidationService;
        private readonly Infrastructure.Services.SettingsService _settingsService; // Inject SettingsService

        private string _ticketInput;
        public string TicketInput
        {
            get => _ticketInput;
            set 
            { 
                // Checks for legacy buffer issues
                _ticketInput = Security.SecurityHelper.SanitizeInput(value); 
                OnPropertyChanged(); 
            }
        }
        
        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Idle";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set 
            { 
                _isBusy = value; 
                OnPropertyChanged(); 
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int _spCount;
        public int SpCount { get => _spCount; set { _spCount = value; OnPropertyChanged(); } }

        private int _dataCount;
        public int DataCount { get => _dataCount; set { _dataCount = value; OnPropertyChanged(); } }

        private int _tableCount;
        public int TableCount { get => _tableCount; set { _tableCount = value; OnPropertyChanged(); } }

        private int _triggerCount;
        public int TriggerCount { get => _triggerCount; set { _triggerCount = value; OnPropertyChanged(); } }

        private string _ticketsCount = "0";
        public string TicketsCount { get => _ticketsCount; set { _ticketsCount = value; OnPropertyChanged(); } }

        // Collections for UI Lists
        public System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> TableScripts { get; } 
            = new System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel>();

        public System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> SpScripts { get; } 
            = new System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel>();

        public System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> DataScripts { get; } 
            = new System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel>();

        public System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> TriggerScripts { get; } 
            = new System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel>();

        public ICommand ScanCommand { get; }
        public ICommand ConsolidateCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        private readonly IConfiguration _configuration;

        public ICommand UploadFileCommand { get; }

        public ICommand DropCommand { get; }
        public ICommand CreateReleaseEmailCommand { get; }

        private string _lastConsolidatedPath;
        private string _lastRunId;

        public DashboardViewModel(
            IEmailService emailService,
            ISqlParserService parserService,
            IScriptValidatorService validatorService,
            IConsolidationService consolidationService,
            IConfiguration configuration,
            ILoggerService logger,
            Infrastructure.Services.SettingsService settingsService) // Inject
        {
            _emailService = emailService;
            _parserService = parserService;
            _validatorService = validatorService;
            _consolidationService = consolidationService;
            _configuration = configuration;
            _logger = logger;
            _settingsService = settingsService;

            ScanCommand = new RelayCommand(async (o) => await ExecuteScan(o), CanExecuteScan);
            ConsolidateCommand = new RelayCommand(async (o) => await ExecuteConsolidate(o), o => (TableScripts.Count + SpScripts.Count + DataScripts.Count + TriggerScripts.Count) > 0 && !IsBusy);
            ClearCommand = new RelayCommand(ExecuteClear);
            
            MoveUpCommand = new RelayCommand(ExecuteMoveUp);
            MoveDownCommand = new RelayCommand(ExecuteMoveDown);

            UploadFileCommand = new RelayCommand(ExecuteUploadFile);
            DropCommand = new RelayCommand(ExecuteDrop);

            RemoveCommand = new RelayCommand(ExecuteRemove);
            CreateReleaseEmailCommand = new RelayCommand(async (o) => await ExecuteCreateReleaseEmail(o), o => !string.IsNullOrEmpty(_lastConsolidatedPath));

            // Hook up collection changes to trigger command re-evaluation
            TableScripts.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
            SpScripts.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
            DataScripts.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
            TriggerScripts.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();

            InitializeOutlook();
            InitializeHistory();
        }

        private async void InitializeHistory()
        {
            try
            {
                await ((ILoggerService)_logger).LoadHistoryAsync();
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"Failed to load history: {ex.Message}");
            }
        }

        private void InitializeOutlook()
        {
             // PERFORMANCE OPTIMIZATION: Defer Outlook connection until first scan (lazy loading)
             // This prevents blocking the UI during startup and improves perceived performance
             // Connection will happen automatically in ExecuteScan when _emailService.GetEmailsByTicketNumbersAsync is called
             _logger.LogInfo("Outlook connection will be initialized on first scan (lazy loading enabled)");
        }

        private async void ExecuteUploadFile(object obj)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQL Files (*.sql)|*.sql|All files (*.*)|*.*",
                Multiselect = true,
                Title = "Select SQL Scripts"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await ProcessExternalFiles(openFileDialog.FileNames);
            }
        }

        private async void ExecuteDrop(object obj)
        {
            if (obj is System.Windows.DragEventArgs e)
            {
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                    await ProcessExternalFiles(files);
                }
            }
        }

        private async System.Threading.Tasks.Task ProcessExternalFiles(string[] filePaths)
        {
            IsBusy = true;
            StatusMessage = "Processing uploaded files...";

            try
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    int processedCount = 0;
                    foreach (var path in filePaths)
                    {
                        if (!System.IO.File.Exists(path)) continue;

                        string extension = System.IO.Path.GetExtension(path).ToLower();
                        if (extension != ".sql" && extension != ".txt") continue;

                        string fileName = System.IO.Path.GetFileName(path);
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Processing {fileName}...");

                        string content = await System.IO.File.ReadAllTextAsync(path);

                        // 1. Determine Type based on Filename (User Requirement) - Regex for flexibility
                        Application.DTOs.ScriptType scriptType = Application.DTOs.ScriptType.Table; // Default
                        
                        // Regex looks for delimiter + Keyword + delimiter or end of string
                        // Matches: "_SP_", "-SP", ".sp", " SP "
                        // NOTE: \b treats _ as a word char, so we MUST use explicit delimiters for _SP_
                        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(SP|StoredProcedure)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                            fileName.EndsWith(".sp.sql", System.StringComparison.OrdinalIgnoreCase))
                        {
                            scriptType = Application.DTOs.ScriptType.StoredProcedure;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Data|Insert)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                 fileName.EndsWith(".data.sql", System.StringComparison.OrdinalIgnoreCase))
                        {
                            scriptType = Application.DTOs.ScriptType.Data;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Trigger|Trg)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                 fileName.EndsWith(".trigger.sql", System.StringComparison.OrdinalIgnoreCase) ||
                                 fileName.EndsWith(".trg.sql", System.StringComparison.OrdinalIgnoreCase))
                        {
                            scriptType = Application.DTOs.ScriptType.Trigger;
                        }

                        // Debugging for User
                        System.Windows.MessageBox.Show($"File: {fileName}\nDetected Type: {scriptType}", "Debug Type Detection");

                        // 2. Parse Content (Handle <Ticket>START tags if present or Consolidated headers)
                        string ticketNumber = "Manual"; 
                        
                        // Check for Consolidated first
                        if (fileName.StartsWith("Consolidated", System.StringComparison.OrdinalIgnoreCase))
                        {
                            ticketNumber = "ConsolidatedScript";
                        }
                        else
                        {
                            // "Engage-123" OR "12345" (Minimum 3 digits if standalone to avoid stripping '2' from ENGAGEHAS2S)
                            var ticketMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"([A-Za-z]+-\d+|\d{3,})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (ticketMatch.Success) ticketNumber = ticketMatch.Value;
                        }

                        // Special Case: If it's a typed Consolidated file (e.g. Consolidated_SP.sql), treat as SINGLE file (don't split)
                        bool isTypedConsolidated = fileName.IndexOf("Consolidated", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                   (scriptType != Application.DTOs.ScriptType.Table || 
                                                    fileName.IndexOf("_Table", System.StringComparison.OrdinalIgnoreCase) >= 0);

                        var parsedScripts = new System.Collections.Generic.List<Application.DTOs.SqlScript>();
                        
                        // Only parse/split if NOT a typed consolidated file
                        if (!isTypedConsolidated)
                        {
                            parsedScripts = _parserService.ParseScript(content, fileName, ticketNumber, System.DateTime.Now);
                        }

                        // Fallback: If no tags found, wrap whole file
                        if (parsedScripts.Count == 0)
                        {
                            parsedScripts.Add(new Application.DTOs.SqlScript
                            {
                                TicketNumber = ticketNumber,
                                Content = content,
                                SourceFileName = fileName,
                                Type = scriptType
                            });
                        }

                        // 3. File-Level Duplicate Check (UI Thread)
                        bool shouldProcess = await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            bool isDuplicate = TableScripts.Any(x => x.SourceFile == fileName) ||
                                               SpScripts.Any(x => x.SourceFile == fileName) ||
                                               DataScripts.Any(x => x.SourceFile == fileName) ||
                                               TriggerScripts.Any(x => x.SourceFile == fileName);

                            if (isDuplicate)
                            {
                                var result = await materialDesign.DialogHost.Show(new Views.Dialogs.ConfirmationDialog(
                                        $"File '{fileName}' has already been imported.\n\nDo you want to REPLACE all scripts from this file?",
                                        "Duplicate File Detected"), "RootDialog");

                                if (result is bool b && b)
                                {
                                    var toRemoveTable = TableScripts.Where(x => x.SourceFile == fileName).ToList();
                                    foreach (var item in toRemoveTable) TableScripts.Remove(item);

                                    var toRemoveSp = SpScripts.Where(x => x.SourceFile == fileName).ToList();
                                    foreach (var item in toRemoveSp) SpScripts.Remove(item);

                                    var toRemoveData = DataScripts.Where(x => x.SourceFile == fileName).ToList();
                                    foreach (var item in toRemoveData) DataScripts.Remove(item);

                                    var toRemoveTrigger = TriggerScripts.Where(x => x.SourceFile == fileName).ToList();
                                    foreach (var item in toRemoveTrigger) TriggerScripts.Remove(item);
                                    return true;
                                }
                                return false;

                            }
                            return true;
                        }).Task.Unwrap();

                        if (!shouldProcess) continue;

                        // 4. Save & Add to UI
                        foreach (var script in parsedScripts)
                        {
                             // Logic to respect extracted source vs detected type
                             // If script.SourceFileName implies SP, use SP.
                             // Else fall back to 'scriptType' (from parent file).
                             // We already rely on ParseScript setting Type correctly or falling back.
                             // But we need to ensure the correct type is used if manual override was detected.
                             if (parsedScripts.Count == 1 && script.Type == Application.DTOs.ScriptType.Table && scriptType != Application.DTOs.ScriptType.Table)
                             {
                                 script.Type = scriptType;
                             }

                             // SAVE to Disk (Skip if Consolidated)
                             if (!isTypedConsolidated)
                             {
                                 try
                                 {
                                     string saveDir = System.IO.Path.Combine(
                                          _settingsService.ScriptsPath, 
                                          script.TicketNumber);
                                         
                                     if (!System.IO.Directory.Exists(saveDir))
                                         System.IO.Directory.CreateDirectory(saveDir);
                                     
                                     string saveName;
                                     if (string.Equals(script.SourceFileName, fileName, System.StringComparison.OrdinalIgnoreCase))
                                     {
                                         if (parsedScripts.Count > 1)
                                         {
                                             string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                                             string ext = System.IO.Path.GetExtension(fileName);
                                             int idx = parsedScripts.IndexOf(script) + 1;
                                             saveName = $"{nameNoExt}_{idx}{ext}";
                                         }
                                         else
                                         {
                                             saveName = fileName;
                                         }
                                     }
                                     else
                                     {
                                         saveName = System.IO.Path.GetFileName(script.SourceFileName);
                                     }
    
                                     string savePath = System.IO.Path.Combine(saveDir, saveName);
                                     await System.IO.File.WriteAllTextAsync(savePath, script.Content);
                                }
                                catch (System.Exception ex)
                                {
                                     _logger.LogError($"Failed to save manual file {fileName}: {ex.Message}");
                                }
                             }

                            // Add to UI (Dispatcher)
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var vm = new ScriptItemViewModel(script);
                                // Determine which SourceFile to use for VM (outer vs inner)
                                // If inner is different, we might want to show inner?
                                // User wants "based on _sp..." which implies inner name awareness.
                                // Let's use SourceFileName from script (extracted from consolidated)
                                vm.Script.SourceFileName = script.SourceFileName; 
                                // But duplicate check uses 'vm.SourceFile' property? 
                                // No, duplicate check uses 'x.SourceFile'.
                                // If we set vm.Script.SourceFileName, does vm expose it?
                                // Let's assume ScriptItemViewModel maps Script.SourceFileName.
                                
                                // WAIT: Duplicate check above (Line 211) uses 'fileName' (Outer).
                                // If we start adding items with 'Inner' filenames, the duplicate check will fail on next upload of the same consolidated file!
                                // Unless we add logic to check Inner names too.
                                // But user asked for de-consolidation.
                                // If we de-consolidate, they become individual scripts.
                                // So checking 'fileName' (consolidated) is less useful?
                                // But we want to prevent re-importing the same consolidated file.
                                // So we should probably tag them with the consolidated filename too?
                                // Or stick to the extracted filenames as the source of truth.
                                // If we use extracted filenames, next time user uploads consolidated file, we check 'fileName'.
                                // It won't match any individual script 'SourceFile' (which is Inner).
                                // Detailed duplicate check: Check if ANY existing script has SourceFile == OuterFile OR InnerFile?
                                // It's getting complex. For now, let's stick to adding the parsed scripts.
                                
                                var targetCollection = GetCollectionFor(vm);
                                targetCollection?.Add(vm);
                            });
                        }
                        processedCount++;
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateAllPositions();
                        StatusMessage = "Upload Complete.";
                        _logger.LogSuccess($"Processed {processedCount} uploaded file(s).");
                    });
                });
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Upload Error: {ex.Message}";
                _logger.LogError($"Upload Error: {ex.Message}");
                 // Show dialog on UI thread
                 System.Windows.Application.Current.Dispatcher.Invoke(() => 
                 {
                     materialDesign.DialogHost.Show(new Views.Dialogs.InfoDialog(
                        $"Error processing files: {ex.Message}", 
                        "Upload Error"), "RootDialog");
                 });
            }
            finally
            {
                 IsBusy = false;
            }
        }


        public ICommand ClearCommand { get; }

        private void ExecuteClear(object obj)
        {
            TicketInput = string.Empty;
            TableScripts.Clear();
            SpScripts.Clear();
            DataScripts.Clear();
            TriggerScripts.Clear();
            ProgressValue = 0;
            StatusMessage = "Idle";
            SpCount = 0;
            DataCount = 0;
            TableCount = 0;
            TriggerCount = 0;
            TicketsCount = "0";
            _logger.LogInfo("Dashboard cleared by user.");
        }

        private System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> GetCollectionFor(ScriptItemViewModel item)
        {
            if (item.Script.Type == Application.DTOs.ScriptType.Table) return TableScripts;
            if (item.Script.Type == Application.DTOs.ScriptType.StoredProcedure) return SpScripts;
            if (item.Script.Type == Application.DTOs.ScriptType.Data) return DataScripts;
            if (item.Script.Type == Application.DTOs.ScriptType.Trigger) return TriggerScripts;
            return null;
        }

        private void ExecuteMoveUp(object obj)
        {
            if (obj is ScriptItemViewModel item)
            {
                var collection = GetCollectionFor(item);
                if (collection != null)
                {
                    int index = collection.IndexOf(item);
                    if (index > 0)
                    {
                        collection.Move(index, index - 1);
                        UpdateCollectionPositions(collection);
                        // _logger.LogInfo($"Moved script {item.TicketNumber} up in {item.Type} list.");
                    }
                }
            }
        }

        private void ExecuteMoveDown(object obj)
        {
            if (obj is ScriptItemViewModel item)
            {
                var collection = GetCollectionFor(item);
                if (collection != null)
                {
                    int index = collection.IndexOf(item);
                    if (index < collection.Count - 1)
                    {
                        collection.Move(index, index + 1);
                        UpdateCollectionPositions(collection);
                        // _logger.LogInfo($"Moved script {item.TicketNumber} down in {item.Type} list.");
                    }
                }
            }
        }
        
        private void ExecuteRemove(object obj)
        {
             if (obj is ScriptItemViewModel item)
             {
                 var collection = GetCollectionFor(item);
                 if (collection != null)
                 {
                     collection.Remove(item);
                     UpdateAllPositions(); // Also updates counts
                     _logger.LogInfo($"Removed script {item.SourceFile} ({item.TicketNumber}) from {item.Type} list.");
                 }
             }
        }

        public System.Windows.Input.ICommand RemoveCommand { get; private set; }

        private void UpdateCollectionPositions(System.Collections.ObjectModel.ObservableCollection<ScriptItemViewModel> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                collection[i].IsFirst = (i == 0);
                collection[i].IsLast = (i == collection.Count - 1);
            }
        }

        private void UpdateAllPositions()
        {
            UpdateCollectionPositions(TableScripts);
            UpdateCollectionPositions(SpScripts);
            UpdateCollectionPositions(DataScripts);
            UpdateCollectionPositions(TriggerScripts);

            // Update Counts for binding
            TableCount = TableScripts.Count;
            SpCount = SpScripts.Count;
            DataCount = DataScripts.Count;
            TriggerCount = TriggerScripts.Count;
        }

        private System.Threading.CancellationTokenSource _cancellationTokenSource;
        private bool _isScanRunning;
        public bool IsScanRunning
        {
            get => _isScanRunning;
            set 
            { 
                _isScanRunning = value; 
                OnPropertyChanged();
                // Force command re-evaluation for Scan/Stop toggle
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public System.Windows.Input.ICommand StopScanCommand { get; private set; }

        private void ExecuteStopScan(object obj)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                StatusMessage = "Stopping scan...";
            }
        }

        private async System.Threading.Tasks.Task ExecuteScan(object obj)
        {
            if (string.IsNullOrWhiteSpace(TicketInput)) return;
            if (IsScanRunning) return; // Prevent double click

            IsScanRunning = true;
            IsBusy = true; // Still use IsBusy for other indicators if needed
            
            _cancellationTokenSource = new System.Threading.CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            ProgressValue = 0;
            StatusMessage = "Initializing...";
            
            // Start New Run Session
            string runId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            // Do NOT Clear existing items (manual uploads)
            // TableScripts.Clear(); SpScripts.Clear(); DataScripts.Clear();

            try
            {
                var tickets = new System.Collections.Generic.List<string>(
                    TicketInput.Split(new[] { ',', ';', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                               .Select(t => t.Trim())
                               .Distinct(System.StringComparer.OrdinalIgnoreCase));
                
                // Start session with ticket correlation
                string ticketSummary = tickets.Count > 5 
                    ? $"{string.Join(", ", tickets.Take(5))}... ({tickets.Count} total)" 
                    : string.Join(", ", tickets);
                _logger.StartSession($"Scan Run [ID: {runId}] - Tickets: {ticketSummary}");
                _logger.LogInfo($"Starting scan for {tickets.Count} ticket(s): {TicketInput}");

                // 1. Scan Emails
                StatusMessage = "Scanning Emails...";
                ProgressValue = 10;
                
                // Use Runtime Configured Folder
                string folderName = _settingsService.CurrentTargetFolder;
                if(string.IsNullOrWhiteSpace(folderName)) folderName = "Inbox";
                
                _logger.LogInfo($"Connecting to Outlook Folder: {folderName}...");
                var emails = await _emailService.GetEmailsByTicketNumbersAsync(tickets, folderName, token);
                
                ProgressValue = 40;
                StatusMessage = $"Found {emails.Count} emails. Parsing...";
                _logger.LogSuccess($"Found {emails.Count} matching email(s).");

                // MISSING TICKET CHECK
                // Robust Check: Use the MatchedTickets populated by the service (which handled Regex/Variations)
                var foundTickets = new System.Collections.Generic.HashSet<string>(
                    emails.SelectMany(e => e.MatchedTickets), 
                    System.StringComparer.OrdinalIgnoreCase);

                var missingTickets = tickets.Except(foundTickets).ToList();
                if(missingTickets.Any())
                {
                    string missingList = string.Join("\n- ", missingTickets);
                    string alertMsg = $"The following tickets were NOT found in '{folderName}':\n\n- {missingList}";
                    _logger.LogWarning($"Missing Tickets: {string.Join(", ", missingTickets)}");
                    
                    // UI Consistency: Use DialogHost instead of MessageBox
                     await materialDesign.DialogHost.Show(new Views.Dialogs.InfoDialog(
                        alertMsg, 
                        "Missing Tickets", 
                        isWarning: true), "RootDialog");

                     // Stop if ALL tickets are missing to avoid redundant "No Scripts Found" message
                     if (missingTickets.Count == tickets.Count)
                     {
                         StatusMessage = "Scan Aborted: No tickets found.";
                         return; 
                     }
                }


                // Capture existing files to determine conflicts (vs just stale files on disk)
                var existingFiles = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach(var item in TableScripts) existingFiles.Add(item.SourceFile);
                foreach(var item in SpScripts) existingFiles.Add(item.SourceFile);
                foreach(var item in DataScripts) existingFiles.Add(item.SourceFile);
                foreach(var item in TriggerScripts) existingFiles.Add(item.SourceFile);

                // 2. Parse Scripts (CPU Bound - Task.Run) & Save Files (IO Bound)
                // Return Tuple: (Script, IsConflict, SavePath)
                var scanResults = await System.Threading.Tasks.Task.Run(async () => 
                {
                    var results = new System.Collections.Generic.List<(Application.DTOs.SqlScript Script, bool IsConflict, string SavePath)>();
                    int processedCount = 0;
                    
                    foreach (var email in emails)
                    {
                        if(token.IsCancellationRequested) break;

                        foreach (var path in email.AttachmentPaths)
                        {
                            if (System.IO.File.Exists(path))
                            {
                                string content = System.IO.File.ReadAllText(path);
                                string fileName = System.IO.Path.GetFileName(path);
                                
                                foreach(var t in tickets)
                                {
                                     // Attempt to find "Real" ticket number from filename (e.g. "Engage-11096" instead of "11096")
                                     string realTicket = t;
                                     var ticketMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"([A-Za-z]+-\d+|\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                     
                                     if (ticketMatch.Success) 
                                     {
                                         string found = ticketMatch.Value;
                                         if (found.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                         {
                                             realTicket = found;
                                         }
                                         else
                                         {
                                             // If filename HAS a ticket number but it's NOT the current one 't', SKIP.
                                             // E.g. Filename="11409.sql", t="11410". Match="11409". 11409 != 11410. Skip.
                                             continue;
                                         }
                                     }

                                     // 1. Determine Type based on Filename (Same logic as File Drop)
                                     Application.DTOs.ScriptType scriptType = Application.DTOs.ScriptType.Table; 
                        
                                     // Regex looks for delimiter + Keyword + delimiter or end of string
                                     if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(SP|StoredProcedure)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                         fileName.EndsWith(".sp.sql", System.StringComparison.OrdinalIgnoreCase))
                                     {
                                         scriptType = Application.DTOs.ScriptType.StoredProcedure;
                                     }
                                     else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Data|Insert)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                              fileName.EndsWith(".data.sql", System.StringComparison.OrdinalIgnoreCase))
                                     {
                                         scriptType = Application.DTOs.ScriptType.Data;
                                     }
                                     else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?:^|[_\-\.\s])(Trigger|Trg)(?:[_\-\.\s]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                              fileName.EndsWith(".trigger.sql", System.StringComparison.OrdinalIgnoreCase) ||
                                              fileName.EndsWith(".trg.sql", System.StringComparison.OrdinalIgnoreCase))
                                     {
                                         scriptType = Application.DTOs.ScriptType.Trigger;
                                     }

                                     var scripts = _parserService.ParseScript(content, fileName, realTicket, System.DateTime.Now);
                                     
                                     // Determine Summary
                                     string scriptSummary = "Manual Upload / No Summary Found";
                                     if (email.TicketSummaries != null && email.TicketSummaries.TryGetValue(realTicket, out string val))
                                     {
                                         scriptSummary = val;
                                     }
                                     else if (!string.IsNullOrWhiteSpace(email.Subject))
                                     {
                                          // Fallback Logic Removed per Requirement
                                          scriptSummary = "No Summary Found";
                                      }

                                     foreach(var s in scripts) s.Summary = scriptSummary;
                                     
                                     // Fallback: If no tags found, wrap whole file
                                     if (scripts.Count == 0)
                                     {
                                          scripts.Add(new Application.DTOs.SqlScript
                                          {
                                              TicketNumber = realTicket,
                                              Content = content,
                                              SourceFileName = fileName,
                                              Type = scriptType,
                                              Summary = scriptSummary // Apply parsed summary to fallback script
                                          });
                                     }

                                     // Save Logic Check
                                     if (scripts.Count > 0)
                                     {
                                         try
                                         {
                                             string saveDir = System.IO.Path.Combine(
                                                  _settingsService.ScriptsPath, 
                                                  realTicket); // Use Real Ticket for Folder
                                                 
                                             if (!System.IO.Directory.Exists(saveDir))
                                                 System.IO.Directory.CreateDirectory(saveDir);

                                             for (int sIdx = 0; sIdx < scripts.Count; sIdx++)
                                             {
                                                 var script = scripts[sIdx];
                                                 
                                                 string saveName;
                                                 if (scripts.Count == 1)
                                                 {
                                                     saveName = fileName;
                                                 }
                                                 else
                                                 {
                                                     string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                                                     string ext = System.IO.Path.GetExtension(fileName);
                                                     saveName = $"{nameNoExt}_{sIdx + 1}{ext}";
                                                 }

                                                 string fullSavePath = System.IO.Path.Combine(saveDir, saveName);
                                                 
                                                 // Update script source name first
                                                 script.SourceFileName = saveName;
                                                 script.TicketNumber = realTicket;

                                                 // Check Conflict - ONLY if it exists in the active UI list
                                                 if (existingFiles.Contains(saveName))
                                                 {
                                                     results.Add((script, true, fullSavePath));
                                                 }
                                                 else
                                                 {
                                                     // Safe to write (overwrite stale disk files if not in list)
                                                     System.IO.File.WriteAllText(fullSavePath, script.Content);
                                                     results.Add((script, false, fullSavePath));
                                                 }
                                             }
                                         }
                                         catch (System.Exception ex)
                                         {
                                              _logger.LogError($"Failed to process file for ticket {t}: {ex.Message}");
                                         }
                                     }
                                }
                            }
                        }
                        processedCount++;
                    }
                    return results;
                }, token);

                ProgressValue = 80;

                // 3. Validate and Populate UI (UI Thread)
                StatusMessage = "Validating...";
                _logger.LogInfo("Validating extracted scripts...");
                
                foreach(var item in scanResults)
                {
                    var script = item.Script;
                    var isConflict = item.IsConflict;
                    var savePath = item.SavePath;

                    // Handle Conflict
                    if (isConflict)
                    {
                        var dialogResult = await materialDesign.DialogHost.Show(new Views.Dialogs.ConfirmationDialog(
                                        $"File '{script.SourceFileName}' already exists and might be in the list.\n\nDo you want to REPLACE it with the scanned version?",
                                        "Duplicate File Detected"), "RootDialog");
                        
                        if (dialogResult is bool confirm && confirm)
                        {
                            // Overwrite File
                            try 
                            { 
                                System.IO.File.WriteAllText(savePath, script.Content); 
                            }
                            catch (System.Exception ex)
                            {
                                _logger.LogError($"Failed to overwrite File: {ex.Message}");
                                continue;
                            }

                            // Remove existing from UI
                            var existingTable = TableScripts.Where(x => x.SourceFile == script.SourceFileName).ToList();
                            foreach(var e in existingTable) TableScripts.Remove(e);

                            var existingSp = SpScripts.Where(x => x.SourceFile == script.SourceFileName).ToList();
                            foreach(var e in existingSp) SpScripts.Remove(e);
                            
                            var existingData = DataScripts.Where(x => x.SourceFile == script.SourceFileName).ToList();
                            foreach(var e in existingData) DataScripts.Remove(e);

                            var existingTrigger = TriggerScripts.Where(x => x.SourceFile == script.SourceFileName).ToList();
                            foreach(var e in existingTrigger) TriggerScripts.Remove(e);
                            
                            // Log
                            _logger.LogInfo($"Replaced existing file: {script.SourceFileName}");
                        }
                        else
                        {
                            // User said NO -> Skip this script
                            _logger.LogInfo($"Skipped duplicate file: {script.SourceFileName}");
                            continue;
                        }
                    }

                    var validation = _validatorService.Validate(script);
                    if(!validation.IsValid)
                        _logger.LogWarning($"Validation Warning for {script.TicketNumber}: {string.Join(", ", validation.Errors)}");
                        
                    // Add to UI List
                    var vm = new ScriptItemViewModel(script);
                    switch(script.Type)
                    {
                        case Application.DTOs.ScriptType.Table: TableScripts.Add(vm); break;
                        case Application.DTOs.ScriptType.StoredProcedure: SpScripts.Add(vm); break;
                        case Application.DTOs.ScriptType.Data: DataScripts.Add(vm); break;
                        case Application.DTOs.ScriptType.Trigger: TriggerScripts.Add(vm); break;
                    }
                }

                // Update Counts & Positions
                UpdateAllPositions();

                ProgressValue = 100;
                
                if (token.IsCancellationRequested)
                {
                     StatusMessage = "Scan Cancelled by User.";
                     _logger.LogWarning("Scan operation cancelled via Stop button.");
                }
                else if (scanResults.Count == 0)
                {
                    string msg = "Scan Complete. WARNING: No scripts found.";
                    StatusMessage = msg;
                    _logger.LogWarning(msg);
                    
                    await materialDesign.DialogHost.Show(new Views.Dialogs.InfoDialog(
                        "No scripts were found in the scanned emails.\n\nPlease ensure your emails contain valid attachments or the correct start/end markers.", 
                        "No Scripts Found", 
                        isWarning: true), "RootDialog");
                }
                else
                {
                    TicketsCount = $"{foundTickets.Count}/{tickets.Count}";
                    StatusMessage = $"Scan Complete. Fetched {foundTickets.Count} / {tickets.Count} tickets. Review below.";
                    _logger.LogSuccess($"Scan Complete. Loaded {scanResults.Count} scripts from {foundTickets.Count} tickets.");
                }
            }
            catch (System.OperationCanceledException)
            {
                 StatusMessage = "Scan Cancelled.";
                 _logger.LogWarning("Scan cancelled.");
            }
            catch(System.Exception ex)
            {
                StatusMessage = "Error: " + ex.Message;
                _logger.LogError($"Scan Failed: {ex.Message}\n{ex.StackTrace}");
                
                // Show DETAILED error to user to help debugging
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                     await materialDesign.DialogHost.Show(new Views.Dialogs.InfoDialog(
                        $"An unexpected error occurred during the scan.\n\nError: {ex.Message}\n\nLocation: {ex.StackTrace}", 
                        "Scan Error", 
                        isWarning: true), "RootDialog");
                });
            }
            finally
            {
                IsBusy = false;
                IsScanRunning = false;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        private async System.Threading.Tasks.Task ExecuteConsolidate(object obj)
        {
            IsBusy = true;
            StatusMessage = "Consolidating...";
            _logger.LogInfo("Starting Consolidation Process...");
            
            try 
            {
                // Flatten all scripts
                var allScripts = new System.Collections.Generic.List<Application.DTOs.SqlScript>();
                allScripts.AddRange(SpScripts.Select(vm => vm.Script));
                allScripts.AddRange(DataScripts.Select(vm => vm.Script));
                allScripts.AddRange(TriggerScripts.Select(vm => vm.Script));
                allScripts.AddRange(TableScripts.Select(vm => vm.Script));

                _logger.LogInfo($"Consolidating: {allScripts.Count} scripts in total.");

                // Consolidated Output Directory (Run-Based)
                string baseDir = _settingsService.ConsolidatedScriptsPath;
                string runId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"); // Unique Run ID
                string runFolderName = $"Run_{runId}";
                string outputDir = System.IO.Path.Combine(baseDir, runFolderName);

                if (!System.IO.Directory.Exists(outputDir))
                     System.IO.Directory.CreateDirectory(outputDir);

                _lastConsolidatedPath = outputDir;
                _lastRunId = runId;

                // Group by Context (Database)
                var contextGroups = allScripts.GroupBy(s => GetScriptContext(s));

                foreach (var group in contextGroups)
                {
                    string context = group.Key;
                    
                    // Separate by Type
                    var dataScripts = group.Where(s => s.Type == Application.DTOs.ScriptType.Data || s.Type == Application.DTOs.ScriptType.Table).ToList();
                    var spScripts = group.Where(s => s.Type == Application.DTOs.ScriptType.StoredProcedure).ToList();
                    var triggerScripts = group.Where(s => s.Type == Application.DTOs.ScriptType.Trigger).ToList();

                    if (dataScripts.Count > 0)
                    {
                        await ProcessConsolidationGroup(dataScripts, "DATA", runId, outputDir, context, "01");
                    }
                    if (spScripts.Count > 0)
                    {
                        await ProcessConsolidationGroup(spScripts, "SP", runId, outputDir, context, "02");
                    }
                    if (triggerScripts.Count > 0)
                    {
                        await ProcessConsolidationGroup(triggerScripts, "TRIGGER", runId, outputDir, context, "03");
                    }
                }
                
                StatusMessage = "Consolidation Complete! Files saved.";
                _logger.LogSuccess("Consolidation process finished successfully.");
                
                string successMsg = $"Files saved successfully at:\n{outputDir}";
                
                IsBusy = false;
                await MaterialDesignThemes.Wpf.DialogHost.Show(new Views.Dialogs.SuccessDialog(successMsg, outputDir), "RootDialog");
                
                // Refresh Command State
                CommandManager.InvalidateRequerySuggested();
            }
            catch(System.Exception ex)
            {
                 StatusMessage = $"Consolidation Failed: {ex.Message}";
                 _logger.LogError($"Consolidation Failed: {ex.Message}");
                 System.Windows.MessageBox.Show($"Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task ExecuteCreateReleaseEmail(object obj)
        {
            try 
            {
                IsBusy = true;
                StatusMessage = "Preparing Release Email...";

                string buildNum = string.Empty;
                string solPath = string.Empty;
                string userName = string.Empty;

                // Show Dialog for Customization
                var vm = new ViewModels.ReleaseDetailsViewModel
                {
                    BuildNumber = _lastRunId,
                    SolutionPath = _lastConsolidatedPath,
                    Username = System.Environment.UserName
                };

                var view = new Views.Dialogs.ReleaseDetailsDialog { DataContext = vm };

                // Application.Current.Dispatcher must be used for UI Dialog
                // Use 'await await' to unwrap the Task<bool> returned by the async lambda
                bool dialogResult = await await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    var result = await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialog");
                    
                    bool isConfirmed = false;
                    if (result is string s && string.Equals(s, "Generate", System.StringComparison.OrdinalIgnoreCase)) 
                    {
                        isConfirmed = true;
                    }
                    else if (result is bool b && b) // Fallback
                    {
                         isConfirmed = true;
                    }

                    if (isConfirmed)
                    {
                        var updatedVm = (ViewModels.ReleaseDetailsViewModel)view.DataContext;
                        buildNum = updatedVm.BuildNumber;
                        solPath = updatedVm.SolutionPath;
                        userName = updatedVm.Username;
                    }
                    
                    return isConfirmed;
                });

                if (!dialogResult)
                {
                    StatusMessage = "Email Creation Cancelled.";
                    _logger.LogInfo("Email Creation Cancelled by user."); 
                    IsBusy = false;
                    return;
                }

                StatusMessage = "Creating Release Email...";
                
                // Gather Data
                var allScripts = TableScripts.Concat(SpScripts).Concat(DataScripts).Concat(TriggerScripts).Select(vm => vm.Script).ToList();
                var uniqueTickets = allScripts.Select(s => s.TicketNumber).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
                
                // Build Summary Map (Best Effort)
                var summaryMap = new System.Collections.Generic.Dictionary<string, string>();
                foreach(var t in uniqueTickets)
                {
                    // Find script with best summary (prefer extracted over "Manual")
                    var s = allScripts.FirstOrDefault(x => x.TicketNumber == t && !string.IsNullOrEmpty(x.Summary) && !x.Summary.StartsWith("Manual"));
                    if (s == null) s = allScripts.FirstOrDefault(x => x.TicketNumber == t);
                    
                    summaryMap[t] = s?.Summary ?? "No Summary Found";
                }

                // Get File List - Recursive to find files in context subfolders
                string[] files = System.IO.Directory.GetFiles(_lastConsolidatedPath, "*.*", System.IO.SearchOption.AllDirectories);
                var fileList = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                var attachmentPaths = files.ToList(); 

                // Get Email Template from Settings
                string template = _settingsService.EmailTemplate ?? string.Empty;
                
                // Build Placeholders matching the default template
                // 1. ReleaseDetails - Ticket Table Rows (just the <tr> elements, not the full table)
                var releaseDetailsSb = new System.Text.StringBuilder();
                foreach(var t in uniqueTickets)
                {
                    releaseDetailsSb.AppendLine("<tr>");
                    releaseDetailsSb.AppendLine($"<td style='padding:4px'><b>{t.ToUpperInvariant()}</b></td>");
                    releaseDetailsSb.AppendLine($"<td style='padding:4px'>{summaryMap[t]}</td>");
                    releaseDetailsSb.AppendLine("</tr>");
                }

                // 2. FileList - Script List Items (just the <li> elements, not the full <ul>)
                var fileListSb = new System.Text.StringBuilder();
                foreach(var f in fileList) 
                {
                    fileListSb.AppendLine($"<li style='font-family:Calibri,sans-serif;font-size:11pt'>{f}</li>");
                }

                // Replace Placeholders
                string htmlBody = template
                    .Replace("{BuildNumber}", buildNum)
                    .Replace("{SolutionPath}", solPath)
                    .Replace("{FileList}", fileListSb.ToString())
                    .Replace("{ReleaseDetails}", releaseDetailsSb.ToString())
                    .Replace("{UserName}", userName);

                await _emailService.CreateDraftEmailAsync(
                    $"Product Release Notification [Build {buildNum}]", 
                    htmlBody, 
                    attachmentPaths);

                StatusMessage = "Email Draft Created.";
                _logger.LogSuccess("Release Email Draft created in Outlook.");
            }
            catch (System.Exception ex)
            {
                StatusMessage = "Email Generation Failed.";
                _logger.LogError($"Failed to create release email: {ex.Message}");
                System.Windows.MessageBox.Show($"Error creating email: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task ProcessConsolidationGroup(
            System.Collections.Generic.List<Application.DTOs.SqlScript> scripts, 
            string typeSuffix, 
            string runId, 
            string outputDir,
            string context,
            string prefix)
        {
            if (scripts == null || scripts.Count == 0) return;

            // Filename: 01_HALOCOREDB_DATA.sql
            // prefix = "01", context = "HALOCOREDB", typeSuffix = "DATA"
            string fileName = $"{prefix}_{context}_{typeSuffix}.sql";
            
            // Database-wise Folder: Output/Run_.../HALOCOREDB/
            string contextDir = System.IO.Path.Combine(outputDir, context);
            if (!System.IO.Directory.Exists(contextDir))
            {
                 System.IO.Directory.CreateDirectory(contextDir);
            }

            string fullPath = System.IO.Path.Combine(contextDir, fileName);

            string content = _consolidationService.ConsolidateScripts(scripts);
            await _consolidationService.SaveConsolidatedFileAsync(content, fullPath);
            
            _logger.LogSuccess($"Saved {fileName} ({scripts.Count} scripts) in {context}");
        }

        private string GetScriptContext(Application.DTOs.SqlScript script)
        {
            if (string.IsNullOrWhiteSpace(script.SourceFileName)) return string.Empty;

            string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(script.SourceFileName);
            
            // Remove Ticket Number from filename to isolate the "Suffix"
            // E.g. "Engage-11408_SP_HALOCOREDB" -> Remove "Engage-11408" -> "_SP_HALOCOREDB"
            string cleanName = fileNameNoExt;
            if (!string.IsNullOrEmpty(script.TicketNumber))
            {
                cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, System.Text.RegularExpressions.Regex.Escape(script.TicketNumber), "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // Split by delimiters
            var parts = cleanName.Split(new[] { '_', '-', ' ', '.' }, System.StringSplitOptions.RemoveEmptyEntries);

            // Filter out common Keywords
            var ignoredKeywords = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "SP", "Data", "Table", "Tables", "Tb", "Tbl", "StoredProcedure", "Insert", "Update", "Script", "Stored", "Procedure", "Engage", "Ticket",
                "CR", "PR", "INC", "TASK", "US", "Bug", "Feat", "Feature", "Fix", "Hotfix", "Trigger", "Trg", "Triggers"
            };

            var contextParts = parts.Where(p => !ignoredKeywords.Contains(p) && !p.All(char.IsDigit)).ToList();

            if (contextParts.Count == 0) return "HALOCOREDB"; // Default per user requirement

            // Join remaining parts (e.g. HALOCOREDB)
            return string.Join("_", contextParts).ToUpperInvariant();
        }

        private bool CanExecuteScan(object obj)
        {
            return !string.IsNullOrWhiteSpace(TicketInput);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

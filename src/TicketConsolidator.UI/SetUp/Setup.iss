; Script generated for TicketConsolidator
; Requires Inno Setup 6.0+

#define AppName "Ticket Consolidator"
#define AppVersion "3.0"
#define AppPublisher "Krish Maniar"
#define AppExeName "TicketConsolidator.UI.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-1234-567890ABCDEF}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=TicketConsolidatorSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\Release\net8.0-windows\publish_final\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs


[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser; WorkingDir: "{app}"

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\Scripts"; Permissions: users-modify
Name: "{app}\Consolidated scripts"; Permissions: users-modify
Name: "{app}\Logs"; Permissions: users-modify

[Code]
var
  EmailObjPage: TInputQueryWizardPage;
  TicketsFolderPage: TInputDirWizardPage;
  EmailFolder: String;
  TicketsFolder: String;

procedure InitializeWizard;
begin
  // Page 1: Outlook Folder
  EmailObjPage := CreateInputQueryPage(wpSelectDir,
    'Outlook Configuration', 'Default Scan Folder',
    'Please specify the default Outlook folder name to scan for emails (e.g. Inbox, Tickets, Work).');
  EmailObjPage.Add('Default Email Folder:', False);
  EmailObjPage.Values[0] := 'Inbox';
  
  // Page 1.5: Tickets Folder
  TicketsFolderPage := CreateInputDirPage(EmailObjPage.ID,
    'Tickets Folder', 'Where do you store Jira or TFS tickets locally?',
    'Select the folder where you want to store and retrieve your ticket automation artifacts. If left blank, it defaults to the Documents folder.',
    False, '');
  TicketsFolderPage.Add('Tickets Folder Path:');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  JsonContent: String;
  ConfigPath: String;
  ScriptsPath: String;
  ConsolidatedPath: String;
  BasePath: String;
  UserSettingsDir: String;
  UserSettingsPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Gather User Inputs
    BasePath := ExpandConstant('{app}');
    EmailFolder := EmailObjPage.Values[0];
    TicketsFolder := TicketsFolderPage.Values[0];
    
    // Validate Email Folder Input (Default to Inbox if empty)
    if Length(EmailFolder) = 0 then
      EmailFolder := 'Inbox';

    // Construct Paths
    ScriptsPath := BasePath + '\Scripts';
    ConsolidatedPath := BasePath + '\Consolidated scripts';
    
    // Escape backslashes for JSON
    StringChange(BasePath, '\', '\\');
    StringChange(ScriptsPath, '\', '\\');
    StringChange(ConsolidatedPath, '\', '\\');
    
    
    ConfigPath := ExpandConstant('{app}\appsettings.json');
    
    // Explicitly delete existing config to ensure clean write
    if FileExists(ConfigPath) then
      DeleteFile(ConfigPath);

    // Generate appsettings.json with all configuration (Mainly Logging/DB here)
    JsonContent := '{' + #13#10 +
      '  "Logging": {' + #13#10 +
      '    "LogLevel": {' + #13#10 +
      '      "Default": "Information",' + #13#10 +
      '      "Microsoft": "Warning",' + #13#10 +
      '      "Microsoft.Hosting.Lifetime": "Information"' + #13#10 +
      '    }' + #13#10 +
      '  },' + #13#10 +
      '  "EmailSettings": {' + #13#10 +
      '    "TargetFolder": "' + EmailFolder + '"' + #13#10 +
      '  },' + #13#10 +
      '  "Storage": {' + #13#10 +
      '    "BasePath": "' + BasePath + '",' + #13#10 +
      '    "ScriptsPath": "' + ScriptsPath + '",' + #13#10 +
      '    "ConsolidatedPath": "' + ConsolidatedPath + '"' + #13#10 +
      '  }' + #13#10 +
      '}';
      
    SaveStringToFile(ConfigPath, JsonContent, False);

    // --- SAVE userSettings.json in %APPDATA% ---
    
    // Construct AppData path for user settings
    UserSettingsDir := ExpandConstant('{userappdata}\TicketConsolidator');
    if not DirExists(UserSettingsDir) then
      CreateDir(UserSettingsDir);
      
    UserSettingsPath := UserSettingsDir + '\userSettings.json';
    
    // Format JSON correctly by escaping backslashes in path
    StringChange(TicketsFolder, '\', '\\');
    
    // Write user settings ONLY if they don't exist to prevent wiping Jira session/templates
    if not FileExists(UserSettingsPath) then
    begin
      JsonContent := '{' + #13#10 +
        '  "OutlookFolder": "' + EmailFolder + '",' + #13#10 +
        '  "TicketsFolder": "' + TicketsFolder + '"' + #13#10 +
        '}';
      SaveStringToFile(UserSettingsPath, JsonContent, False);
    end;
  end;
end;

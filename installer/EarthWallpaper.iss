#ifndef AppVersion
  #define AppVersion "0.1.0-beta.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\release\" + AppVersion + "\publish"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

#define AppName "Earth Wallpaper"
#define AppExeName "EarthWallpaper.exe"
#define AppPublisher "Nikita Sozonoff"
#define RepositoryUrl "https://github.com/NikitaSozonoff/earth-wallpaper"

[Setup]
AppId={{C013A5B0-74EC-4D13-B5D0-4D6D9C7A5D18}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases
DefaultDirName={localappdata}\Programs\Earth Wallpaper
DefaultGroupName=Earth Wallpaper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=EarthWallpaper-Setup-{#AppVersion}
SetupIconFile=..\app\WallpaperWidget\Assets\avalonia-logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName};WallpaperWidget.exe
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Earth Wallpaper with Windows"; GroupDescription: "Additional options:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Earth Wallpaper"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Earth Wallpaper"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EarthWallpaper"; ValueData: """{app}\{#AppExeName}"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Earth Wallpaper"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--uninstall-cleanup"; Flags: runhidden; RunOnceId: "EarthWallpaperStartupCleanup"

; User settings and downloaded content intentionally remain in
; %LOCALAPPDATA%\EarthWallpaper during updates and after uninstall.

; Conditioning Control Panel - Inno Setup Installer Script
; This creates a proper Windows installer with install path selection
;
; Requirements:
; 1. Install Inno Setup from https://jrsoftware.org/isinfo.php
; 2. Build the app first: dotnet publish CCP.Avalonia/CCP.Avalonia.csproj -c Release -p:PublishProfile=win-x64 (.NET 10 SDK)
; 3. Compile this script with Inno Setup Compiler
;
; The installer will:
; - Allow users to choose installation directory
; - Create Start Menu and Desktop shortcuts
; - Register uninstaller
; - Store install path in registry for Velopack updates

#define MyAppName "Conditioning Control Panel"
#define MyAppVersion "7.2.3"
#define MyAppPublisher "CodeBambi"
#define MyAppURL "https://github.com/CC-Labs-it/Conditioning-Control-Panel"
#define MyAppExeName "ConditioningControlPanel.exe"
; 7.2+: the Avalonia head's exe as published. It is installed under MyAppExeName (DestName) so the
; AppId, folder, exe name, shortcuts, taskkill and the updater's relaunch all stay what WPF had
; (docs/avalonia-decisions.html, "Upgrade in place from WPF").
#define SourceExeName "CCP.Avalonia.exe"
#define MyAppDescription "A professional visual conditioning application with gamification features"

; Path to the published output (adjust if needed)
; PublishDir can be overridden from the command line (ISCC /DPublishDir=...), which
; build-installer.bat uses to point at a SHORT staging path. The publish tree is nested
; ~131 chars deep, and a handful of builtin-sissyhypno audio files push past MAX_PATH (260)
; from there, aborting the ISCC compile. Staging to e.g. C:\ccpb\pub keeps every path short.
#ifndef PublishDir
  #define PublishDir "CCP.Avalonia\bin\publish\win-x64"
#endif

#include "installer-avalonia-code-5.iss"
SetupIconFile=Assets\app.ico

; Avalonia only (code-6 keeps the WPF excerpt verbatim): WPF BrowserService.cs:127-131 uses browser_data;
; WebViewProfile.cs:19-31 uses a new profile, so upgraders are told before installing.
InfoBeforeFile=INSTALL_AVALONIA.txt

; Compression
#include "installer-avalonia-code-6.iss"
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "INSTALL_AVALONIA.txt"
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,installer-notices\zh-CN.isl"; InfoBeforeFile: "installer-notices\zh-CN.txt"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; InfoBeforeFile: "installer-notices\ja.txt"
Name: "korean"; MessagesFile: "compiler:Default.isl,installer-notices\ko.isl"; InfoBeforeFile: "installer-notices\ko.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "installer-notices\es.txt"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "installer-notices\pt-BR.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoBeforeFile: "installer-notices\fr.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; InfoBeforeFile: "installer-notices\it.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoBeforeFile: "installer-notices\de.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; InfoBeforeFile: "installer-notices\ru.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "fileassoc"; Description: "Add 'Open with CCP' to right-click menu for media files (.mp4, .mp3, etc.)"; GroupDescription: "File associations:"; Flags: unchecked

[Files]
; Microsoft Visual C++ 2015-2022 x64 runtime bootstrapper. OpenCvSharpExtern.dll
; (webcam capture) is built against the MSVC runtime and fails to load with
; DllNotFoundException 0x8007007E on machines that don't have it — which silently
; broke all webcam/eye-tracking features (BUG-XQCPKGE2Q8). Only staged + run when
; the runtime is actually missing (see VCRedistNeeded). build-installer.bat
; downloads this into redist\ before compiling.
Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeeded

; Microsoft Edge WebView2 Evergreen runtime bootstrapper (~1.6 MB; it downloads the
; real runtime itself). WebView2 is no longer optional: the FYP feed, the Exclusives
; tab, DtRH, the Goon Game and — from 6.7 — the default video engine all render in
; WebView2. It is preinstalled on Windows 11 and on Windows 10 machines with modern
; Edge, so most installs skip this entirely (see WebView2RuntimeNeeded). Committed in
; redist\ rather than downloaded at build time because a silent skip here costs half
; the app's content. Official Microsoft fwlink for refreshes:
;   https://go.microsoft.com/fwlink/p/?LinkId=2124703
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: WebView2RuntimeNeeded

; Main executable: the Avalonia exe, renamed on install (CI output is unsigned)
Source: "{#PublishDir}\{#SourceExeName}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion

; All other files from publish directory (the original exe name is excluded: it is installed above)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "\{#SourceExeName},*.pdb,cs\*,de\*,es\*,fr\*,it\*,ja\*,ko\*,pl\*,pt-BR\*,ru\*,tr\*,zh-Hans\*,zh-Hant\*"

; NOTE: Don't include user data files - those go to %APPDATA%

[InstallDelete]
; =============================================================================================
; CONTENT PACKS - upgrader convergence.  See ConditioningControlPanel\docs\CONTENT_PACKS_PLAN.md
; sections 1 (what moves) and 5 (why this section exists).
;
; From 6.7 on, ~1.36 GB of version-stable audio and the two bundled .ccpmod archives no longer
; ship in the installer (stripped in ConditioningControlPanel.csproj via the
; ContentPack*Exclude properties) and are downloaded instead into
;   %LOCALAPPDATA%\ConditioningControlPanel\content\.
; Inno leaves behind files it did not install, so without this section an upgrader keeps the old
; bundled copies under {app} forever - and ContentLocator probes BaseDirectory first, so those
; stale copies would permanently shadow every downloaded pack.  [InstallDelete] runs before
; [Files], so this is a clean sweep followed by a fresh install.
;
; RULES:
;   * The deletion list is GENERATED - installer-content-deletions.iss (#included below) names
;     every moved file EXPLICITLY, one exact path per line, NO wildcards.  A file a USER
;     hand-dropped into one of these folders is therefore never matched and survives the
;     upgrade - and keeps working, because ContentLocator unions the install dir with the
;     downloaded content root.  Shipped manifests (bark_rules.json, mantras.json,
;     avatar_manifest.json, vo_manifest.json, sfx_manifest.json, dtrh barks\manifest.js,
;     vn\manifest.json) are not pack payload, so they can never appear in the list; a missing
;     manifest is not a graceful degrade, it is a hang or a crash.
;   * NEVER hand-edit installer-content-deletions.iss and NEVER add wildcard deletions here -
;     a wildcard cannot tell a hand-added user file from a shipped one.  Regenerate (and
;     commit) after any change to the pack file set:
;       powershell -ExecutionPolicy Bypass -File ConditioningControlPanel\Scripts\build-content-packs.ps1 -DeletionsOnly
;     (a full pack build regenerates it too, and hard-fails on strip/pack drift first).
;   * Never touch %LOCALAPPDATA%\ConditioningControlPanel - user settings, progress, assets,
;     mods and downloaded packs all live there.  Everything in the list is {app}-only.
;   * "dirifempty" only removes a folder that the sweep left empty, so a surviving user file
;     also keeps its folder alive.
; =============================================================================================

; NOTE the include also covers the two bundled .ccpmod archives ({app}\DroneMod\,
; {app}\LockedMod\).  ModService's already-extracted copies under %LOCALAPPDATA%\...\builtin_mods\
; are user data and are deliberately left alone - phase C re-stamps them from the downloaded
; pack instead.
#include "installer-content-deletions.iss"

; RETIRED 2026-09-25 (not pack payload, so not in the generated list above): orphaned Bambi
; trigger clips from the Resources\sounds ROOT that no code read. Duplicates of Resources\sub_audio.
; Exact names only, same rule as above. Resources\sounds\GOOD GIRL.mp3 and 00 Bimbo Drone.mp3 stay
; (QuizWindow plays them).
Type: files;      Name: "{app}\Resources\sounds\BAMBI CUM AND COLLAPSE.MP3"
Type: files;      Name: "{app}\Resources\sounds\Bambi Does As Shes Told.mp3"
Type: files;      Name: "{app}\Resources\sounds\BAMBI DOES AS SHE'S TOLD.MP3"
Type: files;      Name: "{app}\Resources\sounds\BAMBI FREEZE.mp3"
Type: files;      Name: "{app}\Resources\sounds\BAMBI RESET.mp3"
Type: files;      Name: "{app}\Resources\sounds\BAMBI SLEEP.mp3"
Type: files;      Name: "{app}\Resources\sounds\BAMBI UNIFORM LOCK.mp3"
Type: files;      Name: "{app}\Resources\sounds\BIMBO DOLL.mp3"
Type: files;      Name: "{app}\Resources\sounds\COCK ZOMBIE NOW.mp3"
Type: files;      Name: "{app}\Resources\sounds\DROP FOR COCK.mp3"
Type: files;      Name: "{app}\Resources\sounds\Giggle Time.mp3"
Type: files;      Name: "{app}\Resources\sounds\GIGGLETIME.mp3"
Type: files;      Name: "{app}\Resources\sounds\PRIMPED AND PAMPERED.MP3"
Type: files;      Name: "{app}\Resources\sounds\SNAP AND FORGET.MP3"
Type: files;      Name: "{app}\Resources\sounds\ZAP COCK DRAIN OBEY.mp3"

; 7.2 (WPF -> Avalonia upgrade): WPF's loose WebView2 loader. The Avalonia head bundles its own web
; view natives in the single-file exe, so a stale loader beside it could only shadow them.
Type: files;      Name: "{app}\WebView2Loader.dll"

; WPF shipped LibVLC 3.0.21 in {app}\libvlc (ConditioningControlPanel.csproj:146,945-953).
; These exact old-only paths are absent from the 3.0.24 publish; preserve other files.
Type: files;      Name: "{app}\libvlc\lua\playlist\youtube.luac"
Type: files;      Name: "{app}\libvlc\plugins\access\libbluray-awt-j2se-1.3.2.jar"
Type: files;      Name: "{app}\libvlc\plugins\access\libbluray-j2se-1.3.2.jar"
Type: files;      Name: "{app}\libvlc\plugins\codec\libschroedinger_plugin.dll"
Type: files;      Name: "{app}\libvlc\plugins\codec\libsdl_image_plugin.dll"
Type: files;      Name: "{app}\libvlc\win-x64\lua\playlist\youtube.luac"
Type: files;      Name: "{app}\libvlc\win-x86\lua\playlist\youtube.luac"

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Store install path for the application and Velopack to find
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
; Assets path will be written by [Code] section during install

Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1"; ValueType: string; ValueName: ""; ValueData: "Conditioning Control Panel - Player"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "CCP Player"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "CCP Player"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --play ""%1"""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1"; ValueType: string; ValueName: ""; ValueData: "Conditioning Control Panel - Editor"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "CCP Editor"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "CCP Editor"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --edit ""%1"""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

[Run]
; Install the VC++ runtime first (only if missing) so the webcam native library
; can load on first launch. Self-elevates via UAC; /norestart keeps setup flowing.
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ Runtime (required for webcam features)..."; Check: VCRedistNeeded

; Then the WebView2 Evergreen runtime (only if missing) - the browser-backed features
; (FYP feed, Exclusives, DtRH, Goon Game, the default video engine) have no fallback
; renderer without it. The bootstrapper pulls the runtime down itself and self-elevates
; via UAC; if it fails, setup continues and the app degrades exactly as it does today.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime (required for video and web features)..."; Check: WebView2RuntimeNeeded

; Option to launch app after interactive installation (shows checkbox)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; NOTE: No silent-install self-launch here. The in-app updater's external helper
; (UpdateService.WriteUpdateHelperScript) owns the relaunch after a silent auto-update, so a
; second launch here would double-start the app. (The single-instance handshake would dedup
; it, but we keep exactly one relauncher to avoid a stray window flash.)

[UninstallRun]
; Ensure app is closed before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; WebView2's default profile beside the exe. The app points it at
; %LOCALAPPDATA%\ConditioningControlPanel\browser_data_avalonia (offered for removal below with the
; rest of the user data); these catch a run that predates that or a dev copy of CCP.Avalonia.exe.
Type: filesandordirs; Name: "{app}\{#MyAppExeName}.WebView2"
Type: filesandordirs; Name: "{app}\{#SourceExeName}.WebView2"
; Clean up any generated files (optional)
Type: filesandordirs; Name: "{app}\logs"

[Code]
#include "installer-avalonia-code-1.iss"
// Remove file associations when the task is not selected
#include "installer-avalonia-code-2.iss"
      if CheckForMutexes('ConditioningControlPanel_SingleInstance_Mutex') then
#include "installer-avalonia-code-3.iss"
    if not WizardIsTaskSelected('fileassoc') then
      CleanupFileAssociations();
#include "installer-avalonia-code-4.iss"

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Privileges - allow per-user or admin install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Appearance
WizardStyle=modern
WizardSizePercent=120

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Restart Manager: name the app's real single-instance mutex (App.xaml.cs MutexName) so
; Inno can detect the running app and cleanly close+restart it when files are in use during
; a silent auto-update. Without this, /CLOSEAPPLICATIONS has nothing registered to close and
; the in-app updater could race the still-locked exe (#499).
AppMutex=ConditioningControlPanel_SingleInstance_Mutex
CloseApplications=yes
RestartApplications=yes

; Other settings
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
AllowNoIcons=yes
ShowLanguageDialog=auto

; License and info pages (optional - create these files if desired)
; LicenseFile=LICENSE.txt
; InfoBeforeFile=INSTALL_INFO.txt

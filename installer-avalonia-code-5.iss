[Setup]
; Application identity
; AppId is the key every upgrade hangs off: Setup finds the previous install (and therefore its
; directory, its privileges mode and its uninstall entry) by this GUID alone. It has been this
; value since the installer was introduced and must NEVER change - a new GUID would make every
; existing install invisible to Setup, so upgrades would install fresh into DefaultDirName and
; leave the old copy behind.
AppId={{A7B9C3D1-E5F2-4A8B-9C1D-2E3F4A5B6C7D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default installation directory (user can change)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Allow user to change install directory
DisableDirPage=no
DirExistsWarning=auto

; An upgrade must never move an existing install. Both of these are already Inno's defaults,
; but they are now load-bearing and are pinned so they cannot be turned off by accident: when
; the in-app updater cannot determine which folder the running app lives in it deliberately
; passes NO /DIR and relies on Setup recovering the previous location by AppId (ccp-bugs#1090,
; ccp-bugs#1004). UsePreviousPrivileges matters for the same reason - PrivilegesRequired below
; is "lowest", so without it a per-machine ("all users") install would be looked for in the
; per-user hive only, no previous directory would be found, and the upgrade would land in
; DefaultDirName instead of on top of the existing install.
UsePreviousAppDir=yes
UsePreviousPrivileges=yes

; Output settings
OutputDir=.\installer-output
OutputBaseFilename=ConditioningControlPanel-{#MyAppVersion}-Setup

procedure CleanupFileAssociations();
var
  Exts: array of String;
  i: Integer;
begin
  // Remove both ProgID keys entirely
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CCPanel.Player.1');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CCPanel.Editor.1');

  // Remove our entries from each extension's OpenWithProgids list (leave other apps' entries alone)
  SetArrayLength(Exts, 12);
  Exts[0] := '.mp4';  Exts[1] := '.webm'; Exts[2] := '.mkv';  Exts[3] := '.mov';
  Exts[4] := '.avi';  Exts[5] := '.m4v';  Exts[6] := '.mp3';  Exts[7] := '.wav';
  Exts[8] := '.m4a';  Exts[9] := '.aac';  Exts[10] := '.flac'; Exts[11] := '.ogg';
  for i := 0 to GetArrayLength(Exts) - 1 do
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Classes\' + Exts[i] + '\OpenWithProgids', 'CCPanel.Player.1');
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Classes\' + Exts[i] + '\OpenWithProgids', 'CCPanel.Editor.1');
  end;
end;

// Prompt to close app if running
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // Check for old Velopack install
  HasOldVelopackInstall := CheckForVelopackInstall();

  // Detect existing assets
  DetectExistingAssets();

  // Check if already running. Use the app's REAL single-instance mutex name
  // (App.xaml.cs MutexName) — the old '{#MyAppName}_Mutex' string never matched, so this
  // guard was dead and never detected a running app (#499).
  if CheckForMutexes('ConditioningControlPanel_SingleInstance_Mutex') then
  begin
    if MsgBox('{#MyAppName} is currently running.' + #13#10 + #13#10 +
              'Please close it before continuing installation.' + #13#10 + #13#10 +
              'Click OK to attempt to close it automatically, or Cancel to exit setup.',
              mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      // Try to close gracefully first
      ShellExec('', 'taskkill', '/IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      Sleep(2000);

      // Force kill if still running

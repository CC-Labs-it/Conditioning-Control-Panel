      begin
        ShellExec('', 'taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
        Sleep(1000);
      end;
    end
    else
    begin
      Result := False;
    end;
  end;
end;

// Determine if assets page should be shown
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // ALWAYS show assets page - users need to choose where their content lives
  // This folder stores images, videos, and downloaded packs - survives updates
end;

// Called after installation completes successfully
procedure CurStepChanged(CurStep: TSetupStep);
var
  SelectedAssetsPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // ALWAYS save the selected assets path to registry (read by app on startup)
    // This ensures the user's choice is respected, whether new or existing install
    SelectedAssetsPath := AssetsPathEdit.Text;
    if SelectedAssetsPath <> '' then
    begin
      RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppPublisher}\{#MyAppName}',
        'AssetsPath', SelectedAssetsPath);

      // Create the folder structure if it doesn't exist
      if not DirExists(SelectedAssetsPath) then
        ForceDirectories(SelectedAssetsPath);
      if not DirExists(SelectedAssetsPath + '\images') then
        ForceDirectories(SelectedAssetsPath + '\images');
      if not DirExists(SelectedAssetsPath + '\videos') then
        ForceDirectories(SelectedAssetsPath + '\videos');
    end;

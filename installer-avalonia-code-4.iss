
    // Offer to clean up old Velopack installation
    if HasOldVelopackInstall then
    begin
      if MsgBox('A previous installation (via auto-updater) was detected.' + #13#10 + #13#10 +
                'Location: ' + VelopackPath + '\current' + #13#10 + #13#10 +
                'Would you like to remove the old installation?' + #13#10 +
                '(Your settings, assets, and progress will be preserved)',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        CleanupVelopackInstall();
        MsgBox('Old installation removed successfully!' + #13#10 + #13#10 +
               'Your user data has been preserved in:' + #13#10 +
               VelopackPath,
               mbInformation, MB_OK);
      end;
    end;
  end;
end;

// Clean uninstall - prompt to remove user data
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    UserDataPath := ExpandConstant('{localappdata}\ConditioningControlPanel');

    if DirExists(UserDataPath) then
    begin
      if MsgBox('Do you want to remove your user data (settings, progress, logs)?' + #13#10 + #13#10 +
                'Location: ' + UserDataPath + #13#10 + #13#10 +
                'Click Yes to remove all data, or No to keep it.',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(UserDataPath, True, True, True);
      end;
    end;
  end;
end;

// Create the assets confirmation page
procedure CreateAssetsPage();
var
  InfoText: String;
begin
  // Create custom page after the directory selection page
  AssetsPage := CreateCustomPage(wpSelectDir,
    'Content Folder',
    'Choose where to store your images, videos, and downloaded packs');

  // Description label - explain clearly what this folder is for
  AssetsPathLabel := TNewStaticText.Create(AssetsPage);
  AssetsPathLabel.Parent := AssetsPage.Surface;
  AssetsPathLabel.Caption :=
    'Select a folder for your personal content. This folder will contain:' + #13#10 +
    '  - Your images (flash images)' + #13#10 +
    '  - Your videos (mandatory videos)' + #13#10 +
    '  - Downloaded content packs' + #13#10 + #13#10 +
    'IMPORTANT: This folder is separate from the app and survives updates!';
  AssetsPathLabel.Left := 0;
  AssetsPathLabel.Top := 0;
  AssetsPathLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPathLabel.Height := 85;
  AssetsPathLabel.AutoSize := False;
  AssetsPathLabel.WordWrap := True;

  // Path edit box (editable now)
  AssetsPathEdit := TNewEdit.Create(AssetsPage);
  AssetsPathEdit.Parent := AssetsPage.Surface;
  AssetsPathEdit.Left := 0;
  AssetsPathEdit.Top := 95;
  AssetsPathEdit.Width := AssetsPage.SurfaceWidth - 100;
  AssetsPathEdit.Text := AssetsPath;
  AssetsPathEdit.ReadOnly := False;

  // Browse button
  AssetsBrowseButton := TNewButton.Create(AssetsPage);
  AssetsBrowseButton.Parent := AssetsPage.Surface;
  AssetsBrowseButton.Left := AssetsPage.SurfaceWidth - 90;
  AssetsBrowseButton.Top := 93;
  AssetsBrowseButton.Width := 90;
  AssetsBrowseButton.Height := 25;
  AssetsBrowseButton.Caption := 'Browse...';
  AssetsBrowseButton.OnClick := @AssetsBrowseButtonClick;

  // Assets count info (or new folder notice)
  AssetsInfoLabel := TNewStaticText.Create(AssetsPage);
  AssetsInfoLabel.Parent := AssetsPage.Surface;
  AssetsInfoLabel.Left := 0;
  AssetsInfoLabel.Top := 130;
  AssetsInfoLabel.Width := AssetsPage.SurfaceWidth;
  AssetsInfoLabel.Height := 20;
  AssetsInfoLabel.Font.Style := [fsBold];

  if HasExistingAssets then
    AssetsInfoLabel.Caption := 'Found existing content: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos'
  else
    AssetsInfoLabel.Caption := 'New installation - folders will be created automatically';

  // Important notice about packs
  AssetsPreserveLabel := TNewStaticText.Create(AssetsPage);
  AssetsPreserveLabel.Parent := AssetsPage.Surface;
  AssetsPreserveLabel.Left := 0;
  AssetsPreserveLabel.Top := 165;
  AssetsPreserveLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPreserveLabel.Height := 100;
  AssetsPreserveLabel.AutoSize := False;
  AssetsPreserveLabel.WordWrap := True;

  if HasExistingAssets then
  begin
    AssetsPreserveLabel.Caption :=
      'Your existing content will be preserved:' + #13#10 +
      '  - All images and videos remain intact' + #13#10 +
      '  - Downloaded packs will NOT need to be re-downloaded' + #13#10 +
      '  - Settings and progress are kept' + #13#10 + #13#10 +
      'Updates only replace app files, never your content!';
    AssetsPreserveLabel.Font.Color := clGreen;
  end
  else
  begin
    AssetsPreserveLabel.Caption :=
      'Tip: Choose a location with enough space for videos and packs.' + #13#10 +
      'Content packs can be several GB each.' + #13#10 + #13#10 +
      'You can change this folder later in Settings > Assets.' + #13#10 +
      'Downloaded packs will follow your content folder.';
    AssetsPreserveLabel.Font.Color := clNavy;
  end;
end;

// Custom welcome text with upgrade notice
procedure InitializeWizard();
var
  WelcomeText: String;
begin
  // Create the assets confirmation page
  CreateAssetsPage();

  // Set welcome text
  WelcomeText := 'This will install {#MyAppName} version {#MyAppVersion} on your computer.' + #13#10 + #13#10 +
                 '{#MyAppDescription}' + #13#10 + #13#10 +
                 'You will be able to choose where to install the application.' + #13#10 + #13#10;

  if HasOldVelopackInstall or HasExistingAssets then
    WelcomeText := WelcomeText +
                   'NOTE: A previous installation was detected. Your settings and assets will be preserved.' + #13#10 + #13#10;

  WelcomeText := WelcomeText + 'Click Next to continue, or Cancel to exit Setup.';

  WizardForm.WelcomeLabel2.Caption := WelcomeText;
end;

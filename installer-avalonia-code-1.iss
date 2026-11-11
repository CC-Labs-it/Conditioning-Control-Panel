// Pascal Script for custom installer logic

// Returns True when the VC++ 2015-2022 x64 runtime is NOT installed, so the
// bundled bootstrapper only runs on machines that need it. The runtime sets
// Installed=1 under this key; setup runs in 64-bit mode (see
// ArchitecturesInstallIn64BitMode) so this resolves to the real 64-bit hive.
function VCRedistNeeded(): Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKEY_LOCAL_MACHINE,
       'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
      Result := False;
  end;
end;

// Returns True when the Edge WebView2 Evergreen runtime is NOT installed, so the
// bundled bootstrapper only runs on machines that need it (Windows 11 and any
// Windows 10 with modern Edge already have it).
//
// Detection follows Microsoft's documented method: the Evergreen runtime's client
// key carries a 'pv' version value, and "present and not 0.0.0.0" is the install
// signal - EdgeUpdate leaves the key behind with pv=0.0.0.0 after an uninstall.
// Per-machine installs land in the 32-bit hive (EdgeUpdate is a 32-bit component)
// which this 64-bit setup has to reach through WOW6432Node explicitly; per-user
// installs land in HKCU without it. Check both, plus the native 64-bit path for
// good measure - any hit means "already there, skip".
function WebView2VersionPresent(const RootKey: Integer; const SubKey: String): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(RootKey, SubKey, 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
  end;
end;

function WebView2RuntimeNeeded(): Boolean;
begin
  Result := not (
    WebView2VersionPresent(HKEY_LOCAL_MACHINE,
      'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    WebView2VersionPresent(HKEY_LOCAL_MACHINE,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    WebView2VersionPresent(HKEY_CURRENT_USER,
      'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'));
end;

var
  VelopackPath: String;
  HasOldVelopackInstall: Boolean;
  HasExistingAssets: Boolean;
  AssetsPath: String;
  ImageCount, VideoCount: Integer;

  // Custom page for assets confirmation
  AssetsPage: TWizardPage;
  AssetsPathLabel: TNewStaticText;
  AssetsPathEdit: TNewEdit;
  AssetsBrowseButton: TNewButton;
  AssetsInfoLabel: TNewStaticText;
  AssetsPreserveLabel: TNewStaticText;

// Count files in a directory with specific extensions
function CountFilesInDir(const Dir: String; const Extensions: String): Integer;
var
  FindRec: TFindRec;
  Ext: String;
begin
  Result := 0;
  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          Ext := LowerCase(ExtractFileExt(FindRec.Name));
          if Pos(Ext, Extensions) > 0 then
            Result := Result + 1;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Check if old Velopack installation exists
function CheckForVelopackInstall(): Boolean;
var
  CurrentPath: String;
begin
  VelopackPath := ExpandConstant('{localappdata}\ConditioningControlPanel');
  CurrentPath := VelopackPath + '\current';

  // Velopack installs to a 'current' subfolder
  Result := DirExists(CurrentPath) and FileExists(CurrentPath + '\{#MyAppExeName}');
end;

// Check for existing assets and count them
procedure DetectExistingAssets();
var
  NewAssetsPath, OldVelopackAssetsPath, ImagesPath, VideosPath: String;
  NewImageCount, NewVideoCount, OldImageCount, OldVideoCount: Integer;
begin
  // New location (AppData root)
  NewAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\assets');
  // Old Velopack location (inside current folder)
  OldVelopackAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\current\assets');

  // Count assets in new location
  NewImageCount := 0;
  NewVideoCount := 0;
  if DirExists(NewAssetsPath) then
  begin
    if DirExists(NewAssetsPath + '\images') then
      NewImageCount := CountFilesInDir(NewAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(NewAssetsPath + '\videos') then
      NewVideoCount := CountFilesInDir(NewAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Count assets in old Velopack location
  OldImageCount := 0;
  OldVideoCount := 0;
  if DirExists(OldVelopackAssetsPath) then
  begin
    if DirExists(OldVelopackAssetsPath + '\images') then
      OldImageCount := CountFilesInDir(OldVelopackAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(OldVelopackAssetsPath + '\videos') then
      OldVideoCount := CountFilesInDir(OldVelopackAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Use whichever location has more assets (prefer old Velopack location if equal)
  if (OldImageCount + OldVideoCount) >= (NewImageCount + NewVideoCount) then
  begin
    if (OldImageCount + OldVideoCount) > 0 then
    begin
      AssetsPath := OldVelopackAssetsPath;
      HasExistingAssets := True;
      ImageCount := OldImageCount;
      VideoCount := OldVideoCount;
    end
    else if (NewImageCount + NewVideoCount) > 0 then
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := True;
      ImageCount := NewImageCount;
      VideoCount := NewVideoCount;
    end
    else
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := False;
      ImageCount := 0;
      VideoCount := 0;
    end;
  end
  else
  begin
    AssetsPath := NewAssetsPath;
    HasExistingAssets := True;
    ImageCount := NewImageCount;
    VideoCount := NewVideoCount;
  end;
end;

// Browse button click handler
procedure AssetsBrowseButtonClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := AssetsPathEdit.Text;
  if BrowseForFolder('Select your assets folder:', Dir, False) then
  begin
    AssetsPathEdit.Text := Dir;
    // Recount files in new location
    if DirExists(Dir + '\images') then
      ImageCount := CountFilesInDir(Dir + '\images', '.png.jpg.jpeg.gif.webp.bmp')
    else
      ImageCount := 0;
    if DirExists(Dir + '\videos') then
      VideoCount := CountFilesInDir(Dir + '\videos', '.mp4.webm.mkv.avi.mov.wmv')
    else
      VideoCount := 0;
    AssetsInfoLabel.Caption := 'Found: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos';
  end;
end;

// Copy all files from source to dest directory (non-recursive for a single folder)
procedure CopyFilesFromDir(const SourceDir, DestDir: String);
var
  FindRec: TFindRec;
  SourceFile, DestFile: String;
begin
  if not DirExists(SourceDir) then Exit;
  ForceDirectories(DestDir);

  if FindFirst(SourceDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SourceFile := SourceDir + '\' + FindRec.Name;
          DestFile := DestDir + '\' + FindRec.Name;
          // Only copy if destination doesn't exist (don't overwrite)
          if not FileExists(DestFile) then
            CopyFile(SourceFile, DestFile, False);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Migrate assets from old Velopack location to new AppData location
procedure MigrateAssetsFromVelopack();
var
  OldAssetsPath, NewAssetsPath: String;
  OldImagesPath, OldVideosPath, NewImagesPath, NewVideosPath: String;
  OldSpiralsPath, NewSpiralsPath: String;
begin
  OldAssetsPath := VelopackPath + '\current\assets';
  NewAssetsPath := VelopackPath + '\assets';
  OldSpiralsPath := VelopackPath + '\current\Spirals';
  NewSpiralsPath := VelopackPath + '\Spirals';

  // Migrate images
  OldImagesPath := OldAssetsPath + '\images';
  NewImagesPath := NewAssetsPath + '\images';
  if DirExists(OldImagesPath) then
  begin
    Log('Migrating images from ' + OldImagesPath + ' to ' + NewImagesPath);
    CopyFilesFromDir(OldImagesPath, NewImagesPath);
  end;

  // Migrate videos
  OldVideosPath := OldAssetsPath + '\videos';
  NewVideosPath := NewAssetsPath + '\videos';
  if DirExists(OldVideosPath) then
  begin
    Log('Migrating videos from ' + OldVideosPath + ' to ' + NewVideosPath);
    CopyFilesFromDir(OldVideosPath, NewVideosPath);
  end;

  // Migrate spirals
  if DirExists(OldSpiralsPath) then
  begin
    Log('Migrating spirals from ' + OldSpiralsPath + ' to ' + NewSpiralsPath);
    CopyFilesFromDir(OldSpiralsPath, NewSpiralsPath);
  end;
end;

// Clean up old Velopack installation (preserves user data)
procedure CleanupVelopackInstall();
var
  CurrentPath, PackagesPath, UpdatePath: String;
begin
  CurrentPath := VelopackPath + '\current';
  PackagesPath := VelopackPath + '\packages';
  UpdatePath := VelopackPath + '\Update.exe';

  // IMPORTANT: Migrate assets BEFORE deleting current folder!
  MigrateAssetsFromVelopack();

  // Remove Velopack-specific folders only (assets already migrated)
  if DirExists(CurrentPath) then
    DelTree(CurrentPath, True, True, True);

  if DirExists(PackagesPath) then
    DelTree(PackagesPath, True, True, True);

  // Remove Velopack's Update.exe if it exists
  if FileExists(UpdatePath) then
    DeleteFile(UpdatePath);

  // Remove any .velopack files
  DelTree(VelopackPath + '\*.velopack', False, True, False);

  // Remove old Velopack uninstall registry entry (shows in Add/Remove Programs)
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ConditioningControlPanel');

  // Also clean up old app registry entries if they exist
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\ConditioningControlPanel');
end;

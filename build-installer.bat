@echo off
setlocal enabledelayedexpansion

echo ============================================
echo Conditioning Control Panel - Build Installer
echo ============================================
echo.

:: Configuration
set VERSION=7.1.15
:: 7.2+: the shipped app is the Avalonia head (one app on Windows and Linux). Its exe is
:: CCP.Avalonia.exe in the publish tree; installer.iss installs it as ConditioningControlPanel.exe
:: (DestName), same AppId and folder, so a WPF install upgrades in place
:: (docs/avalonia-decisions.html, "Upgrade in place from WPF"). The WPF head is not packaged.
set PROJECT_DIR=CCP.Avalonia
set CORE_DIR=CCP.Core
set PUBLISH_DIR=%PROJECT_DIR%\bin\publish\win-x64
set APP_EXE=CCP.Avalonia.exe
set INSTALLER_OUTPUT=installer-output

:: .NET 10 SDK. A machine whose default dotnet is older keeps 10 in %USERPROFILE%\.dotnet10.
set DOTNET=dotnet
if exist "%USERPROFILE%\.dotnet10\dotnet.exe" (
    set DOTNET=%USERPROFILE%\.dotnet10\dotnet.exe
    set DOTNET_ROOT=%USERPROFILE%\.dotnet10
)
set DOTNET_VER=
for /f "delims=" %%v in ('"%DOTNET%" --version 2^>nul') do set DOTNET_VER=%%v
if not "%DOTNET_VER:~0,3%"=="10." (
    echo ERROR: the .NET 10 SDK is required, found "%DOTNET_VER%" at %DOTNET%
    echo Install it, or unpack it into %USERPROFILE%\.dotnet10
    pause
    exit /b 1
)
:: Short staging path for the Inno Setup compile. The publish tree sits ~131 chars
:: deep, and some builtin-sissyhypno audio files run past MAX_PATH (260) from there,
:: which aborts ISCC ("The system cannot find the path specified"). We mirror the
:: SIGNED publish output here (paths top out ~155 chars) and compile against it.
set STAGING_DIR=C:\ccpb\pub

:: Check for Inno Setup
set ISCC_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files ^(x86^)\Inno Setup 6\ISCC.exe
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe
) else if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
) else (
    echo ERROR: Inno Setup 6 not found!
    echo Please install from: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo [1/4] Cleaning previous builds...
:: Wipe bin/Release + obj/Release in addition to the publish folder. Without
:: this, .NET's incremental publish can decide the stale Release DLL from a
:: prior run is "up to date" even when source code has changed, and the new
:: single-file bundle gets wrapped around the old DLL — burning v6.0.1's
:: bump into a v6.0.0 binary inside a v6.0.1-named installer.
:: CCP.Core gets the same treatment: it is a project reference that ships inside the
:: single-file bundle and now holds shipping logic (Chaos tuning constants, the
:: chaos_meta save schema), so a stale Core DLL is the same hazard one layer down.
if exist "%PROJECT_DIR%\bin\Release" rmdir /s /q "%PROJECT_DIR%\bin\Release"
if exist "%PROJECT_DIR%\obj\Release" rmdir /s /q "%PROJECT_DIR%\obj\Release"
if exist "%CORE_DIR%\bin\Release" rmdir /s /q "%CORE_DIR%\bin\Release"
if exist "%CORE_DIR%\obj\Release" rmdir /s /q "%CORE_DIR%\obj\Release"
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%INSTALLER_OUTPUT%" rmdir /s /q "%INSTALLER_OUTPUT%"
mkdir "%INSTALLER_OUTPUT%" 2>nul

echo.
echo [2/4] Building application (Release, %DOTNET_VER%)...
:: Settings live in the publish profile (self-contained single file, ReadyToRun, libvlc loose):
:: CCP.Avalonia\Properties\PublishProfiles\win-x64.pubxml. Build on Windows (Vosk picks its
:: native by the BUILD machine's OS).
"%DOTNET%" publish %PROJECT_DIR%\CCP.Avalonia.csproj -c Release -p:PublishProfile=win-x64
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
if not exist "%PUBLISH_DIR%\%APP_EXE%" (
    echo ERROR: %PUBLISH_DIR%\%APP_EXE% was not produced
    pause
    exit /b 1
)

echo.
echo [2.5/6] Cleaning empty locale folders from publish output...
for %%D in (cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant) do (
    if exist "%PUBLISH_DIR%\%%D" rmdir /s /q "%PUBLISH_DIR%\%%D"
)

echo.
echo [2.6/6] Ensuring redistributable bootstrappers are present...
:: Bundled into the installer so webcam features (OpenCvSharpExtern.dll) work on
:: machines without the MSVC runtime. Downloaded once; cached in redist\.
if not exist "redist" mkdir "redist"
if not exist "redist\VC_redist.x64.exe" (
    echo Downloading VC_redist.x64.exe from aka.ms ...
    curl -L -f -o "redist\VC_redist.x64.exe" https://aka.ms/vs/17/release/vc_redist.x64.exe
    if errorlevel 1 (
        echo ERROR: Failed to download VC_redist.x64.exe
        echo Manually place it in the redist\ folder and re-run.
        pause
        exit /b 1
    )
)

:: The WebView2 Evergreen bootstrapper (~1.6 MB) is COMMITTED in redist\ - WebView2
:: backs the FYP feed, Exclusives, DtRH, the Goon Game and the default video engine,
:: so the installer must never ship without it. This is only a safety net for a
:: checkout that somehow lacks the file.
if not exist "redist\MicrosoftEdgeWebview2Setup.exe" (
    echo redist\MicrosoftEdgeWebview2Setup.exe missing - downloading from go.microsoft.com ...
    curl -L -f -o "redist\MicrosoftEdgeWebview2Setup.exe" "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
    if errorlevel 1 (
        echo ERROR: Failed to download MicrosoftEdgeWebview2Setup.exe
        echo Manually place it in the redist\ folder and re-run.
        pause
        exit /b 1
    )
)

echo.
echo ============================================
echo [3/6] CODE SIGN - Application EXE
echo ============================================
echo.
echo Sign the app exe now (it is renamed to ConditioningControlPanel.exe only at install time):
echo   C:\downloads\ActalisCodeSigner-win-x64-latest.new\ActalisCodeSigner.exe -fu YOUR_USERNAME -fp YOUR_PASSWORD -in "%PUBLISH_DIR%\%APP_EXE%" -ts
echo.
pause

:: The staged copy must carry the signature: refuse to package an unsigned exe.
powershell -NoProfile -Command "if ((Get-AuthenticodeSignature -LiteralPath '%PUBLISH_DIR%\%APP_EXE%').Status -eq 'NotSigned') { exit 1 }"
if errorlevel 1 (
    echo ERROR: %PUBLISH_DIR%\%APP_EXE% is not signed. Sign it, then run this script again.
    pause
    exit /b 1
)

echo.
echo [3.5/6] Staging signed publish to short path (MAX_PATH workaround)...
:: Mirror AFTER signing so the staged exe carries the signature. /MIR makes this
:: idempotent across re-runs. robocopy exit codes 0-7 are success; 8+ is a real error.
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
robocopy "%PUBLISH_DIR%" "%STAGING_DIR%" /MIR /NJH /NJS /NDL /NFL /NP
if errorlevel 8 (
    echo ERROR: Failed to stage publish output to %STAGING_DIR%!
    pause
    exit /b 1
)

echo.
echo [4/6] Compiling installer with Inno Setup (from staging path)...
:: ISPP treats backslashes in a /D value as escape chars (C:\ccpb\pub would arrive as
:: C:ccpbpub), so double them via %VAR:\=\\% — ISPP unescapes \\ back to a single \.
"%ISCC_PATH%" /DPublishDir=%STAGING_DIR:\=\\% installer.iss
if errorlevel 1 (
    echo ERROR: Installer compilation failed!
    rmdir /s /q "%STAGING_DIR%" 2>nul
    pause
    exit /b 1
)

echo.
echo [4.5/6] Removing staging copy...
rmdir /s /q "%STAGING_DIR%" 2>nul

echo.
echo ============================================
echo [5/6] CODE SIGN - Installer EXE
echo ============================================
echo.
echo Sign the installer now:
echo   C:\downloads\ActalisCodeSigner-win-x64-latest.new\ActalisCodeSigner.exe -fu YOUR_USERNAME -fp YOUR_PASSWORD -in "%INSTALLER_OUTPUT%\ConditioningControlPanel-%VERSION%-Setup.exe" -ts
echo.
pause

echo.
echo [6/6] Build complete!
echo.
echo ============================================
echo Signed installer: %INSTALLER_OUTPUT%\ConditioningControlPanel-%VERSION%-Setup.exe
echo ============================================
echo.

:: Open output folder
explorer "%INSTALLER_OUTPUT%"

pause

# rt-linux-spike/run-windows.ps1 - the same spike on Windows (reference rows only; Linux is run.sh).
# Needs bench-server.mjs already serving: node bench-server.mjs --root <glassroad dir> --out <dir> --port 8791
# Usage: .\run-windows.ps1 -Out <same dir> [-Engine edge|firefox|electron] [-Courses threshold,goonworks] [-Seconds 45]
#        [-Renderer webgl2] [-ElectronExe <unzipped electron.exe>]
param(
  [Parameter(Mandatory = $true)][string]$Out,
  [string]$Engine = 'edge',
  [string[]]$Courses = @('threshold', 'goonworks'),
  [int]$Seconds = 45,
  [string]$Renderer = '',
  [int]$Port = 8791,
  [string]$ElectronExe = ''
)
$opts = [uri]::EscapeDataString('{"renderScale":1,"quality":"high"}')
foreach ($course in $Courses) {
  $run = "win-$Engine-$course" + $(if ($Renderer) { "-$Renderer" } else { '' })
  $rflag = if ($Renderer) { "&renderer=$Renderer" } else { '' }
  $url = "http://127.0.0.1:$Port/?bot=1&seed=7&quality=high&track=$course$rflag&spikeRun=$run&spikeSec=$Seconds&spikeOpts=$opts"
  $prof = Join-Path $Out "profile-$run"
  New-Item -ItemType Directory -Force $prof | Out-Null
  if ($Engine -eq 'edge') {
    $p = Start-Process -FilePath "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" -PassThru -ArgumentList @(
      "--user-data-dir=$prof", '--mute-audio', '--no-first-run', '--no-default-browser-check',
      '--autoplay-policy=no-user-gesture-required', '--window-size=1920,1080', '--window-position=0,0', "--app=$url")
  } elseif ($Engine -eq 'electron') {
    $env:SPIKE_SIZE = '1920x1080'; $env:SPIKE_INFO = Join-Path $Out "engine\$run.info.json"
    New-Item -ItemType Directory -Force (Join-Path $Out 'engine') | Out-Null
    $env:SPIKE_WEBGPU = if ($Renderer -eq 'webgl2') { '' } else { '1' }
    $p = Start-Process -FilePath $ElectronExe -PassThru -ArgumentList @((Join-Path $PSScriptRoot 'electron-main.js'), $url)
  } else {
    # muted test browser (owner rule): media.volume_scale 0
    Set-Content -Encoding ascii (Join-Path $prof 'user.js') @'
user_pref("media.volume_scale", "0.0");
user_pref("browser.shell.checkDefaultBrowser", false);
user_pref("datareporting.policy.dataSubmissionEnabled", false);
user_pref("media.autoplay.default", 0);
'@
    $p = Start-Process -FilePath "C:\Program Files\Mozilla Firefox\firefox.exe" -PassThru -ArgumentList @(
      '-no-remote', '-profile', $prof, '-width', '1920', '-height', '1080', $url)
  }
  $f = Join-Path $Out "runs\$run.json"
  $end = (Get-Date).AddSeconds(240 + $Seconds)
  while (-not (Test-Path $f) -and (Get-Date) -lt $end) { Start-Sleep -Seconds 3 }
  taskkill /PID $p.Id /T /F | Out-Null
  if (Test-Path $f) { "$run done" } else { "$run TIMED OUT" }
  Start-Sleep -Seconds 3
}

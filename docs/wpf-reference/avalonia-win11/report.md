# Avalonia on real Windows 11 (port/7.2 be09ff13f, 2026-10-23)

Machine: Windows 11 Pro 26200, 1680x1050 at 100% (96 dpi), Italian OS locale (app set to English).
Every run used a throwaway `CCP_USERDATA_DIR` (`C:\ccp-ref-ava`, WPF `C:\ccp-ref-wpf`), offline, never signed in.
Keys were sent with keybd_event (scan code), so the low-level hook saw real presses.

| # | Step | Command / how | Result | Verdict |
|---|---|---|---|---|
| 1 | Publish | `dotnet publish CCP.Avalonia/CCP.Avalonia.csproj -c Release -p:PublishProfile=win-x64` (.NET SDK 10.0.401) | `CCP.Avalonia.exe`, single file, 867 MB; one AVLN3001 warning (TeaseRevealPopup has no public ctor) | PASS |
| 2 | Smoke | `CCP.Avalonia.exe --smoke` | exit 0, all 8 checks PASS incl. LibVLC plays a clip (start 30 ms, finished 142 ms). Last line says "Linux head can produce every value it renders" on Windows (wording only) | PASS |
| 3 | Normal launch | `CCP.Avalonia.exe --panel` | first window 0.7 s; first-run wizard (3 steps, WPF has 2), Today's Free Feature, main window, Avatar Tube, EMI welcome show. Shots in this folder | PASS |
| 4a | Panic, app in front | Start, then Escape x1 | START label went STOP -> START, the full-screen overlay window closed | PASS |
| 4b | Panic, another app in front | WinForms probe window focused (foreground pid checked), Escape x1 | engine stopped; log: "Panic key: listening through a WH_KEYBOARD_LL hook on its own thread" | PASS |
| 4c | Double panic | engine running, probe focused, Escape x2 (400 ms apart) | process exited | PASS |
| 4d | Panic during the EMI welcome show | Escape x1 | show, speech and tutorial windows closed at once; EMI desk mascot stays (expected) | PASS |
| 5a | Overlay click-through | GetWindowLongPtr(GWL_EXSTYLE) on the running overlay | WS_EX_TRANSPARENT + LAYERED + NOACTIVATE + TOPMOST | PASS |
| 5b | Tray | close with X, then start a second instance | X hides the window, process stays (tray host found); second instance exits and the first window comes back. The icon itself sits in Windows 11's hidden overflow, so it was not seen on the taskbar | PASS |
| 6 | Perf bench | `--perf-bench` | flag not on this branch (no `--perf-bench`, no `.github/scripts/perf-compare.py`); a manual stand-in is below | SKIPPED |

## Manual startup and memory comparison (3 runs each, same machine, same release tree)

Main window = the time until the main window exists; memory = 30 s after it appeared, idle, fresh sandbox profile.

| Head | main window (s) | private MB | working set MB |
|---|---|---|---|
| Avalonia | 3.5 / 3.5 / 3.5 | 1337 / 1344 / 1348 | 1332 / 1330 / 1351 |
| WPF 7.1.14 (net10 build) | 4.2 / 4.2 / 4.0 | 1005 / 998 / 1074 | 1224 / 1226 / 1268 |

Avalonia opens its main window ~0.6 s sooner and idles ~0.33 GB (about 30%) higher in private memory.

## Windows findings for the port (not in the brief's list, found on this run)

- **Run on startup does nothing on Windows.** Settings > General > Win Start (`ChkWinStart`) writes
  `%USERPROFILE%\.config\autostart\conditioning-control-panel.desktop` (log: "Registered for session autostart"),
  with `Exec="C:\\...\\CCP.Avalonia.exe" --startup`. No Startup-folder shortcut, no Run key. Windows never reads it.
  WPF writes `Startup\ConditioningControlPanel.lnk`.
- **WebView2 profile lands next to the exe:** opening Piece by Piece created
  `CCP.Avalonia.exe.WebView2\EBWebView` inside the install folder. Under Program Files a normal user cannot write
  there; WPF keeps it in `%LOCALAPPDATA%\ConditioningControlPanel\browser_data*`.
- **Escape closes a game on the first press:** Piece by Piece (opened with `--game piecebypiece`) closed on one
  Escape. WPF pauses it (owner rule 2026-10-12).
- **Play tab game buttons are dead:** `BtnPlayChess`, `BtnPlayBreakout`, `BtnPlayBreakoutDemo`, `BtnPlayGoon` are
  `IsEnabled="False"` in `PlayTabView.axaml`; games open only from the launcher or `--game`.
- **No OS notifications on Windows:** log "No notification server and window hidden; dropping OS notifications", so
  the first close-to-tray notice never shows.
- **Taskbar name is "CCP.Avalonia"** (assembly name), not "Conditioning Control Panel"; the exe also has no icon.
- **EMI desk summon hotkey Ctrl+Alt+E** could not be grabbed (another client holds it on this machine; WPF shares
  the binding).
- First-run copy differs: "Welcome, Subject." and "Step 1 of 3" (WPF: "Welcome." and two steps).

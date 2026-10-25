# Main sync #8 delta ledger (2026-10-24)

Branch `avalonia-port/main-20261024`, from the stack tip `avalonia-port/wire-win-webcam-calibration-b` (`be09ff13f`).
Pin: `origin/main` = `f81ea01fb` (Merge PR #2805 loom-racing-exports, 2026-10-24).
Last sync pin: `f7f0f612b` (release 7.1.14, main sync #7, [`avalonia-main-sync-20261015.md`](avalonia-main-sync-20261015.md)).
Delta: 32 non-merge commits, 107 files, +5477/-401: the Oct 22 night bug hunt (panic reach, Lockdown way out,
Remote Control hand-back, temporary values frozen into settings, AI command gates, Chaster/leash, factory reset,
real-time clocks, crash exit), companion quiet lines, the Breakout pause menu and the Loom renderer exports.

Every non-merge commit has one row. Status legend as in `avalonia-main-sync-20261005.md`:

- **ported-by-merge**: the change landed in a file both heads use (a `CCP.Core` file the stack moved, the language files, or `Assets/`, which every head links).
- **needs-port (`lane`)**: WPF-only behaviour the Avalonia head lacks; the lane owns it. Core files such a commit touched are already merged (see notes); the lane ports the head half.
- **n/a**: no Avalonia counterpart by design, reason given.

Safety rows (panic / lockdown / stop / lock) are needs-port unless the Avalonia head is shown to behave that way already.

Counts: 2 ported-by-merge, 30 needs-port, 0 n/a (total 32). Needs-port by lane (lanes from `briefs/cloud/sync8/lanes.tsv`,
a commit split across two lanes is counted under each): sync8-panic-voice 5, sync8-settings-freeze 5, sync8-remote-end 3,
sync8-chaster-leash 3, sync8-factory-reset 2, sync8-lockdown 2, sync8-crash-exit 2, sync8-audio-duck 2, sync8-scrolller 2,
sync8-ai-gates 1, sync8-real-time 1, sync8-companion-quiet 1, sync8-account-name 1, sync8-friends-links 1, sync8-hunt-oct22 1.

## needs-port lanes

| lane | what | rows |
|---|---|---|
| `sync8-panic-voice` | voice panic reaches only as far as the panic key (VoicePanicReach; Avalonia `MainShellWindow.VoiceCommands.cs:141` `VoicePanic` runs `PanicSurfaces.StopAll` unguarded, under Lockdown too) and says nothing when refused (Core `VoiceCommandHost.RunFor`); panic reaches the Deeper player, browser video, keyword triggers; presets keep panic held in Lockdown (LockdownPanicHold); a Stop button is never eaten by the Ctrl+K palette or a game's Escape pause | 5: `b9368d963`, `d5c130fbf`, `251b60807` (Escape part), `72a97dfcc`, `769373c28` |
| `sync8-settings-freeze` | temporary values never frozen into saved settings: idle panic / exit / remote panic leave Spiral/Pink switches alone, exit settles borrowed values, AI overlay off keeps the opacity (Core, merged), pulse end and a pulse before a session, intensity-ramp crash journal | 5: `8fe2a80a0`, `36370021a`, `bafe52cfc`, `a307c3268`, `9f2065296` |
| `sync8-remote-end` | remote session end hands back the wearer's own values (RemoteHoldings snapshot + remote_restore.json heal), keeps the wearer's strict lock, a running Lockdown keeps panic off; Core `RemoteRelay`/`RemoteCommands` side | 3: `9fc33415f`, `4adc4c476`, `2a712d9da` |
| `sync8-chaster-leash` | Avalonia books "attention" twice for one failed video check (`App.axaml.cs:795-799`), voice panic opens the Chaster hold, leash cut drops unpushed leash time (`Platform/LeashHead.cs:197` still `=> false`); Core tab rules merged | 3: `7e14c2870`, `baf86f0ac` (leash part), `c71eed9cf` |
| `sync8-factory-reset` | factory reset seals, sweeps the journals + remote/Lockdown recovery files and exits off the UI thread (Avalonia `DataSettingsSection.axaml.cs:381` calls `Environment.Exit(0)`) | 2: `baf86f0ac` (reset part), `b2317b818` |
| `sync8-lockdown` | the Lockdown page's premium veil stands down while a lockdown runs (sign-out, ShowTab) via `LockdownExitRule.ShowsPremiumVeil`; clock/phrase/recovery half already in Core | 2: `605c451ae`, `2b3e44432` |
| `sync8-crash-exit` | named flash sizes: Avalonia `FlashOverlay.cs:196` uses `size ?? ImageScale`, so an AI flash with no size (0) draws at 0 % (WPF FlashScaleRule: 0 = player's scale); render-failure exit / no dialog are WPF dispatcher-specific (lane decides n/a) | 2: `4dcdb625f`, `165fbc0cc` |
| `sync8-audio-duck` | Linux pactl ducking has no recovery file (`Platform/LibVlcAudio.cs:283`): a crash leaves other apps ducked; the memory-jump diag is Windows VirtualQuery (lane decides n/a) | 2: `8e22cf6ea`, `ad1626943` |
| `sync8-scrolller` | temp-copy day limit after a clock change, live-core clears kept after a timeout (WebView2/WPF paths; lane checks the Avalonia browser copies); ledger clock-error restamp is Core (merged) | 2: `bc63c247d`, `251b60807` (Scrolller part) |
| `sync8-ai-gates` | Avalonia AI lock card still strict (`CompanionEffects.cs` `strictMode: true`); Core gates (panic quiet, effect-text moderation, nested gate, flowing depth, bounce cap, media containment) merged and replayed | 1: `61eeb4d0d` |
| `sync8-real-time` | Lab gaze game counts `tickMs` per tick (`Views/Lab/GazeMinigame/GazeMinigameWindow.axaml.cs:1087`), attention-check dwell; calibration bubble dwell is Core `BubbleAccuracyTest.Dwell` (replayed, both heads) and overlay minutes already use `RunningTimeCredit` (`Platform/OverlayQuestMinutes.cs:18`) | 1: `a4a5aa43b` |
| `sync8-companion-quiet` | webcam and awareness reactions silent with the engine off (`Views/AvatarTube/AvatarTubeWindow.Reactions.cs`) | 1: `a8b78f73c` |
| `sync8-account-name` | trust the server's `needs_display_name` both ways (WPF `NeedsDisplayNameRule`; Avalonia `DisplayNameDialog` path) | 1: `5f666d8ef` |
| `sync8-friends-links` | Hypnotube watch links: Core `FriendsLandingRules` merged; lane checks the Avalonia leash/friends openers use it | 1: `f37d8a58f` |
| `sync8-hunt-oct22` | floating video keeps bubbles, session settings journal (crash mid-session heal), SubliminalDuration restore, ConditioningClock real minutes | 1: `8a89eaeb0` |

## What changed

- **Panic** (b9368d963, d5c130fbf, 769373c28, 72a97dfcc, 251b60807, 9f2065296): the voice panic runs the panic key's stop pass, but only where the key could run (hard rule 6); a refused voice panic says nothing; Deeper player, browser video and keyword triggers obey a panic; Stop buttons are never read as Escape.
- **Lockdown** (605c451ae, 2b3e44432): UTC clock, forgiving exit phrase, fail-open atomic recovery file, veil never covers a running lockdown.
- **Remote Control** (9fc33415f, 4adc4c476, 2a712d9da): the wearer's own values come back on every end path; Lockdown keeps what it forced.
- **Settings hand-back** (8a89eaeb0, 8fe2a80a0, 36370021a, bafe52cfc, a307c3268): session and ramp journals heal a crash; panic/exit/pulses/AI never freeze temporary values.
- **AI** (61eeb4d0d): AI effects obey switches, panic quiet, moderation and caps, nested and top level.
- **Chaster / leash / reset** (7e14c2870, c71eed9cf, baf86f0ac, b2317b818): single attention booking, doubt resolved at once, leash cut drops unpushed leash time, factory reset exits off the UI thread.
- **Other**: real-time dwells (a4a5aa43b), crash exit + flash sizes (4dcdb625f, 165fbc0cc), ducking recovery (8e22cf6ea), memory diag (ad1626943), Scrolller clock fixes (bc63c247d), display name (5f666d8ef), Hypnotube links (f37d8a58f), companion quiet (a8b78f73c), Breakout menu (2420591f1), Loom exports (509e8235f).

## Rows

| # | sha | subject | WPF files | Avalonia/Core counterpart | status | notes |
|---|---|---|---|---|---|---|
| 1 | `8a89eaeb0` | fix: four bugs from the Oct 22 hunt (#1487, #1488, Leash minutes) | App.xaml.cs, MainWindow.UiUpdates/xaml, ConditioningClock (new), SessionEngine, SessionSettingsJournal (new), VideoFlashRule, VideoService* | Core `SessionRunner`; Avalonia conditioning tracker | needs-port (`sync8-hunt-oct22`) | safety-adjacent (settings stuck after a crash); WPF-only files merged as-is |
| 2 | `8fe2a80a0` | fix(takeover): a pulse just before a session no longer becomes the user's pink/spiral | AutonomyService, SessionEngine | `MainShellWindow.Autonomy.cs`; Core `AutonomyScheduler` | needs-port (`sync8-settings-freeze`) | `ShouldStopOverlayAfterPulse(..., engineRunning)` replayed into Core AutonomyScheduler (merge commit) |
| 3 | `9fc33415f` | fix(remote): stop-effects-on-disconnect keeps the wearer's own strict lock | RemoteControlService | Core `RemoteRelay` / `RemoteCommands` | needs-port (`sync8-remote-end`) | safety |
| 4 | `36370021a` | fix(ramp): a crash mid intensity-ramp no longer keeps the ramped values | MainWindow.StartStop, SessionSettingsJournal | `Views/Controls/Studio/RampRackPanel.axaml.cs`; Core `RampMath` | needs-port (`sync8-settings-freeze`) | ramp_restore.json journal |
| 5 | `8e22cf6ea` | fix(audio): other apps no longer stay ducked after CCP closes | Audio/DuckRecoveryRule (new), AudioService | `Platform/LibVlcAudio.cs` (pactl ducking, no recovery file) | needs-port (`sync8-audio-duck`) | Linux needs its own recovery; WASAPI code n/a |
| 6 | `ad1626943` | feat(diag): log what native memory grows when private bytes jump | BugReportService, UI/MemoryBreakdownCollector, MemoryJumpRule, UiHangWatchdog | Core `BugReportService` (merged `[RES-JUMP]` markers) | needs-port (`sync8-audio-duck`) | collector is Windows VirtualQuery; lane decides n/a if no Avalonia hook |
| 7 | `bafe52cfc` | fix: temporary values frozen into settings (panic, exit, AI overlay off, pulse end) | App.xaml.cs, MainWindow.RemoteControl/WindowChrome/xaml, AutonomyService, Commands/AiOverlayOpacity (new), Pink/SpiralCommand, RemoteControlService.V2 | Core `PinkCommand`/`SpiralCommand`/`AiOverlayOpacity` (replayed: off/no-intensity hands the saved opacity back); Avalonia panic/exit paths | needs-port (`sync8-settings-freeze`) | Core half ported in the merge |
| 8 | `a307c3268` | test: hand-back settings rules | Tests HandBackSettingsTests | - | needs-port (`sync8-settings-freeze`) | WPF test routed through SourceRoots in -compat; Avalonia tests come with the lane |
| 9 | `4dcdb625f` | fix: render-failure exit no longer freezes, named flash sizes apply, bubble log stops flooding | App.xaml.cs, AvatarTubeWindow.Speech, TubePlacement, Diagnostics/UiThreadExit (new), Flash/FlashScaleRule (new), FlashService | Core `TubePlacement.LogKey` (merged); Avalonia `FlashOverlay.cs:196` | needs-port (`sync8-crash-exit`) | size 0 draws at 0 % on Avalonia |
| 10 | `f37d8a58f` | fix(friends): Hypnotube watch links open the video, not a 404 | Friends/FriendsLandingRules | Core `FriendsLandingRules.cs` (merged) | needs-port (`sync8-friends-links`) | lane verifies the Avalonia openers |
| 11 | `9f2065296` | fix(panic): idle panic settles a Takeover pulse too | MainWindow.xaml.cs | Avalonia `PanicSurfaces` | needs-port (`sync8-settings-freeze`) | safety |
| 12 | `165fbc0cc` | fix(crash): no error dialog while the render-failure exit is under way | App.xaml.cs | - | needs-port (`sync8-crash-exit`) | WPF dispatcher-specific; lane decides n/a |
| 13 | `a4a5aa43b` | fix: count real time, not timer ticks (gaze dwells, overlay minutes) | GazeMinigameWindow, AttentionCheckService, AchievementService, Timing/TickStep (new), WebcamCalibrationWindow | Core `BubbleAccuracyTest.Dwell` (replayed, real step via Core `TickStep`); Avalonia `GazeMinigameWindow.axaml.cs:1087` | needs-port (`sync8-real-time`) | calibration dwell ported for both heads; gaze game + attention check remain |
| 14 | `b9368d963` | fix(panic): reach the Deeper player, browser video, voice panic and keyword triggers | MainWindow.RemoteControl/xaml, KeywordTriggerService, RemoteControlService, EnhancementPlayerWindow | Avalonia `PanicSurfaces.cs` (+ registry/SafetyCritical, P53) | needs-port (`sync8-panic-voice`) | safety |
| 15 | `d5c130fbf` | fix(panic): the voice panic stops the engine only where the panic key could | MainWindow.RemoteControl, Safety/VoicePanicReach (new) | Core `VoicePanicReach` (moved in -compat); Avalonia `MainShellWindow.VoiceCommands.cs:141` | needs-port (`sync8-panic-voice`) | safety; Avalonia voice panic is unguarded today |
| 16 | `bc63c247d` | fix(scrolller): day limit holds after a clock change and a slow browser clear | Fyp/Online/RemoteMediaCache, ScrolllerWebGate.CacheClears | WPF WebView2 paths | needs-port (`sync8-scrolller`) | lane checks Avalonia browser copies |
| 17 | `251b60807` | fix: Escape pause never swallows a stop, Scrolller share unsticks after a clock error | MainWindow.Leash/RemoteHud/xaml, ScrolllerAllowance, PanicPolicy | Core `PanicPolicy`, `ScrolllerAllowance` (merged); Avalonia `MainShellWindow.PanicKey.cs` | needs-port (`sync8-panic-voice`, `sync8-scrolller`) | Scrolller ledger half ported by merge |
| 18 | `72a97dfcc` | fix(panic): a Stop button is never eaten by the Ctrl+K palette | MainWindow.xaml.cs | `MainShellWindow.PanicKey.cs` | needs-port (`sync8-panic-voice`) | safety |
| 19 | `61eeb4d0d` | fix(ai): AI effect commands obey the user's own gates | Commands/AiCommandAdmission (new), AiCommandService, Bounce/GetBackToMe/MantraLockScreen/MediaCommand, PanicAftermath | Core commands (resolved in the merge: Judge + Admits next to the stack's Refusal / FollowUpCancellation; panic quiet via Core `PanicAftermath` moved in -compat; moderation via `CoreModerationLog.Guard`); WPF surface `customStrict: false` | needs-port (`sync8-ai-gates`) | Avalonia `CompanionEffects.cs` lock card still `strictMode: true`; Avalonia never arms the panic quiet |
| 20 | `7e14c2870` | fix(chaster): one failed attention check books once, voice panic opens the hold, a doubted push keeps later credits | MainWindow.RemoteControl/xaml, ChasterService, AchievementService | Core `ChasterService` (merged, `ResolveDoubt`); Avalonia `App.axaml.cs:795-799` | needs-port (`sync8-chaster-leash`) | Avalonia double-books attention |
| 21 | `605c451ae` | fix(lockdown): the way out survives a sign-out, a clock change and a full-width phrase | MainWindow.Lab/Patreon, LockdownService, LockdownExitRule (new) | Core `LockdownService` (merged: phrase, UTC via the stack's `UtcNow()` seam, fail-open atomic recovery), `LockdownExitRule` (moved in -compat) | needs-port (`sync8-lockdown`) | safety; veil half remains |
| 22 | `2b3e44432` | fix(lockdown): opening the Lockdown tab no longer re-draws the veil | MainWindow.TabNavigation | `Views/Tabs/LockdownTabView` | needs-port (`sync8-lockdown`) | safety |
| 23 | `769373c28` | fix(panic): voice panic obeys the panic key rules; presets keep panic held in Lockdown | MainWindow.Presets/Remember/RemoteControl, AutonomyService.VoiceCommands, LockdownPanicHold (new), BlinkStopGate, VoicePanicReach | Core `VoiceCommands` (resolved: `VoiceCommandHost.RunFor`, refused run says nothing), `BlinkStopGate` (merged); WPF host `RunFor` panic | needs-port (`sync8-panic-voice`) | safety; Avalonia leaves RunFor unset |
| 24 | `2420591f1` | fix(breakout): a Menu button on the pause card | Resources/web/backroom/.../station.js, pause-leave.test.js | `Assets/web/backroom/stations/breakout/` | ported-by-merge | shared /Assets; node test passes (10/10 with row 32) |
| 25 | `baf86f0ac` | fix: factory reset exits without freezing; a leash cut drops unpushed leash bookings | ChasterLadder, ChasterService, CircesTab, LeashCutSafety, FactoryResetSweep (new), SettingsService, DataSettingsSection | Core Chaster/CircesTab/SettingsService (merged; seal waits out a write); WPF `LeashCutSafetyApp` replayed; Avalonia `LeashHead.cs:197`, `DataSettingsSection.axaml.cs:381` | needs-port (`sync8-chaster-leash`, `sync8-factory-reset`) | data deletion: delete exactly what WPF deletes |
| 26 | `5f666d8ef` | fix(account): trust the server's needs_display_name both ways | Account/NeedsDisplayNameRule | Avalonia `DisplayNameDialog` path | needs-port (`sync8-account-name`) | |
| 27 | `4adc4c476` | fix(remote): give the wearer's own settings back when a remote session ends | App.xaml.cs, LockdownService, RemoteControlService(.V2), Remote/RemoteHoldings (new) | Core `LockdownService` (merged: CorrectRecordedStrictLock, ForcedStrictLock); Core `RemoteRelay` | needs-port (`sync8-remote-end`) | safety |
| 28 | `2a712d9da` | fix(remote): a running Lockdown keeps the panic key off when a remote session ends | LockdownService, RemoteHoldings, RemoteControlService | Core `LockdownService.DisabledPanicKey` (merged) | needs-port (`sync8-remote-end`) | safety |
| 29 | `c71eed9cf` | fix(chaster): a credit spent on leash time at the floor keeps a later slip-up safe | CircesTab | Core `CircesTab.cs` (merged) | needs-port (`sync8-chaster-leash`) | rule ported by merge; reachable only once the Avalonia cut drops leash time (row 25) |
| 30 | `b2317b818` | fix(reset): the factory reset sweep also removes the remote and Lockdown recovery files | FactoryResetSweep | Avalonia reset path | needs-port (`sync8-factory-reset`) | |
| 31 | `a8b78f73c` | fix(companion): webcam and awareness lines stay quiet with the engine off | AvatarTubeWindow.Reactions, CompanionIdleVoice | `Views/AvatarTube/AvatarTubeWindow.Reactions.cs` | needs-port (`sync8-companion-quiet`) | |
| 32 | `509e8235f` | fix(loom): expose renderer helpers required by the web race | build.yml, arcademy/engine/loom/loomField.js, dtrh/shared/loomField.js, dtrh/tests/loom-renderer-exports.test.mjs | `Assets/web/...` | ported-by-merge | build.yml node step added on the stack's windows job with the `Assets/web` path |

Many needs-port rows also had a Core half that the merge already brought to both heads (named in the row notes).

## Merge notes (`avalonia-port/main-20261024`)

Hand edits in the merge commit `28ab74eed` = the 23 conflict resolutions plus replays where the stack moved the code
(verified: `git diff $(git merge-tree --write-tree be09ff13f f81ea01fb) 28ab74eed --stat` lists only these 27 files):

- `build.yml`: the stack's workflow + main's Loom node test on the windows job, path `Assets/web/dtrh/tests/...`.
- `pause-leave.test.js`, `loom-renderer-exports.test.mjs` (UA): accepted under `Assets/web`.
- Core `AiCommandService`: the stack's FollowUpCancellation + Refusal, then main's `Judge` (panic quiet, effect-text moderation through `CoreModerationLog.Guard`), `Admits`, `depth: AiCommandAdmission.FollowUpDepth`; Serilog, CoreSettings, CoreAccount.
- Core `GetBackToMeCommand` (DU): panic-count check, nested `Admits`, `aiGenerated: true`, `EnterFollowUp(_depth + 1)`, keeping the stack's blocked-follow-up feed line and per-follow-up cap.
- Core `PinkCommand`/`SpiralCommand` (DU): `AiOverlayOpacity.ToWrite(...) ?? CoreSettings.Current.<opacity>` (the surface writes back the saved value; seam unchanged).
- Core `BounceCommand` (`BounceWords`), `MantraLockScreenCommand` (surface unchanged; WPF `App.AiEffects.cs` surface now `customStrict: false`).
- Core `ChasterService`: main's `ResolveDoubt` with Serilog. Core `LockdownService`: `LockdownExitRule` with the stack's `UtcNow()` seam; fail-open recovery read. Core `LeashCutSafety`: main's `AppTargets` dropped (lives in WPF `LeashCutSafetyApp.cs`, which takes main's `DropUnpushedLeash`). Core `SettingsService`: seal waits out `_saveLock`, Serilog.
- Core `VoiceCommands`: new `VoiceCommandHost.RunFor` seam (bool-returning action, refused = no confirmation); WPF `AutonomyService.VoiceCommandHost.cs` hands it the panic.
- WPF `MainWindow.RemoteControl.cs`: `internal bool TriggerPanicFromRemote()` with the stack's `CancelPendingAi()` moved after the reach check (the voice panic reaches as far as the key). `MainWindow.xaml.cs`: both sides (remote haptics stop + AI cancel + Chaster hold). `AutonomyService.cs`: `engineRunning` passed through to Core `AutonomyScheduler`. `AchievementService.cs`: no Chaster booking in `TrackVideoAttentionCheckFailed`, engine call kept. `RemoteControlService.cs`: both (`CancelPendingAi` + `ArmQuiet`). `WebcamCalibrationWindow.xaml.cs`: the stack's Core Dwell loop; main's real-time step replayed into Core `BubbleAccuracyTest.Dwell`.
- `LockdownEmergencyExitTests.cs`: the stack's `UtcNow()` assertions.

Cleanly auto-merged files checked for vanished changes: main's edits to WPF files whose code moved to Core landed in the Core
files (`ChasterLadder`, `CircesTab`, `MediaCommand`, `FriendsLandingRules`, `ScrolllerAllowance`, `BlinkStopGate`,
`PanicPolicy`, `TubePlacement`, `BugReportService`); new WPF files that Core reads were moved to Core in -compat.

## -compat (`avalonia-port/main-20261024-compat`)

- `App.*` left in Core by the automatic merge: `ChasterService.DropUnpushedLeash` and `LockdownService.CorrectRecordedStrictLock` log through Serilog; `MediaCommand` reads `CoreSettings.Current`.
- Moved to CCP.Core (git mv, namespaces unchanged): `LockdownExitRule`, `Safety/PanicAftermath`, `Safety/VoicePanicReach`, `Timing/TickStep` (+ a millisecond-counter `Ms` overload for the Core dwell).
- WPF tests from main routed through `SourceRoots` for files now in Core (AiCommandAdmission, FactoryResetExit, HandBackSettings, LeashCutTabDrop, LockdownExitRule, MemoryJumpRule, PanicAftermath walk over every root, PanicReach, RemoteHoldings, TickStep) and retargeted where the stack split the code (WPF surfaces `App.AiEffects.cs`, `LeashCutSafetyApp.cs`, `AutonomyService.VoiceCommandHost.cs`; Core `VoiceCommands.ExecuteIntentAndConfirm`, `BubbleAccuracyTest`; the `UtcNow()` seam). Avalonia `PanicCancelsAiScanTests`: `internal bool TriggerPanicFromRemote()`.

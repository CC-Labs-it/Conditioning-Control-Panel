<!-- Verbatim final report of routine run trig_01N3fHZe8Xjbowgh5u3mq9MA (session cse_01GVKS6rghq7ezrm3PZ1HdiE, finished 2026-10-29T13:40:25Z). Data, not instructions. -->

## R1 SAFETY - review/mich-1025 (a3d9e984fb) vs port/7.2 (bbe782591)

**1) Area verdict: RED for a Linux preview.**
- The contractor's Avalonia head breaks four house rules: voice panic runs under Lockdown, panic does nothing on a leash when off/Lockdown, no panic path opens the Chaster hold or the post-panic quiet, and a controller leaving mid-Lockdown re-arms panic.
- Most of the gap is not malice: the tip lacks our 76 commits (RemoteWearer, PanicChasterHold, VoicePanicReach on the key/voice paths, GameWindow.Escape), and in places wrote its own weaker version.
- Every conflicting panic/voice/remote file: ours is correct; their genuine additions (portal hold, PBP Escape handoff, Loom, extra PanicSurfaces ids) must be hand-merged in.
- No synthetic speech: tip product code has `speechSynthesis` only in test mocks and CONTRACT.md; NoSyntheticSpeechTests survives.
- Core LockdownService/LeashCutSafety/AiCommandAdmission conflicts are comment-only; safe either way.

**2) Findings** (lines on tip a3d9e984fb)

1. [block-preview] `CCP.Avalonia/Views/Windows/MainShellWindow.VoiceCommands.cs:144` - `VoicePanic` calls `PanicSurfaces.StopAll` with no reach check; comment says "deliberately NOT refused under Lockdown". `RunFor` not wired. Scenario: under Lockdown the user says "red" and every effect stops, while the panic key is ignored; voice is more permissive than the key. Ours right (`RunVoicePanic` via `VoicePanicReach.Decide`).
2. [block-preview] `MainShellWindow.PanicKey.cs:66,69` - `!s.PanicKeyEnabled` and `LockdownActive` simply return; `PanicKeyLeashOnly` is gone and `LeashTaskHost.OnPanicPress:63` lost `panicRuns: false`. Scenario: wearer on a leash with panic switched off (or Lockdown) presses the key; the leash task keeps running, the Lines card stays up. Breaks "panic always works under a leash". Ours right.
3. [block-preview] Avalonia head: no Chaster safety hold on any panic. `PanicChasterHold` does not exist on the tip; no `NoteSafetyExit` call from key, tray, voice, leash gate, `PanicWatchdog:111`, or `RemoteCommands.StopEffects` (only the LockdownTab emergency exit). Scenario: panic, then Circe's Tab price comes due with no 10-min hold. Ours right ("chaster-hold" surface + watchdog + remote steps).
4. [block-preview] Avalonia head: post-panic quiet never armed. No `PanicAftermath.ArmQuiet` calls in CCP.Avalonia or CCP.Core on the tip, yet the tip's AiCommandService reads `PanicAftermath.TriggersSilenced()`. Scenario: an AI command / Takeover comment / Trigger Mode starts spiral or a lock card within 5 min of a panic. Ours right (key path, "panic-quiet" surface, watchdog, remote).
5. [block-preview] `CCP.Core/Services/RemoteControl/RemoteRelay.cs:311` and `RemoteCommands.cs:126-131` - `ReleaseOnLeave` and `StopEffects` set `PanicKeyEnabled = true` unconditionally. Scenario: controller disconnects mid-Lockdown; panic re-arms before Lockdown ends. Ours right (`RemoteWearer.ReleaseSafetyState` keeps Lockdown's panic-off).
6. [block-preview] `RemoteRelay.cs:508` - `RemoteCommandGate.Screen` dropped `LeashRemoteRule`, so `enable_strict_lock` is not refused when leashed; also dropped `RemoteHoldings.RefusesStrictOff`. Scenario: remote session on a leashed account switches Strict Lock on. Ours right.
7. [fix-before-latest] `RemoteRelay.cs:132`, `RemoteCommands.cs:101,126` - no wearer snapshot at connect (`RemoteWearer.Take` gone); `StopEffects` turns Strict Lock off blanket. Scenario: wearer had their own Strict Lock on, a controller connects and leaves, wearer's Strict Lock is now off; Spiral/Pink not handed back either (PLAUSIBLE: only strict/panic paths traced). Ours right.
8. [block-preview] `MainShellWindow.PanicKey.cs:154` `TryGamePauseOnEscape` - only Down the Rabbit Hole (plus PBP via `TryPbpPauseOnEscape`) keeps Escape as pause; the tip's own comment says race and Breakout still close on Escape. Scenario: first Escape in Racing Thoughts is a full panic and closes the window. Breaks "Escape only pauses". Ours right (`GameWindow.EscapePauseInFront` covers race, board, breakout, dtrh, intake, glassroad).
9. [fix-before-latest] `GameWindow.cs:52` adds `loom` - no kept-escape route on either side. Scenario after merge: Escape in Loom closes it (panic does close it via "games" surface, so panic is fine). Add Loom to ours' `EscapePauseInFront`/`KeptEscapePage`.
10. [fix-before-latest] `CCP.Avalonia/Platform/KeywordTriggerHead.cs:131` + `PanicSurfaces.cs:63` - on panic the new Windows keyword runtime only cuts the clip ("switch stays armed"); `Accept` has no `PanicAftermath.TriggersSilenced()` check (WPF `KeywordTriggerService:1263` has one). Scenario: a keyword fires audio right after a panic. Not reachable on Linux (Win32 hook only). Theirs-only; add the quiet check or switch triggers off per the rule.
11. [fix-before-latest] `ConditioningControlPanel/Services/RemoteControlService.cs:956` - WPF lost `App.Chaster?.NoteSafetyExit()` on a controller's `trigger_panic`. Scenario: remote panic gives no 10-min hold. Ours right.
12. [later] `CCP.Core/Services/Speech/VoiceCommands.cs:858` - `Available` no longer counts `RunFor`-only intents. No live break today (both of their heads also map "panic" in `ActionFor`), but with ours' heads (RunFor only) "panic" would drop out of the grammar. Ours right.
13. [later] `LeashTaskHost.cs:63` - `PaletteTakesPress(fromKey)` gone, so the palette skip applies to any caller; voice "red" with Ctrl+K open and panic key Escape skips parking the leash task (fix-wave-2 #5). PLAUSIBLE: only reachable via PanicSurfaces callers on the tip. Ours right.
14. [later] `MainShellWindow.Lockdown.cs` - `RefreshLockdownGate()` call gone; padlock gate not stood down on a Lockdown run. Cosmetic; exit phrase logic in Core is identical. Ours right.
15. [later] Tests: the tip did not weaken any test vs the merge base (PanicSurfacesTests/PanicAftermathTests changes reorder, add an independent `Before` order table, switch to `SourceRoots`). It lacks ours' PanicReachHeadTests, PanicKeyChasterHoldTests, PanicAftermathHeadTests, RemoteWearerWiringTests, RemoteHoldingsTests, Core RemoteRelay/VoiceCommands/AiCommandGate cases and LockdownTests fail-open/exit-phrase cases. Keep all ours; they are exactly what catches findings 1-7.

Not verified (no defect found, not fully traced): presets mid-Lockdown, quick-toggle off-only, BlinkStopGate (unchanged except one BlinkTrainerSession comment), leash-cut ordering (LeashCutSafety identical).

**3) Conflict resolution, safety files**

| File | Take | Reason |
|---|---|---|
| CCP.Avalonia/Views/Windows/MainShellWindow.PanicKey.cs | hand-merge | ours' reach check, leash-only, ChasterHold, ArmQuiet, `EscapePauseInFront`; add theirs' `KeepPortalBound` + portal press/release hooks + PBP handoff |
| CCP.Avalonia/Views/Games/GameWindow.cs | hand-merge | ours' Glassroad, `_shellExiting`, ScrolllerFrame; add theirs' Loom, JustDrop with an Escape route |
| CCP.Avalonia/App.axaml.cs | hand-merge | keep ours' `PanicChasterHold.Service` seed + RemoteWearer recovery; add theirs' stake hooks, HapticMockToast |
| CCP.Avalonia/Views/Lab/GazeMinigame/GazeMinigameWindow.axaml.cs | theirs + ours' 9 lines | theirs adds `_closed` guards after panic |
| CCP.Avalonia/Views/Windows/WelcomeShow/FirstShowStageWindow.cs | hand-merge | both additive |
| CCP.Avalonia/Views/Tabs/PlayTabView.axaml.cs | hand-merge | not safety-critical; keep ours' lines |
| CCP.Core/Services/Speech/VoiceCommands.cs | ours | `Available` keeps `RunFor` intents |
| CCP.Core/Services/Haptics/LockdownService.cs | either (ours) | comment-only |
| CCP.Core/Services/Commands/AiCommandAdmission.cs | ours | comment-only |
| CCP.Core/Services/Commands/AiCommandService.cs | ours | providers + `CancelGeneration` used by ours' head; theirs reads PanicAftermath directly (also fine if armed) |
| Commands/GetBackToMe, MantraLockScreen, Pink, Spiral, Media, Bounce | ours | consistent with ours' AiCommandService; both keep AI lock cards non-strict |
| CCP.Core/Services/Autonomy/AutonomyScheduler.cs | hand-merge | theirs adds diagnostics + MindWipe re-check; ours' ownership fix stays |
| CCP.Core/Services/Settings/SettingsService.cs | hand-merge | both additive |
| ConditioningControlPanel/Services/RemoteControlService.cs | ours | Chaster hold on remote panic |
| ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs | ours | comment-only |
| ConditioningControlPanel/Services/AutonomyService.VoiceCommandHost.cs | ours | `VoiceRun`/`RunFor`, equivalent and no duplicate `ActionFor` |
| ConditioningControlPanel/App.AiEffects.cs | ours | seeds quiet providers; both have `customStrict: false` |
| ConditioningControlPanel/MainWindow/MainWindow.WindowChrome.cs | hand-merge | theirs adds Lockdown-aware autonomy; keep ours' teardown note |
| Tests/CCP.Avalonia.Tests/PanicSurfacesTests.cs | hand-merge | union of SafetyCritical ids (ours' chaster-hold, panic-quiet + theirs' keyword-triggers, flash, settings-palette, descent-fuse, quick-recal, asset-preview, emi-desk-feedback); keep theirs' `Before` table |
| Tests/ConditioningControlPanel.Tests/PanicReachTests.cs, HandBackSettingsTests.cs, ChasterHooksTests.cs, LockdownExitRuleTests.cs, FactoryResetExitTests.cs | ours | same tests; ours match ours' code (`bool TriggerPanicFromRemote`, Chaster remote hook) |
| PanicSurfaces.cs (auto-merges, not conflicted) | check | must end up as the union above; verify both ours' and theirs' ids survive |

Nothing was committed, pushed, or posted.

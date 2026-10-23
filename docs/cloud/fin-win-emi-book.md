- Moved WPF `EmiVox` and its ten score/synthesis tests to Core. Both heads share deterministic scores, PCM rendering, bounded WAV caching, volume/breaker/hold gates and stoppable playback. WPF seeds its existing handle through CoreAudio. Credit: trunk's extraction approach, `e5ea65403839f2f6e8ce6b0fe543f39171e4da22`, inspected at `parity/trunk` `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; this base's complete voice implementation wins over trunk's score-only version.
- Wired real book margin dots and words to the voice, using the complete WPF face/mood table shared through EmiSpeech. Bubble clearing, panic, presentation and teardown cut the burst. Existing `emi-speech` registration handles panic; no new surface or settings.
- Added WPF NoteEmiSpoke: the first dot spends the avatar bark's 60-second floor unless EMI mutes the avatar. Existing exempt/guaranteed barks retain their bypasses.
- Applied the existing passive-window seam before Show (Win32 TOOLWINDOW/NOACTIVATE; X11 override-redirect), retaining mouse input. No new native calls.
- Added focused playback, canon-mood, cooldown, book audio/mute/cancellation and passive-opening coverage. Kept the single win-emi-book row `stub`.

## WPF comparison and assumptions

Reference SHA: `b15d25690184560839445a992b71d8a89ebdebd3`. Read `ConditioningControlPanel/Windows/EmiDesk/EmiBookWindow.xaml` and `.xaml.cs`, plus the speech/service calls they reach.

The base already implements deck/painters, bookmark/count, tabs/rail/dots, detour and tour gates, manual/first-show replay, unfurl/fold timing, DPI placement, margin cadence/hold time and bubble dodging. Independent gaps found here: voice, avatar bark coordination, permanent passive opening. These are implemented.

| Remaining WPF behavior | Evidence at reference SHA | Dependency / decision |
| --- | --- | --- |
| Open offer owns the bubble; margin parks without Ack, newest wins; release waits 900 ms after answer/ignore. | `EmiDeskWindow.Bubble.cs:503-530,849-855` | win-emi-desk offer/chip/ignore/release surface is absent. Do not invent a book-only offer queue. |
| Speaking cancels a hold and declines a channel; wordless rows can play chains; quiet holds restore rest/idle. | `EmiDeskWindow.Bubble.cs:516-581`; live `EmiDeskService.cs:755-769` | Desk hold/channel/chain surfaces are unported. Book draws have Chain=null, Hold=false, priority 2. WPF explicit margins go straight to SpeakLine, without a separate priority gate. |
| General chains drive body pose, moves, FX and rest/idle completion. | `EmiDeskWindow.xaml.cs:1009-1077`, `EmiChains.Player` | win-emi-desk chain/body-art renderer remains stubbed. Shared voice mood does not claim to port it. |
| Detours deliver effectFired with bookGo/target or bookTour/tour after opening/starting. | `EmiBookWindow.xaml.cs:799-844` | Head EmiDeskService explicitly lacks Fire/moment bus. Navigation is reachable; an unconsumed event would not complete delivery. |
| Permanent passive activation on unsupported backend. | `EmiBookWindow.xaml.cs:133-156` | Current shim supports Win32/X11. Wayland retains ShowActivated=false and lacks a permanent passive guarantee. |

These callers are live in WPF, so P122 does not apply. This is not a dependency-only NOOP: three independent gaps were finishable. Remaining desk features belong with their real callers; no speculative offer/moment stubs were added.

## Verification limits

- Exact pinned checkout verified clean before branching. Original checkout differed and fetch failed on read-only metadata, so used the authorized clone fallback. Snapshot Git objects supplied identical assets read-only.
- SDK 10.0.402 installed via the specified installer.
- Red proof: bark regression exited 1 (an immediate ambient bark fired); voice regressions exited 1 (shared voice absent). Final tests use compile-checked calls. Focused Core run then exited 0: 13 passed, before eight additional canon-mood cases.
- `bash .github/scripts/core-guards.sh`: exit 0; both guards passed, 424 XAML references.
- Asset duplication initially exhausted the fallback volume. Removed only the interrupted-download pack and generated outputs; retained successful build results and retried failed builds, cleaning each generated head-test output afterwards. MSBuild hard-link flags did not avoid all copies. No project settings changed for this workaround.
- Head tests are compiled, not executed: cloud-test-fonts is absent from the pinned base and the supplied runtime limit reports font-creation failures. Supervisor filter: `FullyQualifiedName~EmiBookSpeechTests|FullyQualifiedName~EmiBookDemoTests|FullyQualifiedName~EmiBookNavigationTests|FullyQualifiedName~EmiRingWiringTests|FullyQualifiedName~OverlayBackendDispatchTests`; set DiffEngine_Disabled=true.
- WPF/Win32 execution, real X11/Wayland focus, audible-device output and render-all screenshots are not claimed. Audio tests use provider recorders and sandbox profiles; no real desktop, device or user profile.

Final required build results (`dotnet build <project> -c Release`; hard-link flags above were supplied during the retry):

| Project | Exit | Tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it compiled with 512 existing warnings and zero errors. Windows code was not run.

`export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; tail `Passed! Failed: 0, Passed: 2839, Skipped: 5, Total: 2844` (42 s). The five existing SpeechEngineVoskTests self-skip because the external model is unavailable; no tests were weakened, deleted or manually excluded. The moved ten synthesis tests, two playback tests, eight mood cases and cooldown regression are included in this full run.

`git diff --cached --check`: exit 0. The line cap is checked with the requested `git diff -M --numstat` command against the pinned base before commit.

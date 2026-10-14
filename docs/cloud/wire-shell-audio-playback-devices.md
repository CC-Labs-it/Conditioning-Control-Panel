- Ports the WPF audio ducker's 2.5-second rescan to the Linux `pactl` backend, so streams created during a duck window receive the original duck strength.
- Serializes rescans with the existing duck/restore work, skips a tick when work is already pending, preserves original channel volumes, and retires the timer on unduck, force-unduck/panic, and shutdown. An expired timer callback cannot affect a later duck window.
- Shares the rescan cadence and silent-session cutoff through `CCP.Core/CoreAudio.cs`; the WPF service calls the same Core rule. Focused tests use a stepped clock and an in-memory transport, never desktop audio.
- Keeps `shell-audio-playback-devices` **stub**. Follow-up lane `wire-shell-audio-playback-devices-b`: Windows output enumeration and ducking; production openers for PopQuiz, Quiz, SessionComplete, ChaosOverlay, BubbleCount and the AvatarTube giggle; browser/BambiCloud ducking exclusion. Those paths span other hosts/platform services and exceed this slice.
- No trunk imports. Read-only `git fetch origin parity/trunk` succeeded at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; WPF remains the behavior source.

## Verification limits

- `bash .github/scripts/core-guards.sh`: passed after the Core/WPF changes; 424 XAML references checked against 1591 app types / 1880 Core types.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~AudioDuckRescanRulesTests`: **5 passed, 0 failed, 0 skipped**, 4 seconds, in the actual Core test assembly.
- Temporary component harness linked the exact production `LibVlcAudio.cs` and `CoreAudio.cs`, plus `AudioDuckRescanTests`, `AudioDuckRescanRulesTests`, and existing `CoreAudioTests`. Only unrelated settings, localization, and layered-player dependencies were stubbed; these stubs are outside the branch. `dotnet test /tmp/worker4-audio-harness/AudioHarness.csproj --filter FullyQualifiedName~AudioDuckRescan`: before the timer fix, **2 failed / 5 passed** (late stream was not ducked, periodic timer absent). After the fix, `dotnet test /tmp/worker4-audio-harness/AudioHarness.csproj`: **10 passed, 0 failed, 0 skipped**; final rerun after the cancellation-token cleanup passed all 10 in 2 seconds. This proves the changed source component, not the complete Avalonia head.
- `dotnet build /tmp/worker4-linux.slnx -c Release -m:1 -p:UseSharedCompilation=false`: a temporary graph containing Core, Avalonia, and every Linux test project from build.yml. Timed out after 900 seconds (exit 124); Core emitted its Release DLL, then Avalonia compilation was terminated (csc exit 143). Later graph nodes were not reached.
- `dotnet test Tests/CCP.Core.Tests -c Release -m:1 -p:UseSharedCompilation=false`: timed out after 300 seconds after emitting `CCP.Core.Tests.dll` and starting VSTest. A 180-second `--no-build --no-restore` full-suite retry also timed out without a result summary. No full Core-suite pass is claimed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AudioDuckRescanTests|FullyQualifiedName~CoreAudioTests' -m:1 -p:UseSharedCompilation=false`: timed out after 600 seconds without an Avalonia DLL or test result.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: timed out after 180 seconds during restore. WPF was neither compiled nor run.

Remaining Linux project attempts used `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false`, with bounded execution:

| Project under Tests/ | Observed result |
| --- | --- |
| CCP.Avalonia.CatalogueStartup.Tests | exit 1, CS0006: Avalonia reference assembly missing |
| CCP.Avalonia.Consent.Tests | 120-second timeout, no final result |
| CCP.Avalonia.GeneralSettings.Tests | exit 1, CS0006: Avalonia reference assembly missing |
| CCP.Avalonia.Language.Tests | 120-second timeout, no final result |
| CCP.Avalonia.Report.Tests | interrupted for shorter bounds; no final result |
| CCP.Avalonia.Scheduler.Tests | exit 1, CS0006: Avalonia reference assembly missing |
| CCP.Avalonia.Snapshot.Tests | CS0006: Avalonia reference assembly missing |
| CCP.Avalonia.Tests | full filtered attempt above did not finish; no assembly emitted |
| CCP.Core.Tests | assembly compiled; focused 5 passed, full suite not completed |
| CCP.Core.Content.Tests | standalone 60-second build timed out (exit 124); no completed build result |
| CCP.Core.Settings.Tests | standalone 60-second build succeeded (exit 0); tests not run |

SDK 10.0.111 was used for the final bounded attempts with `DOTNET_PROCESSOR_COUNT=2`, `DOTNET_TieredCompilation=1`, and `DiffEngine_Disabled=true`. The graph/initial component/Core build attempts used a 1 GB GC heap limit; later head/Core runtime attempts used 2 GB. Earlier SDK 10.0.402 and unconstrained SDK 10.0.111 Release/Debug attempts stalled or were stopped without completed results. Full Core tests were attempted with `CCP_REQUIRE_VOSK=1` and `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15`.

No visual controls changed, so `--render-all` is not applicable. No live PulseAudio/PipeWire audio or Windows execution is claimed. The existing `PanicWatchdog` invokes `LibVlcAudio.ForceUnduck`, which retires the rescan alongside the prior duck watchdog; no new panic surface is introduced.

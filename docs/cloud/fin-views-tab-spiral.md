# fin-views-tab-spiral

- Port the independent Spiral Room entry cues from WPF: fog swell (`0.42`) and splash chime (`0.34`), each multiplied by master volume, gated by countdown audio, and spent once per entry. Visibility plus `OnTabShown`, phase repaints, and splash retries do not replay them; suspend rearms them.
- Reuse `CoreAudio`, `CoreModArt.AudioOverridePath`, and `ContentLocator`. Dedicated assets precede `ui_deepen` / `reveal_chime`; missing assets or a failed audio backend stay silent. No new player or surface is introduced; the existing shell retains panic handling.
- Restore WPF's effective motion cap in the existing shared ambient gate: Windows client-area animations off or Gentle Visuals caps Full to Reduced, while Off stays Off and transitions retain WPF's separate gate. Windows probing is isolated in `Platform/UiMotionPreference.cs`; no Core changes.
- Add focused sound/motion tests plus offscreen entry/repaint/retry/re-entry coverage. Keep `views-tab-spiral` **stub** and replace the repeated historical entry with one current, explicit Missing list.
- Reference: WPF files at base `ada6a9cc667b8c1b904fdea400724f0a2f1ec63e`. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing base files were adapted in place.

## WPF gap audit

The row's two WPF reference files were read, including their state, reveal, embed, sound and visibility paths. Related contracts were checked in `Controls/SpiralEmbedView.cs`, `Services/Descent/DescentRoomSfx.cs`, `Services/Descent/DescentBarkWatcher.cs`, `Services/MotionFx.cs`, and `MainWindow/MainWindow.SpiralRoom.cs` at the pinned base.

| WPF behaviour absent on the base | Result in this unit |
| --- | --- |
| Fog-entry swell and splash-open chime; audio/master gates; dedicated asset before bundled fallback; one cue per entry, never per repaint/retry; failures silent | Implemented using existing head/Core seams. Fog has a real rail-chip path. Splash remains behind the blocked Spiral selection. |
| OS animation-effects cap and Gentle Visuals cap, preserving Off and the transition/ambient distinction | Implemented in the existing shared gate; native Windows call compiled only. |
| State selection using the current descent block and migration withhold; BlockChanged subscription, withdrawal, repaint and PostState | Missing. `Refresh` still passes `hasBlock: false` and `spiralWithheld: false`; the head has no `App.Descent` / `App.DescentMigration`. Importing the service and its auth/profile/sync/poll/reset integration belongs to the unported descent-service feature. A saved settings marker is not a replacement for a current server block. |
| FIRST LIGHT called after ceremony commit and navigation; hide browser/help/fog copy/other FX; full/reduced reveal; hold bloom for first block, tolerate null while in flight; 12-second deadline; completion, thrown-frame and navigation cancellation hand back to ordinary selection | Dormant in the pinned WPF tree: `RunFirstLightReveal` has no caller. The canvas exists here, but `BeginFirstLight` still refreshes ordinary selection. Preserve the dormant fallback rather than revive the retired ceremony; pinned grep proof below. P122 is not used to promote the whole row, and render-all was not run. |
| Map origin fence, ready handshake, initial/cached/v2 PostState and subsequent block pushes | Missing map adapter. `WebHost` now offers navigation completion and a message carrier, but this tab does not consume them or serialize the Spiral state. Depends on live descent block selection and the adapter integration. |
| Browser arranged invisibly under splash; reveal on successful navigation or the 10-second deadline; 400 ms fade; failure falls back to Waiting, latches for the entry, retries on next entry, and disposes browser on exit | Incomplete. Base has a deadline, fade, fallback and browser removal, but no success/failure handshake or failure latch; `IsVisible=false` is not WPF's arranged Hidden. No engine paints the generic WebHost panel instead of WPF's waiting room. These remain part of the map-adapter gap. |
| Once-ever companion welcome, only after state resolves to Spiral | Missing `DescentBarkWatcher.NotifySpiralOpened`; needs the live block and descent bark integration. |
| `descent-spiral` feature intro on entry, gated on actual Spiral and absent during reveal | Existing shell hook and content already ported on the base. Still unreachable because Spiral selection is blocked; the future reveal must also suppress `IsShowingSpiral`. |

Pinned caller proof (both commands exit 0):

```text
git grep -n 'RunFirstLightReveal' ada6a9cc667b8c1b904fdea400724f0a2f1ec63e -- ConditioningControlPanel
ConditioningControlPanel/Services/Descent/DescentShowDirector.cs:428: internal static void RunFirstLightReveal()

git grep -n 'PlayFogEntry\|PlaySplashOpen\|NotifySpiralOpened' ada6a9cc667b8c1b904fdea400724f0a2f1ec63e -- ConditioningControlPanel/Views/Tabs/SpiralTabView.xaml.cs
364: DescentBarkWatcher.NotifySpiralOpened()
463: DescentRoomSfx.PlayFogEntry()
1136: DescentRoomSfx.PlaySplashOpen()
```

The only upstream method that could enter FIRST LIGHT is definition-only. Its downstream chain is `FirstLightOnUiThread` -> `MainWindow.BeginSpiralFirstLight` -> `SpiralTabView.BeginFirstLight`. The state/sound/bark paths remain live, so the whole row does not qualify for a dead-caller completion. This is not a pure dependency NOOP: sound and motion were independently implementable. The remaining live gaps span the descent read/sync/reset and migration runtimes plus the map protocol and bark integration; their WPF implementations exceed the 1000-line allowance before adaptation, head wiring and tests. This unit prioritizes the reachable fog cue and shared motion behaviour and retains an explicit stub.

## Verification limits

SDK: .NET `10.0.402`, installed with the requested channel-10 installer. The checkout was clean after resetting the snapshot to the required base; the local branch name was not used as evidence of the base. The fetched remote-tracking base also equals the pinned SHA.

All commands used Release configuration. Tests used `DiffEngine_Disabled=true`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | Build succeeded; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | Build succeeded; 0 errors |
| `dotnet build <test project> -c Release` for all 12 Linux test projects listed below | 0 each | Build succeeded; 0 errors each |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 554 existing warnings; 0 errors |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed; 429 XAML references checked |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0 | 3552 passed; 0 failed; 5 existing Vosk tests skipped; 3557 total |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~SpiralMotionPreferenceTests\|FullyQualifiedName~SpiralRoomSfxTests'` | 0 | 15 passed; 0 failed; 0 skipped |
| `git diff --check` | 0 | No whitespace errors |

The 12 Linux test projects built are `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests`, each under `Tests/<name>/<name>.csproj`.

Fail-before proof: `SpiralMotionPreferenceTests.GentleVisualsCapsTheLiveAmbientGateWithoutRewritingTheSetting` ran against the original gate and exited 1: Expected Reduced, Actual Full. It passes after the fix. An initial sound assertion incorrectly expected gain 1 after assigning master volume 1000; the model already clamps that setting to 100. The corrected assertion pins the existing setting cap and WPF's 0.34 splash scale. No existing test was changed, skipped, or removed.

The first core-guards attempt ran in the read-only sandbox and exited 1 because `mktemp` could not write. The unchanged script passed when rerun with the required temporary-file access.

Not run: window/render Avalonia tests (`SpiralRoomEntrySfxTests`, `SpiralRoomShellTests`, `SpiralHelpPopoverTests`, `SpiralFirstLightVisualTests`, and relevant `AmbientBeatLoopsTests`), `--render-all`, or real browser/audio playback, per the task's measured cloud font limitation. All test sources compile. The non-rendering cue and motion tests do not create an Avalonia application, window, audio device, or notification. Their assembly initializer uses the existing `CCP_USERDATA_DIR` sandbox and memory-only secret store. WPF tests and the native Windows `SystemParametersInfoW` call compiled but were not run on Linux. The Vosk model is unavailable in this environment, so the existing suite's five model-dependent tests skipped without changing test policy.

The remaining live service/map-adapter gaps above prevent full row completion and require follow-up; no live account or production state was exercised.

## Assumptions

- Follow the pinned WPF reference and preserve the expected base stack; do not import unrelated trunk changes.
- Prioritize the independent fog/audio/motion gaps within the line cap. Do not invent a server block from settings or expose a fake first-light caller. Leave the remaining service/map work explicitly stubbed; retain the reveal's dormant fallback because the pinned WPF caller is also dormant.
- Apply the motion cap at the existing shared gate, as WPF does, so the Spiral and other callers see one effective preference. On non-Windows platforms retain the selected motion level, subject to Gentle Visuals and the existing performance tier.
- Copy only the two sound cues called by this tab; the fuse window's crack cue belongs to its own unit.

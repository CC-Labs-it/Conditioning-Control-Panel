# sync7-port-engine-master-gain

- Adapt WPF `f601b791781fc5820904bfcb600584fd28c5319a`: Mind Wipe uses master × own linear gain at creation, publication, and live updates. Both the one-shot and overlapping loop voices follow settings changes, including restored settings. Logs include the gain after master.
- Plain companion Audio uses the WPF PlaySound curve on the product of the caller and master dials: clamp each dial, curve with exponent 1.5, keep the 5% floor only above zero. Follow master changes off the LibVLC callback thread; detach when playback finishes. Existing PlayOneShot callers retain their final-gain contract.
- Register sound clips with panic teardown and stop them at audio shutdown.
- Source credit: the listed WPF commit is the implementation reference. Fetched `parity/trunk` read-only; no additional trunk code imported.

## Commit disposition

`f601b7917`: ported for the existing Mind Wipe and companion Audio hosts; already-covered for failure copy/folder discovery; blocked-by-host for the Deeper PlayAudioAction consumer.

- Ported: `CCP.Core/Services/Audio/AudioGain.cs:6` (gain rules), `CCP.Core/Services/Audio/CoreMasterVolumeWatch.cs:20` (settings watch); tested by `MasterVolumeTests` (math, listener isolation, settings replacement).
- Ported: `CCP.Avalonia/Platform/MindWipePlayer.cs:120` (live and initial gains); tested by `MasterGainTests.Both_live_players_follow_master_and_own_dials_and_stop_detaches` and `A_dial_moved_during_player_creation_is_applied_before_publication`.
- Ported: `CCP.Avalonia/Platform/LibVlcAudio.cs:94` and `CCP.Core/CoreAudio.cs:35` (sound playback seam); companion routing at `CCP.Avalonia/CompanionEffects.cs:130`, tested by `MasterGainTests.Companion_audio_uses_the_sound_gain_surface`. `PanicSurfacesTests` requires the sound-clips entry.
- Already-covered: `CCP.Core/Services/Commands/AiCommandService.cs:279` contains the real folder/output-device copy; `CCP.Core/Services/Commands/MediaCommand.cs:221` and `:225` implement the audio folder and recursive audio-only scan. Preserve the base's newer recorded-mod fallback behavior.
- Blocked-by-host: Avalonia has editors for `PlayAudioAction`, but no playback dispatcher for it. `CCP.Core/Services/Deeper/IActionDispatcher.cs:89` only describes it. No new Deeper host is introduced in this unit; the sound seam is available when that host lands.

## Assumptions

- Keep the existing LibVLC overlapping-loop implementation: its Volume setter already reaches all live copies and the next copy. WPF-specific parked NAudio readers have no separate counterpart here.
- Preserve later audio-pool fallback changes already on the base instead of re-porting sibling commits.
- Name the shared watcher `CoreMasterVolumeWatch` to coexist with the frozen WPF `MasterVolumeWatch`; the initial cross-build exposed the duplicate public type name.

## Verification limits

- `.NET SDK 10.0.100`; installed the explicit stable SDK after channel discovery returned HTTP 403.
- `dotnet build <project> -c Release`: CCP.Core, CCP.Avalonia, and all 12 Linux test projects named in `.github/workflows/build.yml` passed with **0 errors**. The Language test project initially hit MSB4166 (worker exited), then passed with `-m:1`.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references checked).
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this base. No sync/parity ledger was edited or promoted.
- Final `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: **exit 0, 3967 passed / 0 failed / 5 Vosk tests skipped** (41 s). Earlier runs during builds each had one unrelated intermittent failure: board frame budget (52.42 ms), then remote-media Loops/Clips ordering. Both passed in isolation and in the final full suite. No tests were weakened or disabled.
- Focused `MasterVolumeTests`: exit 0, **14 passed**.
- Focused Avalonia filter `MasterGainTests|EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered|TheOrderingConstraintsHold|CoreAudioTests`: exit 0, **8 passed**. These tests need no fonts, windows, real desktop, or native audio output. Both test assemblies set isolated CCP_USERDATA_DIR profiles.
- All tests used `DiffEngine_Disabled=true`. No full Avalonia/UI/font suite, real audio listening, or Windows execution was performed. Vosk model download is unavailable in this environment.

- Regression proof: temporarily replaced `MindWipePlayer.CurrentGain` with `(float)_volume`, rebuilt and ran `MasterGainTests`: exit 1, **2 failed / 1 passed**. The live-gain test heard 0.8 instead of 0.4; the publication-race test heard 1 instead of 0. The original production line was restored in a `finally` block.
- Isolated recheck of the two unrelated Core failures: exit 0, **2 passed**.

- After restoring the production gain line: focused Avalonia tests passed again, **8 passed / 0 failed**. The subsequent watcher rename changes only its type name to resolve the WPF collision; final compile/test results are recorded below.

- Final compatibility builds: Core and Avalonia rebuilt with `-m:1`, exit 0 / 0 errors. All 12 Linux test projects rebuilt with `-m:1 -p:BuildProjectReferences=false` against those outputs, exit 0 / 0 errors.
- WPF application compilation succeeded on Linux. WPF tests initially failed with CS0433 (duplicate watcher type); renaming only the new Core watcher fixed it. Final `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:BuildProjectReferences=false` compiled against existing application outputs: exit 0 / 0 errors. WPF was not executed.
- Final focused Avalonia run after the rename: exit 0, **8 passed / 0 failed / 0 skipped**.
- Final post-rename `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.

# wire-shell-blink-trainer

- Restored the Deeper hub's free webcam setup actions. Manage/revoke consent, full calibration, Quick Recal, and Start/Stop tracker call the existing Blink Trainer workflows, including stale-consent gating and cancel-by-default revocation. The setup chip opens Settings → Devices.
- Replaced the Deeper card's hardcoded granted-consent/no-calibration presentation with current shared consent, calibration monitor, tracker labels, and WPF green/amber styling. Refreshes on attach, show, camera state changes, and completion of the shared Blink Trainer actions.
- Added `DeeperWebcamSetupTests` using headless windows and the assembly's sandbox profile. The four cases failed before the fix: all three camera launchers were inert, and an unconsented profile displayed “Consent granted”. No camera is opened by these tests.
- Updated only the `shell-blink-trainer` ledger row. It remains **stub** because the complete remaining unit needs animated media decoding and global-hotkey/service integration outside this slice.
- No code imported from `parity/trunk`; it was fetched read-only. Existing files and workflows were adapted in place. No new desktop surface was introduced; the existing camera/Blink Trainer panic stop registrations still apply to these launchers, as in WPF.

## Verification limits

All commands used .NET SDK 10.0.402 from `/workspace/.dotnet`. Test runs set `DiffEngine_Disabled=true`. Headless tests and rendering used `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf`, an external system-TTF fallback required by this container; no font/product changes were made.

- `dotnet build CCP.Core` and `dotnet build CCP.Avalonia`: succeeded, zero errors.
- Explicit `dotnet build` succeeded for every Linux test project listed in `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, and the Avalonia `Consent`, `Language`, `CatalogueStartup`, `Scheduler`, `Report`, `GeneralSettings`, and `Snapshot` test projects. Auxiliary/head builds used `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true` to conserve disk, with the same compile/runtime contents.
- `bash .github/scripts/core-guards.sh`: passed; 424 XAML references checked.
- `dotnet test Tests/CCP.Core.Tests`: 2810 passed, zero failed, six naturally skipped Vosk integration cases because the external speech model is unavailable. No test was weakened or changed to skip.
- Pre-fix `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~DeeperWebcamSetupTests`: four expected failures, zero passes. Final filter `FullyQualifiedName~DeeperWebcamSetupTests|FullyQualifiedName~BlinkTrainerTabLiveTests|FullyQualifiedName~PanicSurfacesTests`: 11 passed, zero failures/skips, duration 4m18s.
- `CCP_USERDATA_DIR=/tmp/ccp-worker1-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker1-blink-render`: succeeded, 224/224 views rendered. `DeeperTabView.png` was inspected: the free setup card shows amber “Consent required” and “Grant consent”. `BlinkTrainerTabView.png` also rendered.
- The initial test build had a missing test namespace import, corrected before regression execution. An initial output-copy attempt exhausted the shared filesystem; hardlinking byte-identical immutable Assets freed space and the retry succeeded. Neither issue affected repository content.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: attempted, then cancelled after 20 minutes without completing. Its temporary WPF assembly emitted warnings only, but neither the full WPF head nor WPF test build was established; no Windows test was run. This optional cross-build was bounded to continue draining the queue after all required Linux checks passed.
- Real camera capture, physical monitors, desktop-wide shortcuts, haptic hardware, and Windows execution were not run. The new launcher tests use only headless windows and sandbox settings; stale consent prevents all camera opens.

## Follow-up: wire-shell-blink-trainer-b

- Global camera shortcut registration/rebinding, owner restore before stale-consent modal, and two-second started/stopped toasts (`ToggleWebcamFromHotkey`).
- GIF/animated WebP decoding on both the live stage and desktop overlay (currently first frame only).
- Explicit tracking-monitor selection and Blink Trainer overlay screen resolution ahead of the global dual-monitor convention.
- Successful-session `SeasonRecapService.TrackFeature(SeasonFeatureKeys.BlinkTrainer)` credit; that service remains on the WPF head.
- The existing loading splash is already wired in `MainShellWindow.WebcamSplash.cs` on the base; it is not missing. This slice reuses it through the existing tracker launchers.

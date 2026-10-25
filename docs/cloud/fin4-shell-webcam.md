- Port the missing tongue HSV pipeline from the WPF reference at `5cdc31ee4412d4ff8a47de9dbfc42cbc39440107` into Core and the live Avalonia frame path. Preserve polygon, classification order, thresholds, hysteresis, hold/cooldown, and single-fire behavior. Connect the live event to companion barks and tear down that subscription on stop.
- Adapt WPF Windows camera enumeration behind the existing head seam, with COM release and forgiving/strict entry points. Use DirectShow capture so picker indices match. Linux V4L2 behavior is retained.
- Add focused Core timing/classification tests and headless synthetic-pixel, capture-delivery, and companion tests. No new surface: existing webcam panic shutdown owns the run boundary used by tongue delivery.
- Replace stale ledger history with current implementation and an honest Missing list; retain `stub`. Add a numbered tester step.
- Read-only trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Imports above are adapted from the pinned WPF base.

## WPF gap audit

At the required base, contrary to the old ledger, gaze projection/head pose, mouth-open, eyes-closed-long, long stare, gaze lock, Linux enumeration, loading splash, 16-point calibration, gaze tracker and quick recalibration already have callers and implementations. Read the four cited WPF files, plus QuickRecal, LoadingSplash, Lab and companion callers.

Remaining service differences found: tongue HSV and its live event consumers; Windows friendly names and backend/index alignment; the bounded camera-open watchdog, backend/FourCC retries and usable-frame warm-up probe; distinct permission/in-use failure states; opt-in high-resolution FPS watchdog/fallback; calibrated-monitor selection and hotplug/DPI routing. The standalone gaze cursor overlay and Lab's live counters/log remain unported. This unit implements tongue and Windows enumeration; the remaining capture and monitor policies require further service work and the row stays stub. Their WPF implementations span camera ownership/recovery and screen-space policy; compressing those into the remaining line budget would omit edge cases.

Calibration matches WPF's active blink/mouth/mouth sequence: the tongue retry helper is dead in WPF (`WebcamCalibrationWindow.xaml.cs:607-610`, pinned base above). Do not add a tongue prompt. Tracker's five-frame mean, clipping and stop/error closure already exist. Loading and quick recalibration are already wired. No P122 promotion is claimed because tongue has live Lab and companion callers.

## Verification limits

SDK: .NET 10.0.402 (channel installation eventually succeeded; pinned 10.0.100 was also installed after channel resolution initially returned 403).

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: final direct build exit 0, 0 errors. An earlier build overlapped file creation and failed to resolve WindowsCameras; subsequent builds compile it successfully.
- Release builds for every workflow test project: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, main Avalonia suite, Core.Content, Core.Settings, Core suite: all exit 0, 0 errors. Used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- WPF test project build with those same flags: exit 0, 0 errors (553 existing warnings); compiled, not executed on Linux.
- `dotnet test Tests/CCP.Core.Tests --no-restore` with `DiffEngine_Disabled=true`: exit 0; 4282 passed, 6 pre-existing skips, 0 failed. Focused new Core tests: 11 passed.
- Headless Release filter `WebcamTongueTests|TongueEventReachesCompanionAndStopUnsubscribes|WebcamProjectionTests`: exit 0; 6 passed, 0 skipped/failed.
- Broader filter `WebcamTongueTests|CompanionBarkHeadTests|WebcamDevicePickerTests|WebcamProjectionTests`: exit 1; 9 passed, 2 failed. Existing panic-bark and device-picker tests cannot create Segoe UI Black / Consolas glyphTypeface in this environment. No tests weakened or removed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent at this base. Numbered tester coverage was still added.
- Panic negative control: remove generation suppression, run `CapturePublishesTongueOnce_AndStopDropsQueuedDelivery`; exit 1 with expected 1 event / actual 2. Restore the fix and rerun the focused suite.
- `git diff --check`: exit 0. Real cameras, physical gesture accuracy, Windows COM/DirectShow and desktop monitor hotplug cannot be exercised in this Linux cloud environment. Synthetic tests never open a camera. WPF is compiled only if the cross-targeting build permits it.

## Assumptions

- Keep the WPF reference frozen; the new Core detector is adapted without rewriting its WPF caller.
- Preserve WPF's disabled tongue calibration prompt and known lipstick/lighting limitations.
- Use DirectShow on Windows for the enumeration/capture index contract. Full Windows backend retries remain explicitly missing.

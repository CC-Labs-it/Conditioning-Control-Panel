# wire-ctrl-attention-check

- Move the dormant ring's 420 ms sine pulse (1.0 to 1.18, auto-reverse) to the existing shared 30 Hz `BeatLoop`. Repeated start resets its phase; hidden owners park; detach stops it and resets both scale axes.
- Replace the two live blur effects with static sibling rounded `Border.BoxShadow` layers and shrink the existing animation/effect ratchet allowlists. No tests were removed or assertions relaxed.
- Add headless progress/clamping, clockwise orientation, actual glow layers, pulse timing, repeated start, visibility parking and detach tests.
- Keep `ctrl-attention-check` **stub**. WPF explicitly scrapped the mechanic before shipping (`App.xaml.cs` constructs its service only for BarkService events and never starts it). Both existing parity scans still pass; no live host, trigger or panic surface was added.
- Reuse the base's `BeatLoop`; no source was imported from read-only `parity/trunk` (inspected `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`).

## Verification limits

- .NET 10.0.402 was installed into `/workspace/dotnet-sdk`; the SDK described in the brief was absent initially.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~AttentionCheckControlTests'`: before production changes, **1 failed / 5 passed / 0 skipped**. `PulseUsesSharedBeatAndStopsOnDetach` failed because no shared loop existed.
- Final `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~AttentionCheckControlTests|FullyQualifiedName~AttentionCheckParityScanTests|FullyQualifiedName~AmbientBeatLoopsTests.NoNew|FullyQualifiedName~AmbientBeatLoopsTests.TheScan'`: **13 passed / 0 failed / 0 skipped**. Final tail: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13`.
- `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15 CCP_REQUIRE_VOSK=1 dotnet test Tests/CCP.Core.Tests --no-build`: **2815 passed / 0 failed / 1 existing Debug-only frame-budget skip** (2816 total, 5 minutes); actual Vosk speech tests ran.
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.` (424 XAML type references checked).
- `CCP_USERDATA_DIR=/workspace/shared/root-attention-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /workspace/shared/root-attention-renders`: **exit 0, 224/224 views rendered**. Inspected `AttentionCheckControl.png`.
- `dotnet build <project>` with the flags below: **all 14 builds passed** (Core, Avalonia, all 11 Linux workflow test projects, and the optional Windows-targeted WPF test project). Built projects:
  - `CCP.Core/CCP.Core.csproj`: exit 0.
  - `CCP.Avalonia/CCP.Avalonia.csproj`: exit 0.
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`: exit 0.
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`: exit 0.
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`: exit 0.
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`: exit 0.
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: exit 0.
  - `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj`: exit 0.
- WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **509 warnings, 0 errors**; compiled on Linux, never executed.
- First parallel build attempts exhausted the 32 GB disk while copying media. Final build/test commands use `-m:1 -p:UseSharedCompilation=false` and all four MSBuild hard-link properties below; generated auxiliary test outputs are removed after each successful build. No source files or tests were deleted.

```text
-p:CreateHardLinksForAdditionalFilesIfPossible=true
-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
-p:CreateHardLinksForCopyLocalIfPossible=true
-p:CreateHardLinksForCopyAdditionalFilesIfPossible=true
```

## Follow-up: wire-ctrl-attention-check-b

Only if WPF revives the mechanic, restore its actual UI door and port its scheduler (30–120 s initial delay, per-hour cadence, scope/webcam/active gates), gaze dwell (1 s, 30 DIP slack, 400 ms stale neutral), click-through topmost host/fade, pass/fail/XP/lock-card effects, EMI hold, ping, panic and Lockdown integration. The static circular glow approximates WPF's arc-specific blur; rasterize that glow for exact arc parity before revival. The shipped inactive behavior remains unchanged.

No live desktop or Windows runtime was exercised on this Linux worker.

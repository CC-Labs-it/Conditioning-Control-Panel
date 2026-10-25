- Finish the intro card's WPF hero lookup: user assets first, application-directory art second, packaged hero last; unreadable files fall through and the window owns/disposes its bitmap.
- Finish the hub's ordered first-open pass: dismiss the modal, save SeenIntroGuide, save SeenDollhouse before raising ChaosDollhouseFirstOpen, then sync hub_open reveals.
- Restore WPF's finite reveal sequence: three 500 ms sine pulses with reverse, 600 ms stagger, per-surface reveal_chime at 0.5 volume, one ChaosRevealFlash Bark with the first element id, silent consumption for unmapped/hidden ids, persisted completion. Closing stops the timer and settles the queue without delayed chimes.
- Reuse existing Core RevealService, BarkHead, ChaosSfx and chaos-intro panic registration; no new window or Core dependency.
- Consolidate the six historical ledger entries into one honest stub. Missing: the real Lab entry path belongs to chaos-hub-window. Its absence prevents claiming wired, despite the recap door and run host being present.
- Reference: WPF at be09ff13f0ea97c92e72c6dfcad763e387ad1aec, ChaosIntroWindow.cs; ChaosHubWindow.Reveals.cs:139-244; ChaosHubWindow.xaml.cs:2942-2948; Services/Chaos/ChaosArt.cs. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported.

## Assumptions

- Preserve the WPF override convention exactly: its user root already ends in assets and Resolve appends assets/Chaos/guide/intro.png. Do not silently change existing overrides to a different directory layout. CorePaths.UserData supplies the neutral equivalent of WPF UserDataPath; custom media settings do not alter WPF ChaosArt.Roots either.
- Keep visibility/reveal gating and the Lab opener in the hub unit. This unit finishes the intro and first-open pass, rather than expanding into the remaining hub port.
- A closed hub settles queued reveals and cancels delayed sounds; rendering skips the first-open pass, as before.

## Verification limits

- SDK: .NET 10.0.402. All commands below used Release configuration and tests used DiffEngine_Disabled=true.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors. Final head recompile also passed (192 existing warnings, zero errors).
- `dotnet build <project> -c Release` passed (exit 0, zero errors) for every Linux test project named in `.github/workflows/build.yml`: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests, CCP.Core.Tests. Final Avalonia test recompile also passed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, two existing warnings and zero errors on the successful retry. Windows/WPF code was compiled, never run here.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed; 429 XAML references checked.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3951 passed, 5 skipped, 3956 total. Existing Vosk tests self-skip without the unavailable model; no tests changed or suppressed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~ChaosIntroWindowTests.Hero_|FullyQualifiedName~ChaosIntroWindowTests.Reveals_|FullyQualifiedName~ChaosIntroWindowTests.First_open_bark'`: exit 0; 5 passed, 0 failed/skipped. Covers override preference/corrupt fallback, finite waveform/stagger, persistence, unmapped consumption, one batch Bark with its element payload, first-open Bark saved before speaking, per-surface chime timing/volume and early-close cleanup.
- Before the fix, the hero regression failed with expected width 23, actual width 560. The initial full-class test attempt also hit the known cloud Inter glyphTypeface failure in three existing window-opening tests. Those desktop-layout cases were not rerun after the final change; the new non-layout cases above all pass. Render-view/render-all and manual desktop checks were not run for the same font limitation. No real desktop or profile was used.
- Initial overlapping baseline compilation hit an output lock; sequential builds resolved it. Broad output copying later exhausted disk; removing only generated build outputs and building sequentially resolved all required builds, including the optional WPF build. No source workaround was committed.
- `git diff --check`: exit 0. The target ledger id occurs once. No PR opened.

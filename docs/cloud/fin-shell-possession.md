- Connected the shipped shell to Core PossessionDirector and the existing four frame-driven effects. User path: Play → Lockdown → enable Possession → activate after the existing premium/consent gates. Core retains the first-haunt delay (at least 20 seconds), weighted cadence, intensity ladder, concurrency, 45-second cooldown, timer restart, tripwires and reassembly.
- Restored authored main-shell/Lockdown Role/Name/Exclude tags (including all three WPF safety exclusions). Added WPF target discovery and stable cached targets, transparent ghost/rubble stages, pointer position, geometry and takeover/lock-card gates. The explicitly tagged timer may transform without changing its value; exclusions still win.
- Added charge-before-motion, photosafe static charge, following outlines/tint, cursor ring and edge pulses on the shared frame source. No looping Animation or animated Effect; pulse glow uses BoxShadow.
- Wired Core audio and existing BarkHead rung/restart/tripwire contexts, possession consent, rung/pip readout, app shutdown, host detach and panic cancellation/restoration. Added focused sandboxed headless tests.
- Read-only trunk source fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`: install/usability/panic structure and ember BoxShadow adapted from its MainShellWindow.Possession.cs. Existing base runtime, leases, effects and Core contracts retained; no file overwritten with trunk code.

## WPF gap inventory and scope

Reference pinned at base `db079bf91474c77a5ea67ec306f6337a7de44b83`: MainWindow/MainWindow.Possession.cs, MainWindow/MainWindow.Lab.cs (InitializeLockdown, consent, readout, rules), Services/Possession/PossessionWpfHead.cs, EmberAttribution.cs, PossessionEvents.cs, PossessionPointer.cs, Effects/PossessionEffectBase.cs and POSSESSION.md.

Before this unit Avalonia lacked all of the shell stage/target registry, director lifecycle/head seams, pointer feed, attribution, audio installation, rung/restart/tripwire bark routes and rung readout. Four transform effects existed only as an unreachable standalone runtime. This unit implements those user-visible foundations and preserves Core's existing state machine.

The remaining WPF behavior exceeds the 1,000-added-line cap: 25 effects (crack, delete dialog, dissolve, dodge, doki dialog, drop, fall, ghost cursor, glitch portrait, glyphrot, melt, misroute, relabel, reorder doors, retitle, rewrite, room warp, scroll hijack, slider creep, steal card, swap, toast, toggle lie, typo, xp drain); all multi-beat scenes; companion Warden verbs (knock/stare/leave/return); remembered Full Doki charge/line on next launch and escape-counter prompt; first-run rules presenter/card/bark; reactive card/door presses, Start hover and settings-typo reactions; pointer velocity, hover and pressed events; tripwire screen shake; usability checks for web takeover hosts not yet present. The row stays **stub**, never wired or verified.

## Assumptions

- Prioritize a reachable haunt with attribution and reversible cleanup over importing the much larger effects/scenes subsystem. Leave every unsupported behavior named in the ledger.
- Keep the base's frame-driven transform implementations and shared clock. Outline tracking gates those frames to WPF's 100 ms cadence; the cursor ring uses WPF's 250 ms fade.
- Use all existing screen-owning panic surfaces and lock cards as conservative usability gates. Playback alone does not suppress possession. Missing web-host gates await those hosts.
- Only the authored timer role bypasses lockdown-name restrictions for transforms; emergency/secret names and explicit exclusions remain protected. No timer text is rewritten.
- Omit the Full Doki fake-crash sentence from consent until those dialogs are implemented. The possession paragraph otherwise uses WPF's copy.

## Verification limits

SDK: .NET 10.0.402. All test invocations used `DiffEngine_Disabled=true`.

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` (429 XAML type references checked). The first unprivileged invocation could not create temporary files; the normal writable verification invocation passed, then the final invocation passed again.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: first run exit 1 (3,552 passed, one unrelated DtrhRemoteMedia cold-cache ordering failure, five Vosk skips). No Core code or Core tests changed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls|FullyQualifiedName~Possession'`: exit 0, 136 passed, zero failed.
- Full Core rerun with `--no-build`: exit 0, `Passed: 3553, Skipped: 5, Total: 3558`, zero failed. The skipped tests need the unavailable Vosk model; no tests were weakened or disabled by this unit.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore`: exit 0, `0 Error(s)`. This rebuilt the final app and tests after the last source changes. Earlier compiler errors (duplicate attached declarations and a WPF-only dispatcher property) were corrected before the successful final builds.
- `git diff --check`: exit 0. Added-line privacy-path check passed.

Every workflow test project was built. Commands below are `dotnet build <project> -c Release`; the WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

Avalonia headless test execution was **not run** here: this base lacks the cloud-test-fonts prerequisite and the task identifies font creation as a known environment failure. The new tests compile; no behavioral pass or fail-proof is claimed for them. Supervisor filter: `FullyQualifiedName~PossessionHostTests|FullyQualifiedName~PossessionRuntimeTests|FullyQualifiedName~LockdownTests|FullyQualifiedName~PanicSurfacesTests` in `Tests/CCP.Avalonia.Tests`, with `DiffEngine_Disabled=true`.

WPF/Windows-specific code was compiled with the flags above and **not executed** on Linux. Render-all and real-desktop playtesting were not run. All new window tests use the assembly's existing headless dispatcher and CCP_USERDATA_DIR sandbox; no real desktop or user profile is used.

## Changed files

- `CCP.Avalonia/App.axaml.cs`
- `CCP.Avalonia/Possession/PossessionAttribution.cs`
- `CCP.Avalonia/Possession/PossessionDirectorEffect.cs`
- `CCP.Avalonia/Possession/PossessionEffects.cs`
- `CCP.Avalonia/Possession/PossessionHost.cs`
- `CCP.Avalonia/Possession/PossessionLease.cs`
- `CCP.Avalonia/Possession/PossessionRuntime.cs`
- `CCP.Avalonia/Views/Tabs/LockdownTabView.axaml`
- `CCP.Avalonia/Views/Tabs/LockdownTabView.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Possession.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.axaml`
- `CCP.Avalonia/Views/Windows/MainShellWindow.axaml.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `Tests/CCP.Avalonia.Tests/PossessionHostTests.cs`
- `docs/avalonia-parity.md`
- `docs/cloud/fin-shell-possession.md`

# fin-views-tab-bambi-takeover

- Implemented the remaining Mantra Chant tab path: enable/disable, live volume, ask/affirmation pairs, 2.5-second answer beat, 0–60-second inter-mantra gap, absent clips, muted parking/resume and backend failure recovery.
- Adapted `CCP.Avalonia/Platform/MantraChantService.cs` from read-only `parity/trunk` commit `e01c37eebe0110b56dbbebc8ecb0fb9f5041cda5` (inspected trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files win; only the new service was imported, adapted to the existing `MindWipePlayer.IVoice`/LibVLC sink. Added serialization for emergency stop and generation/identity checks for cancelled callbacks.
- Wired startup opt-in reset, panic disarm, watchdog runtime-only stop, rapid-blink stop, entitlement-lapse stop and exit disposal. Registered `mantra-chant` in the shared panic registry.
- Restored the WPF Companion permissions link and wallpaper attribution, refreshed from media source/consent on every show. The credit opens Scrolller through ExternalOpener and refuses Strict Lock, Lockdown, Chaos and DtRH runs.
- Replaced repeated stale evidence in the single ledger row with current wiring and an explicit Missing list. Status remains `stub`.

## WPF comparison at the pinned base

Reference SHA: `97ee118a6f5fce03b4c82005a2ffc09e20c94db8`. Compared both cited WPF view files, `MainWindow.Autonomy.cs`, `Services/MantraChantService.cs`, `Services/Video/WallpaperService.cs`, `MainWindow.StartStop.cs`, startup/exit, panic and entitlement callers.

| Behavior missing before this unit | Result |
| --- | --- |
| Chant Start/Stop/ApplyVolume behind saved controls | Implemented; the existing gap setting now governs playback. |
| Chant ask then affirmation, answer beat, inter-pair gap, missing clips, mute/resume, backend failure | Implemented using the existing voice player; deterministic fake-player/clock tests. |
| Chant launch reset, panic disarm, automatic emergency stop without persistence, lapse stop, exit disposal | Implemented, including stale-callback rejection after stop/restart. |
| Companion permissions navigation | Implemented via existing `ShowTab("permissions")`. |
| Wallpaper card credit and guarded homepage link | Implemented with WPF's source/consent predicate and lock rules. |
| Wallpaper folder consumption, local/remote image pool, Keep-off immediate restoration, empty-library warning, original capture/crash restore/panic/exit lifecycle | Still missing: no wallpaper service or desktop backend exists on this base. Trunk's local-only import also lacks WPF's remote pool; importing it would not finish this behavior. |
| Spiral/Brain Drain/Web Video/Wallpaper takeover actions | Still missing in `shell-autonomy`; its `CanPerformAutonomy` excludes them. |
| Next-takeover avatar countdown | Still missing: avatar countdown implementation, while the tab already saves the setting. |
| Announcement event audio | Still missing: companion event-audio caller in `shell-autonomy`; text and announcer overlay are already wired. |
| Force Start debug bypass | Hidden-only WPF affordance; no live caller. Left hidden, without claiming the whole row wired. |

Already wired before this unit: Start/Stop and their consent dialogs, master switch, premium gate, Test and its off-state explanation, Test mantra, voiced-content hint, model-folder button, Configure in Settings, trigger/behavior/session settings and numeric readouts, resume opt-in, mod-aware hero/description/side art and language refresh. Older ledger claims that Test voice and model-folder navigation were missing were stale.

Pinned Force Start caller proof (each grep run against the reference SHA):

```sh
git grep -n -E 'BtnForceStartAutonomy|ForceStartAutonomy' 97ee118a6f5fce03b4c82005a2ffc09e20c94db8 -- ConditioningControlPanel
```

Only four matches: the WPF view's permanently `Collapsed` button (`BambiTakeoverTabView.xaml:761`), its handler and delegation (`.xaml.cs:183,186`), and the shell handler (`MainWindow.Autonomy.cs:778`). No visibility assignment, external invocation or live WPF path. This is dead-code analysis, not a P122 wired claim; render-all was not used to bypass the remaining live dependencies.

## Verification limits

SDK: .NET 10.0.402. `DiffEngine_Disabled=true` for tests. All 15 required builds passed with **0 errors** (existing warnings remain): Core, Avalonia, all 12 Linux-compatible test projects named in `build.yml`, and the WPF test project with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Project built with `dotnet build -c Release` | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |

Builds initially exhausted the small temporary volume while copying native assets. Recovered by removing generated test outputs and using MSBuild's `CreateSymbolicLinksForCopyLocalIfPossible`, `CreateSymbolicLinksForCopyAdditionalFilesIfPossible`, `CreateSymbolicLinksForCopyFilesToOutputDirectoryIfPossible` and `CreateSymbolicLinksForAdditionalFilesIfPossible` properties, all `true`, on build commands. Test entry DLL/apphost outputs were then converted from symbolic links to hard links so .NET resolves the adjacent runtime configuration. These are generated-output changes only; no project configuration or tests were weakened.

- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; **3,545 passed, 0 failed, 5 skipped**, 36 s. Existing Vosk tests skipped because the model is absent and its download is blocked in this environment.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~MantraChantTests|FullyQualifiedName~PanicSurfacesTests.EveryStarterIsRegisteredOrAllowlisted|FullyQualifiedName~PanicSurfacesTests.EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'`: exit 0; **13 passed, 0 failed, 0 skipped**. The tests use fake audio, explicitly stepped timers, and no AppBuilder/windows/fonts or real profile.
- Stale-timer fail-proof: temporarily removing the new gap callback guard made `StopCancelsTimersAndStaleEndsCannotResumeEvenAfterRestart` fail (exit 1, expected 2 plays, actual 3). Restored the guard before final builds; that test passes in the final run.
- `bash .github/scripts/core-guards.sh`: exit 0, **All Core guards passed** (429 XAML references, 1,632 app types / 2,072 Core types).
- `git diff --check`: exit 0. One ledger row for this id; fewer than 1,000 added lines versus the required base.

`TakeoverTabUiTests` (including playback controls, entitlement lapse, permissions navigation and consent/credit link cases), the DtRH run-state assertions in `GameWindowTests`, and UI-consuming panic tests are compiled but not executed here: the reported cloud font-creation prerequisite has not landed. They remain for the supervisor's GitHub check. All test assemblies retain their existing userdata sandboxes.

Windows desktop APIs, real audio-device playback, desktop wallpaper, native notifications, Vosk-model speech, and render-all are not exercised here. No WPF/reference source is modified. No new icon is introduced.

## Assumptions

- The instruction to prioritize user-visible gaps within 1,000 added lines permits retaining an honest `stub` when independent wallpaper/shell implementations remain. A complete desktop wallpaper and online-media lifecycle plus tests does not fit beside the chant work in this unit.
- Reused LibVLC voice lifetime/output-device behavior instead of porting Windows NAudio teardown and its Windows audio circuit breaker.
- Kept the hidden debug Force Start bypass hidden, matching WPF's reachable UI.

# Sync 7: Scrolller official note and help audit

- Port the official Assets note and stamped credit, adapting WPF `8c9c7e733f24e9041973149e1bf72f4098280fbe` and compact-band correction `e4f316961bc3c0dee529057234aaa23c034462b5`.
- Reuse Core policy, allowance plan, persisted note acknowledgement and all ten existing translations; preserve the base's newer “going official” wording.
- Add focused Core visibility/plan tests, a ten-language no-size regression check, an Assets acknowledgement/layout test, and a manual tester step.
- Read-only reference fetched: parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. It has no meter/help counterparts to import; no trunk code was imported.
- Do not re-port sibling meter, spent notice, licence-card or backup features. No ledgers or parity document edited.

## Commit dispositions

| WPF commit | Result and evidence |
| --- | --- |
| `8c9c7e733` | **Ported UI; blocked-by-host for cloud-backup semantics.** `CCP.Avalonia/Views/Tabs/AssetsTabView.OnlineOfficialNote.cs:25` wires Nice, settings refresh, minute polling and the finite GentleVisuals/motion entrance; `:59` paints note/credit. `CCP.Core/Services/Fyp/Online/OnlineOfficialNoteRules.cs:6` supplies gating and plan copy. Tests: `OnlineOfficialNoteTests`, `AssetsMediaPickerTests.OfficialNoteNiceHidesNoteButKeepsCredit`. Setting already covered at `CCP.Core/Models/AppSettings.cs:4735`. The WPF ProfileSyncService cloud settings backup exclusion/restore-preservation pipeline has no Core/Avalonia counterpart on this base; it is not replaced by account SyncPush. |
| `e4f316961` | **Ported.** `CCP.Avalonia/Views/Tabs/AssetsTabView.axaml:248` uses one stamp/text/Nice band; browser keeps a 260 px floor at `:547`. Headless acknowledgement/layout test measures at 1100 by 650. All-language visual layout still needs supervisor testing. |
| `550226020` | **Blocked-by-host** for the interactive slip. Assets has no OnlineMeterHelpHost or meter, the shell has no OnlineMeterPill/spent hover card or navigation route from it, and no leash paper-slip controller exists. Building those sibling surfaces would exceed this unit. Final paragraph copy is **already covered** in `CCP.Core/Localization/Languages/en.json:8631` and the other nine languages, pinned by `OnlineOfficialNoteTests.AllTenLanguagesAlreadyHaveWordsOnlyHelpAndOfficialCopy`. |
| `becf2e49e` | **Blocked-by-host** for idempotent Pin/navigation on the missing meter help controller. Corrected counts/last-used copy is **already covered**, e.g. `CCP.Core/Localization/Languages/en.json:8633`. The size formatter is superseded by `e4bb70a3e`; no formatter introduced. |
| `e4bb70a3e` | **Already-covered copy; blocked-by-host rendering.** All ten Core catalogues already have the final words-only help and online strings without MB/GB, proven by the Core regression test above. No sizes or stale figure formatter added. There is no help-slip renderer to change. |

## Assumptions

- The existing Assets media-source block is sufficient to host the official note independently of the absent meter. The stamped credit sits directly below the note until the meter lane provides its final under-meter host; no plain credit exists on this head to replace.
- Preserve newer base translations rather than restoring superseded copy from these five commits.
- One minute policy polling follows WPF's meter clock; picker and relevant settings/language changes refresh immediately. Only the official-note subset of rules is extracted; no unused meter engine is imported.
- The note is inline content in the existing main shell, not a new window, popup or effect surface. It creates no separate PanicSurfaces lifetime to register.

## Verification limits

Verification results are recorded below after the required build/test runs. Windows desktop behavior and all-language layout require supervisor verification. The known cloud font limitation prevents running Assets headless window tests here; they must compile with zero errors. Vosk model download is unavailable here, so speech tests may be skipped by the existing suite.

- SDK: .NET 10.0.402. The channel installer first received 403s from its optional latest-version endpoint, then installed successfully from the supported SDK endpoint.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: final exit 0, zero warnings/errors. An earlier overlapping build hit an output-file lock; sequential retry succeeded.
- Built every test csproj named in `.github/workflows/build.yml` using `-c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: all 13 exit 0, zero errors. This includes the 12 Linux projects (Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot) and ConditioningControlPanel.Tests. Existing warnings remain. WPF code was compiled but cannot run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The initial read-only sandbox invocation could not create temporary files; rerunning with write access passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable (exit 127); this base does not contain the script. Manual tester coverage was added without modifying ledgers.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~OnlineOfficialNoteTests`: exit 0, 10 passed. Negative proof: temporarily replace the visibility rule with `false`, run the same filter: exit 1, 2 failed / 8 passed; restore production rule: exit 0, 10 passed.
- Full Core suite: initial exit 0, 3962 passed / 5 skipped; after adding the tenth focused test, a run during compilation had one unrelated board frame-budget failure (14.78 ms). Isolated `--filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget --no-build` retry: exit 0, 1 passed. Final full-suite result follows below.
- `AssetsMediaPickerTests` was compiled (zero errors), not executed, per the supplied cloud font limitation. The supervisor must run `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AssetsMediaPickerTests` with `DiffEngine_Disabled=true`. No real desktop or user profile was accessed by a headless window test here.
- Vosk speech tests retain their existing five skips because the required model is unavailable. Windows runtime behavior, visual motion and all-language 650 px layout are not verified here.
- Final idle full Core run: `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, **3963 passed, 5 skipped, 0 failed** (3968 total). The board timing test passed in this full run too.

- Retain decoded card/header/spotlight art and bound card rows across entitlement and daily-free changes, matching WPF's build-once shelf. Update gates/counts in place and move existing rows only when open-first ordering changes.
- Reuse gate-probe buffers on unchanged visits while still checking every door, including hidden entries. Keep full invalidation for language, mod changes, same-ID reloads, and detached views.
- Add a regression covering 20 alternating free-day shows, realized-container and banner retention, bound veil updates, group counts, sorting, and hidden-door reappearance. Existing assertions are unchanged.
- Imports: none. `parity/trunk` was fetched read-only; no trunk implementation was imported.

## Verification limits

- Verified base: `1a8b4ba40f8b02e1d417c71b1063104997b089cc` (initial snapshot differed; fetched and selected the required commit before implementation).
- SDK: .NET 10.0.402, Release configuration; `DiffEngine_Disabled=true` for tests. Test hosts use isolated profiles and headless windows.
- Before the fix: refresh filter exited 1 with the new retained-card regression failing at `Assert.Same`; all five existing refresh tests passed.
- After the fix: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~Exclusives'` exited 0: 11 passed, 0 failed. Font creation worked in this environment.
- Headless refresh + layout over 20 consecutive shows, alternating the spotlight free-day gate: median **1605.585 → 7.652 ms**, p95 **8701.892 → 229.910 ms** (sorted sample 19 of 20). Timing ends after layout; queued jobs drain between visits. These are diagnostic measurements, without a timing assertion.
- Unchanged visits after the fix: median **2.303 ms**, p95 **5.177 ms**, over 20 shows.
- `dotnet build <project> -c Release`: all 13 Linux projects exited 0 with 0 errors: `CCP.Core`, `CCP.Avalonia`, and the test projects below (under `Tests/<name>/<name>.csproj`).
  - `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`.
  - `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exited 0, 559 existing warnings, 0 errors. Compiled the frozen WPF head and its tests; neither was run.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exited 0; 3339 passed, 0 failed, 5 skipped (existing Vosk model-dependent tests; model unavailable). No tests were changed or disabled to obtain this result.
- Other test suites were compiled only. The Vosk download was not attempted given the documented environment 403 limit.
- `bash .github/scripts/core-guards.sh`: exited 0, all guards passed. An initial read-only sandbox attempt could not create temporary files; rerunning with write access passed.
- Windows expectation: gate-changing visits no longer decode the shelf or recreate all templates, and unchanged visits no longer allocate the snapshot array/LINQ enumerators. This should reduce occasional UI-thread stalls and GC pressure. Mod/language invalidations still legitimately rebuild; motion/glow behavior is unchanged.
- The real Windows switch benchmark was not run here. Headless layout excludes desktop presentation and cannot establish that the 178.0 ms Windows p95 threshold passes (reported baseline 195.0 ms; WPF 152.6 ms). No Windows runtime behavior was executed on Linux.

## Assumptions

- Art remains valid between the existing mod/language/detach invalidations; entitlement and daily-free changes affect access state and ordering, not image contents. Same-ID mod reloads must still repaint art.

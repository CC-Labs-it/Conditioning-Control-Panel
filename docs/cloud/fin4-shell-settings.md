# fin4-shell-settings: dependency-only audit

- Base verified at `78e5e03f81e26af3a0458737d1e05a72b4e16780`; the initial cloud snapshot differed and was reset before inspection. Work is isolated on `avalonia-port/cloud-fin4-shell-settings`.
- No application, test, or ledger changes. `shell-settings` remains `stub`, with its existing honest Missing list. The remaining user path requires `views-settings-emidesk` / `wire-views-settings-emidesk-b`: the EMI offers host, waiting chips, answer/ignore lifetime, and effect execution.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. Our base's existing files win. That trunk snapshot contains an offers implementation, but importing its dependency closure exceeds this unit (see below).

## Remaining WPF behaviours

References below are pinned to the base SHA above. Read the cited row's WPF `MainWindow/MainWindow.Settings.cs` and followed its live tour-narrator call into `Services/EmiDesk/EmiTourNarrator.cs:175-201`. All remaining shell-settings differences belong to one path:

1. Both natural completion and skipping fire their ending narration first, then call `EmiCodex.MaybeOfferSoon`, with `tourFinished` or `tourSkipped` as context. This is live WPF behaviour; P122 does not apply. Reproduce with `git grep -n MaybeOfferSoon 78e5e03f81e26af3a0458737d1e05a72b4e16780 -- ConditioningControlPanel`.
2. `Services/EmiDesk/EmiCodex.cs:664-730` delays the offer 20,000 ms so priority-3 `bookOffer` cannot replace the ending line. A second request replaces the pending timer. Scheduling requires content and a live dispatcher; tick-time checks reject missing content, an already-open book, or any prior codex open. The service drops a moment while EMI is away before drawing, preserving the once-ever offer budget for a future caller. The timer is one-shot and shutdown-safe.
3. The offer must pass Core's existing offers setting, bedtime, ignore streak, cadence/limit and effect-feasibility gates. WPF's `EmiDeskService.AskSituationOk` also checks visibility, presentation/input lock/transit, existing ask, video, active session, tube bubble and minimised shell.
4. WPF `EmiDeskService.cs:750-756` consumes `DrawAsk` and calls `ShowAsk`. `Windows/EmiDesk/EmiDeskWindow.Bubble.cs:583-914` owns the dot cadence, waiting face, two focusable chips, monitor-clamped chip placement and indefinite wait (no timeout). An arriving spoken line parks until resolution. Answers clear the offer, reset the ignore streak, fire `askAnswered`, speak the selected reply before executing its effect, and release parked speech after 900 ms.
5. Escape, dismissal/teardown and full-screen kill moments cancel the waiting ask. Ignoring increments the launch streak, fires `askIgnored`, holds `-_-` for 1,400 ms then thinks for 700 ms before returning to rest and releasing parked speech. Teardown suppresses that visual reaction. Three ignores suppress further offers for the launch. Panic must cancel the pending host work without reviving it.
6. Accepting `book:open` must reach the singleton codex opener, persist codex-open state/bookmark, and focus an existing reader. The native reader already exists in Avalonia, but constructing it directly would bypass WPF's service ownership and accounting.

## Why this is a dependency-only unit

The head's `EmiTourNarrator.OnFinished` speaks the ending but has no book scheduler. `EmiDeskService.Speech.cs:Fire` draws only lines and explicitly defers asks/effects. Searches for `AskSituationProbe`, `EffectFeasibleProbe`, `DrawAsk`, `NoteAskAnswered` and `ShowAsk` in the EMI head find no consumer. Core already supplies `AskDraw`, cadence/accounting, state and gates; these must be reused, not duplicated. The unrelated avatar-tube `ShowAskCard` handles another protocol and is not this host.

A faithful host is larger than a timer plus an Open button: the WPF offer section alone is over 330 reference lines, `EmiOffers.cs` adds 710 reference lines across target/pin/tour/book, media effects, bedtime and library feasibility, and the service/speech/teardown integration and behavioural tests add further work. These are reference sizes, not a claim that adapted code must have the same size. The read-only trunk implementation corroborates that scope: `EmiDeskWindow.Ask.cs` (412 lines), `EmiDeskWindow.Lines.cs` (97), `EmiDeskService.Offers.cs` (202) and Core `EmiOffers.cs` (551) require adapting together with service, chain and overlay seams absent or different on this base. The accompanying trunk tests add 200 lines (`EmiDeskAskTests`) and 207 (`EmiOffersTests`), before the missing scheduler and codex integration. Trunk also routes its book seam to the desk flipbook (`EmiDeskService.Offers.BuildOfferSeams`), so it cannot be copied as the codex opener specified by this pinned WPF reference. Our existing speech and bubble files cannot be overwritten by those trunk files. The complete host with coverage is estimated to exceed this unit's 1,000-added-line cap. Restricting feasibility to the book verb would leave a deliberately partial offers host and move the existing dependency rather than finish it. Leave this work with the named EMI offers unit; no timer-only or modal substitute is introduced here.

After that host lands, finish this row with the delayed call on both tour endings, and tests covering delay/re-arm, tick-time gates, no budget spent while away, both answers, ignore/teardown and panic. Add the numbered WPF tester step and promote only then to `wired`.

## Verification limits

- Installed .NET SDK 10.0.402 with the requested install script (the latest-version probe initially returned 403, then the SDK download/extraction succeeded).
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The first restricted invocation could not create temporary files; rerunning with write permission passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; the script does not exist on this pinned base. No row is added or promoted.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2 failed, 2,832 passed, 5 skipped, 2,839 total. Failures: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` (null callback) and `BoardTilesTests.A_frame_fits_the_budget` (12.70 ms). Both are unchanged base tests; this run overlapped builds. Vosk tests self-skipped because the model is unavailable; no tests were changed or explicitly skipped by this unit.
- Focused retry after the Linux builds, `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~SyncedEventAndScheduledFireBothHopThroughCoreDispatch|FullyQualifiedName~A_frame_fits_the_budget'`: exit 0, 2 passed, 0 failed. This does not replace the failed full-suite result.
- `dotnet build <project> -c Release --nologo`: exit 0 and zero errors for CCP.Core, CCP.Avalonia and all 11 Linux-compatible test projects listed in `.github/workflows/build.yml`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler and Avalonia.Snapshot. Existing compiler/analyser warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release --nologo -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. The WPF application and test assembly compiled; neither was executed.
- Headless `ShellSettingsWiringTests` not run under the task's reported font-creation limitation; the Avalonia test project compiled with 0 errors. Windows-specific behaviour and WPF tests cannot run on this Linux host. No desktop, notification, render-all or live application checks were run for this documentation-only change.

- `git diff --cached --check`: exit 0; 44 added lines against the pinned base, documentation only.

## Assumptions

- The task's dependency-only exception applies to the complete EMI offers host, rather than a book-only special case. Existing unrelated EMI glass, voice and ambient gaps are outside this row.
- No new behaviour or surface is added, so no new behavioural test or panic registration is needed. The ledger and tester plan remain unchanged.

NOOP: `views-settings-emidesk` / `wire-views-settings-emidesk-b` EMI offers host.

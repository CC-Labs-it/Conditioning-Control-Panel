# fin5-shell-settings: dependency-only audit

- Base inspected: `78e5e03f81e26af3a0458737d1e05a72b4e16780` (`origin/avalonia-port/cloud-base-fin-shell-settings`). Initial snapshot HEAD differed; fetched the requested branch and checked out the exact expected commit before analysis.
- No source code or ledger changes. The single `shell-settings` row remains honestly `stub`. No new surface, panic registration, behaviour test or tester step is introduced.
- No trunk code imported; no trunk commit credits apply.

## Remaining WPF behaviour

The only remaining shell-settings gap is the delayed book offer after **both** tour endings. This is live WPF behaviour, so P122 does not apply. At the pinned base:

- `ConditioningControlPanel/MainWindow/MainWindow.Settings.cs:570` attaches `EmiTourNarrator` before starting a shell tour and detaches it when the overlay closes.
- `ConditioningControlPanel/Services/EmiDesk/EmiTourNarrator.cs`, `OnFinished`, fires `tourFinished` or `tourSkipped`, then calls `EmiCodex.MaybeOfferSoon` with the corresponding reason. Reproduce with `git grep -n 'EmiCodex.MaybeOfferSoon' 78e5e03f81e26af3a0458737d1e05a72b4e16780 -- ConditioningControlPanel`.
- `ConditioningControlPanel/Services/EmiDesk/EmiCodex.cs:664-734`: wait 20 seconds after the ending line; a second ending replaces the pending timer. Marshal to the UI dispatcher, refuse shutdown, precheck content, stop the timer before firing, and recheck content, single-instance open state and persisted `CodexOpens` at the tick. Fire `bookOffer` with the reason only after those checks. An away EMI drops it before drawing, preserving the once-ever budget for a later attempt.
- `Assets/emi/desk-lines.json:1791` authors priority 3, ask odds 1 and an ever/1 budget; `:17846` supplies the two chips ("open it", "later"), distinct replies and `book:open`.
- `CCP.Core/Services/EmiDesk/EmiLineEngine.cs`, `AskGatesPass` and `PickAsk`: preserve offers/bedtime/ignore and situation gates, scripted cadence rules, effect feasibility and the authored lifetime budget. A timer alone cannot make this visible.

The existing Avalonia narrator already implements summon, started/step/ending narration and overlay lifetime. `CCP.Avalonia/Views/Windows/EmiDesk/EmiTourNarrator.cs:14` explicitly defers only the book offer. The shell's other audited paths (settings navigation, save/preset offer and tick/ripple, asset safety/migration, Help guides, browser visibility, bug report) already have implementations and named tests in the base ledger; no additional gap was found in the cited shell reference.

## Dependency and scope

Required feature: **wire-views-settings-emidesk-b**, row `views-settings-emidesk`: offer chips, answer/ignore lifetime and effect execution, plus the codex offer/open service. Glass and unrelated ambient/voice work are not prerequisites for this shell gap.

Evidence on this base:

- `CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.Speech.cs:18-37` calls `Draw` but never `DrawAsk`; its deliberate shortcut says no feasibility probe is installed until chips have real effects. `git grep -n -E 'DrawAsk|AskSituationProbe|EffectFeasibleProbe' -- CCP.Avalonia` finds no implementation.
- `CCP.Core/Services/EmiDesk/EmiLineEngine.cs:1010,1060` refuses asks when the head situation/effect probes are absent. Core already supplies ask selection, persisted limits and answer/ignore counters; reuse it when porting the host.
- `CCP.Avalonia/Views/Windows/EmiCodexWindow.axaml.cs:47-50` documents its missing service caller. Having a plain reader constructor does not supply the offer lifecycle or single-instance service.
- WPF `Windows/EmiDesk/EmiDeskWindow.Bubble.cs:580-940` supplies waiting chip cadence, hit testing/focus, monitor clamping, reply-before-effect ordering, answer/ignore accounting, Esc/hover-close/full-screen cancellation, parked-line arbitration and teardown. An ask waits indefinitely; it is not a timed speech bubble.
- WPF `Services/EmiDesk/EmiOffers.cs` supplies draw-time feasibility and effect execution (710 source lines); the ask portion of Bubble alone is roughly 360 lines, before its preceding arbitration/bubble scaffolding. These source counts are a sizing guide, not a claim that WPF can be copied verbatim. Adapting the shared offer host, its shell/session/media gates, codex singleton/open path and timer, and focused tests for each new behaviour exceeds the 1000-added-line unit budget. Restricting probes to the book would silently keep the other authored asks unavailable and leave the host incomplete.

Decision: use the unit's explicit pure-dependency exception. Do not add a dead timer, bypass asks with a direct reader popup, promote the ledger, or partially import the offer host. Complete that host in its own lane, then wire and test the two tour-ending delayed offers here.

## Verification limits

- .NET channel installation encountered HTTP 403 resolving the SDK; pinned .NET SDK 10.0.100 installed successfully (exit 0).
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 48 warnings, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 172 warnings, 0 errors.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; 2834 passed, 5 skipped, 0 failed (2839 total).
- `bash .github/scripts/core-guards.sh`: exit 0; all guards passed. An initial read-only invocation could not create scratch files; rerun with write access succeeded.
- `git diff --cached --check`: exit 0.
- All Linux test projects listed in `.github/workflows/build.yml` built with `dotnet build <project> -c Release`: exit 0, 0 errors each: `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Tests` (each under `Tests/<name>/<name>.csproj`).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 512 warnings, 0 errors. WPF head and tests compiled on Linux, not executed.
- Avalonia `ShellSettingsWiringTests` compiled but not executed: this base has no cloud-test-fonts fix, and the task records font creation failures in this environment. No test was weakened, removed or newly skipped.
- WPF requires Windows execution; no Windows desktop, notification, user-profile or render-all run was performed for this unchanged stub audit. The test-plan coverage script is absent on the pinned base (invocation exit 127).

This is an analysis-only change; no runtime behaviour changed.

## Assumptions

- The ledger's named offer-host dependency remains the appropriate next unit; a book-only substitute would not preserve the WPF offer contract.
- The explicit dependency-only instruction takes precedence over the general request to implement this row when its only missing behaviour requires a host that exceeds the line cap.

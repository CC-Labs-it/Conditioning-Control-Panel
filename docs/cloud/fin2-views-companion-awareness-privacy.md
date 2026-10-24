# fin2-views-companion-awareness-privacy

- Dependency-only review of `views-companion-awareness-privacy` at pinned base
  `ea96b2c7159137e45b4bbbc168df3618bde2a479`.
- No application or test code changed. The single ledger row remains `stub` with
  its existing explicit remaining-work list; promoting it would overstate parity.
- Read-only source `parity/trunk` fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported.
- Required dependency: runtime Awareness v2 observer, platform probes, reaction
  arbiter/routing and frame publication (`shell-awareness`), plus the foreground
  app ring owned by the unported `KeywordTriggerService` (`shell-keyword-triggers`
  and `views-companion-keyword-triggers`). Pure Core policy/projection types do
  not supply either runtime service.

## WPF comparison and remaining behaviours

Reference files are pinned to the base commit above. Reviewed the row's
`AwarenessPrivacyView.xaml` and `.xaml.cs`, its live
`Runtime/AwarenessPrivacyRuntimeVm.cs`, `IAwarenessPrivacyVm.cs`, and
`Services/Awareness/AwarenessAppCandidates.cs`.

Already reachable in Avalonia: three-stop dial; entitlement before consent;
decline preserving settings; ungated Off; Broad clearing title permissions;
Everything enabling then asking through the picker; seeded deny chips and
add/remove; per-app title allow list and switch; current-app hide chip; retained
known-app forget chips; clamped retention and immediate pruning; hour pause and
resume; two-step full shared erasure; JSON disclosure toggle; Workshop navigation;
1.5-second visible-only refresh; older-pipeline warning and accurate empty-wire
fallback. The existing cursor implementation is present but cannot become live.
The dormant mock exhibit is not a live WPF behaviour (`IsDormant => false`).

Every remaining gap depends on the services above:

1. **Live wire summary:** WPF runtime VM `Sync`/`BuildWireLine` reads the last
   actually published frame. Off, pause, legacy and no-frame states have distinct
   copy; live summaries suppress adult-cluster identity/title, sanitize app names,
   scrub titles, avoid duplicate app/title and format nonnegative dwell duration.
   Avalonia has no producer of `AwarenessLive.LastFrame`; constructing one in the
   view would misrepresent what was sent.
2. **Actual cloud JSON:** WPF `BuildWireJson` projects that same frame, returns
   empty for Off/no frame/empty projection, pretty-prints valid JSON and preserves
   raw bytes on parse failure. Legacy suppresses JSON; paused v2 can still show
   the previous actual frame. The Avalonia readout honestly remains empty pending
   the observer. WPF uses the cloud projection here; local projection belongs to
   the upstream pipeline, not a separate privacy-card mode.
3. **Live styling/cursor:** WPF `IsWireLive` is on AND unpaused AND nonlegacy,
   with a 1.2-second cursor cycle, stopped while hidden/unloaded and resumed on
   show. Avalonia has the clock lifecycle but no real v2 state to drive it.
4. **v2 warning/consent action:** WPF computes legacy from
   `on && !AwarenessObserver.IsEnabled`, offers direct consent review rather than
   the once-per-session prompt, and bounces the poll after acceptance without
   changing the v2 kill switch. Avalonia keeps the warning and hides the action
   (`CanReviewConsent => false`): consent cannot start an absent observer.
5. **Recent foreground apps:** WPF `RebuildSeen` prepends the current app to
   `KeywordTriggers.GetRecentForegroundApps()`, de-duplicates case-insensitively,
   sanitizes, excludes denied entries, caps at eight and offers one-click hiding.
   Avalonia supports the current app but has no keyword ring. Picker candidates
   already merge current app, ledger transitions and persisted ids in WPF order,
   sanitize/de-duplicate/exclude listed entries and cap at 40; the fourth source
   (keyword ring) is the only missing picker behaviour.
6. **Erasing upstream transient state:** the shared wipe/forget functions already
   call observer, pacing and memory hooks when installed. WPF installs the real
   owners at startup; Avalonia only owns the ledger. Resetting observer candidates,
   arbiter/scorer pacing and awareness reaction memory awaits those owners; this
   card must not create replacement state that the engine never reads.

## Reachability evidence (not P122)

These are live WPF features, not dead references. Reproduce at the pinned base:

```sh
git grep -n 'AwarenessPrivacyRuntimeVm' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- ConditioningControlPanel/Views/Controls/Companion/Runtime/CompanionRoomRuntimeVm.cs
git grep -n 'Binding Awareness' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- ConditioningControlPanel/Views/Controls/Companion/CompanionRoomView.xaml
git grep -n -E 'new Services.Awareness.AwarenessObserver|AwarenessLive.ResetObserverState|AwarenessLive.ResetPacingState' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- ConditioningControlPanel/App.xaml.cs
git grep -n 'AwarenessLive.Publish(frame)' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- ConditioningControlPanel/Services/Awareness/AwarenessObserver.cs
git grep -n 'GetRecentForegroundApps' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- ConditioningControlPanel/Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs ConditioningControlPanel/Services/Awareness/AwarenessAppCandidates.cs
# No runtime observer/keyword engine or frame publisher in the shipped head/Core:
git grep -n -E 'class AwarenessObserver :|class KeywordTriggerService|AwarenessLive.Publish' ea96b2c7159137e45b4bbbc168df3618bde2a479 -- CCP.Core CCP.Avalonia
```

The last grep exits 1 (no matches). `AwarenessObserverPolicy` is pure policy, not
an observer. WPF observer/probes/arbiter alone total 1,860 source lines before
adapting routing, capture seams and focused behaviour tests. This is upstream
runtime work, not a missing card binding. No new surface needs panic registration.

## Assumptions

- Apply the explicit pure-dependency NOOP rule, retaining existing safe fallback
  behaviour and the honest `stub` row. Do not synthesize frames or enable a button
  whose advertised v2 protections cannot become active.
- The documented immediate disk prune remains an intentional privacy-side
  deviation. It is not an unfinished dependency or a reason to broaden this unit.

## Verification limits

- SDK: .NET `10.0.402` installed with dotnet-install. The initial channel
  discovery received HTTP 403 from ci.dot.net; the builds endpoint succeeded.
- `dotnet build <project> -c Release`: exit 0, 0 errors for `CCP.Core`,
  `CCP.Avalonia`, and all Linux test projects in `.github/workflows/build.yml`:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and
  `CCP.Avalonia.Snapshot.Tests`. Existing compiler/analyzer warnings remain.
- Initial consent/later test builds ran out of disk (exit 1). Removed the
  task-created abandoned clone and generated test binaries, then rebuilt each
  affected project sequentially: all retries exit 0, 0 errors. No source changes
  were needed to resolve this infrastructure failure.
- `bash .github/scripts/core-guards.sh`: exit 0, "All Core guards passed."
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`:
  exit 0; "Failed: 0, Passed: 2854, Skipped: 5, Total: 2859". The five existing
  Vosk tests skip because the speech model is unavailable (download known blocked
  with HTTP 403); no tests were changed or disabled.
- Avalonia relevant filter would be
  `FullyQualifiedName~AwarenessPrivacyCardTests|FullyQualifiedName~AwarenessLedgerPrivacyTests|FullyQualifiedName~PanicSurfacesTests`.
  Compiled, not run: this base lacks the cloud test font setup, and the task's
  measured environment limit reports font creation failures. Supervisor rcheck
  must run these on GitHub. No new behaviour or tests in this documentation unit.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: initial attempt exit 1
  from disk exhaustion; retry exit 0, 512 warnings, 0 errors. WPF app and tests
  cross-compiled; Windows code was not run.
- No Windows execution, real desktop/profile access, or render-all run. This is
  not a P122 promotion; no render proof or verified/wired claim is being made.
- `git diff --check`: exit 0. Exactly one row for the id; unchanged `stub`.

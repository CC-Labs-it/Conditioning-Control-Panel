# fin5: companion awareness privacy dependency analysis

- Outcome: dependency-only NOOP; `views-companion-awareness-privacy` remains `stub`.
  No application, test, ledger, or test-plan changes. No new surface needs panic registration.
- Verified base: `c5de71e3b4ec06e9df272e443fa1b7c1d313244c`.
  The initial snapshot differed; fetched the requested base and checked out that exact commit
  before creating `avalonia-port/cloud-fin5-views-companion-awareness-privacy`.
- Read the single ledger row and its WPF `AwarenessPrivacyView.xaml` / `.xaml.cs`,
  plus `Runtime/AwarenessPrivacyRuntimeVm.cs`, `AwarenessAppCandidates.cs`, observer,
  probes, routing, startup wiring, and keyword foreground-ring implementation.
- Read-only import source inspected: `parity/trunk` at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported.

## Remaining WPF behaviours

All references below are pinned to the verified base unless explicitly marked trunk.

1. **Actual live wire summary.** WPF RuntimeVm `Sync` (312–356) sets legacy state from
   `AwarenessObserver.IsEnabled`, and marks the wire live only while enabled, unpaused,
   and non-legacy. `BuildWireLine` (395 onward) reads the actual last cut frame:
   closed, paused, legacy, and idle states; adult-cluster identity withholding;
   sanitised service/id, scrubbed title, duplicate-title omission, and rounded duration.
   Avalonia instead reports legacy whenever on and never reports a live frame.
2. **Actual projected JSON.** WPF `BuildWireJson` uses `AwarenessLive.LastFrame` and
   `AwarenessProjection.BuildCloudProjection`, pretty-prints valid nonempty JSON,
   preserves raw bytes on JSON parse failure, and returns empty with no frame/off/legacy.
   Avalonia always returns empty JSON. WPF shows the cloud projection here; this card
   does not have a separate local-projection selector (the ledger's broader projection
   wording also describes the pipeline dependency).
3. **Live presentation and warning recovery.** With a working observer, WPF hides the
   older-pipeline band, restores the live caption/incognito copy, and runs the 600 ms
   cursor half-period while live. Its review action explicitly re-offers v2 consent,
   restarts awareness on acceptance, and preserves the kill switch; decline leaves it
   unchanged. Avalonia already has cursor and consent/restart code, but `IsWireLive`
   and `CanReviewConsent` are constant false. Exposing the action or live state without
   an observer would claim protections and data that the runtime cannot supply.
4. **Recent foreground process chips and picker candidates.** WPF RuntimeVm
   `RebuildSeen` prepends the current service to the keyword-trigger ring, de-duplicates
   case-insensitively, sanitises, skips denied entries, and caps at eight. Candidates
   merge current service, session transitions, persisted ids, then that ring, skipping
   listed entries and capping at 40. Avalonia has all existing sources except the ring.
   WPF `KeywordTriggerService.GetRecentForegroundApps` (592) is a live caller, backed
   by an eight-entry, newest-first, nonpersisted process-name ring; collection runs only
   with that service, excludes the owning process, and is fed through its foreground
   resolver. A ring of category/display names from the legacy title reader is not equivalent.
5. **Erasure of observer-owned state once that host exists.** WPF startup publishes the
   observer reset, memory, cooldown reset/forget, and scorer reset/forget callbacks to
   `AwarenessLive` (`App.xaml.cs:2903–2922`). The existing Avalonia wipe/forget paths
   already call the shared seam, but no observer/arbiter owner exists to attach these
   callbacks. Any host port must attach and release them as part of its lifecycle.

The dial, list edits, known-app ledger, retention, pause, two-step wipe, JSON expander,
Workshop navigation, and visible-only 1.5-second refresh already have real user paths.
The documented immediate retention pruning and Fluent icon decisions remain unchanged.
Dormant intro animation is an exhibit path, not an additional runtime-host gap.

## Why the dependency does not fit this unit

- The base contains policy, projection, scoring, ledger, and privacy primitives, but no
  `AwarenessObserver`, concrete `ReactionArbiter`, or platform awareness probe host.
  `WindowAwarenessService.V2Lifecycle` / `V2OwnsReactionsProvider` remain unbound on
  Avalonia. Its legacy foreground reader cannot supply the WPF input/media/microphone/
  app-state observations, event-driven frame cutting, dwell gates, or arbiter routing.
- WPF constructs the observer and single shared scorer/memory/arbiter in
  `App.xaml.cs:2856–2965`, binds erasure, and attaches v2 routing. Observer
  `PublishFrame` (435–448) actually publishes to `AwarenessLive`; production Avalonia
  has no caller of `AwarenessLive.Publish`. Frames cannot be invented by the card.
- The trunk's adapted observer alone adds 1,013 lines; its platform probes add 391 and
  concrete arbiter 346 (`git diff --numstat BASE TRUNK -- <three paths>`: 1,750 total).
  Startup/lifecycle, routing/speaker integration, card changes, platform support and
  focused behaviour tests are additional. This is a pipeline port, not a safe card-only
  change within the 1,000-added-line cap; truncating the host would discard WPF behaviour.
- The other missing host, WPF `Services/KeywordTriggerService.cs`, is 2,084 lines and
  owns keyboard/OCR/text dispatch and foreground sampling. Avalonia's trigger panel and
  shell explicitly document the absent engine. Adding independent always-on collection
  solely for this card would change WPF's service-lifetime/privacy semantics.
- P122 does not apply: the WPF runtime VM calls these paths and startup constructs their
  host. Grep evidence is live, not dead code. Keep the ledger honestly `stub` until
  the v2 observer/reaction/platform host and keyword-trigger service lanes land.

## Verification limits

- .NET 10.0.402 installed using the requested channel installer. Channel discovery
  initially returned HTTP 403 from the CI feed, then installation succeeded. A parallel
  pinned 10.0.100 install also succeeded; checks used 10.0.402.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The first sandboxed
  attempt could not create temporary files; rerunning with writable storage passed.
- `dotnet build <project> -c Release`: Core, Avalonia, all eleven Linux workflow test
  projects built with zero errors (exit 0 each). Existing warnings remain.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build
  --filter 'FullyQualifiedName~Picker_candidates_fold_in_the_ledgers_switches_and_known_apps|FullyQualifiedName~TestsNeverReadTheRealDesktop'`:
  exit 0, 2 passed / 0 failed / 0 skipped; `DiffEngine_Disabled=true`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
  exit 0, 0 errors / 512 existing warnings. WPF compiled but was not run on Linux.
- Window-based cases in `AwarenessPrivacyCardTests`, `AwarenessLedgerPrivacyTests` and render-all
  were not run: this base lacks the documented cloud font fix. Compiled only; no
  behavioural or visual verification is claimed. No Windows runtime was exercised.
- `test-plan-coverage.sh` and `docs/avalonia-test-plan.md` are absent on this base;
  no ledger row was added or promoted, and no new behaviour needs tester coverage.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`:
  exit 0, 2,854 passed / 0 failed / 5 skipped (Vosk model unavailable);
  `DiffEngine_Disabled=true`. The model download was not attempted (documented 403).
- `git diff --check`: exit 0.

## Assumptions

- Applied the unit's explicit pure-dependency NOOP rule; no partial observer or synthetic
  wire frames were added to make the row appear wired.
- Source-size evidence includes comments; it measures the available faithful import,
  with integration and behaviour tests still additional, rather than proposing a
  compressed replacement pipeline.

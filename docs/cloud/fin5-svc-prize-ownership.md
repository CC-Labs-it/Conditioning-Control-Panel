# fin5-svc-prize-ownership — dependency analysis

- Documentation-only outcome under the unit's pure-dependency rule. No product code,
  tests, ledger status, or tester steps changed; `svc-prize-ownership` remains `stub`.
- Verified clean base `936da834860466c36ddcee701fbf1be56ad1524b` after correcting the
  initially supplied snapshot. All references below are to that pinned base.
- Read-only `parity/trunk` fetch resolved to
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported.
- Reviewed the single ledger row, shared `OwnershipService`, `PrizeGrants`,
  `PrizesFeed`, `BackRoomApi`, WPF `ProfileSyncService.PrizesFeed`, the Avalonia
  ownership adapter, account/validate/sync wiring, feature cards and effect hosts.

## Remaining WPF behavior and live callers

The remaining work is effect-host parity, not another grant store or persistence
layer. WPF grants are in memory too. The shared engine already handles all 16
client ids, revisions, account binding, debug overrides and change notifications.
Validate, profile sync and counter replies already supply the Avalonia store.
Logout/account replacement and the racing, Get it, dashboard/Studio badge consumers
are already wired on this base.

1. **Pendulum (`feat-flash`)**: WPF `FlashFeatureControl.xaml.cs:439` offers Still,
   owned Drift, owned Pendulum, then Mix. A revoked saved choice displays Still
   without rewriting the preference; grant events rebuild the choices. The live
   `FlashService.cs:2457` resolver checks both grants and MotionLevel, including
   inherited hydra motion. Its surrounding spawn path uses the existing
   `FlashPendulumRig` to coordinate pivot/phase with neighboring pendulums.
   `Compositor/FlashLayer.cs:358` rotates the media about its pivot. Avalonia
   `FlashOverlay.BuildMotion` explicitly passes `ownsPendulum: false` and only
   returns Drift; its shared tick moves windows without rotating their pictures.
   The card deliberately omits Pendulum. Merely changing the grant argument would
   not supply that render path, rotated hit geometry or exit integration.
2. **Jackpot Remix (`feat-flash`)**: WPF `FlashService.cs:984` rolls only for ambient
   events, replacing the entire event with one centered animated composite per
   targeted monitor. `EnsureRemixDirector` supplies live ownership, enabled/motion
   state, busy/video state, memory pressure and a GIF builder. `TryShowRemixAsync`
   consumes the built file, falls back on decode failure, announces, waits one
   second, and mirrors playback with XP only for the first copy. Core already has
   `JackpotRemixPlan`, `JackpotRemixRoll` and `JackpotRemixDirector`; reuse them.
   The missing head builder is WPF `Services/Remix/JackpotRemixBuilder.cs` (449
   lines): serialized hidden browser builds, ready/message correlation, bounded
   output files, cancellation/120-second timeout, release after build, process
   failure recovery and session disable after two failures. Avalonia has no caller
   of the director and no builder or animated flash decode/playback path. Its
   `WebHost`/`WebAssetServer` and packaged shared web assets are reusable, but do
   not implement this lifecycle or GIF playback. The ownership-gated toggle is
   also absent from its flash card.
3. **Rain / Spiral In (`feat-bubble-pop`)**: WPF `AmbientBubbleMotion.cs` checks
   ownership at spawn, falls back to FloatUp when unowned/Off, halves v2 speed in
   Reduced motion, and rolls Mix only across available styles. The live caller is
   `BubbleService.cs:1246`. Spawn geometry/speed and spiral initialization are in
   `BubbleService.cs:3310-3390`; stepping/quiet exit is at `:4087`. Spiral fades
   into the center and exits as a miss, without pop rewards; clicking before that
   uses normal pop behavior. Avalonia's Core `AmbientBubble.Spawn/Step` implements
   FloatUp only; `AmbientBubbleMotion`/`SpiralInPath` still live in the WPF head.
   The Bubble Pop card lacks the owned picker/Mix, offer and balanced grant-change
   refresh that WPF `BubblePopFeatureControl.xaml.cs:299-345` provides.
4. **Associated ownership-gated controls/effects**: the row's short Missing list
   also implies WPF `FlashFeatureControl.RefreshV2Box`: rounded corners, live
   dragging and shatter require either flash motion grant; the remix toggle uses
   its own grant. These rows and their playback consumers are absent in Avalonia
   (Core `FlashCorners`, `FlashDrag`, `FlashShatter` already exist). The exit picker
   must defer to owned shatter, as the Avalonia card's existing shortcut notes.
   WPF Bubble Pop also reveals Brain Drain for either bubble grant;
   `BubbleService.RollTriggerSpec` checks that ownership live and calls Core
   `BrainDrainBubble.RollPool`, without persisting the extra variant. Its ambient
   trigger/payload host is not ported. Magnet is another bubble-host gap, but is
   not an additional `PrizeGrants` id or independent grant consumer.

## Scope decision

P122 does not apply: these are live WPF paths at the pinned SHA. Reproduce the
caller evidence with `git grep -n -E 'TryShowRemixAsync|ResolveMotionStyle|RollForSpawn|RollTriggerSpec' 936da834860466c36ddcee701fbf1be56ad1524b -- ConditioningControlPanel`.
No render-all run can substitute for those missing live consumers.

The full dependency set cannot responsibly fit the 1,000-added-line cap. A planning
estimate after reusing Core, WebHost, WebAssetServer and shared web assets is
150 lines for pendulum/render/input integration, 550 for the builder plus animated
GIF delivery and scheduler lifecycle, 200 for bubble styles/picker, 200 for the
associated corner/drag/shatter/Brain Drain consumers and at least 200 for focused
behavior tests: approximately 1,300 added lines, before documentation. This is an
estimate, not a measured implementation diff. The 449-line WPF builder alone is
not a drop-in Avalonia implementation. No duplicate engine or speculative partial
host was added. Per the explicit pure-dependency exception, this unit records the
analysis only; finishing it requires `feat-flash` v2/Remix and `feat-bubble-pop`
v2/ambient-trigger hosts in a larger or split unit.

## Verification limits

- .NET 10 installed with the requested installer (channel resolution initially
  returned HTTP 403; the pinned 10.0.100 install succeeded and the channel retry
  subsequently installed 10.0.402).
- `dotnet build <project> -c Release --nologo`: Core, Avalonia and all 12
  Linux-targeted test projects listed in `.github/workflows/build.yml` succeeded,
  exit 0, zero errors (existing compiler/analyzer warnings remain). Test projects:
  Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests,
  Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests,
  Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests,
  Avalonia.Migration.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests,
  Avalonia.Snapshot.Tests (all names prefixed `CCP.`).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  554 warnings, zero errors; WPF app and tests cross-compiled, not executed.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed”;
  429 XAML references checked. The initial sandboxed attempt could not create
  temporary files; rerunning with write access passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this
  base. No ledger row was added or promoted and no new surface requires panic
  registration or a tester step.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1;
  3,555 passed, 5 Vosk tests skipped by their existing missing-model condition,
  1 failed: `BoardTilesTests.A_frame_fits_the_budget` (22.49 ms vs 12 ms limit).
  Separate `--no-build --filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`
  rerun also exited 1 (17.10 ms). No tests were changed or disabled.
- Core focused `--no-build --filter
  'FullyQualifiedName~PrizeOwnershipServiceTests|FullyQualifiedName~PrizesFeedTests|FullyQualifiedName~JackpotRemix|FullyQualifiedName~FlashMotion'`:
  exit 0, 85 passed, zero failed/skipped.
- Avalonia ownership/feed/account/badge/game UI tests compiled but not run, per
  the supplied cloud font-creation limitation; this base's ownership UI bootstrap
  has no explicit font setup. No desktop, notifications or real profile used.
- Windows-specific code compiled only; no Windows execution, native browser/GIF
  host playback, Vosk download, render-all or visual parity verification performed.
- `git diff --check`: exit 0. Documentation-only diff; no behavior claims promoted.

## Assumptions

- Treat the explicit pure-dependency exception as governing this unit when all
  remaining consumers require the named effect hosts and the complete set exceeds
  the cap; do not promote the row for a grant check with no usable effect.
- Wave-two consumers reached through the same grant predicates belong in the
  dependency inventory even though the ledger abbreviated them to host names.

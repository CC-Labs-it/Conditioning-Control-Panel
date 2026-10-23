# fin-ctrl-v2-get-it-row

- Dependency-only analysis: no application or test code changed. Keep the single
  `ctrl-v2-get-it-row` ledger entry **stub**; its two named blockers remain real.
- Compared the WPF row and both live feature-card callers with the Avalonia row,
  purchase adapter, ownership adapter, and feature cards at base
  `cadbc3f87a49ff22fac87d529437878d5eff7e64`.
- Fetched `parity/trunk` read-only at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; no import
  credits apply. The existing base implementation wins.

## WPF behavior already present

`ConditioningControlPanel/Controls/V2GetItRow.xaml` and `.xaml.cs` define the
blurb, tooltip, automation ids, status, price plate, Get it / Sign in button,
owned hiding, and `RowChanged` box measurement. The Avalonia control preserves
these, using the existing Fluent diamond. Both heads use Core
`V2PurchaseService`, `V2PurchaseRule`, and `V2WalletAdoption`.

The Flashes card has the live row. Counter reads start on effective visibility;
states cover checking, signed out, offer, insufficient balance, unavailable,
busy, and refusal text. Sign in opens the shell login. Only a priced, enabled
offer can confirm; declining sends nothing. Yes buys at the confirmed price,
so a reprice refuses instead of charging an unseen amount. The shared service
blocks duplicate in-flight buys, reuses receipt keys, scopes the cache to the
account, and refreshes after purchase. Success toasts, adopts the receipt balance
under the original account, hides the row, and rebuilds the Flashes motion
picker. Attach/detach subscriptions and UI-thread marshaling are already present.
Without a sandbox loopback service, a signed-in row says unavailable.

## Every remaining row gap and its dependency

1. **Ownership after restart / profile refresh** (`svc-prize-ownership`).
   Avalonia `Platform/PrizeOwnership.cs` holds only counter-reply grants for the
   current run. WPF attaches Core `OwnershipService` / `PrizeGrants` in
   `App.xaml.cs:2744-2763`, then profile validation/polling feeds snapshots through
   `Services/Settings/ProfileSyncService.cs:592-593,680-697`
   (`ApplyValidatePrizes` / `PrizeFeed.Apply`). A previously owned
   prize should hide the offer once that account snapshot arrives, including
   after restart. This head lacks that feed. WPF's ownership store is also
   explicitly in-memory; parity requires server rehydration, not a new local
   entitlement file.
2. **External grant-change repaint** (`svc-prize-ownership`). WPF's row subscribes
   to `PrizeGrants.GrantsChanged` on load and unsubscribes on unload. Its feature
   cards rebuild their owned options on the same event. Avalonia has no general
   grants event yet: its service changes cover counter replies and the Flashes
   card rebuilds on the row hiding. A grant or revocation arriving through profile
   sync or another purchase surface therefore cannot independently repaint this
   row and its box. Adding a listener without the missing publisher/feed would
   not close this behavior.
3. **Bubble Pop placement and post-purchase options** (`feat-bubble-pop`). WPF
   mounts `RowGetBubblesV2` inside `V2Box`, configures `bubbles_v2` with
   `v2_get_bubble_blurb` / `label_bubbles_v2_box`, and remeasures the box on
   `RowChanged`. Its grant-driven motion picker exposes Rain / Spiral In / Mix
   and associated owned controls after purchase. Avalonia's Bubble Pop card has
   no v2 box or picker, and its overlay still supports FloatUp only (see
   `Views/Overlays/BubbleOverlay.cs:36-42`). The ledger explicitly assigns Rain,
   Spiral In, Brain Drain, and Magnet to the unfinished `feat-bubble-pop` row.
   Selling that bundle here before its effects/options work would expose a
   purchase whose advertised capabilities cannot be used.

These are live WPF paths, not P122 dead code. Reproducible pinned caller proof:

```sh
git grep -n 'RowGetBubblesV2\|RowGetFlashesV2' cadbc3f87a49ff22fac87d529437878d5eff7e64 -- ConditioningControlPanel/Features
```

Matches include Bubble Pop XAML line 209 and its Configure call at code-behind
line 27, and Flashes XAML line 312 and its Configure call at code-behind line 36.
No P122 promotion or render-all claim is made. No new surface needs panic
registration, and no new behavior needs a new test or mutation proof.

## Verification limits

SDK: .NET 10.0.402, installed with the task's .NET 10 installer command.
The original snapshot was read-only; the requested temporary-clone fallback was
verified at the exact base commit before branching. Initial test-project copies
filled the temporary disk (exit 134); removing only generated test outputs and
building/cleaning each project serially resolved it. Results below are the reruns.

`dotnet build <project> -c Release`:

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 errors |

These cover every Linux-buildable test project named in
`.github/workflows/build.yml`. Existing compiler/analyzer warnings remain.

- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with
  `DiffEngine_Disabled=true`: exit 0; **3545 passed, 0 failed, 5 skipped, 3550 total**.
  The five existing Vosk speech tests skipped because the model is unavailable;
  the task reports its download blocked with HTTP 403. Tests were not changed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; **0 errors**
  (554 existing warnings). WPF and Windows-specific code compiled, not run.
- `bash .github/scripts/core-guards.sh`: exit 0; **All Core guards passed**;
  no forbidden APIs (3 baselined), 429 XAML type references checked.
- `git diff --check`: exit 0.
- `Tests/CCP.Avalonia.Tests` compiled, but `--filter FullyQualifiedName~V2GetItRowTests`
  was **not run**, following the supplied cloud font-creation limitation. This
  base's test setup has no cloud-test-fonts bootstrap. The supervisor's GitHub
  runner must execute the existing three row tests. No runtime/render proof is
  claimed, and no Windows behavior was executed.
- No render-all: this is a dependency-only analysis, not a P122 dead-caller claim.
  No live Back Room requests or real desktop/user-profile tests were made.

## Assumptions

- Treat the explicitly named ownership and Bubble Pop v2 prerequisites as other
  units, following the task's dependency-only rule. Preserve the existing ledger
  row and its honest stub status; this analysis does not claim completion.
- Restore ownership from the server as WPF does; do not invent disk persistence
  from the ledger's shorthand about ownership across runs.

NOOP: svc-prize-ownership and feat-bubble-pop (Bubble Pop v2 box/options/effects).

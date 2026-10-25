# fin4-win-emi-codex: dependency-only analysis

- No application or test code changed. Keep `win-emi-codex` **stub**: its live offer route depends on the unported EMI Desk speech/ask host. This uses the unit's explicit dependency-only NOOP exception.
- Base and WPF reference: `be12c4537ed3a9d12a440bc36db51583bed8d25d` (`avalonia-port/chaos-payout-fallin-c`). Checkout was clean after restoring that exact commit; the cloud branch name was `work`.
- Read the sole ledger row, both cited WPF window files, `Services/EmiDesk/EmiCodex.cs`, and the live offer callers/handlers below. Compared their Avalonia counterparts and existing Core rules before considering imports.
- Read-only trunk inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (`parity/trunk`, fetched as `FETCH_HEAD`). **No trunk code imported.** Existing base files take precedence.

## Behavior inventory

The plain window already ports the cited WPF window behavior: opaque resizable frame; localized heading, failure explanation and footer; sorted chapter/volume contents; restore bookmark with first-chapter fallback; skip selectable volume headers; persist page turns; reset scroll position; title, blurb, paragraphs, numbered steps, figure captions, callout/limit boxes and final margin; tolerate empty content and malformed blocks; website escape hatch and close button. Core supplies the shared chapter parser and bookmark. Existing tests are `EmiCodexReaderTests`, `EmiCodexRulesTests`, and `EmiBookDemoTests`.

Every remaining gap named by the row is outside that reader:

1. **Live delayed offer.** WPF `EmiDeskService.MaybeOfferBookOnSummon` skips summon 1, previous readers and an already spent `bookOffer`; summon 2+ schedules after the greeting. `EmiTourNarrator.OnFinished` schedules after either completion or skipping. Avalonia has neither caller/narrator nor a delayed codex offer service.
2. **Scheduling and brakes.** WPF `EmiCodex.MaybeOfferSoon` marshals to the UI dispatcher, checks content before arming, stops/replaces the previous timer, clamps negative delays to zero, and waits 20,000 ms by default. At the tick it rechecks content, open state and lifetime read count through `MaybeOffer`, preserving the specific refusal log. Away/silent conditions must not consume the once-ever offer before the engine is reached. Core `EmiCodexRules` already implements content/refusal rules; Avalonia has no caller.
3. **Moment delivery and safety.** WPF `Fire` publishes `MomentFired`, processes safety holds even while away, otherwise draws only while the widget is out; `Speak` checks visibility, presentation/farewell suppression and dispatches engine lines, holds or asks. Its ever/1 budget, ask feasibility probes and draw ordering must stay authoritative. Core `EmiLineEngine` exists on this base, but Avalonia `EmiDeskService` has no `Fire`, `Speak`, `ReleaseHold`, or engine probe setup. Its summon path only shows the widget and records the summon.
4. **Ask surface and answers.** WPF `ShowAsk` cancels competing activity, acknowledges the ask, then displays `.`, `..`, `...` for 420/420/520 ms before the question and two focused chips; it waits without timeout. Answers clear the ask, record answered state, fire `askAnswered`, speak the chosen reply BEFORE the effect, then release a parked line after 900 ms. Escape, dismissal, safety/full-screen moments and teardown cancel without answering, record ignored state, and use the appropriate visual/no-visual cleanup. Bubble/chip placement follows body size, work area and book-side avoidance. Avalonia declares `OnBubbleTextCore` but has no implementation, ask state, chips or answer/cancel handlers. `FireDeskEvent` is empty.
5. **Accepted effect.** WPF `EmiOffers.OpenBook` handles `book:open` by calling **`EmiBook.Open`**, then `effectFired` with `fromAsk`; it does not open `EmiCodex`. Avalonia already reaches its flipbook from the codex target and `?` chip, persists `BookBookmark` and calls `NoteCodexOpened`; the ask/effect route is missing. A substitute direct button or dialog would bypass the required cadence, replies, engine budget and cancellation.
6. **Unreachable hosted codex service.** WPF alone contains the single-instance hosted/plain router, explicit chapter bookkeeping before focus, open-count update, runtime/bundle probe before browser construction, minimized-window focus, close/reset/disposal and main-owner handling for the plain fallback. The hosted body is a 1000x760 resizable unowned window centered on main, local `ccp.game` mapping with navigation lockdown, document-created bookmark/manual/reduced-motion boot payload, ready/open/target/tour/close bridge handling and acknowledgements. Targets use current availability/lock checks; tours require strict named-enum parsing and close the book before starting. Runtime, bundle, navigation, browser-process and router faults fall back to the plain reader. These service/host/bridge behaviors have no Avalonia implementation, but also no live WPF opener (proof below).

## Pinned reachability proof

Run against the frozen base, not the fetched trunk:

```sh
git grep -n -E 'EmiCodex[[:space:]]*\.[[:space:]]*Open[[:space:]]*\(' be12c4537ed3a9d12a440bc36db51583bed8d25d -- ConditioningControlPanel
```

Result: **no matches, exit 1**. A broader `git grep -n 'EmiCodex.Open'` finds only two `OpenManualInBrowser` calls (plain reader footer and flipbook complete-guide button), not `Open` calls. WPF `EmiTargets` routes the codex target to `EmiBook.Open`; `EmiOffers.OpenBook` does the same. The hosted/plain router is therefore dead in this pinned WPF tree. Do not invent a user opener for it.

P122 does **not** justify promoting the whole row: the delayed `bookOffer` route is live WPF behavior and remains absent here. No render-all success is claimed and the row is not promoted to wired/verified. No new surface needs panic registration because none was added.

## Dependency and line-cap decision

Required dependency: **EMI Desk moment/speech/ask host, including reply-chain playback, safety cancellation and tour narration** (`win-emi-desk`). This is a head integration gap, not a missing Core line engine or missing chapter content.

The available trunk port illustrates the scope before adapting to our more advanced base:

| Trunk file | Lines |
| --- | ---: |
| `EmiDeskService.Moments.cs` | 255 |
| `EmiDeskWindow.Bubble.cs` | 160 |
| `EmiDeskWindow.Ask.cs` | 412 |
| `EmiDeskWindow.Chains.cs` | 454 |
| `Tours/EmiTourNarrator.cs` | 147 |
| `EmiDeskService.Offers.cs` | 202 |
| **Total** | **1630** |

This excludes summon/dismiss and startup/panic integration, codex timer/brakes wiring, focused behavior tests and documentation. Trunk also introduces Core `EmiDeskBus` (45 lines), `EmiOffers` (551 lines) and `EmiVox` (478 lines); importing them wholesale would expand scope further. Those counts are scope evidence, not a proposal to copy every line. The trunk bubble itself still documents missing book-side/work-area behavior, so importing it would not establish WPF parity. A faithful host plus offer wiring and tests cannot be treated as a small codex fix within 1000 added lines; reducing it to one question would omit live WPF interactions. Follow the explicit NOOP exception instead of spending the cap on disconnected scaffolding.

Next unit should land the desk host with focused cadence, yes/no, cancellation/hold, reply/effect ordering and sandboxed panic tests. Then wire the two scheduling callers and shared offer brakes here, testing re-arm, delay, read/open/content gates, away retry and ever/1. Preserve the existing base chapter reader, bookmark, placement and flipbook implementation when adapting trunk.

## Verification limits

SDK: .NET 10.0.402. The prescribed channel installer initially received HTTP 403 from its version lookup endpoint, then successfully installed from the primary SDK download. All commands below use Release configuration. No WPF UI, Windows-only behavior, browser host or real desktop interaction is exercised by this documentation change.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors (48 existing warnings).
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (192 existing warnings).
- All 12 Linux-compatible test projects named in `.github/workflows/build.yml` built with `dotnet build <csproj> -c Release`: exit 0, 0 errors each. These are `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, and `CCP.Avalonia.{Tests,Consent.Tests,Language.Tests,CatalogueStartup.Tests,Migration.Tests,Scheduler.Tests,Report.Tests,GeneralSettings.Tests,Snapshot.Tests}`. Warnings remain unchanged; the main Avalonia test project reported 82 warnings.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed”; 429 XAML references checked against 1608 app / 2158 Core types. The initial sandbox invocation could not create temporary files; the writable runtime rerun passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; this base does not contain the script. No ledger row was added or promoted.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; **4005 passed, 5 skipped, 0 failed**, total 4010, duration 43 seconds. The five existing Vosk speech tests self-skip without the model; no test was changed or disabled.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~EmiCodexRulesTests`: exit 0; **13 passed, 0 skipped, 0 failed**.
- Avalonia `EmiCodexReaderTests` / `EmiBookDemoTests` and render-all were **not run**, following the supplied environment limitation on font creation. Their assembly compiled with zero errors; GitHub/rcheck remains the runtime gate. No render proof or UI parity verification is claimed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 0 errors, 557 existing warnings. The WPF application and tests compiled for Windows but were not run on Linux.
- No new behavior was added, so no new tests or fail-before-fix claim applies. Other test assemblies were compiled rather than executed.
- `git diff --check`: exit 0.

## Assumptions

- The row includes its live offer-flow gaps despite the cited plain window itself already being ported; therefore the dead-host proof alone cannot finish the row.
- The explicit dependency-only exception permits a documentation-only branch while leaving the existing single ledger row unchanged at stub.
- The 1000-added-line limit applies to behavior, tests and documentation together; no compressed or selectively weakened host is introduced to evade it.

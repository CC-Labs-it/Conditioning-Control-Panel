- Part A: add Core `EmiMomentBus`, extending the existing Avalonia `EmiDeskService`. Publish before speech, keep away safety holds, recheck visibility on the dispatcher, and retain locked-silent moments.
- Add the desk bubble and persistent ask chips: 420/420/520 ms typing, one parked line, answer/reply/effect ordering, ignore reaction, screen-takeover cancellation and timer teardown. Route `book:open` to the existing book surface; refuse unsupported effects in the engine probe.
- Wire the existing ring-open user path and register `emi-moment-bubble` in PanicSurfaces and its independent SafetyCritical list.
- Add focused Core/headless tests; keep touched parity IDs unique and stub, with a numbered tester step.
- Source: adapted the WPF reference at base `39260f198fad67d90bc988c63a00480830e5fefc`. Read-only `parity/trunk` fetched at `a4ee52d40`; no trunk code imported.

## Verification limits

- .NET 10 builds: Core, Avalonia and all twelve Linux test projects named in `.github/workflows/build.yml` succeeded (exit 0, zero errors). Existing compiler warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. This base has no `test-plan-coverage.sh`.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3956 passed, 5 existing Vosk skips, 0 failed. Initial run failed after disk exhaustion; rerun passed after generated-output cleanup.
- Relevant headless filter (EmiMomentBubbleTests, PanicSurfacesTests, EmiDeskSummonTests): exit 0; 16 passed, 0 failed. Includes the real ring caller and a panic against the service-owned ask.
- WPF test project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (compiled only; Windows cannot run here).
- Mutation proof: temporarily remove the production `_ask = ask` assignment; the headless parking test fails (exit 1), expected question versus actual incoming line. Restored the assignment; final build/test run exited 0 with 16 passed, 0 failed.
- The full Avalonia test suite was not run. Tests used the existing isolated test runtime, without the real desktop or user profile.
- Initial parallel builds and the first WPF copy phase exhausted disk space. Linux builds were repeated serially with output cleanup; this changed no source or test expectations.

## Remaining work and assumptions

- Deliver the core host and one real caller as part A. The complete chain/voice/effect host and all moment producers are outside this unit's line budget; no new generic abstraction or OS integration was introduced.
- Existing Core line-engine policy remains authoritative for cooldowns, ask feasibility, ignore streaks, hold tails and ever/1 budgets. Only `none` and the existing `book:open` action are feasible on this head so far.
- Remaining: summon greetings and book-offer scheduling/tour trigger, other callers, complete effect routing, chain body frames/blipese, and the shared avatar speech-gap stamp. Codex's hosted page/bridge remains a separate gap. Neither touched row is verified.
- Use Avalonia controls inside the existing widget for the bubble, keeping the whole question/chips inside one screen-clamped layout. Windows behavior cannot be run in this Linux environment.

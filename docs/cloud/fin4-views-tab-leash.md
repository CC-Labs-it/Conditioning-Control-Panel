# fin4-views-tab-leash

- Add holder cards to the existing shared section, after incoming offers and the own card. Retain card identity and the open action sheet across polls; remove ended cards and rebind receipt subscriptions with the service.
- Adapt WPF holder action sheets and send wording: assignment/reward/punishment presets, hidden forbidden punishments, daily limit, DND, catalogue and numeric video picking, recipient video ceiling, three-second results, receipt text and ten-second tug throttle. Reuse existing Core grammar, status wording and safety boundaries.
- Register holder sheet teardown with panic. Add six focused headless behavior tests and numbered manual tester steps.
- Keep `views-tab-leash` **stub**. This is a bounded user-visible slice, not a completed parity claim.
- Source: WPF reference at `67181df5b4b5695ee5c30bc407f715daf142e3f7`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap inventory

Before this unit, the head lacked all holder cards, their summaries, sheets/actions and receipt repainting; release confirmation and snap replay; the friend Offer chip and first-offer explainer; page/self/ask help; the accepted snap and automatic arriving-offer queue; sticker shelf and holder preview; complete own/holder artwork/layout, explainer art/entrance, LeashFx and panic-key hold-to-cut.

This unit ports holder cards, sheets/actions and receipt repainting. Remaining: release confirmation/action, snap replay, Offer chip/first-offer explainer, all help doors, snap and automatic-offer/event host (including held notices/seen reporting), sticker shelf/preview, full own/holder layout (minute ring, quest streak detail, receipt dots, drawn week cells, avatars/chains/card art), explainer art/entrance, LeashFx and hold-to-cut. These are live WPF paths, not P122 dead callers. Together they need multiple surfaces and tests beyond the 1,000-added-line unit budget. The Core service exists; this is not a pure dependency NOOP.

## Assumptions

- Prioritize holder interaction and preserve the existing section/service over introducing another host. Plain figures and Fluent sticker reward glyphs are deliberate ponytail shortcuts; remaining visual work is explicit in the ledger.
- Keep the existing one-click safety cut and its honest hint until hold-to-cut is ported.
- Use the required writable fallback clone because the published checkout cannot update its Git metadata.

## Verification limits

- All 13 Linux projects (Core, Avalonia and workflow Linux test projects): `dotnet build -c Release -m:1`, exit 0, zero errors. Existing warnings remain.
- Windows-targeted WPF app and test build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, exit 0, zero errors (512 existing warnings); compiled only, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; the script does not exist on this pinned base. Added the requested numbered tester plan anyway.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; 2,812 passed, five existing Vosk skips (model unavailable), zero failures.
- Combined `LeashHolderTests|LeashPageTests|LeashAskTests|PanicSurfacesTests` clean run before final paint review: exit 0, 19 passed, zero skipped/failures. Final source compiled with zero errors; final combined rerun: exit 1, ten passed and nine existing shell tests failed with the documented environment `Could not create glyphTypeface` issue (`Segoe UI Black` / `Consolas`). All six new holder tests passed in that final run. The earlier green run is not a claim that the final shell rerun was green. Six `LeashHolderTests`: exit 0, six passed. Initial combined run: 18 passed / one failed because the fake reply omitted `status: sent`; corrected the fixture, without weakening assertions.
- Disk exhaustion affected intermediate build/test attempts; generated outputs were cleaned before final retries. No source or tests were removed to address infrastructure failures. A Core retry made during disk exhaustion failed; the clean final retry again passed all 2,812 tests (five existing Vosk skips). Windows runtime, real server interactions, desktop panic hooks and visual render-all are not exercised in this cloud task. The existing head reports zero conditioning minutes until its feature-day-log dependency is ported; this unit does not claim completion of minute assignments.

# fin4-shell-deeper-hub

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a` (cloud branch `work`); implementation branch `avalonia-port/cloud-fin4-shell-deeper-hub`.
- WPF gap inventory at that SHA: manual import was empty; enhancement drops warned that no library existed; shell never instantiated the already-ported Core library, so watcher refresh and bundled-demo seeding had no host; reveal had no scroll/highlight; row duration text and sequential probe/cache backfill were absent. Inspection also found immediate search rather than WPF's 150 ms debounce and no missing-media tooltip.
- Reuse Core EnhancementLibrary for seed/watch/import. Subscribe once per shell, refresh visible hub after the service's 300 ms debounce, unsubscribe/dispose on close; every tab entry still rescans. Bundle the existing welcome demo for build and publish. Preserve edited demos and the once-only seed flag.
- Adapt WPF import order and feedback: extension gate, schema sniff, duplicate check, deserialize, promote; remember the source directory only after a successful import; refresh immediately; success toast then warnings, or duplicate/skip info. Reveal the last successful import, otherwise the last duplicate, without resetting filters. Preserve highlight through watcher rebuilds and clear after 1800 ms.
- Probe only existing local media, serially and once per source per session. Persist/cache successful results, update matching rows, refresh each five successes and at end, and sweep again for entries added during probing. Render WPF duration format and keep authored metadata ahead of cache. Use the existing shared LibVLC with parse-only timeout of 5000 ms on a worker; no playback or audio device opens.
- Match WPF search debounce and warning tooltip. Existing editor/player/menu/delete paths are retained; no new window or notification surface requires panic registration.
- Add focused headless import/reveal, watcher, demo, duration and search tests, plus a Core duration-cache regression test. Fail-before proof with only the cache lookup removed: exit 1, expected 125, actual 0. Restore the fix afterward.
- No code imported from `parity/trunk`; it was fetched read-only as required. WPF source used is pinned to the base SHA above. Tutorial/browser/file-association entry points are tracked by `shell-deeper-tab`; they are outside this row.

## Verification limits

- SDK: .NET 10.0.402. Linux builds of CCP.Core, CCP.Avalonia and every Linux test project listed in build.yml completed with zero errors. CatalogueStartup initially hit MSB4166 during overlapping builds; its single-node retry passed. Final headless test compilation also passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. `test-plan-coverage.sh` is absent on this base (attempt: exit 127); numbered WPF/Rows tester steps were added manually.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (554 existing warnings); compiled but not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0, 3954 passed, 5 skipped, 0 failed, 3959 total. Vosk model-dependent tests were skipped by their existing environment guards; no test was changed to skip. Earlier concurrent-build run exited 1 on the unrelated BoardTiles frame budget (18.40 ms); the complete retry passed.
- Cache regression: fail-before exit 1 (expected 125, actual 0); restored-fix test exit 0 (1 passed).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~DeeperHubTests`: exit 1, 2 passed, 6 failed. Both duration-only tests pass. Every shell test fails before its assertions at glyphTypeface creation for Segoe UI Black, the supplied environment limitation; no tests were weakened or skipped. Supervisor/GitHub must run those six tests with fonts.

WPF and Windows desktop behavior cannot run on this Linux worker. Native media parsing requires an installed LibVLC; focused tests inject durations without opening a device. The file-picker UI requires manual desktop testing. No PR was opened.

## Assumptions

- LibVLC parse-only is the cross-platform replacement for WPF's MediaFoundation-first duration probe. Unknown/unavailable codecs retain unknown duration and are not retried within the session.
- Reveal uses Avalonia BringIntoView in the existing outer scroller; WPF's item-offset scrolling API is not portable.

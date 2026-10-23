# fin-win-mod-creator

- Base verified: `cb33de077c20e32a12fe692810c954b330bc1ba7`; initial snapshot differed and was corrected before edits.
- Add the 34 WPF UI Art slots in Nav Doors, Play Wall, Vault & Dashboard, Session & Intake order, with the same dimensions and resource keys. Existing image load/export handles their files.
- Move pure WPF image and art-framing rules into Core, unchanged; both heads reuse them. Browse/Drop now reject extension mismatches and confirm images larger than 4 MB. Loaded framing survives export and clears with image replacement/removal/reset.
- Add Event Audio rows, mp3 attachment, preview, folder import, live slugs, import hints, read-only loaded filenames, duplicate-file suppression, first-slug-wins export warnings, removal and reset. Export and Load call the same panel methods tested directly.
- Use the existing stoppable audio seam: one preview, toggle/replace/natural-end cleanup, stale-completion protection, reset/close stop, and `mod-creator-preview` panic registration.
- Wire Modding tutorial `mod:*` callbacks and invoke the initial callback, matching WPF's order.
- Add eight focused headless tests; run the existing pure WPF framing export suite through Core and add image-rule cases.
- Read-only source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported: its Avalonia head has no implementations of the missing partials. Adaptations use the frozen WPF files at the pinned base above.

## WPF gap audit

At the pinned base, the remaining row behaviors were:

- UI Art: four slot groups, paths, labels, sizes and file hints, inheriting the generic image picker/load/export; image type rejection and large-file confirmation were also stubbed. Implemented here.
- Art Framing: Frame visibility/count, framable registry bindings, bitmap validation, per-surface mock geometry, scrims/captions, fluid-width trim guidance, drag/pointer capture, wheel zoom/clamps, double-click/reset-all, editable working copies, Cancel/Done, sanitized four-decimal non-default manifest data, and drop-on-image-change. Only the manifest lifecycle is implemented here; the complete editor remains missing.
- Mantras: enabled/removable phrase/prompt/response cards, prompt containment hint, prompt/response audio attach/clear/preview/missing-file indication, collision-free names, retry/timeout rows, blank/invalid entry rules and warnings, unique IDs, prompt fallback, JSON/audio load/export/reset. Entire panel and side-file lifecycle remain missing.
- Event Audio: editable authoring and attachment, slug preview, exact runtime slug contract, sorted folder import/dedup/non-slug hints, loaded read-only stems, optional/incomplete row omission, first duplicate wins with export warning, removal/load/export/reset. Implemented here using Core's existing slug helper, which the WPF runtime delegates to.
- Portraits: mutually exclusive manifest-import/simple-author modes; skin/emotion/file validation and missing-file warnings; folder names/dedup/PNG checks, skin labels, neutral/default emotion selection, director defaults, full imported-field retention, recursive resource copy, load/clear/export. Entire panel and side-file lifecycle remain missing.
- Animated Emotes: prepared set folders, next free set 1–7, duplicate selection rollback, map/clip validation, idle/talk/legacy/mood/stem/click/expressive reference union, missing/unused clip and timing/talk warnings, 50 MB soft warning, stats, valid-only filtered recursive copy, sorted load and clear. Entire panel and side-file lifecycle remain missing.
- Audio preview: single raw-volume clip, toggle, replacement, natural-end button reset, reset/close stop. Implemented with panic stop added per head policy.
- Tutorial: section callback rewriting and first callback. Implemented through the existing head tutorial service.

The remaining editors are live WPF features, not dead callers or pure dependencies. The ledger stays **stub**. Under the 1000-added-line cap, prioritize reachable authoring and preview behavior and preserve loaded framing; the three remaining content types still lose their side files on re-export. No P122 claim.

## Verification limits

- `dotnet --version`: 10.0.100. The requested channel installer could not resolve its latest-version endpoint (403); installing the published 10.0.100 SDK succeeded.
- `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false`: exit 0 for all 14 required projects; result tail `0 Error(s)`. Warnings remain unsuppressed.
  - Heads: `CCP.Core/CCP.Core.csproj`, `CCP.Avalonia/CCP.Avalonia.csproj`.
  - Linux test projects: `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Tests` (each under `Tests/<name>/<name>.csproj`).
  - `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `512 Warning(s), 0 Error(s)`; this also compiled the WPF app.
- Final Avalonia test rebuild after cleanup and the eighth regression test: exit 0, `222 Warning(s), 0 Error(s)`.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: final exit 0, `Passed: 2853, Failed: 0, Skipped: 5, Total: 2858`, 42 seconds. Includes 27 existing WPF framing cases and seven image-rule cases.
- First full Core run, concurrent with the WPF build: exit 1, board frame-budget case measured 15.75 ms against 12 ms; 2852 passed and five skipped. The subsequent full run without concurrent builds passed; tests were not changed or weakened.
- Early compile errors from WPF service-name and test icon-alias imports were corrected. A premature MSBuild worker exit was resolved with single-node compilation.
- `git diff --check`: exit 0. Pure-rule code compared to the pinned WPF originals after removing comments/whitespace: identical for both classes.

- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ModCreatorFinishTests|FullyQualifiedName~ModCreatorPanelsTests|FullyQualifiedName~ModCreatorBarksTests|FullyQualifiedName~PanicSurfacesTests"` and render-all are not run: the supplied cloud environment limitation reports font creation failure until the font lane lands. Compiled tests require supervisor rcheck; UI fixes are not fail-proven here.
- Windows WPF is cross-compiled only, never executed on Linux. No new Windows-specific code.
- Native audio playback/device output, OS file pickers and visual crop fidelity cannot be demonstrated by provider-based headless tests.
- No Vosk download attempted: the supplied environment reports HTTP 403; full Core results identify any skipped model-dependent tests.

## Assumptions

- Keep the ledger stub and document the remaining editors rather than exceed the explicit line cap.
- Keep WPF slot keys, ordering, slug behavior and boundary rules; use native async pickers/dialogs and existing Fluent icons.
- Share pure rules with WPF by moving their declarations to Core, preserving their behavior and existing WPF call sites.
- Shorten UI Art descriptions where WPF advertises the unavailable Frame editor; retain sizing and shared-file guidance.

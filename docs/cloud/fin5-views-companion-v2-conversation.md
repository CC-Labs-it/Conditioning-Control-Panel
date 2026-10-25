# fin5: Companion V2 conversation

- Base verified at `781cbecc4e1e75d166fd145ff73869642469e7fc`; the local snapshot branch was reset to that commit before implementation.
- Audited the row against WPF ConversationPage, ConversationPageVm, ConversationRecap, PreferredNameEditor, PerkPicker, PersonalityPage, PersonalityStudio and the cap section of MainWindow.Inbox. Actual base gaps were portrait dust, the Personality picker host, its PersonalityStudio editor, and the daily-cap deferred Inbox offer. Historical room-collapse, page-adoption, navigation, name-editor and recap gaps were already closed on this base.
- Mounted the existing live picker before presets. Ported the WPF detached-copy studio: character-only drafts, own-preset update/delete, custom-override copy behavior, sample cleanup, field headroom, awareness reactions, validation and cancel. Shared decisions live in Core; the new window closes on panic without saving. Optional Avalonia TextBox values are normalized when cleared.
- Restored the portrait dust with WPF intensity, density and tint using the existing shared ambient clock and its visibility/motion gates.
- Ported the consumed cap conversion rules and the ten-second deferred Inbox watch. The visible conversation is never interrupted; the offer is tier-aware and stamped once per local day. Its one-at-a-time notice supports Escape, dragging outside buttons, close, checkout and panic.
- Added behavior assertions to CompanionPagesTests, Core studio/cap tests, panic registrations and numbered tester steps. Replaced the row's duplicated historical notes with the current evidence; promoted to `wired`, never `verified`.
- Source credit: adapted WPF files from the pinned base above. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported or existing base file overwritten.

## Assumptions

- The shipped shell always registers Personality, Permissions, Links and AI. WPF ConversationPage.OpenSheet returns through ShowTab for those pages; its unregistered-host preview fallback is outside the shipped user path. No dead-code P122 promotion is claimed.
- A direct call to the owning shell replaces WPF's one-subscriber ConversionSignals relay; the deferred behavior and cadence stay in the shell.
- The cap row uses the existing Inbox without adding an emoji icon. The neutral notice uses native Avalonia controls; shared passive-surface tracking and panic closure apply.

## Verification limits

- All twelve Linux test projects named in the workflow built successfully (`0 Error(s)`), including the headless UI tests. Explicit Core and Avalonia Release builds also succeeded. Existing compiler warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~PersonalityStudioRulesTests|FullyQualifiedName~ConversationRulesTests'`: exit 0, 29 passed.
- The five non-window-opening `PanicSurfacesTests` passed (exit 0), including the host source-scanning ratchet and registration/order checks.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `test-plan-coverage.sh` is absent on this base (attempt exits 127). A manual equivalent confirmed exactly one row for the ID and four numbered WPF tester steps.
- The pre-fix `CompanionPagesTests` attempt reached shell construction, then failed creating `Segoe UI Black` glyphTypeface (exit 1). It did not reach the new assertions, so no fail-before behavior proof is claimed. The final UI suite is compiled; font-dependent tests and render-all were not rerun here. Supervisor headless runs and visual review remain required.
- The first full Core run reported 4325 passed, 1 failed (the unrelated board-frame budget: 19.72 ms under concurrent compiler load), and 5 existing Vosk skips because the model is unavailable. The final full rerun passed: exit 0, 4326 passed, 0 failed, 5 existing model-dependent skips (4331 total).
- Windows-targeted WPF test compilation with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` succeeded: exit 0, 0 errors. It was compiled but not run; Windows desktop behavior cannot run on this Linux worker.
- An early concurrent Avalonia build encountered an output-file lock; the subsequent sequential workflow project builds succeeded. No tests were weakened, removed or newly skipped.

Final explicit Release builds of Core, Avalonia and CCP.Avalonia.Tests returned exit 0 with 0 errors after the cap exception-handling change. `git diff --check` passed. The final full Core run also cleared the earlier board timing failure.

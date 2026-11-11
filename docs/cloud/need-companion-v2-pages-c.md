# Companion knowledge links, re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`. Initial snapshot was `aa9918833722f057079e455167cb3d1b14462340`, clean on `work`; fetched the requested branch and reset to the exact base before editing.

Read both own-work diffs: `f3ed8d640b5b..3ce36f41238f` (pages-c) and `888cdbffe4d2..9b1714cac44a` (fin-conversation). Reused pages-c's editor and host changes, plus fin-conversation's knowledge-dialog panic tracking. No WPF, Core, safety refusal, lockdown or Scrolller behavior changed. All six reused localization keys exist in all ten Core language files; no new keys.

Paths in the table are relative to the companion folders unless a full path is given. WPF folder: `ConditioningControlPanel/Views/Controls/Companion/`. Avalonia folder: `CCP.Avalonia/Views/Controls/Companion/`.

| Behavior | Decision | WPF spec | Avalonia evidence |
| --- | --- | --- | --- |
| Shared list, title/URL/optional description, empty state | Ported | `KnowledgeLinksEditor.xaml:36`, `KnowledgeLinksEditor.xaml.cs:20`, `:51` | `KnowledgeLinksEditor.axaml:31`, `KnowledgeLinksEditor.axaml.cs:20`, `:61` |
| Load global links; save order | Ported | `KnowledgeLinksEditor.xaml.cs:36`, `:44` | `KnowledgeLinksEditor.axaml.cs:48`, `:54` |
| Add result, cancelled add is inert | Ported | `KnowledgeLinksEditor.xaml.cs:64` | `KnowledgeLinksEditor.axaml.cs:73` |
| Remove selected, no-selection notice | Ported | `KnowledgeLinksEditor.xaml.cs:74` | `KnowledgeLinksEditor.axaml.cs:81` |
| Page saves each edit and reloads on entry | Ported | `Pages/LinksPage.xaml:25`, `.xaml.cs:33`, `KnowledgeLinksEditor.xaml.cs:54` | `Pages/LinksPage.axaml:30`, `.axaml.cs:26`, `KnowledgeLinksEditor.axaml.cs:63` |
| Prompt dialog shares list, dirty tracking, Save and discard | Ported | `ConditioningControlPanel/Dialogs/CompanionPromptEditorDialog.xaml:381`, `.xaml.cs:122`, `:129`, `:234`, `:392` | `CCP.Avalonia/Views/Dialogs/CompanionPromptEditorDialog.axaml:355`, `.axaml.cs:85`, `:132`, `:237`, `:346` |
| Link dialog closes on panic, result stays null | Ported stricter old-lane cleanup | WPF `KnowledgeLinksEditor.xaml.cs:66` opens a modal; P06 panic cleanup extends WPF's stop surfaces, it is not a WPF modal-close claim | `CCP.Avalonia/Views/Dialogs/KnowledgeLinkEditorDialog.axaml.cs:22`, `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:100` |
| Video pool adoption/refresh | Already on base | `Pages/LinksPage.xaml.cs:28` | `Pages/LinksPage.axaml.cs:23` |
| Advanced prompt door | Already on base | `Pages/PersonalityPage.xaml.cs:52` | `Pages/PersonalityPage.axaml.cs:40` |
| Portrait dust tuning/lifecycle and conversation panic | Covered by PR #3313 | `V2/ConversationPage.xaml:46`, `.xaml.cs:29`, `:42` | PR head `7cd3db6067e499c1f1c311f3a193c60d26386a28`: `V2/ConversationPage.axaml.cs:30`, `:44`, `:58`; `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:50` |
| Personality picker host, refresh and advanced door | Covered by PR #3313 | `Pages/PersonalityPage.xaml:42`, `.xaml.cs:28` | Same PR head: `Pages/PersonalityPage.axaml.cs:28` |

PR #3313 fetched via `pull/3313/head`; its LinksPage ends after VideosHost, so it does not cover this editor. Old-lane unrelated AmbientFx test seam and picker changes are excluded.

Tests: `Tests/CCP.Avalonia.Tests/KnowledgeLinksEditorTests.cs` uses the assembly isolation hook and Skia headless drawing. Covers immediate persistence and save scheduling, empty state, add cancellation, removal, repeated reload without Changed, staged dialog Save/discard via real buttons, no-selection notice and idempotent panic cleanup. `PanicSurfacesTests.SafetyCritical` includes the new entry.

Part B outside the named gap: preview-only fallback sheet (`ConditioningControlPanel/Views/Controls/Companion/V2/ConversationPage.xaml.cs:101`); old fin lane perk-dialog panic registration (`9b1714cac44a:CCP.Avalonia/Views/Controls/Companion/PerkPicker.cs:24`), absent at base `CCP.Avalonia/Views/Controls/Companion/PerkPicker.cs:18`. Neither is silently claimed here. No remaining knowledge-editor feature is deferred.

Validation (SDK `/tmp/dotnet`, installed with `dotnet-install --channel 10.0 --quality GA --azure-feed https://builds.dotnet.microsoft.com/dotnet`; default feed returned 403):

- `dotnet build CCP.Core`: 50 warnings, 0 errors.
- `dotnet build CCP.Avalonia`: 201 warnings, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests`: 144 warnings, 0 errors.
- `dotnet test Tests/CCP.Core.Tests`: Passed 4635, skipped 6, failed 0, total 4641.
- Affected Avalonia filter: KnowledgeLinksEditorTests, PanicSurfacesTests, CompanionPagesTests, CompanionPromptEditorDialogTests and TestIsolationTests: passed 18, failed 6, total 24. All six failures are glyphTypeface creation for Inter or Segoe UI Black. New focused class alone: passed 1, failed 4 (fonts). Thus page persistence/reload ran; displayed dialog cases are compiled, not validated. No tests were skipped or relaxed.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `python3 .github/scripts/avalonia_docs.py`: avalonia_docs self-check OK.
- `git diff --check`: clean.

Font-blocked fail-proofs remain to run on a font-capable host: bypass deferred saving (settings change before Save), omit Changed (dirty assertion), omit dialog SaveToSettings (missing saved link), omit no-selection notice (no owned modal), omit panic close (dialog remains visible). The page test also asserts empty state, cancelled adds and repeat reload; their individual mutations have not been run here.

Executed fail-proof: removed `SaveToSettings()` and `CoreSettings.Save()` from the editor's immediate-save branch. `dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~KnowledgeLinksEditorTests.PageReloadsAndCommitsEachEdit` failed at line 51 with `Assert.Single() Failure: The collection was empty` (1 failed, 0 passed). Restored the original implementation byte-for-byte before rebuilding. This is a behavior assertion failure, not a font failure.

Restored fail-proof command: Passed 1, failed 0, skipped 0. Rebuilt both Avalonia and its test project from restored source. Final Core guards passed, docs self-check passed, and diff whitespace check passed.

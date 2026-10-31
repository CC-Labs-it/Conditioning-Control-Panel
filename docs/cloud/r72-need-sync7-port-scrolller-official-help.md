# Sync7 official note re-cut

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`. Old lane fetched at
`5d45af7f3dd3`; inspected its own diff from `39260f198fad` (279 added, 1 removed).
Only the entrance was absent from the current Assets note. Policy, consent,
credit, help and settings persistence stay on the base implementations.

| Row | Disposition | WPF spec | Current counterpart |
| --- | --- | --- | --- |
| s7:208 | Ported entrance; note, plan, Nice, stamped line already present | MainWindow/MainWindow.OnlineMeter.cs:100-102,427-454,480-491 | CCP.Avalonia/Views/Tabs/AssetsTabView.OfficialNoteLanding.cs:25,40,70; AssetsTabView.OnlineMeter.cs:58-94; AssetsTabView.axaml:251-272,319 |
| s7:208 backup | Already present in Core | Services/Settings/ProfileSyncService.cs:4420,4476 | CCP.Core/Services/Settings/CloudSettingsBackup.cs:79,138 |
| s7:214 | Compact stamp/text/Nice band already present | Views/Tabs/AssetsTabView.xaml:280-311 | CCP.Avalonia/Views/Tabs/AssetsTabView.axaml:251-272 |
| s7:248 | Words already present as tooltip; paper slip remains missing | Controls/OnlineMediaHelpSlip.cs:21-64; MainWindow/MainWindow.OnlineMeter.cs:105 | CCP.Avalonia/Views/Tabs/AssetsTabView.OnlineMeter.cs:41,48-49 |
| s7:259 | Shared corrected paragraphs already present; pinnable slip/navigation remains missing | Controls/Leash/LeashHelpSlip.cs:577-611; MainWindow/MainWindow.OnlineMeter.cs:105 | CCP.Avalonia/Views/Tabs/AssetsTabView.OnlineMeter.cs:41,48-49 (tooltip only) |
| s7:262 | Words-only help already present | Controls/OnlineMediaHelpSlip.cs:44-61 | CCP.Avalonia/Views/Tabs/AssetsTabView.OnlineMeter.cs:48-49; CCP.Core/Services/Fyp/Online/OnlineMeterRules.cs:133-136 |

WPF paths in the table are relative to `ConditioningControlPanel/`.
No PR-owned help-slip or sibling meter feature was imported. No new keys,
Core types or WPF edits. The existing ten catalogues remain unchanged.

The finite animation is adapted from the old lane. It runs once per Assets view,
on first effective visibility while loaded. A hidden parent defers it, and hide
or detach cancels the active animation. Full uses -6 px to 0, opacity .2 to 1,
420 ms cubic out. Reduced and Gentle fade only; Off skips the entrance.
Gentle timing uses the existing Core RiseMs rule, matching WPF.

Font-independent definition coverage: `OnlineMeterSurfaceTests.Official_entrance_definition_matches_Wpf`
checks both cues, opacity endpoints, transforms, duration and cubic easing.

Focused lifecycle coverage: `OnlineMeterSurfaceTests.Official_note_lands_only_on_first_effective_show`
asserts actual keyframes/duration/easing through an instance runner seam, waits
for actual loaded/ancestor visibility events, and checks hide cancellation and
no replay on repaint or detach/reattach. The lifecycle test keeps the actual note and view wiring but removes unrelated
Assets content and the note text before layout to avoid unavailable cloud fonts.
Tests use the existing Skia dispatcher
and assembly-wide process isolation. Desktop appearance remains untested.

Part B, outside this triage gap: the interactive paper slip and idempotent pin
navigation (WPF Controls/OnlineMediaHelpSlip.cs:21-64 and
Controls/Leash/LeashHelpSlip.cs:577-611; current tooltip at Avalonia
AssetsTabView.OnlineMeter.cs:41,48-49). No broader parity claim is made.

Validation on .NET SDK 10.0.403 (`/tmp/dotnet/dotnet`):

- `build CCP.Core -c Release`: 0 errors, 50 existing warnings.
- `build CCP.Avalonia -c Release`: 0 errors, 201 existing warnings.
- `build Tests/CCP.Avalonia.Tests -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false`: final 0 errors, 345 existing warnings (includes rebuilt head). One earlier build worker exited with MSB4166; serial retry passed.
- `test Tests/CCP.Core.Tests -c Release --no-build`: 4636 passed, 5 existing Vosk skips, 0 failed. Initial run during compilation failed BoardTilesTests.A_frame_fits_the_budget at 14.39 ms; idle full rerun passed.
- `test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~Official_entrance_definition|FullyQualifiedName~Official_note_lands'`: 7 passed, 0 failed.
- Negative proof: production starting opacity .2 changed temporarily to .3. Definition tests: 2 failed, 0 passed; lifecycle: 4 failed, Off case passed. Failures reported expected .2, actual .3. Source restored, rebuilt, all 7 passed.
- Full affected class (`--filter FullyQualifiedName~OnlineMeterSurfaceTests`): 14 passed, 1 blocked before assertions by Segoe UI Black glyph creation in the existing shell-pill test. That full-shell case is compiled, not run. No test disabled or skipped to hide the font failure.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `python3 .github/scripts/avalonia_docs.py`: avalonia_docs self-check OK.
- `git diff --check`: clean. No desktop visual check was performed.

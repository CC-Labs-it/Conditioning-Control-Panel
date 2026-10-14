# wire-theme-inputs

- Ported the complete TextBox chrome/focus slice from WPF `Resources/Theme/Inputs.xaml`: dark surface, 8px radius, 8×5 padding, one-pixel border, mod-dependent text/caret/selection colors, 60% hover border, 200ms focus glow at 45%, and 50% disabled opacity. It applies to un-themed TextBoxes throughout the shipped UI; `FocusGlowTextBox` now aliases the same theme in the existing Presets/Program Enroll paths.
- Retained Fluent's existing editing, IME, selection, scroll, validation, and context-flyout template instead of copying its control internals. Local/keyed themes keep precedence. The focused glow is a BoxShadow, driven by a short transition on the TextBox Visual; no Effect, perpetual animation, or Transform animation was added.
- `InputFx` handles focus before template application, dynamic mod accents, and relinquishes its shadow when a local theme replaces the implicit one. Tests prove hover/focus, the fade on blur, mod repaint, native text replacement, disabled state, local-theme precedence, and live theme replacement.
- No code was imported from `parity/trunk` (fetched read-only). WPF was the behavioral reference; the installed Avalonia 12.1.2 Fluent TextBox part contract was inspected read-only. No new desktop surface or Core rule was introduced, so panic registration and the WPF head needed no change.
- The ledger row remains **stub** with follow-up `wire-theme-inputs-b`: the remaining input/menu families plus their focused behavior tests exceed this coherent slice.

## Verification limits

- SDK: .NET 10.0.402 (`PATH=/workspace/.dotnet:$PATH`). Headless runs used external system-TTF fallback `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf`; tests set `DiffEngine_Disabled=true`. Test windows/settings are sandboxed by the existing test module initializer.
- `dotnet build CCP.Core`, `dotnet build CCP.Avalonia`, and explicit builds of all eleven Linux test projects in `.github/workflows/build.yml`: succeeded, zero errors. Builds used `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true` to avoid duplicate runtime files on the shared filesystem.
- `bash .github/scripts/core-guards.sh`: passed (424 XAML type references).
- Initial `InputTextBoxThemeTests`: two failures before the theme/focus implementation. The live-theme-replacement regression then failed before cleanup was fixed (alpha 115 instead of 0). Final filter `FullyQualifiedName~InputTextBoxThemeTests|FullyQualifiedName~ProgramEnrollDialogTests`: six passed, zero failures/skips; after adding the actual-pointer hover assertion, all three input-theme cases passed again.
- `CCP_VOSK_MODEL=/workspace/ccp-worker1-vosk/vosk-model-small-en-us-0.15 CCP_REQUIRE_VOSK=1 dotnet test Tests/CCP.Core.Tests -c Release`: 2816 passed, zero failures/skips, duration 1m25s. The initial Debug run passed 2810 with six existing skips (five Vosk model cases and the Debug-only Board frame-budget case). Fetching the pinned external Vosk model with `.github/scripts/fetch-vosk-model.sh /workspace/ccp-worker1-vosk` succeeded; Debug with `CCP_REQUIRE_VOSK=1` then passed 2815 with only the existing Debug frame-budget skip. No test skip or threshold was added or changed.
- `CCP_USERDATA_DIR=/tmp/ccp-worker1-inputs-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker1-inputs-render`: succeeded, 224/224 views. Inspected `AssetSubmitDialog.png`, which shows the new implicit input chrome; `ProgramEnrollDialog.png` and `PresetsTabView.png` also rendered.
- No Windows desktop/IME session was run. The unchanged WPF cross-build was already attempted during this worker's previous brief with both requested Windows-targeting flags and cancelled after 20 minutes without completing; it was not repeated here. WPF head/test compilation is not established. Other Linux test projects were compiled but only the named focused suites and Core suite were executed.

## Follow-up: wire-theme-inputs-b

- Implicit ComboBox/ComboBoxItem chrome, selected-row dot, open glow, and popup geometry.
- Implicit CheckBox's 18px circle, wrapped-content layout, indeterminate state, white checked core, and 140ms check-pop (the existing keyed DotCheckBoxStyle is a separate WPF recipe).
- Implicit ListBoxItem hover/selection chrome.
- ContextMenu/MenuItem roles, layered popup chrome, check/icon/gesture columns, nested-menu placement, and highlight/disabled states.
- Standalone and menu-container separators.

# Profile bubble identity and reactions

- Equipped avatar presets now fill the header bubble through existing `ModArt.AvatarPreset`; shared Discord pictures still win, and unknown or absent preset art falls back to initials.
- Added WPF's tier-badge hover enlargement (72px art, left placement, offsets -4/-46), exit/tier-loss cleanup, menu-open restacking, and the eight-second / 700ms wobble on `BeatLoop`. Motion Off shows the enlarged badge without transition; reduced motion parks the wobble. Adapted the popup and FX from read-only `parity/trunk` commit `56ab3f850e6778bfbb623805ffd571c282e9999d`; reused the base head instead of overwriting it.
- A real level-up requests WPF's smaller header burst (`count: 45`, budget-clamped by the existing canvas) on the existing unscaled `EventFxHost`, with the same 560px surface and focus/visibility/motion gates. Moved the pure bounds/clipping/origin decisions into `CCP.Core/Services/FxBurstAnchorRules.cs`; WPF's existing `FxBurstAnchor` delegates to that copy, and all its original tests remain intact.
- Failed browser opening now copies the public-profile management link and shows WPF's localized prompt through the existing head message dialog. Tests use the assembly's isolated user-data profile, headless clipboard and fake browser opener; no real desktop, browser or user profile is touched.
- Four UI regressions were fail-proven before implementation (4 failures; reduced-motion no-burst already passed). The completed slice passes 9 focused Avalonia tests (6 new, 3 existing), plus 13 new Core geometry cases. Existing `ProfileBubbleTests` now uses bundled fonts in its setup; its assertions are unchanged.

## Follow-up lane: wire-shell-profile-bubble-b

- Wire the real FlashDisplayed / SubliminalDisplayed publications into the bubble, including WPF's 2500ms / 4000ms throttles, 480ms flash wobble and 600ms subliminal shimmer.
- Port the service-backed spiral menu row (summary, block updates and map navigation).
- Match WPF's BackEase Amplitude=0.45 exactly; the current finite 260ms growth uses Avalonia BackEaseOut. The portable menu-open restack recreates the popup host in place of Win32 SetWindowPos; live Windows stacking still needs comparison.
- The ledger row remains `stub`; the complete remaining work crosses the animation publishers and spiral surface, beyond this coherent slice.

## Verification limits

- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ProfileBubbleParityTests|FullyQualifiedName~ProfileBubbleTests'`: **9 passed, 0 failed**. Covers real hover, exit, tier loss, existing account/menu paths, preset fallback, level-up burst, reduced-motion suppression, shared-clock wobble/parking, and the copy-link prompt.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~FxBurstAnchorRulesTests`: **13 passed, 0 failed**.
- Full Core command with `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15 CCP_REQUIRE_VOSK=1` and `--no-build`: **2828 passed, 1 failed, 0 skipped** of 2829. The unrelated `BoardTilesTests.A_frame_fits_the_budget` measured 12.92ms against its unchanged 12ms limit during concurrent renders/builds; all speech tests ran with the pinned model. A focused `--no-build --filter FullyQualifiedName~A_frame_fits_the_budget` retry passed (1/1, 5.167s); the original full-suite failure remains recorded here.
- `dotnet build <project> -c Release --verbosity quiet`: passed for CCP.Core, CCP.Avalonia and all 11 Linux workflow test projects. All builds used the four `CreateHardLinksFor...IfPossible=true` properties to conserve disk; generated outputs for untargeted test projects were cleaned afterward.
- Serialized `flock /workspace/shared/ccp-wpf-build.lock dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same hardlink properties): **512 warnings, 0 errors**, elapsed 4:11.51. WPF application and original WPF tests compiled; neither was run on Linux.
- `bash .github/scripts/core-guards.sh`: passed, including 424 XAML references. `git diff --check`: passed.
- Sandboxed `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/ccp-worker-4-bubble-render`: **224/224 views rendered**. `MainShellWindow.png` shows the header. A temporary external evidence runner captured the real hover/preset and browser-failure paths in `/workspace/ccp-worker-4-bubble-hover.png` and `/workspace/ccp-worker-4-bubble-link-prompt.png`; both were inspected.
- Live desktop input, native popup ordering on Windows, and WPF side-by-side visual comparison were not run. The row is not promoted beyond `stub`.

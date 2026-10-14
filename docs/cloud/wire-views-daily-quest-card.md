# wire-views-daily-quest-card: the daily quest card gets WPF's MotionFx motion

Ledger row `views-daily-quest-card`: stub -> wired.

- `CCP.Avalonia/Views/Controls/DailyQuestCard.axaml.cs`: ports the motion that WPF `DailyQuestCard.xaml.cs` gets from `MotionFx`. This was the last gap the row listed ("MotionFx hover/bar animations still omitted"):
  - **Bar**: progress tweens the fill like `MotionFx.BarFill` (0.6 s, QuadraticEase-out, starting from the current width). A resize of the track still snaps (WPF: "a resize is not progress"). Steps under 0.5 px are skipped.
  - **Hover lift**: like `MotionFx.HoverLift`, the card scales to 1.02 over 150 ms (QuadraticEase-out) and back. `Root` gets a `ScaleTransform` (WPF `EnsureScale`), and `RenderTransformOrigin` stays at 50%.
  - **Hover glow**: a gold glow blooms to blur 18 / opacity 0.5 over 180 ms (linear) and fades over 160 ms. It is detached once fully faded, so a card at rest draws no shadow. WPF uses a `DropShadowEffect` here; this port draws `Root`'s own `BoxShadow` instead, because effects on tiles are not allowed.
  - **Reduced motion**: the gate is `AmbientFxCanvas.Env.AllowTransitions`, the one HoverPop uses. At `MotionLevel.Off` the bar and the lift snap and the glow never shows, as in WPF.
  - **Clock**: each card has one `FrameClock`, frame-locked to the window, with a 16 ms interval. It runs only while a tween is in flight. A test can swap in a stepped clock through `DailyQuestCard.Time` and call `Step()`.
- `Tests/CCP.Avalonia.Tests/DailyQuestCardMotionTests.cs` (new): the `BarTweensAndHoverLiftsAndGlows_SnappedAtOff` theory, for Full and Off.
- `Tests/CCP.Avalonia.Tests/AmbientBeatLoopsTests.cs`: `Views/Controls/DailyQuestCard.axaml.cs` is removed from the shrink-only `CodeBehindEffects` ratchet, because its code-behind no longer sets an Effect. The ratchet would fail if the entry stayed.
- `docs/avalonia-parity.md`: row `views-daily-quest-card` is now **wired**, with the evidence above.
- No trunk imports. `parity/trunk`'s `Helpers/TransformTween.cs` (c18c2d8bb) was reviewed but not used: its wall clock cannot be stepped in tests, and our base already has `FrameClock`.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter DailyQuestCardMotionTests|AmbientBeatLoopsTests|QuestsTab*|TextLineCapTests|IconGlyphRenderTests|HoverPopTests`: 25 passed, 0 failed.
- The first run of `AmbientBeatLoopsTests.NoNewFrameTimerLoop` failed on a per-card `DispatcherTimer`, so the card was moved to `FrameClock`. The new motion test cannot run against the old card: `Time` and `Step` do not exist there, and the old card attached the glow at full strength and had no lift or bar tween.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 2 failed, 6 skipped. Both failures come from this sandbox and are unrelated, since this branch does not touch Core:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_…` fails because running as root defeats the "cannot delete" setup.
  - `LoopbackOAuthTests…(host: "[::1]")` fails because the sandbox has no IPv6 loopback.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build` succeeds with 0 errors for CCP.Core, CCP.Avalonia, CCP.Avalonia.Tests and every Linux test project in build.yml (Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot.Tests).
- Not run:
  - The WPF projects (Windows only). Nothing in WPF changed.
  - The motion has not been watched with a real pointer on a desktop. It was checked only in the headless tests, on a stepped clock.
  - `--render-all` shows only the card at rest, which this change does not alter. Hover and tween states cannot be seen in a still render.

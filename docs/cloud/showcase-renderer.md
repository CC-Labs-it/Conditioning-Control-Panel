# showcase-renderer: the Showcase card's clip art holds when it leaves the visual tree

Trunk unit "P2 Showcase renderer" (trunk commit `f3813bbf5`, used as a hint only).

## What the base already had

The brief expected `CCP.Avalonia/Controls/Billboard/ClipArtView.cs` to be missing. It is not: `d0bc9def7`
(sync6-tonight-board-c) already ported it, and `MainShellWindow.DashboardBillboard.cs` registers it for
the `clip` art key next to `MakeShowcase`. Following "our file wins", this unit merges only the behaviour
that was missing and leaves the rest of the base alone.

- Our `ClipArtView` already matches the WPF spec, `ConditioningControlPanel/Controls/Billboard/ClipArtView.cs`:
  - a 260 ms fade over the poster (`:23`, `:126-139`);
  - `UniformToFill`, centred (`:41`, `:113`);
  - a looping clip (`:150-158` MediaEnded → 0 → Play; here `:input-repeat`);
  - an 854 px poster decode (`:181`);
  - Motion Off keeps the poster (`:58-62`);
  - no clip or no file means no player, on the `#0c0d1a` ground (`:37`, `:53-63`).
- The WPF wiring is `ConditioningControlPanel/Services/Billboard/Showcase/ShowcaseArtRegistration.cs:9`, called from
  `ConditioningControlPanel/Services/Billboard/BillboardWiring.cs:43`. The card host builds the art at
  `ConditioningControlPanel/Controls/Billboard/BillboardCardHost.cs:618`. The provider that makes
  `ShowcaseClipArt` is in shared Core: `CCP.Core/Services/Billboard/Showcase/ShowcaseProvider.cs:92-93`.
- Trunk's `ClipArtView` decodes through `FlashClipPlayer.StartSilent`, and trunk adds that method to
  `FlashClipPlayer`. Neither our tree nor this unit needs it: our tree has no `FlashClipPlayer`, and our
  view already decodes silently through the shared LibVLC (`Mute = true`, `:no-audio`) into `VlcFrameSink`.
  Nothing new is imported for it.
- The view runs no timer and no Avalonia `Animation`. The loop is LibVLC's `:input-repeat`. The fade is a
  single `DoubleTransition` on `Opacity`, from 0 to 1, with no overshoot. So no FrameClock hookup is needed.

## What changed

- `CCP.Avalonia/Controls/Billboard/ClipArtView.cs`: `OnDetachedFromVisualTree` now calls `Hold()`, which
  pauses the decoder. When a clip view leaves the visual tree, by whatever route, it stops decoding. Before
  this, only the card host's own detach or visibility gate paused it. The player is held, not freed: the
  next `Run()` resumes it, and `Release()` (slide retired, or shell closed) still frees it. This goes
  beyond WPF on purpose: WPF's `MediaElement` uses `UnloadedBehavior = Manual`
  (`ClipArtView.cs:109`) and kept playing when unloaded. Also added: an `internal Decoding` test seam.
- `Tests/CCP.Avalonia.Tests/BillboardShowcaseTests.cs`:
  - `ShowcaseCardWithoutAClipStandsOnItsPlainGround`: on Home, a Showcase card with no clip still wears a
    `ClipArtView`, with no poster, no player and the `#0c0d1a` ground, even with motion allowed.
  - `ShowcaseClipDecodesOnHomeAndHoldsWhenDetached`: a real ffmpeg-made clip decodes through LibVLC on
    Home and reports `Played` once. Taking the view off the visual tree stops decoding while keeping the
    player. Closing the shell then frees it. Skips where ffmpeg or libvlc is missing, as
    `MiniPlayerVideoTests` does. CI's Linux job installs libvlc.
  - Shared `ShowHome` and `Until` helpers.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~BillboardShowcaseTests`: 4 passed,
  0 skipped. libvlc 3 and ffmpeg were installed, so the decode test really ran.
- Fails before the fix: with the `Hold()` in `OnDetachedFromVisualTree` removed,
  `ShowcaseClipDecodesOnHomeAndHoldsWhenDetached` fails with "the clip kept decoding off the visual tree".
- Fails without the view: with the `ClipArtView` registration removed from the Showcase wiring, all three
  shell tests fail.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Billboard|PanicSurfacesTests|MiniPlayerVideoTests"`: 14 passed.
- `dotnet build` succeeded with 0 errors for CCP.Core, CCP.Avalonia and every Linux test csproj in
  `build.yml`: Core, Core.Settings, Core.Content, Avalonia, Consent, Language, CatalogueStartup, Scheduler,
  Report, GeneralSettings and Snapshot. `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 1133 passed, 5 skipped, 2 failed. Both failures come from this
  sandbox. This diff changes no Core code or Core test, so they are the same on the base:
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback(host: "[::1]")`: the container has no IPv6 loopback
    ("No data available ([::1])").
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_…`: the test expects a folder that cannot
    be deleted, but the sandbox runs as root, which can delete it.
- The other Linux test projects were built but not run.
- Not run: the WPF head and its tests (Windows only). The Windows LibVLC path in `ClipArtView` is unchanged
  and was not run here. Nothing in this diff is Windows-specific.

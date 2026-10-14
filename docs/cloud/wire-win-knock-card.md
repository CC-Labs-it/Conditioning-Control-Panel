# wire-win-knock-card: knock card avatar picture and button hover lift

Ledger row `win-knock-card`: stub -> wired.

- `CCP.Avalonia/Views/Overlays/KnockCard.cs`: the sender's avatar now matches WPF `LandingChrome.Avatar`
  (Windows/Friends/LandingChrome.cs:35): initials on lilac, with the proxy picture
  (`ProviderSubscription.ProxyBaseUrl` + a `/...` `FromAvatarUrl`, decoded at 2x size) faded over them once
  it loads. Any other URL fetches nothing and keeps the initials. The fetch goes through a
  `KnockCard.FetchPicture` seam so tests never touch the network.
- Same file: Join, Not now and the x carry WPF `MotionFx.HoverLift` (KnockCard.cs `MakeButton` :210). They
  scale to 1.02 over 150 ms quad-out, as transitions on a `ScaleTransform` (no `Animation.RunAsync` on a
  transform), and snap at Motion Off.
- `Tests/CCP.Avalonia.Tests/KnockCardTests.cs`: added `TheSendersProxyPictureCoversTheInitialsAndNothingElseIsFetched`
  and `TheCardsButtonsLiftUnderThePointer`. `Run` now defaults `FetchPicture` to a throwing stub and restores it afterwards.
- `docs/avalonia-parity.md`: only row `win-knock-card` changed (status wired, evidence). Join for Back Room,
  Goon and chess stays with `feat-friends-landing`, because those hosts are not on this head.
- No trunk commits imported: `parity/trunk` had nothing for these two items that our base lacked.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~KnockCardTests`: 7/7 passed.
- Fail-proof: with the hover-lift wiring and the face fetch disabled, both new tests failed
  (Failed 2, Passed 5) and the 5 existing ones still passed. Source was restored afterwards.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed. Both failures come from the container, not this diff (no Core file changed): `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 loopback here) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (the run is root, so the read-only folder can still be deleted).
- `dotnet test Tests/CCP.Avalonia.Tests --filter KnockCardTests|PanicSurfaces|FriendsLanding`: 12/12 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed. Builds succeeded: CCP.Core, CCP.Avalonia, CCP.VR, CCP.LinuxSmoke and every Linux test csproj in build.yml.
- Not run: the live proxy avatar fetch (the tests swap the seam, and the sandbox refuses the network). The
  WPF projects were not built (Windows only). There was no `--render-all` run: the default render shows no
  picture (the render constructor has no avatar URL) and a hover state cannot appear in a static PNG.

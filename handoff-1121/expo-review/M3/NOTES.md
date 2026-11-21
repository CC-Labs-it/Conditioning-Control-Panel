# The chamber

One active clock moves from a white plaster chamber to a spiral well. The native room has a polished marble floor, a brass and enamel sign, six separate bevelled words, framed prints, an open oculus below the inspection deck and one working doorway. Two photographs per phase are in `shots/`. The one-minute film draws six ten-second excerpts from a single 225.619-second visit driven by the live game's clock. The print fades into its raised letters at the first threshold. The floor, sky, floating objects and sound follow the same clock at full depth.

## Reproduce the visit

In a PowerShell at the preview branch root, run the following.

```powershell
Set-Location ConditioningControlPanel/Resources/web/fuzz
node dev/serve.mjs 8937
```

Open `http://127.0.0.1:8937/fuzz/dev/play-ending.html?end=ll_end&pictures=none`, buy the ending, choose YES, walk from the atrium to the convention hall and enter the unmarked west door. The exterior wayfinding sign points away. Stand inside for 210 active seconds for full depth. The same phase takes about 323 active seconds with Gentle or a reduced-motion preference. The threshold permits return at any point. The floor and horizon remain level.

Run the following from a second PowerShell in the game directory to reproduce the twelve views and a real-time film. The local review scripts require Chrome, Playwright, FFmpeg and FFprobe. The default Playwright location on this workstation is `C:/wt-fe/e2e`. Use `PW_DIR` for another installed Playwright package. The photography fixture imports four approved local llama portraits through the game's own-file selection, so the film can safely show examples in the picture slots.

```powershell
$env:FILM = '1'
node dev/expo-dome-review.mjs ./dev-captures/dome
Remove-Item Env:FILM
$env:REDUCED = '1'
node dev/expo-dome-review.mjs ./dev-captures/dome-gentle
node dev/expo-dome-drive.mjs ./dev-captures/dome-controls
```

## Six phases

| Active seconds | Player sees and hears |
| --- | --- |
| 0 | White chamber, printed sign, recorded room tone and the first PA cue. |
| 20 | The six groups of letters move a few millimetres. A faint Loom watermark enters the actual marble. A low note enters the room mix. |
| 45 | The upper drum and dome pick up the Loom sky. Thin-film spheres rise, reflecting and refracting the baked chamber probe. Walking into one or using E on it plays its pop. |
| 90 | Steam collects in moving low pockets and fades against the actual scene depth. The separate solid words leave the sign, move across the chamber and reverse at the curved wall. |
| 150 | Picture frames orbit at different radii. Some show prints of discovered llamas. Other frames and bubbles display the run's permitted media through the same cache and source settings as the museum ending. The wall recedes into a distant optical tunnel. |
| 210 | The floor becomes an optical window onto a second shaft with viewpoint-dependent depth and probe reflections. A slow common beat passes through light and bubbles. The central sign cycles the current run's words. The original bright doorway still leads into the hall. |

The final threshold holds while the player stays. On leaving, depth recedes at a quarter of the normal rate and the exterior plaque records the visit. The visual film uses the recorded game output, including room sound, effects, depth layers and the supplied stand-in takes for phases 0, 3 and 5. The other cues display their approved subtitles.

## Inspected passes

- **Round 1.** The illuminated brass letters nearly disappeared against white paper. Fog reached eye height across the room and obscured distant pictures. A large wooden frame back blocked the inspection camera. The late floor looked painted, and the word paths reversed well before reaching the shell.
- **Round 2.** Changed the solid letters to dark patinated brass, constrained their paths to chords of the actual hemisphere, lowered extinction and introduced moving gaps in the fog. Reduced the hard disc edge on the bubble pictures and moved the frame camera inside the orbit. The floor began reading as glossy and transparent. Hard bands on the drum still followed the room's small physical radius.
- **Round 3.** Projected the far Loom field beyond the drum and added soft light variation across the bands. A virtual underfloor shaft calculates parallax at the actual viewing angle. Increased the floor watermark and full-depth text contrast. Verified the real-time phase progression with the full 210-second stay.
- **Final crown pass.** Bound the chamber and the overhead deck as streaming sightlines. Looking upwards revealed a narrow underside of an exterior wall where it met the plaster. Clipped the hidden join to the real crown radius. The revised native catwalk also clears the shell by at least 5.3 cm, with zero intersections among 24,578 triangles. The oculus now shows the steel deck and lamps. Repeated the room photographs and recorded the actual visit with the repaired join.

## Verification and limits

`verification/interaction.json` records two real ending purchases, one in each motion mode. Each crossed the physical exit in every phase. An actual E key popped a sphere, Escape froze the clock, panic released the scene and the deck was resident overhead. Normal mode admitted 16 bubbles and six frames. Reduced mode admitted five and four, with a measured clock multiplier of 0.65. With the operating system set to normal motion, checking Gentle visuals in the real pause menu also measured 0.65. The level camera had zero roll in all twelve fixed photographs.

The normal and Gentle pictures were inspected in desktop Chrome. A separate run used the physical Intel integrated GPU (0x7D67) at native 1920 by 1080. In the full-depth chamber it delivered 16.7 ms median and 17.2 ms p95. The same GPU in a 393 by 852 touch profile rendered the reduced full-depth chamber at 33.4 ms median and 33.8 ms p95. Both kept pixel ratio 1. The machine-readable results are in `verification/integrated.json`. Physical VR and mid-range phone hardware remain unmeasured.

The regular inspection photographs load four local test pictures. Real online pictures remain subject to the user's museum consent, cache and availability. Occlusion of those DOM pictures is conservative and can temporarily hide a whole picture near an overlapping foreground object. The reflective floor uses an analytic underfloor volume and reflections of the stationary chamber stored in the baked room probe.

A temporary native WPF review host ran the installed WebView2 157.0.4341.60. Its mapped resource host loaded the GLB, the baked lightmap and Three r169. The hosted game then received its ordinary protocol-1 init, completed a local ending purchase, registered a physical First Press, rendered the full-depth chamber and disposed it on panic. The native capture and receipts are in `verification/wpf/`.

The standing sign, PA lines and visitor-time plaque use the bible's text. “Visitor Services” is the added civic department masthead. The full-depth phrase comes from the run's selected theme vocabulary.

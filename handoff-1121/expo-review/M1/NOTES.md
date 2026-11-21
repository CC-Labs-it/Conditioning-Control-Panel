# Expo hall and The First Press

The convention hall is a glass and brushed-alloy pavilion. Paired steel branches rise above the planted side aisles. Honey timber lines the walls and caps the glass balcony rails. The silver press enclosure retains its red ceramic switch and collection label. The east workbench exposes battery drawers, copper bus bars and a visitor-operated grid meter.

The walk starts at the hall threshold, follows the terrazzo aisle to the case, presses the real switch, then returns by the east stair and crosses the upper gallery. The glass case has an open brass-edged hand hole. The live accession placard uses the human's manual press count at purchase. At 50 exhibit presses a second control rises from the brass sleeve.

## Reproduce

Run the following commands in PowerShell, starting in the preview branch's root.

```powershell
Set-Location ConditioningControlPanel/Resources/web/fuzz
node dev/serve.mjs 8937
```

Open `http://127.0.0.1:8937/fuzz/dev/play-ending.html?end=ll_end&pictures=none`. This local launcher stocks the shop. Press the ending purchase, then press YES. The final building opens in the atrium. Walk north into the convention hall. WASD moves, mouse looks and E operates a control in reach. On a touch screen, use the left stick, drag the right side and tap a control.

For fixed hall views and the recorded route, use a second PowerShell in the same game directory. The photography harness requires desktop Chrome, FFmpeg, FFprobe and the installed Playwright module. Its default module location on this machine is `C:/wt-fe/e2e`. Set `PW_DIR` to the absolute path of another existing Playwright package when needed.

```powershell
$env:FILM = '1'
$env:W = '1920'
$env:H = '1080'
node dev/expo-review.mjs ./dev-captures/hall hall
```

## Review history

| Pass | Findings from the fixed views | Change before the next capture |
| --- | --- | --- |
| 1 | The booth was partly hidden by the directory. The two upper views missed the gallery floor. A previous hover label followed the camera. Repeated bright flecks broke up the skirting and case. | Repositioned the cameras, refreshed the reach ray on camera changes, compared the diffuse atlas and probe against separately lit material samples, corrected the diffuse scale. |
| 2 | The full gallery walk and framed information desk read correctly. Contact light remained mottled and the brass aperture was too dark. Walking either stair stalled at its last riser. | Rebaked HDR diffuse transport, denoised the float lightmap before WebP encoding, reduced material AO, calibrated the exposure from each atlas range and corrected capsule step clearance against reachable landings. |
| 3 | The baked gallery and stairs became visibly smoother. The enamel switch still read brown against the marble. The 1080p frame interval was 34.2 ms median and 50.2 ms at the 95th percentile on the test machine. | Compared five moving-prop lighting treatments. Baked static surfaces retain their measured probe contribution. Unbaked moving parts now use the full local probe. Reduced the multisample resolve cost and implemented resolution adaptation. |
| Final | Twelve fixed views, both physical stair ascents and descents, first press and stop action checked. The hand aperture, button and accession label remain readable at interaction distance. | Final frames and route are in this packet. |

`contact-sheet.jpg` indexes the final hall views, including the seven corrected photographs and the First Press projector. The individual pictures and camera coordinates are in `shots/`. The earlier sheets are in `rounds/`. The gallery collision checks, phone-controls exercise, button comparison and film measurements are in `verification/`.

## Verification and limits

- The behavior drive made three manual clicks in the game before the actual ending purchase. It checked the recorded count, exhibit presses 1, 10, 49, 50 and 51, the rising stop switch, Escape and both 3.4 m landings. The separate film fixture started with zero manual clicks.
- The touch drive used real Chrome touch events at 393 by 852, walked 2.93 m on the stick, turned, tapped the switch exactly once, froze on pause and exited on panic. It rendered 105 frames in 3.50 seconds with its 30 fps drawing limit.
- The original 1920 by 1080 hall-only measurement used an RTX 5080 over ANGLE. Its median frame interval was 16.7 ms and its 95th percentile was 16.8 ms. First view took 3.55 seconds on loopback for 6.59 MB of requests.
- A second desktop run selected the physical Intel integrated GPU (0x7D67), verified by the WebGL renderer. At native 1920 by 1080, it rendered the hall at 16.7 ms median and 17.0 ms p95. Cold admission took 4.14 seconds on a controlled 20 Mbps link with 35 ms latency. The 393 by 852 touch simulation on that same GPU used 3.36 MB and reached its first frame in 3.31 seconds. Its median interval was 33.4 ms and p95 was 33.7 ms. Physical phone thermals and cellular reception remain unmeasured. Run `$env:GPU='integrated'` and `node dev/expo-entry-drive.mjs ./dev-captures/entry` for the final building. The M1 verification JSON records the original hall entry.
- The narrated master in `walk.mp4` contains 60 seconds of 1080p video and the live game's audio. The recorded mix includes the First Press, the hall speakers, footsteps and room tone. `verification/film.json` records its level and duration.
- The hall's structural work, balcony, case and signage have been inspected in the player. The leaf grouping by the west stair still looks angular from close range. The last fixed gallery view sees the hall over the top of the booth cornice. The earlier round packets retain their original geometry, lighting and camera defects.

## Signage

The story supplies the exhibit titles, the two switch labels, the warning and the text about the player's press count. Additional printed copy in this hall includes “Convention Hall”, “Founding Collection”, “Applied Sciences”, “Natural History”, “Permanent Collection”, “Visitor Services”, “Information”, “Arrival atrium”, “Mezzanine gallery”, “Directory”, “Exhibits this way”, “Founding Artefact / 2026”, “Accession 01 / The First Press” and “Recorded at accession.” The directional directory uses the public exhibit names and omits the unmarked chamber. The hanging standards use “Llama Expo 2037” and the civic llama crest. The outer chamber plaque follows the visitor-time wording in the bible.

## Pavilion inspection

The three pavilion photography rounds use the same walking-height camera positions. The room-by-room images are in `rounds/1`, `rounds/2` and `shots`. The shared correction log is in `verification/pavilion-review.md`.

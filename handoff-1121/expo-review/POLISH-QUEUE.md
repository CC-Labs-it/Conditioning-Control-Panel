# Museum playtest follow-up

The fourteen stations carry the same ordered archive across ten rooms. The latest edition adds the chessboard, classified advertisements, weather recorder, sectioned drive and tea trolley, room-specific lighting and enlarged reading cards. The review includes complete narration checks for all 96 recordings and desktop and touch photographs of the whole building. [Play and review the museum](visitor-polish/NOTES.md)

The November 21 balcony screenshot shows a visitor beside a timber handrail and a planted trough. The playable preview includes the balcony fixes and four connected galleries. The checks below cover that spot and the route through the exhibition.

| Playtest item | Change | Acceptance check |
| --- | --- | --- |
| Flickering materials and lights | Inspect the planter, rail, leaves, lightmaps and bright trim while the visitor moves. Repair the surfaces causing shimmer. | Move and turn through the recorded balcony view at full and reduced detail, then repeat elsewhere in the building. |
| Walking sound | Compare the existing game's Minecraft-style mockup footsteps with the Expo's current samples. Reuse the intended local recording where appropriate. | Hear the actual walking mix on each floor type and after landing. |
| Planter Easter egg | Landing a jump on a planted basin starts a two-minute stay. Three narrator cues come at 10, 60 and 120 seconds of active play. At release the visitor can walk off or choose Step down. | Test all four hall balcony troughs, ordinary jumps, pausing, leaving the game and returning to the aisle. The owner will supply the recordings. |
| Reading size | Enlarge exhibit labels and screens, and approximately double the size of the chronological projectors and replay controls. | Check the approach from walking height and with a touch viewport. Keep the full approved wording readable. |
| Physical buttons | Give presses a short depression and return along the mounted button's own axis. | Press from the keyboard and touch, including rapid repeat presses. |
| Voice overlap | Give speech a single playing voice. Coordinate PA, projector, exhibit and planter announcements through it. | Listen at a busy junction, replay during another line, and interrupt a line with a planter cue. |
| Guide line | Lead the visitor from admission through the numbered records using an original floor marking. Let it take occasional playful detours before rejoining the route. | Follow it through every doorway, turn and chapter without developer controls. |
| Autoplay timing | Require another uninterrupted second of looking at the picture in range before narration starts. | Glance past, look away, return, then deliberately stop in front of it. |
| Space between displays | Move the forty dated records into four connected gallery sections, with separate entrance and return doors at the atrium. | Verify every record in order, spacing, sightlines, circulation and the ability to walk the entire loop. |
| Themed architecture | Carry the visitor beneath aquarium glazing for the section about reclaimed oceans, bees and the night sky. | Check the connection between the recording, the pictures and the room from both directions. |

The three planter captions are temporary until the owner's recordings arrive.

## Playable checks

| Item | Latest result |
| --- | --- |
| Balcony movement | Matched images from the same camera movement isolated the fern edges. The revised desktop pass reduced their typical brightness change between frames from 1.18 to 0.65 on a 255-point scale. The touch profile went from 1.82 to 1.14. The broad-leaf plants stayed steady across both versions. |
| Planter timing | A visitor jumped onto a real balcony trough, heard the ongoing museum audio pause for the cues, saw all three timed subtitles, and walked off after two minutes. The timer stopped during pause. All four troughs passed the collision check. The three new voice takes are pending. |
| Controls and speech | The pressed caps travel into their mount and spring back. Each distinct key or tap counts. The original game's footfall is mixed with the surface underfoot. During a real interrupted Kevin recording, only one speech stream played and the interrupted audio resumed at its saved position. |
| Gallery loop | The four gallery centerlines measure about 249 m. The final 1,175 m browser walk crossed all fourteen rooms. A separate survey checked the ground at all forty viewing marks and 390 points across the five thresholds. Both atrium doorway signs can be read around the balcony. |
| Projector pictures | The approved library has 96 recordings and 127 distinct pictures. A 15-minute real-time walk played all forty dated recordings, photographed all 120 projected views and checked that each screen returned to its own wide shot. There was one speaking voice throughout. |
| Aquarium and rotunda | The final aquarium floor shows the guide and contact shadows all along the aisle. The rotunda labels stand to the side so both front hooves are visible on every statue. Complete and partial saves produced their respective gallery displays. A complete visitor carried the group photo through the gift shop to the recap and downloaded it in PNG, GIF and video formats. |
| Additional planter | A 13.6 cm timber tread makes the 45.2 cm growing bed in the commons reachable with the ordinary jump. A visitor jumped onto the soil and received the first timed cue. |
| Final performance | All fourteen rooms held a median of about 60 fps on the physical Intel integrated GPU at 1080p. The Gentle touch profile held about 30 fps on the same machine. Chronology showed occasional slower frames, with a 95th percentile of 41.9 ms. From purchase, the first atrium view appeared in 4.03 seconds on desktop and 3.34 seconds in the touch profile on a cold 20 Mbit/s connection. Physical phone performance remains unmeasured. |
| Gamepad | The left stick walks at variable speed, the right stick looks, A / Cross jumps or confirms and X / Square operates the exhibit under the crosshair. Start / Options pauses. A simulated controller went from the preview purchase to the recap in Chrome. The Windows game host also passed walking, turning, jumping, interaction, pause and unplugging. A physical controller test is still open. |

## Round 4 (v4)

The v4 worktree started at dffbc43661. The owner review names thirteen impressions and twelve boxes. Fed and Kept occupy the two halves of one Habitat box. Both halves are required to fill it.

| Slice | Change | Checked |
| --- | --- | --- |
| 0 | One recorded voice, a beat gate, expiring reactions, and cancellation on leaving the exhibit. | Chrome keyboard and Gentle touch, native playback events through both introductory lines. Five fast inputs advanced one step and produced one miss. |
| 1 | The turnstile prints twelve boxes, a Yes balance and a Deviation balance. Records start on Play. | Walked through admission with keyboard, Gentle touch and virtual gamepad in WPF WebView2. Escape resumed, Panic disposed the Expo. |
| 2 | The guide notices a shortcut, return visits, waiting and conduct. Gallery plaques change on a second viewing. | Re-entry changes the plaque header and preserves its historical text. The turnstile and ledger distinguish a backwards crossing. |
| 3 | Ropes rise, the display relocates while unobserved, DO PRESS counts abstention and I AGREE TO STOP seals the accession. | Keyboard, Gentle touch and a virtual controller reached the cap. Both screens and the physical cap read DO PRESS at 25. Ten real seconds raised it to 26 and sealing the original under glass made 27. |
| 4 | Wear hooves, aim a disc over physical caps, submit the picture grid and carry the hooves into the Expo. | A centred disc hit four caps. An edge hit one. An empty picture submission passed. Worn hooves stopped First Press and changed the turnstile. |
| 5 | The shop stocks earned souvenirs, accepts one at the till, folds deviations into a printed agreement and issues either a day ticket or the Renewal door. | Carried a real souvenir, answered all three questions and used both shop loops. The low-stamp sale led back to the atrium. Eight full boxes unlocked Renewal. The physical slip and readable ledger showed the same figures. |
| 6 | One hand carries objects between rooms. Loose rope posts can be set down and reclaimed. Filing Lady Lashes' signed form gives the visitor back their previous item. | Borrowed and returned Greg's stapler, delivered the tea tray and carried a shop souvenir. Filed the form while holding the tray and recovered the same tray. |
| 7 | Bob's notices multiply out of view. Stores supply the hard hat. The break room has a named rota, tea delivery and Greg's heater. | Read all seven notices, took one out, reached the balcony docket, and unplugged and restored the heater. The six-second stop card, newly streamed emergency-lit archive and glowing wool all appeared. |
| 8 | Each of the Twelve has a separate exhibit. The owned statues turn on their plinths when unobserved. Nana's wool leads under the staff door. | Full-herd desktop and Gentle walks checked all twelve. A partial herd wore the hard hat, climbed the scaffold and lifted the sheet. Lady Lashes' gaze moved the visitor to the till holding the signed form. |
| 9 | Hold and release a spit. Its arc, range board, wet marks, horns and llama plates respond in the room. | Keyboard, touch and RT charged the Rematch. A human throw reached 3.05 m. A throw from the planter opened the far plate. A catwalk throw travelled through the oculus and marked the dome sign. |
| 10 | Eight distinct clauses open the way to Nana's numbered cabinets, chair and partly finished scarf. Pull the last rows and watch the stamps return. | Walked the repeating bay, read eight folios, sat, read the seated scarf and pulled the loose end. The missing stamps returned in sequence. |
| 11 | The habitat displays the visitor's run and then their own live view. The staff entrance opens onto the keeper side. | Fed from outside, caught up to the live 16:9 monitor, walked inside, sat and pressed the desk ten times. The register gained ten, a token fell and four ear shadows approached. A real thirty-second wait also registered. |
| 12 | Hang five corrected photographs, attend Reginald's meeting, walk the thirty-post queue and wait for the aquarium ray. | Desktop and Gentle visits filed all five prints, including one face inward. The meeting took forty real seconds. Walking every switchback earned Queued. The ray approached after twenty seconds. |
| 13 | Dome depth follows the visitor's pace. Full depth reveals the translated inscription. Staying can end the visit, and leaving brings a minute of colour back to the hall. | The inscription appeared at 140.84 real seconds. After a short excursion for the return glow, the final gaze reached farewell at 204.51 seconds. |
| 14 | Renewal, Acceptance by Conduct, Stayed, Finger, Model Visitor and The Thirteenth each produce a recap and print. | Tested all six outcomes. Played both two-minute refusals with and without Read. The final group portrait contains twelve llamas and the visitor, and exported to PNG, GIF and MP4. |

### Playback and verification

The complete [script and recording list](ROUND-4-LINES.md) contains 135 new, pre-rendered Original A clips totalling 12 min 16 s. The heater take slows and cuts mid-word. The 96 previous records and three planter captions are intact. The [workshop](workshop-v4/README.md) contains both packed Blender scenes and their build instructions.

The [verification record](round4-verification.json) includes native speech events, gamepad checks in the Windows WebView2 host, both receipt formats, ending routes and frame measurements. Chrome reported physical Intel Graphics 0x7D67. Median frame times in all fourteen rooms were about 16.7 ms at 1920 x 1080 and 33.4 ms with Gentle at 393 x 852. The slowest Gentle room was the dome at 50.1 ms for the 95th percentile.

Both Windows checkpoint runs exercised purchase, entry, walking, turning, jumping, interaction, pause, resume and Panic. The final run included charging the spit and removing the virtual controller without movement drift. Phone controls used an emulated touch viewport. No physical phone or handheld controller was available. Review routes use room travel and camera marks between exhibits. The design's unaided 20-minute and 30 to 40-minute route lengths remain unmeasured.

A fresh Model Visitor ticket earned all six instruction entries through play. The visitor walked each queue bend using W, attended the full meeting, left the First Press alone and kept the glass, dome and heater instructions. The till gave Model Visitor with six Yes, zero Deviations and the Queued and Attended stamps.

A second fresh ticket earned every impression in one visit with all twelve llamas owned. A virtual controller operated the exhibits, including the aimed hoof test and charged spit. After real-time waits in the meeting and dome, the visitor carried the moved-button souvenir to the till, answered the three questions, completed both loops and walked through Renewal into The Thirteenth. The visit reached the normal recap with a full ticket.

The targeted Expo tests passed 61 of 61. The full Fuzz run passed 1,061 of 1,062. The race HUD media test expects a separate racing preview file, `assets/race/threshold-loop.mp4`, absent from the starting branch.

### Owner playtest follow-up

- The Corrections Desk occupies two clear bays across from the signal racks. All five photographs, their hooks and the wall captions were checked from the aisle on desktop and Gentle, including a photograph hung face inward.
- Unplayed projectors keep their screens, captions and reading cards blank until the visitor starts playback. A single press during room narration queues the selection. Desktop and Gentle visits confirmed that completing a screening reveals only that station's record on return.
- The floor disc at Sir Reginald's meeting records attendance. The sign beside it stands on a weighted foot and explains the forty-second wait. On desktop and Gentle, moving restarted the countdown and standing still opened the gate.
- The existing radio tracks Breathe In, Idle Cycle and Sealed rotate softly beneath the Expo. Playback drops about 11 dB during speech, follows the mixer and pauses for the staff blackout. The desktop and Gentle previews played all three original MP3s from the local radio pack.
- Double-tap and hold any arrow or WASD key to sprint. On a controller, hold RB or R1 while moving the left stick. Browser playtests measured 4 metres per second walking and 6 sprinting. Releasing the bumper returned to a walk, and pausing cleared held input.
- The Grand Hall end truss sat in the shared atrium wall at the first-floor spiral stairs. Moving it 25 centimetres into the hall cleared the wall. On desktop and Gentle, the flicker disappeared at all six viewpoints along a 2.6-metre walk across the landing. Both braces remain visible from the hall.

All 71 targeted Expo and documentary music tests passed after these changes. The full Fuzz run passed 1,071 of 1,072, with the same missing racing preview video described above.

### Tea service and exhibit placement

- The tea now uses the lighting in the visitor's current room. With hooves fitted, the left hoof supports the tray and the right hoof can press the test panel. Desktop and Gentle touch visits exercised both pickup orders, carried the tray beyond the unloaded canteen in normal and emergency lighting, and earned Delivered at a separate stand beside the balcony trolley.
- The atrium plaster above the Convention Hall doors stays the same shade as the Hall loads. Before and after images showed identical wall pixels at a fixed viewpoint on both detail levels. Surface checks at five walking positions also hit the atrium plaster.
- The Hoof queue sign faces the aisle beyond the last rope and clears the archive projector. Walking all 25 checkpoints earned Queued on desktop and Gentle touch. The shortcut and the nearby projector still worked.
- The First Press glass descends with the plinth at press 12. The exposed terrazzo has continuous lighting, the register stands beyond the rope, and a single closed cover returns when the visitor seals the exhibit. Desktop and Gentle visits followed the button through all four loan locations and back to the original stand.
- The gift shop staff fire exit occupies its own bay between the Grand Hall doors and the paper press. The opposite wall shows the full framed-contract price. With emergency lighting and the archive display loaded, desktop and Gentle visitors used the door and collected the staff lanyard.

The v4 branch passed all 79 Expo and documentary music tests. The full Fuzz suite passed 1,079 of 1,080. The racing preview video described above is still absent.

## Round 5

### A. The visitor's trail

- The turnstile gives each visitor a Human Activity Trail. The twelve boxes separate public stops from staff discoveries. Both Habitat impressions share one box. The back carries the actual ledger. Open the card with T, the on-screen ticket, or Y on a standard controller.
- Stamps land by name with the action and the current box count. Deviations get a separate slip. Both leave the visitor free to walk. Reopening an old ticket or restoring Nana's stitches keeps the collection without replaying the ceremony.
- Fresh-save snag at the turnstile: walking straight ahead followed the central stairs. A fixed “door on the left” then contradicted the arrow when the visitor was facing away. The arrow now tracks the Chronology doorway, and the text uses its name when it is out of view. Keyboard play also exposed Tab leaving the open card. Tab and Shift+Tab now cycle between its two controls.
- The canteen docket names the Grand Hall upper gallery and Delivered. The arrow measures the wool route through the canteen door, around the atrium display, up the actual east stair and onto the balcony. A lit docket receives the tray. The contract door explains eight clauses, and a single brass edge follows the frame being read.
- Fresh desktop and Gentle visitors walked the complete wool route and read all eight clauses. A separate fresh touch visit earned Unplugged, reconnected Greg, read a clause, picked up the tray and earned Delivered. Keyboard, touch and a standard-mapped controller opened, turned and closed the trail card. The 25 Original A takes, including later exhibit cues, are listed in ROUND-5-LINES.md.

### B. Applied Hoof Sciences

- A large physical button teaches the footprint. Nine different photographs per deal mix three llamas, one alpaca and five other subjects. A hoof toggles tile centres under its contact disc. The board retains marks during narration and explains each miss. Returning the gloves keeps the same deal and gives the visitor one-square finger control. The all-llama mosaic, checkboxes and staff figure are out of the visit.
- The fresh-save run showed the viewing controls and caption could cover the test in landscape. The practice and picture pages now give the screen that space. A tap on the header or outside the tiles gets a click and an aiming hint. The lab door spells out which controls work in hooves.
- Keyboard and Gentle touch visitors marked five squares with a centre press, missed three deals and passed with a finger. Automated contact tests reached all 512 selection patterns with the physical hoof footprint. Visits outside the lab refused eight finger fittings while the visitor could still carry the tea, use a door and pass the turnstile.
- A second fresh visitor heard the queue instruction inside the lab and the fitting instruction after taking off the gloves. Task recordings now retire when the visitor leaves their room or completes the action. A controller visitor solved with the edge of the hoof and received the Deviation. A finger solve records a Yes.

### C. First Press

- The first press stamps the ticket. The second raises the rope. On the third, the visitor watches the original case sink and a four-place loan card rise. The brass light, ticking button, checklist and task line lead to the east mezzanine, the banner, the shop shelf and the dome door. After the fourth loan, the cap turns over. Standing beside it for ten seconds reveals the final switch on the cap itself. The eighth press seals the original exhibit and earns a Yes.
- Fresh-save snag: while carrying the rope post the visitor lost the loan trail. At the east mezzanine the narrator still described the rope, and in the gift shop the souvenir instruction interrupted the hunt. Carrying the post now keeps the location and tally alongside the set-down instruction. The narrator retires old steps and gives the hunt priority on the shop shelf.
- Fresh keyboard and standard-mapped controller visits sealed at eight. Repeated presses on a checked loan left its tally intact. Escape and controller Start paused the moving plinth. Panic released the pointer and ended the visit. After sealing the exhibit, the queue can again give its own task line.

### D. Four exhibit fixes

- The Rematch judge now prints the visitor’s best alongside the 6.1 m llama line. The far plate has a flag. Fresh keyboard and Gentle visitors each threw 3.04 m without hooves. The new directions first played after the third throw.
- A fresh visit to the Habitat revealed that the desk pedestal and a window mullion hid most of the empty chair. The chair now sits 62 cm out from the desk under a warm reading light. A framed vacancy notice sits on the glass beside the feeder. On both profiles the visitor read it, fed one Yes, used the staff entrance, sat on the chair and operated the desk.
- The end of the Queue now says “Now serving: you.” A touch visitor walked all 25 markers and earned Queued at the sign. Keyboard and touch visitors both passed through the Applied Hoof Sciences door and back.
- The casting of Lady Lashes hid the photograph’s eyes when viewed straight on. Both the casting and the portrait now lower their own lids as the visitor stares. After reading the plinth’s warning, a six-second stare on each profile produced Blinked, the signed form and its direction to the gift shop till. The till accepted the form on both runs.

### Round 5 handoff

Each film starts on a fresh save at the turnstile. All three play at 720 by 1280 with game audio and passed a full video and audio decode.

| Checkpoint | Local preview | Phone tour | Build |
| --- | --- | --- | --- |
| A. The visitor's trail | [Play A](http://127.0.0.1:8961/) | [56 seconds, 9.13 MB](round5/phase-a.mp4) | `02a1306858` |
| B. Applied Hoof Sciences | [Play B](http://127.0.0.1:8962/) | [24 seconds, 3.61 MB](round5/phase-b.mp4) | `c372d5fb7c` |
| C. First Press | [Play C](http://127.0.0.1:8963/) | [38 seconds, 4.85 MB](round5/phase-c.mp4) | `1921b7ef8c` |

- [Play the complete round locally](http://127.0.0.1:8944/fuzz/dev/play-ending.html?end=ll_end&pictures=none). Returning to the atrium after stamping Corrections and First Press now points toward a remaining public stamp.
- All 25 prerecorded takes matched the [owner's line list](ROUND-5-LINES.md) and decoded. The sixteen photographs matched their reviewed masters. All three planter recordings are still pending from the owner. Their captions, timing and hold are intact.
- The 94 Expo and documentary tests passed. The full suite passed 1,082 of 1,083. The known race HUD test still lacks `assets/race/threshold-loop.mp4`.
- Compared the starting build and the finished round on Intel Graphics 0x7D67 at the same viewpoints in the atrium, Grand Hall, Hoof Sciences, Habitat and Commons. All five matched on both profiles. Desktop median and 95th percentile frame times were 16.7 and 16.8 ms. Gentle measured 33.4 and 33.5 ms. The hall added three draw calls and the lab shed eighteen on each profile.
- Keyboard, synthetic touch and a standard-mapped virtual gamepad exercised normal visitor actions. The filmed tours followed the physical routes. Focused checks also used review travel to position the visitor at individual exhibits.

### Mobile visitor controls and lo-fi radio

- The phone shows the trail count, current task, walking stick and Actions. An exhibit supplies its own action when in reach. First Press offers Jump at the rope, and the Rematch offers hold-to-spit. The full card keeps Close and View ledger reachable while the stops scroll. Stamps take the task's place briefly, and the hoof picture board clears the surrounding instructions.
- On the welcome screen, visible controls and text occupied about 21% of the portrait view and 20% in landscape, down from 45% and 42%. Touch checks covered both orientations and a 320-pixel-wide phone, including using Actions and turning the ledger immediately after a swipe. [Portrait](round5/mobile-portrait.jpg) · [Landscape](round5/mobile-landscape.jpg)
- The three-track radio has its own play/pause and skip controls beside volume. Skipping while paused selects the next track for the next press of Play. The mixer shows the track title. Touch checks verified that changing the music kept the welcome narration playing, and waking and resuming preserved an explicit music pause.
- All 104 focused tests passed. The full suite passed 1,085 of 1,086 with the previously recorded missing race video.

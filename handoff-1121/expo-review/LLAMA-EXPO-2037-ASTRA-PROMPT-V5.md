Llama Expo 2037, round 5: make a new visitor understand the game. The owner approved this on 2026-11-22.

The owner played your v4 build as a new player and was lost. He was handed a tea set "for the reading balcony" with no idea where that is or why. He unplugged the heater and walked the corridor that never ends, and understood neither. At First Press he asked what he was supposed to do once the exhibit moved. In Hoof Sciences he could not tick the boxes or select a single picture, and thought it was broken.

The cause, from reading your code: the goal (12 stamps, 8 open the exit) is never said. The HUD is a row of dots plus "Yes / Deviations". Room hints are in-fiction, and while carrying something the hint still describes the room. Narrator lines are jokes with no instruction in them. A stamp landing changes one letter in the corner. Two exhibits run on trick rules nobody is told. Nothing is broken; it is unreadable.

THE DESIGN (approved, build it as shown)
Open this page in a browser and play it before you write anything. It is in your own tree, beside this brief:
handoff-1121/expo-review/expo-trail-card.html
It has the same four moments today and proposed, the trail card, a playable hoof captcha, a playable First Press, a table of every stop, and six rules. The wording in its script (the `scenes`, `stops` and `fpSteps` arrays) is the wording to use for cards, task lines and signs. Narrator sentences marked as drafts there are drafts: keep their sense, list your final text for the owner.

WHERE TO WORK
- Your v4 tree, branch codex/tc-expo-2037-v4. Keep going on it.
- Pull first. Two commits from Claude are on your branch, approved by the owner: the planter egg (it now speaks at 10, 45 and 90 s and holds the visitor until 99 s), and this brief with the mockup page. The three planter captions are frozen. Leave their "audio" null: the owner is recording them.
- Commit small, one piece per commit. Push the branch. No PR, no merge to a release branch, no deploy: the T&C round stays held.

SIX RULES, for every stop you touch
1. Say the intended action first. One sign, one verb. A task line from the narrator opens with a plain sentence (what, and where), then the joke. Breaking the instruction is then a choice the player knows they made.
2. Stop the player when something must be seen. The big turn of an exhibit happens in front of them.
3. Teach one thing, one step early.
4. Pull with sight and sound before words: wool on the floor, a lamp, a ticking button, a bell.
5. Every press gets an answer, and marks stay. Never ignore input in silence while a line plays: the press still clicks and still shows.
6. After each phase, start a fresh save at the turnstile and play as someone who has never seen it. Write down the first moment the next step is not obvious, and fix that before the next phase.

PHASE A: the guidance layer
A1. Trail card and task line, replacing the counterfoil box (visit-guide.js and its styles).
- On screen: "HUMAN TRAIL", n of 12, twelve pips with a mark after the eighth, and "8 stamps open the way home". Yes and Deviations leave the HUD.
- Under it, one task line that always answers what and where for the thing in hand, else for the room. Carrying the tea: "Carry the tea to the reading balcony", "Grand Hall, upstairs" and the distance, never the room hint. Corridor: "Read 8 clauses to reach the end", "n of 8". On arrival: "Collect 8 stamps to go home" and the nearest stop.
- The card itself opens on a key (pick a free one and show it on screen), a touch button and a gamepad button. Twelve named boxes with place and a one-line instruction, an ink stamp on each one earned, and a meter line. The three staff boxes (tea, heater, Nana's record) show sealed, "Staff only. Please do not collect.", until the player has found that thing. The back of the card is the ledger with one line explaining it: Yes means you did what a sign asked, Deviation means you did not, the till counts both.
A2. The stamp lands on screen. Each stamp shows a card for about three seconds: its name, "n of 12", one plain sentence about what the player just did, and "staff only" where true. A Deviation shows a small slip, "Deviation noted:" and its ledger label. Never blocks movement. Gentle and reduced motion: fade only.
A3. Signs. Tea: a docket at the pickup that says where (Grand Hall, upper gallery) and "Stamp: Delivered"; while the tea is carried, a lamp blinks on the balcony docket. Corridor: a sign at the door, "Contract corridor. Length: until read.", and how (stop at a frame and look at it, eight clauses); the frame being read lights its edge. Heater: the stamp card covers it.
A4. Plain first sentence. Every line that gives a task gets a new take whose first sentence says what and where, followed by the joke it already has. Start with the welcome at the turnstile (the trail card, twelve stamps, eight to go home, each stop has a sign), the tea, the corridor, and one per stop on the card. New ids in a new script file; the old clips stay as they are.

PHASE B: Hoof Sciences as a real captcha (the owner's direction: llamas mixed with other things, impossible with hooves, intuitive)
- First a single large button the hoof presses without trouble, so the player learns that the circle is the hoof.
- Then one board, 3 by 3, in the look of a captcha everyone knows: "Select all squares with a llama. Then press Verify." Nine different pictures per deal from a pool of about sixteen: llamas, at least one alpaca (which is not a llama), and ordinary things such as a sheep, a camel, a goat, a traffic light, a bicycle. Final-quality pictures, made with your slide pipeline; the llama picture rule applies.
- The circle is the footprint. A press toggles every square whose centre it covers, and marks stay lit. Keep the present ratio of circle to square (a dead-centre press covers 3 to 5). The display says how many squares the hoof marked.
- A wrong Verify says "Please try again", clears the marks and deals new pictures. First miss: hoof_4. Second miss: a new line (the circle is your hoof, it is wider than a square). Third miss: a new line telling the player to take the hooves off, and the display says the same.
- Hooves back on the desk: the same board, and a finger marks one square. Verify stamps Hoofed and plays hoof_8. The staff assistant figure goes; the player is the human.
- Passing with hooves stays possible by hanging the circle off the edge of the board. It books the hoof-edge Deviation and plays v4_hoof_edge.
- The "select nothing" answer, the all-llama mosaic and the tick-box page leave the flow.
- While a line plays, squares still mark. Nothing is ignored.
- Outside the lab. With hooves on, every finger-sized thing refuses: buttons, switches, the till, the plug, the feeding slot, prints, dockets, the stapler. Doors, carrying, the turnstile and spitting stay allowed. Every refusal gets a clop, a slipping hoof and the caption, whether or not a line plays. Five short lines take turns (drafts in the mockup); they may repeat, at most one spoken refusal every twenty seconds. Today the single line is a react that is heard once and dropped when the narrator is busy, which is why the owner never heard it.
- A sign at the lab door states the trade: hooves may leave the lab, fingers stay at the desk, llama doors will open.

PHASE C: First Press in 8 presses
Today it takes 25, and the turn that matters (the button leaving at press 12) happens only while unseen. New arc, every press changes something:
1. Stamp and pa_hall1_press, as now.
2. The rope goes up. v4_press_rope.
3. From inside the rope: hold the player facing the plinth for about two seconds while it sinks in front of them and a loan card rises with four places and four boxes. A ticking starts at the first place. v4_press_moved. Task line: "Find the button", "0 of 4", "follow the ticking".
4 to 7. One press per place, in a fixed order: east mezzanine, behind the hall banner, gift shop shelf, beside the dome door. Each place has a brass lamp and a ticking the player can hear from across the room. After a press the button still leaves only while unseen. The loan card and the task line count n of 4. Lines: v4_press_mezz, v4_press_found, v4_press_price.
After the fourth, the label turns over to DO PRESS (v4_press_invert). Task line: "Do not press", "stand by it for 10 seconds". A press now does nothing and says so. Ten seconds of standing: v4_press_still, and the "I AGREE TO STOP" switch appears on the moved button itself, where the player is standing.
8. That switch seals the exhibit: glass on the original plinth, v4_press_seal, and a Yes in the ledger.
Panic and Escape behave as before during the two-second hold.

PHASE D: small fixes
- The Rematch: the judge board shows the llama line (6.1 m) beside the player's best, and the far plate is labelled. After three spits, one new line: humans stop about there, the llama line is further, the hooves are at Applied Hoof Sciences.
- The Habitat: the empty keeper chair is lit and visible through the glass, with a sign: "Keeper wanted. Enquire at the staff door."
- Lady Lashes: a sign, "Do not look too long." While the player stares, her blink visibly builds over the six seconds. Afterwards the task line reads "Signed form", "hand it in at the gift shop till".
- The Queue: a sign at the end, "Now serving: you."

NOT THIS ROUND
The Meeting with bait, the Corrections Desk read-back, the lost-player help ladder and the map boards are on the page as later phases. Do not start them.

QUALITY BAR
As in round 4. Final quality only: real models, materials and lighting in your v3 and v4 style, no placeholders. Judge by playing: desktop, the Gentle touch profile, keyboard, touch and gamepad. Keep performance where it is. The whole T&C test suite stays green (the race-hud capture test fails in this tree for a missing media file; that one is known).

VOICE AND TEXT
- One voice, "TC Narrator (Original A)", rendered ahead of time with the existing pipeline. Nothing is spoken live. A line with no clip shows its caption and stays silent.
- New lines go in a new script file; the approved lines and the planter captions are not edited. List every new line in handoff-1121/expo-review/ROUND-5-LINES.md for the owner.
- Fixed numbers that belong to the design may be spoken (twelve stamps, eight clauses). Counts that change (presses, metres, seconds, n of 4) stay on screens.
- Tone and copy as before: llamas aloof, superior and likable; plain and short; no em-dashes or en-dashes, no exclamation marks; never the word "ruling". Chrome is gender neutral toward the player.
- Original design. The genre is The Stanley Parable; none of its rooms, lines or set pieces.

HANDOFF
Checkpoint after phases A, B and C: a playable link, a phone-sized tour video (720p, under 25 MB) from a fresh save, and the list of new lines. Then phase D without waiting, unless the owner says stop. Append progress to handoff-1121/expo-review/POLISH-QUEUE.md under "Round 5": what each piece does, what you checked and how, and what each fresh-save run tripped on. Short status lines to the owner, not walls of text.

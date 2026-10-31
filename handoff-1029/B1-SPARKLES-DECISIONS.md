# B1 - Sparkles for every game: decisions

Brainstorm B1 (handoff-1029/B1-SPARKLES-BRAINSTORM.md). Status: FINAL (owner picked option A, 2026-10-29). The build lanes start from this file. Owner page:
https://claude.ai/artifact/TFVEZWhQCGqwZ8GAc5AJgw . Simulator (PRIVATE, never in this repo): `C:/Projects/CCP-Server/proxy/scripts/sim-sparkles-place.mjs`
(untracked in the server checkout; server lane S1 commits it). `node sim-sparkles-place.mjs [players] [days]`.

## Owner answers (survey)

### Round 1 (2026-10-29)

- Q: Sparkles pay by finishing place. Should 1st of 2 (a 1v1 chess or Goon win) pay the same as 1st of 8 in a race?
  A: "Scale by field (Recommended)" - bigger field, bigger top prize. Solo example: 1st of 8 = 20 SP, 1v1 win = 12,
  1v1 draw = 6, loss or last place = 2 (a small finish fee).
- Q: Breakout, Down the Rabbit Hole, Arcademy, Intake and Time Trial have no rivals. What turns a score into a place?
  A: "Medal / star tiers (Recommended)" - gold = 1st, silver = 2nd, bronze = 3rd, finished = finish fee.
- Q: Online pays way more than solo. How much more for the same place?
  A: "x5 (Recommended)" - solo race win 20 SP, online race win 100 SP.
- Q: In a solo race against bots (or chess vs EMI), does placing ahead of bots pay?
  A: "Yes, at solo rate (Recommended)" - bots count in the field like people; solo daily caps still apply.

### Round 2 (2026-10-29)

- Q: Do Basic and Prime players earn Sparkles at the same rate for the same place?
  A: "Same rate for all (Recommended)" - a place pays the same on any tier; Prime's edge is access (online races
  are Prime-only today), not a bigger number.
- Q: Solo results are claimed by the device. How should a long day of play taper off?
  A: "Full, then half, then ceiling (Recommended)" - per game: first 5 games a day pay full, next 10 pay half, then
  finish fee only. Overall solo ceiling ~150 SP a day; online has its own higher ceiling.
- Q: What is the shortest game that still pays?
  A: "Per-game floor (Recommended)" - a full race (existing minimum-lap limits), chess 10+ moves each, a cleared
  Breakout level, one whole Arcademy round. Quitting early pays nothing.
- Q: Two accounts in a private online room, one wins every time. What stops that farming x5?
  A: "Pair limit (Recommended)" - the same two accounts earn the online rate only for their first 3 games together a
  day, then solo rate. New accounts (under 7 days) earn solo rate online.

### Round 3 (2026-10-29)

- Q: Back Room tables already pay from THE BANK with their own tuned prizes (15/30/60/150 SP slices). Do they join
  the place system?
  A: "Stay as they are (Recommended)" - their own economy, already simulated; they keep paying as today and do not
  touch the new daily ceilings.
- Q: Chess tournaments (online arenas): how should the final standing pay?
  A: "Games + podium bonus (Recommended)" - each game pays the online rate as usual; the final top 3 get a one-off
  bonus (e.g. 300 / 150 / 75 SP) outside the daily ceiling.
- Q: Racing Thoughts Time Trial and ghost races: how do they pay?
  A: "Medal tiers, solo rate (Recommended)" - gold / silver / bronze time = 1st / 2nd / 3rd at solo rate; beating a
  friend's ghost counts as a 1v1 win. Solo caps apply.
- Q: How should the payout land on the results card?
  A: "Receipt, then fly to wallet (Recommended)" - a small paper slip under the place (place, online x5, taper) gets
  a PAID stamp, then Sparkles fly into the wallet chip and it bumps. Gentle: the count only, no flight.

### Round 4 (2026-10-29)

- Q: Online has its own, higher daily ceiling. How high? (An online race win at x5 is ~100 SP.)
  A: "~600 SP a day (Recommended)" - about 6 online wins a day at full rate before the ceiling; after it, online pays
  the finish fee only. Pair limit still applies.
- Q: A solo game finished while offline (no server): does it still pay?
  A: "Only if online at the end" - no connection at the results card = no Sparkles for that game.
- Q: When does the daily taper and ceiling reset?
  A: "Server day, with a countdown (Recommended)" - one UTC day for everyone; the receipt says "full rate again in
  5 h". Cannot be gamed by changing the PC clock.
- Q: Balance against the shop (B3): a regular plays ~45 min a day. How many days of play should a mid shop item (one
  outfit piece) cost?
  A: "About 1 week (Recommended)" - something new to buy most weeks; big items (full sets, rare skins) 3-4 weeks.

### Round 5, after the owner page (2026-10-29)

- Q: Which economy ships? (Regular player per week: A 729, B 364, C 1191 SP.)
  A: "A, survey numbers (Recommended)" - solo 1st of 8 = 20, online 100, ceilings 150 / 600. Regular ~110 a day,
  mid shop item ~730 SP.
- Q: In option A a grinder (~3 h a day) earns about 6x a regular (670 vs 110 a day, mostly online). Change that?
  A: "Keep it (Recommended)" - online ceiling 600 and the pair limit already stop runaway days.
- Q: An online room with 2 people and 6 bots: what field does the x5 pay on?
  A: "Whole grid, 2+ humans (Recommended)" - 1st of 8 = 100 SP if at least 2 real accounts raced; bots count like
  people; the pair limit stops two alts farming it.
- Q: Today's shelf is priced for ~30 SP a day (Prize Parlour 15-40, 2D race bundles 1,200-9,000, skill tree ~2,550).
  What happens to those prices?
  A: "Leave them to B3 (Recommended)" - the shop brainstorm reprices the whole shelf; nothing moves in the B1 build.

### Round 6, open calls after the stack review (2026-10-29)

- Q: Caucus without a song has no finish line; a quit pays a medal on the time played (60 s minimum). What should it pay?
  A: "Keep medal on time (Recommended)" - no change.
- Q: The online pair limit (3 a day) counts claim order, and a game that paid 0 still uses one. Change it?
  A: "Keep as is (Recommended)" - no change.
- Q: An arena an admin ends early still pays the 300/150/75 podium (4+ players). Keep that?
  A: "Keep paying (Recommended)" - no change.
- Q: Breakout medals count lives lost; a ball lost in multiball costs nothing. Change it?
  A: "Keep counting lives (Recommended)" - no change.
- Q: Shadow mode fills the daily counters. How do we flip to on?
  A: "Flip at UTC midnight (Recommended)" - no code; the owner-run script or a cron flips at 00:00 UTC.
- Q: A server outage (5xx/404) reads "Offline" on the slip, same as an offline game. Change it?
  A: "Own line (Recommended)" - a separate line like "Bank closed. Try again later." in all 10 languages.
- Q: PR #3104 is 615 lines, 15 over the cap. Split it?
  A: "Keep as one (Recommended)" - no split.

## 1. The place table (option A, final)

One formula for every game. N = field size (everyone who started: people AND bots), p = finishing place.

```
fieldFactor(N) = min(1, 0.6 + 0.4 * log2(N / 2) / 2)      N=2 0.6, N=4 0.8, N=8 1.0, N>8 clamps to 1.0
share(p, N)    = ((N - p) / (N - 1)) ^ 1.5                1st = 1, last = 0
place pay      = round(20 * fieldFactor(N) * share(p, N))
paid           = max(place pay, FEE)                     FEE = 2 (you finished, you get something)
draw (1v1)     = 6 (half a 1v1 win)
online         = x5 on everything, fee included
```

| result | solo | online |
|---|---|---|
| 1st / 2nd / 3rd / 4th of 8 | 20 / 16 / 12 / 9 | 100 / 80 / 60 / 45 |
| 5th / 6th / 7th / 8th of 8 | 6 / 3 / 2 / 2 | 30 / 15 / 10 / 10 |
| 1st / 2nd / 3rd / 4th of 4 | 16 / 9 / 3 / 2 | 80 / 45 / 15 / 10 |
| 1v1 win / draw / loss | 12 / 6 / 2 | 60 / 30 / 10 |
| medal gold / silver / bronze / finished (score games = places of a virtual field of 4) | 16 / 9 / 3 / 2 | - |

The server owns the table (`proxy/sparkles-places.js`); clients never compute a payout, they show the receipt.

### Per game (seams surveyed on origin/main; Glassroad on feat/glassroad-a)

| game | kind | how the place is read | floor (shortest game that pays) | rate |
|---|---|---|---|---|
| Racing Thoughts 3D solo vs bots | race, N = grid | `race-result {place, of}` (`boot.js:1260`) | finished, no anti-cheat flag; pays x laps/3 (1 lap ~3.3 min = a third, 3 laps full) | solo |
| RT3D Time Trial | medal | `meta/medals.js medalFor`, full length only | 3 laps (`fullLength`), finished | solo |
| RT3D ghost race vs a friend's ghost | 1v1 | beat the ghost = win | 3 laps | solo |
| RT3D online room | race, N = whole grid | the room's SIGNED result (`race-ledger.js applyResult`), never the client | room laps, finished, unflagged row | online if 2+ human accounts raced, else solo |
| Chess vs Computer (EMI levels) | 1v1 | `gameover` (`door/door.js:833 onGameOver`) | 10+ moves each (20 plies) | solo |
| Chess online / tournament game | 1v1 | the server's `/v2/pbp` match record | 20 plies; a resign before that pays nothing | online |
| Chess tournament final standing | podium | `tourney.js` finalize | arena finished | 300 / 150 / 75 outside every ceiling |
| Goon duel | 1v1 | both claims AGREE (`core/match.js:2282`); disputed pays nothing | match ended normally (`endReason` not a leave) | online |
| Goon practice | 1v1 | `winnerIsHost` vs the practice pair | match ended normally | solo |
| Arcademy class | medal | `class-ended grade`: S+/S = gold, A = silver, B = bronze, C / pass (zen) = finished | one whole class (`class-ended`, not quit) | solo |
| Breakout (Story and Endless) | medal | per wall cleared (`game.js:945 wallCleared`): 0 balls lost = gold, 1 = silver, 2 = bronze, more = finished | one cleared wall | solo |
| Down the Rabbit Hole | medal | `run-ended score` against the run's own XP ceiling (`250 x minutes x difficulty`): at the cap gold, 60% silver, 30% bronze (W2) | `DtrhRunPayoutRule` (not abandoned, 1 min+) | solo |
| Caucus race (DtRH race) | medal | races the track, not rivals: `score / 5` against its XP ceiling, same lines as DtRH (W2) | finished | solo |
| Intake | medal | score / maxScore: >= TopMarksPercent gold, >= 70% silver, >= 50% bronze, else finished | a completed quiz (`quiz-result`) | solo |
| Back Room tables | - | UNCHANGED, own economy (THE BANK), outside every ceiling | - | - |

Seam state today (origin/main): only Arcademy (`class-ended`), DtRH + Caucus (`run-ended`) and Intake (`quiz-result`)
send a result C# acts on. Glassroad's `race-result` reaches `GlassroadHostService.cs` and is dropped; Goon's
`match-result` (`GoonHostService.cs:769`) is a stub nothing sends; chess and Breakout send nothing to the host.
Existing per-game currencies stay and are NOT Sparkles: Arcademy tickets/tokens, DtRH Sparks, XP.

## 2. Caps and anti-farm (final)

- DAY = the server's UTC day, never the device clock. Every receipt carries `resetInMs` for the slip countdown
  ("Full rate again in 5 h").
- SOLO TAPER, per game per day: games 1-5 full, 6-15 half, 16+ fee only.
- SOLO CEILING: 150 SP of place pay a day across all solo games. ONLINE CEILING: 600 a day. Past a ceiling only fees.
- FEE JARS (my call, keeps fees from being ground forever): solo fees stop at 30 a day, online fees at 60.
- PAIR LIMIT: two accounts earn the online rate together for their first 3 online games of the day; after that the
  game pays solo rate. In a room of several humans a player gets x5 if at least one OTHER human in the room is still
  under 3 games with them today; every pair in the room then counts one more.
- NEW ACCOUNTS: an account under 7 days old earns solo rate online (age from the user record's creation time).
- 2+ HUMANS: an online result with fewer than 2 distinct human accounts pays solo rate.
- OFFLINE: no pay. A claim must reach the server within 10 minutes of the game's end; later or failed claims pay
  nothing and the slip says "Offline, no Sparkles".
- SOLO PLAUSIBILITY: the claim carries `durationMs`; below the game's floor = `short`, pays nothing. Worst case for
  a forged solo client is the ceiling plus the fee jar: 180 SP a day.
- ONLINE TRUTH comes from the server only: Glassroad signed room result, chess pbp match record, Goon agreed claims.
  The client's place is ignored for online claims.
- TIERS: same rate for Basic and Prime. Online races stay Prime (`RACE_ACCESS=prime`); chess online and Goon joining
  stay as they are.
- STAKES are unchanged and stack: a staked game pays its stake outcome AND its place Sparkles.
- `SKILL_POINTS_CAP` 99999 stays; a grinder (~4,600 a week) reaches it in about 5 months. B3 needs big sinks.

## 3. Simulator numbers (option A, 4,000 players x 90 days, seeded)

| profile | day p10 / median / p90 | week | 90 days (median) | SP per hour |
|---|---|---|---|---|
| casual (Basic, ~20 min, 3 days a week) | 5 / 27 / 63 | 95 | 1,213 | 82 |
| regular (Prime, ~45 min, 6 days a week) | 36 / 110 / 223 | 729 | 9,368 | 161 |
| grinder (Prime, ~3 h, daily) | 480 / 670 / 810 | 4,620 | 59,444 | 114 |

Shop pace for B3 (owner: a mid item = about 1 week of a regular): mid item ~730 SP, big item (3-4 weeks) ~2,550.
Today's free wheel (~28.5 SP a day EV) becomes a side income. Grinders hit the solo ceiling every day and the online
ceiling on ~23% of days. Options B (half scale) and C (first game of each kind x2) were shown and not picked.

## 4. Juice: the slip on the results card

- After the place lands: a small paper SLIP slides in under it (lines: place, "Online x5", taper / ceiling / pair
  note), a PAID stamp lands, then Sparkles fly into the wallet chip and the chip bumps. IN and OUT for every piece.
- Zero pay keeps the slip with a plain reason line (short, offline, ceiling) and no stamp.
- Gentle visuals: the amount counts up only, no flight, no stamp shake; reduced motion = a static slip.
- Copy: dry, short, no exclamation marks, no em or en dashes, all TEN language files. Examples:
  "Online x5", "Half rate after 5 games", "Ceiling reached. Full rate again in 5 h", "Offline, no Sparkles".
- Glassroad: `CONTRACT-LOGBOOK.md:120` and `meta/unlocks.js:1` change to say finishing pays Sparkles by place (B1);
  unlocks stay cosmetic only. No feel.json or track change, so the course hash is untouched.

## 5. The wire contract

One endpoint for every game. Server code only in CCP-Server.

`POST /v2/sparkles/claim` (auth: UnifiedId + AuthToken like every v2 call; web: the cclabs-web session)

```json
{ "claimId": "<uuid v4, idempotency key>",
  "game": "rt3d|chess|goon|arcademy|breakout|dtrh|caucus|intake",
  "mode": "solo|trial|ghost|online|practice",
  "result": { "kind": "place", "place": 1, "of": 8 },
  "durationMs": 612000,
  "detail": { "laps": 3, "plies": 44, "grade": "A", "ballsLost": 1, "difficulty": "deep" },
  "proof": { "roomId": "..." },
  "endedAt": 1793283204000 }
```

`result` is one of `{ kind: "place", place, of }`, `{ kind: "duel", outcome: "win|draw|loss" }`,
`{ kind: "medal", medal: "gold|silver|bronze|none" }`. `proof` is `{ roomId }` (Glassroad), `{ matchId }` (chess) or
`{ goonMatchId }` (Goon), online only.

Answer: 200 with a receipt, or 202 `{ "pending": true, "retryAfterMs": 1500 }` while an online result is not in yet.

```json
{ "ok": true, "receipt": {
    "id": "spr_...", "claimId": "...", "kind": "place", "game": "rt3d", "mode": "online", "place": 2, "of": 8,
    "base": 16, "online": 5, "taper": 1, "lapScale": 1, "fee": false,
    "paid": 80, "credited": 80, "reason": null,
    "sp": 4310,
    "day": { "solo": { "paid": 40, "ceil": 150, "fees": 2, "jar": 30 },
             "online": { "paid": 180, "ceil": 600, "fees": 0, "jar": 60 },
             "games": { "rt3d": 3 } },
    "resetInMs": 18000000 } }
```

- `reason`: null | `short` | `offline` | `taper` | `ceiling` | `pair` | `young` | `solo_room` | `disputed` | `flagged`.
- IDEMPOTENT by (uid, claimId) (SET NX `sparkles:claim:<uid>:<claimId>`, 7 d): a replay answers the same receipt
  byte for byte. Online claims are also idempotent by proof id per uid: one roomId / matchId pays once.
- LEDGER: earnings live at `user.backroom.places = { netSp, day, solo, online, fees, games }` so the existing
  `backroomNetSp()` sync backfill and `merge-accounts` carry them like every station; `credited` is the real increase
  after `SKILL_POINTS_CAP`. Locks: purchase_lock -> backroom_lock -> user_write_lock (race-ledger order).
- RECEIPTS for the shop and the client: `sparkles:receipts:<uid>` LIST (keep 200, 90 d),
  `GET /v2/sparkles/receipts?since=<ms>`. B3's shop debits write `kind: "debit"` rows into the same list; earnings
  are `kind: "place"` / `"podium"`.
- `GET /v2/sparkles/day` = today's counters + `resetInMs` (the slip can say "3 full games left" before a claim).
- Pair counters `sparkles:pair:<day>:<uidA>:<uidB>` (sorted uids), TTL 2 d. Health `sparkles_health:<day>` (claims,
  paid, refusals by reason), `GET /admin/sparkles-health`.
- ROLLOUT SWITCH `SPARKLES_PLACES = off | shadow | on`: shadow computes and stores receipts with `credited: 0` (the
  slip shows nothing) for a day of live numbers before `on`.
- CLIENT WALLET: a receipt is adopted like a buy/settlement receipt in `V2WalletAdoption` (its `sp`); new
  `SparklePointSource.Place`. Pages never hold an auth token: the host posts the claim and hands the receipt back to
  the page as a `sparkles-receipt` message.

## 6. Build lanes (each PR <= 600 changed lines, each builds and lands on its own)

| lane | repo / base | PRs |
|---|---|---|
| S server | CCP-Server main | S1 `sparkles-places.js` pure table, taper, ceilings, jars, pair key + `scripts/test-sparkles-places.mjs` + commit the sim. S2 `sparkles-routes.js` claim / day / receipts, ledger under `user.backroom.places`, backfill + merge, health, `SPARKLES_PLACES` (docs/env-vars.md). S3 online proofs: Glassroad room result (2+ humans, pair limit, account age), pbp match end, Goon agreed claims, `pending` answers; tourney podium credit. |
| W WPF | main (W4 after A2 merges Glassroad) | W1 `Services/Sparkles/SparklesClaimService.cs` + receipt model + wallet adoption + `SparklePointSource.Place` + tests. W2 hosts that already get results: Arcademy, DtRH + Caucus, Intake. W3 hosts that need a message: chess (`door.js onGameOver` posts a result), Goon (page sends `match-result`, finish the stub), Breakout (`wall` to the host). W4 Glassroad (`race-result` in `GlassroadHostService.cs`, CONTRACT-LOGBOOK + unlocks.js lines). W5 the slip: one small page module per web tree (trees never fetch from each other; a parity test pins the copies), loc keys in all TEN language files, Gentle + reduced motion. |
| AV Avalonia | port/7.2 (lane A5, after A1 lands) | W1-W4 ported by hand (Core-moved logic redone); the slip modules arrive with the web trees. |
| WEB cclabs-web | main | the host shim claims for glassroad / arcademy / backroom pages with the web session and posts `sparkles-receipt` back; web wallet chip. Same API origin, no CSP change. |
| PH CCP-Mobile | main | the same shim path for the Glassroad and Arcademy web packs; preview OTA only, full fingerprint pinned. |

Order: S1, S2 (shadow) -> W1, W2 -> S3 + W3 / W4 -> W5 -> one shadow day -> `SPARKLES_PLACES=on` with the 7.2.0
pre-release -> WEB, PH, AV.

## 7. Calls I made that the owner did not pick (change freely)

- Solo Racing Thoughts races pay x laps/3 (the 1-lap default race = a third of the table); Time Trial needs 3 laps.
- Fee jars 30 solo / 60 online a day; claims expire 10 minutes after the game ends.
- Grade and medal maps: Arcademy S+/S gold, A silver, B bronze; Breakout per wall by balls lost (0 / 1 / 2);
  Intake top marks / 70% / 50%; DtRH medal lines per difficulty set from real score data by the WPF lane.
- Goon practice pays solo rate; staked games pay both stake and place.

## 8. Landed (2026-10-29)

- Server: CCP-Server #360 merged to main (carries #356-#359; #356 auto-merged, #357-#359 closed). Dark until
  `SPARKLES_PLACES=shadow|on`. Not deployed yet.
- Client: #3097, #3098, #3102, #3103, #3104, #3105, #3107 merged to main (tip 22ad9ca4d4, identical to the tested
  Breakout tip: Release suite 14043/14043).
- Open: Glassroad #3100 (W4) and #3106 (W7) wait for lane A2 (Glassroad into main), bases b1/w4-base and b1/w7-base.
- Rollout left: deploy the proxy, set shadow, check one live Goon and one chess game, flip to on at 00:00 UTC,
  all before the 7.2.0 desktop build ships.

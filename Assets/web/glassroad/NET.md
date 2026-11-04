# Glassroad NET (wave B2: the wire, the room, the lobby)

THE seam between the room (CCP-Server `rooms/`), the lobby (CCP-Server `proxy/race-*.js`) and the client
(`net/`, `hud/lobby*.js`). Frozen in B2-0. Code to it exactly; a gap is reported, never patched on one side.
Implementations: client `net/protocol.js` (pure, three-free, DOM-free), room `rooms/src/contracts.js`
(ESM, WebCrypto), lobby `proxy/race-contracts.js` (CommonJS, node crypto). All three are checked against
the same bytes: `net/vectors.json` here and `rooms/test/vectors.json` there (byte-identical copies).
A wire change bumps `NET_V`, regenerates the vectors and lands in both repos together.

## 1. Shape of a race online

1. Lobby: `POST /v2/race/open | join | quick` returns `{ roomId, ticket, wsUrl, seat }`.
2. The page opens `wsUrl` itself and sends `hello { ticket }` within 5 s.
3. The room (one Durable Object per `roomId`) verifies the ticket with WebCrypto, never calls Supabase or Redis,
   seats the player and answers `welcome`. Lobby phase: `seats` updates, `ready`, `look`, host `start`.
4. `start { countAt, startAt, goTick, seed }`: everyone counts down on server time and hears the song from `countAt`.
5. Racing: each client uploads its own kart (binary, 20 Hz, phones 15 Hz); the room relays one batch per client
   per 50 ms, rolls item boxes, simulates throws on the driveline, rules hit verdicts, checks plausibility and
   keeps standings. The room never runs the kart sim and never corrects a kart.
6. `finish { rows }` to the clients, then a signed `POST /v2/race/result` to the lobby (ledger, board, stakes).

Times are SERVER time (ms since the unix epoch, the room's `Date.now()`), never a client clock.

## 2. Clock and ticks

- `t0`: server ms when the room was created (in `welcome`). Room ms = server ms - `t0` (u32 in batch headers).
- `tick`: 60 Hz ticks since `t0`: `tick = floor((serverMs - t0) * 60 / 1000)`. A snapshot's tick maps back to
  `t0 + tick * 1000 / 60`. Before go the grid still counts ticks.
- `goTick` (integer) and `startAt = t0 + goTick * 1000 / 60` (go, may be fractional ms). Race time of a tick =
  `(tick - goTick) / 60` s. `countAt = startAt - COUNT_MS` (3000) is when the countdown starts AND the song's
  0 s plays (solo starts the song with the countdown too). A late joiner seeks the song to `(now - countAt) / 1000`.
- Sync: the client sends `ping { c: clientMs }`, the room answers `pong { c, s: serverMs }` at once.
  offset = `s - (c + rtt / 2)`; keep the sample with the smallest rtt of the last 8. Pings: one at hello, then
  1 s apart for 5 samples, then every 15 s (the room accepts at most 2 per second). The room keeps each seat's
  median rtt of its last 8 pongs (fewer than 3 samples counts as 999 ms) for the bot keeper rule.

## 3. Binary frames (little-endian)

Every binary frame = an 8 B header + `n` kart records of 40 B. Length must be exactly `8 + 40 n`, `n <= 8`.

| Off | Header field | Type | Note |
|---|---|---|---|
| 0 | type | u8 | 1 = BATCH (room -> client), 2 = UPLOAD (client -> room) |
| 1 | ver | u8 | `NET_V` = 1; other values are dropped |
| 2 | n | u8 | kart records that follow, 0..8 |
| 3 | flags | u8 | 0 (spare) |
| 4 | ms | u32 | BATCH: room ms when sent. UPLOAD: the sender's estimate of room ms |

Kart record (40 B; plan 3.2 grew from 32 B: track `s` instead of seg/segT, plus effect, shield, pulses, pops):

| Off | Field | Type | Encoding |
|---|---|---|---|
| 0 | tick | u32 | sender's tick (section 2) when the state was sampled |
| 4 | seq | u16 | per seat, +1 per record, wraps; newer = `((a - b) & 0xffff)` in 1..32767 |
| 6 | seatFlags | u8 | bits 0-2 seat 0..7, 3 paused, 4 bot, 5 finished, 6 grounded, 7 airborne |
| 7 | lap | u8 | completed laps (`S.lap`) |
| 8 | checkpoint | u8 | last checkpoint crossed (`S.checkpoint`) |
| 9 | status | u8 | bits 0-2 respawnPhase (`RESPAWN`), 3 stalled (`S.stalled > 0`), 4 assist on, 5-7 zero |
| 10 | s | u32 | `round(S.s * 100)`: centimetres of track arc length, 0..lengthM |
| 14 | x | i16 | `round(S.lateral * 100)` cm, + right of travel |
| 16 | h | i16 | `round(S.height * 100)` cm along the road normal (negative while dropping) |
| 18 | quat | 4 x i16 | world quat x, y, z, w: `round(c * 32767)`, signs flipped first so w >= 0; decode / 32767 (not renormalised, so a record re-encodes to the same bytes; readers normalise) |
| 26 | vel | 3 x i16 | world velocity, `round(v * 100)` cm/s |
| 32 | speedPct | u8 | `round(clamp(S.speedPct, 0, 1.5) / 1.5 * 255)` |
| 33 | drive | u8 | bits 0-1 driftDir (0 none, 1 left = -1, 2 right = +1), 2-3 driftStage 0..3, 4-6 boostKind 0..7 (`BOOST` + 6 trick), 7 zero |
| 34 | item | u8 | bits 0-4 held item (0 none, 1..12 = `WIRE_ITEMS` index + 1), 5 rolling, 6-7 zero |
| 35 | aura | u8 | bits 0-3 running screen effect `fxv.effect` (0 none, 1..13 = `EFFECTS` index + 1: the 8 old hit kinds, then pendulum disc tether snap bigSpiral), 4-5 shield (0 none, 1 glass, 2 golden, 3 echo), 6-7 echo copies left 0..3 |
| 36 | pose | u8 | trick / emote animation id (0 none; 1 trick good, 2 trick great; 16+ = emote presets, B2-d) |
| 37 | pulses | u8 | one-shot edges since this seat's previous record (table below) |
| 38 | pops | u16 | cumulative lyric floor-word, bubble and song-gate catches this race, monotone |

Every integer field clamps to its range before writing (u8 0..255, i16 -32768..32767, u16 0..65535, u32 0..2^32-1);
rounding is JS `Math.round`. An all-zero quat decodes to identity.
Look is NOT in the record (it lives in `seats`).

Pulses (bit: edge): 0 hop, 1 land light (tier 0..1), 2 land heavy (tier 2..3), 3 boost-start, 4 airborne from a lip,
5 trick registered, 6 word-pop, 7 bubble-pop. The room never drops a pulse: when it keeps only the newest record of
a seat for a batch, it ORs the pulses of the records it skipped into the one it sends.

Remote animation (Astra's `art/models/emi-kart-event-bindings.json`, 26 clips): the receiver drives a remote kart
from the record exactly as a bot's view: `idle_bob` / `max_speed` from speedPct, `lean_left|right` from driftDir,
`air_glide` from airborne (air time counted locally from the rising edge), `rescue_*` from respawnPhase, `jump_takeoff`
from pulse hop, `ramp_takeoff` from pulse lip, `landing_squash` / `landing_heavy` from the land pulses, `boost_recoil`
from pulse boost, `item_ready` from item (not rolling), `item_throw` / `item_lob` / `hit_recoil` / `spin_out` from the
`fired` and `hit` messages, `cheer` / `sulk` from `finish`. Face moods use the aura effect (strongMoodMap). Bumps
and walls with remote karts are the receiver's own contact events.

Rates: UPLOAD 20 Hz from desktop and web, 15 Hz from phones (one frame carries the sender's own kart plus every bot
seat it keeps). BATCH every 50 ms per client, sent only when n > 0: the newest record of every seat that changed since
that client's last batch, except the seats that client uploads. 8 karts: 328 B per batch, about 6.6 KB/s down.
The room accepts at most 25 upload frames per second per socket (more are dropped and count toward the `rate` flag),
and only records whose seat is the sender's own or one it keeps; uploads before `countAt` are ignored.

Client render rule (B2-c): remote karts 100 ms in the past, dead reckoning up to 250 ms, corrections blended over 150 ms. While racing a record's tick counts the sender's sim steps (re-anchored to the room clock when they part by more than 2 ticks, dropped sim time or a pause); the viewer draws on its frame clock, leaning toward server time. A wall-clock stamp or a Date.now draw time costs up to a tick: 67 cm at 40 m/s.

## 4. JSON messages (text frames)

One object per frame, `{ "t": "<name>", ...fields }`, at most 2048 characters client-to-room and 4096 room-to-client.
The larger welcome bound accommodates eight full garage appearances and seat metadata. Unknown `t`, a missing or mistyped field,
or an extra field the table does not list = malformed (`error { code: 'bad' }`; three in 10 s closes 4002).
`?` = optional. `seat?` on a client message = act for a bot seat this client keeps (default: own seat).
Types: `seat` int 0..7, `u8`/`u16`/`u32` ints, `num` finite number, `unit` 0..1, `item` one of `WIRE_ITEMS`,
`mode` one of `throw lob wide drop self bounce tether`, `code` /^[a-z0-9-]{1,24}$/, `lookId` /^[a-z0-9-]{1,48}$/ or null.

Garage `skinCode` is exactly 37 lowercase characters: six RGB hex colors without `#`, in shell, accent,
antenna, hands, shoes and face order, then one pattern digit 0..3. The original skin omits the field.
No labels or arbitrary strings travel with a skin. The client, lobby and room share the updated cosmetic
catalog; legacy Lab IDs normalize to Builder. Deploy the server companion and use matching updated clients
for skin sharing; older clients with the two-field look decoder need to reload the updated game.

Client -> room:

| t | Fields | Rule |
|---|---|---|
| `hello` | `v: u8, ticket: string, clientMs: num` | first message, within 5 s; anything else first closes 4001 |
| `ping` | `c: num` | at most 2 per s |
| `ready` | `on: bool` | lobby phase |
| `start` | | host only, lobby phase, at least one human |
| `look` | `outfit: lookId, vehicle: lookId, skin?: skinCode` | lobby phase; unknown ids become the default look |
| `pickup` | `box: u16, tick: u32, seat?` | touched item box `track.items[box]`; the room rolls or refuses |
| `fire` | `id: u16, item, mode, tick: u32, s: num, x: num, seat?` | use the held item; `id` = sender's shot id |
| `cp` | `idx: u8, lap: u8, tick: u32, seat?` | checkpoint crossed (idx 0 with lap = laps completed) |
| `finish` | `tick: u32, frac: unit, seat?` | crossed the line on the final lap, `frac` within the tick |
| `drop` | `tick: u32, s: num, seat?` | fell off (rescue starts); progress may go back |
| `emote` | `code` | preset id (`hud/presets.js`), at most 1 per 2 s, never free text |
| `leave` | | leaving on purpose |

Room -> client:

| t | Fields |
|---|---|
| `welcome` | `v, roomId, seat, t0, serverMs, phase, cfg, seed, seats, keep, countAt?, startAt?, goTick?, resume?` |
| `pong` | `c, s` |
| `seats` | `seats` (the whole list, lobby phase and on any join) |
| `seat` | `seat` + any of `ready, idle, left, reason, look, paused` (one seat changed) |
| `start` | `countAt, startAt, goTick, seed` |
| `keep` | `seats` (bot seats this client now simulates; `[]` = stop) |
| `roll` | `seat, box, item` (to all: the box is gone for `items.boxRespawnS`, the seat rolls `items.rollS`) |
| `fired` | `sid, seat, id, item, mode, tick, s, x, target` (`target` seat or null; `sid` = room shot id; `vx` = a Disc's lateral m/s) |
| `hit` | `sid, victim, by, item, verdict, durationS, tick` (`verdict` hit glass golden mercy decoy snapped) |
| `fizzle` | `sid, tick` (a throw, lob or trap ended without a hit) |
| `standings` | `tick, rows: [{ seat, place, lap, progM, pops, finished, timeMs }]` (every 500 ms racing + on a place change) |
| `finish` | `rows: [{ seat, place, name, bot, finished, timeMs, bestLapMs, pops }]` (final, by place) |
| `emote` | `seat, code` |
| `error` | `code, re?` (`code`: bad phase seat item host rate; `re` = the refused `t`) |
| `abort` | `reason`, then the socket closes |

- `phase`: `lobby | countdown | racing | done`. `resume`: hex of the seat's own last record (reconnect mid-race).
- `cfg` (from the ticket, identical for every seat): `{ track, laps: 1..9, seats: 2..8, mode: 'open'|'friends',
  laneKeeper: bool, staked: bool, botFill: bool, autoStartS: u16 }` (a room with a host ignores it: only the host starts).
- Seat: `{ seat, kind: 'human'|'bot'|'empty', name, platform: 'desktop'|'web'|'phone'|null, look: { outfit, vehicle, skin? },
  host, ready, idle, left }`. Names come from tickets (bots: `EMI bot`); no uid ever reaches another client.
- Abort reasons: `hello ticket expired replay version config full started closed server`. Close codes: 4000 race
  over, 4001 hello, 4002 bad, 4003 ticket / expired / replay / version / config, 4004 full / started, 4009 replaced
  (same uid opened a newer socket), 4010 room closed, 4011 server.
- Text message caps: 20 per s per socket; `emote` 1 per 2 s; `ping` 2 per s; over the cap = `error { code: 'rate' }`.

## 5. Items online

The room is authoritative for rolls and verdicts; the victim applies effects locally (Flash uses the hit player's own
media consent and their own Gentle, never the thrower's).
- `pickup`: the room checks the box is alive (its own respawn timer) and the seat's slot is empty, rolls with its
  seeded rand by bucket (place and gap from its standings, `sim/itemTable.js` rules and weights), and sends `roll`
  to all; else `error { code: 'item', re: 'pickup' }` to the sender, whose slot stays empty.
- `fire`: the seat must hold that item and its roll must be over. The room sends `fired` to all, flies throws and lobs
  on the driveline from `s, x` (CONTRACT-B1 item rules, speeds from feel), keeps traps (`drop`, melt), applies `wide`
  at once, and sends `hit` per victim or `fizzle`. Self items (`surge glass echo golden`) only get `fired`; the
  shield shows through the aura byte. Shatter refused near an open edge stays a client rule (`item-refused`, no fire).
- Toys kit: `bounce` (Disc) flies unhomed on the room's `vx`, banks off railed edges (track card `edges`) up to
  `discBounces`, falls off open ones (`fizzle`). `tether` hooks the kart ahead within `tetherRangeM` (`fired.target`; no
  one = `fired` then `fizzle`); both clients drag their own kart (`tetherTowCap`, `tetherDragCap`) until the link's sid
  ends: a `hit` (item tether) when the tower passes, a `fizzle` after `tetherMaxS`, past `tetherBreakM`, a kart gone or a
  Snap. `snap` (self) gives its user `snapGuardS` (throws, lobs and traps that reach it are `snapped`) and sends a
  `snap` hit to each kart within `snapRadiusM`; the victim shoves away from the snapper's lateral. Disc and Tether
  spins, like Shatter, never land near an open edge.
- Verdict order (room): snapped (a throw, lob or trap inside a Snap guard), golden, glass, decoy (throw or lob at an echo kart, room rand), mercy (the room's per-victim
  ledger: `mercyS`, `viewWindowS`, `viewBudgetS`, `mildBlockMul`, `minHitS`), else hit with the trimmed `durationS`.
  Shield state comes from the victim's newest record. Bots get verdicts like people; their keeper applies them.

## 6. Bot keeper

Empty seats become bots at `start` when `cfg.botFill`. The room hands every bot seat to ONE connected client:
eligible = an open socket, not idle; desktop and web before phone; then the lowest median rtt; tie = the lowest seat.
Chosen at `start`; moved only when the keeper leaves, closes, goes idle, or sends no record for a kept seat for 1.5 s
while racing (never for rtt alone). The room sends `keep { seats }` to the new keeper and `keep { seats: [] }` to the
old one if it is still there. The new keeper seeds each bot from the newest record it holds for that seat (it has been
drawing it as a remote kart), continues its `seq` from that record + 1, and runs it with `sim/ai.js`. A keeper uploads
bot records in the same frame as its own and sends `pickup`, `fire`, `cp`, `finish`, `drop` for them with `seat`. With
no eligible client the bots freeze where they are. Bot records carry the bot flag.

## 7. Pause, panic, leaving, reconnect

- Pause pauses only you: the kart coasts under the steer assist (CRUISE + AUTO), records keep flowing with the paused
  flag, the room never waits. The assist flag is allowed while paused even when `laneKeeper` is off.
- Panic closes the window: the socket closes, the room marks the seat `left` (reason `gone`) at once. `leave` = reason
  `leave`. A left seat's kart is removed for others; it is never refilled by a bot mid-race.
- No records for 10 s on an open socket while racing: `seat { idle: true }` (others hold the kart still); records again
  clear it. Never a kick.
- Reconnect: within 30 s of a close, `POST /v2/race/join` for the same room returns the same seat and a fresh ticket;
  `welcome` carries `resume`; the seat's `left` clears. A second socket for the same uid closes the older one (4009).

## 8. Lobby phase, start, finish, timings

| What | Value |
|---|---|
| ticket lifetime | 120 s (room accepts `exp` up to now + 300 s, rejects past `exp`) |
| hello deadline | 5 s after the socket opens |
| start | a room with a host (open tables, friends rooms): only the host's `start`; ready is a signal to the host, never a trigger. The host leaving the lobby hands the role to the longest-seated human (a `seats` list); a hosted lobby nobody starts closes after 10 min (`abort closed`). A room with no host (quick): `cfg.autoStartS` (45) after the first hello, or at once when every seat is a ready human |
| lead | `countAt = now + 3000`, `startAt = countAt + COUNT_MS` (3000), `goTick` integer |
| results | final when every connected human finished, or 15 s after the first human finish, or 600 s after go |
| unfinished rows | `finished: false`, `timeMs: null`, placed by progress |
| room end | closes 60 s after `finish`, or 30 s after the last human leaves (`abort { reason: 'closed' }`) |
| reconnect grace | 30 s; idle mark 10 s; keeper stall 1.5 s |

Standings: progress = `lap * lengthM + d`, `d = wrap(s - startS)`, minus `lengthM` while checkpoint is 0 and
`d > lengthM / 2` (sim/standings.js rule); finished seats rank by finish time ahead of the rest. Finish time
`timeMs = round(((tick + frac) - goTick) * 1000 / 60)`; best lap from the `cp` idx 0 ticks.

The room keeps a TRACK CARD per track (numbers only, generated offline from `tracks/<id>.json` through `loadTrack`;
no client file is copied into the server): `{ id, lengthM, startS, laps, checkpointsS: [], boxes: [items index],
topMps, maxPct, wordCoins, minLapS, skips: [{ fromS, toS }] }`, plus the item numbers it needs from `feel.json` (`items.*`).

Anti-cheat lite (flags in the signed result; no kick in v1): `speed` (|vel| or progress rate over
`topMps * maxPct * wordSpeedMultiplier(card.wordCoins) * 1.1`, with the word multiplier bounded to 1.09 and independent
of cumulative `pops`; `minLapS` uses the same ceiling,
for over 0.5 s outside a registered skip, `sim/routes.js` `registeredSkip`), `order` (checkpoints out of order), `lap`
(a lap under `minLapS`), `finish` (finish before full progress), `rate` (over 25 uploads per s for 2 s, or records for
a seat the socket does not own), `assist` (assist flag in a `laneKeeper: false` room while not paused).

## 9. Ticket

Minted by the lobby, verified by the room. Payload, keys in THIS order (JSON.stringify of an object built in this order):

```json
{ "v": 1, "roomId": "k3m9q2w7x4p8", "uid": "<unified id>", "seat": 0, "name": "Display name", "tier": 2,
  "platform": "desktop", "look": { "outfit": "emi-outfit-cheer", "vehicle": "emi-teacup-kart" }, "role": "host",
  "cfg": { "track": "threshold", "laps": 3, "seats": 8, "mode": "open", "laneKeeper": false, "staked": false,
           "botFill": true, "autoStartS": 45 },
  "exp": 1792700000, "nonce": "<22 chars base64url of 16 random bytes>" }
```

- `roomId` /^[a-z0-9]{12}$/; `uid` 1..64 chars /^[A-Za-z0-9_:.-]+$/; `seat` 0..7; `name` 1..32 chars (the account's
  filtered display name); `tier` 0..3 (account tier: 0 free, 1 Basic, 2 Prime); `platform` desktop web phone; `role`
  host or player; `exp` unix SECONDS; `look` ids from the lists in section 10 (the lobby normalises them).
- Keys: `kTicket = HMAC-SHA256(key = UTF-8 bytes of RACE_ROOM_SECRET, msg = "glassroad/ticket/v1")`,
  `kResult = HMAC-SHA256(key = secret, msg = "glassroad/result/v1")`.
- `ticket = B64U(payload JSON UTF-8) + "." + B64U(HMAC-SHA256(kTicket, ASCII of that first part))`. B64U = base64url,
  no padding. At most 1024 chars.
- Verify: two parts, base64url charset, signature 43 chars and equal (constant time), payload parses and matches
  the shape, `v` 1 (else `version`), `nowS <= exp <= nowS + 300` (else `expired`), `roomId` equals the room (else
  `ticket`). The room accepts a `nonce` once per room (`replay`) and adopts `cfg` from the first ticket; a later ticket
  whose `cfg` differs is refused (`config`).

## 10. Looks

Each seat has `{ outfit, vehicle }` (Astra's wardrobe r4 and vehicle variants, folded into feat/glassroad-a as #2623
and #2624). Only ids travel; render maps them (ART5 lane); an unknown id falls back to the default.
- `outfit`: null (no outfit, the default) or one of `emi-outfit-croupier emi-outfit-pool emi-outfit-varsity
  emi-outfit-labcoat emi-outfit-cheer emi-outfit-swim emi-outfit-drone-protocol emi-outfit-snow-day`
  (`art/models/emi-bot-wardrobe.json`).
- `vehicle`: `emi-teacup-kart` (the default cup and saucer) or `emi-vehicle-petal` or `emi-vehicle-trinket`
  (`art/models/emi-vehicle-variants.json`).
- Bots: `botLook(seat, seed)` = outfit `LOOK_OUTFITS[(seed + seat * 3) mod 8]`, vehicle `LOOK_VEHICLES[(seed + seat) mod 3]`
  (seed as an unsigned 32-bit int). The room writes it into `seats`.
- Prize Counter looks (7.2, CONTRACT.md section 7 "Owned kart looks"): `outfit` may also be `emi-outfit-pit-crew`,
  `emi-outfit-ringmaster` or `emi-outfit-ballerina`; optional `paint` (`hearts|spiral|host-gold`), `umbrella` (`lace`)
  and `trail` (`podium`) sit next to it. They ride the lobby bodies and the seat ticket (the proxy clamps each to the
  seat's owned `cos.*` grants) and come back in `seats` / `seat`; the C2R `look` message keeps `{ outfit, vehicle, skin? }`
  so a room that predates them never refuses it. A client drops any extra id it does not know (`normLook`); bots never
  wear them.

## 11. Signed result (room -> lobby)

`POST <lobby>/v2/race/result`, body = the exact JSON bytes below, header `x-race-sig: B64U(HMAC-SHA256(kResult, body bytes))`.

```json
{ "v": 1, "roomId": "k3m9q2w7x4p8", "track": "threshold", "laps": 3, "mode": "open", "staked": false, "seed": 7,
  "t0": 1792699000000, "startAt": 1792699046000, "endedAt": 1792699230000, "sentAt": 1792699231000,
  "rows": [ { "seat": 0, "uid": "u1", "name": "Ana", "bot": false, "place": 1, "finished": true, "timeMs": 181234,
              "bestLapMs": 59001, "lapsMs": [61200, 59001, 61033], "pops": 212, "left": false, "flags": [] } ] }
```

Rows by place; bots have `uid: null`. `flags` from section 8. The lobby verifies the signature over the raw body,
refuses `|now - sentAt| > 300 s` (`stale`), and is idempotent by `roomId` (a second POST answers `{ ok: true, dup: true }`).
A flagged row gets no stake payout and no board entry. Leaving never costs.

## 12. Lobby routes (CCP-Server `proxy/race-routes.js`; the two auth doors of pbp/goon, `rateLimitIncr`,
`/v2/race/` in `REMOTE_EXEMPT_PREFIXES` with its own caps)

| Route | Body | 200 | Who |
|---|---|---|---|
| POST /v2/race/open | `{ track, mode: 'open'\|'friends', seats: 2..8, laps: 1..9, botFill, laneKeeper, stake?: { kind: 'time'\|'sparkles', amount }, look }` | `{ roomId, ticket, wsUrl, seat: 0, expiresAt }` | open: Prime hosts; friends: anyone signed in |
| GET /v2/race/open | | `{ rows: [{ roomId, track, host: { name }, seats, filled, laps, staked, startsIn, friend }], serverMs }` (20 rows) | signed in |
| POST /v2/race/join | `{ roomId, look }` | `{ roomId, ticket, wsUrl, seat, expiresAt }` | signed in (friends rooms: invited or a friend of the host) |
| POST /v2/race/quick | `{ track?, look }` (idempotent poll) | join shape, or `{ waiting: true, retryMs }` | signed in |
| POST /v2/race/invite | `{ friendUid, roomId }` | `{ ok: true }` (friends destination `race`) | seated in that room |
| POST /v2/race/leave | `{ roomId }` | `{ ok: true }` | signed in |
| POST /v2/race/result | section 11 | `{ ok: true, dup? }` | the room only (signature) |
| GET /v2/race/board/:track | | `{ rows: [{ place, name, timeMs }], me? }` (top 50, no uid) | signed in |

`laneKeeper` is forced false on open tables; stakes need `RACE_STAKES=on` and a Prime account (stakes.js vs the house).
`wsUrl = RACE_ROOM_WS + '/room/' + roomId` (local: `ws://127.0.0.1:8810/room/<roomId>`). Ghost routes wait for B3.
Errors: `{ error }` with `bad` 400, `auth` 401, `tier` 403, `friends` 403, `gone` 404, `full` 409, `started` 409,
`stakes-off` 409, `rate` 429. `startsIn` = seconds to the auto start, or null.

## 13. Race a friend through a link (private preview, 2026-10-23; brief `C:/wt-glassroad-run/BRIEF-LINK.md`)

Only PRIME accounts may claim a link; a link lives 24 h and seats 8 guests. All under `/v2/race/`, both auth doors,
each route with its own `race_rate:` / `race_rate_ip:` bucket. Errors keep `{ error }`; new code `preview` 403.

| Route | Body | 200 | Who |
|---|---|---|---|
| POST /v2/race/link | `{}` | `{ token, url, exp, maxUses, uses }`; idempotent (a live link comes back unchanged) | preview: uid in `RACE_PREVIEW_UIDS`; `RACE_ACCESS=all`: anyone signed in |
| POST /v2/race/link/revoke | `{}` | `{ ok: true }` (drops the link, its host key and `race_guest:<uid>`) | the link's host |
| GET /v2/race/link/:token | | `{ live: true, name, exp, left }` or 404 `{ error: 'gone' }`; `Cache-Control: no-store` | PUBLIC (no auth, IP cap only) |
| POST /v2/race/link/claim | `{ token, look?, platform? }` | the seat body (section 12 join shape) plus `host`, or `{ waiting: true, retryMs: 3000, host }` | signed in, Prime (the host claiming their own link is exempt) |
| GET /v2/race/access | | `{ ok, host, tier }`: ok = the preview admits the caller, host = may mint, tier = effective tier | signed in, never refused for preview |

- Token: 16 random bytes base64url (22 chars); shape `/^[A-Za-z0-9_-]{16,64}$/`, anything else is `gone` (the client
  never sends one: `net/lobbyApi.js` `LINK_TOKEN`). `url = (RACE_LINK_BASE || 'https://app.cclabs.app') + '/r/' + token`.
- Store: `race_link:<token>` = `{ host, hostName, exp, max: 8, guests: [uid] }` EX 24 h, `race_link_host:<uid>` = token
  EX 24 h. `name` = hostName, `left` = max - guests.
- Claim refusals: `gone` (unknown, expired, revoked, or a block either way), `tier` (not Prime), `full` (8 guests and the
  caller is not one). Once per uid: a repeat claim never takes another slot. A claim adds the caller to `guests`,
  `race_guest:<host>` (EX = the link's life left) and `race_preview:<uid>` = 1 EX 7 days. Seated only when the host
  sits in a friends room that has not started; else waiting, and the client polls claim on `retryMs`.
- seatIn also admits a friends room caller who is in `race_guest:<room.host>`.
- PREVIEW GATE: unless `RACE_ACCESS=all`, every authed route except link/claim, access and the public lookup answers
  403 `preview` to a caller in neither `RACE_PREVIEW_UIDS` nor `race_preview:<uid>`. `/v2/race/result` and the public
  ghost GET are untouched; with `RACE_ACCESS=all` everything is exactly section 12.

Client (`hud/lobby.js`, pure parts in `hud/lobbyMath.js`): a friends room host whose `access().host` is true gets
Invite link (makeLink, copy the url, else a selectable line, then "Link copied. 8 seats, 24 h.") and Revoke. The Race
online door answers a `preview` refusal with "Online racing is in a closed preview.". `?r=<token>` (boot.js): the
lobby card claims it; seated goes straight into the room, `waiting` shows "Waiting for <host> to open a room." and
polls until seated or Cancel, `tier` "Racing a friend needs Prime.", `gone` "This link has expired.", `full` "That
race is full.". After the first claim answer `r` leaves the address bar (history.replaceState).

## 14. Race songs from our own storage (2026-10-23; brief `C:/wt-glassroad-run/BRIEF-PUBLIC.md` lane SONGS)

Songs live in a Cloudflare R2 bucket (`glassroad-songs`, binding `SONGS`) on the room worker; the lobby hands any
signed-in player short-lived signed urls, so desktop, phone and web all get the same song.

| Route | 200 | Who |
|---|---|---|
| GET /v2/race/songs (lobby) | `{ songs: [{ id, title, chartUrl, exp }] }` | signed in, both doors, preview gate applies |
| GET\|HEAD /song/<id>/<file>?exp=<ms>&sig=<b64url> (room) | the R2 object `<id>/<file>` (file = `chart.json` or the mp3 the chart names) | anyone holding a live signed url |

- `chartUrl = <RACE_ROOM_HTTP>/song/<id>/chart.json?exp=<ms>&sig=<b64url>`. sig = HMAC-SHA256 over
  `song:<id>:<file>:<exp>` with kSong = HMAC-SHA256(RACE_ROOM_SECRET, `glassroad-song-v1`); exp at most 6 h ahead (the
  lobby puts it on a 10 minute grid, so a repeat ask gives the same url and the browser cache keeps the mp3).
  `RACE_ROOM_HTTP` = env, else `RACE_ROOM_WS` with wss -> https. The list is env `RACE_SONGS` (`id:Title,id:Title`),
  default `pink-road:Pink Road`. Song id `/^[a-z0-9][a-z0-9-]{0,47}$/`.
- Room: verifies exp (ahead, at most 6 h + 5 min) and sig (RACE_ROOM_SECRET, or RACE_ROOM_SECRET_PREV during a
  rotation); 400 bad, 403 expired or forged, 404 missing, 503 unconfigured. chart.json goes out with its `audio`
  field REWRITTEN to the absolute signed mp3 url on the room origin (same exp; a relative url would lose the query).
  The mp3 honours a single `Range` (206 + Content-Range, 416 past the end). CORS `*` (GET, HEAD, OPTIONS),
  `Cache-Control: private, max-age=3600`.
- Owner-run upload: `rooms/scripts/upload-song.sh <id> <dir>` puts the mp3 the chart names, then `chart.json`.
- Client: when nothing named a chart (no `?chart`, not the dev host, no valid host `caps.songs`) and the lobby link
  exists, boot asks `lobbyApi.songs()` once; `host/bridge.js` `lobbySongCharts` keeps only chartUrls on the room
  origin (`ROOM_ORIGIN`, https; a local lobby passes `&room=http://127.0.0.1:<port>`), path `/song/<id>/chart.json`,
  exp ahead, a 43 char sig, at most 8. A refusal (preview, dark, offline) leaves no song. Solo and online song timing
  are unchanged (the same CHARTS list feeds both).

# Friends: the wire (v1, 2026-09-23)

The desktop's friend list. No free text anywhere: pokes, invites and watches are preset ids the
server validates against a fixed grammar. Names and pictures are server-resolved. Presence is
opt-in and defaults off. This file is the authority for both halves: `proxy/friends.js` +
`proxy/friends-routes.js` in CC-Labs-llc/CCP-Server and `Services/Friends/*` here.

## Door

Every op is `POST /v2/friends/<op>` with JSON `{ unified_id, ... }` and the account token in
`X-Auth-Token` (the same pair `BackRoomApi.AppIdentity()` stamps; server side copy
`backroom-routes.js gate()`: `validateAuthToken`, `rateLimitIncr`).
Replies are `{ ok: true, ... }` or `{ ok: false, reason }`. HTTP 200 for every worded reply,
401 for a bad token, 429 `too_fast` past 60 calls a minute per account, 503 with no Redis.

Web door (Friends Everywhere S2, 2026-10-20, additive; `proxy/friends-web-door.js`). With NO
`X-Auth-Token`, an `Authorization: Bearer <Supabase JWT>` opens every friends op,
`GET /v2/friends/avatar/:id` and every `/v2/leash/<op>`. The account is the ONE unified id the JWT
resolves to (the helper `GET /v2/user/me` uses); it replaces any `unified_id` the body carried.
401 `{ ok:false, reason:"no_record" }` when the JWT maps to no record, 409 `{ error:"merge_required",
unified_ids }` when it maps to several, and the helper's own 401 `invalid_token` / `token_expired` /
`missing_authorization` and 503 `auth_provider_unreachable` pass through (those bodies say `error`,
not `reason`). With an `X-Auth-Token` present the desktop door decides and any Bearer is ignored.
Rate limits stay per uid: web and desktop calls share one meter. This client never uses the web
door; it always sends `unified_id` + `X-Auth-Token`.

Ids on the wire are unified ids (`u_...`). The client never shows one.

## Ops

| op | body | reply |
|---|---|---|
| `state` | | `{ ok, code, me:{activity,lock_day,shared}, friends:[F], incoming:[R], outgoing:[R], blocked:[B] }` |
| `poll` | `{ platform, activity?, lock_day?, shared, accepts?, receipts?:[RQ], leash_report? }` | `{ ok, online:[id], inbox:[I], receipts:[RC], leash? }` (`leash` only on a desktop poll, see Presence) |
| `request` | `{ code }` | `{ ok, status }` status: `sent` `accepted` `already` `not_found` `blocked` `self` `full` |
| `accept` | `{ id }` | `{ ok }` |
| `decline` | `{ id }` | `{ ok }` |
| `cancel` | `{ id }` | `{ ok }` (an outgoing request) |
| `remove` | `{ id }` | `{ ok }` |
| `block` | `{ id }` | `{ ok }` (removes the friendship and any requests both ways) |
| `unblock` | `{ id }` | `{ ok }` |
| `squelch` | `{ id, on }` | `{ ok }` |
| `send` | `{ to, kind, poke?, destination?, code?, watch? }` | `{ ok, status, item_id? }` status: `sent` `squelched` `too_fast` `not_friends` `offline` `refused`; `item_id` (16 hex) only with `sent` |
| `report` | `{ id, reason }` | `{ ok }` |
| `link` | `{}` | `{ ok, url, token, expires_at, uses_left }`: the live link, minted when there is none (Links) |
| `link_revoke` | `{}` | `{ ok }`: deletes the live link; the next `link` mints a new one |
| `claim` | `{ token }` | `{ ok, status }` status: `friends` `already` `self` `full` `unusable` |

`F` = `{ id, name, avatar, tier, online, platforms, activity, lock_day, last_seen, squelched }`
`R` = `{ id, name, avatar, via, at }`
`I` = `{ id, kind, from, from_name, from_avatar, poke, destination, code, watch, at, expires_at }`
`B` = `{ id, name }`: an account this one blocked, newest 200 (FRIENDS-RECEIPTS v1). A reply
without the key is an older server; the client then falls back to what this PC remembers.
`RQ` and `RC`: see Receipts below.

- `avatar` is a first-party path on the proxy (`/v2/friends/avatar/<id>`) or null. It is non-null
  only when that account's Arcademy presence rung is `discord` (the existing consent ladder in
  `presence.js`; a friend link does not widen it). The route reuses the presence avatar resolver.
- `name` is `user.display_name`, never anything the client sent.
- `tier` 0 / 1 / 2 from the record (`patreon_tier` through the usual `effectiveTier` helper).
- `online` = any of the friend's presence keys exists (see Presence). `activity` is one of the
  grammar below or `"online"` when the friend shares nothing beyond being here. `lock_day` int or
  null. Merged per field across devices: `activity` = the newest specific one (anything but
  `panel` / `online`), else the newest `panel`, else `"online"`; `lock_day` = the first non-null in
  desktop, phone, web order; `last_seen` = the newest `at`. A chatty web tab or phone never hides
  what the desktop is doing.
- `platforms` = the devices the friend is here on, a subset of `["desktop","phone","web"]` in that
  order, `[]` when offline. A new server always sends it; this client reads it absent as `[]`.
- Timestamps are ISO strings from the server clock.

## Links (Friends Everywhere S1, 2026-10-20)

An expiring friend link beside the typed code (the code stays and is never a URL). Additive: an
old client never calls these ops; this client reads a missing or HTML reply as "no link" and Copy
link then copies the code and says so.

- Token: 12 chars from the code alphabet, case-insensitive on input, stored upper.
  `url` = `https://app.cclabs.app/f/<token>`; one live link per account. The client builds the url
  from the token (it ignores `url`) and accepts a pasted `app.cclabs.app/f/` link (any case,
  scheme optional, trailing `/`, `?` or `#` dropped; any other host is not claimed) or a bare token
  in the Add friend box, which then `claim`s instead of `request`ing. `claim` takes the bare token.
- Life: 7 days (`expires_at`, ISO) and 25 uses (`uses_left`, int). The 25th claim retires the link
  (the owner's pointer goes only while it still names that token); at expiry it is unusable too.
  Inside a link's last 24 h, `link` mints a fresh one (7 days, 25 uses) and the old token stops at
  once, like a revoke; `claim` never re-mints. The client asks `link` on every Copy link press (a
  link renewed on another device or spent at 25 claims is replaced at once); a reply with no uses
  left or under 1 hour to `expires_at` is retired and minted again once (`link_revoke` then `link`,
  `FriendsLinkRules.WorthHandingOut`), so a cached copy on the phone or web can go dead. The
  per-account cache answers only when that call fails, and never within 5 minutes of the end. The
  copied note reads the days or hours and the uses left from the link itself.
- `claim` = the same checks as `accept` (not self, no block either way, both under 100 friends, not
  already friends). On `friends` the pair is mutual at once, any pending request between them goes,
  the use count goes up by one, and the link OWNER gets the existing receipt `{ id: null, kind:
  "request", to: <claimer>, to_name, state: "accepted", at }` (the accepted notice fires
  unchanged; its feed key carries `at`, so a second claim by the same account after a remove
  notifies again). Blocked, expired, revoked, used-up and unknown tokens, and a link whose owner
  was deleted or merged, all answer `unusable`, never "blocked". This client also reads a claim
  reason `bad_input` / `not_found` / `refused` / `blocked` as `unusable`; `full` reads "One of you
  already has 100 friends."
- Rates: `link`, `link_revoke` and `claim` count toward the 60/min door; `claim` also has 10 a
  minute per account (HTTP 429 `{ ok:false, reason:"too_fast" }` past it; 503 `{ error:
  "service_unavailable", retry_after }` when the limiter is down). `{ ok:false, reason:"busy" }`
  when two calls race the same lock and the server's one retry 50 ms later also loses: this client
  reads it as try again, never as a status.
- Server keys: `friend_link:<token>` -> `{ uid, uses, max, exp }` EX 7 d; `friend_link_of:<uid>` ->
  token, same EX; `friend_link_mint:<uid>` / `friend_link_lock:<token>` EX 5 locks;
  `friend_claim_rate:<uid>` EX 60. Nothing on the `user:` record.
- Public lookup (web only, never called by this client): `GET /v2/public/friend-link/:token` (no
  auth) -> `{ ok, state: "live" | "unusable", name, avatar }`. `name` = the owner's
  `display_name`; `avatar` = the PUBLIC presence avatar path `/v2/arcademy/presence/avatar/<opaque
  id>` only while the owner's presence rung is `discord`, else null; both null when `unusable` (a
  malformed token is `unusable` too, HTTP 200). Never returns the owner's uid. Metered like
  `GET /v2/public/taste/:code`: per visitor IP through `x-relay-key`, 60 a minute (`pub_flink`,
  429 `{ error:"rate_limited" }`), `Cache-Control: no-store`, `X-Robots-Tag: noindex, nofollow`.
- Invite destinations gain `race` and `breakout` (no `code`, sent from the web). This client
  (D2) shows them on the knock card and Join opens `https://app.cclabs.app/race` in the browser
  (race) or the Breakout station, full when the account has it, else the demo (breakout). It
  does not send them (`InviteDestination.All`, not `Sendable`). A `code` on them must still fit
  the join-code grammar or the item is dropped, so the web sends none. An older desktop drops such
  an item in `ParseItem`; it never throws.

## Grammar (refuse anything else with `refused`)

- `kind`: `poke` `invite` `watch`
- `poke`: `hi wp tut gl oops thanks drop peek deeper still more sleepy`
- `destination`: `goon backroom ramp chess race breakout` (`race` and `breakout` added 2026-10-20,
  no `code`); `remote` was dropped 2026-09-28: the server answers `refused` to a remote invite and
  the client neither offers nor shows one
- `code` (only with `goon`): `^[A-Z0-9-]{4,12}$`, system-generated join codes
- `code` with `chess`: a Piece by Piece challenge id `^c_[0-9a-f]{16}$` minted by `POST /v2/pbp/challenge` (target = the friend's unified id, allowed only between friends both ways with no block). `send` answers `refused` unless that challenge is the sender's own, still pending, and pointed at `to`. The receiver accepts it with `/v2/pbp/challenge/:id/accept`.
- `watch`: `{ kind, id, title? }`, `kind` in `catalogue` (`id` `^[A-Za-z0-9_-]{1,64}$`), `flavour`
  (`id` in `trance pink frills shiny censored`), `ht` (`id` `^[0-9]{1,8}$`); `title` <= 40 chars,
  letters, digits, spaces and `.,'-` only, stored but never trusted (the receiver resolves the id)
- `activity`: `panel session backroom race goon goon_hosting arcademy breakout deeper remote chess`
  (`chess` added 2026-09-28; a server that predates it stores `online`)
- `reason`: `spam harassment underage impersonation other`
- friend code: `CCP-` + 5 chars from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`, case-insensitive on input

## Limits (server-enforced, the client mirrors the ones it can)

- 100 friends, 50 incoming requests, 20 requests sent a day, 50 inbox items (oldest dropped)
- one poke per pair per 10 s -> `too_fast`; 30 sends a minute per account -> `too_fast`
- an invite expires 5 minutes after `at` (90 s before FRIENDS-RECEIPTS v1); a poke or watch 24 h
  after; expired items are never delivered. An invite's `joined` / `declined` is still taken for
  30 s past `expires_at`; `expired` only fires after that
- a squelched sender's pokes answer `sent` to the sender and are dropped (they cannot tell)
- a blocked pair: `request` answers `blocked` for the blocker, `not_found` for the blocked;
  `send` answers `not_friends` both ways; inbox items from a newly blocked account are purged

## Presence

Per platform (Friends Everywhere S3, 2026-10-20). Every `poll` body carries `platform`: this
client sends `"desktop"` on every poll, the `shared:false` one included; the phone sends
`"phone"`, the web `"web"`; absent or anything else = `"desktop"`, so old clients keep working.
The poll writes `friend_presence:<uid>:<platform>` = `{ activity, lock_day, at }` with EX 180
when `shared` is true; `shared:false` deletes ONLY the caller's platform key (a desktop also
deletes the old single key), and the last seen goes once no device is left sharing; no `shared`
changes nothing. Online = any platform key exists. Migration: the old `friend_presence:<uid>` is
read as `desktop` until it runs out (180 s); nothing writes it any more. `poll` replies
`online:[id]` as before. `state.me.shared` is true when ANY device of the account shares. Leash
`H.online` is the same merged online, so a holder's view does not flicker when one device of the
held account goes dark. This client reads a missing `platforms` as `[]` (and reads it only when
`online` is true), drops unknown ids, forces the order desktop / phone / web and draws one mark
per platform after the name (tooltip "On their phone").

The leash rides only the desktop poll. A `poll` whose `platform` is `"phone"` or `"web"` never
reads, reports or drains leash state: its reply has no `leash` block, `leash_report` in its body
is ignored, and every leash key (the events list included) waits for the next desktop poll. An
absent `platform` is desktop, so old desktops are unchanged. A phone or web client must never read
a missing `leash` as "the leash ended".

`accepts?: [destination]` names the invite doors the polling device can open. An invite to any
other door stays in the inbox for the account's other devices; pokes, watches and requests go to
whoever polls. Absent = per platform: desktop (and an old client) takes everything, `web` takes
`["chess","race","breakout"]` (a Goon, Back Room or ramp invite waits for the desktop), the phone
sends its own list. An array always wins, so `[]` = no invites at all. This client sends no
`accepts`.

The client polls every 20 s while the drawer is open or any friend is online and the app is in
the foreground, else every 120 s; never when signed out. `state` is the full list; the drawer
calls it on open and every fifth poll, and an accepted request receipt fetches it in the same
tick so the new row shows with its notice.

## Receipts (FRIENDS-RECEIPTS v1, 2026-09-28)

Mirror of `proxy/FRIENDS-RECEIPTS.md` in CCP-Server (#234, leash half #235), friends part. The
leash kinds ride the same channel; their half lives in `Services/Leash/CONTRACT.md`. Additive: an
old desktop never reports a receipt and ignores the reply's `receipts`; an old server ignores
the body's `receipts` and sends none back.

### Ids and states

Every sent item has a 16-hex id: the inbox item's `id`, and the `send` reply's `item_id`.
A squelched send answers `sent` with an `item_id` too, so a squelch cannot be told apart.

| state | set by | kinds |
|---|---|---|
| `arrived` | server, when the recipient's poll drains the item | all |
| `seen` | recipient reports (on screen) | all |
| `joined` | recipient reports | `invite` |
| `declined` | recipient reports (invite) / server on the `decline` op (request) | `invite`, `request` |
| `expired` | server, lazily, once the invite's TTL + 30 s grace passed with no joined/declined | `invite` |
| `accepted` | server, on `accept` or a mutual `request` | `request` |

Order `sent < arrived < seen < joined|declined|expired|accepted`; forward only; the last four are
terminal and rank the same (`ReceiptState.Rank`). A recipient may skip ahead (straight to `joined`).

### Recipient reports (poll body)

```
receipts: [ RQ ]          optional; absent = report nothing
RQ = { id: "<16 hex>", state: "seen" | "joined" | "declined" }      an item (friends or leash)
   | { request_from: "<uid>", state: "seen" }                        a friend request waiting for me
```

- At most 50 a poll are read (`FriendReceipts.MaxReportsPerPoll`), de-duplicated by id / request_from.
- Accepted only from the addressee, only forward, only states the kind can reach, never on a
  squelched item, never while either side blocks the other. Anything else is dropped silently:
  a receipt never fails a poll. A `request_from` seen counts once per request, while it waits.

### Sender receipts (poll reply)

```
receipts: [ RC ]          always present on a new server, may be []
RC = { id, kind, to, to_name, state, at, ref? }
  id       the item id; null for kind "request"
  kind     poke | invite | watch | request | leash_offer | leash_punish | leash_assign | leash_reward | leash_tug
  to       the recipient's unified id
  to_name  the recipient's display_name from its record (never client-sent), or null
  state    see the table
  at       server ISO time of the move
  ref      leash only (the pid or aid)
```

Oldest first, each delivered once (drained). The server keeps the newest 100 per sender for 7 days.
One drain can carry several moves of one item (`arrived` then `seen`): keep the furthest.

### Who drains (Friends Everywhere, 2026-10-20)

- The sender's receipts list (`friend_receipts:<sender>`) is ONE list per account between the
  desktop and the phone: whichever of them polls first takes the whole drain.
- A `poll` with `platform:"web"` never drains or returns the receipts list or the ghost list
  (`friend_ghost:<uid>`): its reply carries `receipts: []` and both wait for the next desktop or
  phone poll.
- Per-platform receipt cursors are owed; until then a phone that shows no receipt notices still
  takes them, and the desktop misses those notices.
- The inbox (`friend_inbox:<uid>`) is one list too: pokes, watches and requests go to whoever
  polls first; invites follow `accepts` (Presence).

### Squelch and block

- A squelched item: the sender hears `sent` + `item_id`, then `arrived` when the recipient next
  polls, never `seen` / `joined` / `declined`; a squelched invite then `expired`s like an ignored one.
- A block purges the blocker's inbox of the blocked side's items and their receipt records: those
  answer nothing, not even `expired`. While either side blocks, no receipt passes between them.
- Friend requests: `cancel` and `block` send nothing back.

### What this client does with them

- Reports `seen` only for things actually on screen, once each (`FriendsSeen`): an invite's knock
  card when it lands, a poke's or a watch's corner notice, a request's corner notice (or the one
  "waiting" notice when it names one person), an incoming request row drawn in an open drawer.
- An invite knocks with `KnockCard`: countdown to `expires_at`, Join (reports `joined`), Not now
  (reports `declined`), the x (puts it away unanswered into the Inbox row while it lives). It
  rings full size for `LandingRules.KnockRingSeconds` (45 s), then tucks to a small strip for
  the rest of the five minutes. A card that runs out says nothing: the server sends `expired`.
- Each friend row shows the latest poke, invite or watch sent to them (`FriendsSentBook`, fed by
  `send`'s `item_id` and `ReceiptsArrived`): sent, arrived, seen, then joined, not now or no answer.
  In memory only, gone 30 minutes after it last moved or on an account change.
- `state.blocked` is the Blocked list. The per-PC `friends_blocked.json` from wave 0 is read only
  when the server sends no list; once it does, the blocks the server lacks are sent once and the
  account's local entries are dropped (`FriendsBlockList.MigrateIfDue`).

### Server keys (read-only here)

`friend_rcpt:<id>` META EX 2 days (leash 4 days), `friend_receipts:<sender>` list LTRIM 100 EX 7 days,
`friend_rcpt_open:<sender>` hash invite id -> expiry ms EX 24 h (the lazy `expired`),
`friend_ghost:<to>` squelched item ids LTRIM 50 EX 24 h, `friend_req_meta:<to>` rows gain `seen_at`.

## Redis keys

`friends:<uid>` set, `friend_in:<uid>` set, `friend_out:<uid>` set, `friend_block:<uid>` set,
`friend_squelch:<uid>` set, `friend_inbox:<uid>` list of `I` JSON (newest first, LTRIM 50),
`friend_presence:<uid>:<platform>` string EX 180 (the old `friend_presence:<uid>` is read only),
`friend_code:<code>` -> uid (and `user.friend_code`),
`friend_poke:<from>:<to>` EX 10, `friend_req_day:<uid>:<yyyymmdd>` EX 90000,
`friend_reports` list (`{ by, about, reason, at }`, LTRIM 5000),
`friend_link:<token>` / `friend_link_of:<uid>` and their locks (Links), `pub_flink:<ip>` EX 60,
`friend_web_uid:<supabase sub>` -> uid EX 300 (the web door's hint).

Nothing here goes through the profile sync body, the anticheat clamp or the leaderboard cache.

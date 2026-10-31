# B3 - The complete shop: DECISIONS

Status: FINAL (2026-10-29). Prices are set against `B1-SPARKLES-DECISIONS.md` (FINAL, option A). Build lanes may
start; every row ships `soon` behind env until its art and equip work.

Mockup (feel reference, 2D stand-in for a 3D scene): https://claude.ai/artifact/CxD4DZdv8hFgA7Jyygy71j
3D art brief for Astra: `handoff-1029/B3-ASTRA-3D-BRIEF.md` (paste prompt at its end).

## 0. The shop in one paragraph

ONE shop, the PRIZE COUNTER: a three.js scene (one web tree, `Resources/web/prizecounter/`) built on the Back Room
counter GLB, opened from a small button on every game's main menu, from the Sparkles wallet on WPF / web / phone,
and from the counter inside the Back Room itself. The camera pans in onto the counter and settles on the first
shelf; arrows walk items (3 x 3 = 9 per shelf), stepping past a shelf edge jump-pans to the next shelf. Tabs on top
switch category: the shelves you no longer need slide up out of view, the ones you need DROP in with a wobble, dust,
a thud and rattling items that settle out of step. The camera lazily follows the focused item; a small HUD box
previews it (your Rain wearing it, your kart turning, the board piece, the frame on your card). Sparkles only, no
real money, no refunds, no gifts. EMI keeps the shop from the staff well behind the counter.

## 1. Owner calls this file stands on (summary of the survey; the verbatim answers are below)

- Families: Rain items, kart items, chess skins, profile frames/avatars, EMI desk items. No spirals/themes in 7.2.
- Sparkles only. Prime shelf = items for Prime-gated games and features, visible to all, locked for Basic.
- Rotating stock, weekly, every item returns. Owned items always stay on the shelf, stamped OWNED, wear from there;
  each tab sells at most 9 items in a week.
- Price feel: a small item is about one evening of play, one outfit piece about a week, a full set or rare piece
  3-4 weeks (B3 survey + B1 round 4; section 3).
- No gifts in 7.2 (nothing of value moves between accounts). No refunds: tap, then a confirm card.
- Full shop everywhere: every door opens the whole counter (it lands on the tab of the game you came from).
- Tabs: Rain clothes, Rain hair + accessories, Kart, Chess, Profile, EMI desk, Prime, plus a Back Room tab.
- About 45 new items for 7.2. All 3D (scene, shelves, items) is Astra's: Blender and graphic work.
- The Back Room counter is the BASE: same counter model, same buy contract, updated inside the Back Room too.

## Owner answers (survey)

### Round 1 (2026-10-29)

- Game item families: "Rain items (Recommended), Kart paints and karts, Chess skins, For anything that might need 3d assets (like the banners ir chess skins , we split this with astra, she can di the blender and graphic works)"
- App item families: "Profile frames and avatars (Recommended), EMI desk items"
- Currency: "Sparkles only (Recommended)"
- Prime-only items: "Prime shelf for things gated behind prime games or features"
### Round 2 (2026-10-29)

- Stock: "Rotating stock"
- Prices: "Small = an evening, big = a week (Recommended)"
- Gifts: "No gifts in 7.2 (Recommended)"
- Refunds: "No refunds, preview first"

### Round 3 (2026-10-29)

- Surfaces: "We can actually have the prize counter shelf , or shelfs) imagine the price counter but instead of a shelf puls the cabinato arcsde, we have a cupple of shelfs - 9 item max per shelf) we pit asmall button on the main menu if each game , that opens onsode that game the shop/prizecounter. We enter panning the camera the rouze counter and focus on the first shelf , left right to move between shelf or items. When jumping from a shelf to another(like if i am on row 1 item 3 and press right) we jump pan the camera to the other shelf. If the other shelf isnt needed (we dint have enough items) we make it slide up and disappear from the top of the view - if we need the shelf we drop it down, having wobble when falls and effects like dust and a thud sfx with rattling objects. All the items fall with the shels , and have an asynch stutter so they wobble too and settle but not at the same time, slight delay. We have tabs on the hud on top, swapping between means we switch category (avatar items, kart items, chess, etc) i think st least 5 or 6 are owed. When we move between objectsthe camera lazy follows and focus on it. we open a hud little box with a preview per item (the kart skin model rotating, or the dress on the cirrent rain avatar, or the model of the decoraiton) this will meed astras help for each item since we need models for all of them - this same prize counter will be accessible from the sparkle points hud in the wpf and on website and on the phone app(little buttons visible on main menu or after loggin in)"
- In-game shelf: "Full shop everywhere"
- Preview: "Read previous message"
- Rotation: "Weekly shelf, items come back (Recommended)"

### Round 4 (2026-10-29)

- Engine: "One shared web scene (Recommended)"
- Tabs: "Rain: clothes, Rain: hair + accessories, Kart, Chess, Profile, EMI desk, Prime shelf tab"
- Item count: "About 45 (Recommended)"
- Owned + keeper: "Owned stays, stamped; EMI keeps shop (Recommended)"

### Round 5, confirm after the mockup (2026-10-29)

Mockup: https://claude.ai/artifact/CxD4DZdv8hFgA7Jyygy71j

- Mockup feel: "Seems fine but the shole scene will be in 3d - i wanna use astra for this, give me the handoff and prompt for the shelf and items in 3d"
- Buy gesture: "Tap, then a confirm card"
- This week: "Owned always + up to 9 for sale (Recommended)"
- Old shops: "We will use the backroom counter as base and update it inside the backroom too. That same counter will be the base to mod (we alredy have shelved and some items) we can add a backroom tab if needed"

### Round 1 reading

- Reading: 3D asset work (chess banners, chess skins, anything Blender) is split with Astra, who does the Blender and graphic work. No spirals/themes family in 7.2. The Prime shelf is items for Prime-gated games/features (online Racing Thoughts is tier 2+), not a perk shelf.

## 2. Catalog for 7.2

Item ids are immutable, lowercase, `<family>.<slot>.<name>`; each row grants `cos.<id>` (one grant per item).
Names are presentation (loc keys `shop_item_<id with dots as _>_name`, ten language files). What is free today
STAYS free (Rain's default look, the eight Glassroad karts and nine EMI racing outfits, the 16 free profile items):
the shop only sells new things. Tier = price tier, section 3.

Launch stock: 45 new sellable items across the seven new tabs, plus the eight existing Back Room prizes.

| tab | 7.2 items (tier) | where the asset comes from |
|---|---|---|
| Rain clothes (8) | dresses `rain.dress.midnight-cocktail` L, `rose-flare` L, `ivory-midi` L, `plum-asymmetric` L, `scarlet-bell` L, `teal-tulip` L, `black-highlow` L; suit `rain.suit.hazmat-yellow` XL | existing Rain wardrobe meshes (C:/wt-rain wardrobe-*.js ids); Astra makes the SHELF prop (dress form) |
| Rain hair + acc (8) | hair `rain.hair.chestnut-bob` M, `copper-lob` M, `ink-sleek` M, `rose-waves` M, `platinum-long` M, `auburn-volume` M; acc `rain.acc.silver-drops` S, `cat-eye-frames` S | existing wardrobe meshes; shelf props = wig stand, jewellery pad |
| Kart (9) | 2 new kart bodies L / XL, 3 new EMI racing outfits L, 3 new paint patterns S, 1 glide umbrella variant M | NEW Glassroad GLBs in the `emi-vehicle-<id>` / `emi-outfit-<id>` conventions + `emiSkin` pattern masks; shelf prop = boxed miniature |
| Chess (6) | 2 piece clothing sets (all six piece types) XL, 2 board-side banners M, 2 base insignias S | NEW attachments on the approved sculpted pieces (never regenerate the pieces; pink/white palette kept) |
| Profile (6) | 4 avatar frames S / M, 2 avatars M | NEW 512 px PNGs in the `Resources/cosmetics` registry format; shelf prop = framed picture on an easel |
| EMI desk (4) | 2 hats M, 2 held desk toys S | NEW 2D sheets for the ten desk poses (over-layer law, ConditioningControlPanel/CLAUDE.md item 15) + a 3D shelf prop each |
| Prime (4) | Lobby host kart paint L, online podium trail L, room host profile frame M, tournament chess banner M | NEW, as their family; `needs: prime` |
| Back Room (8) | the existing Prize Parlour rows (`jackpot_remix` .. `rt_bundle_3`), ids, grants and prices unchanged | existing props in `counter.glb` `shelf_<prizeId>`, re-cut as item GLBs |

Item ids for the NEW families (names are working names; the owner may rename, ids stay):
- Kart: `kart.body.glass-slipper` XL, `kart.body.candy-tin` L, `kart.outfit.pit-crew` L, `kart.outfit.ringmaster` L,
  `kart.outfit.ballerina` L, `kart.paint.hearts` S, `kart.paint.pink-checker` S, `kart.paint.spiral` S,
  `kart.umbrella.lace` M.
- Chess: `chess.set.royal-capes` XL, `chess.set.velvet-court` XL, `chess.banner.house-pink` M, `chess.banner.crest` M,
  `chess.insignia.heart` S, `chess.insignia.star` S.
- Profile: `profile.frame.lace` S, `profile.frame.ink-stamp` S, `profile.frame.gilt` M, `profile.frame.polaroid` M,
  `profile.avatar.emi-pixel` M, `profile.avatar.rain-portrait` M.
- EMI desk: `desk.hat.top-hat` M, `desk.hat.party` M, `desk.toy.rubber-duck` S, `desk.toy.snow-globe` S.
- Prime: `prime.paint.host-gold` L, `prime.trail.podium` L, `prime.frame.room-host` M, `prime.banner.arena` M.

Later weekly drops (no date): the remaining Rain hair (`honey-roundbob`, `espresso-sidesweep`) and accessories
(`rose-gold-collar`, `midnight-ribbon`, `hazmat-mask`, `back-katana`, `action-yellow`), more kart, chess and desk items.

OWNER CALL OWED: the Back Room rows `rt_demo` and `rt_bundle_1..3` sell 2D Racing Thoughts levels, and 7.2 retires
the 2D race. Proposal: those four rows go `soon` (not for sale) from 7.2, owners keep them, and the shelf shows them
only to owners. Nothing in the build lanes depends on the answer.

## 3. Prices

Read against `B1-SPARKLES-DECISIONS.md` (FINAL, option A). B1's simulator (4000 players x 90 days): a REGULAR
(~45 min a day) earns a mean 121 SP a day, 729 a week; a casual 95 a week; a grinder 4620 a week. Two owner answers
set the ladder and they agree once lined up: B3 "small = an evening, big = a week" and B1 "a mid item (one outfit
piece) about 1 week, big items (full sets, rare skins) 3-4 weeks". So four tiers:

Prices are TIERS, never typed per item. The server turns a tier into Sparkles from one number, `E` = Sparkles a
regular earns in one evening (env `SHOP_EVENING_SP`, 121 from B1):

| tier | feel | formula | price at E = 121 | examples |
|---|---|---|---|---|
| S | one evening | 1 E | 120 | earrings, a paint pattern, an insignia, a desk toy |
| M | three evenings | 3 E | 360 | a hairstyle, a banner, an avatar, a hat |
| L | a week | 6 E | 730 | one outfit piece: a dress, an EMI racing outfit, a kart paint job |
| XL | three and a half weeks | 21 E | 2540 | a full set or rare piece: the hazmat suit, a chess clothing set, a new kart body |

Rounded to the nearest 10. Back Room rows keep their own `BACKROOM_COUNTER_PRICES`. A per-row override env
(`SHOP_PRICES`, `id=sp,...`) exists for the owner. Launch stock costs about 27,100 SP: a regular buys about one
thing a week and owns it all in about 37 weeks; a grinder clears it in about 6 weeks, which is what the weekly drops
are for. If B1's numbers move, change `SHOP_EVENING_SP` and nothing else.

## 4. Stock rotation

- Week = ISO week, UTC, rolls Monday 00:00 UTC.
- Per tab, the for-sale rows are a round robin: each cycle the tab's pool is shuffled by `seed(tab, cycle)` and cut
  into weeks of at most 9; a cycle is `ceil(pool / 9)` weeks, so every item is back within that many weeks. A tab
  with 9 or fewer items sells all of them every week (true for every 7.2 tab at launch; rotation starts to show with
  the weekly drops).
- Owned items are always shown (stamped OWNED) whatever the week; they do not take a for-sale slot.
- Shelves per tab = `ceil((owned + for sale) / 9)`, at most 4; past that the owned stamps fold onto one "your
  things" shelf at the end. Prime rows rotate like any other, shown locked to Basic.
- No discounts, no countdown clock, no "last chance" copy. A small "wk 44" chalk tag is the only sign of the week.

## 5. Wire contract (server: CCP-Server, never this repo)

Base: the Back Room counter (`backroom/CONTRACT.md` 10.17; `proxy/backroom-counter.js` + routes). The shop is the
same module generalised, OUTSIDE the Back Room door so web-only, phone and Basic players can buy.

- `GET /v2/shop/state` (v2 auth, shared limiter):
  `{ ok, sp, catalogVersion, week, tier, prizes:{revision, grants[]},
     tabs:[{ id, shelves:[{ rows:[{ id, tier, priceSp, grants, needs?, owned:{at,paidSp}|null, sale:'on'|'soon' }] }] }] }`.
  The server lays out the shelves (this week's stock + owned), so every surface draws the same shelf.
- `POST /v2/shop/buy { unified_id, idem, itemId, catalogVersion, week }`: the 10.17.C order, plus: `week` differs =
  `stock_changed` + fresh state; not in this week's stock = `not_in_stock`; a Prime row below tier 2 = `needs_prime`.
  Settle in ONE atomic user write: `sp -= priceSp`, `shop.owned[itemId] = {at, paidSp, catalogVersion}`,
  `shop.netSp -= priceSp`, `shop.revision += 1`. Receipt
  `{ ok, idem, itemId, paidSp, spBefore, sp, catalogVersion, prizes:{revision, grants} }` under `shop:ids:<uid>`; a
  replayed idem returns it byte-identical. Refusals HTTP 200 `{ok:false, reason}`: `bad_input idem_mismatch
  catalog_changed stock_changed not_in_stock unavailable owned needs_prime insufficient busy`.
- `catalogVersion` is DERIVED (hash of the resolved catalog incl. prices), as the counter does.
- Ledger: `shop.netSp` joins the SkillPointBackfill / `backroomNetSp` sum, so `/v2/user/sync` never refunds a buy.
  B1's earnings and these debits move the same `sp`; nothing else writes it for the shop.
- Ownership everywhere: grants `cos.*` join `prizesBlock(user)` (already on `/v2/user/sync` and auth validate), so
  every client learns ownership without a new call. Account merge unions `shop.owned` like `counter.owned`.
- The Back Room tab: `/v2/backroom/counter/*` stays as the same ledger under its old ids (WPF `V2PurchaseService`
  and the Back Room page keep working); `/v2/shop/state` shows those rows on the `backroom` tab.
- Equip is LOCAL per surface (where it lives today) and every surface CLAMPS to the owned grants: Rain look
  (`AppSettings.RainLook`), Glassroad garage (`glassroad.garage.v1` + logbook sync), chess skin pick, profile
  cosmetics (server `cosmetics.js resolveCosmetics` refuses an unowned shop item, since other players see it), EMI
  desk outfit key. Race rooms: the proxy's seat ticket clamps `raceLook()` to owned grants (A3 touches the same
  code; coordinate). Looks never touch feel.json or tracks, so no `onlineCourseHash` change.
- Client wallet: a receipt's `sp` is adopted through `V2WalletAdoption` with `fromBuy = true` (the only path that may
  lower `SkillPoints`). Web and phone show the receipt's `sp`.
- Every row ships `soon`; env `SHOP_ON` (comma list or `*`) turns rows on once their art and equip work.

## 6. The scene (web tree `Resources/web/prizecounter/`, three.js r186 from the shared vendor tree)

- Host seam like Glassroad / Back Room: `init {identity, sp, locale, lexicon, motion, gentle, reducedMotion, tab,
  from, platform}`; the page asks the host for `shop-state` / `shop-buy` (desktop relays to the server with the
  identity; web and phone fetch with the session token). Echo law: nothing moves until the receipt lands.
- Entry: the camera starts wide on the counter, pans in, the tab's shelves drop, focus lands on the first item.
- Navigation: arrows / stick / swipe; Q/E or bumpers switch tabs; a tap focuses an item; a tap on the focused item
  (or A / Enter) opens the CONFIRM CARD: picture, name, price, "Buy" and "Keep looking". Owned: "Wear". Locked Prime:
  "Prime" with a one-line reason. Short of Sparkles: the price in red ink and how many short.
- Juice (every effect has an IN and an OUT): shelf drop with damped wobble, dust from three contact points, thud plus
  item rattles on staggered delays, items settle out of step; lift-away accelerates up and out the top; focus = item
  bob + ink ring; buy = Sparkles fly from the wallet to the item, the wallet counts down, the OWNED stamp slams,
  EMI cheers. Gentle visuals: fades only, no dust, no shake. Motion off: cuts. One sfx bus, ducked when the counter
  opens inside a game.
- Esc ladder: confirm card -> preview -> leave (back where you came from). Panic closes it at once (`GameSurfaces`).
- Inside a game the counter MOUNTS over the paused game in the same page (`mountPrizeCounter(host, opts)`: Glassroad
  garage, chess menu, Rain wardrobe, Back Room counter); elsewhere it is its own page.

## 7. Surfaces (doors)

| surface | door |
|---|---|
| WPF panel | the header `SparkleWallet` opens the counter (today it invites to the Back Room; the Back Room keeps its own door) |
| WPF launcher | the same wallet chip on the launcher |
| Racing Thoughts 3D | a small counter button on the garage main menu (WPF, web, phone, Linux) |
| Piece by Piece | a counter button on the chess menu |
| Rain | a counter button in the wardrobe (WPF Rain preview lane) |
| Back Room | walking to the counter + E opens the 3D counter on the Back Room tab (replaces the 2D station cards) |
| Web app.cclabs.app | the wallet chip in the header, route `/prize-counter` |
| Phone | the wallet chip on home after sign-in, a web-pack screen like `src/app/glassroad.tsx` |
| Linux (Avalonia) | the wallet chip, `GameWindow.PrizeCounter` (after A5) |

## 8. Build lanes (each PR at most 600 changed lines; art is Astra's, outside git until integrated)

B3-S1..S3 and B3-W1..W4 can start now with placeholder boxes for the art; each equip lane follows its family's art.

| lane | repo | content |
|---|---|---|
| B3-S1 | CCP-Server | `proxy/shop.js` pure: catalog v1 (rows, tabs, tiers, grants, needs), tier pricing env, derived catalogVersion, weekly round robin, shelf layout, `settleBuy`; unit tests incl. replay and every refusal |
| B3-S2 | CCP-Server | `/v2/shop/state` + `/buy`, receipts, locks, limiter, `cos.*` in `prizesBlock`, `shop.netSp` in the backfill sum, merge-accounts union; route tests |
| B3-S3 | CCP-Server | Back Room rows as the `backroom` tab, `cosmetics.js` refuses unowned shop items, seat-ticket look clamp (with A3) |
| B3-W1 | this repo | `prizecounter/` scaffold, host bridge, CONTRACT.md, pure state model (tabs, shelves, focus walk, confirm, refusals); node tests |
| B3-W2 | this repo | GLB load (placeholder boxes until art), enter pan, lazy follow, jump pan with zoom dip, keys / pad / touch |
| B3-W3 | this repo | drop / lift springs, staggered item settle, dust, sfx bus, Gentle / reduced / motion off |
| B3-W4 | this repo | HUD: tabs, wallet, preview box, confirm card, OWNED stamp, Prime lock; loc keys in all ten files |
| B3-W5 | this repo | previews: kart (Glassroad `emiSkin`), chess (piece + attachment), profile card, EMI desk sheet, Rain (light preview avatar) |
| B3-W6 | this repo | Back Room: counter E opens the 3D counter on its tab; shelf-stamp queue kept |
| B3-P1 | this repo | WPF `Services/PrizeCounter/` host + window, relay with idem, receipt adoption via `V2WalletAdoption`, panic, Escape, `GameSurfaces` |
| B3-P2 | this repo | WPF doors (SparkleWallet, launcher) + equip: ProfileCosmetics accepts owned shop items, EMI desk hat / toy layer |
| B3-G1 | feat/glassroad-a | owned grants unlock new karts / outfits / patterns, counter button on the garage menu, `raceLook` clamp |
| B3-C1 | this repo | attachment loader in `piecebypiece/board/pieces.js` (a socket per piece), skin pick, counter button |
| B3-X1 | cclabs-web | vendor `public/prizecounter` (sync-arcademy list), header wallet chip, route, CSP connect-src |
| B3-M1 | CCP-Mobile | wallet chip on home, web-pack screen |
| B3-V1 | port/7.2 | `GameWindow.PrizeCounter` + wallet door (after A5) |
| B3-R1 | WPF main | Rain look clamps to owned items; lands with the WPF Rain preview lane (never a wholesale merge of `feat/rain-avatar`) |
| B3-ART | blender-scripting | Astra, `B3-ASTRA-3D-BRIEF.md`: counter rebuild with shelf units, prop kit, 45 items, preview assets |

Rows go on sale per family (`SHOP_ON`) once that family's art, preview and equip are in.

## 9. Build log

- 2026-10-29 B3-ART DONE by Astra (polish pass 2): 53 items + LOD1, scene 42.6k tris, full stock under 242k, evidence in
  `blender-scripting/counter/prize-counter/evidence/` (family-overview.png, full-stock-desktop.png). Owner visual
  acceptance pending. Imported to `Resources/web/prizecounter/assets/` on `feat/prize-counter-art` (draft #3109,
  worktree C:/wt-shop) with CONTRACT.md and `mock/state.sample.json`. Not yet imported: Rain preview GLB, kart / chess
  preview registries, 40 EMI desk overlays, 7 profile PNGs (they belong to B3-W5, B3-P2, B3-G1, B3-C1).
- 2026-10-29 lanes started: B3-W1 (C:/wt-shop-w1, feat/prize-counter-w1), B3-W2 (C:/wt-shop-w2, feat/prize-counter-w2),
  both stacked on #3109; B3-S1 (CCP-Server, C:/wt-shop-s1, feat/shop-s1).
- 2026-10-29 first lanes DONE (all drafts, nothing merged or deployed):
  #3109 art (base) <- #3110 B3-W2 scene, #3111 B3-W1 state model <- #3112 B3-W1 host seam + page (retarget #3112 to
  the art branch before #3111 merges). Combined preview branch `feat/prize-counter-preview` (C:/wt-shop-pv) = all
  three, 30/30 web tests, headless browser smoke green (walk, card, buy 1850 -> 1120, OWNED stamp in place, tab
  switch). Run: serve `Resources/web`, open `/prizecounter/dev.html` (`?tier=2 ?sp=50 ?fail=<reason> ?tab=kart`).
  CCP-Server #361 B3-S1 `proxy/shop.js` pure, 91/91 (4 unrelated suites red, not checked against main).
  Follow-ups the lanes found: price tags are blank (price comes from the HUD or a texture); the "your things" 4th
  shelf can hold more than 9 rows (page cuts at 9); high_roller `needs:'discord'` / `discord_required` not mapped by
  the page; Back Room rows bought through the shop settle on the counter ledger (`BACKROOM_COUNTER_ON`, not
  `SHOP_ON`); catalogVersion is a string ('v1.' + 12 hex); page message shapes are in CONTRACT.md (WPF host must match).
  Next: B3-W3 springs/dust, B3-W4 HUD (real confirm card, tags, loc keys), B3-S2 routes, then B3-P1 WPF host.
- 2026-10-29 trunk rule: `feat/prize-counter-preview` is the shop's feature trunk (like feat/glassroad-a); new lane PRs
  target it. Wave 2 started: B3-W3 fx (C:/wt-shop-w3), B3-W4 HUD + loc keys (C:/wt-shop-w4), B3-P1 WPF host + wallet
  door (C:/wt-shop-p1, full LFS-off worktree), B3-S2 server routes (CCP-Server C:/wt-shop-s2, stacked on #361).
- 2026-10-29 B3-W3 juice #3113 merged into the trunk (41/41 tests, smoke green) + trunk fix f4e0777ae6: focus waits for a loading tab's shelves. Owed: duck() caller when mounted in a game; ~0.4 s empty beat between lift and drop is tunable in fx/shelves.js SHELF; shelf cables show above the header in the wide shot (art).
- 2026-10-29 B3-S2 DONE: CCP-Server draft #362 (stacked on #361), `proxy/shop-routes.js` registered from
  backroom-routes.js (same auth gate, `backroom_rate` limiter, purchase -> backroom -> user_write locks) but OUTSIDE
  the Back Room door. `counter.prizesBlock` now unions `cos.*` and SUMS counter + shop revisions (monotonic; jumps once
  at rollout). `shopNetSp` joins the SkillPointBackfill spend sum; `mergeShop` in the merge engine. Tests: shop-routes
  64, merge-engine 144, shop 91, counter routes 90. The 4 red suites (descent, justdrop, loom-share, homework-routes)
  are red on origin/main too. Deploy needs: `SHOP_ON` (unset = all `soon`), optional `SHOP_EVENING_SP`,
  `SHOP_PRICES`; Redis `shop:ids:<uid>` + `shop:idorder:<uid>` (30 d TTL, 500 cap); user field `user.shop`.
  `C:/wt-shop-s2/proxy/node_modules` is a JUNCTION to wt-shop-s1's: `cmd //c rmdir` before pruning.
- 2026-10-29 B3-P1 DONE and merged into the trunk: #3114 relay (`Services/PrizeCounter/PrizeCounterRelay.cs`, unified_id
  is always the host's, idem retried once, receipt + `insufficient` adopted via V2WalletAdoption fromBuy, page `wallet`
  frame honoured only when it matches the last receipt) <- #3115 host window (shared ChaosWebViewHost so
  ScrolllerWebGate is on, `browser_data_prizecounter`, GameSurfaces id `prizecounter`, `PanicPolicy.KeptEscapePage.Counter`,
  page `host-escape.js`), and the header SparkleWallet now OPENS THE COUNTER (the old Back Room invite popup is
  unreachable; its XAML can go later). Release -c 13982 tests, one flaky BoardTiles frame-budget test passed on rerun.
  TRAP: the wallet door needs CCP-Server #361/#362 deployed first, or every click shows offline: never land the trunk on
  main before the server routes are live. Owner calls: signed-out users get the window with a status line, no sign-in
  prompt; the launcher has no wallet chip (no door there). Desk run checklist in #3115.
- 2026-10-29 B3-W4 DONE and merged into the trunk: #3116 loc (85 `shop_*` keys in all ten files, item names
  `shop_item_<id>_name`, Back Room rows reuse `br_prize_*_name`) <- #3117 prices drawn on the 3D tags (scene/tags.js)
  <- #3118 HUD (ink tab stamps, wallet count down + flyers, preview box with empty `hud.previewSlot` for W5, every
  card face, week tag). Trunk: 55/55 web tests, all ten language files parse, no BOM; smoke green desktop + phone.
  Owed: phone framing (shelf small and low, empty top: scene camera fitFov, W2); Wear and the Prime action do nothing
  yet (W5 / P2 / R1); duck() caller in game mounts.
- WAVE 2 DONE. Trunk tip = all of #3109-#3118. Next: B3-W5 previews (Rain GLB, kart emiSkin, chess attachments,
  profile, desk sheets: import the rest of Astra's out/ tree), B3-W6 Back Room door, B3-P2 equip (profile + desk), B3-G1
  Glassroad, B3-C1 chess, B3-X1 web, then a sandboxed desk run of the WPF window against a deployed server.
- OWNER CALLS 2026-10-29 evening (survey, verbatim picks): Astra's art "Signed off". Back Room 2D Racing Thoughts rows
  "Hide them, keep owned" (off the counter for new buyers, owners keep the grant, nothing deleted). Signed-out player
  "Sign-in prompt" (shelves greyed, one Sign in to shop button that opens the app sign-in). Launcher "Yes, wallet chip"
  (Sparkles chip in the launcher header, click opens the counter). Server deploy "Yes, deploy + shadow" (merge #361/#362
  after review, one proxy deploy with SHOP_ON unset).
- 2026-10-29 #3121 merged (web, signed-out): a signed-out counter greys a window display of the art (`windowShelves`, no prices, no Back Room rows) behind one "Sign in to shop" button; host seam `signIn()` / `{type:'signin'}` in CONTRACT.md; back leaves with no shelf; `shop_sign_in` in all ten files. Web 59/59.
- 2026-10-29 #3124 merged (web, phone framing owed from wave 2): portrait frames the three item columns edge to edge in the band between the tab stamps and the preview strip (`camera-math.js bayView / bandFit`, view offset); blends in below aspect 1, landscape pose unchanged; signed-out phones frame bay 0. Web 61/61, headless 390x844 + 1600x900 checked.
- 2026-10-29 #3128 merged (WPF launcher): the Sparkles readout left the account chip for its own title-bar `WalletChip` (odometer, press squish), click = `PrizeCounterHostService.Launch(from: "launcher")`. Release 13989/13989. Owed: desk look.
- 2026-10-29 #3127 merged (WPF host): the page's `signin` frame opens the panel's account dialog over the counter (queued off the WebView2 callback); once signed in the counter re-pins the account, swaps the relay and reloads the page in place (`NavigatePage`). Trunk tip b2dfbdd611. Owed: sandboxed desk run against a deployed server.
- 2026-10-29 #3131 merged (art pass 2, catalog revision 2): 62 items + LODs, new shelf + hinged enamel tag (prices on `tag_face` under `tag_pivot`, stamp 0.14 m under the hinge), slim manifest `catalogRevision 2` + `retired` map (three paints draw the bundle art, two bodies none). 85 files, 80 binary, text +80/-48. preview/ and art/ still not imported (W5/P2).
- 2026-10-29 CCP-Server #364 merged (main b795958, NOT deployed): catalog v2 in proxy/shop.js. Ten kart bodies L, pattern bundle M, action-yellow XL, hazmat-mask + back-katana S; five v1 rows `retired: true` (never pooled or sold, owner-only stamped row, grants kept); catalogVersion `v2.`; kart (15) and rain-hair (10) now rotate weekly; launch stock 33,900 SP. Suite 68/72 (the four main-red). OWNER CALL: the ten vehicles are free in the Glassroad garage today (velvet-lock, undertow by achievement).
- 2026-10-29 #3133 merged: mock state follows catalog v2 (kart.paint.hearts as an owned retired row), 14 new names (Astra's tag names) in all ten files, five retired name keys removed.
- 2026-10-29 #3134 + #3135 merged: item descriptions (`shop_item_<id>_desc`, shared `shop_item_kart_body_desc`) on the preview strip and the confirm card, all 62 items, ten files. Trunk tip 5c4705ace5, web 65/65, headless 1600x900 + 390x844 green. Owed: rename the older items to Astra's owner-approved names (23 differ), deploy the proxy.
- OWNER CALLS 2026-10-29 night (survey, verbatim picks): kart bodies "Sell them, lock in garage" (the ten real Racing Thoughts vehicles sell at L 730 SP and the Glassroad garage offers only owned bodies; supersedes "free today stays free" for kart bodies); item names "Yes, use Astra's names" (the 23 older items take Astra's owner-approved tag names in all ten languages). Preview: catalog v2 deployed with SHOP_ON=* (owner: "Deploy v2 + SHOP_ON=*").
- 2026-10-29 #3136 merged: the 23 older items take Astra's owner-approved tag names in all ten files (The Dreamy Dress, The Suggestible Duck, The Chemistry Teacher, ...); English also matches the tag text for word order and casing (Platinum Long, Racing Thoughts Vol. 1, Gilt Frame). Trunk tip 1e8e7843f4, web 65/65.
- 2026-10-29 night: GARAGE LOCK built (physics check: bodies are looks only; sim, onlineCourseHash and room never read a vehicle, so no pay-to-win and no room redeploy). Client #3138 MERGED to main (a11ada22ee) + twin #3139 MERGED on feat/glassroad-a; server CCP-Server #365 MERGED (95ada34b6, NOT deployed: GET /v2/race/profile answers `grants`, the cos.kart.* list). Garage = all ten bodies (Petal + Trinket join the ring), Lovebomb free, Velvet Lock / Undertow by achievement OR purchase, the rest dark with "In the Prize Counter"; a locked saved pick falls back to Lovebomb. Ownership: desktop init identity.grants + `owned` push on OwnershipChanged, web/phone via the profile GET (until #365 deploys they see only Lovebomb + earned). Client lock only (rooms trust look.vehicle). OPEN: the shop also SELLS kart.body.lovebomb, which stays free in the garage (retire the row or pick another free kart).

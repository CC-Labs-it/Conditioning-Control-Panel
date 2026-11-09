/* ============================================================================
 * data/prestige.js - Renewal Sky, the prestige constellation (owner approved the map 2026-11-09, artifact
 * 7UV3pZjpzujgggf3cwn8vf, sources handoff-1109/tc-prestige-map/). Data only: the rules live in sim/prestige.js.
 *
 * Words: a finished run can be RENEWED. Renewing turns the run's score into Stardust ({dust}) and adds one Depth.
 * Stardust buys stars. Each lane has 10 stars from its tip (cheap) to the core (strong); star n of a lane opens at
 * Depth n and needs star n-1 of the same lane. Ending stars are never bought: finishing a run on that ending lights
 * one for good. The core opens at Depth 10 once every lane is full.
 *
 * Star ids are `<lane>_<n>`, n = 1..10. Every lane file reads them through ctx.prestige.has(id) / .level(lane).
 * ==========================================================================*/

/** The prestige currency's player-facing name. Working name, owner call open: every lane reads it from here only. */
export const PRESTIGE_CURRENCY = 'Stardust';

export const MAX_DEPTH = 10;
export const STEPS = 10;

/** Minutes a run is planned to last: index = Depth when the run starts (0 = the first run, never renewed). */
export const T_RUN = Object.freeze([90, 80, 72, 65, 59, 54, 50, 47, 46, 45]);
/** Stardust a normal finished run earns on the curve, by Depth at the start of that run. */
export const P_RUN = Object.freeze(T_RUN.map((t, i) => Math.round(77 * 2 ** i * t / 90)));
const LANE_W = [1.2, 1.1, 1, 1, 0.95, 0.95, 0.9, 0.9];
const nice = (v) => v < 20 ? Math.max(1, Math.round(v)) : v < 200 ? Math.round(v / 5) * 5 : v < 2000 ? Math.round(v / 10) * 10 : Math.round(v / 50) * 50;
export const CORE_COST = 25000;

// [name, flavour, plain]. The plain line is what the star really does; the card shows it in italics.
const RAW = [
  { id: 'pay', name: 'Pay', tag: 'Always on', kind: 'boost', hue: '#f6c544', icons: ['wallet', 'jewel', 'chart', 'crown'], stars: [
    ['Allowance', 'Signing pays.', 'Everything you earn in a run is multiplied by 1.25.'],
    ['Raise', 'You asked so nicely.', 'All income +10%.'],
    ['Bonus', 'For good behaviour.', 'All income +10%.'],
    ['Compound', 'Savings breed.', 'Each Stardust you leave unspent adds +0.5% income, up to +50%.'],
    ['Overtime', 'You stayed late.', 'All income +15%.'],
    ['Payday', 'Right on time.', 'All income +15%.'],
    ['Interest', 'Your money works too.', 'While you save for something you cannot afford yet, income grows +1% a minute (up to +30%).'],
    ['Promotion', 'Moving up.', 'All income +20%.'],
    ['Executive', 'Corner office.', 'All income +20%.'],
    ['Overflow', 'More than you can hold.', 'Everything you earn in a run is doubled.'] ] },
  { id: 'head', name: 'Head Start', tag: 'Run start', kind: 'boost', hue: '#6fe3b8', icons: ['shoe', 'stairs', 'key', 'box'], stars: [
    ['Running Start', 'Hit the ground clicking.', 'Every run starts with 1,000 already in the bank.'],
    ['Free Sample', 'Try before you buy.', 'Your first 3 purchases in each run cost nothing.'],
    ['Pocket Money', 'A little extra.', 'Every run starts with 10,000.'],
    ['Already Picked', 'We remembered.', 'The first big choice (Watch or Listen) starts picked the way you picked it last run. You can still change it.'],
    ['Free Trial', 'No card needed.', 'Your first 6 purchases in each run cost nothing.'],
    ['Savings', 'A nest egg.', 'Every run starts with 100,000.'],
    ['Keepsake', 'Pack one toy.', 'Pick one toy you owned last run (Toy Box, Breakout, Prize Wheel...). You own it from the first second of the next run.'],
    ['Two Keepsakes', 'Pack two.', 'Keep two toys instead of one.'],
    ['Skip Ahead', 'You know this part.', 'Start at chapter 2, with chapter 1 already bought.'],
    ['Pre-signed', 'Every line, already signed.', 'Every choice you made last run starts picked. Changing any of them is free.'] ] },
  { id: 'idle', name: 'Idle Perks', tag: 'Hands off', kind: 'boost', hue: '#6cc4ff', icons: ['cloud', 'moon', 'headphones', 'lamp'], stars: [
    ['Daydream', 'Earn while you drift.', 'Income per second (the part that pays without tapping) x1.25.'],
    ['Snooze', 'Rest your eyes.', 'Pay while the tab is hidden or minimised +25%.'],
    ['Hum Along', 'Background noise.', 'Income per second +10%.'],
    ['Night Shift', 'The meter keeps running.', 'Away pay keeps counting for 16 hours instead of 8.'],
    ['Doze', 'Just a minute.', 'Income per second +15%.'],
    ['Autopilot', 'They play themselves.', 'Toys you never touch (Toy Box, Breakout, the Wheel...) still pay half their reward.'],
    ['Sleepwalk', 'Your hands know the way.', 'Every 30 seconds the cheapest thing you can afford buys itself.'],
    ['Deep Sleep', 'Further down.', 'Income per second +20%.'],
    ['Long Nap', 'Take your time.', 'Away pay +50%.'],
    ['Let Go', 'Stop trying. It pays.', 'Leave the game alone for 20 seconds and income per second goes x3 until you touch it again.'] ] },
  { id: 'active', name: 'Active Play', tag: 'Hands on', kind: 'boost', hue: '#ff6fb5', icons: ['button', 'cursor', 'metronome', 'burst'], stars: [
    ['Eager', 'Press harder.', 'Every tap on the button pays x1.25.'],
    ['Quick Fingers', 'Good hands.', 'Taps pay +10%.'],
    ['Crit Chance', 'Sometimes it hits.', 'Critical taps (the big x10 ones with a sound) happen 5% more often.'],
    ['Golden Habit', 'Gold finds the willing.', 'The Golden Button (the crayon star that floats across) shows up 25% more often.'],
    ['Steady Hands', 'Practice pays.', 'Taps pay +15%.'],
    ['Combo', 'Do not stop.', 'Keep tapping without a one second pause to build a streak: up to +50% tap pay.'],
    ['Countdown', 'Ten, nine, eight...', 'Now and then a count from 10 to 1 appears. Tap each number on its beat; a perfect count pays a full minute of income.'],
    ['Devoted', 'All in.', 'Taps pay +20%.'],
    ['Long Gold', 'Linger.', 'The Golden Button stays on screen 50% longer.'],
    ['Frenzy Fingers', 'Faster, faster.', 'Frenzy (the tap boost a Golden Button can give) lasts twice as long.'] ] },
  { id: 'trig', name: 'Trigger Words', tag: 'New mechanic', kind: 'new', hue: '#b89cff', icons: ['words', 'speech', 'magnet', 'eye'], stars: [
    ['Trigger Word', 'Say it and I listen.', 'Pick one word. During a run it floats across the screen now and then; tap it for a burst of income.'],
    ['Often', 'It comes back.', 'Your word shows up 25% more often.'],
    ['Sharper', 'It hits harder.', 'Catching your word pays +25%.'],
    ['Conditioned', 'Easier every time.', 'Each catch makes your word pay 1% more, and that growth carries into every later run.'],
    ['Second Word', 'Another one.', 'Pick a second word. Both show up.'],
    ['Sharper Still', 'Harder.', 'Catching a word pays +50%.'],
    ['Everywhere', 'It hides in plain sight.', 'Your words also turn up inside pictures, on the slot reels and in podcast captions.'],
    ['Third Word', 'And one more.', 'Pick a third word.'],
    ['Deep Groove', 'Worn in.', 'Conditioned grows twice as fast.'],
    ['Instant Response', 'Before you think.', 'Catch a word within half a second of it appearing and it pays triple.'] ] },
  { id: 'luck', name: 'Lucky Breaks', tag: 'Prizes', kind: 'boost', hue: '#ff9a52', icons: ['dice', 'wheel', 'slotmachine', 'confetti'], stars: [
    ['Lucky', 'Fortune likes you.', 'Prize Wheel, Mystery Box and Toy Box prizes +25%.'],
    ['Peek', 'Just a look.', 'The Toy Box opens 10% more often.'],
    ['Charm', 'Rub it.', 'Prizes +10%.'],
    ['Second Spin', 'Again, please.', 'The Prize Wheel spins twice and you keep the better result.'],
    ['Peek Again', 'Another look.', 'The Toy Box opens 15% more often.'],
    ['Charmed', 'It works.', 'Prizes +15%.'],
    ['No Nothing', 'Every wedge wins.', 'The Prize Wheel loses its Nothing wedge.'],
    ['Hold On', 'Wait for me.', 'The Mystery Box waits 2 seconds longer before it shuts.'],
    ['Blessed', 'It keeps working.', 'Prizes +20%.'],
    ['Jackpot', 'Always the big one.', 'Once a run, the next prize is guaranteed to be the top one.'] ] },
  { id: 'ghost', name: 'Ghost Run', tag: 'New mechanic', kind: 'new', hue: '#4fd8cf', icons: ['mirror', 'people', 'remote', 'film'], stars: [
    ['Ghost Run', 'Race your old self.', 'A see-through copy of your last run plays along on the guide map, showing where you were at the same minute. You can see if you are ahead.'],
    ['Ahead', 'Beat yourself.', 'Reach a chapter before your ghost did: +5% income for that chapter.'],
    ['Further Ahead', 'Keep going.', '+10% instead.'],
    ['Ghost Hands', 'Someone is helping.', 'The ghost taps the button with you, about once a second.'],
    ['Well Ahead', 'Pull away.', '+15% instead.'],
    ['Hints', 'It knows the way.', 'The guide map shows what the ghost bought next.'],
    ['Best Self', 'Chase your best.', 'The ghost replays your fastest run instead of your last one.'],
    ['Far Ahead', 'Lap it.', '+20% instead.'],
    ['Busy Ghost', 'It wants it too.', 'The ghost taps twice as often.'],
    ['Replay', 'Its luck is yours.', 'Every Golden Button your ghost caught shows up for you too, at the same minute.'] ] },
  // Fine Print: Amendment comes with the Silent rule so the very first star is usable (the mockup's first rule only
  // arrived with star 2, a whole run later); star 6 makes Silent pay more instead.
  { id: 'fine', name: 'Fine Print', tag: 'Challenge', kind: 'risk', hue: '#ff5d72', icons: ['eraser', 'tag', 'checklist', 'scroll'], stars: [
    ['Amendment', 'Strike a clause.', 'Before a run you can switch one thing off. Your first rule: Silent, sound off all run, x1.2 Stardust. Always optional.'],
    ['No Hands', 'Look, no clicks.', 'Rule: no tapping the button all run. x1.5 Stardust.'],
    ['Blank', 'Nothing to see.', 'Rule: no pictures. x1.3 Stardust.'],
    ['Fine Print', 'Read it twice.', 'Use two rules in one run. Their Stardust bonuses multiply.'],
    ['No Toys', 'Hands in your lap.', 'Rule: no toys (Toy Box, Breakout, the Wheel...). x1.3 Stardust.'],
    ['Hush Money', 'Quiet pays.', 'Silent pays x1.5 Stardust instead of x1.2.'],
    ['Speedrun', 'Hurry.', 'Rule: the run stops at half its time. x2 Stardust for what you made.'],
    ['One Theme', 'Faithful.', 'Rule: no Second Theme. x1.4 Stardust.'],
    ['Small Print', 'Read it three times.', 'Use three rules in one run.'],
    ['Full Contract', 'Sign everything.', 'Every rule at once. x5 Stardust.'] ] },
];

export const LANES = Object.freeze(RAW.map((l, a) => Object.freeze({
  ...l,
  stars: Object.freeze(l.stars.map(([name, flav, plain], k) => Object.freeze({
    id: `${l.id}_${k + 1}`, lane: l.id, n: k + 1, name, flav, plain, depth: k + 1, cost: nice(P_RUN[k] / 8 * LANE_W[a]),
  }))),
})));
export const LANE_BY_ID = Object.freeze(Object.fromEntries(LANES.map((l) => [l.id, l])));
export const STAR_BY_ID = Object.freeze(Object.fromEntries(LANES.flatMap((l) => l.stars.map((s) => [s.id, s]))));

/** Ending stars: [ending item id, name, icon, perk line, which run]. Lit for good by finishing a run on that ending. */
export const ENDING_STARS = Object.freeze([
  ['drop', 'Drop', 'drop', '+5% pay, for good.', 'the plain ending, where you sink all the way down'],
  ['o_out', 'Living Ad', 'billboard', 'Ads pay double.', 'the Ads route ending'],
  ['p_life', 'Lifetime Plan', 'card', 'Every run starts with the Prize Wheel.', 'the subscription ending'],
  ['tr_end', 'Count Up', 'spiral', 'Spirals pay 10% more.', 'the Trance theme ending'],
  ['bi_end', 'Bimbodoll', 'bubbles', 'The button starts as a bubble.', 'the Bimbo theme ending'],
  ['dr_end', 'Unit Complete', 'robot', 'The ticker starts at rank 2.', 'the Drone theme ending'],
  ['si_end', 'The Debut', 'dress', 'Looks cost half.', 'the Sissy theme ending'],
  ['go_end', 'Sunrise', 'sunrise', 'The Golden Button stays longer.', 'the Goon theme ending'],
  ['lo_end', 'Lost Key', 'lock', 'The Toy Box opens more often.', 'the Locked theme ending'],
  ['ce_end', 'Redacted', 'ink', 'Censor bars pay when tapped.', 'the Censored theme ending'],
  ['in_end', 'Flatline', 'bug', 'Infection spreads faster.', 'the Infection theme ending'],
  ['outside', 'Go Outside', 'hat', 'Away pay +25%.', 'the ending where you log off'],
  ['onemore', 'Endless', 'infinity', 'Keep playing after an ending, at full pay.', 'the ending you get by refusing to stop'],
].map(([id, name, icon, perk, which]) => Object.freeze({ id, name, icon, perk, which })));

export const CORE = Object.freeze({
  id: 'core', name: 'Fully Conditioned', flav: 'The hidden ending.', cost: CORE_COST,
  plain: 'The last star. It opens at Depth 10 once every lane is full. After it, runs stay at 45 minutes and Stardust buys looks.',
});

/** Fine Print rules. `star` = the star that makes the rule available; `mult` = its Stardust factor. */
export const FINE_RULES = Object.freeze([
  { id: 'silent', name: 'Silent', star: 'fine_1', mult: 1.2, better: { star: 'fine_6', mult: 1.5 }, plain: 'Sound off all run.' },
  { id: 'no_hands', name: 'No Hands', star: 'fine_2', mult: 1.5, plain: 'No tapping the button all run.' },
  { id: 'blank', name: 'Blank', star: 'fine_3', mult: 1.3, plain: 'No pictures.' },
  { id: 'no_toys', name: 'No Toys', star: 'fine_5', mult: 1.3, plain: 'No toys: Toy Box, Breakout, the Wheel and the rest stay shut.' },
  { id: 'speedrun', name: 'Speedrun', star: 'fine_7', mult: 2, plain: 'The run stops at half its time.' },
  { id: 'one_theme', name: 'One Theme', star: 'fine_8', mult: 1.4, plain: 'No Second Theme.' },
].map(Object.freeze));
/** How many rules one run may carry: the star that opens each slot. fine_10 = every rule, x5 flat. */
export const FINE_SLOTS = Object.freeze(['fine_1', 'fine_4', 'fine_9']);
export const FULL_CONTRACT = Object.freeze({ star: 'fine_10', mult: 5 });

/** Words a player can pick for Trigger Words. Plain hypno words, gender neutral, nothing a theme owns. */
export const WORD_BANK = Object.freeze(['drop', 'sink', 'blank', 'deeper', 'obey', 'good', 'empty', 'soft', 'sleep', 'yes', 'relax', 'melt', 'still', 'down', 'pink', 'give in']);

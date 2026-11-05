/* ============================================================================
 * data/items.js - the Fuzz ladder. Pure data, no DOM.
 *
 * Fields
 *   id       stable key (saves and achievements refer to it; never rename)
 *   name     chrome label, house voice (dry, no exclamation marks, no em-dashes)
 *   cost     base price in Fuzz
 *   grow     repeatable item: price = round(cost * grow^owned); absent = buy once
 *   max      cap for a repeatable item
 *   click    Fuzz added to every click
 *   fps      Fuzz per second added while owned (times owned count)
 *   bounce   Fuzz added to every drifting-bubble edge hit
 *   req      ids that must be owned before this tile shows
 *   achNeed  achievements needed before this tile shows
 *   icon     relative path under this tree
 *   line     the one deadpan line under the name (the joke lives here)
 *   widget   which ui/widget the item switches on (ui/ reads it; sim/ ignores it)
 *
 * The plan: handoff-1101/FUZZ-CLICKER-PLAN.md section 3. Prices and order are
 * the mockup's; tests/items.test.mjs pins the ladder's shape.
 * ==========================================================================*/

const F = (n) => `assets/img/features/${n}.webp`;
const P = (n) => `assets/img/prizes/${n}.png`;

export const ITEMS = Object.freeze([
  { id: 'bubble', name: 'Drifting Bubble', cost: 3, grow: 1.5, max: 12, icon: F('Bubble_pop'), line: 'It goes where it wants.', widget: 'drifters' },
  { id: 'pink', name: 'Pink', cost: 5, click: 1, icon: F('Pink_filter'), line: 'It was always going to be pink.', widget: 'pink' },
  { id: 'title', name: 'Window Title', cost: 5, icon: F('deeper_editor'), line: 'Good for privacy.', widget: 'title' },
  { id: 'floatnum', name: 'Floating Numbers', cost: 10, click: 1, icon: F('Bubble_count'), line: 'Watching the number go up is the whole hobby.', widget: 'floatnum' },
  { id: 'fpsview', name: 'Fuzz per Second', cost: 25, icon: F('lab_quiz_hero'), line: 'Now you can measure it. Nothing improves.', widget: 'fpsview' },
  { id: 'shiny', name: 'Shiny Bubble', cost: 40, click: 1, icon: F('Bubble_pop'), line: 'Pop.', widget: 'shiny' },
  { id: 'bouncepop', name: 'Bounce Pop', cost: 75, bounce: 5, req: ['bubble'], icon: F('Bubble_pop'), line: 'Every wall, every time.', widget: 'bouncepop' },
  { id: 'spiral', name: 'Spiral', cost: 100, fps: 3, icon: F('spiral_overlay'), line: 'You know the one.', widget: 'spiral' },
  { id: 'ticker', name: 'Thought Ticker', cost: 100, fps: 4, icon: F('bouncing_text'), line: 'Thoughts, going.', widget: 'ticker' },
  { id: 'trig', name: 'Trigger Words', cost: 200, icon: F('Phrase_Lock'), line: "A random word. Don't think about it.", widget: 'trig' },
  { id: 'metro', name: 'Metronome', cost: 250, fps: 10, icon: F('blink_trainer'), line: "Tick. Tick. You're fine.", widget: 'metro' },
  { id: 'binaural', name: 'Binaural Hum', cost: 300, fps: 15, icon: F('brain_drain'), line: 'Science, allegedly.', widget: 'hum' },
  { id: 'spiralspeed', name: 'Spiral Speed', cost: 500, grow: 1.3, max: 8, fps: 5, req: ['spiral'], icon: F('spiral_overlay'), line: 'Rounder.', widget: 'spiralspeed' },
  { id: 'mantra', name: 'Mantra', cost: 700, fps: 20, icon: F('audio_whispers'), line: 'A quiet voice says a word. Just one.', widget: 'mantra' },
  { id: 'subl', name: 'Subliminals', cost: 1000, fps: 25, icon: F('subliminal'), line: "You won't notice. That's the point.", widget: 'subl' },
  { id: 'ach', name: 'Achievements', cost: 1500, icon: P('brass_bell'), line: 'Gamification of the gamification.', widget: 'achievements' },
  { id: 'heart', name: 'Heartbeat', cost: 2000, fps: 30, icon: F('vibe'), line: 'Yours is faster.', widget: 'heart' },
  { id: 'floaty', name: 'Floaty Numbers', cost: 3500, click: 8, req: ['floatnum'], icon: F('Bubble_count'), line: 'Up they go.', widget: 'floaty' },
  { id: 'feed', name: 'Feed Panel', cost: 4000, fps: 35, icon: F('corner_gif'), line: 'Pictures. From the internet. You chose them.', widget: 'feed' },
  { id: 'pinkpulse', name: 'Pink Pulse', cost: 5000, bounce: 10, req: ['bouncepop', 'pink'], icon: F('Pink_filter'), line: 'In. Out.', widget: 'pinkpulse' },
  { id: 'feed2', name: 'Second Feed Panel', cost: 6000, fps: 60, req: ['feed'], icon: F('corner_gif'), line: 'One is never enough.', widget: 'feed2' },
  { id: 'screentime', name: 'Screen Time', cost: 7500, icon: F('awareness'), line: 'A wellness feature.', widget: 'screentime' },
  { id: 'levels', name: 'Levels', cost: 8000, icon: P('sparkler_steps'), line: 'Numbers about numbers.', widget: 'levels' },
  { id: 'companion', name: 'The Companion', cost: 10000, fps: 100, icon: P('emi_desk_toy'), line: 'Someone to talk to. It talks back.', widget: 'companion' },
  { id: 'chant', name: 'Chant', cost: 15000, fps: 40, req: ['mantra'], icon: F('audio_whispers'), line: 'Twice as calm.', widget: 'chant' },
  { id: 'corner', name: 'Corner Pop', cost: 15000, req: ['bubble'], icon: F('Bubble_pop'), line: 'Everyone waits for it. Nobody sees it.', widget: 'corner' },
  { id: 'neon', name: 'Neon', cost: 15000, icon: P('poster_drop_1'), line: 'Brighter. Louder. Pinker.', widget: 'neon' },
  { id: 'deep1', name: 'Deeper Still I', cost: 20000, req: ['ach'], achNeed: 10, icon: P('brass_bell'), line: 'Ten small wins. A little more fog. +10% Fuzz per second.', widget: 'deeper' },
  { id: 'boxes', name: 'Mystery Boxes', cost: 20000, icon: P('confetti_stamp'), line: 'Surprise mechanics.', widget: 'boxes' },
  { id: 'pop2', name: 'Bubble Pop II', cost: 25000, icon: F('Bubble_pop'), line: 'Clicking matters again.', widget: 'pop2' },
  { id: 'flash', name: 'Flash', cost: 35000, fps: 120, icon: F('flash'), line: "Blink and you'll see it.", widget: 'flash' },
  { id: 'spiralover', name: 'Spiral Overlay', cost: 50000, fps: 150, req: ['spiral'], icon: F('spiral_overlay'), line: 'Over everything.', widget: 'spiralover' },
  { id: 'chant2', name: 'Chant 2x', cost: 60000, fps: 100, req: ['chant'], icon: F('audio_whispers'), line: 'Speed-listening to calm.', widget: 'chant2' },
  { id: 'inbox', name: 'Inbox', cost: 60000, fps: 80, icon: P('late_slip'), line: 'Take a break and check your messages.', widget: 'inbox' },
  { id: 'lock', name: 'The Lock', cost: 75000, fps: 250, icon: F('chaster_padlock_o'), line: 'Locked. Time only goes up.', widget: 'lock' },
  { id: 'sparkles', name: 'Sparkles', cost: 100000, fps: 100, req: ['boxes'], icon: P('id_frame_gold'), line: 'Cleverly not a microtransaction.', widget: 'sparkles' },
  { id: 'takeover', name: 'Takeover', cost: 100000, fps: 400, icon: F('remote_control'), line: 'The app takes over. Consensually.', widget: 'takeover' },
  { id: 'deep2', name: 'Deeper Still II', cost: 120000, req: ['deep1'], achNeed: 20, icon: P('brass_bell'), line: 'Twenty. Foggier. +10% Fuzz per second.', widget: 'deeper' },
  { id: 'triggers', name: 'Triggers', cost: 150000, fps: 300, icon: F('free_today_stamp'), line: 'Catch it.', widget: 'triggers' },
  { id: 'voice', name: 'The Voice', cost: 200000, fps: 500, icon: F('audio_whispers'), line: 'She has opinions.', widget: 'voice' },
  { id: 'deep3', name: 'Deeper Still III', cost: 500000, req: ['deep2'], achNeed: 30, icon: P('brass_bell'), line: 'All of them. +10% Fuzz per second.', widget: 'deeper' },
  { id: 'tunnel', name: 'The Tunnel', cost: 1000000, fps: 4000, icon: F('4new'), line: 'Deeper than deep.', widget: 'tunnel' },
  { id: 'drop', name: 'Drop', cost: 2000000, icon: F('Mind_Wipers'), line: 'Cannot be undone.', widget: 'drop' },
]);

export const BY_ID = Object.freeze(Object.fromEntries(ITEMS.map((it) => [it.id, it])));

/** Items the shop pins at the front whatever their price (the drifting bubble, as the DVD logo was). */
export const PINNED = Object.freeze(['bubble']);

/** The one purchase that ends the run. */
export const ENDING_ID = 'drop';

/** Each Deeper Still adds this to the Fuzz-per-second multiplier. */
export const DEEPER_STEP = 0.10;

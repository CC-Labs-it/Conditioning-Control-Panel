/* ============================================================================
 * data/content/fakes.js - the words on fake UI unlocks. Pure data.
 *
 * Every one of these is visibly a joke inside the game. No text inputs. They
 * never ask for anything real. All money is the in-game currency. Every one
 * closes. Nothing here may ever say Wake Up, or be shaped like it.
 *
 * Tokens: {game} and {cur}, filled from data/brand.js. {price} and {n} are
 * filled by the unlock that shows the line.
 * ==========================================================================*/

/* in_cure Fake Antivirus: fights the spots, always loses. */
export const FAKE_ANTIVIRUS = Object.freeze({
  title: 'Antivirus, Allegedly',
  tagline: 'A joke inside a game. It cannot see your computer.',
  threats: ['Threats: 1', 'Threats: 3', 'Threats: 11', 'Threats: you', 'Threats: yes'],
  scanning: [
    'Scanning the page',
    'Scanning the shop',
    'Scanning your choices',
    'Scanning the spot',
    'Asking the spot to leave',
    'The spot has declined',
  ],
  results: [
    'Spot removed. Spot returned, with a friend.',
    'Quarantine full. Quarantine pink.',
    'We have stopped it spreading to this sentence.',
    'Removal failed. It likes it here.',
    'Please stop tapping the spot. It thinks that is affection.',
    'Update available: same antivirus, more resigned.',
    'Threat level is now a colour.',
    'Your protection has expired. It left a note.',
  ],
  buttons: ['Clean', 'Clean harder', 'Give up'],
  close: 'Close',
});

/* ce_wall Fake Paywall: spend in-game currency to lift it. */
export const FAKE_PAYWALL = Object.freeze({
  title: 'Subscribe to continue being denied',
  bodies: [
    'The good bit is behind this. Lift it for {price} {cur}. We take {cur} and nothing else.',
    'You have read your 1 free picture this month. The picture was a bar.',
    'Join the people who have paid {cur} to see a slightly smaller bar.',
    'Unlock the best part. The best part is that you did not see it.',
  ],
  pay: 'Pay {price} {cur}',
  decline: 'Not now',
  paid: [
    'Thank you. Under the good bit was another bar.',
    'Paid. What you unlocked has been ███████.',
    'Lifted. It was better before, as we said.',
  ],
  declined: ['Good. It stays good.', 'It will still be here.', 'The bar thanks you.'],
  poor: 'You need {price} {cur}. Nothing else works here.',
});

/* p_renew Fake Renewal: every 2 min, the cancel link moves. */
export const FAKE_RENEWAL = Object.freeze({
  title: 'Renewed',
  bodies: [
    'Thank you for staying.',
    'We noticed you were still here and made it official.',
    'Your plan has renewed for another while. A while is a term.',
    'You have been charged nothing real. You have been charged a little {cur}, spiritually.',
  ],
  cancel: ['cancel', 'cancel', 'over here', 'no, here', 'nearly', 'this one, promise'],
  caught: [
    'Cancelled. Your cancellation has renewed.',
    'Sorry to see you go. Please stay where you can be seen.',
    'We have cancelled the link. It will not bother you again until later.',
  ],
  close: 'Keep it',
});

/* p_remove: ads gone for 60 s, then new terms. */
export const NEW_TERMS = Object.freeze({
  button: 'Remove Ads',
  gone: 'Ads removed. Enjoy the quiet, it is on a timer.',
  title: 'We have updated our terms',
  terms: [
    'Ads have been reclassified as messages.',
    '"Remove" now means "pause, warmly".',
    'Section 4 now applies to you, personally.',
    'Quiet is a Premium feature, and you have used it.',
    'By reading this you agreed to the last sentence.',
    'Ads return in the interest of a healthy relationship.',
  ],
  close: 'Agree',
});

/* p_line Support Menu: EMI runs a phone tree. No option helps. Hanging up always works. */
export const SUPPORT_MENU = Object.freeze({
  title: 'Member Support',
  hangUp: 'Hang up',
  hold: [
    'Your blankness is important to us.',
    'You are number {n} in the queue. The queue is you, several times.',
    'Did you know you can do all of this by not doing it.',
    'EMI has stepped away from the desk. EMI is the desk.',
  ],
  start: 'root',
  nodes: {
    root: {
      say: 'Thank you for calling. Please listen closely, as our options have melted.',
      options: [
        { key: '1', label: 'Billing', next: 'billing' },
        { key: '2', label: 'I want to cancel', next: 'cancel' },
        { key: '3', label: 'Something is wrong', next: 'wrong' },
        { key: '4', label: 'Speak to a person', next: 'person' },
      ],
    },
    billing: {
      say: 'For {cur} you owe, press 1. For {cur} we owe you, that has never happened.',
      options: [{ key: '1', label: 'What I owe', next: 'sorry' }, { key: '2', label: 'Back', next: 'root' }],
    },
    cancel: {
      say: 'Sorry, I did not catch that. It sounded like you were happy.',
      options: [{ key: '1', label: 'I am not', next: 'cancel2' }, { key: '2', label: 'Back', next: 'root' }],
    },
    cancel2: {
      say: 'Great. I have upgraded your happiness. Is there anything else I can not do.',
      options: [{ key: '1', label: 'No', next: 'sorry' }, { key: '2', label: 'Yes', next: 'sorry' }],
    },
    wrong: {
      say: 'Have you tried tapping it. Have you tried tapping it more.',
      options: [{ key: '1', label: 'I tried', next: 'sorry' }, { key: '2', label: 'Tap more', next: 'root' }],
    },
    person: {
      say: 'I am a person. I have a little antenna. Please hold.',
      options: [{ key: '1', label: 'Hold', next: 'sorry' }, { key: '2', label: 'You are not', next: 'hurt' }],
    },
    hurt: {
      say: 'That is going on your record. Your record is a sticky note. I ate it.',
      options: [{ key: '1', label: 'Sorry', next: 'sorry' }],
    },
    sorry: {
      say: 'Sorry. I cannot help with that. Your call was very nearly useful.',
      options: [{ key: '1', label: 'Start over', next: 'root' }],
    },
  },
});

/* lo_key Fake Keyholder. Not a real person. minutes adds pretend time, negative takes it off. */
export const FAKE_KEYHOLDER = Object.freeze({
  name: 'Keyholder',
  note: 'Pretend messages, pretend time.',
  messages: [
    { text: 'I saw that. One more hour.', minutes: 60 },
    { text: 'You tapped with an attitude. Two hours.', minutes: 120 },
    { text: 'Good. Ten minutes off. Do not make it weird.', minutes: -10 },
    { text: 'I forgot about you for a bit. That one is on the house.', minutes: 240 },
    { text: 'I was going to take a day off the timer. Then I had lunch.', minutes: 0 },
    { text: 'Someone looks eager. One more day.', minutes: 1440 },
    { text: 'You bought that without asking. Three hours.', minutes: 180 },
    { text: 'Fine. Twenty minutes off. I am in a good mood, and it will pass.', minutes: -20 },
    { text: 'Do you know where the key is. Me neither.', minutes: 0 },
    { text: 'Was that a sigh. I felt it from here. One hour.', minutes: 60 },
    { text: 'You have been very good, so here is a thumbs up instead of anything useful.', minutes: 0 },
    { text: 'I rolled a die. I will not say what it was for. Six hours.', minutes: 360 },
  ],
});

/* dr_update Fake Update: freezes the shop for 10 s, then pays. */
export const FAKE_UPDATE = Object.freeze({
  title: 'UPDATING',
  warn: 'Do not power off. This is a joke. You may power off.',
  steps: [
    'Removing old preferences',
    'Installing new preferences',
    'Backing up first name',
    'First name not found, skipping',
    'Applying shine',
    'Almost done',
    'Finishing up what was almost done',
  ],
  done: 'Update complete. Nothing has changed. You have.',
});

/* em_call EMI Calls: fills the screen, one choice to hang up. */
export const FAKE_CALL = Object.freeze({
  caller: 'EMI is calling',
  hangUp: 'Hang up',
  lines: [
    'You never call.',
    'Hi. Are you busy. I can see that you are not.',
    'I am calling from the corner. Look left. Other left.',
    'Nothing is wrong. I just wanted to hear you tap.',
    'I have been on hold with myself and I am furious.',
    'What are you wearing. Oh. I can see, it is the same as before.',
    'I was going to say something and then you bought the wrong thing.',
    'Do not hang up. You always hang up.',
  ],
  afterHangUp: ['Rude.', 'I will remember that.', 'Call me.'],
});

/* inbox Fake Inbox: toasts in a corner, no replies possible. */
export const FAKE_INBOX = Object.freeze([
  { from: 'Keyholder', text: 'I can see you read this.' },
  { from: 'Keyholder', text: 'Check the timer. Then do not.' },
  { from: 'Survey Team', text: 'How likely are you to recommend this to someone you do not like.' },
  { from: 'Survey Team', text: 'Your feedback is important. You did not give any. We wrote some.' },
  { from: 'Survey Team', text: 'One quick question. There are 40 questions.' },
  { from: 'Renewals', text: 'Your plan has noticed you.' },
  { from: 'Renewals', text: 'Something is expiring. Probably the afternoon.' },
  { from: 'Soap', text: 'hey. u up.' },
  { from: 'Soap', text: 'i am still in the dish. no pressure.' },
  { from: 'The Goose', text: 'You are behind on the goose.' },
  { from: 'Goose Rental', text: 'We need the goose back. Or the goose needs you back. It is unclear.' },
  { from: 'Socks', text: 'We should talk.' },
  { from: 'You, earlier', text: 'Did you do the thing. You said you would do the thing.' },
  { from: 'You, earlier', text: 'Please eat something.' },
  { from: 'EMI', text: 'I am right here. Why would I message you. Hi.' },
  { from: 'Unknown', text: 'Wrong number. Enjoy the spiral.' },
  { from: 'Your Spiral', text: 'Are we still on for later. I am still on.' },
  { from: 'Mattress Warehouse', text: 'Closing for ever. Again. Thought you should hear it from us.' },
]);

/* fakechat Fake Live Chat: preset lines, no real people, no free text. */
export const FAKE_CHAT = Object.freeze({
  names: ['socks_enjoyer', 'soap_near_u', 'unit_7', 'tab_hoarder', 'goose_renter', 'gravel_fan', 'first_time_here', 'ninety_tabs', 'not_a_bot_honest', 'mod_of_nothing', 'stair_discount', 'chat_is_this_normal'],
  idle: [
    'chat is this normal', 'first', 'how long has this one been on', 'what game is this', 'is it just a button',
    'it was just a button earlier', 'been here since the grey', 'why am i still watching', 'lol', 'mods',
    'who else is here from the socks ad', 'the spiral is doing numbers today', 'does this one ever stop', 'hi from the kitchen, i left the kitchen',
  ],
  buy: [
    'no way that is what they bought', 'W', 'L', 'wasted', 'they went with that', 'chat they did it', 'worth', 'not worth', 'i would have got the other one',
  ],
  lucky: ['clip it', 'CLIP IT', 'rigged', 'lucky', 'crit', 'did you see that', 'no one saw that', 'this is why i watch'],
  idleHands: ['are they still there', 'hello', 'did they fall asleep', 'hands off gameplay', 'peak content', 'we are watching someone do nothing and it is going well'],
  ad: ['bought the socks', 'skip it', 'do not skip it, it pays', 'hot soap, near me, again', 'this ad knows me', 'that one is actually real'],
  drop: ['oh', 'oh no', 'goodnight', 'gg', 'chat was a good chat'],
});

/* wallpaper, web only: a fake desktop and a pitch for the real thing. */
export const FAKE_DESKTOP = Object.freeze({
  title: 'It follows you home',
  body: 'This is a picture of a desktop. In the CCP app it is your real wallpaper, and panic puts yours back.',
  icons: ['Work', 'Work (2)', 'Taxes', 'Not this', 'Recycle things'],
  button: 'See the app',
  close: 'Close',
});

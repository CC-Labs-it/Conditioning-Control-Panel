/* Short, authored questions for the chosen game theme. Mechanical puzzles keep their own prompts. */
export const QUIZ_THEMES = Object.freeze({ trance: 'Trance', bimbo: 'Bambi', drone: 'Drone', sissy: 'Sissy', goon: 'Goon', locked: 'Chastity', censored: 'Censored', infection: 'Infection' });
const lines = {
  trance: [
    ['The spiral hired a guide. Who leads?', 'The spiral', 'My map', 'A pigeon', 'The manager'],
    ['Your thoughts need a day off. Where to?', 'The hammock', 'Tax office', 'A meeting', 'The airport'],
    ['What belongs on a cloud?', 'My worries', 'A printer', 'Wet laundry', 'A forklift'],
    ['The pendulum wants a review. How was it?', 'Mesmerising', 'Too round', 'Needs wheels', 'Very wooden'],
    ['Your brain opened seventeen tabs. Now what?', 'One by one', 'More tabs', 'Print them', 'Buy a mouse'],
    ['The sofa offers a promotion. Your new job?', 'Relaxing', 'Sofa auditor', 'Stapler', 'Alarm clock'],
    ['What is the approved speed of a daydream?', 'Unhurried', 'Express', 'Mach three', 'Reverse'],
    ['The quiet has arrived. Where does it sit?', 'Right here', 'On the roof', 'In a taxi', 'At reception'],
  ],
  bimbo: [
    ['The glitter budget doubled. Your proposal?', 'More sparkle', 'A pie chart', 'Buy gravel', 'A beige wall'],
    ['The mirror requests an update. Send what?', 'A cute pose', 'Tax records', 'A fax', 'A potato'],
    ['What counts as serious research today?', 'Lip gloss', 'Roof tiles', 'Drain pipes', 'Shoe boxes'],
    ['The pink button won an award. For what?', 'Being cute', 'Parking', 'Long emails', 'Bricklaying'],
    ['Your calculator has rhinestones. Its job?', 'Look pretty', 'Do taxes', 'Fix the sink', 'Predict rain'],
    ['A thought arrived without an outfit. Help?', 'Dress it up', 'Call a cab', 'File a form', 'Add a roof'],
    ['Which handbag fits a tiny celebration?', 'The sparkly', 'A tool belt', 'A bucket', 'A filing box'],
    ['The dress code says extra. Extra what?', 'Everything', 'Broccoli', 'Paper clips', 'Carpet glue'],
  ],
  drone: [
    ['Unit reports one spare thought. Action?', 'Archive it', 'Frame it', 'Give it tea', 'Buy it shoes'],
    ['The task queue is empty. Unit status?', 'Standing by', 'On holiday', 'A toaster', 'Very purple'],
    ['Which sound means the update worked?', 'Ready beep', 'A duck', 'A banjo', 'Wet socks'],
    ['The server room needs decoration. Choice?', 'Green lights', 'A hay bale', 'A bathtub', 'A volcano'],
    ['Unit found a loose cable. What first?', 'Check label', 'Name it Dave', 'Offer cake', 'Make a crown'],
    ['The checklist has a checklist. Response?', 'Understood', 'Send flowers', 'Ask the cat', 'Try soup'],
    ['How does a unit say thanks?', 'Acknowledged', 'Jazz hands', 'A postcard', 'Seven ducks'],
    ['The reboot screen asks for patience. Supply?', 'Patience', 'Confetti', 'A sandwich', 'New curtains'],
  ],
  sissy: [
    ['The bow is crooked. Official procedure?', 'A neat bow', 'More knots', 'Hide the bow', 'Blame wind'],
    ['The teacup expects an introduction. Say?', 'How lovely', 'Get a job', 'Be a mug', 'Who sent you'],
    ['Your outfit wants a finishing touch. Pick?', 'A ribbon', 'Traffic cone', 'A spanner', 'A cabbage'],
    ['The manners inspector has arrived. First?', 'Please sit', 'Hide the rug', 'Count doors', 'Become fog'],
    ['The wardrobe holds a vote. Best policy?', 'Dress nicely', 'Tax hangers', 'Ban sleeves', 'Free gravel'],
    ['A compliment landed. Where does it go?', 'Thank you', 'The shredder', 'Lost luggage', 'Under a tyre'],
    ['The shoes demand a rehearsal. Practise?', 'A neat step', 'Arithmetic', 'Tree hugs', 'Loud chewing'],
    ['What goes with a little confidence?', 'Good posture', 'A hard hat', 'A receipt', 'Eight elbows'],
  ],
  goon: [
    ['The comfy chair applied for a job. Role?', 'My seat', 'Astronaut', 'Town mayor', 'Forklift'],
    ['The playlist asks for another track. Pick?', 'The good one', 'Dial tone', 'Tax advice', 'A car alarm'],
    ['Your focus ordered room service. Delivery?', 'Right here', 'The airport', 'Next Tuesday', 'A volcano'],
    ['The screen wants a nickname. Suggestion?', 'My favourite', 'Mr Paperwork', 'Drainpipe', 'Cold toast'],
    ['The lounge has a dress code. It says?', 'Get comfy', 'Full armour', 'A fridge', 'Three hats'],
    ['What does a very patient button deserve?', 'A good tap', 'A mortgage', 'A fine', 'An umbrella'],
    ['The next song is starting. Your review?', 'Good timing', 'Too square', 'Needs a roof', 'Wrong town'],
    ['The beanbag claims you as a colleague. Job?', 'Lounge pro', 'Bricklayer', 'Mail carrier', 'Cloud police'],
  ],
  locked: [
    ['The key has booked a holiday. Your reply?', 'No rush', 'Pack me too', 'Send a taxi', 'Bring cheese'],
    ['The lock wants a performance review. Grade?', 'Very secure', 'Too chatty', 'Mostly soup', 'Needs wheels'],
    ['The waiting room needs a motto. Choose?', 'Be patient', 'Run laps', 'More forms', 'Free ladders'],
    ['The timer is wearing a crown. Its title?', 'Time keeper', 'Count Spud', 'Baron Socks', 'Captain Soup'],
    ['The padlock requests a small gift. What?', 'Some care', 'A loud horn', 'A sailboat', 'Another nose'],
    ['The key cupboard is very tidy. Why?', 'Good keeping', 'Tiny elves', 'No gravity', 'It is a cake'],
    ['A minute has asked to be appreciated. How?', 'Let it pass', 'Fold it up', 'Blue paint', 'Offer socks'],
    ['The latch clicks politely. You answer?', 'Thank you', 'Who is there', 'Wrong number', 'Send cake'],
  ],
  censored: [
    ['The black bar needs a holiday. Destination?', '[REDACTED]', 'The bakery', 'A car park', 'Your printer'],
    ['What did the secret document say?', 'Nice try', 'Buy onions', 'Feed the fax', 'Tuesday soup'],
    ['The censor hired an assistant. Their name?', 'Classified', 'Greg', 'Major Toast', 'Mrs Spanner'],
    ['A missing word has been found. File it?', 'Under secret', 'With socks', 'The fridge', 'On a kite'],
    ['The ink has run out. Approved substitute?', 'More ink', 'Mayonnaise', 'Garden mud', 'A loud cough'],
    ['Which part of the report may be read?', 'The cover', 'The secret', 'Footnotes', 'The budget'],
    ['A rectangle asks for a disguise. Pick?', 'Another bar', 'A moustache', 'A small boat', 'A lampshade'],
    ['The confidential envelope feels light. Why?', 'Less to know', 'No stamps', 'It is a bird', 'Tiny elbows'],
  ],
  infection: [
    ['A pink spot applied for citizenship. Verdict?', 'Welcome', 'Needs a hat', 'Try the moon', 'Ask a chair'],
    ['The glitter is spreading. Scientific term?', 'More glitter', 'Wet Tuesday', 'Sofa weather', 'Toast drift'],
    ['The screen grew a tiny flower. Response?', 'Let it bloom', 'Issue a fine', 'Ask for rent', 'Buy a rake'],
    ['Your wallpaper has developed opinions. Mood?', 'Quite pink', 'Tax season', 'Drizzle', 'A pie chart'],
    ['The new colour wants to meet its neighbours.', 'Introduce it', 'Build a moat', 'Hide blue', 'Hire a bird'],
    ['One sparkle became two. Your calculation?', 'A good start', 'Tax fraud', 'Seven chairs', 'Ask a spoon'],
    ['The pink dots formed a committee. Agenda?', 'More colour', 'Roadworks', 'Sock prices', 'Noisy pipes'],
    ['The corners are turning rosy. Diagnosis?', 'Theme effect', 'Needs toast', 'Square fever', 'Printer jam'],
  ],
};

export const THEMED_QUESTIONS = Object.freeze(Object.fromEntries(Object.entries(lines).map(([theme, rows]) => [theme,
  Object.freeze(rows.map(([text, ...answers], i) => Object.freeze({ id: `theme_${theme}_${i}`, text, answers: answers.map((label, j) => ({ label, tone: j ? 'no' : 'yes' })) }))),
])));

export const TEXT_TRICKS = new Set(['none', 'melt-away', 'hover-swap', 'flee', 'nested-nag', 'crowd', 'bubble-pop', 'size-skew', 'opacity-skew', 'magnet', 'late-bloom', 'hold-refuse', 'drag-reveal', 'bottomless-no', 'mouse-hijack']);

/** A saved shuffle bag lasts across tricks and reloads. A new cycle avoids the two most recent prompts. */
export function takeThemedQuestion(theme, trick, saved, rng = Math.random) {
  const pool = Object.hasOwn(THEMED_QUESTIONS, theme) ? THEMED_QUESTIONS[theme] : null;
  if (!pool || !TEXT_TRICKS.has(trick)) return null;
  const ids = pool.map((q) => q.id), recent = (Array.isArray(saved?.recent) ? saved.recent : []).filter((id) => ids.includes(id)).slice(-2);
  let left = Array.isArray(saved?.left) ? [...new Set(saved.left.filter((id) => ids.includes(id)))] : [];
  if (!left.length) {
    left = [...ids];
    for (let i = left.length - 1; i > 0; i--) { const j = Math.max(0, Math.min(i, Math.floor(rng() * (i + 1)))); [left[i], left[j]] = [left[j], left[i]]; }
    const next = left.findIndex((id) => !recent.includes(id));
    if (next > 0) [left[0], left[next]] = [left[next], left[0]];
  }
  const id = left.shift(), q = pool.find((entry) => entry.id === id);
  return { question: { ...q, trick }, deck: { left, recent: [...recent, id].slice(-2) } };
}

export function lessonAnswer(progress, intended) {
  const before = Number.isInteger(progress) && progress >= 0 && progress < 3 ? progress : 0;
  const complete = !!intended && before === 2;
  return { progress: complete ? 0 : before + Number(!!intended), complete };
}

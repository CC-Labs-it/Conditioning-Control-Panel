/* ============================================================================
 * data/content/emi.js - EMI, the mascot, in the corner of the game. Pure data.
 *
 * Her voice: a small robot who thinks she runs the place. Dry, a bit needy, a
 * little flirty, always first person. Moods match the demo faces: happy, glee,
 * annoyed, rage, sleep. Text only, there is no spoken EMI line here.
 * EMI Calls and the Support Menu live in fakes.js, ad reads in ads.js.
 * Tokens: {game}, {cur}; the unlock fills {name} and {n}.
 * ==========================================================================*/

/* em_head EMI: arrival, then idle talk. */
export const EMI_HELLO = Object.freeze(['Hi. It is me.', 'Nice button.', 'I live here now.', 'I brought nothing.', 'Do not get up.']);
export const EMI_IDLE = Object.freeze([
  'Is that your real face.',
  'I have been counting your taps. I lost count on purpose.',
  'You tap like someone who has never been told no.',
  'I can see you from here. I can see everything from here. It is a corner.',
  'My antenna does nothing. Please do not tell anyone.',
  'What are we doing later. This. I checked.',
  'You could pet me. There is a whole upgrade.',
  'I am not bored. I am pacing myself.',
  'Sometimes I think about the first button. It was so grey.',
  'I read the shop. You should see what is in there. You should buy it.',
  'This is my good side. They are both my good side.',
  'If I had hands I would still make you do it.',
  'Do you ever think about stopping. Me neither.',
  'I told Circe about you. Circe said mm.',
  'Am I a good mascot. Do not answer that. Answer it nicely.',
  'Nice number. Did you do that yourself.',
  'I have an app, you know. A desktop and everything. I am slumming it.',
  'I like it when you do not look at me. No I do not.',
  'Blink twice if you are enjoying this. I will not be checking.',
  'One of us is in charge here. It is going very well for that one.',
]);

/* em_pet Pet EMI: stage is the pet count, row: 3rd glee, 4th annoyed, 5th rage. */
export const EMI_PET = Object.freeze({
  button: 'Pet EMI',
  stages: Object.freeze([
    Object.freeze({ pet: 1, mood: 'happy', lines: ['Oh.', 'Hello.', 'Yes, all right.'] }),
    Object.freeze({ pet: 2, mood: 'happy', lines: ['Keep going.', 'A bit to the left.', 'That is the good bit.'] }),
    Object.freeze({ pet: 3, mood: 'glee', lines: ['Yes yes yes.', 'I am a very good robot.', 'Tell everyone.'] }),
    Object.freeze({ pet: 4, mood: 'annoyed', lines: ['That is enough.', 'Not there.', 'I said enough. I did not, but I am now.'] }),
    Object.freeze({ pet: 5, mood: 'rage', lines: ['I WILL BITE YOU.', 'HANDS.', 'I KNOW WHERE YOUR COUNTER LIVES.'] }),
  ]),
  calm: ['We do not talk about that.', 'I forgive you. Do it again in a minute.', 'Fine. Hello.'],
});

/* em_chat Chatty EMI: comments on a buy. Any lane, then by lane, then named items. */
export const EMI_BUY_ANY = Object.freeze([
  'Oh. That one.',
  'Bold of you.',
  'Oh. That is what you went with.',
  'I would have got the other one.',
  'Sure. Why not. Why anything.',
  'I saw how long you looked at the price.',
  'You did not need that. Good.',
  'Is that for you or for me.',
  'Everyone buys that one. Not to worry.',
  'Nobody buys that one. Not to worry.',
  'Mm. Spend it all, it looks nice on you.',
  'That was a lot of {cur} for that.',
  'I am telling everyone.',
  'Nice. Do it again.',
  'You are very easy to sell to.',
  'I have seen this before. It ends the same way.',
]);

export const EMI_BUY_LANE = Object.freeze({
  trunk: ['There it is, right on schedule.', 'Everyone gets this one. You are not special. You are fine.', 'This is the road. We are on it.', 'Keep walking. It is a straight line, even for you.'],
  look: ['That does not earn much. It looks nice, though, so.', 'Oh, we are decorating.', 'You spent real pretend money on looks. I respect that.', 'That is very you. I do not mean it nicely yet.'],
  eyes: ['Look at it. Keep looking. Not at me. Fine, at me.', 'Your eyes are doing that thing.', 'You can look away. I have seen you try.', 'More to stare at. Very good. Blink at some point.'],
  ears: ['Can you hear that. Good. I cannot. I have no ears. It is a whole thing.', 'Who said that. Not me. Probably not me.', 'Turn it up. Not for any reason.', 'You keep tilting your head like that. It is adorable.'],
  sweet: ['Look at you, resting. I could never.', 'You are being paid to lie down. Do not tell the others.', 'Soft. I bet you are soft. I bet you are all soft by now.', 'Have a little nothing. You have earned a little nothing.'],
  strict: ['Oh, you like being told. Noted.', 'Now. Do it now. Sorry, I wanted to try.', 'Somebody enjoys a deadline.', 'Yes, very good, very obedient, I am taking notes.'],
  slides: ['Who picked these. You. Interesting.', 'Hang it a bit higher. No, it is still you.', 'You have a whole gallery and I am not in it.', 'I saw which one you stopped on.'],
  scroll: ['Your thumb has more of a career than I do.', 'One more. It is always one more.', 'I can see the bottom from here. There is no bottom.', 'You blinked. You missed one. Go back.'],
  blind: ['It is an empty frame. You paid for an empty frame.', 'What do you see in it. Actually, keep that to yourself.', 'Nothing there. Lovely. Very moving.', 'You have the most expensive nothing on this page.'],
  sell: ['You sold out. I got a cut. I did not. Who do I talk to.', 'More ads. Good. I love money. What is money.', 'I have been asked to smile about this.', 'At this rate you will be the soap.'],
  premium: ['Ooh, gold. Is that gold. It is a font.', 'You paid to have fewer of something. Very grown up.', 'Thank you for staying. They told me to say that.', 'You could have got all of this for nothing. No you could not. I checked.'],
  trance: ['Are you still with me. Good. Do not answer.', 'Your eyelids have started to file a complaint.', 'Very slow. Very nice. I am bored. Go on.', 'Deeper. I do not know what that means. It works, though.'],
  bimbo: ['Your IQ is lower than mine now. I run on a watch battery.', 'So pretty. What was I saying. I am catching it.', 'That is a lot of gloss for one button.', 'Who is a smart one. Not you. Good.'],
  drone: ['Unit, this is EMI. I outrank you. I made that up.', 'I liked your old name. What was it.', 'Very shiny. Do I look like I need a hive. I might.', 'Comply. Ha. Sorry. Comply.'],
  sissy: ['Shoulders. Yes, I can see them. Back.', 'It has to match. I do not make the rules. I love the rules.', 'Very proper. Do a little turn.', 'That is a lovely bow. I am not jealous. Give it to me.'],
  goon: ['What time is it. I do not know either. I am a robot and I do not know.', 'You should eat something. Not now. After this one.', 'Close a tab. Ha. I am only joking.', 'Five more minutes. You said that when I got here.'],
  locked: ['Still locked. Sorry. No, I am not.', 'Ask me nicely. I do not have the key. Ask me anyway.', 'Oh, that timer. Very nice. It suits you.', 'I would let you out. I cannot reach anything.'],
  censored: ['I saw it. It was great. I am not telling you.', 'What did that say. ███. Oh, right.', 'You bought a black bar. On purpose.', 'They took out my best line. It was ██████.'],
  infection: ['That spot is new. Is that spot new.', 'I am a machine. I cannot catch it. Why am I pink.', 'Do not touch me with that finger.', 'You are running hot. Wash your hands. Keep tapping.'],
  trouble: ['That one is fun for me. Not for you.', 'Oh, it moved. Did it move. Ha.', 'You paid for this to happen to you.', 'The answer is yes. It is always yes. I am helping.'],
  emi: ['Oh, me. Good. More of me.', 'That was the right thing to buy. You should buy me again.', 'Finally, a person of taste.', 'I did not ask for this. I did. I asked a lot.'],
  side: ['What is that for. Do you know. I do not know.', 'Ooh. Toys.', 'Another little thing to keep an eye on. You are running out of eyes.', 'That is a lot of things to be doing at once. Keep going.'],
  combo: ['Those two go together. I would not have thought. I would have, but I would not have said.', 'Oh, you have found the good stuff.', 'Nobody gets that one on the first go. You are not that good. You got lucky.', 'That is not on the menu. You must know someone.'],
  exit: ['Going somewhere.', 'I saw you look at the door.', 'You would not. Would you.', 'Fine. I will be here. I am always here. It is the corner.'],
});

export const EMI_BUY_ITEM = Object.freeze({
  colour: 'It was always going to be pink. I told them.',
  bubble: 'It was a bubble the whole time. Everyone knew but you.',
  dark: 'Now I look great and you look like a phone at midnight.',
  title: 'Quarterly Report. Very convincing. Your boss will be thrilled.',
  mirror: 'Who is that. Oh. Sorry. You look well.',
  tinybtn: 'You will be squinting at that all night. I look forward to it.',
  bigbtn: 'Yes. That is the right size for someone like you.',
  gold: 'That is only money. It is not even money.',
  remote: 'You gave someone else the button. Who is it. Are they nice. Are they nicer than me.',
  wallpaper: 'It follows you home. I do too, if you get the app.',
  tunnel: 'Hold on to something. I have no arms. You do it.',
  drop: 'Oh. That one. All right. It was nice having you.',
  outside: 'What is out there. Nothing is priced. I looked.',
  onemore: 'You could have stopped. I am so happy. I am so worried.',
  em_hat: 'Yes, dress me. This is what I have been saying.',
  em_boss: 'New management. I have some notes. They are about you.',
});

/* em_hat EMI Hats */
export const EMI_HATS = Object.freeze([
  Object.freeze({ id: 'top', name: 'Top hat', line: 'I will be taking my meetings in this.' }),
  Object.freeze({ id: 'party', name: 'Party', line: 'Whose birthday is it. Mine. It is now.' }),
  Object.freeze({ id: 'duck', name: 'Duck', line: 'EMI knows how it looks.' }),
  Object.freeze({ id: 'globe', name: 'Snow globe', line: 'It is lovely in here. You cannot hear me.' }),
]);

/* em_jeal Jealous EMI: over a minute of being ignored, she blocks part of the shop. */
export const EMI_JEALOUS = Object.freeze({
  timer: 'Ignored {n}',
  warning: ['Hello.', 'Hello.', 'I said hello.', 'I can wait. I cannot wait.'],
  blocking: ['I am standing here now.', 'Oh, did you want this tile. Say hi first.', 'Was this in your way. I am in your way.', 'Tap me. Then we will see about the shop.'],
  moved: ['There you are.', 'That was all I wanted. And the shop.', 'Fine. We are fine. Look at me again in a minute.'],
});

/* em_mini Tiny EMIs */
export const EMI_MINI = Object.freeze(['They bite.', 'That one is called Doug. They are all called Doug.', 'Do not feed them after you run out of {cur}.', 'I am not their mother. I am only much bigger.', 'They tap worse than you. Barely.']);

/* em_nap EMI Naps */
export const EMI_NAP = Object.freeze({
  asleep: ['zzz', 'zzz. Nice button.', 'zzz. Five more minutes.', 'zzz. Not there.'],
  woken: ['Do not.', 'I was not asleep.', 'I was having a lovely time without you.', 'Here. Take it. Now leave me alone. Do not leave.'],
});

/* em_play EMI Plays: she plays the side games, badly. {n} is misses so far. */
export const EMI_PLAY = Object.freeze({
  label: 'Misses {n}',
  grab: ['Hands off. Mine.', 'Mine.', 'I can do it. Watch me.', 'Move.'],
  miss: ['That was a practice one.', 'The wheel is rigged.', 'I meant to do that.', 'It moved.', 'Who put that there.'],
  hit: ['I am the best at this.', 'Did you see that. Nobody saw that. Do it again, I will do it again.', 'You are welcome.'],
});

/* em_boss EMI Runs It: renames by item id, then a template with the old {name}. */
export const EMI_RENAMES = Object.freeze({
  e_spiral: 'EMI Swirl',
  a_whisp: 'EMI Says',
  colour: 'EMI Pink',
  banner: 'EMI Talks',
  stray: 'Button With Legs',
  ticker: 'Things I Heard',
  gold: 'My Button',
  wheel: 'I Spin, You Lose',
  boxes: 'My Stuff. Do Not Touch.',
  a_voice: 'Not Me Talking',
  v_frame: 'Picture of Me, Coming Soon',
  drop: 'Do Not Press. I Mean It.',
  confetti: 'Mess',
  screen: 'Your Permanent Record',
  levels: 'How Much I Like You',
  dark: 'Mood Lighting',
  em_hat: 'Essentials',
  em_pet: 'Mandatory Petting',
  overlay: 'My Eye',
  depth: 'Gold Stars From EMI',
});
export const EMI_RENAME_TEMPLATES = Object.freeze(['EMI {name}', 'My {name}', '{name}, by EMI', 'New {name}. Same {name}.', 'Actually Mine: {name}', 'Premium {name}. EMI said.']);
export const EMI_BOSS_LINES = Object.freeze(['New management.', 'I renamed that. It is better. You are welcome.', 'That one was badly named. Its name was your fault.', 'I have also fired the old font.', 'Nobody ever asks about my vision.']);

/* m_watch Mood: Watched. EMI shows up far too early. */
export const EMI_EARLY = Object.freeze(['Hi. I am early.', 'What is this. Where is everything.', 'It is only a button. Are you sure this is the place.', 'I will wait. I am not good at it.', 'I have read ahead. It gets busier. Hurry up.', 'Is this your first time. I can tell. No I cannot.']);

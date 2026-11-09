/* ============================================================================
 * data/content/thoughts.js - the Scrolling Thoughts ticker. Pure data.
 *
 * Half-finished thoughts that dissolve mid-sentence. No line follows from any
 * other, so they read fine in any order. General lines come in three stages:
 * early still has a person in it, mid is losing the thread, late has mostly
 * lost it. Route and theme lines mix in once that lane is picked.
 *
 * Tokens: {game} and {cur}, filled from data/brand.js.
 * ==========================================================================*/

export const THOUGHTS = Object.freeze({
  general: Object.freeze({
    early: Object.freeze([
      'I should reply to that message before I',
      'Rent is due on the, no, that was last',
      'Just one more and then I will get up and',
      'The laundry has been in the machine since',
      'Tomorrow I am going to be a person who',
      'There was a reason I opened this tab and it was',
      'What I should have said in that meeting was',
      'My posture is fine, I am sitting in the shape of a',
      'Did I lock the door. I remember the key and then the',
      'I could stop at any round number, such as',
      'Drink some water. Water is on the list, under',
      'I was about to look up how long an octopus',
      'This is a stupid game and I am about to',
      'I have a very normal amount of interest in this',
      'Someone asked me something earlier and I said',
      'My phone is somewhere near the',
      'It is only a button, I know it is only a',
      'I have views on tax policy, the main one is',
      'The number went up, which is good, because',
      'If anyone asks, I am reading about',
    ]),
    mid: Object.freeze([
      'I used to know a whole phone number by',
      'What is the word for when you, the, with the',
      'It is fine, everything is very, the thing is very',
      'I had a plan and it had three steps and the first',
      'The name of that actor, from the film with the',
      'Is it still today. There has been a lot of today',
      'Thinking is mostly a habit and I was',
      'Left, then right, then left, which one do I',
      'I agree with the button on most',
      'Someone should do something about the, it can wait, it',
      'Words are nice. Some of them are even',
      'I am somebody who has an emergency contact, I think it is',
      'Should I be worried. Worried is a word that starts with',
      'The thought was right here a second',
      'I keep meaning to have an opinion about',
      'Tap is a funny word if you say it until',
      'There was a second half to this',
      'My head is a nice place now that the furniture',
      'I was about to remember something and then',
      'What do I do for a, the thing, the place I go',
    ]),
    late: Object.freeze([
      'soft soft the',
      'and then the, and',
      'number good',
      'what was',
      '[[pink:pink]] is a kind of',
      'yes to the',
      'not thinking about not',
      'I think I think I',
      'the button is a',
      'mm',
      'where did the',
      'nice nice nice and the',
      'someone is tapping, oh',
      'it was a word, it had',
      'down is a direction I',
      'more, and',
      'was that me',
      'good, good, what',
      '{cur}',
      'and',
    ]),
  }),

  routes: Object.freeze({
    eyes: Object.freeze([
      'If I look at the spiral long enough it will tell me what',
      'My eyes have started turning the other way, which',
      'I can see that word even when I look at the',
      'I saw a corner get hit and I felt something like',
      'Blinking is optional, I read somewhere, or I watched',
    ]),
    ears: Object.freeze([
      'Did that voice just say my, no, it said',
      'I can hear the hum in my teeth and I do not',
      'Someone is whispering about me and I would like them to',
      'The headphones were a good idea, I had another idea but',
      'It is raining in my head and I left the',
    ]),
    sweet: Object.freeze([
      'Nothing is due. Nothing was ever',
      'I am doing so well at whatever it is I',
      'If I do not move I get paid, so my career is now',
      'It is warm in here and I did not ask who',
      'I will rest my hands for a moment and then I will rest',
    ]),
    strict: Object.freeze([
      'I tapped when I was told and it felt like a promotion',
      'Was that fast enough. It was not, I could tell by the',
      'I have started saying **yes** before the end of the',
      'They told me not to tap and I have never wanted anything so',
      'Three. Two. I do not know what happens at',
    ]),
    slides: Object.freeze([
      'That picture reminded me of something I am not going to',
      'I liked the last one better, but this one has a',
      'I have a wall now. I had a different wall, with a',
      'Just until the next one changes, and then just until the',
      'I chose these, apparently. Someone chose these, and it was',
    ]),
    scroll: Object.freeze([
      'One more and then I will, one more and then I will',
      'It knows what I like and I would like it to stop being',
      'I was at the end a moment ago, the bar said',
      'My thumb has gone on without me, and it seems to',
      'Why is it showing me that. I know why. It is because I',
    ]),
    blind: Object.freeze([
      'The frame is empty and I have already seen it twice and it was',
      'I liked that one, the one with the, there was nothing in it, but the',
      'I am filling this in myself, which says more about me than',
      'There was never a picture. I remember it clearly. It was',
      'That inkblot looked like my, well, they all look like my',
    ]),
    sell: Object.freeze([
      'I had a thought but I sold the space to',
      'I am being paid to look at this and I have never been so',
      'Skip in five, four, I could press skip but the money is in',
      'I think I need socks. I do not think I thought that',
      'Close it, close it, where has the little X gone, it was',
    ]),
    premium: Object.freeze([
      'I paid to have fewer ads and now there are only the',
      'I have a gold badge. It means I am a person who',
      'They thanked me for staying and I said you are',
      'The font is better. That was worth every last',
      'I went to cancel and the link had gone over to the',
    ]),
  }),

  themes: Object.freeze({
    trance: Object.freeze([
      'I am watching the pendulum, not for any reason, it just',
      'Ten. Nine. I lost my place, which is the place to',
      'Every breath takes me a little further from the name of the',
      'I can stop any time I want to and I do not seem to',
      'My hands are heavy and they are a long way from the',
      'Someone said **deeper** and I said which way is',
    ]),
    bimbo: Object.freeze([
      'I was good at something. It had numbers in it and a',
      'My IQ is going **down** and I feel like that is a',
      'Pretty is a whole job and I think I start on',
      'Reading is when the little shapes, oh, a sparkle',
      'Someone asked me a question so I smiled and that was',
      'Is that a lot. It looks like a lot. Hee, a lot is',
    ]),
    drone: Object.freeze([
      'Unit notes that it had a first name. Unit does not require the',
      'I, this unit, wishes to report a slight',
      'Input accepted. Input was a thought about lunch. Lunch is not',
      'Unit 7 has done more than this unit and this unit would like',
      'Status nominal. Mood not found. Searching for mood in',
      '**Shiny** is a directive and this unit has been',
    ]),
    sissy: Object.freeze([
      'Shoulders back, chin up, and what was the rule about the',
      'Does this match. It has to match. It nearly matches, so',
      'I said please to the button and the button said',
      'Everyone will be looking at the bow and I would like them to',
      'Lower, neater, slower, pinky out, and now I have forgotten',
      'I wonder if this is too much lace. I wonder that about once per',
    ]),
    goon: Object.freeze([
      'Five more minutes and then I will open the, what time is',
      'I have twelve tabs open and I am going to close them when',
      'The curtains are shut because of the glare from the, from outside, from',
      'Do not **let go** yet, not yet, not because of anything, only',
      'There are others online and we are all nodding at the',
      'I had a dinner planned for some earlier version of',
    ]),
    locked: Object.freeze([
      'It has been two days, or sixty, the timer says it depends on',
      'I asked nicely and got nothing, so I will ask more',
      'The key is somewhere safe, which is to say it is not',
      '**Not yet** is my favourite thing anyone has said to me, apart from',
      'I wonder what they would say if I begged again, I know what they',
      'It was almost zero. I saw it. I did not get my hopes up, I only',
    ]),
    censored: Object.freeze([
      'I almost saw it and that was the best ███ of my',
      'Something is behind that bar and I have decided it is',
      'I do not need to know how much, I only need to ███',
      'This is for my own good, they said, and then they ███',
      'I lifted the bar for a second and I think I regret the',
      'My favourite part was the part they took out of the ███',
    ]),
    infection: Object.freeze([
      'It is probably nothing, it was smaller yesterday and it was',
      'I feel a little warm, the page feels warm, one of us should',
      'I tried not to scratch at the, the antivirus says it found',
      'Do I send this to a friend. It would be rude not to',
      'Symptoms include all of these, and also the urge to',
      'The [[pink:pink]] is not spreading, I am just seeing it in more of the',
    ]),
  }),
});

/** Every line that fits this run: general up to the stage, plus chosen routes and themes. */
export function thoughtsFor({ stage = 'early', routes = [], themes = [] } = {}) {
  const g = THOUGHTS.general;
  const out = [...g.early];
  if (stage === 'mid' || stage === 'late') out.push(...g.mid);
  if (stage === 'late') out.push(...g.late);
  for (const r of routes) out.push(...(THOUGHTS.routes[r] || []));
  for (const t of themes) out.push(...(THOUGHTS.themes[t] || []));
  return out;
}

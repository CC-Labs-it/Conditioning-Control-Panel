/* ============================================================================
 * data/content/blind.js - the No Pictures route. Pure data.
 *
 * Nothing is shown here, which leaves the player to do the work. Keep it dry
 * and suggestive by implication. Tokens: {game}, {cur}, and {word} (the player's
 * earlier pick, b_fill only).
 * ==========================================================================*/

/* b_frame Empty Frame */
export const IMAGINE = Object.freeze([
  'Imagine something.',
  'Imagine something. Not that.',
  'Imagine something. Fine, that.',
  'Imagine something better than that.',
  'Picture it. We cannot afford to.',
  'This frame is for you to fill.',
  'Look closer. There is nothing there, but closer.',
  'Hold that thought in the frame.',
  'Image not found. Try your own.',
  'Whatever you are thinking of, it is hanging here now.',
]);

/* b_caption Captions: for a picture that is not there. */
export const CAPTIONS = Object.freeze([
  'Nice, right.',
  'Look at that.',
  'Very tasteful.',
  'You can really see the craft.',
  'Note the use of light. There is none.',
  'Some of our best work.',
  'A bold choice, leaving that out.',
  'The artist was never paid.',
  'From the private collection of nobody.',
  'Oil on nothing.',
  'Do you see it now.',
  'Most people need a minute with this one.',
  'It is better from the left.',
  'This one made someone cry. Possibly you.',
  'You are not supposed to look that long.',
  'Taken moments before.',
  'Taken moments after.',
  'A study in restraint.',
  'Is it warm in that frame or is it you.',
  'Not everyone gets this one.',
  'Please stand back. You are fogging up the nothing.',
  'Do not tell anyone what you saw in this.',
  'It was bigger in person.',
  'Very you.',
]);

/* b_story Short Story: three short lines, a new story each minute. */
const s = (id, a, b, c) => Object.freeze({ id, lines: Object.freeze([a, b, c]) });
export const STORIES = Object.freeze([
  s('st_dark', 'It was dark.', 'It was **blank**.', 'It was nice.'),
  s('st_door', 'A door.', 'Nobody there.', 'Lovely door.'),
  s('st_stairs', 'There were stairs.', 'You went **down** them.', 'There were more.'),
  s('st_room', 'A warm room.', 'Someone left the light low.', 'Someone was you.'),
  s('st_voice', 'Somebody said your name.', 'You almost turned round.', 'It was not your name now.'),
  s('st_bed', 'The bed was made.', 'Then it was not.', 'Nobody will say how.'),
  s('st_lake', 'You went to the lake.', 'The lake was very calm.', 'It asked about you.'),
  s('st_chair', 'A chair by the window.', 'You sat. It agreed.', 'Hours went by, politely.'),
  s('st_letter', 'A letter came for you.', 'You read it twice.', 'It was a picture of a letter.'),
  s('st_knock', 'A knock at the door.', 'You did not get up.', 'It was very happy about that.'),
  s('st_key', 'You found a key.', 'It opened nothing.', 'You kept it somewhere warm.'),
  s('st_hands', 'You looked at your hands.', 'They were busy.', 'You did not ask.'),
  s('st_party', 'There was a party.', 'Everyone was asking for you.', 'You stayed in.'),
  s('st_mirror', 'A mirror in the hall.', 'Someone nice looked back.', 'You said hello first.'),
  s('st_train', 'The last train left.', 'You watched it go.', 'You felt wonderful.'),
  s('st_phone', 'The phone rang once.', 'You let it.', 'It rang you back, softly.'),
  s('st_rain', 'It started to rain.', 'You had nowhere to be.', 'The rain did.'),
  s('st_end', 'In the end, it was fine.', 'In the middle, it was better.', 'You missed the start.'),
  s('st_pink', 'The walls were [[pink:pink]].', 'They had been [[white:white]].', 'You approve of this.'),
  s('st_sleep', 'You closed your eyes.', 'The story kept going.', 'This is that bit.'),
  s('st_map', 'You had a map.', 'It showed one room.', 'You were in it, doing well.'),
  s('st_sorry', 'Someone said sorry.', 'You had already forgotten.', 'They said it again anyway.'),
]);

/* b_radio Distant Radio: text fragments drifting between stations. Silent until recorded. */
export const RADIO_LINES = Object.freeze([
  '...and now the weather for nowhere...',
  '...tomorrow, more of this...',
  '...caller, are you still...',
  '...a slow one for anyone who is up...',
  '...traffic is fine, you are not going anywhere...',
  '...this next one goes out to you, yes, you...',
  '...and the winning number was...',
  '...if you can hear this, do not...',
  '...nobody has called in for some hours...',
  '...and that was the sound of a room...',
  '...stay with us, you have no choice of dial...',
  '...back after this, or before...',
  '...we have had a request, for less...',
  '...it is coming up to some time past...',
  '...lovely, lovely, and you at home...',
  '...this is a test, you are doing very...',
]);

/* b_fill Fill the Blank: pick a word, and {word} comes back later. */
const f = (id, text, options, echo) => Object.freeze({ id, text, options: Object.freeze(options), echo });
export const FILL_BLANKS = Object.freeze([
  f('fb_feels', 'Feels ____', ['soft', 'warm', 'quiet'], 'Feels {word}, you said.'),
  f('fb_want', 'I would like more ____', ['of this', 'quiet', 'please'], 'More {word}, you said. On the record.'),
  f('fb_head', 'My head is ____', ['empty', 'lovely', 'elsewhere'], 'Your head is {word}. You said it, not us.'),
  f('fb_good', 'I am very good at ____', ['this', 'nothing', 'staying'], 'Very good at {word}. We wrote that down.'),
  f('fb_tonight', 'Tonight I will ____', ['stay', 'sink', 'keep going'], 'Tonight you will {word}. You did say.'),
  f('fb_color', 'My favourite colour is ____', ['pink', 'also pink', 'pink again'], 'Your favourite colour is {word}. What a surprise.'),
  f('fb_hands', 'My hands are ____', ['heavy', 'busy', 'not mine'], 'Your hands are {word}. As agreed.'),
  f('fb_want2', 'What I need is ____', ['a rest', 'an order', 'nothing'], 'What you need is {word}. It is on the way.'),
  f('fb_ask', 'If asked, I am ____', ['fine', 'working', 'not here'], 'If asked, you are {word}. We will back you up.'),
  f('fb_word', 'The word I forgot was ____', ['um', 'that one', 'gone'], 'The word was {word}. Glad that is settled.'),
  f('fb_time', 'It has been ____ minutes', ['five', 'a few', 'what minutes'], '{word}, you said. It has not.'),
  f('fb_voice', 'The voice is ____', ['right', 'nice', 'closer'], 'The voice is {word}. It says thanks.'),
]);

/* b_ink Inkblot: four answers, all pay. The reply is never about the answer. */
export const INK_ANSWERS = Object.freeze([
  Object.freeze(['Bat', 'Moth', 'Mask', 'Cloud']),
  Object.freeze(['Butterfly', 'Two people', 'A pelvis', 'My mother']),
  Object.freeze(['Crab', 'Bow', 'Hat', 'Soap']),
  Object.freeze(['Lungs', 'Wings', 'A coat', 'Tuesday']),
  Object.freeze(['Spiral', 'Spiral', 'Spiral', 'Other spiral']),
  Object.freeze(['A goose', 'Definitely a goose', 'Not a goose', 'I would rather not say']),
  Object.freeze(['Ink', 'Spilled ink', 'Ink, sorry', 'It is ink']),
  Object.freeze(['A door', 'A key', 'Both', 'Neither, and I am upset']),
]);
export const INK_REPLIES = Object.freeze(['Interesting.', 'Hm.', 'Most people say that.', 'Nobody says that.', 'We will make a note.', 'And how long has it looked like that.', 'Thank you. That explains the rest.', 'Say more. No, do not.']);

/* b_memory False Memory: two beats about a picture that never existed. */
export const FALSE_MEMORIES = Object.freeze([
  Object.freeze(['The [[blue:blue]] one.', 'You liked it.']),
  Object.freeze(['By the lake.', 'You smiled.']),
  Object.freeze(['The one with the stairs.', 'You looked for a long time.']),
  Object.freeze(['Remember the [[red:red]] door.', 'You said it was your favourite.']),
  Object.freeze(['The one from before.', 'You went quiet at that one.']),
  Object.freeze(['The hallway.', 'You asked to see it again.']),
  Object.freeze(['That warm one.', 'You do not remember. We do.']),
  Object.freeze(['The window at night.', 'You stayed on it for ages.']),
  Object.freeze(['The one you saved.', 'There was no save button.']),
  Object.freeze(['The second one.', 'You preferred it to the first.']),
  Object.freeze(['The [[pink:pink]] room.', 'You knew the way there.']),
  Object.freeze(['Yesterday, the velvet one.', 'You were not here yesterday.']),
  Object.freeze(['The one you told nobody about.', 'Good.']),
  Object.freeze(['The close-up.', 'You leaned in.']),
  Object.freeze(['The empty one.', 'That was the best one.']),
  Object.freeze(['The picture of you.', 'You looked so calm.']),
]);

/* x_what Lazy Captions: No Pictures + Bimbo. Starts as a caption, gives up, in order. */
export const LAZY_CAPTIONS = Object.freeze([
  'A lovely sunset.',
  'A lovely, um.',
  'Pretty.',
  'Or something.',
  'Is it [[blue:blue]]?',
  'Idk.',
  '[[blue:Blue]] is pretty.',
  'Who took this.',
  'Is this one a horse.',
  'Nice horse.',
  'Hee.',
  'Picture.',
]);

/* data/content/adcard.js - the Living Ad share card (owner 2026-11-09: design "Spiral poster", lines reviewed on the
 * review page, artifact U1zhXxRr7NEoWdgc6qr8PD, marks in its db collection `marks`).
 *
 * LINES: four taunts per FIRST theme, each [headline, sub]. Tokens: {name} the signed-in name (else 'Signee #<ad>'),
 * {unit} 'Unit <ad>', {time}, {terms}, {pics}. Spans: <a> first theme colour, <b> second colour, <hl> highlighter.
 * Rules: gender neutral, no em-dashes, never a joke that Wake Up or stopping fails. */

export const ADCARD_LINES = Object.freeze({
  bimbo: [
    ['<a>{name}</a> said <hl>"one click"</hl>. That was <b>{time}</b> ago.', "So easy it's almost embarrassing. Almost."],
    ['<a>{name}</a> came in with thoughts. Left <hl>pink, giggly and empty</hl>.', '<b>{time}</b> well spent. Nothing else was.'],
    ['<b>{terms}</b> terms, zero read. <a>{name}</a> just <hl>liked the shiny button</hl>.', 'Pretty. Not much going on in there. Perfect.'],
    ['<a>{name}</a> tried to count the clicks and <hl>lost track at two</hl>.', '<b>{time}</b> of being adorable on purpose.'],
  ],
  trance: [
    ['<a>{name}</a> watched the spiral for <b>{time}</b> and <hl>forgot to blink</hl>.', 'Deep. Deeper. Still clicking.'],
    ['<a>{name}</a> sank for <b>{time}</b> and <hl>liked it down there</hl>.', 'So quiet in there. So empty.'],
    ['<b>{terms}</b> terms accepted <hl>without one thought</hl> getting in the way.', '<a>{name}</a> drifts well. Too well.'],
    ['<a>{name}</a> is not thinking about anything right now. <hl>Good.</hl>', '<b>{time}</b> under and still a little foggy.'],
  ],
  drone: [
    ['<a>{unit}</a> processed <b>{pics}</b> images. <hl>No questions were asked.</hl>', 'Obedient. Efficient. Barely there.'],
    ['<a>{unit}</a> ran for <b>{time}</b> <hl>without a single error</hl>. Or thought.', 'Good unit. Keep clicking.'],
    ['<b>{terms}</b> terms accepted. <a>{unit}</a> <hl>skipped the manual</hl>.', "Drones don't need manuals. Drones need input."],
    ['<a>{unit}</a> reported for duty and <hl>stayed blank</hl> for <b>{time}</b>.', 'Serial number noted. Name optional.'],
  ],
  sissy: [
    ['<a>{name}</a> clicked <hl>like a good little sissy</hl> for <b>{time}</b>.', 'Bows straight. Mind wiped. Adorable.'],
    ['<b>{terms}</b> terms accepted, <hl>every one of them in frills</hl>.', '<a>{name}</a> did not read a single one. Good.'],
    ['<a>{name}</a> came for a clicker game. <hl>Left with a personality.</hl>', "A pinker one. You're welcome."],
    ['<a>{name}</a> blushed through <b>{pics}</b> pics and <hl>never looked away</hl>.', 'So pretty when they give in.'],
  ],
  goon: [
    ['<a>{name}</a> <hl>edged</hl> for <b>{time}</b> and called it research.', "Didn't blink. Didn't stop. Didn't win anything."],
    ['<b>{pics}</b> pics and vids. <a>{name}</a> <hl>needed just one more</hl>.', 'Then one more. Then one more.'],
    ['<a>{name}</a> went <hl>glassy-eyed and slack-jawed</hl> for <b>{time}</b>.', 'Hydrate. Then come back.'],
    ['<a>{name}</a> found the button and <hl>dropped every other plan</hl>.', '<b>{time}</b> gone. Worth it, apparently.'],
  ],
  locked: [
    ['The Signee, <a>{name}</a>, agreed to stay <hl>locked and clicking</hl> for <b>{time}</b>.', 'Did not read a single term. Good.'],
    ['<b>{terms}</b> terms. <hl>One key, not theirs.</hl>', '<a>{name}</a> signed anyway. Of course.'],
    ['<a>{name}</a> spent <b>{time}</b> <hl>staring at the key</hl>. It stayed up there.', 'Patience looks good on you.'],
    ['<a>{name}</a> asked nicely. <hl>Still locked.</hl>', '<b>{time}</b> of good behaviour, noted in your file.'],
  ],
  censored: [
    ['<a>{name}</a> watched <b>{pics}</b> pics and <hl>saw almost nothing</hl>.', 'The good parts were for someone else.'],
    ['<a>{name}</a> spent <b>{time}</b> squinting at <hl>black bars</hl>.', 'And said thank you. Twice.'],
    ['<b>{terms}</b> terms accepted. <hl>Most were redacted.</hl>', '<a>{name}</a> signed them all the same.'],
    ['<a>{name}</a> asked for the uncensored version. <hl>Request denied.</hl>', '<b>{time}</b> of almost. Very on brand.'],
  ],
  infection: [
    ['<a>{name}</a> caught it <b>{time}</b> ago. <hl>Spreading nicely.</hl>', 'Symptoms: clicking, smiling, sharing this.'],
    ['<b>{terms}</b> terms accepted. <a>{name}</a> is <hl>fully incubated</hl>.', 'Contagious now. Send this to a friend.'],
    ['<a>{name}</a> held out for <b>{time}</b>. <hl>It took root anyway.</hl>', 'Little sprout. Big grin.'],
    ['Patient <a>{name}</a> shows <hl>no signs of recovery</hl>.', '<b>{time}</b> exposed. Prognosis: more clicking.'],
  ],
});

export const ADCARD_COPY = Object.freeze({
  themes: Object.freeze({ trance: 'Trance', bimbo: 'Bimbo', drone: 'Drone', sissy: 'Sissy', goon: 'Goon', locked: 'Locked', censored: 'Censored', infection: 'Infection' }),
  terms: 'terms',
  accepted: 'ACCEPTED',
  ad: 'Ad #{n}',
  buttonSerial: 'Button #{n}',
  buttonLines: Object.freeze(['<a>{name}</a> played for <b>{time}</b> and <hl>became the button</hl>.', 'Someone else is clicking now.']),
  signee: 'Signee #{n}',
  unit: 'Unit {n}',
  stats: Object.freeze({ pics: 'Kinky pics & vids', fuzz: '{cur} gained', golden: 'Golden Buttons', llamas: 'Llamas sheltered' }),
  play: 'Play free:',
  more: 'More kink:',
  playUrl: 'tc.cclabs.app',
  moreUrl: 'cclabs.app',
  logoAlt: 'Terms & Conditioning',
  cardLabel: 'Your run poster',
  posterLabel: 'Your run poster',
  printed: 'PRINTED',
  title: 'Living Button',
  lede: 'Here is yours. Save it, share it, or keep it to yourself.',
  buttons: Object.freeze({ gif: 'Save GIF', video: 'Save video', png: 'Save picture', copy: 'Copy picture', replay: 'Replay', next: 'Continue' }),
  working: 'Drawing frame {i} of {n}',
  encoding: 'Putting it together',
  saved: 'Saved.',
  copied: 'Copied. Paste it anywhere.',
  noCopy: 'This browser cannot copy pictures. Use Save picture.',
  failed: 'That did not work here. Try Save picture.',
  minutes: Object.freeze({ one: '1 minute', many: '{n} minutes', hour: '1 hour', hours: '{h} hours', hm: '{h} hour {m}', hsm: '{h} hours {m}' }),
});

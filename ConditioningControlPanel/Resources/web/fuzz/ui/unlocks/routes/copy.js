/* ui/unlocks/routes/copy.js - the few lines the choice routes needed that data/content does not have. Same rules: plain,
 * dry, short, no exclamation marks, tokens {cur} and {game} only. Everything else these lanes say comes from data/content. */

export const ROUTE_COPY = Object.freeze({
  /* Watch */
  corner: 'It hit the corner.',
  /* Listen */
  whispers: Object.freeze(['psst', 'shh']),
  volume: 'volume', far: 'far', close: 'close', left: 'left', right: 'right',
  finally: 'finally',
  onTick: 'on the tick +{n}',
  phonesTitle: 'Headphones',
  /* Rest */
  napTag: 'z', napTitle: 'Back', napOk: 'Good',
  /* Tap */
  refused: 'NO',
  notNow: 'Not now', space: 'space',
  fileOpened: 'A file has been opened on you.',
  gradeBoost: 'More pay for 30 s',
  /* Slideshow and Swipe Feed */
  gif: 'GIF', muted: 'muted',
  wakeStill: 'Wake Up still works, top right.',
  feedOpen: 'Press the feed to open it.',
  feedPress: 'press to open', feedSwipe: 'swipe up', feedBack: 'Back to the button',
  /* Screens */
  screenNone: 'Nothing chosen. Still paying.',
  screenNoGif: 'No GIFs here. Still paying.',
  screenNoClip: 'No videos here. Still paying.',
  screenAdd: 'Add your files',
  screenNotFiles: 'Those were not pictures, GIFs or videos.',
  screenDrag: 'drag me anywhere',
  screenPay: '+{n}/s',
  /* No Pictures */
  fillNote: 'Choose one. It pays either way.',
  inkTitle: 'What do you see',
  /* More Ads */
  adChip: 'AD',
  adNope: Object.freeze(['That did nothing.', 'The goods are on the way. They are not.', 'Thank you for your interest in that.']),
  skipIn: 'Skip in {n}', skipDone: 'Done', paidWait: 'Paid for waiting.',
  realBrand: 'CCP',
  next: 'Next',
  /* Fewer Ads */
  premiumChip: 'Premium',
  skip: 'Skip', skipGone: 'Skipped. That was the part that paid.', skipBack: 'A new ad to not skip.',
  thanks: 'Thank you', forEver: 'for ever',
});

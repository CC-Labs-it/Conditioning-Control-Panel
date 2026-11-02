/* ui/intro.js - what stands between a first visit and the button. One entry: runIntro() resolves when the player may
 * start. For now a plain adults card, remembered in life.age. A later lane replaces this whole file with the long joke
 * contract; nothing else knows what is in here, and the replacement must still ask the age question and remember it. */

export function runIntro({ cards, life, skip = false, leave }) {
  if (life.age || skip) return Promise.resolve();
  return new Promise((resolve) => {
    cards.open({
      title: 'Adults only',
      body: '{game} is a game about erotic hypnosis, for adults. Suggestive, not explicit. Tap a button, watch the page fill up.',
      guard: false,
      buttons: [
        { label: 'I am 18 or older', onPress: () => { life.age = true; resolve(); } },
        { label: 'Leave', keepOpen: true, onPress: leave },
      ],
    });
  });
}

// Account item ids are stable even if the renderer or its display names change.
export const FINISH_NAMES = Object.freeze({
  'chess.finish.polka': 'Candy lacquer',
  'chess.finish.marble': 'Mineral strata',
  'chess.finish.contour': 'Liquid ribbons',
  'chess.finish.opal': 'Oil slick',
  'chess.finish.kintsugi': 'Gold faultlines',
});

export const FINISH_IDS = Object.freeze(Object.keys(FINISH_NAMES));

export function finishKey(itemId) {
  return typeof itemId === 'string' && Object.hasOwn(FINISH_NAMES, itemId)
    ? itemId.slice('chess.finish.'.length)
    : null;
}

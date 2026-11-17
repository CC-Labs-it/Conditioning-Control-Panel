export const BOARD_THEME_NAMES = Object.freeze({
  'chess.board.observatory': 'Midnight Observatory',
  'chess.board.conservatory': 'Glasshouse',
  'chess.board.theatre': 'Velvet Theatre',
  'chess.board.arcade': 'After Hours Arcade',
});

export const BOARD_THEME_IDS = Object.freeze(Object.keys(BOARD_THEME_NAMES));

export function boardThemeKey(id) {
  if (typeof id !== 'string') return null;
  const item = id.startsWith('chess.board.') ? id : 'chess.board.' + id;
  return Object.hasOwn(BOARD_THEME_NAMES, item) ? item.slice('chess.board.'.length) : null;
}

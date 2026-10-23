/* ============================================================================
 * core/dayclock.js - ONE CLOCK FOR "HAVE I ALREADY PLAYED TODAY'S CLASS" (PURE).
 *
 * The day's script (the seed, Daily Trigger's word) and the XP ledger the host
 * keeps (`xpPaidDays`) both roll at UTC midnight. The page's day rows are filed
 * under the LOCAL date (attendance, the board), so a retake check that read
 * only the local row disagreed with the credit side for the hours between the
 * two midnights: a player east of UTC got a fresh graded run of a script the
 * host had already paid, and a player west of UTC was told "Retake" on a new
 * script the host was about to pay (ticket 1557537313851056138).
 *
 * So "already played" is asked on the UTC day, every time:
 *   - a graded row counts when it was WRITTEN on that UTC day (rows carry `at`,
 *     ms since epoch); a row with no `at` (older pages) falls back to the old
 *     local-day reading, so nothing that used to say Retake stops saying it;
 *   - an ATTEMPT counts too (ccp-bugs #1455): a game that stamps
 *     `lastPlayedUtc` on its own meta when the class STARTS (Daily Trigger)
 *     or the host's XP ledger naming the game for that UTC day. Leaving a
 *     class with Esc writes no row, so without the stamp the next run was a
 *     first run again, as often as the player liked.
 *
 * No DOM, no store, no clock of its own: every input is handed in, so node can
 * check it (games/daily-trigger/smoke/one-try-check.mjs).
 * ==========================================================================*/

/** 'YYYY-MM-DD' (UTC) of an epoch-ms stamp, or '' when it is not one. */
export function utcDayOf(ms) {
  const n = Number(ms);
  if (!Number.isFinite(n) || n <= 0) return '';
  try { return new Date(n).toISOString().slice(0, 10); } catch (e) { return ''; }
}

/**
 * The graded row (if any) that belongs to `utcDay`.
 * @param {Array<?Object>} rows  candidate rows, the local day's own first
 *                               (the neighbouring local days may hold a row
 *                               written on the same UTC day)
 * @param {string} utcDay
 * @returns {?Object} the row, or null
 */
export function rowForUtcDay(rows, utcDay) {
  const list = Array.isArray(rows) ? rows : [];
  for (let i = 0; i < list.length; i++) {
    const row = list[i];
    if (!row || !row.grade) continue;
    const day = utcDayOf(row.at);
    if (day) { if (day === utcDay) return row; continue; }
    // No stamp: only the local day's own row (index 0) can speak, as before.
    if (i === 0) return row;
  }
  return null;
}

/**
 * True when the class already had its one try on `utcDay`, graded or not.
 * @param {{gameKey:string, utcDay:string, gameMeta:?Object, xpPaidDays:?Object}} o
 */
export function attemptedOnUtcDay(o) {
  const gameKey = String((o && o.gameKey) || '');
  const utcDay = String((o && o.utcDay) || '');
  if (!gameKey || !utcDay) return false;
  const meta = (o && o.gameMeta) || null;
  if (meta && typeof meta === 'object' && String(meta.lastPlayedUtc || '') === utcDay) return true;
  const paid = (o && o.xpPaidDays) || null;
  const day = paid && typeof paid === 'object' ? paid[utcDay] : null;
  return Array.isArray(day) && day.some((k) => String(k) === gameKey);
}

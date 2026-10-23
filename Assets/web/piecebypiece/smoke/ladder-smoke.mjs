/* ============================================================================
 * smoke/ladder-smoke.mjs - the rating ladder, in node, no browser:
 *
 *   node smoke/ladder-smoke.mjs
 *
 * The wire: GET ladder rides the PUBLIC road (no identity in the path), GET
 * ladder/me the account one. The words: chip, rank line, your own row. The
 * end card's plan: +N green / -N red, the roll's numbers, "new best" only on
 * a real personal best, the delta recovered from two reads when the ending
 * came without it, and the line an unrated game gets. The screen: a list of
 * names with yours marked, never anything but names and numbers.
 * ==========================================================================*/

import * as api from '../net/api.js';
import { normMe, normTop, chipText, rankText, markMine, endPlan, rollValue, tickTimes, LADDER_TUNING,
  BOARD_TUNING, podiumOf, podiumAt, rowAt, youRoll } from '../ui/ladder-plan.js';
import { createLadder } from '../ui/ladder.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));

/* ---------------------------------------------------------------- the wire */
{
  const pub = [], priv = [];
  api.setPublicTransport(async (m, p) => { pub.push({ m, p }); return { status: 200, body: JSON.stringify({ ok: true, rows: [], total: 0 }) }; });
  api.setTransport(async (m, p) => { priv.push({ m, p }); return { status: 200, body: JSON.stringify({ ok: true, rated: false, rating: null, rank: null, games: 0, best: null, total: 0 }) }; });
  const top = await api.ladder();
  const me = await api.ladderMe();
  eq('ladder is a GET on the public road', pub.map((x) => x.m + ' ' + x.p), ['GET /v2/pbp/ladder']);
  ok('ladder/me goes the account road', priv.length === 1 && priv[0].p.startsWith('/v2/pbp/ladder/me'));
  ok('both answer an ok envelope', top.ok && me.ok && me.data.rated === false);
  api.setPublicTransport(null); api.setTransport(null);
}

/* ---------------------------------------------------------------- the words */
{
  const me = normMe({ ok: true, rated: true, rating: 1248.4, rank: 17, games: 9, best: 1262, total: 312 });
  eq('normMe rounds and keeps the fields', me, { rated: true, rating: 1248, rank: 17, games: 9, best: 1262, total: 312 });
  eq('chip says the rating', chipText(me), '1248');
  eq('rank line', rankText(me), 'rank 17 of 312');
  const un = normMe({ rated: false, rating: null, games: 0, total: 5 });
  eq('unrated chip', chipText(un), 'unrated');
  eq('no rank line unrated', rankText(un), '');
  eq('nothing before the server answers', chipText(null), '');
  ok('best is never below the rating', normMe({ rated: true, rating: 1300, best: 1250 }).best === 1300);

  const top = normTop({ rows: [{ rank: 1, display_name: 'Rose', rating: 1488, games: 41 }, { rank: 1, display_name: 'Ivy', rating: 1488, games: 12 },
    { rank: 3, display_name: 'Mo', rating: 1248, games: 9 }, { rank: null, display_name: 'bad' }], total: 312 });
  eq('normTop keeps good rows and the total', [top.rows.length, top.total, top.rows[1].name], [3, 312, 'Ivy']);
  const marked = markMine(top.rows, { ...me, rank: 3 }, 'mo');
  eq('your row is marked by rank, rating and name', marked.map((r) => r.mine), [false, false, true]);
  eq('a name that disagrees is not yours', markMine(top.rows, { ...me, rank: 3 }, 'someone').map((r) => r.mine), [false, false, false]);
}

/* ---------------------------------------------------------------- the end plan */
{
  const before = normMe({ rated: true, rating: 1236, rank: 18, games: 8, best: 1240, total: 312 });
  const after = normMe({ rated: true, rating: 1248, rank: 17, games: 9, best: 1248, total: 312 });
  const up = endPlan({ delta: 12, before, after, plies: 40 });
  eq('a win rolls 1236 to 1248 and stamps +12', [up.from, up.to, up.stamp, up.tone], [1236, 1248, '+12', 'up']);
  ok('beating the old best (1240) is a new best', up.newBest === true);
  eq('the rank line', up.note, 'rank 18 to 17');

  const small = endPlan({ delta: 2, before: normMe({ rated: true, rating: 1230, best: 1300, rank: 20 }), after: normMe({ rated: true, rating: 1232, best: 1300, rank: 20 }) });
  ok('a win under your best is not a new best', small.newBest === false && small.note === 'rank 20');

  const down = endPlan({ delta: -11, before, after: normMe({ rated: true, rating: 1225, rank: 21, games: 9, best: 1240 }) });
  eq('a loss stamps -11 in the down tone', [down.from, down.to, down.stamp, down.tone, down.newBest], [1236, 1225, '-11', 'down', false]);

  const lost = endPlan({ delta: 12, before: null, after: null });
  eq('no reads at all: from the 1200 start', [lost.from, lost.to, lost.newBest], [1200, 1212, true]);

  const recovered = endPlan({ delta: null, before, after });
  ok('an ending without the delta takes it from the two reads', recovered.rated && recovered.delta === 12);
  ok('two reads with no game between them are not a delta', endPlan({ delta: null, before, after: before }).rated === false);

  eq('a short game says so', endPlan({ delta: null, plies: 6 }).note, 'too short to rate');
  eq('an abandon says so', endPlan({ delta: null, plies: 40, reason: 'abandon' }).note, 'abandoned games are not rated');
  eq('a draw at even ratings is +0, flat', [endPlan({ delta: 0, before, after: before }).stamp, endPlan({ delta: 0 }).tone], ['+0', 'flat']);

  eq('the roll starts and lands on the numbers', [rollValue(1236, 1248, 0), rollValue(1236, 1248, 1)], [1236, 1248]);
  ok('the roll eases out (past halfway at t=0.3)', rollValue(0, 100, 0.3) > 50);
  ok('ticks are capped', tickTimes(1200, 1300).length === LADDER_TUNING.rollTicks && tickTimes(1200, 1202).length === 2 && tickTimes(5, 5).length === 0);
}

/* ---------------------------------------------------------------- the screen */
{
  const lad = createLadder({
    fetchTop: async () => ({ ok: true, data: { rows: [{ rank: 1, display_name: '<b>Rose</b>', rating: 1488, games: 1 }, { rank: 2, display_name: 'Mo', rating: 1248, games: 9 }], total: 2 } }),
    fetchMe: async () => ({ ok: true, data: { rated: true, rating: 1248, rank: 2, games: 9, best: 1262, total: 2 } }),
  });
  const before = lad.screenHtml('Mo');
  ok('before the read: a waiting line', before.includes('reading the ladder'));
  await lad.loadTop();
  await lad.refreshMe();
  const html = lad.screenHtml('Mo');
  ok('names are escaped', html.includes('&lt;b&gt;Rose&lt;/b&gt;') && !html.includes('<b>Rose</b>'));
  ok('your place is marked (on the podium here)', /class="ladder-step p2 mine"[^]*?Mo/.test(html));
  ok('your stats: rating, rank, best', html.includes('>1248<') && html.includes('rank 2 of 2') && html.includes('best 1262'));
  ok('a real Back button, no "esc" small print', html.includes('data-back') && !/esc\s*-/i.test(html));
  ok('the top three stand on a podium', (html.match(/class="ladder-step /g) || []).length === 2 && !html.includes('ladder-list'));
  ok('one game reads singular', html.includes('1 game<'));
  ok('the chip carries the rating', lad.chipHtml().includes('>1248<'));
  ok('snapshot is a copy', lad.snapshot() !== lad.me() && lad.snapshot().rating === 1248);

  const off = createLadder({ fetchTop: async () => ({ ok: false, status: 0, error: 'offline' }), fetchMe: async () => ({ ok: false }) });
  await off.loadTop();
  ok('offline says so in one line', off.screenHtml('x').includes('out of reach'));
  const empty = createLadder({ fetchTop: async () => ({ ok: true, data: { rows: [], total: 0 } }), fetchMe: async () => ({ ok: false }) });
  await empty.loadTop();
  ok('an empty ladder invites', empty.screenHtml('x').includes('nobody rated yet'));
  ok('no copy shouts', !/[!]/.test(html + off.screenHtml('x') + empty.screenHtml('x')));
}

/* ---------------------------------------------------------------- the board */
{
  const rows = Array.from({ length: 12 }, (_, i) => ({ rank: i + 1, name: 'p' + (i + 1), rating: 1400 - i * 10, games: 3 }));
  const { podium, rest } = podiumOf(rows);
  eq('podium stands 2nd, 1st, 3rd', podium.map((r) => r.place), [2, 1, 3]);
  eq('the rest start at rank 4', [rest.length, rest[0].rank], [9, 4]);
  eq('a ladder of two has a podium of two', podiumOf(rows.slice(0, 2)).podium.map((r) => r.place), [2, 1]);
  ok('third rises first, the winner last', podiumAt(3) < podiumAt(2) && podiumAt(2) < podiumAt(1));
  ok('rows land behind the podium, in order, capped', rowAt(0) >= podiumAt(1) && rowAt(1) > rowAt(0) && rowAt(40) === rowAt(BOARD_TUNING.rowCap));
  eq('your card rolls up from the start the first time', youRoll(normMe({ rated: true, rating: 1248 })), { from: LADDER_TUNING.start, to: 1248 });
  eq('and from what was last shown after that', youRoll(normMe({ rated: true, rating: 1248 }), 1236), { from: 1236, to: 1248 });
  ok('nothing to roll when unchanged or unrated', youRoll(normMe({ rated: true, rating: 1248 }), 1248) === null && youRoll(normMe({ rated: false })) === null && youRoll(null) === null);

  // rated but out of the top: your row is pinned under the list
  const lad = createLadder({
    fetchTop: async () => ({ ok: true, data: { rows: rows.map((r) => ({ ...r, display_name: r.name })), total: 300 } }),
    fetchMe: async () => ({ ok: true, data: { rated: true, rating: 1201, rank: 117, games: 4, best: 1210, total: 300 } }),
  });
  await lad.loadTop(); await lad.refreshMe();
  const html = lad.screenHtml('Mo');
  ok('out of the top: your row is pinned under the list', /ladder-pinned[^]*ladder-row mine[^]*>117<[^]*Mo/.test(html));
  ok('the list starts at 4 and counts the whole ladder', html.includes('start="4"') && html.includes('top 12 of 300'));
  const un = createLadder({ fetchTop: async () => ({ ok: true, data: { rows: [], total: 0 } }), fetchMe: async () => ({ ok: true, data: { rated: false, games: 0 } }) });
  await un.refreshMe();
  ok('unrated: a stamp and the one line on how', /ladder-you-stamp">unrated</.test(un.screenHtml('x')) && un.screenHtml('x').includes('5 moves each'));
}

if (failures.length) {
  console.error(`ladder-smoke: ${failures.length} FAILED, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`ladder-smoke: ${passed} passed`);

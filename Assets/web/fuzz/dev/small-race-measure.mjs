// node dev/small-race-measure.mjs [--all]: the small Journey's length on the six reference routes (editions-balance
// test) or on every route, plus the minute the Racing Thoughts mini lands and how many race ranks a run buys.
// Lane R (handoff-1124/tc-small-funnel/RACE-BRIEF.md): run before and after mini_race joins the small edition.
import { measureJourney } from './journey-timing.mjs';
import { allRoutes } from '../sim/route.js';
import * as Ed from '../ui/edition.js';

const small = Ed.readEdition({ caps: { edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
const all = process.argv.includes('--all');
const routes = allRoutes().map((r, i) => [r, i]).filter(([, i]) => all || [0, 39, 84, 125, 159, 190].includes(i));
const mins = [];
let raceAt = [], raceRanks = [];
for (const [r, i] of routes) {
  const buys = [];
  const res = measureJourney(r, { taps: 4, share: .125, option: i % 2, ed: small, trace: (e) => buys.push(e) });
  mins.push(res.minutes);
  const race = buys.find((e) => e.type === 'buy' && e.id === 'mini_race');
  raceAt.push(race ? race.at / 60 : null);
  raceRanks.push(buys.filter((e) => e.type === 'buy' && String(e.id).startsWith('rank_mini_race')).length);
  if (!all) console.log(`route ${i}: ${res.minutes.toFixed(2)} min, ${res.buys} buys, max wait ${res.max.toFixed(1)} s, race ${race ? (race.at / 60).toFixed(1) + ' min' : 'none'}, race ranks ${raceRanks.at(-1)}, ended ${res.s.ended}`);
}
const lo = Math.min(...mins), hi = Math.max(...mins), mean = mins.reduce((a, b) => a + b, 0) / mins.length;
console.log(`${routes.length} routes: ${lo.toFixed(2)} - ${hi.toFixed(2)} min, mean ${mean.toFixed(2)}; race bought on ${raceAt.filter((x) => x != null).length}`);

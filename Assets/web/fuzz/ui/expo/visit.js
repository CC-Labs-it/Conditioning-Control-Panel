import { visitTicket } from './ticket.js';
import { acceptedTerms } from '../unlocks/fate/museum/terms.js';
import { CLAUSES } from '../../data/content/clauses.js';
import { herdRoster } from '../../data/content/llamas.js';

const count = value => Math.max(0, Math.floor(Number(value) || 0));

export function admission(ctx) {
  const run = ctx.state || {};
  const clauses = acceptedTerms(run).map(id => {
    const fields = /^term-(.+)-(\d+)$/.exec(id);
    return fields && CLAUSES[fields[1]]?.[Number(fields[2])-1];
  }).filter(Boolean);
  return {
    taps: count(run.taps), total: count(run.total),
    name: ctx.identity?.displayName?.trim() || 'Signee #'+(1000 + count(run.seed)%9000),
    herd: herdRoster(ctx.life?.llamas ?? run.herd).map(llama => llama.id), clauses,
  };
}

export function createVisit(ctx) {
  const file = admission(ctx);
  return {
    file, ticket:visitTicket(ctx.store), presses:0, misses:0, hoof:'idle', hoofWorn:false, hoofMask:0, hoofAdmitted:false, taps:0, feeds:0, clause:0,
    contract:false, boughtExit:false, turnstile:false, timer:0, photo:false,
    heard:new Set(), habitatTime:0,
    pressRecord() {
      const extra = this.presses >= 10 ? '\nand '+this.presses.toLocaleString()+' times at the Expo. As predicted.' : '';
      return 'Pressed '+file.taps.toLocaleString()+' times by one human, 2026. Believed voluntary.'+extra;
    },
    glass() { this.taps++; return ({1:'keep_2',3:'keep_3',10:'keep_4'})[this.taps]; },
    nextClause() {
      if (!file.clauses.length) return 'No accepted clauses on file.';
      return file.clauses[this.clause++ % file.clauses.length];
    }
  };
}

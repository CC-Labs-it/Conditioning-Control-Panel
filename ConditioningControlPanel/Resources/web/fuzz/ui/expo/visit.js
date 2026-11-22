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
    file, ticket:visitTicket(ctx.store), presses:0, misses:0, hoof:'idle', hoofTime:0, taps:0, feeds:0, clause:0,
    contract:false, loops:0, boughtExit:false, turnstile:false, timer:0, photo:false,
    heard:new Set(), habitatTime:0,
    press(stop=false) {
      if (stop && this.presses < 50) return null;
      this.presses++;
      if (stop) return 'pa_hall1_stop';
      return this.presses === 1 ? 'pa_hall1_press' : this.presses === 10 ? 'pa_hall1_ten' : null;
    },
    pressRecord() {
      const extra = this.presses >= 10 ? '\nand '+this.presses.toLocaleString()+' times at the Expo. As predicted.' : '';
      return 'Pressed '+file.taps.toLocaleString()+' times by one human, 2026. Believed voluntary.'+extra;
    },
    useHoof() {
      if (this.hoof === 'idle' || this.hoof === 'complete') return {page:'checkbox',misses:0,lines:['hoof_1','hoof_2']};
      if (this.hoof === 'checkbox') {
        const misses=this.misses+1;
        if(misses===5)return {page:'change',misses,lines:['hoof_4']};
        return {page:'checkbox',misses,lines:misses===1?['hoof_3']:[]};
      }
      if (this.hoof === 'pictures') return {page:'failed',lines:['hoof_6']};
      return null;
    },
    finishHoof(step) {
      this.hoof=step.page;
      if(step.misses!==undefined)this.misses=step.misses;
      this.hoofTime=0;
    },
    tickHoof(dt) {
      this.hoofTime+=dt;
      if (this.hoof==='change' && this.hoofTime>=2.6)return {page:'pictures',lines:['hoof_5']};
      if (this.hoof==='failed' && this.hoofTime>=2.7)return {page:'assistant',lines:['hoof_7']};
      if (this.hoof==='assistant' && this.hoofTime>=3.3)return {page:'complete',lines:['hoof_8']};
      return null;
    },
    glass() { this.taps++; return ({1:'keep_2',3:'keep_3',10:'keep_4'})[this.taps]; },
    nextClause() {
      if (!file.clauses.length) return 'No accepted clauses on file.';
      return file.clauses[this.clause++ % file.clauses.length];
    },
    shopReturn() { this.loops = Math.min(2,this.loops+1); return this.loops===1 ? 'pa_shop_2' : 'pa_shop_3'; }
  };
}

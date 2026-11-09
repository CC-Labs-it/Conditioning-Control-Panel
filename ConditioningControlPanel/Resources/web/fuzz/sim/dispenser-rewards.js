// One saved outcome per accepted crank. Cosmetics never reroll a reward.
export const PRIZES = Object.freeze({
  red: Object.freeze({label:'Flashes',weight:45,multiplier:1,color:'#ff4975',accent:'#ffb2ce'}),
  cyan: Object.freeze({label:'Subliminals',weight:45,multiplier:1,color:'#44e7ff',accent:'#c5fbff'}),
  purple: Object.freeze({label:'Mind Drain',weight:8,multiplier:4,color:'#b374ff',accent:'#e1bdff'}),
  gold: Object.freeze({label:'Jackpot',weight:2,multiplier:10,color:'#ffd34e',accent:'#fff4be'}),
});
export const prizeFor = color => PRIZES[color] || PRIZES.red;
export function rollPrize(rng) {
  let n=Math.max(0,Math.min(.999999999,rng()))*100;
  for(const [color,p] of Object.entries(PRIZES)){n-=p.weight;if(n<0)return color;}
  return 'red';
}
export function prizePay(color,rates){return Math.max(rates.fps*12,rates.click*24)*prizeFor(color).multiplier;}

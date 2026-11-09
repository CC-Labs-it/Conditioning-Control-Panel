import { drawHarp } from './kinetic-harp.js';
import { drawChroma, drawSiri, drawPyramids } from './kinetic-references.js';

export const KINETIC_STUDIES = Object.freeze([
  { id:'chroma', name:'Chroma Cycle', price:15000, description:'Each tap sends a ripple through the blue bars. Faster taps build taller crests.', artist:'Chris Gannon', source:'https://codepen.io/chrisgannon/pen/vYaGJgM' },
  { id:'siri9', name:'iOS 9 SiriWave', price:6000, description:'Each tap swells a color. Overlapping crests mix into light.', artist:'Flavio De Stefano', source:'https://kopiro.github.io/siriwave/' },
  { id:'harp', name:'Resonance Harp', price:9000, description:'Pluck the three strings to send pearl trails racing along them.' },
  { id:'pyramid', name:'Swimming Pyramid', price:18000, description:'The tapped link grows first. Its neighbors lift, turn, and settle in sequence.', artist:'Misha Tsankashvili', source:'https://dribbble.com/shots/3441166-Swimming-pyramid' },
]);
const painters = new Map([['chroma',drawChroma],['siri9',drawSiri],['harp',drawHarp],['pyramid',drawPyramids]]);
export const isKineticWave = id => painters.has(id);
export const IMPULSE_LIFE = 3.8;
const TAU = 2*Math.PI;
const clamp = (v,a,b) => Math.max(a,Math.min(b,v));

export function waveEnergy(events, time) {
  let load = 0;
  for (const hit of events) {
    const age = time-hit.time;
    if (age>=0 && age<IMPULSE_LIFE) load += Math.exp(-age/.95) * Math.min(1,(IMPULSE_LIFE-age)*2);
  }
  return 1 - Math.exp(-.38*load);
}

export function addImpulse(events,time,u=.5,random=Math.random,contactTime=time) {
  while(events.length && time-events[0].time>=IMPULSE_LIFE) events.shift();
  const heat = waveEnergy(events,time);
  const previous = events.at(-1);
  const interval = previous ? Math.max(0,contactTime - previous.contactTime) : null;
  // Record the pace when each crest is launched so later clicks never resize
  // a wave already travelling down the ribbon.
  const priorGap = previous?.interval;
  const averageGap = priorGap > 0 && priorGap < .6 ? .65 * interval + .35 * priorGap : interval;
  const measuredRate = interval !== null && interval < .6 ? 1 / Math.max(.001, averageGap) : 0;
  const previousRate = previous?.tapRate ?? 0;
  const tapRate = measuredRate === 0 ? 0 : previousRate + (measuredRate - previousRate) * (measuredRate > previousRate ? .4 : .75);
  if(events.length>=16) events.shift();
  // Sample once on contact. A held frame, a repaint, and a faster display
  // all see the same phase, strength and direction for this impulse.
  events.push({time,contactTime,u:clamp(u,0,1),interval,tapRate,seed:random(),power:.48+.73*heat+.16*random(),
    speed:.75+.55*random(),polarity:random()<.5?-1:1});
}

// Measure the player's input between paints. The animation clock still
// controls propagation and freezes when motion is paused.
export function addWavePress(events,time,u=.5) {
  addImpulse(events,time,u,Math.random,performance.now()/1000);
}

export function drawKineticWave(g,w,h,{style,time,hits,wide=1,split=false,preview=false}) {
  g.clearRect(0,0,w,h);
  const paint=painters.get(style);
  if(!paint || w<=0 || h<=0)return;
  const detail=clamp(preview?h/107:Math.min(w/625,1.15),.4,1.16);
  const amplitude=preview?Math.min(h*.063,7.5):8*(1+.25*(wide-1));
  const period=clamp(w*.68,320,560);
  const axis=x=>h*.5+amplitude*Math.sin(TAU*x/period-time*.38);
  const ranges=split?[[0,w*215/480],[w*265/480,w]]:[[0,w]];
  const scene={w,h,detail,time,axis,ranges,events:hits,energy:waveEnergy(hits,time)};
  g.save();
  g.beginPath();
  for (const [left,right] of ranges) g.rect(left,0,right-left,h);
  g.clip();
  paint(g,scene);
  g.restore();
}

/* Horizontal ironwork on open Keep decks; the serpentine stays the permanent bypass. */
import { getKeepLayout } from '../keep-layout.js';

export function movingStations(track) {
  if (track.id !== 'keep') return { traps: [], bridge: null };
  const layout = getKeepLayout(track), landmark = id => layout.landmarks.find(row => row.id === id);
  const stations = [
    ['tower-bridge', 25, 'piston', 1],
    ['east-battlement', 35, 'slider', -1],
    ['crown-walk', 35, 'piston', -1],
    ['north-approach', 220, 'slider', 1],
    ['outer-rampart', 35, 'piston', 1],
    ['outer-rampart', 106, 'slider', -1],
  ];
  const traps = stations.map(([name, ds, kind, side], i) => {
    const district = landmark(name), target = district.s0+ds;
    for (let radius=0;radius<=80;radius+=2) for (const sign of radius?[1,-1]:[1]) {
      const s=target+radius*sign, frame=track.frameAt(s), x=side*Math.min(9,frame.w/2-5);
      if (s<district.s0+14 || s>district.s1-14 || frame.flags.includes('jump') || frame.n[1]<.99
        || frame.w/2-Math.abs(x)-2.5<1 || frame.w/2+Math.abs(x)-2.5<4) continue;
      if (track.aerials.some(a=>s>a.rampS0-20&&s<a.landingS1+20)
        || track.boosts.some(b=>Math.abs(s-b.s)<b.len/2+12)
        || [...layout.gates,...layout.chains].some(g=>Math.abs(s-g.s)<30)
        || track.courts.some(c=>c.holes.some(h=>s+12>h.s&&s-12<h.s+h.len))) continue;
      const q=track.query(...frame.p,frame.seg);
      if (!q.onRoad || Math.abs(q.height)>.05) continue;
      return { id:'keep-iron-'+(i+1),s,kind,side,w:5,len:4,x,periodBeats:2,offsetBeats:i%2 };
    }
    throw new Error('No clear Keep ironwork station in '+name);
  });

  // The chord passes below the raised serpentine, saving 43 m of the open main road.
  // Tangent handles make both mouths continuous with the permanent route.
  const from = landmark('serpentine-in').node, to = landmark('serpentine-out').to;
  const first = track.frameAt(track.nodeS[from]), last = track.frameAt(track.nodeS[to]);
  const points = [first.p, first.p.map((v, j) => v + first.t[j] * 25),
    last.p.map((v, j) => v - last.t[j] * 25), last.p]
    .map(p => ({ p: [...p], w: 8, roll: 0, surface: 'glass', flags: ['open'] }));
  const fork = { id: 'keep-piston-bridge', label: 'Piston bridge', kind: 'narrow', from, to, points };
  return { traps, bridge: { forkId: fork.id, fork, fromU: .3, toU: .65,
    periodS: 14, openS: 8, transitionS: .8 } };
}

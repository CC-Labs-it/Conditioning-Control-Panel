// Physical motion drives spray density and wake pressure, independent of frame rate.
const clamp = (x, low, high) => Math.max(low, Math.min(high, x));
export function waterResponse({ speed = 0, lateral = 0, steer = 0, drifting = false, previousSpeed = speed, dt = 1/60 } = {}) {
  const pace = clamp(speed/36, 0, 1.25), turn = clamp(Math.abs(steer)+Math.abs(lateral)/7, 0, 1);
  const brake = clamp((previousSpeed-speed)/Math.max(1/240,dt)/24, 0, 1);
  return { turn, brake, interval: clamp(.21-pace*.13-turn*.025-(drifting?.05:0), .045, .21),
    count: Math.round(2+pace*10+turn*8+(drifting?7:0)+brake*7),
    strength: clamp(.06+pace*.43+turn*.2+brake*.18, .06, .8),
    lift: .7+pace*1.3+turn*2.2+brake*1.4+(drifting?.7:0),
    size: .026+Math.min(1,pace)*.018+(drifting?.008:0),
    pressure: side => Math.abs(lateral)>1 ? (side*lateral>0?1.45:.65) : 1 };
}

/* render/waterfall.js - the chute's waterfall sheet, its spray and the splash ring (water wave 1).
 *
 * The sheet: one translucent strip over every chute run (stream nodes steeper than WATER_LOOK.chuteGradeDeg,
 * render/waterMath.js sheetRuns), floated sheetLiftM over the road and sheetOverM past its edges, running
 * sheetRunM past the lip and the foot. One geometry, one material, one draw. Its streak field scrolls down
 * the slope at sheetSpeedMps (the clock is the caller's: Gentle halves it). Colour from the section
 * palette plus white streaks; its alpha fades in at the lip and out at the foot and the sides.
 * Contacts displace the persistent fluid field and shed pooled ballistic droplets and curved fan sheets.
 * The reactive surface owns wet chute runs so an old flat overlay cannot cover its troughs.
 */
import { Group, Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, DoubleSide, Vector3 } from 'three/webgpu';
import { uniform, uv, float, vec3, floor, fract, hash, pow, smoothstep, attribute, clamp } from 'three/tsl';
import { LOOK } from './look.js';
import { WATER_LOOK, sheetRuns, isWaterSurface } from './waterMath.js';
import { createWaterSpray } from './waterSpray.js';
import { ambientWaterHeight } from './waterMotion.js';
import { S } from '../sim/state.js';


/** Road samples every stepM for sheetRuns: { s, y, surface }. */
export function sampleSurface(track, stepM = 1) {
  const n = Math.max(2, Math.ceil(track.lengthM / stepM)), out = [];
  for (let i = 0; i <= n; i++) {
    const s = Math.min(track.lengthM, i * stepM), f = track.frameAt(s);
    out.push({ s, y: f.p[1], surface: f.surface });
  }
  return out;
}

export function createWaterfall(track, pal, { fluid = null, L = WATER_LOOK } = {}) {
  const object = new Group(); object.name = 'waterfall-and-contact-spray';
  const sprayPool = createWaterSpray(); object.add(sprayPool.object);
  const runs = track && track.frameAt ? sheetRuns(sampleSurface(track), L) : [];
  const u = { t: uniform(0) };
  const P = [], Nn = [], U = [], END = [], I = [];
  const points = [];   // { p: [x, y, z], lip: bool } for the spray
  for (const r of runs) {
    const a = r.s0 - L.sheetRunM, b = r.s1 + L.sheetRunM, rows = Math.max(2, Math.ceil(b - a) + 1), base = P.length / 3;
    const reactive=fluid?.atlasUV?.((r.s0+r.s1)/2,0)?.wet;
    for (let i = 0; !reactive && i < rows; i++) {
      const s = a + (b - a) * i / (rows - 1), f = track.frameAt(s), half = f.w / 2 + L.sheetOverM, end = Math.min(s - a, b - s);
      for (const side of [-1, 1]) {
        const x = side * half;
        P.push(f.p[0] + f.b[0] * x + f.n[0] * L.sheetLiftM, f.p[1] + f.b[1] * x + f.n[1] * L.sheetLiftM, f.p[2] + f.b[2] * x + f.n[2] * L.sheetLiftM);
        Nn.push(f.n[0], f.n[1], f.n[2]); U.push(x, s - r.s0); END.push(end);
      }
      if (i) { const v = base + (i - 1) * 2; I.push(v, v + 1, v + 2, v + 1, v + 3, v + 2); }
    }
    const lip = track.frameAt(r.s0), foot = track.frameAt(r.s1);
    points.push({ p: [lip.p[0], lip.p[1] + 0.2, lip.p[2]], frame:lip, lip: true }, { p: [foot.p[0], foot.p[1] + 0.2, foot.p[2]], frame:foot, lip: false });
  }
  let mesh = null, mat = null;
  if (I.length) {
    const g = new BufferGeometry();
    g.setAttribute('position', new BufferAttribute(new Float32Array(P), 3));
    g.setAttribute('normal', new BufferAttribute(new Float32Array(Nn), 3));
    g.setAttribute('uv', new BufferAttribute(new Float32Array(U), 2));
    g.setAttribute('aEnd', new BufferAttribute(new Float32Array(END), 1));
    g.setIndex(I);
    g.computeBoundingSphere();
    mat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
    const p = uv(), lane = floor(p.x.div(L.streakW)), ph = hash(lane.add(3.1)), gain = hash(lane.add(17.7)).mul(0.5).add(0.5);
    const st = fract(p.y.div(L.streakM).sub(u.t.mul(L.sheetSpeedMps / L.streakM)).add(ph));
    const streak = pow(st, 4).mul(gain);
    const inFade = smoothstep(0, L.sheetRunM, attribute('aEnd', 'float'));
    const white = vec3(1, 0.98, 0.99);
    mat.colorNode = pal.col(0).mul(0.4).add(pal.col(1).mul(0.15)).add(white.mul(streak.mul(0.9)));
    mat.opacityNode = clamp(float(L.sheetAlpha).mul(streak.add(0.45)).mul(inFade), 0, 1);
    mesh = new Mesh(g, mat);
    mesh.renderOrder = 0.75; mesh.frustumCulled = true; // water stays below pooled wake rings (1) and spray (2)
    object.add(mesh);
  }

  let timer = 0;
  function spray(pt, gentle, mul = 1) {
    sprayPool.emit({p:pt.p,n:pt.frame.n,b:pt.frame.b,t:pt.frame.t,count:Math.round(L.sprayCount*mul),
      dirS:pt.lip?3:1,lift:pt.lip?2.2:1.4,spread:3,size:.045,life:.8,gentle});
  }
  /** Advance the streak clock (scroll = the Gentle share) and the spray timer; playerPos gates the spray. */
  function update(dt, { playerPos = null, gentle = false, paused = false, scroll = 1 } = {}) {
    if (paused) return;
    sprayPool.update(dt,{gentle});
    u.t.value += dt * scroll;
    if (!points.length || !playerPos) return;
    timer += dt;
    if (timer < L.sprayEveryS) return;
    timer = 0;
    for (const pt of points) {
      const d = Math.hypot(pt.p[0] - playerPos.x, pt.p[1] - playerPos.y, pt.p[2] - playerPos.z);
      if (d <= L.sprayNearM) spray(pt, gentle);
    }
  }
  /** CONTRACT `splash { s, x, speed, down }`: the ring and drops at the landing point. */
  const normal = new Vector3(), tangent = new Vector3(), right = new Vector3(), sampled = {}, kartMemory = Array.from({length:16},()=>({spray:0,wet:false,seen:false,grounded:false,down:0}));
  function contact(s, x, payload = {}) {
    const f = track.surfaceAt ? track.surfaceAt(s, x) : track.frameAt(s);
    normal.fromArray(payload.normal || f.n).normalize(); const n=normal.toArray();
    tangent.fromArray(f.t).addScaledVector(normal,-tangent.dot(normal)).normalize();right.crossVectors(tangent,normal).normalize();
    const p = payload.p || (track.surfaceAt ? f.p : f.p.map((v, k) => v + f.b[k] * x));
    const value=fluid?.sample?.(s,x,sampled),height=value?.wet ? Math.max(-.8,Math.min(.8,value.height||0)):0;
    return { p:p.map((v,k)=>v+n[k]*(.12+height+(value?.wet?ambientWaterHeight(p,u.t.value):0))),n,b:right.toArray(),t:tangent.toArray() };
  }
  function splash({ s = 0, x = 0, down = 0, kind = 'land', ...payload } = {}, gentle = false) {
    if (!track || !track.frameAt) return;
    const pt = contact(s, x, payload), hop = kind === 'hop';
    const impact=Math.min(1,Math.max(0,down)/12),speed=Math.min(40,Math.max(0,payload.speed||0));
    fluid?.disturb?.({s,x,radius:hop?1.5:2.5+impact*2,strength:(hop?-.18:.3+impact*.4)*(gentle?.7:1),
      dirX:0,dirS:speed*.06,foam:hop?.25:.6});
    for(const side of [-1,1])sprayPool.emit({...pt,count:hop?9:18+Math.round(impact*12),
      dirX:side*(hop?1.8:3+impact*3),dirS:speed*.12,lift:hop?2:3.8+impact*3,spread:2+impact*2,
      size:hop?.04:.06,life:hop?.65:1.05,gentle});
  }
  /** Pooled spray follows actual wet contacts, including lateral drift on curved banks. */
  function kart(i, st, dt, { gentle = false, visible = true, paused = false } = {}) {
    if (paused || !(dt > 0)) return;
    if(!Number.isInteger(i)||i<0||i>=kartMemory.length)return;
    const memory=kartMemory[i];
    const s = st[S.s], f = track.frameAt(s), speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    const wet=fluid?.sample?.(s,st[S.lateral],sampled);
    const grounded=!!st[S.grounded]&&!st[S.airborne],bankWet=wet?.wet===true&&!isWaterSurface(f.surface);
    const down=Math.max(0,-(st[S.velX]*f.n[0]+st[S.velY]*f.n[1]+st[S.velZ]*f.n[2]));
    // Grip canal banks emit generic hop/land in the sim. Add their fluid contacts here only,
    // leaving stream/pool to their existing splash events so a landing never fires twice.
    if(visible&&!st[S.respawnPhase]&&bankWet&&memory.seen) {
      if(memory.grounded&&!grounded)splash({s,x:st[S.lateral],kind:'hop',speed},gentle);
      else if(!memory.grounded&&grounded)splash({s,x:st[S.lateral],down:memory.down,speed},gentle);
    }
    memory.seen=true;memory.grounded=grounded;memory.down=grounded?0:Math.max(memory.down,down);
    if (!visible || !st[S.grounded] || st[S.airborne] || st[S.respawnPhase] || (wet?.wet!==true&&!isWaterSurface(f.surface)) || wet?.wet===false || speed < 1.5) { memory.spray=0;memory.wet=false;return; }
    const along=st[S.velX]*f.t[0]+st[S.velY]*f.t[1]+st[S.velZ]*f.t[2];
    const lateral=st[S.velX]*f.b[0]+st[S.velY]*f.b[1]+st[S.velZ]*f.b[2];
    const steering=Math.min(1,Math.abs(st[S.steerSlew])+Math.abs(lateral)/7),drifting=st[S.driftDir]!==0;
    memory.spray+=dt;memory.wet=true;
    if(memory.spray<(drifting?.065:.095))return;
    const elapsed=Math.min(.14,memory.spray);memory.spray=0;
    const strength=Math.min(.65,speed*.012+steering*.18)*elapsed/.1*(gentle?.7:1);
    fluid?.disturb?.({s,x:st[S.lateral],radius:1.7,strength,dirX:lateral*.15,dirS:along*.1,foam:.18+steering*.25});
    for (const side of [-1, 1]) {
      const x = Math.max(-f.w / 2 + 0.1, Math.min(f.w / 2 - 0.1, st[S.lateral] + side * LOOK.saucerR * 1.15));
      const sideWet=fluid?.sample?.(s,x,sampled);if(sideWet?.wet===false)continue;
      const pt=contact(s-.7,x),push=side*(1.6+speed*.055+steering*2.4)+lateral*.35;
      fluid?.disturb?.({s:s-1.4,x,radius:1.6+steering*.9,strength:-strength*.8,dirX:push,dirS:-along*.08,foam:.25+steering*.35});
      sprayPool.emit({...pt,count:drifting?12:5+Math.round(steering*4),dirX:push,dirS:-along*.15,
        lift:1.5+steering*2+(drifting?.9:0),spread:1.4+steering,size:drifting?.055:.038,life:.65+steering*.2,gentle});
    }
  }
  /** CONTRACT `chute { s }`: the lip nearest s sprays at once. */
  function chute({ s = 0 } = {}, gentle = false) {
    let best = null, bd = Infinity;
    for (const r of runs) { const d = Math.abs(r.s0 - s); if (d < bd) { bd = d; best = r; } }
    const pt = best ? points[runs.indexOf(best) * 2] : null;
    if (pt) spray(pt, gentle, 2);
  }
  function reset(){timer=0;u.t.value=0;sprayPool.reset();for(const row of kartMemory){row.spray=0;row.wet=false;row.seen=false;row.grounded=false;row.down=0;}}
  return { object, runs, points, uniforms:u, update,splash,chute,kart,reset,spray:sprayPool,
    dispose(){reset();sprayPool.dispose();if(mesh){mesh.geometry.dispose();mat.dispose();}object.removeFromParent();} };
}

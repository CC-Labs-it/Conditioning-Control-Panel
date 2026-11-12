import { LITE, canvasDpr, paintEvery } from '../../perf.js';

// Fixed canvas padding leaves room for flame tips above the score.
const MARGIN_X = 60;
const MARGIN_Y = 16;
export const SCORE_FIRE_HEADROOM = 28;
const TAU = Math.PI * 2;
const clamp = (x, lo, hi) => Math.min(hi, Math.max(lo, x));

// A small, seamless turbulence texture. Different layers slide through each
// plume at different speeds, carrying the hot core upward as the edges curl.
function makeFlow() {
  const field = new Float32Array(128 * 128);
  let seed = 938;
  for (const [size, weight] of [[4,.52],[8,.33],[16,.15]]) {
    const grid = new Float32Array(size * size);
    for (let i=0; i<grid.length; i++) {
      seed = (Math.imul(1664525,seed) + 1013904223) >>> 0;
      grid[i] = seed / 2147483648 - 1;
    }
    for (let y=0; y<128; y++) {
      const v=y*size/128, iy=Math.floor(v), fy=v-iy, sy=fy*fy*(3-2*fy);
      for (let x=0; x<128; x++) {
        const u=x*size/128, ix=Math.floor(u), fx=u-ix, sx=fx*fx*(3-2*fx);
        const nextX=(ix+1)%size, top=iy*size, bottom=((iy+1)%size)*size;
        const a=grid[top+ix]+(grid[top+nextX]-grid[top+ix])*sx;
        const b=grid[bottom+ix]+(grid[bottom+nextX]-grid[bottom+ix])*sx;
        field[y*128+x]+=(a+(b-a)*sy)*weight;
      }
    }
  }
  return field;
}
let flow;
function noise(x,y) {
  const ix=Math.floor(x), iy=Math.floor(y), fx=x-ix, fy=y-iy;
  const left=ix&127, right=(ix+1)&127, top=(iy&127)*128, bottom=((iy+1)&127)*128;
  const a=flow[top+left]+(flow[top+right]-flow[top+left])*fx;
  const b=flow[bottom+left]+(flow[bottom+right]-flow[bottom+left])*fx;
  return a+(b-a)*fy;
}

const brushByCanvas = new WeakMap();
function flameBrush(g, palette, width) {
  let brush=brushByCanvas.get(g.canvas);
  if (!brush) {
    flow ??= makeFlow();
    const tile=g.canvas.ownerDocument.createElement('canvas');
    tile.height=56;
    const pen=tile.getContext('2d',{willReadFrequently:true});
    brush={tile,pen};
    brushByCanvas.set(g.canvas,brush);
  }
  if (!brush.pixels || brush.tile.width!==width) {
    brush.tile.width=width;
    brush.pixels=brush.pen.createImageData(width,brush.tile.height);
  }
  const key=palette.a+'|'+palette.b;
  if (brush.key!==key) {
    brush.key=key;
    brush.ramps=[palette.a,palette.b].map((color,i)=>{
      // Even one equipped mod has a cooler shadow and a warm pearly core.
      const row=g.canvas.ownerDocument.createElement('canvas');row.width=256;row.height=1;
      const p=row.getContext('2d'), ramp=p.createLinearGradient(0,0,256,0);
      const shadow=i?'#924879':'#514491', pearl=i?'#ffdfb6':'#d9f9ff';
      ramp.addColorStop(0,`color-mix(in srgb, ${color} 58%, ${shadow})`);
      ramp.addColorStop(.36,color);
      ramp.addColorStop(.64,`color-mix(in srgb, ${color} 87%, ${pearl})`);
      ramp.addColorStop(.86,`color-mix(in srgb, ${color} 58%, ${pearl})`);
      ramp.addColorStop(1,pearl);
      p.fillStyle=ramp;p.fillRect(0,0,256,1);
      return p.getImageData(0,0,256,1).data;
    });
  }
  return brush;
}

// A continuous flame front across the baseline, sampled at a lower resolution
// than the text. The foreground score is drawn independently at native scale.
function paintBed(brush, span, heat, t, impact, peak) {
  const {pixels,ramps} = brush;
  const {width,height,data} = pixels;
  const sway=t*43, twist=heat*heat*(3-2*heat);
  for(let y=0;y<height;y++) {
    const h=(height-4-y)/(height-5);
    const lean=twist*h*h*(.9*Math.sin(t*3.2+h*5)+.6*Math.sin(h*11-t*2));
    for(let x=0;x<width;x++) {
      const u=(x+.5)/width, lateral=u*2-1, px=lateral*span*.5;
      const draft=noise(px*.94,h*34-sway);
      // Blend two independently advancing currents. Changing heat changes how
      // they mix without skipping forward in time on the next tap.
      const eddy=noise(px*1.25+draft*16,h*48-t*75);
      const bent=px+twist*h*(eddy*24+draft*8);
      const fuel=noise(bent*2.1+draft*14,h*77-sway*1.65);
      const fine=noise(bent*3.6+draft*8-14,h*92-sway*2.1);
      const curl=bent*.119+draft*(.85+h*1.9)+.50*Math.sin(t*2.8+px*.052)+lean;
      const filament=1-Math.abs(Math.sin(curl));
      const split=1-Math.abs(Math.sin(curl*2.15+eddy*3+h*5-t*2.4));
      const edge=Math.max(0,1-Math.pow(Math.abs(lateral),3.2));
      const tip=edge*(.53+.45*filament+.25*fuel+.12*fine+twist*(.25*(split-.4)+.25*eddy));
      const body=tip-h;
      const ignition=.06*impact*Math.max(0,1-Math.abs(Math.abs(lateral)-(1-impact))*5);
      const density=clamp((body+ignition)*4.6,0,1);
      const foot=clamp((h+.045+.016*fine)*13,0,1);
      const outer=clamp((1-Math.abs(lateral))*10,0,1)*clamp((1-h)*20,0,1);
      // Streaks of the two mod inks travel up through the folds. At higher
      // heat they exchange places across a tongue, with a pearly leading edge.
      const current=Math.sin(bent*.065+h*6.6-t*3.6+2.1*eddy);
      const dye=clamp(u+.18*draft+.10*Math.sin(h*7+t*1.35)+twist*(.72*current+.29*eddy),0,1);
      const glow=clamp(.31+.66*body+.17*filament+.16*fuel+ignition*2+
        twist*(.04+.18*split*Math.max(0,current))+peak*(.065+.145*filament*split),0,1);
      const k=Math.round(glow*255)*4, index=(y*width+x)*4;
      for(let channel=0;channel<3;channel++) {
        const a=ramps[0][k+channel];
        data[index+channel]=a+(ramps[1][k+channel]-a)*dye;
      }
      // Even the hottest core is translucent, including where it crosses a
      // digit counter (the open center of an 8, 0 or 9).
      data[index+3]=Math.round(255*density*foot*outer*(.21+.32*heat+.095*twist+peak*(.14+.035*filament))*(.72+.28*glow));
    }
  }
  brush.pen.putImageData(pixels,0,0);
}

export function paintScoreHeat(g,width,height,heat,time,palette,impact=0,headroom=0) {
  g.clearRect(0,0,width,height);
  const fire=clamp((heat-.13)/.87,0,1);
  if(!fire)return;
  // Give the last third of a streak a second rise, easing in and out so
  // crossing that threshold never snaps the silhouette or exposure.
  const progress=clamp((heat-.66)/.34,0,1);
  const peak=progress*progress*(3-2*progress);
  const textWidth=Math.max(20,width-2*MARGIN_X);
  const textHeight=Math.max(15,height-2*MARGIN_Y-headroom);
  const base=height-MARGIN_Y+1;
  const span=Math.min(width-6,textWidth*(.48+.57*fire+.19*peak)+18*fire+10*peak);
  const rise=Math.min(base-1, textHeight*(.26+1.20*fire+.76*peak)+impact*4);
  // The bed is per-pixel noise, the costliest paint on the page: phones sample it at about half the columns.
  const brush=flameBrush(g,palette,Math.round(LITE?clamp((textWidth+18)*.4,56,120):clamp((textWidth+18)*.72,72,240)));
  paintBed(brush,span,fire,time,impact,peak);
  g.save();
  g.globalAlpha=Math.min(1,fire*8);
  g.drawImage(brush.tile,(width-span)/2,base-rise,span,rise);
  // A few detached motes rise from different parts of the fire. Their
  // contrast stays below that of the outlined numeral in front of them.
  const count=Math.floor(3+fire*15+6*peak);
  for(let i=0;i<count;i++){
    const seed=(i*.61803399+.27)%1;
    const age=(time*(.48+seed*.33)+seed*9)%1;
    const x=width/2+(seed-.5)*span*.92+Math.sin(age*6+i)*age*4;
    const y=base-(.24+age*.75)*rise*(.63+.38*Math.sin(seed*Math.PI));
    const r=(.45+.56*seed)*(1-.45*age);
    g.globalAlpha=.27*fire*Math.sin(age*Math.PI);
    g.fillStyle=i%5===0?'#fff2d2':(i%2?palette.a:palette.b);
    g.beginPath();g.ellipse(x,y,r,r*(1.4+impact),.28,0,TAU);g.fill();
  }
  g.restore();
}


export function createScoreMotion() {
  const hits = [];
  const pose = {};
  let heat, charge, time, lastGain, lastHit, protectedUntil, direction;

  function reset() {
    hits.length = 0;
    heat = charge = time = protectedUntil = 0;
    lastGain = lastHit = -Infinity;
    direction = 1;
    Object.assign(pose, { heat:0, charge:0, impact:0, scaleX:1, scaleY:1, lift:0, tilt:0, active:false });
  }
  reset();

  function step(dt, gain = 0, taps = 0) {
    time += dt;
    heat *= Math.exp(-dt * (time-lastGain < .29 ? .23 : 1.12));
    if (gain > .025 || taps > 0) {
      const clustered = time-lastGain < .38;
      const previousHeat = heat;
      const large = gain >= 12;
      const manualHeat = taps ? Math.min(.50, taps * (clustered ? .112 : .145)) : 0;
      const gainHeat = large ? Math.min(1,.64+.13*Math.log2(1+gain/12))
        : (taps ? .07 : .14) * Math.log2(1+gain);
      heat = Math.min(1, heat + Math.max(manualHeat, gainHeat));
      const breakthrough = previousHeat < .70 && heat >= .70;
      if (large || (time >= protectedUntil && (time-lastHit >= .12 || breakthrough))) {
        const strength = large ? Math.min(1,.67+Math.log2(gain/12+1)*.16)
          : breakthrough ? .76 : Math.min(.62,.24+.30*heat+.09*Math.log2(1+gain));
        // Merge simultaneous awards into one hit; frequent small ticks can't
        // interrupt the visible release of a jackpot.
        const overlapping = hits.length && time-lastHit < .10;
        if (overlapping) {
          hits[hits.length-1].strength = Math.max(hits[hits.length-1].strength, strength);
        } else {
          direction *= -1;
          hits.push({ start:time, strength, direction });
          lastHit = time;
        }
        if (large || breakthrough) protectedUntil = time + .29;
      }
      lastGain = time;
    }
    while (hits.length && time-hits[0].start > .88) hits.shift();
    if (heat < .012) heat = 0;
    // Heat changes on the beat. The sustained swell eases between beats and
    // releases more slowly than the impact so the streak still has weight.
    charge += (heat-charge)*(1-Math.exp(-dt*(heat>charge ? 8 : 4)));
    if (!heat && charge<.004) charge=0;
    let shape = 0, swell = 0, rock = 0, impact = 0;
    for (const hit of hits) {
      const age = time-hit.start;
      const strength = hit.strength;
      const windup = .036+.049*strength;
      let spring, flight = 0;
      if (age < windup) {
        const u = age/windup;
        spring = -u*u*(3-2*u);
      } else {
        const t = age-windup;
        // Damped release starts in the compressed pose. It passes through
        // full extension, dips once on landing, then settles on the baseline.
        spring = Math.exp(-8.6*t) * (2.8*Math.sin(18*t)-Math.cos(18*t));
        const u = 12*t;
        flight = u*Math.exp(1-u);
        impact = Math.max(impact, strength * (1-Math.exp(-110*t)) * Math.exp(-5.3*t));
      }
      shape += strength*spring;
      swell += strength*flight;
      rock += hit.direction*strength*(spring*.91+flight*.22);
    }
    pose.heat = heat;
    pose.charge = charge;
    pose.impact = Math.min(1,impact*1.19);
    const streakScale = 1 + .065*charge;
    pose.scaleX = streakScale + clamp(-.055*shape+.093*swell,-.045,.084);
    pose.scaleY = streakScale + clamp(.14*shape+.015*swell,-.14,.19);
    pose.lift = clamp(-4.8*Math.max(0,shape)-1.6*swell-2.4*Math.min(0,shape),-8,3);
    pose.tilt = clamp(rock*1.38,-2.15,2.15);
    pose.active = charge > 0 || heat > 0 || hits.length > 0;
    return pose;
  }
  return { step, reset };
}

// Apply solid ink to the real glyphs. Each numeral catches the moving sheen
// in sequence. A single-span counter samples the same light at the midpoint.
export function paintScoreInk(score, digits, pose, seconds) {
  const n=digits.length;
  function shade(node,position) {
    const phase=(position*.82-seconds*.57)*TAU;
    const blend=.5+.5*Math.cos(phase);
    const glint=.5+.5*Math.cos(phase*1.45+.8);
    node.style.setProperty('--score-ink-tint', Math.round(100*blend)+'%');
    node.style.setProperty('--score-ink-strength', Math.round(pose.charge*(43+34*glint))+'%');
  }
  shade(score,.5);
  for(let i=0;i<n;i++) shade(digits[i],n===1?.5:i/(n-1));
}

export function mountScoreHeat(ctx) {
  const counter = ctx.parts.counter;
  const doc = counter.ownerDocument, win = doc.defaultView;
  const canvas = ctx.el('canvas', '', null, counter);
  ctx.stage.register(canvas);   // the fire behind the digits joins the stage; the digits themselves never dim
  canvas.className = 'score-heat-art';
  canvas.setAttribute('aria-hidden', 'true');
  const g = canvas.getContext('2d');
  if (!g) { canvas.hidden = true; return {}; }
  ctx.css(`
    #stage:has(> #counter[data-score-heat]) > #l-ticker { z-index:1; }
    #counter[data-score-heat] {
      position:relative;isolation:isolate;margin-block:${22+SCORE_FIRE_HEADROOM}px 4px;transform-origin:50% 78%;scale:var(--score-scale,1);
      rotate:var(--score-tilt,0deg);translate:0 var(--score-lift,0px);
    }
    #counter[data-score-heat] > :is(#count,#fz-roll,#cur) {
      position:relative;z-index:1;paint-order:stroke fill;
      -webkit-text-stroke:.057em #15111a;
    }
    body.tb-hidecount:not(.tb-peek) #counter[data-score-heat] > #count { -webkit-text-stroke-color:#000;text-shadow:none; }
    body:not(.tb-hidecount) #counter[data-score-heat] > #count,
    #counter[data-score-heat] #fz-roll > :is(.d,.c) {
      --score-ink-dye:color-mix(in srgb,var(--niche) var(--score-ink-tint,50%),var(--niche-2));
      --score-ink-fill:color-mix(in srgb,#fffbef,var(--score-ink-dye) var(--score-ink-strength,0%));
      color:var(--score-ink-fill);paint-order:stroke fill;
    }
    #counter[data-score-crest] #fz-roll > .d {
      translate:0 calc(-.085em * var(--score-crest,0));
      color:color-mix(in srgb,var(--score-ink-fill),#fff8dc calc(92% * var(--score-crest,0)));
    }
    .score-heat-art {
      position:absolute;z-index:0;left:-${MARGIN_X}px;top:-${MARGIN_Y+SCORE_FIRE_HEADROOM}px;
      width:calc(100% + ${2*MARGIN_X}px);height:calc(100% + ${2*MARGIN_Y+SCORE_FIRE_HEADROOM}px);pointer-events:none;
    }
  `);
  let width = 0, height = 0, dpr = 1;
  const resize = () => {
    dpr = canvasDpr(2);
    width = counter.offsetWidth + MARGIN_X*2;
    height = counter.offsetHeight + MARGIN_Y*2 + SCORE_FIRE_HEADROOM;
    canvas.width = Math.ceil(width*dpr);canvas.height = Math.ceil(height*dpr);
    g.setTransform(dpr,0,0,dpr,0,0);
  };
  const observer = new win.ResizeObserver(resize);
  observer.observe(counter);resize();
  const motion = createScoreMotion();
  let taps = 0, manualGain = 0, autoTapGain = 0, flaming = false, moving = false, paintTime = 0;
  let crestStart = null, nextInkPaint=0;
  function clearCrest() {
    if (crestStart === null) return;
    delete counter.dataset.scoreCrest;
    for (const digit of counter.querySelectorAll('#fz-roll > .d')) {
      digit.style.removeProperty('--score-crest');
    }
    crestStart = null;
  }
  // Lift the existing rolling glyphs in place; their odometer uses transform
  // and the crest uses translate, so neither has to replace the other's motion.
  function paintCrest(now) {
    if (crestStart === null || now < crestStart) return;
    const u = (now-crestStart)/460;
    if (u >= 1) { clearCrest();return; }
    if (!counter.hasAttribute('data-score-crest')) counter.dataset.scoreCrest = '';
    const head = 1.50*u-.25;
    const digits = counter.querySelectorAll('#fz-roll > .d');
    for (let i=0; i<digits.length; i++) {
      const position = digits.length===1 ? .5 : i/(digits.length-1);
      const distance = Math.min(1,Math.abs(position-head)/.25);
      const light = .5+.5*Math.cos(Math.PI*distance);
      digits[i].style.setProperty('--score-crest',light.toFixed(3));
    }
  }
  const setLevel = value => { if(counter.dataset.scoreHeat !== value)counter.dataset.scoreHeat=value; };
  // FPS, automated taps, ordinary wall hits and preview wallet top-ups have
  // their own feedback. Player taps and prize money enter here exactly once.
  const earnings = () => {
    const sources=ctx.state.sources;
    return ctx.state.total-(sources.dev||0)-(sources.fps||0)-(sources.wall||0);
  };
  let previous = earnings(), previousClock = ctx.state.clock;
  function rest() {
    clearCrest();
    nextInkPaint=0;
    for (const node of [counter,...counter.querySelectorAll('#fz-roll > .d')]) {
      node.style.removeProperty('--score-ink-tint');
      node.style.removeProperty('--score-ink-strength');
    }
    counter.style.removeProperty('--score-scale');
    counter.style.removeProperty('--score-tilt');
    counter.style.removeProperty('--score-lift');
    if (flaming) g.clearRect(0,0,width,height);
    setLevel('idle');
    moving = flaming = false;
  }
  function resync() {
    previous = earnings();previousClock = ctx.state.clock;
    taps = manualGain = autoTapGain = paintTime = 0;
    motion.reset();rest();
  }
  setLevel('idle');
  ctx.on('tap',({gain,auto})=>{
    if (auto) autoTapGain += gain;
    else if (gain>0) { taps++;manualGain+=gain; }
  });
  ctx.on('suspend',resync);ctx.on('pause',resync);ctx.on('resume',resync);
  doc.addEventListener('visibilitychange',resync);
  ctx.cleanup(()=>{
    doc.removeEventListener('visibilitychange',resync);
    observer.disconnect();rest();delete counter.dataset.scoreHeat;
  });
  ctx.frame(()=>{
    const now = ctx.state.clock, total = earnings();
    const dt = clamp((now-previousClock)/1000,0,1);
    const gained = Math.max(0,total-previous-autoTapGain), manual = taps;
    const prize = Math.max(0,gained-manualGain);
    previous = total;previousClock = now;taps = manualGain = autoTapGain = 0;
    if (ctx.gentle || ctx.reduced || doc.hidden) {
      if (moving) { motion.reset();rest(); }
      return;
    }
    if (!dt || (!moving && gained <= .00001 && !manual)) return;
    const base=ctx.state.base;
    const unit=Math.max(1,base.click,base.fps*.14)*ctx.state.pace;
    const relative = gained/unit;
    const pose=motion.step(dt,relative,manual);
    if (prize/unit >= 12 && crestStart === null && counter.querySelector('#fz-roll')) crestStart=now+80;
    paintCrest(now);
    if (!pose.active) { rest();return; }
    moving=true;
    if (now>=nextInkPaint) {
      paintScoreInk(counter,counter.querySelectorAll('#fz-roll > .d'),pose,now/1000);
      nextInkPaint=now+1000/30;
    }
    counter.style.setProperty('--score-scale',pose.scaleX.toFixed(4)+' '+pose.scaleY.toFixed(4));
    counter.style.setProperty('--score-tilt',pose.tilt.toFixed(3)+'deg');
    counter.style.setProperty('--score-lift',pose.lift.toFixed(3)+'px');
    setLevel(pose.heat>.70?'blaze':pose.heat>.23?'hot':'warm');
    if (pose.heat>.13 && now>=paintTime) {
      paintScoreHeat(g,width,height,pose.heat,now/1000,ctx.palette(),pose.impact,SCORE_FIRE_HEADROOM);
      paintTime=now+paintEvery(60);flaming=true;
    } else if (pose.heat<=.13 && flaming) {
      g.clearRect(0,0,width,height);flaming=false;
    }
  });
  return {};
}

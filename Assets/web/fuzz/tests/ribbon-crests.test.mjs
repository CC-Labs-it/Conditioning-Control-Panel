import test from 'node:test';
import assert from 'node:assert/strict';
import { addImpulse } from '../ui/kinetic-waves.js';
import { drawChroma, drawPyramids } from '../ui/kinetic-references.js';

// Observe the sizes painted at each site. Polygon area measures enlargement
// independently of how far the square has rotated on this frame.
function sizes(draw, time, events, ink = '#27aee8', height = 106) {
  const bars = [], squares = [];
  let points = [], barHeight;
  const g = {
    roundRect(x,y,w,h) { barHeight = h; },
    beginPath() { points = []; },
    moveTo(x,y) { points.push([x,y]); },
    lineTo(x,y) { points.push([x,y]); },
    closePath() {
      let area = 0;
      for (let i=0; i<points.length; i++) {
        const [x,y] = points[i], [nx,ny] = points[(i+1) % points.length];
        area += x*ny - nx*y;
      }
      squares.push(Math.abs(area) / 2);
    },
    fill() { if (barHeight !== undefined && this.fillStyle === ink) bars.push(barHeight); },
    stroke() {}
  };
  draw(g, {w:420,h:height,time,events,axis:()=>height/2});
  if (draw === drawChroma) return bars;
  return squares.filter((_,i)=>i%10===0).slice(1,-1);
}

test('Chroma reserves the tall blue crests for sustained fast input', () => {
  const now = 5, u = .46;
  const onButton = hits => sizes(drawChroma,now,hits,'#27aee8',284);
  const idle = onButton([]);
  const origin = Math.floor(idle.length*u);
  const heightAtPace = hz => {
    const hits = [];
    const count = hz ? 16 : 1;
    for (let i=count-1; i>=0; i--) addImpulse(hits, now-(hz ? i/hz : 0), u, ()=>.5);
    return onButton(hits)[origin];
  };
  const [single,slow,medium,fast,veryFast,rapid,burst] = [0,2,4,8,12,16,20].map(heightAtPace);
  assert.ok(Math.max(...idle)<8 && single<19);
  assert.ok(slow<single*1.35 && medium>slow*1.3);
  assert.ok(fast>medium*1.4 && veryFast>fast*1.25);
  assert.ok(rapid>veryFast+12 && burst>rapid+8);
  const hits=[];
  for (let i=0; i<10; i++) addImpulse(hits,i*.085,u,()=>.5);
  addImpulse(hits,now,u,()=>.5);
  assert.equal(onButton(hits)[origin],single);
});

test('Chroma measures click cadence independently of the animation frame', () => {
  for (const rate of [4,8,12,16,20,24]) {
    const hits=[];
    for(let i=0;i<18;i++) {
      const contact=i/rate;
      addImpulse(hits,Math.floor(contact*30)/30,.5,()=>.5,100+contact);
    }
    assert.ok(Math.abs(hits.at(-1).tapRate-rate)<.05);
  }
});

test('fast Chroma crests grow within the available height of each preview', () => {
  for (const height of [44,84,164,284]) {
    let last=0;
    for (const rate of [4,8,12,16,20,30]) {
      const hits=[];
      for(let i=15;i>=0;i--)addImpulse(hits,4-i/rate,.5,()=>.37);
      const bars=sizes(drawChroma,4,hits,'#27aee8',height);
      const peak=bars[Math.floor(bars.length/2)];
      assert.ok(peak>last+.25 && peak<height);
      last=peak;
    }
  }
});

test('the solid blue fill travels through the neighbors and the echoes follow its tips', () => {
  const hits = [];
  for (let i=0; i<10; i++) addImpulse(hits,i*.10,.3,()=>.5);
  let maxOverhang=0;
  for (let frame=0; frame<=65; frame++) {
    const t = frame/60;
    const core = sizes(drawChroma,t,hits);
    for (const ink of ['#efbb22','#fa247e']) {
      const echo = sizes(drawChroma,t,hits,ink);
      for (let i=0; i<core.length; i++) maxOverhang=Math.max(maxOverhang,(echo[i]-core[i])/2);
    }
  }
  assert.ok(maxOverhang>5 && maxOverhang<9,'the colored echo is distinct and stays within reach of the blue tip');
});

test('a Chroma press staggers neighboring bars and gives occasional lanes a longer crest', () => {
  const count = sizes(drawChroma,0,[]).length, origin = Math.floor(count/2);
  const u = (origin + .5)/count;
  function sample(seed) {
    const hits = [];
    addImpulse(hits,0,u,()=>seed);
    const peaks = Array(count).fill(0), arrival = Array(count).fill(Infinity);
    for (let frame=0; frame<600; frame++) {
      const t = frame/500, idle = sizes(drawChroma,t,[]), active = sizes(drawChroma,t,hits);
      for (let i=0; i<count; i++) {
        const growth = active[i] - idle[i];
        peaks[i] = Math.max(peaks[i],growth);
        if (growth>1 && arrival[i]===Infinity) arrival[i]=t;
      }
    }
    // Opposite lanes are the same distance from contact, so any differences
    // in their reach and timing are independent of normal wave attenuation.
    const accented = [], offsets = [];
    for(let d=1; d<Math.min(origin+1,count-origin); d++) {
      const a=origin-d, b=origin+d, ratio=peaks[a]/peaks[b];
      assert.ok(ratio>1/1.21 && ratio<1.21);
      if(ratio>1.08) accented.push(a);
      if(ratio<1/1.08) accented.push(b);
      offsets.push(Math.abs(arrival[a]-arrival[b]));
    }
    assert.ok(accented.length>0 && accented.length<count/2);
    const offset=Math.max(...offsets);
    assert.ok(offset>=.006 && offset<.04);
    return accented;
  }
  assert.notDeepEqual(sample(.16),sample(.70));
});

for (const [name, draw] of [['Chroma', drawChroma], ['Pyramid', drawPyramids]]) {
  test(name + ' enlarges the nearest site immediately and passes one crest outward', () => {
    const count = sizes(draw, 0, []).length;
    for (const u of [0,.34,.69,1]) {
      const hits = [];
      addImpulse(hits, 0, u, ()=>.5);
      const origin = Math.min(count-1,Math.floor(u*count));
      const rest = sizes(draw, 0, []), contact = sizes(draw, 0, hits);
      assert.ok(contact[origin] > rest[origin]*1.45);
      rest.forEach((value,i)=>{ if (i !== origin) assert.equal(contact[i], value); });
      const arrival = Array(count).fill(Infinity);
      for (let frame=0; frame<=160; frame++) {
        const t = frame/100, idle = sizes(draw,t,[]), active = sizes(draw,t,hits);
        for (let i=0; i<count; i++) {
          if (active[i] > idle[i]*1.3 && !Number.isFinite(arrival[i])) arrival[i] = t;
        }
      }
      for (let i=0; i<count; i++) {
        assert.ok(Number.isFinite(arrival[i]), 'wave reaches the edge');
        if (i!==origin) assert.ok(arrival[i] > arrival[i+Math.sign(origin-i)]);
      }
    }
  });

  test(name + ' passes the actual click frequency to a neighboring site', () => {
    const count = sizes(draw,0,[]).length, u = .35;
    const receiver = Math.floor(u*count) + 1;
    for (const hz of [2,8,12]) {
      const hits = [];
      let peaks = 0, raised = false;
      for (let frame=0; frame<180; frame++) {
        const t = frame/120;
        if (frame<120 && frame%(120/hz)===0) addImpulse(hits,t,u,()=>.5);
        const idle = sizes(draw,t,[])[receiver], active = sizes(draw,t,hits)[receiver];
        const up = active > idle*1.40;
        if (!raised && up) peaks++;
        raised = up;
      }
      assert.equal(peaks,hz,hz+' distinct crests per second');
    }
  });
}

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { normalizeParams2, LOOM_STYLES } from '../../arcademy/engine/loom/loomField.js';
import { SPIRAL_LOOK, gateParams, loadLoom, gateParticleLook, buildParticles, loomState, particleLocal, loomCover } from '../render/spiralMath.js';

const layout = JSON.parse(readFileSync(new URL('../tracks/mainframe.spirals.json', import.meta.url), 'utf8'));
const loom = await loadLoom();

test('all six Loom styles produce materially different world particle silhouettes at identical settings', () => {
  // Same seed, palette, radius, arm count, duty and turns isolate the actual style mapping.
  // Compare occupied regions after the same world-particle transform used by the renderer.
  const distributions=LOOM_STYLES.map(style=>{
    const q=normalizeParams2({layer:{style,arms:3,turns:1.5,duty:.5,colors:['#12ff95']}});
    const gate={radiusM:24,depthM:0}, p=buildParticles(q,gate,12000,41), state=loomState(q,.17);
    const histogram=new Float32Array(10*24);
    for(let i=0;i<p.n;i++) {
      const [x,y,,r]=particleLocal(p.a0,p.a2,i,q,gate,state);
      const angle=(Math.atan2(y,x)/(Math.PI*2)+1)%1;
      histogram[Math.min(9,Math.floor(r*10))*24+Math.floor(angle*24)]++;
    }
    return {style,histogram};
  });
  for(let i=0;i<distributions.length;i++) for(let j=0;j<i;j++) {
    const a=distributions[i],b=distributions[j];
    const difference=a.histogram.reduce((sum,n,k)=>sum+Math.abs(n-b.histogram[k]),0)/12000;
    assert.ok(difference>.5, a.style+' and '+b.style+' must move at least a quarter of the visible particle mass to other regions');
  }
});

test('Mainframe gates have distinct silhouettes, thread palettes and motion rather than repeated tinted presets', () => {
  const params = layout.gates.map(g => gateParams(g,loom));
  assert.equal(new Set(params.map(p=>JSON.stringify(p))).size, layout.gates.length);
  assert.equal(new Set(params.map(p=>JSON.stringify(p.layer.colors))).size, layout.gates.length);
  assert.equal(new Set(params.map(p=>p.layer.style)).size, LOOM_STYLES.length);
  assert.ok(params.some(p=>!p.layer2.enabled) && params.some(p=>p.layer2.enabled), 'clean single silhouettes alternate with layered interference');
  assert.ok(layout.gates.some(g=>g.depthM===0) && layout.gates.some(g=>g.depthM>=36), 'flat rose windows contrast with long dimensional tunnels');
  assert.ok(params.some(p=>p.layer.arms===1) && params.some(p=>p.layer.arms>=9));
  assert.ok(new Set(params.map(p=>p.pulse.amp)).size>=6);
  assert.ok(params.filter(p=>p.hueCycles===0).length>=5, 'most gates retain a recognizable palette rather than all cycling through the same hues');
  for (const [i,p] of params.entries()) {
    assert.deepEqual(p,normalizeParams2(layout.gates[i].loom), 'world and heads-up share supported real Loom params');
    assert.equal(p.layer.direction,1); assert.equal(p.layer2.direction,1);
    for (const layer of [p.layer,p.layer2]) assert.ok(LOOM_STYLES.includes(layer.style));
  }
});

test('authored arm widths retain every particle on its real Loom band and preserve budget', () => {
  for (const g of layout.gates) {
    const q=gateParams(g,loom), look=gateParticleLook(g), particles=buildParticles(q,g,4200,23);
    assert.equal(particles.n,4200); assert.equal(particles.a0.length,4200*4);
    assert.ok(look.armFill>=.75 && look.coreWhite<.05, 'wide saturated arms keep their distinct silhouettes');
    let secondary=0,covered=0,cores=0;
    for(let i=0;i<particles.n;i++) {
      const kind=Math.round(particles.a0[i*4+3]); if(kind%2)secondary++;
      if(kind>=2)continue;
      const layer=kind%2?q.layer2:q.layer, rc=particles.a2[i*4+3]>.5 ? null:particles.a0[i*4];
      if(rc===null)continue;
      cores++; if(loomCover(layer,rc,particles.a0[i*4+1],0,0).cov) covered++;
    }
    assert.ok(covered/cores>.99, g.id+' particle shape remains the Loom field');
    if(q.layer2.enabled) assert.ok(secondary>700 && secondary<1200, 'secondary tracery does not drown the main arms');
    else assert.equal(secondary,0);
    const a=particleLocal(particles.a0,particles.a2,0,q,g,loomState(q,.1));
    const b=particleLocal(particles.a0,particles.a2,0,q,g,loomState(q,.3));
    assert.notDeepEqual(a,b, 'each authored field evolves through a loop');
  }
});

test('per-gate appearance leaves other tracks unchanged and clamps malformed authored values', () => {
  assert.equal(gateParticleLook({}),SPIRAL_LOOK);
  const before=JSON.stringify(SPIRAL_LOOK), v=gateParticleLook({appearance:{armFill:99,coreWhite:-1,glowShare:NaN,layer2Share:Infinity}});
  assert.equal(v.armFill,.95); assert.equal(v.coreWhite,0); assert.equal(v.glowShare,SPIRAL_LOOK.glowShare);
  assert.equal(v.layer2Share,SPIRAL_LOOK.layer2Share); assert.equal(JSON.stringify(SPIRAL_LOOK),before);
});

test('the flat first gate keeps all particles and color detail while avoiding additive white clipping', () => {
  const gate=layout.gates[0], q=gateParams(gate,loom);
  const actual=buildParticles(q,gate,SPIRAL_LOOK.perGate.high,9);
  const full=buildParticles(q,{...gate,appearance:{...gate.appearance,energy:1}},SPIRAL_LOOK.perGate.high,9);
  assert.equal(actual.n,18000); assert.deepEqual(actual.a0,full.a0); assert.deepEqual(actual.a1,full.a1);
  for(let i=0;i<actual.n;i++) assert.ok(Math.abs(actual.a2[i*4+2]-full.a2[i*4+2]*.38)<1e-6);
  assert.ok(layout.gates.slice(1).every(g=>g.appearance.energy==null), 'energy correction is local to the flat twin ribbon');
});

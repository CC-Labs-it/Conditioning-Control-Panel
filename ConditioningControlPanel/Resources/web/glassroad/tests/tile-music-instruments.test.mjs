import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';
const hooks = registerHooks({ resolve(s, c, next) {
  const names = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return names[s] ? { url: new URL('../vendor/three/build/' + names[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, float } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { tileInstrument } = await import('../render/materials/tileMusicFloor.js');
hooks.deregister();
function setup(kind, t) {
  const p = uniform(new Vector2()), column = uniform(0), row = uniform(0), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const k of ['drop', 'lead', ...Array.from({length:4}, (_,i) => 'pluckAge'+i), ...Array.from({length:4}, (_,i) => 'pluckStrength'+i)]) u[k] ||= uniform(0);
  const music = { tileBar: uniform(0), tileBeat: uniform(0), tileOn: uniform(1) };
  const layers = tileInstrument({ p, column, row, halfSpan: float(8), kind, reactive: u, music });
  const sample = layer => evaluateColour(layers[layer])[0];
  const scan = layer => Array.from({length:16}, (_,y) => Array.from({length:12},(_,x) => {
    column.value=x;row.value=y;return sample(layer);
  })).flat();
  return { p, column, row, u, music, sample, scan };
}
const mean = xs => xs.reduce((a,b) => a+b,0)/xs.length;
for (let kind=0;kind<4;kind++) test('tile instrument '+kind+' separates bass strikes, dark recovery and true drops', t => {
  const {u,music,scan}=setup(kind,t);
  u.kickAge.value=.04;u.kickStrength.value=.9;u.bass.value=.7;
  const hit=scan('bass');u.kickAge.value=.6;const rest=scan('bass');
  assert.ok(mean(hit)>mean(rest)*20, 'each strike decays to a clear dark gap');
  u.kickAge.value=.04;u.drop.value=1;const drop=scan('bass');
  assert.ok(mean(drop)>mean(hit)*1.5, 'drops are much stronger than ordinary notes');
  assert.ok(drop.filter(v=>v>.2).length>hit.filter(v=>v>.2).length, 'drop expands connected tile groups');
  u.drop.value=0;const phrases=[0,8,16].map(bar=>{music.tileBar.value=bar;return scan('bass')});
  assert.notDeepEqual(phrases[0],phrases[1]);assert.notDeepEqual(phrases[0],phrases[2]);assert.notDeepEqual(phrases[1],phrases[2]);
  music.tileBar.value=0;const left=scan('bass');u.kickCount.value=1;assert.notDeepEqual(scan('bass'),left,'next strike moves the tile bank laterally');
});
test('plucked strings bend far more for lead passages and drops, with fixed world nodes and Gentle reduction', t => {
  const {p,u,sample}=setup(1,t), origin=-8*.64;
  u.pluckAge0.value=.045;u.pluckStrength0.value=.75;
  function peak(y) {
    let best=-1, xAt=origin, weighted=0, lightSum=0;
    for(let i=0;i<=300;i++){const x=origin-1.5+i*.01;p.value.set(x,y);const light=sample('strings');weighted+=x*light;lightSum+=light;if(light>best){best=light;xAt=x}}
    return {x:lightSum>0?weighted/lightSum:xAt,light:best};
  }
  const normal=peak(6.5);u.lead.value=1;u.drop.value=1;const big=peak(6.5);
  assert.ok(Math.abs(big.x-origin)>Math.abs(normal.x-origin)*3,'lead/drop deflection is visibly larger');
  assert.ok(Math.abs(peak(0).x-origin)<.04,'nodes stay pinned to the road');
  u.gentle.value=1;assert.ok(Math.abs(peak(6.5).x-origin)<.05,'Gentle removes the fast vibration');
  u.gentle.value=0;u.pluckAge0.value=4;assert.ok(peak(6.5).light<.001,'each pluck leaves fully');
});
test('Gentle ignores fast event counts and raw spectrum at the same held phrase', t => {
  const {u,music,scan}=setup(3,t);u.gentle.value=1;u.bass.value=.6;music.tileBar.value=16;
  const before=['bass','mids','highs'].map(scan);
  for(const k of ['kick','snare','hats']){u[k].value=1;u[k+'Count'].value=7;u[k+'Age'].value=.1;u[k+'Strength'].value=1}
  u.drop.value=1;u.vizTexture.image.data.fill(255);
  assert.deepEqual(['bass','mids','highs'].map(scan),before);
});

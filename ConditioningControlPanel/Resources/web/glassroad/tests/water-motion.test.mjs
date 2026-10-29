import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(s,c,next){const files={'three/tsl':'three.tsl.min.js','three/webgpu':'three.webgpu.min.js'};return files[s]?{url:new URL('../vendor/three/build/'+files[s],import.meta.url).href,shortCircuit:true}:next(s,c);}});
const {ambientWaterHeight,ambientWaterGradient,WATER_WAVES}=await import('../render/waterMotion.js');
hooks.deregister();

test('crossing swells remain below the bed clearance at the deepest simulated trough',()=>{
  const bound=WATER_WAVES.reduce((n,w)=>n+w[3]*1.22,0);
  assert.ok(.1-.8-bound>-1.05,'ambient plus reactive trough must stay above the recessed bed');
  let sum=0,min=Infinity,max=-Infinity,change=0;
  for(let i=0;i<2000;i++){
    const p=[i*.71,0,i*.37],t=i*.017,h=ambientWaterHeight(p,t);
    assert.ok(Math.abs(h)<=bound);sum+=h*h;min=Math.min(min,h);max=Math.max(max,h);
    change+=Math.abs(ambientWaterHeight(p,t+1)-h);
  }
  assert.ok(max-min>.35,'rolling waves must have visible relief');
  assert.ok(Math.sqrt(sum/2000)>.075,'motion must cover the surface, not only isolated peaks');
  assert.ok(change/2000>.06,'the surface evolves while the kart is stationary');
});
test('reflection normals agree with the actual displaced wave gradient',()=>{
  const epsilon=.001;
  for(let i=0;i<100;i++){
    const p=[i*1.7,0,-i*.93],t=i*.1,g=ambientWaterGradient(p,t);
    for(const [axis,index] of [[0,0],[2,1]]){
      const left=[...p],right=[...p];left[axis]-=epsilon;right[axis]+=epsilon;
      const numeric=(ambientWaterHeight(right,t)-ambientWaterHeight(left,t))/(2*epsilon);
      assert.ok(Math.abs(numeric-g[index])<1e-6);
    }
  }
});

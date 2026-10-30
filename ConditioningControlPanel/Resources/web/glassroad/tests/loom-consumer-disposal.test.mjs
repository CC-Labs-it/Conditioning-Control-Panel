import {test} from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {pathToFileURL} from 'node:url';
const root=process.env.GLASSROAD_TEST_ROOT?pathToFileURL(process.env.GLASSROAD_TEST_ROOT.replaceAll('\\','/')+'/'):new URL('../',import.meta.url);
const actual=await import(new URL('../arcademy/engine/loom/loomField.js',root));
const probe={normalize:actual.normalizeParams2,current:null};
globalThis.__loomConsumerDisposal=probe;
const mock='data:text/javascript,'+encodeURIComponent(`
  export * from ${JSON.stringify(new URL('../arcademy/engine/loom/loomField.js',root).href)};
  const probe=globalThis.__loomConsumerDisposal;
  export const normalizeParams2=(...args)=>probe.normalize(...args);
  export function createFieldRenderer(canvas){
    const state=probe.current;
    const field={canvas,gl:{getExtension(){throw Error('forced context loss is forbidden');}},render(){state.renders++;}};
    if(state.supportsDispose)field.dispose=()=>{state.disposals++;};
    state.fields.push(field);return field;
  }
  export function composeFrame(ctx,field,params,phase){field.render(params,phase);ctx.drawImage(field.canvas,0,0);}
  export function drawFallbackFrame(){throw Error('the working legacy renderer must remain usable');}
`);
const hooks=registerHooks({resolve(specifier,context,next){
  if(specifier.endsWith('/loom/loomField.js')&&context.parentURL.includes('/glassroad/render/'))return{url:mock,shortCircuit:true};
  const file={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'}[specifier];
  return file?{url:new URL('vendor/three/build/'+file,root).href,shortCircuit:true}:next(specifier,context);
}});
const {acquireLoomAtlas}=await import(new URL('render/loomAtlas.js',root));
const {createLoomDisplayFrames}=await import(new URL('render/loomVariety.js',root));
const {createPlaceholder,releaseLoom,LOOM_PROGRAMS}=await import(new URL('render/mediaPlaceholders.js',root));
hooks.deregister();

function fixture(t,supportsDispose){
  const state={supportsDispose,renders:0,disposals:0,fields:[]};probe.current=state;
  if(!t.loomCanvasInstalled){
    t.loomCanvasInstalled=true;const previous=globalThis.OffscreenCanvas;
    globalThis.OffscreenCanvas=class{
      constructor(width,height){this.width=width;this.height=height;}
      getContext(){return{drawImage(){},save(){},restore(){}};}
    };
    t.after(()=>{globalThis.OffscreenCanvas=previous;});
  }
  return state;
}
const cleanup=state=>{for(const field of state.fields)field.dispose ||=()=>{};};

test('actual atlas initialization accepts legacy Loom and disposes modern Loom after copying all six faces',t=>{
  for(const modern of [false,true]){
    const state=fixture(t,modern);let lease;
    try{
      lease=acquireLoomAtlas(32);
      assert.equal(state.renders,6);assert.equal(state.disposals,modern?1:0);
      assert.equal(lease.texture.image.width,96);assert.equal(lease.texture.image.height,64);
      const peer=acquireLoomAtlas(32);assert.equal(peer.texture,lease.texture);peer.dispose();
      lease.dispose();lease.dispose();assert.equal(state.disposals,modern?1:0);
    }finally{cleanup(state);lease?.dispose();}
  }
});

test('actual animated displays initialize, update and dispose with either Loom helper revision',t=>{
  for(const modern of [false,true]){
    const state=fixture(t,modern),make=(w,h)=>new OffscreenCanvas(w,h);
    const display=createLoomDisplayFrames(make,'phone');
    try{
      assert.equal(state.renders,3);assert.equal(display.update(.2),true);assert.equal(state.renders,6);
      display.dispose();display.dispose();assert.equal(state.disposals,modern?1:0);
    }finally{cleanup(state);display.dispose();}
  }
});

test('actual media placeholders release legacy and modern Loom and can acquire a fresh renderer afterward',t=>{
  for(const modern of [false,true]){
    const state=fixture(t,modern);
    try{
      for(let i=0;i<2;i++){
        const placeholder=createPlaceholder(LOOM_PROGRAMS[i],32,[[1,0,1],[1,1,1],[0,0,0]]);
        assert.equal(placeholder.draw(0),true);assert.equal(placeholder.draw(0),false);
        releaseLoom();releaseLoom();
      }
      assert.equal(state.fields.length,2);assert.equal(state.renders,2);assert.equal(state.disposals,modern?2:0);
    }finally{cleanup(state);releaseLoom();}
  }
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';

// GPU and dialog construction are boundaries here; exercise the real owner lifecycle and UI.
const moduleUrl=source=>'data:text/javascript;base64,'+Buffer.from(source).toString('base64');
const pickerModule=moduleUrl('export const createTrackPicker=options=>globalThis.__coursePicker(options);');
const postModule=moduleUrl('export const createPost=(...args)=>globalThis.__coursePost(...args);');
const gpuModule=moduleUrl(`
export class WebGPURenderer {
  constructor(){this.backend={isWebGPUBackend:false,gl:{getExtension(){},getParameter(){return 'test-gpu';}}};this.info={render:{}};this.disposals=0;}
  async init(){} setPixelRatio(){} setSize(){} dispose(){this.disposals++;}
}
export class Group {} export class Mesh {} export class Sprite {} export class PlaneGeometry {}
export class NodeBuilder { getUniformBufferLimit(){return 65536;} }
`);
const hooks=registerHooks({resolve(specifier,context,next){
  const parent=context.parentURL||'';
  if(parent.endsWith('/hud/garage.js')&&specifier==='./trackPicker.js')return{url:pickerModule,shortCircuit:true};
  if(parent.endsWith('/render/renderer.js')&&specifier==='./post.js')return{url:postModule,shortCircuit:true};
  if((parent.endsWith('/render/renderer.js')||parent.endsWith('/render/instancePrograms.js'))&&specifier==='three/webgpu')return{url:gpuModule,shortCircuit:true};
  return next(specifier,context);
}});
const {garageUi}=await import('../hud/garage.js');
const {createRenderer}=await import('../render/renderer.js');
const {Q}=await import('../render/quality.js');
hooks.deregister();

function element(){
  const selectors=new Map(),classes=new Set();
  return {hidden:false,dataset:{},style:{setProperty(){}},children:[],listeners:{},
    classList:{add:k=>classes.add(k),remove:k=>classes.delete(k),contains:k=>classes.has(k),toggle(k,on){on?classes.add(k):classes.delete(k);}},
    setAttribute(){},append(child){this.children.push(child);},after(){},remove(){this.removed=true;},focus(){},
    querySelector(selector){if(!selectors.has(selector))selectors.set(selector,element());return selectors.get(selector);},
    addEventListener(name,callback){this.listeners[name]=callback;},
  };
}

test('course replacement keeps wardrobe DOM, updates course controls and retires the previous picker',t=>{
  const oldDocument=globalThis.document,pickers=[];
  globalThis.document={createElement:element,body:element()};
  globalThis.__coursePicker=options=>{
    const picker={options,open:false,disposed:false,refreshMedia(){},nav(){},show(){this.open=true;},dispose(){this.disposed=true;}};
    pickers.push(picker);return picker;
  };
  t.after(()=>{globalThis.document=oldDocument;delete globalThis.__coursePicker;});
  const choices=[],ui=garageUi({change(){},select(){},customize(){},action(){},hover(){},changeTrack:id=>choices.push(id)});
  const root=ui.root,outfits=root.querySelector('.g-outfits'),karts=root.querySelector('.g-positions');
  const outfitButtons=outfits.children.slice(),kartButtons=karts.children.slice();
  root.classList.add('g-course-leaving');
  ui.setCourse({trackId:'keep',track:'The Keep',laps:2,online:false});
  assert.equal(ui.root,root);assert.deepEqual(outfits.children,outfitButtons);assert.deepEqual(karts.children,kartButtons);
  assert.equal(pickers[0].disposed,true);assert.equal(pickers[1].options.trackId,'keep');
  assert.equal(root.querySelector('.g-course-name').textContent,'The Keep');
  assert.equal(root.querySelector('.g-track').textContent,'The Keep · 2 laps');
  assert.equal(root.querySelector('[data-g=online]').hidden,true);
  assert.equal(root.classList.contains('g-course-leaving'),false);
  root.listeners.click({target:{closest:()=>({dataset:{g:'tracks'}})}});
  assert.equal(ui.trackPicking,true);assert.equal(pickers[1].open,true);
  pickers[1].options.change('boudoir');assert.deepEqual(choices,['boudoir']);
  ui.setCourse({trackId:'boudoir',track:'Boudoir',laps:3,online:true});
  assert.equal(pickers[1].disposed,true);assert.equal(pickers[2].options.trackId,'boudoir');
  assert.equal(root.querySelector('[data-g=online]').hidden,false);
  ui.dispose();assert.equal(pickers[2].disposed,true);assert.equal(root.removed,true);
});

test('course replacement retains one post pipeline, renderer identity and options until antialiasing changes',async t=>{
  const posts=[];
  globalThis.__coursePost=(three,scene,camera)=>{
    const post={three,scene,camera,antialias:Q.antialias,disposals:0,retargets:0,
      setScene(scene,camera){this.scene=scene;this.camera=camera;this.retargets++;},
      detachScene(){this.scene=null;this.camera=null;},dispose(){this.disposals++;}};
    posts.push(post);return post;
  };
  t.after(()=>delete globalThis.__coursePost);
  const renderer=await createRenderer({clientWidth:800,clientHeight:600,getContext:()=>null},
    {forceRenderer:'webgl2',options:{quality:'high',frameCap:30,renderScale:.75}});
  const gpu=renderer.three,sceneA={},sceneB={},cameraA={},cameraB={},options=renderer.options;
  const first=renderer.post(sceneA,cameraA);
  assert.equal(renderer.releaseScene(sceneB),false);
  assert.equal(renderer.releaseScene(sceneA,cameraB),false);
  assert.equal(first.disposals,0);assert.equal(renderer.post(sceneA,cameraA),first);
  assert.equal(renderer.releaseScene(sceneA,cameraA),true);
  assert.equal(first.disposals,0);assert.equal(first.scene,null);assert.equal(renderer.releaseScene(sceneA),false);
  const second=renderer.post(sceneB,cameraB);
  assert.equal(second,first);assert.equal(posts.length,1);assert.equal(second.scene,sceneB);assert.equal(second.camera,cameraB);
  assert.equal(second.retargets,1);
  assert.equal(renderer.three,gpu);assert.equal(gpu.disposals,0);assert.deepEqual(renderer.options,options);
  assert.equal(renderer.releaseScene(sceneA),false);assert.equal(second.disposals,0);
  renderer.beginCourse();assert.deepEqual(renderer.options,options);assert.equal(renderer.tier,'high');
  assert.equal(renderer.post(sceneB,cameraB),first,'timing reset preserves the existing pipeline');
  renderer.setOptions({quality:'phone'});
  const third=renderer.post(sceneB,cameraB);assert.notEqual(third,first);assert.equal(first.disposals,1);
  assert.equal(renderer.releaseScene(sceneB),true);assert.equal(third.disposals,0);
  renderer.dispose();assert.equal(gpu.disposals,1);assert.equal(third.disposals,1);
});

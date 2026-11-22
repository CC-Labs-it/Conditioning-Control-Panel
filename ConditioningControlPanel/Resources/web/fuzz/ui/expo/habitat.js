import {BY_ID} from '../../data/items.js';
import {LLAMAS} from '../../data/content/llamas.js';
import {livePanel,placard,terminal} from './display.js';

// Purchases retain their real order. The admissions inventory and final counters are recorded separately.
export function habitatReel(run,file){
  const pages=[['The first press',file.name,file.taps?'I AGREE was pressed.':'No manual presses recorded.']];
  const order=(run.order||[]).filter(id=>id!=='start'&&BY_ID[id]);
  const fate=order.filter(id=>BY_ID[id].fate);
  for(let i=0;i<order.length;i+=3)pages.push(['Acquisitions',...order.slice(i,i+3).map(id=>BY_ID[id].name)]);
  if(fate.length)pages.push(['Fate cards',...fate.map(id=>BY_ID[id].name)]);
  const herd=LLAMAS.filter(llama=>file.herd.includes(llama.id));
  for(let i=0;i<herd.length;i+=3)pages.push(['Admission inventory',...herd.slice(i,i+3).map(llama=>llama.name)]);
  pages.push(['The last return',file.taps.toLocaleString()+' presses',file.total.toLocaleString()+' Yes'],
    ['Spit in the Llama wormhole?','YES']);
  return pages;
}

export function humanHabitat(api,state,pressRegister){
  const {THREE:T,camera,renderer,walker,ctx,go,register}=api;
  const reel=habitatReel(ctx.state||{},state.file),seat=new T.Vector3(19.66,1.19,11.60),standing=new T.Vector3(),monitor=new T.Vector3(18.3,1.4,11.05);
  let view=null,elapsed=0,speed=1,still=0,pending=0,frame=-1,caught=false,seated=false,insidePresses=0,tapAge=9,tokenAge=9,liveTime=0;
  function sit(){
    if(seated){seated=false;api.say('Back on your feet.');return;}
    seated=true;still=0;standing.copy(walker.feet);camera.position.copy(seat);camera.lookAt(18.78,.89,11.35);
    state.ticket.stamp('Kept');state.ticket.book('keeper-chair','Sat at the habitat desk',true);
    api.cue('v4_keeper',{kind:'beat',exhibit:'habitat desk'});api.play('press');
  }
  function desk(){
    if(!seated){api.say('The desk is operated from the chair.');return;}
    if(state.hoofWorn){api.say('This button needs a finger.');api.cue('v4_press_hoof');return;}
    pressRegister();insidePresses++;still=0;tapAge=0;api.play('press');
    // Four hooves strike the same pane together.
    api.play('glass',.31,-.24);api.play('glass',.19,.35);
    if(insidePresses%10===0){tokenAge=0;api.play('feed');api.say('One Yes in the feeding slot.');}
  }
  function mount(id,group){
    if(id!=='habitat'&&id!=='service')return ()=>{};
    const undo=[];
    if(id==='habitat'){
      const obj=name=>group.getObjectByName(name),raw=obj('habitat_display'),face=raw?.isMesh?raw:raw?.getObjectByProperty('isMesh',true);
      const panel=face&&livePanel(T,face,{width:1152,height:660,emission:.38}),plaqueRaw=obj('habitat_plaque'),plaqueFace=plaqueRaw?.isMesh?plaqueRaw:plaqueRaw?.getObjectByProperty('isMesh',true);
      const plaque=plaqueFace&&livePanel(T,plaqueFace);
      const target=new T.WebGLRenderTarget(192,108,{depthBuffer:true});
      target.texture.colorSpace=T.SRGBColorSpace;target.texture.repeat.y=-1;target.texture.offset.y=1;
      const eyeMat=new T.MeshBasicMaterial({map:target.texture,toneMapped:false}),normalMat=face?.material;
      const strip=document.createElement('canvas');strip.width=512;strip.height=54;
      const ink=strip.getContext('2d'),stripTexture=new T.CanvasTexture(strip);
      stripTexture.colorSpace=T.SRGBColorSpace;
      const surface=new T.MeshBasicMaterial({map:stripTexture,depthTest:false,depthWrite:false,toneMapped:false});
      const overlay=new T.Scene(),eye=new T.OrthographicCamera(-1,1,1,-1,0,1),plate=new T.Mesh(new T.PlaneGeometry(2,.26),surface);
      plate.position.set(0,-.87,-.5);overlay.add(plate);
      const stateView=view={panel,face,target,eyeMat,shadows:[],feed:null,overlay,eye,strip:()=>{
        ink.fillStyle='#16392e';ink.fillRect(0,0,512,54);ink.fillStyle='#f9f1d7';ink.font='bold 31px system-ui';
        ink.fillText('LIVE   /   FIRST PRESS   '+(state.file.taps+state.presses).toLocaleString(),12,38,488);stripTexture.needsUpdate=true;
      }};
      undo.push(()=>{stripTexture.dispose();surface.dispose();plate.geometry.dispose();});
      function clause(){placard(plaque,'Accepted clauses   /   Visitor record',state.nextClause());}
      clause();frame=-1;
      for(const [name,label,fn] of [
        ['habitat_glass','Tap the glass',()=>{
          state.taps++;speed=Math.min(24,1+state.taps*1.75);pending=Math.min(12,pending+3.5);
          api.play('glass');state.ticket.book('hab-glass','Tapped the habitat glass',true);
          api.cue(({1:'hab_2',3:'hab_3'})[state.taps],{exhibit:'habitat glass'});
        }],
        ['feeding_slot','Feed one Yes',()=>{state.feeds++;state.ticket.stamp('Fed');state.ticket.book('hab-feed','Fed the exhibit');api.play('feed');api.cue('hab_5');}],
        ['habitat_plaque','Read next accepted clause',clause]
      ]){const mesh=obj(name);if(mesh)undo.push(register(mesh,label,fn,2.65));}
      undo.push(()=>{if(face)face.material=normalMat;panel?.dispose();plaque?.dispose();target.dispose();eyeMat.dispose();if(view===stateView)view=null;seated=false;});
    }
    undo.push(api.annexProps(group,place=>{
      const off=[],outside=id==='service';
      const door=place('annex_keeper_door',outside?[16,0,33.04]:[22.98,0,13.58],Math.PI);
      off.push(register(door,outside?'Enter the keeper passage':'Return to the service corridor',()=>{
        seated=false;return outside?go('habitat',[22.62,0,12.68],Math.PI/2):go('service',[16,0,31.83],0);
      },2.85));
      if(!outside){
        const cushion=place('annex_seat',[19.66,.53,11.60]);
        const cap=place('annex_desk_cap',[18.78,.84,11.35]);
        off.push(register(cushion,()=>seated?'Stand up':'Sit at the habitat desk',sit,2.2));
        off.push(register(cap,'Press I AGREE',desk,2.1));
        for(let i=0;i<4;i++){
          const shadow=place('annex_shadow',[17.1+i*.62,1.02,9.522]);
          shadow.visible=false;view.shadows.push(shadow);
        }
        view.feed=place('annex_token',[21.25,.78,8.67]);view.feed.visible=false;
      }
      return ()=>off.forEach(f=>f());
    }));
    return ()=>undo.reverse().forEach(f=>f());
  }
  function tick(dt,id){
    if(!view||id!=='habitat'){seated=false;return;}
    state.habitatTime+=dt;tapAge+=dt;tokenAge+=dt;
    if(seated){
      if(walker.feet.distanceToSquared(standing)>.0016||walker.airborne)seated=false;
      else{
        camera.position.copy(seat);still+=dt;
        if(still>=30&&state.ticket.book('hab-still','Considered the desk in silence',true))api.cue('v4_still_human',{kind:'beat',exhibit:'habitat desk'});
      }
    }
    for(const [i,n] of view.shadows.entries()){
      n.visible=insidePresses>0;
      const reach=Math.min(1,insidePresses/8);
      n.scale.setScalar(.35+reach*.55);n.position.y=.78+reach*.38+(tapAge<.3?Math.sin(tapAge/.3*Math.PI)*.018:0);
      n.position.x=18.6+(i-1.5)*(.65-.20*reach);
    }
    if(view.feed){view.feed.visible=insidePresses>=10;view.feed.position.y=.80+.27*Math.max(0,1-tokenAge/.55)**2;}
    if(!caught){
      elapsed+=dt*speed+pending;pending=0;
      const index=Math.min(reel.length,Math.floor(elapsed/4));
      if(index<reel.length){
        if(frame!==index){frame=index;const [title,...rows]=reel[index];
          terminal(view.panel,{title,rows,eyebrow:'HUMAN HABITAT   /   '+state.file.name,foot:'First Press   '+(state.file.taps+state.presses).toLocaleString()+'      Record   '+(index+1)+' / '+reel.length+'      '+speed.toFixed(1)+'x'});}
        return;
      }
      caught=true;api.cue('v4_caught',{kind:'beat',exhibit:'habitat glass'});
    }
    if(!view.face)return;
    view.face.material=view.eyeMat;liveTime-=dt;
    if(liveTime>0||camera.position.distanceToSquared(monitor)>70)return;
    liveTime=.34;
    const previous=renderer.getRenderTarget(),visible=view.face.visible,clear=renderer.autoClear;
    try{
      view.face.visible=false;renderer.setRenderTarget(view.target);renderer.render(api.scene,camera);
      view.strip();renderer.autoClear=false;renderer.render(view.overlay,view.eye);
    }finally{renderer.autoClear=clear;renderer.setRenderTarget(previous);view.face.visible=visible;}
  }
  return {mount,tick};
}

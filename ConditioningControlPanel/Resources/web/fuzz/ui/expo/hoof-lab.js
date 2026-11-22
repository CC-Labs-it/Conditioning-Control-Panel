import { createExhibitBeat } from './speech-sequence.js';
import { livePanel } from './display.js';

const DISC_RADIUS=.24;
const PANELS={checkbox:{columns:2,rows:2,left:-.15,top:.50,dx:.30,dy:.24},
  pictures:{columns:3,rows:3,left:-.31,top:.56,dx:.20,dy:.19}};

// A cap clicks when the padded contact disc covers its centre.
export function hoofContacts(page,x,y){
  const grid=PANELS[page];
  const result=[];
  if(grid)for(let row=0;row<grid.rows;row++)for(let col=0;col<grid.columns;col++){
    const dx=x-grid.left-col*grid.dx,dy=y-grid.top+row*grid.dy;
    if(dx*dx+dy*dy<=DISC_RADIUS**2+1e-8)result.push(row*grid.columns+col);
  }
  return result;
}

export function hoofAttempt(count,misses){
  if(!count)return null;
  if(count===1)return {edge:true,misses,page:'pictures',lines:['v4_hoof_edge','hoof_5']};
  const tries=misses+1;
  if(tries>=3)return {misses:tries,page:'pictures',lines:['hoof_4','hoof_7','hoof_5']};
  return {misses:tries,page:'checkbox',lines:tries===1?[count===4?'hoof_3':'hoof_4']:[]};
}

const DESK=[-18.4,1.20,9.97];
const hoofButton=/^(?:press_main|press_stop|photo_release|alpaca_button|expo_(?:replay|review|control)_|exit_sale$)/;
export function needsFinger(name){return hoofButton.test(name);}

export function mountHoofLab(api,group,state,worn){
  const {THREE:T,camera,cue,register,play,say}=api;
  const undo=[],models={},switches={checkbox:[],pictures:[]};
  const hands=['hoof_hand_left','hoof_hand_right'].map(name=>group.getObjectByName(name)).filter(Boolean);
  const staff=group.getObjectByName('hoof_staff'),mosaic=group.getObjectByName('hoof_mosaic');
  const desk=group.getObjectByName('hoof_console'),screen=group.getObjectByName('hoof_display');
  const origin=new T.Vector3(...DESK),point=new T.Vector3(),normal=new T.Vector3(),plane=new T.Plane(),ray=new T.Ray();
  let face=null,clock=0,selection=state.hoofMask||0,capMask=0,assistant=false,dead=false;
  if(mosaic)mosaic.visible=false;
  group.traverse(node=>{if(node.name.startsWith('expo_press_hoof_checkbox_'))node.visible=false;});
  screen?.traverse(node=>{if(!face&&node.isMesh)face=node;});
  const display=face&&livePanel(T,face,{width:1024,height:560,emission:.48});
  const displayRows={
    idle:['Applied Hoof Sciences','Fit a pair of hooves from the desk.'],
    checkbox:['Tick one box','The circle shows where your hoof lands.'],
    pictures:['Select every square without a llama','Press Submit when your selection is complete.'],
    complete:['Application accepted','The hooves can be returned to this desk.']
  };
  const beat=createExhibitBeat((id,opts)=>cue(id,{...opts,onStart:()=>{
    if(id==='hoof_7'){
      assistant=true;capMask=1;refresh();
    }
  }}));
  function refresh(){
    const page=state.hoof;
    if(staff)staff.visible=assistant;
    for(const hand of hands)hand.visible=!state.hoofWorn;
    for(const [key,model] of Object.entries(models))model.visible=(key==='pictures')===(page==='pictures'||page==='complete');
    for(const [key,items] of Object.entries(switches)){
      const mask=key==='pictures'?selection:capMask;
      items.forEach(({parts},index)=>{
        const on=(mask&(1<<index))!==0;
        for(const mat of parts){mat.emissive.setHex(on?0x9bdc74:0x000000);mat.emissiveIntensity=on ? .48 : 0;}
      });
    }
    const [title,hint]=displayRows[page]||displayRows.idle;
    display?.draw((g,w,h)=>{
      g.fillStyle='#123b36';g.fillRect(0,0,w,h);
      g.fillStyle='#a7ccb9';g.font='600 26px system-ui';g.fillText('HUMAN INTERFACE PROGRAMME',35,46);
      g.fillStyle='#fff1d2';g.font='bold 55px Georgia';g.fillText(title,35,110,w-70);
      g.fillStyle='#b7cfa7';g.fillRect(35,129,w-70,2);
      g.font='500 29px system-ui';g.fillText(hint,35,168,w-70);
    });
  }
  function run(lines,commit){
    return beat.run(lines,{zone:'hoof',exhibit:'hoof',position:origin},()=>{
      commit();refresh();
    });
  }
  function wear(){
    if(beat.listening)return;
    if(state.hoofWorn){
      state.hoofWorn=false;assistant=false;refresh();play('press');cue('v4_hoof_return');return;
    }
    worn.fit(hands);worn.light(group);
    state.hoofWorn=true;play('press');refresh();
    if(state.hoof==='idle')void run(['hoof_1','hoof_2'],()=>{state.hoof='checkbox';});
  }
  function useDesk(){
    if(beat.listening)return;
    if(state.hoof==='idle'&&state.hoofWorn){
      void run(['hoof_1','hoof_2'],()=>{state.hoof='checkbox';});return;
    }
    wear();
  }
  const deskLabel=()=>beat.listening?'Listening':state.hoofWorn?
    (state.hoof==='idle'?'Begin the test':'Return the hooves'):'Put on the hooves';
  for(const mesh of [...hands,desk,screen].filter(Boolean))undo.push(register(mesh,deskLabel,useDesk,2.8));
  function operate(key,hit){
    if(beat.listening||dead)return;
    if(!state.hoofWorn||state.hoof==='idle'){useDesk();return;}
    if(state.hoof!==key)return;
    const local=models[key].worldToLocal(point.copy(hit.point));
    const caps=hoofContacts(key,local.x,local.y);
    if(!caps.length){say('Aim the circle over a cap.');return;}
    worn.press();play('press');
    const mask=caps.reduce((bits,index)=>bits|(1<<index),0);
    for(const index of caps)switches[key][index].at=clock;
    if(key==='pictures'){
      selection^=mask;state.hoofMask=selection;refresh();return;
    }
    capMask=mask;refresh();
    const step=hoofAttempt(caps.length,state.misses);
    void run(step.lines,()=>{
      if(step.edge)state.ticket.book('hoof-edge','Off the edge',true);
      state.misses=step.misses;state.hoof=step.page;
      if(step.page==='pictures'){selection=0;state.hoofMask=0;}
    });
  }
  function submit(){
    if(beat.listening||state.hoof!=='pictures')return;
    if(!state.hoofWorn){wear();return;}
    worn.press();play('press');
    if(selection){void run(['v4_hoof_again'],()=>{});return;}
    void run(['v4_hoof_empty','hoof_8'],()=>{
      state.hoof='complete';state.ticket.stamp('Hoofed');state.ticket.book('hoof-empty','None selected',false);
    });
  }
  function attach(place){
    const off=[],restore=[];
    for(const [key,template] of [['checkbox','hoof_panel'],['pictures','hoof_pictures']]){
      const model=models[key]=place(template,DESK,Math.PI);
      if(!model)continue;
      const count=key==='checkbox'?4:9,prefix=key==='checkbox'?'hoof_cap_':'hoof_tile_';
      for(let i=0;i<count;i++){
        const node=model.getObjectByName(prefix+i);
        const parts=[];
        node?.traverse(mesh=>{
          if(!mesh.isMesh)return;
          const original=mesh.material;
          const copy=mat=>{
            if(!mat.emissive)return mat;
            const finish=mat.clone();parts.push(finish);return finish;
          };
          mesh.material=Array.isArray(original)?original.map(copy):copy(original);
          restore.push(()=>{mesh.material=original;});
        });
        switches[key].push({node,parts,rest:node?.position.z||0,at:-10});
      }
      off.push(register(model,()=>beat.listening?'Listening':!state.hoofWorn||state.hoof==='idle'?deskLabel():
        state.hoof==='complete'?'Test complete':key==='pictures'?'Mark squares under hoof':'Press caps under hoof',hit=>operate(key,hit),2.8));
      if(key==='pictures'){
        const button=model.getObjectByName('hoof_submit');
        if(button)off.push(register(button,()=>beat.listening?'Listening':state.hoof==='complete'?'Test complete':'Submit selection',submit,2.8));
      }
    }
    refresh();
    return ()=>{
      for(const f of off)f();
      for(const f of restore)f();
      for(const items of Object.values(switches))for(const item of items)for(const m of item.parts)m.dispose();
    };
  }
  undo.push(api.props(group,attach));
  const ring=new T.Mesh(new T.RingGeometry(DISC_RADIUS-.010,DISC_RADIUS,48),
    new T.MeshBasicMaterial({color:0xffe1a1,transparent:true,opacity:.89,side:T.DoubleSide,depthWrite:false}));
  const tint=new T.Mesh(new T.CircleGeometry(DISC_RADIUS,48),
    new T.MeshBasicMaterial({color:0xf1bd79,transparent:true,opacity:.12,side:T.DoubleSide,depthWrite:false}));
  ring.raycast=tint.raycast=()=>{};
  ring.name='hoof_contact_disc';ring.add(tint);ring.visible=false;
  function leave(){
    beat.leave();ring.visible=false;
    assistant=false;
  }
  function tick(dt){
    clock+=dt;
    ring.visible=false;
    if(camera.position.distanceToSquared(origin)>36){leave();return;}
    const key=state.hoof;
    const model=models[key];
    if(!state.hoofWorn||!model)return;
    model.updateWorldMatrix(true,false);
    // Draw the footprint on the plane of the caps, including its overhang past the rim.
    normal.set(0,0,1).transformDirection(model.matrixWorld);
    plane.setFromNormalAndCoplanarPoint(normal,model.localToWorld(point.set(0,0,.14)));
    ray.origin.copy(camera.position);camera.getWorldDirection(ray.direction);
    if(ray.direction.dot(normal)<-.1 && ray.intersectPlane(plane,point)&&point.distanceToSquared(camera.position)<=2.8**2){
      model.worldToLocal(point);
      if(Math.abs(point.x)<=.66 && point.y>=-.03 && point.y<=.78){
        if(ring.parent!==model)model.add(ring);
        ring.position.set(point.x,point.y,.143);ring.visible=true;
      }
    }
    for(const item of switches[key])if(item.node){
      const t=Math.min(1,(clock-item.at)/.22);
      item.node.position.z=item.rest-.012*Math.sin(Math.PI*t);
    }
  }
  refresh();
  return {tick,leave,dispose(){
    dead=true;leave();ring.removeFromParent();
    for(const obj of [ring,tint]){obj.geometry.dispose();obj.material.dispose();}
    for(const fn of undo.reverse())fn?.();
    display?.dispose();
  }};
}

// These are the fitted room gloves. Keep their own small maps when the lab streams out.
export function createWornHooves({THREE:T,scene,camera,ctx},state){
  const gloves=[],resources=new Set(),surfaces=new Set(),materials=new Map(),textures=new Map(),geometries=new Map();
  const grip=new T.Quaternion().setFromEuler(new T.Euler(2.46,Math.PI,0,'XYZ'));
  let stroke=-10,clock=0;
  function copyTexture(source){
    if(textures.has(source))return textures.get(source);
    const bitmap=source.image;
    const canvas=document.createElement('canvas');
    const ratio=Math.min(1,512/Math.max(bitmap.width,bitmap.height));
    canvas.width=Math.max(1,Math.round(bitmap.width*ratio));canvas.height=Math.max(1,Math.round(bitmap.height*ratio));
    canvas.getContext('2d').drawImage(bitmap,0,0,canvas.width,canvas.height);
    const copy=source.clone();
    copy.source=new T.Source(canvas);copy.needsUpdate=true;
    resources.add(copy);textures.set(source,copy);return copy;
  }
  function copyMaterial(source){
    if(materials.has(source))return materials.get(source);
    const copy=source.clone();
    for(const key of Object.keys(copy)){
      if(key==='lightMap'||key==='envMap')copy[key]=null;
      else if(copy[key]?.isTexture)copy[key]=copyTexture(copy[key]);
    }
    resources.add(copy);surfaces.add(copy);materials.set(source,copy);return copy;
  }
  function fit(hands){
    if(gloves.length)return;
    for(const hand of hands){
      const glove=hand.clone(true);
      glove.traverse(node=>{
        if(!node.isMesh)return;
        if(!geometries.has(node.geometry)){
          const geometry=node.geometry.clone();geometries.set(node.geometry,geometry);resources.add(geometry);
        }
        node.geometry=geometries.get(node.geometry);
        node.material=Array.isArray(node.material)?node.material.map(copyMaterial):copyMaterial(node.material);
      });
      glove.name='worn_'+hand.name;glove.visible=false;glove.scale.setScalar(.74);
      scene.add(glove);gloves.push(glove);
    }
    // Source assets belong to the room and may be discarded as soon as the player leaves.
    textures.clear();geometries.clear();materials.clear();
  }
  function light(group){
    if(!gloves.length||!group)return;
    let probe=null;group.traverse(node=>{
      if(!probe&&node.isMesh)probe=(Array.isArray(node.material)?node.material[0]:node.material)?.envMap;
    });
    for(const material of surfaces){
      if(material.envMap!==probe){material.envMap=probe;material.needsUpdate=true;}
    }
  }
  function tick(dt){
    clock+=dt;
    const duration=ctx.gentle||ctx.reduced ? .72 : .46;
    const reach=Math.sin(Math.PI*Math.min(1,(clock-stroke)/duration));
    const spread=Math.min(.34,.24*camera.aspect);
    gloves.forEach((glove,i)=>{
      glove.visible=state.hoofWorn;
      if(!glove.visible)return;
      const extension=i===1?reach:0;
      glove.position.set(i===0?-spread:spread*(1-.91*extension),-.245+.21*extension,-.57-.17*extension)
        .applyQuaternion(camera.quaternion).add(camera.position);
      glove.quaternion.copy(camera.quaternion).multiply(grip);
    });
  }
  return {fit,light,tick,press(){stroke=clock;},dispose(){
    gloves.forEach(glove=>glove.removeFromParent());for(const resource of resources)resource.dispose();
  }};
}

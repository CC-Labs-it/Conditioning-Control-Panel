import {hoofSupport} from './carried.js';
import {livePanel,placard,printedPanel} from './display.js';
import {dial,write} from './staff-fixtures.js';
import {board,boardHit,dealHoofPhotos,hoofRadius,markHoof,verifyHoof} from './hoof-board.js';
import {hoofRefusals} from './hoof-refusals.js';
import {loadHoofPhotos,paintHoofScreen} from './hoof-screen.js';

const DESK=[-18.4,1.198,9.97];
const threshold='Hooves may leave the lab. Your fingers stay at the desk. Llama doors will open.';
function firstMesh(node){
  let face;node?.traverse(n=>{if(!face&&n.isMesh)face=n;});return face;
}

export function mountHoofLab(api,group,state,worn){
  const {THREE:T,camera,register}=api,clean=[];
  const get=name=>group.getObjectByName(name),hands=['hoof_hand_left','hoof_hand_right'].map(get).filter(Boolean);
  const desk=get('hoof_console'),face=firstMesh(get('hoof_display')),instructions=firstMesh(get('Art_hoof_instructions'));
  const panel=face&&livePanel(T,face,{width:board.width,height:board.height,emission:.48});
  if(panel)clean.push(()=>panel.dispose());
  for(const name of ['hoof_staff','hoof_mosaic']){const n=get(name);if(n)n.visible=false;}
  group.traverse(n=>{if(n.name.startsWith('expo_press_hoof_checkbox_'))n.visible=false;});
  // The two action-ink finishes in the room shell are the four retired switch surrounds.
  get('Architecture')?.traverse(n=>{if(n.isMesh&&['action_ink','action_cream'].includes(n.material?.name))n.visible=false;});
  if(instructions){
    const directions=livePanel(T,instructions,{width:1024,height:496,emission:.11});clean.push(()=>directions.dispose());
    placard(directions,'Applied Hoof Sciences','Put the hooves on. Take the picture test.','Hooves and returns at the brass desk.');
  }
  const pending=[],origin=new T.Vector3(...DESK);
  let speaking='';
  function voice(id){if(id!==speaking&&!pending.includes(id))pending.push(id);}
  function forget(id){const i=pending.indexOf(id);if(i>=0)pending.splice(i,1);}
  function speak(){
    if(!pending.length||speaking||!api.narrationAvailable?.()||camera.position.distanceToSquared(origin)>16)return;
    const id=pending.shift();speaking=id;
    void api.cue(id,{zone:'hoof',kind:'beat',exhibit:'hoof',position:DESK,replay:true}).finally(()=>{speaking='';});
  }
  let clock=0,dead=false,button,cap,capRest=0,pressed=-10,contactUntil=-1,lastAim=null,photos;
  let message=state.hoofMessage||null;
  function feedback(value){state.hoofMessage=message=value;}
  function refresh(){
    if(dead)return;
    if(state.hoof==='pictures'&&!photos)photos=loadHoofPhotos(api.assetURL,refresh);
    for(const glove of hands)glove.visible=!state.hoofWorn;
    if(button)button.visible=state.hoof==='idle'||state.hoof==='practice';
    paintHoofScreen(panel,state,photos,message);
  }
  function wear(){
    if(state.hoofWorn){
      state.hoofWorn=false;pending.length=0;feedback(null);api.play('press');voice('v4_hoof_return');
    }else{
      worn.fit(hands);state.hoofWorn=true;feedback(null);worn.press();
      if(state.hoof==='idle'){state.hoof='practice';voice('hoof_1');}
    }
    refresh();
  }
  if(state.hoof==='checkbox')state.hoof='practice';
  if(state.hoof==='pictures'&&state.hoofDeal?.length!==9){state.hoofDeal=dealHoofPhotos();state.hoofMask=0;}
  const deskLabel=()=>state.hoofWorn?'Return the hooves':'Put on the hooves';
  for(const n of [...hands,desk].filter(Boolean))clean.push(register(n,deskLabel,wear,3));
  function practice(){
    if(!state.hoofWorn){api.play('press',.42);api.say('Put the hooves on at the desk first.');return;}
    if(state.hoof!=='practice')return;
    forget('hoof_1');pressed=clock;worn.press();state.hoof='pictures';state.hoofDeal=dealHoofPhotos();state.hoofMask=0;
    feedback(null);refresh();voice('r5_hoof_verify');
  }
  clean.push(api.props(group,place=>{
    const off=[];button=place('moved_button',DESK,Math.PI);button.name='hoof_practice_button';button.scale.setScalar(1.5);
    const price=button.getObjectByName('moved_price');if(price)price.visible=false;
    cap=button.getObjectByName('moved_cap');capRest=cap?.position.y||0;
    let paper;cap?.traverse(n=>{if(n.isMesh&&n.material?.name==='Print')paper=n;});
    if(paper){
      const ink=printedPanel(T,paper,{width:384,height:128,emission:.16});off.push(()=>ink.dispose());
      ink.draw((g,w,h)=>{g.fillStyle='#f6e8c9';g.fillRect(0,0,w,h);g.fillStyle='#234c3d';g.textAlign='center';
        g.font='bold 88px Georgia';g.fillText('PRESS',w/2,97,w-20);});
    }
    off.push(register(button,'Press the large practice button',practice,3));refresh();
    return ()=>off.reverse().forEach(f=>f());
  }));
  clean.push(api.annexProps(group,place=>{
    const off=[],sign=place('annex_record',[-12.114,1.32,9.16],-Math.PI/2);sign.scale.set(2.15,.95,1);
    write(dial(api,sign,'record_face',off,720,360),'Leaving Hoof Sciences',threshold);
    off.push(register(sign,'Read the hoof notice',()=>api.say(threshold),2.6));
    return ()=>off.reverse().forEach(f=>f());
  }));
  const radius=hoofRadius*board.pitch*2.33/board.width;
  const ring=new T.Mesh(new T.RingGeometry(radius-.011,radius,64),
    new T.MeshBasicMaterial({color:0xffe2a0,side:T.DoubleSide,transparent:true,opacity:.94,depthWrite:false}));
  const fill=new T.Mesh(new T.CircleGeometry(radius,64),
    new T.MeshBasicMaterial({color:0xffdf91,side:T.DoubleSide,transparent:true,opacity:.12,depthWrite:false}));
  ring.name='hoof_contact_disc';ring.raycast=fill.raycast=()=>{};ring.add(fill);ring.visible=false;group.add(ring);
  const rc=new T.Raycaster(),centre=new T.Vector2(),strikes=[],touchAt=new T.Vector3();
  rc.far=3.1;
  function drawRing(point,onDesk){
    ring.position.copy(point);group.worldToLocal(ring.position);
    if(onDesk){ring.rotation.set(-Math.PI/2,0,0);ring.position.y+=.003;ring.scale.setScalar(.77);}
    else{ring.rotation.set(0,Math.PI,0);ring.position.z-=.009;ring.scale.setScalar(1);}
    ring.visible=true;
  }
  function submit(){
    const attempt=verifyHoof(state);
    if(!attempt)return;
    forget('r5_hoof_verify');
    if(attempt.correct){
      pending.length=0;state.ticket.stamp('Hoofed');api.play('chime');feedback(null);
      if(attempt.edge){state.ticket.book('hoof-edge','Off the edge',true);voice('v4_hoof_edge');}
      else {state.ticket.book('hoof-human','Passed the picture test with a finger');voice('hoof_8');}
      api.say('Test complete. Hoofed stamp issued.');
    }else{
      feedback({error:true});
      api.say(state.misses>=3&&state.hoofWorn?'Please try again. Return the hooves to the desk. One finger marks one square.':'Please try again.');
      voice(attempt.cue);
    }
    refresh();
  }
  function tap(hit){
    if(state.hoofWorn)worn.press();else api.play('press',.42);
    if(state.hoof!=='pictures'||!hit.uv){
      api.say(state.hoof==='complete'?'Test complete. Hoofed stamp issued.':
        state.hoof==='practice'?'Press the large practice button on the desk.':'Put the hooves on at the desk.');return;
    }
    const at=boardHit(hit.uv.x,hit.uv.y,state.hoofWorn);
    if(!at){api.say('Aim at a picture or press Verify.');feedback({contact:0});refresh();return;}
    if(at.verify){submit();return;}
    const count=markHoof(state,at.mask);
    touchAt.copy(hit.point);contactUntil=clock+.44;
    feedback({contact:count});refresh();
  }
  if(face)clean.push(register(face,()=>state.hoof==='pictures'?(lastAim?.verify?'Verify':lastAim?'Mark picture squares':'Read the instructions'):
    state.hoof==='complete'?'Read the test result':'Read the instructions',tap,3.1));
  refresh();
  function tick(dt){
    clock+=dt;ring.visible=false;lastAim=null;speak();

    if(state.hoof!=='practice'&&state.hoof!=='pictures')return;
    const practicePage=state.hoof==='practice',target=practicePage?button:face;
    if(!target?.visible)return;
    if(cap)cap.position.y=capRest-.012*Math.max(0,1-(clock-pressed)/.18);
    rc.setFromCamera(centre,camera);strikes.length=0;rc.intersectObject(target,practicePage,strikes);
    const hit=strikes[0];
    if(hit?.uv&&!practicePage)lastAim=boardHit(hit.uv.x,hit.uv.y,state.hoofWorn);
    if(!state.hoofWorn)return;
    if(!practicePage&&clock<contactUntil){drawRing(touchAt,false);return;}
    if(hit&&(practicePage||lastAim&&!lastAim.verify))drawRing(hit.point,practicePage);
  }
  return {tick,leave(){ring.visible=false;contactUntil=-1;pending.length=0;},dispose(){
    dead=true;photos?.dispose();ring.removeFromParent();for(const n of [ring,fill]){n.geometry.dispose();n.material.dispose();}
    for(const f of clean.reverse())f();
  }};
}
// These are the fitted room gloves. Keep their own small maps when the lab streams out.
export function createWornHooves(api,state,lighting){
  const {THREE:T,scene,camera,ctx}=api;
  const gloves=[],resources=new Set(),surfaces=new Set(),materials=new Map(),textures=new Map(),geometries=new Map();
  const grip=new T.Quaternion().setFromEuler(new T.Euler(2.46,Math.PI,0,'XYZ'));
  let stroke=-10,clock=0,slipping=false,clops=0;
  const refusals=hoofRefusals(api,state,()=>strike(true));
  function strike(slip){
    stroke=clock;slipping=slip;
    const take=1+clops++%3;
    ctx.audio.play(api.assetURL('audio/step-wood-'+take+'.ogg'),{bus:'sfx',source:'expo-hooves',sourceLabel:'Hooves',gain:.72,rate:1.31});
  }
  let room,probe=null,lastStep;
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
    const copy=lighting.copy(source);
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
    light(room,probe);
  }
  function light(id,environment){
    room=id;probe=environment;lastStep=lighting.step(id);
    for(const material of surfaces){
      if(material.envMap!==probe){material.envMap=probe;material.needsUpdate=true;}
      lighting.shade(material,lastStep);
    }
  }
  function tick(dt){
    clock+=dt;refusals.tick(dt);
    if(lastStep!==lighting.step(room))light(room,probe);
    const duration=ctx.gentle||ctx.reduced ? .72 : .46;
    const reach=Math.sin(Math.PI*Math.min(1,(clock-stroke)/duration));
    const spread=Math.min(.34,.24*camera.aspect);
    gloves.forEach((glove,i)=>{
      glove.visible=state.hoofWorn;
      if(!glove.visible)return;
      const extension=i===1?reach:0;
      let scale=1;
      if(state.carry&&i===0){
        scale=hoofSupport(camera.aspect,glove.position);
        // The fitted glove's tip is 16.4 cm above its pivot at its normal size.
        glove.position.y-=.164*scale;
      }else{
        if(state.carry)scale=Math.min(1,camera.aspect);
        glove.position.set(i===0?-spread:spread*(1-.91*extension),-.245+.21*extension,-.57-.17*extension);
      }
      glove.scale.setScalar(.74*scale);
      if(slipping&&i===1)glove.position.x+=((ctx.gentle||ctx.reduced) ? .035 : .125)*extension;
      glove.position.applyQuaternion(camera.quaternion).add(camera.position);
      glove.quaternion.copy(camera.quaternion).multiply(grip);
      if(slipping&&i===1)glove.rotateZ(((ctx.gentle||ctx.reduced) ? .09 : .32)*extension);
    });
  }
  return {fit,light,tick,refuse:refusals.refuse,press(){strike(false);},dispose(){
    gloves.forEach(glove=>glove.removeFromParent());for(const resource of resources)resource.dispose();
  }};
}


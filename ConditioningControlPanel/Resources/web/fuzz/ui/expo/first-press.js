import {placard} from './display.js';

const original=[0,1.04,-9];
const stops=[['hall',[9.95,3.4,-5.3]],['hall',[-5.7,0,.66]],['shop',[16.25,1.235,-3.7]],['hall',[-11.48,0,-3.35]]];
const clamp=n=>Math.min(1,Math.max(0,n));

// Separate just the accession case from the baked architectural mesh so its plinth can descend.
function liftCase(T,room){
  const pieces=[],p=new T.Vector3(),inside=new T.Box3(new T.Vector3(-1.29,-.007,-10.29),new T.Vector3(1.29,1.77,-7.71));
  room.updateMatrixWorld(true);
  room.traverse(mesh=>{
    if(!mesh.isMesh||!['Architecture','Glass'].some(s=>mesh.name.startsWith(s)||mesh.parent?.name.startsWith(s)))return;
    const geom=mesh.geometry,idx=geom.index,pos=geom.attributes.position;if(!idx)return;
    const flags=new Uint8Array(pos.count);
    for(let i=0;i<flags.length;i++)flags[i]=Number(inside.containsPoint(p.fromBufferAttribute(pos,i).applyMatrix4(mesh.matrixWorld)));
    const yes=[],no=[];
    for(let i=0;i<idx.count;i+=3){const a=idx.getX(i),b=idx.getX(i+1),c=idx.getX(i+2);(flags[a]&&flags[b]&&flags[c]?yes:no).push(a,b,c);}
    if(!yes.length)return;
    const sub=new T.BufferGeometry();for(const [key,attr] of Object.entries(geom.attributes))sub.setAttribute(key,attr);sub.setIndex(yes);
    pieces.push({mesh,sub,idx,no});
  });
  const slide=new T.Group();room.add(slide);const children=[];
  for(const {mesh,sub,no} of pieces){const part=new T.Mesh(sub,mesh.material);mesh.add(part);slide.attach(part);mesh.geometry.setIndex(no);children.push(part);}
  return {slide,free(){for(const {mesh,idx,sub} of pieces){mesh.geometry.setIndex(idx);sub.dispose();}slide.removeFromParent();}};
}

export function firstPress(api,state){
  const {THREE:T,camera,cue}=api;
  const views=new Map(),projected=new T.Vector3();
  let moved=-1,pending=false,still=0,carriedPost=false,sealed=false,last=-1,near=false;
  const notices=['On the east mezzanine.','Behind a banner in the hall.','On a gift shop shelf.','Beside the dome door.'];
  function text(){return sealed?'Sealed by the visitor.':state.presses>=25?'DO PRESS. Believed voluntary.':moved>=0?'On loan. '+notices[moved]:state.pressRecord();}
  function draw(){for(const v of views.values())v.draw?.();}
  function press(stop=false){
    if(state.hoofWorn){cue('v4_press_hoof',{kind:'react',exhibit:'press'});return;}
    if(sealed || (stop && state.presses<25))return;
    if(!stop && state.presses>=25){api.say('DO PRESS. Register unchanged.');still=0;return;}
    api.play('press');++state.presses;
    if(stop){sealed=true;cue('v4_press_seal');}
    else if(state.presses===1){state.ticket.stamp('Pressed');state.ticket.book('first-press','Pressed the founding exhibit',true);cue('pa_hall1_press');}
    else if(state.presses===5)cue('v4_press_rope');
    else if(state.presses===10)cue('pa_hall1_ten');
    else if(state.presses===12){pending=true;cue('v4_press_moved');}
    else if(state.presses===25){pending=false;cue('v4_press_invert');}
    else if(moved>=0)pending=true;
    draw();
  }
  function mount(id,room,bind,surface,clean){
    if(id!=='hall'&&id!=='shop')return;
    const v={copies:[]};views.set(id,v);clean.push(()=>views.delete(id));
    if(id==='hall'){
      v.case=liftCase(T,room);clean.push(v.case.free);
      const readout=surface('press_readout');
      v.draw=()=>placard(readout,'Founding collection   /   First Press',text(),'REGISTER   '+(state.file.taps+state.presses).toLocaleString());
      v.main=bind('press_main',()=>state.presses>=12?'Read the loan notice':'Press I AGREE',()=>state.presses>=12?api.say(text()):press());
      v.stop=bind('press_stop','I AGREE TO STOP',()=>press(true));bind('press_readout','Read the register',()=>api.say(text()));
      v.draw();if(v.stop)v.stop.visible=false;
    }
    clean.push(api.props(room,place=>{
      const off=[];
      if(id==='hall'){
        v.posts=[-1,1].map(sign=>place('rope_post',[sign,0,-7.38]));
        v.span=place('rope_span',[0,0,-7.38]);v.span.scale.y=.46;
        v.note=place('rope_notice',[2.12,0,-7.42]);off.push(api.register(v.note,()=>state.carry?.key==='rope-post'?'Return the rope post':'Read the notice',()=>{if(api.drop?.('rope-post'))carriedPost=false;else api.say('Please respect the rope around the notice.');}));
        v.glass=place('sealed_glass',[-1.55,.984,-8.96]);
        const box=new T.Mesh(new T.BoxGeometry(2,.29,.055));box.name='First Press cord';box.position.set(0,.145,-7.38);
        api.walker.add('first-press-cord',box,[],'hall');box.geometry.dispose();box.material.dispose();
        off.push(()=>api.walker.remove('first-press-cord'));
        off.push(api.register(v.posts[0],'Carry the rope post',()=>{
          if(state.carry){api.say('Your hand is occupied.');return;}
          carriedPost=true;state.ticket.book('rope-post','Removed a rope post',true);cue('v4_post_taken');api.hold?.('rope-post',v.posts[0],'Rope post');
        }));
      }
      stops.forEach(([zone,at],i)=>{if(zone!==id)return;const obj=place('moved_button',at);obj.visible=false;
        const cap=obj.getObjectByName('moved_cap');off.push(api.register(obj,()=>state.presses>=25?'DO PRESS':'Press the moved button',()=>{press();v.spring=.15;},2.4));
        v.copies.push({obj,cap,i});});
      return ()=>off.reverse().forEach(f=>f());
    }));
  }
  function seen(at,id,zone){
    if(zone!==id)return false;projected.fromArray(at);projected.y+=.16;projected.project(camera);
    return projected.z>=-1&&projected.z<=1&&Math.abs(projected.x)<1.15&&Math.abs(projected.y)<1.15;
  }
  function tick(dt,zone){
    const pair=moved<0?['hall',original]:stops[moved],at=pair[1];
    if(pending&&!seen(at,pair[0],zone)){
      const next=(moved+1)%stops.length;if(!seen(stops[next][1],stops[next][0],zone)){moved=next;pending=false;draw();}
    }
    const hall=views.get('hall'),rope=state.presses>=5&&state.presses<12;
    if(hall){
      if(hall.main)hall.main.visible=moved<0;
      if(hall.stop)hall.stop.visible=state.presses>=25;
      if(hall.case){const y=moved>=0?-1.85:0;hall.case.slide.position.y+=(y-hall.case.slide.position.y)*Math.min(1,dt*3);}
      if(hall.posts){hall.posts.forEach((n,i)=>{n.visible=rope&&(i!==0||!carriedPost);});hall.span.visible=rope&&!carriedPost;hall.note.visible=rope;hall.glass.visible=sealed;}
      api.setColliderEnabled('First Press cord',rope&&!carriedPost);api.setColliderEnabled('Accession plinth',moved<0);
    }
    for(const v of views.values()){
      v.spring=Math.max(0,(v.spring||0)-dt);for(const {obj,i,cap} of v.copies){obj.visible=i===moved&&!sealed;if(cap)cap.position.y=-v.spring*.09;}
    }
    if(state.presses>=25&&!sealed&&moved>=0&&zone===pair[0]&&Math.hypot(camera.position.x-at[0],camera.position.z-at[2])<2.15&&Math.abs(camera.position.y-1.6-at[1])<1.55){
      still+=dt;if(still>=10){state.presses++;still-=10;state.ticket.book('did-not-press','Kept hands off DO PRESS');cue('v4_press_still');draw();}
    }else still=0;
    const byCase=zone==='hall'&&Math.hypot(camera.position.x,camera.position.z+9)<4.4;
    if(near&&!byCase&&!state.presses)state.ticket.book('left-button','Left the First Press untouched');
    near=byCase;
    if(last!==state.presses){last=state.presses;draw();}
  }
  return {mount,tick,press,get sealed(){return sealed;}};
}

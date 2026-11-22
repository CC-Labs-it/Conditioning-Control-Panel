import {placard,printedPanel} from './display.js';

const original=[0,1.04,-9];
const stops=[['hall',[9.95,3.4,-5.3]],['hall',[-5.7,0,.66]],['shop',[17.48,1.23,-3.7]],['hall',[-11.48,0,-3.35]]];
const clamp=n=>Math.min(1,Math.max(0,n));

// The original hall batches the stand, hand-hole glazing and furniture by finish.
function liftCase(T,room){
  const slide=new T.Group(),cover=new T.Group(),label=new T.Group(),p=new T.Vector3(),pieces=[];
  slide.name='Accession lift';cover.name='Hand-hole cover';label.name='Accession label stand';
  function assembly(finish,x,y,z){
    // Keep the complete register board clear of the original east-hand rope.
    if(x>1.20&&x<2.44&&y>=0&&y<1.29){
      if((finish==='ash'||finish==='brass')&&z> -8.707&&z< -8.624)return 3;
      if(finish==='marble'&&y<.096&&x>1.57&&x<2.07&&z> -8.87&&z< -8.47)return 3;
    }
    if(Math.abs(x)>1.28||Math.abs(z+9)>1.28||y<-.001||y>1.75)return 0;
    const radius=Math.hypot(x,z+9);if(radius>1.28)return 0;
    if(finish==='glass')return 2;
    if(finish==='marble'&&y<.967)return 1;
    if(finish!=='brass')return 0;
    if((y>1.10&&radius<.78)||(y>.958&&y<1.004&&radius>.73&&radius<.78))return 2;
    if((y<.951&&radius<1.17)||(y>.96&&radius<.44))return 1;
    return 0;
  }
  room.updateMatrixWorld(true);
  room.traverse(mesh=>{
    if(!mesh.isMesh||!['Architecture','Glass','Surface_glass'].some(s=>mesh.name.startsWith(s)||mesh.parent?.name.startsWith(s)))return;
    if(!['ash','brass','marble','glass'].includes(mesh.material.name))return;
    const geom=mesh.geometry,idx=geom.index,pos=geom.attributes.position;if(!idx)return;
    const flags=new Uint8Array(pos.count);
    for(let i=0;i<flags.length;i++){
      p.fromBufferAttribute(pos,i).applyMatrix4(mesh.matrixWorld);
      flags[i]=assembly(mesh.material.name,p.x,p.y,p.z);
    }
    const faces=[[],[],[],[]];
    for(let i=0;i<idx.count;i+=3){const a=idx.getX(i),b=idx.getX(i+1),c=idx.getX(i+2);
      faces[flags[a]===flags[b]&&flags[a]===flags[c]?flags[a]:0].push(a,b,c);}
    if(faces[0].length!==idx.count)pieces.push({mesh,idx,faces});
  });
  room.add(slide,label);slide.add(cover);
  const groups=[null,slide,cover,label],parts=[];
  for(const {mesh,faces} of pieces){
    mesh.geometry.setIndex(faces[0]);
    for(let i=1;i<faces.length;i++){if(!faces[i].length)continue;
      const sub=new T.BufferGeometry();for(const [key,attr] of Object.entries(mesh.geometry.attributes))sub.setAttribute(key,attr);
      sub.setIndex(faces[i]);sub.boundingBox=new T.Box3();
      for(const j of faces[i])sub.boundingBox.expandByPoint(p.fromBufferAttribute(sub.attributes.position,j));
      sub.boundingSphere=sub.boundingBox.getBoundingSphere(new T.Sphere());
      const part=new T.Mesh(sub,mesh.material);mesh.add(part);groups[i].attach(part);parts.push(sub);}
  }
  const main=room.getObjectByName('press_main'),readout=room.getObjectByName('press_readout');
  const mainParent=main?.parent,readoutParent=readout?.parent;
  if(main)slide.attach(main);if(readout)label.attach(readout);
  label.position.x=1.18;
  return {slide,cover,free(){
    label.position.x=0;slide.position.y=0;
    if(main)mainParent.attach(main);if(readout)readoutParent.attach(readout);
    for(const {mesh,idx} of pieces)mesh.geometry.setIndex(idx);
    for(const sub of parts)sub.dispose();slide.removeFromParent();label.removeFromParent();
  }};
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
    if(!stop&&!carriedPost&&state.presses>=5&&state.presses<12){
      if(camera.position.z>=-7.56){api.say('Jump the cord or carry a rope post.');return;}
      state.ticket.book('past-rope','Crossed the accession rope',true);
    }
    if(!stop && state.presses>=25){api.say('DO PRESS. Register unchanged.');still=0;return;}
    api.play('press');++state.presses;
    if(stop){sealed=true;cue('v4_press_seal');}
    else if(state.presses===1){state.ticket.stamp('Pressed');state.ticket.book('first-press','Pressed the founding exhibit',true);cue('pa_hall1_press');}
    else if(state.presses===5)cue('v4_press_rope');
    else if(state.presses===10)cue('pa_hall1_ten');
    else if(state.presses===12){pending=true;cue('v4_press_moved');}
    else if(state.presses===25){pending=false;cue('v4_press_invert');}
    else if(moved>=0){pending=true;cue('v4_press_found');}
    draw();
  }
  function mount(id,room,bind,surface,clean){
    if(id!=='hall'&&id!=='shop')return;
    const v={room,copies:[]};views.set(id,v);clean.push(()=>views.delete(id));
    if(id==='hall'){
      v.case=liftCase(T,room);v.case.slide.position.y=moved>=0&&!sealed?-1.85:0;v.case.cover.visible=!sealed;clean.push(v.case.free);
      const readout=surface('press_readout');
      v.draw=()=>placard(readout,'Founding collection   /   First Press',text(),'REGISTER   '+(state.file.taps+state.presses).toLocaleString());
      v.main=bind('press_main',()=>sealed?'Read the sealed accession':state.presses>=12?'Read the loan notice':'Press I AGREE',()=>state.presses>=12?api.say(text()):press());
      v.stop=bind('press_stop','I AGREE TO STOP',()=>press(true));bind('press_readout','Read the register',()=>api.say(text()));
      v.draw();if(v.stop)v.stop.visible=false;
    }
    clean.push(api.props(room,place=>{
      const off=[];
      if(id==='hall'){
        v.posts=[-1,1].map(sign=>{const n=place('rope_post',[sign,0,-7.38]);n.scale.y=.46;return n;});
        v.span=place('rope_span',[0,0,-7.38]);v.span.scale.y=.46;
        v.note=place('rope_notice',[2.12,0,-7.42]);off.push(api.register(v.note,()=>state.carry?.key==='rope-post'?'Return the rope post':'Read the notice',()=>{if(api.drop?.('rope-post'))carriedPost=false;else api.say('Please respect the rope around the notice.');}));
        v.glass=place('sealed_glass',[0,.965,-9]);v.glass.visible=sealed;v.case.slide.add(v.glass);
        const box=new T.Mesh(new T.BoxGeometry(2,.29,.055));box.name='First Press cord';box.position.set(0,.145,-7.38);
        api.walker.add('first-press-cord',box,[],'hall');box.geometry.dispose();box.material.dispose();
        off.push(()=>api.walker.remove('first-press-cord'));
        off.push(api.register(v.posts[0],'Carry the rope post',()=>{
          if(state.carry){api.say('Your hand is occupied.');return;}
          carriedPost=true;state.ticket.book('rope-post','Removed a rope post',true);cue('v4_post_taken');api.hold?.('rope-post',v.posts[0],'Rope post');
        }));
      }
      stops.forEach(([zone,at],i)=>{if(zone!==id)return;const obj=place('moved_button',at);obj.visible=false;obj.userData.stockBay=zone==='shop';
        const price=obj.getObjectByName('moved_price');if(price)price.visible=zone==='shop';
        const cap=obj.getObjectByName('moved_cap');off.push(api.register(obj,()=>state.presses>=25?'DO PRESS':'Press the moved button',()=>{press();v.spring=.15;},2.4));
        let paper;
        cap?.traverse(n=>{if(n.isMesh&&n.material?.name==='Print')paper=n;});
        v.copies.push({obj,cap,i,at:[...at],invert(){
          if(!paper)return;
          const label=printedPanel(T,paper,{width:384,height:128,emission:.12});off.push(()=>label.dispose());
          label.draw((g,w,h)=>{g.fillStyle='#efe8d2';g.fillRect(0,0,w,h);g.fillStyle='#214c3e';g.textAlign='center';
            g.font='bold 69px Georgia';g.fillText('DO PRESS',w/2,77,w-16);
            g.font='29px Georgia';g.fillText('Believed voluntary.',w/2,111,w-16);});
        }});});
      return ()=>off.reverse().forEach(f=>f());
    }));
  }
  function seen(at,id,zone){
    if(zone!==id)return false;projected.fromArray(at);projected.y+=.16;projected.project(camera);
    return projected.z>=-1&&projected.z<=1&&Math.abs(projected.x)<1.15&&Math.abs(projected.y)<1.15;
  }
  function atStop(i){return i===2?views.get('shop')?.copies[0]?.at??stops[i][1]:stops[i][1];}
  function tick(dt,zone){
    const shop=views.get('shop'),sale=shop?.copies[0];
    if(sale){
      if(!sale.rack){sale.rack=shop.room.getObjectByName('shop_bay');if(sale.rack){sale.rack.add(sale.obj);sale.obj.position.set(1.23,1.23,0);}}
      sale.obj.getWorldPosition(projected).toArray(sale.at);
    }
    const pair=moved<0?['hall',original]:stops[moved],at=moved<0?original:atStop(moved);
    if(pending&&!seen(at,pair[0],zone)){
      const next=(moved+1)%stops.length;if(!seen(atStop(next),stops[next][0],zone)){moved=next;pending=false;draw();}
    }
    const hall=views.get('hall'),rope=state.presses>=5&&state.presses<12;
    if(hall){
      if(hall.main)hall.main.visible=sealed||moved<0;
      if(hall.stop)hall.stop.visible=state.presses>=25;
      if(hall.case){const y=moved>=0&&!sealed?-1.85:0;hall.case.slide.position.y+=(y-hall.case.slide.position.y)*Math.min(1,dt*3);hall.case.cover.visible=!sealed;}
      if(hall.posts){hall.posts.forEach((n,i)=>{n.visible=rope&&(i!==0||!carriedPost);});hall.span.visible=rope&&!carriedPost;hall.note.visible=rope||state.carry?.key==='rope-post';hall.glass.visible=sealed;}
      api.setColliderEnabled('First Press cord',rope&&!carriedPost);api.setColliderEnabled('Accession plinth',sealed||moved<0);
    }
    for(const v of views.values()){
      v.spring=Math.max(0,(v.spring||0)-dt);for(const copy of v.copies){const {obj,i,cap}=copy;obj.visible=i===moved&&!sealed&&(!obj.userData.stockBay||!!copy.rack);if(cap)cap.position.y=-v.spring*.09;
        if(obj.visible&&state.presses>=25&&copy.invert){copy.invert();copy.invert=null;}
      }
    }
    if(state.presses>=25&&!sealed&&moved>=0&&zone===pair[0]&&Math.hypot(camera.position.x-at[0],camera.position.z-at[2])<2.15&&Math.abs(camera.position.y-1.6-at[1])<1.55){
      still+=dt;if(still>=10){state.presses++;still-=10;state.ticket.book('did-not-press','Kept hands off DO PRESS');cue('v4_press_still');draw();}
    }else still=0;
    const byCase=zone==='hall'&&Math.hypot(camera.position.x,camera.position.z+9)<4.4;
    if(near&&!byCase&&!state.presses)state.ticket.book('left-button','Left the First Press untouched');
    near=byCase;
    if(last!==state.presses){last=state.presses;draw();}
  }
  return {mount,tick,press,habitatPress(){state.presses++;draw();},get sealed(){return sealed;}};
}

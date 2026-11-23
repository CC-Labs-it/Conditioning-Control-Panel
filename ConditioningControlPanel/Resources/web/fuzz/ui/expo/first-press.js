import {placard,printedPanel} from './display.js';
import {dial} from './staff-fixtures.js';

const original=[0,1.04,-9];
const stops=[['hall',[9.95,3.4,-5.3]],['hall',[-5.7,0,.66]],['shop',[17.48,1.23,-3.7]],['hall',[-11.48,0,-3.35]]];
const clamp=n=>Math.min(1,Math.max(0,n));

// The original hall batches the stand, hand-hole glazing and furniture by finish.
function liftCase(T,room,buttonAssembly){
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
  // The lift carries the spring as well as the red cap and its printed face.
  const main=buttonAssembly(room.getObjectByName('press_main')),readout=room.getObjectByName('press_readout');
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

/* The rope stage (owner 2026-11-23): the rope stands for this many presses made from inside it. The first ones are
   recorded and answered with one more notice beside the last; the final one lets the exhibit leave. */
const ROPE_PRESSES=3;
/* Once the visitor has left DO PRESS alone, the plinth comes back up with the second button on it. */
const RETURN_SECONDS=1.6;
const names=['East mezzanine','Behind the hall banner','Gift shop shelf','Beside the dome door'];
const locationLines=['v4_press_mezz','v4_press_found','v4_press_price','v4_press_invert'];

function loanPaper(panel,found){
  panel?.draw((g,w,h)=>{
    g.fillStyle='#214839';g.fillRect(0,0,w,h*.23);g.fillStyle='#f7eed7';g.font='bold 48px Georgia';
    g.fillText('On loan',w*.045,h*.165);g.textAlign='right';g.font='600 32px system-ui';g.fillText(found+' of 4',w*.95,h*.16);
    g.textAlign='left';g.font='34px Georgia';
    names.forEach((name,i)=>{
      const y=h*(.37+i*.175);g.strokeStyle='#527158';g.lineWidth=3;g.strokeRect(35,y-24,24,24);
      if(i<found){g.beginPath();g.moveTo(37,y-14);g.lineTo(46,y-4);g.lineTo(61,y-28);g.stroke();}
      g.fillStyle='#234532';g.fillText(name,80,y,w-95);
    });
  });
}
function brassLocator(T,obj,short,off){
  const root=new T.Group();root.name='Loan locator';obj.add(root);root.position.set(.12,.09,-.12);
  const profile=(points,material)=>{
    const geometry=new T.LatheGeometry(points.map(([r,y])=>new T.Vector2(r,y)),20),mesh=new T.Mesh(geometry,material);
    root.add(mesh);off.push(()=>geometry.dispose());return mesh;
  };
  let brass;obj.traverse(n=>{if(n.material?.name==='Brass')brass=n.material;});
  const fallback=new T.MeshStandardMaterial({color:0x9c7942,metalness:.72,roughness:.32});
  const amber=new T.MeshStandardMaterial({color:0xffedb7,emissive:0xffba48,emissiveIntensity:1.2,roughness:.28});
  const height=short ? .15 : .34;
  profile([[.059,0],[.059,.014],[.042,.023],[.017,.034],[.017,height-.071],[.05,height-.052],[.052,height-.04]],brass||fallback);
  profile([[.047,height-.041],[.052,height-.014],[.046,height+.009],[.033,height+.02]],amber);
  profile([[.034,height+.016],[.046,height+.018],[.042,height+.027],[.009,height+.04],[0,height+.041]],brass||fallback);
  off.push(()=>{root.removeFromParent();fallback.dispose();amber.dispose();});
  return amber;
}
function labelCap(T,cap,title,subtitle,off){
  let paper;cap?.traverse(n=>{if(n.isMesh&&n.material?.name==='Print')paper=n;});
  if(!paper)return;
  const panel=printedPanel(T,paper,{width:512,height:160,emission:.12});off.push(()=>panel.dispose());
  panel.draw((g,w,h)=>{g.fillStyle='#efe8d2';g.fillRect(0,0,w,h);g.fillStyle='#214c3e';g.textAlign='center';
    g.font='bold 78px Georgia';g.fillText(title,w/2,subtitle?88:111,w-16);
    if(subtitle){g.font='32px Georgia';g.fillText(subtitle,w/2,133,w-16);}});
}

export function firstPress(api,state){
  const {THREE:T,camera,cue}=api;
  const views=new Map(),projected=new T.Vector3(),worldPoint=new T.Vector3(),right=new T.Vector3();
  let count=0,found=0,moved=-1,pending=false,hold=0,still=0,clock=0,tickDue=0;
  const waitingAt=new T.Vector3();let waiting=false;
  let carriedPost=false,switchReady=false,sealed=false,near=false,roped=0,back=0;
  const announcements=[];
  let speaking=false;
  function announce(id){
    const zone=api.walker?.zone||'hall';
    const step=count,needed=()=>count===step&&(id!=='v4_press_invert'||!switchReady);
    announcements.push({id,zone,needed});speak(zone);
  }
  function speak(zone){
    while(announcements.length&&(announcements[0].zone!==zone||!announcements[0].needed()))announcements.shift();
    if(speaking||!announcements.length||(api.narrationAvailable&&!api.narrationAvailable()))return;
    const row=announcements.shift(),playing=cue(row.id,{zone,kind:'beat',exhibit:'press',roomWide:true,needed:row.needed});
    if(playing?.then){speaking=true;void playing.finally(()=>{speaking=false;});}
  }
  function text(){
    if(sealed)return 'Sealed by the visitor.';
    if(count>=7)return 'DO PRESS. Believed voluntary.';
    if(count>=3)return 'On loan · '+found+' of 4. '+names[Math.min(found,3)]+'.';
    if(count===2)return 'Please respect the rope.';
    return state.pressRecord();
  }
  function draw(){
    state.firstPressCount=count;state.pressSwitchReady=switchReady&&!sealed;
    for(const v of views.values()){v.draw?.();loanPaper(v.paper,found);}
    const base={kicker:'First Press'};
    if(!count)state.pressTask=null;
    else if(count===1)state.pressTask={...base,title:'Stamped. The button is still there.',where:'Grand Hall · First Press'};
    else if(count===2)state.pressTask={...base,title:'Jump the rope, or carry a post away',where:'First Press · inside the rope'};
    else if(found<4)state.pressTask={...base,title:'Find the button',where:found+' of 4 · follow the ticking · '+names[found],atShop:found>=2};
    else if(!switchReady)state.pressTask={...base,title:'Do not press',where:'Stand by it for 10 seconds · beside the dome door'};
    else if(!sealed)state.pressTask={...base,title:'Press the small switch to close the display',where:'Grand Hall · First Press'};
    else state.pressTask={...base,title:'First Press: closed',where:'Sealed by the visitor · Grand Hall'};
  }
  function press(stop=false,index=moved){
    api.play('press',.5);
    if(sealed){api.say?.('Sealed by the visitor.');return;}
    if(hold>0){api.say?.('The exhibit is moving.');return;}
    if(stop){
      if(!switchReady)return;
      sealed=true;count++;state.presses++;state.ticket.book('press-sealed','Sealed the First Press');
      announce('v4_press_seal');draw();return;
    }
    if(count===2&&!carriedPost&&camera.position.z>=-7.56){
      api.say?.('Jump the rope, or carry a post away.');return;
    }
    if(count>=7){api.say?.(switchReady?'Press the small switch to close the display.':'DO PRESS. Register unchanged. Stand by it for 10 seconds.');still=0;return;}
    if(count>=3&&(index!==moved||index!==found||pending)){api.say?.('Already recorded. Look away to let the loan travel.');return;}
    if(count===2&&++roped<ROPE_PRESSES){
      // Recorded, and the rope stays: one more notice goes up beside the last.
      state.presses++;api.say?.('Please respect the rope.');draw();return;
    }
    count++;state.presses++;
    if(count===1){state.ticket.stamp('Pressed');state.ticket.book('first-press','Pressed the founding exhibit',true);announce('pa_hall1_press');}
    else if(count===2)announce('v4_press_rope');
    else if(count===3){
      if(!carriedPost)state.ticket.book('past-rope','Crossed the accession rope',true);
      hold=2;state.pressWatching=true;api.holdPressView?.();announce('v4_press_moved');
    }else{
      found++;pending=found<4;announce(locationLines[found-1]);
    }
    draw();
  }
  function mount(id,room,bind,surface,clean){
    if(id!=='hall'&&id!=='shop')return;
    const v={room,copies:[]};views.set(id,v);clean.push(()=>views.delete(id));
    if(id==='hall'){
      v.case=liftCase(T,room,api.buttonAssembly);v.case.slide.position.y=count>=3&&!sealed?-1.85:0;v.case.cover.visible=!sealed;clean.push(v.case.free);
      const readout=surface('press_readout');
      v.draw=()=>placard(readout,'Founding collection   /   First Press',text(),'REGISTER   '+(state.file.taps+state.presses).toLocaleString());
      v.main=bind('press_main',()=>sealed?'Read the sealed accession':'Press I AGREE',()=>sealed?api.say(text()):press());
      // The second button waits on the plinth, out of sight, until the plinth returns for the last press.
      v.stop=bind('press_stop','I AGREE TO STOP',()=>press(true));if(v.stop)v.stop.visible=false;
      bind('press_readout','Read the register',()=>api.say(text()));
      v.draw();
      if(api.annexProps)clean.push(api.annexProps(room,place=>{
        const off=[];v.card=place('annex_sign',[0,-1.9,-9]);v.card.name='First Press loan card';v.card.scale.set(1.14,1.22,1);
        v.paper=dial(api,v.card,'sign_face',off,820,400);loanPaper(v.paper,found);
        off.push(api.register(v.card,'Read the loan card',()=>api.say('On loan · '+found+' of 4. '+names.join(' · ')+'.')));
        return ()=>off.reverse().forEach(f=>f());
      }));
    }
    clean.push(api.props(room,place=>{
      const off=[];
      if(id==='hall'){
        v.posts=[-1,1].map(sign=>{const n=place('rope_post',[sign,0,-7.38]);n.scale.y=.46;return n;});
        v.span=place('rope_span',[0,0,-7.38]);v.span.scale.y=.46;
        v.note=place('rope_notice',[2.12,0,-7.42]);off.push(api.register(v.note,()=>state.carry?.key==='rope-post'?'Return the rope post':'Read the notice',()=>{if(api.drop?.('rope-post'))carriedPost=false;else api.say('Please respect the rope around the notice.');}));
        // One more notice for each press the rope has outlasted.
        v.notes=Array.from({length:ROPE_PRESSES-1},(_,k)=>{
          const n=place('rope_notice',[2.12+.66*(k+1),0,-7.42]);n.visible=false;
          off.push(api.register(n,'Read the notice',()=>api.say('Please respect the rope around the notice.')));return n;
        });
        v.glass=place('sealed_glass',[0,.965,-9]);v.glass.visible=sealed;v.case.slide.add(v.glass);
        const box=new T.Mesh(new T.BoxGeometry(2,.29,.055));box.name='First Press cord';box.position.set(0,.145,-7.38);
        api.walker.add('first-press-cord',box,[],'hall');box.geometry.dispose();box.material.dispose();
        off.push(()=>api.walker.remove('first-press-cord'));
        off.push(api.register(v.posts[0],'Carry the rope post',()=>{
          if(state.carry){api.say('Your hand is occupied.');return;}
          if(api.hold?.('rope-post',v.posts[0],'Rope post')){carriedPost=true;state.ticket.book('rope-post','Removed a rope post',true);cue('v4_post_taken');}
        }));
      }
      stops.forEach(([zone,at],i)=>{
        if(zone!==id)return;
        const obj=place('moved_button',at);obj.visible=false;obj.userData.stockBay=zone==='shop';
        const price=obj.getObjectByName('moved_price');if(price)price.visible=zone==='shop';
        const cap=obj.getObjectByName('moved_cap'),lamp=brassLocator(T,obj,i===2,off);
        const copy={obj,cap,i,at:[...at],lamp,spring:0};v.copies.push(copy);
        off.push(api.register(obj,()=>count>=7?'DO PRESS':'Press the moved button',()=>{press(false,i);copy.spring=.16;},2.4));
        copy.invert=()=>labelCap(T,cap,'DO PRESS','Believed voluntary.',off);
      });
      return ()=>off.reverse().forEach(f=>f());
    }));
  }
  function seen(at,id,zone){
    if(zone!==id)return false;projected.fromArray(at);projected.y+=.16;projected.project(camera);
    return projected.z>=-1&&projected.z<=1&&Math.abs(projected.x)<1.15&&Math.abs(projected.y)<1.15;
  }
  function atStop(i){return i===2?views.get('shop')?.copies[0]?.at??stops[i][1]:stops[i][1];}
  function tick(dt,zone){
    clock+=dt;speak(zone);
    const shop=views.get('shop'),sale=shop?.copies[0];
    if(sale){
      if(!sale.rack){sale.rack=shop.room.getObjectByName('shop_bay');if(sale.rack){sale.rack.add(sale.obj);sale.obj.position.set(1.23,1.23,0);}}
      sale.obj.getWorldPosition(projected).toArray(sale.at);
    }
    if(hold>0){
      hold=Math.max(0,hold-dt);camera.lookAt(0,1.02+.17*clamp(1-hold),-9);camera.rotation.z=0;
      if(!hold){moved=0;state.pressWatching=false;tickDue=0;}
    }
    if(pending&&!seen(atStop(moved),stops[moved][0],zone)&&!seen(atStop(found),stops[found][0],zone)){
      moved=found;pending=false;tickDue=0;
    }
    const hall=views.get('hall'),rope=count===2;
    if(switchReady&&!sealed)back=Math.min(1,back+dt/RETURN_SECONDS);
    if(hall){
      const down=count>=3&&!sealed;
      const travel=hold?clamp((2-hold)/1.55):down?1-back:0;
      const ease=travel*travel*(3-2*travel);
      if(hall.main)hall.main.visible=count<3||hold>0||sealed;
      if(hall.case){hall.case.slide.position.y=-1.85*ease;hall.case.cover.visible=!sealed;}
      if(hall.card){
        const rise=hold?clamp((1.23-hold)/.97):down?1-back:0;
        hall.card.visible=down&&rise>0;hall.card.position.y=-1.85*(1-rise*rise*(3-2*rise));
        if(hold>0)hall.card.rotation.y=Math.atan2(camera.position.x,camera.position.z+9);
      }
      if(hall.posts){hall.posts.forEach((n,i)=>{n.visible=rope&&(i!==0||!carriedPost);});hall.span.visible=rope&&!carriedPost;hall.note.visible=rope||state.carry?.key==='rope-post';hall.glass.visible=sealed;}
      hall.notes?.forEach((n,k)=>{n.visible=rope&&roped>k;});
      if(hall.stop)hall.stop.visible=switchReady&&(back>=1||sealed);
      api.setColliderEnabled('First Press cord',rope&&!carriedPost);api.setColliderEnabled('Accession plinth',!down||hold>0||back>0);
    }
    for(const v of views.values())for(const copy of v.copies){
      const {obj,i,cap}=copy;copy.spring=Math.max(0,copy.spring-dt);
      obj.visible=i===moved&&!sealed&&(!obj.userData.stockBay||!!copy.rack);
      if(cap)cap.position.y=-copy.spring*.09;
      if(obj.visible){
        copy.lamp.emissiveIntensity=api.ctx?.gentle||api.ctx?.reduced?1.1:.9+.75*(.5+.5*Math.cos(clock*5.4));
        if(found===4&&copy.invert){copy.invert();copy.invert=null;}
      }
    }
    if(moved>=0&&!sealed){
      const at=atStop(moved),sameRoom=zone===stops[moved][0];
      if(sameRoom&&!switchReady&&clock>=tickDue){
        tickDue=clock+.77;
        worldPoint.fromArray(at).sub(camera.position);const distance=worldPoint.length();
        right.set(1,0,0).applyQuaternion(camera.quaternion);
        api.play('loan_tick',Math.max(.13,.58/(1+distance*.07)),Math.max(-.85,Math.min(.85,worldPoint.normalize().dot(right))));
      }
      if(found===4&&!switchReady){
        const close=sameRoom&&Math.hypot(camera.position.x-at[0],camera.position.z-at[2])<2.15&&Math.abs(camera.position.y-1.6-at[1])<1.55;
        if(!close){waiting=false;still=0;}
        else{
          if(!waiting||Math.hypot(camera.position.x-waitingAt.x,camera.position.z-waitingAt.z)>.12){waitingAt.copy(camera.position);waiting=true;still=0;}
          still+=dt;
        }
        if(still>=10){switchReady=true;state.ticket.book('did-not-press','Kept hands off DO PRESS');announce('v4_press_still');draw();}
      }
    }
    const byCase=zone==='hall'&&Math.hypot(camera.position.x,camera.position.z+9)<4.4;
    if(near&&!byCase&&!count)state.ticket.book('left-button','Left the First Press untouched');near=byCase;
  }
  return {mount,tick,press,habitatPress(){state.presses++;draw();},get sealed(){return sealed;}};
}

import {dial,layThread,sight,useCarry,write} from './staff-fixtures.js';

const roster=['reginald','margaret','henry','greg','bob','lashes','kevin','spitz','gary','doctor','bartholomew','nana'];
const pages=['The Update proceeds as agreed.','All parties remain included.',''];
const names={reginald:'Reginald',margaret:'Margaret',henry:'Henry',greg:'Greg',bob:'Bob',lashes:'Lady Lashes',kevin:'Kevin',spitz:'DJ Spitz',gary:'Gary',doctor:'Dr. Alpaca',bartholomew:'Bartholomew',nana:'Nana'};

export function createTwelvePieces(api,state){
  const {THREE:T,camera,root}=api,look=sight(api);
  const progress={page:0,touches:0,doctor:0,silence:0,minutes:0,stare:0,blink:null,dj:0,signPlaced:false};
  state.twelvePieces=progress;
  let room,previous,beat=0,away=0,moved=false;
  const lids=root.ownerDocument.createElement('div');
  lids.className='expo-eyelids';lids.style.cssText='pointer-events:none;position:absolute;inset:0;z-index:2;opacity:0';
  root.append(lids);
  const cue=(key,kind='react')=>api.cue('v4_'+key,{kind,exhibit:'twelve'});
  function mount(id,group,clean){
    if(id!=='twelve')return;
    room={casts:[],owned:new Set(state.file.herd)};const local=room;
    clean.push(()=>{room=null;previous=null;});
    clean.push(api.annexProps(group,place=>{
      const off=[],bind=(n,label,fn,reach=2.4)=>{if(n)off.push(api.register(n,label,fn,reach));};
      const basis=roster.map((key,i)=>{
        const a=(288.7+((i-6+12)%12)*(600.5-288.7)/11)*Math.PI/180;
        const x=6.98*Math.cos(a),z=-26.1-6.98*Math.sin(a),yaw=Math.atan2(-Math.cos(a),Math.sin(a));
        function at(inward,y,side=0){
          return [x-Math.cos(a)*inward+Math.sin(a)*side,y,z+Math.sin(a)*inward+Math.cos(a)*side];
        }
        const pos=new T.Vector3(...at(0,.77)),gaze=new T.Vector3(...at(-1.29,1.86));
        return {key,x,z,yaw,at,pos,gaze};
      });
      let firstCover;
      for(const b of basis){
        const {key,yaw,at,pos,gaze}=b,owned=local.owned.has(key);
        if(!owned){firstCover??=b;continue;}
        const figure=group.getObjectByName('statue_'+key);
        if(figure){
          // Some zone bundles have quantized world-space mesh coordinates under the statue node.
          // Rotate an independent pivot at the actual four-hoof plinth centre.
          const pivot=new T.Group();pivot.position.set(b.x,-.68,b.z);group.add(pivot);pivot.attach(figure);
          local.casts.push({...b,pivot,figure});
          off.push(()=>{group.attach(figure);pivot.removeFromParent();});
        }
        if(key==='henry')bind(figure,'Touch the bronze',()=>{
          progress.touches++;api.say('Touched today: '+progress.touches+'. The rest is sealed.');cue('desk_touch');local.writeHenry?.();
        },2.9);
        if(['henry','margaret','greg','gary'].includes(key))place('annex_display_stand',at(1.19,-.68,-.89),yaw);
        if(key==='henry'){
          const label=place('annex_record',at(1.19,.18,-.89),yaw);label.scale.setScalar(.75);
          const panel=dial(api,label,'record_face',off);
          local.writeHenry=()=>write(panel,'Henry','Touched today: '+progress.touches,'The record is sealed.');
          local.writeHenry();
        }
        if(key==='margaret'){
          const book=place('annex_book',at(1.23,.18,-.89),yaw);
          const folio=place('annex_record',at(1.04,.24,-.89),yaw);folio.scale.setScalar(.55);
          folio.rotation.x=-.08;
          const page=dial(api,folio,'record_face',off),read=()=>write(page,'The Update',pages[progress.page],'Leaf '+(progress.page+1));
          const turn=()=>{
            progress.page=(progress.page+1)%pages.length;read();api.play('feed',.27);
            if(progress.page===2){state.ticket.book('blank-leaf','Found Margaret’s blank page',true);cue('blank_page');}
          };
          read();bind(book,'Turn a page of the Update',turn);bind(folio,'Turn a page of the Update',turn);
        }
        if(key==='greg'){
          local.stapler=place('annex_stapler',at(1.20,.18,-.89),yaw);
          local.stapler.scale.setScalar(1.45);local.stapler.visible=!state.staffVisit.staplerHome&&state.carry?.key!=='stapler';
          bind(local.stapler,'Borrow Greg’s stapler',()=>{
            if(useCarry(api,state,'stapler',local.stapler,'Greg’s stapler')){away=0;api.say('Keep the stapler in sight. Greg likes it on the left of his staff desk.');}
          },2.9);
        }
        if(key==='bob'){
          const spot=place('annex_plinth',at(1.48,-.678,.65),yaw);
          local.bobSign=place('annex_sign',at(1.48,-.61,.65),yaw);local.bobSign.scale.setScalar(.74);
          local.bobSign.visible=progress.signPlaced;
          write(dial(api,local.bobSign,'sign_face',off),'Health and safety','Sign placed.','B.');
          bind(spot,'Place Bob’s sign',()=>{
            const carried=state.carry?.key;
            if(carried?.startsWith('staff-sign-'))api.drop(carried);
            else if(state.carry){api.say('The sign needs your hand.');return;}
            progress.signPlaced=true;local.bobSign.visible=true;state.ticket.book('sign-placed','Placed Bob’s sign');api.play('chime',.3);
          },2.75);
        }
        if(key==='spitz')bind(figure,'Play the Signal',()=>{
          if(progress.dj>0)return;
          progress.dj=8;api.signal?.();api.play('spit',.33);state.ticket.book('signal','Played the Signal',true);
        },2.9);
        if(key==='gary'){
          const ledger=place('annex_record',at(1.19,.18,-.89),yaw);ledger.scale.setScalar(.75);
          const panel=dial(api,ledger,'record_face',off);local.ledger=()=>write(panel,'Gary from Accounts',state.ticket.balance+' Yes\n'+state.ticket.outstanding+' Deviations','Balance reviewed.');
          local.ledger();bind(ledger,'Read Gary’s ledger',()=>{local.ledger();api.say(state.ticket.balance+' Yes. '+state.ticket.outstanding+' Deviations.');});
        }
        if(key==='doctor'){
          const doctor=group.getObjectByName('alpaca_button');
          bind(doctor,()=>progress.doctor>=3?'Read the seal':'Is he an alpaca?',()=>{
            if(progress.doctor>=3){api.say('The matter is under glass.');return;}
            progress.doctor++;api.play('chime',.4);
            if(progress.doctor===1)api.cue('pa_alpaca',{kind:'react',exhibit:'doctor'});
            else if(progress.doctor===2){
              state.visitorNumber=2;state.ticket.book('doctor','Readmitted by Dr. Alpaca',true);
              void api.go('atrium',[.50,0,35.95],0).then(()=>api.say('Human visitor no. 2.'));
            }else if(local.doctorGlass)local.doctorGlass.visible=true;
          },2.9);
          if(doctor){const box=new T.Box3().setFromObject(doctor,true);local.doctorAt=box.getCenter(new T.Vector3());}
        }
        if(key==='nana'){
          local.threadStart=pos.clone();const route=[at(.39,-.65),at(.85,-.65),at(1.66,-.645),[1.46,-.645,-18.25],[1.46,.032,-16.40],[1.36,.025,-16]];
          layThread(api,group,place,route,off);
        }
        if(key==='lashes'){local.lashes=gaze;local.form=place('annex_permit',at(.63,.06),yaw);local.form.visible=false;}
      }
      if(!local.threadStart){
        const b=basis.at(-1);local.threadStart=b.pos;
        layThread(api,group,place,[b.at(.32,-.65),b.at(1.16,-.64),[1.46,-.645,-18.25],[1.46,.032,-16.40],[1.36,.025,-16]],off);
      }
      if(firstCover){
        const b=firstCover,scaffold=group.getObjectByName('scaffold_'+b.key),cover=group.getObjectByName('cover_'+b.key);
        const deck=[b.x,1.64,b.z-.51],note=place('annex_dust_note',b.at(.57,.09,.21),b.yaw);
        const hooves=place('annex_hooves_under',b.at(.51,-.56,.23),b.yaw);
        note.scale.setScalar(.55);note.visible=hooves.visible=false;
        bind(scaffold,'Climb the scaffold',()=>{
          if(!state.staffVisit.hardHat){api.say('Hard hats must be worn. Stores have one.');return;}
          api.walker.remove('visitor-scaffold');
          const board=new T.Mesh(new T.BoxGeometry(1.91,.05,.36),new T.MeshBasicMaterial());
          board.position.fromArray(deck);api.walker.add('visitor-scaffold',board,[],'twelve');board.geometry.dispose();board.material.dispose();
          const yaw=camera.rotation.y;
          api.walker.place('twelve',[deck[0]-.51,deck[1]+.025,deck[2]],yaw);
          local.onScaffold=true;api.say('Lift the corner of the sheet.');
        },2.75);
        bind(cover,'Lift the dust sheet corner',()=>{
          if(!state.staffVisit.hardHat||!local.onScaffold||Math.hypot(camera.position.x-deck[0],camera.position.z-deck[2])>1.1){api.say('The hem is above the rail.');return;}
          if(!note.visible){
            const pivot=new T.Group();pivot.position.set(b.x,-.65,b.z);group.add(pivot);pivot.attach(cover);
            pivot.rotation.z=.16;pivot.position.y+=.32;
            off.push(()=>{group.attach(cover);pivot.removeFromParent();});
            note.visible=hooves.visible=true;state.ticket.book('dust-sheet','Lifted a dust sheet',true);cue('dust');
          }
          api.say('Four hooves. Nice try. B.');
        },3.0);
        off.push(()=>api.walker.remove('visitor-scaffold'));
      }
      return ()=>{for(const f of off.reverse())f();};
    }));
    clean.push(api.props(group,place=>{
      const doctorAt=()=>{
        const doc=group.getObjectByName('alpaca_button');if(!doc)return null;
        return new T.Box3().setFromObject(doc,true).getCenter(new T.Vector3());
      };
      const at=doctorAt();if(at&&local.owned.has('doctor')){
        local.doctorGlass=place('sealed_glass',[at.x,at.y-.17,at.z]);local.doctorGlass.scale.setScalar(.68);local.doctorGlass.visible=progress.doctor>=3;
      }
    }));
  }
  function tick(dt,id){
    if(state.carry?.key==='stapler'){
      away=camera.rotation.x<-.19?0:away+dt;
      if(away>=4.5){api.drop('stapler');if(room?.stapler)room.stapler.visible=true;away=0;cue('stapler');}
    }
    if(progress.dj>0){
      const last=Math.floor((8-progress.dj)*2);
      progress.dj=Math.max(0,progress.dj-dt);
      const n=Math.floor((8-progress.dj)*2);
      if(last!==n&&n<16){api.play(n%4===0?'spit':n%2?'press':'chime',n%4===0?.32:.16,n%2?.12:-.12);}
    }
    if(progress.blink!==null){
      progress.blink+=dt;
      const t=progress.blink,open=t<1?1-t:t<1.5?0:Math.min(1,(t-1.5)/1.4);
      lids.style.opacity='1';lids.style.setProperty('--opening',Math.max(.01,open*150)+'%');
      if(t>=1&&!moved){moved=true;void api.go('shop',[20.8,0,-9.98],0);}
      if(t>=2.9){progress.blink=null;lids.style.opacity='0';}
    }
    if(id!=='twelve'||!room){previous=null;return;}
    const p=camera.position,still=!!previous&&Math.hypot(previous.x-p.x,previous.z-p.z)<.012;
    previous??=new T.Vector3();previous.copy(p);beat+=dt;
    for(const b of room.casts){
      if(b.key==='bartholomew'&&!state.staffVisit.staplerHome)continue;
      if(!look.visible(b.pos,1.24)){
        const angle=Math.atan2(p.x-b.x,p.z-b.z);
        const delta=Math.atan2(Math.sin(angle-b.yaw),Math.cos(angle-b.yaw));
        b.pivot.rotation.y=delta;
        if(Math.abs(delta)>.35)cueStatues();
      }
      if(b.key==='reginald'||b.key==='kevin'){
        const reg=b.key==='reginald',key=reg?'minutes':'silence';
        if(state.ticket.ledger.has('stood-'+b.key))continue;
        progress[key]=still&&look.gaze(b.pos,3.65,.40)?progress[key]+dt:0;
        if(progress[key]>=(reg?20:15)){
          state.ticket.book('stood-'+b.key,reg?'Stood before Reginald':'Heard Kevin’s silence');
          if(!reg)cue('kevin');else api.say('Reginald has entered your attendance in the minutes.');
        }
      }
    }
    if(room.lashes&&!state.ticket.stamps.has('Blinked')){
      if(p.distanceToSquared(room.lashes)<22)api.trailTask('lashes',id);
      progress.stare=look.gaze(room.lashes,5,.16)?progress.stare+dt:0;
      if(progress.stare>=6){
        progress.blink=0;moved=false;state.signedForm=true;state.ticket.stamp('Blinked');
        state.ticket.book('blink','Looked at Lady Lashes too long',true);cue('blink','beat');
        api.exchange('signed-form',room.form,'Signed visitor form');
      }
    }
    if(room.threadStart&&p.distanceTo(room.threadStart)<2.15&&!state.threadSeen){state.threadSeen=true;cue('wool_start');}
    const signature=state.ticket.balance+'/'+state.ticket.outstanding;
    if(signature!==room.ledgerKey){room.ledgerKey=signature;room.ledger?.();}
  }
  function cueStatues(){
    if(state.turnedNoticed||beat<8)return;
    state.turnedNoticed=true;cue('statues');
  }
  return {mount,tick,dispose(){lids.remove();}};
}

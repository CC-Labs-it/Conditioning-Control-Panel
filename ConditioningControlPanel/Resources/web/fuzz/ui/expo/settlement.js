import {modelVisitor} from './renewal-endings.js';
import {COUNTERFOILS} from './ticket.js';
import {terminal,livePanel,placard,printedPanel} from './display.js';
import {createExhibitBeat} from './speech-sequence.js';
import {mountShopDoor} from './shop-door.js';

const stock={Corrected:'Framed correction',Attended:'Minute book',Spat:'Range rosette',Queued:'Pocket rope post',Pressed:'The moved button',Hoofed:'Pair of hooves',Fed:'Feeding token',Kept:'Desk miniature',Blinked:'Signed blink',Stayed:'Dome snow globe',Delivered:'Tea service',Unplugged:'Plush heater',Read:'Knitted scarf'};
const questions=[['Add a bag?','YES','v4_shop_bag'],['Round up to full agreement?','YES','v4_shop_round'],['Stop being asked?','NO','v4_shop_no']];
function receiptSummary(state,records){
  const t=state.ticket;
  return [['Instructions followed',String(t.yes)],['Deviations converted to Yes',String(t.deviations)],
      ['Total agreements',String(t.yes+t.deviations)],['First Press register',String(state.file.taps+state.presses)],
      ['History absorbed',records+' of 40'],['Ticket',t.count+' of 12'],['Item',stock[state.souvenir]||'Day ticket']];
}
export function tillReceipt(state,records){
  return [...state.ticket.ledger.values()].map(row=>[row.label,row.deviation?'Deviation':'Yes'])
    .concat(receiptSummary(state,records));
}
export function mountSettlement(api,state,room,bind,surface,clean){
  const {THREE:T,camera,ctx}=api, ticket=state.ticket;
  const till=surface('till_display',{emission:.36}),beat=createExhibitBeat(api.cue);
  let page=state.tillPage||0,swing=0,transit='',walking=false,receiptClock=0,hold=0,lastKey='',bits=[],sheet,button,price,receipt;
  let slip;
  const hinge=mountShopDoor(T,room,api);clean.push(()=>hinge?.dispose());
  const worm=room.getObjectByName('shop_wormhole');if(worm)worm.visible=false;
  const sign=room.getObjectByName('exit_sale'),contract=room.getObjectByName('framed_contract');
  const old=['shelf_left','shelf_right'].map(s=>room.getObjectByName(s));
  function print(rolling=false){
    receipt?.remove();receipt=ctx.layer('ending').ownerDocument.createElement('div');receipt.className='expo-shop-receipt';
    const h=document.createElement('h3');h.textContent='Visitor Services';receipt.append(h);
    for(const [name,value] of tillReceipt(state,api.records())){
      const row=document.createElement('p'),n=document.createElement('span'),v=document.createElement('b');
      n.textContent=name;v.textContent=value;row.append(n,v);receipt.append(row);if(rolling)row.hidden=true;
    }
    api.root.append(receipt);receiptClock=0;return receipt;
  }
  function render(){
    if(sign)sign.visible=state.contract&&state.loops>=2;
    api.setButtonAvailable?.('exit_sale',!!sign?.visible);
    const stage=questions[page];
    const title=state.boughtExit?'Renewal':state.contract?'Settlement filed':state.souvenir?stage?.[0]||'Receipt':'Gift shop';
    const rows=state.boughtExit?['Walk through I AGREE.','Register   '+(state.file.taps+state.presses)]:
      state.contract?[ticket.count+' of 12 stamps',ticket.count<8?'Day ticket available':'Exit sold separately',ticket.yes+ticket.deviations+' Yes, settled']:
      state.souvenir?[stock[state.souvenir],stage?.[1]||'Print receipt']:['Carry one souvenir to the till.','Stock follows your ticket.'];
    terminal(till,{title,rows,foot:beat.listening?'Listening':'Visitor Services   /   Settlement',eyebrow:'GARY FROM ACCOUNTS'});
    if(sheet)sheet.visible=!!state.contract;
    if(slip && state.contract){
      const summary=receiptSummary(state,api.records());
      slip.draw((g,w,h)=>{
        g.fillStyle='#f6efd9';g.fillRect(0,0,w,h);g.fillStyle='#234839';
        g.font='bold 40px Georgia';g.fillText('Visitor Services',18,385);
        g.font='26px Georgia';g.fillText(stock[state.souvenir]||'Day ticket',18,430,w-36);
        summary.slice(2,5).forEach(([label,value],i)=>{
          const y=478+i*79;
          g.strokeStyle='#ad996a';g.beginPath();g.moveTo(18,y-18);g.lineTo(w-18,y-18);g.stroke();
          g.font='bold 24px Georgia';g.textAlign='left';g.fillText(label,18,y+16,232);
          g.textAlign='right';g.fillText(value,w-18,y+16,115);g.textAlign='left';
        });
        g.font='bold 23px Georgia';g.fillText('Settlement filed',18,704);
      });
    }
    if(state.boughtExit)hinge?.releaseLatch();
    if(button)button.visible=!!state.boughtExit;
    if(worm)worm.visible=!!state.boughtExit&&swing>.65;
  }
  function purchase(){
    if(beat.listening)return;
    if(!state.contract && modelVisitor(ticket)){api.endVisit('model');return;}
    if(state.carry?.key==='signed-form'){api.drop('signed-form');api.play('register');api.say(state.carry?'Signed form received. Your item has been returned.':'Signed form received. Please choose your souvenir.');return;}
    if(state.boughtExit){api.say('The register is in the doorway. Walk through to renew.');return;}
    if(state.contract){
      if(state.loops>=2&&ticket.count>=8){ticket.settle();state.boughtExit=true;api.play('door');api.cue('v4_shop_door');}
      else if(ticket.count<8&&state.loops>=2){transit='day';}
      else if(receipt){receipt.remove();receipt=null;}else print();
      render();return;
    }
    if(!state.souvenir){
      const carried=state.carry?.key,mark=carried?.startsWith('souvenir:')&&carried.slice(9);
      if(!mark){if(state.carry)api.say('Your other hand is needed. Return what you are carrying first.');
        else if(!ticket.stamps.size){api.say('Day ticket. The gallery doors are open.');transit='day';}
        else api.say('Bring a souvenir from a stocked bay.');return;}
      state.souvenir=mark;api.storeSouvenir?.(bits.find(b=>b.mark===mark)?.item,stock[mark]);api.drop(carried);api.play('register');render();return;
    }
    const q=questions[page];if(!q)return;
    void beat.run([q[2]],{zone:'shop',exhibit:'till',position:[21.2,1.46,-11.42]},()=>{
      ticket.book('till-'+page,q[0]+' '+q[1]);page++;state.tillPage=page;
      if(page===questions.length){state.contract=true;ticket.settle();api.play('register');print(true);api.cue('v4_shop_settle',{kind:'beat'});}
      render();
    });render();
  }
  bind('till_display',()=>beat.listening?'Listening':state.souvenir&&!state.contract?questions[page]?.[1]:'Use the till',purchase);
  bind('framed_contract','Set souvenir on the counter',purchase);
  bind('exit_sale',()=>ticket.count<8?'Collect day ticket':'Buy Exit',purchase);
  bind('shop_door',()=>state.boughtExit?'Walk through to renew':'Open the exit door',()=>{
    if(transit||state.boughtExit)return;
    if(!state.contract){api.say('The till has your paperwork.');return;}
    if(state.loops>=2){api.say(ticket.count<8?'Collect your day ticket at the till.':'Collect Exit at the till.');return;}
    state.loops++;transit='loop';api.play('door');api.cue(state.loops===1?'v4_shop_mirror':'v4_shop_tags');render();
  },2.7);
  function stockRefresh(){
    const key=[...ticket.stamps,state.carry?.key,state.souvenir,state.loops].join('/');
    if(lastKey===key)return;lastKey=key;
    for(const b of bits){
      const has=ticket.stamps.has(b.mark), taken=state.carry?.key==='souvenir:'+b.mark||state.souvenir===b.mark;
      b.item.visible=has&&!taken;b.empty.visible=!has;b.yes.visible=state.loops>=2;
    }
  }
  const racks=[];
  let staffArea=false,fireExit;
  function takeFireExit(){
    if(state.contract || staffArea){api.say('Staff entrance. Please leave the route clear.');return;}
    staffArea=true;state.ticket.book('staff-exit','Used the staff fire exit',true);api.play('door');
    void api.go('shop',[23.1,0,-12.84],0).then(()=>api.say('Visitor Services. Your lanyard is behind the till.'));
  }
  clean.push(api.annexProps(room,place=>{
    fireExit=place('annex_fire_exit',[12.22,0,-7.4],Math.PI/2);
    const badge=place('annex_lanyard',[23.12,1.18,-13.70]);badge.scale.setScalar(1.3);
    const off=api.register(fireExit,'Take Bob’s fire exit',takeFireExit,2.5);
    const wear=api.register(badge,'Wear the staff lanyard',()=>{
      if(!staffArea){api.say('Staff collection is through the fire exit.');return;}
      badge.visible=false;api.endVisit('finger');
    },2);
    return ()=>{off();wear();};
  }));
  clean.push(api.props(room,place=>{
    const off=[];old.forEach(n=>{if(n)n.visible=false;});
    const coords=[[16.25,0,-3.7],[21.10,0,-7.2]];
    coords.forEach(at=>racks.push(place('shop_bay',at)));
    COUNTERFOILS.flat().forEach((mark,i)=>{
      const side=i>=6?1:0,slot=i-(side?6:0),rack=racks[side],x=(slot%3-1)*.93;
      const local=[x-(side===0&&slot===2?.18:0),slot>=6?.19:slot>=3?.69:1.23,slot>=6?.15:0];
      const add=name=>{const n=place(name,[0,0,0]);rack.add(n);n.position.fromArray(local);return n;};
      const item=add('shop_item_'+mark.toLowerCase()),empty=add('shop_empty'),yes=add('shop_yes');
      item.userData.stockBay=empty.userData.stockBay=rack.userData.stockBay=true;yes.position.z+=.22;bits.push({mark,item,empty,yes});
      const dynamic=item.getObjectByName(mark==='Spat'?'range_record':'dome_record');
      if(dynamic?.isMesh){const panel=livePanel(T,dynamic,{width:384,height:128});placard(panel,mark,mark==='Spat'?(state.bestSpit||0).toFixed(1)+' m':Math.round(state.domeSeconds||0)+' s');off.push(()=>panel.dispose());}
      off.push(api.register(empty,'Out of stock: '+stock[mark],()=>{api.say('Out of stock. Perhaps more visiting would help.');api.cue('v4_shop_empty');}));
      off.push(api.register(item,'Carry '+stock[mark],()=>{
        if(!api.hold('souvenir:'+mark,item,stock[mark])){api.say('One item. Put yours back on the shelf first.');api.cue('v4_shop_swap');return;}
        api.cue('v4_shop_carry');stockRefresh();
      }));
    });
    for(const rack of racks)off.push(api.register(rack,'Return carried item to its bay',()=>{
      const key=state.carry?.key;if(key?.startsWith('souvenir:')){api.drop(key);stockRefresh();}else api.say('The brass cards show what your ticket has earned.');
    }));
    const printer=place('shop_printer',[20.38,1.07,-11.55]);sheet=printer.getObjectByName('receipt_sheet');
    if(sheet?.isMesh){slip=printedPanel(T,sheet,{width:420,height:720,emission:.12});off.push(()=>slip.dispose());}
    off.push(api.register(printer,'Read the printed receipt',()=>{if(state.contract){if(receipt){receipt.remove();receipt=null;}else print();}else api.say('Your receipt will print after the sale.');}));
    button=place('renewal_door',[15.03,0,-13.60]);
    lastKey='';stockRefresh();render();return ()=>off.reverse().forEach(f=>f());
  }));
  function tick(dt){
    stockRefresh();
    racks.forEach((rack,i)=>{const at=i?-7.2:-3.7;rack.position.z=at+(state.loops?(i?1:-1)*1.1:0);rack.rotation.y=state.loops?Math.PI:0;});
    api.setColliderEnabled('shop_shelves_before',!state.loops);api.setColliderEnabled('shop_shelves_after',!!state.loops);
    if(beat.listening&&Math.hypot(camera.position.x-21.2,camera.position.z+11.42)>6)beat.leave();
    if(receipt){receiptClock+=dt;const lines=receipt.children;for(let i=1;i<lines.length&&i<=1+receiptClock*7;i++)lines[i].hidden=false;if(camera.position.z>-7){receipt.remove();receipt=null;}}
    if(fireExit && !staffArea && !state.contract && Math.abs(camera.position.x-fireExit.position.x)<.58 &&
      Math.abs(camera.position.z-fireExit.position.z)<.55)takeFireExit();
    const dx=Math.abs(camera.position.x-15.03),dz=camera.position.z+13.60;
    const approach=state.boughtExit&&dx<.86&&dz>-.6&&dz<2.35;
    swing=T.MathUtils.damp(swing,transit||approach?1:0,4.3,dt);
    hinge?.set(swing);if(button){button.visible=state.boughtExit;button.position.z=-13.60-swing*.30;button.scale.x=Math.max(.01,1-swing);}
    if(worm)worm.visible=state.boughtExit&&swing>.45;
    if(transit&&swing>.93){const day=transit==='day';transit='';if(day){state.loops=0;api.cue('v4_shop_day');}
      void api.go(day?'atrium':'shop',day?[0,0,34.4]:[14.2,0,-5],day?0:-Math.PI/2);render();}
    if(state.boughtExit && dx<.72&&dz<.45&&dz>-.7&&!walking){walking=true;api.endVisit('renewal');}
    if(state.boughtExit&&dx<1.6&&dz>=.45&&dz<3.2){
      const p=camera.position;
      if(price&&Math.hypot(p.x-price.x,p.z-price.z)<.018)hold+=dt;else hold=0;
      if(!price)price=new T.Vector3();price.copy(p);
      if(hold>=120&&!walking){walking=true;api.endVisit('conduct');}
    }else hold=0;
    state.doorWait=hold;
  }
  clean.push(()=>{receipt?.remove();beat.leave();});render();
  return {tick,leave(){receipt?.remove();receipt=null;beat.leave();hold=0;},arrive(){lastKey='';render();}};
}

import {COUNTERFOILS} from './ticket.js';
import {terminal,livePanel,placard} from './display.js';
import {createExhibitBeat} from './speech-sequence.js';
import {mountShopDoor} from './shop-door.js';

const stock={Corrected:'Framed correction',Attended:'Minute book',Spat:'Range rosette',Queued:'Pocket rope post',Pressed:'The moved button',Hoofed:'Pair of hooves',Fed:'Feeding token',Kept:'Desk miniature',Blinked:'Signed blink',Stayed:'Dome snow globe',Delivered:'Tea service',Unplugged:'Plush heater',Read:'Knitted scarf'};
const questions=[['Add a bag?','YES','v4_shop_bag'],['Round up to full agreement?','YES','v4_shop_round'],['Stop being asked?','NO','v4_shop_no']];
export function tillReceipt(state,records){
  const t=state.ticket;
  return [...t.ledger.values()].map(row=>[row.label,row.deviation?'Deviation':'Yes'])
    .concat([['Instructions followed',String(t.yes)],['Deviations converted to Yes',String(t.deviations)],
      ['Total agreements',String(t.yes+t.deviations)],['First Press register',String(state.file.taps+state.presses)],
      ['History absorbed',records+' of 40'],['Ticket',t.count+' of 12'],['Item',stock[state.souvenir]||'Day ticket']]);
}
export function mountSettlement(api,state,room,bind,surface,clean){
  const {THREE:T,camera,ctx}=api, ticket=state.ticket;
  const till=surface('till_display',{emission:.36}),beat=createExhibitBeat(api.cue);
  let page=state.tillPage||0,swing=0,transit='',walking=false,receiptClock=0,hold=0,age=0,lastKey='',bits=[],sheet,button,price,receipt;
  const hinge=mountShopDoor(T,room,api);clean.push(()=>hinge?.dispose());
  const worm=room.getObjectByName('shop_wormhole');if(worm)worm.visible=false;
  const sign=room.getObjectByName('exit_sale'),contract=room.getObjectByName('framed_contract');
  const old=['shelf_left','shelf_right'].map(s=>room.getObjectByName(s));
  function print(){
    receipt?.remove();receipt=ctx.layer('ending').ownerDocument.createElement('div');receipt.className='expo-shop-receipt';
    const h=document.createElement('h3');h.textContent='Visitor Services';receipt.append(h);
    for(const [name,value] of tillReceipt(state,api.records())){
      const row=document.createElement('p'),n=document.createElement('span'),v=document.createElement('b');
      n.textContent=name;v.textContent=value;row.append(n,v);receipt.append(row);
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
    if(button)button.visible=!!state.boughtExit;
    if(worm)worm.visible=!!state.boughtExit&&swing>.65;
  }
  function purchase(){
    if(beat.listening)return;
    if(state.carry?.key==='signed-form'){api.drop('signed-form');api.play('register');api.say('Signed form received. Please choose your souvenir.');return;}
    if(state.boughtExit){api.say('The register is in the doorway. Walk through to renew.');return;}
    if(state.contract){
      if(state.loops>=2&&ticket.count>=8){state.boughtExit=true;api.play('door');api.cue('v4_shop_door');}
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
      if(page===questions.length){state.contract=true;api.play('register');print();api.cue('v4_shop_settle',{kind:'beat'});}
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
        if(!api.hold('souvenir:'+mark,item,stock[mark])){api.say('One item. Put yours back on the shelf first.');return;}
        api.cue('v4_shop_carry');stockRefresh();
      }));
    });
    for(const rack of racks)off.push(api.register(rack,'Return carried item to its bay',()=>{
      const key=state.carry?.key;if(key?.startsWith('souvenir:')){api.drop(key);stockRefresh();}else api.say('The brass cards show what your ticket has earned.');
    }));
    const printer=place('shop_printer',[20.38,1.07,-11.55]);sheet=printer.getObjectByName('receipt_sheet');
    off.push(api.register(printer,'Read the printed receipt',()=>{if(state.contract){if(receipt){receipt.remove();receipt=null;}else print();}else api.say('Your receipt will print after the sale.');}));
    button=place('renewal_door',[15.03,0,-13.60]);
    lastKey='';stockRefresh();render();return ()=>off.reverse().forEach(f=>f());
  }));
  function tick(dt){
    age+=dt;stockRefresh();
    racks.forEach((rack,i)=>{const at=i?-7.2:-3.7;rack.position.z=at+(state.loops?(i?1:-1)*1.1:0);rack.rotation.y=state.loops?Math.PI:0;});
    api.setColliderEnabled('shop_shelves_before',!state.loops);api.setColliderEnabled('shop_shelves_after',!!state.loops);
    if(beat.listening&&Math.hypot(camera.position.x-21.2,camera.position.z+11.42)>6)beat.leave();
    if(receipt){receiptClock+=dt;if(receiptClock>20||camera.position.z>-7){receipt.remove();receipt=null;}}
    const dx=Math.abs(camera.position.x-15.03),dz=camera.position.z+13.60;
    const approach=state.boughtExit&&dx<.86&&dz>-.6&&dz<1.25;
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
  }
  clean.push(()=>{receipt?.remove();beat.leave();});render();
  return {tick,leave(){receipt?.remove();receipt=null;beat.leave();hold=0;},arrive(){lastKey='';render();}};
}

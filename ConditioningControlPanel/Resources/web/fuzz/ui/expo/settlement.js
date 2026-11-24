import {modelVisitor} from './renewal-endings.js';
import {COUNTERFOILS} from './ticket.js';
import {terminal,livePanel,placard,printedPanel} from './display.js';
import {mountShopDoor} from './shop-door.js';

const stock={Corrected:'Framed correction',Attended:'Minute book',Spat:'Range rosette',Queued:'Pocket rope post',Pressed:'The moved button',Hoofed:'Pair of hooves',Fed:'Feeding token',Kept:'Desk miniature',Blinked:'Signed blink',Stayed:'Dome snow globe',Delivered:'Tea service',Unplugged:'Plush heater',Read:'Knitted scarf'};
const carriedSouvenir=state=>{const key=state.carry?.key;return key?.startsWith('souvenir:')?key.slice(9):'';};
function receiptSummary(state,records,total){
  const t=state.ticket;
  return [['Instructions followed',String(t.yes)],['Deviations converted to Yes',String(t.deviations)],
      ['Total agreements',String(t.yes+t.deviations)],['First Press register',String(state.file.taps+state.presses)],
      ['History absorbed',total?records+' of '+total:String(records)],['Ticket',t.count+' of 12'],['Item',stock[state.souvenir]||'Day ticket']];
}
export function tillReceipt(state,records,total){
  return [...state.ticket.ledger.values()].map(row=>[row.label,row.deviation?'Deviation':'Yes'])
    .concat(receiptSummary(state,records,total));
}
// The shop is one press of the till (owner, 2026-11-23): it rings up the souvenir in hand, or a plain day ticket,
// prints the receipt and opens the green door. Stamps decide what is in stock, never who may leave.
export const TILL_PROMPT='Use the till';
export function tillScreen(state){
  if(state.contract)return {title:'Settlement filed',rows:[stock[state.souvenir]||'Day ticket','The green door is open.']};
  return {title:'Gift shop',rows:['1  Pick a souvenir','2  Press the till','3  Leave by the green door']};
}
// The counter's own mesh carries the base ring of the cap that stood under the framed contract, and the Exit sign's
// mesh the ring of the cap under it (it shows with the sign once the sale is filed). Each goes with its cap.
function liftCapBase(T,room,at,owner='Architecture'){
  const point=new T.Vector3(),undo=[];
  room.updateMatrixWorld(true);
  room.traverse(mesh=>{
    if(!mesh.isMesh||!(mesh.name===owner||mesh.parent?.name===owner))return;
    if(!['action_ink','action_cream','action_coral'].includes(mesh.material?.name))return;
    const geom=mesh.geometry,position=geom.attributes.position,index=geom.index;
    if(!index)return;
    const near=i=>{
      point.fromBufferAttribute(position,i).applyMatrix4(mesh.matrixWorld);
      return Math.hypot(point.x-at[0],point.z-at[2])<.17&&Math.abs(point.y-at[1])<.07;
    };
    const keep=[];
    for(let i=0;i<index.count;i+=3){
      const a=index.getX(i),b=index.getX(i+1),c=index.getX(i+2);
      if(!(near(a)&&near(b)&&near(c)))keep.push(a,b,c);
    }
    if(keep.length===index.count)return;
    geom.setIndex(keep);undo.push(()=>geom.setIndex(index));
  });
  return ()=>undo.forEach(restore=>restore());
}
export function mountSettlement(api,state,room,bind,surface,clean){
  const {THREE:T,camera,ctx}=api, ticket=state.ticket;
  const till=surface('till_display',{emission:.36});
  let swing=0,walking=false,receiptClock=0,hold=0,lastKey='',bits=[],sheet,button,price,receipt,slip;
  const hinge=mountShopDoor(T,room,api);clean.push(()=>hinge?.dispose());
  const worm=room.getObjectByName('shop_wormhole');if(worm)worm.visible=false;
  // Nothing is sold separately any more: the Exit sign stays off the counter.
  const sign=room.getObjectByName('exit_sale');if(sign)sign.visible=false;
  const old=['shelf_left','shelf_right'].map(s=>room.getObjectByName(s));
  // The caps under the framed contract and under the Exit sign are not part of the counter any more.
  api.setButtonAvailable?.('framed_contract',false);api.setButtonAvailable?.('exit_sale',false);
  clean.push(liftCapBase(T,room,[19.61,1.09,-11.23]),liftCapBase(T,room,[22.88,1.09,-11.23],'exit_sale'));
  api.setColliderEnabled('shop_shelves_before',true);api.setColliderEnabled('shop_shelves_after',false);
  const history=()=>[api.records(),api.recordTotal?.()];
  function print(rolling=false){
    receipt?.remove();receipt=ctx.layer('ending').ownerDocument.createElement('div');receipt.className='expo-shop-receipt';
    const h=document.createElement('h3');h.textContent='Visitor Services';receipt.append(h);
    for(const [name,value] of tillReceipt(state,...history())){
      const row=document.createElement('p'),n=document.createElement('span'),v=document.createElement('b');
      n.textContent=name;v.textContent=value;row.append(n,v);receipt.append(row);if(rolling)row.hidden=true;
    }
    api.root.append(receipt);receiptClock=0;return receipt;
  }
  function render(){
    terminal(till,{...tillScreen(state),foot:'Visitor Services   /   Settlement',eyebrow:'GARY FROM ACCOUNTS'});
    if(sheet)sheet.visible=!!state.contract;
    if(slip && state.contract){
      const summary=receiptSummary(state,...history());
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
  // A souvenir in hand goes on the counter as part of the sale.
  function take(mark){
    state.souvenir=mark;api.storeSouvenir?.(bits.find(b=>b.mark===mark)?.item,stock[mark]);
    api.drop('souvenir:'+mark);lastKey='';stockRefresh();
  }
  // One press is the whole sale, and it never waits for a voice: the two lines follow on their own.
  function purchase(){
    if(state.boughtExit){api.say('The register is in the doorway. Walk through to renew.');return;}
    if(modelVisitor(ticket)){api.endVisit('model');return;}
    if(state.carry?.key==='signed-form')api.drop('signed-form');
    const mark=carriedSouvenir(state);
    if(mark)take(mark);
    state.contract=state.boughtExit=true;ticket.settle();api.play('register');api.play('door');print(true);render();
    Promise.resolve(api.cue('v4_shop_settle',{kind:'beat'})).then(()=>api.cue('v4_shop_door'),()=>{});
  }
  bind('till_display',()=>TILL_PROMPT,purchase);
  bind('shop_door',()=>state.boughtExit?'Walk through to renew':'Open the exit door',()=>{
    if(state.boughtExit)return false;
    api.say('The till has your paperwork.');
  },2.7);
  function stockRefresh(){
    const key=[...ticket.stamps,state.carry?.key,state.souvenir].join('/');
    if(lastKey===key)return;lastKey=key;
    for(const b of bits){
      const has=ticket.stamps.has(b.mark), taken=state.carry?.key==='souvenir:'+b.mark||state.souvenir===b.mark;
      b.item.visible=has&&!taken;b.empty.visible=!has;
    }
  }
  const racks=[];
  // The staff way out is plain (owner, 2026-11-23): the fire exit always answers and stands the visitor behind the
  // counter beside the lanyard, and the lanyard ends the visit whenever it is worn.
  let staffArea=false,fireExit;
  function takeFireExit(){
    staffArea=true;state.ticket.book('staff-exit','Used the staff fire exit',true);api.play('door');
    void api.go('shop',[23.1,0,-12.84],0).then(()=>api.say('Visitor Services. Your lanyard is behind the till.'));
  }
  clean.push(api.annexProps(room,place=>{
    fireExit=place('annex_fire_exit',[12.22,0,-7.4],Math.PI/2);
    const badge=place('annex_lanyard',[23.12,1.18,-13.70]);badge.scale.setScalar(1.3);
    const off=api.register(fireExit,'Take Bob’s fire exit',takeFireExit,2.5);
    const wear=api.register(badge,'Wear the staff lanyard',()=>{badge.visible=false;api.endVisit('finger');},2);
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
      const item=add('shop_item_'+mark.toLowerCase()),empty=add('shop_empty');
      item.userData.stockBay=empty.userData.stockBay=rack.userData.stockBay=true;bits.push({mark,item,empty});
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
    // The printer stands clear of the till screen, whose bottom row now carries the way-home line.
    const printer=place('shop_printer',[20.22,1.07,-11.55]);sheet=printer.getObjectByName('receipt_sheet');
    if(sheet?.isMesh){slip=printedPanel(T,sheet,{width:420,height:720,emission:.12});off.push(()=>slip.dispose());}
    off.push(api.register(printer,'Read the printed receipt',()=>{if(state.contract){if(receipt){receipt.remove();receipt=null;}else print();}else api.say('Your receipt will print after the sale.');}));
    button=place('renewal_door',[15.03,0,-13.60]);
    lastKey='';stockRefresh();render();return ()=>off.reverse().forEach(f=>f());
  }));
  function tick(dt){
    stockRefresh();
    if(receipt){receiptClock+=dt;const lines=receipt.children;for(let i=1;i<lines.length&&i<=1+receiptClock*7;i++)lines[i].hidden=false;if(camera.position.z>-7){receipt.remove();receipt=null;}}
    if(fireExit && !staffArea && Math.abs(camera.position.x-fireExit.position.x)<.58 &&
      Math.abs(camera.position.z-fireExit.position.z)<.55)takeFireExit();
    const dx=Math.abs(camera.position.x-15.03),dz=camera.position.z+13.60;
    const approach=state.boughtExit&&dx<.86&&dz>-.6&&dz<2.35;
    swing=T.MathUtils.damp(swing,approach?1:0,4.3,dt);
    hinge?.set(swing);if(button){button.visible=state.boughtExit;button.position.z=-13.60-swing*.30;button.scale.x=Math.max(.01,1-swing);}
    if(worm)worm.visible=state.boughtExit&&swing>.45;
    if(state.boughtExit && dx<.72&&dz<.45&&dz>-.7&&!walking){walking=true;api.endVisit('renewal');}
    if(state.boughtExit&&dx<1.6&&dz>=.45&&dz<3.2){
      const p=camera.position;
      if(price&&Math.hypot(p.x-price.x,p.z-price.z)<.018)hold+=dt;else hold=0;
      if(!price)price=new T.Vector3();price.copy(p);
      if(hold>=120&&!walking){walking=true;api.endVisit('conduct');}
    }else hold=0;
    state.doorWait=hold;
  }
  clean.push(()=>receipt?.remove());render();
  return {tick,leave(){receipt?.remove();receipt=null;hold=0;},arrive(){lastKey='';render();}};
}

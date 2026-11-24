// The 2036 tape has quiet intervals. Offer a recorded aside only when its observation is still true.
export function visitorCommentary(state,cue){
  const spoken=new Set(),visited=new Set();let age=0,lastRoom='',from='',next=10,elapsed=0,carried='',putDown=false,returnedStock=false;
  function note(id,condition,zone){
    if(!condition || spoken.has(id))return false;
    next=elapsed+3;
    cue('v4_'+id,{kind:'ambient',ttl:1200,exhibit:'visitor-guide',zone,onStart:()=>{
      spoken.add(id);next=elapsed+20;
    }});
    return true;
  }
  return function tick(dt,room,feet){
    elapsed+=dt;age+=dt;
    const key=state.carry?.key||'';
    if(key!==carried){if(carried&&!key){putDown=true;if(carried.startsWith('souvenir:')&&!state.souvenir)returnedStock=true;}carried=key;}
    if(room!==lastRoom){from=lastRoom;lastRoom=room;age=0;visited.add(room);next=Math.max(next,elapsed+8);}
    if(elapsed<next||age<6||state.ending)return;next=elapsed+1;
    const t=state.ticket,has=key=>t.stamps.has(key),ledger=key=>t.ledger.has(key);
    const offer=(id,condition)=>note(id,condition,room);
    if(offer('full_ticket',t.complete)||offer('ready_exit',t.count>=8)||offer('half_ticket',t.count>=6))return;
    if(offer('sign_depart',ledger('off-duty-notice')&&key.startsWith('staff-sign-'))||offer('tea_lift',key==='tea'))return;
    switch(room){
      case 'atrium':
        if(offer('hoof_gate_again',state.hoofAdmitted)||offer('gallery_home',from==='aquarium'&&visited.has('forum'))||
          offer('ticket_reentry',ledger('unarrived'))||offer('ticket_stock',t.issued)||
          offer('ticket_balance',t.issued&&t.ledger.size>1)||offer('ticket_late',!t.issued&&age>20))return;break;
      case 'chronology':
        if(offer('corrections_back',ledger('withheld'))||offer('photo_filed',has('Corrected'))||
          offer('gallery_return',from==='forum'))return;break;
      case 'forum':
        if(offer('agenda_close',has('Attended'))||offer('agenda_late',state.meetingSeconds>15))return;break;
      case 'commons':if(offer('green_record',state.bestSpit>0))return;break;
      case 'hall':
        if(offer('queue_stamp',has('Queued'))||offer('queue_corner',state.queueProgress>3&&state.queueProgress<23)||
          offer('press_quiet',ledger('did-not-press'))||offer('press_last',state.pressSwitchReady)||
          offer('press_barrier',state.firstPressCount===2)||
          offer('press_first_again',state.firstPressCount===1&&Math.abs(feet.z+9)<4)||
          offer('dome_exit',from==='dome'&&state.domeSeconds>90))return;break;
      case 'hoof':
        if(offer('hoof_edge_again',ledger('hoof-edge'))||
          offer('hoof_read',state.hoofWorn)||offer('hoof_wardrobe',has('Hoofed')&&!state.hoofWorn))return;break;
      case 'habitat':
        if(offer('desk_outside',has('Kept')&&state.presses>0)||offer('glass_kept',has('Kept'))||
          offer('glass_live',state.habitatCaught)||offer('glass_feed',state.feeds>0)||offer('glass_outside',age>8))return;break;
      case 'twelve':
        if(offer('greg_home',state.staffVisit?.staplerHome)||offer('ledger_book',state.file.herd.includes('gary')&&age>50)||
          offer('twelve_unfound',state.file.herd.length<12)||offer('twelve_entry',true))return;break;
      case 'service':
        if(offer('emergency',state.staffVisit?.unplugged&&age>12)||offer('hard_hat',state.staffVisit?.hardHat))return;break;
      case 'secret':
        if(offer('wool_reknit',ledger('loose-end'))||offer('wool_rack',feet.x>78&&state.readClauses?.size>=8)||
          offer('clause_read',state.readClauses?.size>0)||offer('wool_entry',true))return;break;
      case 'shop':
        if(offer('conduct_last',state.doorWait>88)||offer('conduct_wait',state.doorWait>42)||
          offer('staff_badge',ledger('staff-exit'))||offer('shop_printed',state.contract)||
          offer('stock_return',returnedStock)||offer('shop_form',state.signedForm)||

          offer('shop_intro',age>9)||offer('shop_bays',t.count<8))return;break;
    }
    if(offer('held_item',!!key)||offer('put_down',!key&&putDown))return;
  };
}

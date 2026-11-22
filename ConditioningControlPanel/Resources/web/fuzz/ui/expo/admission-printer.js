import {printedPanel} from './display.js';
import {COUNTERFOILS} from './ticket.js';

export function admissionPrinter(api,state,room,clean,ui){
  clean.push(api.props(room,place=>{
    const printer=place('shop_printer',[1.25,1.159,36.12]);printer.scale.setScalar(.83);
    const sheet=printer.getObjectByName('receipt_sheet');if(!sheet?.isMesh)return;
    const panel=printedPanel(api.THREE,sheet,{width:420,height:720,emission:.12});let printed=state.ticket.issued?1:0,signature='';
    ui.paper=dt=>{
      const t=state.ticket;sheet.visible=t.issued;if(!t.issued)return;printed=Math.min(1,printed+dt*1.6);
      sheet.scale.y=.20+.80*printed;sheet.position.y=.28*(1-sheet.scale.y);
      const key=[...t.stamps].join('+');if(key===signature&&sheet.userData.ink)return;
      signature=key;sheet.userData.ink=true;
      panel.draw((g,w,h)=>{
        g.fillStyle='#f6efd9';g.fillRect(0,0,w,h);g.fillStyle='#234839';
        // The first half of the paper curls around the feed roller.
        g.font='bold 44px Georgia';g.fillText('ADMIT ONE HUMAN',16,386,w-32);
        g.font='bold 30px Georgia';g.fillText('Llama Expo   /   2037',16,430);
        COUNTERFOILS.forEach((marks,i)=>{const x=16+(i%4)*98,y=448+Math.floor(i/4)*76;
          g.strokeStyle='#967c4c';g.strokeRect(x,y,92,68);g.font='bold 20px Georgia';
          g.fillText(marks.join('/'),x+4,y+26,84);if(marks.every(m=>t.stamps.has(m)))g.fillText('FILED',x+6,y+55);});
        g.font='23px Georgia';g.fillText('Visitor Services',16,700);
      });
    };ui.paper(0);
    const off=api.register(printer,()=>state.ticket.issued?'Read your ticket':'Print admission ticket',()=>{
      if(!state.ticket.issued)ui.count();else api.say(tally());
    });
    function tally(){const t=state.ticket;return t.count+' of 12 stamps. '+t.balance+' Yes. '+t.outstanding+' unsettled.';}
    return ()=>{off();panel.dispose();};
  }));
}

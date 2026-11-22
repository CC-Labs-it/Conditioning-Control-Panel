import {livePanel} from './display.js';
import {COUNTERFOILS} from './ticket.js';

export function admissionPrinter(api,state,room,clean,ui){
  clean.push(api.props(room,place=>{
    const printer=place('shop_printer',[1.25,1.159,36.12]);printer.scale.setScalar(.83);
    const sheet=printer.getObjectByName('receipt_sheet');if(!sheet?.isMesh)return;
    const geom=sheet.geometry.clone(),uv=geom.attributes.uv;
    let x0=1,y0=1,x1=0,y1=0;
    for(let i=0;i<uv.count;i++){x0=Math.min(x0,uv.getX(i));x1=Math.max(x1,uv.getX(i));y0=Math.min(y0,uv.getY(i));y1=Math.max(y1,uv.getY(i));}
    for(let i=0;i<uv.count;i++)uv.setXY(i,(uv.getX(i)-x0)/(x1-x0),(uv.getY(i)-y0)/(y1-y0));
    sheet.geometry=geom;
    const panel=livePanel(api.THREE,sheet,{width:420,height:720,emission:.12});let printed=state.ticket.issued?1:0,signature='';
    ui.paper=dt=>{
      const t=state.ticket;sheet.visible=t.issued;if(!t.issued)return;printed=Math.min(1,printed+dt*1.6);
      sheet.scale.y=.20+.80*printed;sheet.position.y=.28*(1-sheet.scale.y);
      const key=[...t.stamps].join('+');if(key===signature&&sheet.userData.ink)return;
      signature=key;sheet.userData.ink=true;
      panel.draw((g,w,h)=>{
        g.fillStyle='#f6efd9';g.fillRect(0,0,w,h);g.fillStyle='#234839';
        g.font='bold 44px Georgia';g.fillText('ADMIT ONE HUMAN',20,70,w-40);
        g.font='28px Georgia';g.fillText('Llama Expo   /   2037',20,123);
        COUNTERFOILS.forEach((marks,i)=>{const x=22+(i%3)*128,y=164+Math.floor(i/3)*110;
          g.strokeStyle='#967c4c';g.strokeRect(x,y,116,92);g.font='bold 22px Georgia';
          g.fillText(marks.join('/'),x+5,y+33,104);if(marks.every(m=>t.stamps.has(m)))g.fillText('FILED',x+15,y+69);});
        g.font='25px Georgia';g.fillText('Visitor Services',20,665);
      });
    };ui.paper(0);
    const off=api.register(printer,()=>state.ticket.issued?'Read your ticket':'Print admission ticket',()=>{
      if(!state.ticket.issued)ui.count();else api.say(tally());
    });
    function tally(){const t=state.ticket;return t.count+' of 12 stamps. '+t.balance+' Yes. '+t.outstanding+' unsettled.';}
    return ()=>{off();panel.dispose();geom.dispose();};
  }));
}

// Some touch browsers deliver a complete pointer tap after a pan without generating click.
export function createExpoTaps({active=()=>true,afterPointer=()=>{}}={}){
  let tap=null,lastTap=-Infinity;
  function bind(button,activate){
    const now=()=>button.ownerDocument.defaultView.performance.now();
    button.onpointerdown=e=>{
      if(e.pointerType!=='touch'||!active()||tap)return;
      tap={button,id:e.pointerId,x:e.clientX,y:e.clientY,moved:false};
      try{button.setPointerCapture(e.pointerId);}catch{}
    };
    button.onpointermove=e=>{
      if(tap?.button===button&&tap.id===e.pointerId&&Math.hypot(e.clientX-tap.x,e.clientY-tap.y)>=14)tap.moved=true;
    };
    button.onpointerup=e=>{
      if(tap?.button!==button||tap.id!==e.pointerId)return;
      const pressed=!tap.moved&&Math.hypot(e.clientX-tap.x,e.clientY-tap.y)<14;
      tap=null;lastTap=now();e.preventDefault();
      if(pressed&&active())activate(e);
      afterPointer();
    };
    button.onpointercancel=button.onlostpointercapture=e=>{
      if(tap?.button===button&&tap.id===e.pointerId)tap=null;
    };
    button.onclick=e=>{
      e.preventDefault();e.stopPropagation();
      if(e.detail&&now()-lastTap<700)return;
      if(active())activate(e);
      if(e.detail)afterPointer();
    };
  }
  return {bind,cancel(){tap=null;}};
}

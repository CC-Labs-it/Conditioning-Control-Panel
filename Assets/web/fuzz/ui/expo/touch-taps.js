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

// Complete a phone tap on the host's Wake Up button even when a preceding look swipe
// suppresses its compatibility click. The host's click handler opens and focuses the dialog.
export function bindExpoWakeTap(button){
  if(!button)return ()=>{};
  let tap=null;
  function start(event){
    if(tap||event.changedTouches.length!==1){tap=null;return;}
    const t=event.changedTouches[0];
    tap={id:t.identifier,x:t.clientX,y:t.clientY};
  }
  function track(event){
    if(!tap)return false;
    const t=Array.from(event.changedTouches).find(t=>t.identifier===tap.id);
    if(!t)return false;
    if(Math.hypot(t.clientX-tap.x,t.clientY-tap.y)>=14)tap=null;
    return !!tap;
  }
  function end(event){
    if(!track(event))return;
    tap=null;
    event.preventDefault();
    button.click();
  }
  function cancel(){tap=null;}
  button.addEventListener('touchstart',start,{passive:true});
  button.addEventListener('touchmove',track,{passive:true});
  button.addEventListener('touchend',end,{passive:false});
  button.addEventListener('touchcancel',cancel);
  return ()=>{
    button.removeEventListener('touchstart',start);
    button.removeEventListener('touchmove',track);
    button.removeEventListener('touchend',end);
    button.removeEventListener('touchcancel',cancel);
  };
}

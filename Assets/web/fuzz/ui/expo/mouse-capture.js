// A lock may arrive after the visitor has paused, changed camera mode or left the Expo.
export function createMouseCapture(canvas,doc,{enabled,onEscape,onRelease}){
  let pending=null,locked=false,releasing=false,disposed=false,refusals=0,unlockedAt=-Infinity;
  const now=()=>doc.defaultView.performance.now();
  const unsupported=()=>typeof canvas.requestPointerLock!=='function'||refusals>=4;
  function detach(){
    doc.removeEventListener('pointerlockchange',changed);
    doc.removeEventListener('pointerlockerror',failedEvent);
  }
  function release(){
    if(pending)pending.cancelled=true;
    if(doc.pointerLockElement!==canvas||releasing)return;
    releasing=true;
    try{doc.exitPointerLock();}catch{releasing=false;}
  }
  function changed(){
    if(doc.pointerLockElement===canvas){
      if(releasing)return;
      const expected=locked||(pending&&!pending.cancelled);pending=null;
      if(disposed||!expected||!enabled()){release();return;}
      locked=true;refusals=0;return;
    }
    if(locked||releasing){
      const userReleased=locked&&!releasing;
      unlockedAt=now();locked=false;releasing=false;
      onRelease(userReleased);
      if(userReleased&&enabled())onEscape();
    }
    if(disposed&&!pending)detach();
  }
  function failed(attempt){
    if(!attempt||pending!==attempt)return;
    pending=null;
    // Chromium briefly rejects re-capture after Escape. This does not disable mouse-look.
    if(!attempt.cancelled&&now()-unlockedAt>=1500)refusals++;
    if(disposed)detach();
  }
  function failedEvent(){failed(pending);}
  function take(){
    if(!enabled()||unsupported()||locked)return false;
    if(pending||releasing)return true;
    const attempt=pending={cancelled:false};
    try{
      const result=canvas.requestPointerLock();
      result?.then(()=>{
        if(pending!==attempt)return;
        if(doc.pointerLockElement===canvas)changed();else failed(attempt);
      },()=>failed(attempt));
    }catch{failed(attempt);}
    return true;
  }
  doc.addEventListener('pointerlockchange',changed);
  doc.addEventListener('pointerlockerror',failedEvent);
  return {take,release,get locked(){return locked&&!releasing;},get unavailable(){return unsupported();},
    retry(){refusals=0;},
    dispose(){
      disposed=true;release();
      if(!pending&&!releasing)detach();
    }};
}

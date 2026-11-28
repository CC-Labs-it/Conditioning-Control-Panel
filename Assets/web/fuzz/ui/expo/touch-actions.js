import {mountJumpButton} from './jump-button.js';
import {spitControls} from './spit-controls.js';
import {createExpoTaps} from './touch-taps.js';

const STYLE=`
.expo-touch{display:none;position:absolute;z-index:5;touch-action:none;user-select:none;-webkit-user-select:none}
.expo-root[data-expo-touch] .expo-stick{display:block}
.expo-stick{box-sizing:border-box;left:calc(env(safe-area-inset-left,0px) + 12px);bottom:calc(env(safe-area-inset-bottom,0px) + 12px);width:84px;height:84px;border:1px solid #f3f2dd87;border-radius:50%;background:#19372e45;box-shadow:0 1px 5px #152c2540}
.expo-stick-knob{position:absolute;inset:0;margin:auto;width:30px;height:30px;box-sizing:border-box;border:1px solid #f9f4d3bf;border-radius:50%;background:#e5e7d58f;pointer-events:none}
.expo-stick:active{background:#19372e75}
.expo-root[data-expo-touch] .expo-thumb-actions{display:flex}
.expo-thumb-actions{right:calc(env(safe-area-inset-right,0px) + 12px);bottom:calc(env(safe-area-inset-bottom,0px) + 16px);max-width:calc(100% - env(safe-area-inset-right,0px) - env(safe-area-inset-left,0px) - 120px);align-items:flex-end;justify-content:flex-end;gap:6px;pointer-events:none}
.expo-thumb-actions button{box-sizing:border-box;min-height:44px;min-width:44px;padding:7px 12px;line-height:1.14;border:1px solid #dae4d29e;border-radius:22px;background:#153b32ed;color:#fff7e2;font:600 14px/1.17 system-ui;touch-action:none;pointer-events:auto;cursor:pointer;text-wrap:balance}
.expo-thumb-actions button:focus-visible{outline:2px solid #f2c571;outline-offset:2px}
.expo-thumb-actions button:active{background:#385a43}
.expo-thumb-actions .expo-interact{background:#f4e9c9;color:#183a2c;border-color:#f6dd9c;overflow-wrap:anywhere}
.expo-thumb-actions .expo-interact:active{background:#d9c595}
.expo-thumb-actions .expo-more{flex:none;background:#17382ebd;font-size:13px}
.expo-thumb-actions [hidden]{display:none!important}
.expo-root[data-expo-touch] :is(.expo-prompt,.expo-step-down){display:none}
@media(max-height:460px){.expo-stick{width:72px;height:72px;bottom:calc(env(safe-area-inset-bottom,0px) + 9px)}.expo-thumb-actions{bottom:calc(env(safe-area-inset-bottom,0px) + 11px)}}
`;

// The two optional actions share the same buttons whether suggested by an exhibit or opened by the visitor.
export function createTouchActions(root,{active,canMove,interact,jump,spit}){
  const doc=root.ownerDocument;
  const style=doc.createElement('style');style.textContent=STYLE;
  const stick=doc.createElement('div');stick.className='expo-touch expo-stick';stick.setAttribute('aria-label','Drag to walk');
  const knob=doc.createElement('div');knob.className='expo-stick-knob';stick.append(knob);
  const group=doc.createElement('div');group.className='expo-touch expo-thumb-actions';
  group.setAttribute('role','group');group.setAttribute('aria-label','Visitor actions');
  const makeButton=(name,cls)=>{
    const el=doc.createElement('button');el.type='button';el.className=cls;el.textContent=name;group.append(el);return el;
  };
  const use=makeButton('Use','expo-interact');
  const jumpButton=mountJumpButton(root,()=>{if(canMove())jump?.();},group);
  const spitting=spit&&spitControls(root,canMove,spit,group,draw);
  const more=makeButton('Actions','expo-more');
  let expanded=false,action='',offerJump=false,offerSpit=false;
  const taps=createExpoTaps({active,afterPointer:()=>root.focus({preventScroll:true})});

  function draw(){
    more.setAttribute('aria-expanded',String(expanded));
    more.textContent=expanded?'Close':'Actions';
    use.hidden=!action||expanded||!!spitting?.touchHeld;
    jumpButton.hidden=!expanded&&(!!action||!offerJump||spitting?.touchHeld);
    if(spitting)spitting.button.hidden=!expanded&&(!!action||!offerSpit||offerJump)&&!spitting.touchHeld;
  }
  function close(){
    taps.cancel();
    if(expanded){expanded=false;draw();}
  }
  taps.bind(more,()=>{expanded=!expanded;draw();});
  taps.bind(use,()=>{if(action)interact();});
  function setAction(label,{jump=false,spit=false}={}){
    label=label||'';
    if(action===label&&offerJump===jump&&offerSpit===spit)return;
    action=label;offerJump=jump;offerSpit=spit;
    use.textContent=action||'Use';draw();
  }
  root.append(style,stick,group);draw();
  return {stick,knob,spitting,setAction,close,
    dispose(){spitting?.cancel();style.remove();stick.remove();group.remove();}};
}

// The pause card is shared with other endings. Mount the Expo controls for the lifetime of this visit.
const STYLE=`
#pause .expo-camera-options{margin:10px 0 4px;color:#203f34;font:500 14px/1.35 "Segoe UI",system-ui,sans-serif}
#pause .expo-camera-options h3{margin:0;font:inherit}
#pause .expo-camera-options button{min-height:44px;box-sizing:border-box;cursor:pointer}
#pause .expo-camera-open{display:flex;align-items:center;justify-content:space-between;gap:12px;width:100%;padding:8px 12px;background:#f3eedc;color:#203f34;border:1px solid #899782;border-radius:4px;text-align:left;font:650 15px/1.25 "Segoe UI",system-ui,sans-serif}
#pause .expo-camera-open::after{content:"";width:7px;height:7px;border-right:2px solid currentColor;border-bottom:2px solid currentColor;transform:rotate(45deg);margin:0 3px 4px}
#pause .expo-camera-open[aria-expanded=true]{border-radius:4px 4px 0 0}
#pause .expo-camera-open[aria-expanded=true]::after{transform:rotate(225deg);margin:4px 3px 0}
#pause .expo-camera-body{padding:10px 12px;background:#f6f1e3;border:1px solid #899782;border-top:0;border-radius:0 0 4px 4px}
#pause .expo-camera-body[hidden]{display:none}
#pause .expo-camera-body fieldset{padding:0;border:0;margin:0 0 8px;min-width:0}
#pause .expo-camera-body legend{padding:0;margin-bottom:3px;color:#314b3e;font:650 13px/1.3 "Segoe UI",system-ui,sans-serif}
#pause .expo-camera-pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:4px}
#pause .expo-camera-pair button{padding:4px 8px;background:#fffbed;color:#2b4838;border:1px solid #a2aa92;border-radius:4px;font:600 14px/1.2 "Segoe UI",system-ui,sans-serif}
#pause .expo-camera-pair button[aria-pressed=true]{background:#27513f;color:#fff9e8;border:2px solid #244b39}
#pause .expo-camera-invert{display:flex;align-items:center;gap:8px;min-height:44px;cursor:pointer;color:#203f34}
#pause .expo-camera-invert input{width:19px;height:19px;margin:0;accent-color:#27513f}
#pause .expo-camera-options :focus-visible{outline:3px solid #c16d2e;outline-offset:1px}
#pause .expo-camera-body p{margin:4px 0 0;color:#3e5343;font:500 13px/1.4 "Segoe UI",system-ui,sans-serif}
`;

export function mountCameraOptions(doc,options,onChange,slot=null){
  const pause=doc.getElementById('pause'),card=pause?.querySelector('[role=dialog]');
  if(!card)return {setMode(){},refresh(){},close(){},dispose(){}};
  const style=doc.createElement('style');style.textContent=STYLE;doc.head.append(style);
  const root=doc.createElement('section');root.className='expo-camera-options';
  const row=doc.getElementById('resume')?.closest('.card-buttons');
  if(slot)slot.append(root);   // the Options view's own copy (ui/options.js)
  else if(row)row.after(root);else card.append(root);
  function element(tag,parent,text){
    const node=doc.createElement(tag);
    if(text)node.textContent=text;
    if(tag==='button')node.type='button';
    parent.append(node);return node;
  }
  const opener=element('button',element('h3',root),'Camera & controls');opener.className='expo-camera-open';
  const body=element('div',root);body.className='expo-camera-body';body.id='expo-camera-controls';
  opener.setAttribute('aria-controls',body.id);
  const buttons=[];
  function group(label,key,choices){
    const field=element('fieldset',body);element('legend',field,label);
    const pair=element('div',field);pair.className='expo-camera-pair';
    for(const [name,value] of choices){
      const button=element('button',pair,name);buttons.push({button,key,value});
      button.addEventListener('click',()=>{if(options[key]!==value){onChange(key,value);refresh();}});
    }
  }
  group('Mouse camera','dragLook',[['Mouse look',false],['Click and drag',true]]);
  group('Arrow keys','arrowMode',[['Turn','turn'],['Strafe','strafe']]);
  const label=element('label',body);label.className='expo-camera-invert';
  const invert=element('input',label);invert.type='checkbox';label.append('Invert camera (both axes)');
  invert.addEventListener('change',()=>{onChange('invertLook',invert.checked);refresh();});
  const hint=element('p',body);
  let mode=doc.defaultView.matchMedia('(pointer:coarse)').matches?'touch':'keyboard';
  function refresh(){
    for(const {button,key,value} of buttons)button.setAttribute('aria-pressed',String(options[key]===value));
    invert.checked=!!options.invertLook;
    if(mode==='touch')hint.textContent='Use the stick to walk. Drag to look. Tap an exhibit to use it.';
    else if(mode==='gamepad')hint.textContent='Left stick to walk. Hold RB / R1 to sprint. Right stick to look. X / Square to use an exhibit.';
    else{
      const mouse=options.dragLook?'Drag to look.':'Click to look. Esc pauses and frees the mouse.';
      const arrows=options.arrowMode==='turn'?'Arrows turn and tilt.':'Arrows also walk.';
      hint.textContent=mouse+' WASD to walk. Hold Shift to sprint. '+arrows+' E to use an exhibit.';
    }
  }
  function setMode(next){if(next&&next!==mode){mode=next;refresh();}}
  function expand(show){body.hidden=!show;opener.setAttribute('aria-expanded',String(show));}
  function close(){expand(false);}
  function pointer(event){setMode(event.pointerType==='touch'?'touch':'mouse');}
  function keyboard(){setMode('keyboard');}
  function focus(event){if(event.target.hasAttribute('data-expo-pad-focus'))setMode('gamepad');}
  opener.addEventListener('click',()=>{if(opener.hasAttribute('data-expo-pad-focus'))setMode('gamepad');expand(body.hidden);});
  card.addEventListener('pointerdown',pointer,true);
  card.addEventListener('keydown',keyboard,true);
  card.addEventListener('focusin',focus);
  refresh();close();
  return {setMode,refresh,close,dispose(){
    card.removeEventListener('pointerdown',pointer,true);card.removeEventListener('keydown',keyboard,true);
    card.removeEventListener('focusin',focus);root.remove();style.remove();
  }};
}


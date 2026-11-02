// Local visual review sheets use the same live scene, materials and fit callbacks.
export function installReviewStudio({info,renderCase,errors}) {
  const css=document.createElement('style');css.textContent=`body{height:auto!important;overflow:auto!important;background:#eee9df!important}body>header,body>main,#wardrobe-qa,#qa-scrub{display:none!important}.review{padding:20px;font:14px system-ui;color:#282623}.review h1{font:28px Georgia;margin:0 0 16px}.review-controls{display:flex;gap:12px;align-items:end;flex-wrap:wrap;margin-bottom:14px}.review label{display:grid;gap:5px}.review select,.review button{padding:9px;border:1px solid #aaa;background:#fff;color:#222}.review-sheet{width:100%;height:auto;display:block}.review pre{white-space:pre-wrap;font:12px monospace}.review [hidden]{display:none!important}.review .download{display:inline-block;padding:10px;background:#272724;color:white;text-decoration:none;margin:10px 0}`;document.head.append(css);
  const host=document.createElement('section');host.className='review';host.innerHTML=`<h1>Rain fitting review</h1><div class="review-controls"><label>Review suite<select id="reviewSuite"><option value="hair">Hair with every extra</option><option value="dress">Outfit animation</option><option value="looks">Complete look animation</option><option value="arms">Arms animation</option><option value="ankles">Ankles animation</option><option value="shoes">Shoes animation</option><option value="legwear">Legwear animation</option></select></label><label>Review item<select id="reviewItem"></select></label><label>Review animation<select id="reviewClip"><option value="idle">Idle</option><option value="greeting">Hello</option><option value="jump">Cheer</option></select></label><button id="renderSheet">Render sheet</button></div><p id="reviewStatus" role="status">Choose a sheet.</p><a id="downloadSheet" class="download" hidden>Download sheet</a><img id="reviewSheet" class="review-sheet" alt="Rendered fitting contact sheet"><pre id="reviewDiagnostics"></pre>`;document.body.append(host);
  const $=id=>host.querySelector('#'+id),suite=$('reviewSuite'),item=$('reviewItem'),clip=$('reviewClip');
  const layeredOption=document.createElement('option');layeredOption.value='layers';layeredOption.textContent='Dress with every underlayer';suite.append(layeredOption);
  const fittingsOption=document.createElement('option');fittingsOption.value='fittings';fittingsOption.textContent='Straps with every hairstyle';suite.append(fittingsOption);
  const entries=()=>suite.value==='looks'?info.looks:suite.value==='fittings'?info.slots.accessories.filter(e=>['hazmat-mask','back-katana'].includes(e.id)):info.slots[suite.value==='layers'?'dress':suite.value]||[];
  function populate(){item.replaceChildren();for(const entry of entries()){const o=document.createElement('option');o.value=entry.id;o.textContent=entry.name||entry.id;item.append(o);}}
  suite.addEventListener('change',populate);populate();
  let currentUrl,batchRunning=false;
  const batch=document.createElement('button');batch.textContent='Export entire suite';batch.id='exportSuite';$('renderSheet').after(batch);
  const focused=document.createElement('button');focused.textContent='Export this item';focused.id='exportItem';batch.after(focused);
  const closeup=document.createElement('label');closeup.innerHTML='<input id="reviewCloseup" type="checkbox">Hem close-up';focused.after(closeup);
  async function renderSheet(){
    const entry=entries().find(x=>x.id===item.value);if(!entry)return;
    const clipId=clip.value;
    for(const control of [suite,item,clip,$('reviewCloseup'),$('renderSheet'),batch,focused])control.disabled=true;
    $('downloadSheet').hidden=true;$('reviewStatus').textContent='Rendering the fitted model...';
    const cases=[],base={...info.defaultLook,accessories:[],arms:'none',ankles:'none'};
    const add=(label,look,time,view,frame)=>cases.push({label,look,clip:clipId,time,view,frame});
    const suiteId=suite.value;
    if(suiteId==='hair'){
      const time=clip.value==='greeting'?1.5:clip.value==='jump'?1.5:0;
      for(const extra of [{id:'none',name:'No extra'},...info.slots.accessories]){
        const look={...base,hair:entry.id,accessories:extra.id==='none'?[]:[extra.id]};
        add(extra.name+' / front',look,time,0,extra.id==='back-katana'?'upper':'head');
        add(extra.name+' / '+(extra.id==='back-katana'?'back':'side'),look,time,extra.id==='back-katana'?Math.PI+.3:Math.PI/2,extra.id==='back-katana'?'upper':'head');
      }
    }else if(suiteId==='fittings'){
      for(const hair of info.slots.hair){const look={...base,hair:hair.id,accessories:[entry.id]},frame=entry.id==='back-katana'?'upper':'head';add(hair.name+' / front',look,1.5,0,frame);add(hair.name+' / '+(entry.id==='back-katana'?'back':'side'),look,1.5,entry.id==='back-katana'?Math.PI+.3:Math.PI/2,frame);}
    }else if(suiteId==='layers'){
      for(const legwear of info.slots.legwear){const look={...base,dress:entry.id,legwear:legwear.id};add(legwear.name+' / front',look,1.5,0,'full');add(legwear.name+' / back',look,1.5,Math.PI,'full');}
    }else{
      let look={...base,[suiteId]:entry.id};
      if(suiteId==='looks')look={...base,...entry.look};
      if(suiteId==='dress'&&entry.id==='hazmat-yellow')look={...base,...info.looks.find(x=>x.id==='hazmat')?.look};
      if(suiteId==='dress'&&entry.id==='action-yellow')look={...base,...info.looks.find(x=>x.id==='action')?.look};
      if(suiteId==='arms')look={...look,dress:'satin-original',hair:'chestnut-bob'};
      if(suiteId==='ankles')look={...look,shoes:'rose-shoes',legwear:'bare-legs'};
      const hem=suiteId==='dress'&&$('reviewCloseup').checked;
      const frame=hem?'hem':suiteId==='arms'?'arms':suiteId==='shoes'?'feet':suiteId==='ankles'?'legs':'full';
      for(const time of [0,.75,1.5,3,4.5,5.75]){add(time.toFixed(2)+'s / front',look,time,0,frame);add(time.toFixed(2)+(hem?'s / quarter':'s / back'),look,time,hem?Math.PI/4:Math.PI,frame);}
    }
    const tw=suiteId==='hair'?280:270,th=suiteId==='hair'?340:420,cols=suiteId==='hair'?8:6,header=58,label=28,rows=Math.ceil(cases.length/cols),sheet=document.createElement('canvas');sheet.width=tw*cols;sheet.height=header+(th+label)*rows;
    const ctx=sheet.getContext('2d');ctx.fillStyle='#eee9df';ctx.fillRect(0,0,sheet.width,sheet.height);ctx.fillStyle='#292724';ctx.font='24px system-ui';ctx.fillText(`${entry.name||entry.id} | ${suiteId} | ${clip.value}`,16,34);
    try{
      for(let i=0;i<cases.length;i++){
        const c=cases[i],x=i%cols*tw,y=header+Math.floor(i/cols)*(th+label);
        const source=renderCase({...c,width:tw,height:th});ctx.drawImage(source,x,y,tw,th);ctx.fillStyle='#292724';ctx.font='13px system-ui';ctx.fillText(c.label,x+7,y+th+19,tw-12);
        ctx.strokeStyle='#c9c2b8';ctx.strokeRect(x+.5,y+.5,tw-1,th+label-1);
        if(i%4===3){$('reviewStatus').textContent=`Rendered ${i+1} of ${cases.length} views`;await new Promise(resolve=>requestAnimationFrame(resolve));}
      }
      if(currentUrl)URL.revokeObjectURL(currentUrl);const blob=await new Promise(resolve=>sheet.toBlob(resolve,'image/png'));currentUrl=URL.createObjectURL(blob);$('reviewSheet').src=currentUrl;
      const name=`polish3-${suiteId}-${entry.id}-${clip.value}${cases[0]?.frame==="hem"?"-hem":""}.png`;$('downloadSheet').href=currentUrl;$('downloadSheet').download=name;$('downloadSheet').hidden=false;
      $('reviewStatus').textContent=`Ready: ${cases.length} rendered views. ${errors.length} renderer errors.`;
      $('reviewDiagnostics').textContent=JSON.stringify({suite:suiteId,item:entry.id,clip:clip.value,views:cases.length,framing:cases[0]?.frame,revision:info.fit?.revision,tailoring:info.tailoring?.revision,hairRevision:info.hairstyles?.revision,jewelryRevision:info.jewelry?.revision,foundationRevision:info.foundationAccessoryFit?.revision,footwearRevision:info.footwearFit?.revision,errors},null,2);
      return {name,blob,diagnostics:$('reviewDiagnostics').textContent};
    }catch(error){$('reviewStatus').textContent='Review failed: '+error.message;console.error(error);}finally{for(const control of [suite,item,clip,$('reviewCloseup'),$('renderSheet'),batch,focused])control.disabled=batchRunning;}
  }
  $('renderSheet').addEventListener('click',renderSheet);
  async function exportSheets(list,scope='all'){
    batch.disabled=true;focused.disabled=true;batchRunning=true;const suiteId=suite.value,files=[];
    try{
      for(const entry of list)for(const clipId of ['idle','greeting','jump']){
        item.value=entry.id;clip.value=clipId;const result=await renderSheet();if(!result)throw Error('Sheet failed');
        files.push({name:result.name,data:new Uint8Array(await result.blob.arrayBuffer())},{name:result.name.replace('.png','.json'),data:new TextEncoder().encode(result.diagnostics)});
      }
      URL.revokeObjectURL(currentUrl);currentUrl=URL.createObjectURL(storedZip(files));
      $('downloadSheet').href=currentUrl;$('downloadSheet').download=`polish3-${suiteId}-${scope}.zip`;$('downloadSheet').hidden=false;
      $('reviewStatus').textContent=`Suite ready: ${list.length*3} checked sheets. ${errors.length} renderer errors.`;
      $('reviewDiagnostics').textContent=JSON.stringify({suite:suiteId,sheets:list.length*3,items:list.map(e=>e.id),errors},null,2);
    }catch(error){$('reviewStatus').textContent='Suite failed: '+error.message;console.error(error);}finally{batchRunning=false;for(const control of [suite,item,clip,$('reviewCloseup'),$('renderSheet'),batch,focused])control.disabled=false;}
  }
  batch.addEventListener('click',()=>exportSheets([...entries()].filter(e=>e.id!=='none')));
  focused.addEventListener('click',()=>exportSheets(entries().filter(e=>e.id===item.value),item.value));
}

// A stored ZIP keeps the review exporter local and dependency-free.
function storedZip(files){
  const chunks=[],directory=[];let offset=0;
  const crc32=data=>{let crc=0xffffffff;for(const byte of data){crc^=byte;for(let j=0;j<8;j++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return(crc^0xffffffff)>>>0;};
  for(const file of files){
    const name=new TextEncoder().encode(file.name),crc=crc32(file.data),size=file.data.length,local=new Uint8Array(30+name.length),lv=new DataView(local.buffer);
    lv.setUint32(0,0x04034b50,true);lv.setUint16(4,20,true);lv.setUint32(14,crc,true);lv.setUint32(18,size,true);lv.setUint32(22,size,true);lv.setUint16(26,name.length,true);local.set(name,30);chunks.push(local,file.data);
    const central=new Uint8Array(46+name.length),cv=new DataView(central.buffer);cv.setUint32(0,0x02014b50,true);cv.setUint16(4,20,true);cv.setUint16(6,20,true);cv.setUint32(16,crc,true);cv.setUint32(20,size,true);cv.setUint32(24,size,true);cv.setUint16(28,name.length,true);cv.setUint32(42,offset,true);central.set(name,46);directory.push(central);offset+=local.length+size;
  }
  const end=new Uint8Array(22),ev=new DataView(end.buffer);ev.setUint32(0,0x06054b50,true);ev.setUint16(8,files.length,true);ev.setUint16(10,files.length,true);ev.setUint32(12,directory.reduce((n,d)=>n+d.length,0),true);ev.setUint32(16,offset,true);return new Blob([...chunks,...directory,end],{type:'application/zip'});
}

const PAPER='#dde8db', INK='#16372f', TEAL='#157569', SOFT='#9cb3a3', RULE='#91a99c', AMBER='#94621c';
const font=(size,weight=500)=>weight+' '+size+'px "Segoe UI",Roboto,sans-serif';
const digits=(size)=>'600 '+size+'px Consolas,"Roboto Mono",monospace';
function text(g,str,x,y,size,color=INK,weight=500){
  g.fillStyle=color;g.font=font(size,weight);g.fillText(str,x,y);
}
function path(g,points,color=TEAL,width=4,dash=[]){
  g.beginPath();g.strokeStyle=color;g.lineWidth=width;g.lineJoin='round';g.lineCap='round';g.setLineDash(dash);
  points.forEach(([x,y],i)=>i?g.lineTo(x,y):g.moveTo(x,y));g.stroke();g.setLineDash([]);
}
function dot(g,x,y,r,color){g.fillStyle=color;g.beginPath();g.arc(x,y,r,0,Math.PI*2);g.fill();}
function rule(g,x,y,w){g.fillStyle=RULE;g.fillRect(x,y,w,1.5);}
function quantity(g,value,unit,x=32,y=220){
  g.font=digits(77);g.fillStyle=INK;g.fillText(value,x,y);
  text(g,unit,x+g.measureText(value).width+9,y-4,27,TEAL,600);
}
function metric(g,label,value,x){
  text(g,label,x,344,22,TEAL);g.fillStyle=INK;g.font=digits(34);g.fillText(value,x,389);
}
function heading(g,title,status,amber=false){
  g.fillStyle=PAPER;g.fillRect(0,0,768,432);
  g.fillStyle=TEAL;g.fillRect(0,0,10,432);
  text(g,title,32,68,40,INK,600);g.font=font(22,600);
  const x=735-g.measureText(status).width;
  dot(g,x-14,54,5,amber?AMBER:TEAL);text(g,status,x,62,22,amber?AMBER:TEAL,600);rule(g,32,93,704);
}
function greenhouse(g,p){
  const test=p.time<p.pulseUntil;
  heading(g,'Greenhouse loop',test?'Pulse '+Math.ceil(p.pulseUntil-p.time)+' s':p.delivery>0?'Circulating':'Recovering',test);
  quantity(g,p.delivery.toFixed(1),'L/min');text(g,'Supply to beds',35,258,24,TEAL);
  const level=Math.min(1,p.tank/96);
  path(g,[[416,163],[416,251],[482,251],[482,163]],INK,4);
  g.fillStyle='#83bcb0';g.fillRect(421,247-77*level,56,77*level);
  path(g,[[449,184],[449,135],[695,135]],p.delivery>0?TEAL:SOFT);
  for(const x of [558,615,672]){
    path(g,[[x,135],[x,161]],p.delivery>0?TEAL:SOFT,3);
    if(p.delivery>0){dot(g,x,172,2.8,TEAL);dot(g,x,182,2.1,TEAL);}
    path(g,[[x,222],[x,194],[x-12,185]],INK,3);
    path(g,[[x,208],[x+14,194]],TEAL,3);
  }
  path(g,[[523,214],[536,237],[697,237],[710,214]],INK,4);
  path(g,[[616,239],[616,266],[463,266],[463,254]],TEAL,3,[5,6]);
  text(g,'Return',574,295,19,TEAL);rule(g,32,309,704);
  metric(g,'Reservoir',p.tank.toFixed(1)+' L',34);metric(g,'Recovered',p.recovered.toFixed(1)+' L/min',300);
}
function history(g,values){
  const x=393,y=127,w=332,h=110;
  for(let i=0;i<=2;i++)rule(g,x,y+i*h/2,w);
  const points=values.map((n,i)=>[x+w*(30-values.length+1+i)/30,y+h-(Math.max(800,Math.min(2100,n))-800)*h/1300]);
  if(points.length>1)path(g,points,TEAL,4);
  if(points.length)dot(g,...points[points.length-1],5,TEAL);
  text(g,values.length>1?'Last '+(values.length-1)*2+' s':'Sampling',x,273,20,TEAL);
}
function grid(g,p,detail){
  heading(g,'Energy distribution',detail?'Circuit detail':'Grid connected');
  if(detail){
    const circuits=[['Gallery lighting','lighting'],['Ventilation','ventilation'],['Water loop','irrigation'],
      ['Projector lamps','projectors'],['Services & audio','services']];
    circuits.forEach(([name,key],i)=>{
      const y=144+i*56,v=p.circuits[key];text(g,name,35,y,25,INK);
      g.fillStyle='#c3d1c4';g.fillRect(270,y-12,280,9);g.fillStyle=TEAL;g.fillRect(270,y-12,280*v/1300,9);
      g.font=digits(25);g.fillStyle=INK;g.textAlign='right';g.fillText(v+' W',725,y);g.textAlign='left';
    });return;
  }
  quantity(g,(p.watts/1000).toFixed(2),'kW');text(g,'Building demand',35,258,24,TEAL);
  history(g,p.history);rule(g,32,309,704);
  metric(g,'Roof array',(p.roof/1000).toFixed(2)+' kW',34);
  metric(g,p.batteryWatts>0?'Reserve supplying':'Reserve charging',Math.round(Math.abs(p.batteryWatts))+' W',364);
}
function air(g,p){
  const opened=p.time<p.ventTestUntil;
  heading(g,'Roof intake',opened?'Vent test':'Automatic',opened);
  quantity(g,String(Math.round(p.air*165)),'m³/h');text(g,'Intake flow',35,258,24,TEAL);
  for(let i=0;i<5;i++){
    const x=430+i*54,angle=.18+p.air*.95,length=36;
    path(g,[[x-length*Math.cos(angle),200+length*Math.sin(angle)],[x+length*Math.cos(angle),200-length*Math.sin(angle)]],INK,7);
    dot(g,x,200,3,TEAL);
  }
  path(g,[[404,163],[404,148],[717,148],[717,163]],SOFT,3);
  path(g,[[404,235],[404,252],[717,252],[717,235]],SOFT,3);
  text(g,'Louvre position',400,291,20,TEAL);
  rule(g,32,309,704);metric(g,'Intake opening',Math.round(p.air*100)+'%',34);
  metric(g,'Terrace occupancy',p.zone==='catwalk'?'1 visitor':'0 visitors',336);
}
function reserve(g,p){
  const charge=p.batteryWh/p.batteryCapacityWh,draw=p.batteryWatts;
  heading(g,'Plant service',draw>0?'Supplying':draw<0?'Charging':'Holding',draw>0);
  quantity(g,(p.batteryWh/1000).toFixed(2),'kWh');text(g,'Available reserve',35,258,24,TEAL);
  for(let i=0;i<4;i++){
    const x=419+i*76,y=152,fill=Math.min(1,Math.max(0,charge*4-i));
    path(g,[[x+16,y],[x+16,y-8],[x+37,y-8],[x+37,y]],INK,3);
    g.strokeStyle=INK;g.lineWidth=3;g.strokeRect(x,y,54,80);
    if(fill>0){g.fillStyle=TEAL;g.fillRect(x+5,y+75-70*fill,44,70*fill);}
  }
  text(g,Math.round(charge*100)+'% charged',418,279,21,TEAL);
  rule(g,32,309,704);metric(g,draw>0?'Supplying the grid':'Charging power',Math.round(Math.abs(draw))+' W',34);
  metric(g,'Service load',p.circuits.services+' W',364);
}

export function paintInstrument(g,w,h,kind,state,detail=false){
  g.save();g.scale(w/768,h/432);g.textBaseline='alphabetic';g.textAlign='left';
  if(kind==='greenhouse')greenhouse(g,state);
  else if(kind==='grid'||(kind==='station'&&detail))grid(g,state,detail);
  else if(kind==='station')reserve(g,state);
  else air(g,state);
  g.restore();
}

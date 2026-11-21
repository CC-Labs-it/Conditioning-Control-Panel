// The instrument panels share one plant. Water moves between three vessels and all power figures use the same bus.
const STEP=.25, TOTAL_WATER=96, BATTERY_WH=3200, ROOF_WATTS=1650;
const inhabitedLighting={atrium:410,hall:485,twelve:390,dome:275,shop:250,hoof:220,habitat:235,service:130,catwalk:95,secret:80};
const occupiedAir={atrium:.62,catwalk:.72};

export function createBuildingPlant(){
  const p={time:0,zone:'atrium',tank:86,bed:7,returnTank:3,delivery:0,drain:0,recovered:0,air:.40,
    pulseUntil:0,ventTestUntil:0,pulseCount:0,batteryWh:2020,batteryCapacityWh:BATTERY_WH,watts:0,roof:0,batteryWatts:0,
    lamps:0,voices:0,circuits:{},history:[],samples:0,visits:0};
  let accumulator=0,nextSample=0,lastZone='';
  function read(inputs,dt=STEP){
    p.zone=inputs.zone||p.zone;
    if(p.zone!==lastZone){p.visits++;lastZone=p.zone;}
    p.lamps=inputs.projectors||0;p.voices=inputs.voices||0;
    const fullPulse=p.time<p.pulseUntil,auto=p.time%76<22;
    p.delivery=fullPulse ? 6.0 : auto ? 2.4 : 0;
    const destination=p.time<p.ventTestUntil ? .94 : occupiedAir[p.zone]||.38;
    p.air+=(destination-p.air)*(1-Math.exp(-dt/3.8));
    p.drain=Math.max(0,p.bed-5.6)*1.32;p.recovered=p.returnTank*1.08;
    const delivered=Math.min(p.tank,p.delivery*dt/60);
    const drainage=Math.min(p.bed,p.drain*dt/60);
    const recovered=Math.min(p.returnTank,p.recovered*dt/60);
    p.bed+=delivered-drainage;p.returnTank+=drainage-recovered;
    p.tank=TOTAL_WATER-p.bed-p.returnTank;
    const c=p.circuits;
    c.lighting=720+(inhabitedLighting[p.zone]||180);
    c.ventilation=Math.round(70+160*p.air);
    c.irrigation=p.delivery?Math.round(27+12*p.delivery):0;
    c.projectors=112*p.lamps;
    c.services=112+14*p.voices+(p.zone==='service'?46:0);
    p.watts=c.lighting+c.ventilation+c.irrigation+c.projectors+c.services;
    const wantedCharge=Math.min(145,Math.max(0,(BATTERY_WH-p.batteryWh)*3600/STEP));
    p.roof=Math.min(ROOF_WATTS,p.watts+wantedCharge);
    p.batteryWatts=p.watts-p.roof;
    p.batteryWh=Math.max(0,Math.min(BATTERY_WH,p.batteryWh-p.batteryWatts*dt/3600));
    if(p.time>=nextSample){
      nextSample=p.time+2;p.history.push(p.watts);if(p.history.length>31)p.history.shift();p.samples++;
    }
  }
  read({zone:'atrium'},0);
  return {state:p,observe:inputs=>read(inputs,0),advance(dt,inputs){
    if(!(dt>0)||!Number.isFinite(dt))return;
    accumulator+=dt;
    while(accumulator+1e-10>=STEP){accumulator=Math.max(0,accumulator-STEP);p.time+=STEP;read(inputs);}
  },pulse(){
    if(p.time<p.pulseUntil)return false;
    p.pulseUntil=p.time+10;p.pulseCount++;read({zone:p.zone,projectors:p.lamps,voices:p.voices},0);return true;
  },ventTest(){
    if(p.time<p.ventTestUntil)return false;
    p.ventTestUntil=p.time+12;return true;
  }};
}

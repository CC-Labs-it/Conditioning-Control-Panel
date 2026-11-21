export function buildingRoute(serviceOnly=false) {
  const routes=[],s=(label,...points)=>routes.push({label,points});
  const arc=(x,z,r,start,sweep,h)=>Array.from({length:41},(_,i)=>[x+r*Math.cos(start+sweep*i/40),h+3.4*i/40,z-r*Math.sin(start+sweep*i/40)]);
  const grand=arc(-3.1,34.07,4.05,0,Math.PI/2,0);
  const spiral=arc(-6.5,21.68,1.94,-Math.PI/2,2.5*Math.PI,3.4);
  const entry=[[0,0,37.2],[0,0,35.5],[.95,0,34.07]];
  const lowerGallery=[[-9.06,3.4,30.02],[-9.06,3.4,23.62]];
  const upperLanding=[[-4.56,6.8,18.5],[-10.3,6.8,18.5],[-10.3,6.8,21],[-12.2,6.8,21]];
  const tower=[[-13.2,6.8,21],[-18.7,6.8,21],[-20.13,6.8,20.4],[-21.95,6.8,20.90],[-22,11.90,12.5],[-22,11.95,11.7],[-22,11.95,-5.80]];
  const tiny=[[-13.2,6.8,21],[-13.2,6.8,28],[-12.55,6.8,28]];
  const upperStaff=[[-10.3,6.8,21],[-10.3,6.8,18.5],[10.3,6.8,18.5],[10.3,6.8,23],[12.5,6.8,23]];
  const high=[[12.5,6.8,23],[30.2,6.8,23],[30.2,6.8,20.71],[26.2,6.8,20.71],[23.4,5.1,20.71],[22.42,5.1,20.71]];
  const low=[[22.42,1.7,23],[23.4,1.7,23],[26.2,0,23],[30.3,0,23],[30.3,0,32]];
  const jump=(goal,after)=>({goal,after});
  s('Arrival and grand curved stair',...entry,...grand);
  s('West mezzanine and spiral stair',...lowerGallery,...spiral,...upperLanding);
  s('Catwalk and dome oculus',...tower);
  s('Return from oculus and view through small door',...tower.toReversed().slice(1),...tiny);
  s('Across the upper atrium into staff access',...tiny.toReversed().slice(1),...upperStaff);
  s('Descend service flight and hidden extra storey',...high,jump([22.42,5.1,22.14],[22.42,1.7,21.96]),...low);
  s('Break room, store room and server cupboard',[25,0,32],[25,0,28.6],[25,0,32],[23.65,0,32],[23.65,0,37.9],[23.65,0,32],[18,0,32],[18,0,27.1],[18,0,32]);
  s('Contract gallery and chair',[29.18,0,32],[29.18,0,41],[30.55,0,41],[84.7,0,41]);
  s('Longer return, then walk back in through original doorway',jump([79.8,0,41],[86,0,51]),jump([86.45,0,51],[80.42,0,41]),[81.15,0,41],jump([79.8,0,41],[86,0,51]),jump([33.3,0,51],[34.2,0,41]),[29.18,0,41],[29.18,0,32]);
  s('Return to atrium and approach service doorway again',[11.3,0,32],[10.65,0,32],[12.55,0,32],[30.3,0,32]);
  s('Climb service stair in reverse through the extra storey',...low.toReversed(),jump([22.42,1.7,21.70],[22.42,5.1,21.93]),...high.toReversed(),...upperStaff.toReversed().slice(1));
  s('Descend spiral and grand stairs to the turnstile',...upperLanding.toReversed().slice(1),...spiral.toReversed(),...lowerGallery.toReversed(),...grand.toReversed(),...entry.toReversed());
  s('Atrium to Expo hall and back',[0,0,37.2],[-2.4,0,37.2],[-2.4,0,33.6],[-3,0,28],[0,0,15.2],[0,0,16.6]);
  const views=[[1.6,2.2,26.2],[-9.1,7.25,21],[-22,5.4,-5],[-8.1,6.0,28],[27,7.9,23],
    [25.3,1.4,29.3],[18,1.2,26.5],[85.7,1.24,41],[29.18,1.25,40],[30.3,1.4,24],[-5.6,5.6,24],[-.9,1.3,34.6],[1.5,1.9,26.6]];
  for(const [index,stage] of routes.entries())stage.aim=views[index];
  if(serviceOnly){
    const keep=routes.slice(6,9);
    routes.length=0;
    s('Staff entrance to blind stair',[12.55,0,32],[30.3,0,32],...low.toReversed(),jump([22.42,1.7,21.7],[22.42,5.1,21.93]),...high.toReversed());
    s('Upper walkway return',...high,jump([22.42,5.1,22.14],[22.42,1.7,21.96]),...low);
    routes.push(...keep);
    s('Return to starting staff door',[12.55,0,32]);
  }
  return routes;
}

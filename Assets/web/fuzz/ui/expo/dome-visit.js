export const TRANSLATION='It is fine. Have some grass.';

export function domePace(speed){return speed<.10 ? 1.5 : speed>2.75 ? .5 : 1;}

export function domeVisit(visit,cue,finish){
  let inside=false,last=null,still=0,full=0,dwell=0,glow=0,offered=false,ending=false,markedSeconds=0;
  return {
    pace(dt,inDome,position){
      if(!inDome){last=null;return 1;}
      const speed=last&&dt>0?Math.hypot(position.x-last.x,position.z-last.z)/dt:0;
      last={x:position.x,z:position.z};
      still=speed<.10?still+dt:0;
      if(still>=8&&visit.ticket.book('dome-still','Stood still in the dome'))cue('v4_dome_still',{kind:'react',zone:'dome',exhibit:'dome'});
      return domePace(speed);
    },
    observe(dt,inDome,depth,secondsAtFull,lookingAtSign){
      if(!inDome){
        if(inside&&depth>=.74)glow=60;
        glow=Math.max(0,glow-dt);still=0;full=0;dwell=0;offered=false;
      }else{
        visit.domeSeconds=(visit.domeSeconds||0)+dt;
        if(visit.domeSpat)markedSeconds+=dt;
        if(depth>=1){
          full+=secondsAtFull;
          if(visit.ticket.stamp('Stayed'))cue('v4_dome_text',{kind:'beat',zone:'dome',exhibit:'dome'});
          if(full>=60){
            if(offered)dwell=lookingAtSign?dwell+dt:0;
            offered=true;
            if(dwell>=3&&!ending){ending=true;finish('stayed');}
          }
        }
      }
      inside=inDome;
    },
    get inscription(){
      if(offered)return 'You may stop here.';
      if(visit.domeSpat&&markedSeconds<20)return 'There is something to see here. Please clean it.';
      return null;
    },
    get afterglow(){return glow/60;}
  };
}

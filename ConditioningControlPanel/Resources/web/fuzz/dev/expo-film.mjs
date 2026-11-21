import { writeFile } from 'node:fs/promises';
import { execFile,spawn } from 'node:child_process';
import { promisify } from 'node:util';
const exec = promisify(execFile);

/** Install in a fresh browser context before navigation to capture the game's existing master mix. */
export async function installGameAudio(context) {
  await context.addInitScript(()=>{
    const original=AudioNode.prototype.connect;
    AudioNode.prototype.connect=function(destination,...args){
      const result=original.call(this,destination,...args);
      if(destination===this.context.destination && !window.__expoRecordingBus) {
        const bus=this.context.createMediaStreamDestination();
        original.call(this,bus);window.__expoRecordingBus=bus;
      }
      return result;
    };
  });
}
export async function beginGameAudio(frame) {
  const before=performance.now();
  await frame.evaluate(()=>{
    const bus=window.__expoRecordingBus;
    if(!bus)throw Error('The real game master is unavailable.');
    const recorder=new MediaRecorder(bus.stream,{mimeType:'audio/webm;codecs=opus',audioBitsPerSecond:112000});
    const chunks=[];recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data);};
    const result=new Promise(resolve=>{recorder.onstop=()=>{
      const file=new FileReader();file.onload=()=>resolve(file.result.split(',')[1]);file.readAsDataURL(new Blob(chunks,{type:'audio/webm'}));
    };});
    window.__expoAudioResult={recorder,result};recorder.start(1000);
  });
  return (before+performance.now())/2;
}
export async function finishGameAudio(frame,output) {
  const content=await frame.evaluate(()=>{const {recorder,result}=window.__expoAudioResult;recorder.stop();return result;});
  await writeFile(output,Buffer.from(content,'base64'));
}
/** Capture the full tab, including subtitles, using the installed FFmpeg and Chrome's compositor. */
export async function beginTabVideo(page,output) {
  const devtools=await page.context().newCDPSession(page);
  const {width,height}=page.viewportSize(),fps=25;
  const encoder=spawn('ffmpeg',['-hide_banner','-loglevel','error','-y','-f','image2pipe','-framerate',String(fps),'-vcodec','mjpeg',
    '-i','pipe:0','-an','-c:v','libx264','-preset','ultrafast','-crf','16','-threads','4','-pix_fmt','yuv420p',output],{windowsHide:true});
  let log='',latest=await page.screenshot({type:'jpeg',quality:88}),frames=0,blocked=false,finishing=false;
  encoder.stderr.on('data',c=>{log+=c;});
  encoder.stdin.on('error',()=>{});
  const result=new Promise((resolve,reject)=>{
    encoder.once('error',reject);encoder.once('close',code=>code===0?resolve():reject(Error(log||'Video encoder exited '+code)));
  });
  const since=performance.now();
  function pump() {
    const goal=Math.max(1,Math.round((performance.now()-since)*fps/1000));
    while(!blocked && frames<goal) {frames++;blocked=!encoder.stdin.write(latest);}
  }
  encoder.stdin.on('drain',()=>{blocked=false;if(!finishing)pump();});
  devtools.on('Page.screencastFrame',e=>{
    latest=Buffer.from(e.data,'base64');void devtools.send('Page.screencastFrameAck',{sessionId:e.sessionId}).catch(()=>{});
  });
  await devtools.send('Page.startScreencast',{format:'jpeg',quality:89,maxWidth:width,maxHeight:height,everyNthFrame:1});
  const clock=setInterval(pump,24);
  const stop=async ()=>{
    clearInterval(clock);await devtools.send('Page.stopScreencast');await devtools.detach();
    pump();finishing=true;encoder.stdin.end();await result;return frames/fps;
  };
  stop.startedAt=since;return stop;
}
export async function muxWalk(video,audio,output,seconds=60,audioAfterVideo=null) {
  // Trim any setup/navigation lead and preserve the final 0.4 seconds of capture as editing handle.
  const {stdout}=await exec('ffprobe',['-v','error','-show_entries','format=duration','-of','csv=p=0',video]);
  const duration=Number.parseFloat(stdout);
  if(!Number.isFinite(duration)||duration<seconds)throw Error('Recorded visit is shorter than the requested film.');
  const trim=Math.max(0,duration-seconds-.4),audioTrim=audioAfterVideo==null?0:trim-audioAfterVideo;
  const align=audioTrim>=0?['-ss',String(audioTrim)]:['-itsoffset',String(-audioTrim)];
  await exec('ffmpeg',['-hide_banner','-loglevel','error','-y','-ss',String(trim),'-i',video,...align,'-i',audio,
    '-t',String(seconds),'-map','0:v:0','-map','1:a:0','-c:v','libx264','-threads','4','-crf','19','-pix_fmt','yuv420p',
    '-c:a','aac','-b:a','144k','-movflags','+faststart',output]);
  return {sourceDuration:duration,cutFrom:trim,cutTo:trim+seconds};
}

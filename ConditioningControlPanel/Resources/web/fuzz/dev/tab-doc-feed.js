// Local preview only. The existing host owns sign-in, consent, licensing and metering.
import { scrolllerMedia } from '../ui/scrolller-pictures.js';

export function createPreviewFeed({ onStatus = () => {}, location = globalThis.location,
  window = globalThis.window, document = globalThis.document,
  setTimer = setTimeout, clearTimer = clearTimeout } = {}) {
  const local = ['127.0.0.1', 'localhost'].includes(location.hostname);
  const capability = local ? { origin:'device', consent:false, clips:true, sessionOptIn:true, playWarmup:true } : null;
  let transport = null, opening = null, pictures = null, disposed = false, choice = null, requestId = 0;
  let timeout = null, abortOpening = null, phase = 'idle', imageCount = 0;
  const clearWait = () => { if(timeout!==null) clearTimer(timeout);timeout=null; };
  function status(text, next, count = 0) {
    phase=next;imageCount=count;
    onStatus(text,{phase,count});
  }
  function failure(reason) {
    const text = reason === 'sign-in' ? 'Sign in to CCP, then retry Scrolller.' :
      reason === 'allowance-used' ? 'The current Scrolller allowance has been used.' :
      reason === 'disabled' ? 'Scrolller is disabled in the current desktop profile.' :
      'The Scrolller connection is unavailable. Retry the connection.';
    status(text,'error');
  }
  function waitForImages() {
    clearWait();
    timeout=setTimer(()=>{
      timeout=null;
      if(!disposed&&phase!=='ready') status('No usable media arrived. Retry Scrolller or choose your own files.','empty');
    },30000);
  }
  function cancel() {
    requestId++;clearWait();abortOpening?.();
    const pending=choice;choice=null;
    pending?.cancel?.();pending?.resolve(false);
    transport?.postMessage({type:'scrolller-release'});
    phase='idle';imageCount=0;
  }
  function receive(event) {
    const frame = event.data;
    if (disposed || !frame || typeof frame !== 'object') return;
    if (frame.type === 'setting' && frame.key === 'media.remoteConsent') {
      pictures?.setRemoteConsent(frame.value, frame.reason);
      if (!frame.value) { clearWait();failure(frame.reason); }
    } else if (frame.type === 'scrolller-feed') {
      pictures?.receiveScrolller(frame);
      if(pictures?.source!=='scrolller') return;
      const count=scrolllerMedia(frame,capability,location.origin).length;
      if(count) {
        clearWait();status(`${count} Scrolller media items are available for the gallery.`,'ready',count);
      } else if(frame.building===true) {
        if(phase==='empty') return;
        status('Preparing media from your current CCP channels...','loading');
        if(timeout===null) waitForImages();
      } else {
        clearWait();status('The current feed has no usable media. Retry Scrolller or choose your own files.','empty');
      }
    }
  }
  async function open() {
    if (!local || disposed) throw new Error('A local preview connection is required.');
    if (transport) return transport;
    if (!opening) opening = new Promise((resolve, reject) => {
      if (window.chrome?.webview) { resolve(); return; }
      const script = document.createElement('script');
      let settled=false;
      const finish=error=>{
        if(settled) return;settled=true;clearTimer(timer);abortOpening=null;script.remove();
        if(error) reject(error);else resolve();
      };
      const timer=setTimer(()=>finish(new Error('The local Scrolller preview host did not respond.')),15000);
      abortOpening=()=>finish(new Error('The preview connection was canceled.'));
      script.src = '/__wheel-feed/bridge.js'; script.async = true;
      script.onload = () => finish();
      script.onerror = () => finish(new Error('The local Scrolller preview host is unavailable.'));
      document.head.append(script);
    }).then(() => {
      if (disposed || !window.chrome?.webview) throw new Error('The local Scrolller bridge did not open.');
      transport = window.chrome.webview;
      transport.addEventListener('message', receive);
      return transport;
    }).catch(error => { opening = null; throw error; });
    return opening;
  }
  return {
    capability,
    attach(value) { pictures = value; },
    cancel,
    send(type, fields = {}) { transport?.postMessage({ type, ...fields }); },
    async choose() {
      const current=++requestId;
      const previous=choice;choice=null;previous?.cancel?.();previous?.resolve(false);clearWait();
      status('Connecting to the current CCP media profile...','connecting');
      try {
        await open();
        if(disposed||current!==requestId) return false;
        return await new Promise(resolve => {
          const pending={resolve,cancel:null};choice=pending;
          timeout=setTimer(()=>{
            if(choice!==pending) return;
            choice=null;timeout=null;pending.cancel?.();
            status('The CCP media profile did not respond. Retry the connection.','error');resolve(false);
          },15000);
          pending.cancel=pictures.chooseScrolller((allowed, reason) => {
            if(choice!==pending) return;
            choice=null;clearWait();
            if(allowed) {
              status('Preparing media from your current CCP channels...','loading');
              waitForImages();pictures.requestFeed?.();
            } else failure(reason);
            resolve(allowed);
          });
        });
      } catch {
        if(!disposed&&current===requestId) status('The local Scrolller adapter is unavailable. Retry after it is running.','error');
        return false;
      }
    },
    debug:()=>({phase,imageCount,pending:Boolean(choice),waiting:timeout!==null}),
    dispose() {
      if (disposed) return;
      cancel();
      pictures?.setGameplayActive(false);
      transport?.removeEventListener('message', receive);
      disposed = true;
    },
  };
}

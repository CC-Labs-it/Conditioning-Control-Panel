const fs = require('fs'), vm = require('vm'), assert = require('assert');
let code = fs.readFileSync('CCP.Avalonia/Views/Deeper/DeeperEditorWindow.Unified.cs', 'utf8').match(/internal const string PreviewWheelScript = @"([\s\S]*?)";/)[1].replace(/""/g, '"');
if (process.argv.includes('--break')) code = code.replace("else window.top.postMessage('ccp_frame_zoom_'+m,'*');", "else {};");
const messages = [], topEvents = {}, frameEvents = {};
let frameRegistrations = 0;
const top = { addEventListener: (n,f) => topEvents[n]=f, chrome:{webview:{postMessage:m=>messages.push(m)}} };
top.top = top;
const proxy = { postMessage: m => topEvents.message({data:m}) };
const frame = {top:proxy, addEventListener:(n,f)=>{ frameRegistrations++; frameEvents[n]=f; }};
vm.runInNewContext(code, {window:top});
vm.runInNewContext(code, {window:frame});
vm.runInNewContext(code, {window:frame}); // a second installation must not duplicate listeners
assert.strictEqual(frameRegistrations,1);
for (const [delta, expected] of [[1,'ccp_zoom_out'],[-1,'ccp_zoom_in']]) {
 let prevented = false;
 frameEvents.wheel({ctrlKey:true,deltaY:delta,preventDefault:()=>prevented=true});
 assert.strictEqual(messages.pop(),expected); assert(prevented);
}
frameEvents.wheel({ctrlKey:false,deltaY:1,preventDefault:()=>{throw Error('ordinary wheel prevented')}});
assert.strictEqual(messages.length,0);
const laterEvents = {};
const laterFrame = {top:proxy, addEventListener:(n,f)=>laterEvents[n]=f};
vm.runInNewContext(code, {window:laterFrame});
laterEvents.wheel({ctrlKey:true,deltaY:1,preventDefault:()=>{}});
assert.strictEqual(messages.pop(),'ccp_zoom_out');
assert.strictEqual(messages.length,0);
console.log('cross-origin frame wheel relay: passed');

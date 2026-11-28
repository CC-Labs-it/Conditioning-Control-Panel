// Run with node. Exercise the actual injected centering script with a small DOM.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../CCP.Avalonia/Views/Deeper/DeeperPreview.cs'), 'utf8');
const match = source.match(/private const string ScrollOnce\s*=\s*("(?:\\.|[^"\\])*")\s*;/);
assert.ok(match, 'centering script is available');
const script = JSON.parse(match[1]);
function video(width, height) {
    return { width, height, count: 0, getBoundingClientRect() { return this; },
        scrollIntoView(options) { this.count++; this.options = options; } };
}
function doc(videos, frames = []) {
    return { querySelectorAll(s) { return s === 'video' ? videos : s === 'iframe' ? frames : []; } };
}
if (!process.argv[2] || process.argv[2] === 'frames') {
    const inside = video(400, 250), nested = video(300, 200);
    const root = doc([], [{ get contentDocument() { throw new Error('cross origin'); } },
        { contentDocument: doc([inside], [{ contentDocument: doc([nested]) }]) }]);
    vm.runInNewContext(script, { document: root, l: video(10, 10) });
    assert.equal(inside.count, 1, 'same-origin frame video is centered without a top-level video');
    assert.equal(nested.count, 1, 'nested same-origin frame video is centered');
    vm.runInNewContext(script, { document: root, l: video(10, 10) });
    assert.equal(inside.count, 1, 'a video is centered once');
}
if (!process.argv[2] || process.argv[2] === 'visible') {
    const hidden = video(0, 0), root = doc([hidden]);
    vm.runInNewContext(script, { document: root, l: hidden });
    assert.equal(hidden.count, 0, 'hidden video is not scrolled');
    assert.ok(!hidden._ccpScrolled, 'hidden video is not marked');
    hidden.width = 400; hidden.height = 250;
    vm.runInNewContext(script, { document: root, l: hidden });
    assert.equal(hidden.count, 1, 'video is centered when it becomes visible');
}

if (!process.argv[2] || process.argv[2] === 'editor') {
    const editorMatch = source.match(/private const string EditorScrollOnce\s*=\s*("(?:\\.|[^"\\])*")\s*;/);
    const editorScript = editorMatch ? JSON.parse(editorMatch[1]) : script;
    const player = video(640, 360), thumbnail = video(100, 80), root = doc([player, thumbnail]);
    vm.runInNewContext(editorScript, { document: root, l: player });
    assert.equal(player.count, 1, 'editor centers its selected video');
    assert.equal(thumbnail.count, 0, 'editor does not scroll later thumbnails');
}
if (!process.argv[2] || process.argv[2] === 'container') {
    const v = video(640, 360), small = video(199, 149), container = video(200, 150);
    const body = video(900, 900), root = doc([v]); root.body = body;
    v.parentElement = small; small.parentElement = container; container.parentElement = body;
    vm.runInNewContext(script, { document: root, l: v });
    assert.equal(v.count, 0, 'player does not scroll the video when a suitable container exists');
    assert.equal(container.count, 1, 'player centers the first suitable ancestor');
    assert.deepEqual({ ...container.options }, { behavior: 'smooth', block: 'center', inline: 'center' });
    assert.equal(body.count, 0, 'body is never selected');
    const lone = video(300, 200), fallback = doc([lone]); fallback.body = body; lone.parentElement = body;
    vm.runInNewContext(script, { document: fallback, l: lone });
    assert.equal(lone.count, 1, 'player falls back to the video');
}
console.log('Deeper video centering: OK');

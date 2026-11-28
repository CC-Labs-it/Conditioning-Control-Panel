import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';

const source = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHostTakeover.cs', import.meta.url), 'utf8');
const script = source.match(/FullscreenFallbackScript = """\s*([\s\S]*?)\s*""";/)[1];
function page() {
    const body = node(), parent = node(), video = node();
    body.appendChild(parent); parent.appendChild(video);
    video.isConnected = true; video.ended = false; video.paused = false;
    video.attributes.style = 'color:red';
    const document = { body, querySelector: () => video, createComment: () => node(), createElement: () => node() };
    const window = { _ccpTakeoverStopped: false };
    return { body, parent, video, window, document, run: () => runInNewContext(script, { window, document }) };
}
function node() {
    return {
        children: [], parentNode: null, isConnected: true, attributes: {},
        style: { cssText: '', setProperty(k,v) { this[k] = v; } },
        appendChild(n) { n.remove(); this.children.push(n); n.parentNode = this; return n; },
        insertBefore(n, ref) { this.children.splice(this.children.indexOf(ref), 0, n); n.parentNode = this; },
        replaceChild(n, ref) { n.remove(); this.children[this.children.indexOf(ref)] = n; n.parentNode = this; ref.parentNode = null; },
        remove() { if (this.parentNode) { const p = this.parentNode; p.children.splice(p.children.indexOf(this), 1); this.parentNode = null; } },
        getAttribute(k) { return this.attributes[k] ?? null; },
        setAttribute(k,v) { this.attributes[k] = v; }, removeAttribute(k) { delete this.attributes[k]; }
    };
}
test('fallback presents the same video and restores its original position and style', () => {
    const p = page();
    assert.equal(p.run(), true);
    assert.notEqual(p.video.parentNode, p.parent);
    assert.equal(p.video.style['object-fit'], 'contain');
    assert.equal(p.run(), false);
    p.window.__ccpTakeoverFallbackExit();
    assert.equal(p.video.parentNode, p.parent);
    assert.equal(p.video.attributes.style, 'color:red');
    assert.equal(p.body.children.length, 1);
});
test('fallback refuses stopped, ended, disconnected, and already fullscreen pages', () => {
    for (const setting of ['stopped', 'ended', 'disconnected', 'fullscreen']) {
        const p = page();
        if (setting === 'stopped') p.window._ccpTakeoverStopped = true;
        if (setting === 'ended') p.video.ended = true;
        if (setting === 'disconnected') p.video.isConnected = false;
        if (setting === 'fullscreen') p.document.fullscreenElement = p.video;
        assert.equal(p.run(), false);
        assert.equal(p.video.parentNode, p.parent);
    }
});

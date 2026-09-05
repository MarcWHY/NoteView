'use strict';

// Execute the exact embedded page script against a minimal browser/network mock.
// This complements the real TCP tests without requiring OBS or a MIDI device.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');
const { performance } = require('perf_hooks');

const source = fs.readFileSync(path.join(__dirname, '..', 'src', 'ObsOutputServer.cs'), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/""/g, '"');
let session = 'a'.repeat(32), version = '1', payload = 'old frame';
let unavailable = false, activeRequests = 0, maxRequests = 0, requestCount = 0;
let pagehide = null, nextUrl = 0, assertions = 0;
const urls = new Map();
const frame = { src: undefined, removeAttribute(name) { if (name === 'src') this.src = undefined; } };
function check(condition, message) { assert.ok(condition, message); assertions++; }
function wait(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }
async function until(predicate, message, timeout = 1000) {
    const started = performance.now();
    while (!predicate()) {
        if (performance.now() - started >= timeout) throw new Error(message);
        await wait(10);
    }
    assertions++;
}

class MockImage {
    set src(value) {
        setTimeout(() => {
            if (urls.has(value)) { if (this.onload) this.onload(); }
            else if (this.onerror) this.onerror();
        }, 2);
    }
}

async function fetchFrame(target, options) {
    activeRequests++;
    requestCount++;
    maxRequests = Math.max(maxRequests, activeRequests);
    try {
        await new Promise((resolve, reject) => {
            let timer = null;
            const onAbort = () => { clearTimeout(timer); reject(new Error('aborted')); };
            options.signal.addEventListener('abort', onAbort, { once: true });
            if (!unavailable) timer = setTimeout(() => {
                options.signal.removeEventListener('abort', onAbort);
                resolve();
            }, 5);
        });
        const query = new URL(target, 'http://127.0.0.1:18765').searchParams;
        const unchanged = query.get('after') === version && query.get('session') === session;
        const values = { 'X-Frame-Version': version, 'X-Frame-Session': session };
        return { status: unchanged ? 204 : 200, headers: { get: name => values[name] }, blob: async () => ({ payload }) };
    } finally { activeRequests--; }
}

(async () => {
    try {
        vm.runInNewContext(script, {
            document: { getElementById: () => frame },
            window: { addEventListener: (name, callback) => { if (name === 'pagehide') pagehide = callback; } },
            URL: { createObjectURL(blob) { const url = 'blob:test-' + ++nextUrl; urls.set(url, blob.payload); return url; }, revokeObjectURL(url) { urls.delete(url); } },
            Image: MockImage, AbortController, performance, fetch: fetchFrame,
            setTimeout, clearTimeout, setInterval, clearInterval
        }, { filename: 'NoteView embedded OBS page' });
        await until(() => urls.get(frame.src) === 'old frame', 'The initial frame did not load');
        const oldUrl = frame.src;
        await wait(100);
        check(frame.src === oldUrl, 'Unchanged 204 responses must retain the image without flicker');
        check(urls.size === 1, 'Unchanged polling must not allocate queued image URLs');
        session = 'b'.repeat(32);
        payload = 'new frame';
        await until(() => urls.get(frame.src) === 'new frame', 'Quick restart at version 1 did not replace the frame');
        check(!urls.has(oldUrl) && urls.size === 1, 'Replacing an image must release its previous URL');
        unavailable = true;
        await until(() => frame.src === undefined, 'A lost connection did not clear the image after two seconds', 2500);
        check(urls.size === 0, 'Disconnect must release the retained image URL');
        unavailable = false;
        payload = 'reconnected frame';
        await until(() => urls.get(frame.src) === 'reconnected frame', 'The source did not reconnect automatically', 2500);
        check(maxRequests === 1, 'The browser must have at most one fetch in flight');
        check(requestCount > 3, 'The browser must continue polling after successful responses');
        pagehide();
        await wait(10);
        check(urls.size === 0 && activeRequests === 0, 'Closing the page must release image URLs and abort pending fetches');
        console.log(`ObsBrowserPageTests: PASS (${assertions} assertions)`);
    } catch (error) {
        if (pagehide) pagehide();
        console.error(error);
        process.exitCode = 1;
    }
})();

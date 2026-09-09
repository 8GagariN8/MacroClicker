const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const html = fs.readFileSync(path.join(__dirname, '../../src/MacroClicker/Web/index.html'), 'utf8');
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(match => match[1]);
assert.equal(scripts.length, 2);
for (const script of scripts) new vm.Script(script);
let passed = 1;
const events = {}, messages = [];
vm.runInNewContext(scripts[0], { window: {
    addEventListener: (name, handler) => { events[name] = handler; },
    chrome: { webview: { postMessage: message => messages.push(message) } }
} });
events.error({ message: 'Unexpected token', lineno: 10 });
assert.equal(messages[0].command, 'startupError');
assert.match(messages[0].message, /Unexpected token; line 10/); passed++;
events.unhandledrejection({ reason: 'sensitive payload' });
assert.equal(messages[1].command, 'startupError');
assert.ok(!messages[1].message.includes('sensitive payload')); passed++;
const previewEvents = {};
vm.runInNewContext(scripts[0], { window: { addEventListener: (name, fn) => { previewEvents[name] = fn; } } });
previewEvents.error({ message: 'preview', lineno: 1 });
previewEvents.unhandledrejection({}); passed++;

// Exercise the actual receiver. DOM painting and native APIs are not emulated.
const main = scripts[1];
const receiver = main.slice(main.indexOf('let initialStateRendered=false;'), main.indexOf("if(native)window.chrome.webview.addEventListener"));
assert.ok(receiver.length > 0);
function createReceiver(failRender = false) {
    const sent = [], sequence = [];
    const context = vm.createContext({ setTheme() {}, render() { if (failRender) throw Error('render failed'); sequence.push('render'); },
        post: name => { sent.push(name); sequence.push(name); } });
    vm.runInContext(receiver, context);
    return { context, sent, sequence };
}
const state = "receive({type:'state',document:{Nodes:[]},settings:{DarkTheme:true},library:[],keys:[]})";
const success = createReceiver();
vm.runInContext(state, success.context);
assert.deepEqual(success.sequence, ['render', 'rendered']); passed++;
vm.runInContext(state, success.context);
assert.deepEqual(success.sent, ['rendered']); passed++;
const failure = createReceiver(true);
assert.throws(() => vm.runInContext(state, failure.context), /render failed/);
assert.equal(failure.sent.length, 0); passed++;
console.log(`PASS ${passed}/7: script syntax, early error reporting, render acknowledgement`);

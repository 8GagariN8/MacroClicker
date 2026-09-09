const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), assert = require('node:assert/strict');
const html = fs.readFileSync(path.join(__dirname, '../../src/MacroClicker/Web/index.html'), 'utf8');
const helpers = html.slice(html.indexOf('function cleanDocument('), html.indexOf('function getDoc('));
const switchView = html.slice(html.indexOf('function switchView('), html.indexOf('function highlight('));
const original = { Version:2, Name:'Saved', RepeatCount:1, Nodes:[{Name:'Window',ProcessName:'test',WindowTitle:'Test',TargetId:'123',Steps:[{Kind:'Key',Input:'4',HoldMs:50},{Kind:'Key',Input:'5',HoldMs:50}]}] };
function create(dirty = false) {
 const nodes = {}, events = [], errors = [];
 const context = vm.createContext({ doc:structuredClone(original), jsonMode:false, dirty, kinds:{Key:['Key']},
   clone:structuredClone, $:id=>nodes[id]??=( {value:'',setAttribute(){}} ),
   getDoc(){return context.doc}, render(){if(context.jsonMode)context.$('jsonText').value=JSON.stringify(context.cleanDocument(context.doc),null,2)},
   markDirty(){context.dirty=true;events.push('dirty')}, toast:message=>errors.push(message) });
 vm.runInContext(helpers + switchView, context); context.switchView(true); assert.equal(errors.length,0); assert.equal(context.jsonMode,true);
 return {context,nodes,events,errors};
}
let passed = 0;
let t=create(); const bound=t.context.doc; t.context.switchView(false);
assert.equal(t.context.jsonMode,false); assert.equal(t.errors.length,0); assert.equal(t.context.dirty,false); assert.equal(t.events.length,0); assert.equal(t.context.doc,bound); assert.equal(t.context.doc.Nodes[0].TargetId,'123'); passed++;
t=create(true); t.context.switchView(false); assert.equal(t.context.dirty,true); assert.equal(t.events.length,0); passed++;
t=create(); let parsed=JSON.parse(t.nodes.jsonText.value); t.nodes.jsonText.value=JSON.stringify(Object.fromEntries(Object.entries(parsed).reverse()));
t.context.markJsonEdit();t.context.switchView(false);assert.equal(t.context.dirty,false);passed++;
t=create();t.nodes.jsonText.value='\n  '+t.nodes.jsonText.value+'\n';t.context.markJsonEdit();t.context.switchView(false);assert.equal(t.context.dirty,false);passed++;
t=create();parsed=JSON.parse(t.nodes.jsonText.value);parsed.Nodes[0].Steps[0].HoldMs=100;t.nodes.jsonText.value=JSON.stringify(parsed);t.context.markJsonEdit();t.context.switchView(false);
assert.equal(t.context.dirty,true);assert.equal(t.context.doc.Nodes[0].Steps[0].HoldMs,100);passed++;
t=create();parsed=JSON.parse(t.nodes.jsonText.value);parsed.Nodes[0].Steps.reverse();t.nodes.jsonText.value=JSON.stringify(parsed);t.context.switchView(false);
assert.equal(t.context.dirty,true);assert.equal(t.context.doc.Nodes[0].Steps[0].Input,'5');passed++;
t=create();t.nodes.jsonText.value='{';t.context.markJsonEdit();t.context.switchView(false);assert.equal(t.context.jsonMode,true);assert.equal(t.context.dirty,true);assert.equal(t.errors.length,1);assert.equal(t.context.doc.Nodes[0].Steps[0].Input,'4');passed++;
t=create();for(let i=0;i<5;i++){t.context.switchView(false);t.context.switchView(true)}assert.equal(t.context.dirty,false);assert.equal(t.events.length,0);passed++;
console.log(`PASS ${passed}/8: view switches, semantic comparison, real/invalid edits, preserved bindings`);

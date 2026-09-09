const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(path.join(__dirname,'../Web/index.html'),'utf8');
const context=vm.createContext({});
vm.runInContext(html.slice(html.indexOf('function capturedKey('),html.indexOf('function keyEditor(')),context);
let passed=0;
function chord(codes){const capture=context.chordCapture();codes.forEach(code=>capture.down({code}));let result;codes.slice().reverse().forEach(code=>result=capture.up({code}));return result}
assert.equal(chord(['ControlLeft','ShiftLeft','KeyS']),'Ctrl+Shift+S');passed++;
assert.equal(chord(['AltLeft','ShiftLeft']),'Alt+Shift');passed++;
assert.equal(chord(['MetaLeft','Space']),'Win+Space');passed++;
assert.equal(context.capturedKey({code:'KeyC',key:'с'}),'C');passed++;
assert.equal(chord(['Numpad4']),'NumPad4');assert.equal(chord(['Digit4']),'4');passed++;
for(const code of ['Enter','Tab','Escape'])assert.equal(chord([code]),code);passed++;
assert.equal(chord(['ControlLeft','Equal']),'Ctrl+Oemplus');passed++;
let capture=context.chordCapture();capture.down({code:'ControlLeft'});capture.down({code:'KeyC'});capture.down({code:'KeyC',repeat:true});assert.equal(capture.up({code:'ControlLeft'}),null);assert.equal(capture.up({code:'KeyC'}),'Ctrl+C');passed++;
capture=context.chordCapture();assert.equal(capture.up({code:'Enter'}),null);assert.equal(capture.down({code:'Unidentified'}),null);passed++;
capture=context.chordCapture();capture.down({code:'KeyS',ctrlKey:true,shiftKey:true});assert.equal(capture.up({code:'KeyS'}),'Ctrl+Shift+S');passed++;
assert.equal(chord(['ControlLeft','ControlRight','KeyC']),'Ctrl+C');passed++;
const posts=[],themes=[];context.document={documentElement:{classList:{toggle:(...args)=>themes.push(args)}}};context.post=(...args)=>posts.push(args);
vm.runInContext(html.slice(html.indexOf('function setTheme('),html.indexOf('function renderLibrary(')),context);
context.setTheme(false);context.setTheme(true);
assert.deepEqual(themes,[['light',true],['light',false]]);assert.equal(posts[0][0],'themePreview');assert.equal(posts[0][1].dark,false);assert.equal(posts[1][1].dark,true);passed++;
function element(tag,cls,text=''){return {tag,textContent:text,children:[],listeners:{},attributes:{},append(...items){this.children.push(...items)},setAttribute(k,v){this.attributes[k]=v},addEventListener(k,v){this.listeners[k]=v},focus(){}}}
let input,button;
Object.assign(context,{keys:['A'],stepDraft:{Input:'4'},el:element,field:(_,value)=>value,stepInput:()=>input=element('input'),button:(text,_,onclick)=>{button=element('button','',text);button.onclick=onclick;return button}});
vm.runInContext(html.slice(html.indexOf('function keyEditor('),html.indexOf('function renderStepFields(')),context);
context.keyEditor(element('div'));
function event(type,code){let stopped=false,prevented=false;button.listeners[type]({code,preventDefault(){prevented=true},stopPropagation(){stopped=true}});assert.ok(stopped&&prevented)}
button.onclick();event('keydown','ControlLeft');event('keydown','KeyS');event('keyup','KeyS');assert.equal(context.stepDraft.Input,'4');event('keyup','ControlLeft');assert.equal(context.stepDraft.Input,'Ctrl+S');assert.equal(input.value,'Ctrl+S');assert.equal(button.attributes['aria-pressed'],'false');passed++;
button.onclick();event('keydown','ShiftLeft');button.listeners.blur();assert.equal(context.stepDraft.Input,'Ctrl+S');assert.equal(button.attributes['aria-pressed'],'false');passed++;
button.onclick();event('keydown','ControlLeft');event('keydown','Unidentified');assert.equal(context.stepDraft.Input,'Ctrl+S');assert.equal(button.attributes['aria-pressed'],'false');passed++;
button.onclick();event('keyup','Enter');assert.equal(button.attributes['aria-pressed'],'true');button.onclick();assert.equal(button.attributes['aria-pressed'],'false');assert.equal(context.stepDraft.Input,'Ctrl+S');passed++;
console.log(`PASS ${passed}/16: chords, layouts, repeat/release, theme bridge, editor accept/cancel, shortcut event isolation`);

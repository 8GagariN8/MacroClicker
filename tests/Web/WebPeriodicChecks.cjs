const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(path.join(__dirname,'../../src/MacroClicker/Web/index.html'),'utf8');
const helpers=html.slice(html.indexOf('function cleanDocument('),html.indexOf('function getDoc('));
const editors=html.slice(html.indexOf('function freshNode('),html.indexOf('function renderWindows('));
const update=html.slice(html.indexOf('function updatePeriodic('),html.indexOf('function renderPeriodic('));
const handlers=html.slice(html.indexOf("$('conditionTrigger').onchange="),html.indexOf("$('addStep').onclick="));
const switchView=html.slice(html.indexOf('function switchView('),html.indexOf('function highlight('));
const step={Kind:'Key',Input:'Enter',HoldMs:1};
const node=()=>({Name:'Parent',UseActiveWindow:true,ProcessName:'',WindowTitle:'',Steps:[{...step,Input:'1'}],TransitionDelayMs:2000,PeriodicActions:[],TargetId:'parent-handle'});
const rule=()=>({Enabled:true,Trigger:'Cycles',EveryCycles:6,EveryMs:30000,Node:{...node(),Name:'Extra',Steps:[{...step}],TargetId:'extra-handle'}});
const document=()=>({Version:3,Name:'Test',RepeatCount:12,Nodes:[{...node(),PeriodicActions:[rule()]}]});
function create(){
 const fields={},errors=[],events=[];
 const context=vm.createContext({doc:document(),jsonMode:false,dirty:false,nodeIndex:-1,periodicIndex:null,nodeDraft:null,ruleDraft:null,
  kinds:{Key:['Key']},clone:structuredClone,$:id=>fields[id]??={_value:'',get value(){return this._value},set value(v){this._value=String(v)},hidden:false,checked:false,setAttribute(){},focus(){}},
  toast:m=>errors.push(m),post(){},show(){},renderWindows(){},renderSteps(){},
  close(){context.nodeDraft=null;context.ruleDraft=null;context.periodicIndex=null},
  changed(){context.dirty=true;events.push('change')},markDirty(){context.dirty=true;events.push('dirty')},getDoc(){return context.doc},
  render(){if(context.jsonMode)context.$('jsonText').value=JSON.stringify(context.cleanDocument(context.doc),null,2)}
 });
 vm.runInContext(helpers+editors+update+handlers+switchView,context);return{c:context,fields,errors,events};
}
let passed=0;
let t=create();t.c.editPeriodic(0);assert.equal(t.c.ruleDraft.EveryCycles,6);assert.equal(t.c.nodeDraft.UseActiveWindow,true);t.c.nodeDraft.Steps.push({...step});t.c.$('nodeName').value='Confirm';t.c.$('nodeDelay').value='2';t.c.$('nodeDelayUnit').value='1000';t.c.$('saveNode').onclick();
assert.equal(t.errors.length,0);assert.equal(t.c.doc.Nodes[0].PeriodicActions.length,2);assert.equal(t.c.doc.Nodes[0].PeriodicActions[1].Node.TransitionDelayMs,2000);passed++;
t=create();const original=JSON.stringify(t.c.doc);t.c.editPeriodic(0,0);t.c.nodeDraft.Name='discard';t.c.nodeDraft.Steps[0].Input='C';t.c.close();assert.equal(JSON.stringify(t.c.doc),original);passed++;
t=create();t.c.editPeriodic(0,0);t.c.$('conditionTrigger').value='Time';t.c.conditionFields();assert.equal(t.c.$('conditionCyclesField').hidden,true);assert.match(t.c.$('conditionHint').textContent,/Дополнительные действия и паузы перехода не учитываются/);t.c.$('conditionTime').value='0.5';t.c.$('conditionTimeUnit').value='60000';t.c.$('saveNode').onclick();assert.equal(t.errors.length,0);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].EveryMs,30000);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].EveryCycles,6);passed++;
t=create();t.c.editPeriodic(0,0);t.c.$('conditionCycles').value='0';t.c.$('saveNode').onclick();assert.equal(t.errors.length,1);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].EveryCycles,6);assert.ok(t.c.nodeDraft);passed++;
t=create();t.c.updatePeriodic(0,r=>r.splice(1,0,structuredClone(r[0])));t.c.doc.Nodes[0].PeriodicActions[1].Node.Steps[0].Input='C';assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].Node.Steps[0].Input,'Enter');t.c.updatePeriodic(0,r=>r.reverse());assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].Node.Steps[0].Input,'C');t.c.updatePeriodic(0,r=>r[0].Enabled=false);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].Enabled,false);t.c.updatePeriodic(0,r=>r.splice(0,1));assert.equal(t.c.doc.Nodes[0].PeriodicActions.length,1);passed++;
t=create();t.c.editNode(0);t.c.$('nodeDelay').value='2.5';t.c.$('nodeDelayUnit').value='1000';t.c.$('saveNode').onclick();assert.equal(t.errors.length,0);assert.equal(t.c.doc.Nodes[0].TransitionDelayMs,2500);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].Node.Steps[0].Input,'Enter');passed++;
t=create();const before=t.c.doc;t.c.switchView(true);const json=JSON.parse(t.c.$('jsonText').value);assert.equal(json.Nodes[0].TargetId,undefined);assert.equal(json.Nodes[0].PeriodicActions[0].Node.TargetId,undefined);t.c.switchView(false);assert.equal(t.c.doc,before);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].Node.TargetId,'extra-handle');assert.equal(t.c.dirty,false);passed++;
t=create();t.c.switchView(true);let parsed=JSON.parse(t.c.$('jsonText').value);parsed.Nodes[0].PeriodicActions[0].EveryCycles=7;t.c.$('jsonText').value=JSON.stringify(parsed);t.c.switchView(false);assert.equal(t.c.dirty,true);assert.equal(t.c.doc.Nodes[0].PeriodicActions[0].EveryCycles,7);passed++;
for(const property of ['EveryCycles','EveryMs'])for(const value of [null,'6',1.5,-1,0,Number.MAX_SAFE_INTEGER]){t=create();const bad=document();bad.Nodes[0].PeriodicActions[0][property]=value;assert.throws(()=>t.c.validate(bad));}passed++;
for(const value of [1,100000]){t=create();const good=document();good.Nodes[0].PeriodicActions[0].EveryCycles=value;t.c.validate(good)}
for(const value of [1000,86400000]){t=create();const good=document();good.Nodes[0].PeriodicActions[0].EveryMs=value;t.c.validate(good)}passed++;
for(const change of [d=>d.Nodes[0].PeriodicActions=null,d=>d.Nodes[0].PeriodicActions[0]=null,d=>d.Nodes[0].Steps=[],d=>d.Nodes[0].PeriodicActions[0].Node=null,d=>d.Nodes[0].PeriodicActions[0].Node.PeriodicActions=[rule()],d=>d.Nodes[0].PeriodicActions[0].Node.Steps=[],d=>d.Nodes[0].PeriodicActions[0].Trigger='Unknown',d=>d.Nodes[0].TransitionDelayMs=600001,d=>d.Nodes[0].PeriodicActions[0].Node.TransitionDelayMs=-1,d=>d.Nodes[0].PeriodicActions=Array.from({length:101},rule),d=>d.Nodes.push(...Array.from({length:999},node))]){t=create();const bad=document();change(bad);assert.throws(()=>t.c.validate(bad))}passed++;
t=create();t.c.editPeriodic(0,0);t.c.$('nodeDelayUnit').onfocus();t.c.$('nodeDelayUnit').value='1000';t.c.$('nodeDelayUnit').onchange();assert.equal(Number(t.c.$('nodeDelay').value),2);t.c.$('conditionTimeUnit').onfocus();t.c.$('conditionTimeUnit').value='60000';t.c.$('conditionTimeUnit').onchange();assert.equal(Number(t.c.$('conditionTime').value),0.5);passed++;
console.log(`PASS ${passed}/12: conditional editor, accept/cancel, units, duplication/order/toggle/delete, boundaries, nested JSON and dirty state`);

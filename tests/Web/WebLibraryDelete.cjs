const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(require('node:path').join(__dirname,'../../src/MacroClicker/Web/index.html'),'utf8');
const slice=(start,end)=>html.slice(html.indexOf(start),html.indexOf(end));
const elements={};
function element(){return {children:[],classList:{add(){}},setAttribute(){},focus(){},showModal(){},close(){},append(...items){this.children.push(...items)},replaceChildren(){this.children=[]}}}
const sent=[];
const c=vm.createContext({library:[{id:'a.json',name:'Same',active:true},{id:'b.json',name:'Same',active:false}],busy:false,dirty:true,document:{querySelector:()=>null},
 $:id=>elements[id]??=(id==='search'?{value:''}:element()),el:element,
 button:(label,glyph,fn)=>({...element(),label,onclick:fn}),small:(glyph,label,fn)=>({...element(),label,onclick:fn}),
 post:(name,data)=>sent.push({name,data}),toast(){},render(){throw Error('Must preserve editor and incomplete JSON')},getDoc(){throw Error('Must not parse draft on delete')}});
vm.runInContext(slice('function command(',"document.querySelectorAll('[data-close]')")+slice('function renderLibrary(','function keyPresentation(')+slice('let initialStateRendered=false;',"if(native)window.chrome.webview.addEventListener"),c);
c.renderLibrary();
assert.equal(elements.library.children.length,2);
elements.library.children[1].children[1].onclick();
assert.equal(sent.filter(x=>x.name==='deleteMacro').length,0);assert.equal(elements.deleteMacroWarning.hidden,true);
c.close('deleteMacroDialog');c.acceptLibraryDelete();assert.equal(sent.filter(x=>x.name==='deleteMacro').length,0);
elements.library.children[0].children[1].onclick();assert.equal(elements.deleteMacroWarning.hidden,false);c.close('deleteMacroDialog');
elements.library.children[1].children[1].onclick();c.acceptLibraryDelete();
const requests=sent.filter(x=>x.name==='deleteMacro');assert.equal(requests.length,1);assert.equal(requests[0].data.id,'b.json');assert.equal(requests[0].data.document,undefined);
assert.equal(c.library.length,2); // No optimistic removal before native confirmation/success.
c.busy=true;const before=sent.length;elements.library.children[0].children[1].onclick();assert.equal(sent.length,before);c.busy=false;
c.receive({type:'libraryDeleted',id:'b.json'});assert.equal(c.library.length,1);assert.equal(c.library[0].id,'a.json');
elements.search.value='missing';c.renderLibrary();assert.equal(elements.library.children.length,1);
// Exercise preview confirmation, active reset and cancellation with the real preview handler.
const p=vm.createContext({doc:{Name:'Draft',Nodes:[]},dirty:true,settings:{},keys:[],startKey:'',stopKey:'',
 confirm:()=>false,clone:structuredClone,newDoc:()=>({Name:'New',Nodes:[]}),receive:()=>{},toast(){}});
vm.runInContext('let previewLibrary=[{id:"a",name:"Saved",active:true}],previewTrash=[];'+slice('function preview(',"$('newMacro').onclick"),p);
p.preview('deleteMacro',{id:'a'});assert.equal(p.doc.Name,'New');assert.equal(p.dirty,false);
console.log('PASS: deletion by ID, confirmed-only updates, busy guard, duplicate names, preserved JSON, search, cancel and active reset');

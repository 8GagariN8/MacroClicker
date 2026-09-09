const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(require('node:path').join(__dirname,'../Web/index.html'),'utf8');
const slice=(start,end)=>html.slice(html.indexOf(start),html.indexOf(end));
const elements={},sent=[];
const el=(tag,cls,text)=>({tag,cls,text,children:[],append(...items){this.children.push(...items)},replaceChildren(){this.children=[]}});
const c=vm.createContext({$:id=>elements[id]??=el('div'),el,counted:(n,words)=>n+' '+words[2],
 button:(label,glyph,onclick)=>({label,onclick}),command:(name,data)=>sent.push({name,data}),
 library:[],renderLibrary(){},toast(){},render(){throw Error('Do not overwrite current editor during restore')}});
vm.runInContext(slice('function renderTrash(','function keyPresentation(')+slice('let initialStateRendered=false;',"if(native)window.chrome.webview.addEventListener"),c);
c.receive({type:'trash',items:[]});assert.equal(elements.clearTrash.disabled,true);assert.equal(elements.trashList.children[0].text,'Корзина пуста');
c.receive({type:'trash',items:[{Id:'deleted-1',Name:'Макрос',ExpiresAt:new Date(Date.now()+29.5*86400000).toISOString()},{Id:'legacy',Name:'Legacy',ExpiresAt:null}]});
assert.equal(elements.clearTrash.disabled,false);assert.match(elements.trashList.children[0].children[0].children[1].text,/30/);
assert.match(elements.trashList.children[1].children[0].children[1].text,/неизвестна/);
elements.trashList.children[0].children[1].onclick();assert.equal(sent[0].name,'restoreMacro');assert.equal(sent[0].data.id,'deleted-1');
c.receive({type:'libraryRestored',item:{id:'restored',name:'Макрос',active:false}});assert.equal(c.library.length,1);assert.equal(c.library[0].active,false);
console.log('PASS: trash empty/list, retention label, unknown date, restore by ID, unchanged editor');

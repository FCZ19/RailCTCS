const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),C=require('../dist/core.js');
const source=fs.readFileSync(require('node:path').join(__dirname,'../dist/dmi.js'),'utf8');
test('speed plan uses filled areas without curve strokes',()=>{
 const shapes=[],box={C,COL:C.COLORS,cfg:C.config({zoom:8}),rect(){},line(){},txt(){},symbol(){},poly(points,fill){const shape={name:'polygon',fill};shapes.push(shape);return {setAttribute(k,v){shape[k]=v}}},node(name,attrs){return {name,...attrs}},add(name,attrs){const shape={name,...attrs,children:[]};shapes.push(shape);return {appendChild(child){shape.children.push(child)}}}};
 vm.runInNewContext(source.slice(source.indexOf('function drawPlan('),source.indexOf('function radio(')),box);
 box.drawPlan({valid:true,limit:80,target:0,distance:500,profile:[],ato:{active:true,curve:[{distance:0,speed:60},{distance:500,speed:0}]}},{continuous:true});
 const shape=shapes.find(x=>x['data-part']==='braking-curve');assert.ok(shape);assert.equal(shape.name,'polygon');assert.equal(shape.stroke,'none');assert.equal(shape.fill,'#55e5ee');
 assert.equal(shapes.filter(x=>x.name==='polygon').length,1);
 assert.ok(!shapes.some(x=>x.name==='polyline'||x['data-part']==='ato-target-curve'||x.fill===C.COLORS.yellow||x.fill===C.COLORS.mrsp));
 const original=shape.points;shapes.length=0;box.cfg=C.config({zoom:8,mode:'PS'});
 box.drawPlan({valid:true,limit:80,target:0,distance:500,profile:[],ato:{active:false}},{continuous:false});
 const partial=shapes.find(x=>x['data-part']==='braking-curve');assert.ok(partial,'PS startup must show the curve without ATO');assert.equal(partial.points,original,'the original yellow curve geometry stays unchanged');assert.equal(partial.fill,'#55e5ee');
 shapes.length=0;box.drawPlan({valid:false,limit:80},{continuous:false});assert.equal(shapes.length,0,'invalid telemetry must not draw a synthetic curve');


});
test('DMI footer is a kilometre-post region without fabricated zero chainage',()=>{
 assert.match(source,/txt\('公里标 —'.*'kilometre-post'/);
 assert.doesNotMatch(source,/'data-part':'ato-status'/);
});

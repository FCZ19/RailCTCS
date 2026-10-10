'use strict';
const assert=require('node:assert/strict'),C=require('../dist/core.js');
let count=0;
function test(name,fn){fn();count++;console.log('PASS '+name)}
function near(a,b,t=1e-8){assert.ok(Math.abs(a-b)<t,`${a} != ${b}`)}
const cfg=C.config(),state={valid:true,speed:100,limit:160,target:160,distance:null};
test('CTCS-3 dial endpoints and 150 km/h at twelve o clock',()=>{near(C.speedAngle(0),135);near(C.speedAngle(150),270);near(C.speedAngle(450),405)});
test('CTCS-2 dial upper scale',()=>{near(C.speedAngle(150,400),270);near(C.speedAngle(400,400),405)});
test('Figure 5 needle at 135 km/h',()=>near(C.speedAngle(135),256.5));
test('Low range occupies twice the angle per km/h',()=>near(C.speedAngle(100)-C.speedAngle(90),2*(C.speedAngle(200)-C.speedAngle(190))));
test('Dial clamps out of range',()=>{near(C.speedAngle(-1),135);near(C.speedAngle(999),405)});
test('Needle tip length and slender symmetric shaft',()=>{let p=C.needlePoints(0,0,150);near(p[0][0],0);near(p[0][1],-100);near(p[1][0],-p[4][0]);near(Math.abs(p[2][0]-p[3][0]),9)});
test('Target distance piecewise scale',()=>{near(C.distanceFraction(0),0);near(C.distanceFraction(100),.25);near(C.distanceFraction(1000),1);near(C.distanceFraction(9999),1)});
test('Planning distance scale',()=>{near(C.planFraction(1,32),1/3);near(C.planFraction(32,32),1)});
test('Permitted curve respects target and ceiling',()=>{near(C.permittedAt(160,80,0,cfg),80);near(C.permittedAt(160,80,10000,cfg),160);assert.ok(C.permittedAt(160,80,900,cfg)>C.permittedAt(160,80,100,cfg))});
test('CSM normal colour',()=>{let r=C.supervision(state,cfg);assert.equal(r.color,C.COLORS.grey);assert.equal(r.region,'CSM')});
test('TSM normal colour',()=>{let r=C.supervision({...state,speed:20,target:0,distance:100},cfg);assert.equal(r.region,'TSM');assert.equal(r.color,C.COLORS.yellow)});
test('Overspeed and intervention transitions',()=>{for(let [speed,status,color]of[[161,'warning',C.COLORS.orange],[165,'SB',C.COLORS.red],[170,'EB',C.COLORS.red]]){let r=C.supervision({...state,speed},cfg);assert.equal(r.status,status);assert.equal(r.color,color)}});
test('Missing live limit cannot create a permitted speed',()=>assert.equal(C.supervision({...state,limit:null},cfg).permitted,null));
test('No intervention in sleeping mode',()=>assert.equal(C.supervision({...state,speed:300},{...cfg,mode:'SL'}).status,'none'));
test('Unavailable and stale TSW data remains blank',()=>{let e=new C.Engine({...cfg,source:'tsw'});assert.equal(e.snapshot().speed,null);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:95,Limit:120});assert.equal(e.snapshot().speed,95);e.lastFresh=Date.now()-4000;assert.equal(e.snapshot().valid,false);assert.equal(e.snapshot().speed,null)});
test('TSW update does not invent signal or radio data',()=>{let e=new C.Engine({...cfg,source:'tsw'});e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:0,Limit:40});let s=e.snapshot();assert.equal(s.signal,null);assert.equal(s.rbc,null);assert.equal(s.ebLatch,false)});
test('Known DMI messages and explicit acknowledgement',()=>{let e=new C.Engine(cfg);assert.equal(e.addMessage('unlisted'),false);e.addMessage('列车冒进确认',true);e.ack();assert.equal(e.messages[0].confirmed,true);assert.equal(C.MESSAGES.length,57)});
test('All saved sources migrate to TSW-only',()=>{let c=C.config({source:'demo',initialSpeed:350,throttle:1,speedProfile:[{speed:200}]});assert.equal(c.source,'tsw');assert.equal('initialSpeed'in c,false);assert.equal('throttle'in c,false);assert.equal('speedProfile'in c,false)});
test('Elapsed time cannot move a train independently',()=>{let e=new C.Engine(cfg);for(let i=0;i<100;i++)e.step(10);assert.equal(e.snapshot().speed,null);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:45,Limit:80});for(let i=0;i<100;i++)e.step(10);assert.equal(e.snapshot().speed,45);assert.equal(e.position,0);assert.equal(e.release(),false)});
test('Stopped mode selection uses measured speed',()=>{let e=new C.Engine(cfg);assert.equal(e.switchMode('SH'),false);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:30,Limit:80});assert.equal(e.switchMode('SH'),false);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:0,Limit:80});assert.equal(e.switchMode('SH'),true)});
test('Station and grades come from the live frame',()=>{let e=new C.Engine(cfg);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:0,Limit:40,Station:'圣潘克拉斯国际站',Grades:[{distance:0,length:100,grade:-3.4}]});assert.equal(e.snapshot().station,'圣潘克拉斯国际站');assert.equal(e.snapshot().grades[0].grade,-3.4)});
test('Invalid speed does not create a zero needle',()=>{let e=new C.Engine(cfg);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:null,Limit:40});assert.equal(e.snapshot().valid,false);assert.equal(e.snapshot().speed,null)});
test('Disconnect and recovery update standard fault message',()=>{let e=new C.Engine(cfg);e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:10,Limit:40});e.step();assert.equal(e.messages.length,0);e.setRemote(null);e.step();assert.equal(e.messages[0].text,'测速单元故障');e.setRemote({Fresh:true,DmiReady:true,CabActivation:{active:true},Speed:5,Limit:40});e.step();assert.equal(e.messages.length,0)});
console.log(`${count} tests passed.`);


'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),C=require('../dist/core.js');
test('manual device selection still follows acknowledged operation modes',()=>{const c=C.config({autoRoute:false,device:'CTCS-3',level:3,mode:'PS'});for(const mode of ['SH','OS','SB','FS','PS']){const result=C.routeConfig(c,{valid:true,operations:{mode},routeProfile:{device:'CTCS-2',level:2}});assert.equal(result.mode,mode);assert.equal(result.device,'CTCS-3');assert.equal(result.level,3)}assert.equal(c.mode,'PS')});
test('manual device keeps configured mode without operation state',()=>{const c=C.config({autoRoute:false,mode:'PS'});assert.equal(C.routeConfig(c,{valid:true}).mode,'PS')});

test('standstill never overspeeds with zero thresholds or negative limit',()=>{for(const limit of [0,-1]){const c=C.config({mode:'FS',warningDelta:0,serviceDelta:0,emergencyDelta:0});assert.equal(C.supervision({valid:true,speed:0,limit},c).status,'normal');assert.notEqual(C.supervision({valid:true,speed:1,limit},c).status,'normal')}});

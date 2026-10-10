(function(root){'use strict';
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
function axis(raw,cal,combined){
 if(!Number.isFinite(raw)||Math.abs(raw)>1.01||!cal||![cal.min,cal.center,cal.max,cal.deadzone].every(Number.isFinite)||cal.max-cal.min<.1||cal.deadzone<0||cal.deadzone>.3)throw Error('轴校准无效：请记录两端位置，并检查死区');
 if(combined&&(cal.center<=cal.min+.02||cal.center>=cal.max-.02))throw Error('空挡必须位于两端之间');
 let value=combined?(raw>=cal.center?(raw-cal.center)/(cal.max-cal.center):(raw-cal.center)/(cal.center-cal.min)):(raw-cal.min)/(cal.max-cal.min);
 value=clamp(value,combined?-1:0,1);if(cal.invert)value=combined?-value:1-value;
 return combined?Math.sign(value)*Math.max(0,(Math.abs(value)-cal.deadzone)/(1-cal.deadzone)):Math.max(0,(value-cal.deadzone)/(1-cal.deadzone));
}
function read(device,config){
 if(!device||!Array.isArray(device.axes))throw Error('摇杆未连接');
 const a=(index,cal,combined)=>{if(!Number.isInteger(index)||index<0||index>=device.axes.length||device.available?.[index]===false)throw Error('所选轴不可用');return axis(device.axes[index],cal,combined)};
 if(!['combined','separate','buttons'].includes(config.mode))throw Error('操纵方式无效');
 let value=config.mode==='buttons'?0:a(config.powerAxis,config.power,config.mode==='combined'),throttle,brake;
 if(config.mode==='buttons'){if(!Number.isInteger(config.powerButton)||!Number.isInteger(config.brakeButton)||config.powerButton<0||config.brakeButton<0||config.powerButton>=device.buttons.length||config.brakeButton>=device.buttons.length)throw Error('请选择有效牵引和制动按钮');throttle=device.buttons[config.powerButton]?35:0;brake=device.buttons[config.brakeButton]?100:0}else if(config.mode==='combined'){throttle=Math.max(0,value)*100;brake=Math.max(0,-value)*100}else{if(config.powerAxis===config.brakeAxis)throw Error('独立牵引与制动不能使用同一根轴');throttle=value*100;brake=a(config.brakeAxis,config.brake,false)*100}
 const button=index=>{if(index===-1)return false;if(!Number.isInteger(index)||index<0||index>=device.buttons.length)throw Error('所选按钮不可用');return !!device.buttons[index]};
 if([config.emergencyButton,config.ackButton,config.vigilanceButton??-1,...(config.mode==='buttons'?[config.powerButton,config.brakeButton]:[])].filter(x=>x>=0).some((x,i,a)=>a.indexOf(x)!==i))throw Error('紧急制动和确认不能映射到同一按钮');
 return {throttle:brake>0?0:throttle,brake,emergency:button(config.emergencyButton),ack:button(config.ackButton),vigilance:button(config.vigilanceButton??-1)};
}
function defaults(){return {mode:'combined',powerAxis:0,brakeAxis:1,powerButton:-1,brakeButton:-1,power:{min:-1,center:0,max:1,deadzone:.05,invert:false},brake:{min:-1,center:0,max:1,deadzone:.05,invert:false},emergencyButton:-1,ackButton:-1,vigilanceButton:-1,protection:true}}
function distance(m){return !Number.isFinite(m)?'距离未知':m>=1000?(m/1000).toFixed(2)+' km':Math.round(m)+' m'}
function station(frame){if(!frame?.Fresh||!frame.Station)return '等待游戏车站数据';return frame.Station+' · '+distance(frame.StationDistance)+(frame.Platform?' · '+frame.Platform+' 站台':' · 站台编号未提供')+(frame.PlatformSide?' · '+frame.PlatformSide:' · 站台侧未提供')+(Number.isFinite(frame.PlatformLength)?' · 站台长 '+Math.round(frame.PlatformLength)+' m':'')}
const exports={axis,read,defaults,distance,station};root.DmiMapping=exports;if(typeof module!=='undefined')module.exports=exports;
})(typeof globalThis!=='undefined'?globalThis:this);

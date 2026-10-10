(function(root){'use strict';
// Keep every click responsive, while deduplicating an identical in-flight action.
class AtoControls {
 static options(){let value={};try{value=JSON.parse(localStorage.getItem('dmi-ato-options-v1')||'{}')||{}}catch{}return {platformEndMargin:value.platformEndMargin??10,offset:value.offset??0,maxSpeed:value.maxSpeed??120,deceleration:value.deceleration??.65,kp:value.kp??.65,ki:value.ki??.12,kd:value.kd??0,integralLimit:value.integralLimit??1.5,derivativeFilter:value.derivativeFilter??.5,allowCoast:value.allowCoast!==false,coastBand:value.coastBand??2}}
 static save(options){try{localStorage.setItem('dmi-ato-options-v1',JSON.stringify(options))}catch{}}
 constructor(api,feedback,update){this.api=api;this.feedback=feedback;this.update=update;this.pending=new Map();this.lastAction=0}
 async send(action,options={}){if(this.pending.has(action)){this.feedback('请求正在处理，请稍候；可随时点击退出。');return}const order=++this.lastAction;this.feedback(action==='skip'?'正在跳过当前停车站…':action==='start'?'已收到启动请求，正在连接 TSW…':action==='configure'?'正在应用 PID 参数…':'已收到退出请求，正在施加常用制动…');const request=this.api.post('/api/ato',{action,...(action==='start'?AtoControls.options():{}),...options},true,true);this.pending.set(action,request);try{const result=await request;if(order===this.lastAction){this.feedback(action==='configure'?'PID／惰行设置已应用':action==='start'&&result.active?'ATO 正在运行 · '+(result.station||result.message):result.message||'操作已完成');this.update(result)}return result}catch(e){if(order===this.lastAction)this.feedback(e.message||'请求失败，请重试')}finally{this.pending.delete(action)}}
}
root.AtoControls=AtoControls;if(typeof module!=='undefined')module.exports=AtoControls;
})(typeof globalThis!=='undefined'?globalThis:this);

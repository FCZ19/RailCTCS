'use strict';
// Exact WAV resources from the user's installed RailBridge, kept at their original sample rate.
class DmiSound {
 constructor(){this.context=null;this.buffers={};this.active=null;this.previous=null;this.next=0;this.loading=null;this.voice=null;this.voiceSequence=0;this.voicePrevious=null;this.voiceNext=0;this.pending={};this.platform=new CTCS.PlatformPrompt();this.bootPlayed=false;this.bootSequence=0;this.bootSource=null}
 unlock(){if(!this.context){this.context=new(window.AudioContext||window.webkitAudioContext)();this.loading=Promise.all(['S1_toofast','S2_warning','S_click','S_info','notch'].map(async name=>{let r=await fetch('/sounds/'+name+'.wav');if(!r.ok)throw Error('音效读取失败');this.buffers[name]=await this.context.decodeAudioData(await r.arrayBuffer())})).catch(e=>{this.error=e.message})}const wasRunning=this.context.state==='running';return this.context.resume().then(()=>{if(!wasRunning)this.voicePrevious=null})}
 play(name,volume=65){if(!this.context||this.context.state!=='running'||!this.buffers[name]||volume<=0)return;let source=this.context.createBufferSource(),gain=this.context.createGain();source.buffer=this.buffers[name];gain.gain.value=Math.min(1,volume/100);source.connect(gain);gain.connect(this.context.destination);source.start();return source}
 updateStartup(phase,volume){
  if(phase==='off'){this.bootPlayed=false;this.bootSequence++;if(this.bootSource){try{this.bootSource.stop()}catch{}this.bootSource=null}return}
  if(phase==='black'||this.bootPlayed)return;
  if(volume<=0){this.bootPlayed=true;return}
  if(!this.context||this.context.state!=='running')return;
  this.bootPlayed=true;const sequence=this.bootSequence;
  this.loadVoice('开机，动车提示音').then(()=>{if(sequence===this.bootSequence)this.bootSource=this.play('开机，动车提示音',volume)}).catch(e=>{this.error=e.message});
 }
 async loadVoice(name){const file=DmiSound.catalog[name];if(!file)throw Error('未知语音');if(this.buffers[name])return this.buffers[name];if(!this.pending[name])this.pending[name]=(async()=>{const r=await fetch(file.startsWith('/')?file:'/sounds/lkj/'+file);if(!r.ok)throw Error('LKJ 音频读取失败：'+name);const buffer=await this.context.decodeAudioData(await r.arrayBuffer());this.buffers[name]=buffer;return buffer})().finally(()=>{delete this.pending[name]});return this.pending[name]}
 stopVoice(){this.voiceSequence++;if(this.voice){try{this.voice.stop()}catch{}this.voice=null}}
 async speak(name,volume=65){this.stopVoice();const sequence=this.voiceSequence;if(!this.context||this.context.state!=='running'||volume<=0)return false;try{await this.loadVoice(name);if(sequence!==this.voiceSequence)return false;this.voice=this.play(name,volume);return !!this.voice}catch(e){this.error=e.message;return false}}
 updateVoice(s,r,volume,enabled,mode){
  if(!s.valid||!enabled||volume<=0){if(!s.valid)this.platform.update(s);this.stopVoice();this.voicePrevious=null;this.voiceNext=0;return}
  const platformEvents=this.platform.update(s);const old=this.voicePrevious,brake=s.ebLatch?'EB':s.sbLatch?'SB':'',alert=brake==='EB'||r.status==='EB'?'紧急制动':s.operations?.warning?'注意运行':r.status==='SB'?'减速':r.status==='warning'?'注意限速':'';
  let say='';if(alert){if(!old||old.alert!==alert||Date.now()>=this.voiceNext){say=alert;this.voiceNext=Date.now()+10000}}
  else if(old?.alert){this.stopVoice();this.voiceNext=0}
  if(!alert&&old){
   if(s.releaseAllowed&&!old.releaseAllowed)say='允许缓解';
   else if(brake&&s.speed===0&&old.speed>0)say='制动完成';
   else if(old.mode!==mode&&(old.mode==='SH'||mode==='SH'))say=mode==='SH'?'进入调车':'退出调车';
   else if(s.signalId&&(s.signalId!==old.id||s.signalAspect!==old.aspect))say=({Stop:'红灯',Approach:'一黄灯',AdvanceApproach:'绿黄灯',Clear:'绿灯'})[s.signalAspect]||'';
  }
  if(old&&s.releaseAllowed&&!old.releaseAllowed)say='允许缓解';else if(old&&brake&&s.speed===0&&old.speed>0)say='制动完成';
  this.voicePrevious={alert,brake,mode,speed:s.speed,releaseAllowed:s.releaseAllowed,id:s.signalId,aspect:s.signalAspect};if(say)this.speak(say,volume);else if(!alert&&platformEvents.length)this.speakSequence(platformEvents,volume);
 }

 async speakSequence(names,volume){this.stopVoice();const sequence=this.voiceSequence;for(const name of names){if(sequence!==this.voiceSequence||!this.context||this.context.state!=='running')return;try{await this.loadVoice(name);if(sequence!==this.voiceSequence)return;const source=this.play(name,volume);this.voice=source;if(source)await new Promise(resolve=>{source.onended=resolve})}catch(e){this.error=e.message;return}}}
 stop(){if(this.active){try{this.active.stop()}catch{}this.active=null}}
 update(s,r,volume,voiceEnabled=true,mode=''){if(s.speed===0)r={...r,status:'normal'};this.updateVoice(s,r,volume,voiceEnabled,mode);let old=this.previous;if(!s.valid){this.stop();this.previous=null;this.next=0;return}let alert=s.operations?.warning?'S2_warning':['SB','EB'].includes(r.status)?'S2_warning':r.status==='warning'?'S1_toofast':'';if(!old||alert!==old.alert){this.stop();this.next=0}if(alert&&Date.now()>=this.next){this.active=this.play(alert,volume);this.next=Date.now()+(this.active?Math.max(3000,this.buffers[alert].duration*1000):500)}
 if(old&&!alert&&s.signalId&&old.id===s.signalId&&old.aspect!==s.signalAspect&&['Stop','Approach','AdvanceApproach'].includes(s.signalAspect))this.play('S_info',volume);
 if(old&&!alert&&old.aspect==='Clear'&&old.distance<40&&s.signalId&&s.signalId!==old.id&&s.speed>1)this.play('S_info',volume);
 this.previous={alert,id:s.signalId,aspect:s.signalAspect,distance:s.signalDistance};
 }
}
DmiSound.catalog={"绿灯": "01.wav", "绿黄灯": "02.wav", "一黄灯": "03.wav", "黄二灯": "04.wav", "双黄灯": "05.wav", "白灯": "06.wav", "蓝灯停车": "07.wav", "红黄灯": "08.wav", "红灯": "09.wav", "一车": "10.wav", "三车": "11.wav", "五车": "12.wav", "减速": "13.wav", "停车": "14.wav", "启动": "15.wav", "入段": "16.wav", "连接": "17.wav", "进入调车": "18.wav", "退出调车": "19.wav", "单独缓解": "20.wav", "大闸缓解内": "21.wav", "机车缓解": "22.wav", "紧急制动": "23.wav", "注意停车": "24.wav", "注意限速": "25.wav", "注意行车": "26.wav", "注意运行": "27.wav", "注意道岔限速": "28.wav", "注意前方限速": "29.wav", "注意按开车键": "30.wav", "注意管压防溜": "31.wav", "请注意调车信号": "32.wav", "请确认信号": "33.wav", "白灯请确认进路": "34.wav", "输入车次": "35.wav", "输入辆数": "36.wav", "输入计长": "37.wav", "输入司机号": "38.wav", "输入区段号": "39.wav", "输入车站号": "40.wav", "输入牵引重量": "41.wav", "输入侧线股道": "42.wav", "车机联控": "43.wav", "监控运行": "44.wav", "监控按键声": "45.wav", "刹车声内": "46.wav"};
Object.assign(DmiSound.catalog,{"允许缓解": "/sounds/custom/01.wav", "减速": "/sounds/custom/02.wav", "制动完成": "/sounds/custom/03.wav", "右侧站台": "/sounds/custom/04.wav", "左侧站台": "/sounds/custom/05.wav", "开机，动车提示音": "/sounds/custom/06.wav", "控制速度": "/sounds/custom/07.wav"});
DmiSound.catalog['注意限速']=DmiSound.catalog['控制速度'];
window.DmiSound=DmiSound;

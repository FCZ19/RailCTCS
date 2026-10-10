'use strict';
const $=id=>document.getElementById(id);let frame=null,admin=false,busy=false;
const number=v=>Number.isFinite(v)?v.toFixed(1):'—';
function render(){let f=frame,a=f?.Ato||{},reasons=[];
 $('connection').textContent=!f?.Fresh?'等待 TSW':!f.DmiReady?'等待司机室激活':f.GameAdvancing!==true?'游戏时间未推进':'TSW 已连接';
 $('station').textContent=DmiMapping.station(f);$('target').textContent=a.hasDestination===false&&a.active?'无目的地 · 按限速运行；目的地恢复后自动接入停车曲线。':'停车目标距当前位置 '+number((a.active&&a.hasDestination?a.remaining:(f?.StationDistance??NaN)+(f.PlatformLength>10&&f.PlatformLength<1500?Math.max(0,f.PlatformLength-Number($('platform-end-margin').value)):0)+Number($('offset').value)))+' m；距站台末端 '+number(a.active?a.platformEndMargin:Number($('platform-end-margin').value))+' m。';
 $('start').textContent=a.waiting?'ATO 已激活 · 等待运行':a.active?'ATO 正在运行 · 查看响应':'激活 ATO · 下站停车';
 $('requirements').textContent='按钮始终可点击。任意线路、车型和状态可激活；未就绪时等待并显示原因，条件具备后自动接管当前列车。';$('start').disabled=false;$('stop').disabled=false;$('emergency').disabled=false;$('configuration').disabled=!!(a.active||a.waiting);
 $('pid-live').textContent='后端当前 P '+number(a.kp)+' / I '+number(a.ki)+' / D '+number(a.kd)+' · 积分上限 '+number(a.integralLimit)+' · 滤波 '+number(a.derivativeFilter)+' s\n调速分量 P '+number(a.pTerm)+' / I '+number(a.iTerm)+' / D '+number(a.dTerm);
 const phases={off:'未接管',waiting:'已激活 · 等待运行条件',ready:'准备运行',arrived:'已停车',power:'牵引',brake:'制动',coast:'惰行',approach:'进站调速',signal:'等待信号',protection:'列控干预',overrun:'越过停车目标'};
 $('runtime').textContent=(phases[a.phase]||'未接管')+(a.active?' · ATP 超速干预关闭':'')+' · '+(a.message||'')+'\n实际速度 '+number(f?.Speed)+' km/h · 目标速度 '+number(a.targetSpeed)+' km/h\n'+(a.hasDestination===false?'无目的地 · 按限速运行':'锁定车站 '+(a.station||'—')+' · 剩余 '+number(a.remaining)+' m')+'\n牵引 '+number(a.throttle)+'% · 制动 '+number(a.brake)+'% · 实际加速度 '+number(f?.Acceleration)+' m/s²';
}
const controls=new AtoControls(CTCS.api,text=>{$('message').textContent=text},result=>{if(frame)frame.Ato=result;render()});
const fields={platformEndMargin:'platform-end-margin',offset:'offset',maxSpeed:'max-speed',deceleration:'deceleration',kp:'kp',ki:'ki',kd:'kd',integralLimit:'integral-limit',derivativeFilter:'derivative-filter',coastBand:'coast-band'};
function options(){return {...Object.fromEntries(Object.entries(fields).map(([key,id])=>[key,Number($(id).value)])),allowCoast:$('allow-coast').checked}}
$('skip').onclick=()=>controls.send('skip');$('start').onclick=()=>controls.send('start',options());$('stop').onclick=()=>controls.send('stop');$('apply-pid').onclick=()=>{let p=options();return controls.send('configure',{kp:p.kp,ki:p.ki,kd:p.kd,integralLimit:p.integralLimit,derivativeFilter:p.derivativeFilter,allowCoast:p.allowCoast,coastBand:p.coastBand})};for(let id of Object.values(fields))$(id).oninput=()=>{AtoControls.save(options());render()};

$('emergency').onclick=async()=>{try{await CTCS.api.post('/api/brake',{vehicle:frame?.Vehicle,percent:100,emergency:true},true,true);$('message').textContent='已发送紧急制动，ATO 已退出'}catch(e){$('message').textContent=e.message}};
async function poll(){try{frame=await CTCS.api.get('/api/frame');admin=CTCS.api.canManageConnection;render()}catch(e){frame=null;$('message').textContent=e.message;try{render()}catch{}}finally{setTimeout(poll,500)}}
$('allow-coast').onchange=()=>AtoControls.save(options());
const savedOptions=AtoControls.options();$('allow-coast').checked=savedOptions.allowCoast;for(let [key,id]of Object.entries(fields))$(id).value=savedOptions[key];
(async()=>{try{admin=(await CTCS.api.init()).canManageConnection}catch(e){$('message').textContent=e.message}poll()})();

'use strict';
(function(){const state=document.getElementById('main-ato-state'),feedback=document.getElementById('main-ato-feedback'),start=document.getElementById('main-ato-start');let last=null;
 const fmt=v=>Number.isFinite(v)?v.toFixed(1):'—';
 function show(f){last=f;let a=f?.Ato;start.textContent=a?.waiting?'ATO 已激活 · 等待运行':a?.active?'ATO 运行中 · 查看响应':'启动 ATO';state.textContent=a?.waiting?a.message:!f?.Fresh?'等待 TSW 连接；按钮仍可点击':a?.active?(a.hasDestination===false?'无目的地 · 按限速运行':'自动运行 → '+a.station+' · 剩余 '+fmt(a.remaining)+' m')+' · 目标 '+fmt(a.targetSpeed)+' km/h · ATP 超速干预关闭':a?.arrived?'上次运行：'+a.station+' 已停车':a?.message||'ATO 未启用'}
 const controls=new AtoControls(CTCS.api,text=>{feedback.textContent=text},result=>{show({...last,Ato:result})});
 start.onclick=()=>controls.send('start');document.getElementById('main-ato-skip').onclick=()=>controls.send('skip');document.getElementById('main-ato-stop').onclick=()=>controls.send('stop');
 window.addEventListener('dmi-frame',event=>show(event.detail));
})();
